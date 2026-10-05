using System;

namespace Cascade50.Core.Audio;

public enum Wave
{
    Square,
    Pulse25,
    Pulse12,
    Triangle,
    Saw,
    Sine,
    Noise,
}

/// <summary>
/// A small PCM synthesiser in the spirit of the Oric's AY-3-8912, with a few modern comforts:
/// more waveforms, attack/decay envelopes, vibrato, a low-pass filter and echo. 16-bit mono.
/// </summary>
public static class Synth
{
    public const int SampleRate = 44100;

    /// <summary>
    /// A single voice sliding from <paramref name="f0"/> to <paramref name="f1"/> Hz (exponentially),
    /// with linear attack, then a decay from <paramref name="volume"/> to <paramref name="endVolume"/>.
    /// </summary>
    public static float[] Tone(Wave wave, double seconds, double f0, double f1, double volume = 0.5,
        double endVolume = 0, double attack = 0.004, double vibrato = 0, double vibratoHz = 6, int seed = 1)
    {
        int n = Math.Max(1, (int)(seconds * SampleRate));
        var s = new float[n];
        double phase = 0;
        uint lfsr = (uint)seed * 2654435761u | 1;
        float noise = 0;
        double ratio = f0 > 0 && f1 > 0 ? Math.Log(f1 / f0) : 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            double f = ratio != 0 ? f0 * Math.Exp(ratio * t) : f0 + (f1 - f0) * t;
            if (vibrato > 0)
                f *= 1 + vibrato * Math.Sin(2 * Math.PI * vibratoHz * i / SampleRate);
            double prev = phase;
            phase += f / SampleRate;
            double p = phase % 1.0;
            double v = wave switch
            {
                Wave.Square => p < 0.5 ? 1 : -1,
                Wave.Pulse25 => p < 0.25 ? 1 : -1,
                Wave.Pulse12 => p < 0.125 ? 1 : -1,
                Wave.Triangle => 4 * Math.Abs(p - 0.5) - 1,
                Wave.Saw => 2 * p - 1,
                Wave.Sine => Math.Sin(2 * Math.PI * p),
                _ => 0,
            };
            if (wave == Wave.Noise)
            {
                // New random level every cycle of f (so the "pitch" of noise can be swept).
                if ((int)phase != (int)prev || i == 0)
                {
                    lfsr ^= lfsr << 13;
                    lfsr ^= lfsr >> 17;
                    lfsr ^= lfsr << 5;
                    noise = (lfsr & 0xFFFF) / 32768f - 1f;
                }
                v = noise;
            }
            double env = volume + (endVolume - volume) * t;
            double at = i / (attack * SampleRate);
            if (at < 1)
                env *= at;
            // Short release so nothing clicks at the end.
            int tail = n - i;
            if (tail < 200)
                env *= tail / 200.0;
            s[i] = (float)(v * env);
        }
        return s;
    }

    public static float[] Silence(double seconds) => new float[Math.Max(1, (int)(seconds * SampleRate))];

    /// <summary>Notes played one after another: (Hz, seconds); 0 Hz is a rest.</summary>
    public static float[] Notes(Wave wave, double volume, params (double hz, double seconds)[] notes)
    {
        var parts = new float[notes.Length][];
        for (int i = 0; i < notes.Length; i++)
            parts[i] = notes[i].hz <= 0
                ? Silence(notes[i].seconds)
                : Tone(wave, notes[i].seconds, notes[i].hz, notes[i].hz, volume, volume * 0.3, 0.003, 0.004);
        return Concat(parts);
    }

    public static float[] Mix(params float[][] clips)
    {
        int n = 0;
        foreach (var c in clips)
            n = Math.Max(n, c.Length);
        var s = new float[n];
        foreach (var c in clips)
            for (int i = 0; i < c.Length; i++)
                s[i] += c[i];
        return s;
    }

    /// <summary>Adds <paramref name="clip"/> into <paramref name="into"/> at a sample offset.</summary>
    public static void AddAt(float[] into, float[] clip, int offset, float gain = 1f)
    {
        for (int i = 0; i < clip.Length; i++)
        {
            int j = offset + i;
            if (j >= into.Length)
                break;
            if (j >= 0)
                into[j] += clip[i] * gain;
        }
    }

    public static float[] Concat(params float[][] clips)
    {
        int n = 0;
        foreach (var c in clips)
            n += c.Length;
        var s = new float[n];
        int pos = 0;
        foreach (var c in clips)
        {
            Array.Copy(c, 0, s, pos, c.Length);
            pos += c.Length;
        }
        return s;
    }

    /// <summary>One-pole low-pass filter: softens the harsh square-wave edges.</summary>
    public static float[] LowPass(float[] s, double cutoffHz)
    {
        double rc = 1.0 / (2 * Math.PI * cutoffHz);
        double dt = 1.0 / SampleRate;
        float a = (float)(dt / (rc + dt));
        float y = 0;
        var o = new float[s.Length];
        for (int i = 0; i < s.Length; i++)
        {
            y += a * (s[i] - y);
            o[i] = y;
        }
        return o;
    }

    /// <summary>Feedback echo (the clip is lengthened so the echoes can ring out).</summary>
    public static float[] Echo(float[] s, double delaySeconds, double feedback, int repeats = 3, bool wrap = false)
    {
        int d = (int)(delaySeconds * SampleRate);
        int extra = wrap ? 0 : d * repeats;
        var o = new float[s.Length + extra];
        Array.Copy(s, o, s.Length);
        float g = (float)feedback;
        for (int r = 1; r <= repeats; r++, g *= (float)feedback)
            for (int i = 0; i < s.Length; i++)
            {
                int j = i + d * r;
                if (wrap)
                    j %= o.Length;
                if (j < o.Length)
                    o[j] += s[i] * g;
            }
        return o;
    }

    /// <summary>Converts to 16-bit little-endian PCM with soft clipping.</summary>
    public static byte[] ToPcm(float[] samples, float gain = 1f)
    {
        var b = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            float x = samples[i] * gain;
            x = x > 1 || x < -1 ? MathF.Tanh(x) : x;
            short v = (short)(x * 32000);
            b[i * 2] = (byte)(v & 0xFF);
            b[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        return b;
    }

    /// <summary>Frequency of a note name such as "A4", "C#5" or "Eb3".</summary>
    public static double Hz(string note)
    {
        if (string.IsNullOrEmpty(note) || note == "." || note == "-")
            return 0;
        int semis = note[0] switch { 'C' => -9, 'D' => -7, 'E' => -5, 'F' => -4, 'G' => -2, 'A' => 0, 'B' => 2, _ => 0 };
        int i = 1;
        if (i < note.Length && note[i] == '#') { semis++; i++; }
        else if (i < note.Length && note[i] == 'b') { semis--; i++; }
        int octave = int.Parse(note[i..]);
        return 440.0 * Math.Pow(2, (semis + (octave - 4) * 12) / 12.0);
    }
}
