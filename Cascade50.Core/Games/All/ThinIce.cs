using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 48 Thin Ice: a penguin skates across a frozen lake. Every tile it leaves cracks and melts;
/// reach the exit having melted as much ice as possible. Levels are carved from a random walk,
/// so there is always a way to melt every tile. Later lakes add thick ice, keys and crates.
/// </summary>
public sealed class ThinIce : MiniGame, Capture.ICaptureHints
{
    public override int Number => 48;
    public override string Title => "Thin Ice";
    public override Category Category => Category.Puzzle;
    public override string Tagline => "Melt every tile of ice behind you, then skate to the exit.";
    public override Color Accent => Pal.Ice;
    public override Pad Pad => Pad.Stick;
    public int CaptureTicks => 60 * 12;

    public override string[] HowToPlay =>
    [
        "Each ice tile you leave melts. Melt as many as you can, then reach the flag. Melt them all for a PERFECT bonus.",
        "Thick ice takes two visits. Keys open frozen locks. Push crates into the water. Step in the water, or get stuck, and you lose a life.",
        "There is always a perfect route. 20 lakes, 3 lives.",
    ];

    public override string[] DesktopControls =>
    [
        "ARROWS / WASD: skate one tile.",
        "Or click a tile next to the penguin.",
        "R: retry the lake (costs a life).",
    ];

    public override string[] TouchControls => ["Swipe, tap a tile or use the stick.", "RETRY restarts the lake (costs a life)."];

    private const int MaxW = 14, MaxH = 9, LastLevel = 20;
    private const float AreaY = 30, AreaH = 322;

    // On phones the stick sits bottom-left, so the lake shifts right.
    private float AreaX => IsTouch ? 112 : 14;
    private float AreaW => IsTouch ? 362 : 456;

    private enum Cell : byte
    {
        Snow,
        Water,
        Ice,
        Exit,
        Lock,
    }

    private static readonly Dictionary<char, Color> Cols = new()
    {
        ['k'] = new Color(28, 34, 62), ['w'] = Pal.White, ['o'] = Pal.Orange, ['r'] = new Color(230, 40, 60),
        ['b'] = new Color(60, 80, 130), ['y'] = Pal.Gold, ['Y'] = new Color(255, 240, 150), ['d'] = new Color(170, 120, 20),
    };

    private static readonly PixelArt Penguin = new(
    [
        "...kkkk...",
        "..kkkkkk..",
        ".kkwwwwkk.",
        ".kwkwwkwk.",
        ".kwwoowwk.",
        "kkrrrrrrkk",
        "kkwwrwwwkk",
        "bkwwrwwwkb",
        ".kwwwwwwk.",
        ".kwwwwwwk.",
        "..kwwwwk..",
        "..oo..oo..",
    ], Cols);

    private static readonly PixelArt KeyArt = new(
    [
        ".yyy......",
        "yY.yy.....",
        "y...yyyyyy",
        "yy.yy..y.y",
        ".yyy...d.d",
    ], Cols);

    // Level state.
    private int _w, _h;
    private readonly Cell[,] _cell = new Cell[MaxW, MaxH];
    private readonly int[,] _hp = new int[MaxW, MaxH];
    private readonly bool[,] _cracked = new bool[MaxW, MaxH];
    private readonly bool[,] _key = new bool[MaxW, MaxH];
    private readonly bool[,] _block = new bool[MaxW, MaxH];
    private readonly float[,] _melt = new float[MaxW, MaxH];

    // Saved copy for restarts.
    private readonly Cell[,] _cell0 = new Cell[MaxW, MaxH];
    private readonly int[,] _hp0 = new int[MaxW, MaxH];
    private readonly bool[,] _key0 = new bool[MaxW, MaxH];
    private readonly bool[,] _block0 = new bool[MaxW, MaxH];
    private Point _start;
    private readonly List<Point> _solution = new();

    private Point _pos, _from;
    private float _moveT = 1;
    private bool _faceLeft;
    private int _keys, _totalIce, _meltedIce;
    private int _queued = -1;
    private float _stuckTime, _fallTime = -1, _clearTime = -1, _banner;
    private bool _perfect;
    private float _tile, _ox, _oy;
    private Point _blockFrom, _blockTo;
    private float _blockT = 1;
    private bool _blockSinking;

    // Autoplay.
    private int _autoStep, _autoWait;
    private bool _autoLost, _autoRelease;

    private static readonly Point[] Dirs = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];

    protected override void Start()
    {
        Lives = 3;
        Level = 1;
        BuildLevel();
    }

    // ------------------------------------------------------------------ generation

    private bool In2(Point p) => p.X >= 0 && p.Y >= 0 && p.X < _w && p.Y < _h;

    private void BuildLevel()
    {
        _w = Math.Min(MaxW, 5 + Level);
        _h = Math.Min(MaxH, 4 + (Level + 1) / 2);
        _tile = MathF.Min(44, MathF.Min(AreaW / _w, AreaH / _h));
        _ox = AreaX + (AreaW - _w * _tile) / 2;
        _oy = AreaY + (AreaH - _h * _tile) / 2;

        var visits = new int[_w, _h];
        var best = new List<Point>();
        var path = new List<Point>();
        int target = (int)(_w * _h * MathF2.Clamp(0.62f + Level * 0.012f, 0, 0.85f));
        int thickMax = Level >= 3 ? 1 + Level / 3 : 0;
        for (int attempt = 0; attempt < 400; attempt++)
        {
            Array.Clear(visits);
            path.Clear();
            var s = new Point(RandInt(0, _w), RandInt(0, _h));
            path.Add(s);
            visits[s.X, s.Y] = 1;
            int thick = thickMax;
            while (true)
            {
                var cur = path[^1];
                Point pick = new(-1, -1);
                float bestW = float.MaxValue;
                foreach (var d in Dirs)
                {
                    var n = new Point(cur.X + d.X, cur.Y + d.Y);
                    if (!In2(n))
                        continue;
                    int v = visits[n.X, n.Y];
                    bool revisit = v == 1 && thick > 0 && path.Count > 3 && Chance(0.3f);
                    if (v != 0 && !revisit)
                        continue;
                    // Warnsdorff with noise: prefer cells with few free neighbours.
                    int free = 0;
                    foreach (var e in Dirs)
                    {
                        var m = new Point(n.X + e.X, n.Y + e.Y);
                        if (In2(m) && visits[m.X, m.Y] == 0)
                            free++;
                    }
                    float wgt = free + Rand(0, 2.2f) + (v == 1 ? 1.5f : 0);
                    if (wgt < bestW)
                    {
                        bestW = wgt;
                        pick = n;
                    }
                }
                if (pick.X < 0 || path.Count > target * 1.3f)
                    break;
                if (visits[pick.X, pick.Y] == 1)
                    thick--;
                visits[pick.X, pick.Y]++;
                path.Add(pick);
            }
            // The exit must be a fresh cell.
            while (path.Count > 1 && visits[path[^1].X, path[^1].Y] > 1)
            {
                visits[path[^1].X, path[^1].Y]--;
                path.RemoveAt(path.Count - 1);
            }
            if (path.Count > best.Count)
            {
                best.Clear();
                best.AddRange(path);
            }
            if (best.Count >= target)
                break;
        }

        _solution.Clear();
        _solution.AddRange(best);
        Array.Clear(visits);
        foreach (var p in best)
            visits[p.X, p.Y]++;

        Array.Clear(_cell0);
        Array.Clear(_hp0);
        Array.Clear(_key0);
        Array.Clear(_block0);
        for (int x = 0; x < _w; x++)
            for (int y = 0; y < _h; y++)
            {
                if (visits[x, y] > 0)
                {
                    _cell0[x, y] = Cell.Ice;
                    _hp0[x, y] = visits[x, y];
                }
                else
                {
                    // Off-route cells: mostly snowdrifts, some open water.
                    _cell0[x, y] = Chance(0.3f) ? Cell.Water : Cell.Snow;
                }
            }
        _start = best[0];
        var exit = best[^1];
        _cell0[exit.X, exit.Y] = Cell.Exit;
        _hp0[exit.X, exit.Y] = 0;

        int n2 = best.Count;
        // Keys and locks.
        if (Level >= 5 && n2 > 8)
        {
            int locks = Level >= 12 ? 2 : 1;
            for (int l = 0; l < locks; l++)
                for (int tries = 0; tries < 40; tries++)
                {
                    int j = RandInt(n2 / 2, n2 - 1);
                    int i = RandInt(1, j);
                    var lp = best[j];
                    var kp = best[i];
                    if (visits[lp.X, lp.Y] != 1 || _cell0[lp.X, lp.Y] != Cell.Ice || _key0[kp.X, kp.Y] || _cell0[kp.X, kp.Y] != Cell.Ice)
                        continue;
                    // Keys must be collected before their locks, counting earlier locks too.
                    if (FirstIndex(best, kp) >= j || FirstIndex(best, kp) < 1)
                        continue;
                    if (!KeyOrderOk(best, kp, j))
                        continue;
                    _cell0[lp.X, lp.Y] = Cell.Lock;
                    _hp0[lp.X, lp.Y] = 1;
                    _key0[kp.X, kp.Y] = true;
                    break;
                }
        }
        // Crates that must be pushed into the water on the way.
        if (Level >= 8 && n2 > 8)
        {
            int crates = Level >= 14 ? 2 : 1;
            for (int c = 0; c < crates; c++)
                for (int tries = 0; tries < 60; tries++)
                {
                    int k = RandInt(2, n2 - 1);
                    var bp = best[k];
                    var prev = best[k - 1];
                    var to = new Point(bp.X * 2 - prev.X, bp.Y * 2 - prev.Y);
                    if (visits[bp.X, bp.Y] != 1 || _cell0[bp.X, bp.Y] != Cell.Ice || _key0[bp.X, bp.Y] || _block0[bp.X, bp.Y])
                        continue;
                    if (!In2(to) || visits[to.X, to.Y] != 0)
                        continue;
                    _cell0[to.X, to.Y] = Cell.Water;
                    _block0[bp.X, bp.Y] = true;
                    break;
                }
        }
        ResetLevel();
        _banner = 2f;
        Status = "LAKE " + Level + " OF " + LastLevel;
    }

    private static int FirstIndex(List<Point> path, Point p)
    {
        for (int i = 0; i < path.Count; i++)
            if (path[i] == p)
                return i;
        return -1;
    }

    private bool KeyOrderOk(List<Point> path, Point newKey, int newLock)
    {
        // Walk the route counting keys held; it must never be negative at a lock.
        int held = 0;
        for (int i = 0; i < path.Count; i++)
        {
            var p = path[i];
            if (i == newLock || _cell0[p.X, p.Y] == Cell.Lock && FirstIndex(path, p) == i)
            {
                held--;
                if (held < 0)
                    return false;
            }
            if ((_key0[p.X, p.Y] || p == newKey) && FirstIndex(path, p) == i)
                held++;
        }
        return true;
    }

    private void ResetLevel()
    {
        _totalIce = 0;
        _meltedIce = 0;
        for (int x = 0; x < MaxW; x++)
            for (int y = 0; y < MaxH; y++)
            {
                _cell[x, y] = _cell0[x, y];
                _hp[x, y] = _hp0[x, y];
                _key[x, y] = _key0[x, y];
                _block[x, y] = _block0[x, y];
                _cracked[x, y] = false;
                _melt[x, y] = 0;
                if (x < _w && y < _h && (_cell[x, y] == Cell.Ice || _cell[x, y] == Cell.Lock))
                    _totalIce++;
            }
        _pos = _from = _start;
        _moveT = 1;
        _keys = 0;
        _queued = -1;
        _stuckTime = 0;
        _fallTime = -1;
        _clearTime = -1;
        _blockT = 1;
        _autoStep = 0;
        _autoLost = false;
    }

    // ------------------------------------------------------------------ play

    private bool Enterable(Point p, Point d)
    {
        if (!In2(p))
            return false;
        if (_block[p.X, p.Y])
        {
            var to = new Point(p.X + d.X, p.Y + d.Y);
            return In2(to) && !_block[to.X, to.Y] && (_cell[to.X, to.Y] is Cell.Water or Cell.Ice);
        }
        return _cell[p.X, p.Y] switch
        {
            Cell.Ice or Cell.Exit or Cell.Water => true,
            Cell.Lock => _keys > 0,
            _ => false,
        };
    }

    private bool SafeMove(Point p, Point d) => Enterable(p, d) && _cell[p.X, p.Y] != Cell.Water;

    private bool HasSafeMove()
    {
        foreach (var d in Dirs)
            if (SafeMove(new Point(_pos.X + d.X, _pos.Y + d.Y), d))
                return true;
        return false;
    }

    protected override void Update()
    {
        if (_banner > 0)
            _banner -= Dt;
        for (int x = 0; x < _w; x++)
            for (int y = 0; y < _h; y++)
                if (_melt[x, y] > 0)
                    _melt[x, y] = MathF.Max(0, _melt[x, y] - Dt * 2.5f);
        if (_blockT < 1)
        {
            _blockT = MathF.Min(1, _blockT + Dt * 7);
            if (_blockT >= 1 && _blockSinking)
            {
                var c = Centre(_blockTo);
                Fx.Burst(c.X, c.Y, Pal.Sky, 18, 80, 0.6f, 2f, 120);
                Sound.Play(Sfx.Splash, -0.3f, 0.8f);
                _blockSinking = false;
            }
        }

        bool retry = Ui.Button(new RectF(492, 300, 132, 34), "RETRY", Keys.R, _fallTime < 0 && _clearTime < 0, new Color(70, 40, 90));
        if (_fallTime >= 0)
        {
            _fallTime += Dt;
            if (_fallTime > 1.2f)
            {
                if (LoseLife())
                    return;
                ResetLevel();
                _banner = 1.2f;
            }
            return;
        }
        if (_clearTime >= 0)
        {
            _clearTime += Dt;
            if (_clearTime > 1.8f)
            {
                if (Level >= LastLevel)
                {
                    EndGame(true, "Every lake conquered!");
                    return;
                }
                Level++;
                Sound.Play(Sfx.LevelUp);
                BuildLevel();
            }
            return;
        }

        if (retry)
        {
            Fall(false);
            return;
        }

        int dir = ReadDirection();
        if (dir >= 0)
            _queued = dir;

        if (_moveT < 1)
        {
            _moveT = MathF.Min(1, _moveT + Dt * 9);
            if (_moveT >= 1)
                Arrive();
            return;
        }

        if (_queued >= 0)
        {
            TryMove(Dirs[_queued]);
            _queued = -1;
        }

        if (_moveT >= 1 && _fallTime < 0 && _clearTime < 0)
        {
            if (!HasSafeMove())
            {
                _stuckTime += Dt;
                if (_stuckTime > 0.4f && Tick % 10 == 0)
                    Sound.Play(Sfx.Crack, Rand(-0.6f, -0.2f), 0.4f);
                if (_stuckTime > 1.3f)
                    Fall(true);
            }
            else
            {
                _stuckTime = 0;
            }
        }
    }

    private int ReadDirection()
    {
        bool h = In.LeftPressed || In.RightPressed, v = In.UpPressed || In.DownPressed;
        if (h && v)
        {
            if (MathF.Abs(In.AxisX) >= MathF.Abs(In.AxisY))
                v = false;
            else
                h = false;
        }
        if (h)
            return In.RightPressed ? 0 : 1;
        if (v)
            return In.DownPressed ? 2 : 3;

        // Tap a neighbouring tile, or swipe.
        if (In.PointerReleased)
        {
            var delta = In.Pointer - In.PointerStart;
            if (delta.Length() > 18)
                return MathF.Abs(delta.X) > MathF.Abs(delta.Y) ? (delta.X > 0 ? 0 : 1) : (delta.Y > 0 ? 2 : 3);
            var c = Centre(_pos);
            var d = In.Pointer - c;
            if (d.Length() > _tile * 0.5f && d.Length() < _tile * 1.6f)
                return MathF.Abs(d.X) > MathF.Abs(d.Y) ? (d.X > 0 ? 0 : 1) : (d.Y > 0 ? 2 : 3);
        }
        return -1;
    }

    private void TryMove(Point d)
    {
        var to = new Point(_pos.X + d.X, _pos.Y + d.Y);
        if (!Enterable(to, d))
        {
            Sound.Play(Sfx.Thud, -0.4f, 0.4f);
            return;
        }
        if (d.X != 0)
            _faceLeft = d.X < 0;
        if (_block[to.X, to.Y])
        {
            var bt = new Point(to.X + d.X, to.Y + d.Y);
            _block[to.X, to.Y] = false;
            _blockFrom = to;
            _blockTo = bt;
            _blockT = 0;
            if (_cell[bt.X, bt.Y] == Cell.Water)
            {
                _blockSinking = true;
            }
            else
            {
                _block[bt.X, bt.Y] = true;
                _blockSinking = false;
            }
            Sound.Play(Sfx.Whoosh, -0.4f, 0.5f);
        }
        // Leave the current tile.
        var from = _pos;
        if (_cell[from.X, from.Y] is Cell.Ice or Cell.Lock)
        {
            _hp[from.X, from.Y]--;
            var c = Centre(from);
            if (_hp[from.X, from.Y] <= 0)
            {
                _cell[from.X, from.Y] = Cell.Water;
                _melt[from.X, from.Y] = 1;
                _meltedIce++;
                AddScore(10, c.X, c.Y - 8, Pal.Ice);
                Sound.Play(Sfx.Splash, Rand(0.1f, 0.5f), 0.45f);
                Fx.Burst(c.X, c.Y, Pal.Ice, 8, 50, 0.4f, 1.6f);
            }
            else
            {
                _cracked[from.X, from.Y] = true;
                AddScore(5, c.X, c.Y - 8, Pal.Sky);
                Sound.Play(Sfx.Crack, Rand(-0.1f, 0.2f), 0.6f);
                Fx.Burst(c.X, c.Y, Pal.White, 6, 40, 0.3f, 1.4f);
            }
        }
        _from = from;
        _pos = to;
        _moveT = 0;
        Sound.Play(Sfx.Step, Rand(0.2f, 0.5f), 0.35f);
    }

    private void Arrive()
    {
        var p = _pos;
        var c = Centre(p);
        switch (_cell[p.X, p.Y])
        {
            case Cell.Water:
                Fall(true);
                return;
            case Cell.Lock:
                _keys--;
                _cell[p.X, p.Y] = Cell.Ice;
                Sound.Play(Sfx.PowerUp, 0.2f, 0.7f);
                Fx.Burst(c.X, c.Y, Pal.Purple, 20, 90, 0.5f, 2f);
                break;
            case Cell.Exit:
                ClearLake();
                return;
        }
        if (_key[p.X, p.Y])
        {
            _key[p.X, p.Y] = false;
            _keys++;
            Sound.Play(Sfx.Pickup);
            Fx.Burst(c.X, c.Y, Pal.Gold, 16, 80, 0.5f, 2f);
            Fx.Float("KEY", c.X, c.Y - 12, Pal.Gold);
        }
    }

    private void ClearLake()
    {
        var c = Centre(_pos);
        _perfect = _meltedIce >= _totalIce;
        int bonus = 50 + 25 * Level;
        AddScore(bonus, c.X, c.Y - 14, Pal.Gold);
        if (_perfect)
        {
            AddScore(20 * _totalIce, 240, 180, Pal.Lime);
            Sound.Play(Sfx.Bonus);
            Fx.Burst(240, 190, Pal.Lime, 50, 220, 1f, 2.5f);
        }
        else
        {
            Sound.Play(Sfx.Correct);
        }
        Fx.Burst(c.X, c.Y, Pal.Gold, 30, 140, 0.8f, 2.5f);
        _clearTime = 0;
    }

    private void Fall(bool splash)
    {
        _fallTime = 0;
        var c = Centre(_pos);
        if (splash)
        {
            _cell[_pos.X, _pos.Y] = Cell.Water;
            _melt[_pos.X, _pos.Y] = 1;
        }
        Fx.Burst(c.X, c.Y, Pal.Sky, 30, 110, 0.8f, 2.4f, 160);
        Fx.Burst(c.X, c.Y, Pal.White, 12, 60, 0.5f, 1.6f, 100);
        Sound.Play(Sfx.Splash, -0.5f);
    }

    // ------------------------------------------------------------------ drawing

    private Vector2 Centre(Point p) => new(_ox + (p.X + 0.5f) * _tile, _oy + (p.Y + 0.5f) * _tile);

    private static float Hash(int x, int y, int k) => ((x * 73856093) ^ (y * 19349663) ^ (k * 83492791)) % 1000 / 1000f;

    private static void Night(Gfx g, RectF r, float t)
    {
        g.GradientV(r.X, r.Y, r.W, r.H, new Color(8, 18, 44), new Color(18, 44, 86));
        float s = r.H / 360f;
        g.Glow(r.X + r.W * 0.2f, r.Y + r.H * 0.1f, 200 * s, new Color(40, 160, 140), 0.18f);
        g.Glow(r.X + r.W * 0.75f, r.Y + r.H * 0.05f, 220 * s, new Color(90, 60, 180), 0.16f);
    }

    private static void Snowfall(Gfx g, RectF r, float t, int count)
    {
        var rng = new Random(48);
        for (int i = 0; i < count; i++)
        {
            float depth = 0.3f + 0.7f * (float)rng.NextDouble();
            float x = r.X + Backdrops.Mod((float)rng.NextDouble() * r.W + MathF.Sin(t * 0.8f + i) * 12, r.W);
            float y = r.Y + Backdrops.Mod((float)rng.NextDouble() * r.H + t * 22 * depth, r.H);
            g.Circle(x, y, 0.8f + depth * 1.1f * r.H / 360f, Pal.White * (0.35f + 0.5f * depth));
        }
    }

    private void DrawIce(Gfx g, float x, float y, float s, int hp, bool cracked, int cx, int cy, float t)
    {
        if (hp >= 2)
        {
            g.GradientV(x + 1, y + 1, s - 2, s - 2, new Color(240, 252, 255), new Color(170, 220, 248));
            g.Rect(x + 1, y + 1, s - 2, 3, Pal.White);
            g.Rect(x + 1, y + s - 4, s - 2, 3, new Color(120, 180, 225));
            // Frost pattern.
            float m = s / 2;
            g.Line(x + m - s * 0.22f, y + m, x + m + s * 0.22f, y + m, 1.2f, new Color(150, 200, 240));
            g.Line(x + m, y + m - s * 0.22f, x + m, y + m + s * 0.22f, 1.2f, new Color(150, 200, 240));
            g.Line(x + m - s * 0.15f, y + m - s * 0.15f, x + m + s * 0.15f, y + m + s * 0.15f, 1f, new Color(150, 200, 240));
            g.Line(x + m + s * 0.15f, y + m - s * 0.15f, x + m - s * 0.15f, y + m + s * 0.15f, 1f, new Color(150, 200, 240));
        }
        else
        {
            g.GradientV(x + 1, y + 1, s - 2, s - 2, new Color(170, 225, 250), new Color(110, 185, 235));
            g.Rect(x + 1, y + 1, s - 2, 1.5f, new Color(220, 245, 255));
            g.Line(x + s * 0.2f, y + s * 0.75f, x + s * 0.45f, y + s * 0.5f, 1.2f, Pal.White * 0.35f);
            g.Line(x + s * 0.55f, y + s * 0.82f, x + s * 0.8f, y + s * 0.57f, 1.2f, Pal.White * 0.25f);
        }
        if (cracked)
        {
            var cc = new Color(60, 110, 170);
            float m = s / 2;
            g.Line(x + m, y + m, x + s * 0.12f, y + s * 0.2f, 1.2f, cc);
            g.Line(x + m, y + m, x + s * 0.9f, y + s * 0.35f, 1.2f, cc);
            g.Line(x + m, y + m, x + s * 0.4f, y + s * 0.92f, 1.2f, cc);
            g.Line(x + s * 0.7f, y + s * 0.42f, x + s * 0.8f, y + s * 0.8f, 1f, cc);
        }
        float tw = MathF.Sin(t * (1.5f + Hash(cx, cy, 2) * 2) + Hash(cx, cy, 1) * 20);
        if (tw > 0.75f)
        {
            float sx = x + s * (0.2f + 0.6f * Hash(cx, cy, 3)), sy = y + s * (0.2f + 0.6f * Hash(cx, cy, 4));
            float k = (tw - 0.75f) * 4;
            g.Glow(sx, sy, s * 0.3f, Pal.White, 0.6f * k);
            g.Line(sx - s * 0.12f * k, sy, sx + s * 0.12f * k, sy, 1, Pal.White);
            g.Line(sx, sy - s * 0.12f * k, sx, sy + s * 0.12f * k, 1, Pal.White);
        }
    }

    private static void DrawWater(Gfx g, float x, float y, float s, int cx, int cy, float t)
    {
        g.GradientV(x, y, s, s, new Color(14, 50, 120), new Color(8, 30, 84));
        for (int i = 0; i < 2; i++)
        {
            float wy = y + s * (0.3f + 0.4f * i) + MathF.Sin(t * 2 + cx + i * 2) * 2;
            float wx = x + s * 0.2f + MathF.Sin(t * 1.3f + cy * 3 + i) * s * 0.1f;
            g.Line(wx, wy, wx + s * 0.4f, wy, 1.2f, new Color(70, 130, 210) * 0.6f);
        }
    }

    private static void DrawSnow(Gfx g, float x, float y, float s, int cx, int cy)
    {
        g.Rect(x, y, s, s, new Color(24, 52, 96));
        float h = Hash(cx, cy, 5);
        g.Ellipse(x + s * 0.5f, y + s * 0.62f, s * 0.48f, s * 0.36f, new Color(150, 180, 215));
        g.Ellipse(x + s * 0.5f, y + s * 0.55f, s * 0.45f, s * 0.34f, new Color(225, 238, 250));
        g.Ellipse(x + s * (0.35f + h * 0.2f), y + s * 0.42f, s * 0.24f, s * 0.2f, Pal.White);
        if (h > 0.6f)
        {
            // A little pine tree on some drifts.
            float px = x + s * 0.62f, py = y + s * 0.2f;
            g.Triangle(new Vector2(px, py), new Vector2(px - s * 0.16f, py + s * 0.3f), new Vector2(px + s * 0.16f, py + s * 0.3f), new Color(30, 110, 80));
            g.Triangle(new Vector2(px, py + s * 0.12f), new Vector2(px - s * 0.2f, py + s * 0.45f), new Vector2(px + s * 0.2f, py + s * 0.45f), new Color(24, 90, 66));
        }
    }

    private static void DrawExit(Gfx g, float x, float y, float s, float t)
    {
        g.GradientV(x + 1, y + 1, s - 2, s - 2, new Color(170, 225, 250), new Color(110, 185, 235));
        float cx = x + s / 2, cy = y + s / 2;
        g.Glow(cx, cy, s * 0.9f, Pal.Gold, 0.4f + 0.2f * MathF.Sin(t * 4));
        g.Ring(cx, cy + s * 0.22f, s * 0.3f, 1.5f, Pal.Gold * 0.8f);
        g.Rect(cx - s * 0.12f, cy - s * 0.35f, 2, s * 0.6f, new Color(90, 60, 30));
        float wave = MathF.Sin(t * 6) * s * 0.04f;
        g.Triangle(new Vector2(cx - s * 0.1f, cy - s * 0.35f), new Vector2(cx + s * 0.3f, cy - s * 0.24f + wave), new Vector2(cx - s * 0.1f, cy - s * 0.1f), new Color(230, 40, 60));
    }

    private static void DrawLock(Gfx g, float x, float y, float s, float t)
    {
        g.GradientV(x + 1, y + 1, s - 2, s - 2, new Color(150, 120, 230), new Color(80, 50, 160));
        g.Rect(x + 1, y + 1, s - 2, 2, new Color(200, 180, 255));
        float cx = x + s / 2, cy = y + s / 2;
        g.Glow(cx, cy, s * 0.6f, Pal.Purple, 0.4f);
        g.Arc(cx, cy - s * 0.08f, s * 0.15f, 2.5f, MathF.PI, MathF2.Tau, Pal.Gold);
        g.RoundRect(cx - s * 0.22f, cy - s * 0.08f, s * 0.44f, s * 0.32f, 2, Pal.Gold);
        g.Circle(cx, cy + s * 0.05f, s * 0.05f, new Color(60, 40, 10));
        g.Rect(cx - 1, cy + s * 0.05f, 2, s * 0.12f, new Color(60, 40, 10));
    }

    private static void DrawCrate(Gfx g, float x, float y, float s)
    {
        float m = s * 0.12f;
        g.Rect(x + m, y + m + 2, s - 2 * m, s - 2 * m, Color.Black * 0.35f);
        g.Rect(x + m, y + m, s - 2 * m, s - 2 * m, new Color(150, 95, 45));
        g.RectOutline(x + m, y + m, s - 2 * m, s - 2 * m, 2, new Color(95, 55, 25));
        g.Line(x + m + 2, y + m + 2, x + s - m - 2, y + s - m - 2, 2, new Color(110, 65, 30));
        g.Line(x + s - m - 2, y + m + 2, x + m + 2, y + s - m - 2, 2, new Color(110, 65, 30));
        g.Rect(x + m, y + m, s - 2 * m, 2, new Color(190, 140, 80));
        g.Rect(x + m, y + m, s - 2 * m, 2, Pal.White * 0.3f);
    }

    public override void Draw(Gfx g)
    {
        Night(g, g.Visible, Time);
        // Lake shore frame.
        var board = new RectF(_ox - 6, _oy - 6, _w * _tile + 12, _h * _tile + 12);
        g.Glow(board.CenterX, board.CenterY, board.W * 0.7f, Pal.Sky, 0.12f);
        g.RoundRect(board.Offset(0, 3), 8, Color.Black * 0.4f);
        g.RoundRect(board, 8, new Color(220, 236, 250));
        g.Rect(_ox, _oy, _w * _tile, _h * _tile, new Color(10, 36, 90));

        for (int x = 0; x < _w; x++)
            for (int y = 0; y < _h; y++)
            {
                float px = _ox + x * _tile, py = _oy + y * _tile;
                switch (_cell[x, y])
                {
                    case Cell.Snow:
                        DrawSnow(g, px, py, _tile, x, y);
                        break;
                    case Cell.Water:
                        DrawWater(g, px, py, _tile, x, y, Time);
                        if (_melt[x, y] > 0)
                        {
                            float k = _melt[x, y];
                            float inset = (1 - k) * _tile * 0.5f;
                            g.Rect(px + inset, py + inset, _tile - inset * 2, _tile - inset * 2, new Color(150, 210, 245) * k);
                        }
                        break;
                    case Cell.Ice:
                        DrawIce(g, px, py, _tile, _hp[x, y], _cracked[x, y], x, y, Time);
                        break;
                    case Cell.Exit:
                        DrawExit(g, px, py, _tile, Time);
                        break;
                    case Cell.Lock:
                        DrawLock(g, px, py, _tile, Time);
                        break;
                }
                if (_key[x, y])
                {
                    float bob = MathF.Sin(Time * 3 + x) * 2;
                    g.Glow(px + _tile / 2, py + _tile / 2, _tile * 0.5f, Pal.Gold, 0.5f);
                    g.PixelsCentered(KeyArt, px + _tile / 2, py + _tile / 2 + bob, _tile / 14f);
                }
                if (_block[x, y] && !(_blockT < 1 && new Point(x, y) == _blockTo))
                    DrawCrate(g, px, py, _tile);
            }

        if (_blockT < 1)
        {
            var a = new Vector2(_ox + _blockFrom.X * _tile, _oy + _blockFrom.Y * _tile);
            var b = new Vector2(_ox + _blockTo.X * _tile, _oy + _blockTo.Y * _tile);
            var p = Vector2.Lerp(a, b, _blockT);
            DrawCrate(g, p.X, p.Y, _tile);
        }

        // Penguin.
        var from = Centre(_from);
        var to = Centre(_pos);
        var pos = Vector2.Lerp(from, to, MathF2.EaseInOut(_moveT));
        float hop = MathF.Sin(_moveT * MathF.PI) * _tile * 0.18f;
        float px2 = _tile / 13f;
        if (_fallTime >= 0)
        {
            float k = MathF.Min(1, _fallTime / 0.8f);
            g.SetClip(new RectF(pos.X - _tile, pos.Y - _tile * 2, _tile * 2, _tile * 2 + _tile * 0.25f));
            g.PixelsCentered(Penguin, pos.X, pos.Y - _tile * 0.1f + k * _tile * 0.9f, px2, _faceLeft);
            g.SetClip(null);
            g.Ring(pos.X, pos.Y + _tile * 0.25f, _tile * (0.2f + k * 0.4f), 1.5f, Pal.White * (1 - k));
        }
        else if (!IsOver)
        {
            g.Ellipse(pos.X, pos.Y + _tile * 0.38f, _tile * 0.3f, _tile * 0.1f, Color.Black * 0.3f);
            float wobble = _stuckTime > 0.4f ? MathF.Sin(Time * 40) * 1.5f : 0;
            float jump = _clearTime >= 0 ? MathF.Abs(MathF.Sin(_clearTime * 9)) * _tile * 0.3f : 0;
            g.PixelsCentered(Penguin, pos.X + wobble, pos.Y - _tile * 0.1f - hop - jump, px2, _faceLeft);
        }

        Snowfall(g, g.Visible, Time, 70);
        DrawPanel(g);

        if (_banner > 0)
        {
            float a = MathF.Min(1, _banner * 2);
            g.TextShadow("LAKE " + Level, 242, 170, 3f, Pal.White * a, Align.Center, Color.Black * (0.8f * a));
        }
        if (_clearTime >= 0)
        {
            g.TextShadow(_perfect ? "PERFECT!" : "LAKE CLEARED", 242, 160, _perfect ? 3.5f : 2.5f, (_perfect ? Pal.Lime : Pal.Gold), Align.Center);
            if (!_perfect)
                g.TextShadow((_totalIce - _meltedIce) + " TILES LEFT UNMELTED", 242, 194, 1.5f, Pal.Ice, Align.Center);
        }
        if (_stuckTime > 0.4f && _fallTime < 0)
            g.TextShadow("THE ICE IS GIVING WAY!", 242, 40, 1.5f, Pal.Orange, Align.Center);
    }

    private void DrawPanel(Gfx g)
    {
        var r = new RectF(484, 30, 148, 316);
        g.Panel(r, new Color(14, 26, 56) * 0.92f, new Color(110, 170, 230), 10);
        g.Text("LAKE", r.CenterX, 42, 1.5f, Pal.Sky, Align.Center);
        g.TextShadow(Level + "/" + LastLevel, r.CenterX, 58, 2.5f, Pal.White, Align.Center);

        g.Text("ICE MELTED", r.CenterX, 96, 1.5f, Pal.Sky, Align.Center);
        g.TextShadow(_meltedIce + "/" + _totalIce, r.CenterX, 112, 2.5f, Pal.Ice, Align.Center);
        float frac = _totalIce > 0 ? _meltedIce / (float)_totalIce : 0;
        g.RoundRect(r.X + 14, 140, r.W - 28, 10, 4, new Color(20, 40, 80));
        if (frac > 0)
            g.RoundRect(r.X + 14, 140, (r.W - 28) * frac, 10, 4, Pal.Lerp(Pal.Sky, Pal.Lime, frac));

        g.Text("KEYS", r.CenterX, 166, 1.5f, Pal.Sky, Align.Center);
        if (_keys == 0)
            g.Text("-", r.CenterX, 184, 2f, Pal.Grey, Align.Center);
        for (int i = 0; i < _keys; i++)
            g.PixelsCentered(KeyArt, r.CenterX + (i - (_keys - 1) / 2f) * 30, 190, 2.2f);

        // Legend.
        float ly = 216, s = 16;
        DrawIce(g, r.X + 14, ly, s, 1, false, 0, 0, 0);
        g.Text("THIN", r.X + 36, ly + 5, 1f, Pal.LightGrey);
        DrawIce(g, r.X + 78, ly, s, 2, false, 0, 0, 0);
        g.Text("THICK", r.X + 100, ly + 5, 1f, Pal.LightGrey);
        DrawLock(g, r.X + 14, ly + 22, s, Time);
        g.Text("LOCK", r.X + 36, ly + 27, 1f, Pal.LightGrey);
        DrawCrate(g, r.X + 78, ly + 22, s);
        g.Text("PUSH", r.X + 100, ly + 27, 1f, Pal.LightGrey);
        DrawExit(g, r.X + 14, ly + 44, s, Time);
        g.Text("EXIT", r.X + 36, ly + 49, 1f, Pal.LightGrey);
        DrawWater(g, r.X + 78, ly + 44, s, 0, 0, Time);
        g.Text("SPLASH", r.X + 100, ly + 49, 1f, Pal.LightGrey);
    }

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        Night(g, r, time);
        float s = r.H / 4.6f;
        int cols = (int)(r.W / s) + 1;
        float ox = r.CenterX - cols * s / 2, oy = r.Y + r.H * 0.12f;
        // A little lake: the penguin skates along row 1, melting ice behind it.
        float cycle = time * 1.6f % (cols + 2);
        int at = (int)cycle;
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < cols; x++)
            {
                float px = ox + x * s, py = oy + y * s;
                bool route = y == 1 || (y == 2 && x == cols - 1);
                if (y == 0 || y == 3)
                    DrawSnow(g, px, py, s, x, y);
                else if (y == 1 && x < at)
                    DrawWater(g, px, py, s, x, y, time);
                else if (y == 2 && x == cols - 2)
                    DrawExit(g, px, py, s, time);
                else if (route || x % 3 != 0)
                    DrawIce(g, px, py, s, y == 2 && x % 2 == 0 ? 2 : 1, false, x, y, time);
                else
                    DrawWater(g, px, py, s, x, y, time);
            }
        float f = cycle - at;
        float penX = ox + (MathF.Min(at, cols - 1) + 0.5f + (at < cols - 1 ? MathF2.EaseInOut(f) : 0)) * s;
        float hop = MathF.Sin(f * MathF.PI) * s * 0.15f;
        g.Glow(penX, oy + s * 1.5f, s, Pal.White, 0.2f);
        g.PixelsCentered(Penguin, penX, oy + s * 1.4f - hop, s / 13f);
        Snowfall(g, r, time, 30);
    }

    public override void AutoPlay(Controls c)
    {
        if (_autoRelease)
        {
            c.PointerReleased = true;
            _autoRelease = false;
            return;
        }
        if (_moveT < 1 || _fallTime >= 0 || _clearTime >= 0)
            return;
        if (--_autoWait > 0)
            return;
        _autoWait = 9 + RandInt(0, 6);

        // Follow the known route; after a slip, wander greedily.
        int idx = -1;
        if (!_autoLost && _autoStep + 1 < _solution.Count && _solution[_autoStep] == _pos)
            idx = _autoStep + 1;
        if (idx >= 0 && Chance(0.0012f * Level))
        {
            _autoLost = true;
            idx = -1;
        }
        Point d;
        if (idx >= 0)
        {
            var n = _solution[idx];
            d = new Point(n.X - _pos.X, n.Y - _pos.Y);
            _autoStep = idx;
        }
        else
        {
            _autoLost = true;
            d = Dirs[RandInt(0, 4)];
            foreach (var e in Dirs)
            {
                var p = new Point(_pos.X + e.X, _pos.Y + e.Y);
                if (SafeMove(p, e))
                {
                    d = e;
                    if (Chance(0.5f))
                        break;
                }
            }
        }
        if (IsTouch && Tick % 2 == 0)
        {
            // Tap the tile next to the penguin.
            c.Pointer = Centre(new Point(_pos.X + d.X, _pos.Y + d.Y));
            c.PointerStart = c.Pointer;
            c.PointerPressed = true;
            c.PointerDown = true;
            _autoRelease = true;
            return;
        }
        c.SetDirections(d.X, d.Y);
    }
}
