using System;
using Microsoft.Xna.Framework;

namespace Cascade50.Core.Graphics;

/// <summary>
/// The Cascade 50 palette: the Oric's eight pure colours, plus softer shades for modern graphics.
/// Colours are premultiplied: fade one with <c>colour * 0.5f</c>, and use <see cref="Add"/> for glows.
/// Never build a translucent colour with <c>new Color(r, g, b, a)</c>.
/// </summary>
public static class Pal
{
    // The Oric eight.
    public static readonly Color Black = new(0, 0, 0);
    public static readonly Color Red = new(255, 40, 40);
    public static readonly Color Green = new(40, 230, 60);
    public static readonly Color Yellow = new(255, 230, 40);
    public static readonly Color Blue = new(40, 90, 255);
    public static readonly Color Magenta = new(240, 50, 230);
    public static readonly Color Cyan = new(40, 230, 240);
    public static readonly Color White = new(255, 255, 255);

    // Extended shades.
    public static readonly Color Orange = new(255, 140, 20);
    public static readonly Color Pink = new(255, 120, 180);
    public static readonly Color Purple = new(130, 60, 220);
    public static readonly Color Lime = new(170, 255, 60);
    public static readonly Color Teal = new(20, 150, 150);
    public static readonly Color Sky = new(110, 180, 255);
    public static readonly Color Navy = new(14, 20, 60);
    public static readonly Color Night = new(6, 8, 24);
    public static readonly Color Brown = new(130, 80, 40);
    public static readonly Color Sand = new(230, 200, 130);
    public static readonly Color Grass = new(40, 150, 50);
    public static readonly Color Forest = new(20, 80, 35);
    public static readonly Color Grey = new(128, 128, 140);
    public static readonly Color LightGrey = new(200, 200, 210);
    public static readonly Color DarkGrey = new(50, 52, 64);
    public static readonly Color Gold = new(255, 200, 60);
    public static readonly Color Silver = new(190, 200, 215);
    public static readonly Color Ice = new(200, 240, 255);
    public static readonly Color Water = new(30, 110, 200);
    public static readonly Color DeepWater = new(10, 40, 110);
    public static readonly Color Skin = new(240, 190, 150);

    // Interface colours.
    public static readonly Color Panel = new(18, 22, 48);
    public static readonly Color PanelLight = new(34, 42, 90);
    public static readonly Color Accent = new(255, 200, 40);

    /// <summary>The Oric eight by attribute number 0..7.</summary>
    public static readonly Color[] Oric = [Black, Red, Green, Yellow, Blue, Magenta, Cyan, White];

    /// <summary>The six bright Oric colours, for rainbows.</summary>
    public static readonly Color[] Rainbow = [Red, Yellow, Green, Cyan, Blue, Magenta];

    /// <summary>Additive version of a colour: brightens whatever is underneath (glows, lasers, sparks).</summary>
    public static Color Add(Color c, float intensity = 1f) =>
        new((byte)Math.Clamp(c.R * intensity, 0, 255), (byte)Math.Clamp(c.G * intensity, 0, 255),
            (byte)Math.Clamp(c.B * intensity, 0, 255), (byte)0);

    public static Color Lerp(Color a, Color b, float t) => Color.Lerp(a, b, MathF2.Clamp(t, 0, 1));

    public static Color Darken(Color c, float amount) => Lerp(c, Black, amount);
    public static Color Lighten(Color c, float amount) => Lerp(c, White, amount);

    /// <summary>Hue 0..360, saturation and value 0..1.</summary>
    public static Color Hsv(float h, float s, float v)
    {
        h = ((h % 360) + 360) % 360 / 60f;
        int i = (int)h;
        float f = h - i, p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
        var (r, g, b) = i switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };
        return new Color(r, g, b);
    }

    /// <summary>Cycles through the rainbow over time.</summary>
    public static Color Cycle(float time, float speed = 1f) => Hsv(time * 120 * speed, 0.85f, 1f);
}
