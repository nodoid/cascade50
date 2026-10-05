using System;

namespace Cascade50.Core.Audio;

/// <summary>
/// The menu theme: a chiptune loop (lead, bass, arpeggio and drums) sequenced from note strings.
/// Each bar is 16 steps; a note name starts a note, '-' holds it, '.' is a rest.
/// </summary>
public static class Music
{
    private const double Step = 0.125; // 16th notes at 120 bpm

    private static readonly string[] Lead =
    [
        "E5 - - - D5 - C5 - A4 - - - C5 - D5 -",
        "C5 - - - A4 - F4 - A4 - C5 - F5 - E5 -",
        "E5 - - - G5 - E5 - C5 - - - D5 - E5 -",
        "D5 - - - B4 - G4 - B4 - D5 - G5 - - -",
        "A5 - G5 - E5 - - - C5 - E5 - A5 - G5 -",
        "F5 - E5 - C5 - - - A4 - C5 - F5 - E5 -",
        "E5 - - - G5 - - - C6 - B5 - G5 - E5 -",
        "D5 - - - B4 - D5 - G5 - - - - - . .",
    ];

    private static readonly string[] Chords = ["A3 C4 E4", "F3 A3 C4", "C4 E4 G4", "G3 B3 D4"];
    private static readonly string[] Roots = ["A2", "F2", "C3", "G2"];

    public static float[] MenuTheme()
    {
        int bars = Lead.Length;
        int total = (int)(bars * 16 * Step * Synth.SampleRate);
        var mix = new float[total];
        int stepSamples = (int)(Step * Synth.SampleRate);

        // Lead.
        for (int bar = 0; bar < bars; bar++)
        {
            var tokens = Lead[bar].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int s = 0; s < tokens.Length; s++)
            {
                if (tokens[s] is "-" or ".")
                    continue;
                int len = 1;
                while (s + len < tokens.Length && tokens[s + len] == "-")
                    len++;
                double hz = Synth.Hz(tokens[s]);
                var note = Synth.Tone(Wave.Pulse25, len * Step * 0.95, hz, hz, 0.20, 0.08, 0.005, 0.006, 5.5);
                Synth.AddAt(mix, note, (bar * 16 + s) * stepSamples);
            }
        }

        for (int bar = 0; bar < bars; bar++)
        {
            int chord = bar % 4;
            int barStart = bar * 16 * stepSamples;
            // Bass: octave-jumping eighths.
            double root = Synth.Hz(Roots[chord]);
            for (int e = 0; e < 8; e++)
            {
                double hz = e % 2 == 0 ? root : root * 2;
                var n = Synth.Tone(Wave.Triangle, Step * 1.8, hz, hz, 0.38, 0.15, 0.002);
                Synth.AddAt(mix, n, barStart + e * 2 * stepSamples);
            }
            // Arpeggio: chord tones in 16ths, quiet.
            var tones = Chords[chord].Split(' ');
            for (int s = 0; s < 16; s++)
            {
                double hz = Synth.Hz(tones[s % tones.Length]) * 2;
                var n = Synth.Tone(Wave.Pulse12, Step * 0.6, hz, hz, 0.07, 0.0, 0.001);
                Synth.AddAt(mix, n, barStart + s * stepSamples);
            }
            // Drums.
            for (int beat = 0; beat < 4; beat++)
            {
                int at = barStart + beat * 4 * stepSamples;
                if (beat % 2 == 0)
                    Synth.AddAt(mix, Synth.Tone(Wave.Sine, 0.16, 150, 45, 0.6, 0, 0.001), at);
                else
                    Synth.AddAt(mix, Synth.LowPass(Synth.Tone(Wave.Noise, 0.14, 7000, 4000, 0.28, 0, 0.001, seed: bar), 6000), at);
                Synth.AddAt(mix, Synth.Tone(Wave.Noise, 0.03, 12000, 12000, 0.08, 0, 0.001, seed: beat + 9), at + 2 * stepSamples);
            }
        }

        var echoed = Synth.Echo(mix, Step * 3, 0.22f, 2, wrap: true);
        return Synth.LowPass(echoed, 7000);
    }
}
