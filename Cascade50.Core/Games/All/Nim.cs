using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 24 Nim: matchstick Nim against the computer. Take any number of matches from one row; whoever
/// takes the very last match loses. The computer starts out careless and soon plays perfectly.
/// </summary>
public sealed class Nim : MiniGame, Cascade50.Core.Capture.ICaptureHints
{
    public int CaptureTicks => 280;
    public override int Number => 24;
    public override string Title => "Nim";
    public override Category Category => Category.Brain;
    public override string Tagline => "Take matches in turn. Whoever takes the last one loses.";
    public override Color Accent => Pal.Orange;
    public override Pad Pad => Pad.None;

    public override string[] HowToPlay =>
    [
        "Take as many matches as you like from ONE row. Then the computer takes its turn.",
        "Whoever takes the LAST match loses the round.",
        "Wins score 100 x the round. The computer gets sharper; three defeats end the game.",
    ];

    public override string[] DesktopControls => ["Click a match, then TAKE.", "Or UP/DOWN row, LEFT/RIGHT count, SPACE."];
    public override string[] TouchControls => ["Tap a match to pick it and all to its right, then TAKE."];

    private const int MaxRows = 5, MaxPerRow = 9;
    private const float MatX = 18, MatY = 32, MatW = 440, MatH = 318;
    private const float Spacing = 34, MatchH = 46;
    private const float BurnTime = 1.5f;

    private enum Phase { Player, CpuThink, CpuShow, RoundOver }

    private struct Burn
    {
        public int Row, Slot;
        public float T;
    }

    private readonly int[] _start = new int[MaxRows];
    private readonly int[] _heap = new int[MaxRows];
    private int _rows;
    private readonly List<Burn> _burns = new();
    private Phase _phase;
    private float _timer;
    private int _selRow, _selCount;
    private int _cpuRow, _cpuCount;
    private int _hoverRow = -1, _hoverCount;
    private int _round, _losses, _wins;
    private bool _playerStarts;
    private bool _playerWonRound;
    private string _message = "";
    private float _messageT;
    private readonly List<(int Row, int Count)> _moves = new();

    // Autoplay.
    private int _apStage;
    private float _apWait;
    private int _apRow, _apCount;

    protected override void Start()
    {
        _round = 0;
        _losses = 0;
        _wins = 0;
        Lives = 3;
        NewRound();
    }

    private void NewRound()
    {
        _round++;
        Level = _round;
        _burns.Clear();
        if (_round == 1)
        {
            _rows = 3;
            _start[0] = 3;
            _start[1] = 5;
            _start[2] = 7;
        }
        else
        {
            _rows = Math.Min(MaxRows, 3 + _round / 2);
            int total;
            do
            {
                total = 0;
                for (int r = 0; r < _rows; r++)
                {
                    _start[r] = RandInt(1, MaxPerRow + 1);
                    total += _start[r];
                }
            } while (total < 10);
        }
        for (int r = 0; r < _rows; r++)
            _heap[r] = _start[r];
        _playerStarts = _round % 2 == 1;
        Status = "ROUND " + _round;
        if (_playerStarts)
            BeginPlayerTurn();
        else
        {
            _phase = Phase.CpuThink;
            _timer = 1.2f;
            Say("THE COMPUTER GOES FIRST");
        }
        _apStage = 0;
        _apWait = 0.6f;
    }

    private void Say(string text)
    {
        _message = text;
        _messageT = 0;
    }

    private void BeginPlayerTurn()
    {
        _phase = Phase.Player;
        _selRow = FirstRow();
        _selCount = 1;
        Say("YOUR TURN");
    }

    private int FirstRow()
    {
        for (int r = 0; r < _rows; r++)
            if (_heap[r] > 0)
                return r;
        return 0;
    }

    private int Total()
    {
        int t = 0;
        for (int r = 0; r < _rows; r++)
            t += _heap[r];
        return t;
    }

    // ------------------------------------------------------------------ geometry

    private float RowGap => MathF.Min(84, (MatH - 30) / _rows);
    private float MH => MathF.Min(64, RowGap - 16);
    private float MK => MH / MatchH;
    private float RowBase(int r) => MatY + 18 + (MatH - 30 - RowGap * _rows) / 2 + RowGap * (r + 1) - 6;
    private float SlotX(int r, int slot) => MatX + MatW / 2 + 12 - (_start[r] - 1) * Spacing / 2 + slot * Spacing;

    private RectF TakeButton => new(474, 296, 150, 42);

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        _messageT += Dt;
        UpdateBurns();
        UpdateHover();

        switch (_phase)
        {
            case Phase.Player:
                PlayerInput();
                break;
            case Phase.CpuThink:
                _timer -= Dt;
                if (_timer <= 0)
                {
                    ChooseCpuMove();
                    _phase = Phase.CpuShow;
                    _timer = 0.8f;
                    Sound.Play(Sfx.Select, -0.3f, 0.6f);
                }
                break;
            case Phase.CpuShow:
                _timer -= Dt;
                if (_timer <= 0)
                {
                    TakeMatches(_cpuRow, _cpuCount);
                    if (Total() == 0)
                        EndRound(true);
                    else
                        BeginPlayerTurn();
                }
                break;
            case Phase.RoundOver:
                _timer -= Dt;
                if (_timer <= 0 && _burns.Count == 0)
                {
                    if (_losses >= 3)
                        EndGame(false, $"You won {_wins} round{(_wins == 1 ? "" : "s")}.");
                    else
                        NewRound();
                }
                break;
        }
    }

    private void UpdateHover()
    {
        _hoverRow = -1;
        if (_phase != Phase.Player)
            return;
        if (!In.HasHover && !In.PointerDown && !In.PointerPressed)
            return;
        HitMatch(In.Pointer, out _hoverRow, out _hoverCount);
    }

    private bool HitMatch(Vector2 p, out int row, out int count)
    {
        row = -1;
        count = 0;
        for (int r = 0; r < _rows; r++)
        {
            float baseY = RowBase(r);
            if (p.Y < baseY - MH - 10 || p.Y > baseY + 8)
                continue;
            for (int s = 0; s < _heap[r]; s++)
                if (MathF.Abs(p.X - SlotX(r, s)) < Spacing / 2)
                {
                    row = r;
                    count = _heap[r] - s;
                    return true;
                }
        }
        return false;
    }

    private void PlayerInput()
    {
        // Keyboard.
        if (In.UpPressed || In.DownPressed)
        {
            int d = In.UpPressed ? -1 : 1;
            int r = _selRow;
            for (int i = 0; i < _rows; i++)
            {
                r = (r + d + _rows) % _rows;
                if (_heap[r] > 0)
                    break;
            }
            _selRow = r;
            _selCount = Math.Clamp(_selCount, 1, _heap[r]);
            Sound.Play(Sfx.Tick, 0.2f, 0.5f);
        }
        if (In.LeftPressed && _selCount < _heap[_selRow])
        {
            _selCount++;
            Sound.Play(Sfx.Tick, 0.4f, 0.5f);
        }
        if (In.RightPressed && _selCount > 1)
        {
            _selCount--;
            Sound.Play(Sfx.Tick, 0.0f, 0.5f);
        }

        // Pointer.
        bool take = false;
        if (In.PointerPressed && HitMatch(In.Pointer, out int pr, out int pc))
        {
            if (pr == _selRow && pc == _selCount && !IsTouch)
                take = true; // a second click on the same choice takes it
            _selRow = pr;
            _selCount = pc;
            Sound.Play(Sfx.Select, 0.2f, 0.5f);
        }

        if (_heap[_selRow] == 0)
        {
            _selRow = FirstRow();
            _selCount = 1;
        }
        _selCount = Math.Clamp(_selCount, 1, Math.Max(1, _heap[_selRow]));

        string label = "TAKE " + _selCount;
        if (Ui.Button(TakeButton, label, color: new Color(170, 70, 20)))
            take = true;
        if (In.FirePressed || In.EnterPressed)
            take = true;

        if (take && _heap[_selRow] > 0)
        {
            TakeMatches(_selRow, _selCount);
            if (Total() == 0)
                EndRound(false);
            else
            {
                _phase = Phase.CpuThink;
                _timer = Rand(0.9f, 1.5f);
                Say("COMPUTER THINKING...");
            }
        }
    }

    private void TakeMatches(int row, int count)
    {
        count = Math.Min(count, _heap[row]);
        for (int i = 0; i < count; i++)
        {
            int slot = _heap[row] - 1 - i;
            _burns.Add(new Burn { Row = row, Slot = slot, T = -i * 0.08f });
        }
        _heap[row] -= count;
        Sound.Play(Sfx.Fuse, Rand(-0.2f, 0.2f), 0.7f);
        Sound.Play(Sfx.Whoosh, 0.3f, 0.4f);
    }

    private void UpdateBurns()
    {
        for (int i = _burns.Count - 1; i >= 0; i--)
        {
            var b = _burns[i];
            float before = b.T;
            b.T += Dt;
            if (before < 0 && b.T >= 0)
                Sound.Play(Sfx.Crack, Rand(0.2f, 0.6f), 0.25f);
            float x = SlotX(b.Row, b.Slot), baseY = RowBase(b.Row);
            float burn = MathF2.Clamp(b.T / 1.0f, 0, 1);
            float fy = baseY - MH + burn * MH * 0.8f;
            if (b.T >= 0 && b.T < 1.1f && Tick % 2 == 0)
            {
                Fx.Spark(x + Rand(-1.5f, 1.5f), fy - 2, Rand(-8, 8), Rand(-60, -30), Chance(0.5f) ? Pal.Orange : Pal.Yellow, Rand(0.2f, 0.45f), Rand(1.5f, 2.5f));
                if (Chance(0.4f))
                    Fx.Spark(x, fy - 8, Rand(-6, 6), Rand(-30, -15), new Color(120, 114, 108) * 0.45f, Rand(0.6f, 1.1f), Rand(1.5f, 3f), -5, false);
            }
            if (b.T >= BurnTime)
            {
                Fx.Burst(x, baseY - MH / 2, new Color(90, 80, 70), 6, 30, 0.6f, 2, -10, false);
                _burns.RemoveAt(i);
            }
            else
                _burns[i] = b;
        }
    }

    // ------------------------------------------------------------------ the computer

    /// <summary>Misère Nim: is the side to move lost against perfect play?</summary>
    private static bool LosingForMover(int[] h, int rows)
    {
        bool big = false;
        int x = 0, ones = 0, total = 0;
        for (int r = 0; r < rows; r++)
        {
            big |= h[r] > 1;
            x ^= h[r];
            total += h[r];
            if (h[r] == 1)
                ones++;
        }
        if (total == 0)
            return false; // the previous player took the last match and lost
        return big ? x == 0 : ones % 2 == 1;
    }

    private (int Row, int Count) BestMove(bool perfect)
    {
        _moves.Clear();
        var good = new List<(int, int)>();
        for (int r = 0; r < _rows; r++)
            for (int k = 1; k <= _heap[r]; k++)
            {
                _heap[r] -= k;
                bool total0 = Total() == 0;
                if (LosingForMover(_heap, _rows))
                    good.Add((r, k));
                if (!total0)
                    _moves.Add((r, k));
                _heap[r] += k;
            }
        if (perfect && good.Count > 0)
            return good[Rng.Next(good.Count)];
        if (_moves.Count > 0)
            return _moves[Rng.Next(_moves.Count)];
        // Forced to take the last match.
        for (int r = 0; r < _rows; r++)
            if (_heap[r] > 0)
                return (r, 1);
        return (0, 0);
    }

    private void ChooseCpuMove()
    {
        float mistake = _round switch { 1 => 0.5f, 2 => 0.35f, 3 => 0.22f, 4 => 0.12f, 5 => 0.05f, _ => 0f };
        // Mistakes are more likely early in a round, when the position is harder to read.
        if (Total() <= 4)
            mistake *= 0.5f;
        (_cpuRow, _cpuCount) = BestMove(!Chance(mistake));
        Say($"COMPUTER TAKES {_cpuCount}");
    }

    private void EndRound(bool playerWon)
    {
        _phase = Phase.RoundOver;
        _timer = 2.6f;
        _playerWonRound = playerWon;
        if (playerWon)
        {
            _wins++;
            int pts = 100 * _round;
            AddScore(pts, MatX + MatW / 2, 180, Pal.Yellow);
            Say("YOU WIN THE ROUND!");
            Sound.Play(Sfx.Bonus);
            Fx.Burst(MatX + MatW / 2, 180, Pal.Gold, 40, 160, 1f, 3);
        }
        else
        {
            _losses++;
            Lives = 3 - _losses;
            Say("YOU TOOK THE LAST MATCH!");
            Sound.Play(Sfx.Lose);
            Fx.Shake(3, 0.3f);
        }
    }

    // ------------------------------------------------------------------ drawing

    public override void Draw(Gfx g)
    {
        DrawTable(g, Screen.Bounds, Time);

        // Felt mat.
        var mat = new RectF(MatX, MatY, MatW, MatH);
        g.RoundRect(mat.Offset(3, 4), 14, Color.Black * 0.45f);
        g.RoundRect(mat, 14, new Color(18, 70, 46));
        g.RoundRect(mat.Inflate(-6, -6), 10, new Color(24, 92, 58));
        g.Glow(MatX + MatW / 2, MatY + 40, 260, new Color(255, 210, 140), 0.12f);
        Backdrops.Vignette(g, mat, 0.25f);

        bool playerTurn = _phase == Phase.Player;
        for (int r = 0; r < _rows; r++)
        {
            float baseY = RowBase(r);
            bool rowSel = playerTurn && r == _selRow;
            // Row label and groove.
            g.RoundRect(MatX + 14, baseY - MH / 2 - 12, 24, 24, 6, rowSel ? Pal.Orange * 0.9f : Color.Black * 0.3f);
            g.Text((r + 1).ToString(), MatX + 26, baseY - MH / 2 - 6, 1.5f, rowSel ? Pal.White : Pal.LightGrey, Align.Center);
            g.Rect(MatX + 48, baseY + 3, MatW - 64, 2, Color.Black * 0.18f);
            g.Text(_heap[r].ToString(), MatX + MatW - 18, baseY - MH / 2 - 6, 1.5f, _heap[r] == 0 ? Pal.Grey : Pal.Gold, Align.Right);

            for (int s = 0; s < _heap[r]; s++)
            {
                bool sel = false, hover = false;
                if (playerTurn)
                {
                    sel = r == _selRow && s >= _heap[r] - _selCount;
                    hover = r == _hoverRow && s >= _heap[r] - _hoverCount;
                }
                else if (_phase == Phase.CpuShow)
                    sel = r == _cpuRow && s >= _heap[r] - _cpuCount;
                float lift = sel ? 6 + MathF.Sin(Time * 6 + s) * 1.2f : hover ? 3 : 0;
                Color? glow = sel ? (_phase == Phase.CpuShow ? Pal.Cyan : Pal.Orange) : hover ? Pal.Yellow : null;
                DrawMatch(g, SlotX(r, s), baseY - lift, MK, 0, glow, s * 7 + r * 3);
            }
        }
        foreach (var b in _burns)
            DrawMatch(g, SlotX(b.Row, b.Slot), RowBase(b.Row) - 6 * MathF.Max(0, 1 - b.T * 2), MK, MathF.Max(0, b.T), null, b.Slot * 7 + b.Row * 3);

        DrawPanel(g);
    }

    private void DrawPanel(Gfx g)
    {
        var p = new RectF(468, 32, 162, 318);
        g.Panel(p, new Color(36, 22, 14) * 0.92f, new Color(150, 90, 40), 10);
        g.TextShadow("ROUND " + _round, p.CenterX, p.Y + 12, 2f, Pal.Gold, Align.Center);

        // Whose turn.
        string who = _phase switch
        {
            Phase.Player => "YOUR TURN",
            Phase.CpuThink or Phase.CpuShow => "COMPUTER",
            _ => _playerWonRound ? "YOU WIN!" : "YOU LOSE",
        };
        var wc = _phase == Phase.Player ? Pal.Orange : _phase == Phase.RoundOver ? (_playerWonRound ? Pal.Lime : Pal.Red) : Pal.Cyan;
        float pulse = 0.7f + 0.3f * MathF2.Pulse(Time, 1.2f);
        g.Glow(p.CenterX, p.Y + 52, 60, wc, 0.2f * pulse);
        g.Text(who, p.CenterX, p.Y + 46, 1.5f, wc, Align.Center);
        if (_phase == Phase.CpuThink)
            for (int i = 0; i < 3; i++)
                g.Circle(p.CenterX - 10 + i * 10, p.Y + 68, 2.2f, Pal.Cyan * (0.3f + 0.7f * MathF2.Pulse(Time + i * 0.2f, 0.9f)));

        g.TextWrapped(_message, p.X + 10, p.Y + 84, p.W - 20, 1f, Pal.White * MathF.Min(1, _messageT * 4), 1.4f, Align.Center);

        // Rule reminder with a little burning match.
        g.Rect(p.X + 12, p.Y + 126, p.W - 24, 1, Pal.Orange * 0.4f);
        g.Text("LAST MATCH", p.CenterX, p.Y + 138, 1.5f, Pal.LightGrey, Align.Center);
        g.Text("LOSES", p.CenterX, p.Y + 154, 1.5f, Pal.Red, Align.Center);
        g.Rect(p.X + 12, p.Y + 174, p.W - 24, 1, Pal.Orange * 0.4f);

        // Record.
        g.Text("WON", p.X + 20, p.Y + 186, 1.5f, Pal.LightGrey);
        g.Text(_wins.ToString(), p.Right - 20, p.Y + 186, 1.5f, Pal.Lime, Align.Right);
        g.Text("LOST", p.X + 20, p.Y + 206, 1.5f, Pal.LightGrey);
        for (int i = 0; i < 3; i++)
        {
            float x = p.Right - 22 - i * 14;
            g.Circle(x, p.Y + 212, 5, i < _losses ? Pal.Red : Color.Black * 0.4f);
            if (i < _losses)
                g.Glow(x, p.Y + 212, 10, Pal.Red, 0.4f);
        }
        if (_phase == Phase.Player)
            g.Text("PICK, THEN TAKE", p.CenterX, p.Y + 246, 1f, Pal.Grey, Align.Center);
    }

    private static void DrawTable(Gfx g, RectF r, float time)
    {
        g.GradientV(r.X, r.Y, r.W, r.H, new Color(70, 40, 22), new Color(40, 22, 12));
        for (int i = 0; i < 26; i++)
        {
            float y = r.Y + i * r.H / 26 + MathF.Sin(i * 1.7f) * 3;
            g.Rect(r.X, y, r.W, 1.2f, new Color(30, 16, 8) * 0.45f);
            g.Rect(r.X + (i * 53 % (int)MathF.Max(1, r.W)), y + 4, r.W * 0.3f, 0.8f, new Color(110, 70, 40) * 0.35f);
        }
        g.Glow(r.CenterX - r.W * 0.12f, r.Y, r.W * 0.6f, new Color(255, 190, 110), 0.18f + 0.02f * MathF.Sin(time * 3));
    }

    /// <summary>A match standing upright. <paramref name="burn"/> runs from 0 (unlit) through the burning to 1.5 (gone).</summary>
    private static void DrawMatch(Gfx g, float x, float baseY, float k, float burn, Color? glow, int seed)
    {
        float h = MatchH * k, w = 5.4f * k;
        float fade = burn > 1.1f ? MathF.Max(0, 1 - (burn - 1.1f) / 0.4f) : 1;
        float top = baseY - h;
        float lean = (seed % 5 - 2) * 0.3f * k;
        if (glow is Color gc)
            g.Glow(x, top + h * 0.4f, 26 * k, gc, 0.35f);
        // Shadow on the felt.
        g.Line(x + 2 * k, baseY, x + 9 * k, baseY - h * 0.15f, 3 * k, Color.Black * (0.25f * fade));
        // Stick, shaded as a cylinder; charred from the top as it burns.
        float charred = burn <= 0 ? 0 : MathF.Min(1, burn / 1.0f) * 0.8f;
        float cy = top + 5 * k + (h - 5 * k) * charred;
        g.GradientH(x - w / 2 + lean, cy, w / 2, baseY - cy, new Color(250, 222, 166) * fade, new Color(226, 190, 128) * fade);
        g.GradientH(x + lean, cy, w / 2, baseY - cy, new Color(226, 190, 128) * fade, new Color(176, 136, 84) * fade);
        if (charred > 0)
        {
            g.Rect(x - w / 2 + lean, top + 5 * k, w * 0.85f, cy - top - 5 * k, new Color(46, 34, 28) * fade);
            if (burn < 1.1f)
            {
                g.Rect(x - w / 2 + lean, cy - 1.5f * k, w, 2 * k, Pal.Orange);
                g.Glow(x + lean, cy, 9 * k, Pal.Orange, 0.8f);
            }
        }
        // Head.
        Color head = burn > 0.12f ? new Color(40, 30, 28) : new Color(200, 34, 30);
        g.Ellipse(x + lean, top + 4 * k, 3.8f * k, 5.4f * k, head * fade);
        if (burn <= 0.12f)
            g.Ellipse(x - 1.2f * k + lean, top + 2.4f * k, 1.2f * k, 2f * k, new Color(255, 140, 120));
        // Flame.
        if (burn > 0 && burn < 1.15f)
        {
            float fy = burn < 0.15f ? top + 2 * k : cy - 2 * k;
            float size = (burn < 0.15f ? 0.6f + burn * 4 : 1.2f - (burn - 0.15f) * 0.5f) * k;
            float flick = MathF.Sin(burn * 40 + seed) * 1.2f * k;
            g.Glow(x, fy - 6 * size, 30 * size, Pal.Orange, 0.6f);
            g.Ellipse(x + flick * 0.3f, fy - 5 * size, 4.2f * size, 9 * size, Pal.Orange);
            g.Ellipse(x + flick * 0.2f, fy - 3.5f * size, 2.6f * size, 6 * size, Pal.Yellow);
            g.Ellipse(x, fy - 1.5f * size, 1.4f * size, 3 * size, Pal.White);
        }
    }

    // ------------------------------------------------------------------ icon

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        float s = r.H / 70f;
        DrawTable(g, r, time);
        g.RoundRect(r.X + 6 * s, r.Y + 6 * s, r.W - 12 * s, r.H - 12 * s, 8 * s, new Color(24, 92, 58));
        g.Glow(r.CenterX, r.Y + 10 * s, r.W * 0.6f, new Color(255, 210, 140), 0.12f);
        int[] rows = [3, 5, 7];
        float cycle = time % 3.2f;
        for (int row = 0; row < 3; row++)
        {
            float baseY = r.Y + (27 + row * 19) * s;
            for (int i = 0; i < rows[row]; i++)
            {
                float x = r.CenterX + (i - (rows[row] - 1) / 2f) * 13 * s;
                float burn = 0;
                if (row == 2 && i >= 5)
                    burn = MathF.Max(0, cycle - 0.3f - (i - 5) * 0.25f);
                else if (row == 0 && i == 2)
                    burn = 0.35f + 0.25f * MathF2.Pulse(time, 3);
                if (burn < 1.5f)
                    DrawMatch(g, x, baseY, 0.36f * s, burn, null, i + row * 3);
            }
        }
    }

    // ------------------------------------------------------------------ autoplay

    public override void AutoPlay(Controls c)
    {
        if (_phase != Phase.Player)
        {
            _apStage = 0;
            _apWait = 0.7f;
            return;
        }
        if (_apWait > 0)
        {
            _apWait -= Dt;
            return;
        }
        switch (_apStage)
        {
            case 0:
                (_apRow, _apCount) = BestMove(!Chance(0.12f));
                c.Pointer = new Vector2(SlotX(_apRow, _heap[_apRow] - _apCount), RowBase(_apRow) - MH / 2);
                c.PointerPressed = c.PointerDown = true;
                _apStage = 1;
                break;
            case 1:
                c.Pointer = new Vector2(SlotX(_apRow, Math.Max(0, _heap[_apRow] - _apCount)), RowBase(_apRow) - MH / 2);
                c.PointerReleased = true;
                _apStage = 2;
                _apWait = 0.6f;
                break;
            case 2:
                c.Pointer = TakeButton.Center;
                c.PointerPressed = c.PointerDown = true;
                _apStage = 3;
                break;
            default:
                c.Pointer = TakeButton.Center;
                c.PointerReleased = true;
                _apStage = 0;
                _apWait = 0.5f;
                break;
        }
    }
}
