using System;
using Microsoft.Xna.Framework;

namespace Cascade50.Core.Graphics;

/// <summary>Ready-made backgrounds the games share.</summary>
public static class Backdrops
{
    /// <summary>Deep-space gradient with twinkling stars drifting left at <paramref name="speed"/> px/s.</summary>
    public static void Space(Gfx g, float time, float speed = 0, int seed = 7, RectF? area = null)
    {
        var r = area ?? g.Visible;
        g.GradientV(r.X, r.Y, r.W, r.H, new Color(4, 4, 22), new Color(16, 6, 40));
        Stars(g, time, speed, seed, r);
    }

    public static void Stars(Gfx g, float time, float speed, int seed, RectF r, int count = 140)
    {
        var rng = new Random(seed);
        for (int i = 0; i < count; i++)
        {
            float depth = 0.2f + 0.8f * (float)rng.NextDouble();
            float x = (float)rng.NextDouble() * r.W;
            float y = r.Y + (float)rng.NextDouble() * r.H;
            x = r.X + Mod(x - time * speed * depth, r.W);
            float tw = 0.55f + 0.45f * MathF.Sin(time * (1 + 3 * (float)rng.NextDouble()) + i);
            var c = i % 7 == 0 ? Pal.Sky : i % 11 == 0 ? Pal.Pink : Pal.White;
            float size = 0.5f + depth * 1.1f;
            g.Circle(x, y, size, c * (tw * depth));
            if (depth > 0.85f)
                g.Glow(x, y, size * 4, c, 0.25f * tw);
        }
    }

    /// <summary>Stars falling downwards (vertical shooters).</summary>
    public static void StarsDown(Gfx g, float time, float speed, int seed, RectF r, int count = 120)
    {
        var rng = new Random(seed);
        g.GradientV(r.X, r.Y, r.W, r.H, new Color(2, 4, 20), new Color(10, 4, 34));
        for (int i = 0; i < count; i++)
        {
            float depth = 0.2f + 0.8f * (float)rng.NextDouble();
            float x = r.X + (float)rng.NextDouble() * r.W;
            float y = r.Y + Mod((float)rng.NextDouble() * r.H + time * speed * depth, r.H);
            g.Rect(x, y, 1 + depth, 1 + depth * (speed > 100 ? 4 : 1), Pal.White * depth);
        }
    }

    /// <summary>A sky gradient with optional sun/moon glow.</summary>
    public static void Sky(Gfx g, Color top, Color bottom, RectF? area = null)
    {
        var r = area ?? g.Visible;
        g.GradientV(r.X, r.Y, r.W, r.H, top, bottom);
    }

    /// <summary>Rolling silhouette hills along the bottom of the area.</summary>
    public static void Hills(Gfx g, float baseY, float amplitude, float scroll, Color color, int seed = 3, RectF? area = null)
    {
        var r = area ?? g.Visible;
        var rng = new Random(seed);
        float f1 = 0.008f + 0.01f * (float)rng.NextDouble(), f2 = 0.02f + 0.02f * (float)rng.NextDouble();
        float p1 = (float)rng.NextDouble() * 10, p2 = (float)rng.NextDouble() * 10;
        const float step = 8;
        for (float x = r.X; x < r.Right; x += step)
        {
            float wx = x + scroll;
            float h = amplitude * (0.6f * MathF.Sin(wx * f1 + p1) + 0.4f * MathF.Sin(wx * f2 + p2));
            float wx2 = x + step + scroll;
            float h2 = amplitude * (0.6f * MathF.Sin(wx2 * f1 + p1) + 0.4f * MathF.Sin(wx2 * f2 + p2));
            float top = baseY - MathF.Max(h, h2);
            g.Rect(x, top, step + 0.5f, r.Bottom - top, color);
            // Smooth the top edge.
            g.Triangle(new Vector2(x, baseY - h), new Vector2(x + step, baseY - h2), new Vector2(h > h2 ? x + step : x, top), color);
        }
    }

    /// <summary>A faint grid (retro vector look).</summary>
    public static void Grid(Gfx g, RectF r, float spacing, Color color, float scrollY = 0)
    {
        for (float x = r.X; x <= r.Right; x += spacing)
            g.Rect(x, r.Y, 1, r.H, color);
        for (float y = r.Y + Mod(scrollY, spacing); y <= r.Bottom; y += spacing)
            g.Rect(r.X, y, r.W, 1, color);
    }

    /// <summary>Darkens the edges of an area.</summary>
    public static void Vignette(Gfx g, RectF r, float strength = 0.5f)
    {
        var dark = Color.Black * strength;
        g.GradientV(r.X, r.Y, r.W, 40, dark, Color.Transparent);
        g.GradientV(r.X, r.Bottom - 40, r.W, 40, Color.Transparent, dark);
        g.GradientH(r.X, r.Y, 50, r.H, dark, Color.Transparent);
        g.GradientH(r.Right - 50, r.Y, 50, r.H, Color.Transparent, dark);
    }

    public static float Mod(float a, float m) => ((a % m) + m) % m;
}
