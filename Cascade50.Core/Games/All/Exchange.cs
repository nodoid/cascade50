using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 09 Exchange: swap neighbouring jewels on an 8x8 board to line up three or more. Lines of four,
/// L / T shapes and lines of five leave special jewels behind. Reach each level's target score
/// before the moves run out.
/// </summary>
public sealed class Exchange : MiniGame
{
    public override int Number => 9;
    public override string Title => "Exchange";
    public override Category Category => Category.Puzzle;
    public override string Tagline => "Swap glittering jewels into lines of three before the moves run out.";
    public override Color Accent => Pal.Magenta;
    public override Pad Pad => Pad.None;

    public override string[] HowToPlay =>
    [
        "Exchange two neighbouring jewels to line up three or more of a colour. Falling jewels can chain.",
        "Four in a line makes a striped jewel, an L or T a bomb, five a star that clears a colour.",
        "Hit the target score before your moves run out to reach the next level.",
    ];

    public override string[] DesktopControls => ["Drag, or click two jewels.", "Arrows + SPACE pick and swap."];
    public override string[] TouchControls => ["Drag a jewel onto a neighbour,", "or tap one jewel, then another."];

    private const int N = 8;
    private const float C = 38;
    private const float BoardX = 26, BoardY = 22 + (338 - N * C) / 2;
    private const int Star = 99;
    private const int SpNone = 0, SpRow = 1, SpCol = 2, SpBomb = 3, SpStar = 4;

    private enum Phase { Idle, Swap, Clear, Fall, LevelDone }

    private struct Cell
    {
        public int Type, Special;
        public float OffY, Vel;
    }

    private struct Create
    {
        public int R, C, Type, Special;
    }

    private static readonly Color[] GemColours =
    [
        new(255, 50, 70), new(255, 150, 30), new(255, 225, 50), new(50, 220, 90),
        new(50, 140, 255), new(180, 70, 255), new(80, 240, 240),
    ];

    private static readonly Vector2[][] Shapes = BuildShapes();

    private readonly Cell[,] _g = new Cell[N, N];
    private readonly bool[,] _clear = new bool[N, N];
    private readonly bool[,] _fired = new bool[N, N];
    private readonly int[,] _hRun = new int[N, N], _vRun = new int[N, N];
    private readonly List<Create> _creates = new();
    private readonly List<(int r, int c)> _queue = new();
    private readonly List<(int r0, int c0, int len, bool horiz)> _runs = new();
    private readonly List<Vector2> _starPts = new();

    private Phase _phase;
    private float _t;
    private int _sr0, _sc0, _sr1, _sc1;
    private bool _swapBack;
    private int _chain;
    private int _moves, _target, _levelScore, _colours;
    private int _selR = -1, _selC = -1;
    private int _curR = 3, _curC = 3;
    private bool _keyboard;
    private bool _dragging;
    private Vector2 _dragStart;
    private int _dragR, _dragC;
    private float _idle;
    private float _banner;
    private string _bannerText = "";
    private float _comboFlash;
    private string _comboText = "";
    private int _landSounds;
    private float _shuffleFlash;
    private float _displayScore;

    // Autoplay.
    private int _apTimer, _apStep;
    private int _apR0, _apC0, _apR1, _apC1;

    protected override void Start()
    {
        Level = 1;
        StartLevel();
    }

    private void StartLevel()
    {
        _colours = Level >= 4 ? 7 : 6;
        _moves = Math.Max(14, 22 - Level);
        _target = 700 + (Level - 1) * 250 + (Level - 1) * (Level - 1) * 40;
        _levelScore = 0;
        _chain = 0;
        _selR = _selC = -1;
        Status = "LEVEL " + Level;
        FillBoard();
        for (int c = 0; c < N; c++)
            for (int r = 0; r < N; r++)
            {
                _g[r, c].OffY = -(N + 1) * C - c * 18 - (N - r) * 4;
                _g[r, c].Vel = 0;
            }
        _phase = Phase.Fall;
        _banner = 2.2f;
        _bannerText = "LEVEL " + Level;
        Sound.Play(Sfx.Shuffle);
    }

    private void FillBoard()
    {
        for (int tries = 0; tries < 100; tries++)
        {
            for (int r = 0; r < N; r++)
                for (int c = 0; c < N; c++)
                {
                    int t;
                    do
                        t = RandInt(0, _colours);
                    while ((c >= 2 && _g[r, c - 1].Type == t && _g[r, c - 2].Type == t) ||
                           (r >= 2 && _g[r - 1, c].Type == t && _g[r - 2, c].Type == t));
                    _g[r, c] = new Cell { Type = t };
                }
            if (HasMove())
                return;
        }
    }

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        _t += Dt;
        if (_banner > 0) _banner -= Dt;
        if (_comboFlash > 0) _comboFlash -= Dt;
        if (_shuffleFlash > 0) _shuffleFlash -= Dt;
        _displayScore = MathF2.Approach(_displayScore, _levelScore, MathF.Max(4, MathF.Abs(_levelScore - _displayScore) * 6 * Dt));
        _landSounds = 0;

        switch (_phase)
        {
            case Phase.Idle:
                _idle += Dt;
                HandleInput();
                break;
            case Phase.Swap:
                if (_t >= 0.16f)
                    FinishSwap();
                break;
            case Phase.Clear:
                if (_t >= 0.26f)
                    FinishClear();
                break;
            case Phase.Fall:
                UpdateFall();
                break;
            case Phase.LevelDone:
                if (_t >= 2.0f)
                {
                    Level++;
                    StartLevel();
                }
                break;
        }
    }

    private bool CellAt(Vector2 p, out int r, out int c)
    {
        c = (int)MathF.Floor((p.X - BoardX) / C);
        r = (int)MathF.Floor((p.Y - BoardY) / C);
        return r >= 0 && c >= 0 && r < N && c < N;
    }

    private void HandleInput()
    {
        // Pointer: drag or tap-tap.
        if (In.PointerPressed)
        {
            _keyboard = false;
            if (CellAt(In.Pointer, out int r, out int c))
            {
                if (_selR >= 0 && Math.Abs(r - _selR) + Math.Abs(c - _selC) == 1)
                {
                    BeginSwap(_selR, _selC, r, c);
                    return;
                }
                _selR = r;
                _selC = c;
                _dragging = true;
                _dragStart = In.Pointer;
                _dragR = r;
                _dragC = c;
                Sound.Play(Sfx.Select, 0.3f, 0.5f);
            }
            else
            {
                _selR = _selC = -1;
            }
        }
        if (_dragging)
        {
            if (!In.PointerDown || In.PointerReleased)
                _dragging = false;
            else
            {
                var d = In.Pointer - _dragStart;
                if (d.Length() > C * 0.45f)
                {
                    int dr = 0, dc = 0;
                    if (MathF.Abs(d.X) > MathF.Abs(d.Y)) dc = Math.Sign(d.X);
                    else dr = Math.Sign(d.Y);
                    int r2 = _dragR + dr, c2 = _dragC + dc;
                    _dragging = false;
                    if (r2 >= 0 && c2 >= 0 && r2 < N && c2 < N)
                    {
                        BeginSwap(_dragR, _dragC, r2, c2);
                        return;
                    }
                }
            }
        }

        // Keyboard cursor.
        int kr = In.UpPressed ? -1 : In.DownPressed ? 1 : 0;
        int kc = kr != 0 ? 0 : In.LeftPressed ? -1 : In.RightPressed ? 1 : 0;
        if (kr != 0 || kc != 0)
        {
            if (!_keyboard)
            {
                _keyboard = true;
                if (_selR >= 0) { _curR = _selR; _curC = _selC; }
                return;
            }
            int nr = Math.Clamp(_curR + kr, 0, N - 1), nc = Math.Clamp(_curC + kc, 0, N - 1);
            if (_selR >= 0 && _selR == _curR && _selC == _curC)
            {
                if (nr != _curR || nc != _curC)
                {
                    BeginSwap(_curR, _curC, nr, nc);
                    _curR = nr;
                    _curC = nc;
                }
                return;
            }
            _curR = nr;
            _curC = nc;
            Sound.Play(Sfx.Tick, 0.2f, 0.3f);
        }
        if (In.FirePressed)
        {
            _keyboard = true;
            if (_selR == _curR && _selC == _curC)
                _selR = _selC = -1;
            else
            {
                _selR = _curR;
                _selC = _curC;
                Sound.Play(Sfx.Select, 0.3f, 0.5f);
            }
        }
    }

    private void BeginSwap(int r0, int c0, int r1, int c1)
    {
        _sr0 = r0; _sc0 = c0; _sr1 = r1; _sc1 = c1;
        _swapBack = false;
        _phase = Phase.Swap;
        _t = 0;
        _selR = _selC = -1;
        _idle = 0;
        Sound.Play(Sfx.Whoosh, 0.4f, 0.4f);
    }

    private void SwapCells(int r0, int c0, int r1, int c1) => (_g[r0, c0], _g[r1, c1]) = (_g[r1, c1], _g[r0, c0]);

    private void FinishSwap()
    {
        SwapCells(_sr0, _sc0, _sr1, _sc1);
        if (_swapBack)
        {
            _phase = Phase.Idle;
            return;
        }
        Array.Clear(_clear);
        _creates.Clear();
        ref var a = ref _g[_sr1, _sc1]; // the jewel that was at (r0,c0) now sits at (r1,c1)
        ref var b = ref _g[_sr0, _sc0];
        bool starMove = a.Special == SpStar || b.Special == SpStar;
        bool doubleSpecial = a.Special != SpNone && b.Special != SpNone;
        if (starMove)
        {
            int colour = a.Special == SpStar ? b.Type : a.Type;
            if (a.Special == SpStar && b.Special == SpStar)
            {
                for (int r = 0; r < N; r++)
                    for (int c = 0; c < N; c++)
                        _clear[r, c] = true;
                Fx.Shake(6, 0.5f);
            }
            else
            {
                for (int r = 0; r < N; r++)
                    for (int c = 0; c < N; c++)
                        if (_g[r, c].Type == colour)
                            _clear[r, c] = true;
            }
            _clear[_sr0, _sc0] = _clear[_sr1, _sc1] = true;
            if (a.Special == SpStar) _fired[_sr1, _sc1] = true;
            if (b.Special == SpStar) _fired[_sr0, _sc0] = true;
            StarRays(_sr1, _sc1, colour);
            Sound.Play(Sfx.Warp, 0.2f);
        }
        else if (doubleSpecial)
        {
            _clear[_sr0, _sc0] = _clear[_sr1, _sc1] = true;
            FindMatches();
        }
        else if (!FindMatches())
        {
            // No match: swap back.
            _swapBack = true;
            _phase = Phase.Swap;
            _t = 0;
            (_sr0, _sc0, _sr1, _sc1) = (_sr1, _sc1, _sr0, _sc0);
            Sound.Play(Sfx.Wrong, 0, 0.5f);
            return;
        }
        _moves--;
        _chain = 1;
        BeginClear();
    }

    /// <summary>Marks every run of three or more and records the special jewels they create.</summary>
    private bool FindMatches()
    {
        _runs.Clear();
        for (int r = 0; r < N; r++)
            for (int c = 0; c < N; c++)
                _hRun[r, c] = _vRun[r, c] = -1;
        for (int r = 0; r < N; r++)
        {
            int c = 0;
            while (c < N)
            {
                int t = _g[r, c].Type, e = c + 1;
                while (e < N && t >= 0 && t != Star && _g[r, e].Type == t) e++;
                if (t >= 0 && t != Star && e - c >= 3)
                {
                    for (int k = c; k < e; k++) _hRun[r, k] = _runs.Count;
                    _runs.Add((r, c, e - c, true));
                }
                c = e;
            }
        }
        for (int c = 0; c < N; c++)
        {
            int r = 0;
            while (r < N)
            {
                int t = _g[r, c].Type, e = r + 1;
                while (e < N && t >= 0 && t != Star && _g[e, c].Type == t) e++;
                if (t >= 0 && t != Star && e - r >= 3)
                {
                    for (int k = r; k < e; k++) _vRun[k, c] = _runs.Count;
                    _runs.Add((r, c, e - r, false));
                }
                r = e;
            }
        }
        if (_runs.Count == 0)
            return false;

        Span<bool> used = stackalloc bool[_runs.Count];
        // Fives make stars.
        for (int i = 0; i < _runs.Count; i++)
        {
            var run = _runs[i];
            if (run.len < 5) continue;
            used[i] = true;
            var (pr, pc) = Pivot(run);
            _creates.Add(new Create { R = pr, C = pc, Type = Star, Special = SpStar });
        }
        // Crossings make bombs.
        for (int r = 0; r < N; r++)
            for (int c = 0; c < N; c++)
            {
                int h = _hRun[r, c], v = _vRun[r, c];
                if (h < 0 || v < 0 || used[h] || used[v]) continue;
                used[h] = used[v] = true;
                _creates.Add(new Create { R = r, C = c, Type = _g[r, c].Type, Special = SpBomb });
            }
        // Fours make striped jewels.
        for (int i = 0; i < _runs.Count; i++)
        {
            var run = _runs[i];
            if (used[i] || run.len != 4) continue;
            var (pr, pc) = Pivot(run);
            _creates.Add(new Create { R = pr, C = pc, Type = _g[pr, pc].Type, Special = run.horiz ? SpRow : SpCol });
        }
        foreach (var run in _runs)
            for (int k = 0; k < run.len; k++)
            {
                int r = run.horiz ? run.r0 : run.r0 + k, c = run.horiz ? run.c0 + k : run.c0;
                _clear[r, c] = true;
            }
        return true;
    }

    private (int r, int c) Pivot((int r0, int c0, int len, bool horiz) run)
    {
        for (int k = 0; k < run.len; k++)
        {
            int r = run.horiz ? run.r0 : run.r0 + k, c = run.horiz ? run.c0 + k : run.c0;
            if (_phase == Phase.Swap && ((r == _sr0 && c == _sc0) || (r == _sr1 && c == _sc1)))
                return (r, c);
        }
        int m = run.len / 2;
        return run.horiz ? (run.r0, run.c0 + m) : (run.r0 + m, run.c0);
    }

    private void BeginClear()
    {
        // Specials caught in the blast fire, possibly setting off more.
        _queue.Clear();
        for (int r = 0; r < N; r++)
            for (int c = 0; c < N; c++)
                if (_clear[r, c] && _g[r, c].Special != SpNone && !_fired[r, c])
                    _queue.Add((r, c));
        int specials = 0;
        while (_queue.Count > 0)
        {
            var (r, c) = _queue[^1];
            _queue.RemoveAt(_queue.Count - 1);
            if (_fired[r, c]) continue;
            _fired[r, c] = true;
            specials++;
            var cell = _g[r, c];
            var p = CellCentre(r, c);
            switch (cell.Special)
            {
                case SpRow:
                    for (int k = 0; k < N; k++) Mark(r, k);
                    for (int k = 0; k < 14; k++)
                        Fx.Spark(p.X, p.Y, (k % 2 == 0 ? 1 : -1) * Rand(200, 500), Rand(-15, 15), Pal.White, 0.45f, 2.5f);
                    Sound.Play(Sfx.Laser, Rand(-0.1f, 0.2f), 0.7f);
                    break;
                case SpCol:
                    for (int k = 0; k < N; k++) Mark(k, c);
                    for (int k = 0; k < 14; k++)
                        Fx.Spark(p.X, p.Y, Rand(-15, 15), (k % 2 == 0 ? 1 : -1) * Rand(200, 500), Pal.White, 0.45f, 2.5f);
                    Sound.Play(Sfx.Laser, Rand(-0.1f, 0.2f), 0.7f);
                    break;
                case SpBomb:
                    for (int dr = -1; dr <= 1; dr++)
                        for (int dc = -1; dc <= 1; dc++)
                            Mark(r + dr, c + dc);
                    Fx.Explode(p.X, p.Y, 1.1f);
                    Sound.Play(Sfx.Explode, 0, 0.8f);
                    break;
                case SpStar:
                {
                    int colour = MostCommonColour();
                    for (int rr = 0; rr < N; rr++)
                        for (int cc = 0; cc < N; cc++)
                            if (_g[rr, cc].Type == colour) Mark(rr, cc);
                    StarRays(r, c, colour);
                    Sound.Play(Sfx.Warp, 0.3f, 0.8f);
                    break;
                }
            }
        }
        // Created specials stay on the board.
        foreach (var cr in _creates)
            _clear[cr.R, cr.C] = true;

        int count = 0;
        float sx = 0, sy = 0;
        for (int r = 0; r < N; r++)
            for (int c = 0; c < N; c++)
                if (_clear[r, c])
                {
                    count++;
                    var p = CellCentre(r, c);
                    sx += p.X;
                    sy += p.Y;
                    int t = _g[r, c].Type;
                    var col = t == Star || t < 0 ? Pal.White : GemColours[t];
                    Fx.Burst(p.X, p.Y, col, 7, 110, 0.5f, 2f);
                    Fx.Spark(p.X, p.Y, Rand(-30, 30), Rand(-80, -30), Pal.White, 0.4f, 1.5f);
                }
        if (count > 0)
        {
            int points = count * 10 * _chain + specials * 30 + _creates.Count * 20;
            _levelScore += points;
            AddScore(points, sx / count, sy / count - 8, _chain > 1 ? Pal.Cyan : Pal.Yellow);
            Sound.Play(Sfx.Pop, MathF.Min(0.9f, -0.2f + _chain * 0.18f), 0.8f);
            if (_chain >= 2)
            {
                Sound.Play(Sfx.Correct, MathF.Min(1, _chain * 0.15f), 0.5f);
                _comboText = _chain >= 5 ? "INCREDIBLE!" : _chain >= 4 ? "AMAZING!" : _chain >= 3 ? "SUPERB!" : "CASCADE!";
                _comboFlash = 1.2f;
            }
            if (_creates.Count > 0)
                Sound.Play(Sfx.PowerUp, 0.3f, 0.6f);
        }
        _phase = Phase.Clear;
        _t = 0;
    }

    private void Mark(int r, int c)
    {
        if (r < 0 || c < 0 || r >= N || c >= N || _g[r, c].Type < 0)
            return;
        _clear[r, c] = true;
        if (_g[r, c].Special != SpNone && !_fired[r, c])
            _queue.Add((r, c));
    }

    private int MostCommonColour()
    {
        Span<int> counts = stackalloc int[8];
        for (int r = 0; r < N; r++)
            for (int c = 0; c < N; c++)
                if (_g[r, c].Type >= 0 && _g[r, c].Type < 7 && !_clear[r, c])
                    counts[_g[r, c].Type]++;
        int best = 0;
        for (int i = 1; i < 7; i++)
            if (counts[i] > counts[best]) best = i;
        return best;
    }

    private void StarRays(int r0, int c0, int colour)
    {
        var p0 = CellCentre(r0, c0);
        for (int r = 0; r < N; r++)
            for (int c = 0; c < N; c++)
                if (_g[r, c].Type == colour)
                {
                    var p = CellCentre(r, c);
                    var d = p - p0;
                    Fx.Spark(p0.X, p0.Y, d.X * 2.5f, d.Y * 2.5f, colour < 7 && colour >= 0 ? GemColours[colour] : Pal.White, 0.4f, 3f);
                }
        Fx.Burst(p0.X, p0.Y, Pal.White, 24, 180, 0.6f, 2.5f);
    }

    private void FinishClear()
    {
        for (int r = 0; r < N; r++)
            for (int c = 0; c < N; c++)
            {
                if (_clear[r, c])
                    _g[r, c] = new Cell { Type = -1 };
                _clear[r, c] = false;
                _fired[r, c] = false;
            }
        foreach (var cr in _creates)
        {
            _g[cr.R, cr.C] = new Cell { Type = cr.Type, Special = cr.Special };
            var p = CellCentre(cr.R, cr.C);
            Fx.Burst(p.X, p.Y, Pal.White, 16, 90, 0.5f, 2f);
        }
        _creates.Clear();

        // Gravity.
        for (int c = 0; c < N; c++)
        {
            int write = N - 1;
            for (int r = N - 1; r >= 0; r--)
            {
                if (_g[r, c].Type < 0) continue;
                if (write != r)
                {
                    var cell = _g[r, c];
                    cell.OffY += (r - write) * C;
                    _g[write, c] = cell;
                    _g[r, c] = new Cell { Type = -1 };
                }
                write--;
            }
            int missing = write + 1;
            for (int r = write; r >= 0; r--)
                _g[r, c] = new Cell { Type = RandInt(0, _colours), OffY = -missing * C - 6 };
        }
        _phase = Phase.Fall;
        _t = 0;
    }

    private void UpdateFall()
    {
        bool moving = false;
        for (int r = 0; r < N; r++)
            for (int c = 0; c < N; c++)
            {
                ref var cell = ref _g[r, c];
                if (cell.OffY >= 0) continue;
                cell.Vel += 2400 * Dt;
                cell.OffY += cell.Vel * Dt;
                if (cell.OffY >= 0)
                {
                    cell.OffY = 0;
                    if (cell.Vel > 300 && _landSounds++ < 2)
                        Sound.Play(Sfx.Tick, Rand(0.2f, 0.6f), 0.25f);
                    cell.Vel = 0;
                }
                else
                    moving = true;
            }
        if (moving)
            return;

        if (FindMatches())
        {
            _chain++;
            BeginClear();
            return;
        }
        _chain = 0;
        if (_levelScore >= _target)
        {
            int bonus = _moves * 50;
            if (bonus > 0)
                AddScore(bonus, 180, 200, Pal.Cyan);
            _levelScore += bonus;
            _bannerText = "LEVEL CLEAR!";
            _banner = 2f;
            _phase = Phase.LevelDone;
            _t = 0;
            Sound.Play(Sfx.LevelUp);
            for (int i = 0; i < 6; i++)
                Fx.Burst(Rand(BoardX, BoardX + N * C), Rand(BoardY, BoardY + N * C), Pal.Rainbow[i], 20, 150, 0.9f, 2.5f);
            return;
        }
        if (_moves <= 0)
        {
            EndGame(false, "Out of moves!");
            return;
        }
        if (!HasMove())
        {
            Reshuffle();
            return;
        }
        _phase = Phase.Idle;
        _idle = 0;
    }

    private void Reshuffle()
    {
        for (int tries = 0; tries < 200; tries++)
        {
            for (int i = N * N - 1; i > 0; i--)
            {
                int j = RandInt(0, i + 1);
                (_g[i / N, i % N], _g[j / N, j % N]) = (_g[j / N, j % N], _g[i / N, i % N]);
            }
            if (!AnyMatch() && HasMove())
                break;
            if (tries > 150)
                FillBoard();
        }
        for (int r = 0; r < N; r++)
            for (int c = 0; c < N; c++)
            {
                _g[r, c].OffY = -(N + 1) * C - c * 10;
                _g[r, c].Vel = 0;
            }
        _shuffleFlash = 1.5f;
        Sound.Play(Sfx.Shuffle);
        _phase = Phase.Fall;
    }

    private bool AnyMatch()
    {
        for (int r = 0; r < N; r++)
            for (int c = 0; c < N; c++)
                if (MatchAt(r, c) >= 3) return true;
        return false;
    }

    /// <summary>Length of the longest line through (r, c).</summary>
    private int MatchAt(int r, int c)
    {
        int t = _g[r, c].Type;
        if (t < 0 || t == Star) return 0;
        int h = 1, v = 1;
        for (int k = c - 1; k >= 0 && _g[r, k].Type == t; k--) h++;
        for (int k = c + 1; k < N && _g[r, k].Type == t; k++) h++;
        for (int k = r - 1; k >= 0 && _g[k, c].Type == t; k--) v++;
        for (int k = r + 1; k < N && _g[k, c].Type == t; k++) v++;
        int best = 0;
        if (h >= 3) best += h;
        if (v >= 3) best += v;
        return best;
    }

    private int SwapValue(int r0, int c0, int r1, int c1)
    {
        if (_g[r0, c0].Special == SpStar || _g[r1, c1].Special == SpStar)
            return 12;
        if (_g[r0, c0].Special != SpNone && _g[r1, c1].Special != SpNone)
            return 10;
        SwapCells(r0, c0, r1, c1);
        int v = MatchAt(r0, c0) + MatchAt(r1, c1);
        SwapCells(r0, c0, r1, c1);
        if (v > 0)
            v += (_g[r0, c0].Special != SpNone ? 3 : 0) + (_g[r1, c1].Special != SpNone ? 3 : 0);
        return v;
    }

    private bool HasMove() => FindBestMove(out _, out _, out _, out _, false) > 0;

    private int FindBestMove(out int br0, out int bc0, out int br1, out int bc1, bool random)
    {
        br0 = bc0 = br1 = bc1 = -1;
        int best = 0;
        for (int r = 0; r < N; r++)
            for (int c = 0; c < N; c++)
                for (int d = 0; d < 2; d++)
                {
                    int r1 = r + (d == 1 ? 1 : 0), c1 = c + (d == 0 ? 1 : 0);
                    if (r1 >= N || c1 >= N) continue;
                    int v = SwapValue(r, c, r1, c1);
                    if (v <= 0) continue;
                    // Prefer lower moves (more cascades) and a little randomness.
                    float score = v * 10 + r + (random ? Rand(0, 12) : 0);
                    if (score > best)
                    {
                        best = (int)score;
                        br0 = r; bc0 = c; br1 = r1; bc1 = c1;
                    }
                }
        return best;
    }

    // ------------------------------------------------------------------ drawing

    private static Vector2 CellCentre(int r, int c) => new(BoardX + c * C + C / 2, BoardY + r * C + C / 2);

    private static Vector2[][] BuildShapes()
    {
        static Vector2[] Ngon(int n, float rot, float sx = 1, float sy = 1)
        {
            var pts = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                float a = rot + MathF2.Tau * i / n;
                pts[i] = new Vector2(MathF.Cos(a) * sx, MathF.Sin(a) * sy);
            }
            return pts;
        }
        return
        [
            Ngon(8, MathF.PI / 8, 0.95f, 0.95f),                 // ruby: octagon
            Ngon(6, 0, 1f, 0.92f),                                // amber: hexagon
            Ngon(4, -MathF.PI / 2, 0.85f, 1.05f),                 // topaz: diamond
            [new(-0.45f, -0.95f), new(0.45f, -0.95f), new(0.8f, -0.55f), new(0.8f, 0.55f),
             new(0.45f, 0.95f), new(-0.45f, 0.95f), new(-0.8f, 0.55f), new(-0.8f, -0.55f)], // emerald
            Ngon(20, 0, 0.92f, 0.92f),                            // sapphire: round
            Ngon(3, -MathF.PI / 2, 1.08f, 1.08f),                 // amethyst: triangle
            Ngon(5, -MathF.PI / 2, 1f, 1f),                       // aquamarine: pentagon
        ];
    }

    private void DrawGem(Gfx g, int type, int special, float x, float y, float size, float time, float alpha = 1f)
    {
        var p = new Vector2(x, y);
        if (type == Star || special == SpStar)
        {
            var col = Pal.Cycle(time * 0.8f + x * 0.01f);
            g.Glow(x, y, size * 2.2f, col, 0.6f * alpha);
            StarShape(g, p, size * 1.05f, time * 1.5f, Color.White * alpha);
            StarShape(g, p, size * 0.85f, time * 1.5f, col * alpha);
            StarShape(g, p + new Vector2(0, -size * 0.1f), size * 0.45f, time * 1.5f, Pal.Lighten(col, 0.6f) * alpha);
            g.Circle(x - size * 0.2f, y - size * 0.25f, size * 0.12f, Color.White * alpha);
            return;
        }
        if (type < 0) return;
        var c = GemColours[type];
        var shape = Shapes[type];
        g.Glow(x, y, size * 1.7f, c, 0.22f * alpha);
        g.Shape(shape, p + new Vector2(0, size * 0.08f), 0, size * 1.06f, Pal.Darken(c, 0.65f) * alpha);
        g.Shape(shape, p, 0, size, Pal.Darken(c, 0.15f) * alpha);
        g.Shape(shape, p + new Vector2(0, size * 0.12f), 0, size * 0.72f, Pal.Darken(c, 0.35f) * alpha);
        g.Shape(shape, p + new Vector2(0, -size * 0.1f), 0, size * 0.58f, Pal.Lighten(c, 0.25f) * alpha);
        g.Shape(shape, p + new Vector2(-size * 0.15f, -size * 0.28f), 0, size * 0.22f, Pal.Lighten(c, 0.7f) * alpha);
        // Twinkle.
        float tw = MathF.Sin(time * 2.3f + x * 0.37f + y * 0.71f);
        if (tw > 0.93f)
        {
            float k = (tw - 0.93f) / 0.07f;
            g.Glow(x - size * 0.3f, y - size * 0.35f, size * 0.8f, Color.White, 0.8f * k * alpha);
            g.Rect(x - size * 0.3f - size * 0.4f * k, y - size * 0.35f - 0.6f, size * 0.8f * k, 1.2f, Color.White * alpha);
            g.Rect(x - size * 0.3f - 0.6f, y - size * 0.35f - size * 0.4f * k, 1.2f, size * 0.8f * k, Color.White * alpha);
        }
        if (special == SpRow || special == SpCol)
        {
            float pulse = 0.6f + 0.4f * MathF.Sin(time * 8);
            for (int i = -1; i <= 1; i++)
            {
                if (special == SpRow)
                    g.Rect(x - size * 0.75f, y + i * size * 0.36f - 1.2f, size * 1.5f, 2.4f, Color.White * (0.85f * alpha));
                else
                    g.Rect(x + i * size * 0.36f - 1.2f, y - size * 0.75f, 2.4f, size * 1.5f, Color.White * (0.85f * alpha));
            }
            g.Glow(x, y, size * 1.9f, Color.White, 0.25f * pulse * alpha);
        }
        else if (special == SpBomb)
        {
            float pulse = 0.5f + 0.5f * MathF.Sin(time * 6);
            g.Ring(x, y, size * 1.05f, 2f, Pal.Lighten(c, 0.6f) * alpha);
            g.Glow(x, y, size * 2.1f, c, (0.35f + 0.35f * pulse) * alpha);
            g.Circle(x, y, size * 0.22f, Color.White * alpha);
        }
    }

    private void StarShape(Gfx g, Vector2 p, float r, float rot, Color col)
    {
        _starPts.Clear();
        _starPts.Add(p);
        for (int i = 0; i <= 10; i++)
        {
            float a = rot - MathF.PI / 2 + MathF.PI * i / 5;
            float rr = i % 2 == 0 ? r : r * 0.45f;
            _starPts.Add(p + MathF2.FromAngle(a, rr));
        }
        g.Polygon(_starPts, col);
    }

    public override void Draw(Gfx g)
    {
        float time = Time;
        g.GradientV(0, 0, 640, 360, new Color(28, 8, 48), new Color(8, 4, 26));
        // Soft drifting lights behind the board.
        for (int i = 0; i < 6; i++)
        {
            float x = 320 + MathF.Sin(time * 0.13f + i * 1.7f) * 300;
            float y = 190 + MathF.Cos(time * 0.11f + i * 2.3f) * 140;
            g.Glow(x, y, 120, GemColours[i], 0.12f);
        }

        // Board.
        var board = new RectF(BoardX - 8, BoardY - 8, N * C + 16, N * C + 16);
        g.Glow(board.CenterX, board.CenterY, 230, Pal.Purple, 0.25f);
        g.Panel(board, new Color(14, 8, 34), new Color(150, 90, 220), 10);
        for (int r = 0; r < N; r++)
            for (int c = 0; c < N; c++)
                g.RoundRect(BoardX + c * C + 1, BoardY + r * C + 1, C - 2, C - 2, 5,
                    (r + c) % 2 == 0 ? new Color(34, 22, 66) : new Color(26, 16, 52));

        // Highlights.
        if (_selR >= 0)
        {
            var p = CellCentre(_selR, _selC);
            float pulse = 0.6f + 0.4f * MathF.Sin(time * 10);
            g.Glow(p.X, p.Y, C, Pal.Yellow, 0.4f * pulse);
            g.RectOutline(p.X - C / 2 + 1, p.Y - C / 2 + 1, C - 2, C - 2, 2, Pal.Yellow);
        }
        if (_keyboard && _phase == Phase.Idle && !IsTouch)
        {
            var p = CellCentre(_curR, _curC);
            g.RectOutline(p.X - C / 2, p.Y - C / 2, C, C, 2, Color.White * (0.6f + 0.4f * MathF.Sin(time * 6)));
        }
        if (_phase == Phase.Idle && _idle > 6 && FindBestMove(out int hr0, out int hc0, out int hr1, out int hc1, false) > 0)
        {
            float k = MathF2.Pulse(_idle, 1f);
            var a = CellCentre(hr0, hc0);
            var b = CellCentre(hr1, hc1);
            g.Glow(a.X, a.Y, C * 0.8f, Color.White, 0.35f * k);
            g.Glow(b.X, b.Y, C * 0.8f, Color.White, 0.35f * k);
        }

        // Jewels.
        g.SetClip(new RectF(BoardX - 4, BoardY - 4, N * C + 8, N * C + 8));
        for (int r = 0; r < N; r++)
            for (int c = 0; c < N; c++)
            {
                var cell = _g[r, c];
                if (cell.Type < 0) continue;
                var p = CellCentre(r, c);
                p.Y += cell.OffY;
                float size = 14.5f;
                float alpha = 1;
                if (_phase == Phase.Swap)
                {
                    float k = MathF2.EaseInOut(MathF2.Clamp(_t / 0.16f, 0, 1));
                    if (r == _sr0 && c == _sc0)
                    {
                        p = Vector2.Lerp(CellCentre(_sr0, _sc0), CellCentre(_sr1, _sc1), k);
                        size *= 1 + 0.15f * MathF.Sin(k * MathF.PI);
                    }
                    else if (r == _sr1 && c == _sc1)
                        p = Vector2.Lerp(CellCentre(_sr1, _sc1), CellCentre(_sr0, _sc0), k);
                }
                if (_phase == Phase.Clear && _clear[r, c] && !IsCreated(r, c))
                {
                    float k = MathF2.Clamp(_t / 0.26f, 0, 1);
                    size *= 1 + 0.3f * k;
                    alpha = 1 - k;
                    g.Glow(p.X, p.Y, C * (0.6f + k), Color.White, 0.7f * (1 - k));
                }
                if (r == _selR && c == _selC)
                    size *= 1.08f + 0.04f * MathF.Sin(time * 10);
                DrawGem(g, cell.Type, cell.Special, p.X, p.Y, size, time, alpha);
            }
        g.SetClip(null);

        DrawPanel(g, time);

        if (_comboFlash > 0)
        {
            float a = MathF.Min(1, _comboFlash * 2);
            float s = 2.5f + (1.2f - _comboFlash) * 0.6f;
            g.TextShadow(_comboText, BoardX + N * C / 2, BoardY + N * C / 2 - 12, s, Pal.Cycle(time * 2) * a, Align.Center);
        }
        if (_shuffleFlash > 0)
            g.TextShadow("NO MOVES - SHUFFLE!", BoardX + N * C / 2, BoardY + N * C / 2 - 8, 2f, Pal.White * MathF.Min(1, _shuffleFlash), Align.Center);
        if (_banner > 0)
        {
            float a = MathF.Min(1, _banner * 1.5f);
            var cx = BoardX + N * C / 2;
            g.RoundRect(cx - 130, BoardY + N * C / 2 - 30, 260, 56, 10, Color.Black * (0.6f * a));
            g.TextShadow(_bannerText, cx, BoardY + N * C / 2 - 20, 3f, Pal.Yellow * a, Align.Center);
            if (_phase != Phase.LevelDone)
                g.Text("TARGET " + _target, cx, BoardY + N * C / 2 + 8, 1.5f, Pal.White * a, Align.Center);
            else
                g.Text("MOVES BONUS " + _moves * 50, cx, BoardY + N * C / 2 + 8, 1.5f, Pal.Cyan * a, Align.Center);
        }
    }

    private bool IsCreated(int r, int c)
    {
        foreach (var cr in _creates)
            if (cr.R == r && cr.C == c) return true;
        return false;
    }

    private void DrawPanel(Gfx g, float time)
    {
        var panel = new RectF(BoardX + N * C + 24, BoardY - 8, 640 - (BoardX + N * C + 24) - 18, N * C + 16);
        g.Panel(panel, new Color(18, 10, 40) * 0.95f, new Color(110, 60, 170), 10);
        float x = panel.X + 16, w = panel.W - 32;
        float y = panel.Y + 14;

        g.Text("LEVEL", x, y, 1.5f, Pal.LightGrey);
        g.TextShadow(Level.ToString(), x + w, y - 4, 2.5f, Pal.Yellow, Align.Right);
        y += 32;

        g.Text("TARGET", x, y, 1.5f, Pal.LightGrey);
        g.Text(_target.ToString(), x + w, y, 1.5f, Pal.White, Align.Right);
        y += 22;
        // Progress bar.
        float k = MathF2.Clamp(_displayScore / _target, 0, 1);
        g.RoundRect(x, y, w, 18, 6, new Color(8, 4, 20));
        if (k > 0)
        {
            var fill = k >= 1 ? Pal.Lime : Pal.Lerp(Pal.Magenta, Pal.Gold, k);
            g.RoundRect(x + 2, y + 2, (w - 4) * k, 14, 5, fill);
            g.Rect(x + 4, y + 4, (w - 8) * k, 3, Color.White * 0.35f);
            g.Glow(x + 2 + (w - 4) * k, y + 9, 16, fill, 0.6f);
        }
        g.RectOutline(x, y, w, 18, 1, Color.White * 0.15f);
        y += 26;
        g.Text(((int)_displayScore).ToString(), x + w / 2, y, 2f, k >= 1 ? Pal.Lime : Pal.White, Align.Center);
        y += 34;

        g.Text("MOVES", x, y, 1.5f, Pal.LightGrey);
        y += 16;
        bool low = _moves <= 4;
        var mc = low ? Pal.Lerp(Pal.Red, Pal.Orange, MathF2.Pulse(time, 0.6f)) : Pal.Cyan;
        g.Glow(x + w / 2, y + 20, 50, mc, low ? 0.4f : 0.2f);
        g.TextShadow(_moves.ToString(), x + w / 2, y, 5f, mc, Align.Center);
        y += 54;

        // Legend of specials.
        g.Rect(x, y, w, 1, Color.White * 0.15f);
        y += 10;
        DrawGem(g, 4, SpRow, x + 10, y + 10, 9, time);
        g.Text("4 IN A LINE", x + 26, y + 6, 1f, Pal.LightGrey);
        y += 24;
        DrawGem(g, 1, SpBomb, x + 10, y + 10, 9, time);
        g.Text("L OR T SHAPE", x + 26, y + 6, 1f, Pal.LightGrey);
        y += 24;
        DrawGem(g, Star, SpStar, x + 10, y + 10, 9, time);
        g.Text("5 IN A LINE", x + 26, y + 6, 1f, Pal.LightGrey);
    }

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        g.GradientV(r.X, r.Y, r.W, r.H, new Color(40, 12, 70), new Color(10, 4, 30));
        g.Glow(r.CenterX, r.CenterY, r.W * 0.6f, Pal.Purple, 0.4f);
        float cell = r.H / 3.4f;
        int cols = Math.Max(3, (int)(r.W / cell));
        float x0 = r.CenterX - cols * cell / 2;
        float y0 = r.CenterY - 1.5f * cell;
        float swap = MathF2.EaseInOut(MathF2.Clamp((time % 3f - 1f) / 0.4f, 0, 1));
        float back = MathF2.EaseInOut(MathF2.Clamp((time % 3f - 2.2f) / 0.4f, 0, 1));
        float k = swap - back;
        for (int row = 0; row < 3; row++)
            for (int c = 0; c < cols; c++)
            {
                int t = (row * 5 + c * 3 + (row == 1 ? 1 : 0)) % 6;
                if (row == 1 && c == cols / 2) t = 2;
                if (row == 0 && c == cols / 2 + 1) t = 2;
                if (row == 2 && c == cols / 2 + 1) t = 2;
                if (row == 1 && c == cols / 2 + 1) t = 4;
                float x = x0 + c * cell + cell / 2, y = y0 + row * cell + cell / 2;
                g.RoundRect(x - cell / 2 + 1, y - cell / 2 + 1, cell - 2, cell - 2, 3, (row + c) % 2 == 0 ? new Color(50, 30, 90) : new Color(36, 22, 70));
                if (row == 1 && c == cols / 2) x += cell * k;
                else if (row == 1 && c == cols / 2 + 1) x -= cell * k;
                DrawGem(g, t, (row == 2 && c == 1) ? SpBomb : SpNone, x, y, cell * 0.38f, time);
            }
        if (k > 0.95f)
        {
            float cx = x0 + (cols / 2 + 1) * cell + cell / 2;
            g.Glow(cx, r.CenterY, cell * 2, Pal.Yellow, 0.6f);
        }
    }

    public override void AutoPlay(Controls c)
    {
        if (_phase != Phase.Idle)
        {
            _apStep = 0;
            _apTimer = 0;
            return;
        }
        if (++_apTimer < 30)
            return;
        if (_apStep == 0)
        {
            if (FindBestMove(out _apR0, out _apC0, out _apR1, out _apC1, true) <= 0)
                return;
            c.Pointer = CellCentre(_apR0, _apC0);
            c.PointerPressed = true;
            c.PointerDown = true;
            _apStep = 1;
            return;
        }
        float k = MathF.Min(1, _apStep / 6f);
        c.Pointer = Vector2.Lerp(CellCentre(_apR0, _apC0), CellCentre(_apR1, _apC1), k);
        c.PointerDown = true;
        c.PointerMoved = true;
        _apStep++;
        if (_apStep > 8)
        {
            c.PointerDown = false;
            c.PointerReleased = true;
            _apStep = 0;
            _apTimer = 0;
        }
    }
}
