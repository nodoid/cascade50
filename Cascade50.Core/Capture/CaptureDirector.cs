using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Cascade50.Core.Audio;
using Cascade50.Core.Games;
using Cascade50.Core.Scenes;

namespace Cascade50.Core.Capture;

/// <summary>
/// Store asset capture, run from the desktop build:
/// <code>
/// Cascade50 --capture &lt;dir&gt; --size 2880x1800[,1440x900] [--mobile] [--only g01,menu]   screenshots
/// Cascade50 --art &lt;dir&gt;                                                              icons and store art
/// Cascade50 --video &lt;file.mp4&gt; --size 1920x1080 [--mobile]                              app preview (needs ffmpeg)
/// </code>
/// Every image is rendered natively at the requested size (the renderer is resolution independent).
/// </summary>
public sealed class CaptureDirector
{
    private sealed record Shot(string Name, Func<Cascade50Game, Scene> Create, int Ticks, int Frames = 1);

    private enum Mode
    {
        Stills,
        Art,
        Video,
    }

    private readonly Mode _mode;
    private readonly string _output;
    private readonly List<(int w, int h)> _sizes = new();
    private readonly List<Shot> _shots = new();
    private readonly List<(string name, ArtKind kind, int w, int h)> _art = new();
    private readonly Dictionary<(int, int), RenderTarget2D> _targets = new();
    private int _index = -1;
    private Scene _scene;
    private int _frame;
    private bool _ready;
    private Process _ffmpeg;
    private Stream _ffmpegIn;
    private int _videoFrame;
    private readonly List<(int frame, Sfx sfx, float pitch, float volume)> _sounds = new();
    private readonly List<(int start, int end)> _musicSpans = new();
    private string _videoTemp;

    private CaptureDirector(Mode mode, string output, bool mobile)
    {
        _mode = mode;
        _output = output;
        Mobile = mobile;
    }

    public bool Mobile { get; }

    public static CaptureDirector FromArgs(string[] args)
    {
        string Arg(string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
        bool mobile = Array.IndexOf(args, "--mobile") >= 0;
        string only = Arg("--only");
        CaptureDirector d;
        if (Arg("--capture") is string dir)
        {
            d = new CaptureDirector(Mode.Stills, dir, mobile);
            d.ParseSizes(Arg("--size") ?? "1920x1080");
            d.AddStills(only);
        }
        else if (Arg("--art") is string artDir)
        {
            d = new CaptureDirector(Mode.Art, artDir, false);
            d.AddArt();
        }
        else if (Arg("--video") is string file)
        {
            d = new CaptureDirector(Mode.Video, file, mobile);
            d.ParseSizes(Arg("--size") ?? "1920x1080");
            d.AddVideo(only);
        }
        else
        {
            return null;
        }
        return d;
    }

    private void ParseSizes(string spec)
    {
        foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var wh = part.Split('x');
            _sizes.Add((int.Parse(wh[0], CultureInfo.InvariantCulture), int.Parse(wh[1], CultureInfo.InvariantCulture)));
        }
    }

    private static string Slug(MiniGame g) =>
        $"g{g.Number:D2}-" + g.Title.ToLowerInvariant().Replace(' ', '-').Replace("'", "");

    private void AddStills(string only)
    {
        var all = new List<Shot>
        {
            new("splash", a => new SplashScene(a), 140),
            new("menu", a => new MenuScene(a), 40),
            new("info", a => new InfoScene(a, GameCatalog.IndexOf(22)), 30),
            new("options", a => new MenuScene(a) { ShowOptions = true }, 10),
        };
        var info = GameCatalog.Info;
        for (int i = 0; i < info.Count; i++)
        {
            int index = i;
            all.Add(new Shot(Slug(info[i]), a => new PlayScene(a, index), info[i] is ICaptureHints h ? h.CaptureTicks : 420));
        }
        // Each game's info page (big icon and instructions), only when asked for by name: --only i05
        if (!string.IsNullOrEmpty(only))
            for (int i = 0; i < info.Count; i++)
            {
                int index = i;
                all.Add(new Shot("i" + Slug(info[i])[1..], a => new InfoScene(a, index), 30));
            }
        foreach (var s in all)
            if (Matches(s.Name, only))
                _shots.Add(s);
    }

    private static bool Matches(string name, string only)
    {
        if (string.IsNullOrEmpty(only))
            return true;
        foreach (var p in only.Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (name.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private void AddArt()
    {
        _art.Add(("icon-1024", ArtKind.Icon, 1024, 1024));
        _art.Add(("icon-foreground-1024", ArtKind.IconForeground, 1024, 1024));
        _art.Add(("icon-background-1024", ArtKind.IconBackground, 1024, 1024));
        _art.Add(("splash-1240x600", ArtKind.Splash, 1240, 600));
        _art.Add(("splash-square-1152", ArtKind.Splash, 1152, 1152));
        _art.Add(("feature-graphic-1024x500", ArtKind.Titled, 1024, 500));
        _art.Add(("super-hero-art-3840x2160", ArtKind.Untitled, 3840, 2160));
        _art.Add(("super-hero-art-1920x1080", ArtKind.Untitled, 1920, 1080));
        _art.Add(("titled-hero-art-3840x2160", ArtKind.Titled, 3840, 2160));
        _art.Add(("titled-hero-art-1920x1080", ArtKind.Titled, 1920, 1080));
        _art.Add(("box-art-2160x2160", ArtKind.Titled, 2160, 2160));
        _art.Add(("poster-art-1440x2160", ArtKind.Titled, 1440, 2160));
        _art.Add(("branded-key-art-584x800", ArtKind.Titled, 584, 800));
        _art.Add(("featured-promotional-square-art-2160x2160", ArtKind.Untitled, 2160, 2160));
        _art.Add(("featured-promotional-square-art-1080x1080", ArtKind.Untitled, 1080, 1080));
    }

    /// <summary>About 29 seconds: the title, the menu, then a run of games.</summary>
    private void AddVideo(string only)
    {
        _shots.Add(new Shot("splash", a => new SplashScene(a), 0, 150));
        _shots.Add(new Shot("menu", a => new MenuScene(a), 0, 90));
        int[] games = string.IsNullOrEmpty(only)
            ? [11, 22, 2, 40, 21, 48, 32, 15, 35, 49]
            : Array.ConvertAll(only.Split(','), s => int.Parse(s, CultureInfo.InvariantCulture));
        foreach (int n in games)
        {
            int index = GameCatalog.IndexOf(n);
            _shots.Add(new Shot("game" + n, a => new PlayScene(a, index), 240, 63));
        }
    }

    public void Start(Cascade50Game app)
    {
        app.Input.Controls.IsTouch = Mobile;
        if (_mode == Mode.Video)
            StartFfmpeg(app);
    }

    public void Update(Cascade50Game app)
    {
        if (_ready)
            return;
        if (_mode == Mode.Art)
        {
            if (++_index >= _art.Count)
            {
                Finish(app);
                return;
            }
            app.SetSceneImmediately(new ArtScene(app, _art[_index].kind));
            _ready = true;
            return;
        }

        if (_scene == null)
        {
            if (++_index >= _shots.Count)
            {
                Finish(app);
                return;
            }
            _scene = _shots[_index].Create(app);
            app.SetSceneImmediately(_scene);
            for (int i = 0; i < _shots[_index].Ticks; i++)
                TickScene(app, _scene);
            _frame = 0;
            if (_mode == Mode.Video && _shots[_index].Name is "splash" or "menu")
                _musicSpans.Add((_videoFrame, _videoFrame + _shots[_index].Frames));
        }
        else
        {
            // Videos run at 30 fps: two game ticks per frame.
            TickScene(app, _scene);
            TickScene(app, _scene);
        }
        _ready = true;
    }

    private void TickScene(Cascade50Game app, Scene scene)
    {
        var c = app.Input.Controls;
        if (scene is PlayScene ps)
        {
            ps.CaptureTick();
            return;
        }
        c.Clear();
        app.Ui.BeginFrame(c);
        scene.Tick();
    }

    public void Draw(Cascade50Game app)
    {
        if (!_ready)
            return;
        _ready = false;
        var device = app.GraphicsDevice;

        if (_mode == Mode.Art)
        {
            var (name, _, w, h) = _art[_index];
            var rt = Target(device, w, h);
            Render(app, rt, w, h);
            Save(rt, Path.Combine(_output, name + ".png"));
            return;
        }

        var shot = _shots[_index];
        if (_mode == Mode.Stills)
        {
            foreach (var (w, h) in _sizes)
            {
                var rt = Target(device, w, h);
                Render(app, rt, w, h);
                Save(rt, Path.Combine(_output, $"{w}x{h}", shot.Name + ".png"));
            }
            Console.WriteLine("captured " + shot.Name);
            _scene = null;
            return;
        }

        // Video frame.
        var (vw, vh) = _sizes[0];
        var target = Target(device, vw, vh);
        Render(app, target, vw, vh);
        var data = new byte[vw * vh * 4];
        target.GetData(data);
        _ffmpegIn?.Write(data, 0, data.Length);
        _videoFrame++;
        if (++_frame >= shot.Frames)
        {
            Console.WriteLine("recorded " + shot.Name);
            _scene = null;
        }
    }

    private void Render(Cascade50Game app, RenderTarget2D rt, int w, int h)
    {
        var device = app.GraphicsDevice;
        device.SetRenderTarget(rt);
        device.Clear(Color.Black);
        app.RenderScene(app.Gfx, w, h, 0);
        device.SetRenderTarget(null);
    }

    private RenderTarget2D Target(GraphicsDevice device, int w, int h)
    {
        if (!_targets.TryGetValue((w, h), out var rt))
        {
            rt = new RenderTarget2D(device, w, h, false, SurfaceFormat.Color, DepthFormat.None);
            _targets[(w, h)] = rt;
        }
        return rt;
    }

    private static void Save(RenderTarget2D rt, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = File.Create(path);
        rt.SaveAsPng(file, rt.Width, rt.Height);
    }

    // ------------------------------------------------------------------ video

    /// <summary>Records sounds the games make during a video, to build the soundtrack.</summary>
    internal void Heard(Sfx sfx, float pitch, float volume)
    {
        if (_mode == Mode.Video && _scene != null && _frame > 0)
            _sounds.Add((_videoFrame, sfx, pitch, volume));
    }

    private void StartFfmpeg(Cascade50Game app)
    {
        var (w, h) = _sizes[0];
        _videoTemp = Path.Combine(Path.GetTempPath(), "cascade50-video-" + Environment.ProcessId);
        Directory.CreateDirectory(_videoTemp);
        var psi = new ProcessStartInfo(FindFfmpeg())
        {
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        foreach (var a in new[]
                 {
                     "-y", "-loglevel", "error", "-f", "rawvideo", "-pix_fmt", "rgba", "-s", $"{w}x{h}", "-r", "30", "-i", "-",
                     "-c:v", "libx264", "-pix_fmt", "yuv420p", "-profile:v", "high", "-crf", "16", "-r", "30",
                     Path.Combine(_videoTemp, "video.mp4"),
                 })
            psi.ArgumentList.Add(a);
        _ffmpeg = Process.Start(psi);
        _ffmpegIn = _ffmpeg!.StandardInput.BaseStream;
        app.UseRecordingSound(this);
    }

    private static string FindFfmpeg()
    {
        foreach (var p in new[] { "/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg", "/usr/bin/ffmpeg" })
            if (File.Exists(p))
                return p;
        return "ffmpeg";
    }

    private void Finish(Cascade50Game app)
    {
        if (_mode == Mode.Video && _ffmpeg != null)
        {
            _ffmpegIn.Close();
            _ffmpeg.WaitForExit();
            string wav = Path.Combine(_videoTemp, "audio.wav");
            WriteSoundtrack(wav);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_output))!);
            var mux = new ProcessStartInfo(FindFfmpeg()) { UseShellExecute = false };
            foreach (var a in new[]
                     {
                         "-y", "-loglevel", "error", "-i", Path.Combine(_videoTemp, "video.mp4"), "-i", wav,
                         "-c:v", "copy", "-c:a", "aac", "-b:a", "256k", "-ac", "2", "-ar", "44100", "-shortest", _output,
                     })
                mux.ArgumentList.Add(a);
            Process.Start(mux)!.WaitForExit();
            Directory.Delete(_videoTemp, true);
            Console.WriteLine("video: " + _output);
        }
        foreach (var rt in _targets.Values)
            rt.Dispose();
        _targets.Clear();
        app.Exit();
    }

    private void WriteSoundtrack(string path)
    {
        int total = _videoFrame * Synth.SampleRate / 30;
        var mix = new float[total];
        var music = Music.MenuTheme();
        foreach (var (start, end) in _musicSpans)
        {
            int s0 = start * Synth.SampleRate / 30, s1 = Math.Min(total, end * Synth.SampleRate / 30);
            for (int i = s0; i < s1; i++)
            {
                float fade = MathF.Min(1, (s1 - i) / (Synth.SampleRate * 0.3f));
                mix[i] += music[(i - s0) % music.Length] * 0.45f * fade;
            }
        }
        var clips = new Dictionary<Sfx, float[]>();
        foreach (var (id, pcm) in SoundBank.Build())
            clips[id] = pcm;
        int lastFrame = -100;
        foreach (var (frame, sfx, pitch, volume) in _sounds)
        {
            var clip = clips[sfx];
            double rate = Math.Pow(2, pitch);
            int start = frame * Synth.SampleRate / 30;
            float gain = volume * 0.6f * (frame == lastFrame ? 0.6f : 1f);
            lastFrame = frame;
            for (int i = 0; ; i++)
            {
                int src = (int)(i * rate);
                if (src >= clip.Length || start + i >= total)
                    break;
                mix[start + i] += clip[src] * gain;
            }
        }
        var pcmBytes = Synth.ToPcm(mix);
        using var w = new BinaryWriter(File.Create(path));
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + pcmBytes.Length);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(Synth.SampleRate);
        w.Write(Synth.SampleRate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8.ToArray());
        w.Write(pcmBytes.Length);
        w.Write(pcmBytes);
    }
}

/// <summary>Optional: a game can say how long to auto-play before its screenshot is taken.</summary>
public interface ICaptureHints
{
    int CaptureTicks { get; }
}

/// <summary>Sound used while recording a video: silent, but every effect is noted for the soundtrack.</summary>
internal sealed class RecordingSound : ISound
{
    private readonly CaptureDirector _director;

    public RecordingSound(CaptureDirector director)
    {
        _director = director;
    }

    public void Play(Sfx sfx, float pitch = 0, float volume = 1) => _director.Heard(sfx, pitch, volume);
    public void Loop(LoopSfx loop, bool on, float pitch = 0, float volume = 1) { }
    public void StopLoops() { }
}
