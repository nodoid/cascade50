using System;
using Microsoft.Xna.Framework;
using Cascade50.Core.Graphics;

namespace Cascade50.Core.Scenes;

/// <summary>The Cascade 50 look: logo, cassette artwork and the app backdrop.</summary>
public static class Brand
{
    /// <summary>The app backdrop: a night sky over a scrolling neon grid, covering the whole display.</summary>
    public static void Backdrop(Gfx g, float time, RectF? area = null)
    {
        var r = area ?? g.Visible;
        g.GradientV(r.X, r.Y, r.W, r.H * 0.62f, new Color(6, 4, 26), new Color(40, 10, 60));
        float horizon = r.Y + r.H * 0.62f;
        g.GradientV(r.X, horizon, r.W, r.Bottom - horizon, new Color(30, 6, 48), new Color(8, 2, 18));
        Backdrops.Stars(g, time, 4, 11, new RectF(r.X, r.Y, r.W, horizon - r.Y - 10), 90);

        // Sun.
        float sx = r.CenterX, sy = horizon - 4;
        g.Glow(sx, sy, 150, Pal.Magenta, 0.35f);
        for (int i = 0; i < 9; i++)
        {
            float y0 = sy - 56 + i * 7;
            float half = MathF.Sqrt(MathF.Max(0, 56 * 56 - (y0 - sy) * (y0 - sy)));
            var c = Pal.Lerp(Pal.Yellow, Pal.Magenta, i / 8f);
            if (y0 < sy)
                g.Rect(sx - half, y0, half * 2, 5 - i * 0.4f, c * 0.9f);
        }

        // Perspective grid.
        var line = Pal.Add(Pal.Magenta, 0.55f);
        g.Rect(r.X, horizon, r.W, 1.5f, Pal.Add(Pal.Pink, 0.9f));
        float scroll = (time * 0.6f) % 1f;
        for (int i = 0; i < 14; i++)
        {
            float z = (i + 1 - scroll) / 14f;
            float y = horizon + (r.Bottom - horizon) * z * z;
            g.Rect(r.X, y, r.W, 1 + z, line * z);
        }
        for (int i = -16; i <= 16; i++)
        {
            float x0 = sx + i * 14;
            float x1 = sx + i * 110;
            g.Line(x0, horizon, x1, r.Bottom + 40, 1.2f, line * 0.7f);
        }
    }

    /// <summary>Margin decoration around the playfield while a game is running.</summary>
    public static void Bezel(Gfx g, float time, Color accent)
    {
        var v = g.Visible;
        g.GradientV(v.X, v.Y, v.W, v.H, new Color(10, 10, 26), new Color(4, 4, 12));
        // Rainbow pin stripes framing the playfield, as on the cassette inlay.
        for (int i = 0; i < Pal.Rainbow.Length; i++)
        {
            var c = Pal.Rainbow[i] * 0.55f;
            float d = 3 + i * 3;
            if (v.X < -d)
            {
                g.Rect(-d, 0, 1.5f, Screen.Height, c);
                g.Rect(Screen.Width + d - 1.5f, 0, 1.5f, Screen.Height, c);
            }
            if (v.Y < -d)
            {
                g.Rect(0, -d, Screen.Width, 1.5f, c);
                g.Rect(0, Screen.Height + d - 1.5f, Screen.Width, 1.5f, c);
            }
        }
        g.Glow(Screen.Width / 2f, Screen.Height / 2f, 520, accent, 0.05f);
    }

    /// <summary>The "CASCADE" word mark in rainbow Oric letters with "50" beside it.</summary>
    public static void Logo(Gfx g, float cx, float cy, float scale, float time, bool wave = true)
    {
        const string word = "CASCADE";
        float gw = Gfx.GlyphW * scale;
        float badge = scale * 9.5f;
        float total = word.Length * gw + badge * 1.1f + scale * 3;
        float x = cx - total / 2;
        float y = cy - Gfx.GlyphH * scale / 2;
        for (int i = 0; i < word.Length; i++)
        {
            float dy = wave ? MathF.Sin(time * 3 + i * 0.6f) * scale * 0.6f : 0;
            var c = Pal.Rainbow[i % Pal.Rainbow.Length];
            string ch = word[i].ToString();
            g.Text(ch, x + i * gw + scale * 0.8f, y + dy + scale * 0.8f, scale, Color.Black * 0.7f);
            g.Glow(x + i * gw + gw / 2, y + dy + Gfx.GlyphH * scale / 2, gw * 1.1f, c, 0.25f);
            g.Text(ch, x + i * gw, y + dy, scale, c);
            // Bright top half, like a chrome highlight.
            g.Rect(x + i * gw, y + dy + scale * 1, gw * 0.9f, scale * 0.5f, Color.White * 0.12f);
        }
        float bx = x + word.Length * gw + scale * 3 + badge * 0.55f;
        g.Circle(bx + scale * 0.4f, cy + scale * 0.6f, badge * 0.62f, Color.Black * 0.6f);
        g.Glow(bx, cy, badge * 1.2f, Pal.Gold, 0.4f + 0.15f * MathF.Sin(time * 4));
        g.Circle(bx, cy, badge * 0.62f, Pal.Red);
        g.Ring(bx, cy, badge * 0.62f, scale * 0.6f, Pal.Gold);
        g.Text("50", bx + scale * 0.4f, cy - Gfx.GlyphH * scale * 0.8f / 2, scale * 0.8f, Pal.White, Align.Center);
    }

    /// <summary>A compact cassette tape with spinning reels and an Oric rainbow label.</summary>
    public static void Cassette(Gfx g, float cx, float cy, float width, float time, float reelSpeed = 1f, bool label = true)
    {
        float w = width, h = width * 0.63f;
        float x = cx - w / 2, y = cy - h / 2;
        float r = w * 0.05f;
        g.RoundRect(x + w * 0.015f, y + w * 0.02f, w, h, r, Color.Black * 0.5f);
        g.RoundRect(x, y, w, h, r, new Color(28, 28, 36));
        g.RoundRect(x + w * 0.01f, y + w * 0.01f, w * 0.98f, h * 0.5f, r, new Color(44, 44, 56));

        // Label with rainbow stripes.
        float lx = x + w * 0.07f, ly = y + h * 0.08f, lw = w * 0.86f, lh = h * 0.62f;
        g.RoundRect(lx, ly, lw, lh, r * 0.6f, new Color(245, 240, 225));
        float stripeH = lh * 0.075f;
        for (int i = 0; i < Pal.Rainbow.Length; i++)
            g.Rect(lx, ly + lh * 0.46f + i * stripeH, lw, stripeH + 0.3f, Pal.Rainbow[i]);
        if (label)
        {
            float ts = w / 120f;
            g.TextFit("CASCADE 50", cx, ly + lh * 0.1f, lw * 0.9f, ts * 1.6f, new Color(30, 30, 60), Align.Center);
        }

        // Reel window.
        float wy = ly + lh * 0.46f + stripeH * 3;
        float ww = lw * 0.62f, wh = lh * 0.36f;
        g.RoundRect(cx - ww / 2, wy - wh / 2, ww, wh, wh / 2, new Color(20, 20, 26));
        g.RoundRect(cx - ww / 2 + 2, wy - wh / 2 + 2, ww - 4, wh - 4, wh / 2 - 2, new Color(70, 50, 40) * 0.9f);
        float rr = wh * 0.42f;
        for (int side = -1; side <= 1; side += 2)
        {
            float rx = cx + side * ww * 0.3f;
            g.Circle(rx, wy, rr, new Color(235, 235, 240));
            g.Circle(rx, wy, rr * 0.45f, new Color(40, 40, 48));
            float a = time * 4 * reelSpeed * side;
            for (int k = 0; k < 6; k++)
            {
                var d = MathF2.FromAngle(a + k * MathF.PI / 3, rr * 0.42f);
                g.Line(rx, wy, rx + d.X, wy + d.Y, rr * 0.12f, new Color(235, 235, 240));
            }
        }
        g.Rect(cx - ww * 0.3f, wy - rr * 0.95f, ww * 0.6f, 1.2f, new Color(80, 50, 30));

        // Bottom trapezoid with screw holes.
        float by = y + h * 0.78f;
        g.Polygon([new Vector2(x + w * 0.2f, by), new Vector2(x + w * 0.8f, by), new Vector2(x + w * 0.86f, y + h), new Vector2(x + w * 0.14f, y + h)],
            new Color(52, 52, 64));
        for (int i = 0; i < 4; i++)
            g.Circle(x + w * (0.3f + i * 0.133f), by + h * 0.12f, w * 0.018f, new Color(14, 14, 18));
        g.Circle(x + w * 0.05f, y + h * 0.08f, w * 0.015f, new Color(90, 90, 100));
        g.Circle(x + w * 0.95f, y + h * 0.08f, w * 0.015f, new Color(90, 90, 100));
        g.Circle(x + w * 0.05f, y + h * 0.92f, w * 0.015f, new Color(90, 90, 100));
        g.Circle(x + w * 0.95f, y + h * 0.92f, w * 0.015f, new Color(90, 90, 100));
    }

    public static string CategoryName(Games.Category c) => c switch
    {
        Games.Category.Arcade => "ARCADE",
        Games.Category.Shooter => "SHOOTERS",
        Games.Category.Skill => "SKILL",
        Games.Category.Puzzle => "PUZZLE",
        _ => "BRAIN",
    };
}
