using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 38 Sitting Target: a fairground shooting gallery. Tin ducks, rabbits and stars trundle past on
/// conveyor belts, bullseyes pop up and the bonus wheel spins. Score enough in each 60-second round
/// to qualify for the next.
/// </summary>
public sealed class SittingTarget : MiniGame
{
    public override int Number => 38;
    public override string Title => "Sitting Target";
    public override Category Category => Category.Shooter;
    public override string Tagline => "Roll up! Knock down the tin ducks at the fairground shooting gallery.";
    public override Color Accent => Pal.Red;

    public override string[] HowToPlay =>
    [
        "Shoot the targets on the belts: ducks 10, rabbits 20, stars 30, pop-up bullseyes 50.",
        "Shoot the wheel while it is lit for a prize. Reload when the gun is empty.",
        "Reach the target score in each 60-second round. Accuracy earns a bonus.",
    ];

    public override string[] DesktopControls => ["Aim with the mouse and click,", "or ARROWS + SPACE. X / R reload."];
    public override string[] TouchControls => ["Tap a target to shoot it.", "Tap RELOAD to refill the gun."];
    public override Pad Pad => Pad.None;

    // ------------------------------------------------------------------ layout

    private const float CounterY = 288;
    private static readonly float[] RailY = [148, 208, 258];
    private static readonly Vector2 WheelPos = new(320, 90);
    private const float WheelR = 30;
    private const int Rounds = 5;
    private const int Magazine = 8;
    private const float RoundTime = 60;
    private static readonly RectF ReloadRect = new(540, 318, 92, 34);

    private static readonly Dictionary<char, Color> Colours = new()
    {
        ['y'] = new Color(250, 210, 50), ['Y'] = new Color(200, 150, 30), ['o'] = Pal.Orange, ['k'] = new Color(20, 20, 30),
        ['w'] = new Color(235, 235, 240), ['W'] = new Color(180, 180, 195), ['p'] = Pal.Pink, ['s'] = new Color(110, 110, 120),
        ['g'] = new Color(90, 200, 90), ['G'] = new Color(50, 140, 60),
    };

    private static readonly PixelArt Duck = new(
    [
        "......yyy.......",
        ".....yyyyy......",
        ".....yykyyoo....",
        ".....yyyyyooo...",
        "......yyyy......",
        ".yy..yyyyyy.....",
        "yyyyyyyyyyyyy...",
        "yyyyyYYYyyyyyy..",
        ".yyyyyYYYyyyyy..",
        "..yyyyyyyyyyy...",
        "...YYYYYYYYY....",
        "......ss........",
        "......ss........",
    ], Colours);

    private static readonly PixelArt Rabbit = new(
    [
        "..ww...ww..",
        "..wp...pw..",
        "..wp...pw..",
        "..wp...pw..",
        "..ww...ww..",
        "...wwwww...",
        "..wwwwwww..",
        "..wkwwwkw..",
        "..wwwpwww..",
        "...wwwww...",
        "..WwwwwwW..",
        ".wwwwwwwww.",
        ".wwwwwwwww.",
        ".WwwwwwwwW.",
        "..WWWWWWW..",
        "....sss....",
        "....sss....",
    ], Colours);

    private static readonly PixelArt Frog = new(
    [
        "..gg...gg..",
        ".gkkg.gkkg.",
        ".gkwg.gwkg.",
        "ggggggggggg",
        "gGgggggggGg",
        "gggGGGGGggg",
        ".ggggggggg.",
        "gg.ggggg.gg",
        "....sss....",
        "....sss....",
    ], Colours);

    private enum Kind { Duck, Rabbit, Star, Golden }

    private struct Target
    {
        public Kind Kind;
        public float X;
        public float Down;      // 0 standing .. 1 knocked flat
        public bool Hit;
        public float Wobble;
    }

    private struct PopUp
    {
        public float X, T, Life;
        public bool Hit;
        public bool Frog;
    }

    private struct Hole
    {
        public Vector2 Pos;
        public float Life;
    }

    private enum Phase { Ready, Play, Summary }

    private readonly List<Target>[] _rows = [new(), new(), new()];
    private readonly float[] _rowSpeed = new float[3];
    private readonly float[] _rowOffset = new float[3];
    private readonly List<PopUp> _pops = new();
    private readonly List<Hole> _holes = new();
    private Phase _phase;
    private float _phaseTime;
    private int _round;
    private int _roundScore, _shots, _hits;
    private float _timeLeft;
    private Vector2 _cross;
    private int _ammo;
    private float _reload, _cooldown, _recoil;
    private float _wheelAngle, _wheelSpeed, _wheelLit, _wheelTimer;
    private float _double;
    private float _popTimer;
    private int _bonus;
    private int _lastTick;
    private float _flashTime;
    private string _flashText;
    private Color _flashColour;

    private static readonly string[] WheelLabels = ["50", "100", "X2", "25", "200", "+8", "75", "500"];
    private static readonly Color[] WheelCols =
    [
        Pal.Red, Pal.Yellow, Pal.Cyan, Pal.Orange, Pal.Magenta, Pal.Lime, Pal.Sky, Pal.Gold,
    ];

    private int Qualify => 900 + 300 * (_round - 1);

    protected override void Start()
    {
        _round = 1;
        _cross = new Vector2(320, 200);
        NewRound();
    }

    private void NewRound()
    {
        Level = _round;
        _phase = Phase.Ready;
        _phaseTime = 0;
        _roundScore = 0;
        _shots = 0;
        _hits = 0;
        _timeLeft = RoundTime;
        _ammo = Magazine;
        _reload = 0;
        _double = 0;
        _wheelLit = 0;
        _wheelTimer = 6;
        _wheelSpeed = 1.2f;
        _popTimer = 3;
        _pops.Clear();
        _holes.Clear();
        float k = 1 + 0.16f * (_round - 1);
        _rowSpeed[0] = 46 * k;
        _rowSpeed[1] = -64 * k;
        _rowSpeed[2] = 92 * k * (_round % 2 == 0 ? -1 : 1);
        float[] gaps = [76, 84, 70];
        for (int r = 0; r < 3; r++)
        {
            _rows[r].Clear();
            int n = (int)(720 / gaps[r]);
            for (int i = 0; i < n; i++)
            {
                var kind = r == 0 ? Kind.Duck : r == 1 ? Kind.Rabbit : Kind.Star;
                if (r == 0 && i == 4)
                    kind = Kind.Golden;
                _rows[r].Add(new Target { Kind = kind, X = i * gaps[r], Wobble = Rand(0, 6) });
            }
            _rowOffset[r] = 0;
        }
        Status = $"ROUND {_round}/{Rounds}  NEED {Qualify}";
    }

    private static int Points(Kind k) => k switch { Kind.Duck => 10, Kind.Rabbit => 20, Kind.Star => 30, _ => 100 };

    private void Flash(string text, Color c)
    {
        _flashText = text;
        _flashColour = c;
        _flashTime = 1.4f;
    }

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        _phaseTime += Dt;
        if (_flashTime > 0)
            _flashTime -= Dt;
        _recoil = MathF.Max(0, _recoil - Dt * 6);
        _wheelAngle += _wheelSpeed * Dt;
        UpdateBelts();
        UpdateAim();

        switch (_phase)
        {
            case Phase.Ready:
                if (_phaseTime > 2.2f)
                {
                    _phase = Phase.Play;
                    _phaseTime = 0;
                    Sound.Play(Sfx.Bell);
                }
                break;
            case Phase.Play:
                UpdatePlay();
                break;
            case Phase.Summary:
                if (_phaseTime > 4f)
                {
                    if (_roundScore < Qualify)
                    {
                        EndGame(false, $"You needed {Qualify} points");
                        return;
                    }
                    if (_round >= Rounds)
                    {
                        EndGame(true, "Champion marksman!");
                        return;
                    }
                    _round++;
                    NewRound();
                    Sound.Play(Sfx.LevelUp);
                }
                break;
        }

        for (int i = _holes.Count - 1; i >= 0; i--)
        {
            var h = _holes[i];
            h.Life -= Dt * 0.25f;
            if (h.Life <= 0)
                _holes.RemoveAt(i);
            else
                _holes[i] = h;
        }
    }

    private void UpdateBelts()
    {
        for (int r = 0; r < 3; r++)
        {
            var row = _rows[r];
            for (int i = 0; i < row.Count; i++)
            {
                var t = row[i];
                t.X += _rowSpeed[r] * Dt;
                bool wrapped = false;
                if (t.X > 680)
                {
                    t.X -= 720;
                    wrapped = true;
                }
                else if (t.X < -40)
                {
                    t.X += 720;
                    wrapped = true;
                }
                if (wrapped && t.Hit)
                {
                    // Comes back round the belt standing up again.
                    t.Hit = false;
                    t.Down = 0;
                    if (t.Kind == Kind.Golden || (r == 0 && Chance(0.12f)))
                        t.Kind = Chance(0.5f) ? Kind.Golden : Kind.Duck;
                }
                if (t.Hit)
                    t.Down = MathF.Min(1, t.Down + Dt * 5);
                t.Wobble += Dt;
                row[i] = t;
            }
        }
    }

    private void UpdateAim()
    {
        // Mouse hover, finger drag, or the keyboard / stick moving the sight.
        if (In.HasHover && In.PointerMoved && !IsTouch)
            _cross = In.Pointer;
        if (In.PointerDown && IsTouch)
            _cross = In.Pointer;
        _cross.X += In.AxisX * 300 * Dt;
        _cross.Y += In.AxisY * 300 * Dt;
        _cross.X = MathF2.Clamp(_cross.X, 6, 634);
        _cross.Y = MathF2.Clamp(_cross.Y, Screen.HudHeight + 6, 354);
    }

    private void UpdatePlay()
    {
        _timeLeft -= Dt;
        int secs = (int)MathF.Ceiling(_timeLeft);
        if (secs <= 5 && secs != _lastTick && secs > 0)
            Sound.Play(Sfx.Tick, 0.3f);
        _lastTick = secs;
        if (_timeLeft <= 0)
        {
            EndRound();
            return;
        }
        if (_double > 0)
            _double -= Dt;
        if (_cooldown > 0)
            _cooldown -= Dt;

        // Reload.
        if (_reload > 0)
        {
            int before = (int)((0.9f - _reload) / 0.9f * Magazine);
            _reload -= Dt;
            int after = (int)((0.9f - MathF.Max(0, _reload)) / 0.9f * Magazine);
            if (after != before)
                Sound.Play(Sfx.Click, 0.2f + after * 0.05f, 0.5f);
            if (_reload <= 0)
            {
                _ammo = Magazine;
                Sound.Play(Sfx.Card, 0, 0.8f);
            }
        }
        if ((Ui.Button(ReloadRect, "RELOAD", Keys.R, _ammo < Magazine && _reload <= 0, Pal.Darken(Pal.Brown, 0.2f)) || In.AltPressed)
            && _ammo < Magazine && _reload <= 0)
        {
            _reload = 0.9f;
            Sound.Play(Sfx.Shuffle, 0, 0.6f);
        }

        // Shooting.
        bool shoot = false;
        if (In.PointerPressed)
        {
            _cross = In.Pointer;
            shoot = true;
        }
        if (In.FirePressed)
            shoot = true;
        if (shoot && _cooldown <= 0)
        {
            if (_reload > 0)
            {
            }
            else if (_ammo <= 0)
            {
                Sound.Play(Sfx.Click, -0.5f);
                Flash("RELOAD!", Pal.Orange);
            }
            else
            {
                Shoot(_cross);
            }
        }

        // Pop-up bullseyes.
        _popTimer -= Dt;
        if (_popTimer <= 0)
        {
            _popTimer = Rand(2.2f, 4f) / (1 + 0.12f * (_round - 1));
            _pops.Add(new PopUp { X = Rand(60, 580), T = 0, Life = MathF.Max(1.1f, 2f - 0.15f * _round), Frog = Chance(0.3f) });
            Sound.Play(Sfx.Pop, 0.4f, 0.4f);
        }
        for (int i = _pops.Count - 1; i >= 0; i--)
        {
            var p = _pops[i];
            p.T += Dt;
            _pops[i] = p;
            if (p.T > p.Life + 0.6f)
                _pops.RemoveAt(i);
        }

        // Bonus wheel lights up now and then.
        if (_wheelLit > 0)
        {
            _wheelLit -= Dt;
        }
        else if ((_wheelTimer -= Dt) <= 0)
        {
            _wheelLit = 3.5f;
            _wheelTimer = Rand(7, 11);
            _wheelSpeed = Rand(2.5f, 4f) * (Chance(0.5f) ? 1 : -1);
            Sound.Play(Sfx.Bell, 0.5f, 0.6f);
        }
        if (_wheelLit <= 0)
            _wheelSpeed = MathF2.Approach(_wheelSpeed, 0.6f * MathF.Sign(_wheelSpeed == 0 ? 1 : _wheelSpeed), Dt);
    }

    private static float PopHeight(PopUp p)
    {
        float up = MathF.Min(1, p.T / 0.2f);
        float down = MathF.Max(0, (p.T - p.Life) / 0.3f);
        return MathF2.Clamp(up - down, 0, 1);
    }

    private static Vector2 PopCentre(PopUp p) => new(p.X, CounterY + 18 - 44 * PopHeight(p));

    private void Shoot(Vector2 at)
    {
        _ammo--;
        _shots++;
        _cooldown = 0.16f;
        _recoil = 1;
        Sound.Play(Sfx.Shoot, Rand(-0.1f, 0.1f), 0.8f);
        Fx.Burst(at.X, at.Y, Pal.White, 5, 40, 0.15f, 1.2f);
        int mult = _double > 0 ? 2 : 1;

        // Front to back: pop-ups, then the belts, then the wheel.
        for (int i = 0; i < _pops.Count; i++)
        {
            var p = _pops[i];
            if (p.Hit || PopHeight(p) < 0.5f)
                continue;
            var c = PopCentre(p);
            if (Vector2.Distance(c, at) < 17)
            {
                p.Hit = true;
                p.Life = MathF.Min(p.Life, p.T);
                _pops[i] = p;
                int pts = 50 * mult;
                if (Vector2.Distance(c, at) < 6)
                {
                    pts *= 2;
                    Flash("BULLSEYE!", Pal.Red);
                }
                Hit(pts, c, Pal.Red);
                Sound.Play(Sfx.Bell, 0.4f);
                return;
            }
        }
        for (int r = 2; r >= 0; r--)
        {
            var row = _rows[r];
            for (int i = 0; i < row.Count; i++)
            {
                var t = row[i];
                if (t.Hit)
                    continue;
                if (!TargetRect(r, t).Contains(at))
                    continue;
                t.Hit = true;
                row[i] = t;
                var col = t.Kind switch { Kind.Duck => Pal.Yellow, Kind.Rabbit => Pal.White, Kind.Star => Pal.Cyan, _ => Pal.Gold };
                Hit(Points(t.Kind) * mult, new Vector2(t.X, RailY[r] - 20), col);
                Sound.Play(Sfx.Bell, t.Kind == Kind.Star ? 0.6f : t.Kind == Kind.Rabbit ? 0.2f : -0.1f, 0.7f);
                if (t.Kind == Kind.Golden)
                {
                    Flash("GOLDEN DUCK!", Pal.Gold);
                    Sound.Play(Sfx.Coin);
                }
                return;
            }
        }
        if (Vector2.Distance(at, WheelPos) < WheelR)
        {
            if (_wheelLit > 0)
            {
                _wheelLit = 0;
                _wheelTimer = Rand(7, 11);
                float a = MathF2.WrapAngle(MathF.Atan2(at.Y - WheelPos.Y, at.X - WheelPos.X) - _wheelAngle);
                int seg = (int)(((a + MathF2.Tau) % MathF2.Tau) / (MathF2.Tau / 8)) % 8;
                _hits++;
                Sound.Play(Sfx.Bonus);
                switch (WheelLabels[seg])
                {
                    case "X2":
                        _double = 8;
                        Flash("DOUBLE POINTS!", Pal.Cyan);
                        break;
                    case "+8":
                        _ammo = Magazine;
                        _reload = 0;
                        Flash("FREE RELOAD!", Pal.Lime);
                        break;
                    default:
                        int pts = int.Parse(WheelLabels[seg]);
                        Hit(pts, WheelPos, WheelCols[seg]);
                        _hits--;
                        Flash("WHEEL PRIZE " + pts, Pal.Gold);
                        break;
                }
                Fx.Burst(WheelPos.X, WheelPos.Y, Pal.Gold, 30, 140, 0.8f);
            }
            else
            {
                Sound.Play(Sfx.Thud, 0.3f, 0.5f);
                AddHole(at);
            }
            return;
        }
        // A miss.
        if (at.Y < CounterY)
        {
            AddHole(at);
            Sound.Play(Sfx.Thud, Rand(0.2f, 0.5f), 0.4f);
        }
        else
        {
            Fx.Burst(at.X, at.Y, Pal.Sand, 8, 70, 0.4f, 1.5f, 200, false);
            Sound.Play(Sfx.Crack, 0.4f, 0.4f);
        }
    }

    private void AddHole(Vector2 at)
    {
        if (_holes.Count > 40)
            _holes.RemoveAt(0);
        _holes.Add(new Hole { Pos = at, Life = 1 });
    }

    private void Hit(int pts, Vector2 pos, Color col)
    {
        _hits++;
        _roundScore += pts;
        AddScore(pts, pos.X, pos.Y - 10, col);
        Fx.Burst(pos.X, pos.Y, col, 16, 110, 0.5f, 2f, 150);
        Fx.Burst(pos.X, pos.Y, Pal.White, 6, 50, 0.25f, 1.5f);
    }

    private void EndRound()
    {
        _phase = Phase.Summary;
        _phaseTime = 0;
        _timeLeft = 0;
        float acc = _shots == 0 ? 0 : _hits / (float)_shots;
        _bonus = (int)(acc * acc * 150 * _round);
        _bonus -= _bonus % 5;
        _roundScore += _bonus;
        Score += _bonus;
        Sound.Play(Sfx.Bell);
        Sound.Play(_roundScore >= Qualify ? Sfx.Correct : Sfx.Wrong, 0, 0.8f);
        _pops.Clear();
    }

    private static float ArtScale(int r) => r == 0 ? 2.2f : r == 1 ? 2.2f : 1;

    private static RectF TargetRect(int r, Target t)
    {
        float base_ = RailY[r];
        return t.Kind switch
        {
            Kind.Star => new RectF(t.X - 14, base_ - 32, 28, 30),
            Kind.Rabbit => new RectF(t.X - 12, base_ - 38, 24, 38),
            _ => new RectF(t.X - 18, base_ - 29, 36, 29),
        };
    }

    // ------------------------------------------------------------------ drawing

    public override void Draw(Gfx g)
    {
        DrawBooth(g, Screen.Bounds, Time);
        DrawWheel(g, WheelPos, WheelR, _wheelAngle, _wheelLit > 0, Time);

        foreach (var h in _holes)
        {
            g.Circle(h.Pos.X, h.Pos.Y, 2.2f, new Color(20, 6, 6) * h.Life);
            g.Circle(h.Pos.X - 0.6f, h.Pos.Y - 0.6f, 1, new Color(60, 30, 20) * h.Life);
        }

        for (int r = 0; r < 3; r++)
        {
            foreach (var t in _rows[r])
                DrawTarget(g, t.Kind, t.X, RailY[r], t.Down, _rowSpeed[r] < 0, t.Wobble, 1);
            DrawRail(g, RailY[r], _rowSpeed[r], Time);
        }

        // Pop-ups rise from behind the counter.
        foreach (var p in _pops)
        {
            var c = PopCentre(p);
            g.Rect(c.X - 2, c.Y, 4, CounterY + 40 - c.Y, new Color(90, 60, 30));
            if (p.Hit)
            {
                g.Ellipse(c.X, c.Y, 16, 4, Pal.DarkGrey);
                continue;
            }
            if (p.Frog)
            {
                g.PixelsCentered(Frog, c.X, c.Y, 2.6f);
                g.Glow(c.X, c.Y, 22, Pal.Lime, 0.25f);
            }
            else
            {
                DrawBullseye(g, c, 16);
            }
        }

        DrawCounter(g);

        // Crosshair.
        if (_phase == Phase.Play)
        {
            var c = _cross + new Vector2(0, -_recoil * 4);
            var col = _ammo > 0 && _reload <= 0 ? Pal.Lime : Pal.Red;
            g.Glow(c, 18, col, 0.25f);
            g.Ring(c.X, c.Y, 11, 1.6f, col);
            g.Rect(c.X - 17, c.Y - 0.8f, 10, 1.6f, col);
            g.Rect(c.X + 7, c.Y - 0.8f, 10, 1.6f, col);
            g.Rect(c.X - 0.8f, c.Y - 17, 1.6f, 10, col);
            g.Rect(c.X - 0.8f, c.Y + 7, 1.6f, 10, col);
            g.Circle(c.X, c.Y, 1.4f, Pal.White);
        }

        if (_phase == Phase.Ready)
        {
            g.Panel(new RectF(170, 120, 300, 90), Pal.Panel * 0.92f, Pal.Gold, 10);
            g.TextShadow("ROUND " + _round, 320, 132, 3, Pal.Gold, Align.Center);
            g.Text($"SCORE {Qualify} TO QUALIFY", 320, 168, 1.5f, Pal.White, Align.Center);
            g.Text("60 SECONDS", 320, 188, 1.5f, Pal.Cyan, Align.Center);
        }
        else if (_phase == Phase.Summary)
        {
            bool ok = _roundScore >= Qualify;
            float acc = _shots == 0 ? 0 : _hits * 100f / _shots;
            g.Panel(new RectF(160, 96, 320, 150), Pal.Panel * 0.94f, ok ? Pal.Gold : Pal.Red, 10);
            g.TextShadow("TIME UP!", 320, 108, 3, Pal.Gold, Align.Center);
            g.Text($"HITS {_hits} OF {_shots}  ({acc:0}%)", 320, 144, 1.5f, Pal.White, Align.Center);
            g.Text($"ACCURACY BONUS +{_bonus}", 320, 164, 1.5f, Pal.Cyan, Align.Center);
            g.Text($"ROUND SCORE {_roundScore} / {Qualify}", 320, 184, 1.5f, Pal.White, Align.Center);
            g.TextShadow(ok ? (_round >= Rounds ? "CHAMPION!" : "QUALIFIED!") : "NOT ENOUGH!", 320, 210, 2.5f, ok ? Pal.Lime : Pal.Red, Align.Center);
        }

        if (_flashTime > 0 && _flashText != null)
            g.TextShadow(_flashText, 320, 120, 2.5f, _flashColour * MathF.Min(1, _flashTime * 2), Align.Center);
    }

    private static void DrawBooth(Gfx g, RectF r, float time)
    {
        float s = r.H / 360f;
        // Back wall: deep red velvet with stripes.
        g.GradientV(r.X, r.Y, r.W, r.H, new Color(70, 10, 20), new Color(30, 4, 12));
        for (float x = r.X; x < r.Right; x += 32 * s)
            g.Rect(x, r.Y, 14 * s, r.H, Color.Black * 0.12f);
        g.Glow(r.CenterX, r.Y + r.H * 0.45f, r.W * 0.55f, Pal.Orange, 0.18f);

        // Striped canopy with scalloped edge.
        float bottom = r.Y + 50 * s;
        float sw = 40 * s;
        int i = 0;
        for (float x = r.X; x < r.Right; x += sw, i++)
        {
            var c = i % 2 == 0 ? new Color(220, 30, 40) : new Color(245, 240, 230);
            g.GradientV(x, r.Y, sw + 0.5f, bottom - r.Y, Pal.Darken(c, 0.25f), c);
            g.Pie(x + sw / 2, bottom, sw / 2, 0, MathF.PI, c);
        }
        g.Rect(r.X, bottom - 2 * s, r.W, 2 * s, Color.Black * 0.2f);
        // Light bulbs along the edge.
        int n = 0;
        for (float x = r.X + sw / 2; x < r.Right; x += sw, n++)
        {
            bool on = ((int)(time * 4) + n) % 3 != 0;
            float by = bottom + sw / 2 - 2 * s;
            g.Circle(x, by, 2.5f * s, on ? Pal.Yellow : new Color(120, 100, 40));
            if (on)
                g.Glow(x, by, 12 * s, Pal.Yellow, 0.6f);
        }
        // Bunting.
        for (int b = 0; b < 2; b++)
        {
            float x0 = b == 0 ? r.X : r.X + r.W * 0.58f, x1 = b == 0 ? r.X + r.W * 0.42f : r.Right;
            float sag = 14 * s, y0 = bottom + 24 * s;
            for (int k = 0; k < 9; k++)
            {
                float t0 = k / 9f, t1 = (k + 1) / 9f;
                float xa = MathF2.Lerp(x0, x1, t0), xb = MathF2.Lerp(x0, x1, t1);
                float ya = y0 + sag * 4 * t0 * (1 - t0), yb = y0 + sag * 4 * t1 * (1 - t1);
                g.Line(xa, ya, xb, yb, 1 * s, Pal.LightGrey * 0.6f);
                float tm = (t0 + t1) / 2, xm = (xa + xb) / 2, ym = y0 + sag * 4 * tm * (1 - tm);
                var fc = Pal.Rainbow[(k + b * 3) % 6];
                g.Triangle(new Vector2(xm - 6 * s, ym), new Vector2(xm + 6 * s, ym), new Vector2(xm, ym + 11 * s), Pal.Darken(fc, 0.15f));
            }
        }
    }

    private static void DrawWheel(Gfx g, Vector2 c, float rad, float angle, bool lit, float time, float textScale = 1)
    {
        if (lit)
            g.Glow(c, rad * 2.2f, Pal.Gold, 0.5f + 0.2f * MathF.Sin(time * 12));
        g.Circle(c.X, c.Y, rad + 4, new Color(60, 40, 20));
        float seg = MathF2.Tau / 8;
        for (int i = 0; i < 8; i++)
        {
            var col = WheelCols[i];
            if (!lit)
                col = Pal.Darken(col, 0.45f);
            g.Pie(c.X, c.Y, rad, angle + i * seg, angle + (i + 1) * seg, col);
        }
        for (int i = 0; i < 8; i++)
        {
            var p = c + MathF2.FromAngle(angle + (i + 0.5f) * seg, rad * 0.7f);
            if (textScale > 0)
                g.Text(WheelLabels[i], p.X, p.Y - 2.8f * textScale, textScale * 0.7f, Pal.Black, Align.Center);
        }
        for (int i = 0; i < 12; i++)
        {
            var p = c + MathF2.FromAngle(i * MathF2.Tau / 12, rad + 2);
            bool on = lit && ((int)(time * 10) + i) % 2 == 0;
            g.Circle(p.X, p.Y, 1.5f * rad / 25, on ? Pal.White : Pal.Gold * 0.6f);
        }
        g.Circle(c.X, c.Y, rad * 0.16f, Pal.Gold);
        // The pointer at the top.
        g.Triangle(new Vector2(c.X - 4 * rad / 25, c.Y - rad - 6 * rad / 25), new Vector2(c.X + 4 * rad / 25, c.Y - rad - 6 * rad / 25),
            new Vector2(c.X, c.Y - rad + 3 * rad / 25), Pal.White);
    }

    private static void DrawRail(Gfx g, float y, float speed, float time)
    {
        g.GradientV(0, y, 640, 9, new Color(170, 170, 185), new Color(70, 70, 85));
        g.Rect(0, y, 640, 1, Pal.White * 0.6f);
        float off = Backdrops.Mod(time * speed, 16);
        for (float x = -16 + off; x < 650; x += 16)
            g.Rect(x, y + 4, 6, 2, new Color(50, 50, 60));
        g.Rect(0, y + 9, 640, 3, Color.Black * 0.35f);
    }

    private static void DrawTarget(Gfx g, Kind kind, float x, float baseY, float down, bool flip, float wobble, float s)
    {
        if (x < -30 || x > 670)
            return;
        // Knocked targets fall backwards (squash towards the rail).
        float sy = MathF.Cos(down * MathF.PI / 2) * 0.9f + 0.1f;
        if (down >= 1)
            sy = 0.12f;
        float tilt = MathF.Sin(wobble * 6) * 0.6f;
        var tint = down > 0 ? 0.65f : 1f;
        switch (kind)
        {
            case Kind.Star:
            {
                float r = 13 * s;
                var c = new Vector2(x, baseY - (r + 4 * s) * sy);
                g.Rect(x - 1.5f * s, c.Y, 3 * s, baseY - c.Y, new Color(110, 110, 120));
                if (down <= 0)
                    g.Glow(c, r * 1.8f, Pal.Cyan, 0.3f);
                DrawStar(g, c, r, sy, wobble * 0.5f, Pal.Darken(Pal.Cyan, 0.3f) * tint);
                DrawStar(g, c, r * 0.7f, sy, wobble * 0.5f, Pal.Lighten(Pal.Cyan, 0.3f) * tint);
                break;
            }
            default:
            {
                var art = kind == Kind.Rabbit ? Rabbit : Duck;
                float px = (kind == Kind.Rabbit ? 2.2f : 2.25f) * s;
                Color? col = kind == Kind.Golden ? Pal.Gold : null;
                DrawArt(g, art, x + tilt * 0, baseY, px, px * sy, flip, col, tint);
                if (kind == Kind.Golden && down <= 0)
                    g.Glow(x, baseY - 14 * s, 26 * s, Pal.Gold, 0.45f);
                break;
            }
        }
    }

    private static void DrawArt(Gfx g, PixelArt art, float cx, float baseY, float px, float py, bool flipX, Color? tint, float shade)
    {
        float x0 = cx - art.Width * px / 2, y0 = baseY - art.Height * py;
        foreach (var run in art.Runs)
        {
            float rx = flipX ? art.Width - run.X - run.Length : run.X;
            var c = tint ?? run.Color;
            if (tint.HasValue && run.Color.R < 60)
                c = run.Color;
            g.Rect(x0 + rx * px, y0 + run.Y * py, run.Length * px + 0.02f, py + 0.02f, Pal.Darken(c, 1 - shade));
        }
    }

    private static readonly Vector2[] StarPts = new Vector2[10];

    private static void DrawStar(Gfx g, Vector2 c, float r, float sy, float spin, Color col)
    {
        for (int i = 0; i < 10; i++)
        {
            float a = -MathF.PI / 2 + i * MathF.PI / 5 + spin * 0;
            float rr = i % 2 == 0 ? r : r * 0.45f;
            StarPts[i] = new Vector2(c.X + MathF.Cos(a) * rr, c.Y + MathF.Sin(a) * rr * sy);
        }
        for (int i = 0; i < 10; i++)
            g.Triangle(c, StarPts[i], StarPts[(i + 1) % 10], col);
    }

    private static void DrawBullseye(Gfx g, Vector2 c, float r)
    {
        g.Circle(c.X, c.Y, r + 1.5f, new Color(60, 30, 10));
        g.Circle(c.X, c.Y, r, Pal.White);
        g.Circle(c.X, c.Y, r * 0.75f, Pal.Red);
        g.Circle(c.X, c.Y, r * 0.5f, Pal.White);
        g.Circle(c.X, c.Y, r * 0.25f, Pal.Red);
    }

    private void DrawCounter(Gfx g)
    {
        g.GradientV(0, CounterY - 6, 640, 8, new Color(190, 120, 60), new Color(140, 80, 35));
        g.GradientV(0, CounterY + 2, 640, 70, new Color(120, 70, 30), new Color(60, 32, 14));
        for (int i = 0; i < 5; i++)
            g.Rect(0, CounterY + 14 + i * 14, 640, 1, Color.Black * 0.25f);
        for (int i = 0; i < 9; i++)
            g.Rect(30 + i * 73, CounterY + 2, 1, 70, Color.Black * 0.2f);
        g.Rect(0, CounterY - 6, 640, 1.5f, Pal.Sand * 0.6f);

        // Ammo on the counter.
        var box = new RectF(10, 314, 190, 40);
        g.Panel(box, new Color(40, 24, 10) * 0.9f, Pal.Gold * 0.6f, 6);
        float filled = _reload > 0 ? (0.9f - _reload) / 0.9f * Magazine : _ammo;
        for (int i = 0; i < Magazine; i++)
        {
            float x = box.X + 12 + i * 22, y = box.Y + 8;
            bool have = i < filled;
            if (have)
            {
                g.Rect(x, y + 8, 10, 18, new Color(200, 150, 50));
                g.Rect(x + 1, y + 8, 3, 18, Pal.Gold);
                g.RoundRect(x + 1, y, 8, 11, 4, new Color(170, 100, 60));
                g.Rect(x - 1, y + 24, 12, 3, new Color(150, 110, 40));
            }
            else
            {
                g.RoundRect(x, y + 4, 10, 22, 3, Color.Black * 0.35f);
            }
        }
        if (_reload > 0)
            g.TextShadow("RELOADING", box.CenterX, box.Y - 12, 1.5f, Pal.Orange, Align.Center);
        else if (_ammo == 0 && _phase == Phase.Play && (int)(Time * 4) % 2 == 0)
            g.TextShadow(IsTouch ? "TAP RELOAD" : "PRESS X TO RELOAD", box.CenterX, box.Y - 12, 1.5f, Pal.Red, Align.Center);

        // Timer and round score.
        var clock = new RectF(232, 312, 176, 42);
        g.Panel(clock, Pal.Panel * 0.9f, Pal.Gold * 0.7f, 6);
        int secs = (int)MathF.Ceiling(MathF.Max(0, _timeLeft));
        g.Text("TIME", clock.X + 10, clock.Y + 8, 1, Pal.LightGrey);
        g.Text(secs.ToString("00"), clock.X + 10, clock.Y + 18, 2.5f, secs <= 10 ? Pal.Red : Pal.White);
        g.Text("ROUND", clock.Right - 10, clock.Y + 8, 1, Pal.LightGrey, Align.Right);
        g.Text($"{_roundScore}/{Qualify}", clock.Right - 10, clock.Y + 20, 1.5f, _roundScore >= Qualify ? Pal.Lime : Pal.Yellow, Align.Right);
        if (_double > 0)
            g.TextShadow("X2", clock.CenterX + 8, clock.Y - 14, 1.5f, Pal.Cyan, Align.Center);
    }

    // ------------------------------------------------------------------ icon

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        float s = r.H / 70f;
        DrawBooth(g, r, time);
        float speed = 30 * s;
        for (int i = 0; i < 5; i++)
        {
            float x = r.X + Backdrops.Mod(i * 34 * s + time * speed, r.W + 40 * s) - 20 * s;
            float down = i == 2 ? MathF2.Clamp((time % 3) - 1.5f, 0, 1) * 1 : 0;
            DrawTarget(g, i % 3 == 1 ? Kind.Golden : Kind.Duck, x, r.Y + r.H * 0.86f, down, false, time + i, s * 0.6f);
        }
        g.GradientV(r.X, r.Y + r.H * 0.86f, r.W, 3 * s, new Color(170, 170, 185), new Color(70, 70, 85));
        g.GradientV(r.X, r.Y + r.H * 0.86f + 3 * s, r.W, r.H, new Color(120, 70, 30), new Color(60, 32, 14));
        DrawWheel(g, new Vector2(r.X + r.W * 0.82f, r.Y + r.H * 0.5f), 12 * s, time * 2, true, time, 0);
        DrawBullseye(g, new Vector2(r.X + r.W * 0.18f, r.Y + r.H * 0.52f), 8 * s);
        // Crosshair sweeping.
        var c = new Vector2(r.CenterX + MathF.Sin(time * 1.1f) * r.W * 0.25f, r.Y + r.H * 0.62f + MathF.Sin(time * 1.7f) * r.H * 0.08f);
        g.Glow(c, 12 * s, Pal.Lime, 0.3f);
        g.Ring(c.X, c.Y, 7 * s, 1.2f * s, Pal.Lime);
        g.Rect(c.X - 11 * s, c.Y - 0.6f * s, 7 * s, 1.2f * s, Pal.Lime);
        g.Rect(c.X + 4 * s, c.Y - 0.6f * s, 7 * s, 1.2f * s, Pal.Lime);
        g.Rect(c.X - 0.6f * s, c.Y - 11 * s, 1.2f * s, 7 * s, Pal.Lime);
        g.Rect(c.X - 0.6f * s, c.Y + 4 * s, 1.2f * s, 7 * s, Pal.Lime);
    }

    // ------------------------------------------------------------------ autopilot

    private Vector2 _autoAim;
    private float _autoWait;

    public override void AutoPlay(Controls c)
    {
        if (_phase != Phase.Play)
            return;
        if (_ammo == 0 && _reload <= 0)
        {
            c.AltPressed = true;
            c.Alt = true;
            return;
        }
        // Pick the best-value target we can reach soon.
        float best = float.MaxValue;
        bool found = false;
        const float sight = 300;
        if (_wheelLit > 0.5f)
        {
            best = Vector2.Distance(_cross, WheelPos) / sight - 0.6f;
            _autoAim = WheelPos + new Vector2(0, -12);
            found = true;
        }
        foreach (var p in _pops)
        {
            if (p.Hit || p.T > p.Life - 0.4f)
                continue;
            var pc = new Vector2(p.X, CounterY + 18 - 44);
            float cost = Vector2.Distance(_cross, pc) / sight - 0.4f;
            if (cost < best)
            {
                best = cost;
                _autoAim = pc;
                found = true;
            }
        }
        for (int r = 0; r < 3; r++)
            foreach (var t in _rows[r])
            {
                if (t.Hit)
                    continue;
                var rect = TargetRect(r, t);
                var tc = new Vector2(rect.CenterX, rect.CenterY);
                float tt = Vector2.Distance(_cross, tc) / sight + 0.1f;
                var pred = tc + new Vector2(_rowSpeed[r] * tt, 0);
                if (pred.X < 20 || pred.X > 620)
                    continue;
                float cost = tt - Points(t.Kind) * 0.004f;
                if (cost < best)
                {
                    best = cost;
                    _autoAim = pred;
                    found = true;
                }
            }
        if (!found)
            return;
        var d = _autoAim - _cross;
        float len = d.Length();
        if (len > 3)
        {
            var dir = d / len * MathF.Min(1, len / 12);
            c.SetDirections(dir.X, dir.Y);
        }
        _autoWait -= Dt;
        if (len < 7 && _cooldown <= 0 && _autoWait <= 0)
        {
            c.FirePressed = true;
            c.Fire = true;
            _autoWait = Rand(0.15f, 0.45f);
        }
    }
}
