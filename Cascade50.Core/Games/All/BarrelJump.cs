using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 02 Barrel Jump: climb a zig-zag of sloping girders while a brute at the top hurls barrels down
/// at you. Jump them, grab the bonus items and climb to the captive to finish the level.
/// </summary>
public sealed class BarrelJump : MiniGame, Capture.ICaptureHints
{
    public override int Number => 2;
    public override string Title => "Barrel Jump";
    public override Category Category => Category.Arcade;
    public override string Tagline => "Dodge the brute's barrels and climb the girders to the top.";
    public override Color Accent => Pal.Red;

    public override string[] HowToPlay =>
    [
        "Climb to the captive at the top. The brute rolls barrels down the slopes and ladders.",
        "Jump a barrel for 100. Grab the hat, bag and umbrella. Reach the top to bank the bonus timer.",
        "Each level is faster. 3 lives.",
    ];

    public override string[] DesktopControls => ["ARROWS / WASD to run and climb, SPACE to jump.", "P or ESC to pause."];
    public override string[] TouchControls => ["Stick to run and climb, JUMP to jump."];
    public override Pad Pad => Pad.Stick | Pad.Fire;
    public override string FireLabel => "JUMP";
    public int CaptureTicks => 780;

    // ------------------------------------------------------------------ level layout

    private readonly struct Girder
    {
        public readonly float X0, X1, Y0, Y1;

        public Girder(float x0, float x1, float y0, float y1)
        {
            X0 = x0;
            X1 = x1;
            Y0 = y0;
            Y1 = y1;
        }

        public float YAt(float x) => Y0 + (Y1 - Y0) * (MathF2.Clamp(x, X0, X1) - X0) / (X1 - X0);

        /// <summary>The direction a barrel rolls on this girder (downhill).</summary>
        public int Downhill => Y1 > Y0 ? 1 : -1;
    }

    private readonly struct Ladder
    {
        public readonly float X;
        public readonly int Upper; // girder above (-1 = the captive's platform)

        public Ladder(float x, int upper)
        {
            X = x;
            Upper = upper;
        }

        public int Lower => Upper + 1;
    }

    // Girders from the top (0) to the ground (5).
    private static readonly Girder[] Girders =
    [
        new(40, 560, 90, 98),
        new(80, 600, 150, 142),
        new(40, 560, 196, 204),
        new(80, 600, 256, 248),
        new(40, 560, 300, 308),
        new(0, 640, 352, 346),
    ];

    private const float PlatformY = 60, PlatformX0 = 196, PlatformX1 = 320;

    private static readonly Ladder[] Ladders =
    [
        new(292, -1),
        new(310, 0), new(500, 0),
        new(150, 1), new(390, 1),
        new(260, 2), new(510, 2),
        new(130, 3), new(420, 3),
        new(300, 4), new(530, 4),
    ];

    private const float BruteX = 74, DrumX = 26;
    private const float BarrelR = 6;
    private const float Gravity = 640, JumpSpeed = 178, WalkSpeed = 72, ClimbSpeed = 46;

    // ------------------------------------------------------------------ pixel art

    private static readonly Dictionary<char, Color> Colours = new()
    {
        ['y'] = Pal.Yellow, ['Y'] = Pal.Gold, ['s'] = Pal.Skin, ['K'] = new Color(30, 20, 20), ['o'] = Pal.Orange,
        ['b'] = Pal.Blue, ['k'] = new Color(70, 40, 25), ['n'] = new Color(120, 70, 35), ['N'] = new Color(80, 45, 20),
        ['t'] = new Color(220, 170, 120), ['w'] = Pal.White, ['R'] = Pal.Red, ['h'] = Pal.Gold, ['p'] = Pal.Pink,
        ['P'] = Pal.Magenta, ['u'] = Pal.Purple, ['c'] = Pal.Cyan, ['g'] = Pal.Lime,
    };

    private static readonly PixelArt[] HeroWalk =
    [
        new(["..yyyy..", ".yyyyyyy", "..ssss..", "..sKsK..", "..ssss..", "...ss...", ".oooooo.", "oooooooo", "s.oooo.s",
             "..bbbb..", "..bbbb..", ".bb..bb.", ".b....b.", "kk....kk"], Colours),
        new(["..yyyy..", ".yyyyyyy", "..ssss..", "..sKsK..", "..ssss..", "...ss...", ".oooooo.", "oooooooo", "s.oooo.s",
             "..bbbb..", "..bbbb..", "...bb...", "...bb...", "...kkk.."], Colours),
    ];

    private static readonly PixelArt HeroJump = new(
    [
        "..yyyy..", ".yyyyyyy", "..ssss..", "..sKsK..", "..ssss..", "s..ss..s", "soooooos", ".oooooo.", "..oooo..",
        "..bbbb..", ".bb..bb.", "bb....bb", "k......k",
    ], Colours);

    private static readonly PixelArt HeroClimb = new(
    [
        "..yyyy..", ".yyyyyy.", "..kkkk..", "..kkkk..", "..kkkk..", "s..kk...", "soooooo.", ".oooooos", "..oooo.s",
        "..bbbb..", "..bbbb..", ".bb..bb.", ".b...bb.", "kk......",
    ], Colours);

    private static readonly PixelArt BruteIdle = new(
    [
        ".....nnnnnn.....",
        "....nnnnnnnn....",
        "...nnttttttnn...",
        "...ntwKttwKtn...",
        "...nttttttttn...",
        "..nnttKRRKttnn..",
        ".nnnnttttttnnnn.",
        "nnnnnnnnnnnnnnnn",
        "nnn.nnttttnn.nnn",
        "nnn.nttttttn.nnn",
        "nnn.nttttttn.nnn",
        "ttt.nnnnnnnn.ttt",
        "...nnnn..nnnn...",
        "...nnn....nnn...",
        "..NNNN....NNNN..",
    ], Colours);

    private static readonly PixelArt BruteUp = new(
    [
        "tt...nnnnnn...tt",
        "nn..nnnnnnnn..nn",
        "nn.nnttttttnn.nn",
        "nn.ntwKttwKtn.nn",
        "nn.nttttttttn.nn",
        "nnnnttKRRKttnnnn",
        ".nnnnttttttnnnn.",
        "..nnnnnnnnnnnn..",
        "...nnttttttnn...",
        "...nttttttttn...",
        "...nttttttttn...",
        "....nnnnnnnn....",
        "...nnnn..nnnn...",
        "...nnn....nnn...",
        "..NNNN....NNNN..",
    ], Colours);

    private static readonly PixelArt Captive = new(
    [
        "..hhhh..", ".hhhhhh.", ".hssssh.", ".hsKsKh.", ".hssssh.", "hh.ss.hh", "s.pppp.s", "..pppp..", ".pPPPPp.",
        ".pppppp.", "pppppppp", "..s..s..", "..s..s..", ".PP..PP.",
    ], Colours);

    private static readonly PixelArt[] ItemArt =
    [
        new(["...uu...", "..uuuu..", "..uuuu..", "..uuuu..", "ppuuuupp", "pppppppp"], Colours), // hat
        new(["..oooo..", ".o....o.", "oooooooo", "oYYYYYYo", "oooooooo", "oooooooo"], Colours), // bag
        new(["..cccc..", ".cccccc.", "cccccccc", "...w....", "...w....", "...w.w..", "....w..."], Colours), // umbrella
    ];

    // ------------------------------------------------------------------ state

    private enum BarrelState
    {
        Rolling,
        Falling,
        OnLadder,
    }

    private sealed class Barrel
    {
        public float X, Y, Vy, Rot;
        public int G, Dir;
        public BarrelState State;
        public int ScoredJump = -1;
        public bool Bounced;
    }

    private struct Item
    {
        public float X;
        public int G, Kind;
        public bool Taken;
    }

    private readonly List<Barrel> _barrels = new();
    private readonly List<Item> _items = new();

    private float _px, _py, _vy, _jumpVx;
    private int _pg;           // girder the hero stands on / jumped from
    private int _ladder = -1;  // ladder being climbed
    private bool _air;
    private bool _faceLeft;
    private int _jumpId;
    private float _walkAnim;
    private int _stepTick;

    private float _throwTimer, _bruteAnim;
    private int _bonus;
    private float _bonusTimer;
    private float _dying, _rescued, _banner;

    protected override void Start()
    {
        Lives = 3;
        Level = 1;
        ResetLevel();
    }

    private void ResetLevel()
    {
        _barrels.Clear();
        _items.Clear();
        _items.Add(new Item { X = 470, G = 1, Kind = 0 });
        _items.Add(new Item { X = 120, G = 2, Kind = 1 });
        _items.Add(new Item { X = 360, G = 3, Kind = 2 });
        _px = 80;
        _pg = 5;
        _py = Girders[5].YAt(_px);
        _air = false;
        _ladder = -1;
        _faceLeft = false;
        _throwTimer = 1.6f;
        _bruteAnim = 0;
        _bonus = 5000;
        _bonusTimer = 0;
        _dying = 0;
        _rescued = 0;
        _banner = 2f;
    }

    private float BarrelSpeed => MathF.Min(150, 78 + (Level - 1) * 13);
    private float ThrowInterval => MathF.Max(1.05f, 2.7f - (Level - 1) * 0.25f);

    protected override void Update()
    {
        if (_banner > 0)
            _banner -= Dt;

        if (_rescued > 0)
        {
            _rescued -= Dt;
            if (Tick % 6 == 0)
                Fx.Spark(Rand(PlatformX0 + 20, PlatformX1 - 20), PlatformY - 20, Rand(-20, 20), -40, Pal.Pink, 0.8f, 2.5f);
            if (_rescued <= 0)
            {
                Level++;
                ResetLevel();
            }
            return;
        }

        if (_dying > 0)
        {
            _dying -= Dt;
            if (_dying <= 0 && !LoseLife())
                ResetLevel();
            return;
        }

        // Bonus timer: runs down in hundreds; at zero the hero is out of time.
        _bonusTimer += Dt;
        if (_bonusTimer >= 1.5f)
        {
            _bonusTimer -= 1.5f;
            _bonus = Math.Max(0, _bonus - 100);
            if (_bonus <= 1000 && _bonus > 0)
                Sound.Play(Sfx.Tick, 0.4f, 0.6f);
            if (_bonus == 0)
            {
                Kill();
                return;
            }
        }

        UpdateHero();
        if (_dying > 0 || _rescued > 0)
            return;
        UpdateBrute();
        UpdateBarrels();
    }

    // ------------------------------------------------------------------ hero

    private void UpdateHero()
    {
        if (_ladder >= 0)
        {
            Climb();
            return;
        }

        var g = Girders[_pg];
        if (_air)
        {
            _px = MathF2.Clamp(_px + _jumpVx * Dt, g.X0 + 4, g.X1 - 4);
            _py += _vy * Dt;
            _vy += Gravity * Dt;
            float floor = g.YAt(_px);
            if (_vy > 0 && _py >= floor)
            {
                _py = floor;
                _air = false;
                Sound.Play(Sfx.Land, 0, 0.35f);
            }
        }
        else
        {
            float ax = In.AxisX;
            if (MathF.Abs(ax) > 0.3f)
            {
                _px = MathF2.Clamp(_px + MathF.Sign(ax) * WalkSpeed * Dt, g.X0 + 4, g.X1 - 4);
                _faceLeft = ax < 0;
                _walkAnim += Dt * 8;
                if (++_stepTick % 14 == 0)
                    Sound.Play(Sfx.Step, Rand(0.2f, 0.5f), 0.25f);
            }
            _py = g.YAt(_px);

            if (In.FirePressed)
            {
                _air = true;
                _vy = -JumpSpeed;
                _jumpVx = MathF.Abs(ax) > 0.3f ? MathF.Sign(ax) * WalkSpeed : 0;
                _jumpId++;
                Sound.Play(Sfx.Jump, 0.1f, 0.6f);
            }
            else if (In.AxisY < -0.4f || In.AxisY > 0.4f)
            {
                bool up = In.AxisY < 0;
                for (int i = 0; i < Ladders.Length; i++)
                {
                    var l = Ladders[i];
                    if (MathF.Abs(l.X - _px) > 7)
                        continue;
                    if (up && l.Lower == _pg)
                    {
                        _ladder = i;
                        _px = l.X;
                        _py = LadderBottom(l) - 1;
                        break;
                    }
                    if (!up && l.Upper == _pg)
                    {
                        _ladder = i;
                        _px = l.X;
                        _py = LadderTop(l) + 1;
                        break;
                    }
                }
            }
        }

        // Bonus items.
        for (int i = 0; i < _items.Count; i++)
        {
            var it = _items[i];
            if (it.Taken || it.G != _pg)
                continue;
            float iy = Girders[it.G].YAt(it.X) - 8;
            if (MathF.Abs(it.X - _px) < 10 && MathF.Abs(iy - (_py - 8)) < 16)
            {
                it.Taken = true;
                _items[i] = it;
                int pts = 300 * Math.Min(Level, 3);
                AddScore(pts, it.X, iy - 12, Pal.Pink);
                Fx.Burst(it.X, iy, Pal.Pink, 16, 90, 0.5f, 2f);
                Sound.Play(Sfx.Pickup);
            }
        }

        // Too close to the brute.
        if (_pg == 0 && _px < BruteX + 22)
            Kill();
    }

    private static float LadderTop(Ladder l) => l.Upper < 0 ? PlatformY : Girders[l.Upper].YAt(l.X);
    private static float LadderBottom(Ladder l) => Girders[l.Lower].YAt(l.X);

    private void Climb()
    {
        var l = Ladders[_ladder];
        float top = LadderTop(l), bottom = LadderBottom(l);
        float ay = In.AxisY;
        if (MathF.Abs(ay) > 0.3f)
        {
            _py = MathF2.Clamp(_py + MathF.Sign(ay) * ClimbSpeed * Dt, top, bottom);
            _walkAnim += Dt * 6;
            if (++_stepTick % 16 == 0)
                Sound.Play(Sfx.Step, 0.7f, 0.2f);
        }
        if (_py <= top + 0.01f && ay < 0)
        {
            if (l.Upper < 0)
            {
                Rescue();
                return;
            }
            _pg = l.Upper;
            _ladder = -1;
            _py = top;
        }
        else if (_py >= bottom - 0.01f && ay > 0)
        {
            _pg = l.Lower;
            _ladder = -1;
            _py = bottom;
        }
        else if ((_py <= top + 0.5f || _py >= bottom - 0.5f) && MathF.Abs(In.AxisX) > 0.5f)
        {
            _pg = _py <= top + 0.5f ? (l.Upper < 0 ? l.Lower : l.Upper) : l.Lower;
            _ladder = -1;
            _py = Girders[_pg].YAt(_px);
        }
    }

    private void Rescue()
    {
        _ladder = -1;
        _py = PlatformY;
        _rescued = 2.6f;
        AddScore(_bonus, _px, PlatformY - 30, Pal.Gold);
        Fx.Burst(_px, PlatformY - 10, Pal.Pink, 30, 120, 0.9f, 2.5f);
        Fx.Burst(_px, PlatformY - 10, Pal.Gold, 20, 90, 0.7f, 2f);
        Sound.Play(Sfx.LevelUp);
        foreach (var b in _barrels)
            Fx.Burst(b.X, b.Y, Pal.Orange, 8, 60, 0.4f);
        _barrels.Clear();
    }

    private void Kill()
    {
        if (_dying > 0)
            return;
        _dying = 1.6f;
        _ladder = -1;
        Fx.Burst(_px, _py - 8, Pal.White, 24, 110, 0.6f, 2f);
        Fx.Burst(_px, _py - 8, Pal.Orange, 16, 80, 0.7f, 2.5f);
        Fx.Shake(4, 0.3f);
        Sound.Play(Sfx.Hurt);
    }

    // ------------------------------------------------------------------ brute and barrels

    private void UpdateBrute()
    {
        if (_banner > 1.2f)
            return;
        _throwTimer -= Dt;
        if (_bruteAnim > 0)
        {
            _bruteAnim -= Dt;
            if (_bruteAnim <= 0)
            {
                var g = Girders[0];
                _barrels.Add(new Barrel { X = BruteX + 26, Y = g.YAt(BruteX + 26) - BarrelR, G = 0, Dir = g.Downhill });
                Sound.Play(Sfx.Thud, Rand(-0.4f, -0.1f), 0.8f);
                Fx.Shake(1.5f, 0.12f);
            }
        }
        else if (_throwTimer <= 0 && _barrels.Count < 4 + Level * 2)
        {
            _bruteAnim = 0.45f;
            _throwTimer = ThrowInterval * Rand(0.7f, 1.3f);
        }
    }

    private void UpdateBarrels()
    {
        float speed = BarrelSpeed;
        float ladderChance = MathF.Min(0.55f, 0.18f + Level * 0.05f);
        for (int i = _barrels.Count - 1; i >= 0; i--)
        {
            var b = _barrels[i];
            var g = Girders[b.G];
            switch (b.State)
            {
                case BarrelState.Rolling:
                {
                    float oldX = b.X;
                    b.X += b.Dir * speed * Dt;
                    b.Rot += b.Dir * speed * Dt / BarrelR;
                    b.Y = g.YAt(b.X) - BarrelR;

                    // Tumble down a ladder?
                    for (int l = 0; l < Ladders.Length; l++)
                    {
                        var ld = Ladders[l];
                        if (ld.Upper != b.G || (oldX - ld.X) * (b.X - ld.X) > 0)
                            continue;
                        float p = ladderChance + (_pg > b.G && _pg == ld.Lower && MathF.Abs(_px - ld.X) < 120 ? 0.2f : 0);
                        if (Chance(p))
                        {
                            b.State = BarrelState.OnLadder;
                            b.X = ld.X;
                        }
                        break;
                    }

                    if (b.State == BarrelState.Rolling)
                    {
                        if (b.G == Girders.Length - 1 && b.X < DrumX + 14)
                        {
                            // Into the burning drum.
                            Fx.Burst(DrumX, g.YAt(DrumX) - 24, Pal.Orange, 18, 90, 0.6f, 2.5f, -60);
                            Sound.Play(Sfx.Whoosh, -0.3f, 0.5f);
                            _barrels.RemoveAt(i);
                            continue;
                        }
                        if (b.X > g.X1 + 2 || b.X < g.X0 - 2)
                        {
                            b.State = BarrelState.Falling;
                            b.Vy = 0;
                            b.Bounced = false;
                            b.G++;
                        }
                    }
                    break;
                }
                case BarrelState.Falling:
                {
                    b.X += b.Dir * speed * 0.35f * Dt;
                    b.Y += b.Vy * Dt;
                    b.Vy += Gravity * Dt;
                    b.Rot += b.Dir * 0.2f;
                    float floor = Girders[b.G].YAt(b.X) - BarrelR;
                    if (b.Vy > 0 && b.Y >= floor)
                    {
                        b.Y = floor;
                        if (!b.Bounced && b.Vy > 120)
                        {
                            b.Vy = -b.Vy * 0.35f;
                            b.Bounced = true;
                            Sound.Play(Sfx.Bounce, Rand(-0.5f, -0.2f), 0.45f);
                            Fx.Burst(b.X, b.Y + BarrelR, Pal.Sand, 5, 40, 0.3f, 1.5f, 0, false);
                        }
                        else
                        {
                            b.State = BarrelState.Rolling;
                            b.Dir = Girders[b.G].Downhill;
                        }
                    }
                    break;
                }
                case BarrelState.OnLadder:
                {
                    b.Y += 80 * Dt;
                    float floor = Girders[b.G + 1].YAt(b.X) - BarrelR;
                    if (b.Y >= floor)
                    {
                        b.G++;
                        b.Y = floor;
                        b.State = BarrelState.Rolling;
                        b.Dir = Girders[b.G].Downhill;
                        Sound.Play(Sfx.Thud, Rand(0f, 0.3f), 0.35f);
                    }
                    break;
                }
            }

            // Hit the hero?
            float hx = _px, hy = _py - 8;
            float dx = b.X - hx, dy = b.Y - hy;
            if (MathF.Abs(dx) < 2 + BarrelR && MathF.Abs(dy) < 6 + BarrelR)
            {
                Kill();
                return;
            }

            // Jumped over it?
            if (_air && b.State == BarrelState.Rolling && b.G == _pg && b.ScoredJump != _jumpId && MathF.Abs(dx) < 9 && b.Y > _py)
            {
                b.ScoredJump = _jumpId;
                AddScore(100, b.X, _py - 24, Pal.Yellow);
                Sound.Play(Sfx.Coin, Rand(-0.1f, 0.2f), 0.7f);
            }
        }
    }

    // ------------------------------------------------------------------ drawing

    public override void Draw(Gfx g)
    {
        DrawBackdrop(g, Screen.Bounds, Time);

        // Girders.
        for (int i = 0; i < Girders.Length; i++)
            DrawGirder(g, Girders[i], 1f);
        DrawGirder(g, new Girder(PlatformX0, PlatformX1, PlatformY, PlatformY), 1f);

        // Ladders.
        foreach (var l in Ladders)
            DrawLadder(g, l.X, LadderTop(l), LadderBottom(l), 1f);

        // Burning drum.
        DrawDrum(g, DrumX, Girders[5].YAt(DrumX), 1f, Time);

        // Items.
        foreach (var it in _items)
        {
            if (it.Taken)
                continue;
            float iy = Girders[it.G].YAt(it.X) - 9 + MathF.Sin(Time * 3 + it.X) * 1.5f;
            g.Glow(it.X, iy, 16, Pal.Pink, 0.35f + 0.15f * MathF.Sin(Time * 5));
            g.PixelsCentered(ItemArt[it.Kind], it.X, iy, 1.6f);
        }

        // Captive and brute.
        float cy = PlatformY - 11;
        g.Glow(250, cy, 26, Pal.Pink, 0.3f);
        g.PixelsCentered(Captive, 250, cy, 1.5f, _rescued <= 0 && (int)(Time * 1.5f) % 2 == 0);
        if (_rescued > 0)
        {
            DrawHeart(g, (250 + _px) / 2, PlatformY - 38 + MathF.Sin(Time * 6) * 2, 7, Pal.Pink);
        }
        else if ((int)(Time * 2) % 3 != 0)
        {
            g.TextShadow("HELP!", 270, cy - 22, 1.5f, Pal.White);
        }

        float by = Girders[0].YAt(BruteX);
        bool up = _bruteAnim > 0;
        g.Glow(BruteX, by - 15, 34, Pal.Orange, 0.18f);
        g.PixelsCentered(up ? BruteUp : BruteIdle, BruteX, by - 15, 2f);
        if (up)
            DrawBarrel(g, BruteX, by - 38, BarrelR, 0, false);
        else
            for (int i = 0; i < 3; i++)
                DrawBarrel(g, BruteX - 30 + (i % 2) * 2, by - BarrelR - i * 11.5f, BarrelR, MathF.PI / 2, true);

        // Barrels.
        foreach (var b in _barrels)
            DrawBarrel(g, b.X, b.Y, BarrelR, b.Rot, b.State == BarrelState.OnLadder);

        // Hero.
        DrawHero(g);

        // Bonus box.
        var box = new RectF(538, 28, 94, 30);
        g.Panel(box, Pal.Panel * 0.9f, _bonus <= 1000 && (Tick / 15) % 2 == 0 ? Pal.Red : Pal.Cyan, 6);
        g.Text("BONUS", box.CenterX, box.Y + 4, 1f, Pal.Cyan, Align.Center);
        g.Text(_bonus.ToString(), box.CenterX, box.Y + 14, 1.5f, _bonus <= 1000 ? Pal.Red : Pal.White, Align.Center);

        if (_banner > 0)
        {
            float a = MathF.Min(1, _banner * 1.5f);
            g.TextShadow("LEVEL " + Level, 320, 206, 3f, Pal.Yellow * a, Align.Center);
            g.TextShadow("CLIMB TO THE TOP!", 320, 236, 1.5f, Pal.White * a, Align.Center);
        }
        if (_rescued > 0)
            g.TextShadow("RESCUED!", 320, 206, 3f, Pal.Pink, Align.Center);
    }

    private void DrawHero(Gfx g)
    {
        if (_dying > 0)
        {
            // Spin, then lie flat.
            float t = 1.6f - _dying;
            bool flip = (int)(t * 10) % 2 == 0;
            if (t < 0.9f)
                g.Pixels(HeroWalk[0], _px - 6, _py - 21, 1.5f, flip);
            else
                g.Pixels(HeroJump, _px - 6, _py - 10, 1.5f, false, Pal.Lerp(Pal.White, Pal.Red, 0.5f));
            g.Glow(_px, _py - 10, 20, Pal.White, 0.4f * _dying);
            return;
        }
        PixelArt art;
        bool flipX = _faceLeft;
        if (_ladder >= 0)
        {
            art = HeroClimb;
            flipX = (int)_walkAnim % 2 == 0;
        }
        else if (_air)
            art = HeroJump;
        else
            art = HeroWalk[(int)_walkAnim % 2];
        g.Glow(_px, _py - 10, 18, Pal.Yellow, 0.18f);
        g.Pixels(art, _px - 6, _py - art.Height * 1.5f, 1.5f, flipX);
    }

    private static void DrawBackdrop(Gfx g, RectF r, float time)
    {
        g.GradientV(r.X, r.Y, r.W, r.H, new Color(12, 6, 34), new Color(44, 14, 46));
        Backdrops.Stars(g, time, 0, 21, new RectF(r.X, r.Y, r.W, r.H * 0.7f), (int)(60 * r.W / 640));
        float s = r.W / 640f;
        g.Glow(r.X + 470 * s, r.Y + 70 * s, 90 * s, Pal.Sky, 0.22f);
        g.Circle(r.X + 470 * s, r.Y + 70 * s, 24 * s, new Color(210, 220, 240));
        g.Circle(r.X + 462 * s, r.Y + 64 * s, 5 * s, new Color(180, 190, 215));
        g.Circle(r.X + 478 * s, r.Y + 78 * s, 4 * s, new Color(180, 190, 215));
        // City skyline.
        var rng = new Random(5);
        float x = r.X;
        while (x < r.Right)
        {
            float w = (16 + rng.Next(30)) * s, h = (40 + rng.Next(120)) * s;
            var col = new Color(24, 12, 40);
            g.Rect(x, r.Bottom - h, w, h, col);
            for (float wy = r.Bottom - h + 6 * s; wy < r.Bottom - 4 * s; wy += 9 * s)
                for (float wx = x + 3 * s; wx < x + w - 4 * s; wx += 7 * s)
                    if (rng.Next(5) == 0)
                        g.Rect(wx, wy, 3 * s, 4 * s, new Color(90, 70, 40));
            x += w + 2 * s;
        }
    }

    private static void DrawGirder(Gfx g, Girder gd, float s)
    {
        float th = 7 * s;
        var main = new Color(190, 30, 70);
        var dark = new Color(110, 10, 40);
        var hi = new Color(255, 120, 150);
        var a = new Vector2(gd.X0, gd.Y0 + th / 2);
        var b = new Vector2(gd.X1, gd.Y1 + th / 2);
        g.Line(a + new Vector2(0, 2 * s), b + new Vector2(0, 2 * s), th, Color.Black * 0.35f);
        g.Line(a, b, th, dark);
        g.Line(new Vector2(gd.X0, gd.Y0 + 1 * s), new Vector2(gd.X1, gd.Y1 + 1 * s), 2 * s, main);
        g.Line(new Vector2(gd.X0, gd.Y0 + th - 1 * s), new Vector2(gd.X1, gd.Y1 + th - 1 * s), 2 * s, main);
        g.Line(new Vector2(gd.X0, gd.Y0 + 0.4f * s), new Vector2(gd.X1, gd.Y1 + 0.4f * s), 0.8f * s, hi);
        float step = 10 * s;
        for (float x = gd.X0; x + step <= gd.X1 + 0.1f; x += step)
        {
            float y0 = gd.YAt(x), y1 = gd.YAt(x + step / 2), y2 = gd.YAt(x + step);
            g.Line(x, y0 + 1.5f * s, x + step / 2, y1 + th - 1.5f * s, 1.2f * s, main);
            g.Line(x + step / 2, y1 + th - 1.5f * s, x + step, y2 + 1.5f * s, 1.2f * s, main);
        }
    }

    private static void DrawLadder(Gfx g, float x, float top, float bottom, float s)
    {
        var rail = new Color(60, 200, 230);
        float hw = 5 * s;
        g.Rect(x - hw, top, 1.6f * s, bottom - top, rail);
        g.Rect(x + hw - 1.6f * s, top, 1.6f * s, bottom - top, rail);
        for (float y = top + 4 * s; y < bottom - 1; y += 5 * s)
            g.Rect(x - hw, y, hw * 2, 1.2f * s, rail * 0.8f);
    }

    private static void DrawBarrel(Gfx g, float x, float y, float r, float rot, bool endOn)
    {
        var wood = new Color(190, 110, 45);
        var band = new Color(90, 50, 20);
        if (endOn)
        {
            g.Ellipse(x, y, r * 1.35f, r * 0.85f, band);
            g.Ellipse(x, y, r * 1.2f, r * 0.7f, wood);
            g.Rect(x - r * 0.9f, y - r * 0.7f, 1.3f, r * 1.4f, band);
            g.Rect(x + r * 0.9f - 1.3f, y - r * 0.7f, 1.3f, r * 1.4f, band);
            return;
        }
        g.Circle(x, y + 1, r, Color.Black * 0.3f);
        g.Circle(x, y, r, band);
        g.Circle(x, y, r * 0.82f, wood);
        var d = MathF2.FromAngle(rot, r * 0.8f);
        var p = new Vector2(x, y);
        g.Line(p - d, p + d, 1.4f, band);
        var n = new Vector2(-d.Y, d.X) * 0.45f;
        g.Line(p - d * 0.7f + n, p + d * 0.7f + n, 1f, band * 0.7f);
        g.Line(p - d * 0.7f - n, p + d * 0.7f - n, 1f, band * 0.7f);
        g.Circle(x - r * 0.3f, y - r * 0.35f, r * 0.22f, Pal.Lighten(wood, 0.35f));
    }

    private static void DrawDrum(Gfx g, float x, float footY, float s, float time)
    {
        float w = 22 * s, h = 22 * s;
        float flick = 0.7f + 0.3f * MathF.Sin(time * 17) * MathF.Sin(time * 7.3f);
        g.Glow(x, footY - h - 4 * s, 34 * s, Pal.Orange, 0.6f * flick);
        for (int i = 0; i < 4; i++)
        {
            float fx = x - 7 * s + i * 4.5f * s;
            float fh = (7 + 5 * MathF.Abs(MathF.Sin(time * (9 + i * 2.1f) + i))) * s;
            g.Triangle(new Vector2(fx - 3 * s, footY - h), new Vector2(fx + 3 * s, footY - h), new Vector2(fx, footY - h - fh), i % 2 == 0 ? Pal.Orange : Pal.Yellow);
        }
        g.Rect(x - w / 2, footY - h, w, h, new Color(30, 60, 160));
        g.Rect(x - w / 2, footY - h, w, 3 * s, new Color(90, 140, 255));
        g.Rect(x - w / 2, footY - 4 * s, w, 2 * s, new Color(90, 140, 255));
        g.Text("OIL", x, footY - h + 7 * s, 1f * s, Pal.White, Align.Center);
    }

    private static void DrawHeart(Gfx g, float x, float y, float r, Color c)
    {
        g.Glow(x, y, r * 3, c, 0.5f);
        g.Circle(x - r * 0.5f, y, r * 0.6f, c);
        g.Circle(x + r * 0.5f, y, r * 0.6f, c);
        g.Triangle(new Vector2(x - r * 1.08f, y + r * 0.2f), new Vector2(x + r * 1.08f, y + r * 0.2f), new Vector2(x, y + r * 1.2f), c);
    }

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        float s = r.H / 70f;
        DrawBackdrop(g, r, time);
        float cx = r.CenterX;
        var top = new Girder(cx - 70 * s, cx + 60 * s, r.Y + 24 * s, r.Y + 30 * s);
        var bot = new Girder(cx - 60 * s, cx + 70 * s, r.Y + 64 * s, r.Y + 58 * s);
        DrawLadder(g, cx + 20 * s, top.YAt(cx + 20 * s), bot.YAt(cx + 20 * s), s);
        DrawGirder(g, top, s);
        DrawGirder(g, bot, s);
        // Brute on the top girder.
        bool up = (time % 1.6f) < 0.4f;
        g.PixelsCentered(up ? BruteUp : BruteIdle, cx - 44 * s, top.YAt(cx - 44 * s) - 8.5f * s, 1.1f * s);
        // Barrels rolling.
        float t = (time * 50 * s) % (130 * s);
        float bx = cx - 28 * s + t;
        if (bx < top.X1)
            DrawBarrel(g, bx, top.YAt(bx) - 4.5f * s, 4.5f * s, t / (4.5f * s), false);
        float bx2 = cx + 60 * s - ((time * 50 * s + 60 * s) % (130 * s));
        DrawBarrel(g, bx2, bot.YAt(bx2) - 4.5f * s, 4.5f * s, -bx2 / (4.5f * s), false);
        // Hero jumping over the lower barrel.
        float hx = cx - 20 * s;
        float d = MathF.Abs(bx2 - hx);
        float jump = d < 18 * s ? MathF.Cos(d / (18 * s) * MathF.PI / 2) * 10 * s : 0;
        var art = jump > 0 ? HeroJump : HeroWalk[(int)(time * 6) % 2];
        g.Glow(hx, bot.YAt(hx) - 8 * s - jump, 12 * s, Pal.Yellow, 0.3f);
        g.Pixels(art, hx - 4.8f * s, bot.YAt(hx) - art.Height * 1.2f * s - jump, 1.2f * s, bx2 < hx);
        g.PixelsCentered(Captive, cx + 46 * s, top.YAt(cx + 46 * s) - 9 * s, 1.2f * s);
        DrawHeart(g, cx + 58 * s, top.YAt(cx + 46 * s) - 22 * s + MathF.Sin(time * 4) * 2 * s, 3f * s, Pal.Pink);
    }

    // ------------------------------------------------------------------ autoplay

    public override void AutoPlay(Controls c)
    {
        if (_dying > 0 || _rescued > 0)
            return;
        if (_ladder >= 0)
        {
            var l = Ladders[_ladder];
            float top = LadderTop(l), bottom = LadderBottom(l);
            float dir = -1;
            foreach (var b in _barrels)
            {
                // A barrel tumbling down this ladder: retreat.
                if (b.State == BarrelState.OnLadder && MathF.Abs(b.X - l.X) < 2 && b.Y < _py && _py - b.Y < 70)
                    dir = 1;
                // A barrel rolling along the girder above: wait below it.
                if (l.Upper >= 0 && b.State == BarrelState.Rolling && b.G == l.Upper)
                {
                    float bdx = b.X - l.X;
                    bool coming = MathF.Sign(bdx) == -b.Dir || MathF.Abs(bdx) < 12;
                    if (coming && MathF.Abs(bdx) < 70 && _py - top < 30)
                        dir = _py - top < 18 ? 1 : 0;
                }
            }
            c.SetDirections(0, dir);
            return;
        }
        if (_air)
        {
            c.SetDirections(MathF.Sign(_jumpVx), 0);
            return;
        }

        // Head for any bonus item on this girder, then the nearest ladder up.
        float target = _px;
        float best = float.MaxValue;
        foreach (var l in Ladders)
            if (l.Lower == _pg && MathF.Abs(l.X - _px) < best)
            {
                best = MathF.Abs(l.X - _px);
                target = l.X;
            }
        bool goingForItem = false;
        foreach (var it in _items)
            if (!it.Taken && it.G == _pg)
            {
                target = it.X;
                goingForItem = true;
            }

        float dx = target - _px;
        bool jump = false, clearToClimb = true;
        float flee = 0, jumpDir = 0;
        foreach (var b in _barrels)
        {
            float bdx = b.X - _px;
            if (b.State == BarrelState.Rolling && b.G == _pg)
            {
                bool coming = MathF.Sign(bdx) == -b.Dir || MathF.Abs(bdx) < 6;
                // Jump towards the barrel: the hero clears it with plenty to spare.
                float trigger = 40 + (BarrelSpeed - 78) * 0.2f;
                if (coming && MathF.Abs(bdx) < trigger && MathF.Abs(bdx) > 12)
                {
                    jump = true;
                    jumpDir = MathF.Sign(bdx);
                }
                if (coming && MathF.Abs(bdx) < 90)
                    clearToClimb = false;
            }
            // Keep out from under a ladder a barrel is coming down.
            if (b.State == BarrelState.OnLadder && b.G + 1 == _pg && MathF.Abs(bdx) < 30)
                flee = bdx > 0 ? -1 : 1;
            if (b.State == BarrelState.Falling && b.G == _pg && MathF.Abs(bdx) < 30)
                clearToClimb = false;
        }
        if (jump)
        {
            c.FirePressed = true;
            c.Fire = true;
            c.SetDirections(jumpDir, 0);
            return;
        }
        if (flee != 0)
        {
            c.SetDirections(flee, 0);
            return;
        }
        if (MathF.Abs(dx) < 2.5f && !goingForItem)
            c.SetDirections(0, clearToClimb ? -1 : 0);
        else if (MathF.Abs(dx) >= 2.5f)
            c.SetDirections(MathF.Sign(dx), 0);
    }
}
