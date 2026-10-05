using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 20 Jet Mobile: a jetpack astronaut builds a rocket, fuels it pod by pod while blasting alien
/// swarms with a laser, then blasts off to a nastier planet.
/// </summary>
public sealed class JetMobile : MiniGame
{
    public override int Number => 20;
    public override string Title => "Jet Mobile";
    public override Category Category => Category.Arcade;
    public override string Tagline => "Build your rocket, fuel it up and blast off, jetpack and laser in hand.";
    public override Color Accent => Pal.Magenta;

    public override string[] HowToPlay =>
    [
        "Fly to each rocket part and drop it on the launch pad, then carry fuel pods to the rocket. Fly into the full rocket to take off.",
        "Laser the aliens: one touch costs a life. The screen wraps at the sides.",
        "Aliens 25-80, gems 250, launch 1000. Each planet is nastier.",
    ];

    public override string[] DesktopControls => ["LEFT / RIGHT to fly, UP to thrust.", "SPACE to fire the laser."];
    public override string[] TouchControls => ["Stick to fly (push up to thrust), FIRE for the laser."];
    public override Pad Pad => Pad.Stick | Pad.Fire;

    private const float GroundY = 340, RocketX = 432, SegH = 27, SegW = 30, FuelNeeded = 5;

    private static readonly RectF[] Platforms =
    [
        new(56, 150, 118, 8),
        new(268, 214, 76, 8),
        new(476, 112, 118, 8),
    ];

    private static readonly Dictionary<char, Color> Colours = new()
    {
        ['w'] = Pal.White, ['c'] = Pal.Cyan, ['g'] = new Color(255, 200, 40), ['k'] = Pal.Grey, ['s'] = Pal.LightGrey,
    };

    private static readonly PixelArt[] ManArt =
    [
        new(["....wwww..", "...wwwwww.", "..gwwccccw", "..gwwccccw", "..gwwwwwww", ".gggwwww..", ".gggwwwwkk", ".gggwwww..", ".gggwwww..", "..g.wwww..", "....ww.ww.", "....ww.ww.", "...www.www"], Colours),
        new(["....wwww..", "...wwwwww.", "..gwwccccw", "..gwwccccw", "..gwwwwwww", ".gggwwww..", ".gggwwwwkk", ".gggwwww..", ".gggwwww..", "..g.wwww..", "....wwww..", "...ww..ww.", "..www..www"], Colours),
        new(["....wwww..", "...wwwwww.", "..gwwccccw", "..gwwccccw", "..gwwwwwww", ".gggwwww..", ".gggwwwwkk", ".gggwwww..", ".gggwwww..", "..g.wwww..", ".....www..", ".....ww...", ".....www.."], Colours),
    ];

    private enum ItemKind { Middle, Nose, Fuel, Gem }

    private sealed class Item
    {
        public ItemKind Kind;
        public Vector2 Pos;
        public float Vy;
        public bool Resting, Carried, Delivering;
        public int GemType;
    }

    private sealed class Alien
    {
        public Vector2 Pos, Vel;
        public int Type;
        public float T;
    }

    private struct Beam
    {
        public float X0, Head, Y, Dir, Life;
        public Color Colour;
    }

    private readonly List<Item> _items = new();
    private readonly List<Alien> _aliens = new();
    private readonly List<Beam> _beams = new();

    private Vector2 _pos, _vel;
    private int _facing = 1;
    private bool _grounded, _thrusting;
    private float _walk, _fireCool, _dead, _spawnTimer, _gemTimer, _banner;
    private int _parts, _fuel;
    private Item _objective;
    private float _launch, _landing, _invuln;

    private int AlienType => (Level - 1) % 4;

    protected override void Start()
    {
        Lives = 4;
        Level = 1;
        _parts = 1;
        _fuel = 0;
        _items.Clear();
        _aliens.Clear();
        _beams.Clear();
        StartPlanet(true);
        _landing = 0;
    }

    private void StartPlanet(bool newRocket)
    {
        _aliens.Clear();
        _beams.Clear();
        _items.Clear();
        _objective = null;
        if (newRocket)
            _parts = 1;
        _fuel = 0;
        _pos = new Vector2(RocketX - 60, GroundY - 14);
        _vel = Vector2.Zero;
        _spawnTimer = 1.5f;
        _gemTimer = Rand(8, 14);
        _banner = 2.5f;
        NextObjective();
        Status = "PLANET " + Level;
    }

    private void NextObjective()
    {
        if (_parts == 1)
            _objective = new Item { Kind = ItemKind.Middle, Pos = new Vector2(Platforms[1].CenterX, Platforms[1].Y - SegH / 2), Resting = true };
        else if (_parts == 2)
            _objective = new Item { Kind = ItemKind.Nose, Pos = new Vector2(Platforms[2].CenterX + 20, Platforms[2].Y - SegH / 2), Resting = true };
        else if (_fuel < FuelNeeded)
            _objective = new Item { Kind = ItemKind.Fuel, Pos = new Vector2(PickDropX(), Screen.HudHeight + 6) };
        else
            _objective = null;
        if (_objective != null)
            _items.Add(_objective);
    }

    private float PickDropX()
    {
        for (int i = 0; i < 10; i++)
        {
            float x = Rand(30, 610);
            if (MathF.Abs(x - RocketX) > 60)
                return x;
        }
        return 120;
    }

    private float RocketTopY => GroundY - _parts * SegH;

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        if (_banner > 0)
            _banner -= Dt;
        UpdateBeams();

        if (_launch > 0)
        {
            UpdateLaunch();
            return;
        }
        if (_landing > 0)
        {
            _landing -= Dt;
            Sound.Loop(LoopSfx.Thrust, true, -0.3f, 0.6f);
            if (_landing < 1.2f && Tick % 2 == 0)
                Fx.Burst(RocketX + Rand(-20, 20), GroundY, Pal.LightGrey, 2, 60, 0.6f, 3f, -20, false);
            return;
        }

        if (_dead > 0)
        {
            _dead -= Dt;
            if (_dead <= 0)
            {
                _pos = new Vector2(RocketX - 60, GroundY - 14);
                _vel = Vector2.Zero;
                _invuln = 2;
            }
            UpdateAliens();
            UpdateItems();
            return;
        }

        if (_invuln > 0)
            _invuln -= Dt;
        MovePlayer();
        Shoot();
        UpdateItems();
        UpdateAliens();
        SpawnStuff();

        // Climb aboard the fuelled rocket.
        if (_parts == 3 && _fuel >= FuelNeeded && MathF.Abs(_pos.X - RocketX) < 16 && _pos.Y > GroundY - 3 * SegH - 6)
        {
            _launch = 3.2f;
            AddScore(1000, RocketX, GroundY - 90, Pal.Gold);
            Sound.Play(Sfx.Warp);
        }
    }

    private void MovePlayer()
    {
        float ax = In.AxisX;
        _thrusting = In.Up || In.AxisY < -0.4f;
        if (MathF.Abs(ax) > 0.2f)
            _facing = ax > 0 ? 1 : -1;
        _vel.X = MathF2.Approach(_vel.X, ax * 130, (_grounded ? 700 : 380) * Dt);
        _vel.Y += (_thrusting ? -560 : 300) * Dt;
        _vel.Y = MathF2.Clamp(_vel.Y, -150, 190);
        if (_thrusting)
        {
            Sound.Loop(LoopSfx.Thrust, true, 0.2f, 0.4f);
            if (Tick % 2 == 0)
                Fx.Spark(_pos.X - _facing * 6, _pos.Y + 8, Rand(-20, 20), Rand(60, 120), Tick % 4 == 0 ? Pal.Yellow : Pal.Orange, 0.25f, 2f);
        }

        // Horizontal move with wrap, then vertical with platform collisions.
        _pos.X += _vel.X * Dt;
        if (_pos.X < 0) _pos.X += 640;
        if (_pos.X >= 640) _pos.X -= 640;
        var box = PlayerBox(_pos);
        foreach (var p in Platforms)
            if (box.Intersects(p))
            {
                _pos.X = _vel.X > 0 ? p.X - 7 : p.Right + 7;
                _vel.X = 0;
                box = PlayerBox(_pos);
            }

        bool wasGrounded = _grounded;
        _grounded = false;
        _pos.Y += _vel.Y * Dt;
        box = PlayerBox(_pos);
        foreach (var p in Platforms)
            if (box.Intersects(p))
            {
                if (_vel.Y > 0)
                {
                    _pos.Y = p.Y - 14;
                    _grounded = true;
                }
                else
                {
                    _pos.Y = p.Bottom + 14;
                }
                _vel.Y = 0;
                box = PlayerBox(_pos);
            }
        if (_pos.Y > GroundY - 14)
        {
            _pos.Y = GroundY - 14;
            _vel.Y = 0;
            _grounded = true;
        }
        if (_pos.Y < Screen.HudHeight + 16)
        {
            _pos.Y = Screen.HudHeight + 16;
            _vel.Y = MathF.Max(0, _vel.Y);
        }
        if (_grounded && !wasGrounded)
            Sound.Play(Sfx.Land, 0, 0.3f);
        if (_grounded && MathF.Abs(_vel.X) > 10)
        {
            _walk += Dt;
            if ((int)(_walk * 8) != (int)((_walk - Dt) * 8))
                Sound.Play(Sfx.Step, 0.3f, 0.2f);
        }
    }

    private static RectF PlayerBox(Vector2 p) => new(p.X - 6, p.Y - 13, 12, 26);

    private void Shoot()
    {
        if (_fireCool > 0)
            _fireCool -= Dt;
        if ((In.FirePressed || In.Fire) && _fireCool <= 0)
        {
            _fireCool = 0.2f;
            float x = _pos.X + _facing * 10;
            _beams.Add(new Beam { X0 = x, Head = x, Y = _pos.Y - 1, Dir = _facing, Life = 0.45f, Colour = Pal.Rainbow[Tick / 3 % 6] });
            Sound.Play(Sfx.Laser, Rand(0.1f, 0.4f), 0.5f);
        }
    }

    private void UpdateBeams()
    {
        for (int i = _beams.Count - 1; i >= 0; i--)
        {
            var b = _beams[i];
            b.Life -= Dt;
            float oldHead = b.Head;
            b.Head += b.Dir * 1000 * Dt;
            if (MathF.Abs(b.Head - b.X0) > 240)
                b.Head = b.X0 + b.Dir * 240;
            if (b.Life < 0.25f)
                b.X0 += b.Dir * 900 * Dt;
            bool hit = false;
            float lo = MathF.Min(oldHead, b.Head) - 6, hi = MathF.Max(oldHead, b.Head) + 6;
            lo = MathF.Min(lo, MathF.Min(b.X0, b.Head));
            hi = MathF.Max(hi, MathF.Max(b.X0, b.Head));
            for (int a = _aliens.Count - 1; a >= 0; a--)
            {
                var al = _aliens[a];
                if (MathF.Abs(al.Pos.Y - b.Y) < 10 && al.Pos.X > lo && al.Pos.X < hi)
                {
                    KillAlien(a, true);
                    hit = true;
                    break;
                }
            }
            if (hit || b.Life <= 0 || (b.Dir > 0 ? b.X0 >= b.Head : b.X0 <= b.Head))
                _beams.RemoveAt(i);
            else
                _beams[i] = b;
        }
    }

    private void KillAlien(int index, bool byPlayer)
    {
        var a = _aliens[index];
        _aliens.RemoveAt(index);
        var col = AlienColour(a.Type);
        Fx.Burst(a.Pos.X, a.Pos.Y, col, 18, 120, 0.5f, 2.5f);
        Fx.Burst(a.Pos.X, a.Pos.Y, Pal.White, 6, 60, 0.3f, 2f);
        Sound.Play(Sfx.Explode, Rand(-0.1f, 0.4f), 0.6f);
        if (byPlayer)
            AddScore(25 + a.Type * 15 + (Level - 1) / 4 * 10, a.Pos.X, a.Pos.Y - 12, col);
    }

    private static Color AlienColour(int type) => type switch
    {
        0 => Pal.Orange,
        1 => Pal.Cyan,
        2 => Pal.Lime,
        _ => Pal.Pink,
    };

    private void UpdateItems()
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var it = _items[i];
            if (it.Carried)
            {
                if (_dead > 0)
                {
                    it.Carried = false;
                    it.Resting = false;
                    continue;
                }
                it.Pos = _pos + new Vector2(0, 16);
                if (it.Kind != ItemKind.Gem && MathF.Abs(_pos.X - RocketX) < 10 && _pos.Y < RocketTopY - 24)
                {
                    it.Carried = false;
                    it.Delivering = true;
                    it.Pos.X = RocketX;
                    it.Vy = 0;
                    Sound.Play(Sfx.Select);
                }
                continue;
            }
            if (it.Delivering)
            {
                it.Vy = MathF.Min(it.Vy + 300 * Dt, 160);
                it.Pos.Y += it.Vy * Dt;
                float landY = RocketTopY - (it.Kind == ItemKind.Fuel ? 4 : SegH / 2);
                if (it.Pos.Y >= landY)
                {
                    _items.RemoveAt(i);
                    Deliver(it.Kind);
                }
                continue;
            }
            if (!it.Resting)
            {
                it.Vy = MathF.Min(it.Vy + 200 * Dt, 110);
                it.Pos.Y += it.Vy * Dt;
                foreach (var p in Platforms)
                    if (it.Pos.X > p.X - 4 && it.Pos.X < p.Right + 4 && it.Pos.Y + 8 >= p.Y && it.Pos.Y + 8 - it.Vy * Dt <= p.Y + 2)
                    {
                        it.Pos.Y = p.Y - 8;
                        it.Resting = true;
                    }
                if (it.Pos.Y >= GroundY - 8)
                {
                    it.Pos.Y = GroundY - 8;
                    it.Resting = true;
                }
                if (it.Resting)
                    Sound.Play(Sfx.Thud, 0.3f, 0.4f);
            }
            // Pick up.
            if (_dead <= 0 && !HasCarried() && Vector2.Distance(it.Pos, _pos) < 18)
            {
                if (it.Kind == ItemKind.Gem)
                {
                    AddScore(250, it.Pos.X, it.Pos.Y - 12, Pal.Gold);
                    Fx.Burst(it.Pos.X, it.Pos.Y, Pal.Gold, 16, 80, 0.5f, 2f);
                    Sound.Play(Sfx.Coin);
                    _items.RemoveAt(i);
                    continue;
                }
                it.Carried = true;
                it.Resting = false;
                Sound.Play(Sfx.Pickup);
            }
        }
    }

    private bool HasCarried()
    {
        foreach (var it in _items)
            if (it.Carried)
                return true;
        return false;
    }

    private void Deliver(ItemKind kind)
    {
        Fx.Burst(RocketX, RocketTopY, Pal.Magenta, 16, 70, 0.5f, 2f);
        if (kind == ItemKind.Fuel)
        {
            _fuel++;
            AddScore(100, RocketX, RocketTopY - 20, Pal.Magenta);
            Sound.Play(Sfx.PowerUp, _fuel * 0.1f, 0.7f);
            if (_fuel >= FuelNeeded)
            {
                Fx.Float("ROCKET READY!", RocketX, RocketTopY - 40, Pal.Lime, 2f);
                Sound.Play(Sfx.Bonus);
            }
        }
        else
        {
            _parts++;
            AddScore(100, RocketX, RocketTopY - 20, Pal.Sky);
            Sound.Play(Sfx.Land, 0.3f);
        }
        NextObjective();
    }

    private void SpawnStuff()
    {
        _spawnTimer -= Dt;
        int max = Math.Min(3 + Level, 9);
        if (_spawnTimer <= 0 && _aliens.Count < max)
        {
            _spawnTimer = MathF.Max(0.35f, 1.3f - Level * 0.08f);
            bool left = Chance(0.5f);
            int type = AlienType;
            float speed = 1 + (Level - 1) / 4 * 0.25f;
            var a = new Alien { Type = type, Pos = new Vector2(left ? -10 : 650, Rand(50, GroundY - 40)) };
            float dir = left ? 1 : -1;
            a.Vel = type switch
            {
                0 => new Vector2(dir * Rand(60, 95), Rand(15, 45)) * speed,
                1 => new Vector2(dir * Rand(55, 80), Rand(-60, 60)) * speed,
                2 => new Vector2(dir * 50, 0) * speed,
                _ => new Vector2(dir * 90, 0) * speed,
            };
            _aliens.Add(a);
        }
        _gemTimer -= Dt;
        if (_gemTimer <= 0)
        {
            _gemTimer = Rand(10, 18);
            _items.Add(new Item { Kind = ItemKind.Gem, Pos = new Vector2(PickDropX(), Screen.HudHeight + 6), GemType = RandInt(0, 3) });
        }
    }

    private void UpdateAliens()
    {
        float speed = 1 + (Level - 1) / 4 * 0.25f;
        for (int i = _aliens.Count - 1; i >= 0; i--)
        {
            var a = _aliens[i];
            a.T += Dt;
            var to = Wrapped(_pos - a.Pos);
            switch (a.Type)
            {
                case 2:
                {
                    var want = Vector2.Normalize(to + new Vector2(0.01f, 0)) * (55 + Level * 3) * speed;
                    a.Vel = Vector2.Lerp(a.Vel, want, 1.2f * Dt);
                    break;
                }
                case 3:
                    if (a.T > 1.2f && a.T < 1.25f)
                        a.Vel = Vector2.Normalize(to + new Vector2(0.01f, 0)) * (130 + Level * 4) * speed;
                    break;
            }
            a.Pos += a.Vel * Dt;
            if (a.Pos.X < -12) a.Pos.X += 664;
            if (a.Pos.X > 652) a.Pos.X -= 664;

            // Platforms and ground.
            bool blocked = a.Pos.Y > GroundY - 6 || a.Pos.Y < Screen.HudHeight + 6;
            int hitPlatform = -1;
            for (int p = 0; p < Platforms.Length; p++)
                if (Platforms[p].Inflate(5, 5).Contains(a.Pos))
                    hitPlatform = p;
            if (blocked || hitPlatform >= 0)
            {
                if (a.Type == 0 || a.Type == 3)
                {
                    // Meteors and dive-bombers burst on impact.
                    KillAlien(i, false);
                    continue;
                }
                if (a.Type == 1)
                {
                    if (hitPlatform >= 0)
                    {
                        var pr = Platforms[hitPlatform];
                        if (a.Pos.Y < pr.CenterY) a.Pos.Y = pr.Y - 6; else a.Pos.Y = pr.Bottom + 6;
                    }
                    else
                    {
                        a.Pos.Y = MathF2.Clamp(a.Pos.Y, Screen.HudHeight + 6, GroundY - 6);
                    }
                    a.Vel.Y = -a.Vel.Y;
                }
                else
                {
                    a.Pos -= a.Vel * Dt;
                    a.Vel.Y = -a.Vel.Y * 0.5f;
                }
            }

            if (_dead <= 0 && _invuln <= 0 && _launch <= 0 && MathF.Abs(to.X) < 12 && MathF.Abs(to.Y) < 16)
            {
                KillAlien(i, false);
                PlayerDies();
                return;
            }
        }
    }

    private static Vector2 Wrapped(Vector2 d)
    {
        if (d.X > 320) d.X -= 640;
        if (d.X < -320) d.X += 640;
        return d;
    }

    private void PlayerDies()
    {
        Fx.Explode(_pos.X, _pos.Y, 1.4f);
        Fx.Burst(_pos.X, _pos.Y, Pal.White, 20, 120, 0.8f, 2f);
        Sound.Play(Sfx.BigExplode);
        _dead = 1.6f;
        _beams.Clear();
        if (Lives <= 1)
        {
            Lives = 0;
            EndGame(false, "Lost in space on planet " + Level + ".");
            return;
        }
        LoseLife();
    }

    private void UpdateLaunch()
    {
        float before = _launch;
        _launch -= Dt;
        float t = 3.2f - _launch;
        Sound.Loop(LoopSfx.Thrust, true, -0.2f + t * 0.2f, 0.8f);
        Fx.Shake(2 + t, 0.1f);
        float y = RocketLift(t);
        Fx.Spark(RocketX + Rand(-6, 6), GroundY - y + 2, Rand(-30, 30), Rand(120, 220), Pal.Yellow, 0.4f, 3f);
        Fx.Spark(RocketX + Rand(-8, 8), GroundY - y + 4, Rand(-40, 40), Rand(80, 160), Pal.Orange, 0.6f, 3.5f);
        if (t < 1.4f && Tick % 2 == 0)
            Fx.Burst(RocketX + Rand(-30, 30), GroundY - 2, Pal.LightGrey, 3, 90, 0.9f, 4f, -30, false);
        foreach (var a in _aliens)
            Fx.Burst(a.Pos.X, a.Pos.Y, AlienColour(a.Type), 8, 60, 0.4f);
        _aliens.Clear();
        if (_launch <= 0 && before > 0)
        {
            Level++;
            Sound.Play(Sfx.LevelUp);
            StartPlanet((Level - 1) % 4 == 0);
            _landing = 2.4f;
        }
    }

    private static float RocketLift(float t) => t < 0.6f ? 0 : (t - 0.6f) * (t - 0.6f) * 90;

    // ------------------------------------------------------------------ drawing

    public override void Draw(Gfx g)
    {
        float t = Time;
        var hue = (Level - 1) * 67f;
        g.GradientV(0, 0, 640, GroundY, Pal.Hsv(240 + hue * 0.3f, 0.8f, 0.1f), Pal.Hsv(280 + hue, 0.6f, 0.3f));
        Backdrops.Stars(g, t, 0, 20 + Level, new RectF(0, 0, 640, GroundY), 100);
        // A big planet in the sky.
        var pc = Pal.Hsv(200 + hue, 0.6f, 0.75f);
        g.Glow(150, 80, 100, pc, 0.25f);
        g.Circle(150, 80, 40, pc);
        g.Circle(143, 86, 38, Pal.Darken(pc, 0.55f));
        g.Ellipse(136, 92, 7, 4, Pal.Darken(pc, 0.7f));
        g.Ellipse(160, 98, 5, 3, Pal.Darken(pc, 0.7f));
        g.Ellipse(124, 70, 4, 3, Pal.Darken(pc, 0.65f));

        // Ground.
        var ground = Pal.Hsv(30 + hue, 0.6f, 0.55f);
        g.GradientV(0, GroundY, 640, 360 - GroundY, ground, Pal.Darken(ground, 0.7f));
        g.Rect(0, GroundY, 640, 2, Pal.Lighten(ground, 0.4f));
        for (int i = 0; i < 16; i++)
            g.Ellipse((i * 113) % 640, GroundY + 8 + (i % 3) * 4, 6 + i % 4, 1.5f, Pal.Darken(ground, 0.3f));

        // Platforms.
        foreach (var p in Platforms)
        {
            g.Glow(p.CenterX, p.CenterY, p.W * 0.6f, Pal.Lime, 0.15f);
            g.GradientV(p.X, p.Y, p.W, p.H, Pal.Lime, Pal.Forest);
            g.Rect(p.X, p.Y, p.W, 1.5f, Pal.White * 0.7f);
            for (float x = p.X + 6; x < p.Right - 4; x += 12)
                g.Rect(x, p.Y + 3, 6, 2, Pal.Forest);
        }

        // Rocket.
        float lift = _launch > 0 ? RocketLift(3.2f - _launch) : _landing > 0 ? _landing * _landing * 60 : 0;
        DrawRocket(g, RocketX, GroundY - lift, _parts, _fuel / FuelNeeded, 1f, _launch > 0 || _landing > 0, t);
        if (_parts < 3 && _launch <= 0 && _landing <= 0)
        {
            // Ghost outline of the missing parts.
            for (int k = _parts; k < 3; k++)
                g.RectOutline(RocketX - SegW / 2, GroundY - (k + 1) * SegH, SegW, SegH, 1, Pal.White * (0.15f + 0.1f * MathF2.Pulse(t)));
        }

        // Items.
        foreach (var it in _items)
            if (!it.Carried)
                DrawItem(g, it, 1f, t);

        // Aliens.
        foreach (var a in _aliens)
            DrawAlien(g, a.Pos, a.Type, a.T, a.Vel.X < 0, 1f);

        // Beams.
        foreach (var b in _beams)
        {
            float x0 = b.X0, x1 = b.Head;
            g.Line(x0, b.Y, x1, b.Y, 5, Pal.Add(b.Colour, 0.25f));
            g.Line(x0, b.Y, x1, b.Y, 2, Pal.Lighten(b.Colour, 0.3f));
            g.Glow(x1, b.Y, 8, b.Colour, 0.8f);
        }

        // Player.
        if (_dead <= 0 && _launch <= 0 && _landing <= 0 && (_invuln <= 0 || (int)(_invuln * 10) % 2 == 0))
        {
            foreach (var it in _items)
                if (it.Carried)
                    DrawItem(g, it, 1f, t);
            int frame = !_grounded ? 2 : MathF.Abs(_vel.X) > 10 ? (int)(_walk * 8) % 2 : 0;
            if (_thrusting)
            {
                float fl = 0.7f + 0.3f * MathF.Sin(t * 40);
                g.Glow(_pos.X - _facing * 6, _pos.Y + 10, 12 * fl, Pal.Orange, 0.8f);
                g.Triangle(new Vector2(_pos.X - _facing * 8 - 3, _pos.Y + 6), new Vector2(_pos.X - _facing * 8 + 3, _pos.Y + 6),
                    new Vector2(_pos.X - _facing * 8, _pos.Y + 14 + 6 * fl), Pal.Yellow);
            }
            g.PixelsCentered(ManArt[frame], _pos.X, _pos.Y, 2f, _facing < 0);
            // Wrap ghost at the edges.
            if (_pos.X < 12 || _pos.X > 628)
                g.PixelsCentered(ManArt[frame], _pos.X + (_pos.X < 12 ? 640 : -640), _pos.Y, 2f, _facing < 0);
        }

        // Fuel gauge.
        if (_parts == 3)
        {
            g.Text("FUEL", RocketX + 20, GroundY + 6, 1f, Pal.Magenta);
            for (int i = 0; i < FuelNeeded; i++)
                g.Rect(RocketX + 46 + i * 9, GroundY + 6, 7, 7, i < _fuel ? Pal.Magenta : Pal.DarkGrey);
        }

        if (_banner > 0)
        {
            float a = MathF.Min(1, _banner);
            g.TextShadow("PLANET " + Level, 320, 120, 3f, Pal.Yellow * a, Align.Center);
            g.TextShadow(_parts < 3 ? "BUILD THE ROCKET" : "FUEL THE ROCKET", 320, 152, 1.5f, Pal.Cyan * a, Align.Center);
        }
    }

    private static void DrawRocket(Gfx g, float x, float baseY, int parts, float fuel, float s, bool firing, float t)
    {
        float w = SegW * s, h = SegH * s;
        if (firing)
        {
            float fl = 0.7f + 0.3f * MathF.Sin(t * 50);
            g.Glow(x, baseY + 6 * s, 40 * s * fl, Pal.Orange, 0.9f);
            g.Triangle(new Vector2(x - 8 * s, baseY), new Vector2(x + 8 * s, baseY), new Vector2(x, baseY + 34 * s * fl), Pal.Orange);
            g.Triangle(new Vector2(x - 4 * s, baseY), new Vector2(x + 4 * s, baseY), new Vector2(x, baseY + 20 * s * fl), Pal.Yellow);
        }
        float total = parts * h;
        for (int k = 0; k < parts; k++)
        {
            float top = baseY - (k + 1) * h;
            if (k == 2)
            {
                // Nose cone.
                g.Triangle(new Vector2(x - w / 2, top + h), new Vector2(x + w / 2, top + h), new Vector2(x, top - 6 * s), Pal.Silver);
                g.Triangle(new Vector2(x - w / 2, top + h), new Vector2(x, top + h), new Vector2(x, top - 6 * s), Pal.White);
            }
            else
            {
                g.GradientH(x - w / 2, top, w, h, Pal.LightGrey, Pal.Grey);
                g.Rect(x - w / 2, top, w, 1.5f * s, Pal.White);
                g.RectOutline(x - w / 2, top, w, h, 1 * s, Pal.DarkGrey);
                if (k == 0)
                {
                    g.Triangle(new Vector2(x - w / 2, top + h * 0.3f), new Vector2(x - w / 2, top + h), new Vector2(x - w / 2 - 8 * s, top + h), Pal.Red);
                    g.Triangle(new Vector2(x + w / 2, top + h * 0.3f), new Vector2(x + w / 2, top + h), new Vector2(x + w / 2 + 8 * s, top + h), Pal.Red);
                }
                else
                {
                    g.Circle(x, top + h / 2, 5 * s, Pal.DarkGrey);
                    g.Circle(x, top + h / 2, 3.5f * s, Pal.Sky);
                }
            }
        }
        // Fuel fill (glowing purple, rising from the bottom).
        if (fuel > 0 && parts >= 2)
        {
            float fh = MathF.Min(total, 2 * h) * fuel;
            g.Rect(x - w / 2 + 1 * s, baseY - fh, w - 2 * s, fh, Pal.Magenta * 0.55f);
            g.Glow(x, baseY - fh, 14 * s, Pal.Magenta, 0.3f);
        }
    }

    private static void DrawItem(Gfx g, Item it, float s, float t)
    {
        var p = it.Pos;
        switch (it.Kind)
        {
            case ItemKind.Middle:
                g.Glow(p, 20 * s, Pal.Sky, 0.3f);
                g.GradientH(p.X - SegW / 2 * s, p.Y - SegH / 2 * s, SegW * s, SegH * s, Pal.LightGrey, Pal.Grey);
                g.Circle(p.X, p.Y, 5 * s, Pal.DarkGrey);
                g.Circle(p.X, p.Y, 3.5f * s, Pal.Sky);
                break;
            case ItemKind.Nose:
                g.Glow(p, 20 * s, Pal.Sky, 0.3f);
                g.Triangle(new Vector2(p.X - SegW / 2 * s, p.Y + SegH / 2 * s), new Vector2(p.X + SegW / 2 * s, p.Y + SegH / 2 * s), new Vector2(p.X, p.Y - SegH / 2 * s - 6 * s), Pal.Silver);
                break;
            case ItemKind.Fuel:
                g.Glow(p, 18 * s, Pal.Magenta, 0.4f + 0.2f * MathF.Sin(t * 6));
                g.RoundRect(p.X - 9 * s, p.Y - 7 * s, 18 * s, 15 * s, 3 * s, Pal.Purple);
                g.Rect(p.X - 9 * s, p.Y - 2 * s, 18 * s, 5 * s, Pal.Magenta);
                g.Text("F", p.X - 2.4f * s, p.Y - 2.4f * s, 0.8f * s, Pal.White);
                break;
            default:
            {
                var c = it.GemType == 0 ? Pal.Cyan : it.GemType == 1 ? Pal.Gold : Pal.Red;
                g.Glow(p, 16 * s, c, 0.5f + 0.3f * MathF.Sin(t * 8));
                g.Triangle(new Vector2(p.X - 7 * s, p.Y - 2 * s), new Vector2(p.X + 7 * s, p.Y - 2 * s), new Vector2(p.X, p.Y + 8 * s), c);
                g.Triangle(new Vector2(p.X - 7 * s, p.Y - 2 * s), new Vector2(p.X + 7 * s, p.Y - 2 * s), new Vector2(p.X, p.Y - 7 * s), Pal.Lighten(c, 0.5f));
                break;
            }
        }
    }

    private static void DrawAlien(Gfx g, Vector2 p, int type, float t, bool left, float s)
    {
        var c = AlienColour(type);
        switch (type)
        {
            case 0:
            {
                // Fuzzy meteor with a tail.
                float d = left ? 1 : -1;
                for (int i = 1; i <= 4; i++)
                    g.Circle(p.X + d * i * 4 * s, p.Y - i * 1.5f * s, (5 - i) * s, c * (0.5f - i * 0.1f));
                g.Glow(p, 14 * s, c, 0.6f);
                for (int i = 0; i < 8; i++)
                {
                    float a = i * MathF.PI / 4 + t * 4;
                    g.Circle(p.X + MathF.Cos(a) * 5 * s, p.Y + MathF.Sin(a) * 5 * s, 2.2f * s, c);
                }
                g.Circle(p.X, p.Y, 4.5f * s, Pal.Yellow);
                break;
            }
            case 1:
                g.Glow(p, 16 * s, c, 0.4f);
                g.Circle(p.X, p.Y, 8 * s, c * 0.35f);
                g.Ring(p.X, p.Y, 8 * s, 1.5f * s, c);
                g.Circle(p.X - 3 * s, p.Y - 3 * s, 2 * s, Pal.White);
                break;
            case 2:
                g.Glow(p, 16 * s, c, 0.5f);
                g.Ellipse(p.X, p.Y + 1 * s, 10 * s, 4 * s, c);
                g.Ellipse(p.X, p.Y - 2 * s, 5 * s, 4 * s, Pal.Lighten(c, 0.5f));
                for (int i = -1; i <= 1; i++)
                    g.Circle(p.X + i * 5 * s, p.Y + 1.5f * s, 1.2f * s, (int)(t * 8 + i) % 2 == 0 ? Pal.White : Pal.Yellow);
                break;
            default:
            {
                float d = left ? -1 : 1;
                g.Glow(p, 16 * s, c, 0.5f);
                g.Triangle(new Vector2(p.X + d * 10 * s, p.Y), new Vector2(p.X - d * 8 * s, p.Y - 7 * s), new Vector2(p.X - d * 4 * s, p.Y), c);
                g.Triangle(new Vector2(p.X + d * 10 * s, p.Y), new Vector2(p.X - d * 4 * s, p.Y), new Vector2(p.X - d * 8 * s, p.Y + 7 * s), Pal.Darken(c, 0.3f));
                g.Glow(p.X - d * 7 * s, p.Y, 5 * s, Pal.Orange, 0.8f);
                break;
            }
        }
    }

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        float s = r.H / 70f;
        g.GradientV(r.X, r.Y, r.W, r.H, new Color(10, 6, 30), new Color(70, 20, 80));
        Backdrops.Stars(g, time, 0, 21, r, 40);
        float gy = r.Bottom - 8 * s;
        g.GradientV(r.X, gy, r.W, 8 * s, new Color(160, 90, 50), new Color(60, 30, 20));
        g.GradientV(r.X + 8 * s, r.Y + 30 * s, 40 * s, 4 * s, Pal.Lime, Pal.Forest);
        DrawRocket(g, r.Right - 26 * s, gy, 3, 0.4f + 0.4f * MathF2.Pulse(time, 3), 0.85f * s, false, time);
        float px = r.X + r.W * 0.38f + MathF.Sin(time) * 10 * s, py = r.CenterY - 2 * s + MathF.Sin(time * 1.3f) * 5 * s;
        float fl = 0.7f + 0.3f * MathF.Sin(time * 40);
        g.Glow(px - 6 * s, py + 12 * s, 10 * s * fl, Pal.Orange, 0.8f);
        g.Triangle(new Vector2(px - 9 * s, py + 7 * s), new Vector2(px - 3 * s, py + 7 * s), new Vector2(px - 6 * s, py + (16 + 5 * fl) * s), Pal.Yellow);
        g.PixelsCentered(ManArt[2], px, py, 2f * s);
        float bx = px + 12 * s;
        float len = (time * 2 % 1) * 70 * s;
        var bc = Pal.Rainbow[(int)(time * 10) % 6];
        g.Line(bx, py - 1 * s, bx + len, py - 1 * s, 3 * s, Pal.Add(bc, 0.4f));
        g.Line(bx, py - 1 * s, bx + len, py - 1 * s, 1.4f * s, Pal.Lighten(bc, 0.4f));
        DrawAlien(g, new Vector2(r.X + r.W * 0.72f, r.Y + 18 * s + MathF.Sin(time * 2) * 4 * s), 2, time, true, s * 0.9f);
        DrawAlien(g, new Vector2(r.X + r.W * 0.18f, r.Y + 16 * s), 0, time, false, s * 0.8f);
    }

    // ------------------------------------------------------------------ autopilot

    public override void AutoPlay(Controls c)
    {
        if (_dead > 0 || _launch > 0 || _landing > 0)
            return;

        // Shoot aliens that are lined up, and turn to face close ones at our height.
        Alien threat = null;
        float threatDist = float.MaxValue;
        foreach (var a in _aliens)
        {
            var d = Wrapped(a.Pos - _pos);
            float dist = d.Length();
            if (dist < threatDist)
            {
                threatDist = dist;
                threat = a;
            }
        }
        bool fire = false;
        float ax = 0;
        bool up = false;
        if (threat != null)
        {
            var d = Wrapped(threat.Pos - _pos);
            if (MathF.Abs(d.Y) < 10 && MathF.Abs(d.X) < 220)
            {
                if (MathF.Sign(d.X) == _facing)
                    fire = true;
                else if (MathF.Abs(d.X) < 140)
                    ax = MathF.Sign(d.X) * 0.5f;
            }
        }

        // Navigate towards the current goal.
        Vector2 goal;
        Item carried = null;
        foreach (var it in _items)
            if (it.Carried)
                carried = it;
        if (carried != null)
            goal = new Vector2(RocketX, MathF.Min(RocketTopY - 50, 200));
        else if (_objective != null && !_objective.Delivering && (_objective.Resting || _objective.Pos.Y > 120))
            goal = _objective.Pos;
        else if (_parts == 3 && _fuel >= FuelNeeded)
            goal = new Vector2(RocketX, GroundY - 14);
        else
        {
            goal = new Vector2(320, 90);
            foreach (var it in _items)
                if (it.Kind == ItemKind.Gem && it.Resting)
                    goal = it.Pos;
        }

        // Going under a platform: drop down beside it first.
        foreach (var p in Platforms)
            if (goal.X > p.X - 10 && goal.X < p.Right + 10 && p.Y < goal.Y && _pos.Y < p.Bottom + 4)
            {
                float edgeX = MathF.Abs(_pos.X - (p.X - 18)) < MathF.Abs(_pos.X - (p.Right + 18)) ? p.X - 18 : p.Right + 18;
                goal = new Vector2(edgeX, MathF.Abs(edgeX - _pos.X) < 12 ? goal.Y : MathF.Min(_pos.Y, 80));
                break;
            }
        var to = Wrapped(goal - _pos);
        float wy = goal.Y;
        if (MathF.Abs(to.X) > 30)
            wy = MathF.Min(goal.Y, 80);
        // Under a platform and need to go up: slide out from under it first.
        foreach (var p in Platforms)
            if (_pos.X > p.X - 10 && _pos.X < p.Right + 10 && p.Y > goal.Y && p.Y < _pos.Y)
            {
                float edge = MathF.Abs(_pos.X - p.X) < MathF.Abs(_pos.X - p.Right) ? p.X - 18 : p.Right + 18;
                to = new Vector2(edge - _pos.X, 0);
                wy = MathF.Max(_pos.Y, p.Bottom + 18);
            }
        if (ax == 0)
            ax = MathF.Abs(to.X) < 4 ? 0 : MathF2.Clamp(to.X / 40, -1, 1);
        // Hold height near the waypoint height (thrust is a binary decision).
        float wantVy = MathF2.Clamp((wy - _pos.Y) * 3, -140, 160);
        up = _vel.Y > wantVy;
        // Fight anything that comes close: line up with it, face it and fire.
        if (threat != null && threatDist < 56)
        {
            // Too close: back off, shooting if it happens to be lined up.
            var d = Wrapped(threat.Pos - _pos);
            up = d.Y > -4;
            if (!fire)
                ax = -MathF.Sign(d.X);
        }
        else if (threat != null && threatDist < 130 && MathF.Abs(Wrapped(threat.Pos - _pos).Y) < 36 && (threat.Type == 1 || threat.Type == 2))
        {
            var d = Wrapped(threat.Pos - _pos);
            wantVy = MathF2.Clamp(d.Y * 4, -140, 160);
            up = _vel.Y > wantVy;
            if (MathF.Sign(d.X) != _facing)
                ax = MathF.Sign(d.X) * 0.4f;
            else if (MathF.Abs(d.X) < 80)
                ax = -MathF.Sign(d.X) * 0.7f;
            else
                ax = 0;
            fire = MathF.Abs(d.Y) < 9 && MathF.Sign(d.X) == _facing;
        }
        c.SetDirections(ax, up ? -1 : 0);
        c.Fire = fire && _fireCool <= 0;
        c.FirePressed = c.Fire;
    }
}
