using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Cascade50.Core.Audio;
using Cascade50.Core.Games;
using Cascade50.Core.Graphics;

namespace Cascade50.Core.Scenes;

/// <summary>The game picker: category tabs over a scrolling grid of the fifty games.</summary>
public sealed class MenuScene : Scene
{
    public const int Columns = 4;
    public const float TileW = 148, TileH = 104, Gap = 8;
    public const float GridX = (Screen.Width - Columns * TileW - (Columns - 1) * Gap) / 2;
    public const float GridTop = 52;

    private static readonly string[] Tabs = ["ALL", "ARCADE", "SHOOTERS", "SKILL", "PUZZLE", "BRAIN"];
    private static int _tab;
    private static float _savedScroll;

    private readonly List<int> _shown = new();
    private int _selected;
    private float _scroll, _scrollVel, _targetScroll = float.NaN;
    private bool _dragging;
    private float _dragStartScroll;
    private bool _options;

    public MenuScene(Cascade50Game app) : base(app)
    {
    }

    /// <summary>Opens the options panel straight away (store screenshots).</summary>
    internal bool ShowOptions
    {
        set => _options = value;
    }

    public override void Enter()
    {
        App.Sound.SetMusic(true);
        Filter();
        int last = App.Profile.LastGame;
        _selected = Math.Max(0, _shown.IndexOf(last));
        _scroll = _savedScroll;
        ClampScroll();
        EnsureVisible(_selected, true);
    }

    public override void Leave() => _savedScroll = _scroll;

    private void Filter()
    {
        _shown.Clear();
        var info = GameCatalog.Info;
        for (int i = 0; i < info.Count; i++)
            if (_tab == 0 || (int)info[i].Category == _tab - 1)
                _shown.Add(i);
    }

    private float ViewTop => GridTop;
    private float ViewHeight => Screen.Height - GridTop;
    private int Rows => (_shown.Count + Columns - 1) / Columns;
    private float ContentHeight => Rows * (TileH + Gap) + Gap;
    private float MaxScroll => MathF.Max(0, ContentHeight - ViewHeight);

    private RectF TileRect(int slot)
    {
        int col = slot % Columns, row = slot / Columns;
        return new RectF(GridX + col * (TileW + Gap), ViewTop + Gap / 2 + row * (TileH + Gap) - _scroll, TileW, TileH);
    }

    private void ClampScroll() => _scroll = MathF2.Clamp(_scroll, 0, MaxScroll);

    private void EnsureVisible(int slot, bool instant = false)
    {
        if (slot < 0)
            return;
        int row = slot / Columns;
        float top = row * (TileH + Gap);
        float target = _scroll;
        if (top < _scroll)
            target = top;
        else if (top + TileH + Gap > _scroll + ViewHeight)
            target = top + TileH + Gap * 1.5f - ViewHeight;
        target = MathF2.Clamp(target, 0, MaxScroll);
        if (instant)
            _scroll = target;
        else
            _targetScroll = target;
    }

    protected override void Update()
    {
        var ui = App.Ui;
        if (_options)
        {
            UpdateOptions();
            return;
        }

        // Tabs.
        float tx = 160;
        for (int i = 0; i < Tabs.Length; i++)
        {
            float w = Gfx.TextWidth(Tabs[i], 1.25f) + 16;
            if (ui.Button(new RectF(tx, 14, w, 24), Tabs[i], Keys.None, true, _tab == i ? Pal.Purple : Pal.Panel, _tab == i, 1.25f))
            {
                _tab = i;
                Filter();
                _scroll = 0;
                _selected = 0;
            }
            tx += w + 4;
        }
        if (In.KeyPressed(Keys.Tab))
        {
            _tab = (_tab + 1) % Tabs.Length;
            Filter();
            _scroll = 0;
            _selected = 0;
            App.Sound.Play(Sfx.Click);
        }
        if (ui.Button(new RectF(Screen.Width - 70, 14, 62, 24), "OPTIONS", Keys.O, true, Pal.Panel, false, 1.25f))
        {
            _options = true;
            return;
        }

        if (In.BackPressed && App.Platform.CanQuit)
        {
            _options = true;
            return;
        }

        // Keyboard / pad navigation.
        int before = _selected;
        if (In.LeftPressed) _selected--;
        if (In.RightPressed) _selected++;
        if (In.UpPressed) _selected -= Columns;
        if (In.DownPressed) _selected += Columns;
        if (_selected != before)
        {
            _selected = Math.Clamp(_selected, 0, _shown.Count - 1);
            App.Sound.Play(Sfx.Tick);
            EnsureVisible(_selected);
        }
        if ((In.FirePressed || In.EnterPressed) && _selected >= 0 && _selected < _shown.Count)
        {
            App.Sound.Play(Sfx.Select);
            App.ShowInfo(_shown[_selected]);
            return;
        }

        // Wheel.
        if (In.Wheel != 0)
        {
            _scroll -= In.Wheel * 60;
            _targetScroll = float.NaN;
        }

        // Pointer: drag to scroll, tap / click to open.
        var p = In.Pointer;
        bool inGrid = p.Y >= ViewTop;
        if (In.PointerPressed && inGrid)
        {
            _dragging = true;
            _dragStartScroll = _scroll;
            _scrollVel = 0;
            _targetScroll = float.NaN;
        }
        if (_dragging && In.PointerDown)
        {
            float prev = _scroll;
            _scroll = _dragStartScroll - (p.Y - In.PointerStart.Y);
            _scrollVel = (_scroll - prev) * 60;
        }
        if (_dragging && In.PointerReleased)
        {
            _dragging = false;
            if (Vector2.Distance(p, In.PointerStart) < 10)
            {
                _scrollVel = 0;
                int slot = SlotAt(p);
                if (slot >= 0)
                {
                    _selected = slot;
                    App.Sound.Play(Sfx.Select);
                    App.ShowInfo(_shown[slot]);
                    return;
                }
            }
        }
        if (!In.IsTouch && In.HasHover && In.PointerMoved && !_dragging)
        {
            int slot = SlotAt(p);
            if (slot >= 0)
                _selected = slot;
        }

        if (!_dragging)
        {
            if (!float.IsNaN(_targetScroll))
            {
                _scroll = MathF2.Lerp(_scroll, _targetScroll, 0.25f);
                if (MathF.Abs(_scroll - _targetScroll) < 0.5f)
                {
                    _scroll = _targetScroll;
                    _targetScroll = float.NaN;
                }
            }
            else
            {
                _scroll += _scrollVel / 60;
                _scrollVel *= 0.92f;
            }
        }
        ClampScroll();
    }

    private int SlotAt(Vector2 p)
    {
        if (p.Y < ViewTop)
            return -1;
        for (int i = 0; i < _shown.Count; i++)
            if (TileRect(i).Contains(p))
                return i;
        return -1;
    }

    private void UpdateOptions()
    {
        var ui = App.Ui;
        var panel = OptionsRect;
        float bx = panel.X + 24, bw = panel.W - 48;
        if (ui.Button(new RectF(bx, panel.Y + 52, bw, 30), "SOUND EFFECTS: " + (App.Sound.SoundOn ? "ON" : "OFF"), Keys.S))
        {
            App.Sound.SoundOn = !App.Sound.SoundOn;
            App.SaveSettings();
        }
        if (ui.Button(new RectF(bx, panel.Y + 90, bw, 30), "MUSIC: " + (App.Sound.MusicOn ? "ON" : "OFF"), Keys.M))
        {
            App.Sound.MusicOn = !App.Sound.MusicOn;
            App.Sound.SetMusic(App.Sound.MusicOn);
            App.SaveSettings();
        }
        bool canQuit = App.Platform.CanQuit && !App.IsMobile;
        float cw = canQuit ? (bw - 8) / 2 : bw;
        if (ui.Button(new RectF(bx, panel.Bottom - 42, cw, 30), "CLOSE", Keys.Escape) || In.FirePressed)
            _options = false;
        if (canQuit && ui.Button(new RectF(bx + cw + 8, panel.Bottom - 42, cw, 30), "QUIT", Keys.Q, true, Pal.Darken(Pal.Red, 0.4f)))
            App.Quit();
    }

    private static RectF OptionsRect => RectF.Centered(320, 190, 340, 260);

    public override void Draw(Gfx g)
    {
        Brand.Backdrop(g, Seconds);
        var info = GameCatalog.Info;

        // Tiles.
        g.SetClip(new RectF(g.Visible.X, ViewTop, g.Visible.W, g.Visible.Bottom - ViewTop));
        for (int i = 0; i < _shown.Count; i++)
        {
            var r = TileRect(i);
            if (r.Bottom < ViewTop || r.Top > Screen.Height)
                continue;
            DrawTile(g, info[_shown[i]], r, i == _selected && !In.IsTouch, Seconds);
        }
        g.SetClip(null);

        // Header.
        g.GradientV(g.Visible.X, g.Visible.Y, g.Visible.W, ViewTop - g.Visible.Y, new Color(8, 6, 28), new Color(8, 6, 28) * 0.85f);
        g.Rect(g.Visible.X, ViewTop - 1, g.Visible.W, 1, Pal.Add(Pal.Magenta, 0.7f));
        Brand.Logo(g, 72, 26, 2.2f, Seconds, false);

        // Scroll bar.
        if (MaxScroll > 0)
        {
            float trackH = ViewHeight - 8;
            float barH = MathF.Max(24, trackH * ViewHeight / ContentHeight);
            float barY = ViewTop + 4 + (trackH - barH) * (_scroll / MaxScroll);
            g.RoundRect(Screen.Width - 5, barY, 3, barH, 1.5f, Color.White * 0.35f);
        }

        if (!_options)
            App.Ui.Draw(g);
        else
        {
            DrawOptions(g);
        }
    }

    private void DrawOptions(Gfx g)
    {
        g.Rect(g.Visible, Color.Black * 0.6f);
        var r = OptionsRect;
        g.Panel(r, Pal.Panel, Pal.Accent, 12);
        g.TextShadow("OPTIONS", r.CenterX, r.Y + 16, 2.5f, Pal.Accent, Align.Center);
        App.Ui.Draw(g);
        float y = r.Y + 134;
        g.Text(Cascade50Game.Name + "  V" + typeof(MenuScene).Assembly.GetName().Version?.ToString(3), r.CenterX, y, 1.25f, Pal.White, Align.Center);
        g.Text(Cascade50Game.Credit, r.CenterX, y + 16, 1.25f, Pal.Cyan, Align.Center);
        g.Text(Cascade50Game.BasedOn, r.CenterX, y + 32, 1f, Pal.LightGrey, Align.Center);
        int played = App.Profile.GamesPlayed();
        g.Text($"GAMES TRIED: {played} OF {GameCatalog.Count}", r.CenterX, y + 50, 1.25f, Pal.Yellow, Align.Center);
    }

    /// <summary>A menu tile: animated picture, number, title and best score.</summary>
    public void DrawTile(Gfx g, MiniGame game, RectF r, bool selected, float time)
    {
        var accent = game.Accent;
        if (selected)
            g.Glow(r.CenterX, r.CenterY, r.W * 0.75f, accent, 0.35f);
        g.RoundRect(r.Offset(0, 3), 9, Color.Black * 0.45f);
        g.Panel(r, Pal.Panel, selected ? Color.White : Pal.Darken(accent, 0.35f), 9);
        var icon = new RectF(r.X + 4, r.Y + 4, r.W - 8, 70);
        g.SetClip(Intersect(icon, new RectF(g.Visible.X, ViewTop, g.Visible.W, Screen.Height - ViewTop)));
        g.Rect(icon, Color.Black);
        try
        {
            game.DrawIcon(g, icon, time + game.Number * 0.37f);
        }
        catch (Exception e)
        {
            // A broken icon must never take the menu down.
            Platform.Diagnostics.Report(e, game.Title + " icon");
        }
        g.Offset = Vector2.Zero;
        g.SetClip(new RectF(g.Visible.X, ViewTop, g.Visible.W, g.Visible.Bottom - ViewTop));
        // Number badge.
        g.RoundRect(r.X + 6, r.Y + 6, 22, 14, 4, Color.Black * 0.7f);
        g.Text(game.Number.ToString("D2"), r.X + 17, r.Y + 9, 1f, accent, Align.Center);
        g.TextFit(game.Title.ToUpperInvariant(), r.CenterX, r.Y + 78, r.W - 10, 1.5f, Color.White, Align.Center);
        int best = App.Profile.Best(game.Number);
        string sub = best > 0 ? "BEST " + best : Brand.CategoryName(game.Category);
        g.Text(sub, r.CenterX, r.Y + 92, 1f, best > 0 ? Pal.Yellow : Pal.Grey, Align.Center);
    }

    private static RectF Intersect(RectF a, RectF b)
    {
        float x0 = MathF.Max(a.X, b.X), y0 = MathF.Max(a.Y, b.Y);
        float x1 = MathF.Min(a.Right, b.Right), y1 = MathF.Min(a.Bottom, b.Bottom);
        return new RectF(x0, y0, MathF.Max(0, x1 - x0), MathF.Max(0, y1 - y0));
    }
}
