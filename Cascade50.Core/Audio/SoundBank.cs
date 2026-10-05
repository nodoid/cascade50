using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Audio;

namespace Cascade50.Core.Audio;

/// <summary>Every one-shot sound effect. Pass a pitch (-1..1 octaves) for variety.</summary>
public enum Sfx
{
    Click,
    Select,
    Back,
    Start,
    Shoot,
    Laser,
    Zap,
    Explode,
    BigExplode,
    Hit,
    Hurt,
    Die,
    Jump,
    Land,
    Bounce,
    Coin,
    Pickup,
    PowerUp,
    Bonus,
    LevelUp,
    Win,
    Lose,
    GameOver,
    Correct,
    Wrong,
    Tick,
    Beep,
    Card,
    Shuffle,
    Splash,
    Crack,
    Whoosh,
    Thud,
    Pop,
    Warp,
    Alarm,
    Bell,
    Cannon,
    Step,
    Fuse,
}

/// <summary>Continuous sounds a game can switch on and off (engines, thrusters).</summary>
public enum LoopSfx
{
    Engine,
    Thrust,
    Wind,
    Hum,
}

/// <summary>What the games use to make noise.</summary>
public interface ISound
{
    void Play(Sfx sfx, float pitch = 0f, float volume = 1f);

    /// <summary>Starts, adjusts or stops a looping sound. Call every tick while it should play.</summary>
    void Loop(LoopSfx loop, bool on, float pitch = 0f, float volume = 1f);

    void StopLoops();
}

/// <summary>Silent sound for tests and capture.</summary>
public sealed class NullSound : ISound
{
    public readonly List<Sfx> Played = new();
    public void Play(Sfx sfx, float pitch = 0, float volume = 1) => Played.Add(sfx);
    public void Loop(LoopSfx loop, bool on, float pitch = 0, float volume = 1) { }
    public void StopLoops() { }
}

/// <summary>Builds every sound at start-up from <see cref="Synth"/> and plays them.</summary>
public sealed class SoundBank : ISound, IDisposable
{
    private readonly Dictionary<Sfx, SoundEffect> _effects = new();
    private readonly Dictionary<LoopSfx, SoundEffectInstance> _loops = new();
    private readonly List<SoundEffect> _loopEffects = new();
    private readonly HashSet<LoopSfx> _loopTouched = new();
    private readonly bool _enabled;
    private SoundEffect _music;
    private SoundEffectInstance _musicInstance;

    public SoundBank()
    {
        try
        {
            foreach (var (id, pcm) in Build())
                _effects[id] = new SoundEffect(Synth.ToPcm(pcm), Synth.SampleRate, AudioChannels.Mono);
            foreach (var (id, pcm) in BuildLoops())
            {
                var fx = new SoundEffect(Synth.ToPcm(pcm), Synth.SampleRate, AudioChannels.Mono);
                _loopEffects.Add(fx);
                var inst = fx.CreateInstance();
                inst.IsLooped = true;
                _loops[id] = inst;
            }
            _enabled = true;
        }
        catch (Exception)
        {
            // No audio device (e.g. a headless machine): run silently.
            _enabled = false;
        }
    }

    public bool SoundOn { get; set; } = true;

    public bool MusicOn
    {
        get => _musicOn;
        set
        {
            _musicOn = value;
            if (!value)
                _musicInstance?.Stop();
        }
    }

    private bool _musicOn = true;

    public void Play(Sfx sfx, float pitch = 0f, float volume = 1f)
    {
        if (!_enabled || !SoundOn)
            return;
        if (_effects.TryGetValue(sfx, out var e))
        {
            try
            {
                e.Play(Math.Clamp(volume, 0, 1) * 0.8f, Math.Clamp(pitch, -1f, 1f), 0f);
            }
            catch (Exception)
            {
                // Too many voices: drop the sound.
            }
        }
    }

    public void Loop(LoopSfx loop, bool on, float pitch = 0f, float volume = 1f)
    {
        if (!_enabled || !_loops.TryGetValue(loop, out var inst))
            return;
        try
        {
            if (on && SoundOn)
            {
                _loopTouched.Add(loop);
                inst.Pitch = Math.Clamp(pitch, -1f, 1f);
                inst.Volume = Math.Clamp(volume, 0, 1) * 0.6f;
                if (inst.State != SoundState.Playing)
                    inst.Play();
            }
            else if (inst.State == SoundState.Playing)
            {
                inst.Stop();
            }
        }
        catch (Exception)
        {
        }
    }

    /// <summary>Stops any loop that was not refreshed since the last call (called once per frame).</summary>
    public void EndFrame()
    {
        foreach (var (id, inst) in _loops)
            if (!_loopTouched.Contains(id) && inst.State == SoundState.Playing)
                inst.Stop();
        _loopTouched.Clear();
    }

    public void StopLoops()
    {
        foreach (var inst in _loops.Values)
            if (inst.State == SoundState.Playing)
                inst.Stop();
        _loopTouched.Clear();
    }

    /// <summary>Plays the menu theme (looped) while <paramref name="on"/>.</summary>
    public void SetMusic(bool on)
    {
        if (!_enabled)
            return;
        try
        {
            if (on && MusicOn)
            {
                if (_music == null)
                {
                    _music = new SoundEffect(Synth.ToPcm(Music.MenuTheme()), Synth.SampleRate, AudioChannels.Mono);
                    _musicInstance = _music.CreateInstance();
                    _musicInstance.IsLooped = true;
                    _musicInstance.Volume = 0.45f;
                }
                if (_musicInstance.State != SoundState.Playing)
                    _musicInstance.Play();
            }
            else
            {
                _musicInstance?.Stop();
            }
        }
        catch (Exception)
        {
        }
    }

    public void Pause()
    {
        StopLoops();
        try
        {
            _musicInstance?.Pause();
        }
        catch (Exception)
        {
        }
    }

    internal static IEnumerable<(Sfx, float[])> Build()
    {
        yield return (Sfx.Click, Synth.Tone(Wave.Square, 0.03, 1800, 1500, 0.25, 0));
        yield return (Sfx.Select, Synth.Concat(Synth.Tone(Wave.Pulse25, 0.05, 880, 880, 0.3, 0.2), Synth.Tone(Wave.Pulse25, 0.08, 1320, 1320, 0.3, 0)));
        yield return (Sfx.Back, Synth.Concat(Synth.Tone(Wave.Pulse25, 0.05, 990, 990, 0.3, 0.2), Synth.Tone(Wave.Pulse25, 0.08, 660, 660, 0.3, 0)));
        yield return (Sfx.Start, Arpeggio(Wave.Pulse25, 0.35, 0.06, "C5", "E5", "G5", "C6", "E6", "G6"));
        yield return (Sfx.Shoot, Synth.LowPass(Synth.Tone(Wave.Square, 0.12, 1600, 380, 0.35, 0.02), 6000));
        yield return (Sfx.Laser, Synth.Mix(Synth.Tone(Wave.Saw, 0.2, 2400, 300, 0.3, 0), Synth.Tone(Wave.Square, 0.2, 1200, 150, 0.15, 0)));
        yield return (Sfx.Zap, Synth.Mix(Synth.Tone(Wave.Noise, 0.25, 9000, 3000, 0.25, 0), Synth.Tone(Wave.Saw, 0.25, 3000, 1500, 0.2, 0, vibrato: 0.15, vibratoHz: 40)));
        yield return (Sfx.Explode, Synth.LowPass(Synth.Mix(Synth.Tone(Wave.Noise, 0.5, 3000, 300, 0.6, 0), Synth.Tone(Wave.Square, 0.3, 160, 40, 0.3, 0)), 3500));
        yield return (Sfx.BigExplode, Synth.Echo(Synth.LowPass(Synth.Mix(Synth.Tone(Wave.Noise, 1.2, 2200, 120, 0.8, 0), Synth.Tone(Wave.Triangle, 1.0, 110, 30, 0.6, 0)), 2200), 0.09, 0.35, 3));
        yield return (Sfx.Hit, Synth.Mix(Synth.Tone(Wave.Noise, 0.12, 5000, 2000, 0.4, 0), Synth.Tone(Wave.Square, 0.1, 400, 150, 0.3, 0)));
        yield return (Sfx.Hurt, Synth.Tone(Wave.Square, 0.25, 500, 120, 0.4, 0.05, vibrato: 0.1, vibratoHz: 30));
        yield return (Sfx.Die, Synth.Echo(Synth.Mix(Synth.Tone(Wave.Square, 0.9, 700, 60, 0.35, 0, vibrato: 0.06, vibratoHz: 12), Synth.Tone(Wave.Noise, 0.6, 2000, 200, 0.3, 0)), 0.12, 0.3, 2));
        yield return (Sfx.Jump, Synth.Tone(Wave.Pulse25, 0.2, 280, 900, 0.35, 0.05));
        yield return (Sfx.Land, Synth.LowPass(Synth.Tone(Wave.Noise, 0.08, 1200, 300, 0.4, 0), 2000));
        yield return (Sfx.Bounce, Synth.Tone(Wave.Triangle, 0.1, 600, 300, 0.6, 0.1));
        yield return (Sfx.Coin, Synth.Concat(Synth.Tone(Wave.Pulse25, 0.06, 988, 988, 0.3, 0.25), Synth.Tone(Wave.Pulse25, 0.22, 1319, 1319, 0.3, 0)));
        yield return (Sfx.Pickup, Arpeggio(Wave.Pulse12, 0.3, 0.04, "C6", "E6", "G6", "C7"));
        yield return (Sfx.PowerUp, Synth.Tone(Wave.Pulse25, 0.5, 300, 1800, 0.3, 0.2, vibrato: 0.05, vibratoHz: 18));
        yield return (Sfx.Bonus, Synth.Echo(Arpeggio(Wave.Square, 0.3, 0.07, "G5", "C6", "E6", "G6", "C7"), 0.1, 0.3, 2));
        yield return (Sfx.LevelUp, Synth.Echo(Synth.Concat(
            Synth.Notes(Wave.Pulse25, 0.35, (Synth.Hz("C5"), 0.1), (Synth.Hz("E5"), 0.1), (Synth.Hz("G5"), 0.1)),
            Synth.Tone(Wave.Pulse25, 0.4, Synth.Hz("C6"), Synth.Hz("C6"), 0.35, 0, vibrato: 0.01)), 0.12, 0.3, 2));
        yield return (Sfx.Win, Fanfare());
        yield return (Sfx.Lose, Synth.Notes(Wave.Square, 0.3, (Synth.Hz("G4"), 0.18), (Synth.Hz("F#4"), 0.18), (Synth.Hz("F4"), 0.18), (Synth.Hz("E4"), 0.5)));
        yield return (Sfx.GameOver, Synth.Echo(Synth.LowPass(Synth.Notes(Wave.Square, 0.3,
            (Synth.Hz("C5"), 0.22), (Synth.Hz("G4"), 0.22), (Synth.Hz("E4"), 0.22), (Synth.Hz("A4"), 0.16), (Synth.Hz("B4"), 0.16),
            (Synth.Hz("A4"), 0.16), (Synth.Hz("Ab4"), 0.2), (Synth.Hz("Bb4"), 0.2), (Synth.Hz("Ab4"), 0.2), (Synth.Hz("G4"), 0.6)), 5000), 0.15, 0.25, 2));
        yield return (Sfx.Correct, Synth.Concat(Synth.Tone(Wave.Sine, 0.08, 1047, 1047, 0.5, 0.4), Synth.Tone(Wave.Sine, 0.25, 1568, 1568, 0.5, 0)));
        yield return (Sfx.Wrong, Synth.LowPass(Synth.Tone(Wave.Saw, 0.35, 160, 140, 0.4, 0.2), 1500));
        yield return (Sfx.Tick, Synth.Tone(Wave.Square, 0.015, 2400, 2400, 0.2, 0));
        yield return (Sfx.Beep, Synth.Tone(Wave.Square, 0.08, 1000, 1000, 0.25, 0.2));
        yield return (Sfx.Card, Synth.LowPass(Synth.Tone(Wave.Noise, 0.07, 8000, 4000, 0.3, 0), 6000));
        yield return (Sfx.Shuffle, Synth.Concat(Burstlets(6, 0.05)));
        yield return (Sfx.Splash, Synth.LowPass(Synth.Mix(Synth.Tone(Wave.Noise, 0.5, 6000, 800, 0.45, 0), Synth.Tone(Wave.Sine, 0.2, 300, 100, 0.3, 0)), 3000));
        yield return (Sfx.Crack, Synth.Mix(Synth.Tone(Wave.Noise, 0.15, 12000, 6000, 0.5, 0), Synth.Tone(Wave.Square, 0.05, 900, 600, 0.2, 0)));
        yield return (Sfx.Whoosh, Synth.LowPass(Synth.Tone(Wave.Noise, 0.4, 2000, 8000, 0.05, 0.3, attack: 0.2), 2500));
        yield return (Sfx.Thud, Synth.LowPass(Synth.Mix(Synth.Tone(Wave.Sine, 0.2, 120, 50, 0.7, 0), Synth.Tone(Wave.Noise, 0.08, 800, 200, 0.3, 0)), 800));
        yield return (Sfx.Pop, Synth.Tone(Wave.Sine, 0.08, 400, 1400, 0.5, 0));
        yield return (Sfx.Warp, Synth.Echo(Synth.Tone(Wave.Saw, 0.6, 100, 2000, 0.25, 0, vibrato: 0.1, vibratoHz: 25), 0.08, 0.4, 3));
        yield return (Sfx.Alarm, Synth.Concat(Synth.Tone(Wave.Square, 0.15, 880, 880, 0.25, 0.25), Synth.Tone(Wave.Square, 0.15, 660, 660, 0.25, 0.25),
            Synth.Tone(Wave.Square, 0.15, 880, 880, 0.25, 0.25), Synth.Tone(Wave.Square, 0.15, 660, 660, 0.25, 0)));
        yield return (Sfx.Bell, Synth.Mix(Synth.Tone(Wave.Sine, 1.0, 1760, 1760, 0.4, 0), Synth.Tone(Wave.Sine, 0.8, 2640, 2640, 0.2, 0), Synth.Tone(Wave.Sine, 0.4, 4400, 4400, 0.1, 0)));
        yield return (Sfx.Cannon, Synth.Echo(Synth.LowPass(Synth.Mix(Synth.Tone(Wave.Noise, 0.6, 1500, 100, 0.8, 0), Synth.Tone(Wave.Sine, 0.4, 90, 35, 0.8, 0)), 1500), 0.2, 0.3, 2));
        yield return (Sfx.Step, Synth.LowPass(Synth.Tone(Wave.Noise, 0.03, 3000, 1000, 0.25, 0), 3000));
        yield return (Sfx.Fuse, Synth.Tone(Wave.Noise, 0.3, 14000, 9000, 0.12, 0.12));
    }

    private static IEnumerable<(LoopSfx, float[])> BuildLoops()
    {
        // Loops are an exact number of cycles long so they repeat seamlessly.
        yield return (LoopSfx.Engine, Synth.LowPass(Synth.Mix(Cycles(Wave.Saw, 80, 40, 0.35), Cycles(Wave.Square, 40, 20, 0.2)), 1200));
        yield return (LoopSfx.Thrust, Synth.LowPass(Synth.Tone(Wave.Noise, 1.0, 3000, 3000, 0.35, 0.35, attack: 0.0001), 1500));
        yield return (LoopSfx.Wind, Synth.LowPass(Synth.Tone(Wave.Noise, 1.5, 6000, 6000, 0.3, 0.3, attack: 0.0001), 600));
        yield return (LoopSfx.Hum, Synth.LowPass(Synth.Mix(Cycles(Wave.Sine, 110, 55, 0.35), Cycles(Wave.Triangle, 165, 82, 0.15)), 2000));
    }

    private static float[] Cycles(Wave wave, double hz, int cycles, double volume)
    {
        int n = (int)Math.Round(cycles / hz * Synth.SampleRate);
        var full = new float[n];
        for (int i = 0; i < n; i++)
        {
            double p = (i * hz / Synth.SampleRate) % 1.0;
            full[i] = (float)(volume * wave switch
            {
                Wave.Saw => 2 * p - 1,
                Wave.Square => p < 0.5 ? 1 : -1,
                Wave.Triangle => 4 * Math.Abs(p - 0.5) - 1,
                _ => Math.Sin(2 * Math.PI * p),
            });
        }
        return full;
    }

    private static float[] Arpeggio(Wave wave, double volume, double step, params string[] notes)
    {
        var parts = new float[notes.Length][];
        for (int i = 0; i < notes.Length; i++)
        {
            double hz = Synth.Hz(notes[i]);
            bool last = i == notes.Length - 1;
            parts[i] = Synth.Tone(wave, last ? step * 3 : step, hz, hz, volume, last ? 0 : volume * 0.7);
        }
        return Synth.Concat(parts);
    }

    private static float[] Fanfare()
    {
        var lead = Synth.Notes(Wave.Pulse25, 0.3, (Synth.Hz("G5"), 0.12), (Synth.Hz("G5"), 0.12), (Synth.Hz("G5"), 0.12),
            (Synth.Hz("C6"), 0.5), (0, 0.05), (Synth.Hz("A5"), 0.12), (Synth.Hz("C6"), 0.12), (Synth.Hz("E6"), 0.7));
        var bass = Synth.Notes(Wave.Triangle, 0.4, (Synth.Hz("C3"), 0.36), (Synth.Hz("C4"), 0.55), (Synth.Hz("F3"), 0.24), (Synth.Hz("C4"), 0.7));
        return Synth.Echo(Synth.Mix(lead, bass), 0.12, 0.25, 2);
    }

    private static float[][] Burstlets(int count, double each)
    {
        var parts = new float[count][];
        for (int i = 0; i < count; i++)
            parts[i] = Synth.LowPass(Synth.Tone(Wave.Noise, each, 7000, 3000, 0.25, 0, seed: i + 3), 5000);
        return parts;
    }

    public void Dispose()
    {
        foreach (var e in _effects.Values)
            e.Dispose();
        foreach (var i in _loops.Values)
            i.Dispose();
        foreach (var e in _loopEffects)
            e.Dispose();
        _musicInstance?.Dispose();
        _music?.Dispose();
    }
}
