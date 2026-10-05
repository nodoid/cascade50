using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 25 Noughts and Crosses: neon chalk tic-tac-toe against the computer, moving up to four-in-a-row
/// on a 4x4 board. The computer thinks harder every round.
/// </summary>
public sealed class NoughtsAndCrosses : MiniGame
{
    public override int Number => 25;
    public override string Title => "Noughts and Crosses";
    public override Category Category => Category.Brain;
    public override string Tagline => "Outwit the computer at three, then four, in a row.";
    public override Color Accent => Pal.Pink;
    public override Pad Pad => Pad.None;

    public override string[] HowToPlay =>
    [
        "You are X. Get three in a row before the computer's O does. From round 4 it's a 4x4 board and you need four.",
        "Wins score 100 x the round, draws 25 x. The computer gets smarter; three defeats end the game.",
    ];

    public override string[] DesktopControls => ["Click a square.", "Or ARROWS to move, SPACE to place."];
    public override string[] TouchControls => ["Tap a square to place your X."];

    private const int MaxRounds = 12;
    private const float BoardCX = 240, BoardCY = 192;

    private enum Phase { Intro, Player, CpuThink, RoundOver }

    private readonly int[] _cells = new int[16];   // 0 empty, 1 X (player), 2 O (computer)
    private readonly float[] _placed = new float[16];
    private int _size, _need;
    private readonly List<int[]> _lines = new();
    private int[] _winLine;
    private float _winT;
    private Phase _phase;
    private float _timer;
    private int _cursor;
    private int _hover = -1;
    private int _round, _wins, _draws, _losses;
    private int _result; // 1 player won, 2 computer won, 3 draw
    private float _gridT;
    private string _message = "";

    // Autoplay.
    private int _apStage;
    private float _apWait;
    private int _apCell;

    protected override void Start()
    {
        Lives = 3;
        _round = 0;
        _wins = _draws = _losses = 0;
        NewRound();
    }

    private void NewRound()
    {
        _round++;
        Level = _round;
        _size = _round <= 3 ? 3 : 4;
        _need = _size;
        Array.Clear(_cells);
        _winLine = null;
        _gridT = 0;
        BuildLines();
        _cursor = _size * _size / 2;
        Status = $"ROUND {_round} OF {MaxRounds}";
        _phase = Phase.Intro;
        _timer = 0.8f;
        _apStage = 0;
        _apWait = 0.5f;
        Sound.Play(Sfx.Whoosh, 0.2f, 0.5f);
        _message = _round == 4 ? "NOW FOUR IN A ROW!" : PlayerStarts ? "YOU GO FIRST" : "COMPUTER GOES FIRST";
    }

    private bool PlayerStarts => _round % 2 == 1;

    private void BuildLines()
    {
        _lines.Clear();
        int n = _size;
        for (int r = 0; r < n; r++)
        {
            var row = new int[n];
            var col = new int[n];
            for (int i = 0; i < n; i++)
            {
                row[i] = r * n + i;
                col[i] = i * n + r;
            }
            _lines.Add(row);
            _lines.Add(col);
        }
        var d1 = new int[n];
        var d2 = new int[n];
        for (int i = 0; i < n; i++)
        {
            d1[i] = i * n + i;
            d2[i] = i * n + (n - 1 - i);
        }
        _lines.Add(d1);
        _lines.Add(d2);
    }

    private float Cell => _size == 3 ? 88 : 70;
    private float BoardX => BoardCX - Cell * _size / 2;
    private float BoardY => BoardCY - Cell * _size / 2;
    private Vector2 CellCentre(int i) => new(BoardX + (i % _size + 0.5f) * Cell, BoardY + (i / _size + 0.5f) * Cell);

    private int CellAt(Vector2 p)
    {
        int cx = (int)MathF.Floor((p.X - BoardX) / Cell), cy = (int)MathF.Floor((p.Y - BoardY) / Cell);
        return cx < 0 || cy < 0 || cx >= _size || cy >= _size ? -1 : cy * _size + cx;
    }

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        _gridT += Dt;
        if (_winLine != null)
            _winT += Dt;

        _hover = _phase == Phase.Player && (In.HasHover || In.PointerDown) ? CellAt(In.Pointer) : -1;

        switch (_phase)
        {
            case Phase.Intro:
                _timer -= Dt;
                if (_timer <= 0)
                {
                    if (PlayerStarts)
                        _phase = Phase.Player;
                    else
                        StartCpu();
                }
                break;
            case Phase.Player:
                PlayerInput();
                break;
            case Phase.CpuThink:
                _timer -= Dt;
                if (_timer <= 0)
                {
                    int m = CpuMove();
                    Place(m, 2);
                    if (!CheckEnd())
                    {
                        _phase = Phase.Player;
                        _message = "YOUR TURN";
                    }
                }
                break;
            case Phase.RoundOver:
                _timer -= Dt;
                if (_timer <= 0)
                {
                    if (_losses >= 3)
                        EndGame(false, $"{_wins} won, {_draws} drawn.");
                    else if (_round >= MaxRounds)
                        EndGame(true, $"Series over: {_wins} won, {_draws} drawn!");
                    else
                        NewRound();
                }
                break;
        }
    }

    private void StartCpu()
    {
        _phase = Phase.CpuThink;
        _timer = Rand(0.5f, 0.9f);
        _message = "COMPUTER THINKING...";
    }

    private void PlayerInput()
    {
        int n = _size;
        int cx = _cursor % n, cy = _cursor / n;
        if (In.LeftPressed) cx = (cx + n - 1) % n;
        if (In.RightPressed) cx = (cx + 1) % n;
        if (In.UpPressed) cy = (cy + n - 1) % n;
        if (In.DownPressed) cy = (cy + 1) % n;
        int nc = cy * n + cx;
        if (nc != _cursor)
        {
            _cursor = nc;
            Sound.Play(Sfx.Tick, 0.3f, 0.4f);
        }
        if (In.PointerMoved && _hover >= 0)
            _cursor = _hover;

        int pick = -1;
        if (In.PointerPressed)
        {
            int c = CellAt(In.Pointer);
            if (c >= 0)
            {
                _cursor = c;
                pick = c;
            }
        }
        if (In.FirePressed || In.EnterPressed)
            pick = _cursor;
        if (pick < 0)
            return;
        if (_cells[pick] != 0)
        {
            Sound.Play(Sfx.Wrong, 0, 0.4f);
            return;
        }
        Place(pick, 1);
        if (!CheckEnd())
            StartCpu();
    }

    private void Place(int cell, int who)
    {
        _cells[cell] = who;
        _placed[cell] = Time;
        var p = CellCentre(cell);
        Sound.Play(who == 1 ? Sfx.Laser : Sfx.Zap, who == 1 ? 0.3f : -0.1f, 0.35f);
        Sound.Play(Sfx.Pop, who == 1 ? 0.2f : -0.2f, 0.5f);
        Fx.Burst(p.X, p.Y, who == 1 ? Pal.Pink : Pal.Cyan, 14, 70, 0.5f, 2f);
    }

    private bool CheckEnd()
    {
        int w = Winner(_cells, out var line);
        bool full = true;
        for (int i = 0; i < _size * _size; i++)
            full &= _cells[i] != 0;
        if (w == 0 && !full)
            return false;
        _phase = Phase.RoundOver;
        _timer = 2.8f;
        _winLine = line;
        _winT = 0;
        float mult = _size == 4 ? 1.5f : 1f;
        if (w == 1)
        {
            _result = 1;
            _wins++;
            AddScore((int)(100 * _round * mult), BoardCX, BoardCY, Pal.Yellow);
            _message = "YOU WIN!";
            Sound.Play(Sfx.Bonus);
            var a = CellCentre(line[0]);
            var b = CellCentre(line[^1]);
            for (int i = 0; i < 6; i++)
            {
                var p = Vector2.Lerp(a, b, i / 5f);
                Fx.Burst(p.X, p.Y, Pal.Gold, 12, 120, 0.8f, 2.5f);
            }
        }
        else if (w == 2)
        {
            _result = 2;
            _losses++;
            Lives = 3 - _losses;
            _message = "THE COMPUTER WINS";
            Sound.Play(Sfx.Lose);
            Fx.Shake(3, 0.3f);
        }
        else
        {
            _result = 3;
            _draws++;
            AddScore((int)(25 * _round * mult), BoardCX, BoardCY, Pal.Sky);
            _message = "A DRAW";
            Sound.Play(Sfx.Correct, -0.3f, 0.6f);
        }
        return true;
    }

    private int Winner(int[] cells, out int[] line)
    {
        foreach (var l in _lines)
        {
            int v = cells[l[0]];
            if (v == 0)
                continue;
            bool all = true;
            for (int i = 1; i < l.Length && all; i++)
                all = cells[l[i]] == v;
            if (all)
            {
                line = l;
                return v;
            }
        }
        line = null;
        return 0;
    }

    // ------------------------------------------------------------------ the computer

    private int CpuMove() => ChooseMove(2, _round switch
    {
        1 => 0.5f, 2 => 0.35f, 3 => 0.18f, 4 => 0.22f, 5 => 0.14f, 6 => 0.07f, _ => 0f,
    }, _size == 3 ? 9 : _round <= 5 ? 2 : _round <= 7 ? 3 : 4);

    private readonly long[] _scores = new long[16];

    private int ChooseMove(int me, float mistake, int depth)
    {
        int n = _size * _size;
        int empties = 0;
        for (int i = 0; i < n; i++)
            if (_cells[i] == 0)
                empties++;
        if (empties == 0)
            return 0;
        if (empties == n && _size == 3)
        {
            // Open with the centre or a corner.
            int[] openings = [4, 0, 2, 6, 8];
            return openings[RandInt(0, openings.Length)];
        }
        int best = -1;
        long bestScore = long.MinValue;
        foreach (int c in Ordered())
        {
            if (_cells[c] != 0)
                continue;
            _cells[c] = me;
            long s = -Negamax(3 - me, depth - 1, int.MinValue + 1, int.MaxValue - 1, 1);
            // Among equally good moves, prefer ones that build threats (then a little randomness).
            s = s * 2000 + Math.Clamp(Evaluate(me), -900, 900) + RandInt(0, 40);
            _cells[c] = 0;
            _scores[c] = s;
            if (s > bestScore)
            {
                bestScore = s;
                best = c;
            }
        }
        if (Chance(mistake))
        {
            // A real slip: pick a move that is genuinely worse than the best, if there is one.
            long bestBand = bestScore / 2000;
            int count = 0, pick = -1;
            for (int c = 0; c < n; c++)
                if (_cells[c] == 0 && _scores[c] / 2000 < bestBand && RandInt(0, ++count) == 0)
                    pick = c;
            if (pick >= 0)
                return pick;
        }
        return best;
    }

    private readonly int[][] _order = new int[2][];

    private int[] Ordered()
    {
        int k = _size == 3 ? 0 : 1;
        if (_order[k] != null)
            return _order[k];
        int n = _size * _size;
        var list = new List<int>();
        for (int i = 0; i < n; i++)
            list.Add(i);
        float mid = (_size - 1) / 2f;
        list.Sort((a, b) =>
        {
            float da = MathF.Abs(a % _size - mid) + MathF.Abs(a / _size - mid);
            float db = MathF.Abs(b % _size - mid) + MathF.Abs(b / _size - mid);
            return da.CompareTo(db);
        });
        return _order[k] = list.ToArray();
    }

    /// <summary>Score from the point of view of <paramref name="me"/>, to move.</summary>
    private int Negamax(int me, int depth, int alpha, int beta, int ply)
    {
        int w = Winner(_cells, out _);
        if (w != 0)
            return w == me ? 100000 - ply : -100000 + ply;
        bool any = false;
        int n = _size * _size;
        for (int i = 0; i < n && !any; i++)
            any = _cells[i] == 0;
        if (!any)
            return 0;
        if (depth <= 0)
            return Evaluate(me);
        int best = int.MinValue + 1;
        foreach (int c in Ordered())
        {
            if (_cells[c] != 0)
                continue;
            _cells[c] = me;
            int s = -Negamax(3 - me, depth - 1, -beta, -alpha, ply + 1);
            _cells[c] = 0;
            if (s > best)
                best = s;
            if (best > alpha)
                alpha = best;
            if (alpha >= beta)
                break;
        }
        return best;
    }

    private int Evaluate(int me)
    {
        int score = 0;
        foreach (var l in _lines)
        {
            int mine = 0, theirs = 0;
            foreach (int c in l)
            {
                if (_cells[c] == me) mine++;
                else if (_cells[c] != 0) theirs++;
            }
            if (theirs == 0 && mine > 0) score += mine * mine * mine * 10;
            else if (mine == 0 && theirs > 0) score -= theirs * theirs * theirs * 12;
        }
        return score;
    }

    // ------------------------------------------------------------------ drawing

    public override void Draw(Gfx g)
    {
        DrawBoardBackground(g, Screen.Bounds, Time, true);

        float cell = Cell;
        float bx = BoardX, by = BoardY, size = cell * _size;
        var chalk = new Color(210, 240, 230);

        // Hover / cursor.
        if (_phase == Phase.Player)
        {
            int c = _hover >= 0 ? _hover : _cursor;
            var p = CellCentre(c);
            float pulse = MathF2.Pulse(Time, 1f);
            var col = _cells[c] == 0 ? Pal.Yellow : Pal.Red;
            g.Glow(p.X, p.Y, cell * 0.6f, col, 0.15f + 0.1f * pulse);
            g.RectOutline(p.X - cell / 2 + 7, p.Y - cell / 2 + 7, cell - 14, cell - 14, 1.5f, col * (0.5f + 0.4f * pulse));
            if (_cells[c] == 0)
                DrawX(g, p, cell * 0.28f, 1f, Pal.Pink * 0.3f);
        }

        // The grid, drawn in with chalk.
        float gp = MathF2.Clamp(_gridT / 0.6f, 0, 1);
        for (int i = 1; i < _size; i++)
        {
            float k = MathF2.EaseOut(MathF2.Clamp(gp * 1.4f - (i - 1) * 0.15f, 0, 1));
            float x = bx + i * cell, y = by + i * cell;
            float wob = MathF.Sin(i * 3.1f) * 2;
            g.GlowLine(new Vector2(x + wob, by - 6), new Vector2(x - wob, by - 6 + (size + 12) * k), 2.5f, chalk);
            g.GlowLine(new Vector2(bx - 6, y - wob), new Vector2(bx - 6 + (size + 12) * k, y + wob), 2.5f, chalk);
        }

        // Pieces.
        for (int i = 0; i < _size * _size; i++)
        {
            if (_cells[i] == 0)
                continue;
            var p = CellCentre(i);
            float t = MathF2.Clamp((Time - _placed[i]) / 0.35f, 0, 1);
            bool inWin = _winLine != null && Array.IndexOf(_winLine, i) >= 0;
            float glow = inWin ? 0.5f + 0.4f * MathF2.Pulse(_winT, 0.6f) : 0.25f;
            if (_cells[i] == 1)
            {
                g.Glow(p.X, p.Y, cell * 0.55f, Pal.Pink, glow * 0.6f);
                DrawX(g, p, cell * 0.3f, t, inWin ? Pal.Lighten(Pal.Pink, 0.3f) : Pal.Pink);
            }
            else
            {
                g.Glow(p.X, p.Y, cell * 0.55f, Pal.Cyan, glow * 0.6f);
                DrawO(g, p, cell * 0.29f, t, inWin ? Pal.Lighten(Pal.Cyan, 0.3f) : Pal.Cyan);
            }
        }

        // Winning line.
        if (_winLine != null)
        {
            var a = CellCentre(_winLine[0]);
            var b = CellCentre(_winLine[^1]);
            var dir = Vector2.Normalize(b - a);
            a -= dir * cell * 0.4f;
            b += dir * cell * 0.4f;
            float k = MathF2.EaseOut(MathF2.Clamp(_winT / 0.5f, 0, 1));
            var end = Vector2.Lerp(a, b, k);
            var col = _result == 1 ? Pal.Gold : Pal.Red;
            g.GlowLine(a, end, 4f + MathF2.Pulse(_winT, 0.6f) * 2, col);
            g.Glow(end, 20, col, 0.8f);
        }

        DrawPanel(g);
    }

    private static void DrawX(Gfx g, Vector2 p, float r, float t, Color c)
    {
        float t1 = MathF2.Clamp(t * 2, 0, 1), t2 = MathF2.Clamp(t * 2 - 1, 0, 1);
        var a1 = p + new Vector2(-r, -r);
        var b1 = p + new Vector2(r, r);
        var a2 = p + new Vector2(r, -r);
        var b2 = p + new Vector2(-r, r);
        if (t1 > 0)
            g.GlowLine(a1, Vector2.Lerp(a1, b1, t1), r * 0.16f, c);
        if (t2 > 0)
            g.GlowLine(a2, Vector2.Lerp(a2, b2, t2), r * 0.16f, c);
    }

    private static void DrawO(Gfx g, Vector2 p, float r, float t, Color c)
    {
        if (t <= 0)
            return;
        float a0 = -MathF.PI / 2, a1 = a0 + MathF2.Tau * t;
        float w = r * 0.16f;
        g.Arc(p.X, p.Y, r, w * 4, a0, a1, Pal.Add(c, 0.18f));
        g.Arc(p.X, p.Y, r, w * 2, a0, a1, Pal.Add(c, 0.3f));
        g.Arc(p.X, p.Y, r, w, a0, a1, c);
    }

    private void DrawPanel(Gfx g)
    {
        var p = new RectF(462, 34, 166, 314);
        g.Panel(p, new Color(10, 22, 18) * 0.9f, new Color(90, 150, 120) * 0.8f, 10);
        g.TextShadow("ROUND " + _round, p.CenterX, p.Y + 12, 2f, Pal.Gold, Align.Center);
        g.Text(_size == 3 ? "THREE IN A ROW" : "FOUR IN A ROW", p.CenterX, p.Y + 36, 1f, Pal.LightGrey, Align.Center);

        // Players.
        float y = p.Y + 58;
        bool you = _phase == Phase.Player;
        bool cpu = _phase == Phase.CpuThink;
        g.RoundRect(p.X + 10, y, p.W - 20, 30, 6, you ? Pal.Pink * 0.25f : Color.Black * 0.25f);
        DrawX(g, new Vector2(p.X + 28, y + 15), 7, 1, Pal.Pink);
        g.Text("YOU", p.X + 46, y + 9, 1.5f, you ? Pal.White : Pal.LightGrey);
        g.RoundRect(p.X + 10, y + 36, p.W - 20, 30, 6, cpu ? Pal.Cyan * 0.25f : Color.Black * 0.25f);
        DrawO(g, new Vector2(p.X + 28, y + 51), 7, 1, Pal.Cyan);
        g.Text("COMPUTER", p.X + 46, y + 45, 1.5f, cpu ? Pal.White : Pal.LightGrey);
        if (cpu)
            for (int i = 0; i < 3; i++)
                g.Circle(p.Right - 24 + i * 6, y + 66 + 6, 1.8f, Pal.Cyan * (0.3f + 0.7f * MathF2.Pulse(Time + i * 0.2f, 0.9f)));

        // Message.
        var mc = _phase == Phase.RoundOver ? (_result == 1 ? Pal.Lime : _result == 2 ? Pal.Red : Pal.Sky) : Pal.White;
        g.TextWrapped(_message, p.X + 10, y + 86, p.W - 20, 1.5f, mc, 1.3f, Align.Center);

        // Tally.
        float ty = p.Y + 222;
        g.Rect(p.X + 12, ty - 10, p.W - 24, 1, Pal.Teal * 0.6f);
        g.Text("WON", p.X + 18, ty, 1.5f, Pal.LightGrey);
        g.Text(_wins.ToString(), p.Right - 18, ty, 1.5f, Pal.Lime, Align.Right);
        g.Text("DRAWN", p.X + 18, ty + 20, 1.5f, Pal.LightGrey);
        g.Text(_draws.ToString(), p.Right - 18, ty + 20, 1.5f, Pal.Sky, Align.Right);
        g.Text("LOST", p.X + 18, ty + 40, 1.5f, Pal.LightGrey);
        for (int i = 0; i < 3; i++)
        {
            float x = p.Right - 22 - i * 14;
            g.Circle(x, ty + 46, 5, i < _losses ? Pal.Red : Color.Black * 0.4f);
            if (i < _losses)
                g.Glow(x, ty + 46, 10, Pal.Red, 0.4f);
        }
    }

    private static void DrawBoardBackground(Gfx g, RectF r, float time, bool tray)
    {
        // Wooden frame and a well-used blackboard.
        g.GradientV(r.X, r.Y, r.W, r.H, new Color(96, 60, 32), new Color(60, 36, 18));
        var b = r.Inflate(-r.H * 0.025f, -r.H * 0.025f);
        g.GradientV(b.X, b.Y, b.W, b.H, new Color(26, 46, 38), new Color(14, 28, 22));
        int seed = 0;
        float Rnd() { float v = MathF.Sin(++seed * 12.9898f) * 43758.547f; return v - MathF.Floor(v); }
        for (int i = 0; i < 40; i++)
        {
            float x = b.X + Rnd() * b.W, y = b.Y + Rnd() * b.H;
            float rx = 10 + Rnd() * b.W * 0.12f;
            g.Ellipse(x, y, rx, rx * (0.15f + Rnd() * 0.25f), Color.White * 0.018f);
        }
        for (int i = 0; i < 10; i++)
        {
            float x = b.X + Rnd() * b.W, y = b.Y + Rnd() * b.H;
            g.Line(x, y, x + 20 + Rnd() * 50, y + Rnd() * 10 - 5, 1, Color.White * 0.04f);
        }
        Backdrops.Vignette(g, b, 0.35f);
        if (tray)
        {
            g.Rect(r.X, r.Bottom - 8, r.W, 8, new Color(110, 70, 38));
            g.RoundRect(r.X + 40, r.Bottom - 11, 26, 5, 2, new Color(240, 240, 230));
            g.RoundRect(r.X + 74, r.Bottom - 10, 18, 4, 2, new Color(255, 160, 200));
        }
    }

    // ------------------------------------------------------------------ icon

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        float s = r.H / 70f;
        DrawBoardBackground(g, r, time, false);
        float cell = 19 * s;
        float bx = r.CenterX - cell * 1.5f, by = r.CenterY - cell * 1.5f;
        var chalk = new Color(210, 240, 230);
        for (int i = 1; i < 3; i++)
        {
            g.GlowLine(new Vector2(bx + i * cell, by), new Vector2(bx + i * cell, by + cell * 3), 0.8f * s, chalk);
            g.GlowLine(new Vector2(bx, by + i * cell), new Vector2(bx + cell * 3, by + i * cell), 0.8f * s, chalk);
        }
        Vector2 C(int i) => new(bx + (i % 3 + 0.5f) * cell, by + (i / 3 + 0.5f) * cell);
        int[] xs = [0, 3, 6];
        int[] os = [4, 8, -1];
        float cycle = (time + 2.6f) % 5f;
        for (int k = 0; k < 3; k++)
        {
            float tx = MathF2.Clamp((cycle - k * 0.9f) / 0.35f, 0, 1);
            float to = MathF2.Clamp((cycle - k * 0.9f - 0.45f) / 0.35f, 0, 1);
            if (os[k] >= 0)
                DrawO(g, C(os[k]), cell * 0.3f, to, Pal.Cyan);
            DrawX(g, C(xs[k]), cell * 0.3f, tx, Pal.Pink);
        }
        if (cycle > 2.4f)
        {
            float k = MathF2.EaseOut(MathF2.Clamp((cycle - 2.4f) / 0.5f, 0, 1));
            var a = C(0) - new Vector2(0, cell * 0.45f);
            var b = C(6) + new Vector2(0, cell * 0.45f);
            g.GlowLine(a, Vector2.Lerp(a, b, k), 1.3f * s, Pal.Gold);
            g.Glow(Vector2.Lerp(a, b, k), 8 * s, Pal.Gold, 0.8f);
        }
    }

    // ------------------------------------------------------------------ autoplay

    public override void AutoPlay(Controls c)
    {
        if (_phase != Phase.Player)
        {
            _apStage = 0;
            _apWait = 0.5f;
            return;
        }
        if (_apWait > 0)
        {
            _apWait -= Dt;
            return;
        }
        if (_apStage == 0)
        {
            _apCell = ChooseMove(1, 0.08f, _size == 3 ? 9 : 2);
            c.Pointer = CellCentre(_apCell);
            c.PointerPressed = c.PointerDown = true;
            _apStage = 1;
        }
        else
        {
            c.Pointer = CellCentre(_apCell);
            c.PointerReleased = true;
            _apStage = 0;
            _apWait = 0.4f;
        }
    }
}
