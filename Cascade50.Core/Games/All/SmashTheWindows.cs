using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 40 Smash the Windows: breakout where the bricks are the windows of a building at night. Lit
/// windows drop power-ups, double glazing takes two hits and the cat does not like it one bit.
/// </summary>
public sealed class SmashTheWindows : MiniGame
{
    public override int Number => 40;
    public override string Title => "Smash the Windows";
    public override Category Category => Category.Arcade;
    public override string Tagline => "Bounce a ball through every window of the old tower block.";
    public override Color Accent => Pal.Yellow;

    public override string[] HowToPlay =>
    [
        "Bounce the ball off your bat to break every window. Don't drop it!",
        "Double glazing takes 2 hits, shutters 3. Lit windows drop power-ups.",
        "Mind the cat! Each building is harder.",
    ];

    public override string[] DesktopControls => ["Mouse or ARROWS move the bat.", "SPACE or click to launch."];
    public override string[] TouchControls => ["Drag anywhere to move the bat.", "Tap to launch the ball."];
    public override Pad Pad => Pad.Horizontal;

    // ------------------------------------------------------------------ layout

    private const int Cols = 12, Rows = 7;
    private const float WinW = 34, WinH = 18, GapX = 6, GapY = 9;
    private const float GridX = 80, GridY = 58;
    private const float BatY = 334, BallR = 4;

    private enum Win : byte { None, Normal, Lit, Double, Cracked, Cat, Shutter, Shutter2, Shutter3, Broken }

    private static readonly string[][] Maps =
    [
        [
            "nnnnnnnnnnnn",
            "nlnnnnnnnnln",
            "nnncnnnnnnnn",
            "nnnnnlnnnnnn",
            "nnnnnnnncnnn",
            "nlnnnnnnnnln",
            "nnnnnnnnnnnn",
        ],
        [
            "dddddddddddd",
            "dnnnlnnlnnnd",
            "dnnnnnnnnnnd",
            "dnlnndcnnlnd",
            "dnnnnnnnnnnd",
            "dnnlnnnnlnnd",
            "dddddddddddd",
        ],
        [
            "nnn.nnnn.nnn",
            "nln.dccd.nln",
            "nnn.nnnn.nnn",
            "....llll....",
            "nnn.nnnn.nnn",
            "ndn.nddn.ndn",
            "nnn.nnnn.nnn",
        ],
        [
            "ssssssssssss",
            "nnlnnnnnnlnn",
            "ndddnnnndddn",
            "nnnnnccnnnnn",
            "nlnnddddnnln",
            "nnnnnnnnnnnn",
            "..ssss..ssss",
        ],
        [
            "lddddddddddl",
            "dsnnnnnnnnsd",
            "dncnlddlncnd",
            "dnnnnssnnnnd",
            "dnlnddddnlnd",
            "dsnnnnnnnnsd",
            "lddddddddddl",
        ],
    ];

    private static readonly string[] Names = ["THE TOWER BLOCK", "THE OFFICE BLOCK", "THE TERRACE", "THE HOTEL", "THE TOWN HALL"];

    private static readonly Color[] Facades =
    [
        new(110, 50, 40), new(60, 66, 84), new(120, 90, 60), new(70, 50, 80), new(90, 85, 80),
    ];

    private struct Ball
    {
        public Vector2 Pos, Vel;
    }

    private enum Power { Wide, Multi, Laser, Slow, Life }

    private struct Capsule
    {
        public Vector2 Pos;
        public Power Kind;
    }

    private struct Bolt
    {
        public Vector2 Pos;
    }

    private struct Cat
    {
        public Vector2 Pos, Vel;
        public float Spin;
    }

    private readonly Win[,] _wins = new Win[Cols, Rows];
    private readonly bool[,] _curtainL = new bool[Cols, Rows];
    private readonly List<Ball> _balls = new();
    private readonly List<Capsule> _caps = new();
    private readonly List<Bolt> _bolts = new();
    private readonly List<Cat> _cats = new();
    private float _batX, _batW = 64;
    private bool _stuck;
    private float _stuckTime;
    private float _wide, _laser, _slow, _laserCool;
    private float _speed;
    private int _left;
    private float _banner;
    private float _hitFlash;
    private int _combo;
    private float _speedUp;
    private float _sinceBreak, _autoOffset;

    protected override void Start()
    {
        Lives = 3;
        Level = 1;
        _batX = 320;
        LoadLevel();
    }

    private void LoadLevel()
    {
        var map = Maps[(Level - 1) % Maps.Length];
        _left = 0;
        for (int c = 0; c < Cols; c++)
            for (int r = 0; r < Rows; r++)
            {
                var w = map[r][c] switch
                {
                    'n' => Win.Normal, 'l' => Win.Lit, 'd' => Win.Double, 'c' => Win.Cat, 's' => Win.Shutter, _ => Win.None,
                };
                // Later rounds: some plain windows get double glazing.
                if (w == Win.Normal && Level > Maps.Length && Chance(0.25f))
                    w = Win.Double;
                _wins[c, r] = w;
                _curtainL[c, r] = Chance(0.5f);
                if (w != Win.None)
                    _left++;
            }
        _balls.Clear();
        _caps.Clear();
        _bolts.Clear();
        _cats.Clear();
        _wide = _laser = _slow = 0;
        _speed = 230 + 18 * (Level - 1);
        _speedUp = 0;
        _banner = 2.2f;
        Status = Names[(Level - 1) % Names.Length];
        ResetBall();
    }

    private void ResetBall()
    {
        _balls.Clear();
        _balls.Add(new Ball { Pos = new Vector2(_batX, BatY - BallR - 4), Vel = Vector2.Zero });
        _stuck = true;
        _stuckTime = 0;
        _combo = 0;
    }

    private static RectF WinRect(int c, int r) => new(GridX + c * (WinW + GapX), GridY + r * (WinH + GapY), WinW, WinH);

    private static bool Solid(Win w) => w is not (Win.None or Win.Broken);

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        if (_banner > 0)
            _banner -= Dt;
        if (_hitFlash > 0)
            _hitFlash -= Dt;
        if (_wide > 0)
            _wide -= Dt;
        if (_laser > 0)
            _laser -= Dt;
        if (_slow > 0)
            _slow -= Dt;
        _sinceBreak += Dt;

        // Bat: follows the pointer directly, or the keys / stick.
        float targetW = _wide > 0 ? 104 : 64;
        _batW = MathF2.Approach(_batW, targetW, 160 * Dt);
        if ((In.HasHover && In.PointerMoved && !IsTouch) || In.PointerDown)
            _batX += (In.Pointer.X - _batX) * MathF.Min(1, Dt * 25);
        _batX += In.AxisX * 420 * Dt;
        _batX = MathF2.Clamp(_batX, _batW / 2 + 4, 636 - _batW / 2);

        float speed = (_speed + _speedUp) * (_slow > 0 ? 0.65f : 1);
        _speedUp = MathF.Min(140, _speedUp + Dt * 2.2f);

        if (_stuck)
        {
            _stuckTime += Dt;
            var b = _balls[0];
            b.Pos = new Vector2(_batX + 8, BatY - BallR - 4);
            _balls[0] = b;
            if (In.FirePressed || In.PointerReleased || In.AltPressed || _stuckTime > 3f)
            {
                _stuck = false;
                b.Vel = MathF2.FromAngle(-MathF.PI / 2 + 0.35f, speed);
                _balls[0] = b;
                Sound.Play(Sfx.Bounce, 0.3f);
            }
        }
        else
        {
            for (int i = _balls.Count - 1; i >= 0; i--)
            {
                var b = _balls[i];
                if (b.Vel.LengthSquared() > 1)
                    b.Vel = Vector2.Normalize(b.Vel) * speed;
                bool lost = false;
                for (int sub = 0; sub < 3 && !lost; sub++)
                    lost = MoveBall(ref b, Dt / 3);
                if (lost)
                {
                    _balls.RemoveAt(i);
                    continue;
                }
                _balls[i] = b;
            }
            if (_balls.Count == 0)
            {
                Sound.Play(Sfx.Lose);
                Fx.Burst(_batX, BatY, Pal.Red, 20, 100, 0.6f);
                if (LoseLife())
                    return;
                _wide = _laser = _slow = 0;
                _caps.Clear();
                ResetBall();
                return;
            }
        }

        // Laser.
        if (_laser > 0 && !_stuck)
        {
            _laserCool -= Dt;
            if (_laserCool <= 0 || In.FirePressed)
            {
                _laserCool = 0.35f;
                _bolts.Add(new Bolt { Pos = new Vector2(_batX - _batW / 2 + 6, BatY - 6) });
                _bolts.Add(new Bolt { Pos = new Vector2(_batX + _batW / 2 - 6, BatY - 6) });
                Sound.Play(Sfx.Laser, 0.3f, 0.4f);
            }
        }
        for (int i = _bolts.Count - 1; i >= 0; i--)
        {
            var bolt = _bolts[i];
            bolt.Pos.Y -= 480 * Dt;
            bool gone = bolt.Pos.Y < Screen.HudHeight;
            if (!gone && HitWindowAt(bolt.Pos, out int c, out int r))
            {
                Smash(c, r);
                gone = true;
            }
            if (gone)
                _bolts.RemoveAt(i);
            else
                _bolts[i] = bolt;
        }

        // Power-up capsules.
        for (int i = _caps.Count - 1; i >= 0; i--)
        {
            var cap = _caps[i];
            cap.Pos.Y += 90 * Dt;
            if (cap.Pos.Y > BatY - 8 && cap.Pos.Y < BatY + 8 && MathF.Abs(cap.Pos.X - _batX) < _batW / 2 + 8)
            {
                Collect(cap);
                _caps.RemoveAt(i);
                continue;
            }
            if (cap.Pos.Y > 370)
                _caps.RemoveAt(i);
            else
                _caps[i] = cap;
        }

        // Cats leaping off.
        for (int i = _cats.Count - 1; i >= 0; i--)
        {
            var cat = _cats[i];
            cat.Vel.Y += 400 * Dt;
            cat.Pos += cat.Vel * Dt;
            cat.Spin += Dt * 5;
            if (cat.Pos.Y > 380)
                _cats.RemoveAt(i);
            else
                _cats[i] = cat;
        }

        if (_left <= 0)
        {
            AddScore(500 * Level, 320, 200, Pal.Cyan);
            Sound.Play(Sfx.LevelUp);
            Level++;
            LoadLevel();
        }
    }

    /// <summary>Moves a ball one sub-step; returns true when it falls off the bottom.</summary>
    private bool MoveBall(ref Ball b, float dt)
    {
        b.Pos += b.Vel * dt;
        if (b.Pos.X < BallR)
        {
            b.Pos.X = BallR;
            b.Vel.X = MathF.Abs(b.Vel.X);
            Sound.Play(Sfx.Bounce, -0.4f, 0.3f);
        }
        else if (b.Pos.X > 640 - BallR)
        {
            b.Pos.X = 640 - BallR;
            b.Vel.X = -MathF.Abs(b.Vel.X);
            Sound.Play(Sfx.Bounce, -0.4f, 0.3f);
        }
        if (b.Pos.Y < Screen.HudHeight + BallR)
        {
            b.Pos.Y = Screen.HudHeight + BallR;
            b.Vel.Y = MathF.Abs(b.Vel.Y);
            Sound.Play(Sfx.Bounce, -0.4f, 0.3f);
        }

        // Bat.
        if (b.Vel.Y > 0 && b.Pos.Y + BallR >= BatY - 4 && b.Pos.Y - BallR <= BatY + 4 && MathF.Abs(b.Pos.X - _batX) <= _batW / 2 + BallR)
        {
            float k = MathF2.Clamp((b.Pos.X - _batX) / (_batW / 2), -1, 1);
            float jitter = Rand(-0.06f, 0.06f) * (1 + MathF.Min(4, _sinceBreak / 6));
            float ang = MathF2.Clamp(-MathF.PI / 2 + k * 1.05f + jitter, -MathF.PI / 2 - 1.15f, -MathF.PI / 2 + 1.15f);
            b.Vel = MathF2.FromAngle(ang, b.Vel.Length());
            b.Pos.Y = BatY - 4 - BallR;
            _combo = 0;
            Sound.Play(Sfx.Bounce, 0.1f + k * 0.2f, 0.7f);
            Fx.Burst(b.Pos.X, BatY - 4, Pal.Cyan, 6, 60, 0.25f, 1.5f);
        }
        if (b.Pos.Y > 372)
            return true;

        // Windows.
        int c0 = Math.Max(0, (int)((b.Pos.X - BallR - GridX) / (WinW + GapX)));
        int c1 = Math.Min(Cols - 1, (int)((b.Pos.X + BallR - GridX) / (WinW + GapX)));
        int r0 = Math.Max(0, (int)((b.Pos.Y - BallR - GridY) / (WinH + GapY)));
        int r1 = Math.Min(Rows - 1, (int)((b.Pos.Y + BallR - GridY) / (WinH + GapY)));
        if (b.Pos.X + BallR < GridX || b.Pos.Y + BallR < GridY)
            return false;
        for (int c = c0; c <= c1; c++)
            for (int r = r0; r <= r1; r++)
            {
                if (!Solid(_wins[c, r]))
                    continue;
                var rect = WinRect(c, r);
                float nx = MathF2.Clamp(b.Pos.X, rect.X, rect.Right), ny = MathF2.Clamp(b.Pos.Y, rect.Y, rect.Bottom);
                float dx = b.Pos.X - nx, dy = b.Pos.Y - ny;
                if (dx * dx + dy * dy > BallR * BallR)
                    continue;
                // Bounce off the side we came in through.
                float penX = MathF.Min(b.Pos.X + BallR - rect.X, rect.Right - (b.Pos.X - BallR));
                float penY = MathF.Min(b.Pos.Y + BallR - rect.Y, rect.Bottom - (b.Pos.Y - BallR));
                if (penX < penY)
                {
                    b.Vel.X = b.Pos.X < rect.CenterX ? -MathF.Abs(b.Vel.X) : MathF.Abs(b.Vel.X);
                    b.Pos.X += b.Pos.X < rect.CenterX ? -penX : penX;
                }
                else
                {
                    b.Vel.Y = b.Pos.Y < rect.CenterY ? -MathF.Abs(b.Vel.Y) : MathF.Abs(b.Vel.Y);
                    b.Pos.Y += b.Pos.Y < rect.CenterY ? -penY : penY;
                }
                // Never let the ball go too flat.
                if (MathF.Abs(b.Vel.Y) < b.Vel.Length() * 0.25f)
                    b.Vel.Y = MathF.Sign(b.Vel.Y == 0 ? 1 : b.Vel.Y) * b.Vel.Length() * 0.3f;
                Smash(c, r);
                return false;
            }
        return false;
    }

    private bool HitWindowAt(Vector2 p, out int c, out int r)
    {
        c = (int)MathF.Floor((p.X - GridX) / (WinW + GapX));
        r = (int)MathF.Floor((p.Y - GridY) / (WinH + GapY));
        if (c < 0 || r < 0 || c >= Cols || r >= Rows || !Solid(_wins[c, r]))
            return false;
        return WinRect(c, r).Contains(p);
    }

    private void Smash(int c, int r)
    {
        var rect = WinRect(c, r);
        var w = _wins[c, r];
        float cx = rect.CenterX, cy = rect.CenterY;
        switch (w)
        {
            case Win.Double:
                _wins[c, r] = Win.Cracked;
                Sound.Play(Sfx.Crack, 0.4f, 0.7f);
                Fx.Burst(cx, cy, Pal.Ice, 5, 50, 0.3f, 1.2f, 200, false);
                AddScore(5);
                return;
            case Win.Shutter:
            case Win.Shutter2:
                _wins[c, r] = w + 1;
                Sound.Play(Sfx.Thud, 0.3f, 0.7f);
                Fx.Burst(cx, cy, Pal.Silver, 6, 60, 0.3f, 1.5f);
                AddScore(5);
                return;
        }
        _combo++;
        int pts = w switch { Win.Lit => 15, Win.Cracked => 25, Win.Cat => 100, Win.Shutter3 => 50, _ => 10 } * Math.Min(_combo, 4);
        AddScore(pts, cx, cy - 6, w == Win.Cat ? Pal.Pink : Pal.Yellow);
        _wins[c, r] = Win.Broken;
        _left--;
        _sinceBreak = 0;
        Sound.Play(Sfx.Crack, Rand(-0.1f, 0.4f));
        Sound.Play(Sfx.Splash, 0.8f + Rand(0, 0.2f), 0.35f);
        // Glass shards tumble down.
        var glass = w == Win.Lit || w == Win.Cat ? Pal.Gold : Pal.Ice;
        for (int i = 0; i < 14; i++)
            Fx.Spark(cx + Rand(-14, 14), cy + Rand(-7, 7), Rand(-70, 70), Rand(-90, 20), glass, Rand(0.5f, 1f), Rand(1, 2.4f), 420, i % 3 == 0);
        Fx.Burst(cx, cy, Pal.White, 8, 80, 0.25f, 1.5f);
        if (w == Win.Lit || (w == Win.Normal && Chance(0.04f)))
        {
            var kind = Pick(Power.Wide, Power.Multi, Power.Laser, Power.Slow, Power.Wide, Power.Multi);
            if (Chance(0.08f))
                kind = Power.Life;
            _caps.Add(new Capsule { Pos = new Vector2(cx, cy), Kind = kind });
        }
        if (w == Win.Cat)
        {
            Sound.Play(Sfx.Hurt, 0.9f);
            Sound.Play(Sfx.Whoosh, 0.9f, 0.6f);
            Fx.Float("HISSS!", cx, cy - 20, Pal.Pink);
            _cats.Add(new Cat { Pos = new Vector2(cx, cy), Vel = new Vector2(Rand(-120, 120), -200), Spin = 0 });
        }
    }

    private void Collect(Capsule cap)
    {
        Sound.Play(Sfx.PowerUp);
        Fx.Burst(cap.Pos.X, cap.Pos.Y, CapColour(cap.Kind), 18, 100, 0.5f);
        AddScore(25);
        switch (cap.Kind)
        {
            case Power.Wide:
                _wide = 14;
                Fx.Float("WIDE BAT", cap.Pos.X, cap.Pos.Y - 14, Pal.Cyan);
                break;
            case Power.Laser:
                _laser = 10;
                Fx.Float("LASER", cap.Pos.X, cap.Pos.Y - 14, Pal.Red);
                break;
            case Power.Slow:
                _slow = 10;
                _speedUp = 0;
                Fx.Float("SLOW", cap.Pos.X, cap.Pos.Y - 14, Pal.Lime);
                break;
            case Power.Life:
                Lives++;
                Fx.Float("EXTRA LIFE", cap.Pos.X, cap.Pos.Y - 14, Pal.Pink);
                Sound.Play(Sfx.Bonus);
                break;
            default:
                Fx.Float("MULTIBALL", cap.Pos.X, cap.Pos.Y - 14, Pal.Yellow);
                if (_stuck)
                    break;
                int n = _balls.Count;
                for (int i = 0; i < n && _balls.Count < 8; i++)
                {
                    var b = _balls[i];
                    for (int k = -1; k <= 1; k += 2)
                    {
                        float a = MathF2.Angle(b.Vel) + k * 0.5f;
                        var v = MathF2.FromAngle(a, b.Vel.Length());
                        if (v.Y > -40)
                            v.Y = -MathF.Abs(v.Y) - 60;
                        _balls.Add(new Ball { Pos = b.Pos, Vel = v });
                    }
                }
                break;
        }
    }

    private static Color CapColour(Power p) => p switch
    {
        Power.Wide => Pal.Cyan, Power.Multi => Pal.Yellow, Power.Laser => Pal.Red, Power.Slow => Pal.Lime, _ => Pal.Pink,
    };

    private static string CapLetter(Power p) => p switch
    {
        Power.Wide => "W", Power.Multi => "M", Power.Laser => "L", Power.Slow => "S", _ => "+",
    };

    // ------------------------------------------------------------------ drawing

    private static readonly Dictionary<char, Color> CatColours = new() { ['k'] = new Color(15, 12, 20), ['e'] = Pal.Lime };

    private static readonly PixelArt CatArt = new(
    [
        "k...k...",
        "kk.kk...",
        "kkkkk...",
        "kekek...",
        "kkkkk..k",
        ".kkk..k.",
        "kkkkkkk.",
        "kkkkkk..",
    ], CatColours);

    public override void Draw(Gfx g)
    {
        DrawNight(g, Screen.Bounds, Time, 11);
        var facade = Facades[(Level - 1) % Facades.Length];
        DrawBuilding(g, Screen.Bounds, facade, Time, Level);

        for (int c = 0; c < Cols; c++)
            for (int r = 0; r < Rows; r++)
                DrawWindow(g, WinRect(c, r), _wins[c, r], _curtainL[c, r], Time + c * 0.7f + r * 1.3f, 1);

        // Pavement.
        g.GradientV(0, 346, 640, 14, new Color(60, 60, 70), new Color(30, 30, 38));
        g.Rect(0, 346, 640, 1, Pal.Grey);

        foreach (var cat in _cats)
            g.PixelsCentered(CatArt, cat.Pos.X, cat.Pos.Y, 2.4f, cat.Vel.X < 0);

        foreach (var cap in _caps)
        {
            var col = CapColour(cap.Kind);
            g.Glow(cap.Pos, 16, col, 0.5f);
            g.RoundRect(cap.Pos.X - 12, cap.Pos.Y - 6, 24, 12, 6, Pal.Darken(col, 0.3f));
            g.RoundRect(cap.Pos.X - 12, cap.Pos.Y - 6, 24, 5, 3, Pal.Lighten(col, 0.3f) * 0.6f);
            g.Text(CapLetter(cap.Kind), cap.Pos.X, cap.Pos.Y - 4, 1, Pal.White, Align.Center);
        }

        foreach (var bolt in _bolts)
        {
            g.Glow(bolt.Pos, 8, Pal.Red, 0.8f);
            g.Rect(bolt.Pos.X - 1, bolt.Pos.Y - 6, 2, 10, Pal.White);
        }

        // Bat.
        var batCol = _laser > 0 ? Pal.Red : _wide > 0 ? Pal.Cyan : Pal.Orange;
        g.Glow(_batX, BatY, _batW * 0.7f, batCol, 0.35f);
        g.RoundRect(_batX - _batW / 2, BatY - 4, _batW, 9, 4.5f, Pal.Darken(batCol, 0.25f));
        g.RoundRect(_batX - _batW / 2 + 2, BatY - 3, _batW - 4, 3, 1.5f, Pal.Lighten(batCol, 0.5f));
        if (_laser > 0)
        {
            g.Rect(_batX - _batW / 2 + 4, BatY - 9, 4, 6, Pal.Silver);
            g.Rect(_batX + _batW / 2 - 8, BatY - 9, 4, 6, Pal.Silver);
        }

        foreach (var b in _balls)
        {
            g.Glow(b.Pos, 14, Pal.White, 0.5f);
            g.Circle(b.Pos.X, b.Pos.Y, BallR, Pal.White);
            g.Circle(b.Pos.X - 1.2f, b.Pos.Y - 1.2f, BallR * 0.4f, Pal.Sky);
        }

        // Power timers.
        float px = 8;
        if (_wide > 0) px = Timer(g, px, "WIDE", _wide / 14, Pal.Cyan);
        if (_laser > 0) px = Timer(g, px, "LASER", _laser / 10, Pal.Red);
        if (_slow > 0) Timer(g, px, "SLOW", _slow / 10, Pal.Lime);

        if (_banner > 0)
        {
            float a = MathF.Min(1, _banner);
            g.TextShadow(Names[(Level - 1) % Names.Length], 320, 262, 2.5f, Pal.Yellow * a, Align.Center);
            g.TextShadow("BUILDING " + Level, 320, 290, 1.5f, Pal.White * a, Align.Center);
        }
        if (_stuck && _banner <= 0 && (int)(Time * 3) % 2 == 0)
            g.TextShadow(IsTouch ? "TAP TO LAUNCH" : "SPACE / CLICK TO LAUNCH", 320, 290, 1.5f, Pal.White, Align.Center);
    }

    private static float Timer(Gfx g, float x, string label, float k, Color c)
    {
        g.RoundRect(x, 30, 64, 14, 4, Pal.Panel * 0.8f);
        g.Rect(x + 2, 40, 60 * MathF2.Clamp(k, 0, 1), 2, c);
        g.Text(label, x + 32, 31, 1, c, Align.Center);
        return x + 70;
    }

    private static void DrawNight(Gfx g, RectF r, float time, int seed)
    {
        g.GradientV(r.X, r.Y, r.W, r.H, new Color(4, 6, 24), new Color(30, 20, 60));
        Backdrops.Stars(g, time, 0, seed, r, 70);
        float s = r.H / 360f;
        g.Glow(r.X + r.W * 0.9f, r.Y + r.H * 0.2f, 60 * s, Pal.Ice, 0.4f);
        g.Circle(r.X + r.W * 0.9f, r.Y + r.H * 0.2f, 16 * s, new Color(240, 240, 220));
        g.Circle(r.X + r.W * 0.9f + 6 * s, r.Y + r.H * 0.2f - 3 * s, 13 * s, new Color(30, 22, 60));
        // Distant skyline.
        var rng = new Random(seed);
        for (float x = r.X; x < r.Right; x += 26 * s)
        {
            float h = (40 + rng.Next(0, 70)) * s;
            g.Rect(x, r.Bottom - h, 24 * s, h, new Color(16, 14, 36));
            for (int k = 0; k < 4; k++)
                if (rng.Next(3) == 0)
                    g.Rect(x + 4 * s + (k % 2) * 10 * s, r.Bottom - h + 6 * s + (k / 2) * 10 * s, 4 * s, 4 * s, Pal.Gold * 0.35f);
        }
    }

    private static void DrawBuilding(Gfx g, RectF r, Color facade, float time, int level)
    {
        float s = r.W / 640f, sy = r.H / 360f;
        float x0 = r.X + 64 * s, x1 = r.X + 576 * s, top = r.Y + 40 * sy;
        g.GradientV(x0, top, x1 - x0, r.Bottom - top, Pal.Lighten(facade, 0.05f), Pal.Darken(facade, 0.45f));
        // Brick courses.
        for (float y = top + 4 * sy; y < r.Bottom; y += 6 * sy)
            g.Rect(x0, y, x1 - x0, 0.8f * sy, Color.Black * 0.12f);
        g.Rect(x0 - 4 * s, top - 4 * sy, x1 - x0 + 8 * s, 6 * sy, Pal.Darken(facade, 0.3f));
        // Roof bits: water tank and an aerial with a blinking light.
        float tx = x0 + 60 * s;
        g.Rect(tx, top - 14 * sy, 30 * s, 10 * sy, new Color(80, 60, 50));
        g.Rect(tx - 2 * s, top - 16 * sy, 34 * s, 3 * sy, new Color(60, 45, 40));
        g.Rect(tx + 4 * s, top - 6 * sy, 2 * s, 4 * sy, new Color(60, 45, 40));
        g.Rect(tx + 24 * s, top - 6 * sy, 2 * s, 4 * sy, new Color(60, 45, 40));
        float ax = x1 - 70 * s;
        g.Rect(ax, top - 17 * sy, 1.5f * s, 13 * sy, Pal.Grey);
        g.Rect(ax - 8 * s, top - 12 * sy, 17 * s, 1.2f * sy, Pal.Grey);
        g.Rect(ax - 5 * s, top - 8 * sy, 11 * s, 1.2f * sy, Pal.Grey);
        if ((int)(time * 1.5f) % 2 == 0)
            g.Glow(ax + 0.7f * s, top - 18 * sy, 8 * s, Pal.Red, 0.9f);
        g.Rect(x0, top, 3 * s, r.Bottom - top, Color.Black * 0.25f);
        g.Rect(x1 - 3 * s, top, 3 * s, r.Bottom - top, Color.Black * 0.25f);
    }

    private static void DrawWindow(Gfx g, RectF w, Win kind, bool curtainLeft, float t, float s)
    {
        if (kind == Win.None)
            return;
        var frame = new Color(220, 215, 200);
        g.Rect(w.X - 2 * s, w.Bottom, w.W + 4 * s, 2.5f * s, new Color(170, 165, 150));
        if (kind == Win.Broken)
        {
            g.Rect(w, new Color(8, 6, 14));
            g.RectOutline(w, 1.5f * s, Pal.Darken(frame, 0.4f));
            // Jagged leftovers.
            g.Triangle(new Vector2(w.X, w.Y), new Vector2(w.X + w.W * 0.35f, w.Y), new Vector2(w.X, w.Y + w.H * 0.5f), Pal.Ice * 0.35f);
            g.Triangle(new Vector2(w.Right, w.Bottom), new Vector2(w.Right - w.W * 0.3f, w.Bottom), new Vector2(w.Right, w.Y + w.H * 0.4f), Pal.Ice * 0.3f);
            return;
        }
        if (kind is Win.Shutter or Win.Shutter2 or Win.Shutter3)
        {
            g.GradientV(w.X, w.Y, w.W, w.H, new Color(150, 155, 165), new Color(90, 95, 105));
            for (float y = w.Y + 3 * s; y < w.Bottom; y += 3 * s)
                g.Rect(w.X, y, w.W, 0.8f * s, Color.Black * 0.3f);
            if (kind >= Win.Shutter2)
                g.Circle(w.X + w.W * 0.3f, w.CenterY, 3 * s, Color.Black * 0.35f);
            if (kind >= Win.Shutter3)
                g.Circle(w.X + w.W * 0.7f, w.CenterY - 2 * s, 3.5f * s, Color.Black * 0.35f);
            g.RectOutline(w, 1.2f * s, new Color(60, 60, 70));
            return;
        }
        bool lit = kind is Win.Lit or Win.Cat;
        if (lit)
        {
            g.Glow(w.CenterX, w.CenterY, w.W * 0.9f, Pal.Gold, 0.35f);
            g.GradientV(w.X, w.Y, w.W, w.H, new Color(255, 220, 120), new Color(230, 150, 50));
            var curtain = curtainLeft ? new Color(170, 40, 50) : new Color(50, 120, 70);
            g.Rect(w.X, w.Y, w.W * 0.22f, w.H, curtain);
            g.Rect(w.Right - w.W * 0.22f, w.Y, w.W * 0.22f, w.H, curtain);
            g.Rect(w.X, w.Y, w.W, 2.5f * s, Pal.Darken(curtain, 0.3f));
            if (kind == Win.Cat)
            {
                g.PixelsCentered(CatArt, w.CenterX, w.Bottom - 8.5f * s, 2f * s);
                if ((int)(t * 0.7f) % 4 == 0)
                    g.Rect(w.CenterX - 3 * s, w.Bottom - 12 * s, 4 * s, 1 * s, new Color(15, 12, 20));
            }
        }
        else
        {
            g.GradientV(w.X, w.Y, w.W, w.H, new Color(30, 50, 100), new Color(12, 18, 44));
            g.Line(w.X + w.W * 0.15f, w.Bottom - 2 * s, w.X + w.W * 0.45f, w.Y + 2 * s, 2 * s, Pal.White * 0.18f);
            g.Line(w.X + w.W * 0.4f, w.Bottom - 2 * s, w.X + w.W * 0.6f, w.Y + 6 * s, 1 * s, Pal.White * 0.12f);
        }
        bool dbl = kind is Win.Double or Win.Cracked;
        g.RectOutline(w, (dbl ? 2.4f : 1.5f) * s, frame);
        g.Rect(w.CenterX - 0.75f * s, w.Y, 1.5f * s, w.H, frame);
        if (dbl)
            g.Rect(w.X, w.CenterY - 0.75f * s, w.W, 1.5f * s, frame);
        if (kind == Win.Cracked)
        {
            var c = new Vector2(w.X + w.W * 0.3f, w.Y + w.H * 0.4f);
            g.Line(c, new Vector2(w.X + 2 * s, w.Y + 2 * s), 1 * s, Pal.White * 0.8f);
            g.Line(c, new Vector2(w.X + w.W * 0.48f, w.Bottom - 2 * s), 1 * s, Pal.White * 0.8f);
            g.Line(c, new Vector2(w.X + 2 * s, w.Bottom - 4 * s), 1 * s, Pal.White * 0.7f);
            g.Line(c, new Vector2(w.X + w.W * 0.46f, w.Y + 3 * s), 1 * s, Pal.White * 0.7f);
        }
    }

    // ------------------------------------------------------------------ icon

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        float s = r.H / 70f;
        DrawNight(g, r, time, 11);
        float bx = r.X + r.W * 0.2f, bw = r.W * 0.6f, top = r.Y + 8 * s;
        g.GradientV(bx, top, bw, r.Bottom - top, Facades[0], Pal.Darken(Facades[0], 0.45f));
        for (float y = top + 3 * s; y < r.Bottom; y += 4 * s)
            g.Rect(bx, y, bw, 0.6f * s, Color.Black * 0.12f);
        int cols = 5, rows = 3;
        float ww = bw / cols * 0.7f, wh = 9 * s;
        int broken = (int)(time * 1.5f) % (cols * rows);
        for (int c = 0; c < cols; c++)
            for (int row = 0; row < rows; row++)
            {
                int idx = row * cols + c;
                var w = new RectF(bx + bw / cols * (c + 0.15f), top + 6 * s + row * 14 * s, ww, wh);
                var kind = idx == 7 ? Win.Cat : idx % 4 == 1 ? Win.Lit : idx % 5 == 3 ? Win.Double : Win.Normal;
                if (idx < broken && idx != 7)
                    kind = Win.Broken;
                DrawWindow(g, w, kind, idx % 2 == 0, time + idx, s * 0.6f);
            }
        // Ball and bat.
        float bt = time * 1.6f;
        var ball = new Vector2(r.CenterX + MathF.Sin(bt) * r.W * 0.3f, r.Y + r.H * 0.62f + MathF.Abs(MathF.Cos(bt * 1.3f)) * r.H * 0.2f);
        float batX = r.CenterX + MathF.Sin(bt) * r.W * 0.3f;
        g.Glow(batX, r.Bottom - 6 * s, 24 * s, Pal.Orange, 0.4f);
        g.RoundRect(batX - 14 * s, r.Bottom - 8 * s, 28 * s, 4 * s, 2 * s, Pal.Orange);
        g.Glow(ball, 8 * s, Pal.White, 0.6f);
        g.Circle(ball.X, ball.Y, 2.4f * s, Pal.White);
        // Glass shards.
        for (int i = 0; i < 8; i++)
        {
            float k = (time * 0.9f + i * 0.125f) % 1;
            float x = r.X + r.W * (0.35f + 0.04f * i) + MathF.Sin(i * 3) * 10 * s * k;
            float y = r.Y + r.H * 0.35f + k * k * r.H * 0.6f;
            g.Rect(x, y, 2 * s, 1.5f * s, Pal.Ice * (1 - k));
        }
    }

    // ------------------------------------------------------------------ autopilot

    public override void AutoPlay(Controls c)
    {
        if (_stuck)
        {
            c.FirePressed = Tick % 50 == 10;
            c.Fire = c.FirePressed;
            return;
        }
        if (Tick % 80 == 0)
            _autoOffset = Rand(-0.6f, 0.6f);
        // Track the most urgent falling ball, predicting wall bounces.
        float target = _batX, bestT = float.MaxValue;
        foreach (var b in _balls)
        {
            if (b.Vel.Y <= 0)
                continue;
            float t = (BatY - 4 - b.Pos.Y) / b.Vel.Y;
            if (t < 0 || t > bestT)
                continue;
            bestT = t;
            float x = b.Pos.X + b.Vel.X * t;
            float span = 640 - 2 * BallR;
            x = Backdrops.Mod(x - BallR, 2 * span);
            if (x > span)
                x = 2 * span - x;
            target = x + BallR;
            // Aim the deflection slightly towards the middle.
            target += _autoOffset * _batW * 0.5f;
        }
        if (bestT == float.MaxValue)
        {
            // Nothing coming down: catch a capsule, or idle under the lowest ball.
            foreach (var cap in _caps)
                target = cap.Pos.X;
            if (_caps.Count == 0 && _balls.Count > 0)
                target = _balls[0].Pos.X;
        }
        float d = target - _batX;
        c.SetDirections(MathF2.Clamp(d / 18, -1, 1), 0);
    }
}
