using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 30 Phaser: a neon vector rock-blaster. Rotate, thrust and fire phaser bolts in a wrap-around
/// field; big rocks split into medium and small ones, saucers shoot back, and hyperspace gets you
/// out of a tight spot.
/// </summary>
public sealed class Phaser : MiniGame, Capture.ICaptureHints
{
    public override int Number => 30;
    public override string Title => "Phaser";
    public override Category Category => Category.Shooter;
    public override string Tagline => "Blast the drifting rocks to dust in a neon vector storm.";
    public override Color Accent => Pal.Cyan;

    public override string[] HowToPlay =>
    [
        "Shoot the rocks: big ones split into medium, then small. Clear the field for the next, busier wave.",
        "Saucers shoot back and the small ones aim. Rocks score 20, 50 and 100; saucers 200 and 1000.",
        "An extra ship every 10,000. Hyperspace when cornered!",
    ];

    public override string[] DesktopControls => ["LEFT / RIGHT turn, UP thrust.", "SPACE fire, X or DOWN hyperspace."];
    public override string[] TouchControls => ["Stick turns, hold THRUST to fly.", "FIRE shoots. Tap the sky to warp."];
    public override Pad Pad => Pad.Horizontal | Pad.Fire | Pad.Alt;
    public override string AltLabel => "THRUST";

    public int CaptureTicks => 330;

    private const float FieldTop = Screen.HudHeight, FieldW = Screen.Width, FieldH = Screen.Height - Screen.HudHeight;
    private const float BulletSpeed = 400, BulletLife = 0.85f;
    private const int MaxBullets = 6;

    private sealed class Rock
    {
        public Vector2 Pos, Vel;
        public float Angle, Spin, R;
        public int Size;
        public float[] Radii;
    }

    private struct Bolt
    {
        public Vector2 Pos, Vel;
        public float Life;
        public bool Enemy;
    }

    private struct Debris
    {
        public Vector2 Pos, Vel;
        public float Angle, Spin, Len, Life;
        public Color Color;
    }

    private static readonly Vector2[] ShipShape = [new(13, 0), new(-8, -8), new(-4, 0), new(-8, 8)];

    private readonly List<Rock> _rocks = new();
    private readonly List<Bolt> _bolts = new();
    private readonly List<Debris> _debris = new();
    private readonly Vector2[] _pts = new Vector2[16];

    private Vector2 _pos, _vel;
    private float _angle;
    private bool _alive, _thrusting;
    private float _respawn, _invuln, _hyper, _fireCool;
    private int _nextLife;
    private float _nextWave, _waveTime, _banner;
    private float _beatTimer;
    private int _beat;

    private bool _saucer, _saucerSmall;
    private Vector2 _saucerPos, _saucerVel;
    private float _saucerTimer, _saucerFire, _saucerTurn;

    private float _autoHyperCool;

    protected override void Start()
    {
        Lives = 3;
        Level = 0;
        _nextLife = 10000;
        _rocks.Clear();
        _bolts.Clear();
        _debris.Clear();
        _saucer = false;
        _nextWave = 0;
        _beatTimer = 0;
        _fireCool = 0;
        _autoHyperCool = 0;
        SpawnShip();
        _invuln = 1f;
        NewWave();
    }

    private void SpawnShip()
    {
        _alive = true;
        _pos = new Vector2(FieldW / 2, FieldTop + FieldH / 2);
        _vel = Vector2.Zero;
        _angle = -MathF.PI / 2;
        _invuln = 2.5f;
        _hyper = 0;
    }

    private void NewWave()
    {
        Level++;
        _waveTime = 0;
        _banner = 2f;
        int count = Math.Min(5 + (Level - 1), 11);
        for (int i = 0; i < count; i++)
        {
            // Start around the edges, away from the ship.
            Vector2 p;
            int tries = 0;
            do
            {
                p = new Vector2(Rand(0, FieldW), FieldTop + Rand(0, FieldH));
                tries++;
            }
            while (Delta(_pos, p).Length() < 150 && tries < 40);
            AddRock(p, 3, Rand(0, MathF2.Tau));
        }
        _saucerTimer = MathF.Max(7, 16 - Level * 1.2f) + Rand(0, 4);
        if (Level > 1)
            Sound.Play(Sfx.LevelUp);
    }

    private void AddRock(Vector2 pos, int size, float dir, Vector2 baseVel = default)
    {
        float speed = size switch
        {
            3 => Rand(22, 42),
            2 => Rand(45, 75),
            _ => Rand(70, 115),
        } * (1 + MathF.Min(Level - 1, 8) * 0.06f);
        float r = size switch { 3 => 30, 2 => 16, _ => 8.5f };
        int n = size == 1 ? 8 : size == 2 ? 10 : 13;
        var radii = new float[n];
        for (int i = 0; i < n; i++)
            radii[i] = r * Rand(0.72f, 1.08f);
        _rocks.Add(new Rock
        {
            Pos = pos, Vel = baseVel * 0.4f + MathF2.FromAngle(dir, speed), Angle = Rand(0, MathF2.Tau),
            Spin = Rand(-1.2f, 1.2f) * (4 - size) * 0.6f, R = r, Size = size, Radii = radii,
        });
    }

    // ------------------------------------------------------------------ wrap helpers

    private static Vector2 Wrap(Vector2 p) =>
        new(Backdrops.Mod(p.X, FieldW), FieldTop + Backdrops.Mod(p.Y - FieldTop, FieldH));

    /// <summary>Shortest vector from a to b across the wrap-around field.</summary>
    private static Vector2 Delta(Vector2 a, Vector2 b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        if (dx > FieldW / 2) dx -= FieldW;
        else if (dx < -FieldW / 2) dx += FieldW;
        if (dy > FieldH / 2) dy -= FieldH;
        else if (dy < -FieldH / 2) dy += FieldH;
        return new Vector2(dx, dy);
    }

    private static bool Touching(Vector2 a, Vector2 b, float r) => Delta(a, b).LengthSquared() < r * r;

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        _waveTime += Dt;
        if (_banner > 0)
            _banner -= Dt;
        _autoHyperCool -= Dt;

        UpdateShip();
        UpdateBolts();
        UpdateRocks();
        UpdateSaucer();
        UpdateDebris();
        if (IsOver)
            return;

        // The heartbeat quickens as the wave goes on.
        if (_rocks.Count > 0 && _alive)
        {
            float interval = MathF2.Lerp(0.95f, 0.26f, MathF2.Clamp(_waveTime / 45f, 0, 1));
            _beatTimer -= Dt;
            if (_beatTimer <= 0)
            {
                _beatTimer = interval;
                _beat ^= 1;
                Sound.Play(Sfx.Thud, _beat == 0 ? -0.75f : -0.9f, 0.45f);
            }
        }

        if (_rocks.Count == 0 && !_saucer)
        {
            if (_nextWave <= 0)
                _nextWave = 2f;
            _nextWave -= Dt;
            if (_nextWave <= 0.01f)
            {
                _nextWave = 0;
                NewWave();
            }
        }

        while (Score >= _nextLife)
        {
            _nextLife += 10000;
            Lives++;
            Sound.Play(Sfx.Bonus);
            Fx.Float("EXTRA SHIP!", 320, 60, Pal.Lime, 2f);
        }
    }

    private void UpdateShip()
    {
        _thrusting = false;
        if (!_alive)
        {
            _respawn -= Dt;
            if (_respawn <= 0 && (CentreClear() || _respawn < -3))
                SpawnShip();
            return;
        }
        if (_invuln > 0)
            _invuln -= Dt;
        if (_fireCool > 0)
            _fireCool -= Dt;
        if (_hyper > 0)
        {
            _hyper -= Dt;
            if (_hyper <= 0)
            {
                Fx.Burst(_pos.X, _pos.Y, Pal.Cyan, 26, 140, 0.5f, 2f);
                Sound.Play(Sfx.Warp, 0.5f, 0.6f);
                _invuln = MathF.Max(_invuln, 0.4f);
            }
            return;
        }

        _angle += In.AxisX * 4.6f * Dt;
        _thrusting = IsTouch ? In.Alt : In.Up;
        if (_thrusting)
        {
            _vel += MathF2.FromAngle(_angle, 250 * Dt);
            var back = _pos - MathF2.FromAngle(_angle, 6);
            var ev = -MathF2.FromAngle(_angle + Rand(-0.35f, 0.35f), Rand(80, 150)) + _vel;
            Fx.Spark(back.X, back.Y, ev.X, ev.Y, Chance(0.5f) ? Pal.Orange : Pal.Yellow, 0.25f, 1.6f);
            Sound.Loop(LoopSfx.Thrust, true, 0, 0.55f);
        }
        _vel *= 1 - 0.45f * Dt;
        if (_vel.Length() > 290)
            _vel = Vector2.Normalize(_vel) * 290;
        _pos = Wrap(_pos + _vel * Dt);

        int mine = 0;
        foreach (var b in _bolts)
            if (!b.Enemy)
                mine++;
        bool fire = In.FirePressed || (In.Fire && _fireCool <= 0);
        if (fire && mine < MaxBullets && _fireCool <= 0.08f)
        {
            var nose = _pos + MathF2.FromAngle(_angle, 13);
            _bolts.Add(new Bolt { Pos = nose, Vel = _vel + MathF2.FromAngle(_angle, BulletSpeed), Life = BulletLife });
            _fireCool = 0.2f;
            Sound.Play(Sfx.Laser, Rand(0.1f, 0.35f), 0.5f);
            Fx.Spark(nose.X, nose.Y, 0, 0, Pal.Cyan, 0.12f, 3f);
        }

        bool hyper = IsTouch ? In.PointerPressed : In.AltPressed || In.DownPressed;
        if (hyper)
        {
            Fx.Burst(_pos.X, _pos.Y, Pal.Cyan, 26, 160, 0.45f, 2f);
            Sound.Play(Sfx.Warp, -0.3f, 0.7f);
            _pos = new Vector2(Rand(30, FieldW - 30), FieldTop + Rand(30, FieldH - 30));
            _vel = Vector2.Zero;
            _hyper = 0.55f;
        }
    }

    private bool CentreClear()
    {
        var c = new Vector2(FieldW / 2, FieldTop + FieldH / 2);
        foreach (var r in _rocks)
            if (Touching(c, r.Pos, r.R + 70))
                return false;
        return !_saucer || !Touching(c, _saucerPos, 90);
    }

    private void UpdateBolts()
    {
        for (int i = _bolts.Count - 1; i >= 0; i--)
        {
            var b = _bolts[i];
            b.Pos = Wrap(b.Pos + b.Vel * Dt);
            b.Life -= Dt;
            bool dead = b.Life <= 0;

            if (!dead && !b.Enemy && _saucer && Touching(b.Pos, _saucerPos, _saucerSmall ? 10 : 16))
            {
                int points = _saucerSmall ? 1000 : 200;
                AddScore(points, _saucerPos.X, _saucerPos.Y - 10, Pal.Magenta);
                KillSaucer();
                dead = true;
            }
            if (!dead && b.Enemy && _alive && _hyper <= 0 && _invuln <= 0 && Touching(b.Pos, _pos, 9))
            {
                KillShip();
                dead = true;
            }
            if (!dead)
                for (int j = _rocks.Count - 1; j >= 0; j--)
                    if (Touching(b.Pos, _rocks[j].Pos, _rocks[j].R * 0.95f + 1))
                    {
                        BreakRock(j, !b.Enemy, b.Vel);
                        dead = true;
                        break;
                    }
            if (dead)
                _bolts.RemoveAt(i);
            else
                _bolts[i] = b;
            if (IsOver)
                return;
        }
    }

    private void BreakRock(int index, bool scores, Vector2 hitVel)
    {
        var r = _rocks[index];
        _rocks.RemoveAt(index);
        var col = RockColour(r.Size);
        if (scores)
            AddScore(r.Size == 3 ? 20 : r.Size == 2 ? 50 : 100, r.Pos.X, r.Pos.Y - r.R, col);
        Fx.Burst(r.Pos.X, r.Pos.Y, col, 6 + r.Size * 8, 50 + r.Size * 35, 0.7f, 1.8f);
        Fx.Burst(r.Pos.X, r.Pos.Y, Pal.White, 4 + r.Size * 2, 60, 0.3f, 1.5f);
        Fx.Shake(r.Size, 0.15f);
        for (int i = 0; i < r.Size * 3; i++)
            AddDebris(r.Pos, r.R * 0.5f, col, r.Vel);
        Sound.Play(r.Size == 3 ? Sfx.BigExplode : Sfx.Explode, r.Size == 1 ? 0.4f : r.Size == 2 ? 0 : -0.3f, 0.7f);
        if (r.Size > 1 && _rocks.Count < 34)
        {
            float baseDir = MathF2.Angle(hitVel == Vector2.Zero ? r.Vel : hitVel);
            AddRock(r.Pos, r.Size - 1, baseDir + Rand(0.4f, 1.3f), r.Vel);
            AddRock(r.Pos, r.Size - 1, baseDir - Rand(0.4f, 1.3f), r.Vel);
        }
    }

    private void UpdateRocks()
    {
        foreach (var r in _rocks)
        {
            r.Pos = Wrap(r.Pos + r.Vel * Dt);
            r.Angle += r.Spin * Dt;
        }
        if (!_alive || _hyper > 0 || _invuln > 0)
            return;
        for (int i = _rocks.Count - 1; i >= 0; i--)
            if (Touching(_pos, _rocks[i].Pos, _rocks[i].R * 0.85f + 7))
            {
                BreakRock(i, true, _vel);
                KillShip();
                return;
            }
    }

    private void UpdateSaucer()
    {
        if (!_saucer)
        {
            if (_rocks.Count == 0)
                return;
            _saucerTimer -= Dt;
            if (_saucerTimer > 0)
                return;
            _saucer = true;
            _saucerSmall = Score > 12000 || Chance(0.15f + Level * 0.07f);
            bool fromLeft = Chance(0.5f);
            _saucerPos = new Vector2(fromLeft ? -20 : FieldW + 20, FieldTop + Rand(30, FieldH - 30));
            _saucerVel = new Vector2((fromLeft ? 1 : -1) * (_saucerSmall ? 95 : 70), 0);
            _saucerFire = 1f;
            _saucerTurn = 1.2f;
            return;
        }

        _saucerPos.X += _saucerVel.X * Dt;
        _saucerPos.Y = FieldTop + Backdrops.Mod(_saucerPos.Y + _saucerVel.Y * Dt - FieldTop, FieldH);
        Sound.Loop(LoopSfx.Hum, true, (_saucerSmall ? 0.6f : 0.1f) + 0.25f * MathF.Sin(Time * 14), 0.45f);
        _saucerTurn -= Dt;
        if (_saucerTurn <= 0)
        {
            _saucerTurn = Rand(0.8f, 1.8f);
            _saucerVel.Y = Pick(-55f, 0f, 55f);
        }
        if (_saucerPos.X < -40 || _saucerPos.X > FieldW + 40)
        {
            _saucer = false;
            _saucerTimer = MathF.Max(6, 15 - Level) + Rand(0, 5);
            return;
        }

        _saucerFire -= Dt;
        if (_saucerFire <= 0 && _saucerPos.X > 0 && _saucerPos.X < FieldW)
        {
            _saucerFire = _saucerSmall ? MathF.Max(0.6f, 1.1f - Level * 0.05f) : 1.3f;
            float dir;
            if (_saucerSmall && _alive)
            {
                float err = MathF.Max(0.04f, 0.4f - Level * 0.04f - Score / 100000f);
                dir = MathF2.Angle(Delta(_saucerPos, _pos)) + Rand(-err, err);
            }
            else
            {
                dir = Rand(0, MathF2.Tau);
            }
            _bolts.Add(new Bolt { Pos = _saucerPos, Vel = MathF2.FromAngle(dir, 210), Life = 1.4f, Enemy = true });
            Sound.Play(Sfx.Zap, _saucerSmall ? 0.5f : 0.1f, 0.4f);
        }

        for (int i = _rocks.Count - 1; i >= 0; i--)
            if (Touching(_saucerPos, _rocks[i].Pos, _rocks[i].R + 10))
            {
                BreakRock(i, false, _saucerVel);
                KillSaucer();
                return;
            }
        if (_alive && _hyper <= 0 && _invuln <= 0 && Touching(_saucerPos, _pos, _saucerSmall ? 16 : 22))
        {
            AddScore(_saucerSmall ? 1000 : 200, _saucerPos.X, _saucerPos.Y - 10, Pal.Magenta);
            KillSaucer();
            KillShip();
        }
    }

    private void KillSaucer()
    {
        _saucer = false;
        _saucerTimer = MathF.Max(6, 15 - Level) + Rand(0, 5);
        Fx.Explode(_saucerPos.X, _saucerPos.Y, 1.2f);
        Fx.Burst(_saucerPos.X, _saucerPos.Y, Pal.Magenta, 24, 150, 0.7f, 2f);
        for (int i = 0; i < 8; i++)
            AddDebris(_saucerPos, 6, Pal.Magenta, _saucerVel * 0.3f);
        Sound.Play(Sfx.BigExplode, 0.2f);
    }

    private void KillShip()
    {
        if (!_alive)
            return;
        _alive = false;
        // The hull flies apart into glowing struts.
        for (int i = 0; i < ShipShape.Length; i++)
        {
            var a = Rotate(ShipShape[i]);
            var b = Rotate(ShipShape[(i + 1) % ShipShape.Length]);
            var mid = _pos + (a + b) / 2;
            _debris.Add(new Debris
            {
                Pos = mid, Vel = _vel * 0.4f + Vector2.Normalize((a + b) / 2 + new Vector2(0.01f, 0)) * Rand(30, 70),
                Angle = MathF2.Angle(b - a), Spin = Rand(-4, 4), Len = (b - a).Length(), Life = 2f, Color = Pal.White,
            });
        }
        Fx.Explode(_pos.X, _pos.Y, 1.5f);
        Fx.Burst(_pos.X, _pos.Y, Pal.Cyan, 30, 180, 0.9f, 2f);
        Sound.Play(Sfx.BigExplode, -0.4f);
        _respawn = 2f;
        LoseLife();
    }

    private Vector2 Rotate(Vector2 p)
    {
        float c = MathF.Cos(_angle), s = MathF.Sin(_angle);
        return new Vector2(p.X * c - p.Y * s, p.X * s + p.Y * c);
    }

    private void AddDebris(Vector2 at, float spread, Color col, Vector2 baseVel)
    {
        if (_debris.Count > 120)
            return;
        _debris.Add(new Debris
        {
            Pos = at + new Vector2(Rand(-spread, spread), Rand(-spread, spread)),
            Vel = baseVel * 0.5f + MathF2.FromAngle(Rand(0, MathF2.Tau), Rand(30, 110)),
            Angle = Rand(0, MathF2.Tau), Spin = Rand(-6, 6), Len = Rand(3, 7), Life = Rand(0.5f, 1.1f), Color = col,
        });
    }

    private void UpdateDebris()
    {
        for (int i = _debris.Count - 1; i >= 0; i--)
        {
            var d = _debris[i];
            d.Life -= Dt;
            if (d.Life <= 0)
            {
                _debris.RemoveAt(i);
                continue;
            }
            d.Pos += d.Vel * Dt;
            d.Vel *= 1 - 0.8f * Dt;
            d.Angle += d.Spin * Dt;
            _debris[i] = d;
        }
    }

    private static Color RockColour(int size) => size switch
    {
        3 => Pal.Sky,
        2 => Pal.Magenta,
        _ => Pal.Lime,
    };

    // ------------------------------------------------------------------ drawing

    public override void Draw(Gfx g)
    {
        Backdrops.Space(g, Time, 3, 30, Screen.Bounds);
        g.Glow(140, 270, 230, Pal.Purple, 0.16f);
        g.Glow(520, 110, 200, Pal.Teal, 0.14f);
        Backdrops.Grid(g, Screen.Play, 40, new Color(20, 60, 90) * 0.18f);

        foreach (var r in _rocks)
            DrawWrapped(g, r.Pos, r.R + 4, 0, r);

        foreach (var d in _debris)
        {
            var dir = MathF2.FromAngle(d.Angle, d.Len / 2);
            float a = MathF.Min(1, d.Life * 1.5f);
            g.GlowLine(d.Pos - dir, d.Pos + dir, 1.3f, d.Color * a);
        }

        if (_saucer)
            DrawWrapped(g, _saucerPos, 24, 1, null);

        foreach (var b in _bolts)
        {
            var col = b.Enemy ? Pal.Orange : Pal.Cyan;
            var tail = b.Pos - Vector2.Normalize(b.Vel) * 7;
            g.Glow(b.Pos, 9, col, 0.8f);
            g.GlowLine(tail, b.Pos, 1.6f, col);
            g.Circle(b.Pos, 1.6f, Pal.White);
        }

        if (_alive)
        {
            if (_hyper > 0)
            {
                float k = _hyper / 0.55f;
                g.Ring(_pos.X, _pos.Y, 4 + k * 30, 1.5f, Pal.Cyan * (1 - k));
                g.Glow(_pos, 30 * (1 - k) + 4, Pal.Cyan, 0.5f);
            }
            else if (_invuln <= 0 || (int)(_invuln * 12) % 2 == 0)
            {
                DrawWrapped(g, _pos, 16, 2, null);
            }
        }

        if (_banner > 0)
        {
            float a = MathF.Min(1, _banner * 1.5f);
            g.TextShadow("WAVE " + Level, 320, 150, 3f, Pal.Cyan * a, Align.Center);
        }
        else if (!_alive && !IsOver && _respawn <= 0)
        {
            g.Text("CLEARING SPACE...", 320, 180, 1.5f, Pal.LightGrey * (0.5f + 0.5f * MathF2.Pulse(Time, 0.8f)), Align.Center);
        }
    }

    /// <summary>Draws an object, plus its copies on the far side when it straddles an edge.</summary>
    private void DrawWrapped(Gfx g, Vector2 pos, float radius, int kind, Rock rock)
    {
        for (int ox = -1; ox <= 1; ox++)
        {
            float x = pos.X + ox * FieldW;
            if (x < -radius || x > FieldW + radius)
                continue;
            for (int oy = -1; oy <= 1; oy++)
            {
                float y = pos.Y + oy * FieldH;
                if (y < FieldTop - radius || y > Screen.Height + radius)
                    continue;
                var p = new Vector2(x, y);
                switch (kind)
                {
                    case 0:
                        DrawRock(g, p, rock);
                        break;
                    case 1:
                        DrawSaucer(g, p, _saucerSmall ? 0.62f : 1f, Time);
                        break;
                    default:
                        DrawShip(g, p, _angle, 1.1f, _thrusting, Time);
                        break;
                }
            }
        }
    }

    private void DrawRock(Gfx g, Vector2 p, Rock r)
    {
        int n = r.Radii.Length;
        for (int i = 0; i < n; i++)
            _pts[i] = p + MathF2.FromAngle(r.Angle + MathF2.Tau * i / n, r.Radii[i]);
        var col = RockColour(r.Size);
        g.Glow(p, r.R * 1.6f, col, 0.12f);
        var fill = Pal.Darken(col, 0.86f);
        for (int i = 0; i < n; i++)
            g.Triangle(p, _pts[i], _pts[(i + 1) % n], fill);
        for (int i = 0; i < n; i++)
            g.GlowLine(_pts[i], _pts[(i + 1) % n], r.Size == 1 ? 1.2f : 1.5f, col);
        // A couple of craters for texture.
        if (r.Size > 1)
        {
            var c1 = p + MathF2.FromAngle(r.Angle + 1, r.R * 0.35f);
            var c2 = p + MathF2.FromAngle(r.Angle + 3.6f, r.R * 0.45f);
            g.Ring(c1.X, c1.Y, r.R * 0.18f, 1f, col * 0.45f, 10);
            g.Ring(c2.X, c2.Y, r.R * 0.12f, 1f, col * 0.35f, 8);
        }
    }

    private static readonly Vector2[] ShipTmp = new Vector2[4];

    private static void DrawShip(Gfx g, Vector2 p, float angle, float scale, bool thrust, float time, float lw = 1.5f)
    {
        float c = MathF.Cos(angle) * scale, s = MathF.Sin(angle) * scale;
        for (int i = 0; i < 4; i++)
            ShipTmp[i] = new Vector2(p.X + ShipShape[i].X * c - ShipShape[i].Y * s, p.Y + ShipShape[i].X * s + ShipShape[i].Y * c);
        g.Glow(p, 22 * scale, Pal.Cyan, 0.25f);
        if (thrust)
        {
            float flick = 0.7f + 0.3f * MathF.Sin(time * 60);
            var tip = p + MathF2.FromAngle(angle + MathF.PI, (10 + 9 * flick) * scale);
            var l = p + MathF2.FromAngle(angle + MathF.PI - 0.5f, 6 * scale);
            var r = p + MathF2.FromAngle(angle + MathF.PI + 0.5f, 6 * scale);
            g.Glow(tip, 12 * scale, Pal.Orange, 0.6f);
            g.GlowLine(l, tip, lw * 0.85f, Pal.Orange);
            g.GlowLine(r, tip, lw * 0.85f, Pal.Yellow);
        }
        var fill = new Color(10, 40, 60);
        g.Triangle(ShipTmp[0], ShipTmp[1], ShipTmp[2], fill);
        g.Triangle(ShipTmp[0], ShipTmp[2], ShipTmp[3], fill);
        for (int i = 0; i < 4; i++)
            g.GlowLine(ShipTmp[i], ShipTmp[(i + 1) % 4], lw, i == 2 || i == 1 ? Pal.Cyan : Pal.White);
        g.Circle(ShipTmp[0], 1.4f * scale, Pal.White);
    }

    private static void DrawSaucer(Gfx g, Vector2 p, float k, float time)
    {
        var col = Pal.Magenta;
        float w = 18 * k, h = 5 * k;
        g.Glow(p, 30 * k, col, 0.35f);
        g.Ellipse(p.X, p.Y, w, h, new Color(40, 10, 40));
        var l = new Vector2(p.X - w, p.Y);
        var r = new Vector2(p.X + w, p.Y);
        var tl = new Vector2(p.X - w * 0.55f, p.Y - h);
        var tr = new Vector2(p.X + w * 0.55f, p.Y - h);
        var bl = new Vector2(p.X - w * 0.55f, p.Y + h);
        var br = new Vector2(p.X + w * 0.55f, p.Y + h);
        var dl = new Vector2(p.X - w * 0.3f, p.Y - h * 2.3f);
        var dr = new Vector2(p.X + w * 0.3f, p.Y - h * 2.3f);
        g.GlowLine(l, r, 1.3f, col);
        g.GlowLine(l, tl, 1.3f, col);
        g.GlowLine(tl, tr, 1.3f, col);
        g.GlowLine(tr, r, 1.3f, col);
        g.GlowLine(l, bl, 1.3f, col);
        g.GlowLine(bl, br, 1.3f, col);
        g.GlowLine(br, r, 1.3f, col);
        g.GlowLine(tl, dl, 1.3f, Pal.Pink);
        g.GlowLine(dl, dr, 1.3f, Pal.Pink);
        g.GlowLine(dr, tr, 1.3f, Pal.Pink);
        for (int i = 0; i < 3; i++)
        {
            float x = p.X + (i - 1) * w * 0.5f;
            bool on = ((int)(time * 8) + i) % 3 == 0;
            g.Circle(x, p.Y + h * 0.1f, 1.3f * k, on ? Pal.Yellow : Pal.White * 0.5f);
        }
    }

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        Backdrops.Space(g, time, 6, 30, r);
        float s = r.H / 70f;
        g.Glow(r.X + r.W * 0.2f, r.Bottom, 90 * s, Pal.Purple, 0.25f);
        var centre = new Vector2(r.CenterX, r.CenterY + 4 * s);

        // Rocks drifting gently around fixed spots.
        for (int i = 0; i < 4; i++)
        {
            float fx = i switch { 0 => 0.2f, 1 => 0.8f, 2 => 0.62f, _ => 0.33f };
            float fy = i switch { 0 => 0.32f, 1 => 0.72f, 2 => 0.2f, _ => 0.8f };
            float x = r.X + r.W * fx + MathF.Sin(time * 0.6f + i * 2) * 8 * s;
            float y = r.Y + r.H * fy + MathF.Cos(time * 0.5f + i) * 5 * s;
            int size = i == 0 ? 3 : i == 1 ? 2 : 1;
            float rad = (size == 3 ? 16 : size == 2 ? 10 : 5.5f) * s;
            var col = RockColour(size);
            var p = new Vector2(x, y);
            g.Glow(p, rad * 1.7f, col, 0.18f);
            const int n = 12;
            Vector2 prev = default, first = default;
            for (int k = 0; k <= n; k++)
            {
                float ang = time * (0.3f + i * 0.15f) * (i % 2 == 0 ? 1 : -1) + MathF2.Tau * k / n;
                float rr = rad * (0.86f + 0.14f * MathF.Sin(k * 2.7f + i * 1.3f));
                var q = k == n ? first : p + MathF2.FromAngle(ang, rr);
                if (k == 0)
                    first = q;
                else
                {
                    g.Triangle(p, prev, q, Pal.Darken(col, 0.85f));
                    g.GlowLine(prev, q, 1.6f, col);
                }
                prev = q;
            }
        }

        float angle = time * 0.9f;
        DrawShip(g, centre, angle, 0.95f * s, true, time, 1.8f);
        // Bolts streaming from the nose.
        for (int k = 0; k < 3; k++)
        {
            float t = Backdrops.Mod(time * 2.2f + k / 3f, 1f);
            float a = angle - k * 0.25f;
            var b = centre + MathF2.FromAngle(a, (18 + t * 70) * s);
            g.Glow(b, 7 * s, Pal.Cyan, 0.9f * (1 - t));
            g.Circle(b, 1.5f * s, Pal.White * (1 - t));
        }
        DrawSaucer(g, new Vector2(r.Right - 26 * s, r.Y + 16 * s + MathF.Sin(time * 2) * 4 * s), 0.7f * s, time);
    }

    // ------------------------------------------------------------------ autopilot

    public override void AutoPlay(Controls c)
    {
        if (!_alive || _hyper > 0)
            return;

        // Pick the nearest target (the saucer counts double).
        Vector2 target = default, targetVel = default;
        float best = float.MaxValue;
        bool danger = false;
        foreach (var r in _rocks)
        {
            var d = Delta(_pos, r.Pos);
            float dist = d.Length() - r.R;
            if (dist < best)
            {
                best = dist;
                target = r.Pos;
                targetVel = r.Vel;
            }
            if (dist < 26 && Vector2.Dot(d, r.Vel - _vel) < 0)
                danger = true;
        }
        if (_saucer && Delta(_pos, _saucerPos).Length() * 0.5f < best)
        {
            best = Delta(_pos, _saucerPos).Length() * 0.5f;
            target = _saucerPos;
            targetVel = _saucerVel;
        }
        foreach (var b in _bolts)
            if (b.Enemy && Delta(_pos, b.Pos).Length() < 28)
                danger = true;

        if (danger && _invuln <= 0 && _autoHyperCool <= 0)
        {
            _autoHyperCool = 3f;
            if (c.IsTouch)
            {
                c.Pointer = new Vector2(320, 200);
                c.PointerPressed = c.PointerDown = true;
            }
            else
            {
                c.AltPressed = c.Alt = true;
            }
            return;
        }
        if (best == float.MaxValue)
        {
            c.SetDirections(0.4f, 0);
            return;
        }

        var to = Delta(_pos, target);
        float t = to.Length() / BulletSpeed;
        var aim = to + (targetVel - _vel) * t;
        float err = MathF2.WrapAngle(MathF2.Angle(aim) - _angle);
        bool thrust = _vel.Length() < 15 && best > 120 && Tick % 200 < 12;
        c.SetDirections(MathF2.Clamp(err * 3f, -1, 1), !c.IsTouch && thrust ? -1 : 0);
        if (c.IsTouch)
            c.Alt = thrust;
        if (MathF.Abs(err) < 0.14f && best < 280)
        {
            c.Fire = true;
            c.FirePressed = Tick % 7 == 0;
        }
    }
}
