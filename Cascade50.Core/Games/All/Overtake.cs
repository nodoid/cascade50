using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 28 Overtake: a pseudo-3D road race against the clock. Weave through the traffic on a winding,
/// hilly road, reach each checkpoint for more time, and avoid lorries and oil slicks.
/// </summary>
public sealed class Overtake : MiniGame
{
    public override int Number => 28;
    public override string Title => "Overtake";
    public override Category Category => Category.Skill;
    public override string Tagline => "Race the clock and overtake everything on the road.";
    public override Color Accent => Pal.Red;
    public override Pad Pad => Pad.Stick;

    public override string[] HowToPlay =>
    [
        "Overtake as many cars as you can before the clock runs out. Checkpoints add time.",
        "Crashing spins you and costs time; oil slicks send you sliding. Keep off the grass!",
        "100 per car passed, plus distance.",
    ];

    public override string[] DesktopControls => ["UP to accelerate, DOWN to brake.", "LEFT / RIGHT to steer."];
    public override string[] TouchControls => ["Stick up to accelerate, down to brake, left and right to steer."];

    // ------------------------------------------------------------------ road model

    private const float SegL = 200, RoadW = 1200, CamH = 1000, CamDepth = 0.84f;
    private const int DrawDist = 150;
    private const float MaxSpeed = SegL * 60;
    private const float PlayerZ = CamH * CamDepth;
    private const float HorizonY = 168;
    private const int CheckpointEvery = 1300;

    private static float Hash(int i, int k)
    {
        float v = MathF.Sin(i * 127.1f + k * 311.7f) * 43758.547f;
        return v - MathF.Floor(v);
    }

    /// <summary>Curvature of segment i: sections of straights and bends, eased in and out.</summary>
    private static float CurveAt(int i)
    {
        if (i < 60)
            return 0;
        int sec = i / 90;
        float f = (i % 90) / 90f;
        float h = Hash(sec, 1);
        if (h < 0.3f)
            return 0;
        float amount = (Hash(sec, 2) - 0.5f) * 9f;
        return amount * MathF.Sin(f * MathF.PI);
    }

    private static float HillAt(float segPos) =>
        1100 * MathF.Sin(segPos * 0.011f) + 700 * MathF.Sin(segPos * 0.027f + 1.3f) - 1100 * MathF.Sin(0) - 700 * MathF.Sin(1.3f);

    // ------------------------------------------------------------------ state

    private sealed class Car
    {
        public float Z, X, Speed, TargetX;
        public bool Lorry, Passed;
        public Color Col;
    }

    private struct Slick
    {
        public float Z, X;
    }

    private readonly List<Car> _cars = new();
    private readonly List<Slick> _slicks = new();
    private float _pos;          // camera z
    private float _speed;
    private float _x;            // -1..1 is the road
    private float _time;
    private int _overtaken;
    private int _checkpoint;
    private float _spin;         // > 0 while spinning
    private float _spinDir;
    private float _skyScroll;
    private float _bannerT;
    private string _banner = "";
    private float _distanceScore;
    private float _crashCool;
    private int _lastBeep;
    private float _steerVis;

    // Projection scratch, per drawn segment.
    private readonly float[] _sx1 = new float[DrawDist + 1], _sy1 = new float[DrawDist + 1], _sw1 = new float[DrawDist + 1];
    private readonly float[] _sx2 = new float[DrawDist + 1], _sy2 = new float[DrawDist + 1], _sw2 = new float[DrawDist + 1];
    private readonly float[] _clip = new float[DrawDist + 1];
    private readonly float[] _bottom = new float[DrawDist + 1];
    private readonly float[] _scale = new float[DrawDist + 1];
    private readonly bool[] _visible = new bool[DrawDist + 1];
    private readonly Vector2[] _quad = new Vector2[4];

    private static readonly Color[] CarCols =
    [
        new(40, 110, 230), new(240, 200, 40), new(240, 240, 240), new(30, 160, 90), new(150, 60, 200),
        new(250, 130, 30), new(60, 60, 70), new(120, 200, 230), new(200, 60, 120),
    ];

    protected override void Start()
    {
        _pos = 0;
        _speed = 0;
        _x = 0;
        _time = 50;
        _overtaken = 0;
        _checkpoint = 0;
        Level = 1;
        _cars.Clear();
        _slicks.Clear();
        for (int i = 0; i < 10; i++)
            SpawnCar(PlayerZ + 2500 + i * 2400);
        Banner("GO!");
        Status = "TO CHECKPOINT 1";
    }

    private void Banner(string text)
    {
        _banner = text;
        _bannerT = 2f;
    }

    private void SpawnCar(float z)
    {
        float lane = Pick(-0.66f, 0f, 0.66f);
        bool lorry = Chance(0.22f + MathF.Min(_checkpoint, 5) * 0.03f);
        _cars.Add(new Car
        {
            Z = z,
            X = lane,
            TargetX = lane,
            Lorry = lorry,
            Speed = MaxSpeed * (lorry ? Rand(0.28f, 0.4f) : Rand(0.38f, 0.62f)),
            Col = CarCols[RandInt(0, CarCols.Length)],
        });
    }

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        if (_bannerT > 0)
            _bannerT -= Dt;
        if (_crashCool > 0)
            _crashCool -= Dt;

        float pct = _speed / MaxSpeed;
        int baseSeg = (int)(_pos / SegL);
        float curve = CurveAt(baseSeg);

        // Controls.
        bool accel = In.Up || In.AxisY < -0.3f;
        bool brake = In.Down || In.AxisY > 0.3f;
        float steer = MathF.Abs(In.AxisX) > 0.15f ? In.AxisX : (In.Left ? -1 : In.Right ? 1 : 0);

        if (_spin > 0)
        {
            _spin -= Dt;
            _speed = MathF2.Approach(_speed, 0, MaxSpeed * 0.6f * Dt);
            _x += _spinDir * Dt * 0.6f * pct;
            if (Tick % 3 == 0)
                Fx.Spark(320 + Rand(-30, 30), 335, Rand(-40, 40), Rand(-40, -10), Pal.LightGrey * 0.6f, 0.6f, 4, 0, false);
        }
        else
        {
            if (accel)
                _speed += MaxSpeed / 4.5f * Dt;
            else if (brake)
                _speed -= MaxSpeed * 0.9f * Dt;
            else
                _speed -= MaxSpeed / 6f * Dt;
            _x += steer * Dt * 2.1f * MathF.Min(1, pct * 1.4f + 0.1f);
        }
        _steerVis = MathF2.Approach(_steerVis, _spin > 0 ? 0 : steer, Dt * 6);

        // Centrifugal force on bends.
        _x -= Dt * 2 * pct * curve * pct * 0.32f;
        _skyScroll += curve * pct * Dt * 60;

        // Grass slows you down.
        bool offRoad = MathF.Abs(_x) > 1.05f;
        if (offRoad && _speed > MaxSpeed * 0.3f)
        {
            _speed = MathF2.Approach(_speed, MaxSpeed * 0.3f, MaxSpeed * 1.2f * Dt);
            Fx.Shake(1.2f, 0.05f);
            if (Tick % 4 == 0)
                Fx.Spark(320 + Rand(-30, 30), 340, Rand(-60, 60), Rand(-80, -30), new Color(90, 160, 60), 0.4f, 2.5f, 200, false);
        }
        _x = MathF2.Clamp(_x, -2.2f, 2.2f);
        _speed = MathF2.Clamp(_speed, 0, MaxSpeed);

        float before = _pos;
        _pos += _speed * Dt;
        _distanceScore += _speed * Dt / 1000f;
        while (_distanceScore >= 1)
        {
            _distanceScore -= 1;
            AddScore(1);
        }

        // Engine.
        Sound.Loop(LoopSfx.Engine, true, -0.6f + pct * 1.3f + (accel ? 0.05f : 0), 0.45f);

        // Checkpoints.
        int cpSeg = (_checkpoint + 1) * CheckpointEvery;
        if ((_pos + PlayerZ) / SegL >= cpSeg && (before + PlayerZ) / SegL < cpSeg)
        {
            _checkpoint++;
            Level = _checkpoint + 1;
            float bonus = MathF.Max(15, 30 - _checkpoint * 2.5f);
            _time += bonus;
            Banner($"CHECKPOINT! +{(int)bonus} SEC");
            Sound.Play(Sfx.Bell);
            Sound.Play(Sfx.Bonus, 0, 0.6f);
            AddScore(250 * _checkpoint, 320, 140, Pal.Cyan);
            Status = "TO CHECKPOINT " + (_checkpoint + 1);
            // More oil and traffic further on.
            for (int i = 0; i < 2 + _checkpoint; i++)
                _slicks.Add(new Slick { Z = _pos + PlayerZ + Rand(4000, 30000), X = Pick(-0.66f, 0f, 0.66f) + Rand(-0.15f, 0.15f) });
        }

        UpdateTraffic(before);

        _time -= Dt;
        int secs = (int)MathF.Ceiling(_time);
        if (secs <= 10 && secs != _lastBeep && secs > 0)
        {
            _lastBeep = secs;
            Sound.Play(Sfx.Beep, 0.4f, 0.5f);
        }
        if (_time <= 0)
        {
            _time = 0;
            EndGame(false, $"Time up! {_overtaken} cars overtaken.");
        }
    }

    private void UpdateTraffic(float before)
    {
        float pz = _pos + PlayerZ, pzBefore = before + PlayerZ;
        int want = 10 + Math.Min(_checkpoint, 6) * 2;
        for (int i = _cars.Count - 1; i >= 0; i--)
        {
            var c = _cars[i];
            float cBefore = c.Z;
            c.Z += c.Speed * Dt;
            // Occasional lane changes.
            if (Chance(0.002f))
                c.TargetX = MathF2.Clamp(c.TargetX + Pick(-0.66f, 0.66f), -0.66f, 0.66f);
            c.X = MathF2.Approach(c.X, c.TargetX, Dt * 0.4f);

            // Overtaken?
            if (!c.Passed && cBefore >= pzBefore - 200 && c.Z < pz - 200)
            {
                c.Passed = true;
                _overtaken++;
                AddScore(100, 320, 250, Pal.Yellow);
                Sound.Play(Sfx.Coin, Rand(-0.1f, 0.2f), 0.4f);
                Sound.Play(Sfx.Whoosh, 0.2f, 0.3f);
            }

            // Collision.
            float halfW = c.Lorry ? 0.27f : 0.21f;
            float len = c.Lorry ? 700 : 350;
            if (_crashCool <= 0 && MathF.Abs(c.X - _x) < halfW + 0.18f && c.Z - pz > -len && c.Z - pz < 250)
            {
                Crash(c);
            }

            if (c.Z < _pos - 1000 || c.Z > _pos + DrawDist * SegL * 1.6f)
                _cars.RemoveAt(i);
        }
        int ahead = 0;
        foreach (var c in _cars)
            if (c.Z > pz)
                ahead++;
        if (ahead < want)
            SpawnCar(_pos + DrawDist * SegL + Rand(0, 3000));

        for (int i = _slicks.Count - 1; i >= 0; i--)
        {
            var s = _slicks[i];
            if (_spin <= 0 && MathF.Abs(s.X - _x) < 0.24f && s.Z <= pz && s.Z > pzBefore)
            {
                _spin = 1.3f;
                _spinDir = Chance(0.5f) ? 1 : -1;
                _speed *= 0.75f;
                Sound.Play(Sfx.Zap, -0.6f, 0.6f);
                Fx.Float("OIL!", 320, 260, Pal.Orange);
            }
            if (s.Z < _pos - 500)
                _slicks.RemoveAt(i);
        }
        if (_slicks.Count < 1 + _checkpoint && Chance(0.01f))
            _slicks.Add(new Slick { Z = _pos + DrawDist * SegL + Rand(0, 5000), X = Pick(-0.66f, 0f, 0.66f) + Rand(-0.1f, 0.1f) });
    }

    private void Crash(Car c)
    {
        _crashCool = 1.5f;
        _spin = 1.0f;
        _spinDir = c.X > _x ? -1 : 1;
        _speed = MathF.Min(_speed, c.Speed * 0.6f);
        _time = MathF.Max(0.5f, _time - 3);
        c.Z += 300;
        Fx.Explode(320, 300, 0.8f);
        Fx.Float("CRASH! -3 SEC", 320, 220, Pal.Red, 2f);
        Sound.Play(Sfx.Hit);
        Sound.Play(Sfx.Crack, -0.3f, 0.7f);
        Fx.Shake(6, 0.4f);
    }

    // ------------------------------------------------------------------ drawing

    private static readonly Color SkyTop = new(30, 40, 110), SkyBottom = new(250, 140, 90), Fog = new(200, 130, 110);

    public override void Draw(Gfx g)
    {
        // Sky, sun and two layers of hills.
        g.GradientV(0, 0, 640, HorizonY + 40, SkyTop, SkyBottom);
        float sunX = 320 - Backdrops.Mod(_skyScroll * 0.3f, 1200) + 600;
        if (sunX > 900) sunX -= 1200;
        g.Glow(sunX, HorizonY - 30, 160, Pal.Orange, 0.5f);
        g.Circle(sunX, HorizonY - 30, 34, new Color(255, 220, 140));
        for (int i = 0; i < 4; i++)
            g.Rect(sunX - 40, HorizonY - 26 + i * 7, 80, 2 + i * 0.5f, new Color(250, 140, 90) * 0.9f);
        Backdrops.Hills(g, HorizonY + 4, 28, _skyScroll * 0.6f, new Color(120, 70, 110), 5, Screen.Bounds);
        Backdrops.Hills(g, HorizonY + 18, 18, _skyScroll * 1.2f, new Color(70, 50, 90), 9, Screen.Bounds);

        DrawRoad(g);
        DrawSprites(g);
        DrawPlayer(g);
        DrawHud(g);
    }

    private void Project(float worldX, float worldY, float worldZ, float camX, float camY, out float sx, out float sy, out float sw, out float scale)
    {
        float cz = worldZ - _pos;
        scale = CamDepth / MathF.Max(cz, 1);
        sx = 320 + scale * (worldX - camX) * 320;
        sy = HorizonY - scale * (worldY - camY) * 180;
        sw = scale * RoadW * 320;
    }

    private void DrawRoad(Gfx g)
    {
        int baseSeg = (int)(_pos / SegL);
        float basePct = (_pos % SegL) / SegL;
        float playerSegPos = (_pos + PlayerZ) / SegL;
        float camY = CamH + HillAt(playerSegPos);
        float camX = _x * RoadW;
        float x = 0, dx = -CurveAt(baseSeg) * basePct;
        float maxY = 360;
        for (int n = 0; n < DrawDist; n++)
        {
            int i = baseSeg + n;
            float z1 = i * SegL, z2 = (i + 1) * SegL;
            Project(0, HillAt(i), z1, camX - x, camY, out float x1, out float y1, out float w1, out float s1);
            Project(0, HillAt(i + 1), z2, camX - x - dx, camY, out float x2, out float y2, out float w2, out _);
            x += dx;
            dx += CurveAt(i);
            _visible[n] = false;
            _clip[n] = maxY;
            _sx1[n] = x1; _sy1[n] = y1; _sw1[n] = w1;
            _sx2[n] = x2; _sy2[n] = y2; _sw2[n] = w2;
            _scale[n] = s1;
            if (z1 - _pos <= CamDepth * 10 || y2 >= maxY || y2 >= y1)
                continue;
            _visible[n] = true;
            float bottom = MathF.Min(y1, maxY);
            if (bottom < y1)
            {
                float t = (bottom - y2) / (y1 - y2);
                _sx1[n] = MathF2.Lerp(x2, x1, t);
                _sw1[n] = MathF2.Lerp(w2, w1, t);
                _bottom[n] = bottom;
            }
            else
                _bottom[n] = y1;
            maxY = y2;
        }
        // Paint far to near so each strip's overlap is covered by the nearer one.
        for (int n = DrawDist - 1; n >= 0; n--)
            if (_visible[n])
                DrawSegment(g, baseSeg + n, n, _sx1[n], _bottom[n], _sw1[n], _sx2[n], _sy2[n], _sw2[n]);
    }

    private void DrawSegment(Gfx g, int i, int n, float x1, float y1, float w1, float x2, float y2, float w2)
    {
        bool light = (i / 3) % 2 == 0;
        float fog = MathF.Pow(n / (float)DrawDist, 1.6f);
        var grass = Pal.Lerp(light ? new Color(70, 150, 60) : new Color(56, 128, 50), Fog, fog);
        var rumble = Pal.Lerp(light ? new Color(230, 230, 230) : new Color(200, 30, 30), Fog, fog);
        var road = Pal.Lerp(light ? new Color(92, 92, 100) : new Color(86, 86, 94), Fog, fog);
        var lane = Pal.Lerp(new Color(235, 235, 235), Fog, fog);
        bool checkpoint = i % CheckpointEvery == 0 && i > 0;

        g.Rect(0, y2, 640, y1 - y2 + 1f, grass);
        Quad(g, x1, y1, w1 * 1.18f, x2, y2, w2 * 1.18f, rumble);
        Quad(g, x1, y1, w1, x2, y2, w2, checkpoint ? Pal.Lerp(Pal.White, Fog, fog) : road);
        if (light)
        {
            float l1 = w1 * 0.03f, l2 = w2 * 0.03f;
            for (int k = -1; k <= 1; k += 2)
            {
                float o1 = w1 * 0.333f * k, o2 = w2 * 0.333f * k;
                QuadAt(g, x1 + o1, y1, l1, x2 + o2, y2, l2, lane);
            }
        }
    }

    private void Quad(Gfx g, float x1, float y1, float w1, float x2, float y2, float w2, Color c)
    {
        _quad[0] = new Vector2(x1 - w1, y1);
        _quad[1] = new Vector2(x2 - w2, y2);
        _quad[2] = new Vector2(x2 + w2, y2);
        _quad[3] = new Vector2(x1 + w1, y1);
        g.Polygon(_quad, c);
    }

    private void QuadAt(Gfx g, float x1, float y1, float w1, float x2, float y2, float w2, Color c) => Quad(g, x1, y1, w1, x2, y2, w2, c);

    private void DrawSprites(Gfx g)
    {
        int baseSeg = (int)(_pos / SegL);
        // Far to near.
        for (int n = DrawDist - 1; n > 0; n--)
        {
            if (!_visible[n] && _sy1[n] >= _clip[n])
                continue;
            int i = baseSeg + n;
            float fog = MathF.Pow(n / (float)DrawDist, 1.6f);
            float clip = _clip[n];
            float s = _scale[n];
            float sx = _sx1[n], sy = _sy1[n], sw = _sw1[n];

            // Roadside scenery.
            float h = Hash(i, 7);
            if (i % 4 == 0 && h < 0.7f)
            {
                float side = Hash(i, 8) < 0.5f ? -1 : 1;
                float off = 1.35f + Hash(i, 9) * 1.4f;
                float px = sx + side * sw * off;
                int kind = (int)(Hash(i, 10) * 3);
                Clip(g, clip, sy);
                if (kind == 0) DrawTree(g, px, sy, sw * 0.7f, fog);
                else if (kind == 1) DrawPost(g, px, sy, sw * 0.5f, fog, side);
                else DrawBush(g, px, sy, sw * 0.5f, fog);
                Unclip(g, clip, sy);
            }
            if (i % CheckpointEvery == 0 && i > 0)
            {
                Clip(g, clip, sy);
                DrawGantry(g, sx, sy, sw, fog);
                Unclip(g, clip, sy);
            }

            // Oil slicks and cars within this segment.
            float zA = i * SegL, zB = zA + SegL;
            foreach (var o in _slicks)
                if (o.Z >= zA && o.Z < zB)
                {
                    float ox = sx + o.X * sw;
                    g.Ellipse(ox, sy - 1, sw * 0.2f, sw * 0.04f + 0.5f, new Color(20, 16, 30) * (1 - fog));
                    g.Ellipse(ox - sw * 0.05f, sy - 1.5f, sw * 0.07f, sw * 0.015f + 0.3f, Pal.Purple * (0.5f * (1 - fog)));
                }
            foreach (var c in _cars)
                if (c.Z >= zA && c.Z < zB)
                {
                    float t = (c.Z - zA) / SegL;
                    float cx = MathF2.Lerp(_sx1[n], _sx2[n], t), cy = MathF2.Lerp(_sy1[n], _sy2[n], t), cw = MathF2.Lerp(_sw1[n], _sw2[n], t);
                    float px = cx + c.X * cw;
                    Clip(g, clip, cy);
                    if (c.Lorry)
                        DrawLorry(g, px, cy, cw * 0.54f / 80, c.Col, fog);
                    else
                        DrawCarRear(g, px, cy, cw * 0.42f / 70, c.Col, fog, 0, false);
                    Unclip(g, clip, cy);
                }
        }
    }

    private static void Clip(Gfx g, float clip, float bottom)
    {
        if (bottom > clip + 0.5f)
            g.SetClip(new RectF(0, 0, 640, clip));
    }

    private static void Unclip(Gfx g, float clip, float bottom)
    {
        if (bottom > clip + 0.5f)
            g.SetClip(Screen.Bounds);
    }

    private static void DrawTree(Gfx g, float x, float y, float k, float fog)
    {
        if (k < 0.5f) return;
        var trunk = Pal.Lerp(new Color(90, 60, 40), Fog, fog);
        var leaf = Pal.Lerp(new Color(30, 110, 50), Fog, fog);
        var leaf2 = Pal.Lerp(new Color(50, 140, 60), Fog, fog);
        g.Rect(x - k * 0.06f, y - k * 0.5f, k * 0.12f, k * 0.5f, trunk);
        g.Triangle(new Vector2(x, y - k * 1.6f), new Vector2(x - k * 0.45f, y - k * 0.45f), new Vector2(x + k * 0.45f, y - k * 0.45f), leaf);
        g.Triangle(new Vector2(x, y - k * 1.9f), new Vector2(x - k * 0.35f, y - k * 0.95f), new Vector2(x + k * 0.35f, y - k * 0.95f), leaf2);
    }

    private static void DrawBush(Gfx g, float x, float y, float k, float fog)
    {
        if (k < 0.5f) return;
        var c = Pal.Lerp(new Color(60, 130, 50), Fog, fog);
        g.Circle(x - k * 0.2f, y - k * 0.2f, k * 0.25f, c);
        g.Circle(x + k * 0.15f, y - k * 0.25f, k * 0.3f, Pal.Lighten(c, 0.1f));
        g.Circle(x, y - k * 0.4f, k * 0.25f, c);
    }

    private static void DrawPost(Gfx g, float x, float y, float k, float fog, float side)
    {
        if (k < 0.5f) return;
        var post = Pal.Lerp(new Color(220, 220, 220), Fog, fog);
        g.Rect(x - k * 0.04f, y - k * 0.5f, k * 0.08f, k * 0.5f, post);
        g.Rect(x - k * 0.04f, y - k * 0.42f, k * 0.08f, k * 0.08f, Pal.Lerp(Pal.Black, Fog, fog));
        g.Glow(x, y - k * 0.38f, k * 0.08f + 1, side < 0 ? Pal.Red : Pal.Orange, 0.6f * (1 - fog));
    }

    private static void DrawGantry(Gfx g, float x, float y, float sw, float fog)
    {
        var steel = Pal.Lerp(new Color(60, 60, 70), Fog, fog);
        float span = sw * 1.25f;
        float h = sw * 0.9f;
        g.Rect(x - span - sw * 0.03f, y - h, sw * 0.06f, h, steel);
        g.Rect(x + span - sw * 0.03f, y - h, sw * 0.06f, h, steel);
        g.Rect(x - span, y - h - sw * 0.16f, span * 2, sw * 0.18f, Pal.Lerp(new Color(20, 90, 200), Fog, fog));
        float ts = sw * 0.12f / 8f;
        if (ts > 0.3f)
            g.Text("CHECKPOINT", x, y - h - sw * 0.135f, ts, Pal.Lerp(Pal.White, Fog, fog), Align.Center);
    }

    /// <summary>A car seen from behind. <paramref name="k"/> = pixels per metre-ish unit (car is ~70k wide).</summary>
    private static void DrawCarRear(Gfx g, float x, float y, float k, Color col, float fog, float tilt, bool player)
    {
        float w = 70 * k, h = 30 * k;
        if (w < 1.5f) return;
        var body = Pal.Lerp(col, Fog, fog);
        var dark = Pal.Lerp(Pal.Darken(col, 0.45f), Fog, fog);
        var glass = Pal.Lerp(new Color(30, 40, 70), Fog, fog);
        // Shadow and wheels.
        g.Ellipse(x, y, w * 0.55f, h * 0.12f + 0.5f, Color.Black * (0.4f * (1 - fog)));
        g.Rect(x - w * 0.46f, y - h * 0.32f, w * 0.16f, h * 0.32f, Pal.Lerp(new Color(20, 20, 24), Fog, fog));
        g.Rect(x + w * 0.30f, y - h * 0.32f, w * 0.16f, h * 0.32f, Pal.Lerp(new Color(20, 20, 24), Fog, fog));
        // Body.
        g.RoundRect(x - w / 2, y - h * 0.8f, w, h * 0.55f, h * 0.12f, body);
        g.Rect(x - w / 2, y - h * 0.32f, w, h * 0.08f, dark);
        // Cabin.
        _q[0] = new Vector2(x - w * 0.36f, y - h * 0.78f);
        _q[1] = new Vector2(x - w * 0.27f + tilt * w * 0.05f, y - h * 1.12f);
        _q[2] = new Vector2(x + w * 0.27f + tilt * w * 0.05f, y - h * 1.12f);
        _q[3] = new Vector2(x + w * 0.36f, y - h * 0.78f);
        g.Polygon(_q, dark);
        _q[0] = new Vector2(x - w * 0.31f, y - h * 0.8f);
        _q[1] = new Vector2(x - w * 0.24f + tilt * w * 0.05f, y - h * 1.06f);
        _q[2] = new Vector2(x + w * 0.24f + tilt * w * 0.05f, y - h * 1.06f);
        _q[3] = new Vector2(x + w * 0.31f, y - h * 0.8f);
        g.Polygon(_q, glass);
        // Lights and plate.
        var lightCol = Pal.Lerp(Pal.Red, Fog, fog * 0.5f);
        g.Rect(x - w * 0.46f, y - h * 0.66f, w * 0.18f, h * 0.12f, lightCol);
        g.Rect(x + w * 0.28f, y - h * 0.66f, w * 0.18f, h * 0.12f, lightCol);
        if (w > 12)
        {
            g.Glow(x - w * 0.37f, y - h * 0.6f, w * 0.18f, Pal.Red, 0.5f * (1 - fog));
            g.Glow(x + w * 0.37f, y - h * 0.6f, w * 0.18f, Pal.Red, 0.5f * (1 - fog));
            g.Rect(x - w * 0.1f, y - h * 0.62f, w * 0.2f, h * 0.12f, Pal.Lerp(new Color(250, 220, 60), Fog, fog));
            g.Rect(x - w / 2, y - h * 0.8f, w, h * 0.05f, Pal.Lighten(body, 0.25f));
        }
        if (player)
        {
            g.Rect(x - w * 0.5f, y - h * 0.95f, w, h * 0.06f, Pal.Darken(col, 0.2f));
            g.Rect(x - w * 0.52f, y - h * 1.0f, w * 0.06f, h * 0.2f, dark);
            g.Rect(x + w * 0.46f, y - h * 1.0f, w * 0.06f, h * 0.2f, dark);
        }
    }

    private static readonly Vector2[] _q = new Vector2[4];

    private static void DrawLorry(Gfx g, float x, float y, float k, Color col, float fog)
    {
        float w = 80 * k, h = 90 * k;
        if (w < 1.5f) return;
        g.Ellipse(x, y, w * 0.55f, h * 0.05f + 0.5f, Color.Black * (0.4f * (1 - fog)));
        var box = Pal.Lerp(new Color(220, 222, 228), Fog, fog);
        g.Rect(x - w * 0.42f, y - h * 0.12f, w * 0.2f, h * 0.12f, Pal.Lerp(new Color(20, 20, 24), Fog, fog));
        g.Rect(x + w * 0.22f, y - h * 0.12f, w * 0.2f, h * 0.12f, Pal.Lerp(new Color(20, 20, 24), Fog, fog));
        g.Rect(x - w / 2, y - h, w, h * 0.86f, box);
        g.Rect(x - w / 2, y - h * 0.62f, w, h * 0.14f, Pal.Lerp(col, Fog, fog));
        g.Rect(x - w * 0.01f, y - h * 0.98f, w * 0.02f, h * 0.82f, Pal.Lerp(new Color(150, 150, 160), Fog, fog));
        g.Rect(x - w / 2, y - h * 0.16f, w, h * 0.04f, Pal.Lerp(new Color(230, 160, 30), Fog, fog));
        var lightCol = Pal.Lerp(Pal.Red, Fog, fog * 0.5f);
        g.Rect(x - w * 0.48f, y - h * 0.24f, w * 0.1f, h * 0.06f, lightCol);
        g.Rect(x + w * 0.38f, y - h * 0.24f, w * 0.1f, h * 0.06f, lightCol);
        if (w > 12)
        {
            g.Glow(x - w * 0.43f, y - h * 0.21f, w * 0.14f, Pal.Red, 0.5f * (1 - fog));
            g.Glow(x + w * 0.43f, y - h * 0.21f, w * 0.14f, Pal.Red, 0.5f * (1 - fog));
        }
    }

    private void DrawPlayer(Gfx g)
    {
        float bounce = _speed > 100 ? MathF.Sin(Time * 30) * 0.8f * (_speed / MaxSpeed) : 0;
        float x = 320, y = 352 + bounce;
        float wobble = _spin > 0 ? MathF.Sin(_spin * 25) * 14 : 0;
        x += wobble + _steerVis * 6;
        float k = 2.0f;
        // Exhaust and speed lines.
        if (In.Up || In.AxisY < -0.3f)
        {
            g.Glow(x - 40, y - 8, 12 + MathF.Sin(Time * 40) * 3, Pal.Orange, 0.7f);
            g.Glow(x + 40, y - 8, 12 + MathF.Sin(Time * 37) * 3, Pal.Orange, 0.7f);
        }
        DrawCarRear(g, x, y, k, new Color(220, 30, 40), 0, _steerVis, true);
        if (_spin > 0)
            for (int i = 0; i < 3; i++)
                g.Circle(x + Rand(-40, 40), y - Rand(0, 12), Rand(4, 9), Pal.LightGrey * 0.25f);
    }

    private void DrawHud(Gfx g)
    {
        // Time, big and central.
        int secs = (int)MathF.Ceiling(_time);
        var tc = secs <= 10 ? (secs % 2 == 0 ? Pal.Red : Pal.Yellow) : Pal.Yellow;
        g.RoundRect(270, 26, 100, 40, 8, Color.Black * 0.45f);
        g.Text("TIME", 320, 29, 1f, Pal.White * 0.8f, Align.Center);
        g.TextShadow(secs.ToString(), 320, 39, 3f, tc, Align.Center);

        // Speedometer.
        int kmh = (int)(_speed / MaxSpeed * 290);
        g.RoundRect(10, 26, 112, 40, 8, Color.Black * 0.45f);
        g.TextShadow(kmh.ToString(), 72, 34, 2.5f, Pal.White, Align.Right);
        g.Text("KM/H", 78, 44, 1f, Pal.LightGrey);
        float bar = _speed / MaxSpeed;
        g.Rect(16, 59, 100, 3, Color.Black * 0.5f);
        g.Rect(16, 59, 100 * bar, 3, Pal.Lerp(Pal.Lime, Pal.Red, bar));

        // Cars passed and checkpoint distance.
        g.RoundRect(518, 26, 112, 40, 8, Color.Black * 0.45f);
        g.TextShadow(_overtaken.ToString(), 580, 34, 2.5f, Pal.Cyan, Align.Right);
        g.Text("PASSED", 584, 44, 1f, Pal.LightGrey);
        float toCp = (_checkpoint + 1) * CheckpointEvery - (_pos + PlayerZ) / SegL;
        float cpFrac = 1 - MathF2.Clamp(toCp / CheckpointEvery, 0, 1);
        g.Rect(524, 59, 100, 3, Color.Black * 0.5f);
        g.Rect(524, 59, 100 * cpFrac, 3, Pal.Sky);

        if (_bannerT > 0)
        {
            float a = MathF.Min(1, _bannerT * 2);
            g.TextShadow(_banner, 320, 100, 2.5f, Pal.Yellow * a, Align.Center);
        }
    }

    // ------------------------------------------------------------------ icon

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        float s = r.H / 70f;
        float hy = r.Y + r.H * 0.48f;
        g.GradientV(r.X, r.Y, r.W, hy - r.Y, SkyTop, SkyBottom);
        g.Glow(r.CenterX + 20 * s, hy - 8 * s, 40 * s, Pal.Orange, 0.5f);
        g.Circle(r.CenterX + 20 * s, hy - 8 * s, 9 * s, new Color(255, 220, 140));
        Backdrops.Hills(g, hy + 2 * s, 6 * s, time * 4, new Color(100, 60, 100), 5, r);
        g.Rect(r.X, hy, r.W, r.Bottom - hy, new Color(60, 140, 55));
        // Road in perspective, with moving stripes.
        float bend = MathF.Sin(time * 0.6f) * 14 * s;
        int strips = 18;
        for (int i = strips - 1; i >= 0; i--)
        {
            float z0 = i / (float)strips, z1 = (i + 1) / (float)strips;
            float y0 = r.Bottom - (r.Bottom - hy) * z0, y1 = r.Bottom - (r.Bottom - hy) * z1;
            float w0 = (1 - z0 * 0.92f) * r.W * 0.48f, w1 = (1 - z1 * 0.92f) * r.W * 0.48f;
            float c0 = r.CenterX + bend * z0 * z0, c1 = r.CenterX + bend * z1 * z1;
            bool light = ((int)(i + time * 12) % 2) == 0;
            _q[0] = new Vector2(c0 - w0 * 1.15f, y0); _q[1] = new Vector2(c1 - w1 * 1.15f, y1);
            _q[2] = new Vector2(c1 + w1 * 1.15f, y1); _q[3] = new Vector2(c0 + w0 * 1.15f, y0);
            g.Polygon(_q, light ? Pal.White : Pal.Red);
            _q[0] = new Vector2(c0 - w0, y0); _q[1] = new Vector2(c1 - w1, y1);
            _q[2] = new Vector2(c1 + w1, y1); _q[3] = new Vector2(c0 + w0, y0);
            g.Polygon(_q, light ? new Color(92, 92, 100) : new Color(84, 84, 92));
            if (light)
                g.Line(c0, y0, c1, y1, MathF.Max(0.5f, w0 * 0.04f), Pal.White);
        }
        // Traffic ahead and the player.
        float tz = 0.55f + 0.1f * MathF.Sin(time);
        float ty = r.Bottom - (r.Bottom - hy) * tz;
        DrawCarRear(g, r.CenterX + bend * tz * tz + 10 * s * (1 - tz), ty, (1 - tz * 0.92f) * 0.45f * s, CarCols[1], 0, 0, false);
        DrawCarRear(g, r.CenterX + MathF.Sin(time * 1.3f) * 6 * s, r.Bottom - 2 * s, 0.42f * s, new Color(220, 30, 40), 0, MathF.Sin(time * 1.3f), true);
    }

    // ------------------------------------------------------------------ autoplay

    private static readonly float[] Lanes = [-0.66f, 0f, 0.66f];

    public override void AutoPlay(Controls c)
    {
        float pz = _pos + PlayerZ;
        float bestFree = -1, target = _x;
        foreach (float lane in Lanes)
        {
            float free = 99999;
            foreach (var car in _cars)
                if (MathF.Abs(car.X - lane) < 0.45f && car.Z > pz - 200)
                    free = MathF.Min(free, car.Z - pz);
            foreach (var o in _slicks)
                if (MathF.Abs(o.X - lane) < 0.3f && o.Z > pz)
                    free = MathF.Min(free, o.Z - pz);
            free -= MathF.Abs(lane - _x) * 1500;
            if (free > bestFree)
            {
                bestFree = free;
                target = lane;
            }
        }
        float curve = CurveAt((int)(_pos / SegL));
        float steer = MathF2.Clamp((target - _x) * 4 + curve * 0.12f * (_speed / MaxSpeed), -1, 1);
        bool brake = false;
        foreach (var car in _cars)
            if (MathF.Abs(car.X - _x) < 0.5f && car.Z - pz > 150 && car.Z - pz < 900 + _speed * 0.12f && car.Speed < _speed)
                brake = true;
        c.SetDirections(steer, brake ? 1 : -1);
    }
}
