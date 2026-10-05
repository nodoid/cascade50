using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 43 Space Ship: a horizontal shooter. Each stage runs through an asteroid belt, an alien base with
/// gun turrets, a narrowing tunnel and finally a boss. Power capsules add a double shot, missiles,
/// option drones and a shield.
/// </summary>
public sealed class SpaceShip : MiniGame, Cascade50.Core.Capture.ICaptureHints
{
    public override int Number => 43;
    public override string Title => "Space Ship";
    public override Category Category => Category.Shooter;
    public override string Tagline => "Blast through asteroids, alien bases and tunnels to the boss.";
    public override Color Accent => Pal.Sky;

    public override string[] HowToPlay =>
    [
        "Fly through the asteroid belt, the alien base and the tunnel, then destroy the boss's glowing core.",
        "Wipe out a whole wave, or a red ship, for a power capsule: double shot, missiles, options, shield.",
        "Don't touch the walls!",
    ];

    public override string[] DesktopControls => ["ARROWS to fly, hold SPACE to fire."];
    public override string[] TouchControls => ["Stick to fly, hold FIRE to shoot."];
    public int CaptureTicks => 1800;

    public override Pad Pad => Pad.Stick | Pad.Fire;

    // ------------------------------------------------------------------ constants

    private const float Scroll = 70;
    private const float TBase = 22, TTunnel = 44, TTunnelEnd = 64, TBoss = 66;
    private const float Top = Screen.HudHeight;

    private enum Kind { Flyer, Chaser, Rock, Pebble, Turret, Red }

    private enum Upgrade { Double, Missile, Option1, Option2, Shield }

    private struct Enemy
    {
        public Kind Kind;
        public Vector2 Pos, Vel;
        public float Phase, Cool, Base;
        public int Hp, Formation;
        public bool Ceiling;
        public float Flash;
    }

    private struct Bullet
    {
        public Vector2 Pos, Vel;
        public bool Missile;
    }

    private struct Boss
    {
        public bool Active;
        public Vector2 Pos;
        public float Phase, Cool, Cool2, Flash, Enter;
        public int Hp, MaxHp;
    }

    private static readonly Dictionary<char, Color> Colours = new()
    {
        ['w'] = Pal.White, ['s'] = Pal.Silver, ['g'] = Pal.Grey, ['b'] = Pal.Sky, ['B'] = new Color(40, 80, 200), ['r'] = Pal.Red,
        ['o'] = Pal.Orange, ['y'] = Pal.Yellow, ['c'] = Pal.Cyan, ['m'] = Pal.Magenta, ['p'] = Pal.Purple, ['l'] = Pal.Lime,
        ['G'] = new Color(40, 140, 70), ['k'] = new Color(30, 30, 40),
    };

    private static readonly PixelArt ShipArt = new(
    [
        "..ss..............",
        ".sgss.............",
        ".sgsssssss........",
        "BBbbbbbbbbsswww...",
        "BBbbbbbbbbbbbbbccw",
        "BBbbbbbbbbsswww...",
        ".sgsssssss........",
        ".sgss.............",
        "..ss..............",
    ], Colours);

    private static readonly PixelArt[] FlyerArt =
    [
        new(["..mmm...", ".mmmmmm.", "mmyymmmm", "mmmmmmmm", ".mmmmmm.", "..mmm..."], Colours),
        new(["..ppp...", ".pppppp.", "ppyypppp", "pppppppp", ".pppppp.", "..ppp..."], Colours),
    ];

    private static readonly PixelArt ChaserArt = new(
    [
        "....ll..",
        "..llll..",
        "lllGGll.",
        "GGGGGGlll",
        "lllGGll.",
        "..llll..",
        "....ll..",
    ], Colours);

    private static readonly PixelArt RedArt = new(
    [
        "..rrrr..",
        ".rrooorr",
        "rroyyorr",
        "rroyyorr",
        ".rrooorr",
        "..rrrr..",
    ], Colours);

    private static readonly Vector2[] RockShape;

    static SpaceShip()
    {
        var rng = new Random(43);
        RockShape = new Vector2[9];
        for (int i = 0; i < RockShape.Length; i++)
            RockShape[i] = MathF2.FromAngle(i * MathF2.Tau / RockShape.Length, 0.8f + 0.2f * (float)rng.NextDouble());
    }

    // ------------------------------------------------------------------ state

    private readonly List<Enemy> _enemies = new();
    private readonly List<Bullet> _shots = new();
    private readonly List<Bullet> _bombs = new();
    private readonly List<Vector2> _caps = new();
    private readonly List<Vector2> _trail = new();
    private readonly int[] _formAlive = new int[64];
    private readonly int[] _formKilled = new int[64];
    private int _nextForm;
    private Boss _boss;
    private Vector2 _pos;
    private float _invuln, _respawn, _cool, _missileCool;
    private int _upgrades;           // how many of the upgrades are owned, in order
    private int _shield;
    private int _stage;
    private float _t;                // seconds into the stage
    private float _waveTimer, _rockTimer, _chaserTimer, _turretT, _redTimer;
    private float _banner;
    private string _bannerText;
    private float _clear;

    protected override void Start()
    {
        Lives = 3;
        _stage = 1;
        _pos = new Vector2(110, 190);
        NewStage();
    }

    private void NewStage()
    {
        Level = _stage;
        _t = 0;
        _enemies.Clear();
        _bombs.Clear();
        _caps.Clear();
        _boss = default;
        _waveTimer = 1.5f;
        _rockTimer = 0.5f;
        _chaserTimer = 4;
        _redTimer = 8;
        _turretT = TBase + 0.5f;
        _clear = 0;
        _invuln = 2;
        Banner($"STAGE {_stage}: ASTEROID BELT");
        Status = $"STAGE {_stage}  ASTEROID BELT";
    }

    private void Banner(string text)
    {
        _bannerText = text;
        _banner = 2.2f;
    }

    private bool Has(Upgrade u) => _upgrades > (int)u;

    private float Difficulty => 1 + 0.35f * (_stage - 1);

    // ------------------------------------------------------------------ terrain

    /// <summary>Ceiling and floor at stage time t (each screen column maps to a time).</summary>
    private void Terrain(float t, out float ceil, out float floor)
    {
        ceil = Top - 30;
        floor = 390;
        if (t >= TBase && t < TTunnel)
        {
            float k = MathF.Min(1, MathF.Min(t - TBase, TTunnel - t) / 1.2f);
            float block = MathF.Floor(t * 1.4f);
            float hf = 26 + 16 * (0.5f + 0.5f * MathF.Sin(block * 1.7f + _stage));
            float hc = 16 + 12 * (0.5f + 0.5f * MathF.Sin(block * 2.3f + 1 + _stage));
            floor = MathF2.Lerp(390, 360 - hf, k);
            ceil = MathF2.Lerp(Top - 30, Top + hc, k);
        }
        else if (t >= TTunnel && t < TTunnelEnd)
        {
            float k = MathF.Min(1, MathF.Min(t - TTunnel, TTunnelEnd - t) / 1.6f);
            float p = (t - TTunnel) / (TTunnelEnd - TTunnel);
            float centre = 190 + 70 * MathF.Sin(t * 0.55f + _stage) * (0.4f + 0.6f * p);
            float gap = MathF.Max(96, 175 - _stage * 8 - p * 45);
            floor = MathF2.Lerp(390, MathF.Min(352, centre + gap / 2), k);
            ceil = MathF2.Lerp(Top - 30, MathF.Max(Top + 6, centre - gap / 2), k);
        }
    }

    private void TerrainAtX(float x, out float ceil, out float floor) => Terrain(_t + x / Scroll, out ceil, out floor);

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        if (_banner > 0)
            _banner -= Dt;
        if (_invuln > 0)
            _invuln -= Dt;
        bool scrolling = _t < TBoss;
        _t += Dt;
        if (_t >= TBase && _t - Dt < TBase)
        {
            Banner("WARNING: ALIEN BASE");
            Status = $"STAGE {_stage}  ALIEN BASE";
        }
        if (_t >= TTunnel && _t - Dt < TTunnel)
        {
            Banner("ENTERING THE TUNNEL");
            Status = $"STAGE {_stage}  TUNNEL";
        }
        if (_t >= TBoss && _t - Dt < TBoss)
            StartBoss();

        if (_clear > 0)
        {
            _clear -= Dt;
            if (_clear <= 0)
            {
                _stage++;
                NewStage();
                Sound.Play(Sfx.LevelUp);
            }
        }

        if (_respawn > 0)
        {
            _respawn -= Dt;
            if (_respawn <= 0)
            {
                _pos = new Vector2(80, SafeY());
                _invuln = 2.5f;
                _trail.Clear();
            }
        }
        else
        {
            UpdatePlayer();
        }
        Spawn();
        UpdateEnemies(scrolling);
        UpdateBoss();
        UpdateShots();
        UpdateBombs();
        UpdateCaps(scrolling);
    }

    private float SafeY()
    {
        TerrainAtX(80, out float c, out float f);
        return MathF2.Clamp(190, c + 30, f - 30);
    }

    private void UpdatePlayer()
    {
        var input = new Vector2(In.AxisX, In.AxisY);
        if (input.LengthSquared() > 1)
            input.Normalize();
        _pos += input * 175 * Dt;
        _pos.X = MathF2.Clamp(_pos.X, 20, 600);
        _pos.Y = MathF2.Clamp(_pos.Y, Top + 10, 350);
        _trail.Insert(0, _pos);
        if (_trail.Count > 40)
            _trail.RemoveAt(_trail.Count - 1);

        // Exhaust.
        if (Tick % 2 == 0)
            Fx.Spark(_pos.X - 18, _pos.Y + Rand(-1.5f, 1.5f), -120 + input.X * 40, Rand(-10, 10), Pal.Lerp(Pal.Cyan, Pal.Orange, Rand(0, 1)), 0.2f, 1.6f);

        // Weapons.
        _cool -= Dt;
        _missileCool -= Dt;
        if ((In.Fire || In.FirePressed) && _cool <= 0)
        {
            _cool = 0.13f;
            FireFrom(_pos + new Vector2(18, 0));
            for (int i = 0; i < OptionCount; i++)
                FireFrom(OptionPos(i));
            Sound.Play(Sfx.Shoot, 0.3f, 0.35f);
        }
        if (Has(Upgrade.Missile) && In.Fire && _missileCool <= 0)
        {
            _missileCool = 0.6f;
            _shots.Add(new Bullet { Pos = _pos + new Vector2(4, 6), Vel = new Vector2(170, 170), Missile = true });
            if (OptionCount > 0)
                _shots.Add(new Bullet { Pos = OptionPos(0) + new Vector2(0, 6), Vel = new Vector2(170, 170), Missile = true });
            Sound.Play(Sfx.Whoosh, 0.6f, 0.25f);
        }

        // Walls.
        TerrainAtX(_pos.X, out float ceil, out float floor);
        if (_pos.Y - 6 < ceil || _pos.Y + 6 > floor)
        {
            _pos.Y = _pos.Y - 6 < ceil ? ceil + 10 : floor - 10;
            Damage(true);
        }
    }

    private int OptionCount => Has(Upgrade.Option2) ? 2 : Has(Upgrade.Option1) ? 1 : 0;

    private Vector2 OptionPos(int i)
    {
        int idx = Math.Min(_trail.Count - 1, 14 * (i + 1));
        var p = idx >= 0 ? _trail[idx] : _pos;
        return p + new Vector2(-10, (i == 0 ? -1 : 1) * 16);
    }

    private void FireFrom(Vector2 p)
    {
        _shots.Add(new Bullet { Pos = p, Vel = new Vector2(560, 0) });
        if (Has(Upgrade.Double))
            _shots.Add(new Bullet { Pos = p, Vel = MathF2.FromAngle(-0.45f, 520) });
    }

    private void Damage(bool wall = false)
    {
        if (_invuln > 0 || _respawn > 0)
            return;
        if (_shield > 0)
        {
            _shield--;
            _invuln = 0.8f;
            Sound.Play(Sfx.Hit, 0.3f);
            Fx.Burst(_pos.X, _pos.Y, Pal.Cyan, 16, 90, 0.4f);
            if (_shield == 0)
                _upgrades = Math.Min(_upgrades, (int)Upgrade.Shield);
            return;
        }
        Fx.Explode(_pos.X, _pos.Y, 1.6f);
        Fx.Burst(_pos.X, _pos.Y, Pal.Sky, 24, 150, 0.8f);
        Sound.Play(Sfx.BigExplode);
        _bombs.Clear();
        _respawn = 1.6f;
        _upgrades /= 2;
        _shield = 0;
        if (LoseLife())
            EndGame(false, wall ? "You hit the wall" : null);
    }

    // ------------------------------------------------------------------ spawning

    private void Spawn()
    {
        if (_t >= TBoss || _clear > 0)
            return;
        float d = Difficulty;
        bool belt = _t < TBase;
        bool tunnel = _t >= TTunnel;

        _waveTimer -= Dt;
        if (_waveTimer <= 0)
        {
            _waveTimer = (tunnel ? 6f : 4f) / d + Rand(0, 1);
            int form = _nextForm = (_nextForm + 1) % _formAlive.Length;
            int n = 5;
            _formAlive[form] = n;
            _formKilled[form] = 0;
            TerrainAtX(640, out float c, out float f);
            float baseY = MathF2.Clamp(Rand(70, 300), c + 40, f - 40);
            for (int i = 0; i < n; i++)
                _enemies.Add(new Enemy
                {
                    Kind = Kind.Flyer, Pos = new Vector2(660 + i * 30, baseY), Vel = new Vector2(-120 - 15 * d, 0), Base = baseY,
                    Phase = i * 0.5f, Hp = 1, Formation = form,
                });
        }
        if (belt)
        {
            _rockTimer -= Dt;
            if (_rockTimer <= 0)
            {
                _rockTimer = Rand(0.5f, 1.1f) / d;
                bool big = Chance(0.35f);
                _enemies.Add(new Enemy
                {
                    Kind = big ? Kind.Rock : Kind.Pebble, Pos = new Vector2(670, Rand(40, 340)), Vel = new Vector2(-Rand(70, 130) * d, Rand(-25, 25)),
                    Hp = big ? 4 : 1, Phase = Rand(0, 6), Base = Rand(-2, 2), Formation = -1,
                });
            }
        }
        else
        {
            _chaserTimer -= Dt;
            if (_chaserTimer <= 0)
            {
                _chaserTimer = Rand(2f, 3.5f) / d;
                TerrainAtX(640, out float c, out float f);
                _enemies.Add(new Enemy
                {
                    Kind = Kind.Chaser, Pos = new Vector2(660, MathF2.Clamp(Rand(60, 320), c + 20, f - 20)), Vel = new Vector2(-150 - 20 * d, 0),
                    Hp = 1, Formation = -1,
                });
            }
        }
        _redTimer -= Dt;
        if (_redTimer <= 0)
        {
            _redTimer = Rand(9, 13);
            TerrainAtX(640, out float c, out float f);
            _enemies.Add(new Enemy
            {
                Kind = Kind.Red, Pos = new Vector2(660, MathF2.Clamp(Rand(60, 320), c + 30, f - 30)), Vel = new Vector2(-90, 0), Hp = 2,
                Phase = 0, Formation = -1,
            });
        }
        // Turrets ride on the base's floor and ceiling.
        while (_turretT < TTunnel - 1 && _turretT < _t + 680 / Scroll)
        {
            float x = (_turretT - _t) * Scroll;
            bool ceilingGun = ((int)(_turretT * 10)) % 2 == 0;
            Terrain(_turretT, out float c, out float f);
            _enemies.Add(new Enemy
            {
                Kind = Kind.Turret, Pos = new Vector2(x, ceilingGun ? c + 7 : f - 7), Hp = 3, Ceiling = ceilingGun, Cool = Rand(1, 2.5f),
                Formation = -1,
            });
            _turretT += Rand(1.1f, 1.9f) / MathF.Sqrt(Difficulty);
        }
    }

    private void UpdateEnemies(bool scrolling)
    {
        float d = Difficulty;
        for (int i = _enemies.Count - 1; i >= 0; i--)
        {
            var e = _enemies[i];
            e.Phase += Dt;
            if (e.Flash > 0)
                e.Flash -= Dt;
            switch (e.Kind)
            {
                case Kind.Flyer:
                    e.Pos.X += e.Vel.X * Dt;
                    e.Pos.Y = e.Base + MathF.Sin(e.Phase * 3) * 45;
                    break;
                case Kind.Chaser:
                    e.Pos.X += e.Vel.X * Dt;
                    e.Pos.Y += MathF2.Clamp(_pos.Y - e.Pos.Y, -1, 1) * 60 * Dt;
                    break;
                case Kind.Red:
                    e.Pos.X += e.Vel.X * Dt;
                    e.Pos.Y += MathF.Sin(e.Phase * 2) * 60 * Dt;
                    break;
                case Kind.Turret:
                    e.Pos.X -= (scrolling ? Scroll : 0) * Dt;
                    e.Cool -= Dt;
                    if (e.Cool <= 0 && e.Pos.X < 620 && e.Pos.X > _pos.X + 30 && _respawn <= 0)
                    {
                        e.Cool = Rand(1.6f, 2.6f) / d;
                        var aim = Vector2.Normalize(_pos - e.Pos);
                        _bombs.Add(new Bullet { Pos = e.Pos, Vel = aim * (130 + 15 * d) });
                        Sound.Play(Sfx.Zap, 0.1f, 0.3f);
                    }
                    break;
                default:
                    e.Pos += e.Vel * Dt;
                    break;
            }
            // Flyers and reds sometimes shoot back on later stages.
            if (e.Kind is Kind.Flyer or Kind.Red && _stage >= 2 && Chance(0.002f * d) && e.Pos.X > _pos.X + 60 && e.Pos.X < 630)
                _bombs.Add(new Bullet { Pos = e.Pos, Vel = Vector2.Normalize(_pos - e.Pos) * 140 });

            if (e.Pos.X < -40 || e.Pos.Y < -40 || e.Pos.Y > 400)
            {
                if (e.Formation >= 0)
                    _formAlive[e.Formation]--;
                _enemies.RemoveAt(i);
                continue;
            }
            _enemies[i] = e;
            if (_respawn <= 0 && MathF2.Circles(e.Pos, Radius(e.Kind), _pos, 7))
            {
                if (e.Kind != Kind.Turret)
                    Kill(i, false);
                Damage();
            }
        }
    }

    private static float Radius(Kind k) => k switch { Kind.Rock => 15, Kind.Pebble => 7, Kind.Turret => 9, _ => 8 };

    private void Kill(int i, bool byPlayer)
    {
        var e = _enemies[i];
        _enemies.RemoveAt(i);
        var col = e.Kind switch
        {
            Kind.Flyer => Pal.Magenta, Kind.Chaser => Pal.Lime, Kind.Red => Pal.Red, Kind.Turret => Pal.Orange, _ => new Color(170, 150, 130),
        };
        Fx.Burst(e.Pos.X, e.Pos.Y, col, 18, 120, 0.5f, 2.2f);
        if (e.Kind is Kind.Rock or Kind.Pebble)
            Sound.Play(Sfx.Crack, Rand(-0.3f, 0.2f), 0.6f);
        else
            Sound.Play(Sfx.Explode, Rand(-0.2f, 0.3f), 0.7f);
        if (!byPlayer)
        {
            if (e.Formation >= 0)
                _formAlive[e.Formation]--;
            return;
        }
        int pts = e.Kind switch { Kind.Flyer => 100, Kind.Chaser => 150, Kind.Rock => 50, Kind.Pebble => 30, Kind.Turret => 200, _ => 300 };
        AddScore(pts, e.Pos.X, e.Pos.Y - 10);
        if (e.Kind == Kind.Rock)
            for (int k = -1; k <= 1; k += 2)
                _enemies.Add(new Enemy { Kind = Kind.Pebble, Pos = e.Pos, Vel = e.Vel + new Vector2(-20, k * 60), Hp = 1, Formation = -1 });
        if (e.Kind == Kind.Red)
            _caps.Add(e.Pos);
        if (e.Formation >= 0)
        {
            _formAlive[e.Formation]--;
            _formKilled[e.Formation]++;
            if (_formKilled[e.Formation] == 5)
            {
                _caps.Add(e.Pos);
                AddScore(500, e.Pos.X, e.Pos.Y - 24, Pal.Cyan);
                Sound.Play(Sfx.Bonus, 0, 0.6f);
            }
        }
    }

    private void StartBoss()
    {
        int hp = 60 + 30 * (_stage - 1);
        _boss = new Boss { Active = true, Pos = new Vector2(760, 190), Hp = hp, MaxHp = hp, Cool = 2, Cool2 = 4, Enter = 0 };
        Banner("WARNING! BOSS APPROACHING");
        Status = $"STAGE {_stage}  BOSS";
        Sound.Play(Sfx.Alarm);
    }

    private void UpdateBoss()
    {
        if (!_boss.Active)
            return;
        ref var b = ref _boss;
        b.Phase += Dt;
        if (b.Flash > 0)
            b.Flash -= Dt;
        if (b.Pos.X > 520)
        {
            b.Pos.X -= 80 * Dt;
            Sound.Loop(LoopSfx.Hum, true, -0.5f, 0.5f);
            return;
        }
        float d = Difficulty + MathF.Max(0, b.Phase - 30) * 0.08f;
        b.Pos.Y = 190 + MathF.Sin(b.Phase * 0.7f) * 95;
        Sound.Loop(LoopSfx.Hum, true, -0.6f + (1 - b.Hp / (float)b.MaxHp) * 0.6f, 0.35f);
        b.Cool -= Dt;
        if (b.Cool <= 0)
        {
            // A spread of plasma.
            b.Cool = MathF.Max(0.9f, 2.0f - 0.2f * d);
            int n = 3 + Math.Min(4, _stage);
            for (int i = 0; i < n; i++)
            {
                float a = MathF.PI + (i - (n - 1) / 2f) * 0.22f;
                _bombs.Add(new Bullet { Pos = b.Pos + new Vector2(-40, 0), Vel = MathF2.FromAngle(a, 150 + 10 * d) });
            }
            Sound.Play(Sfx.Zap, -0.3f, 0.6f);
        }
        b.Cool2 -= Dt;
        if (b.Cool2 <= 0)
        {
            // Aimed shots from the two gun pods.
            b.Cool2 = MathF.Max(0.5f, 1.3f - 0.1f * d);
            for (int k = -1; k <= 1; k += 2)
            {
                var gp = b.Pos + new Vector2(-10, k * 44);
                _bombs.Add(new Bullet { Pos = gp, Vel = Vector2.Normalize(_pos - gp) * (170 + 10 * d) });
            }
            Sound.Play(Sfx.Laser, -0.4f, 0.4f);
        }
        if (_respawn <= 0 && MathF.Abs(_pos.X - b.Pos.X) < 46 && MathF.Abs(_pos.Y - b.Pos.Y) < 50)
            Damage();
    }

    private void UpdateShots()
    {
        for (int i = _shots.Count - 1; i >= 0; i--)
        {
            var s = _shots[i];
            if (s.Missile)
            {
                TerrainAtX(s.Pos.X, out _, out float floor);
                // Missiles skim along the floor once they reach it.
                if (s.Pos.Y > floor - 8 || s.Pos.Y > 346)
                    s.Vel = new Vector2(260, 0);
            }
            s.Pos += s.Vel * Dt;
            bool gone = s.Pos.X > 650 || s.Pos.Y < Top - 10 || s.Pos.Y > 370;
            if (!gone)
            {
                TerrainAtX(s.Pos.X, out float c, out float f);
                if (s.Pos.Y < c || s.Pos.Y > f + 2)
                {
                    gone = true;
                    Fx.Burst(s.Pos.X, s.Pos.Y, Pal.Yellow, 4, 40, 0.2f, 1.2f);
                }
            }
            for (int k = _enemies.Count - 1; k >= 0 && !gone; k--)
            {
                var e = _enemies[k];
                if (!MathF2.Circles(s.Pos, 3, e.Pos, Radius(e.Kind) + 2))
                    continue;
                gone = true;
                e.Hp -= s.Missile ? 2 : 1;
                e.Flash = 0.08f;
                _enemies[k] = e;
                if (e.Hp <= 0)
                    Kill(k, true);
                else
                    Sound.Play(Sfx.Hit, 0.4f, 0.35f);
            }
            if (!gone && _boss.Active && _boss.Pos.X < 600)
            {
                var core = _boss.Pos + new Vector2(-50, 0);
                if (MathF2.Circles(s.Pos, 3, core, 14))
                {
                    gone = true;
                    _boss.Hp -= s.Missile ? 3 : 1;
                    _boss.Flash = 0.08f;
                    Sound.Play(Sfx.Hit, 0.1f, 0.4f);
                    Fx.Burst(s.Pos.X, s.Pos.Y, Pal.Cyan, 4, 60, 0.2f, 1.5f);
                    if (_boss.Hp <= 0)
                        BossDown();
                }
                else if (s.Pos.X > _boss.Pos.X - 40 && MathF.Abs(s.Pos.X - _boss.Pos.X) < 46 && MathF.Abs(s.Pos.Y - _boss.Pos.Y) < 56)
                {
                    gone = true;
                    Fx.Burst(s.Pos.X, s.Pos.Y, Pal.Silver, 3, 40, 0.15f, 1.2f);
                }
            }
            if (gone)
                _shots.RemoveAt(i);
            else
                _shots[i] = s;
        }
    }

    private void BossDown()
    {
        var p = _boss.Pos;
        _boss.Active = false;
        for (int i = 0; i < 6; i++)
            Fx.Explode(p.X + Rand(-40, 40), p.Y + Rand(-40, 40), 1.8f);
        Fx.Shake(8, 0.8f);
        Sound.Play(Sfx.BigExplode);
        Sound.Play(Sfx.BigExplode, -0.5f);
        AddScore(5000 * _stage, p.X, p.Y - 30, Pal.Gold);
        Banner("STAGE CLEAR!");
        _bombs.Clear();
        _clear = 3.5f;
    }

    private void UpdateBombs()
    {
        for (int i = _bombs.Count - 1; i >= 0; i--)
        {
            var b = _bombs[i];
            b.Pos += b.Vel * Dt;
            if (b.Pos.X < -10 || b.Pos.X > 660 || b.Pos.Y < Top - 10 || b.Pos.Y > 370)
            {
                _bombs.RemoveAt(i);
                continue;
            }
            _bombs[i] = b;
            if (_respawn <= 0 && MathF2.Circles(b.Pos, 3, _pos, 5))
            {
                _bombs.RemoveAt(i);
                Damage();
                if (_respawn > 0 || IsOver)
                    return;
            }
        }
    }

    private static readonly string[] UpgradeNames = ["DOUBLE", "MISSILE", "OPTION", "OPTION 2", "SHIELD"];

    private void UpdateCaps(bool scrolling)
    {
        for (int i = _caps.Count - 1; i >= 0; i--)
        {
            var c = _caps[i] + new Vector2(-(scrolling ? 50 : 30) * Dt, MathF.Sin(Time * 3 + i) * 0.3f);
            if (c.X < -20)
            {
                _caps.RemoveAt(i);
                continue;
            }
            _caps[i] = c;
            if (_respawn <= 0 && MathF2.Circles(c, 10, _pos, 10))
            {
                _caps.RemoveAt(i);
                Sound.Play(Sfx.PowerUp);
                if (_upgrades < UpgradeNames.Length)
                {
                    Fx.Float(UpgradeNames[_upgrades] + "!", c.X, c.Y - 12, Pal.Cyan);
                    _upgrades++;
                    if (_upgrades == UpgradeNames.Length)
                        _shield = 3;
                }
                else
                {
                    _shield = Math.Min(3, _shield + 1);
                    AddScore(1000, c.X, c.Y - 12, Pal.Gold);
                }
            }
        }
    }

    // ------------------------------------------------------------------ drawing

    public override void Draw(Gfx g)
    {
        float scroll = _t < TBoss ? _t * Scroll : TBoss * Scroll + (_t - TBoss) * 20;
        DrawBackdrop(g, Screen.Bounds, scroll, Time, _stage);
        DrawTerrain(g);

        foreach (var c in _caps)
        {
            float pulse = 0.6f + 0.4f * MathF.Sin(Time * 8);
            g.Glow(c, 18, Pal.Orange, 0.6f * pulse);
            g.RoundRect(c.X - 9, c.Y - 6, 18, 12, 6, Pal.Orange);
            g.RoundRect(c.X - 7, c.Y - 5, 14, 4, 2, Pal.Yellow * 0.7f);
            g.Text("P", c.X, c.Y - 4, 1, Pal.White, Align.Center);
        }

        foreach (var e in _enemies)
            DrawEnemy(g, e);
        if (_boss.Active)
            DrawBoss(g, _boss, Time);

        foreach (var s in _shots)
        {
            if (s.Missile)
            {
                g.Glow(s.Pos, 7, Pal.Orange, 0.6f);
                g.RotatedRect(s.Pos, 8, 3, MathF2.Angle(s.Vel), Pal.Silver);
            }
            else
            {
                g.Glow(s.Pos, 8, Pal.Cyan, 0.6f);
                g.RotatedRect(s.Pos, 12, 2.4f, MathF2.Angle(s.Vel), Pal.White);
            }
        }
        foreach (var b in _bombs)
        {
            g.Glow(b.Pos, 9, Pal.Pink, 0.7f);
            g.Circle(b.Pos.X, b.Pos.Y, 2.8f, Pal.Pink);
            g.Circle(b.Pos.X, b.Pos.Y, 1.4f, Pal.White);
        }

        if (_respawn <= 0 && !IsOver && (_invuln <= 0 || (int)(_invuln * 12) % 2 == 0))
        {
            for (int i = 0; i < OptionCount; i++)
            {
                var o = OptionPos(i);
                g.Glow(o, 12, Pal.Orange, 0.8f);
                g.Circle(o.X, o.Y, 4.5f, Pal.Orange);
                g.Circle(o.X - 1, o.Y - 1, 2, Pal.Yellow);
            }
            DrawShip(g, _pos, 1, Time);
            if (_shield > 0)
            {
                g.Arc(_pos.X + 4, _pos.Y, 18, 2.5f, -1.1f, 1.1f, Pal.Cyan * (0.4f + 0.2f * _shield));
                g.Glow(_pos.X + 14, _pos.Y, 16, Pal.Cyan, 0.2f * _shield);
            }
        }

        DrawHud(g);
    }

    private static void DrawBackdrop(Gfx g, RectF r, float scroll, float time, int stage)
    {
        float s = r.H / 360f;
        var top = stage % 3 == 1 ? new Color(4, 6, 24) : stage % 3 == 2 ? new Color(20, 4, 24) : new Color(2, 16, 22);
        g.GradientV(r.X, r.Y, r.W, r.H, top, new Color(8, 8, 30));
        Backdrops.Stars(g, scroll / 70f, 70 * 0.5f, 43, r, 120);
        // Nebula and a distant planet drifting by slowly.
        g.Glow(r.X + Backdrops.Mod(500 - scroll * 0.05f, r.W + 300) - 150, r.Y + r.H * 0.35f, 180 * s, stage % 2 == 0 ? Pal.Magenta : Pal.Purple, 0.22f);
        float px = r.X + Backdrops.Mod(r.W * 0.7f - scroll * 0.1f, r.W + 200) - 100;
        g.Glow(px, r.Y + r.H * 0.7f, 70 * s, Pal.Sky, 0.25f);
        float py = r.Y + r.H * 0.7f;
        g.Circle(px, py, 36 * s, new Color(30, 55, 110));
        g.Circle(px - 5 * s, py - 5 * s, 30 * s, new Color(45, 80, 150));
        g.Circle(px - 11 * s, py - 11 * s, 20 * s, new Color(60, 105, 180));
        g.Ellipse(px - 4 * s, py + 6 * s, 22 * s, 3 * s, new Color(80, 130, 200) * 0.5f);
        g.Ellipse(px, py, 60 * s, 7 * s, new Color(150, 170, 220) * 0.25f);
    }

    private void DrawTerrain(Gfx g)
    {
        const float step = 6;
        for (float x = 0; x < 640; x += step)
        {
            float t0 = _t + x / Scroll;
            Terrain(t0, out float c0, out float f0);
            Terrain(t0 + step / Scroll, out float c1, out float f1);
            bool tunnel = t0 >= TTunnel;
            var rock = tunnel ? new Color(70, 50, 90) : new Color(60, 70, 90);
            var edge = tunnel ? Pal.Magenta : Pal.Cyan;
            if (f0 < 362 || f1 < 362)
            {
                g.GradientV(x, MathF.Min(f0, f1), step + 0.5f, 360 - MathF.Min(f0, f1) + 2, Pal.Lighten(rock, 0.15f), Pal.Darken(rock, 0.5f));
                g.Line(x, f0, x + step, f1, 2, edge * 0.8f);
                if (!tunnel && ((int)((t0 * Scroll) / 18)) % 3 == 0)
                    g.Rect(x, MathF.Min(f0, f1) + 8, step, 2, Pal.Yellow * 0.4f);
            }
            if (c0 > Top - 4 || c1 > Top - 4)
            {
                g.GradientV(x, Top, step + 0.5f, MathF.Max(c0, c1) - Top, Pal.Darken(rock, 0.5f), Pal.Lighten(rock, 0.15f));
                g.Line(x, c0, x + step, c1, 2, edge * 0.8f);
            }
        }
    }

    private void DrawEnemy(Gfx g, Enemy e)
    {
        Color? tint = e.Flash > 0 ? Pal.White : null;
        switch (e.Kind)
        {
            case Kind.Flyer:
                g.Glow(e.Pos, 14, Pal.Magenta, 0.35f);
                g.PixelsCentered(FlyerArt[(int)(e.Phase * 4) % 2], e.Pos.X, e.Pos.Y, 2.2f, false, tint);
                break;
            case Kind.Chaser:
                g.Glow(e.Pos, 14, Pal.Lime, 0.35f);
                g.PixelsCentered(ChaserArt, e.Pos.X, e.Pos.Y, 2.2f, true, tint);
                break;
            case Kind.Red:
                g.Glow(e.Pos, 18, Pal.Red, 0.5f + 0.2f * MathF.Sin(e.Phase * 10));
                g.PixelsCentered(RedArt, e.Pos.X, e.Pos.Y, 2.4f, false, tint);
                break;
            case Kind.Turret:
            {
                float dir = e.Ceiling ? 1 : -1;
                g.Rect(e.Pos.X - 10, e.Pos.Y - (e.Ceiling ? 7 : -1), 20, 7, tint ?? new Color(120, 90, 60));
                g.Circle(e.Pos.X, e.Pos.Y, 7, tint ?? Pal.Orange);
                var aim = Vector2.Normalize(_pos - e.Pos + new Vector2(0.01f, 0));
                if (aim.Y * dir < 0)
                    aim = new Vector2(aim.X, 0);
                g.Line(e.Pos, e.Pos + aim * 12, 3, Pal.DarkGrey);
                g.Glow(e.Pos, 10, Pal.Orange, 0.3f);
                break;
            }
            default:
            {
                float r = Radius(e.Kind);
                float a = e.Phase * e.Base;
                g.Shape(RockShape, e.Pos, a, r, tint ?? new Color(90, 78, 70));
                g.Shape(RockShape, e.Pos + new Vector2(-r * 0.15f, -r * 0.15f), a, r * 0.7f, tint ?? new Color(130, 115, 100));
                break;
            }
        }
    }

    private static readonly Vector2[] ArmUp = [new(-10, 0), new(30, -20), new(20, -52), new(-20, -48)];
    private static readonly Vector2[] ArmDown = [new(-10, 0), new(-20, 48), new(20, 52), new(30, 20)];
    private static readonly Vector2[] BossHull = [new(-46, 0), new(-10, -30), new(44, -24), new(44, 24), new(-10, 30)];

    private static void DrawBoss(Gfx g, Boss b, float time)
    {
        var p = b.Pos;
        var hull = b.Flash > 0 ? Pal.White : new Color(90, 70, 120);
        g.Glow(p, 90, Pal.Purple, 0.3f);
        // Arms with gun pods.
        for (int k = -1; k <= 1; k += 2)
        {
            g.Shape(k < 0 ? ArmUp : ArmDown, p, 0, 1, Pal.Darken(hull, 0.2f));
            g.Circle(p.X - 10, p.Y + k * 44, 8, Pal.Silver);
            g.Circle(p.X - 14, p.Y + k * 44, 4, Pal.Red);
        }
        g.Shape(BossHull, p, 0, 1, hull);
        g.Rect(p.X - 4, p.Y - 26, 40, 4, Pal.Lighten(hull, 0.2f));
        g.Rect(p.X - 4, p.Y + 22, 40, 4, Pal.Darken(hull, 0.3f));
        // The core.
        float hp = b.Hp / (float)b.MaxHp;
        var coreCol = Pal.Lerp(Pal.Red, Pal.Cyan, hp);
        float pulse = 0.7f + 0.3f * MathF.Sin(time * 8);
        g.Rect(p.X - 50, p.Y - 3, 12, 6, Pal.DarkGrey);
        g.Glow(p.X - 50, p.Y, 32, coreCol, 0.8f * pulse);
        g.Circle(p.X - 50, p.Y, 12, Pal.Darken(coreCol, 0.4f));
        g.Circle(p.X - 50, p.Y, 9, coreCol);
        g.Circle(p.X - 53, p.Y - 3, 3.5f, Pal.White * 0.8f);
        // Engines.
        for (int k = -1; k <= 1; k++)
            g.Glow(p.X + 48, p.Y + k * 14, 14, Pal.Orange, 0.6f + 0.2f * MathF.Sin(time * 20 + k));
    }

    private static void DrawShip(Gfx g, Vector2 p, float s, float time)
    {
        float f = 0.8f + 0.2f * MathF.Sin(time * 40);
        g.Glow(p.X - 18 * s, p.Y, 12 * s, Pal.Orange, 0.7f);
        g.Triangle(new Vector2(p.X - 16 * s, p.Y - 3 * s), new Vector2(p.X - 16 * s, p.Y + 3 * s), new Vector2(p.X - (24 + 6 * f) * s, p.Y), Pal.Orange);
        g.Glow(p, 20 * s, Pal.Sky, 0.25f);
        g.PixelsCentered(ShipArt, p.X, p.Y, 2f * s);
    }

    private void DrawHud(Gfx g)
    {
        // Power-up bar.
        float x = 8, y = 340;
        for (int i = 0; i < UpgradeNames.Length; i++)
        {
            bool on = _upgrades > i;
            string label = i == 3 ? "OPT 2" : UpgradeNames[i] == "OPTION" ? "OPTION" : UpgradeNames[i];
            float w = Gfx.TextWidth(label, 1) + 10;
            g.RoundRect(x, y, w, 14, 3, on ? Pal.Darken(Pal.Orange, 0.3f) : Pal.Panel * 0.8f);
            g.Text(label, x + 5, y + 3, 1, on ? Pal.Yellow : Pal.Grey);
            x += w + 4;
        }
        if (_shield > 0)
            for (int i = 0; i < _shield; i++)
                g.Circle(x + 6 + i * 10, y + 7, 3.5f, Pal.Cyan);

        // Stage progress.
        float bx = 440, bw = 190;
        g.RoundRect(bx, y + 4, bw, 6, 3, Pal.Panel * 0.8f);
        float k = MathF2.Clamp(_t / TBoss, 0, 1);
        g.RoundRect(bx, y + 4, bw * k, 6, 3, Pal.Sky);
        g.Rect(bx + bw * TBase / TBoss, y + 2, 1, 10, Pal.White * 0.6f);
        g.Rect(bx + bw * TTunnel / TBoss, y + 2, 1, 10, Pal.White * 0.6f);
        g.Circle(bx + bw, y + 7, 4, _boss.Active ? Pal.Red : Pal.Grey);

        if (_boss.Active && _boss.Pos.X < 600)
        {
            g.RoundRect(220, 30, 200, 8, 3, Pal.Panel * 0.8f);
            g.RoundRect(220, 30, 200 * _boss.Hp / (float)_boss.MaxHp, 8, 3, Pal.Red);
            g.Text("BOSS", 216, 30, 1, Pal.Red, Align.Right);
        }
        if (_banner > 0 && _bannerText != null)
        {
            float a = MathF.Min(1, _banner * 2);
            g.TextShadow(_bannerText, 320, 150, 2.2f, (_bannerText.StartsWith("WARN") ? Pal.Red : Pal.Yellow) * a, Align.Center);
        }
    }

    // ------------------------------------------------------------------ icon

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        float s = r.H / 70f;
        DrawBackdrop(g, r, time * 60, time, 1);
        // Tunnel walls.
        for (float x = r.X; x < r.Right; x += 4 * s)
        {
            float t = (x - r.X) / s * 0.03f + time * 1.5f;
            float c = r.Y + (8 + 5 * MathF.Sin(t)) * s, f = r.Bottom - (8 + 5 * MathF.Sin(t * 1.3f + 2)) * s;
            g.GradientV(x, r.Y, 4.5f * s, c - r.Y, new Color(40, 30, 60), new Color(90, 60, 110));
            g.GradientV(x, f, 4.5f * s, r.Bottom - f, new Color(90, 60, 110), new Color(40, 30, 60));
            g.Rect(x, c - 1 * s, 4.5f * s, 1.2f * s, Pal.Magenta * 0.8f);
            g.Rect(x, f, 4.5f * s, 1.2f * s, Pal.Magenta * 0.8f);
        }
        var sp = new Vector2(r.X + r.W * 0.25f, r.CenterY + MathF.Sin(time * 1.4f) * 8 * s);
        // Shots.
        for (int i = 0; i < 4; i++)
        {
            float k = (time * 1.6f + i * 0.25f) % 1;
            var p = new Vector2(sp.X + 14 * s + k * r.W * 0.7f, sp.Y);
            g.Glow(p, 5 * s, Pal.Cyan, 0.6f);
            g.Rect(p.X - 3 * s, p.Y - 0.6f * s, 6 * s, 1.2f * s, Pal.White);
        }
        var o = sp + new Vector2(-12 * s, -10 * s + MathF.Sin(time * 3) * 2 * s);
        g.Glow(o, 6 * s, Pal.Orange, 0.8f);
        g.Circle(o.X, o.Y, 2.4f * s, Pal.Orange);
        DrawShip(g, sp, 0.8f * s, time);
        // Enemies in a wave.
        for (int i = 0; i < 4; i++)
        {
            float ex = r.X + r.W * (0.62f + i * 0.09f);
            float ey = r.CenterY + MathF.Sin(time * 3 + i * 0.8f) * 12 * s;
            g.Glow(ex, ey, 7 * s, Pal.Magenta, 0.4f);
            g.PixelsCentered(FlyerArt[(int)(time * 4) % 2], ex, ey, 1.1f * s);
        }
    }

    // ------------------------------------------------------------------ autopilot

    public override void AutoPlay(Controls c)
    {
        c.Fire = true;
        c.FirePressed = Tick % 8 == 0;
        if (_respawn > 0)
            return;
        // Aim for the nearest enemy (or the boss core), then pick the safest nearby move.
        float targetY = 190;
        float best = float.MaxValue;
        foreach (var e in _enemies)
        {
            if (e.Pos.X < _pos.X + 10 || e.Kind == Kind.Turret)
                continue;
            float d = e.Pos.X - _pos.X;
            if (d < best)
            {
                best = d;
                targetY = e.Pos.Y;
            }
        }
        if (_boss.Active)
            targetY = _boss.Pos.Y;
        foreach (var cap in _caps)
            if (cap.X > _pos.X - 20)
                targetY = cap.Y;
        float targetX = _boss.Active ? 140 : 120;

        float bestCost = float.MaxValue;
        Vector2 bestDir = Vector2.Zero;
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                var dir = new Vector2(dx, dy);
                if (dir.LengthSquared() > 1)
                    dir.Normalize();
                float cost = 0;
                for (int step = 1; step <= 4; step++)
                {
                    float t = step * 0.12f;
                    var p = _pos + dir * 175 * MathF.Min(t, 0.3f);
                    TerrainAtX(p.X, out float ce, out float fl);
                    float margin = MathF.Min(p.Y - ce, fl - p.Y);
                    if (margin < 18)
                        cost += (18 - margin) * 30;
                    if (p.Y < Top + 12 || p.Y > 348)
                        cost += 200;
                    foreach (var b in _bombs)
                    {
                        float d2 = Vector2.DistanceSquared(b.Pos + b.Vel * t, p);
                        if (d2 < 22 * 22)
                            cost += (22 * 22 - d2) * 2;
                    }
                    foreach (var e in _enemies)
                    {
                        var ep = e.Pos + (e.Kind == Kind.Turret ? new Vector2(-Scroll * t, 0) : e.Kind == Kind.Flyer ? new Vector2(e.Vel.X * t, 0) : e.Vel * t);
                        float rr = Radius(e.Kind) + 16;
                        float d2 = Vector2.DistanceSquared(ep, p);
                        if (d2 < rr * rr)
                            cost += (rr * rr - d2) * 2;
                    }
                    if (_boss.Active && MathF.Abs(p.X - _boss.Pos.X) < 70 && MathF.Abs(p.Y - _boss.Pos.Y) < 70)
                        cost += 300;
                }
                var end = _pos + dir * 50;
                cost += MathF.Abs(end.Y - targetY) * 1.2f + MathF.Abs(end.X - targetX) * 0.6f;
                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestDir = dir;
                }
            }
        c.SetDirections(bestDir.X, bestDir.Y);
    }
}
