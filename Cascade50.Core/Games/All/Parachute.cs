using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 29 Parachute: a Paratrooper-style flak gun. Helicopters and jets cross the dusk sky dropping
/// paratroopers; shoot them (or their parachutes) before four land on one side and storm the gun.
/// </summary>
public sealed class Parachute : MiniGame, Cascade50.Core.Capture.ICaptureHints
{
    public int CaptureTicks => 540;
    public override int Number => 29;
    public override string Title => "Parachute";
    public override Category Category => Category.Shooter;
    public override string Tagline => "Hold the base: shoot down the paratroopers before they land.";
    public override Color Accent => Pal.Orange;
    public override Pad Pad => Pad.Horizontal | Pad.Fire;

    public override string[] HowToPlay =>
    [
        "Shoot the helicopters and the paratroopers they drop.",
        "If four land on one side, they storm the gun! Hit a parachute to drop its trooper onto others.",
        "Later, shoot the bombers' bombs.",
    ];

    public override string[] DesktopControls => ["LEFT/RIGHT to aim, SPACE to fire.", "Or hold the mouse to aim and fire."];
    public override string[] TouchControls => ["Stick to aim, FIRE to shoot.", "Or hold a finger on the sky."];

    private const float GroundY = 334, GunX = 320, GunY = 318;
    private const float MinA = -MathF.PI + 0.12f, MaxA = -0.12f;
    private const float ShellSpeed = 430;

    private static readonly Dictionary<char, Color> Cols = new()
    {
        ['g'] = new Color(70, 100, 60), ['G'] = new Color(110, 150, 80), ['k'] = new Color(30, 30, 36), ['w'] = Pal.White,
        ['s'] = new Color(150, 200, 230), ['r'] = Pal.Red, ['y'] = Pal.Yellow, ['d'] = new Color(80, 86, 100), ['D'] = new Color(130, 140, 155),
        ['b'] = new Color(60, 70, 120), ['B'] = new Color(100, 110, 170), ['o'] = Pal.Orange, ['p'] = new Color(240, 190, 150),
    };

    private static readonly PixelArt Heli = new(
    [
        "........kk........",
        "..gg...gGGGG......",
        "..gGg.gGGGsssg....",
        "...gGGGGGGssssg...",
        "....gGGGGGGGGGGg..",
        "......gggggggg....",
        ".......k....k.....",
        ".....kkkkkkkkkk...",
    ], Cols);

    private static readonly PixelArt Jet = new(
    [
        "dd..............",
        "dDd.............",
        "dDDd.....ss.....",
        "dDDDDDDDDDssDD..",
        ".DDDDDDDDDDDDDDy",
        "....dDDDd.......",
        "......dd........",
    ], Cols);

    private static readonly PixelArt Bomber = new(
    [
        "bb......BB..........",
        "bBb.....bB..........",
        "bBBBBBBBBBBBBBBBss..",
        ".bBBBBBBBBBBBBBBBBBb",
        "..bbbbbbbbBBbbbbbb..",
        "........bBBb........",
        ".........bb.........",
    ], Cols);

    private static readonly PixelArt Trooper = new(
    [
        ".kk.",
        ".pp.",
        "gGGg",
        "gGGg",
        ".gg.",
        ".kk.",
        "k..k",
    ], Cols);

    private enum AKind { Heli, Jet, Bomber }

    private sealed class Aircraft
    {
        public AKind Kind;
        public float X, Y, Vx;
        public int Drops;
        public float NextDrop;
        public bool BombDropped;
    }

    private enum TState { Falling, Chute, Landed, Plummet, Assault }

    private sealed class Trooper_
    {
        public TState State;
        public float X, Y, Vy, T;
        public int Side;     // -1 left, 1 right
        public int Slot;
        public float SwayPhase;
        public Color Chute;
    }

    private struct Shell
    {
        public Vector2 Pos, Vel;
    }

    private struct Bomb
    {
        public Vector2 Pos, Vel;
    }

    private readonly List<Aircraft> _air = new();
    private readonly List<Trooper_> _troops = new();
    private readonly List<Shell> _shells = new();
    private readonly List<Bomb> _bombs = new();
    private float _angle = -MathF.PI / 2, _angVel;
    private float _cool;
    private float _spawn;
    private float _levelTimer;
    private float _banner;
    private string _bannerText = "";
    private float _assault;
    private int _assaultSide;
    private float _recoil;
    private float _bombedT;

    private static readonly Color[] ChuteCols = [Pal.White, new(240, 220, 120), new(250, 160, 160), new(170, 220, 250)];

    protected override void Start()
    {
        Level = 1;
        _spawn = 0.2f;
        _levelTimer = 30;
        _bannerText = "WAVE 1";
        _banner = 2;
    }

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        if (_banner > 0)
            _banner -= Dt;
        if (_recoil > 0)
            _recoil -= Dt * 6;

        if (_bombedT > 0)
        {
            _bombedT -= Dt;
            if (_bombedT <= 0)
                EndGame(false, "A bomb hit the gun!");
            return;
        }

        if (_assault > 0)
        {
            UpdateAssault();
            return;
        }

        UpdateGun();
        UpdateLevel();
        UpdateAircraft();
        UpdateShells();
        UpdateTroopers();
        UpdateBombs();
    }

    private void UpdateGun()
    {
        float input = In.Left ? -1 : In.Right ? 1 : MathF.Abs(In.AxisX) > 0.2f ? In.AxisX : 0;
        bool pointerAim = In.PointerDown && In.Pointer.Y < GunY - 10;
        if (pointerAim)
        {
            float target = MathF.Atan2(In.Pointer.Y - GunY, In.Pointer.X - GunX);
            float d = MathF2.WrapAngle(target - _angle);
            input = MathF2.Clamp(d * 6, -1, 1);
        }
        _angVel = MathF2.Approach(_angVel, input * 2.6f, Dt * 20);
        _angle = MathF2.Clamp(_angle + _angVel * Dt, MinA, MaxA);

        _cool -= Dt;
        if ((In.Fire || In.FirePressed || pointerAim) && _cool <= 0 && _shells.Count < 12)
        {
            _cool = 0.14f;
            var dir = MathF2.FromAngle(_angle);
            var muzzle = new Vector2(GunX, GunY) + dir * 24;
            _shells.Add(new Shell { Pos = muzzle, Vel = dir * ShellSpeed });
            _recoil = 1;
            Sound.Play(Sfx.Cannon, Rand(0.2f, 0.4f), 0.35f);
            Fx.Spark(muzzle.X, muzzle.Y, dir.X * 60, dir.Y * 60, Pal.Yellow, 0.12f, 4);
            Fx.Spark(muzzle.X, muzzle.Y, dir.X * 20 + Rand(-10, 10), dir.Y * 20 - 10, Pal.LightGrey * 0.4f, 0.5f, 3, -20, false);
        }
    }

    private void UpdateLevel()
    {
        _levelTimer -= Dt;
        if (_levelTimer <= 0)
        {
            Level++;
            _levelTimer = 30;
            _bannerText = "WAVE " + Level + (Level == 3 ? " - BOMBERS!" : "");
            _banner = 2;
            Sound.Play(Sfx.LevelUp, 0, 0.6f);
        }
        _spawn -= Dt;
        if (_spawn <= 0)
        {
            _spawn = MathF.Max(0.7f, 1.9f - Level * 0.16f) * Rand(0.7f, 1.3f);
            float dir = Chance(0.5f) ? 1 : -1;
            var kind = AKind.Heli;
            float roll = Rand(0, 1);
            if (Level >= 3 && roll < 0.18f + Level * 0.02f)
                kind = AKind.Bomber;
            else if (Level >= 2 && roll < 0.45f)
                kind = AKind.Jet;
            float y = kind switch
            {
                AKind.Bomber => 50,
                AKind.Jet => Rand(42, 62),
                _ => Pick(70f, 98f, 126f),
            };
            float speed = kind switch
            {
                AKind.Jet => 170 + Level * 8,
                AKind.Bomber => 80 + Level * 4,
                _ => Rand(55, 85) + Level * 6,
            };
            var a = new Aircraft
            {
                Kind = kind, X = dir > 0 ? -30 : 670, Y = y, Vx = dir * speed,
                Drops = kind == AKind.Heli ? RandInt(2, 4) + (Level >= 4 ? 1 : 0) : kind == AKind.Jet ? (Chance(0.5f) ? 1 : 0) : 0,
            };
            a.NextDrop = dir > 0 ? Rand(50, 260) : Rand(380, 590);
            _air.Add(a);
            if (kind == AKind.Jet)
                Sound.Play(Sfx.Whoosh, -0.3f, 0.5f);
        }
    }

    private void UpdateAircraft()
    {
        bool rotor = false;
        for (int i = _air.Count - 1; i >= 0; i--)
        {
            var a = _air[i];
            a.X += a.Vx * Dt;
            if (a.Kind == AKind.Heli)
                rotor = true;
            // Drop paratroopers away from the gun.
            if (a.Drops > 0 && MathF.Abs(a.X - a.NextDrop) < MathF.Abs(a.Vx) * Dt * 1.5f && MathF.Abs(a.X - GunX) > 34 && a.X > 16 && a.X < 624)
            {
                a.Drops--;
                a.NextDrop = a.X + MathF.Sign(a.Vx) * Rand(60, 200);
                _troops.Add(new Trooper_
                {
                    State = TState.Falling, X = a.X, Y = a.Y + 8, Vy = 20, T = Rand(0.5f, 1.0f),
                    SwayPhase = Rand(0, 6), Chute = ChuteCols[RandInt(0, ChuteCols.Length)],
                });
                Sound.Play(Sfx.Pop, 0.4f, 0.3f);
            }
            // Bombers drop on the gun.
            if (a.Kind == AKind.Bomber && !a.BombDropped)
            {
                float fall = MathF.Sqrt(2 * (GunY - a.Y) / 160f);
                float landX = a.X + a.Vx * fall;
                if (MathF.Abs(landX - GunX) < 6)
                {
                    a.BombDropped = true;
                    _bombs.Add(new Bomb { Pos = new Vector2(a.X, a.Y + 8), Vel = new Vector2(a.Vx, 0) });
                    Sound.Play(Sfx.Whoosh, 0.5f, 0.5f);
                }
            }
            if (a.X < -60 || a.X > 700)
                _air.RemoveAt(i);
        }
        if (rotor)
            Sound.Loop(LoopSfx.Hum, true, -0.4f, 0.25f);
    }

    private void UpdateShells()
    {
        for (int i = _shells.Count - 1; i >= 0; i--)
        {
            var s = _shells[i];
            s.Pos += s.Vel * Dt;
            _shells[i] = s;
            bool remove = s.Pos.X < -10 || s.Pos.X > 650 || s.Pos.Y < Screen.HudHeight;
            for (int j = _air.Count - 1; j >= 0 && !remove; j--)
            {
                var a = _air[j];
                float hw = a.Kind == AKind.Bomber ? 23 : a.Kind == AKind.Jet ? 18 : 20, hh = 10;
                if (MathF.Abs(s.Pos.X - a.X) < hw && MathF.Abs(s.Pos.Y - a.Y) < hh)
                {
                    int pts = a.Kind switch { AKind.Jet => 250, AKind.Bomber => 300, _ => 100 };
                    AddScore(pts, a.X, a.Y - 12, a.Kind == AKind.Heli ? Pal.Yellow : Pal.Orange);
                    Fx.Explode(a.X, a.Y, 1.1f);
                    Fx.Burst(a.X, a.Y, Pal.DarkGrey, 14, 90, 0.9f, 3, 160, false);
                    Sound.Play(Sfx.Explode, Rand(-0.3f, 0.1f));
                    _air.RemoveAt(j);
                    remove = true;
                }
            }
            for (int j = _troops.Count - 1; j >= 0 && !remove; j--)
            {
                var t = _troops[j];
                if (t.State is TState.Landed or TState.Assault)
                    continue;
                // The canopy?
                if (t.State == TState.Chute)
                {
                    float sx = t.X + Sway(t);
                    float dx = (s.Pos.X - sx) / 15, dy = (s.Pos.Y - (t.Y - 22)) / 10;
                    if (dx * dx + dy * dy < 1)
                    {
                        t.State = TState.Plummet;
                        t.Vy = 40;
                        AddScore(75, sx, t.Y - 30, Pal.Pink);
                        Fx.Burst(sx, t.Y - 22, t.Chute, 12, 60, 0.6f, 2f, 60, false);
                        Sound.Play(Sfx.Pop, -0.2f, 0.6f);
                        remove = true;
                        continue;
                    }
                }
                if (MathF.Abs(s.Pos.X - (t.X + Sway(t))) < 6 && MathF.Abs(s.Pos.Y - t.Y) < 9)
                {
                    KillTrooper(j, 50);
                    remove = true;
                }
            }
            for (int j = _bombs.Count - 1; j >= 0 && !remove; j--)
                if (Vector2.DistanceSquared(_bombs[j].Pos, s.Pos) < 64)
                {
                    AddScore(150, _bombs[j].Pos.X, _bombs[j].Pos.Y - 10, Pal.Cyan);
                    Fx.Explode(_bombs[j].Pos.X, _bombs[j].Pos.Y, 0.8f);
                    Sound.Play(Sfx.Explode, 0.3f, 0.7f);
                    _bombs.RemoveAt(j);
                    remove = true;
                }
            if (remove)
                _shells.RemoveAt(i);
        }
    }

    private static float Sway(Trooper_ t) => t.State == TState.Chute ? MathF.Sin(t.SwayPhase) * 3 : 0;

    private void KillTrooper(int j, int pts)
    {
        var t = _troops[j];
        float x = t.X + Sway(t);
        AddScore(pts, x, t.Y - 12);
        Fx.Burst(x, t.Y, Pal.Red, 10, 70, 0.5f, 2f, 200, false);
        Fx.Burst(x, t.Y, new Color(110, 150, 80), 6, 60, 0.4f, 1.8f, 200, false);
        Sound.Play(Sfx.Hit, Rand(0, 0.4f), 0.6f);
        if (t.State == TState.Chute)
            Fx.Burst(x, t.Y - 20, t.Chute, 8, 30, 1f, 2.5f, -10, false);
        _troops.RemoveAt(j);
    }

    private void UpdateTroopers()
    {
        for (int i = _troops.Count - 1; i >= 0; i--)
        {
            var t = _troops[i];
            switch (t.State)
            {
                case TState.Falling:
                    t.Vy = MathF.Min(t.Vy + 200 * Dt, 140);
                    t.Y += t.Vy * Dt;
                    t.T -= Dt;
                    if (t.T <= 0)
                    {
                        t.State = TState.Chute;
                        Sound.Play(Sfx.Whoosh, 0.6f, 0.2f);
                    }
                    break;
                case TState.Chute:
                    t.Vy = MathF2.Approach(t.Vy, 30 + Level * 2, 220 * Dt);
                    t.Y += t.Vy * Dt;
                    t.SwayPhase += Dt * 2.2f;
                    break;
                case TState.Plummet:
                    t.Vy += 400 * Dt;
                    t.Y += t.Vy * Dt;
                    break;
            }
            if (t.State is TState.Falling or TState.Chute or TState.Plummet && t.Y >= GroundY - 7)
            {
                if (t.State == TState.Plummet || t.State == TState.Falling)
                {
                    // Splat, taking out anyone underneath.
                    int squashed = 0;
                    for (int j = _troops.Count - 1; j >= 0; j--)
                        if (j != i && _troops[j].State == TState.Landed && MathF.Abs(_troops[j].X - t.X) < 9)
                        {
                            squashed++;
                            Fx.Burst(_troops[j].X, GroundY - 6, Pal.Red, 8, 60, 0.4f, 2, 200, false);
                            _troops.RemoveAt(j);
                            if (j < i)
                                i--;
                        }
                    if (squashed > 0)
                    {
                        AddScore(100 * squashed, t.X, GroundY - 30, Pal.Lime);
                        Sound.Play(Sfx.Bonus, 0, 0.5f);
                    }
                    KillTrooper(i, 25);
                    Sound.Play(Sfx.Thud, -0.4f, 0.7f);
                    continue;
                }
                t.State = TState.Landed;
                t.X += Sway(t);
                t.Y = GroundY - 7;
                t.Side = t.X < GunX ? -1 : 1;
                Sound.Play(Sfx.Land, -0.2f, 0.6f);
                Fx.Burst(t.X, GroundY - 2, t.Chute, 8, 40, 0.6f, 2, 80, false);
                int count = 0;
                foreach (var o in _troops)
                    if (o.State == TState.Landed && o.Side == t.Side)
                        count++;
                if (count >= 4)
                    BeginAssault(t.Side);
                else if (count == 3)
                    Sound.Play(Sfx.Alarm, 0, 0.5f);
            }
        }
    }

    private void UpdateBombs()
    {
        for (int i = _bombs.Count - 1; i >= 0; i--)
        {
            var b = _bombs[i];
            b.Vel.Y += 160 * Dt;
            b.Pos += b.Vel * Dt;
            _bombs[i] = b;
            if (MathF.Abs(b.Pos.X - GunX) < 22 && b.Pos.Y > GunY - 14)
            {
                Fx.Explode(GunX, GunY, 2.5f);
                Fx.Burst(GunX, GunY, Pal.White, 30, 200, 0.8f, 3);
                Sound.Play(Sfx.BigExplode);
                _bombs.Clear();
                _bombedT = 1.2f;
                return;
            }
            if (b.Pos.Y > GroundY)
            {
                Fx.Explode(b.Pos.X, GroundY, 0.8f);
                Sound.Play(Sfx.Explode, -0.3f, 0.6f);
                _bombs.RemoveAt(i);
            }
        }
    }

    private void BeginAssault(int side)
    {
        _assault = 3.2f;
        _assaultSide = side;
        int slot = 0;
        foreach (var t in _troops)
            if (t.State == TState.Landed && t.Side == side)
            {
                t.State = TState.Assault;
                t.Slot = slot++;
            }
        _bannerText = "THEY'RE STORMING THE GUN!";
        _banner = 3;
        Sound.Play(Sfx.Alarm);
    }

    private void UpdateAssault()
    {
        _assault -= Dt;
        foreach (var t in _troops)
        {
            if (t.State != TState.Assault)
                continue;
            // Form a human pyramid against the bunker, then climb onto it.
            var target = PyramidSlot(t.Slot, _assaultSide);
            t.X = MathF2.Approach(t.X, target.X, 70 * Dt);
            if (MathF.Abs(t.X - target.X) < 1)
                t.Y = MathF2.Approach(t.Y, target.Y, 30 * Dt);
            if (Tick % 10 == 0)
                Sound.Play(Sfx.Step, 0.3f, 0.3f);
        }
        if (_assault <= 0)
        {
            Fx.Explode(GunX, GunY, 2.5f);
            Sound.Play(Sfx.BigExplode);
            EndGame(false, "The paratroopers stormed the gun!");
        }
    }

    private static Vector2 PyramidSlot(int slot, int side)
    {
        // Three on the ground, then one on top, then one on the bunker.
        float baseX = GunX + side * 30;
        return slot switch
        {
            0 => new Vector2(baseX + side * 18, GroundY - 7),
            1 => new Vector2(baseX + side * 9, GroundY - 7),
            2 => new Vector2(baseX, GroundY - 7),
            3 => new Vector2(baseX + side * 4.5f, GroundY - 21),
            _ => new Vector2(GunX + side * 12, GunY - 18),
        };
    }

    // ------------------------------------------------------------------ drawing

    public override void Draw(Gfx g)
    {
        DrawSky(g, Screen.Bounds, Time);

        // Aircraft.
        foreach (var a in _air)
            DrawAircraft(g, a.Kind, a.X, a.Y, a.Vx < 0, Time, 2.3f);

        // Bombs.
        foreach (var b in _bombs)
        {
            g.Glow(b.Pos, 10, Pal.Red, 0.5f + 0.3f * MathF.Sin(Time * 20));
            g.Ellipse(b.Pos.X, b.Pos.Y, 3, 4.5f, new Color(40, 40, 46));
            g.Rect(b.Pos.X - 3, b.Pos.Y - 7, 6, 2, Pal.Red);
        }

        // Troopers.
        foreach (var t in _troops)
            DrawTrooper(g, t);

        // Shells.
        foreach (var s in _shells)
        {
            g.Glow(s.Pos, 7, Pal.Yellow, 0.7f);
            g.Line(s.Pos, s.Pos - Vector2.Normalize(s.Vel) * 6, 2, Pal.White);
        }

        DrawGun(g, _angle, _recoil, 1);

        // Landing warnings: how close each side is to an assault.
        int left = 0, right = 0;
        foreach (var t in _troops)
            if (t.State == TState.Landed)
            {
                if (t.Side < 0) left++;
                else right++;
            }
        DrawMeter(g, 20, left, Align.Left);
        DrawMeter(g, 620, right, Align.Right);

        if (_banner > 0)
        {
            float a = MathF.Min(1, _banner);
            g.TextShadow(_bannerText, 320, 150, 2.5f, Pal.Yellow * a, Align.Center);
        }
    }

    private void DrawMeter(Gfx g, float x, int count, Align align)
    {
        float dir = align == Align.Left ? 1 : -1;
        for (int i = 0; i < 4; i++)
        {
            float cx = x + dir * i * 11;
            bool on = i < count;
            var col = on ? (count >= 3 ? Pal.Red : Pal.Orange) : Color.Black * 0.35f;
            g.RoundRect(cx - 4, 342, 8, 12, 2, col);
            if (on && count >= 3)
                g.Glow(cx, 348, 10, Pal.Red, 0.3f + 0.3f * MathF2.Pulse(Time, 0.5f));
        }
    }

    private static void DrawSky(Gfx g, RectF r, float time)
    {
        float k = r.H / 360f;
        g.GradientV(r.X, r.Y, r.W, r.H * 0.6f, new Color(30, 20, 70), new Color(170, 70, 90));
        g.GradientV(r.X, r.Y + r.H * 0.6f, r.W, r.H * 0.4f, new Color(170, 70, 90), new Color(250, 160, 80));
        g.Glow(r.X + r.W * 0.75f, r.Y + r.H * 0.86f, r.W * 0.25f, Pal.Orange, 0.45f);
        g.Circle(r.X + r.W * 0.75f, r.Y + r.H * 0.86f, 28 * k, new Color(255, 210, 130));
        // A few stars high up.
        for (int i = 0; i < 30; i++)
        {
            float sx = r.X + Backdrops.Mod(i * 97.3f, r.W), sy = r.Y + Backdrops.Mod(i * 41.7f, r.H * 0.35f);
            g.Circle(sx, sy, 0.6f * k + 0.3f, Pal.White * (0.3f + 0.3f * MathF.Sin(time * 2 + i)));
        }
        Backdrops.Hills(g, r.Y + r.H * 0.86f, 30 * k, 0, new Color(90, 40, 70), 4, r);
        Backdrops.Hills(g, r.Y + r.H * 0.9f, 18 * k, 200, new Color(60, 28, 52), 8, r);
        // Desert floor.
        float gy = r.Y + GroundY / 360f * r.H;
        g.GradientV(r.X, gy, r.W, r.Bottom - gy, new Color(190, 140, 80), new Color(120, 80, 50));
        g.Rect(r.X, gy, r.W, 1.5f * k, new Color(230, 190, 120));
    }

    private static void DrawAircraft(Gfx g, AKind kind, float x, float y, bool flip, float time, float px)
    {
        switch (kind)
        {
            case AKind.Heli:
            {
                g.PixelsCentered(Heli, x, y, px, flip);
                // Spinning rotor and tail rotor.
                float w = 13 * px * MathF.Abs(MathF.Cos(time * 30));
                g.Rect(x - w, y - 4.5f * px, w * 2, px * 0.7f, new Color(40, 40, 46));
                float tx = x + (flip ? 1 : -1) * 7.5f * px;
                g.Circle(tx, y - 2.5f * px, 2.2f * px * MathF.Abs(MathF.Sin(time * 40)) + 0.5f, new Color(60, 60, 70));
                g.Glow(x + (flip ? 1 : -1) * 2 * px, y + 3.5f * px, 3 * px, Pal.Red, 0.6f * (MathF.Sin(time * 8) > 0 ? 1 : 0.2f));
                break;
            }
            case AKind.Jet:
                g.PixelsCentered(Jet, x, y, px, flip);
                g.Glow(x + (flip ? 1 : -1) * 8 * px, y + 0.5f * px, 5 * px, Pal.Orange, 0.8f);
                break;
            default:
                g.PixelsCentered(Bomber, x, y, px, flip);
                g.Glow(x + (flip ? 1 : -1) * 10 * px, y, 4 * px, Pal.Orange, 0.6f);
                break;
        }
    }

    private void DrawTrooper(Gfx g, Trooper_ t)
    {
        float x = t.X + Sway(t);
        if (t.State == TState.Chute)
        {
            float tilt = MathF.Cos(t.SwayPhase) * 2;
            var top = new Vector2(x + tilt, t.Y - 24);
            // Rigging lines.
            g.Line(x - 1, t.Y - 5, top.X - 13, top.Y + 3, 0.7f, Pal.LightGrey * 0.8f);
            g.Line(x + 1, t.Y - 5, top.X + 13, top.Y + 3, 0.7f, Pal.LightGrey * 0.8f);
            g.Line(x, t.Y - 5, top.X, top.Y + 3, 0.7f, Pal.LightGrey * 0.6f);
            // Canopy with stripes.
            g.Pie(top.X, top.Y + 3, 15, MathF.PI, MathF.PI * 2, t.Chute);
            for (int i = 0; i < 3; i++)
                g.Pie(top.X, top.Y + 3, 15, MathF.PI + (i * 2 + 1) * MathF.PI / 6, MathF.PI + (i * 2 + 2) * MathF.PI / 6, Pal.Darken(t.Chute, 0.25f));
            g.Ellipse(top.X, top.Y + 3, 15, 2.5f, Pal.Darken(t.Chute, 0.4f));
            g.Ellipse(top.X - 4, top.Y - 5, 4, 2, Color.White * 0.35f);
        }
        else if (t.State == TState.Plummet)
        {
            // A torn canopy flapping above.
            g.Line(x - 4, t.Y - 10, x + 3, t.Y - 16 + MathF.Sin(Time * 30) * 3, 2, Pal.LightGrey * 0.7f);
        }
        bool walk = t.State == TState.Assault && (int)(Time * 8) % 2 == 0;
        g.PixelsCentered(Trooper, x, t.Y + (walk ? -0.5f : 0), 2f, walk);
    }

    private static readonly Vector2[] Bunker = new Vector2[4];

    private static void DrawGun(Gfx g, float angle, float recoil, float k)
    {
        // Barrel.
        var dir = MathF2.FromAngle(angle);
        var pivot = new Vector2(GunX, GunY - 4 * k);
        var end = pivot + dir * (26 - recoil * 3) * k;
        g.Line(pivot, end, 6 * k, new Color(60, 66, 80));
        g.Line(pivot, end, 3 * k, new Color(140, 150, 170));
        g.Rect(end.X - 3 * k, end.Y - 3 * k, 6 * k, 6 * k, new Color(80, 86, 100));
        // Turret dome and bunker.
        g.Glow(GunX, GunY - 4 * k, 34 * k, Pal.Cyan, 0.12f);
        g.Pie(GunX, GunY, 14 * k, MathF.PI, MathF.PI * 2, new Color(90, 100, 120));
        g.Pie(GunX - 3 * k, GunY - 2 * k, 8 * k, MathF.PI * 1.1f, MathF.PI * 1.6f, new Color(150, 160, 180));
        Bunker[0] = new(GunX - 22 * k, GroundY);
        Bunker[1] = new(GunX - 16 * k, GunY);
        Bunker[2] = new(GunX + 16 * k, GunY);
        Bunker[3] = new(GunX + 22 * k, GroundY);
        g.Polygon(Bunker, new Color(110, 100, 90));
        g.Rect(GunX - 16 * k, GunY, 32 * k, 2 * k, new Color(150, 140, 120));
        g.Rect(GunX - 4 * k, GunY + 6 * k, 8 * k, 4 * k, Pal.Cyan * 0.8f);
    }

    // ------------------------------------------------------------------ icon

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        float s = r.H / 70f;
        DrawSky(g, r, time);
        // Helicopter crossing and a trooper drifting down.
        float hx = r.X + Backdrops.Mod(time * 30 * s + r.W * 0.45f, r.W + 60 * s) - 30 * s;
        DrawAircraft(g, AKind.Heli, hx, r.Y + 14 * s, false, time, 1.1f * s);
        for (int i = 0; i < 2; i++)
        {
            float ph = Backdrops.Mod(time * 0.25f + i * 0.5f, 1);
            float tx = r.X + r.W * (i == 0 ? 0.22f : 0.78f) + MathF.Sin(time * 2 + i) * 3 * s;
            float ty = r.Y + (20 + ph * 36) * s;
            var c = ChuteCols[i + 1];
            g.Line(tx, ty - 3 * s, tx - 7 * s, ty - 12 * s, 0.5f * s, Pal.LightGrey);
            g.Line(tx, ty - 3 * s, tx + 7 * s, ty - 12 * s, 0.5f * s, Pal.LightGrey);
            g.Pie(tx, ty - 12 * s, 8 * s, MathF.PI, MathF.PI * 2, c);
            g.Pie(tx, ty - 12 * s, 8 * s, MathF.PI * 1.33f, MathF.PI * 1.66f, Pal.Darken(c, 0.25f));
            g.PixelsCentered(Trooper, tx, ty, 1f * s);
        }
        // The gun, firing up at an angle.
        float gunScale = s * 1.2f;
        float a = -MathF.PI / 2 + MathF.Sin(time * 1.4f) * 0.7f;
        var pivot = new Vector2(r.CenterX, r.Bottom - 10 * s);
        var dir = MathF2.FromAngle(a);
        float st = Backdrops.Mod(time * 2.5f, 1);
        var shell = pivot + dir * (20 + st * 60) * s;
        g.Glow(shell, 5 * s, Pal.Yellow, 0.8f);
        g.Circle(shell.X, shell.Y, 1.2f * s, Pal.White);
        g.Line(pivot, pivot + dir * 18 * gunScale, 4 * gunScale, new Color(60, 66, 80));
        g.Line(pivot, pivot + dir * 18 * gunScale, 2 * gunScale, new Color(140, 150, 170));
        g.Pie(pivot.X, pivot.Y + 2 * s, 9 * gunScale, MathF.PI, MathF.PI * 2, new Color(90, 100, 120));
        g.Rect(pivot.X - 14 * gunScale, pivot.Y + 2 * s, 28 * gunScale, r.Bottom - pivot.Y, new Color(110, 100, 90));
    }

    // ------------------------------------------------------------------ autoplay

    public override void AutoPlay(Controls c)
    {
        var muzzle = new Vector2(GunX, GunY - 4);
        Vector2? best = null;
        float bestScore = float.MaxValue;
        void Consider(Vector2 p, Vector2 v, float priority)
        {
            // Lead the target.
            var aim = p;
            for (int k = 0; k < 3; k++)
                aim = p + v * (Vector2.Distance(aim, muzzle) / ShellSpeed);
            if (aim.Y > GunY - 20)
                return;
            float score = priority + MathF.Abs(MathF2.WrapAngle(MathF.Atan2(aim.Y - muzzle.Y, aim.X - muzzle.X) - _angle)) * 60;
            if (score < bestScore)
            {
                bestScore = score;
                best = aim;
            }
        }
        foreach (var b in _bombs)
            Consider(b.Pos, b.Vel, -400);
        foreach (var t in _troops)
        {
            if (t.State is TState.Landed or TState.Assault || t.Y < 150)
                continue;
            float vx = t.State == TState.Chute ? MathF.Cos(t.SwayPhase) * 3 * 2.2f : 0;
            Consider(new Vector2(t.X + Sway(t), t.Y), new Vector2(vx, t.Vy), -t.Y * 0.8f);
        }
        foreach (var a in _air)
            if (a.X > 150 && a.X < 490)  // a human takes a moment to react
                Consider(new Vector2(a.X, a.Y), new Vector2(a.Vx, 0), 40);
        float target = -MathF.PI / 2;
        if (best is Vector2 bp)
            target = MathF.Atan2(bp.Y - muzzle.Y, bp.X - muzzle.X);
        float d = MathF2.WrapAngle(target - _angle);
        c.SetDirections(MathF.Abs(d) < 0.03f ? 0 : MathF.Sign(d), 0);
        bool onTarget = MathF.Abs(d) < 0.06f && best != null;
        c.Fire = onTarget;
        c.FirePressed = onTarget && Tick % 8 == 0;
    }
}
