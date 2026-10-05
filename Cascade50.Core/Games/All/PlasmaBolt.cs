using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 32 Plasma Bolt: a vertical scrolling shoot-'em-up over an alien world. Hold fire for rapid
/// shots, charge and release a piercing plasma bolt, grab spread and shield power-ups, and face a
/// mothership every fourth wave.
/// </summary>
public sealed class PlasmaBolt : MiniGame, Capture.ICaptureHints
{
    public override int Number => 32;
    public override string Title => "Plasma Bolt";
    public override Category Category => Category.Shooter;
    public override string Tagline => "Charge your plasma cannon and blast through alien waves and motherships.";
    public override Color Accent => Pal.Magenta;
    public int CaptureTicks => 480;

    public override string[] HowToPlay =>
    [
        "Hold FIRE for rapid shots. Hold it (or ALT) to charge, then let go to fire a plasma bolt that pierces everything.",
        "Ground turrets shoot back and a mothership attacks every 4th wave. Grab S for spread shot, H for a shield.",
    ];

    public override string[] DesktopControls => ["ARROWS / WASD move, SPACE fire.", "Hold X to charge, let go to bolt."];
    public override string[] TouchControls => ["Stick moves, hold FIRE to shoot.", "Hold BOLT to charge, let go to fire."];
    public override Pad Pad => Pad.Stick | Pad.Fire | Pad.Alt;
    public override string AltLabel => "BOLT";

    private const float ScrollSpeed = 34, Tile = 32;
    private const float MinX = 12, MaxX = 628, MinY = Screen.HudHeight + 20, MaxY = 346;

    // ------------------------------------------------------------------ art

    private static readonly Dictionary<char, Color> Colours = new()
    {
        ['w'] = Pal.White, ['W'] = Pal.Ice, ['c'] = Pal.Sky, ['b'] = new Color(40, 70, 170), ['s'] = Pal.Silver,
        ['r'] = Pal.Red, ['R'] = Pal.Orange, ['o'] = Pal.Orange, ['m'] = new Color(170, 30, 160), ['M'] = Pal.Magenta,
        ['y'] = Pal.Yellow, ['g'] = Pal.Grey, ['G'] = Pal.Green, ['l'] = Pal.Lime, ['p'] = new Color(80, 36, 140),
        ['P'] = new Color(140, 76, 210), ['e'] = new Color(40, 150, 60),
    };

    private static string[] Mirror(params string[] half)
    {
        var rows = new string[half.Length];
        for (int i = 0; i < half.Length; i++)
        {
            var h = half[i];
            var rev = h.ToCharArray(0, h.Length - 1);
            Array.Reverse(rev);
            rows[i] = h + new string(rev);
        }
        return rows;
    }

    private static readonly PixelArt Fighter = new(Mirror(
        ".......w", "......cw", "......cW", ".....ccW", ".....cbW", "....ccbb", "r...cbbb", "r..ccbbs",
        "rc.cbbss", "rccbbssW", "ccbbbsss", "cbbbbbbb", ".ccbb.oo", "..cc..o."), Colours);

    private static readonly PixelArt[] EnemyArt =
    [
        new(Mirror("..m...", "...m..", "..mmmm", ".mmyyM", "mmmmmm", "m.mmMM", "m.m...", "...mm."), Colours),
        new(Mirror("e.....", "ee...l", "eee.ll", ".eelll", "..elwl", "...lll", "....ll", ".....l"), Colours),
        new(Mirror("...oooo", "..oyyyy", ".ooyrrr", "oooyrww", "o.oyrrr", "o.ooyyy", "o..oooo", "...o.o."), Colours),
    ];

    private static readonly PixelArt Boss = new(Mirror(
        "..............M", "....pp.......mM", "...pPPp.....mmM", "...pPPp....mmMM", "..ppPPpp..mmMyy",
        ".pppppppppmmMyw", "ppPPPPPPppmmMyy", "pPPPPPPPPpmmmMM", "pPPrrrPPPPpmmmm", "pPPrRrPPPPPpppp",
        "ppPrrrPPPPPPPPP", ".pppPPPPPPPPPPP", "..pppppPPPPPPPP", "...g.g.ppppppPP", "...g.g...ppppPP",
        "..........gg.pp"), Colours);

    private const float BossPx = 4;

    // ------------------------------------------------------------------ state

    private enum Pattern { Snake, Sweep, Diver, Kamikaze, Turret, Boss }

    private sealed class Enemy
    {
        public Pattern Pattern;
        public int Type, Group, LastBolt;
        public Vector2 Pos, Origin, Vel;
        public float T, Hp, MaxHp, Phase, Dir, Fire, Flash, Angle, Speed;
        public bool Dying;
        public float DieTimer;
    }

    private struct Shot
    {
        public Vector2 Pos, Vel;
        public float Radius, Damage;
        public bool Plasma;
        public int Id;
    }

    private struct Spawn
    {
        public float Time;
        public Pattern Pattern;
        public int Type, Group;
        public float X, Y, Dir, Phase;
    }

    private struct Pickup
    {
        public Vector2 Pos;
        public bool Shield;
        public float T;
    }

    private readonly List<Enemy> _enemies = new();
    private readonly List<Shot> _shots = new();
    private readonly List<Shot> _bullets = new();
    private readonly List<Spawn> _spawns = new();
    private readonly List<Pickup> _pickups = new();
    private readonly Dictionary<int, int> _groupLeft = new();
    private readonly HashSet<int> _groupSpoiled = new();

    private Vector2 _pos;
    private float _respawn, _invuln, _fireCool, _charge, _scroll, _waveTime, _banner, _turretTimer;
    private bool _alive, _prevFire, _prevAlt, _shield;
    private int _spread, _boltId, _spawnIndex;
    private bool _bossWave;
    private int _autoBoltTimer;

    protected override void Start()
    {
        Lives = 3;
        Level = 0;
        _enemies.Clear();
        _shots.Clear();
        _bullets.Clear();
        _pickups.Clear();
        _spread = 0;
        _shield = false;
        _charge = 0;
        _fireCool = 0;
        _prevFire = _prevAlt = false;
        _autoBoltTimer = 0;
        _pos = new Vector2(320, 310);
        _alive = true;
        _invuln = 1.5f;
        NewWave();
    }

    private void NewWave()
    {
        Level++;
        _waveTime = 0;
        _banner = 2.2f;
        _spawns.Clear();
        _spawnIndex = 0;
        _groupLeft.Clear();
        _groupSpoiled.Clear();
        _bossWave = Level % 4 == 0;
        _turretTimer = 3;
        if (_bossWave)
        {
            _spawns.Add(new Spawn { Time = 2.0f, Pattern = Pattern.Boss, Group = -1 });
            Sound.Play(Sfx.Alarm, 0, 0.7f);
        }
        else
        {
            int groups = 3 + Math.Min(Level, 5);
            for (int gi = 0; gi < groups; gi++)
            {
                var pat = Level switch
                {
                    1 => Pick(Pattern.Snake, Pattern.Sweep),
                    2 => Pick(Pattern.Snake, Pattern.Sweep, Pattern.Kamikaze),
                    _ => Pick(Pattern.Snake, Pattern.Sweep, Pattern.Kamikaze, Pattern.Diver, Pattern.Snake),
                };
                int type = Chance(0.15f + Level * 0.04f) ? 2 : Chance(0.5f) ? 1 : 0;
                int count = type == 2 ? RandInt(2, 4) : RandInt(4, 8);
                float start = 1.6f + gi * MathF.Max(1.6f, 2.8f - Level * 0.12f);
                float x = Rand(110, 530), y = Rand(70, 170), dir = Chance(0.5f) ? 1 : -1, phase = Rand(0, MathF2.Tau);
                for (int k = 0; k < count; k++)
                {
                    var sp = new Spawn { Time = start + k * 0.32f, Pattern = pat, Type = type, Group = gi, X = x, Y = y, Dir = dir, Phase = phase };
                    if (pat == Pattern.Diver || pat == Pattern.Kamikaze)
                    {
                        // Divers and kamikazes arrive spread out, not in a snake.
                        sp.X = 80 + (480f / Math.Max(1, count - 1)) * k * (dir > 0 ? 1 : -1) + (dir > 0 ? 0 : 480);
                        sp.Time = start + k * 0.2f;
                    }
                    _spawns.Add(sp);
                }
                _groupLeft[gi] = count;
            }
            _spawns.Sort((a, b) => a.Time.CompareTo(b.Time));
        }
        if (Level > 1)
            Sound.Play(Sfx.LevelUp, 0, 0.7f);
    }

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        _scroll += ScrollSpeed * Dt;
        _waveTime += Dt;
        if (_banner > 0)
            _banner -= Dt;

        UpdatePlayer();
        SpawnEnemies();
        UpdateEnemies();
        if (IsOver)
            return;
        UpdateShots();
        UpdateBullets();
        UpdatePickups();

        // Wave complete?
        if (_spawnIndex >= _spawns.Count && _waveTime > 3)
        {
            bool airLeft = false;
            foreach (var e in _enemies)
                if (e.Pattern != Pattern.Turret)
                    airLeft = true;
            if (!airLeft)
                NewWave();
        }
    }

    private void UpdatePlayer()
    {
        bool fire = In.Fire, alt = In.Alt;
        bool fireReleased = _prevFire && !fire, altReleased = _prevAlt && !alt;
        _prevFire = fire;
        _prevAlt = alt;

        if (!_alive)
        {
            _respawn -= Dt;
            _charge = 0;
            if (_respawn <= 0)
            {
                _alive = true;
                _invuln = 2.5f;
                _pos = new Vector2(320, 320);
            }
            return;
        }
        if (_invuln > 0)
            _invuln -= Dt;

        var move = new Vector2(In.AxisX, In.AxisY);
        if (move.LengthSquared() > 1)
            move.Normalize();
        _pos += move * 210 * Dt;
        _pos.X = MathF2.Clamp(_pos.X, MinX, MaxX);
        _pos.Y = MathF2.Clamp(_pos.Y, MinY, MaxY);

        // Charging: ALT charges quickly; holding FIRE charges slowly while it shoots.
        float before = _charge;
        if (alt)
            _charge = MathF.Min(1, _charge + Dt / 0.75f);
        else if (fire)
            _charge = MathF.Min(1, _charge + Dt / 1.4f);
        if (before < 1 && _charge >= 1)
            Sound.Play(Sfx.PowerUp, 0.6f, 0.4f);
        if ((alt || fire) && Tick % 10 == 0 && _charge > 0.2f && _charge < 1)
            Sound.Play(Sfx.Tick, _charge, 0.25f);

        if (altReleased && _charge >= 0.25f)
            FireBolt();
        else if (fireReleased && _charge >= 1)
            FireBolt();
        else if (altReleased || fireReleased)
            _charge = 0;

        if (_fireCool > 0)
            _fireCool -= Dt;
        if (fire && !alt && _fireCool <= 0)
        {
            _fireCool = 0.095f;
            AddShot(_pos + new Vector2(-5, -10), new Vector2(0, -470));
            AddShot(_pos + new Vector2(5, -10), new Vector2(0, -470));
            if (_spread >= 1)
            {
                AddShot(_pos + new Vector2(-8, -6), MathF2.FromAngle(-MathF.PI / 2 - 0.22f, 450));
                AddShot(_pos + new Vector2(8, -6), MathF2.FromAngle(-MathF.PI / 2 + 0.22f, 450));
            }
            if (_spread >= 2)
            {
                AddShot(_pos + new Vector2(-10, -4), MathF2.FromAngle(-MathF.PI / 2 - 0.45f, 430));
                AddShot(_pos + new Vector2(10, -4), MathF2.FromAngle(-MathF.PI / 2 + 0.45f, 430));
            }
            Sound.Play(Sfx.Shoot, Rand(0.3f, 0.5f), 0.22f);
        }

        // Engine glow particles.
        if (Tick % 3 == 0)
            Fx.Spark(_pos.X + Rand(-2, 2), _pos.Y + 14, Rand(-10, 10), Rand(80, 140), Chance(0.5f) ? Pal.Orange : Pal.Yellow, 0.18f, 1.6f);
    }

    private void AddShot(Vector2 p, Vector2 v) =>
        _shots.Add(new Shot { Pos = p, Vel = v, Radius = 3, Damage = 1 });

    private void FireBolt()
    {
        float k = _charge;
        _charge = 0;
        _boltId++;
        _shots.Add(new Shot
        {
            Pos = _pos + new Vector2(0, -14), Vel = new Vector2(0, -360), Radius = 7 + 9 * k, Damage = 5 + 15 * k,
            Plasma = true, Id = _boltId,
        });
        Sound.Play(Sfx.Zap, -0.4f + (1 - k) * 0.4f, 0.9f);
        Sound.Play(Sfx.Whoosh, 0.2f, 0.5f);
        Fx.Burst(_pos.X, _pos.Y - 14, Pal.Magenta, 16, 120, 0.35f, 2f);
        Fx.Shake(1.5f * k, 0.12f);
    }

    private void SpawnEnemies()
    {
        while (_spawnIndex < _spawns.Count && _spawns[_spawnIndex].Time <= _waveTime)
        {
            var s = _spawns[_spawnIndex++];
            var e = new Enemy
            {
                Pattern = s.Pattern, Type = s.Type, Group = s.Group, Phase = s.Phase, Dir = s.Dir, LastBolt = -1,
                Fire = Rand(1.0f, 3.0f), Speed = 1 + MathF.Min(Level, 12) * 0.05f,
            };
            switch (s.Pattern)
            {
                case Pattern.Snake:
                    e.Origin = new Vector2(s.X, -16);
                    break;
                case Pattern.Sweep:
                    e.Origin = new Vector2(s.Dir > 0 ? -16 : Screen.Width + 16, s.Y);
                    break;
                case Pattern.Diver:
                case Pattern.Kamikaze:
                    e.Origin = new Vector2(MathF2.Clamp(s.X, 30, 610), -16);
                    e.Vel = new Vector2(0, 90);
                    break;
                case Pattern.Boss:
                    e.Origin = new Vector2(320, -70);
                    e.Hp = e.MaxHp = 150 + Level * 35;
                    e.Fire = 1.5f;
                    break;
            }
            e.Pos = e.Origin;
            if (s.Pattern != Pattern.Boss)
                e.Hp = e.MaxHp = s.Type == 0 ? 1 : s.Type == 1 ? 2 : 7;
            _enemies.Add(e);
        }

        // Ground turrets roll in with the landscape.
        if (Level >= 2 && !_bossWave)
        {
            _turretTimer -= Dt;
            if (_turretTimer <= 0)
            {
                _turretTimer = MathF.Max(2.5f, 6 - Level * 0.3f) + Rand(0, 2);
                float x = 16 + RandInt(1, 19) * Tile - Tile / 2;
                _enemies.Add(new Enemy
                {
                    Pattern = Pattern.Turret, Pos = new Vector2(x, -16), Hp = 5, MaxHp = 5, Fire = Rand(1, 2), Group = -1, LastBolt = -1,
                    Angle = MathF.PI / 2,
                });
            }
        }
    }

    private void UpdateEnemies()
    {
        for (int i = _enemies.Count - 1; i >= 0; i--)
        {
            var e = _enemies[i];
            e.T += Dt;
            if (e.Flash > 0)
                e.Flash -= Dt;
            if (e.Dying)
            {
                // The mothership's death throes.
                e.DieTimer -= Dt;
                if (Tick % 5 == 0)
                {
                    Fx.Explode(e.Pos.X + Rand(-50, 50), e.Pos.Y + Rand(-25, 25), 0.9f);
                    Sound.Play(Sfx.Explode, Rand(-0.6f, 0.2f), 0.6f);
                }
                if (e.DieTimer <= 0)
                {
                    Fx.Explode(e.Pos.X, e.Pos.Y, 3f);
                    Fx.Burst(e.Pos.X, e.Pos.Y, Pal.Magenta, 60, 260, 1.2f, 3f);
                    Sound.Play(Sfx.BigExplode, -0.5f);
                    _enemies.RemoveAt(i);
                }
                continue;
            }

            bool gone = Move(e);
            if (gone)
            {
                if (e.Group >= 0)
                    _groupSpoiled.Add(e.Group);
                _enemies.RemoveAt(i);
                continue;
            }

            // Shooting.
            bool onScreen = e.Pos.Y > Screen.HudHeight + 4 && e.Pos.Y < 330 && e.Pos.X > 0 && e.Pos.X < Screen.Width;
            e.Fire -= Dt * (1 + Level * 0.06f);
            if (onScreen && e.Fire <= 0 && _alive)
                EnemyShoot(e);

            // Ramming the player.
            if (_alive && _invuln <= 0 && e.Pattern != Pattern.Turret)
            {
                bool hit = e.Pattern == Pattern.Boss
                    ? MathF.Abs(_pos.X - e.Pos.X) < Boss.Width * BossPx / 2 - 6 && MathF.Abs(_pos.Y - e.Pos.Y) < Boss.Height * BossPx / 2 - 4
                    : Vector2.DistanceSquared(_pos, e.Pos) < 13 * 13;
                if (hit)
                {
                    if (e.Pattern != Pattern.Boss)
                        Damage(e, 99, false);
                    HurtPlayer();
                    if (IsOver)
                        return;
                }
            }
        }
    }

    /// <summary>Moves an enemy along its pattern; true when it has left the screen.</summary>
    private bool Move(Enemy e)
    {
        float t = e.T * e.Speed;
        switch (e.Pattern)
        {
            case Pattern.Snake:
                e.Pos = new Vector2(e.Origin.X + MathF.Sin(t * 2.1f + e.Phase) * 95, e.Origin.Y + 62 * t);
                return e.Pos.Y > Screen.Height + 20;
            case Pattern.Sweep:
                e.Pos = new Vector2(e.Origin.X + e.Dir * 115 * t, e.Origin.Y + 22 * t + MathF.Sin(t * 3 + e.Phase) * 26);
                return e.T > 1 && (e.Pos.X < -24 || e.Pos.X > Screen.Width + 24 || e.Pos.Y > Screen.Height + 20);
            case Pattern.Diver:
                if (e.T < 1.4f)
                    e.Pos.Y = MathF2.Lerp(-16, 110 + e.Origin.X % 60, MathF2.EaseOut(e.T / 1.4f));
                else if (e.T < 2.6f)
                    e.Pos.X += MathF.Sin(e.T * 4) * 20 * Dt;
                else
                {
                    if (e.Vel.Y < 100)
                        e.Vel = new Vector2((_pos.X - e.Pos.X) * 0.6f, 120);
                    e.Vel.Y += 160 * Dt;
                    e.Pos += e.Vel * Dt;
                }
                return e.Pos.Y > Screen.Height + 20;
            case Pattern.Kamikaze:
                e.Vel.X = MathF2.Approach(e.Vel.X, MathF.Sign(_pos.X - e.Pos.X) * 70, 120 * Dt);
                e.Vel.Y = 95 * e.Speed;
                e.Pos += e.Vel * Dt;
                return e.Pos.Y > Screen.Height + 20;
            case Pattern.Turret:
                e.Pos.Y += ScrollSpeed * Dt;
                e.Angle = MathF.Atan2(_pos.Y - e.Pos.Y, _pos.X - e.Pos.X);
                return e.Pos.Y > Screen.Height + 20;
            case Pattern.Boss:
                float enter = MathF2.EaseOut(MathF.Min(1, e.T / 2.5f));
                e.Pos = new Vector2(320 + MathF.Sin(e.T * 0.55f) * 190 * enter, MathF2.Lerp(-70, 92, enter) + MathF.Sin(e.T * 1.3f) * 10);
                return false;
        }
        return true;
    }

    private void EnemyShoot(Enemy e)
    {
        float speed = 115 + Math.Min(Level, 14) * 7;
        var aim = MathF2.Angle(_pos - e.Pos);
        switch (e.Pattern)
        {
            case Pattern.Turret:
                e.Fire = Rand(1.4f, 2.4f);
                for (int k = 0; k < 2; k++)
                    AddBullet(e.Pos + MathF2.FromAngle(e.Angle, 12), MathF2.FromAngle(e.Angle, speed * (1 + k * 0.15f)));
                Sound.Play(Sfx.Cannon, 0.4f, 0.25f);
                break;
            case Pattern.Boss:
                if (e.T < 2.5f)
                {
                    e.Fire = 0.5f;
                    break;
                }
                int phase = (int)(e.T / 3.5f) % 3;
                if (phase == 0)
                {
                    e.Fire = 0.7f;
                    for (int k = -2; k <= 2; k++)
                        AddBullet(e.Pos + new Vector2(0, 26), MathF2.FromAngle(aim + k * 0.17f, speed));
                    Sound.Play(Sfx.Zap, 0.1f, 0.4f);
                }
                else if (phase == 1)
                {
                    e.Fire = 0.11f;
                    float a = e.T * 3.1f;
                    AddBullet(e.Pos + new Vector2(-46, 10), MathF2.FromAngle(a, speed * 0.85f));
                    AddBullet(e.Pos + new Vector2(46, 10), MathF2.FromAngle(MathF.PI - a, speed * 0.85f));
                    if (Tick % 4 == 0)
                        Sound.Play(Sfx.Tick, 0.5f, 0.3f);
                }
                else
                {
                    e.Fire = 1.1f;
                    int n = 12 + Math.Min(Level, 12);
                    float off = Rand(0, 1);
                    for (int k = 0; k < n; k++)
                        AddBullet(e.Pos + new Vector2(0, 10), MathF2.FromAngle(MathF2.Tau * (k + off) / n, speed * 0.75f));
                    Sound.Play(Sfx.Cannon, -0.2f, 0.5f);
                }
                break;
            default:
                e.Fire = Rand(1.8f, 3.6f);
                if (e.Pos.Y > _pos.Y - 40)
                    break;
                if (e.Type == 2)
                {
                    for (int k = -1; k <= 1; k++)
                        AddBullet(e.Pos + new Vector2(0, 8), MathF2.FromAngle(aim + k * 0.25f, speed));
                }
                else
                {
                    AddBullet(e.Pos + new Vector2(0, 8), MathF2.FromAngle(aim, speed));
                }
                Sound.Play(Sfx.Zap, 0.6f, 0.2f);
                break;
        }
    }

    private void AddBullet(Vector2 p, Vector2 v)
    {
        if (_bullets.Count < 220)
            _bullets.Add(new Shot { Pos = p, Vel = v, Radius = 3 });
    }

    private void UpdateShots()
    {
        for (int i = _shots.Count - 1; i >= 0; i--)
        {
            var s = _shots[i];
            s.Pos += s.Vel * Dt;
            bool dead = s.Pos.Y < Screen.HudHeight - 20 || s.Pos.X < -20 || s.Pos.X > Screen.Width + 20;
            if (s.Plasma && Tick % 2 == 0)
                Fx.Spark(s.Pos.X + Rand(-s.Radius, s.Radius) * 0.6f, s.Pos.Y + s.Radius, Rand(-20, 20), 60, Pal.Magenta, 0.3f, 2f);
            for (int j = _enemies.Count - 1; j >= 0 && !dead; j--)
            {
                var e = _enemies[j];
                if (e.Dying)
                    continue;
                if (!HitsEnemy(s.Pos, s.Radius, e))
                    continue;
                if (s.Plasma)
                {
                    if (e.LastBolt == s.Id)
                        continue;
                    e.LastBolt = s.Id;
                    Damage(e, s.Damage, true);
                    // The bolt pierces, but the mothership soaks it up.
                    if (e.Pattern == Pattern.Boss)
                        dead = true;
                }
                else
                {
                    Damage(e, s.Damage, true);
                    Fx.Spark(s.Pos.X, s.Pos.Y, Rand(-40, 40), Rand(-60, 10), Pal.Yellow, 0.15f, 1.4f);
                    dead = true;
                }
            }
            if (s.Plasma)
            {
                // Plasma also burns away enemy bullets.
                for (int j = _bullets.Count - 1; j >= 0; j--)
                    if (Vector2.DistanceSquared(_bullets[j].Pos, s.Pos) < s.Radius * s.Radius)
                        _bullets.RemoveAt(j);
            }
            if (dead)
                _shots.RemoveAt(i);
            else
                _shots[i] = s;
        }
    }

    private static bool HitsEnemy(Vector2 p, float r, Enemy e)
    {
        if (e.Pattern == Pattern.Boss)
            return MathF.Abs(p.X - e.Pos.X) < Boss.Width * BossPx / 2 + r - 6 && MathF.Abs(p.Y - e.Pos.Y) < Boss.Height * BossPx / 2 + r - 6;
        float er = e.Pattern == Pattern.Turret ? 12 : e.Type == 2 ? 13 : 10;
        return Vector2.DistanceSquared(p, e.Pos) < (r + er) * (r + er);
    }

    private void Damage(Enemy e, float amount, bool byPlayer)
    {
        e.Hp -= amount;
        e.Flash = 0.08f;
        if (e.Hp > 0)
        {
            Sound.Play(Sfx.Hit, e.Pattern == Pattern.Boss ? -0.4f : 0.3f, 0.25f);
            return;
        }
        if (e.Pattern == Pattern.Boss)
        {
            e.Dying = true;
            e.DieTimer = 1.8f;
            int points = 2000 + Level * 500;
            AddScore(points, e.Pos.X, e.Pos.Y - 40, Pal.Magenta);
            Fx.Float("MOTHERSHIP DESTROYED!", 320, 160, Pal.Yellow, 2f);
            _bullets.Clear();
            DropPickup(e.Pos, Chance(0.5f));
            return;
        }
        int pts = e.Pattern == Pattern.Turret ? 200 : e.Type == 0 ? 50 : e.Type == 1 ? 100 : 250;
        if (byPlayer)
            AddScore(pts, e.Pos.X, e.Pos.Y - 10, e.Pattern == Pattern.Turret ? Pal.Orange : Pal.Yellow);
        var col = e.Pattern == Pattern.Turret ? Pal.Orange : e.Type == 0 ? Pal.Magenta : e.Type == 1 ? Pal.Lime : Pal.Orange;
        Fx.Burst(e.Pos.X, e.Pos.Y, col, 22, 150, 0.6f, 2.4f);
        Fx.Burst(e.Pos.X, e.Pos.Y, Pal.White, 8, 70, 0.3f, 2f);
        if (e.Type == 2 || e.Pattern == Pattern.Turret)
            Fx.Explode(e.Pos.X, e.Pos.Y, 0.8f);
        Sound.Play(e.Type == 2 || e.Pattern == Pattern.Turret ? Sfx.BigExplode : Sfx.Explode, Rand(-0.2f, 0.4f), 0.6f);
        _enemies.Remove(e);

        if (e.Group >= 0 && _groupLeft.TryGetValue(e.Group, out int left))
        {
            _groupLeft[e.Group] = left - 1;
            if (left - 1 == 0 && !_groupSpoiled.Contains(e.Group) && byPlayer)
            {
                AddScore(500, e.Pos.X, e.Pos.Y + 8, Pal.Cyan);
                DropPickup(e.Pos, _spread >= 2 || Chance(0.3f));
            }
        }
        else if (Chance(0.04f))
        {
            DropPickup(e.Pos, Chance(0.5f));
        }
    }

    private void DropPickup(Vector2 at, bool shield) =>
        _pickups.Add(new Pickup { Pos = at, Shield = shield || _spread >= 2 && Chance(0.7f) });

    private void UpdateBullets()
    {
        for (int i = _bullets.Count - 1; i >= 0; i--)
        {
            var b = _bullets[i];
            b.Pos += b.Vel * Dt;
            if (b.Pos.Y > Screen.Height + 10 || b.Pos.Y < Screen.HudHeight - 10 || b.Pos.X < -10 || b.Pos.X > Screen.Width + 10)
            {
                _bullets.RemoveAt(i);
                continue;
            }
            _bullets[i] = b;
            if (_alive && _invuln <= 0 && Vector2.DistanceSquared(b.Pos, _pos) < (_shield ? 15 * 15 : 5 * 5))
            {
                _bullets.RemoveAt(i);
                HurtPlayer();
                return; // the hit may clear the bullet list
            }
        }
    }

    private void HurtPlayer()
    {
        if (_shield)
        {
            _shield = false;
            _invuln = 1f;
            Fx.Burst(_pos.X, _pos.Y, Pal.Cyan, 30, 140, 0.5f, 2f);
            Sound.Play(Sfx.Crack, 0.2f);
            return;
        }
        _alive = false;
        _respawn = 1.6f;
        _spread = Math.Max(0, _spread - 1);
        _charge = 0;
        Fx.Explode(_pos.X, _pos.Y, 1.6f);
        Fx.Burst(_pos.X, _pos.Y, Pal.Sky, 30, 180, 0.8f, 2.5f);
        Sound.Play(Sfx.BigExplode);
        // Clear nearby bullets so the respawn is fair.
        _bullets.Clear();
        LoseLife();
    }

    private void UpdatePickups()
    {
        for (int i = _pickups.Count - 1; i >= 0; i--)
        {
            var p = _pickups[i];
            p.T += Dt;
            p.Pos.Y += 45 * Dt;
            p.Pos.X += MathF.Sin(p.T * 3) * 30 * Dt;
            if (p.Pos.Y > Screen.Height + 12)
            {
                _pickups.RemoveAt(i);
                continue;
            }
            if (_alive && Vector2.DistanceSquared(p.Pos, _pos) < 18 * 18)
            {
                _pickups.RemoveAt(i);
                if (p.Shield)
                {
                    _shield = true;
                    Fx.Float("SHIELD", p.Pos.X, p.Pos.Y - 10, Pal.Cyan);
                }
                else
                {
                    _spread = Math.Min(2, _spread + 1);
                    Fx.Float(_spread == 2 ? "MAX SPREAD" : "SPREAD", p.Pos.X, p.Pos.Y - 10, Pal.Lime);
                }
                AddScore(100);
                Sound.Play(Sfx.PowerUp);
                Fx.Burst(p.Pos.X, p.Pos.Y, p.Shield ? Pal.Cyan : Pal.Lime, 20, 100, 0.5f, 2f);
                continue;
            }
            _pickups[i] = p;
        }
    }

    // ------------------------------------------------------------------ drawing

    private static float Hash(int x, int y)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
        }
    }

    private static float Noise(float x, float y)
    {
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
        float fx = x - ix, fy = y - iy;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        float a = Hash(ix, iy), b = Hash(ix + 1, iy), c = Hash(ix, iy + 1), d = Hash(ix + 1, iy + 1);
        return MathF2.Lerp(MathF2.Lerp(a, b, fx), MathF2.Lerp(c, d, fx), fy);
    }

    /// <summary>0 = acid sea, 1 = alien rock, 2 = station plating.</summary>
    private static int TileType(int col, int row)
    {
        float n = Noise(col * 0.22f, row * 0.18f) * 0.7f + Noise(col * 0.6f + 7, row * 0.5f) * 0.3f;
        // A metal station strip appears every so often.
        float band = Noise(3.3f, row * 0.05f);
        if (band > 0.62f && col >= 5 && col <= 14)
            return 2;
        return n < 0.4f ? 0 : 1;
    }

    private static void DrawTerrain(Gfx g, float scroll, float time, RectF area, float tile, float dim)
    {
        int firstRow = (int)MathF.Floor(-scroll / tile) - 1;
        int lastRow = (int)MathF.Floor((area.H - scroll) / tile);
        int cols = (int)MathF.Ceiling(area.W / tile);
        for (int row = firstRow; row <= lastRow; row++)
        {
            float y = area.Y + row * tile + scroll;
            for (int col = 0; col < cols; col++)
            {
                float x = area.X + col * tile;
                int type = TileType(col, row);
                float h = Hash(col, row);
                switch (type)
                {
                    case 0:
                    {
                        g.GradientV(x, y, tile + 0.5f, tile + 0.5f, new Color(10, 40, 60), new Color(14, 26, 54));
                        float sy = Backdrops.Mod(time * 9 + h * tile, tile);
                        g.Rect(x + h * tile * 0.5f, y + sy, tile * 0.35f, 1, Pal.Teal * 0.5f);
                        g.Rect(x + (1 - h) * tile * 0.4f, y + Backdrops.Mod(sy + tile * 0.5f, tile), tile * 0.25f, 1, Pal.Cyan * 0.25f);
                        break;
                    }
                    case 1:
                    {
                        var c = Pal.Lerp(new Color(56, 28, 64), new Color(74, 38, 72), Noise(col * 0.5f + 3, row * 0.5f));
                        g.Rect(x, y, tile + 0.5f, tile + 0.5f, c);
                        g.Circle(x + tile * (0.2f + 0.6f * h), y + tile * (0.3f + 0.4f * Hash(row, col)), tile * 0.09f, Pal.Darken(c, 0.35f));
                        g.Circle(x + tile * (0.8f - 0.5f * h), y + tile * 0.75f, tile * 0.06f, Pal.Lighten(c, 0.15f));
                        if (h > 0.93f)
                        {
                            g.Circle(x + tile / 2, y + tile / 2, tile * 0.3f, Pal.Darken(c, 0.3f));
                            g.Ring(x + tile / 2, y + tile / 2, tile * 0.3f, tile * 0.06f, Pal.Lighten(c, 0.2f), 14);
                        }
                        // Glowing moss where the rock meets the sea.
                        if (TileType(col, row - 1) == 0)
                            g.Rect(x, y, tile + 0.5f, tile * 0.08f, new Color(90, 200, 90) * 0.8f);
                        if (TileType(col, row + 1) == 0)
                            g.Rect(x, y + tile * 0.92f, tile + 0.5f, tile * 0.08f, new Color(40, 120, 60) * 0.8f);
                        if (col > 0 && TileType(col - 1, row) == 0)
                            g.Rect(x, y, tile * 0.08f, tile + 0.5f, new Color(70, 170, 80) * 0.7f);
                        if (col < cols - 1 && TileType(col + 1, row) == 0)
                            g.Rect(x + tile * 0.92f, y, tile * 0.08f, tile + 0.5f, new Color(70, 170, 80) * 0.7f);
                        break;
                    }
                    default:
                    {
                        g.GradientV(x, y, tile + 0.5f, tile + 0.5f, new Color(70, 76, 100), new Color(46, 50, 70));
                        g.RectOutline(x + 1, y + 1, tile - 2, tile - 2, 1, new Color(30, 32, 46));
                        g.Rect(x + 2, y + 2, tile - 4, 1, new Color(110, 116, 140));
                        float rv = tile * 0.06f;
                        g.Circle(x + tile * 0.15f, y + tile * 0.15f, rv, Pal.Grey);
                        g.Circle(x + tile * 0.85f, y + tile * 0.85f, rv, Pal.Grey);
                        if (h > 0.7f)
                        {
                            bool on = (int)(time * 2 + h * 10) % 2 == 0;
                            var lc = h > 0.85f ? Pal.Red : Pal.Cyan;
                            g.Circle(x + tile / 2, y + tile / 2, tile * 0.08f, on ? lc : lc * 0.3f);
                            if (on)
                                g.Glow(x + tile / 2, y + tile / 2, tile * 0.4f, lc, 0.4f);
                        }
                        else if (h < 0.3f)
                        {
                            g.Rect(x + tile * 0.2f, y + tile * 0.45f, tile * 0.6f, tile * 0.1f, new Color(30, 32, 46));
                        }
                        break;
                    }
                }
            }
        }
        g.Rect(area, Color.Black * dim);
    }

    public override void Draw(Gfx g)
    {
        DrawTerrain(g, _scroll, Time, Screen.Bounds, Tile, 0.3f);
        // Drifting cloud shadows for depth.
        for (int i = 0; i < 3; i++)
        {
            float cy = Backdrops.Mod(_scroll * 1.6f + i * 160, 520) - 80;
            g.Glow(120 + i * 200, cy, 110, Color.White, 0.05f);
        }

        // Ground turrets (under everything that flies).
        foreach (var e in _enemies)
            if (e.Pattern == Pattern.Turret)
                DrawTurret(g, e.Pos, e.Angle, e.Flash > 0, 1);

        // Pickups.
        foreach (var p in _pickups)
        {
            var col = p.Shield ? Pal.Cyan : Pal.Lime;
            float bob = 1 + 0.1f * MathF.Sin(p.T * 8);
            g.Glow(p.Pos, 20, col, 0.6f);
            g.Circle(p.Pos, 9 * bob, Pal.Darken(col, 0.6f));
            g.Ring(p.Pos.X, p.Pos.Y, 9 * bob, 1.5f, col);
            g.Text(p.Shield ? "H" : "S", p.Pos.X, p.Pos.Y - 6, 1.5f, Pal.White, Align.Center);
        }

        foreach (var e in _enemies)
        {
            if (e.Pattern == Pattern.Turret)
                continue;
            if (e.Pattern == Pattern.Boss)
            {
                DrawBoss(g, e);
                continue;
            }
            var col = e.Type == 0 ? Pal.Magenta : e.Type == 1 ? Pal.Lime : Pal.Orange;
            g.Glow(e.Pos, 18, col, 0.3f);
            g.PixelsCentered(EnemyArt[e.Type], e.Pos.X, e.Pos.Y, 2.2f);
            if (e.Flash > 0)
                g.PixelsCentered(EnemyArt[e.Type], e.Pos.X, e.Pos.Y, 2.2f, false, Color.White * 0.6f);
        }

        // Player shots.
        foreach (var s in _shots)
        {
            if (s.Plasma)
            {
                float pulse = 1 + 0.12f * MathF.Sin(Time * 30);
                g.Glow(s.Pos, s.Radius * 3.2f, Pal.Magenta, 0.9f);
                g.Circle(s.Pos, s.Radius * pulse, Pal.Magenta);
                g.Circle(s.Pos, s.Radius * 0.65f * pulse, Pal.Pink);
                g.Circle(s.Pos, s.Radius * 0.35f, Pal.White);
            }
            else
            {
                g.Glow(s.Pos, 8, Pal.Yellow, 0.5f);
                g.Rect(s.Pos.X - 1.2f, s.Pos.Y - 5, 2.4f, 9, Pal.Yellow);
                g.Rect(s.Pos.X - 0.6f, s.Pos.Y - 4, 1.2f, 6, Pal.White);
            }
        }

        // Player.
        if (_alive && !IsOver && (_invuln <= 0 || (int)(_invuln * 12) % 2 == 0))
        {
            g.Glow(_pos, 26, Pal.Sky, 0.3f);
            float flame = 4 + 3 * MathF.Sin(Time * 40);
            g.Glow(_pos.X, _pos.Y + 15, 8 + flame, Pal.Orange, 0.7f);
            g.PixelsCentered(Fighter, _pos.X, _pos.Y, 2f);
            if (_shield)
            {
                g.Glow(_pos, 26, Pal.Cyan, 0.25f + 0.1f * MathF.Sin(Time * 6));
                g.Ring(_pos.X, _pos.Y, 18, 1.5f, Pal.Cyan * 0.8f);
            }
            if (_charge > 0.05f)
            {
                var cc = _charge >= 1 ? Pal.Lerp(Pal.Magenta, Pal.White, MathF2.Pulse(Time, 0.25f)) : Pal.Magenta;
                g.Arc(_pos.X, _pos.Y, 22, 2.5f, -MathF.PI / 2, -MathF.PI / 2 + MathF2.Tau * _charge, cc);
                g.Glow(_pos.X, _pos.Y - 16, 6 + 10 * _charge, Pal.Magenta, 0.4f + 0.5f * _charge);
            }
        }

        // Enemy bullets on top so they're always visible.
        foreach (var b in _bullets)
        {
            g.Glow(b.Pos, 9, Pal.Red, 0.6f);
            g.Circle(b.Pos, 3.2f, Pal.Orange);
            g.Circle(b.Pos, 1.6f, Pal.White);
        }

        // Charge meter.
        if (_alive)
        {
            g.RoundRect(8, 27, 84, 12, 3, Color.Black * 0.6f);
            g.RoundRect(10, 29, 80 * _charge, 8, 2, _charge >= 1 ? Pal.Pink : Pal.Magenta);
            g.Text("BOLT", 50, 30, 1f, Pal.White * 0.8f, Align.Center);
            for (int i = 0; i < _spread; i++)
                g.Text("S", 100 + i * 10, 29, 1.25f, Pal.Lime);
            if (_shield)
                g.Text("H", 124, 29, 1.25f, Pal.Cyan);
        }

        if (_banner > 0)
        {
            float a = MathF.Min(1, _banner * 1.5f);
            g.TextShadow(_bossWave ? "WARNING!" : "WAVE " + Level, 320, 150, 3f, (_bossWave ? Pal.Red : Pal.Magenta) * a, Align.Center);
            if (_bossWave)
                g.Text("MOTHERSHIP APPROACHING", 320, 182, 1.5f, Pal.Yellow * a, Align.Center);
        }
    }

    private void DrawBoss(Gfx g, Enemy e)
    {
        g.Glow(e.Pos, 100, Pal.Magenta, 0.25f);
        g.Glow(e.Pos.X, e.Pos.Y + 6, 30, Pal.Yellow, 0.4f + 0.2f * MathF.Sin(Time * 6));
        bool flash = e.Flash > 0 || (e.Dying && (int)(e.DieTimer * 20) % 2 == 0);
        g.PixelsCentered(Boss, e.Pos.X, e.Pos.Y, BossPx);
        if (flash)
            g.PixelsCentered(Boss, e.Pos.X, e.Pos.Y, BossPx, false, Color.White * (e.Dying ? 0.8f : 0.3f));
        // Engine glows.
        for (int k = -1; k <= 1; k += 2)
            g.Glow(e.Pos.X + k * 30, e.Pos.Y + 30, 12 + 3 * MathF.Sin(Time * 30), Pal.Orange, 0.7f);
        if (!e.Dying)
        {
            float w = 200;
            g.RoundRect(320 - w / 2 - 2, 28, w + 4, 8, 3, Color.Black * 0.7f);
            g.RoundRect(320 - w / 2, 30, w * MathF.Max(0, e.Hp / e.MaxHp), 4, 2, Pal.Lerp(Pal.Red, Pal.Magenta, e.Hp / e.MaxHp));
        }
    }

    private static void DrawTurret(Gfx g, Vector2 p, float angle, bool flash, float s)
    {
        g.RoundRect(p.X - 13 * s, p.Y - 13 * s, 26 * s, 26 * s, 4 * s, new Color(40, 42, 58));
        g.RoundRect(p.X - 11 * s, p.Y - 11 * s, 22 * s, 22 * s, 3 * s, new Color(80, 84, 110));
        g.Circle(p, 8 * s, flash ? Pal.White : new Color(120, 60, 50));
        g.RotatedRect(p + MathF2.FromAngle(angle, 9 * s), 14 * s, 4 * s, angle, flash ? Pal.White : Pal.Grey);
        g.Circle(p, 4 * s, Pal.Orange);
        g.Glow(p, 10 * s, Pal.Orange, 0.3f);
    }

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        float s = r.H / 70f;
        DrawTerrain(g, time * 20 * s, time, r, 16 * s, 0.35f);

        var ship = new Vector2(r.CenterX + MathF.Sin(time * 1.2f) * 24 * s, r.Bottom - 14 * s);
        // Enemies snaking down.
        for (int i = 0; i < 5; i++)
        {
            float t = time * 0.5f - i * 0.25f;
            float y = r.Y + Backdrops.Mod(t * 40 * s, r.H + 20 * s) - 10 * s;
            float x = r.CenterX + MathF.Sin(t * 3) * 40 * s;
            g.Glow(x, y, 9 * s, Pal.Magenta, 0.4f);
            g.PixelsCentered(EnemyArt[0], x, y, 1.1f * s);
        }
        DrawTurret(g, new Vector2(r.X + 22 * s, r.Y + 22 * s), MathF.Atan2(ship.Y - r.Y - 22 * s, ship.X - r.X - 22 * s), false, 0.6f * s);

        // A plasma bolt rising from the ship.
        float bt = Backdrops.Mod(time * 0.9f, 1f);
        var bolt = new Vector2(ship.X, ship.Y - 10 * s - bt * r.H);
        g.Glow(bolt, 14 * s, Pal.Magenta, 0.9f);
        g.Circle(bolt, 5 * s, Pal.Magenta);
        g.Circle(bolt, 2.5f * s, Pal.White);
        for (int k = 0; k < 3; k++)
        {
            float sy = ship.Y - 10 * s - Backdrops.Mod(time * 3 + k / 3f, 1f) * r.H * 0.8f;
            g.Rect(ship.X - 4 * s, sy, 1.2f * s, 4 * s, Pal.Yellow);
            g.Rect(ship.X + 3 * s, sy, 1.2f * s, 4 * s, Pal.Yellow);
        }
        g.Glow(ship.X, ship.Y + 8 * s, 6 * s, Pal.Orange, 0.8f);
        g.PixelsCentered(Fighter, ship.X, ship.Y, 1.1f * s);
    }

    // ------------------------------------------------------------------ autopilot

    public override void AutoPlay(Controls c)
    {
        if (!_alive)
            return;
        // Line up under the most threatening enemy, dodge close bullets, grab power-ups.
        float targetX = _pos.X, targetY = 300;
        float best = float.MaxValue;
        foreach (var e in _enemies)
        {
            if (e.Dying || e.Pos.Y < Screen.HudHeight || e.Pos.Y > _pos.Y - 20)
                continue;
            float d = MathF.Abs(e.Pos.X - _pos.X) - e.Pos.Y * 0.3f + (e.Pattern == Pattern.Turret ? 60 : 0);
            if (d < best)
            {
                best = d;
                targetX = e.Pos.X;
            }
        }
        foreach (var p in _pickups)
            if (p.Pos.Y > 150)
            {
                targetX = p.Pos.X;
                targetY = MathF.Max(p.Pos.Y, 240);
            }

        var dodge = Vector2.Zero;
        foreach (var b in _bullets)
        {
            var d = _pos - b.Pos;
            float dist = d.Length();
            if (dist < 55 && Vector2.Dot(d, b.Vel) > 0)
                dodge += d / (dist * dist + 1) * 60;
        }
        foreach (var e in _enemies)
        {
            if (e.Pattern is Pattern.Turret or Pattern.Boss)
                continue;
            var d = _pos - e.Pos;
            if (d.Length() < 45)
                dodge += d / (d.LengthSquared() + 1) * 80;
        }
        float mx = MathF2.Clamp((targetX - _pos.X) / 30f, -1, 1);
        float my = MathF2.Clamp((targetY - _pos.Y) / 30f, -1, 1);
        if (dodge.LengthSquared() > 0.04f)
        {
            mx = MathF2.Clamp(dodge.X * 3, -1, 1);
            my = MathF2.Clamp(dodge.Y * 3, -1, 1);
        }
        c.SetDirections(mx, my);

        // Hold fire, and every so often charge a plasma bolt on ALT.
        _autoBoltTimer++;
        int cycle = _autoBoltTimer % 150;
        if (cycle < 100)
        {
            c.Fire = true;
            c.FirePressed = cycle == 0;
        }
        else if (cycle < 148)
        {
            c.Alt = true;
            c.AltPressed = cycle == 100;
        }
    }
}
