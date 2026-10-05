using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 08 Dynamite: a mad miner scurries along a beam at the top of the shaft, dropping lit sticks of
/// dynamite. Catch them in your stack of water buckets before they hit the ground.
/// </summary>
public sealed class Dynamite : MiniGame, Capture.ICaptureHints
{
    public override int Number => 8;
    public override string Title => "Dynamite";
    public override Category Category => Category.Arcade;
    public override string Tagline => "Catch the mad miner's dynamite in your water buckets. Don't miss!";
    public override Color Accent => Pal.Red;

    public override string[] HowToPlay =>
    [
        "The mad miner drops lit dynamite. Catch every stick in your buckets to douse it.",
        "Miss one and it all blows up, costing a bucket. Lose all three and it's over.",
        "Catches score more each wave. Extra bucket every 2000.",
    ];

    public override string[] DesktopControls => ["LEFT / RIGHT or the mouse to move.", "P or ESC to pause."];
    public override string[] TouchControls => ["Drag anywhere, or use the stick."];
    public override Pad Pad => Pad.Horizontal;
    public int CaptureTicks => 1500;

    private const float BeamY = 84, GroundY = 346, BucketTop = 288, BucketGap = 20, BucketHalfW = 23;
    private const float WallL = 22, WallR = 618;

    private static readonly Dictionary<char, Color> Colours = new()
    {
        ['y'] = Pal.Yellow, ['w'] = Pal.White, ['s'] = Pal.Skin, ['K'] = new Color(30, 20, 20), ['b'] = new Color(150, 140, 130),
        ['R'] = new Color(150, 30, 40), ['r'] = new Color(200, 40, 40), ['d'] = new Color(130, 20, 30), ['B'] = new Color(50, 70, 150),
        ['k'] = new Color(60, 40, 25),
    };

    private static readonly PixelArt[] MinerArt =
    [
        new([
            "...yyyyyy...", "..yyyywwyy..", ".yyyyyyyyyy.", "...ssssss...", "...sKssKs...", "...ssssss...", "..bbsRRsbb..",
            "..bbbbbbbb..", "...bbbbbb...", ".rrdrrdrrdr.", "srrdrrdrrdrs", "s.rrdrrdrr.s", "..BBBBBBBB..", "..BBB..BBB..",
            "..BBB..BBB..", ".kkkk..kkkk.",
        ], Colours),
        new([
            "...yyyyyy...", "..yyyywwyy..", ".yyyyyyyyyy.", "...ssssss...", "...sKssKs...", "...ssssss...", "..bbsRRsbb..",
            "..bbbbbbbb..", "...bbbbbb...", ".rrdrrdrrdr.", "srrdrrdrrdrs", "s.rrdrrdrr.s", "..BBBBBBBB..", "...BBBBBB...",
            "...BB..BB...", "..kkk..kkk..",
        ], Colours),
    ];

    private struct Stick
    {
        public Vector2 Pos;
        public float Swing;
    }

    private readonly List<Stick> _sticks = new();
    private float _bucketX = 320;
    private float _minerX = 320, _minerTarget = 320, _minerAnim;
    private float _dropTimer;
    private int _toDrop, _wave;
    private float _pause, _banner;
    private float _armUp;
    private int _nextBonus = 2000;
    private bool _gloat;
    private Vector2 _lastPointer;

    protected override void Start()
    {
        Lives = 3;
        _wave = 0;
        NextWave();
    }

    private void NextWave()
    {
        _wave++;
        Level = _wave;
        _toDrop = 10 + _wave * 5;
        _dropTimer = 1.5f;
        _banner = 2f;
        _gloat = false;
    }

    private float FallSpeed => MathF.Min(420, 95 + _wave * 22);
    private float MinerSpeed => MathF.Min(520, 90 + _wave * 35);
    private float DropInterval => MathF.Max(0.12f, 0.95f - _wave * 0.075f);
    private int CatchPoints => 10 * Math.Min(_wave, 8);

    protected override void Update()
    {
        if (_banner > 0)
            _banner -= Dt;
        _armUp = MathF.Max(0, _armUp - Dt);
        Status = $"WAVE {_wave}  STICKS {_toDrop + _sticks.Count}";

        MoveBuckets();

        if (_pause > 0)
        {
            _pause -= Dt;
            return;
        }

        MoveMiner();
        UpdateSticks();

        if (_toDrop == 0 && _sticks.Count == 0)
        {
            AddScore(100 * _wave, 320, 200, Pal.Cyan);
            Sound.Play(Sfx.LevelUp);
            NextWave();
        }
    }

    private void MoveBuckets()
    {
        float speed = MathF.Min(400, 330 + _wave * 8);
        float target = _bucketX;
        if (MathF.Abs(In.AxisX) > 0.1f)
            target = _bucketX + In.AxisX * speed * Dt;
        // Finger drag, or the desktop mouse when it moves.
        bool mouse = In.HasHover && !IsTouch && In.PointerMoved && Vector2.DistanceSquared(In.Pointer, _lastPointer) > 0.5f;
        if (In.PointerDown || mouse)
            target = MathF2.Approach(_bucketX, In.Pointer.X, speed * 2f * Dt);
        _lastPointer = In.Pointer;
        _bucketX = MathF2.Clamp(target, WallL + BucketHalfW, WallR - BucketHalfW);
    }

    private void MoveMiner()
    {
        float dx = _minerTarget - _minerX;
        float step = MinerSpeed * Dt;
        if (MathF.Abs(dx) <= step)
        {
            _minerX = _minerTarget;
            _minerTarget = Rand(WallL + 30, WallR - 30);
        }
        else
        {
            _minerX += MathF.Sign(dx) * step;
            _minerAnim += Dt * 10;
        }

        if (_toDrop > 0)
        {
            _dropTimer -= Dt;
            if (_dropTimer <= 0)
            {
                _dropTimer = DropInterval * Rand(0.75f, 1.25f);
                _sticks.Add(new Stick { Pos = new Vector2(_minerX, BeamY - 6), Swing = Rand(0, 6) });
                _toDrop--;
                _armUp = 0.2f;
                Sound.Play(Sfx.Fuse, Rand(-0.2f, 0.3f), 0.35f);
                if (Chance(0.25f + _wave * 0.03f))
                    _minerTarget = Rand(WallL + 30, WallR - 30);
            }
        }
    }

    private void UpdateSticks()
    {
        int buckets = Math.Max(1, Lives);
        for (int i = _sticks.Count - 1; i >= 0; i--)
        {
            var s = _sticks[i];
            float oldY = s.Pos.Y;
            s.Pos.Y += FallSpeed * Dt;
            s.Swing += Dt * 6;
            _sticks[i] = s;
            if (Tick % 3 == i % 3)
                Fx.Spark(s.Pos.X + 3, s.Pos.Y - 10, Rand(-40, 40), Rand(-60, -10), Chance(0.5f) ? Pal.Yellow : Pal.Orange, 0.25f, 1.4f);

            // Caught by any bucket?
            bool caught = false;
            for (int b = 0; b < buckets && !caught; b++)
            {
                float rim = BucketTop + b * BucketGap;
                if (oldY + 8 < rim && s.Pos.Y + 8 >= rim && MathF.Abs(s.Pos.X - _bucketX) < BucketHalfW + 3)
                    caught = true;
            }
            if (caught)
            {
                _sticks.RemoveAt(i);
                AddScore(CatchPoints, s.Pos.X, BucketTop - 16, Pal.Yellow);
                Fx.Burst(s.Pos.X, BucketTop, Pal.Sky, 12, 90, 0.4f, 2f, 300, false);
                Fx.Burst(s.Pos.X, BucketTop, Pal.White, 6, 60, 0.3f, 1.5f, 200, false);
                Sound.Play(Sfx.Splash, Rand(-0.2f, 0.4f), 0.6f);
                if (Score >= _nextBonus)
                {
                    _nextBonus += 2000;
                    if (Lives < 3)
                    {
                        Lives++;
                        Fx.Float("EXTRA BUCKET!", _bucketX, BucketTop - 40, Pal.Cyan, 1.5f);
                        Sound.Play(Sfx.PowerUp);
                    }
                }
                continue;
            }

            if (s.Pos.Y >= GroundY - 4)
            {
                Kaboom(s.Pos);
                return;
            }
        }
    }

    private void Kaboom(Vector2 at)
    {
        // The missed stick goes off, and every other lit stick with it.
        Fx.Explode(at.X, at.Y, 2f);
        Sound.Play(Sfx.BigExplode);
        foreach (var s in _sticks)
            Fx.Explode(s.Pos.X, s.Pos.Y, 1f);
        _toDrop += _sticks.Count - 1;
        _sticks.Clear();
        _gloat = true;
        _pause = 1.6f;
        _dropTimer = 1f;
        Fx.Burst(_bucketX, BucketTop + 40, Pal.Sky, 16, 120, 0.6f, 2.5f, 300, false);
        LoseLife();
    }

    // ------------------------------------------------------------------ drawing

    public override void Draw(Gfx g)
    {
        var v = g.Visible;
        DrawShaft(g, Screen.Bounds, Time, 1f);

        // Beam.
        g.Rect(v.X, BeamY, v.W, 9, new Color(120, 80, 45));
        g.Rect(v.X, BeamY, v.W, 2, new Color(170, 120, 70));
        g.Rect(v.X, BeamY + 9, v.W, 2, Color.Black * 0.4f);
        for (float x = 60; x < 640; x += 130)
        {
            g.Rect(x - 4, BeamY + 9, 8, 30, new Color(100, 65, 35));
            g.Line(x - 4, BeamY + 11, x - 22, BeamY + 34, 4, new Color(100, 65, 35));
        }
        // Hanging lanterns.
        for (int i = 0; i < 3; i++)
        {
            float lx = 125 + i * 195, ly = BeamY + 30 + MathF.Sin(Time * 1.4f + i) * 1.5f;
            float flick = 0.85f + 0.15f * MathF.Sin(Time * 13 + i * 3);
            g.Line(lx, BeamY + 11, lx, ly - 6, 1, Pal.Grey);
            g.Glow(lx, ly, 70 * flick, Pal.Orange, 0.35f);
            g.RoundRect(lx - 4, ly - 6, 8, 11, 2, new Color(255, 200, 100));
            g.Rect(lx - 5, ly - 7, 10, 2, Pal.DarkGrey);
        }

        // Miner.
        float bob = MathF.Abs(MathF.Sin(_minerAnim)) * 1.5f;
        var art = MinerArt[(int)_minerAnim % 2];
        bool faceLeft = _minerTarget < _minerX;
        g.Glow(_minerX + (faceLeft ? -6 : 6), BeamY - 28, 26, Pal.Yellow, 0.4f);
        g.Pixels(art, _minerX - 12, BeamY - 32 - bob, 2f, faceLeft);
        if (_armUp > 0)
            DrawStick(g, new Vector2(_minerX + (faceLeft ? -14 : 14), BeamY - 30), 0, Time);
        if (_gloat && _pause > 0)
            g.TextShadow("HA HA!", _minerX, BeamY - 48, 1.5f, Pal.Yellow, Align.Center);

        // Sticks.
        foreach (var s in _sticks)
            DrawStick(g, s.Pos, MathF.Sin(s.Swing) * 0.3f, Time + s.Swing);

        // Buckets (top one catches first).
        int buckets = IsOver ? 0 : Math.Max(0, Lives);
        for (int b = buckets - 1; b >= 0; b--)
            DrawBucket(g, _bucketX, BucketTop + b * BucketGap, 1f, Time + b);

        if (_banner > 0)
        {
            float a = MathF.Min(1, _banner * 1.5f);
            g.TextShadow("WAVE " + _wave, 320, 160, 3f, Pal.Yellow * a, Align.Center);
            g.TextShadow($"{_toDrop} STICKS", 320, 192, 1.5f, Pal.White * a, Align.Center);
        }
    }

    private static readonly Vector3[] Rocks = MakeRocks();

    private static Vector3[] MakeRocks()
    {
        var rng = new Random(17);
        var a = new Vector3[48];
        for (int i = 0; i < a.Length; i++)
            a[i] = new Vector3((float)rng.NextDouble(), (float)rng.NextDouble(), 10 + (float)rng.NextDouble() * 30);
        return a;
    }

    private static readonly Vector2[] Quad = new Vector2[4];

    private static void DrawShaft(Gfx g, RectF r, float time, float s)
    {
        g.GradientV(r.X, r.Y, r.W, r.H, new Color(40, 26, 22), new Color(18, 12, 12));
        for (int i = 0; i < Rocks.Length; i++)
        {
            float x = r.X + Rocks[i].X * r.W, y = r.Y + Rocks[i].Y * r.H;
            float w = Rocks[i].Z * s;
            g.Ellipse(x, y, w, w * 0.45f, new Color(60, 42, 34) * 0.6f);
            g.Ellipse(x - w * 0.2f, y - w * 0.12f, w * 0.5f, w * 0.15f, new Color(90, 66, 52) * 0.35f);
            if (i % 9 == 0)
                g.Glow(x, y, 6 * s, Pal.Gold, 0.25f + 0.15f * MathF.Sin(time * 2 + i));
        }
        // Shaft walls, extending into any margin.
        float k = r.W / 640;
        var v = g.Visible;
        float left = r.X + WallL * k, right = r.X + WallR * k;
        float l0 = MathF.Min(r.X, v.X), r1 = MathF.Max(r.Right, v.Right);
        if (r.W < 600)
        {
            l0 = r.X;
            r1 = r.Right;
        }
        g.GradientH(l0, r.Y, left - l0, r.H, new Color(30, 20, 16), new Color(80, 56, 42));
        g.GradientH(right, r.Y, r1 - right, r.H, new Color(80, 56, 42), new Color(30, 20, 16));
        // Dirt floor.
        float gy = r.Y + GroundY * (r.H / 360);
        g.Rect(l0, gy, r1 - l0, r.Bottom - gy, new Color(70, 50, 35));
        g.Rect(l0, gy, r1 - l0, 2 * s, new Color(110, 80, 55));
    }

    private static void DrawStick(Gfx g, Vector2 p, float angle, float time, float s = 1)
    {
        var up = MathF2.FromAngle(angle - MathF.PI / 2);
        g.RotatedRect(p, 6 * s, 16 * s, angle, new Color(200, 40, 40));
        g.RotatedRect(p - new Vector2(1.2f, 0) * s, 2 * s, 15 * s, angle, new Color(240, 100, 90));
        g.RotatedRect(p + up * 3 * s, 6.5f * s, 2 * s, angle, new Color(120, 20, 25));
        var fuseEnd = p + up * 8 * s + new Vector2(3, -4) * s;
        g.Line(p + up * 8 * s, fuseEnd, 1.2f * s, Pal.Sand);
        float f = 0.7f + 0.3f * MathF.Sin(time * 35);
        g.Glow(fuseEnd, 10 * s * f, Pal.Orange, 0.9f);
        g.Circle(fuseEnd.X, fuseEnd.Y, 1.6f * s, Pal.Yellow);
    }

    private static void DrawBucket(Gfx g, float x, float rim, float s, float time)
    {
        float hw = BucketHalfW * s, h = 17 * s;
        var body = new Color(70, 110, 190);
        Quad[0] = new Vector2(x - hw, rim);
        Quad[1] = new Vector2(x + hw, rim);
        Quad[2] = new Vector2(x + hw * 0.75f, rim + h);
        Quad[3] = new Vector2(x - hw * 0.75f, rim + h);
        g.Polygon(Quad, body);
        Quad[0] = new Vector2(x - hw * 0.6f, rim + 1 * s);
        Quad[1] = new Vector2(x - hw * 0.35f, rim + 1 * s);
        Quad[2] = new Vector2(x - hw * 0.25f, rim + h - 1 * s);
        Quad[3] = new Vector2(x - hw * 0.45f, rim + h - 1 * s);
        g.Polygon(Quad, Pal.Lighten(body, 0.35f));
        g.Rect(x - hw * 0.85f, rim + h * 0.45f, hw * 1.7f, 2 * s, new Color(50, 80, 140));
        g.Ellipse(x, rim, hw, 4 * s, new Color(110, 150, 220));
        g.Ellipse(x, rim + 0.5f * s, hw * 0.88f, 3 * s, new Color(40, 110, 200));
        g.Ellipse(x + MathF.Sin(time * 3) * 4 * s, rim, hw * 0.4f, 1.2f * s, Pal.Ice * 0.7f);
        g.Arc(x, rim + 4 * s, hw * 0.9f, 1.3f * s, MathF.PI * 1.15f, MathF.PI * 1.85f, Pal.LightGrey * 0.8f);
    }

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        float s = r.H / 70f;
        DrawShaft(g, r, time, s * 0.7f);
        float beam = r.Y + 20 * s;
        g.Rect(r.X, beam, r.W, 5 * s, new Color(120, 80, 45));
        g.Rect(r.X, beam, r.W, 1.2f * s, new Color(170, 120, 70));
        float mx = r.CenterX + MathF.Sin(time * 1.7f) * 40 * s;
        g.Glow(mx, beam - 10 * s, 14 * s, Pal.Yellow, 0.4f);
        g.Pixels(MinerArt[(int)(time * 8) % 2], mx - 9 * s, beam - 12 * 1.5f * s * 0.85f - 2 * s, 1.3f * s, MathF.Cos(time * 1.7f) < 0);
        for (int i = 0; i < 3; i++)
        {
            float t = (time * 0.7f + i / 3f) % 1;
            float sx = r.CenterX + MathF.Sin(time * 1.7f - (t * 1.2f)) * 40 * s;
            float sy = beam + 8 * s + t * 36 * s;
            DrawStick(g, new Vector2(sx, sy), MathF.Sin(time * 4 + i) * 0.3f, time + i, s * 0.8f);
        }
        float bx = r.CenterX + MathF.Sin(time * 1.7f - 0.9f) * 40 * s;
        DrawBucket(g, bx, r.Bottom - 22 * s, s * 0.55f, time);
        DrawBucket(g, bx, r.Bottom - 14 * s, s * 0.55f, time + 1);
    }

    // ------------------------------------------------------------------ autoplay

    public override void AutoPlay(Controls c)
    {
        // Head for the stick that will reach the buckets first.
        float best = float.MaxValue, target = _minerX;
        foreach (var s in _sticks)
        {
            float t = (BucketTop - 8 - s.Pos.Y) / FallSpeed;
            if (t < -0.02f)
                continue;
            if (t < best)
            {
                best = t;
                target = s.Pos.X;
            }
        }
        float dx = target - _bucketX;
        c.SetDirections(MathF.Abs(dx) < 4 ? 0 : MathF2.Clamp(dx / 25, -1, 1), 0);
        c.Pointer = new Vector2(_bucketX, 300);
    }
}
