using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 36 Rats: the Pied Piper of Hamelin. Wander the cobbled streets playing your pipe; every rat
/// you pass joins the conga line behind you. Lead the line on to a jetty and they all leap into
/// the river for a bonus that grows with the length of the line.
/// </summary>
public sealed class Rats : MiniGame, Capture.ICaptureHints
{
    public override int Number => 36;
    public override string Title => "Rats";
    public override Category Category => Category.Arcade;
    public override string Tagline => "Pipe the rats of Hamelin into a conga line and march them into the river.";
    public override Color Accent => Pal.Orange;
    public override Pad Pad => Pad.Stick;
    public int CaptureTicks => 540;

    public override string[] HowToPlay =>
    [
        "You are the Pied Piper. Walk over rats and they follow you in a line. Don't bump into houses, barrels or your own rats!",
        "March on to a jetty and the line jumps in the river: length x length x 10 points. Each trip makes you faster.",
    ];

    public override string[] DesktopControls => ["ARROWS / WASD to steer the piper."];
    public override string[] TouchControls => ["Push the stick to steer the piper."];

    private const int Cols = 32, Rows = 16, Cell = 20;
    private const float Top = 32;
    private const int RiverX = 29;

    private enum Tile : byte { Street, House, Water, Jetty, Fountain }

    private struct FreeRat
    {
        public Point Cell, From;
        public float Move, Wait, Angle, Wiggle;
    }

    private struct Jumper
    {
        public Vector2 From, To;
        public float T, Delay;
    }

    private static readonly Dictionary<char, Color> Colours = new()
    {
        ['g'] = Pal.Lime, ['r'] = Pal.Red, ['y'] = Pal.Yellow, ['s'] = Pal.Skin, ['k'] = new Color(40, 20, 20),
        ['o'] = new Color(200, 150, 80), ['b'] = new Color(80, 50, 30),
    };

    private static readonly PixelArt[] PiperArt =
    [
        new([
            "......g.....", ".....rg.....", "....rrrr....", "...rrrrrr...", "....ssss....", "....sksk....", "....ssss....",
            "...rryyyoooo", "..rrryyyy...", "..rrryyyy...", "...rryyy....", "...rryyy....", "...rr.yy....", "...bb.bb....",
        ], Colours),
        new([
            "......g.....", ".....rg.....", "....rrrr....", "...rrrrrr...", "....ssss....", "....sksk....", "....ssss....",
            "...rryyyoooo", "..rrryyyy...", "..rrryyyy...", "...rryyy....", "...rryyy....", "....ryy.....", "....bbb.....",
        ], Colours),
    ];

    private static readonly float[] Tune = [0.1f, 0.3f, 0.45f, 0.3f, 0.1f, 0.45f, 0.6f, 0.45f, 0.3f, 0.1f, -0.1f, 0.1f];

    private readonly Tile[,] _map = new Tile[Cols, Rows];
    private readonly bool[,] _barrel = new bool[Cols, Rows];
    private readonly List<Point> _trail = new();
    private readonly List<FreeRat> _rats = new();
    private readonly List<Jumper> _jumpers = new();
    private readonly Queue<Point> _bfs = new();
    private readonly Point[,] _cameFrom = new Point[Cols, Rows];
    private readonly bool[,] _seen = new bool[Cols, Rows];
    private readonly bool[,] _blocked = new bool[Cols, Rows];

    private Point _dir, _nextDir;
    private int _line, _deliveries, _note;
    private float _stepT, _stepTime, _dead, _banner;
    private bool _alive;

    private static readonly Point Up = new(0, -1), Down = new(0, 1), Left = new(-1, 0), Right = new(1, 0);

    protected override void Start()
    {
        Lives = 3;
        Level = 1;
        _deliveries = 0;
        _note = 0;
        _rats.Clear();
        _jumpers.Clear();
        BuildMap();
        for (int i = 0; i < 5; i++)
            AddBarrel();
        Respawn();
        _banner = 2;
        for (int i = 0; i < 4; i++)
            SpawnRat();
    }

    private void BuildMap()
    {
        for (int x = 0; x < Cols; x++)
            for (int y = 0; y < Rows; y++)
            {
                Tile t = Tile.Street;
                if (x >= RiverX)
                    t = Tile.Water;
                else if (x == 0 || y == 0 || y == Rows - 1)
                    t = Tile.House;
                _map[x, y] = t;
                _barrel[x, y] = false;
            }
        // Blocks of houses separated by streets.
        int[] bxs = [3, 9, 15, 21];
        int[] bys = [3, 7, 11];
        foreach (int bx in bxs)
            foreach (int by in bys)
            {
                if (bx == 9 && by == 7)
                    continue; // the market square
                bool fountain = bx == 15 && by == 7;
                for (int x = bx; x < bx + 4; x++)
                    for (int y = by; y < by + 2; y++)
                        _map[x, y] = fountain ? (x is 16 or 17 ? Tile.Fountain : Tile.Street) : Tile.House;
            }
        foreach (int jy in new[] { 2, 9, 14 })
            _map[RiverX, jy] = Tile.Jetty;
    }

    private bool InGrid(Point p) => p.X >= 0 && p.Y >= 0 && p.X < Cols && p.Y < Rows;

    private bool Solid(Point p) => !InGrid(p) || _map[p.X, p.Y] is Tile.House or Tile.Water or Tile.Fountain || _barrel[p.X, p.Y];

    private bool FreeStreet(Point p) => InGrid(p) && _map[p.X, p.Y] == Tile.Street && !_barrel[p.X, p.Y];

    private bool OnLine(Point p, int skipTail)
    {
        for (int i = 0; i <= _line - skipTail && i < _trail.Count; i++)
            if (_trail[i] == p)
                return true;
        return false;
    }

    private void Respawn()
    {
        _trail.Clear();
        var start = new Point(2, 1);
        _trail.Add(start);
        _trail.Add(new Point(1, 1));
        _dir = _nextDir = Right;
        _line = 0;
        _alive = true;
        _stepT = 0;
        _stepTime = MathF.Max(0.085f, 0.19f - _deliveries * 0.009f);
    }

    private void AddBarrel()
    {
        for (int tries = 0; tries < 60; tries++)
        {
            var p = new Point(RandInt(1, RiverX - 1), RandInt(1, Rows - 1));
            if (!FreeStreet(p) || (p.Y <= 2 && p.X < 8) || p.X >= RiverX - 1)
                continue;
            // Never next to another barrel, so a street can't be sealed off.
            bool crowded = false;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    if (InGrid(new Point(p.X + dx, p.Y + dy)) && (_barrel[p.X + dx, p.Y + dy] || _map[p.X + dx, p.Y + dy] == Tile.Fountain))
                        crowded = true;
            if (crowded || OnLine(p, 0))
                continue;
            bool nearRat = false;
            foreach (var r in _rats)
                nearRat |= r.Cell == p;
            if (nearRat)
                continue;
            _barrel[p.X, p.Y] = true;
            return;
        }
    }

    private void SpawnRat()
    {
        for (int tries = 0; tries < 60; tries++)
        {
            var p = new Point(RandInt(1, RiverX), RandInt(1, Rows - 1));
            if (!FreeStreet(p) || OnLine(p, 0))
                continue;
            if (_trail.Count > 0 && Math.Abs(p.X - _trail[0].X) + Math.Abs(p.Y - _trail[0].Y) < 4)
                continue;
            bool taken = false;
            foreach (var r in _rats)
                taken |= r.Cell == p;
            if (taken)
                continue;
            _rats.Add(new FreeRat { Cell = p, From = p, Move = 1, Wait = Rand(0.5f, 1.5f), Angle = Rand(0, MathF2.Tau), Wiggle = Rand(0, 6) });
            Fx.Burst(CellCentre(p).X, CellCentre(p).Y, Pal.Grey, 6, 40, 0.3f, 1.5f, 0, false);
            return;
        }
    }

    private static Vector2 CellCentre(Point p) => new(p.X * Cell + Cell / 2f, Top + p.Y * Cell + Cell / 2f);

    private static Vector2 CellCentre(Vector2 p) => new(p.X * Cell + Cell / 2f, Top + p.Y * Cell + Cell / 2f);

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        if (_banner > 0)
            _banner -= Dt;
        UpdateRats();
        UpdateJumpers();

        if (!_alive)
        {
            _dead -= Dt;
            if (_dead <= 0)
                Respawn();
            return;
        }

        // Steering: remember the last direction asked for (no reversing into the line).
        Point want = _nextDir;
        if (In.LeftPressed || (In.Left && !In.Up && !In.Down)) want = Left;
        else if (In.RightPressed || (In.Right && !In.Up && !In.Down)) want = Right;
        else if (In.UpPressed || (In.Up && !In.Left && !In.Right)) want = Up;
        else if (In.DownPressed || (In.Down && !In.Left && !In.Right)) want = Down;
        if (want.X != -_dir.X || want.Y != -_dir.Y || _line == 0)
            _nextDir = want;

        _stepT += Dt;
        if (_stepT >= _stepTime)
        {
            _stepT -= _stepTime;
            Advance();
        }
        Status = _line > 0 ? "LINE OF " + _line : "RATS!";
    }

    private void Advance()
    {
        var head = _trail[0];
        if (_map[head.X, head.Y] == Tile.Jetty)
            _nextDir = Left; // back along the jetty to the quay
        _dir = _nextDir;
        var next = new Point(head.X + _dir.X, head.Y + _dir.Y);

        if (Solid(next) || OnLine(next, 1))
        {
            Crash(next);
            return;
        }
        _trail.Insert(0, next);
        if (_trail.Count > 400)
            _trail.RemoveAt(_trail.Count - 1);

        if (_note++ % 2 == 0)
        {
            Sound.Play(Sfx.Beep, Tune[(_note / 2) % Tune.Length], 0.1f);
            var hp = CellCentre(next);
            Fx.Spark(hp.X + 8, hp.Y - 6, Rand(-10, 25), Rand(-40, -25), Pal.Cycle(Time), 0.8f, 1.6f);
        }

        // Collect rats.
        for (int i = _rats.Count - 1; i >= 0; i--)
        {
            var r = _rats[i];
            if (r.Cell == next || (r.Move < 0.5f && r.From == next))
            {
                _rats.RemoveAt(i);
                _line++;
                var c = CellCentre(next);
                AddScore(10, c.X, c.Y - 12, Pal.Yellow);
                Fx.Burst(c.X, c.Y, Pal.LightGrey, 8, 50, 0.3f, 1.5f);
                Sound.Play(Sfx.Pickup, MathF.Min(1, -0.3f + _line * 0.06f), 0.6f);
            }
        }

        if (_map[next.X, next.Y] == Tile.Jetty)
            Deliver(next);
    }

    private void Deliver(Point jetty)
    {
        if (_line > 0)
        {
            int bonus = _line * _line * 10;
            var c = CellCentre(jetty);
            AddScore(bonus, c.X - 30, c.Y - 18, Pal.Cyan);
            Fx.Float(_line + " RATS!", c.X - 40, c.Y + 4, Pal.White, 1.5f);
            for (int i = 0; i < _line; i++)
            {
                var from = CellCentre(_trail[Math.Min(i + 1, _trail.Count - 1)]);
                var to = new Vector2(RiverX * Cell + Rand(24, 54), c.Y + Rand(-26, 26));
                _jumpers.Add(new Jumper { From = from, To = to, Delay = i * 0.07f });
            }
            _deliveries++;
            Level = 1 + _deliveries;
            Sound.Play(Sfx.Bonus);
            _stepTime = MathF.Max(0.085f, 0.19f - _deliveries * 0.009f);
            AddBarrel();
            if (_deliveries % 3 == 0)
                AddBarrel();
        }
        else
        {
            Sound.Play(Sfx.Thud, 0, 0.4f);
        }
        _line = 0;
        // Turn back along the jetty.
        _dir = _nextDir = new Point(-_dir.X, -_dir.Y);
    }

    private void Crash(Point at)
    {
        _alive = false;
        _dead = 1.6f;
        var c = CellCentre(_trail[0]);
        Fx.Burst(c.X, c.Y, Pal.Yellow, 20, 120, 0.6f, 2f);
        Fx.Shake(4, 0.3f);
        Sound.Play(Sfx.Hurt);
        // The line scatters: rats run back into the streets.
        int scatter = Math.Min(_line, 6);
        for (int i = 0; i < _line; i++)
        {
            var p = CellCentre(_trail[Math.Min(i + 1, _trail.Count - 1)]);
            Fx.Burst(p.X, p.Y, Pal.Grey, 4, 60, 0.4f, 1.5f, 0, false);
        }
        if (_line > 0)
            Sound.Play(Sfx.Pop, 0.5f, 0.5f);
        _line = 0;
        for (int i = 0; i < scatter && _rats.Count < 10; i++)
            SpawnRat();
        LoseLife();
    }

    private void UpdateRats()
    {
        int target = Math.Min(3 + _deliveries / 2, 8);
        if (_rats.Count < target && Tick % 40 == 0)
            SpawnRat();
        for (int i = 0; i < _rats.Count; i++)
        {
            var r = _rats[i];
            r.Wiggle += Dt * 10;
            if (r.Move < 1)
            {
                r.Move = MathF.Min(1, r.Move + Dt * 3);
            }
            else
            {
                r.Wait -= Dt;
                if (r.Wait <= 0)
                {
                    r.Wait = Rand(0.6f, 1.8f);
                    var d = Pick(Up, Down, Left, Right);
                    var n = new Point(r.Cell.X + d.X, r.Cell.Y + d.Y);
                    if (FreeStreet(n) && !OnLine(n, 0) && (_trail.Count == 0 || n != _trail[0]))
                    {
                        r.From = r.Cell;
                        r.Cell = n;
                        r.Move = 0;
                        r.Angle = MathF.Atan2(d.Y, d.X);
                        if (Chance(0.3f))
                            Sound.Play(Sfx.Pop, 0.9f, 0.08f);
                    }
                }
            }
            _rats[i] = r;
        }
    }

    private void UpdateJumpers()
    {
        for (int i = _jumpers.Count - 1; i >= 0; i--)
        {
            var j = _jumpers[i];
            if (j.Delay > 0)
            {
                j.Delay -= Dt;
                _jumpers[i] = j;
                continue;
            }
            j.T += Dt * 1.8f;
            if (j.T >= 1)
            {
                Fx.Burst(j.To.X, j.To.Y, Pal.Sky, 10, 70, 0.5f, 1.8f, 120);
                Fx.Burst(j.To.X, j.To.Y, Pal.White, 4, 40, 0.3f, 1.2f, 120);
                Sound.Play(Sfx.Splash, Rand(-0.2f, 0.4f), 0.4f);
                _jumpers.RemoveAt(i);
                continue;
            }
            _jumpers[i] = j;
        }
    }

    // ------------------------------------------------------------------ drawing

    private static float Hash(int x, int y)
    {
        unchecked
        {
            int h = x * 73856093 ^ y * 19349663;
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
        }
    }

    private void DrawTown(Gfx g)
    {
        for (int x = 0; x < Cols; x++)
            for (int y = 0; y < Rows; y++)
            {
                float px = x * Cell, py = Top + y * Cell;
                switch (_map[x, y])
                {
                    case Tile.Street:
                    case Tile.Fountain:
                        DrawCobbles(g, px, py, x, y);
                        break;
                    case Tile.House:
                        DrawHouse(g, px, py, x, y);
                        break;
                    case Tile.Water:
                    case Tile.Jetty:
                        DrawWater(g, px, py, x, y);
                        break;
                }
            }
        // Above the top row: rooftops continue.
        g.GradientV(0, Screen.HudHeight, RiverX * Cell, Top - Screen.HudHeight, new Color(70, 30, 26), new Color(110, 46, 36));
        g.GradientV(RiverX * Cell, Screen.HudHeight, (Cols - RiverX) * Cell, Top - Screen.HudHeight, Pal.DeepWater, Pal.Water);
        g.Rect(0, Top + Rows * Cell, 640, 360 - Top - Rows * Cell, new Color(70, 30, 26));

        // River bank.
        g.Rect(RiverX * Cell - 2, Top, 3, Rows * Cell, new Color(90, 80, 70));

        // Jetties.
        for (int y = 0; y < Rows; y++)
            if (_map[RiverX, y] == Tile.Jetty)
            {
                float px = RiverX * Cell, py = Top + y * Cell;
                g.Rect(px - 1, py + 2, Cell + 8, Cell - 4, new Color(120, 80, 40));
                for (int k = 0; k < 4; k++)
                    g.Rect(px + k * 7, py + 2, 1, Cell - 4, new Color(70, 45, 20));
                g.Circle(px + Cell + 6, py + 3, 2.5f, new Color(70, 45, 20));
                g.Circle(px + Cell + 6, py + Cell - 3, 2.5f, new Color(70, 45, 20));
                float pulse = MathF2.Pulse(Time, 1f);
                g.Glow(px + Cell / 2f, py + Cell / 2f, 22, Pal.Cyan, 0.2f + 0.25f * pulse);
                g.RectOutline(px - 1, py + 2, Cell + 8, Cell - 4, 1, Pal.Cyan * (0.4f + 0.4f * pulse));
            }

        // Fountain in the square.
        var fc = new Vector2(17 * Cell, Top + 8 * Cell);
        g.Circle(fc, 21, new Color(120, 120, 130));
        g.Circle(fc, 18, new Color(150, 150, 160));
        g.Circle(fc, 15, Pal.Water);
        g.Glow(fc, 20, Pal.Sky, 0.3f);
        g.Circle(fc, 4, new Color(170, 170, 180));
        for (int k = 0; k < 6; k++)
        {
            float a = Time * 1.5f + k * MathF2.Tau / 6;
            g.Circle(fc + MathF2.FromAngle(a, 8 + 2 * MathF.Sin(Time * 4 + k)), 1.4f, Pal.Ice * 0.8f);
        }

        // Barrels.
        for (int x = 0; x < Cols; x++)
            for (int y = 0; y < Rows; y++)
                if (_barrel[x, y])
                    DrawBarrel(g, CellCentre(new Point(x, y)), 1);
    }

    private static void DrawCobbles(Gfx g, float px, float py, int x, int y)
    {
        g.Rect(px, py, Cell + 0.5f, Cell + 0.5f, new Color(58, 54, 56));
        for (int k = 0; k < 4; k++)
        {
            float h = Hash(x * 4 + k, y);
            float cx = px + (k % 2) * 10 + 5 + (h - 0.5f) * 2;
            float cy = py + (k / 2) * 10 + 5 + (Hash(y, x * 4 + k) - 0.5f) * 2;
            var col = Pal.Lerp(new Color(92, 86, 88), new Color(112, 104, 100), h);
            g.Ellipse(cx, cy + 0.8f, 4.4f, 3.6f, new Color(44, 40, 42));
            g.Ellipse(cx, cy, 4.2f, 3.4f, col);
            g.Ellipse(cx - 1, cy - 1, 1.6f, 1.1f, Color.White * 0.08f);
        }
    }

    private void DrawHouse(Gfx g, float px, float py, int x, int y)
    {
        // Roof tiles.
        float h = Hash(x / 2 + 31, y / 2 + 7);
        var roof = Pal.Lerp(new Color(150, 60, 40), new Color(120, 50, 70), h);
        g.GradientV(px, py, Cell + 0.5f, Cell + 0.5f, Pal.Lighten(roof, 0.1f), Pal.Darken(roof, 0.25f));
        for (int k = 1; k < 4; k++)
            g.Rect(px, py + k * 5, Cell + 0.5f, 1, Pal.Darken(roof, 0.4f));
        for (int k = 0; k < 4; k++)
            g.Rect(px + 4 + ((k + x) % 2) * 8, py + k * 5 + 1, 1, 4, Pal.Darken(roof, 0.35f));
        // A chimney now and then.
        if (Hash(x, y) > 0.86f)
        {
            g.Rect(px + 12, py + 3, 5, 7, new Color(110, 100, 95));
            g.Rect(px + 11, py + 2, 7, 2, new Color(80, 70, 66));
        }
        // Front wall where the house faces a street below: half-timbered.
        bool facade = y + 1 < Rows && _map[x, y + 1] != Tile.House;
        if (facade)
        {
            float fy = py + Cell - 9;
            g.Rect(px, fy, Cell + 0.5f, 9, new Color(235, 220, 180));
            g.Rect(px, fy, Cell + 0.5f, 1.5f, new Color(80, 50, 30));
            g.Rect(px, fy + 7.5f, Cell + 0.5f, 1.5f, new Color(80, 50, 30));
            g.Rect(px + 1, fy, 1.5f, 9, new Color(80, 50, 30));
            g.Line(px + 3, fy + 1, px + 9, fy + 8, 1.2f, new Color(80, 50, 30));
            bool lit = Hash(y, x) > 0.5f;
            g.Rect(px + 11, fy + 2, 6, 4, lit ? new Color(255, 210, 110) : new Color(60, 70, 90));
            if (lit)
                g.Glow(px + 14, fy + 4, 8, Pal.Gold, 0.3f);
        }
    }

    private void DrawWater(Gfx g, float px, float py, int x, int y)
    {
        g.GradientV(px, py, Cell + 0.5f, Cell + 0.5f, new Color(26, 86, 160), new Color(18, 66, 136));
        float off = Backdrops.Mod(Time * 14 + Hash(x, y) * Cell, Cell);
        g.Rect(px + 3, py + off, 7, 1, Pal.Sky * 0.45f);
        g.Rect(px + 11, py + Backdrops.Mod(off + 9, Cell), 6, 1, Pal.Ice * 0.3f);
    }

    private static void DrawBarrel(Gfx g, Vector2 c, float s)
    {
        g.Circle(c.X + 1.5f * s, c.Y + 2 * s, 8 * s, Color.Black * 0.35f);
        g.Circle(c, 8 * s, new Color(110, 66, 30));
        g.Ring(c.X, c.Y, 6.5f * s, 1.4f * s, new Color(70, 70, 80), 16);
        g.Circle(c, 4.5f * s, new Color(140, 90, 45));
        g.Ring(c.X, c.Y, 2.6f * s, 1f * s, new Color(90, 55, 25), 12);
        g.Circle(c.X - 2 * s, c.Y - 2.5f * s, 1.5f * s, Color.White * 0.25f);
    }

    private static void DrawRat(Gfx g, Vector2 p, float angle, float wiggle, float s, float shade)
    {
        var f = MathF2.FromAngle(angle);
        var side = new Vector2(-f.Y, f.X);
        var body = Pal.Lerp(new Color(150, 140, 150), new Color(150, 110, 80), shade);
        // Tail.
        var t0 = p - f * 6 * s;
        var t1 = t0 - f * 5 * s + side * MathF.Sin(wiggle) * 2.5f * s;
        var t2 = t1 - f * 4 * s + side * MathF.Sin(wiggle + 1.5f) * 2.5f * s;
        g.Line(t0, t1, 1.3f * s, Pal.Pink * 0.9f);
        g.Line(t1, t2, 1f * s, Pal.Pink * 0.9f);
        g.Circle(p.X + 1 * s, p.Y + 1.5f * s, 6f * s, Color.Black * 0.35f);
        g.Circle(p - f * 2.5f * s, 5.4f * s, Color.Black * 0.6f);
        g.Circle(p + f * 1.5f * s, 4.8f * s, Color.Black * 0.6f);
        g.Circle(Head(p, f, s), 3.8f * s, Color.Black * 0.6f);
        g.Circle(p - f * 2.5f * s, 4.6f * s, body);
        g.Circle(p + f * 1.5f * s, 4f * s, body);
        var head = Head(p, f, s);
        g.Circle(head, 3f * s, Pal.Lighten(body, 0.1f));
        g.Circle(head + f * 2.6f * s, 1.1f * s, Pal.Pink);
        g.Circle(head - f * 1.2f * s + side * 2.4f * s, 1.5f * s, Pal.Pink * 0.9f);
        g.Circle(head - f * 1.2f * s - side * 2.4f * s, 1.5f * s, Pal.Pink * 0.9f);
        g.Circle(head + f * 0.8f * s + side * 1.2f * s, 0.7f * s, Pal.Black);
        g.Circle(head + f * 0.8f * s - side * 1.2f * s, 0.7f * s, Pal.Black);
        g.Circle(p - f * 3 * s - side * 1.5f * s, 1.6f * s, Color.White * 0.12f);
    }

    private static Vector2 Head(Vector2 p, Vector2 f, float s) => p + f * 5.2f * s;

    private Vector2 LinePos(int index, float frac)
    {
        // index 0 = the piper; rat k follows at index k + 1.
        var a = _trail[Math.Min(index + 1, _trail.Count - 1)];
        var b = _trail[Math.Min(index, _trail.Count - 1)];
        return CellCentre(Vector2.Lerp(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y), frac));
    }

    public override void Draw(Gfx g)
    {
        DrawTown(g);

        float frac = _alive ? MathF.Min(1, _stepT / _stepTime) : 1;

        foreach (var r in _rats)
        {
            var p = CellCentre(Vector2.Lerp(new Vector2(r.From.X, r.From.Y), new Vector2(r.Cell.X, r.Cell.Y), MathF2.EaseInOut(r.Move)));
            DrawRat(g, p, r.Angle, r.Wiggle, 1.2f, 0.3f);
        }

        if (_alive && _trail.Count > 1)
        {
            // The conga line, tail first.
            for (int i = _line; i >= 1; i--)
            {
                var p = LinePos(i, frac);
                var ahead = LinePos(i - 1, frac);
                float ang = MathF2.Angle(ahead - p + new Vector2(0.001f, 0));
                float hop = MathF.Abs(MathF.Sin(Time * 14 + i * 0.8f)) * 1.5f;
                DrawRat(g, p - new Vector2(0, hop), ang, Time * 12 + i, 1.2f, (i % 3) * 0.4f);
            }
            var hp = LinePos(0, frac);
            float bob = MathF.Abs(MathF.Sin(frac * MathF.PI)) * 1.5f;
            g.Ellipse(hp.X, hp.Y + 8, 7, 3, Color.Black * 0.3f);
            g.Glow(hp, 22, Pal.Gold, 0.15f);
            g.PixelsCentered(PiperArt[(_trail.Count + (frac > 0.5f ? 1 : 0)) % 2], hp.X, hp.Y - 5 - bob, 2f, _dir.X < 0);
        }
        else if (!_alive && _trail.Count > 0)
        {
            var hp = CellCentre(_trail[0]);
            float spin = (1.6f - _dead) * 8;
            g.PixelsCentered(PiperArt[0], hp.X, hp.Y - 4, 1.7f, (int)spin % 2 == 0);
            for (int k = 0; k < 3; k++)
            {
                var star = hp + MathF2.FromAngle(Time * 5 + k * 2.1f, 10) - new Vector2(0, 16);
                g.Circle(star, 1.8f, Pal.Yellow);
            }
            g.TextShadow("OUCH!", hp.X, hp.Y - 34, 1.5f, Pal.Yellow, Align.Center);
        }

        // Rats leaping into the river.
        foreach (var j in _jumpers)
        {
            if (j.Delay > 0)
                continue;
            var p = Vector2.Lerp(j.From, j.To, j.T) - new Vector2(0, MathF.Sin(j.T * MathF.PI) * 26);
            DrawRat(g, p, MathF2.Angle(j.To - j.From) + (j.T - 0.5f) * 2, Time * 20, 1f, 0.2f);
        }

        if (_banner > 0)
        {
            float a = MathF.Min(1, _banner);
            g.RoundRect(170, 150, 300, 50, 10, Color.Black * (0.6f * a));
            g.TextShadow("PIPE THE RATS TO", 320, 157, 2f, Pal.Yellow * a, Align.Center);
            g.TextShadow("THE RIVER JETTIES", 320, 178, 2f, Pal.Cyan * a, Align.Center);
        }
    }

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        float s = r.H / 70f;
        g.Rect(r, new Color(58, 54, 56));
        // Cobbles.
        float cs = 7 * s;
        for (float y = r.Y; y < r.Bottom; y += cs)
            for (float x = r.X; x < r.Right; x += cs)
            {
                float h = Hash((int)(x / cs), (int)(y / cs));
                g.Ellipse(x + cs / 2, y + cs / 2, cs * 0.42f, cs * 0.34f, Pal.Lerp(new Color(92, 86, 88), new Color(112, 104, 100), h));
            }
        // River on the right.
        g.GradientV(r.Right - 26 * s, r.Y, 26 * s, r.H, new Color(26, 86, 160), new Color(18, 66, 136));
        for (int k = 0; k < 5; k++)
        {
            float wy = r.Y + Backdrops.Mod(time * 14 * s + k * 15 * s, r.H);
            g.Rect(r.Right - 22 * s + (k % 2) * 8 * s, wy, 7 * s, 1 * s, Pal.Sky * 0.5f);
        }
        // Rooftops along the top.
        g.GradientV(r.X, r.Y, r.W - 26 * s, 12 * s, new Color(150, 60, 40), new Color(110, 44, 34));
        g.Rect(r.X, r.Y + 12 * s, r.W - 26 * s, 4 * s, new Color(235, 220, 180));
        // Piper leading a line of rats across.
        float u = Backdrops.Mod(time * 0.25f, 1f);
        float px = r.X + 60 * s + u * (r.W - 100 * s);
        float py = r.CenterY + 8 * s;
        for (int k = 6; k >= 1; k--)
        {
            float rx = px - 4 * s - k * 15 * s;
            if (rx < r.X - 10 * s)
                continue;
            float hop = MathF.Abs(MathF.Sin(time * 12 + k)) * 1.5f * s;
            DrawRat(g, new Vector2(rx, py + 4 * s - hop), 0, time * 12 + k, 0.85f * s, (k % 3) * 0.4f);
        }
        g.Glow(px, py, 18 * s, Pal.Gold, 0.25f);
        g.PixelsCentered(PiperArt[(int)(time * 6) % 2], px, py - 2 * s, 1.5f * s);
        DrawBarrel(g, new Vector2(r.X + 20 * s, r.Bottom - 10 * s), 0.9f * s);
        // Notes floating from the pipe.
        for (int k = 0; k < 3; k++)
        {
            float t = Backdrops.Mod(time * 0.8f + k / 3f, 1f);
            var np = new Vector2(px + 10 * s + t * 12 * s, py - 6 * s - t * 22 * s);
            var col = Pal.Rainbow[k * 2] * (1 - t);
            g.Circle(np, 2 * s, col);
            g.Rect(np.X + 1.4f * s, np.Y - 6 * s, 0.9f * s, 6 * s, col);
        }
    }

    // ------------------------------------------------------------------ autopilot

    public override void AutoPlay(Controls c)
    {
        if (!_alive || _trail.Count == 0)
            return;
        // Head for a rat, or for the nearest jetty once the line is long (or there's no rat).
        bool deliver = _line >= 5 || (_rats.Count == 0 && _line > 0);
        var dir = PathStep(deliver);
        if (dir == Point.Zero)
            dir = SafestStep();
        c.SetDirections(dir.X, dir.Y);
    }

    private Point PathStep(bool toJetty)
    {
        var head = _trail[0];
        Array.Clear(_seen);
        for (int x = 0; x < Cols; x++)
            for (int y = 0; y < Rows; y++)
                _blocked[x, y] = Solid(new Point(x, y));
        for (int i = 1; i <= _line && i < _trail.Count; i++)
            _blocked[_trail[i].X, _trail[i].Y] = true;
        _bfs.Clear();
        _bfs.Enqueue(head);
        _seen[head.X, head.Y] = true;
        // Don't plan a reverse.
        var back = new Point(head.X - _dir.X, head.Y - _dir.Y);
        Point goal = Point.Zero;
        bool found = false;
        while (_bfs.Count > 0 && !found)
        {
            var p = _bfs.Dequeue();
            for (int k = 0; k < 4; k++)
            {
                var d = k == 0 ? Up : k == 1 ? Down : k == 2 ? Left : Right;
                var n = new Point(p.X + d.X, p.Y + d.Y);
                if (!InGrid(n) || _seen[n.X, n.Y] || _blocked[n.X, n.Y])
                    continue;
                if (p == head && n == back && _line > 0)
                    continue;
                bool jetty = _map[n.X, n.Y] == Tile.Jetty;
                if (jetty && !toJetty)
                    continue;
                _seen[n.X, n.Y] = true;
                _cameFrom[n.X, n.Y] = p;
                bool isGoal = toJetty ? jetty : HasRat(n);
                if (isGoal)
                {
                    goal = n;
                    found = true;
                    break;
                }
                if (!jetty)
                    _bfs.Enqueue(n);
            }
        }
        if (!found)
            return Point.Zero;
        var step = goal;
        while (_cameFrom[step.X, step.Y] != head)
            step = _cameFrom[step.X, step.Y];
        return new Point(step.X - head.X, step.Y - head.Y);
    }

    private bool HasRat(Point p)
    {
        foreach (var r in _rats)
            if (r.Cell == p)
                return true;
        return false;
    }

    private Point SafestStep()
    {
        var head = _trail[0];
        Point best = _dir;
        int bestFree = -1;
        for (int k = 0; k < 4; k++)
        {
            var d = k == 0 ? Up : k == 1 ? Down : k == 2 ? Left : Right;
            if (d.X == -_dir.X && d.Y == -_dir.Y && _line > 0)
                continue;
            var n = new Point(head.X + d.X, head.Y + d.Y);
            if (Solid(n) || OnLine(n, 1))
                continue;
            int free = 0;
            for (int j = 0; j < 4; j++)
            {
                var d2 = j == 0 ? Up : j == 1 ? Down : j == 2 ? Left : Right;
                var n2 = new Point(n.X + d2.X, n.Y + d2.Y);
                if (!Solid(n2) && !OnLine(n2, 1))
                    free++;
            }
            if (free > bestFree)
            {
                bestFree = free;
                best = d;
            }
        }
        return best;
    }
}
