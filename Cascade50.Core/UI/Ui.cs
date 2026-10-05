using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.UI;

/// <summary>
/// Immediate-mode buttons. Call <see cref="Button"/> from Update every tick the button should exist;
/// it returns true on the tick it is clicked / tapped (or its key is pressed). The framework draws
/// all of this tick's buttons on top of the scene. A press that starts on a button is hidden from
/// the game's own pointer handling.
/// </summary>
public sealed class Ui
{
    private sealed class Widget
    {
        public string Id;
        public RectF Rect;
        public string Label;
        public Keys Key;
        public bool Enabled;
        public bool Selected;
        public Color Color;
        public float Scale;
    }

    private List<Widget> _widgets = new();
    private List<Widget> _last = new();
    private string _captured;
    private string _activated;
    private Controls _in;
    private readonly ISound _sound;

    public Ui(ISound sound)
    {
        _sound = sound;
    }

    /// <summary>True while a press that started on a button is held.</summary>
    public bool Capturing => _captured != null;

    internal void BeginFrame(Controls input)
    {
        _in = input;
        (_last, _widgets) = (_widgets, _last);
        _widgets.Clear();
        _activated = null;

        if (input.PointerPressed)
        {
            var hit = Find(input.Pointer);
            if (hit != null)
            {
                _captured = hit.Id;
                input.PointerPressed = false;
            }
        }
        if (_captured != null)
        {
            if (input.PointerReleased)
            {
                var hit = Find(input.Pointer);
                if (hit != null && hit.Id == _captured)
                    _activated = _captured;
                _captured = null;
                input.PointerReleased = false;
            }
            else if (!input.PointerDown)
            {
                _captured = null;
            }
            input.PointerDown = false;
        }
    }

    private Widget Find(Vector2 p)
    {
        for (int i = _last.Count - 1; i >= 0; i--)
            if (_last[i].Enabled && _last[i].Rect.Contains(p))
                return _last[i];
        return null;
    }

    /// <summary>A button. <paramref name="key"/> is its keyboard shortcut (shown on desktop).</summary>
    public bool Button(RectF rect, string label, Keys key = Keys.None, bool enabled = true, Color? color = null,
        bool selected = false, float textScale = 1.5f, string id = null)
    {
        var w = new Widget
        {
            Id = id ?? label + "@" + (int)rect.X + "," + (int)rect.Y,
            Rect = rect,
            Label = label,
            Key = key,
            Enabled = enabled,
            Selected = selected,
            Color = color ?? Pal.PanelLight,
            Scale = textScale,
        };
        _widgets.Add(w);
        if (!enabled)
            return false;
        bool clicked = _activated == w.Id || (key != Keys.None && _in != null && _in.KeyPressed(key));
        if (clicked)
            _sound?.Play(Sfx.Click);
        return clicked;
    }

    public void Draw(Gfx g)
    {
        foreach (var w in _widgets)
        {
            bool hover = w.Enabled && _in != null && _in.HasHover && !_in.IsTouch && w.Rect.Contains(_in.Pointer);
            bool down = w.Id == _captured && _in != null && w.Rect.Contains(_in.Pointer);
            var fill = w.Color;
            if (!w.Enabled)
                fill = Pal.Darken(fill, 0.55f);
            else if (down)
                fill = Pal.Darken(fill, 0.25f);
            else if (hover || w.Selected)
                fill = Pal.Lighten(fill, 0.2f);
            var border = w.Selected ? Pal.Accent : hover ? Color.White : Pal.Lighten(w.Color, 0.35f);
            var r = down ? w.Rect.Offset(0, 1) : w.Rect;
            g.RoundRect(r.Offset(0, 2), 7, Color.Black * 0.4f);
            g.Panel(r, fill, w.Enabled ? border : Pal.DarkGrey, 7);
            var text = w.Enabled ? Color.White : Pal.Grey;
            float th = Gfx.GlyphH * w.Scale;
            g.TextFit(w.Label, r.CenterX, r.CenterY - th / 2 + 0.5f, r.W - 10, w.Scale, text, Align.Center);
            if (w.Key != Keys.None && _in != null && !_in.IsTouch && w.Enabled)
            {
                string k = KeyName(w.Key);
                float labelW = MathF.Min(Gfx.TextWidth(w.Label, w.Scale), r.W - 10);
                bool room = (r.W - labelW) / 2 >= Gfx.TextWidth(k, 1f) + 7 || r.H >= th + 24;
                if (k.Length > 0 && room && !string.Equals(k, w.Label, StringComparison.OrdinalIgnoreCase))
                    g.Text(k, r.X + 5, r.Y + 4, 1f, Pal.Accent * 0.9f);
            }
        }
    }

    public static string KeyName(Keys k) => k switch
    {
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (k - Keys.D0))).ToString(),
        >= Keys.A and <= Keys.Z => k.ToString(),
        Keys.Space => "SPC",
        Keys.Enter => "ENT",
        Keys.Escape => "ESC",
        Keys.Back => "DEL",
        Keys.Left => "<",
        Keys.Right => ">",
        Keys.Up => "^",
        Keys.Down => "v",
        Keys.Tab => "TAB",
        _ => "",
    };
}
