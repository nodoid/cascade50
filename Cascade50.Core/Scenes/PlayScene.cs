using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Cascade50.Core.Audio;
using Cascade50.Core.Games;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Scenes;

/// <summary>Runs one game: score bar, get-ready banner, pause menu, game over and best scores.</summary>
public sealed class PlayScene : Scene
{
    private enum State
    {
        Intro,
        Playing,
        Paused,
        Ending,
        Over,
    }

    private const int IntroTicks = 75;

    private readonly int _index;
    private MiniGame _game;
    private State _state;
    private int _stateTicks;
    private bool _newBest;
    private int _best;
    private string _error;

    public PlayScene(Cascade50Game app, int index) : base(app)
    {
        _index = index;
    }

    public MiniGame Game => _game;

    /// <summary>The current state (for the soak test).</summary>
    internal string StateName => _state.ToString();

    public override Pad? TouchPad =>
        _state is State.Playing or State.Intro && _game != null && _game.Pad != Pad.None ? _game.Pad : null;

    public override bool TextMode => _game?.TextEntry ?? false;

    public override void Enter()
    {
        App.Sound.SetMusic(false);
        NewGame();
    }

    public override void Leave()
    {
        App.Sound.StopLoops();
    }

    private void NewGame()
    {
        _game = GameCatalog.Create(_index);
        _game.Begin(In, App.Ui, App.GameSound, Environment.TickCount);
        App.Input.TouchPad.FireLabel = _game.FireLabel;
        App.Input.TouchPad.AltLabel = _game.AltLabel;
        _best = App.Profile.Best(_game.Number);
        _newBest = false;
        SetState(State.Intro);
    }

    private void SetState(State s)
    {
        _state = s;
        _stateTicks = 0;
        App.Input.Reset();
    }

    public override void Deactivated()
    {
        if (_state is State.Playing or State.Intro)
            SetState(State.Paused);
    }

    protected override void Update()
    {
        _stateTicks++;
        switch (_state)
        {
            case State.Intro:
                if (_stateTicks >= IntroTicks || (_stateTicks > 20 && (In.FirePressed || In.PointerPressed)))
                    SetState(State.Playing);
                else if (In.BackPressed)
                    SetState(State.Paused);
                break;

            case State.Playing:
                if (In.BackPressed || In.PausePressed || PauseButton())
                {
                    App.Sound.StopLoops();
                    App.Sound.Play(Sfx.Click);
                    SetState(State.Paused);
                    break;
                }
                StepGame();
                if (_game.IsOver)
                    SetState(State.Ending);
                break;

            case State.Ending:
                _game.Fx.Update(MiniGame.Dt);
                if (_stateTicks > 70)
                {
                    _best = App.Profile.Best(_game.Number);
                    _newBest = App.Profile.Record(_game.Number, _game.Score) && _game.Score > 0;
                    if (_newBest)
                        App.Sound.Play(Sfx.Bonus);
                    SetState(State.Over);
                }
                break;

            case State.Paused:
                UpdatePaused();
                break;

            case State.Over:
                UpdateOver();
                break;
        }
    }

    private void StepGame()
    {
        try
        {
            _game.Step();
        }
        catch (Exception e)
        {
            // Never crash the whole app because one game misbehaved.
            _error = e.GetType().Name;
            Platform.Diagnostics.Report(e, _game.Title + " update");
            _game.Abort("Oops! Something went wrong.");
        }
    }

    /// <summary>One tick of automatic play (store capture): no intro, restarts when the game ends.</summary>
    internal void CaptureTick()
    {
        if (_state != State.Playing)
            SetState(State.Playing);
        In.Clear();
        _game.AutoPlay(In);
        App.Ui.BeginFrame(In);
        PauseButton();
        StepGame();
        if (_game.IsOver)
            NewGame();
        _state = State.Playing;
    }

    private bool PauseButton() =>
        App.Ui.Button(new RectF(Screen.Width - 26, 2, 24, 18), "II", Keys.None, true, Pal.Panel, false, 1.25f, "pause");

    private static RectF PanelRect => RectF.Centered(320, 196, 300, 240);

    private void UpdatePaused()
    {
        var ui = App.Ui;
        var r = PanelRect;
        float bx = r.X + 30, bw = r.W - 60;
        if (ui.Button(new RectF(bx, r.Y + 64, bw, 34), "RESUME", Keys.Space) || In.PausePressed || In.EnterPressed)
        {
            SetState(State.Playing);
            return;
        }
        if (ui.Button(new RectF(bx, r.Y + 106, bw, 34), "RESTART", Keys.R))
        {
            NewGame();
            return;
        }
        if (ui.Button(new RectF(bx, r.Y + 148, bw, 34), "HOW TO PLAY", Keys.H))
        {
            App.ShowInfo(_index);
            return;
        }
        if (ui.Button(new RectF(bx, r.Y + 190, bw, 34), "QUIT TO MENU", Keys.M) || (In.BackPressed && _stateTicks > 2))
        {
            App.ShowMenu(_index);
        }
    }

    private void UpdateOver()
    {
        if (_stateTicks < 30)
            return;
        var ui = App.Ui;
        var r = PanelRect;
        float y = r.Bottom - 50;
        float w = (r.W - 40 - 16) / 3;
        if (ui.Button(new RectF(r.X + 20, y, w, 34), "MENU", Keys.Escape) || In.BackPressed)
        {
            App.ShowMenu(_index);
            return;
        }
        if (ui.Button(new RectF(r.X + 28 + w, y, w, 34), "AGAIN", Keys.Space) || In.EnterPressed)
        {
            App.Sound.Play(Sfx.Start);
            NewGame();
            return;
        }
        if (ui.Button(new RectF(r.X + 36 + 2 * w, y, w, 34), "NEXT", Keys.N))
            App.ShowInfo((_index + 1) % GameCatalog.Count);
    }

    public override void Draw(Gfx g)
    {
        Brand.Bezel(g, Seconds, _game.Accent);

        // The game, clipped to its playfield and shaken by its effects.
        g.SetClip(Screen.Bounds);
        g.Rect(Screen.Bounds, Color.Black);
        g.Offset = _game.Fx.ShakeOffset;
        try
        {
            _game.Draw(g);
        }
        catch (Exception e)
        {
            _error ??= e.GetType().Name;
            Platform.Diagnostics.Report(e, _game.Title + " draw");
        }
        _game.Fx.Draw(g);
        g.Offset = Vector2.Zero;
        g.SetClip(null);

        DrawHud(g);
        if (_state == State.Playing)
            App.Ui.Draw(g);

        switch (_state)
        {
            case State.Intro:
                DrawIntro(g);
                break;
            case State.Paused:
                DrawPaused(g);
                break;
            case State.Over:
                DrawOver(g);
                break;
        }
    }

    private void DrawHud(Gfx g)
    {
        var accent = _game.Accent;
        g.GradientV(0, 0, Screen.Width, Screen.HudHeight, new Color(6, 8, 26) * 0.92f, new Color(14, 16, 44) * 0.92f);
        g.Rect(0, Screen.HudHeight - 1, Screen.Width, 1, accent * 0.8f);

        g.Text("SCORE", 6, 7, 1f, Pal.Grey);
        g.Text(_game.Score.ToString(), 40, 4, 2f, Color.White);

        string mid = _game.Status ?? _game.Title.ToUpperInvariant();
        g.TextFit(mid, 320, 5, 220, 1.5f, accent, Align.Center);

        float x = Screen.Width - 34;
        int best = Math.Max(_best, _game.Score);
        x -= g.Text(best.ToString(), x, 4, 2f, best > _best && _best > 0 ? Pal.Yellow : Pal.LightGrey, Align.Right) + 4;
        x -= g.Text("BEST", x, 7, 1f, Pal.Grey, Align.Right) + 10;
        if (_game.Level > 0)
        {
            x -= g.Text(_game.Level.ToString(), x, 4, 2f, Pal.Cyan, Align.Right) + 4;
            x -= g.Text("LV", x, 7, 1f, Pal.Grey, Align.Right) + 10;
        }
        if (_game.Lives >= 0)
        {
            int shown = Math.Min(_game.Lives, 6);
            for (int i = 0; i < shown; i++)
            {
                float hx = x - 6 - i * 13;
                Heart(g, hx, 11, Pal.Red);
            }
            if (_game.Lives > 6)
                g.Text("+", x - 6 - 6 * 13, 7, 1f, Pal.Red, Align.Center);
        }
    }

    private static void Heart(Gfx g, float cx, float cy, Color c)
    {
        g.Circle(cx - 2.6f, cy - 1.5f, 3.2f, c);
        g.Circle(cx + 2.6f, cy - 1.5f, 3.2f, c);
        g.Triangle(new Vector2(cx - 5.6f, cy - 0.4f), new Vector2(cx + 5.6f, cy - 0.4f), new Vector2(cx, cy + 5.5f), c);
    }

    private void DrawIntro(Gfx g)
    {
        float t = _stateTicks / (float)IntroTicks;
        float a = t < 0.75f ? 1 : (1 - t) / 0.25f;
        g.Rect(Screen.Bounds, Color.Black * (0.45f * a));
        float s = MathF2.EaseOut(MathF2.Clamp(t * 3, 0, 1));
        g.TextShadow(_game.Title.ToUpperInvariant(), 320, 140 - (1 - s) * 30, 4f, _game.Accent * a, Align.Center);
        g.TextShadow("GET READY!", 320, 186, 2f, Color.White * a, Align.Center);
        var lines = In.IsTouch ? _game.TouchControls : _game.DesktopControls;
        if (lines.Length > 0)
            g.TextWrapped(lines[0], 120, 214, 400, 1.25f, Pal.Yellow * a, 1.35f, Align.Center);
    }

    private void DrawPaused(Gfx g)
    {
        g.Rect(g.Visible, Color.Black * 0.6f);
        var r = PanelRect;
        g.Panel(r, Pal.Panel, _game.Accent, 12);
        g.TextShadow("PAUSED", r.CenterX, r.Y + 22, 3f, _game.Accent, Align.Center);
        App.Ui.Draw(g);
    }

    private void DrawOver(Gfx g)
    {
        float a = MathF.Min(1, _stateTicks / 20f);
        g.Rect(g.Visible, Color.Black * (0.55f * a));
        var r = PanelRect;
        g.Panel(r.Offset(0, (1 - MathF2.EaseOut(a)) * 40), Pal.Panel, _game.Accent, 12);
        float y = r.Y + 18;
        g.TextShadow(_game.Won ? "WELL DONE!" : "GAME OVER", r.CenterX, y, 3f, _game.Won ? Pal.Green : _game.Accent, Align.Center);
        y += 34;
        if (!string.IsNullOrEmpty(_game.EndMessage))
            y += g.TextWrapped(_game.EndMessage.ToUpperInvariant(), r.X + 16, y, r.W - 32, 1.25f, Pal.LightGrey, 1.3f, Align.Center) + 4;
        g.Text("SCORE", r.CenterX, y + 4, 1.25f, Pal.Grey, Align.Center);
        g.TextShadow(_game.Score.ToString(), r.CenterX, y + 18, 3.5f, Color.White, Align.Center);
        y += 50;
        if (_newBest)
        {
            if ((_stateTicks / 15) % 2 == 0)
                g.TextShadow("NEW BEST SCORE!", r.CenterX, y, 2f, Pal.Yellow, Align.Center);
        }
        else
        {
            g.Text($"BEST {Math.Max(_best, _game.Score)}", r.CenterX, y, 1.5f, Pal.Yellow, Align.Center);
        }
        if (_error != null)
            g.Text(_error, r.CenterX, r.Bottom - 62, 1f, Pal.Red, Align.Center);
        if (_stateTicks >= 30)
            App.Ui.Draw(g);
    }
}
