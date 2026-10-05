using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 10 Force Field: guard a reactor core with a rotating arc of force. Missiles, comets and alien
/// darts fly in from every side; deflect them back out, ideally into the saucers that fired them.
/// </summary>
public sealed class ForceField : MiniGame, Capture.ICaptureHints
{
    public override int Number => 10;
    public override string Title => "Force Field";
    public override Category Category => Category.Shooter;
    public override string Tagline => "Swing the force field round the reactor and bat the missiles away.";
    public override Color Accent => Pal.Cyan;
    public override Pad Pad => Pad.Horizontal;

    public override string[] HowToPlay =>
    [
        "Turn the force field to deflect missiles, comets and darts before they reach the reactor.",
        "Bounced shots fly back out: hit saucers or other shots for big bonuses.",
        "Touch gold orbs to widen the field, green to repair, white for a nova.",
    ];

    public override string[] DesktopControls => ["LEFT / RIGHT to turn the field,", "or point with the mouse."];
    public override string[] TouchControls => ["Use the stick to turn the field,", "or touch where it should face."];

    public int CaptureTicks => 1800;

    private static readonly Vector2 Core = new(320, 191);
    private const float CoreR = 20, ShieldR = 56;
    private const float BaseHalf = MathF.PI / 4, WideHalf = MathF.PI * 0.45f;

    private enum Kind { Missile, Comet, Dart, Orb }

    private enum Orb { Wide, Repair, Nova }

    private sealed class Shot
    {
        public Kind Kind;
        public Orb Orb;
        public Vector2 Pos, Vel;
        public bool Deflected;
        public float Age, Spin, Radial, Angle, Omega, Rs;
        public bool Dead;
    }

    private sealed class Saucer
    {
        public float Angle, Radius, Omega, FireTimer, Age, Flash;
        public bool Leaving, Dead;
        public Vector2 Pos => Core + MathF2.FromAngle(Angle, Radius);
    }

    private static readonly Vector2[] MissileShape = [new(8, 0), new(2, -3), new(-6, -3), new(-6, 3), new(2, 3)];
    private static readonly Vector2[] FinShape = [new(-3, 0), new(-8, -5), new(-8, 5)];
    private static readonly Vector2[] DartShape = [new(9, 0), new(-6, -6), new(-2, 0), new(-6, 6)];

    private static readonly Dictionary<char, Color> SaucerColours = new()
    {
        ['g'] = new Color(120, 255, 140), ['G'] = new Color(40, 160, 70), ['w'] = Pal.White, ['c'] = Pal.Cyan,
        ['d'] = new Color(30, 70, 50), ['y'] = Pal.Yellow,
    };

    private static readonly PixelArt SaucerArt = new(
    [
        ".....cccc.....",
        "....cwwwcc....",
        "...cccccccc...",
        ".GGGGGGGGGGGG.",
        "GgygGgygGgygGG",
        ".GGGGGGGGGGGG.",
        "...dd....dd...",
    ], SaucerColours);

    private readonly List<Shot> _shots = new();
    private readonly List<Saucer> _saucers = new();
    private float _angle = -MathF.PI / 2, _angVel, _aim;
    private bool _pointerAim;
    private float _half = BaseHalf, _wideTimer;
    private float _integrity;
    private int _wave, _toSpawn;
    private float _spawnTimer, _waveBanner, _waveGap;
    private float _shieldFlash, _coreFlash, _novaRing = -1;
    private int _combo;
    private float _comboTimer;
    private float _orbTimer;
    private int _deflects;

    protected override void Start()
    {
        _integrity = 100;
        _wave = 0;
        NextWave();
    }

    private void NextWave()
    {
        _wave++;
        Level = _wave;
        Status = "WAVE " + _wave;
        _toSpawn = 10 + 5 * _wave;
        _spawnTimer = 2.2f;
        _waveBanner = 2.2f;
        _orbTimer = Rand(6, 10);
        int saucers = Math.Min(3, (_wave + 1) / 2);
        for (int i = 0; i < saucers; i++)
            _saucers.Add(new Saucer
            {
                Angle = Rand(0, MathF2.Tau), Radius = 400, Omega = (Chance(0.5f) ? 1 : -1) * Rand(0.18f, 0.3f),
                FireTimer = Rand(3, 5),
            });
    }

    private float SpeedScale => 1 + 0.08f * (_wave - 1);

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        if (_waveBanner > 0) _waveBanner -= Dt;
        if (_shieldFlash > 0) _shieldFlash -= Dt * 3;
        if (_coreFlash > 0) _coreFlash -= Dt * 2;
        if (_comboTimer > 0 && (_comboTimer -= Dt) <= 0) _combo = 0;
        if (_novaRing >= 0)
        {
            _novaRing += Dt * 520;
            if (_novaRing > 420) _novaRing = -1;
        }

        UpdateShield();
        UpdateSpawning();
        UpdateSaucers();
        UpdateShots();
        Sound.Loop(LoopSfx.Hum, true, -0.6f + (1 - _integrity / 100f) * 0.5f, 0.25f);

        if (_integrity <= 0)
        {
            _integrity = 0;
            Fx.Explode(Core.X, Core.Y, 3f);
            Fx.Burst(Core.X, Core.Y, Pal.Cyan, 60, 260, 1.2f, 3);
            Sound.Play(Sfx.BigExplode);
            EndGame(false, "The reactor has gone critical!");
        }
    }

    private void UpdateShield()
    {
        float axis = In.AxisX;
        if (MathF.Abs(axis) > 0.1f)
        {
            _pointerAim = false;
            _angVel = MathF2.Approach(_angVel, axis * 5.2f, 40 * Dt);
        }
        else
        {
            _angVel = MathF2.Approach(_angVel, 0, 40 * Dt);
        }
        if ((In.PointerDown || (In.HasHover && In.PointerMoved)) && Vector2.Distance(In.Pointer, Core) > 8)
        {
            _pointerAim = true;
            _aim = MathF2.Angle(In.Pointer - Core);
        }
        if (_pointerAim && MathF.Abs(axis) <= 0.1f)
        {
            float d = MathF2.WrapAngle(_aim - _angle);
            float step = 11 * Dt;
            _angle += MathF.Abs(d) < step ? d : MathF.Sign(d) * step;
            _angVel = 0;
        }
        else
        {
            _angle += _angVel * Dt;
        }
        _angle = MathF2.WrapAngle(_angle);

        if (_wideTimer > 0)
        {
            int before = (int)(_wideTimer * 4);
            _wideTimer -= Dt;
            if (_wideTimer < 2 && (int)(_wideTimer * 4) != before)
                Sound.Play(Sfx.Tick, 0.5f, 0.3f);
        }
        _half = MathF2.Approach(_half, _wideTimer > 0 ? WideHalf : BaseHalf, 1.5f * Dt);
    }

    private void UpdateSpawning()
    {
        if (_waveGap > 0)
        {
            _waveGap -= Dt;
            if (_waveGap <= 0)
                NextWave();
            return;
        }
        if (_toSpawn > 0)
        {
            _spawnTimer -= Dt;
            if (_spawnTimer <= 0)
            {
                float a = Rand(0, MathF2.Tau);
                SpawnAttacker(a);
                if (_wave >= 2 && _toSpawn > 0 && Chance(MathF.Min(0.5f, 0.08f * _wave)))
                    SpawnAttacker(a + MathF.PI + Rand(-0.3f, 0.3f));
                _spawnTimer = MathF.Max(0.4f, 1.15f - 0.1f * _wave) * Rand(0.7f, 1.3f);
            }
        }
        _orbTimer -= Dt;
        if (_orbTimer <= 0)
        {
            _orbTimer = Rand(9, 14);
            var orb = _integrity < 60 && Chance(0.5f) ? Orb.Repair : Chance(0.25f) ? Orb.Nova : Orb.Wide;
            float a = Rand(0, MathF2.Tau);
            _shots.Add(new Shot { Kind = Kind.Orb, Orb = orb, Pos = EdgePoint(a), Vel = -MathF2.FromAngle(a, 38) });
        }

        bool anyHostile = false;
        foreach (var s in _shots)
            if (!s.Deflected && s.Kind != Kind.Orb) anyHostile = true;
        if (_toSpawn == 0 && !anyHostile && _waveGap <= 0)
        {
            int bonus = (int)_integrity * _wave;
            AddScore(bonus, Core.X, Core.Y - 60, Pal.Cyan);
            Sound.Play(Sfx.LevelUp);
            foreach (var sc in _saucers)
                sc.Leaving = true;
            _waveGap = 2.5f;
        }
    }

    private static Vector2 EdgePoint(float a)
    {
        // Where a ray from the core leaves the playfield (a little outside it).
        var d = MathF2.FromAngle(a);
        float tx = d.X > 0 ? (652 - Core.X) / d.X : d.X < 0 ? (-12 - Core.X) / d.X : float.MaxValue;
        float ty = d.Y > 0 ? (372 - Core.Y) / d.Y : d.Y < 0 ? (Screen.HudHeight - 12 - Core.Y) / d.Y : float.MaxValue;
        return Core + d * MathF.Min(tx, ty);
    }

    private void SpawnAttacker(float a)
    {
        _toSpawn--;
        var kind = Kind.Missile;
        float roll = Rand(0, 1);
        if (_wave >= 3 && roll < 0.25f) kind = Kind.Dart;
        else if (_wave >= 2 && roll < 0.5f) kind = Kind.Comet;
        var s = new Shot { Kind = kind, Pos = EdgePoint(a), Spin = Rand(0, 6) };
        float sp = SpeedScale;
        switch (kind)
        {
            case Kind.Missile:
                s.Vel = -MathF2.FromAngle(a, Rand(62, 80) * sp);
                break;
            case Kind.Comet:
                s.Vel = -MathF2.FromAngle(a + Rand(-0.12f, 0.12f), Rand(105, 125) * sp);
                break;
            case Kind.Dart:
                s.Angle = a;
                s.Radial = Vector2.Distance(s.Pos, Core);
                s.Omega = (Chance(0.5f) ? 1 : -1) * Rand(0.5f, 0.8f);
                s.Rs = 66 * SpeedScale;
                break;
        }
        Schedule(s);
        _shots.Add(s);
        Sound.Play(kind == Kind.Comet ? Sfx.Whoosh : Sfx.Fuse, Rand(-0.2f, 0.2f), 0.25f);
    }

    private static float Eta(Shot s)
    {
        float d = Vector2.Distance(s.Pos, Core) - ShieldR;
        if (s.Kind == Kind.Dart)
            return (s.Radial - ShieldR) / MathF.Max(1, s.Rs);
        return d / MathF.Max(1, s.Vel.Length());
    }

    /// <summary>
    /// Slows a new attacker so that it never arrives so close to another one that the field
    /// could not swing round in time: hard, but always fair.
    /// </summary>
    private void Schedule(Shot s)
    {
        float slack = MathF.Max(0.12f, 0.4f - 0.035f * _wave);
        float a = MathF2.Angle(s.Pos - Core);
        for (int pass = 0; pass < 3; pass++)
        {
            float eta = Eta(s);
            float need = eta;
            foreach (var o in _shots)
            {
                if (o.Deflected || o.Kind == Kind.Orb) continue;
                float sep = MathF.Abs(MathF2.WrapAngle(MathF2.Angle(o.Pos - Core) - a));
                float gap = MathF.Max(0, sep - BaseHalf * 1.4f) / 5f + slack;
                float oe = Eta(o);
                if (MathF.Abs(oe - eta) < gap)
                    need = MathF.Max(need, oe + gap);
            }
            if (need <= eta + 0.001f) return;
            float k = MathF.Max(0.4f, eta / need);
            if (s.Kind == Kind.Dart) s.Rs *= k;
            else s.Vel *= k;
        }
    }

    private void UpdateSaucers()
    {
        foreach (var sc in _saucers)
        {
            sc.Age += Dt;
            if (sc.Flash > 0) sc.Flash -= Dt * 3;
            if (sc.Leaving)
            {
                sc.Radius += 160 * Dt;
                if (sc.Radius > 420) sc.Dead = true;
                continue;
            }
            sc.Radius = MathF2.Approach(sc.Radius, 158, 90 * Dt);
            sc.Angle += sc.Omega * Dt;
            var p = sc.Pos;
            // Keep them on the playfield (the field is wider than tall).
            if (p.Y < 46 || p.Y > 340)
                sc.Radius = MathF2.Approach(sc.Radius, 125, 120 * Dt);
            if (sc.Radius < 170 && _waveGap <= 0)
            {
                sc.FireTimer -= Dt;
                if (sc.FireTimer <= 0)
                {
                    sc.FireTimer = MathF.Max(2.2f, 4.5f - _wave * 0.2f) * Rand(0.8f, 1.2f);
                    var dir = Vector2.Normalize(Core - p);
                    var m = new Shot { Kind = Kind.Missile, Pos = p + dir * 10, Vel = dir * 70 * SpeedScale };
                    Schedule(m);
                    _shots.Add(m);
                    Sound.Play(Sfx.Zap, -0.3f, 0.4f);
                    Fx.Burst(p.X, p.Y, Pal.Lime, 6, 50, 0.3f, 1.5f);
                }
            }
        }
        _saucers.RemoveAll(s => s.Dead);
    }

    private void UpdateShots()
    {
        for (int i = 0; i < _shots.Count; i++)
        {
            var s = _shots[i];
            if (s.Dead) continue;
            s.Age += Dt;
            if (s.Kind == Kind.Dart && !s.Deflected)
            {
                s.Radial -= s.Rs * Dt;
                s.Angle += s.Omega * Dt * MathF.Min(1, s.Radial / 150);
                var np = Core + MathF2.FromAngle(s.Angle, s.Radial);
                s.Vel = (np - s.Pos) / Dt;
                s.Pos = np;
            }
            else
            {
                if (s.Deflected)
                    Steer(s);
                s.Pos += s.Vel * Dt;
            }

            // Trails.
            if (s.Kind == Kind.Missile && Tick % 2 == 0)
                Fx.Spark(s.Pos.X - s.Vel.X * 0.08f, s.Pos.Y - s.Vel.Y * 0.08f, Rand(-8, 8), Rand(-8, 8),
                    s.Deflected ? Pal.Cyan : Pal.Orange, 0.35f, 1.6f);
            else if (s.Kind == Kind.Comet)
                Fx.Spark(s.Pos.X, s.Pos.Y, Rand(-10, 10), Rand(-10, 10), s.Deflected ? Pal.White : Pal.Sky, 0.5f, 2.2f);

            var off = s.Pos - Core;
            float d = off.Length();

            if (!s.Deflected)
            {
                // Force field contact.
                if (d < ShieldR + 6 && d > ShieldR - 8 && Vector2.Dot(s.Vel, off) < 0)
                {
                    float rel = MathF.Abs(MathF2.WrapAngle(MathF2.Angle(off) - _angle));
                    if (rel <= _half + 0.04f)
                    {
                        if (s.Kind == Kind.Orb)
                            Collect(s);
                        else
                            Deflect(s, off / d);
                        continue;
                    }
                }
                if (d < CoreR + 4)
                {
                    if (s.Kind == Kind.Orb)
                    {
                        Fx.Burst(s.Pos.X, s.Pos.Y, OrbColour(s.Orb), 10, 60, 0.4f, 1.5f);
                        Sound.Play(Sfx.Back, 0, 0.4f);
                    }
                    else
                        HitCore(s);
                    s.Dead = true;
                    continue;
                }
            }
            else
            {
                // Deflected shots smash whatever they meet.
                foreach (var o in _shots)
                {
                    if (o == s || o.Dead || o.Deflected || o.Kind == Kind.Orb) continue;
                    if (Vector2.DistanceSquared(o.Pos, s.Pos) < 13 * 13)
                    {
                        o.Dead = s.Dead = true;
                        Bonus(80, o.Pos, ShotColour(o));
                        Fx.Burst(o.Pos.X, o.Pos.Y, ShotColour(o), 20, 140, 0.6f, 2.5f);
                        Sound.Play(Sfx.Explode, Rand(-0.2f, 0.2f), 0.7f);
                        break;
                    }
                }
                if (s.Dead) continue;
                foreach (var sc in _saucers)
                {
                    if (sc.Leaving || sc.Dead) continue;
                    if (Vector2.DistanceSquared(sc.Pos, s.Pos) < 18 * 18)
                    {
                        sc.Dead = s.Dead = true;
                        var p = sc.Pos;
                        Bonus(250, p, Pal.Lime);
                        Fx.Explode(p.X, p.Y, 1.4f);
                        Fx.Burst(p.X, p.Y, Pal.Lime, 24, 160, 0.8f, 2.5f);
                        Sound.Play(Sfx.BigExplode);
                        break;
                    }
                }
                if (s.Dead) continue;
                if (s.Pos.X < -30 || s.Pos.X > 670 || s.Pos.Y < -10 || s.Pos.Y > 390)
                    s.Dead = true;
            }
        }
        _shots.RemoveAll(s => s.Dead);
    }

    private void Steer(Shot s)
    {
        // Bounced shots curve gently towards the nearest saucer ahead of them.
        Saucer best = null;
        float bestD = float.MaxValue;
        var dir = Vector2.Normalize(s.Vel);
        foreach (var sc in _saucers)
        {
            if (sc.Leaving) continue;
            var to = sc.Pos - s.Pos;
            float dist = to.Length();
            if (dist < 1 || Vector2.Dot(to / dist, dir) < 0.6f) continue;
            if (dist < bestD) { bestD = dist; best = sc; }
        }
        if (best == null) return;
        float speed = s.Vel.Length();
        var want = Vector2.Normalize(best.Pos - s.Pos) * speed;
        s.Vel = Vector2.Normalize(Vector2.Lerp(s.Vel, want, 2.5f * Dt)) * speed;
    }

    private void Deflect(Shot s, Vector2 n)
    {
        s.Vel = (s.Vel - 2 * Vector2.Dot(s.Vel, n) * n) * 1.6f;
        if (s.Vel.LengthSquared() < 160 * 160)
            s.Vel = Vector2.Normalize(s.Vel) * 160;
        s.Deflected = true;
        s.Pos = Core + n * (ShieldR + 7);
        _shieldFlash = 1;
        _deflects++;
        _combo++;
        _comboTimer = 2.5f;
        int pts = 10 * Math.Min(_combo, 8);
        AddScore(pts, s.Pos.X, s.Pos.Y - 10, Pal.Cyan);
        Fx.Burst(s.Pos.X, s.Pos.Y, Pal.Cyan, 12, 120, 0.4f, 2f);
        Fx.Burst(s.Pos.X, s.Pos.Y, Pal.White, 5, 60, 0.25f, 1.5f);
        Sound.Play(Sfx.Bounce, MathF.Min(0.8f, -0.2f + _combo * 0.08f), 0.8f);
    }

    private void Bonus(int pts, Vector2 p, Color c)
    {
        AddScore(pts * _wave, p.X, p.Y - 12, c);
        Sound.Play(Sfx.Coin, 0.3f, 0.5f);
    }

    private void Collect(Shot s)
    {
        s.Dead = true;
        var c = OrbColour(s.Orb);
        Fx.Burst(s.Pos.X, s.Pos.Y, c, 24, 140, 0.7f, 2.5f);
        switch (s.Orb)
        {
            case Orb.Wide:
                _wideTimer = 9;
                Fx.Float("WIDE FIELD!", Core.X, Core.Y - 80, Pal.Gold, 2f);
                Sound.Play(Sfx.PowerUp);
                break;
            case Orb.Repair:
                _integrity = MathF.Min(100, _integrity + 30);
                Fx.Float("REPAIRED!", Core.X, Core.Y - 80, Pal.Lime, 2f);
                Sound.Play(Sfx.Pickup);
                break;
            case Orb.Nova:
                _novaRing = 0;
                Fx.Float("NOVA!", Core.X, Core.Y - 80, Pal.White, 2f);
                Sound.Play(Sfx.Warp);
                Fx.Shake(4, 0.4f);
                foreach (var o in _shots)
                    if (!o.Dead && !o.Deflected && o.Kind != Kind.Orb)
                    {
                        o.Dead = true;
                        AddScore(30, o.Pos.X, o.Pos.Y, Pal.White);
                        Fx.Burst(o.Pos.X, o.Pos.Y, ShotColour(o), 16, 120, 0.5f, 2f);
                    }
                break;
        }
        AddScore(50, s.Pos.X, s.Pos.Y);
    }

    private void HitCore(Shot s)
    {
        float dmg = s.Kind switch { Kind.Comet => 16, Kind.Dart => 10, _ => 12 };
        _integrity -= dmg;
        _coreFlash = 1;
        _combo = 0;
        Fx.Explode(s.Pos.X, s.Pos.Y, 1f);
        Fx.Shake(5, 0.3f);
        Sound.Play(Sfx.Hurt);
        if (_integrity > 0 && _integrity < 35)
            Sound.Play(Sfx.Alarm, 0, 0.4f);
    }

    private static Color OrbColour(Orb o) => o switch { Orb.Wide => Pal.Gold, Orb.Repair => Pal.Lime, _ => Pal.White };

    private static Color ShotColour(Shot s) => s.Kind switch
    {
        Kind.Comet => Pal.Sky, Kind.Dart => Pal.Magenta, Kind.Orb => OrbColour(s.Orb), _ => Pal.Orange,
    };

    // ------------------------------------------------------------------ drawing

    public override void Draw(Gfx g)
    {
        float time = Time;
        Backdrops.Space(g, time, 4, 10, Screen.Bounds);
        g.Glow(120, 90, 160, Pal.Purple, 0.25f);
        g.Glow(540, 300, 180, Pal.Teal, 0.2f);
        // Faint targeting rings.
        for (int i = 1; i <= 4; i++)
            g.Ring(Core.X, Core.Y, ShieldR + i * 34, 1, Pal.Cyan * 0.06f, 64);
        for (int i = 0; i < 12; i++)
        {
            var d = MathF2.FromAngle(i * MathF.PI / 6 + time * 0.05f);
            g.Line(Core + d * (ShieldR + 14), Core + d * (ShieldR + 140), 1, Pal.Cyan * 0.04f);
        }

        DrawCore(g, time);

        if (_novaRing >= 0)
        {
            float a = 1 - _novaRing / 420;
            g.Ring(Core.X, Core.Y, _novaRing, 6, Color.White * a, 72);
            g.Glow(Core.X, Core.Y, _novaRing, Pal.Sky, 0.3f * a);
        }

        foreach (var sc in _saucers)
            DrawSaucer(g, sc, time);
        foreach (var s in _shots)
            DrawShot(g, s, time);

        DrawShield(g, _angle, _half, time, 1f);
        DrawWarnings(g, time);
        DrawIntegrity(g, time);

        if (_waveBanner > 0)
        {
            float a = MathF.Min(1, _waveBanner);
            g.TextShadow("WAVE " + _wave, Core.X, Core.Y - 112, 3f, Pal.Yellow * a, Align.Center);
        }
        if (_waveGap > 0 && _waveGap < 2.3f)
            g.TextShadow("WAVE CLEAR", Core.X, Core.Y + 92, 2f, Pal.Cyan * MathF.Min(1, _waveGap), Align.Center);
        if (_combo >= 3)
            g.Text("COMBO x" + Math.Min(_combo, 8), 630, 30, 1.5f, Pal.Cyan, Align.Right);
    }

    private void DrawCore(Gfx g, float time)
    {
        float health = _integrity / 100f;
        var col = health > 0.6f ? Pal.Cyan : health > 0.3f ? Pal.Lerp(Pal.Orange, Pal.Cyan, (health - 0.3f) / 0.3f) : Pal.Lerp(Pal.Red, Pal.Orange, health / 0.3f);
        float pulse = MathF2.Pulse(time, health < 0.3f ? 0.4f : 1.2f);
        g.Glow(Core.X, Core.Y, 70 + pulse * 15, col, 0.45f);
        if (_coreFlash > 0)
            g.Glow(Core.X, Core.Y, 90, Pal.Red, _coreFlash);
        g.Circle(Core.X, Core.Y, CoreR + 3, new Color(20, 30, 50));
        g.Ring(Core.X, Core.Y, CoreR + 3, 2, Pal.Lighten(col, 0.3f) * 0.8f);
        // Rotating reactor vanes.
        for (int i = 0; i < 3; i++)
        {
            float a0 = time * 1.6f + i * MathF2.Tau / 3;
            g.Arc(Core.X, Core.Y, CoreR - 5, 4, a0, a0 + 1.4f, Pal.Darken(col, 0.2f));
        }
        g.Circle(Core.X, Core.Y, 10 + pulse * 2, col);
        g.Circle(Core.X, Core.Y, 6 + pulse, Pal.Lighten(col, 0.6f));
        g.Glow(Core.X, Core.Y, 22, Color.White, 0.5f + 0.3f * pulse);
    }

    private void DrawShield(Gfx g, float angle, float half, float time, float scale)
    {
        var c = Core;
        float r = ShieldR * scale;
        float flash = MathF.Max(0, _shieldFlash);
        var col = _wideTimer > 0 ? Pal.Lerp(Pal.Gold, Pal.Cyan, MathF2.Pulse(time, 0.5f)) : Pal.Cyan;
        float a0 = angle - half, a1 = angle + half;
        g.Arc(c.X, c.Y, r, 16 * scale, a0, a1, Pal.Add(col, 0.12f + 0.2f * flash), 40);
        g.Arc(c.X, c.Y, r, 9 * scale, a0, a1, Pal.Add(col, 0.25f + 0.3f * flash), 40);
        g.Arc(c.X, c.Y, r, 4 * scale, a0, a1, Pal.Lighten(col, 0.4f + 0.4f * flash), 40);
        // Shimmer travelling along the arc.
        for (int i = 0; i < 3; i++)
        {
            float k = (time * 0.8f + i / 3f) % 1f;
            var p = c + MathF2.FromAngle(a0 + (a1 - a0) * k, r);
            g.Glow(p.X, p.Y, 10 * scale, Color.White, 0.5f);
        }
        var e0 = c + MathF2.FromAngle(a0, r);
        var e1 = c + MathF2.FromAngle(a1, r);
        g.Circle(e0.X, e0.Y, 3 * scale, Color.White);
        g.Circle(e1.X, e1.Y, 3 * scale, Color.White);
        g.Glow(e0.X, e0.Y, 9 * scale, col, 0.7f);
        g.Glow(e1.X, e1.Y, 9 * scale, col, 0.7f);
    }

    private static void DrawSaucer(Gfx g, Saucer sc, float time)
    {
        var p = sc.Pos;
        if (p.X < -40 || p.X > 680 || p.Y < -40 || p.Y > 400) return;
        g.Glow(p.X, p.Y, 28, Pal.Lime, 0.35f);
        g.PixelsCentered(SaucerArt, p.X, p.Y + MathF.Sin(sc.Age * 3) * 1.5f, 2f);
        int lit = (int)(time * 8) % 3;
        for (int i = 0; i < 3; i++)
            g.Glow(p.X - 8 + i * 8, p.Y + 1, 5, i == lit ? Pal.Yellow : Pal.Lime, 0.6f);
    }

    private static void DrawShot(Gfx g, Shot s, float time)
    {
        float a = MathF2.Angle(s.Vel);
        switch (s.Kind)
        {
            case Kind.Missile:
            {
                var body = s.Deflected ? Pal.Ice : new Color(220, 220, 230);
                var fin = s.Deflected ? Pal.Cyan : Pal.Red;
                var back = s.Pos - MathF2.FromAngle(a, 11);
                g.Glow(back.X, back.Y, 10 + MathF.Sin(time * 40) * 2, s.Deflected ? Pal.Cyan : Pal.Orange, 0.8f);
                g.Shape(FinShape, s.Pos, a, 1.4f, fin);
                g.Shape(MissileShape, s.Pos, a, 1.4f, body);
                var nose = s.Pos + MathF2.FromAngle(a, 7);
                g.Circle(nose.X, nose.Y, 2, fin);
                break;
            }
            case Kind.Comet:
            {
                var col = s.Deflected ? Pal.White : Pal.Sky;
                var dir = s.Vel.LengthSquared() > 0 ? Vector2.Normalize(s.Vel) : Vector2.UnitX;
                for (int i = 6; i >= 1; i--)
                {
                    var p = s.Pos - dir * i * 5;
                    g.Circle(p.X, p.Y, 6 - i * 0.7f, col * (0.5f - i * 0.06f));
                }
                g.Glow(s.Pos, 22, col, 0.7f);
                g.Circle(s.Pos.X, s.Pos.Y, 6, Pal.Lighten(col, 0.4f));
                g.Circle(s.Pos.X - 1.5f, s.Pos.Y - 1.5f, 2.5f, Color.White);
                break;
            }
            case Kind.Dart:
            {
                var col = s.Deflected ? Pal.Cyan : Pal.Magenta;
                g.Glow(s.Pos, 16, col, 0.6f);
                g.Shape(DartShape, s.Pos, a, 1.1f, col);
                g.Shape(DartShape, s.Pos, a, 0.55f, Pal.Lighten(col, 0.6f));
                break;
            }
            case Kind.Orb:
            {
                var col = OrbColour(s.Orb);
                float pulse = MathF2.Pulse(time + s.Pos.X * 0.01f, 0.6f);
                g.Glow(s.Pos, 20 + pulse * 6, col, 0.6f);
                g.Circle(s.Pos.X, s.Pos.Y, 8, Pal.Darken(col, 0.3f));
                g.Ring(s.Pos.X, s.Pos.Y, 8, 1.5f, Pal.Lighten(col, 0.5f));
                string label = s.Orb switch { Orb.Wide => "W", Orb.Repair => "+", _ => "N" };
                g.Text(label, s.Pos.X + 0.5f, s.Pos.Y - 3.5f, 1f, Color.White, Align.Center);
                break;
            }
        }
    }

    private void DrawWarnings(Gfx g, float time)
    {
        // Arrows at the edge for threats that are just arriving.
        foreach (var s in _shots)
        {
            if (s.Deflected || s.Age > 0.9f || s.Kind == Kind.Orb) continue;
            var dir = Vector2.Normalize(s.Pos - Core);
            var p = new Vector2(MathF2.Clamp(s.Pos.X, 14, 626), MathF2.Clamp(s.Pos.Y, Screen.HudHeight + 12, 348));
            float blink = (int)(time * 10) % 2 == 0 ? 1 : 0.4f;
            var col = ShotColour(s) * blink;
            float a = MathF2.Angle(-dir);
            g.Triangle(p + MathF2.FromAngle(a, 8), p + MathF2.FromAngle(a + 2.4f, 7), p + MathF2.FromAngle(a - 2.4f, 7), col);
        }
    }

    private void DrawIntegrity(Gfx g, float time)
    {
        float x = 14, y = IsTouch ? 44 : 338, w = 120;
        g.Text("REACTOR", x, y - 13, 1f, Pal.LightGrey);
        g.RoundRect(x, y, w, 10, 3, Color.Black * 0.6f);
        float k = _integrity / 100f;
        var col = k > 0.6f ? Pal.Cyan : k > 0.3f ? Pal.Orange : Pal.Red;
        if (k < 0.3f && (int)(time * 4) % 2 == 0) col = Pal.Lighten(col, 0.4f);
        g.RoundRect(x + 1, y + 1, (w - 2) * k, 8, 3, col);
        g.Glow(x + (w - 2) * k, y + 5, 10, col, 0.5f);
        if (_wideTimer > 0)
        {
            g.Text("WIDE FIELD", 626, 325, 1f, Pal.Gold, Align.Right);
            g.RoundRect(506, 338, 120 * (_wideTimer / 9), 10, 3, Pal.Gold);
        }
    }

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        Backdrops.Space(g, time, 6, 10, r);
        float s = r.H / 70f;
        var c = r.Center;
        float angle = -MathF.PI / 2 + MathF.Sin(time * 1.2f) * 1.2f;
        g.Glow(c.X, c.Y, 40 * s, Pal.Cyan, 0.5f);
        g.Circle(c.X, c.Y, 10 * s, new Color(20, 30, 50));
        g.Circle(c.X, c.Y, 6 * s, Pal.Cyan);
        g.Circle(c.X, c.Y, 3.5f * s, Pal.Ice);
        for (int i = 0; i < 3; i++)
        {
            float a0 = time * 1.6f + i * MathF2.Tau / 3;
            g.Arc(c.X, c.Y, 8.5f * s, 2 * s, a0, a0 + 1.4f, Pal.Teal);
        }
        float rr = 24 * s;
        g.Arc(c.X, c.Y, rr, 8 * s, angle - 0.8f, angle + 0.8f, Pal.Add(Pal.Cyan, 0.3f), 30);
        g.Arc(c.X, c.Y, rr, 3 * s, angle - 0.8f, angle + 0.8f, Pal.Ice, 30);
        // Missiles flying in and bouncing off.
        for (int i = 0; i < 3; i++)
        {
            float a = i * MathF2.Tau / 3 + 0.5f;
            float k = (time * 0.6f + i / 3f) % 1f;
            float dist = MathF2.Lerp(r.W * 0.6f, rr + 4 * s, k);
            var p = c + MathF2.FromAngle(a, dist);
            g.Glow(p.X, p.Y, 6 * s, Pal.Orange, 0.8f);
            g.Shape(FinShape, p, a + MathF.PI, 0.45f * s, Pal.Red);
            g.Shape(MissileShape, p, a + MathF.PI, 0.45f * s, Pal.White);
        }
        // A comet bounced away from the shield.
        float kb = (time * 0.8f) % 1f;
        var pb = c + MathF2.FromAngle(angle, rr + kb * r.W * 0.5f);
        g.Glow(pb.X, pb.Y, 8 * s, Pal.Cyan, 0.8f);
        g.Circle(pb.X, pb.Y, 2.5f * s, Pal.White);
    }

    public override void AutoPlay(Controls c)
    {
        // Face the threat that will reach the field soonest; grab orbs when nothing is close.
        Shot best = null;
        float bestT = float.MaxValue;
        foreach (var s in _shots)
        {
            if (s.Deflected) continue;
            float d = Vector2.Distance(s.Pos, Core) - ShieldR;
            if (d < -6) continue;
            float speed = MathF.Max(20, s.Vel.Length());
            float t = d / speed + (s.Kind == Kind.Orb ? 1.2f : 0);
            if (t < bestT)
            {
                bestT = t;
                best = s;
            }
        }
        if (best == null)
            return;
        float target = MathF2.Angle(best.Pos - Core);
        if (best.Kind == Kind.Dart)
            target += best.Omega * 0.3f;
        float diff = MathF2.WrapAngle(target - _angle);
        c.SetDirections(MathF.Abs(diff) < 0.12f ? 0 : MathF2.Clamp(diff * 2.5f, -1, 1), 0);
    }
}
