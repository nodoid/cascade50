using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 27 Orbitter: a Gyruss-style tube shooter. Circle the rim and fire into the vortex as alien
/// formations spiral out, orbit and dive. Clear each wave to warp on towards Earth.
/// </summary>
public sealed class Orbitter : MiniGame
{
    public override int Number => 27;
    public override string Title => "Orbitter";
    public override Category Category => Category.Shooter;
    public override string Tagline => "Circle the rim and blast the aliens spiralling out of the void.";
    public override Color Accent => Pal.Cyan;
    public override Pad Pad => Pad.Horizontal | Pad.Fire;

    public override string[] HowToPlay =>
    [
        "Fly round the rim and fire into the centre. Aliens spiral out, orbit, then dive at you.",
        "Clear a wave to warp to the next planet. Shoot the gold satellite for a double laser.",
        "Divers score double. Three ships.",
    ];

    public override string[] DesktopControls => ["LEFT / RIGHT to circle, SPACE to fire.", "Or hold the mouse to steer and fire."];
    public override string[] TouchControls => ["Stick to circle, FIRE to shoot.", "Or hold a finger where you want to fly."];

    private static readonly Vector2 C = new(320, 192);
    private const float R = 156;

    private static readonly string[] Planets = ["NEPTUNE", "URANUS", "SATURN", "JUPITER", "MARS", "EARTH"];
    private static readonly Color[] PlanetCols =
    [
        new(60, 110, 230), new(110, 210, 220), new(220, 190, 120), new(220, 150, 100), new(220, 90, 50), new(60, 140, 230),
    ];

    private static readonly Dictionary<char, Color> Cols = new()
    {
        ['r'] = Pal.Red, ['o'] = Pal.Orange, ['y'] = Pal.Yellow, ['g'] = Pal.Lime, ['c'] = Pal.Cyan, ['b'] = Pal.Sky,
        ['m'] = Pal.Magenta, ['p'] = Pal.Pink, ['w'] = Pal.White, ['k'] = new Color(30, 30, 50), ['G'] = Pal.Gold,
    };

    private static readonly PixelArt[] Aliens =
    [
        new([
            "..m...m..",
            "...mmm...",
            ".mmwmwmm.",
            "mmmmmmmmm",
            "m.mmmmm.m",
            "m.m...m.m",
            "...m.m...",
        ], Cols),
        new([
            "...ccc...",
            ".ccbbbcc.",
            "cbbwbwbbc",
            "ccbbbbbcc",
            ".c.c.c.c.",
            "c.......c",
        ], Cols),
        new([
            "o...y...o",
            ".o.yyy.o.",
            "..ooooo..",
            ".orrwrro.",
            "ooorrrooo",
            "o.o...o.o",
            "o.......o",
        ], Cols),
        new([
            ".g.....g.",
            "..g...g..",
            ".ggggggg.",
            "gg.ggg.gg",
            "ggggggggg",
            ".g.g.g.g.",
            "g.......g",
        ], Cols),
    ];

    private static readonly PixelArt Satellite = new(
    [
        "bb.....bb",
        "bb..w..bb",
        "bbbwwwbbb",
        "bb.www.bb",
        "bb.....bb",
    ], Cols);

    private static readonly PixelArt GoldSat = new(
    [
        "GG.....GG",
        "GG..w..GG",
        "GGGwywGGG",
        "GG.www.GG",
        "GG.....GG",
    ], Cols);

    private static readonly Vector2[] ShipShape =
    [
        new(12, 0), new(-6, 9), new(-2, 0), new(-6, -9),
    ];

    private static readonly Vector2[] ShipCore =
    [
        new(9, 0), new(-2, 4), new(-2, -4),
    ];

    private enum EState { Waiting, Enter, Orbit, Dive, Return }

    private sealed class Enemy
    {
        public EState State;
        public float R, A;
        public float Delay;
        public float T;
        public float SlotA, SlotR;
        public float SpinDir;
        public int Type;
        public bool Fired;
        public float DiveTurn;
    }

    private struct Bolt
    {
        public float R, A, Speed;
    }

    private struct Star
    {
        public float A, Z;
    }

    private readonly List<Enemy> _enemies = new();
    private readonly List<Bolt> _shots = new();
    private readonly List<Bolt> _bombs = new();
    private readonly Star[] _stars = new Star[180];
    private readonly float[] _starPrevR = new float[180];
    private float _angle = MathF.PI / 2, _angVel;
    private float _cool;
    private float _respawn, _invuln;
    private float _formation;
    private int _wave, _groupsLeft, _groupIndex;
    private float _groupTimer;
    private float _warp;
    private float _banner;
    private string _bannerText = "";
    private float _doubleShot;
    private int _killed, _waveTotal;
    // Satellites.
    private readonly float[] _satA = new float[3];
    private readonly bool[] _satAlive = new bool[3];
    private float _satR, _satT;
    private bool _satDone;
    private float _starSpeed = 0.25f;

    protected override void Start()
    {
        Lives = 3;
        _wave = 0;
        for (int i = 0; i < _stars.Length; i++)
            _stars[i] = new Star { A = Rand(0, MathF2.Tau), Z = Rand(0, 1) };
        NextWave();
    }

    private string PlanetName => Planets[(_wave - 1) % Planets.Length];
    private Color PlanetCol => PlanetCols[(_wave - 1) % PlanetCols.Length];

    private void NextWave()
    {
        _wave++;
        Level = _wave;
        _enemies.Clear();
        _bombs.Clear();
        _groupsLeft = 3 + Math.Min(_wave, 4);
        _groupIndex = 0;
        _groupTimer = 0.8f;
        _killed = 0;
        _waveTotal = _groupsLeft * 8;
        _satDone = false;
        _satT = 0;
        _bannerText = _wave <= Planets.Length ? "APPROACHING " + PlanetName : $"{PlanetName} - SECTOR {(_wave - 1) / Planets.Length + 1}";
        _banner = 2.5f;
        Status = PlanetName;
    }

    private void SpawnGroup()
    {
        int type = (_groupIndex + _wave) % Aliens.Length;
        float baseA = Rand(0, MathF2.Tau);
        float spin = Chance(0.5f) ? 1 : -1;
        float slotR = Rand(60, 104);
        for (int i = 0; i < 8; i++)
        {
            _enemies.Add(new Enemy
            {
                State = EState.Waiting,
                Delay = i * 0.16f,
                SlotA = baseA + i * MathF2.Tau / 8,
                SlotR = slotR + (i % 2) * 14,
                SpinDir = spin,
                Type = type,
                R = 4,
            });
        }
        _groupIndex++;
        _groupsLeft--;
        Sound.Play(Sfx.Warp, 0.5f, 0.25f);
    }

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        if (_banner > 0)
            _banner -= Dt;
        if (_doubleShot > 0)
            _doubleShot -= Dt;
        if (_invuln > 0)
            _invuln -= Dt;
        _formation += Dt * (0.25f + _wave * 0.03f);

        float targetSpeed = _warp > 0 ? 4f : 0.25f;
        _starSpeed = MathF2.Approach(_starSpeed, targetSpeed, Dt * 3);
        for (int i = 0; i < _stars.Length; i++)
        {
            var s = _stars[i];
            _starPrevR[i] = StarR(s.Z);
            s.Z += Dt * _starSpeed * (0.25f + s.Z);
            if (s.Z > 1)
            {
                s.Z = Rand(0.01f, 0.08f);
                s.A = Rand(0, MathF2.Tau);
                _starPrevR[i] = StarR(s.Z);
            }
            _stars[i] = s;
        }

        if (_warp > 0)
        {
            _warp -= Dt;
            Sound.Loop(LoopSfx.Thrust, true, 0.4f + (3 - _warp) * 0.15f, 0.4f);
            MovePlayer();
            if (_warp <= 0)
                NextWave();
            return;
        }

        // Ship.
        if (_respawn > 0)
        {
            _respawn -= Dt;
            if (_respawn <= 0)
                _invuln = 2f;
        }
        else
        {
            MovePlayer();
            _cool -= Dt;
            bool fire = In.Fire || In.FirePressed || In.PointerDown;
            if (fire && _cool <= 0 && _shots.Count < 8)
            {
                _cool = 0.15f;
                if (_doubleShot > 0)
                {
                    _shots.Add(new Bolt { R = R - 8, A = _angle - 0.05f, Speed = 340 });
                    _shots.Add(new Bolt { R = R - 8, A = _angle + 0.05f, Speed = 340 });
                }
                else
                    _shots.Add(new Bolt { R = R - 8, A = _angle, Speed = 340 });
                Sound.Play(Sfx.Laser, 0.3f + Rand(-0.05f, 0.05f), 0.3f);
            }
        }

        // Enemy groups.
        if (_groupsLeft > 0 && (_groupTimer -= Dt) <= 0)
        {
            SpawnGroup();
            _groupTimer = MathF.Max(2.2f, 4f - _wave * 0.25f);
        }

        UpdateEnemies();
        UpdateShots();
        UpdateSatellites();

        if (_groupsLeft == 0 && _enemies.Count == 0 && _warp <= 0)
        {
            AddScore(500 * _wave, C.X, C.Y, Pal.Cyan);
            Sound.Play(Sfx.LevelUp);
            _warp = 3f;
            _bannerText = "WARP!";
            _banner = 2.5f;
            _shots.Clear();
            _bombs.Clear();
        }
    }

    private static float StarR(float z) => 4 + 420 * z * z;

    private void MovePlayer()
    {
        float input = 0;
        if (In.Left) input += 1;
        if (In.Right) input -= 1;
        if (MathF.Abs(In.AxisX) > 0.2f && !In.Left && !In.Right)
            input = -In.AxisX;
        if (In.PointerDown && Vector2.Distance(In.Pointer, C) > 30)
        {
            float target = MathF.Atan2(In.Pointer.Y - C.Y, In.Pointer.X - C.X);
            float d = MathF2.WrapAngle(target - _angle);
            input = MathF2.Clamp(d * 4, -1, 1);
        }
        _angVel = MathF2.Approach(_angVel, input * 3.4f, Dt * 22);
        _angle = MathF2.WrapAngle(_angle + _angVel * Dt);
        if (MathF.Abs(_angVel) > 0.5f && Tick % 3 == 0 && _respawn <= 0)
        {
            var p = Polar(R + 6, _angle);
            Fx.Spark(p.X, p.Y, Rand(-10, 10), Rand(-10, 10), Pal.Cyan, 0.3f, 1.5f);
        }
    }

    private static Vector2 Polar(float r, float a) => C + new Vector2(MathF.Cos(a) * r, MathF.Sin(a) * r);
    private static float Persp(float r) => 0.3f + 0.7f * MathF2.Clamp(r / R, 0, 1.3f);

    private void UpdateEnemies()
    {
        float diveChance = 0.004f + _wave * 0.0025f;
        int diving = 0;
        foreach (var e in _enemies)
            if (e.State == EState.Dive)
                diving++;
        for (int i = _enemies.Count - 1; i >= 0; i--)
        {
            var e = _enemies[i];
            switch (e.State)
            {
                case EState.Waiting:
                    e.Delay -= Dt;
                    if (e.Delay <= 0)
                        e.State = EState.Enter;
                    break;
                case EState.Enter:
                case EState.Return:
                {
                    e.T = MathF.Min(1, e.T + Dt * 0.55f);
                    float k = MathF2.EaseInOut(e.T);
                    float slotA = e.SlotA + _formation * e.SpinDir;
                    e.R = MathF2.Lerp(4, e.SlotR, k);
                    e.A = slotA + (1 - k) * 3.2f * e.SpinDir;
                    if (e.T >= 1)
                        e.State = EState.Orbit;
                    break;
                }
                case EState.Orbit:
                    e.A = e.SlotA + _formation * e.SpinDir;
                    e.R = e.SlotR + MathF.Sin(Time * 2 + i) * 5;
                    if (_respawn <= 0 && diving < 2 + _wave / 2 && Chance(diveChance))
                    {
                        e.State = EState.Dive;
                        e.Fired = false;
                        e.DiveTurn = Rand(0.25f, 0.6f) + MathF.Min(_wave, 6) * 0.05f;
                        diving++;
                        Sound.Play(Sfx.Whoosh, 0.4f, 0.3f);
                    }
                    break;
                case EState.Dive:
                {
                    e.R += Dt * (70 + _wave * 6) * (0.6f + e.R / R);
                    float d = MathF2.WrapAngle(_angle - e.A);
                    // Homes in early, then commits to its line so it can be dodged.
                    float home = e.R < R * 0.6f ? 1 : 0.15f;
                    e.A += (MathF2.Clamp(d, -1, 1) * e.DiveTurn + e.SpinDir * 0.35f) * home * Dt * 1.6f;
                    if (!e.Fired && e.R > R * 0.45f)
                    {
                        e.Fired = true;
                        if (Chance(0.5f + _wave * 0.08f))
                        {
                            _bombs.Add(new Bolt { R = e.R, A = e.A, Speed = 120 + _wave * 8 });
                            Sound.Play(Sfx.Zap, 0.3f, 0.25f);
                        }
                    }
                    if (e.R > R + 30)
                    {
                        e.State = EState.Return;
                        e.T = 0;
                        e.R = 4;
                    }
                    break;
                }
            }
            // Formation shots.
            if (e.State == EState.Orbit && Chance(0.0012f + _wave * 0.0006f))
            {
                _bombs.Add(new Bolt { R = e.R, A = e.A, Speed = 100 + _wave * 6 });
                Sound.Play(Sfx.Zap, 0.6f, 0.15f);
            }
            // Ramming the ship.
            if (e.State == EState.Dive && _respawn <= 0 && _invuln <= 0 &&
                Vector2.Distance(Polar(e.R, e.A), Polar(R, _angle)) < 12)
            {
                var p = Polar(e.R, e.A);
                Fx.Explode(p.X, p.Y, 1);
                _enemies.RemoveAt(i);
                _killed++;
                ShipHit();
            }
        }
    }

    private void UpdateShots()
    {
        for (int i = _shots.Count - 1; i >= 0; i--)
        {
            var s = _shots[i];
            s.R -= s.Speed * Dt * (0.4f + 0.6f * s.R / R);
            _shots[i] = s;
            bool remove = s.R < 10;
            if (!remove)
            {
                var p = Polar(s.R, s.A);
                for (int j = _enemies.Count - 1; j >= 0; j--)
                {
                    var e = _enemies[j];
                    if (e.State == EState.Waiting)
                        continue;
                    var ep = Polar(e.R, e.A);
                    float rad = 15 * Persp(e.R);
                    if (Vector2.DistanceSquared(p, ep) < rad * rad)
                    {
                        KillEnemy(j, ep);
                        remove = true;
                        break;
                    }
                }
                if (!remove && HitSatellite(p))
                    remove = true;
            }
            if (remove)
                _shots.RemoveAt(i);
        }

        for (int i = _bombs.Count - 1; i >= 0; i--)
        {
            var b = _bombs[i];
            b.R += b.Speed * Dt * (0.5f + b.R / R);
            _bombs[i] = b;
            if (b.R > R + 30)
            {
                _bombs.RemoveAt(i);
                continue;
            }
            if (_respawn <= 0 && _invuln <= 0 && Vector2.Distance(Polar(b.R, b.A), Polar(R, _angle)) < 9)
            {
                _bombs.RemoveAt(i);
                ShipHit();
                return;
            }
        }
    }

    private void KillEnemy(int j, Vector2 p)
    {
        var e = _enemies[j];
        bool dive = e.State == EState.Dive;
        int pts = dive ? 200 : 100;
        AddScore(pts, p.X, p.Y - 8, dive ? Pal.Orange : Pal.Yellow);
        var col = e.Type switch { 0 => Pal.Magenta, 1 => Pal.Cyan, 2 => Pal.Orange, _ => Pal.Lime };
        Fx.Burst(p.X, p.Y, col, 18, 120 * Persp(e.R), 0.5f, 2.2f);
        Fx.Burst(p.X, p.Y, Pal.White, 6, 60, 0.25f, 2f);
        Sound.Play(Sfx.Explode, Rand(-0.1f, 0.4f), 0.6f);
        _enemies.RemoveAt(j);
        _killed++;
    }

    private void ShipHit()
    {
        var p = Polar(R, _angle);
        Fx.Explode(p.X, p.Y, 1.6f);
        Fx.Burst(p.X, p.Y, Pal.Cyan, 30, 160, 0.9f, 2.5f);
        Sound.Play(Sfx.BigExplode);
        _bombs.Clear();
        _doubleShot = 0;
        if (!LoseLife())
            _respawn = 1.6f;
        // Divers break off and regroup.
        foreach (var e in _enemies)
            if (e.State == EState.Dive)
            {
                e.State = EState.Return;
                e.T = 0;
                e.R = 4;
            }
    }

    private void UpdateSatellites()
    {
        if (!_satDone && _killed >= _waveTotal / 3)
        {
            _satDone = true;
            _satT = 9f;
            _satR = 10;
            for (int i = 0; i < 3; i++)
            {
                _satAlive[i] = true;
                _satA[i] = Rand(0, MathF2.Tau);
            }
            Sound.Play(Sfx.PowerUp, -0.3f, 0.4f);
        }
        if (_satT <= 0)
            return;
        _satT -= Dt;
        _satR = MathF2.Approach(_satR, 70, Dt * 30);
        float baseA = Time * 0.8f;
        for (int i = 0; i < 3; i++)
            _satA[i] = baseA + (i - 1) * 0.35f;
    }

    private bool HitSatellite(Vector2 p)
    {
        if (_satT <= 0)
            return false;
        for (int i = 0; i < 3; i++)
        {
            if (!_satAlive[i])
                continue;
            var sp = Polar(_satR, _satA[i]);
            if (Vector2.DistanceSquared(p, sp) < 100)
            {
                _satAlive[i] = false;
                if (i == 1)
                {
                    _doubleShot = 20;
                    AddScore(500, sp.X, sp.Y - 8, Pal.Gold);
                    Fx.Float("DOUBLE LASER!", C.X, C.Y + 30, Pal.Gold);
                    Sound.Play(Sfx.Bonus);
                    Fx.Burst(sp.X, sp.Y, Pal.Gold, 30, 120, 0.8f, 2.5f);
                }
                else
                {
                    AddScore(300, sp.X, sp.Y - 8, Pal.Sky);
                    Sound.Play(Sfx.Pickup);
                    Fx.Burst(sp.X, sp.Y, Pal.Sky, 16, 90, 0.5f, 2f);
                }
                return true;
            }
        }
        return false;
    }

    // ------------------------------------------------------------------ drawing

    public override void Draw(Gfx g)
    {
        g.GradientV(0, 0, 640, 360, new Color(4, 2, 18), new Color(12, 4, 30));
        // Nebula swirl.
        g.Glow(C.X - 120, C.Y - 60, 220, Pal.Purple, 0.18f);
        g.Glow(C.X + 140, C.Y + 80, 200, Pal.Teal, 0.12f);

        // Destination planet, growing as the wave goes on.
        float progress = _waveTotal > 0 ? (float)_killed / _waveTotal : 0;
        float pr = 10 + 26 * progress + (_warp > 0 ? (3 - _warp) * 40 : 0);
        DrawPlanet(g, C.X, C.Y, pr, (_wave - 1) % Planets.Length, Time);

        // Starfield tunnel.
        bool streak = _starSpeed > 0.6f;
        for (int i = 0; i < _stars.Length; i++)
        {
            var s = _stars[i];
            float r = StarR(s.Z);
            var d = new Vector2(MathF.Cos(s.A), MathF.Sin(s.A));
            var p = C + d * r;
            var col = i % 9 == 0 ? Pal.Sky : i % 13 == 0 ? Pal.Pink : Pal.White;
            float b = MathF.Min(1, s.Z * 1.6f);
            if (streak)
                g.Line(C + d * _starPrevR[i] * 0.9f, p, 0.6f + s.Z * 1.6f, col * b);
            else
                g.Circle(p.X, p.Y, 0.4f + s.Z * 1.4f, col * b);
        }

        // Tunnel rings.
        for (int i = 0; i < 6; i++)
        {
            float z = Backdrops.Mod(Time * 0.25f * (_warp > 0 ? 6 : 1) + i / 6f, 1);
            float rr = 10 + z * z * (R - 10);
            g.Ring(C.X, C.Y, rr, 1, Pal.Purple * (0.08f + 0.25f * z));
        }
        // The rim track.
        g.Ring(C.X, C.Y, R, 2, Pal.Cyan * 0.25f, 96);
        g.Ring(C.X, C.Y, R + 4, 1, Pal.Cyan * 0.12f, 96);
        for (int i = 0; i < 24; i++)
        {
            var p = Polar(R + 2, i * MathF2.Tau / 24 + Time * 0.05f);
            g.Circle(p.X, p.Y, 1.2f, Pal.Cyan * 0.5f);
        }

        // Satellites.
        if (_satT > 0)
        {
            for (int i = 0; i < 3; i++)
            {
                if (!_satAlive[i])
                    continue;
                var sp = Polar(_satR, _satA[i]);
                float k = Persp(_satR);
                g.Glow(sp.X, sp.Y, 16 * k, i == 1 ? Pal.Gold : Pal.Sky, 0.5f);
                g.PixelsCentered(i == 1 ? GoldSat : Satellite, sp.X, sp.Y, 1.6f * k);
            }
        }

        // Enemies.
        foreach (var e in _enemies)
        {
            if (e.State == EState.Waiting)
                continue;
            var p = Polar(e.R, e.A);
            float k = Persp(e.R);
            var col = e.Type switch { 0 => Pal.Magenta, 1 => Pal.Cyan, 2 => Pal.Orange, _ => Pal.Lime };
            g.Glow(p.X, p.Y, 20 * k, col, e.State == EState.Dive ? 0.6f : 0.3f);
            g.PixelsCentered(Aliens[e.Type], p.X, p.Y, 3f * k, (int)(Time * 4 + e.SlotA * 3) % 2 == 0);
        }

        // Shots.
        foreach (var s in _shots)
        {
            float k = Persp(s.R);
            var a = Polar(s.R, s.A);
            var b = Polar(s.R + 16 * k, s.A);
            g.GlowLine(a, b, 2.6f * k, Pal.Cyan);
            g.Glow(a, 8 * k, Pal.White, 0.6f);
        }
        foreach (var b in _bombs)
        {
            var p = Polar(b.R, b.A);
            float k = Persp(b.R);
            g.Glow(p.X, p.Y, 9 * k, Pal.Red, 0.8f);
            g.Circle(p.X, p.Y, 2.4f * k, Pal.Yellow);
        }

        // Ship.
        if (_respawn <= 0 && !IsOver && (_invuln <= 0 || (int)(_invuln * 12) % 2 == 0))
        {
            var p = Polar(R, _angle);
            float face = _angle + MathF.PI;
            var col = _doubleShot > 0 ? Pal.Gold : Pal.Ice;
            g.Glow(p.X, p.Y, 26, _doubleShot > 0 ? Pal.Gold : Pal.Cyan, 0.4f);
            var back = Polar(R + 13, _angle);
            g.Glow(back.X, back.Y, 9 + MathF.Sin(Time * 30) * 2, Pal.Orange, 0.8f);
            g.Shape(ShipShape, p, face, 1.4f, Pal.Darken(col, 0.3f));
            g.ShapeOutline(ShipShape, p, face, 1.4f, 1.3f, col, true);
            g.Shape(ShipCore, p, face, 1.4f, Pal.Cyan);
        }

        if (_banner > 0)
        {
            float a = MathF.Min(1, _banner);
            g.TextShadow(_bannerText, C.X, C.Y + 52, 2f, Pal.Yellow * a, Align.Center);
            if (_warp <= 0 && _wave > 0)
                g.Text("WAVE " + _wave, C.X, C.Y + 74, 1.5f, Pal.White * a, Align.Center);
        }
        if (_doubleShot > 0)
            g.Text("DOUBLE LASER", 630, 340, 1f, Pal.Gold * MathF.Min(1, _doubleShot), Align.Right);
    }

    private static void DrawPlanet(Gfx g, float x, float y, float r, int which, float time)
    {
        var col = PlanetCols[which];
        g.Glow(x, y, r * 2.4f, col, 0.35f);
        if (which == 2)
            g.Ellipse(x, y, r * 2.1f, r * 0.55f, new Color(170, 150, 100) * 0.6f);
        g.Circle(x, y, r, Pal.Darken(col, 0.35f));
        g.Circle(x - r * 0.15f, y - r * 0.15f, r * 0.82f, col);
        // Bands.
        for (int i = -2; i <= 2; i++)
            g.Ellipse(x - r * 0.1f, y + i * r * 0.3f, r * 0.75f * (1 - MathF.Abs(i) * 0.18f), r * 0.07f, Pal.Lighten(col, 0.2f) * 0.5f);
        if (which == 5)
        {
            g.Ellipse(x - r * 0.3f, y - r * 0.1f, r * 0.3f, r * 0.2f, Pal.Green);
            g.Ellipse(x + r * 0.25f, y + r * 0.3f, r * 0.25f, r * 0.15f, Pal.Green);
        }
        g.Ellipse(x + r * 0.35f, y + r * 0.35f, r * 0.6f, r * 0.6f, Color.Black * 0.35f);
        if (which == 2)
            g.Arc(x, y, r * 1.6f, r * 0.12f, 0.15f, MathF.PI - 0.15f, new Color(230, 210, 150) * 0.8f);
        _ = time;
    }

    // ------------------------------------------------------------------ icon

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        float s = r.H / 70f;
        g.GradientV(r.X, r.Y, r.W, r.H, new Color(4, 2, 18), new Color(14, 4, 34));
        var c = new Vector2(r.CenterX, r.CenterY);
        for (int i = 0; i < 70; i++)
        {
            float a = i * 2.39996f;
            float z = Backdrops.Mod(time * 0.3f + i * 0.137f, 1);
            float rr = (2 + z * z * 90) * s;
            var d = new Vector2(MathF.Cos(a), MathF.Sin(a));
            g.Line(c + d * rr * 0.85f, c + d * rr, (0.3f + 0.6f * z) * s, Pal.White * (z * 0.8f));
        }
        DrawPlanet(g, c.X, c.Y, 7 * s, 2, time);
        float ring = 30 * s;
        g.Ring(c.X, c.Y, ring, 1.2f * s, Pal.Cyan * 0.3f);
        for (int i = 0; i < 6; i++)
        {
            float a = time * 0.9f + i * MathF2.Tau / 6;
            float er = (14 + 4 * MathF.Sin(time * 2 + i)) * s;
            var p = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * er;
            g.Glow(p.X, p.Y, 6 * s, Pal.Magenta, 0.4f);
            g.PixelsCentered(Aliens[i % 2 == 0 ? 0 : 2], p.X, p.Y, 0.8f * s);
        }
        float sa = MathF.PI / 2 + MathF.Sin(time * 0.8f) * 0.8f;
        var sp = c + new Vector2(MathF.Cos(sa), MathF.Sin(sa)) * ring;
        g.Glow(sp.X, sp.Y, 12 * s, Pal.Cyan, 0.5f);
        g.Shape(ShipShape, sp, sa + MathF.PI, 0.6f * s, Pal.Ice);
        float st = Backdrops.Mod(time * 2, 1);
        var b0 = c + new Vector2(MathF.Cos(sa), MathF.Sin(sa)) * ring * (1 - st);
        var b1 = c + new Vector2(MathF.Cos(sa), MathF.Sin(sa)) * ring * MathF.Min(1, 1.15f - st);
        g.GlowLine(b0, b1, 1.2f * s, Pal.Cyan);
    }

    // ------------------------------------------------------------------ autoplay

    public override void AutoPlay(Controls c)
    {
        float target = _angle;
        float best = float.MaxValue;
        foreach (var e in _enemies)
        {
            if (e.State == EState.Waiting)
                continue;
            float d = MathF.Abs(MathF2.WrapAngle(e.A - _angle));
            if (e.State == EState.Dive)
            {
                if (e.R > R - 90)
                    continue;
                d -= 1.5f * e.R / R;
            }
            if (d < best)
            {
                best = d;
                target = e.A;
            }
        }
        if (_satT > 0 && _satAlive[1])
            target = _satA[1];
        // Keep out of the way of divers close to the rim.
        foreach (var e in _enemies)
            if (e.State == EState.Dive && e.R > R - 90 && MathF.Abs(MathF2.WrapAngle(e.A - _angle)) < 0.35f)
                target = _angle + (MathF2.WrapAngle(e.A - _angle) > 0 ? -0.7f : 0.7f);
        // Dodge incoming fire.
        foreach (var b in _bombs)
            if (b.R > R - 70 && MathF.Abs(MathF2.WrapAngle(b.A - _angle)) < 0.14f)
                target = _angle + (MathF2.WrapAngle(b.A - _angle) > 0 ? -0.5f : 0.5f);
        float diff = MathF2.WrapAngle(target - _angle);
        // Left increases the angle.
        c.SetDirections(MathF.Abs(diff) < 0.04f ? 0 : diff > 0 ? -1 : 1, 0);
        c.Fire = true;
        c.FirePressed = Tick % 9 == 0;
    }
}
