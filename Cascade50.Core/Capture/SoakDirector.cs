using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Xna.Framework.Input;
using Cascade50.Core.Games;
using Cascade50.Core.Platform;
using Cascade50.Core.Scenes;

namespace Cascade50.Core.Capture;

/// <summary>
/// Unattended soak test of the real app: <c>Cascade50 --soak &lt;log&gt; [--mobile] [--seconds 60]</c>.
/// For every game it browses the menu and info page, plays with the game's autopilot, pauses and
/// resumes, loses focus, plays again after game over and quits back to the menu, while changing the
/// window shape. Everything runs through the normal scenes, renderer and (muted) sound engine.
/// Exceptions the app recovers from are logged too. The exit code is 0 only when nothing went wrong.
/// </summary>
public sealed class SoakDirector
{
    private enum Phase
    {
        Menu,
        Info,
        Play,
        Leaving,
    }

    private sealed class Result
    {
        public string Title;
        public int PlayTicks, GameOvers, BestScore, Pauses, Errors;
        public double MaxFrameMs;
    }

    private static readonly (int w, int h)[] WindowSizes = [(1280, 720), (1024, 768), (1600, 700), (900, 900), (1440, 900)];

    private readonly string _logPath;
    private readonly int _playTicks;
    private readonly StreamWriter _log;
    private readonly List<Result> _results = new();
    private readonly List<string> _errors = new();
    private readonly Random _rng = new(50);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private int _index = -1;
    private Phase _phase;
    private int _phaseTicks, _playedTicks, _overTicks, _pausedTicks, _restarts, _gameTicks;
    private bool _deactivated;
    private Result _current;
    private Type _expected;

    private SoakDirector(string logPath, bool mobile, int seconds)
    {
        _logPath = logPath;
        Mobile = mobile;
        _playTicks = seconds * Cascade50Game.TicksPerSecond;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logPath))!);
        _log = new StreamWriter(logPath) { AutoFlush = true };
        Diagnostics.Reported += (e, context) => Error(context, e);
    }

    public bool Mobile { get; }
    public bool Failed => _errors.Count > 0;

    /// <summary>Where best scores are kept during the soak, so the player's own are untouched.</summary>
    public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "cascade50-soak-" + Environment.ProcessId);

    public static SoakDirector FromArgs(string[] args)
    {
        int i = Array.IndexOf(args, "--soak");
        if (i < 0 || i + 1 >= args.Length)
            return null;
        int s = Array.IndexOf(args, "--seconds");
        int seconds = s >= 0 && s + 1 < args.Length ? int.Parse(args[s + 1]) : 60;
        return new SoakDirector(args[i + 1], Array.IndexOf(args, "--mobile") >= 0, seconds);
    }

    public void Log(string line)
    {
        string text = $"[{_clock.Elapsed:hh\\:mm\\:ss}] {line}";
        _log.WriteLine(text);
        Console.WriteLine(text);
    }

    public void Error(string context, Exception e)
    {
        string text = $"ERROR in {context}: {e}";
        _errors.Add(text);
        if (_current != null)
            _current.Errors++;
        Log(text);
    }

    public void Start(Cascade50Game app)
    {
        Log($"Soak test: {GameCatalog.Count} games, {_playTicks / 60} s of play each, {(Mobile ? "touch" : "desktop")} layout");
        Next(app);
    }

    private void Next(Cascade50Game app)
    {
        if (_current != null)
        {
            _results.Add(_current);
            Log($"  done {_current.Title}: {_current.PlayTicks / 60} s played, {_current.GameOvers} game overs, best {_current.BestScore}, " +
                $"{_current.Pauses} pauses, slowest frame {_current.MaxFrameMs:F1} ms, {GC.GetTotalMemory(false) / 1048576} MB managed, {_current.Errors} errors");
        }
        if (++_index >= GameCatalog.Count)
        {
            Finish(app);
            return;
        }
        var info = GameCatalog.Info[_index];
        _current = new Result { Title = $"{info.Number:D2} {info.Title}" };
        Log($"Game {_current.Title}");
        var (w, h) = WindowSizes[_index % WindowSizes.Length];
        app.SetWindowSize(w, h);
        _restarts = 0;
        _gameTicks = 0;
        _deactivated = false;
        Go(app, Phase.Menu);
    }

    private void Go(Cascade50Game app, Phase phase)
    {
        _phase = phase;
        _phaseTicks = 0;
        switch (phase)
        {
            case Phase.Menu:
                _expected = typeof(MenuScene);
                app.ShowMenu(_index);
                break;
            case Phase.Info:
                _expected = typeof(InfoScene);
                app.ShowInfo(_index);
                break;
            case Phase.Play:
                _expected = typeof(PlayScene);
                _playedTicks = 0;
                app.Play(_index);
                break;
        }
    }

    /// <summary>Called every tick after the real input is read: replaces it with the soak's own.</summary>
    public void Inject(Cascade50Game app)
    {
        var c = app.Input.Controls;
        var scene = app.Scene;
        if (scene == null || scene.GetType() != _expected)
            return; // still fading between scenes
        _phaseTicks++;
        c.Clear();
        c.IsTouch = Mobile;

        switch (_phase)
        {
            case Phase.Menu:
                // Wander round the grid, scroll, then open the game.
                if (_phaseTicks % 9 == 0)
                    c.SetDirections(_rng.Next(3) - 1, _rng.Next(3) - 1);
                if (_phaseTicks % 25 == 0)
                    c.Wheel = _rng.Next(2) == 0 ? 1 : -1;
                if (_phaseTicks >= 70)
                    Go(app, Phase.Info);
                break;

            case Phase.Info:
                c.SetDirections(0, _phaseTicks < 40 ? 1 : -1);
                if (_phaseTicks >= 70)
                    Go(app, Phase.Play);
                break;

            case Phase.Play:
                PlayTick(app, (PlayScene)scene, c);
                break;
        }
    }

    private void PlayTick(Cascade50Game app, PlayScene ps, Input.Controls c)
    {
        var game = ps.Game;
        _current.BestScore = Math.Max(_current.BestScore, game.Score);
        switch (ps.StateName)
        {
            case "Intro":
                break;

            case "Playing":
                _playedTicks++;
                _current.PlayTicks++;
                game.AutoPlay(c);
                c.IsTouch = Mobile;
                if (_playedTicks >= _playTicks)
                {
                    c.PausePressed = true; // then quit to the menu from the pause screen
                }
                else if (_playedTicks == _playTicks / 3)
                {
                    c.PausePressed = true;
                }
                else if (_playedTicks == _playTicks * 2 / 3 && !_deactivated)
                {
                    _deactivated = true;
                    ps.Deactivated(); // as if the window lost focus
                }
                break;

            case "Paused":
                if (_pausedTicks == 0)
                    _current.Pauses++;
                _pausedTicks++;
                if (_pausedTicks > 30)
                {
                    _pausedTicks = 0;
                    c.PressedKeys.Add(_playedTicks >= _playTicks ? Keys.M : Keys.Space);
                    if (_playedTicks >= _playTicks)
                        _phase = Phase.Leaving;
                }
                return;

            case "Ending":
                _overTicks = 0;
                break;

            case "Over":
                if (++_overTicks == 1)
                    _current.GameOvers++;
                if (_overTicks > 50)
                {
                    _overTicks = 0;
                    if (_playedTicks < _playTicks && _restarts < 3)
                    {
                        _restarts++;
                        c.PressedKeys.Add(Keys.Space); // AGAIN
                    }
                    else
                    {
                        c.PressedKeys.Add(Keys.Escape); // MENU
                        _phase = Phase.Leaving;
                    }
                }
                break;
        }
    }

    /// <summary>Called after each tick: notices when a game has been left for the menu.</summary>
    public void AfterTick(Cascade50Game app)
    {
        if (_index >= GameCatalog.Count)
            return;
        if (_phase == Phase.Leaving && app.Scene is MenuScene)
        {
            Next(app);
            return;
        }
        // Watchdog: a game that can't be finished or left is a bug too.
        if (++_gameTicks > _playTicks * 4 + 6000)
        {
            Error(_current.Title + " watchdog", new TimeoutException($"stuck in {app.Scene?.GetType().Name} ({(app.Scene as PlayScene)?.StateName})"));
            Next(app);
        }
    }

    public void FrameTime(double ms)
    {
        if (_current != null && _phase == Phase.Play)
            _current.MaxFrameMs = Math.Max(_current.MaxFrameMs, ms);
    }

    private void Finish(Cascade50Game app)
    {
        Log("");
        Log($"Finished in {_clock.Elapsed:hh\\:mm\\:ss}: {_results.Count} games played, {_errors.Count} errors.");
        foreach (var r in _results)
            if (r.Errors > 0 || r.PlayTicks == 0)
                Log($"  PROBLEM {r.Title}: {r.Errors} errors, {r.PlayTicks / 60} s played");
        Log(_errors.Count == 0 ? "RESULT: PASS" : "RESULT: FAIL");
        _log.Dispose();
        try
        {
            Directory.Delete(DataDirectory, true);
        }
        catch (Exception)
        {
        }
        Environment.ExitCode = _errors.Count == 0 ? 0 : 1;
        app.Exit();
    }
}
