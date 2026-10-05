using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 23 Motorway: guide a hedgehog across six lanes of evening motorway traffic, then over a river
/// on drifting logs and narrowboats, into the five cosy hollows in the hedge at the top.
/// </summary>
public sealed class Motorway : MiniGame
{
    public override int Number => 23;
    public override string Title => "Motorway";
    public override Category Category => Category.Arcade;
    public override string Tagline => "Help a hedgehog across a busy motorway and a river to safety.";
    public override Color Accent => Pal.Orange;

    public override string[] HowToPlay =>
    [
        "Hop across six lanes of traffic, then ride the logs and boats over the river. Don't fall in!",
        "Fill all five hollows in the hedge to clear a level. Beat the clock for a time bonus.",
        "Beetles score 200. Each level adds traffic.",
    ];

    public override string[] DesktopControls => ["ARROWS / WASD to hop.", "Or click beside the hedgehog."];
    public override string[] TouchControls => ["Stick, swipe or tap beside the hedgehog to hop."];
    public override Pad Pad => Pad.Stick;

    // ------------------------------------------------------------------ layout

    private const float RowH = 21, Top = 24;
    private const int Rows = 16, StartRow = 15, HomeRow = 0;
    private const float HopTime = 0.13f;
    private static readonly float[] BayX = [64, 192, 320, 448, 576];

    private static float RowY(int r) => Top + r * RowH + RowH / 2;

    private enum RowKind { Home, River, Bank, Shoulder, Road, Reservation, Verge }

    private static RowKind KindOf(int r) => r switch
    {
        0 => RowKind.Home,
        <= 4 => RowKind.River,
        5 => RowKind.Bank,
        6 or 14 => RowKind.Shoulder,
        10 => RowKind.Reservation,
        15 => RowKind.Verge,
        _ => RowKind.Road,
    };

    // ------------------------------------------------------------------ art

    private static readonly Color HogDark = new(70, 46, 28), HogSpine = new(186, 146, 98), HogTip = new(236, 220, 190),
        HogFace = new(226, 186, 136), HogFeet = new(120, 80, 50);

    /// <summary>A vector hedgehog. <paramref name="face"/>: 0 up, 1 down, 2 left, 3 right.</summary>
    private static void DrawHedgehog(Gfx g, float x, float y, float k, int face, bool curled, float t)
    {
        Vector2 P(float lx, float ly) => face switch
        {
            1 => new Vector2(x - lx * k, y - ly * k),
            2 => new Vector2(x + ly * k, y - lx * k),
            3 => new Vector2(x - ly * k, y + lx * k),
            _ => new Vector2(x + lx * k, y + ly * k),
        };
        bool side = face >= 2;
        void Oval(float lx, float ly, float rx, float ry, Color c)
        {
            var p = P(lx, ly);
            g.Ellipse(p.X, p.Y, (side ? ry : rx) * k, (side ? rx : ry) * k, c);
        }
        if (curled)
        {
            g.Circle(x, y, 7.5f * k, HogDark);
            for (int i = 0; i < 20; i++)
            {
                float a = i / 20f * MathF2.Tau + t * 6;
                var d = MathF2.FromAngle(a);
                g.Line(new Vector2(x, y) + d * 3.5f * k, new Vector2(x, y) + d * 8.5f * k, 1.4f * k, i % 2 == 0 ? HogSpine : Pal.Darken(HogSpine, 0.2f));
                g.Circle(x + d.X * 8.5f * k, y + d.Y * 8.5f * k, 0.6f * k, HogTip);
            }
            g.Circle(x, y, 3.5f * k, Pal.Darken(HogDark, 0.1f));
            return;
        }
        // Feet, body, spines, face.
        Oval(-6.3f, -2.5f, 1.6f, 1.6f, HogFeet);
        Oval(6.3f, -2.5f, 1.6f, 1.6f, HogFeet);
        Oval(-5.4f, 8.2f, 1.6f, 1.6f, HogFeet);
        Oval(5.4f, 8.2f, 1.6f, 1.6f, HogFeet);
        Oval(0, 2, 7, 8.4f, HogDark);
        for (int ring = 0; ring < 2; ring++)
            for (int i = 0; i < 13; i++)
            {
                float a = MathF2.Lerp(-2.5f, 2.5f, i / 12f) + ring * 0.12f;
                float r0 = ring == 0 ? 0.35f : 0.65f, r1 = ring == 0 ? 0.85f : 1.12f;
                var b0 = P(MathF.Sin(a) * 6.6f * r0, 2 + MathF.Cos(a) * 8 * r0);
                var b1 = P(MathF.Sin(a) * 6.6f * r1, 2.6f + MathF.Cos(a) * 8 * r1);
                g.Line(b0, b1, 1.3f * k, (i + ring) % 2 == 0 ? HogSpine : Pal.Darken(HogSpine, 0.18f));
                if (ring == 1)
                    g.Circle(b1.X, b1.Y, 0.55f * k, HogTip);
            }
        Oval(-3, -4.6f, 1.3f, 1.3f, Pal.Darken(HogFace, 0.25f));
        Oval(3, -4.6f, 1.3f, 1.3f, Pal.Darken(HogFace, 0.25f));
        Oval(0, -6, 3.5f, 3.8f, HogFace);
        g.Triangle(P(-2.3f, -7.6f), P(2.3f, -7.6f), P(0, -11.6f), HogFace);
        var nose = P(0, -11.4f);
        g.Circle(nose.X, nose.Y, 1.2f * k, Pal.Black);
        var e1 = P(-1.7f, -6.8f);
        var e2 = P(1.7f, -6.8f);
        g.Circle(e1.X, e1.Y, 0.85f * k, Pal.Black);
        g.Circle(e2.X, e2.Y, 0.85f * k, Pal.Black);
    }

    private static readonly Dictionary<char, Color> FoxCols = new()
    {
        ['o'] = new Color(230, 110, 30), ['w'] = new Color(250, 240, 230), ['k'] = new Color(20, 14, 10), ['d'] = new Color(150, 60, 20),
    };

    private static readonly PixelArt Fox = new(
    [
        "d.........d",
        "do.......od",
        "ooo.....ooo",
        ".ooooooooo.",
        ".ookoookoo.",
        "..ooooooo..",
        "..wwoooww..",
        "...wwkww...",
        "....www....",
    ], FoxCols);

    private static readonly Dictionary<char, Color> BugCols = new()
    {
        ['g'] = new Color(60, 220, 120), ['G'] = new Color(20, 120, 70), ['k'] = new Color(20, 20, 20), ['w'] = Color.White,
    };

    private static readonly PixelArt Beetle = new(
    [
        "k.....k",
        ".k.k.k.",
        "..kkk..",
        ".gGgGg.",
        "kgggGgk",
        ".gGgGg.",
        "kgggGgk",
        ".gGgGg.",
        "k.ggg.k",
    ], BugCols);

    private static readonly Color[] CarColours =
    [
        new(220, 40, 50), new(40, 110, 230), new(240, 200, 40), new(240, 240, 240), new(30, 160, 90),
        new(150, 60, 200), new(250, 130, 30), new(60, 60, 70), new(120, 200, 230), new(170, 30, 40),
    ];

    // ------------------------------------------------------------------ state

    private enum VType { Car, Hatch, Van, Lorry, Coach, Bike, Caravan, Log, Boat }

    private sealed class Mover
    {
        public float X, Len;
        public VType Type;
        public Color Col, Col2;
        public bool Sinks;
        public float Phase;
    }

    private sealed class Lane
    {
        public int Row;
        public float BaseSpeed, Speed;
        public bool River;
        public float Spawn;
        public readonly List<Mover> Items = new();
    }

    private readonly Lane[] _lanes = new Lane[Rows];
    private readonly bool[] _homes = new bool[5];

    private float _hogX, _hogY;          // drawn position
    private int _hogRow;
    private float _fromX, _fromY, _toX, _toY, _hopT;
    private bool _hopping;
    private int _face;                   // 0 up, 1 down, 2 left, 3 right
    private float _holdTimer;
    private int _bestRow;
    private float _timeLeft, _timeMax;
    private float _dead;                 // > 0 while the death animation plays
    private string _deathText;
    private int _deathKind;              // 0 squashed, 1 splash, 2 other
    private float _banner;
    private string _bannerText;
    private int _bugBay = -1;
    private float _bugTimer, _bugLife;
    private int _foxBay = -1;
    private float _foxTimer, _foxLife;
    private float _homeFlash;
    private int _lastHome = -1;

    protected override void Start()
    {
        Lives = 3;
        Level = 1;
        for (int r = 0; r < Rows; r++)
            _lanes[r] = null;
        // Top carriageway runs east (right), the lower one west, as on a British motorway.
        MakeLane(1, 46, true);
        MakeLane(2, -34, true);
        MakeLane(3, 56, true);
        MakeLane(4, -42, true);
        MakeLane(7, 48, false);   // lane 1 (slow): lorries
        MakeLane(8, 80, false);   // lane 2
        MakeLane(9, 118, false);  // lane 3 (fast)
        MakeLane(11, -124, false);
        MakeLane(12, -84, false);
        MakeLane(13, -52, false);
        ApplyLevel();
        for (int i = 0; i < 1500; i++)
            UpdateLanes(Dt);
        _bugTimer = 6;
        _foxTimer = 10;
        Respawn();
        Banner("LEVEL 1");
    }

    private void MakeLane(int row, float speed, bool river) =>
        _lanes[row] = new Lane { Row = row, BaseSpeed = speed, River = river, Spawn = 0 };

    private void ApplyLevel()
    {
        float k = 1 + MathF.Min(Level - 1, 8) * 0.11f;
        foreach (var l in _lanes)
            if (l != null)
                l.Speed = l.BaseSpeed * (l.River ? 1 + (k - 1) * 0.6f : k);
        _timeMax = MathF.Max(24, 42 - (Level - 1) * 2.5f);
    }

    private void Respawn()
    {
        _hogRow = StartRow;
        _hogX = _toX = _fromX = 320;
        _hogY = _toY = _fromY = RowY(StartRow);
        _hopping = false;
        _face = 0;
        _bestRow = StartRow;
        _timeLeft = _timeMax;
        _dead = 0;
    }

    private void Banner(string text)
    {
        _bannerText = text;
        _banner = 2f;
    }

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        if (_banner > 0)
            _banner -= Dt;
        if (_homeFlash > 0)
            _homeFlash -= Dt;
        UpdateLanes(Dt);
        UpdateBays();

        // Engine rumble rises with the traffic.
        Sound.Loop(LoopSfx.Engine, true, -0.7f + 0.05f * MathF.Sin(Time * 0.7f), 0.12f);

        if (_dead > 0)
        {
            _dead -= Dt;
            if (_dead <= 0 && !IsOver)
                Respawn();
            return;
        }

        _timeLeft -= Dt;
        if (_timeLeft <= 0)
        {
            Die("Out of time!", 2);
            return;
        }
        if (_timeLeft < 6 && (int)(_timeLeft * 2) != (int)((_timeLeft + Dt) * 2))
            Sound.Play(Sfx.Tick, 0.4f, 0.5f);

        HandleInput();

        if (_hopping)
        {
            _hopT += Dt;
            float t = MathF.Min(1, _hopT / HopTime);
            _hogX = MathF2.Lerp(_fromX, _toX, t);
            _hogY = MathF2.Lerp(_fromY, _toY, t);
            if (t >= 1)
            {
                _hopping = false;
                Landed();
                if (_dead > 0 || IsOver)
                    return;
            }
        }

        // Riding something on the river.
        var kind = KindOf(_hogRow);
        if (!_hopping && kind == RowKind.River)
        {
            var lane = _lanes[_hogRow];
            var under = PlatformAt(lane, _hogX, 3);
            if (under == null)
            {
                Die("Splash! Hedgehogs can't swim.", 1);
                return;
            }
            _hogX += lane.Speed * Dt;
            _toX = _hogX;
            if (_hogX < 6 || _hogX > 634)
            {
                Die("Swept away!", 1);
                return;
            }
        }

        // Traffic.
        int rowNow = _hopping ? (MathF.Abs(_hogY - _fromY) < MathF.Abs(_hogY - _toY) ? RowOf(_fromY) : RowOf(_toY)) : _hogRow;
        if (KindOf(rowNow) == RowKind.Road && HitByTraffic(rowNow, _hogX))
            Die(Pick("Squashed!", "Splat!", "Flattened!"), 0);
    }

    private static int RowOf(float y) => Math.Clamp((int)((y - Top) / RowH), 0, Rows - 1);

    private void HandleInput()
    {
        int dx = 0, dy = 0;
        if (In.UpPressed) dy = -1;
        else if (In.DownPressed) dy = 1;
        else if (In.LeftPressed) dx = -1;
        else if (In.RightPressed) dx = 1;

        // Holding a direction keeps hopping.
        bool held = In.Up || In.Down || In.Left || In.Right;
        if (held && dx == 0 && dy == 0)
        {
            _holdTimer += Dt;
            if (_holdTimer > 0.3f)
            {
                _holdTimer = 0.14f;
                if (In.Up) dy = -1;
                else if (In.Down) dy = 1;
                else if (In.Left) dx = -1;
                else dx = 1;
            }
        }
        else if (!held)
        {
            _holdTimer = 0;
        }

        // Swipe, or tap beside the hedgehog.
        if (In.PointerReleased && dx == 0 && dy == 0)
        {
            var d = In.Pointer - In.PointerStart;
            if (d.Length() < 16)
                d = In.Pointer - new Vector2(_hogX, _hogY);
            if (d.Length() > 6)
            {
                if (MathF.Abs(d.X) > MathF.Abs(d.Y)) dx = MathF.Sign(d.X);
                else dy = MathF.Sign(d.Y);
            }
        }

        if ((dx != 0 || dy != 0) && !_hopping)
            Hop(dx, dy);
    }

    private void Hop(int dx, int dy)
    {
        int nr = _hogRow + dy;
        if (nr < 0 || nr >= Rows)
            return;
        float nx = MathF2.Clamp(_hogX + dx * RowH, 10, 630);
        _face = dy < 0 ? 0 : dy > 0 ? 1 : dx < 0 ? 2 : 3;
        _fromX = _hogX;
        _fromY = _hogY;
        _toX = nx;
        _toY = RowY(nr);
        _hogRow = nr;
        _hopT = 0;
        _hopping = true;
        Sound.Play(Sfx.Jump, 0.3f + Rand(-0.1f, 0.1f), 0.35f);
    }

    private void Landed()
    {
        var kind = KindOf(_hogRow);
        if (kind == RowKind.Home)
        {
            int bay = -1;
            for (int i = 0; i < 5; i++)
                if (MathF.Abs(_hogX - BayX[i]) < 15)
                    bay = i;
            if (bay < 0)
            {
                Die("Ouch! Prickly hedge.", 2);
                return;
            }
            if (_homes[bay])
            {
                Die("That hollow is taken!", 2);
                return;
            }
            if (bay == _foxBay)
            {
                Die("A fox was waiting!", 2);
                return;
            }
            ReachHome(bay);
            return;
        }
        if (_hogRow < _bestRow)
        {
            _bestRow = _hogRow;
            AddScore(10);
        }
        if (kind == RowKind.River)
            Sound.Play(Sfx.Land, 0.2f, 0.3f);
        else
            Sound.Play(Sfx.Step, 0.5f, 0.25f);
    }

    private void ReachHome(int bay)
    {
        _homes[bay] = true;
        _lastHome = bay;
        _homeFlash = 1f;
        int bonus = 50 + (int)_timeLeft * 10;
        if (bay == _bugBay)
        {
            bonus += 200;
            _bugBay = -1;
            Sound.Play(Sfx.Pickup);
            Fx.Float("BEETLE!", BayX[bay], RowY(0) + 20, Pal.Lime);
        }
        AddScore(bonus, BayX[bay], RowY(0) + 8, Pal.Yellow);
        Fx.Burst(BayX[bay], RowY(0), Pal.Gold, 24, 90, 0.7f, 2f);
        Sound.Play(Sfx.Coin);

        bool all = true;
        foreach (bool h in _homes)
            all &= h;
        if (all)
        {
            int levelBonus = 500 + 250 * Level;
            AddScore(levelBonus, 320, 180, Pal.Cyan);
            Sound.Play(Sfx.LevelUp);
            for (int i = 0; i < 5; i++)
            {
                _homes[i] = false;
                Fx.Burst(BayX[i], RowY(0), Pal.Rainbow[i], 20, 120, 0.9f, 2.5f);
            }
            Level++;
            ApplyLevel();
            Banner("LEVEL " + Level);
            _foxBay = -1;
        }
        Respawn();
    }

    private void Die(string text, int kind)
    {
        _deathText = text;
        _deathKind = kind;
        _dead = 1.3f;
        _hopping = false;
        if (kind == 0)
        {
            Fx.Burst(_hogX, _hogY, new Color(196, 154, 104), 26, 140, 0.6f, 2f);
            Fx.Burst(_hogX, _hogY, Pal.White, 10, 80, 0.3f, 2f);
            Sound.Play(Sfx.Hit);
            Sound.Play(Sfx.Alarm, 0.6f, 0.25f); // horn
        }
        else if (kind == 1)
        {
            Fx.Burst(_hogX, _hogY, Pal.Sky, 24, 90, 0.7f, 2f, 160);
            Fx.Burst(_hogX, _hogY, Pal.White, 10, 60, 0.4f, 1.5f, 120);
            Sound.Play(Sfx.Splash);
        }
        else
        {
            Fx.Burst(_hogX, _hogY, Pal.Orange, 16, 70, 0.6f, 2f);
            Sound.Play(Sfx.Hurt);
        }
        Fx.Float(text, 320, 190, Pal.White, 2f);
        Fx.Shake(4, 0.3f);
        Lives--;
        if (Lives <= 0)
        {
            Lives = 0;
            EndGame(false, text);
        }
        else
        {
            Sound.Play(Sfx.Die, 0, 0.6f);
        }
    }

    // ------------------------------------------------------------------ lanes

    private void UpdateLanes(float dt)
    {
        foreach (var lane in _lanes)
        {
            if (lane == null)
                continue;
            for (int i = lane.Items.Count - 1; i >= 0; i--)
            {
                var m = lane.Items[i];
                m.X += lane.Speed * dt;
                m.Phase += dt;
                if ((lane.Speed > 0 && m.X - m.Len / 2 > 660) || (lane.Speed < 0 && m.X + m.Len / 2 < -20))
                    lane.Items.RemoveAt(i);
            }
            lane.Spawn -= dt;
            if (lane.Spawn <= 0)
                TrySpawn(lane);
        }
    }

    private void TrySpawn(Lane lane)
    {
        var m = NewMover(lane);
        float x = lane.Speed > 0 ? -m.Len / 2 - 4 : 644 + m.Len / 2;
        float gap = lane.River ? Rand(26, 70) : Rand(34, 60);
        foreach (var o in lane.Items)
        {
            float d = MathF.Abs(o.X - x) - (o.Len + m.Len) / 2;
            if (d < gap)
            {
                lane.Spawn = 0.1f;
                return;
            }
        }
        m.X = x;
        lane.Items.Add(m);
        float density = 1 + MathF.Min(Level - 1, 8) * 0.14f;
        lane.Spawn = lane.River ? Rand(0.4f, 1.6f) : Rand(0.8f, 3.0f) / density;
    }

    private Mover NewMover(Lane lane)
    {
        var m = new Mover { Phase = Rand(0, 10) };
        switch (lane.Row)
        {
            case 1:
            case 4:
                m.Type = VType.Log;
                m.Len = RowH * RandInt(3, 6);
                break;
            case 2:
                m.Type = VType.Boat;
                m.Len = RandInt(5, 8) * RowH;
                m.Col = Pick(new Color(30, 110, 60), new Color(150, 30, 40), new Color(30, 60, 140), new Color(110, 30, 110));
                m.Col2 = Pick(Pal.Gold, new Color(240, 220, 170), Pal.Orange);
                break;
            case 3:
                m.Type = VType.Log;
                m.Len = RowH * RandInt(2, 4);
                m.Sinks = Level >= 2 && Chance(0.3f + Level * 0.05f);
                break;
            default:
                bool slow = lane.Row == 7 || lane.Row == 13;
                bool fast = lane.Row == 9 || lane.Row == 11;
                float roll = Rand(0, 1);
                if (slow)
                    m.Type = roll < 0.45f ? VType.Lorry : roll < 0.6f ? VType.Caravan : roll < 0.75f ? VType.Coach : roll < 0.9f ? VType.Van : VType.Car;
                else if (fast)
                    m.Type = roll < 0.2f ? VType.Bike : roll < 0.7f ? VType.Car : VType.Hatch;
                else
                    m.Type = roll < 0.4f ? VType.Car : roll < 0.65f ? VType.Hatch : roll < 0.85f ? VType.Van : VType.Coach;
                m.Len = m.Type switch
                {
                    VType.Car => 32, VType.Hatch => 27, VType.Van => 38, VType.Lorry => Rand(70, 88),
                    VType.Coach => 68, VType.Bike => 17, VType.Caravan => 62, _ => 30,
                };
                m.Col = CarColours[RandInt(0, CarColours.Length)];
                m.Col2 = CarColours[RandInt(0, CarColours.Length)];
                break;
        }
        return m;
    }

    private static Mover PlatformAt(Lane lane, float x, float margin)
    {
        foreach (var m in lane.Items)
            if (MathF.Abs(m.X - x) < m.Len / 2 - margin && !Submerged(m))
                return m;
        return null;
    }

    private static bool Submerged(Mover m) => m.Sinks && MathF.Sin(m.Phase * 1.1f) < -0.55f;

    private bool HitByTraffic(int row, float x)
    {
        var lane = _lanes[row];
        if (lane == null)
            return false;
        foreach (var m in lane.Items)
            if (MathF.Abs(m.X - x) < m.Len / 2 + 6)
                return true;
        return false;
    }

    private void UpdateBays()
    {
        if (_bugBay >= 0)
        {
            _bugLife -= Dt;
            if (_bugLife <= 0)
                _bugBay = -1;
        }
        else if ((_bugTimer -= Dt) <= 0)
        {
            int b = RandInt(0, 5);
            if (!_homes[b] && b != _foxBay)
            {
                _bugBay = b;
                _bugLife = 6;
            }
            _bugTimer = Rand(7, 13);
        }
        if (Level < 3)
            return;
        if (_foxBay >= 0)
        {
            _foxLife -= Dt;
            if (_foxLife <= 0)
                _foxBay = -1;
        }
        else if ((_foxTimer -= Dt) <= 0)
        {
            int b = RandInt(0, 5);
            if (!_homes[b] && b != _bugBay)
            {
                _foxBay = b;
                _foxLife = Rand(3, 5);
                Sound.Play(Sfx.Beep, -0.5f, 0.3f);
            }
            _foxTimer = Rand(6, 11);
        }
    }

    // ------------------------------------------------------------------ drawing

    public override void Draw(Gfx g)
    {
        DrawScenery(g, Time);
        DrawBays(g);

        // River craft.
        for (int r = 1; r <= 4; r++)
            foreach (var m in _lanes[r].Items)
                DrawFloater(g, m, RowY(r), Time);

        // Hedgehog (on a log it rides above the water; on the road it goes under the traffic glow).
        bool drawHog = !(_dead > 0) || (_deathKind == 2 && (int)(_dead * 10) % 2 == 0);
        if (drawHog && _hogRow <= 5)
            DrawHog(g);

        // Traffic.
        for (int r = 7; r <= 13; r++)
        {
            var lane = _lanes[r];
            if (lane == null)
                continue;
            foreach (var m in lane.Items)
                DrawVehicle(g, m.Type, m.X, RowY(r), m.Len, MathF.Sign(lane.Speed), m.Col, m.Col2);
        }
        if (drawHog && _hogRow > 5)
            DrawHog(g);

        if (_dead > 0 && _deathKind == 0)
        {
            // A flattened hedgehog silhouette.
            g.Ellipse(_hogX, _hogY, 12, 5, new Color(80, 52, 30) * MathF.Min(1, _dead));
            for (int i = 0; i < 7; i++)
                g.Line(_hogX - 10 + i * 3.3f, _hogY - 3, _hogX - 11 + i * 3.6f, _hogY - 8, 1.2f, new Color(196, 154, 104) * MathF.Min(1, _dead));
        }
        else if (_dead > 0 && _deathKind == 1)
        {
            float k = 1.3f - _dead;
            for (int i = 0; i < 3; i++)
                g.Ring(_hogX, _hogY, 4 + (k * 30 + i * 6), 1.5f, Pal.Ice * MathF.Max(0, 0.8f - k * 0.6f));
        }

        // Timer bar along the bottom.
        float frac = MathF2.Clamp(_timeLeft / _timeMax, 0, 1);
        var tc = frac > 0.35f ? Pal.Lime : frac > 0.15f ? Pal.Yellow : Pal.Red;
        g.Rect(440, 349, 190, 7, Color.Black * 0.6f);
        g.Rect(441, 350, 188 * frac, 5, tc);
        g.Glow(441 + 188 * frac, 352, 10, tc, 0.6f);
        g.Text("TIME", 434, 348, 1f, Pal.White * 0.85f, Align.Right);

        if (_banner > 0)
        {
            float a = MathF.Min(1, _banner);
            g.TextShadow(_bannerText, 320, 170, 3f, Pal.Yellow * a, Align.Center);
        }
    }

    private void DrawBays(Gfx g)
    {
        float y = RowY(0);
        for (int i = 0; i < 5; i++)
        {
            float x = BayX[i];
            bool flash = i == _lastHome && _homeFlash > 0;
            g.Ellipse(x, y + 1, 17, 10, new Color(8, 26, 12));
            g.Ellipse(x, y + 3, 14, 7, new Color(52, 36, 22));
            for (int k = 0; k < 6; k++)
                g.Circle(x - 10 + k * 4, y + 6 + (k % 2), 1.6f, k % 2 == 0 ? new Color(170, 90, 30) : new Color(200, 140, 40));
            if (_homes[i])
            {
                g.Glow(x, y, 22, Pal.Gold, flash ? 0.6f : 0.2f);
                DrawHedgehog(g, x, y + 1, 0.9f, 0, true, 0);
                float zt = Backdrops.Mod(Time * 0.7f + i * 0.3f, 1);
                g.Text("z", x + 9 + zt * 5, y - 6 - zt * 8, 1f, Pal.White * (1 - zt));
            }
            else if (i == _foxBay)
            {
                g.Glow(x, y, 18, Pal.Red, 0.25f + 0.15f * MathF.Sin(Time * 8));
                g.PixelsCentered(Fox, x, y, 1.6f);
            }
            else if (i == _bugBay)
            {
                g.Glow(x, y, 14, Pal.Lime, 0.4f + 0.2f * MathF.Sin(Time * 6));
                g.PixelsCentered(Beetle, x, y + MathF.Sin(Time * 9) * 0.6f, 1.3f);
            }
            else
            {
                g.Glow(x, y + 2, 14, Pal.Gold, 0.08f + 0.06f * MathF2.Pulse(Time + i * 0.4f, 2));
            }
        }
    }

    private void DrawHog(Gfx g)
    {
        float x = _hogX, y = _hogY;
        g.Ellipse(x, y + 6, 8, 3, Color.Black * 0.4f);
        float lift = _hopping ? MathF.Sin(MathF.Min(1, _hopT / HopTime) * MathF.PI) * 4 : 0;
        DrawHedgehog(g, x, y - lift, 0.95f, _face, _hopping, Time);
        if (_timeLeft < 6 && _dead <= 0)
            g.Glow(x, y, 16, Pal.Red, 0.25f * MathF2.Pulse(Time, 0.5f));
    }

    private static void DrawScenery(Gfx g, float time)
    {
        // Hedge with hollows (row 0) drawn last over everything at the top.
        // River.
        float ry = Top + RowH, rh = RowH * 4;
        g.GradientV(0, ry, 640, rh, new Color(16, 50, 100), new Color(10, 34, 78));
        for (int i = 0; i < 40; i++)
        {
            float wx = Backdrops.Mod(i * 71 + time * (i % 2 == 0 ? 14 : -10), 660) - 10;
            float wy = ry + 4 + (i * 37 % (int)rh);
            g.Rect(wx, wy, 10 + i % 5 * 3, 1, Pal.Sky * (0.18f + 0.1f * MathF.Sin(time * 2 + i)));
        }
        // Moon glints.
        for (int i = 0; i < 6; i++)
            g.Glow(500 + MathF.Sin(time * 1.5f + i) * 6, ry + 10 + i * 13, 12, Pal.Ice, 0.12f);

        // Hedge.
        float hy = Top;
        g.GradientV(0, hy - 2, 640, RowH + 4, new Color(16, 60, 28), new Color(10, 40, 18));
        for (int i = 0; i < 46; i++)
        {
            float bx = i * 14 + (i % 3) * 3;
            g.Circle(bx, hy + 6 + (i % 4), 9, new Color(22, 80 + i % 3 * 10, 36));
            g.Circle(bx + 3, hy + 3 + (i % 3), 4, new Color(40, 120, 50));
        }
        // Bank (row 5).
        DrawGrass(g, RowY(5) - RowH / 2, RowH, 5, new Color(34, 90, 40), new Color(24, 66, 30), time);
        // Carriageways.
        float roadTop = RowY(6) - RowH / 2;
        g.GradientV(0, roadTop, 640, RowH * 9, new Color(46, 48, 56), new Color(38, 40, 48));
        // Hard shoulders slightly lighter.
        g.Rect(0, RowY(6) - RowH / 2, 640, RowH, new Color(56, 56, 62));
        g.Rect(0, RowY(14) - RowH / 2, 640, RowH, new Color(56, 56, 62));
        // Central reservation.
        float cy = RowY(10) - RowH / 2;
        DrawGrass(g, cy + 3, RowH - 6, 10, new Color(30, 80, 36), new Color(22, 60, 28), time);
        for (int side = 0; side < 2; side++)
        {
            float by = side == 0 ? cy + 2 : cy + RowH - 4;
            g.Rect(0, by, 640, 2.5f, new Color(170, 175, 185));
            g.Rect(0, by + 0.5f, 640, 0.8f, Color.White * 0.7f);
            for (float px = 8; px < 640; px += 32)
                g.Rect(px, by - 1, 2, 4.5f, new Color(110, 110, 120));
        }
        // Edge lines: solid white beside the hard shoulders, studs glowing.
        float[] edges = [RowY(6) + RowH / 2, RowY(9) + RowH / 2, RowY(11) - RowH / 2, RowY(14) - RowH / 2];
        for (int i = 0; i < edges.Length; i++)
        {
            g.Rect(0, edges[i] - 1, 640, 2, Color.White * 0.75f);
            var stud = (i == 1 || i == 2) ? Pal.Orange : Pal.Red;
            for (float sx = 16; sx < 640; sx += 48)
                g.Glow(sx, edges[i], 4, stud, 0.7f);
        }
        // Lane dashes.
        foreach (float ly in new[] { RowY(7) + RowH / 2, RowY(8) + RowH / 2, RowY(11) + RowH / 2, RowY(12) + RowH / 2 })
            for (float dx = 0; dx < 640; dx += 40)
            {
                g.Rect(dx + 6, ly - 0.75f, 20, 1.5f, Color.White * 0.6f);
                g.Glow(dx + 34, ly, 3, Pal.White, 0.5f);
            }
        // Verge where the hedgehog starts.
        DrawGrass(g, RowY(15) - RowH / 2, RowH, 15, new Color(34, 90, 40), new Color(24, 66, 30), time);
        // A motorway sign.
        g.Rect(22, RowY(15) - 2, 2, 11, Pal.LightGrey);
        g.Rect(40, RowY(15) - 2, 2, 11, Pal.LightGrey);
        g.RoundRect(12, RowY(15) - 9, 40, 12, 2, new Color(20, 70, 170));
        g.RectOutline(13, RowY(15) - 8, 38, 10, 0.8f, Color.White);
        g.Text("M50", 32, RowY(15) - 7, 1f, Color.White, Align.Center);
    }

    private static void DrawGrass(Gfx g, float y, float h, int seed, Color a, Color b, float time)
    {
        g.GradientV(0, y, 640, h, a, b);
        for (int i = 0; i < 70; i++)
        {
            float x = (i * 97 + seed * 13) % 640;
            float gy = y + 3 + (i * 7 + seed) % Math.Max(1, (int)h - 5);
            g.Line(x, gy + 3, x + 1 + MathF.Sin(time + i) * 0.6f, gy, 1, new Color(60, 140, 60));
            if (i % 9 == 0)
                g.Circle(x + 4, gy + 1, 1.3f, i % 2 == 0 ? Pal.Yellow : Pal.Pink);
        }
    }

    private static void DrawFloater(Gfx g, Mover m, float y, float time)
    {
        float x0 = m.X - m.Len / 2;
        if (m.Type == VType.Log)
        {
            float sink = m.Sinks ? MathF.Sin(m.Phase * 1.1f) : 1;
            float a = m.Sinks ? MathF2.Clamp((sink + 0.55f) * 3, 0.15f, 1) : 1;
            g.RoundRect(x0, y - 8 + 2, m.Len, 16, 8, Color.Black * 0.3f * a);
            g.RoundRect(x0, y - 8, m.Len, 16, 7, new Color(112, 70, 36) * a);
            g.RoundRect(x0 + 2, y - 7, m.Len - 4, 5, 3, new Color(150, 100, 55) * a);
            for (float bx = x0 + 10; bx < x0 + m.Len - 10; bx += 13)
                g.Rect(bx, y - 2 + (int)bx % 3, 7, 1.2f, new Color(70, 42, 20) * a);
            // Cut end showing the rings.
            g.Ellipse(x0 + m.Len - 4, y, 4, 7.5f, new Color(210, 170, 110) * a);
            g.Ellipse(x0 + m.Len - 4, y, 2.4f, 4.5f, new Color(170, 120, 70) * a);
            g.Ellipse(x0 + m.Len - 4, y, 1, 2, new Color(210, 170, 110) * a);
            if (m.Sinks)
            {
                if (sink < -0.3f)
                    g.Rect(x0, y - 8, m.Len, 16, new Color(16, 50, 100) * MathF2.Clamp(-sink, 0, 0.85f));
                if (sink < 0)
                    for (int i = 0; i < 3; i++)
                        g.Circle(x0 + m.Len * (0.2f + i * 0.3f), y + MathF.Sin(time * 5 + i) * 3, 1.5f, Pal.Ice * 0.6f);
            }
            g.Rect(x0 - 3, y + 7, 3, 1, Pal.Ice * 0.4f);
            g.Rect(x0 + m.Len, y + 7, 3, 1, Pal.Ice * 0.4f);
            return;
        }
        // Narrowboat with painted panels.
        g.RoundRect(x0 + 2, y - 7 + 2, m.Len, 15, 6, Color.Black * 0.3f);
        g.RoundRect(x0, y - 8, m.Len, 16, 7, Pal.Darken(m.Col, 0.35f));
        g.RoundRect(x0 + 6, y - 6, m.Len - 18, 12, 3, m.Col);
        g.Rect(x0 + 6, y - 1, m.Len - 18, 2, m.Col2);
        for (float wx = x0 + 12; wx < x0 + m.Len - 20; wx += 14)
            g.Circle(wx, y, 2.2f, Pal.Gold);
        // Roof plants and chimney.
        g.Circle(x0 + m.Len * 0.45f, y - 3, 2.2f, Pal.Lime);
        g.Circle(x0 + m.Len * 0.45f + 3, y - 2, 1.4f, Pal.Pink);
        g.Circle(x0 + m.Len * 0.25f, y + 2, 2.5f, new Color(30, 30, 30));
        g.Glow(x0 + m.Len * 0.25f, y + 2, 7, Pal.Orange, 0.25f + 0.1f * MathF.Sin(time * 6));
        // Wake.
        g.Rect(x0 - 6, y - 7, 6, 1, Pal.Ice * 0.35f);
        g.Rect(x0 - 10, y + 6, 8, 1, Pal.Ice * 0.3f);
        g.Rect(x0 + m.Len, y - 7, 6, 1, Pal.Ice * 0.35f);
    }

    private static void DrawVehicle(Gfx g, VType type, float x, float y, float len, float dir, Color col, Color col2, float k = 1)
    {
        float front = x + dir * len / 2;
        float h = k * (type switch { VType.Lorry or VType.Coach => 16, VType.Van => 14, VType.Bike => 6, VType.Caravan => 13, _ => 12 });
        float x0 = x - len / 2;
        // Headlight beams.
        g.Glow(front + dir * 14 * k, y - h * 0.3f, 14 * k, Pal.Yellow, 0.22f);
        g.Glow(front + dir * 14 * k, y + h * 0.3f, 14 * k, Pal.Yellow, 0.22f);
        g.RoundRect(x0 + k, y - h / 2 + 2 * k, len, h, 3 * k, Color.Black * 0.45f);
        switch (type)
        {
            case VType.Bike:
            {
                g.RoundRect(x0, y - 2 * k, len, 4 * k, 2 * k, Pal.DarkGrey);
                g.RoundRect(x - 4 * k, y - 3 * k, 8 * k, 6 * k, 2 * k, col);
                g.Circle(x - dir * 2 * k, y, 3 * k, col2);
                g.Circle(x - dir * 2 * k, y - 0.8f * k, 1.4f * k, Color.White * 0.6f);
                break;
            }
            case VType.Lorry:
            {
                float cab = 18 * k;
                float cabX = dir > 0 ? x0 + len - cab : x0;
                float trX = dir > 0 ? x0 : x0 + cab + 2 * k;
                float trL = len - cab - 2 * k;
                g.RoundRect(trX, y - h / 2, trL, h, 2 * k, new Color(220, 222, 228));
                g.Rect(trX + 2 * k, y - 2 * k, trL - 4 * k, 4 * k, col2);
                for (float rx = trX + 6 * k; rx < trX + trL - 4 * k; rx += 9 * k)
                    g.Rect(rx, y - h / 2 + k, k, h - 2 * k, Color.Black * 0.12f);
                g.RoundRect(cabX, y - h / 2 + k, cab, h - 2 * k, 3 * k, col);
                g.Rect(dir > 0 ? cabX + cab - 6 * k : cabX + 2 * k, y - h / 2 + 2 * k, 4 * k, h - 4 * k, new Color(30, 40, 70));
                break;
            }
            case VType.Coach:
            {
                g.RoundRect(x0, y - h / 2, len, h, 4 * k, col);
                g.Rect(x0 + 4 * k, y - h / 2 + 2 * k, len - 8 * k, 3 * k, new Color(40, 60, 100));
                g.Rect(x0 + 4 * k, y + h / 2 - 5 * k, len - 8 * k, 3 * k, new Color(40, 60, 100));
                g.Rect(x0 + 4 * k, y - k, len - 8 * k, 2 * k, col2);
                g.Rect(dir > 0 ? x0 + len - 6 * k : x0 + 2 * k, y - h / 2 + 2 * k, 4 * k, h - 4 * k, new Color(30, 40, 70));
                break;
            }
            case VType.Caravan:
            {
                float carL = 28 * k;
                float cx0 = dir > 0 ? x0 + len - carL : x0;
                float van0 = dir > 0 ? x0 : x0 + carL + 4 * k;
                float vanL = len - carL - 4 * k;
                g.Rect(dir > 0 ? van0 + vanL : cx0 + carL, y - 0.5f * k, 4 * k, k, Pal.Grey);
                g.RoundRect(van0, y - 7 * k, vanL, 14 * k, 4 * k, new Color(236, 232, 220));
                g.Rect(van0 + 3 * k, y - k, vanL - 6 * k, 2 * k, new Color(200, 120, 60));
                g.Rect(van0 + vanL / 2 - 4 * k, y - 5 * k, 8 * k, 3 * k, new Color(120, 170, 220));
                CarBody(g, cx0, y, carL, 11 * k, dir, col, k);
                break;
            }
            case VType.Van:
            {
                g.RoundRect(x0, y - h / 2, len, h, 3 * k, col);
                g.Rect(x0 + 3 * k, y - h / 2 + 2 * k, len - 14 * k, h - 4 * k, Pal.Lighten(col, 0.15f));
                g.Rect(dir > 0 ? x0 + len - 8 * k : x0 + 3 * k, y - h / 2 + 2 * k, 5 * k, h - 4 * k, new Color(30, 40, 70));
                break;
            }
            default:
                CarBody(g, x0, y, len, h, dir, col, k);
                break;
        }
        // Lights.
        float fx = dir > 0 ? x0 + len - 1.5f * k : x0 + 1.5f * k, bx = dir > 0 ? x0 + 1.5f * k : x0 + len - 1.5f * k;
        if (type == VType.Bike)
        {
            g.Circle(fx, y, 1.6f * k, Pal.Ice);
            g.Glow(bx, y, 5 * k, Pal.Red, 0.7f);
            return;
        }
        g.Circle(fx, y - h / 2 + 2.5f * k, 1.5f * k, Pal.Ice);
        g.Circle(fx, y + h / 2 - 2.5f * k, 1.5f * k, Pal.Ice);
        g.Rect(bx - k, y - h / 2 + 1.5f * k, 2 * k, 2.5f * k, Pal.Red);
        g.Rect(bx - k, y + h / 2 - 4 * k, 2 * k, 2.5f * k, Pal.Red);
        g.Glow(bx - dir * 2 * k, y, 9 * k, Pal.Red, 0.45f);
    }

    private static void CarBody(Gfx g, float x0, float y, float len, float h, float dir, Color col, float k)
    {
        g.RoundRect(x0, y - h / 2, len, h, 4 * k, col);
        // Roof and glass.
        float roofL = len * 0.45f;
        float roofX = x0 + len * (dir > 0 ? 0.22f : 0.33f);
        g.RoundRect(roofX - 3 * k, y - h / 2 + 1.5f * k, roofL + 6 * k, h - 3 * k, 3 * k, new Color(30, 40, 70));
        g.RoundRect(roofX, y - h / 2 + 2 * k, roofL, h - 4 * k, 2 * k, Pal.Darken(col, 0.15f));
        g.Rect(roofX + k, y - h / 2 + 2.5f * k, roofL - 2 * k, k, Color.White * 0.25f);
    }

    // ------------------------------------------------------------------ icon

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        float s = r.H / 70f;
        float Y(float v) => r.Y + v * s;
        // Hedge with a sleeping hedgehog in its hollow.
        g.GradientV(r.X, r.Y, r.W, 12 * s, new Color(16, 60, 28), new Color(10, 40, 18));
        for (float bx = 0; bx < r.W + 10 * s; bx += 9 * s)
            g.Circle(r.X + bx, Y(4), 6 * s, new Color(26, 90, 40));
        g.Ellipse(r.CenterX + 34 * s, Y(7), 11 * s, 6 * s, new Color(8, 26, 12));
        DrawHedgehog(g, r.CenterX + 34 * s, Y(7), 0.6f * s, 0, true, 0);
        // River.
        g.GradientV(r.X, Y(12), r.W, 18 * s, new Color(16, 50, 100), new Color(10, 34, 78));
        for (int i = 0; i < 8; i++)
            g.Rect(r.X + Backdrops.Mod(i * 37 * s + time * 8 * s, r.W), Y(15 + i * 2), 8 * s, 0.6f * s, Pal.Sky * 0.25f);
        for (int i = 0; i < 2; i++)
        {
            float lx = r.X + Backdrops.Mod(time * 16 * s + i * (r.W + 60 * s) / 2, r.W + 60 * s) - 55 * s;
            g.RoundRect(lx, Y(15), 50 * s, 11 * s, 5 * s, new Color(112, 70, 36));
            g.RoundRect(lx + 2 * s, Y(16), 46 * s, 3.5f * s, 2 * s, new Color(150, 100, 55));
            g.Ellipse(lx + 47 * s, Y(20.5f), 3 * s, 5 * s, new Color(210, 170, 110));
            g.Ellipse(lx + 47 * s, Y(20.5f), 1.6f * s, 3 * s, new Color(170, 120, 70));
        }
        // Bank.
        g.GradientV(r.X, Y(30), r.W, 12 * s, new Color(34, 96, 42), new Color(24, 66, 30));
        // Road.
        g.GradientV(r.X, Y(42), r.W, 28 * s, new Color(48, 50, 58), new Color(36, 38, 46));
        g.Rect(r.X, Y(42.5f), r.W, 1 * s, Color.White * 0.7f);
        for (float dx = 0; dx < r.W; dx += 20 * s)
        {
            g.Rect(r.X + dx + 3 * s, Y(56), 10 * s, 1 * s, Color.White * 0.6f);
            g.Glow(r.X + dx + 16 * s, Y(56.5f), 2 * s, Pal.White, 0.5f);
        }
        float cx = r.X + Backdrops.Mod(time * 60 * s, r.W + 60 * s) - 30 * s;
        float lorryX = r.X + r.W - Backdrops.Mod(time * 32 * s + 40 * s, r.W + 80 * s) + 40 * s;
        float car2 = r.X + Backdrops.Mod(time * 60 * s + (r.W + 60 * s) / 2, r.W + 60 * s) - 30 * s;
        DrawVehicle(g, VType.Car, cx, Y(49), 26 * s, 1, CarColours[0], CarColours[4], 0.8f * s);
        DrawVehicle(g, VType.Hatch, car2, Y(49), 22 * s, 1, CarColours[2], CarColours[3], 0.8f * s);
        DrawVehicle(g, VType.Lorry, lorryX, Y(63), 66 * s, -1, CarColours[1], CarColours[0], 0.8f * s);
        // The hero, mid-hop on the bank.
        float hop = MathF.Abs(MathF.Sin(time * 3.2f)) * 4 * s;
        float hx = r.CenterX - 12 * s, hy = Y(35);
        g.Ellipse(hx, hy + 6 * s, 7 * s, 2.5f * s, Color.Black * 0.4f);
        g.Glow(hx, hy - hop, 20 * s, Pal.Orange, 0.18f);
        DrawHedgehog(g, hx, hy - hop, 1.05f * s, 0, false, 0);
    }

    // ------------------------------------------------------------------ autoplay

    public override void AutoPlay(Controls c)
    {
        if (_dead > 0 || _hopping || IsOver || Tick % 2 != 0)
            return;
        int r = _hogRow;
        var cur = KindOf(r);

        float drift1 = MathF.Sign(_lanes[1].Speed);
        if (r == 1)
        {
            // Ride towards an empty hollow ahead and hop in as it passes; if none is ahead, drop back a row.
            int best = -1;
            float bd = 999;
            for (int i = 0; i < 5; i++)
            {
                float d = (BayX[i] - _hogX) * drift1;
                if (!_homes[i] && i != _foxBay && d > -9 && d < bd)
                {
                    bd = d;
                    best = i;
                }
            }
            if (best >= 0 && MathF.Abs(BayX[best] - _hogX) < 9)
                c.SetDirections(0, -1);
            else if (best < 0 && SafeFor(2, _hogX, 0))
                c.SetDirections(0, 1);
            else if ((best < 0 || (drift1 > 0 ? _hogX > 590 : _hogX < 50)) && SafeFor(1, _hogX - drift1 * RowH, 0))
                c.SetDirections(-drift1, 0);
            return;
        }
        if (r == 2 && !BayAhead(_hogX, drift1))
        {
            if (!SafeFor(2, _hogX, 0) && SafeFor(1, _hogX, 0))
                c.SetDirections(0, -1);
            return;
        }

        if (SafeFor(r - 1, _hogX, 0.45f))
        {
            c.SetDirections(0, -1);
            return;
        }
        if (cur == RowKind.River)
        {
            // Riding: the hedgehog moves with its log, so staying put is safe unless it sinks or nears the edge.
            var lane = _lanes[r];
            float drift = MathF.Sign(lane.Speed);
            var under = PlatformAt(lane, _hogX, 2);
            bool sinking = under != null && under.Sinks && MathF.Sin((under.Phase + 0.6f) * 1.1f) < -0.4f;
            bool nearEdge = drift > 0 ? _hogX > 560 : _hogX < 80;
            if (under != null && !sinking && !nearEdge)
                return;
            if (SafeFor(r, _hogX - drift * RowH, 0))
                c.SetDirections(-drift, 0);
            else if (SafeFor(r + 1, _hogX, 0.4f))
                c.SetDirections(0, 1);
            return;
        }
        if (SafeFor(r, _hogX, 0.5f))
            return;
        foreach (int dx in new[] { -1, 1 })
            if (SafeFor(r, MathF2.Clamp(_hogX + dx * RowH, 10, 630), 0.5f))
            {
                c.SetDirections(dx, 0);
                return;
            }
        if (r < StartRow && SafeFor(r + 1, _hogX, 0.5f))
        {
            c.SetDirections(0, 1);
            return;
        }
        if (KindOf(r - 1) != RowKind.River)
            c.SetDirections(0, -1);
    }

    private bool BayAhead(float x, float drift)
    {
        for (int i = 0; i < 5; i++)
            if (!_homes[i] && (BayX[i] - x) * drift > 30)
                return true;
        return false;
    }

    private bool SafeFor(int row, float x, float horizon)
    {
        if (row < 0 || row >= Rows)
            return false;
        var kind = KindOf(row);
        if (kind == RowKind.Home)
            return false;
        var lane = _lanes[row];
        if (lane == null)
            return true;
        if (lane.River)
        {
            foreach (var m in lane.Items)
            {
                float mx = m.X + lane.Speed * HopTime;
                bool sinkSoon = m.Sinks && MathF.Sin((m.Phase + 0.8f) * 1.1f) < -0.4f;
                if (MathF.Abs(mx - x) < m.Len / 2 - 6 && !sinkSoon && !Submerged(m))
                    return true;
            }
            return false;
        }
        for (float t = 0; t <= horizon; t += 0.05f)
            foreach (var m in lane.Items)
                if (MathF.Abs(m.X + lane.Speed * t - x) < m.Len / 2 + 9)
                    return false;
        return true;
    }
}
