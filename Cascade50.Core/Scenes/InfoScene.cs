using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Cascade50.Core.Audio;
using Cascade50.Core.Games;
using Cascade50.Core.Graphics;

namespace Cascade50.Core.Scenes;

/// <summary>A game's page: picture, how to play, controls for this device, best score and PLAY.</summary>
public sealed class InfoScene : Scene
{
    private const float TextX = 318, TextW = 304, TextTop = 22, TextBottom = 304;
    private const float BodyScale = 1.5f;

    private readonly int _index;
    private readonly MiniGame _game;
    private float _scroll, _contentHeight;
    private bool _dragging;
    private float _dragStart;

    public InfoScene(Cascade50Game app, int index) : base(app)
    {
        _index = index;
        _game = GameCatalog.Info[index];
    }

    public override void Enter() => App.Sound.SetMusic(true);

    protected override void Update()
    {
        var ui = App.Ui;
        if (ui.Button(new RectF(16, 316, 90, 34), "BACK", Keys.Escape) || In.BackPressed)
        {
            App.Sound.Play(Sfx.Back);
            App.ShowMenu();
            return;
        }
        if (ui.Button(new RectF(112, 316, 40, 34), "<", Keys.Left, true, Pal.Panel) || In.KeyPressed(Keys.A))
        {
            App.SwitchTo(new InfoScene(App, (_index + GameCatalog.Count - 1) % GameCatalog.Count));
            return;
        }
        if (ui.Button(new RectF(158, 316, 40, 34), ">", Keys.Right, true, Pal.Panel) || In.KeyPressed(Keys.D))
        {
            App.SwitchTo(new InfoScene(App, (_index + 1) % GameCatalog.Count));
            return;
        }
        if (ui.Button(new RectF(470, 312, 154, 40), "PLAY", Keys.Enter, true, Pal.Darken(Pal.Green, 0.35f), false, 2.5f) ||
            In.KeyPressed(Keys.Space))
        {
            App.Sound.Play(Sfx.Start);
            App.Play(_index);
            return;
        }

        // Scroll the instructions.
        float max = MathF.Max(0, _contentHeight - (TextBottom - TextTop));
        if (In.Wheel != 0)
            _scroll -= In.Wheel * 40;
        if (In.Down || In.KeyDown(Keys.PageDown))
            _scroll += 4;
        if (In.Up || In.KeyDown(Keys.PageUp))
            _scroll -= 4;
        if (In.PointerPressed && In.Pointer.X > TextX - 8 && In.Pointer.Y < TextBottom)
        {
            _dragging = true;
            _dragStart = _scroll;
        }
        if (_dragging && In.PointerDown)
            _scroll = _dragStart - (In.Pointer.Y - In.PointerStart.Y);
        if (In.PointerReleased)
            _dragging = false;
        _scroll = MathF2.Clamp(_scroll, 0, max);
    }

    public override void Draw(Gfx g)
    {
        Brand.Backdrop(g, Seconds);
        var accent = _game.Accent;

        // Left: picture and title.
        var pic = new RectF(16, 20, 288, 162);
        g.Glow(pic.CenterX, pic.CenterY, 220, accent, 0.25f);
        g.Panel(pic.Inflate(4, 4), Pal.Panel, accent, 10);
        g.SetClip(pic);
        g.Rect(pic, Color.Black);
        try
        {
            _game.DrawIcon(g, pic, Seconds);
        }
        catch (Exception e)
        {
            Platform.Diagnostics.Report(e, _game.Title + " icon");
        }
        g.Offset = Vector2.Zero;
        g.SetClip(null);

        g.RoundRect(22, 26, 30, 18, 4, Color.Black * 0.7f);
        g.Text(_game.Number.ToString("D2"), 37, 31, 1.25f, accent, Align.Center);
        g.TextShadow(_game.Title.ToUpperInvariant(), 160, 196, 2.5f, Color.White, Align.Center);
        float ty = 222 + g.TextWrapped(_game.Tagline, 20, 222, 280, 1.25f, Pal.Cyan, 1.35f, Align.Center);
        int best = App.Profile.Best(_game.Number);
        int plays = App.Profile.Plays(_game.Number);
        g.Text(Brand.CategoryName(_game.Category), 160, ty + 8, 1.25f, accent, Align.Center);
        g.Text(best > 0 ? $"BEST SCORE {best}" : "NOT PLAYED YET", 160, ty + 26, 1.5f, best > 0 ? Pal.Yellow : Pal.Grey, Align.Center);
        if (plays > 0)
            g.Text($"PLAYED {plays} TIME{(plays == 1 ? "" : "S")}", 160, ty + 44, 1f, Pal.LightGrey, Align.Center);

        // Right: instructions.
        var panel = new RectF(TextX - 10, TextTop - 8, TextW + 18, TextBottom - TextTop + 16);
        g.Panel(panel, Pal.Panel * 0.92f, Pal.Darken(accent, 0.4f), 10);
        g.SetClip(new RectF(TextX - 4, TextTop, TextW + 8, TextBottom - TextTop));
        float y = TextTop + 4 - _scroll;
        y += Heading(g, "HOW TO PLAY", y, accent);
        foreach (var para in _game.HowToPlay)
            y += g.TextWrapped(para, TextX, y, TextW, BodyScale, Color.White) + 7;
        y += 4;
        y += Heading(g, In.IsTouch ? "CONTROLS (TOUCH)" : "CONTROLS", y, accent);
        foreach (var line in In.IsTouch ? _game.TouchControls : _game.DesktopControls)
            y += g.TextWrapped(line, TextX, y, TextW, BodyScale, Pal.Yellow) + 4;
        _contentHeight = y + _scroll - TextTop;
        g.SetClip(null);
        if (_contentHeight > TextBottom - TextTop)
        {
            float max = _contentHeight - (TextBottom - TextTop);
            float trackH = TextBottom - TextTop;
            float barH = MathF.Max(20, trackH * trackH / _contentHeight);
            g.RoundRect(TextX + TextW + 2, TextTop + (trackH - barH) * (_scroll / max), 3, barH, 1.5f, Color.White * 0.4f);
            if (_scroll < max - 1)
                g.Text("MORE...", TextX + TextW, TextBottom + 1, 1f, Pal.Grey, Align.Right);
        }

        App.Ui.Draw(g);
    }

    private static float Heading(Gfx g, string text, float y, Color accent)
    {
        g.Text(text, TextX, y, 2f, accent);
        g.Rect(TextX, y + 18, TextW, 1, accent * 0.5f);
        return 26;
    }
}
