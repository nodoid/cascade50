using System;
using Microsoft.Xna.Framework;

namespace Cascade50.Core.Graphics;

/// <summary>
/// The virtual screen every scene draws on. Games always get the 640x360 playfield; on wider or
/// taller displays the extra margin is filled by the backdrop (and holds the touch controls).
/// </summary>
public static class Screen
{
    public const int Width = 640;
    public const int Height = 360;

    /// <summary>Height of the score bar drawn over the top of the playfield while a game runs.</summary>
    public const int HudHeight = 22;

    public static readonly RectF Bounds = new(0, 0, Width, Height);

    /// <summary>The part of the playfield below the score bar.</summary>
    public static readonly RectF Play = new(0, HudHeight, Width, Height - HudHeight);
}

/// <summary>Floating point rectangle in virtual pixels.</summary>
public readonly struct RectF
{
    public readonly float X, Y, W, H;

    public RectF(float x, float y, float w, float h)
    {
        X = x;
        Y = y;
        W = w;
        H = h;
    }

    public float Left => X;
    public float Top => Y;
    public float Right => X + W;
    public float Bottom => Y + H;
    public Vector2 Center => new(X + W / 2, Y + H / 2);
    public float CenterX => X + W / 2;
    public float CenterY => Y + H / 2;

    public bool Contains(Vector2 p) => p.X >= X && p.Y >= Y && p.X < X + W && p.Y < Y + H;
    public bool Contains(float px, float py) => px >= X && py >= Y && px < X + W && py < Y + H;

    public bool Intersects(RectF o) => X < o.X + o.W && o.X < X + W && Y < o.Y + o.H && o.Y < Y + H;

    public RectF Inflate(float dx, float dy) => new(X - dx, Y - dy, W + 2 * dx, H + 2 * dy);
    public RectF Offset(float dx, float dy) => new(X + dx, Y + dy, W, H);

    public static RectF Centered(float cx, float cy, float w, float h) => new(cx - w / 2, cy - h / 2, w, h);

    public override string ToString() => $"({X},{Y} {W}x{H})";
}

/// <summary>Small maths helpers shared by the games.</summary>
public static class MathF2
{
    public const float Tau = MathF.PI * 2;

    public static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    public static float Lerp(float a, float b, float t) => a + (b - a) * t;
    public static float Approach(float v, float target, float step) =>
        v < target ? MathF.Min(v + step, target) : MathF.Max(v - step, target);

    public static Vector2 FromAngle(float radians, float length = 1f) =>
        new(MathF.Cos(radians) * length, MathF.Sin(radians) * length);

    public static float Angle(Vector2 v) => MathF.Atan2(v.Y, v.X);

    /// <summary>Wraps an angle into -PI..PI.</summary>
    public static float WrapAngle(float a)
    {
        while (a > MathF.PI) a -= Tau;
        while (a < -MathF.PI) a += Tau;
        return a;
    }

    public static float Dist(Vector2 a, Vector2 b) => Vector2.Distance(a, b);

    public static bool Circles(Vector2 a, float ra, Vector2 b, float rb) =>
        Vector2.DistanceSquared(a, b) < (ra + rb) * (ra + rb);

    /// <summary>Smooth 0..1..0 pulse with the given period in seconds.</summary>
    public static float Pulse(float time, float period = 1f) => 0.5f - 0.5f * MathF.Cos(time * Tau / period);

    public static float EaseOut(float t) => 1 - (1 - t) * (1 - t);
    public static float EaseInOut(float t) => t < 0.5f ? 2 * t * t : 1 - MathF.Pow(-2 * t + 2, 2) / 2;
}
