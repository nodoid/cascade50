using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 44 Star Trek: the classic 1970s strategy game, with a bridge display. Command the starship
/// Endeavour across an 8x8 galaxy, hunt down every Kraal warship before the deadline, and dock
/// at starbases to refuel. Turn based: every action you take, the enemy answers.
/// </summary>
public sealed class StarTrek : MiniGame
{
    public override int Number => 44;
    public override string Title => "Star Trek";
    public override Category Category => Category.Brain;
    public override string Tagline => "Command the starship Endeavour and clear the galaxy of Kraal warships.";
    public override Color Accent => Pal.Gold;
    public override Pad Pad => Pad.None;

    public override string[] HowToPlay =>
    [
        "Destroy every Kraal warship before time runs out. Scan the chart to find them, then warp in.",
        "Phasers hit every enemy in the sector but weaken with range. Torpedoes kill outright. Kraal fire back after each action: keep shields up.",
        "Dock next to a starbase to refuel.",
    ];

    public override string[] DesktopControls =>
    [
        "C chart, L scan, W warp, I impulse, P phasers,",
        "T torpedo, S shields, D dock. 1-4 pick amounts.",
        "Click cells, or ARROWS + SPACE to aim.",
    ];

    public override string[] TouchControls => ["Tap a command, then tap a sector or quadrant.", "Tap the amount buttons for phasers and shields."];

    private const int N = 8;
    private const float Cell = 34, GridX = 34, GridY = 56, MaxEnergy = 3000, StartDate = 2300, Deadline = 30;

    private static readonly string[] QuadNames =
    [
        "ANTARES", "RIGEL", "PROCYON", "VEGA", "CANOPUS", "ALTAIR", "SAGITTARIUS", "POLLUX",
        "SIRIUS", "DENEB", "CAPELLA", "BETELGEUSE", "ALDEBARAN", "REGULUS", "ARCTURUS", "SPICA",
    ];

    private static readonly string[] Roman = ["I", "II", "III", "IV"];

    private struct Quad
    {
        public int Kraal, Bases, Stars;
        public bool Known;
    }

    private sealed class Kraal
    {
        public Point P;
        public float Energy, Max;
        public float Flash;
    }

    private enum Obj : byte
    {
        Empty,
        Star,
        Base,
    }

    private enum Cmd
    {
        None,
        Impulse,
        Torpedo,
        Phasers,
        Shields,
    }

    private readonly Quad[,] _gal = new Quad[N, N];
    private readonly Obj[,] _sec = new Obj[N, N];
    private readonly Color[,] _starCol = new Color[N, N];
    private readonly List<Kraal> _kraal = new();
    private readonly List<string> _log = new();
    private Point _quad, _ship, _cursor, _pick;
    private bool _chart, _docked;
    private Cmd _cmd;
    private float _energy, _shields, _date, _busy;
    private int _torps, _kraalLeft, _kraalTotal;

    // Animations.
    private readonly List<(Vector2 from, Vector2 to, Color col, float life)> _beams = new();
    private readonly List<Vector2> _torpPath = new();
    private float _torpT = -1;
    private Action _torpDone;
    private Vector2 _shipDraw, _moveFrom, _moveTo;
    private float _moveT = 1;
    private float _shieldFlash, _warpFx, _redAlert;

    protected override void Start()
    {
        Array.Clear(_gal);
        _kraalTotal = 0;
        int bases = 0;
        for (int x = 0; x < N; x++)
            for (int y = 0; y < N; y++)
            {
                var q = new Quad { Stars = RandInt(1, 7) };
                float r = (float)Rng.NextDouble();
                q.Kraal = r > 0.92f ? 3 : r > 0.84f ? 2 : r > 0.74f ? 1 : 0;
                _kraalTotal += q.Kraal;
                if (Chance(0.05f))
                {
                    q.Bases = 1;
                    bases++;
                }
                _gal[x, y] = q;
            }
        // Make sure there are enough enemies and at least three starbases.
        while (_kraalTotal < 12)
        {
            int x = RandInt(0, N), y = RandInt(0, N);
            if (_gal[x, y].Kraal < 3)
            {
                _gal[x, y].Kraal++;
                _kraalTotal++;
            }
        }
        while (_kraalTotal > 16)
        {
            int x = RandInt(0, N), y = RandInt(0, N);
            if (_gal[x, y].Kraal > 0)
            {
                _gal[x, y].Kraal--;
                _kraalTotal--;
            }
        }
        while (bases < 3)
        {
            int x = RandInt(0, N), y = RandInt(0, N);
            if (_gal[x, y].Bases == 0)
            {
                _gal[x, y].Bases = 1;
                bases++;
            }
        }
        _kraalLeft = _kraalTotal;
        _energy = MaxEnergy - 400;
        _shields = 400;
        _torps = 10;
        _date = StartDate;
        _log.Clear();
        _quad = new Point(RandInt(0, N), RandInt(0, N));
        // Begin somewhere quiet.
        _gal[_quad.X, _quad.Y].Kraal = 0;
        _kraalLeft = _kraalTotal = CountKraal();
        EnterQuadrant(false);
        Log("CAPTAIN: DESTROY " + _kraalTotal + " KRAAL BY STARDATE " + (StartDate + Deadline) + ".");
        _cursor = _ship;
        _pick = _quad;
        UpdateStatus();
    }

    private int CountKraal()
    {
        int n = 0;
        foreach (var q in _gal)
            n += q.Kraal;
        return n;
    }

    private static string QuadName(Point q) => QuadNames[q.Y * 2 + q.X / 4] + " " + Roman[q.X % 4];

    private void Log(string s)
    {
        _log.Insert(0, s);
        if (_log.Count > 3)
            _log.RemoveAt(3);
    }

    private void UpdateStatus()
    {
        Status = "STARDATE " + _date.ToString("0.0");
        Level = 0;
    }

    private void EnterQuadrant(bool hostileFirst)
    {
        Array.Clear(_sec);
        _kraal.Clear();
        _docked = false;
        ref var q = ref _gal[_quad.X, _quad.Y];
        q.Known = true;
        _ship = RandomEmpty();
        _shipDraw = CellCentre(_ship);
        _moveT = 1;
        for (int i = 0; i < q.Stars; i++)
        {
            var p = RandomEmpty();
            _sec[p.X, p.Y] = Obj.Star;
            _starCol[p.X, p.Y] = Pick(Pal.Yellow, Pal.Orange, Pal.White, Pal.Sky, new Color(255, 120, 90));
        }
        for (int i = 0; i < q.Bases; i++)
        {
            var p = RandomEmpty();
            _sec[p.X, p.Y] = Obj.Base;
        }
        for (int i = 0; i < q.Kraal; i++)
        {
            float e = Rand(150, 260) + (_date - StartDate) * 3;
            _kraal.Add(new Kraal { P = RandomEmpty(), Energy = e, Max = e });
        }
        _cursor = _ship;
        if (_kraal.Count > 0)
        {
            Log("RED ALERT! " + _kraal.Count + " KRAAL IN " + QuadName(_quad) + ".");
            Sound.Play(Sfx.Alarm, 0, 0.7f);
            _redAlert = 1.5f;
            if (hostileFirst && Chance(0.5f))
                EnemyTurn();
        }
        else
        {
            Log("ENTERING " + QuadName(_quad) + ".");
        }
    }

    private bool Occupied(Point p)
    {
        if (p == _ship || _sec[p.X, p.Y] != Obj.Empty)
            return true;
        foreach (var k in _kraal)
            if (k.P == p)
                return true;
        return false;
    }

    private Point RandomEmpty()
    {
        for (int i = 0; i < 500; i++)
        {
            var p = new Point(RandInt(0, N), RandInt(0, N));
            if (!Occupied(p))
                return p;
        }
        return new Point(0, 0);
    }

    private static Vector2 CellCentre(Point p) => new(GridX + (p.X + 0.5f) * Cell, GridY + (p.Y + 0.5f) * Cell);
    private static bool InGrid(Point p) => p.X >= 0 && p.Y >= 0 && p.X < N && p.Y < N;
    private static float CellDist(Point a, Point b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private bool BaseAdjacent()
    {
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                var p = new Point(_ship.X + dx, _ship.Y + dy);
                if (InGrid(p) && _sec[p.X, p.Y] == Obj.Base)
                    return true;
            }
        return false;
    }

    private int WarpEnergy(Point to) => 100 + 60 * Math.Max(Math.Abs(to.X - _quad.X), Math.Abs(to.Y - _quad.Y));

    private float WarpTime(Point to)
    {
        int d = Math.Max(Math.Abs(to.X - _quad.X), Math.Abs(to.Y - _quad.Y));
        return 0.6f + 0.4f * d;
    }

    // ------------------------------------------------------------------ update

    private static readonly RectF ViewRect = new(6, 26, 340, 330);
    private const float PanelX = 352;

    private RectF CmdRect(int i) => new(PanelX + (i % 2) * 142, 156 + (i / 2) * 35, 136, 31);
    private static RectF SubRect(int i) => new(PanelX + i * 71, 302, 67, 30);

    protected override void Update()
    {
        UpdateAnimations();
        UpdateStatus();
        if (IsOver)
            return;
        bool busy = _busy > 0 || _torpT >= 0 || _moveT < 1;

        // Commands.
        int kraalHere = _kraal.Count;
        const bool en = true;
        if (!busy & Ui.Button(CmdRect(0), _chart ? "SECTOR" : "CHART", Keys.C, en, new Color(40, 70, 120)))
        {
            _chart = !_chart;
            _cmd = Cmd.None;
            _pick = _quad;
        }
        if (!busy & Ui.Button(CmdRect(1), "SCAN", Keys.L, en, new Color(40, 70, 120)))
            LongRangeScan();
        if (!busy & Ui.Button(CmdRect(2), "WARP", Keys.W, en && (!_chart || _pick != _quad), new Color(70, 50, 130), _chart))
        {
            if (!_chart)
            {
                _chart = true;
                _pick = _quad;
                _cmd = Cmd.None;
                Log("SELECT A QUADRANT, THEN WARP.");
            }
            else
            {
                Warp(_pick);
            }
        }
        if (!busy & Ui.Button(CmdRect(3), "IMPULSE", Keys.I, en && !_chart, new Color(40, 100, 110), _cmd == Cmd.Impulse))
            SetCmd(Cmd.Impulse, "SELECT A SECTOR TO MOVE TO.");
        if (!busy & Ui.Button(CmdRect(4), "PHASERS", Keys.P, en && !_chart && kraalHere > 0, new Color(140, 80, 30), _cmd == Cmd.Phasers))
            SetCmd(Cmd.Phasers, "PHASER ENERGY?");
        if (!busy & Ui.Button(CmdRect(5), "TORPEDO " + _torps, Keys.T, en && !_chart && _torps > 0, new Color(140, 40, 40), _cmd == Cmd.Torpedo))
            SetCmd(Cmd.Torpedo, "SELECT A TARGET.");
        if (!busy & Ui.Button(CmdRect(6), "SHIELDS", Keys.S, en, new Color(40, 90, 140), _cmd == Cmd.Shields))
            SetCmd(Cmd.Shields, "TRANSFER ENERGY TO SHIELDS?");
        if (!busy & Ui.Button(CmdRect(7), "DOCK", Keys.D, en && !_chart && BaseAdjacent() && !_docked, new Color(40, 110, 60)))
            Dock();

        // Amounts.
        if (_cmd == Cmd.Phasers)
        {
            int[] amounts = [100, 250, 500, 1000];
            for (int i = 0; i < 4; i++)
                if (!busy & Ui.Button(SubRect(i), amounts[i].ToString(), Keys.D1 + i, en && _energy > amounts[i], new Color(140, 80, 30)))
                    FirePhasers(amounts[i]);
        }
        else if (_cmd == Cmd.Shields)
        {
            int[] amounts = [-250, 250, 500, 1000];
            for (int i = 0; i < 4; i++)
            {
                bool ok = amounts[i] < 0 ? _shields >= 1 : _energy > 1;
                if (!busy & Ui.Button(SubRect(i), (amounts[i] > 0 ? "+" : "") + amounts[i], Keys.D1 + i, en && ok, new Color(40, 90, 140)))
                    SetShields(amounts[i]);
            }
        }

        if (busy)
            return;

        // Aiming with the keyboard.
        if (In.KeyPressed(Keys.Left)) MoveCursor(-1, 0);
        if (In.KeyPressed(Keys.Right)) MoveCursor(1, 0);
        if (In.KeyPressed(Keys.Up)) MoveCursor(0, -1);
        if (In.KeyPressed(Keys.Down)) MoveCursor(0, 1);
        bool confirm = In.KeyPressed(Keys.Space) || In.KeyPressed(Keys.Enter);

        if (_chart)
        {
            if (In.PointerPressed)
            {
                var c = PointToCell(In.Pointer);
                if (InGrid(c))
                {
                    if (c == _pick && c != _quad)
                        Warp(c);
                    else
                        _pick = c;
                    Sound.Play(Sfx.Select, 0.1f, 0.5f);
                }
            }
            else if (confirm && _pick != _quad)
            {
                Warp(_pick);
            }
            return;
        }

        if (_cmd is Cmd.Impulse or Cmd.Torpedo)
        {
            Point? target = null;
            if (In.PointerPressed)
            {
                var c = PointToCell(In.Pointer);
                if (InGrid(c))
                    target = c;
            }
            else if (confirm)
            {
                target = _cursor;
            }
            if (target is Point t && t != _ship)
            {
                _cursor = t;
                if (_cmd == Cmd.Impulse)
                    Impulse(t);
                else
                    FireTorpedo(t);
            }
        }
        else if (In.PointerPressed)
        {
            // Tapping a sector with no command picked: torpedo at Kraal, otherwise impulse.
            var c = PointToCell(In.Pointer);
            if (InGrid(c) && c != _ship)
            {
                _cursor = c;
                bool enemy = false;
                foreach (var k in _kraal)
                    if (k.P == c)
                        enemy = true;
                SetCmd(enemy && _torps > 0 ? Cmd.Torpedo : Cmd.Impulse, enemy ? "TAP AGAIN TO FIRE A TORPEDO." : "TAP AGAIN TO MOVE THERE.");
            }
        }
    }

    private void MoveCursor(int dx, int dy)
    {
        if (_chart)
            _pick = new Point(Math.Clamp(_pick.X + dx, 0, N - 1), Math.Clamp(_pick.Y + dy, 0, N - 1));
        else
            _cursor = new Point(Math.Clamp(_cursor.X + dx, 0, N - 1), Math.Clamp(_cursor.Y + dy, 0, N - 1));
        Sound.Play(Sfx.Tick, 0.2f, 0.3f);
    }

    private static Point PointToCell(Vector2 p) =>
        new((int)MathF.Floor((p.X - GridX) / Cell), (int)MathF.Floor((p.Y - GridY) / Cell));

    private void SetCmd(Cmd c, string msg)
    {
        _cmd = _cmd == c ? Cmd.None : c;
        if (_cmd != Cmd.None)
            Log(msg);
    }

    // ------------------------------------------------------------------ actions

    private void LongRangeScan()
    {
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                int x = _quad.X + dx, y = _quad.Y + dy;
                if (x >= 0 && y >= 0 && x < N && y < N)
                    _gal[x, y].Known = true;
            }
        _chart = true;
        _pick = _quad;
        Sound.Play(Sfx.Beep, 0.3f, 0.6f);
        Log("LONG-RANGE SCAN COMPLETE.");
    }

    private void Warp(Point to)
    {
        if (to == _quad)
            return;
        int cost = WarpEnergy(to);
        if (cost > _energy)
        {
            Log("NOT ENOUGH ENERGY TO WARP.");
            Sound.Play(Sfx.Wrong);
            CheckEnd();
            return;
        }
        _energy -= cost;
        _date += WarpTime(to);
        _quad = to;
        _chart = false;
        _cmd = Cmd.None;
        _warpFx = 1;
        Sound.Play(Sfx.Warp);
        EnterQuadrant(true);
        CheckEnd();
    }

    private void Impulse(Point to)
    {
        var start = _ship;
        var d = new Vector2(to.X - start.X, to.Y - start.Y);
        float len = d.Length();
        d /= len;
        Point last = start;
        for (float s = 0.25f; s <= len + 0.01f; s += 0.25f)
        {
            var p = new Point((int)MathF.Round(start.X + d.X * s), (int)MathF.Round(start.Y + d.Y * s));
            if (p == last)
                continue;
            if (!InGrid(p) || Occupied(p))
                break;
            last = p;
        }
        if (last == start)
        {
            Log("COURSE BLOCKED.");
            Sound.Play(Sfx.Wrong, 0, 0.5f);
            return;
        }
        float cells = CellDist(start, last);
        _energy -= 10 * cells;
        _date += 0.1f;
        _moveFrom = CellCentre(start);
        _moveTo = CellCentre(last);
        _moveT = 0;
        _ship = last;
        _docked = false;
        _cmd = Cmd.None;
        _cursor = last;
        Sound.Play(Sfx.Whoosh, -0.2f, 0.6f);
        _busy = 0.45f;
        _afterMove = true;
    }

    private bool _afterMove;

    private void FirePhasers(int amount)
    {
        _energy -= amount;
        _cmd = Cmd.None;
        int n = _kraal.Count;
        Sound.Play(Sfx.Laser, -0.2f);
        var from = CellCentre(_ship);
        for (int i = _kraal.Count - 1; i >= 0; i--)
        {
            var k = _kraal[i];
            float dist = CellDist(_ship, k.P);
            float hit = amount / (float)n * (3f / (dist + 2f)) * Rand(0.8f, 1.2f);
            k.Energy -= hit;
            k.Flash = 0.5f;
            var to = CellCentre(k.P);
            _beams.Add((from, to, Pal.Orange, 0.5f));
            Fx.Float(((int)hit).ToString(), to.X, to.Y - 16, Pal.Orange, 1.2f);
            if (k.Energy <= 0)
                DestroyKraal(i);
        }
        _busy = 0.6f;
        EnemyTurn();
        CheckEnd();
    }

    private void FireTorpedo(Point target)
    {
        _torps--;
        _cmd = Cmd.None;
        _torpPath.Clear();
        var start = CellCentre(_ship);
        var dir = CellCentre(target) - start;
        dir.Normalize();
        _torpPath.Add(start);
        Action result = () => Log("TORPEDO MISSED.");
        var pos = start;
        Point lastCell = _ship;
        for (int step = 0; step < 200; step++)
        {
            pos += dir * (Cell * 0.2f);
            var c = PointToCell(pos);
            if (!InGrid(c))
                break;
            if (c == lastCell)
                continue;
            lastCell = c;
            int ki = _kraal.FindIndex(k => k.P == c);
            if (ki >= 0)
            {
                var kk = _kraal[ki];
                result = () =>
                {
                    int idx = _kraal.IndexOf(kk);
                    if (idx >= 0)
                        DestroyKraal(idx);
                };
                pos = CellCentre(c);
                break;
            }
            if (_sec[c.X, c.Y] == Obj.Star)
            {
                var cc = c;
                result = () =>
                {
                    Log("THE STAR AT " + (cc.X + 1) + "," + (cc.Y + 1) + " ABSORBED THE TORPEDO.");
                    Fx.Burst(CellCentre(cc).X, CellCentre(cc).Y, Pal.Yellow, 14, 70, 0.5f, 2f);
                    Sound.Play(Sfx.Thud, 0.2f, 0.6f);
                };
                pos = CellCentre(c);
                break;
            }
            if (_sec[c.X, c.Y] == Obj.Base)
            {
                var cc = c;
                result = () =>
                {
                    _sec[cc.X, cc.Y] = Obj.Empty;
                    _gal[_quad.X, _quad.Y].Bases = 0;
                    Fx.Explode(CellCentre(cc).X, CellCentre(cc).Y, 1.5f);
                    Sound.Play(Sfx.BigExplode);
                    Log("YOU DESTROYED A STARBASE! COURT MARTIAL PENDING.");
                };
                pos = CellCentre(c);
                break;
            }
        }
        _torpPath.Add(pos);
        _torpT = 0;
        _torpDone = () =>
        {
            result();
            EnemyTurn();
            CheckEnd();
        };
        Sound.Play(Sfx.Shoot, -0.3f);
    }

    private void SetShields(int delta)
    {
        float before = _shields;
        if (delta > 0)
            delta = (int)MathF.Min(delta, _energy - 1);
        else
            delta = (int)MathF.Max(delta, -_shields);
        _shields += delta;
        _energy -= delta;
        _cmd = Cmd.None;
        Sound.Play(delta > 0 ? Sfx.PowerUp : Sfx.Pop, 0, 0.6f);
        Log("SHIELDS NOW " + (int)_shields + ".");
        if (before != _shields && _kraal.Count > 0)
        {
            _busy = 0.3f;
            EnemyTurn();
        }
        CheckEnd();
    }

    private void Dock()
    {
        _docked = true;
        _energy = MaxEnergy - _shields;
        if (_shields < 500)
        {
            _shields = 500;
            _energy = MaxEnergy - 500;
        }
        _torps = 10;
        _date += 0.2f;
        _cmd = Cmd.None;
        Sound.Play(Sfx.PowerUp);
        Sound.Play(Sfx.Bell, 0.3f, 0.5f);
        Log("DOCKED. ENERGY AND TORPEDOES REPLENISHED.");
        var c = CellCentre(_ship);
        Fx.Burst(c.X, c.Y, Pal.Cyan, 24, 80, 0.7f, 2f);
        CheckEnd();
    }

    private void DestroyKraal(int i)
    {
        var k = _kraal[i];
        var c = CellCentre(k.P);
        _kraal.RemoveAt(i);
        _gal[_quad.X, _quad.Y].Kraal--;
        _kraalLeft--;
        AddScore(150, c.X, c.Y - 20, Pal.Gold);
        Fx.Explode(c.X, c.Y, 1.3f);
        Fx.Burst(c.X, c.Y, Pal.Lime, 20, 120, 0.7f, 2.5f);
        Sound.Play(Sfx.BigExplode, Rand(-0.3f, 0.1f));
        Log("KRAAL WARSHIP DESTROYED! " + _kraalLeft + " REMAIN.");
    }

    private void EnemyTurn()
    {
        if (_kraal.Count == 0)
            return;
        if (_docked)
        {
            Log("STARBASE SHIELDS PROTECT THE ENDEAVOUR.");
            return;
        }
        var to = CellCentre(_ship);
        float total = 0;
        foreach (var k in _kraal)
        {
            float dist = CellDist(_ship, k.P);
            float dmg = k.Energy * Rand(0.5f, 1.1f) / (dist * 0.5f + 1) * 0.55f;
            total += dmg;
            _beams.Add((CellCentre(k.P), to, Pal.Lime, 0.5f));
        }
        Sound.Play(Sfx.Zap, -0.4f, 0.7f);
        _shieldFlash = 0.5f;
        _shields -= total;
        if (_shields < 0)
        {
            _energy += _shields;
            Fx.Shake(4, 0.3f);
            Sound.Play(Sfx.Hurt);
            Log("HIT FOR " + (int)total + "! SHIELDS DOWN, HULL DAMAGED.");
            _shields = 0;
        }
        else
        {
            Log("KRAAL FIRE: " + (int)total + " ABSORBED. SHIELDS " + (int)_shields + ".");
            Fx.Shake(1.5f, 0.2f);
        }
        // Some of them manoeuvre.
        foreach (var k in _kraal)
            if (Chance(0.3f))
            {
                var p = new Point(k.P.X + RandInt(-1, 2), k.P.Y + RandInt(-1, 2));
                if (InGrid(p) && !Occupied(p))
                    k.P = p;
            }
    }

    private void CheckEnd()
    {
        if (IsOver)
            return;
        if (_energy <= 0)
        {
            _energy = 0;
            var c = CellCentre(_ship);
            Fx.Explode(c.X, c.Y, 2.5f);
            Lives = 0;
            EndGame(false, "The Endeavour has been destroyed.");
            return;
        }
        if (_kraalLeft <= 0)
        {
            int timeBonus = (int)(MathF.Max(0, StartDate + Deadline - _date) * 100);
            AddScore(500 + timeBonus + (int)(_energy / 5), 176, 180, Pal.Gold);
            EndGame(true, "The galaxy is safe. Well done, Captain!");
            return;
        }
        if (_date >= StartDate + Deadline)
        {
            EndGame(false, "Out of time: " + _kraalLeft + " Kraal remain.");
            return;
        }
        if (_energy + _shields < 30 && !BaseAdjacent() || _energy + _shields < 160 && _kraal.Count == 0 && _gal[_quad.X, _quad.Y].Bases == 0)
            EndGame(false, "The Endeavour is stranded without power.");
    }

    private void UpdateAnimations()
    {
        if (_busy > 0)
            _busy -= Dt;
        if (_shieldFlash > 0)
            _shieldFlash -= Dt;
        if (_warpFx > 0)
            _warpFx -= Dt * 1.2f;
        if (_redAlert > 0)
            _redAlert -= Dt;
        for (int i = _beams.Count - 1; i >= 0; i--)
        {
            var b = _beams[i];
            b.life -= Dt;
            if (b.life <= 0)
                _beams.RemoveAt(i);
            else
                _beams[i] = b;
        }
        foreach (var k in _kraal)
            if (k.Flash > 0)
                k.Flash -= Dt;
        if (_moveT < 1)
        {
            _moveT = MathF.Min(1, _moveT + Dt * 2.5f);
            _shipDraw = Vector2.Lerp(_moveFrom, _moveTo, MathF2.EaseInOut(_moveT));
            if (_moveT >= 1 && _afterMove)
            {
                _afterMove = false;
                EnemyTurn();
                CheckEnd();
            }
        }
        else
        {
            _shipDraw = CellCentre(_ship);
        }
        if (_torpT >= 0)
        {
            float len = Vector2.Distance(_torpPath[0], _torpPath[^1]);
            _torpT += Dt * 300 / MathF.Max(1, len);
            var p = Vector2.Lerp(_torpPath[0], _torpPath[^1], MathF.Min(1, _torpT));
            Fx.Spark(p.X, p.Y, Rand(-10, 10), Rand(-10, 10), Pal.Orange, 0.3f, 1.6f);
            if (_torpT >= 1)
            {
                _torpT = -1;
                var done = _torpDone;
                _torpDone = null;
                done?.Invoke();
                _busy = 0.5f;
            }
        }
    }

    // ------------------------------------------------------------------ drawing

    private static readonly Vector2[] KraalShape = [new(14, 0), new(-4, -13), new(-10, -11), new(-6, 0), new(-10, 11), new(-4, 13)];

    private static void DrawEndeavour(Gfx g, Vector2 p, float s, float t, bool glow = true)
    {
        if (glow)
            g.Glow(p, 22 * s, Pal.Sky, 0.35f);
        // Nacelles.
        g.RoundRect(p.X - 11 * s, p.Y + 1 * s, 4 * s, 14 * s, 2 * s, new Color(170, 180, 200));
        g.RoundRect(p.X + 7 * s, p.Y + 1 * s, 4 * s, 14 * s, 2 * s, new Color(170, 180, 200));
        g.Rect(p.X - 10 * s, p.Y + 1.5f * s, 2 * s, 3 * s, Pal.Red);
        g.Rect(p.X + 8 * s, p.Y + 1.5f * s, 2 * s, 3 * s, Pal.Red);
        g.Line(p.X - 8 * s, p.Y + 8 * s, p.X - 2 * s, p.Y + 5 * s, 1.5f * s, new Color(150, 160, 180));
        g.Line(p.X + 8 * s, p.Y + 8 * s, p.X + 2 * s, p.Y + 5 * s, 1.5f * s, new Color(150, 160, 180));
        float glowK = 0.6f + 0.4f * MathF.Sin(t * 5);
        g.Glow(p.X - 9 * s, p.Y + 13 * s, 5 * s, Pal.Cyan, glowK);
        g.Glow(p.X + 9 * s, p.Y + 13 * s, 5 * s, Pal.Cyan, glowK);
        // Engineering hull and neck.
        g.Ellipse(p.X, p.Y + 6 * s, 3 * s, 7 * s, new Color(190, 200, 215));
        // Saucer.
        g.Circle(p.X, p.Y - 5 * s, 9 * s, new Color(120, 130, 150));
        g.Circle(p.X, p.Y - 5 * s, 8 * s, new Color(210, 218, 230));
        g.Ring(p.X, p.Y - 5 * s, 5 * s, 0.8f * s, new Color(150, 160, 180));
        g.Circle(p.X, p.Y - 5 * s, 2 * s, Pal.Sky);
    }

    private static void DrawKraal(Gfx g, Vector2 p, float s, float t, float flash)
    {
        g.Glow(p, 22 * s, Pal.Lime, 0.25f + flash);
        float ang = -MathF.PI / 2 + MathF.Sin(t * 1.3f + p.X) * 0.08f;
        g.Shape(KraalShape, p, ang, s * 1.05f, new Color(20, 60, 30));
        g.Shape(KraalShape, p, ang, s, Pal.Lerp(new Color(70, 110, 60), Pal.White, flash * 1.5f));
        g.ShapeOutline(KraalShape, p, ang, s, 1.2f * s, Pal.Lime * 0.8f);
        g.Circle(p.X, p.Y + 2 * s, 2.5f * s, Pal.Red);
        g.Glow(p.X, p.Y + 2 * s, 6 * s, Pal.Red, 0.6f);
    }

    private static void DrawBase(Gfx g, Vector2 p, float s, float t)
    {
        g.Glow(p, 20 * s, Pal.Cyan, 0.35f);
        g.Ring(p.X, p.Y, 11 * s, 2.5f * s, new Color(150, 170, 200));
        for (int i = 0; i < 4; i++)
        {
            float a = t * 0.8f + i * MathF.PI / 2;
            g.Line(p.X, p.Y, p.X + MathF.Cos(a) * 11 * s, p.Y + MathF.Sin(a) * 11 * s, 1.5f * s, new Color(120, 140, 170));
            g.Circle(p.X + MathF.Cos(a) * 11 * s, p.Y + MathF.Sin(a) * 11 * s, 2 * s, Pal.Cyan);
        }
        g.Circle(p.X, p.Y, 4.5f * s, new Color(200, 210, 230));
        g.Circle(p.X, p.Y, 2 * s, Pal.Cyan);
    }

    private static void DrawSun(Gfx g, Vector2 p, float s, Color c, float t)
    {
        g.Glow(p, 20 * s, c, 0.6f + 0.1f * MathF.Sin(t * 2 + p.X));
        g.Circle(p.X, p.Y, 6 * s, Pal.Lighten(c, 0.3f));
        g.Circle(p.X - 1.5f * s, p.Y - 1.5f * s, 3 * s, Pal.White);
    }

    public override void Draw(Gfx g)
    {
        Backdrops.Space(g, Time, 3, 44, g.Visible);
        g.Glow(520, 300, 200, new Color(60, 30, 120), 0.25f);

        // Viewscreen frame.
        g.Panel(ViewRect, new Color(4, 6, 20) * 0.85f, new Color(200, 160, 60), 10);
        g.SetClip(ViewRect.Inflate(-2, -2));
        Backdrops.Stars(g, Time, _warpFx > 0 ? 600 * _warpFx : 4, 9, ViewRect, 60);
        if (_chart)
            DrawChart(g);
        else
            DrawSector(g);
        g.SetClip(null);

        // Log.
        for (int i = 0; i < Math.Min(2, _log.Count); i++)
            g.Text(_log[i], ViewRect.X + 10, ViewRect.Bottom - 23 + i * 10, 1f, i == 0 ? Pal.Ice : Pal.Grey * 0.8f);
        DrawPanel(g);

        if (_redAlert > 0 && (int)(_redAlert * 4) % 2 == 0)
            g.TextShadow("RED ALERT", ViewRect.CenterX, ViewRect.Y + 140, 3f, Pal.Red, Align.Center);
    }

    private void DrawSector(Gfx g)
    {
        var gridCol = new Color(60, 110, 160) * 0.35f;
        for (int i = 0; i <= N; i++)
        {
            g.Rect(GridX + i * Cell, GridY, 1, N * Cell, gridCol);
            g.Rect(GridX, GridY + i * Cell, N * Cell, 1, gridCol);
        }
        for (int i = 0; i < N; i++)
        {
            g.Text((i + 1).ToString(), GridX + (i + 0.5f) * Cell, GridY - 10, 1f, Pal.Grey * 0.7f, Align.Center);
            g.Text((i + 1).ToString(), GridX - 10, GridY + (i + 0.5f) * Cell - 4, 1f, Pal.Grey * 0.7f, Align.Center);
        }
        // Targeting overlay.
        if (_cmd is Cmd.Impulse or Cmd.Torpedo)
        {
            var cc = CellCentre(_cursor);
            var col = _cmd == Cmd.Torpedo ? Pal.Red : Pal.Cyan;
            g.Line(_shipDraw, cc, 1, col * 0.5f);
            g.RectOutline(GridX + _cursor.X * Cell + 2, GridY + _cursor.Y * Cell + 2, Cell - 4, Cell - 4, 1.5f, col * (0.6f + 0.4f * MathF2.Pulse(Time, 0.5f)));
        }
        for (int x = 0; x < N; x++)
            for (int y = 0; y < N; y++)
            {
                var p = CellCentre(new Point(x, y));
                if (_sec[x, y] == Obj.Star)
                    DrawSun(g, p, 1.2f, _starCol[x, y], Time);
                else if (_sec[x, y] == Obj.Base)
                    DrawBase(g, p, 1.2f, Time);
            }
        foreach (var k in _kraal)
        {
            var p = CellCentre(k.P);
            DrawKraal(g, p, 1.05f, Time, MathF.Max(0, k.Flash));
            float f = MathF2.Clamp(k.Energy / k.Max, 0, 1);
            g.Rect(p.X - 12, p.Y + 14, 24, 3, new Color(40, 20, 20));
            g.Rect(p.X - 12, p.Y + 14, 24 * f, 3, f > 0.5f ? Pal.Lime : Pal.Orange);
        }
        if (!IsOver || _energy > 0)
        {
            DrawEndeavour(g, _shipDraw, 1.05f, Time);
            if (_shieldFlash > 0 || _shields > 0)
            {
                float k = _shieldFlash > 0 ? _shieldFlash * 2 : 0.15f;
                g.Ring(_shipDraw.X, _shipDraw.Y, 17, 1.5f + k * 2, Pal.Sky * MathF.Min(1, k + 0.1f), 36);
                if (_shieldFlash > 0)
                    g.Glow(_shipDraw, 26, Pal.Sky, k * 0.6f);
            }
            if (_docked)
                g.Text("DOCKED", _shipDraw.X, _shipDraw.Y + 16, 1f, Pal.Cyan, Align.Center);
        }
        foreach (var b in _beams)
        {
            float k = b.life / 0.5f;
            var wob = new Vector2(MathF.Sin(Time * 60) * 1.5f, 0);
            g.GlowLine(b.from, b.to + wob, 1.8f * k + 0.4f, b.col * k);
        }
        if (_torpT >= 0)
        {
            var p = Vector2.Lerp(_torpPath[0], _torpPath[^1], MathF.Min(1, _torpT));
            g.Glow(p, 14, Pal.Orange, 0.9f);
            g.Circle(p.X, p.Y, 3, Pal.White);
        }
        g.Text("SECTOR VIEW: " + QuadName(_quad), ViewRect.X + 10, ViewRect.Y + 6, 1f, Pal.Gold);
    }

    private void DrawChart(Gfx g)
    {
        var gridCol = new Color(200, 160, 60) * 0.3f;
        for (int i = 0; i <= N; i++)
        {
            g.Rect(GridX + i * Cell, GridY, 1, N * Cell, gridCol);
            g.Rect(GridX, GridY + i * Cell, N * Cell, 1, gridCol);
        }
        for (int x = 0; x < N; x++)
            for (int y = 0; y < N; y++)
            {
                var q = _gal[x, y];
                float cx = GridX + x * Cell, cy = GridY + y * Cell;
                if (!q.Known)
                {
                    g.Rect(cx + 1, cy + 1, Cell - 1, Cell - 1, new Color(20, 20, 40) * 0.6f);
                    g.Text("?", cx + Cell / 2, cy + Cell / 2 - 4, 1f, Pal.DarkGrey, Align.Center);
                    continue;
                }
                if (q.Kraal > 0)
                    g.Rect(cx + 1, cy + 1, Cell - 1, Cell - 1, new Color(120, 20, 20) * 0.35f);
                for (int i = 0; i < q.Kraal; i++)
                {
                    var p = new Vector2(cx + 7 + i * 8, cy + 7);
                    g.Shape(KraalShape, p, -MathF.PI / 2, 0.28f, Pal.Lime);
                }
                if (q.Bases > 0)
                    DrawBase(g, new Vector2(cx + Cell - 8, cy + Cell - 8), 0.38f, Time);
                for (int i = 0; i < q.Stars; i++)
                    g.Circle(cx + 5 + i * 3f, cy + Cell - 5, 1f, Pal.Yellow * 0.8f);
                g.Text(q.Kraal + "" + q.Bases + q.Stars, cx + 4, cy + 14, 1f, Pal.LightGrey * 0.6f);
            }
        var cur = new RectF(GridX + _quad.X * Cell, GridY + _quad.Y * Cell, Cell, Cell);
        g.RectOutline(cur.Inflate(-1, -1), 2, Pal.White);
        DrawEndeavour(g, new Vector2(cur.Right - 8, cur.Y + 9), 0.38f, Time, false);
        if (_pick != _quad)
        {
            var pr = new RectF(GridX + _pick.X * Cell, GridY + _pick.Y * Cell, Cell, Cell);
            g.RectOutline(pr.Inflate(-1, -1), 2, Pal.Gold * (0.6f + 0.4f * MathF2.Pulse(Time, 0.6f)));
            g.Line(cur.Center, pr.Center, 1, Pal.Gold * 0.5f);
            g.Text(QuadName(_pick) + "  ENERGY " + WarpEnergy(_pick) + "  TIME " + WarpTime(_pick).ToString("0.0"), ViewRect.X + 10, ViewRect.Y + 6, 1f, Pal.Gold);
        }
        else
        {
            g.Text("GALACTIC CHART - TAP A QUADRANT", ViewRect.X + 10, ViewRect.Y + 6, 1f, Pal.Gold);
        }
    }

    private void DrawPanel(Gfx g)
    {
        var r = new RectF(PanelX - 2, 26, 288, 124);
        g.Panel(r, new Color(10, 12, 30) * 0.92f, new Color(200, 160, 60), 10);
        string cond = _docked ? "DOCKED" : _kraal.Count > 0 ? "RED" : _energy < 600 ? "YELLOW" : "GREEN";
        var cc = _docked ? Pal.Cyan : _kraal.Count > 0 ? Pal.Red : _energy < 600 ? Pal.Yellow : Pal.Green;
        float x = r.X + 10, y = r.Y + 8;
        g.Text("STARSHIP ENDEAVOUR", x, y, 1f, Pal.Gold);
        g.Text("CONDITION", r.Right - 10, y, 1f, Pal.Grey, Align.Right);
        g.TextShadow(cond, r.Right - 10, y + 10, 1.5f, cc * (cond == "RED" ? 0.7f + 0.3f * MathF2.Pulse(Time, 0.5f) : 1), Align.Right);
        g.Text("QUADRANT", x, y + 14, 1f, Pal.Grey);
        g.Text(QuadName(_quad), x + 54, y + 14, 1f, Pal.White);
        // Bars.
        void Bar(string label, float v, float max, float by, Color c)
        {
            g.Text(label, x, by, 1f, Pal.Grey);
            g.Rect(x + 60, by, 150, 8, new Color(30, 30, 50));
            g.Rect(x + 60, by, 150 * MathF2.Clamp(v / max, 0, 1), 8, c);
            g.Text(((int)v).ToString(), r.Right - 10, by, 1f, Pal.White, Align.Right);
        }
        Bar("ENERGY", _energy, MaxEnergy, y + 30, _energy < 600 ? Pal.Orange : Pal.Lime);
        Bar("SHIELDS", _shields, 1500, y + 44, Pal.Sky);
        float left = MathF.Max(0, StartDate + Deadline - _date);
        Bar("TIME", left, Deadline, y + 58, left < 5 ? Pal.Red : Pal.Gold);
        g.Text("TORPEDOES", x, y + 76, 1f, Pal.Grey);
        for (int i = 0; i < 10; i++)
            g.Rect(x + 60 + i * 9, y + 75, 6, 9, i < _torps ? Pal.Orange : new Color(50, 40, 40));
        g.Text("KRAAL LEFT", x, y + 94, 1f, Pal.Grey);
        g.TextShadow(_kraalLeft + " / " + _kraalTotal, x + 70, y + 91, 1.5f, Pal.Lime);
        g.Text("SD " + _date.ToString("0.0"), r.Right - 10, y + 94, 1f, Pal.LightGrey, Align.Right);

        if (_cmd == Cmd.Phasers || _cmd == Cmd.Shields)
            g.Text(_cmd == Cmd.Phasers ? "PHASER ENERGY:" : "SHIELD TRANSFER:", PanelX, 292, 1f, Pal.Gold);
        else if (_cmd is Cmd.Impulse or Cmd.Torpedo)
            g.TextWrapped(_cmd == Cmd.Impulse ? "TAP A SECTOR TO MOVE THERE." : "TAP A TARGET TO FIRE A TORPEDO.", PanelX, 304, 280, 1.5f, Pal.Gold);
        else if (_chart)
            g.TextWrapped("TAP A QUADRANT, THEN WARP.", PanelX, 304, 280, 1.5f, Pal.Gold);
    }

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        Backdrops.Space(g, time, 6, 44, r);
        float s = r.H / 162f;
        // Bridge viewscreen frame.
        g.RectOutline(r.Inflate(-4 * s, -4 * s), 2 * s, new Color(200, 160, 60) * 0.7f);
        var ship = new Vector2(r.X + r.W * 0.3f, r.CenterY + 20 * s);
        var foe = new Vector2(r.X + r.W * 0.72f, r.CenterY - 22 * s + MathF.Sin(time) * 6 * s);
        DrawSun(g, new Vector2(r.X + r.W * 0.85f, r.Y + r.H * 0.75f), 2 * s, Pal.Orange, time);
        DrawBase(g, new Vector2(r.X + r.W * 0.14f, r.Y + r.H * 0.25f), 1.4f * s, time);
        float cycle = time % 2.5f;
        if (cycle < 0.7f)
            g.GlowLine(ship + new Vector2(0, -12 * s), foe, 2 * s * (1 - cycle / 0.7f) + 0.5f, Pal.Orange);
        else if (cycle > 1.4f && cycle < 2.2f)
        {
            float k = (cycle - 1.4f) / 0.8f;
            var p = Vector2.Lerp(foe, ship, k);
            g.Glow(p, 10 * s, Pal.Lime, 0.9f);
            g.Circle(p.X, p.Y, 2.5f * s, Pal.White);
        }
        DrawKraal(g, foe, 2.2f * s, time, cycle < 0.7f ? 0.4f : 0);
        DrawEndeavour(g, ship, 2.6f * s, time);
        if (cycle > 2.1f)
            g.Ring(ship.X, ship.Y, 40 * s, 3 * s, Pal.Sky * (1 - (cycle - 2.1f) / 0.4f));
    }

    // ------------------------------------------------------------------ autoplay

    private int _autoWait;
    private Point _autoTarget = new(-1, -1);

    public override void AutoPlay(Controls c)
    {
        if (_busy > 0 || _torpT >= 0 || _moveT < 1)
            return;
        if (--_autoWait > 0)
            return;
        _autoWait = 26;

        if (_energy < 450 && _shields >= 1 && (_kraal.Count == 0 || _shields > 400))
        {
            PressCmd(c, Cmd.Shields, Keys.S, Keys.D1);
            return;
        }
        if (_kraal.Count > 0 && !_chart)
        {
            if (_shields < 300 && _energy > 900)
            {
                PressCmd(c, Cmd.Shields, Keys.S, Keys.D3);
                return;
            }
            // Torpedo a Kraal with a clear line, else phasers.
            if (_torps > 0)
                foreach (var k in _kraal)
                    if (ClearShot(k.P))
                    {
                        if (_cmd != Cmd.Torpedo)
                        {
                            c.PressedKeys.Add(Keys.T);
                            return;
                        }
                        Tap(c, CellCentre(k.P));
                        return;
                    }
            if (_energy > 700)
            {
                PressCmd(c, Cmd.Phasers, Keys.P, _energy > 2000 ? Keys.D3 : Keys.D2);
                return;
            }
        }
        if (_chart)
        {
            // Pick the nearest known enemy quadrant (or the nearest unknown one) and warp.
            if (_autoTarget.X < 0 || _gal[_autoTarget.X, _autoTarget.Y].Kraal == 0 && _gal[_autoTarget.X, _autoTarget.Y].Known && !NeedBase())
                _autoTarget = ChooseQuadrant();
            if (_pick != _autoTarget)
            {
                Tap(c, CellCentre(_autoTarget));
                return;
            }
            c.PressedKeys.Add(Keys.W);
            _autoTarget = new Point(-1, -1);
            return;
        }
        if (_cmd != Cmd.None)
        {
            c.PressedKeys.Add(_cmd == Cmd.Impulse ? Keys.I : _cmd == Cmd.Torpedo ? Keys.T : _cmd == Cmd.Phasers ? Keys.P : Keys.S);
            return;
        }
        // Refuel if a starbase is here and we need it.
        if (NeedBase() || _torps < 10 && _energy < 2500)
        {
            if (BaseAdjacent() && !_docked)
            {
                c.PressedKeys.Add(Keys.D);
                return;
            }
            for (int x = 0; x < N; x++)
                for (int y = 0; y < N; y++)
                    if (_sec[x, y] == Obj.Base && !_docked)
                    {
                        // Move next to it.
                        for (int dx = -1; dx <= 1; dx++)
                            for (int dy = -1; dy <= 1; dy++)
                            {
                                var p = new Point(x + dx, y + dy);
                                if (InGrid(p) && !Occupied(p))
                                {
                                    _cmd = Cmd.Impulse;
                                    _cursor = p;
                                    c.PressedKeys.Add(Keys.Space);
                                    return;
                                }
                            }
                    }
        }
        bool unknownNear = false;
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                int x = _quad.X + dx, y = _quad.Y + dy;
                if (x >= 0 && y >= 0 && x < N && y < N && !_gal[x, y].Known)
                    unknownNear = true;
            }
        c.PressedKeys.Add(unknownNear ? Keys.L : Keys.C);
    }

    private bool NeedBase() => _energy < 1100 || _torps < 3;

    private void PressCmd(Controls c, Cmd cmd, Keys key, Keys amount)
    {
        if (_cmd != cmd)
            c.PressedKeys.Add(key);
        else
            c.PressedKeys.Add(amount);
    }

    private static void Tap(Controls c, Vector2 p)
    {
        c.Pointer = p;
        c.PointerPressed = true;
        c.PointerDown = true;
    }

    private bool ClearShot(Point target)
    {
        var start = CellCentre(_ship);
        var dir = CellCentre(target) - start;
        dir.Normalize();
        var pos = start;
        for (int step = 0; step < 200; step++)
        {
            pos += dir * (Cell * 0.2f);
            var cc = PointToCell(pos);
            if (!InGrid(cc))
                return false;
            if (cc == _ship)
                continue;
            if (cc == target)
                return true;
            if (Occupied(cc))
                return false;
        }
        return false;
    }

    private Point ChooseQuadrant()
    {
        Point best = _quad;
        float bd = float.MaxValue;
        bool needBase = NeedBase();
        for (int x = 0; x < N; x++)
            for (int y = 0; y < N; y++)
            {
                var p = new Point(x, y);
                if (p == _quad)
                    continue;
                var q = _gal[x, y];
                float d = Math.Max(Math.Abs(x - _quad.X), Math.Abs(y - _quad.Y));
                float score;
                if (needBase)
                    score = q.Known && q.Bases > 0 ? d : 100 + d;
                else
                    score = q.Known ? (q.Kraal > 0 ? d : 200 + d) : 20 + d;
                if (score < bd)
                {
                    bd = score;
                    best = p;
                }
            }
        return best;
    }
}
