using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 03 Black Hole: fly a little ship around a black hole with real inverse-square gravity, scooping
/// up energy stars before they spiral in. Fuel is short, asteroids fall past, and the hole grows.
/// </summary>
public sealed class BlackHole : MiniGame
{
    public override int Number => 3;
    public override string Title => "Black Hole";
    public override Category Category => Category.Skill;
    public override string Tagline => "Skim the event horizon to steal energy stars from a hungry black hole.";
    public override Color Accent => Pal.Orange;

    public override string[] HowToPlay =>
    [
        "Gravity pulls you towards the hole. Thrust to orbit and collect stars before they fall in.",
        "Stars refuel you and score double near the hole. Avoid asteroids and the horizon!",
        "8 stars clear a level and the hole grows. 3 ships.",
    ];

    public override string[] DesktopControls => ["ARROWS rotate, SPACE thrusts.", "P or ESC to pause."];
    public override string[] TouchControls => ["Stick to rotate, THRUST to fire the engine."];
    public override Pad Pad => Pad.Horizontal | Pad.Fire;
    public override string FireLabel => "THRUST";

    private static readonly Vector2 Hole = new(320, 192);
    private const float ThrustAccel = 135, TurnRate = 3.8f, ShipR = 6;
    private const int StarsPerLevel = 8;

    private static readonly Vector2[] ShipShape = [new(9, 0), new(-6, -6), new(-3, 0), new(-6, 6)];

    // Accretion disc particles: radius factor, start angle, colour heat.
    private const int DiscCount = 320;
    private static readonly float[] DiscR = new float[DiscCount], DiscA = new float[DiscCount], DiscHeat = new float[DiscCount];

    static BlackHole()
    {
        var rng = new Random(42);
        for (int i = 0; i < DiscCount; i++)
        {
            float u = (float)rng.NextDouble();
            DiscR[i] = 1.5f + u * u * 3.4f;
            DiscA[i] = (float)rng.NextDouble() * MathF2.Tau;
            DiscHeat[i] = 1 - (DiscR[i] - 1.5f) / 3.4f;
        }
    }

    private struct Star
    {
        public Vector2 Pos;
        public float Age;
    }

    private struct Rock
    {
        public Vector2 Pos, Vel;
        public float R, Angle, Spin;
        public Vector2[] Shape;
    }

    private readonly List<Star> _stars = new();
    private readonly List<Rock> _rocks = new();
    private readonly Vector2[] _trail = new Vector2[40];
    private readonly Vector2[] _predict = new Vector2[45];
    private int _trailHead, _predictCount;

    private Vector2 _pos, _vel;
    private float _angle;
    private float _fuel;
    private bool _thrusting;
    private float _invuln, _dying, _banner;
    private int _deathKind; // 0 asteroid, 1 spaghetti, 2 fuel
    private int _collected;
    private float _rockTimer, _starTimer, _lowFuelBeep;
    private float _horizon, _gm, _gulp;

    protected override void Start()
    {
        Lives = 3;
        Level = 1;
        SetupLevel();
        Respawn();
    }

    private void SetupLevel()
    {
        _horizon = 15 + (Level - 1) * 3.5f;
        _gm = 430000 * (1 + (Level - 1) * 0.1f);
        _collected = 0;
        _stars.Clear();
        _rocks.Clear();
        _rockTimer = 4;
        _starTimer = 0;
        _banner = 2.2f;
        for (int i = 0; i < 3; i++)
            SpawnStar();
    }

    private void Respawn()
    {
        float r = 150;
        _pos = Hole + new Vector2(r, 0);
        _vel = new Vector2(0, -MathF.Sqrt(_gm / r));
        _angle = -MathF.PI / 2;
        _fuel = 100;
        _invuln = 2.2f;
        _dying = 0;
        for (int i = 0; i < _trail.Length; i++)
            _trail[i] = _pos;
    }

    private float DeadlyRadius => _horizon * 1.3f;

    private Vector2 GravityAt(Vector2 p)
    {
        var d = Hole - p;
        float r2 = d.LengthSquared() + 30;
        return d / MathF.Sqrt(r2) * (_gm / r2);
    }

    private void SpawnStar()
    {
        for (int tries = 0; tries < 30; tries++)
        {
            float r = Rand(MathF.Max(48, _horizon * 3.2f), 168);
            float a = Rand(0, MathF2.Tau);
            var p = Hole + MathF2.FromAngle(a, r);
            if (p.Y < 40 || p.Y > 340 || p.X < 20 || p.X > 620)
                continue;
            if (Vector2.Distance(p, _pos) < 50)
                continue;
            _stars.Add(new Star { Pos = p });
            return;
        }
    }

    private void SpawnRock()
    {
        int side = RandInt(0, 4);
        Vector2 p = side switch
        {
            0 => new Vector2(-15, Rand(40, 340)),
            1 => new Vector2(655, Rand(40, 340)),
            2 => new Vector2(Rand(20, 620), 10),
            _ => new Vector2(Rand(20, 620), 375),
        };
        var toHole = Vector2.Normalize(Hole - p);
        var tangent = new Vector2(-toHole.Y, toHole.X) * (Chance(0.5f) ? 1 : -1);
        float speed = Rand(35, 60) + Level * 4;
        _rocks.Add(new Rock
        {
            Pos = p, Vel = (toHole * 0.7f + tangent * Rand(0.3f, 0.8f)) * speed, R = Rand(6, 11),
            Angle = Rand(0, 6), Spin = Rand(-2, 2), Shape = RockShape(),
        });
    }

    private Vector2[] RockShape()
    {
        var pts = new Vector2[9];
        for (int i = 0; i < pts.Length; i++)
            pts[i] = MathF2.FromAngle(i * MathF2.Tau / pts.Length, Rand(0.75f, 1f));
        return pts;
    }

    protected override void Update()
    {
        if (_banner > 0)
            _banner -= Dt;
        if (_gulp > 0)
            _gulp -= Dt;
        Sound.Loop(LoopSfx.Hum, true, -0.7f + Level * 0.05f, 0.25f);

        UpdateStars();
        UpdateRocks();

        if (_dying > 0)
        {
            _dying -= Dt;
            if (_deathKind == 1)
            {
                // Being stretched into the hole.
                _pos = Vector2.Lerp(_pos, Hole, 3 * Dt);
            }
            if (_dying <= 0 && !LoseLife())
                Respawn();
            return;
        }

        if (_invuln > 0)
            _invuln -= Dt;

        // Steering and thrust.
        _angle += In.AxisX * TurnRate * Dt;
        _thrusting = (In.Fire || In.Up) && _fuel > 0;
        if (_thrusting)
        {
            _vel += MathF2.FromAngle(_angle, ThrustAccel * Dt);
            _fuel -= 9 * Dt;
            var back = _pos - MathF2.FromAngle(_angle, 6);
            var ev = -MathF2.FromAngle(_angle + Rand(-0.3f, 0.3f), Rand(60, 110)) + _vel * 0.5f;
            Fx.Spark(back.X, back.Y, ev.X, ev.Y, Chance(0.5f) ? Pal.Orange : Pal.Yellow, 0.3f, 1.8f);
        }
        Sound.Loop(LoopSfx.Thrust, _thrusting, 0, 0.5f);
        _fuel = MathF.Max(0, _fuel - 1.6f * Dt);

        _vel += GravityAt(_pos) * Dt;
        _pos += _vel * Dt;

        // Soft walls.
        const float top = Screen.HudHeight + ShipR, bottom = 360 - ShipR;
        if (_pos.X < ShipR || _pos.X > 640 - ShipR)
        {
            _pos.X = MathF2.Clamp(_pos.X, ShipR, 640 - ShipR);
            _vel.X *= -0.5f;
            Sound.Play(Sfx.Bounce, -0.3f, 0.4f);
        }
        if (_pos.Y < top || _pos.Y > bottom)
        {
            _pos.Y = MathF2.Clamp(_pos.Y, top, bottom);
            _vel.Y *= -0.5f;
            Sound.Play(Sfx.Bounce, -0.3f, 0.4f);
        }

        if (Tick % 2 == 0)
        {
            _trail[_trailHead] = _pos;
            _trailHead = (_trailHead + 1) % _trail.Length;
        }
        Predict();

        // Low fuel warning.
        if (_fuel < 25)
        {
            _lowFuelBeep -= Dt;
            if (_lowFuelBeep <= 0)
            {
                Sound.Play(Sfx.Beep, 0.5f, 0.5f);
                _lowFuelBeep = 0.25f + _fuel / 25f;
            }
        }

        float dist = Vector2.Distance(_pos, Hole);
        if (dist < DeadlyRadius)
        {
            Die(1);
            return;
        }
        if (_fuel <= 0)
        {
            Die(2);
            return;
        }

        // Stars.
        for (int i = _stars.Count - 1; i >= 0; i--)
        {
            var s = _stars[i];
            if (Vector2.Distance(s.Pos, _pos) < ShipR + 9)
            {
                float sr = Vector2.Distance(s.Pos, Hole);
                bool risky = sr < _horizon * 4.5f;
                int pts = (risky ? 100 : 50) * Level;
                AddScore(pts, s.Pos.X, s.Pos.Y - 12, risky ? Pal.Orange : Pal.Yellow);
                if (risky)
                    Fx.Float("DARING!", s.Pos.X, s.Pos.Y - 26, Pal.Orange, 1f);
                _fuel = MathF.Min(100, _fuel + 22);
                _collected++;
                Fx.Burst(s.Pos.X, s.Pos.Y, Pal.Yellow, 20, 100, 0.5f, 2f);
                Fx.Burst(s.Pos.X, s.Pos.Y, Pal.White, 8, 50, 0.3f, 1.5f);
                Sound.Play(Sfx.Pickup, MathF.Min(0.8f, _collected * 0.08f), 0.8f);
                _stars.RemoveAt(i);
                if (_collected >= StarsPerLevel)
                {
                    AddScore(500 * Level, 320, 120, Pal.Cyan);
                    Sound.Play(Sfx.LevelUp);
                    Level++;
                    SetupLevel();
                    _invuln = 1.5f;
                    _fuel = 100;
                    return;
                }
            }
        }

        // Asteroids.
        if (_invuln <= 0)
            foreach (var r in _rocks)
                if (Vector2.Distance(r.Pos, _pos) < r.R + ShipR - 1)
                {
                    Die(0);
                    return;
                }
    }

    private void Die(int kind)
    {
        _deathKind = kind;
        _dying = kind == 1 ? 1.4f : 1.2f;
        _thrusting = false;
        if (kind == 1)
        {
            Sound.Play(Sfx.Warp, -0.6f);
            Fx.Burst(_pos.X, _pos.Y, Pal.Orange, 20, 60, 0.6f, 2f);
        }
        else
        {
            Fx.Explode(_pos.X, _pos.Y, 1.2f);
            Sound.Play(kind == 0 ? Sfx.BigExplode : Sfx.Die);
            if (kind == 2)
                Fx.Float("OUT OF FUEL", _pos.X, _pos.Y - 16, Pal.Red);
        }
    }

    private void Predict()
    {
        var p = _pos;
        var v = _vel;
        _predictCount = 0;
        const float step = 1f / 30f;
        for (int i = 0; i < _predict.Length * 2; i++)
        {
            v += GravityAt(p) * step;
            p += v * step;
            if (i % 2 == 1)
                _predict[_predictCount++] = p;
            if (Vector2.Distance(p, Hole) < DeadlyRadius)
                break;
        }
    }

    private void UpdateStars()
    {
        for (int i = _stars.Count - 1; i >= 0; i--)
        {
            var s = _stars[i];
            s.Age += Dt;
            // Stars slowly spiral in.
            var d = s.Pos - Hole;
            float r = d.Length();
            float a = MathF.Atan2(d.Y, d.X) - 0.12f * Dt * 150 / MathF.Max(r, 20);
            r -= (2.5f + Level * 0.6f) * Dt * 120 / MathF.Max(r, 30);
            s.Pos = Hole + MathF2.FromAngle(a, r);
            if (r < _horizon * 1.1f)
            {
                Fx.Burst(s.Pos.X, s.Pos.Y, Pal.Yellow, 10, 40, 0.4f, 1.5f);
                Sound.Play(Sfx.Whoosh, 0.3f, 0.35f);
                _gulp = 0.5f;
                _stars.RemoveAt(i);
                continue;
            }
            _stars[i] = s;
        }
        int want = 3 + Math.Min(2, Level / 3);
        if (_stars.Count < want)
        {
            _starTimer -= Dt;
            if (_starTimer <= 0)
            {
                SpawnStar();
                _starTimer = 0.8f;
            }
        }
    }

    private void UpdateRocks()
    {
        _rockTimer -= Dt;
        if (_rockTimer <= 0 && _banner <= 0)
        {
            SpawnRock();
            _rockTimer = MathF.Max(1.6f, 5.5f - Level * 0.6f) * Rand(0.7f, 1.3f);
        }
        for (int i = _rocks.Count - 1; i >= 0; i--)
        {
            var r = _rocks[i];
            r.Vel += GravityAt(r.Pos) * Dt;
            r.Pos += r.Vel * Dt;
            r.Angle += r.Spin * Dt;
            float dist = Vector2.Distance(r.Pos, Hole);
            if (dist < _horizon * 1.1f)
            {
                Fx.Burst(r.Pos.X, r.Pos.Y, Pal.Orange, 18, 70, 0.6f, 2.2f);
                Sound.Play(Sfx.Warp, Rand(-0.9f, -0.5f), 0.45f);
                Fx.Shake(1.5f, 0.15f);
                _gulp = 0.7f;
                _rocks.RemoveAt(i);
                continue;
            }
            if (r.Pos.X < -60 || r.Pos.X > 700 || r.Pos.Y < -60 || r.Pos.Y > 420)
            {
                _rocks.RemoveAt(i);
                continue;
            }
            _rocks[i] = r;
        }
    }

    // ------------------------------------------------------------------ drawing

    public override void Draw(Gfx g)
    {
        Backdrops.Space(g, Time, 4, 9, g.Visible);
        DrawWell(g, Hole, _horizon, Time, 1f, _gulp);

        // Orbit prediction.
        if (_dying <= 0)
            for (int i = 0; i < _predictCount; i++)
            {
                float k = 1 - i / (float)_predict.Length;
                g.Circle(_predict[i].X, _predict[i].Y, 1.1f, Pal.Cyan * (0.5f * k));
            }

        // Stars.
        foreach (var s in _stars)
            DrawStar(g, s.Pos, 1f, Time + s.Pos.X, MathF.Min(1, s.Age * 3));

        // Asteroids.
        foreach (var r in _rocks)
            DrawRock(g, r.Pos, r.R, r.Angle, r.Shape);

        // Trail.
        for (int i = 0; i < _trail.Length - 1; i++)
        {
            int a = (_trailHead + i) % _trail.Length, b = (a + 1) % _trail.Length;
            float k = i / (float)_trail.Length;
            if (Vector2.DistanceSquared(_trail[a], _trail[b]) < 400)
                g.Line(_trail[a], _trail[b], 1.5f, Pal.Add(Pal.Sky, 0.35f * k));
        }

        // Ship.
        if (_dying > 0)
        {
            if (_deathKind == 1)
            {
                // Spaghettified: stretched along the line to the hole.
                float k = 1 - _dying / 1.4f;
                var dir = Hole - _pos;
                float len = 8 + k * 40;
                var n = dir.Length() > 0.1f ? Vector2.Normalize(dir) : Vector2.UnitX;
                g.GlowLine(_pos - n * len * 0.3f, _pos + n * len, 2f * (1 - k) + 0.5f, Pal.Lerp(Pal.Cyan, Pal.Orange, k));
            }
        }
        else if (_invuln <= 0 || (int)(_invuln * 10) % 2 == 0)
        {
            if (_thrusting)
            {
                var back = _pos - MathF2.FromAngle(_angle, 5);
                g.Glow(back, 14 + MathF.Sin(Time * 40) * 3, Pal.Orange, 0.7f);
            }
            g.Glow(_pos, 20, Pal.Cyan, 0.35f);
            g.Shape(ShipShape, _pos, _angle, 1f, new Color(30, 60, 90));
            g.ShapeOutline(ShipShape, _pos, _angle, 1f, 1.2f, Pal.Cyan, true);
            g.Circle(_pos.X + MathF.Cos(_angle) * 2, _pos.Y + MathF.Sin(_angle) * 2, 1.5f, Pal.White);
        }

        DrawHud(g);

        if (_banner > 0)
        {
            float a = MathF.Min(1, _banner * 1.5f);
            g.TextShadow("LEVEL " + Level, 320, 82, 3f, Pal.Yellow * a, Align.Center);
            g.TextShadow(Level == 1 ? "COLLECT 8 STARS" : "THE HOLE GROWS...", 320, 112, 1.5f, Pal.White * a, Align.Center);
        }
    }

    private void DrawHud(Gfx g)
    {
        var box = new RectF(IsTouch ? 168 : 8, 332, 150, 22);
        g.Panel(box, Pal.Panel * 0.85f, Pal.PanelLight, 6);
        g.Text("FUEL", box.X + 8, box.Y + 8, 1f, Pal.LightGrey);
        var bar = new RectF(box.X + 38, box.Y + 7, 104, 10);
        g.Rect(bar, Pal.DarkGrey);
        var fc = _fuel < 25 ? ((Tick / 10) % 2 == 0 ? Pal.Red : Pal.Orange) : Pal.Lerp(Pal.Orange, Pal.Lime, _fuel / 100f);
        g.GradientH(bar.X, bar.Y, bar.W * _fuel / 100f, bar.H, Pal.Darken(fc, 0.3f), fc);
        g.Glow(bar.X + bar.W * _fuel / 100f, bar.CenterY, 10, fc, 0.4f);

        var box2 = new RectF(IsTouch ? 328 : 512, 332, 120, 22);
        g.Panel(box2, Pal.Panel * 0.85f, Pal.PanelLight, 6);
        g.Text("STARS", box2.X + 8, box2.Y + 8, 1f, Pal.LightGrey);
        for (int i = 0; i < StarsPerLevel; i++)
        {
            float x = box2.X + 46 + i * 9, y = box2.CenterY;
            if (i < _collected)
            {
                g.Glow(x, y, 6, Pal.Yellow, 0.6f);
                g.Circle(x, y, 2.6f, Pal.Yellow);
            }
            else
            {
                g.Circle(x, y, 2.2f, Pal.DarkGrey);
            }
        }
    }

    private static void DrawWell(Gfx g, Vector2 c, float rh, float time, float s, float gulp)
    {
        // Gravity ripples falling inward.
        for (int i = 0; i < 6; i++)
        {
            float r = Backdrops.Mod(i * 45 - time * 22, 270) * s + rh * 2;
            float a = MathF.Min(1, r / (60 * s)) * (1 - r / (300 * s));
            if (a > 0)
                g.Ring(c.X, c.Y, r, 1f * s, Pal.Add(Pal.Purple, 0.25f * a));
        }
        g.Glow(c, rh * 9, Pal.Purple, 0.35f);
        g.Glow(c, rh * 5.5f, Pal.Orange, 0.3f + gulp * 0.4f);
        g.Glow(c, rh * 2.8f, Pal.Gold, 0.45f + gulp * 0.5f);

        // Back half of the disc (above the hole).
        DrawBands(g, c, rh, false);
        DrawDisc(g, c, rh, time, false);

        // Lensed light bending over the top.
        g.Arc(c.X, c.Y - rh * 0.1f, rh * 1.45f, rh * 0.32f, MathF.PI * 1.05f, MathF.PI * 1.95f, Pal.Add(Pal.Orange, 0.7f));
        g.Arc(c.X, c.Y - rh * 0.1f, rh * 1.4f, rh * 0.14f, MathF.PI * 1.1f, MathF.PI * 1.9f, Pal.Add(Pal.Yellow, 0.7f));
        g.Arc(c.X, c.Y + rh * 0.1f, rh * 1.35f, rh * 0.16f, MathF.PI * 0.15f, MathF.PI * 0.85f, Pal.Add(Pal.Orange, 0.35f));

        // The hole and its photon ring.
        g.Circle(c.X, c.Y, rh * 1.12f, Pal.Add(Pal.White, 0.9f));
        g.Circle(c.X, c.Y, rh, Pal.Black);
        g.Ring(c.X, c.Y, rh * 1.06f, MathF.Max(1, rh * 0.08f), Pal.Add(Pal.Gold, 0.8f), 48);

        DrawBands(g, c, rh, true);
        DrawDisc(g, c, rh, time, true);
    }

    private const float Tilt = 0.3f;

    /// <summary>Smooth glowing bands of the disc: half ellipses (front = the lower half).</summary>
    private static void DrawBands(Gfx g, Vector2 c, float rh, bool front)
    {
        const int seg = 28;
        for (int b = 0; b < 5; b++)
        {
            float rf = 1.6f + b * 0.75f;
            float r = rf * rh;
            float heat = 1 - b / 5f;
            var col = Pal.Lerp(Pal.Red, Pal.Gold, heat);
            float w = rh * (0.55f - b * 0.06f);
            float a0 = front ? 0 : MathF.PI;
            var prev = Vector2.Zero;
            for (int i = 0; i <= seg; i++)
            {
                float a = a0 + MathF.PI * i / seg;
                var p = new Vector2(c.X + MathF.Cos(a) * r, c.Y + MathF.Sin(a) * r * Tilt);
                if (i > 0)
                {
                    float beam = 0.6f + 0.4f * MathF.Cos(a - 0.05f);
                    g.Line(prev, p, w * 2.2f, Pal.Add(col, 0.08f * beam));
                    g.Line(prev, p, w, Pal.Add(col, 0.16f * beam));
                }
                prev = p;
            }
        }
    }

    private static void DrawDisc(Gfx g, Vector2 c, float rh, float time, bool front)
    {
        const float tilt = Tilt;
        for (int i = 0; i < DiscCount; i++)
        {
            float rf = DiscR[i];
            float omega = 2.2f / MathF.Pow(rf, 1.5f);
            float a = DiscA[i] - time * omega;
            float sin = MathF.Sin(a);
            if ((sin > 0) != front)
                continue;
            float r = rf * rh;
            float x = c.X + MathF.Cos(a) * r, y = c.Y + sin * r * tilt;
            // Hidden behind the hole?
            if (!front && MathF.Abs(x - c.X) < rh && MathF.Abs(y - c.Y) < rh)
                continue;
            float heat = DiscHeat[i];
            var col = heat > 0.75f ? Pal.Lerp(Pal.Gold, Pal.White, (heat - 0.75f) * 4) : Pal.Lerp(Pal.Red, Pal.Gold, heat / 0.75f);
            float size = (0.8f + heat * 1.3f) * MathF.Max(0.7f, rh / 14f);
            // Doppler beaming: the side coming towards us is brighter.
            float beam = 0.55f + 0.45f * MathF.Cos(a);
            g.Glow(x, y, size * 4, col, 0.25f * beam);
            g.Circle(x, y, size * 0.7f, Pal.Add(col, 0.6f + 0.4f * beam));
        }
    }

    private static void DrawStar(Gfx g, Vector2 p, float s, float t, float appear)
    {
        float pulse = 0.8f + 0.2f * MathF.Sin(t * 6);
        float k = 7 * s * pulse * appear;
        g.Glow(p, 18 * s * appear, Pal.Yellow, 0.6f);
        g.RotatedRect(p, k * 2.4f, 1.6f * s, t * 0.8f, Pal.Add(Pal.White, 0.9f));
        g.RotatedRect(p, k * 2.4f, 1.6f * s, t * 0.8f + MathF.PI / 2, Pal.Add(Pal.White, 0.9f));
        g.Circle(p.X, p.Y, 2.6f * s * appear, Pal.White);
    }

    private static void DrawRock(Gfx g, Vector2 p, float r, float angle, Vector2[] shape)
    {
        g.Glow(p, r * 2, Pal.Orange, 0.15f);
        g.Shape(shape, p, angle, r, new Color(90, 75, 70));
        g.Shape(shape, p + new Vector2(-r * 0.15f, -r * 0.15f), angle, r * 0.7f, new Color(130, 110, 100));
        g.Circle(p.X + MathF.Cos(angle) * r * 0.3f, p.Y + MathF.Sin(angle) * r * 0.3f, r * 0.18f, new Color(70, 58, 55));
    }

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        Backdrops.Space(g, time, 3, 9, r);
        float s = r.H / 70f;
        var c = new Vector2(r.CenterX, r.CenterY + 2 * s);
        DrawWell(g, c, 9 * s, time, s, 0);
        // A ship slinging round in an ellipse.
        float a = time * 1.4f;
        var p = c + new Vector2(MathF.Cos(a) * 52 * s, MathF.Sin(a) * 24 * s);
        var v = new Vector2(-MathF.Sin(a) * 52, MathF.Cos(a) * 24);
        float ang = MathF.Atan2(v.Y, v.X);
        for (int i = 1; i < 14; i++)
        {
            float b = a - i * 0.07f;
            g.Circle(c.X + MathF.Cos(b) * 52 * s, c.Y + MathF.Sin(b) * 24 * s, 1.2f * s, Pal.Sky * (0.6f * (1 - i / 14f)));
        }
        g.Glow(p - MathF2.FromAngle(ang, 5 * s), 8 * s, Pal.Orange, 0.8f);
        g.Shape(ShipShape, p, ang, s * 0.75f, new Color(30, 60, 90));
        g.ShapeOutline(ShipShape, p, ang, s * 0.75f, 1f * s, Pal.Cyan, true);
        DrawStar(g, new Vector2(r.X + r.W * 0.18f, r.Y + r.H * 0.3f), s * 0.8f, time, 1);
        DrawStar(g, new Vector2(r.X + r.W * 0.82f, r.Y + r.H * 0.72f), s * 0.8f, time + 2, 1);
    }

    // ------------------------------------------------------------------ autoplay

    public override void AutoPlay(Controls c)
    {
        if (_dying > 0)
            return;
        var toHole = Hole - _pos;
        float dist = toHole.Length();
        var radial = toHole / MathF.Max(dist, 0.1f);
        var tangent = new Vector2(-radial.Y, radial.X);

        // Desired velocity: towards the nearest star, plus orbit bias and avoidance.
        Vector2 target = Hole + new Vector2(140, 0);
        float best = float.MaxValue;
        foreach (var st in _stars)
        {
            float d = Vector2.Distance(st.Pos, _pos) + (Vector2.Distance(st.Pos, Hole) < _horizon * 3 ? 150 : 0);
            if (d < best)
            {
                best = d;
                target = st.Pos;
            }
        }
        var want = target - _pos;
        float wl = want.Length();
        var vDes = wl > 0.1f ? want / wl * MathF.Min(95, 30 + wl * 0.9f) : Vector2.Zero;

        // Will the current path dive too close? Then raise the orbit.
        float minD = dist;
        for (int i = 0; i < _predictCount; i++)
            minD = MathF.Min(minD, Vector2.Distance(_predict[i], Hole));
        float safe = _horizon * 3.4f;
        if (minD < safe || dist < safe * 1.2f)
        {
            float spin = MathF.Sign(Vector2.Dot(_vel, tangent));
            if (spin == 0)
                spin = 1;
            vDes = tangent * spin * MathF.Sqrt(_gm / MathF.Max(dist, 20)) * 1.05f - radial * 40;
        }
        foreach (var r in _rocks)
        {
            var d = _pos - r.Pos;
            float l = d.Length();
            if (l < 55 && l > 0.1f)
                vDes += d / l * 90;
        }

        var aReq = (vDes - _vel) * 2f - GravityAt(_pos);
        float face = MathF.Atan2(aReq.Y, aReq.X);
        float diff = MathF2.WrapAngle(face - _angle);
        c.SetDirections(MathF2.Clamp(diff * 3, -1, 1), 0);
        bool thrust = MathF.Abs(diff) < 0.5f && aReq.Length() > 25;
        c.Fire = thrust;
        c.FirePressed = thrust && Tick % 20 == 0;
    }
}
