using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 07 Do Your Sums: mental arithmetic against the clock on a classroom blackboard. Sums get harder:
/// adding, taking away, times tables, dividing, then two-step sums.
/// </summary>
public sealed class DoYourSums : MiniGame
{
    public override int Number => 7;
    public override string Title => "Do Your Sums";
    public override Category Category => Category.Brain;
    public override string Tagline => "Quick-fire mental arithmetic against the clock. No calculators!";
    public override Color Accent => Pal.Lime;

    public override string[] HowToPlay =>
    [
        "Answer each sum before the chalk line runs out. A wrong answer or running out of time costs a life.",
        "Answer quickly for a time bonus; a streak multiplies your points. Sums get harder every 5 right.",
        "Survive all 40 to top the class.",
    ];

    public override string[] DesktopControls => ["Type the answer, ENTER to check.", "BACKSPACE deletes. Or click the pad."];
    public override string[] TouchControls => ["Tap the number pad, then OK."];
    public override Pad Pad => Pad.None;
    public override bool TextEntry => true;

    private const int TotalQuestions = 40;

    private static readonly string[] LevelNames =
    [
        "ADDING UP", "TAKE AWAY", "TIMES TABLES", "SHARING OUT", "MIXED BAG", "TWO STEPS", "BIG NUMBERS", "HEAD OF THE CLASS",
    ];

    private static readonly Dictionary<char, Color> Chalk = new() { ['X'] = Color.White };
    private static readonly PixelArt TimesGlyph = new(["......", "X...X.", ".X.X..", "..X...", ".X.X..", "X...X.", "......"], Chalk);
    private static readonly PixelArt DivideGlyph = new(["......", "..X...", "......", "XXXXX.", "......", "..X...", "......"], Chalk);

    private static readonly string[] CharStrings = BuildCharStrings();

    private static string[] BuildCharStrings()
    {
        var a = new string[96];
        for (int i = 0; i < 96; i++)
            a[i] = ((char)(32 + i)).ToString();
        return a;
    }

    private string _question = "";
    private int _answer;
    private string _typed = "";
    private int _asked, _correct, _streak;
    private float _timeLimit, _timeLeft;
    private float _feedback; // >0 while showing the result of the last answer
    private bool _lastRight;
    private int _lastAnswer;
    private float _banner;
    private int _lastTickSec;
    private bool _over;

    // Autoplay.
    private float _autoThink;
    private string _autoPlan;
    private int _autoClick;
    private Vector2 _autoPos;

    protected override void Start()
    {
        Lives = 3;
        Level = 1;
        _banner = 2f;
        NewQuestion();
    }

    private int Mult => Math.Min(5, 1 + _streak / 3);

    private void NewQuestion()
    {
        _asked++;
        Status = $"SUM {_asked} OF {TotalQuestions}";
        _typed = "";
        int a, b, c;
        int kind = Level switch
        {
            1 => 0,
            2 => RandInt(0, 2),
            3 => Chance(0.6f) ? 2 : RandInt(0, 2),
            4 => Chance(0.6f) ? 3 : 2,
            5 => RandInt(0, 4),
            _ => Chance(0.6f) ? 4 : RandInt(0, 4),
        };
        int big = Level >= 7 ? 3 : 1;
        switch (kind)
        {
            case 0:
                a = RandInt(2, Level == 1 ? 11 : 30 * big);
                b = RandInt(2, Level == 1 ? 11 : 30 * big);
                _question = $"{a} + {b}";
                _answer = a + b;
                break;
            case 1:
                a = RandInt(5, 40 * big);
                b = RandInt(1, a);
                _question = $"{a} - {b}";
                _answer = a - b;
                break;
            case 2:
                a = RandInt(2, Level >= 5 ? 13 : 11);
                b = RandInt(2, 11 + (Level >= 7 ? 10 : 0));
                _question = $"{a} * {b}";
                _answer = a * b;
                break;
            case 3:
                b = RandInt(2, 11);
                _answer = RandInt(2, Level >= 6 ? 13 : 11);
                _question = $"{b * _answer} / {b}";
                break;
            default:
            {
                int form = RandInt(0, 3);
                a = RandInt(2, 10);
                b = RandInt(2, 10);
                c = RandInt(2, 20);
                if (form == 0)
                {
                    _question = $"{a} * {b} + {c}";
                    _answer = a * b + c;
                }
                else if (form == 1)
                {
                    _question = $"({a} + {b}) * {Math.Min(c, 9)}";
                    _answer = (a + b) * Math.Min(c, 9);
                }
                else
                {
                    int p = a * b;
                    c = RandInt(1, p);
                    _question = $"{a} * {b} - {c}";
                    _answer = p - c;
                }
                break;
            }
        }
        _timeLimit = MathF.Max(4f, 11f - (Level - 1) * 0.6f) + (kind == 4 ? 4 : 0);
        _timeLeft = _timeLimit;
        _lastTickSec = 99;
        _autoPlan = null;
    }

    protected override void Update()
    {
        if (_banner > 0)
            _banner -= Dt;

        // Number pad.
        for (int i = 0; i < 12; i++)
        {
            var r = PadRect(i);
            string label = PadLabel(i);
            var col = label == "OK" ? Pal.Forest : label == "DEL" ? new Color(110, 40, 40) : new Color(70, 50, 35);
            if (Ui.Button(r, label, Keys.None, _feedback <= 0 && !IsOver, color: col, textScale: label.Length > 1 ? 1.5f : 2.5f, id: "pad" + i))
                Press(label);
        }

        if (_feedback > 0)
        {
            _feedback -= Dt;
            if (_feedback <= 0)
            {
                if (_over)
                {
                    if (Lives <= 0)
                        EndGame(false, $"{_correct} right out of {_asked}");
                    else
                        EndGame(true, "Top of the class!");
                    return;
                }
                NewQuestion();
            }
            return;
        }

        foreach (char ch in In.Typed)
            if (ch >= '0' && ch <= '9')
                Press(ch.ToString());
        if (In.BackspacePressed)
            Press("DEL");
        if (In.EnterPressed)
            Press("OK");

        _timeLeft -= Dt;
        int sec = (int)MathF.Ceiling(_timeLeft);
        if (sec != _lastTickSec && sec <= 3 && sec > 0)
            Sound.Play(Sfx.Tick, 0.4f, 0.7f);
        _lastTickSec = sec;
        if (_timeLeft <= 0)
        {
            _timeLeft = 0;
            Answer(false, true);
        }
    }

    private void Press(string key)
    {
        if (_feedback > 0)
            return;
        if (key == "DEL")
        {
            if (_typed.Length > 0)
            {
                _typed = _typed[..^1];
                Sound.Play(Sfx.Back, 0, 0.5f);
            }
        }
        else if (key == "OK")
        {
            if (_typed.Length > 0)
                Answer(int.Parse(_typed) == _answer, false);
        }
        else if (_typed.Length < 5 && !(_typed == "0"))
        {
            _typed += key;
            Sound.Play(Sfx.Tick, 0.2f + _typed.Length * 0.1f, 0.4f);
        }
    }

    private void Answer(bool right, bool timeout)
    {
        _lastRight = right;
        _lastAnswer = _answer;
        _feedback = right ? 0.7f : 1.4f;
        if (right)
        {
            _correct++;
            _streak++;
            int pts = (10 * Level + (int)(_timeLeft * 3)) * Mult;
            AddScore(pts, 340, 160, Pal.Yellow);
            Sound.Play(Sfx.Correct, MathF.Min(0.6f, _streak * 0.05f));
            Fx.Burst(216, 192, Pal.White, 18, 90, 0.6f, 2f, 60, false);
            Fx.Burst(216, 192, Pal.Lime, 10, 70, 0.5f, 2f);
            if (_streak % 3 == 0 && Mult > 1)
            {
                Fx.Float("STREAK x" + Mult, 216, 226, Pal.Cyan, 2f);
                Sound.Play(Sfx.Bonus, 0.2f, 0.6f);
            }
            if (_correct % 5 == 0)
            {
                Level++;
                _banner = 2f;
                Sound.Play(Sfx.LevelUp);
            }
        }
        else
        {
            _streak = 0;
            Sound.Play(Sfx.Wrong);
            Fx.Shake(4, 0.3f);
            if (timeout)
                Fx.Float("TOO SLOW!", 216, 110, Pal.Orange, 2f);
            Lives--;
            if (Lives <= 0)
            {
                Lives = 0;
                _over = true;
                _feedback = 1.8f;
            }
        }
        if (_asked >= TotalQuestions && Lives > 0)
        {
            _over = true;
            _feedback = 1.8f;
            AddScore(500, 216, 200, Pal.Gold);
        }
    }

    // ------------------------------------------------------------------ layout

    private static RectF PadRect(int i)
    {
        int col = i % 3, row = i / 3;
        return new RectF(436 + col * 66, 70 + row * 66, 60, 60);
    }

    private static string PadLabel(int i) => i switch
    {
        9 => "DEL",
        10 => "0",
        11 => "OK",
        _ => (i + 1).ToString(),
    };

    // ------------------------------------------------------------------ drawing

    public override void Draw(Gfx g)
    {
        var v = g.Visible;
        // Classroom wall.
        g.GradientV(v.X, v.Y, v.W, v.H, new Color(200, 170, 120), new Color(150, 115, 80));
        for (float x = v.X; x < v.Right; x += 24)
            g.Rect(x, v.Y, 2, v.H, new Color(0, 0, 0) * 0.04f);
        g.Rect(v.X, 340, v.W, v.Bottom - 340, new Color(110, 70, 40));
        g.Rect(v.X, 340, v.W, 3, new Color(150, 100, 60));
        g.Panel(new RectF(426, 30, 208, 306), new Color(60, 40, 28) * 0.9f, new Color(120, 80, 50), 10);
        g.Text("ANSWER PAD", 530, 46, 1f, Pal.Sand, Align.Center);

        DrawBoard(g, new RectF(10, 30, 412, 300), 1f, Time);

        // Header.
        string lvl = LevelNames[Math.Min(Level, LevelNames.Length) - 1];
        g.Text($"LEVEL {Level}: {lvl}", 30, 46, 1.5f, Pal.White * 0.7f);
        if (_streak >= 2)
            g.Text($"STREAK {_streak}  x{Mult}", 402, 46, 1.5f, Pal.Yellow * 0.9f, Align.Right);

        // The sum.
        float scale = Gfx.TextWidth(_question + " = ", 4) > 380 ? 3 : 4;
        float qy = 104;
        DrawExpr(g, _question + " =", 216, qy, scale, Pal.White);

        // Answer box.
        var box = new RectF(136, 166, 160, 52);
        bool showResult = _feedback > 0;
        var boxCol = showResult ? (_lastRight ? Pal.Lime : Pal.Red) : Pal.White * 0.8f;
        g.RectOutline(box, 2, boxCol);
        string shown = showResult && !_lastRight ? _lastAnswer.ToString() : _typed;
        if (shown.Length > 0)
            DrawExpr(g, shown, box.CenterX, box.Y + 10, 4, showResult ? boxCol : Pal.Yellow);
        else if ((Tick / 25) % 2 == 0 && !showResult)
            g.Rect(box.CenterX - 2, box.Y + 10, 4, 32, Pal.Yellow * 0.8f);
        if (showResult)
        {
            if (_lastRight)
            {
                g.Line(box.Right + 14, box.CenterY, box.Right + 24, box.CenterY + 12, 4, Pal.Lime);
                g.Line(box.Right + 24, box.CenterY + 12, box.Right + 44, box.CenterY - 16, 4, Pal.Lime);
            }
            else
            {
                g.Line(box.Right + 14, box.CenterY - 14, box.Right + 40, box.CenterY + 14, 4, Pal.Red);
                g.Line(box.Right + 14, box.CenterY + 14, box.Right + 40, box.CenterY - 14, 4, Pal.Red);
                g.Text("ANSWER", box.X - 12, box.CenterY - 4, 1f, Pal.Red, Align.Right);
            }
        }

        // Chalk timer line.
        float k = _timeLeft / _timeLimit;
        var tl = new RectF(40, 246, 352, 10);
        var tc = k < 0.3f ? Pal.Lerp(Pal.Red, Pal.Orange, k / 0.3f) : Pal.Lerp(Pal.Yellow, Pal.Lime, (k - 0.3f) / 0.7f);
        g.RoundRect(tl, 5, Color.Black * 0.25f);
        if (k > 0)
        {
            g.RoundRect(new RectF(tl.X, tl.Y, MathF.Max(10, tl.W * k), tl.H), 5, tc);
            g.Glow(tl.X + tl.W * k, tl.CenterY, 14, tc, 0.6f);
        }
        g.Text("TIME", tl.X, tl.Bottom + 6, 1f, Pal.White * 0.6f);
        g.Text($"{_correct} RIGHT", tl.Right, tl.Bottom + 6, 1f, Pal.White * 0.6f, Align.Right);

        if (_banner > 0)
        {
            float a = MathF.Min(1, _banner * 1.5f);
            g.Panel(RectF.Centered(216, 180, 330, 70), new Color(20, 50, 35) * (0.95f * a), Pal.White * a, 8);
            g.TextShadow("LEVEL " + Level, 216, 156, 3f, Pal.Yellow * a, Align.Center);
            g.TextShadow(lvl, 216, 188, 1.5f, Pal.White * a, Align.Center);
        }
    }

    private static void DrawBoard(Gfx g, RectF r, float s, float time)
    {
        g.RoundRect(r.Offset(0, 3 * s), 6 * s, Color.Black * 0.3f);
        g.RoundRect(r, 6 * s, new Color(120, 75, 40));
        g.Rect(r.X + 3 * s, r.Y + 3 * s, r.W - 6 * s, 1.5f * s, new Color(170, 115, 65));
        var inner = r.Inflate(-9 * s, -9 * s);
        g.GradientV(inner.X, inner.Y, inner.W, inner.H, new Color(36, 78, 56), new Color(24, 56, 40));
        // Old chalk smudges.
        g.Ellipse(inner.X + inner.W * 0.25f, inner.Y + inner.H * 0.7f, 60 * s, 14 * s, Color.White * 0.03f);
        g.Ellipse(inner.X + inner.W * 0.7f, inner.Y + inner.H * 0.3f, 70 * s, 18 * s, Color.White * 0.03f);
        g.Ellipse(inner.X + inner.W * 0.5f, inner.Y + inner.H * 0.55f, 90 * s, 10 * s, Color.White * 0.025f);
        // Chalk tray.
        g.Rect(r.X + 20 * s, r.Bottom - 7 * s, r.W - 40 * s, 5 * s, new Color(150, 95, 50));
        g.RoundRect(r.X + 40 * s, r.Bottom - 10 * s, 18 * s, 4 * s, 2 * s, Pal.White);
        g.RoundRect(r.X + 66 * s, r.Bottom - 10 * s, 12 * s, 4 * s, 2 * s, Pal.Yellow);
        g.RoundRect(r.Right - 80 * s, r.Bottom - 13 * s, 28 * s, 8 * s, 2 * s, new Color(90, 70, 120));
        g.Rect(r.Right - 80 * s, r.Bottom - 8 * s, 28 * s, 3 * s, new Color(200, 200, 200));
    }

    /// <summary>Chalky text where '*' is a times sign and '/' a divide sign.</summary>
    private static void DrawExpr(Gfx g, string text, float cx, float y, float scale, Color c)
    {
        float w = Gfx.TextWidth(text, scale);
        float x = cx - w / 2;
        float gw = Gfx.GlyphW * scale;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            float gx = x + i * gw;
            if (ch == '*' || ch == '/')
            {
                var art = ch == '*' ? TimesGlyph : DivideGlyph;
                g.Pixels(art, gx + scale * 0.6f, y + scale * 0.6f, scale, false, c * 0.35f);
                g.Pixels(art, gx, y, scale, false, c);
            }
            else if (ch != ' ')
            {
                string one = CharStrings[Math.Clamp(ch - 32, 0, 95)];
                g.Text(one, gx + scale * 0.6f, y + scale * 0.6f, scale, c * 0.35f);
                g.Text(one, gx, y, scale, c);
            }
        }
    }

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        float s = r.H / 70f;
        g.Rect(r, new Color(190, 160, 115));
        DrawBoard(g, r.Inflate(-4 * s, -3 * s), s, time);
        int q = (int)(time / 2.2f) % 3;
        string[] qs = ["7 * 8 =", "56 / 7 =", "9 + 6 ="];
        string[] ans = ["56", "8", "15"];
        DrawExpr(g, qs[q], r.CenterX, r.Y + 14 * s, 1.7f * s, Pal.White);
        float t = time % 2.2f;
        if (t > 0.8f)
        {
            string a = ans[q][..Math.Min(ans[q].Length, 1 + (int)((t - 0.8f) * 4))];
            DrawExpr(g, a, r.CenterX, r.Y + 31 * s, 1.9f * s, Pal.Yellow);
        }
        if (t > 1.5f)
        {
            float x = r.CenterX + 18 * s, y = r.Y + 36 * s;
            g.Line(x, y, x + 5 * s, y + 6 * s, 2.2f * s, Pal.Lime);
            g.Line(x + 5 * s, y + 6 * s, x + 15 * s, y - 7 * s, 2.2f * s, Pal.Lime);
        }
        float k = 1 - t / 2.2f;
        g.RoundRect(r.X + 18 * s, r.Bottom - 20 * s, (r.W - 36 * s) * k, 3 * s, 1.5f * s, Pal.Lerp(Pal.Red, Pal.Lime, k));
    }

    // ------------------------------------------------------------------ autoplay

    public override void AutoPlay(Controls c)
    {
        if (_autoClick == 1)
        {
            c.Pointer = _autoPos;
            c.PointerReleased = true;
            _autoClick = 0;
            return;
        }
        if (_feedback > 0 || _banner > 1.2f)
            return;
        if (_autoPlan == null)
        {
            _autoThink = Rand(0.6f, 1.4f) + Level * 0.2f;
            int ans = Chance(0.1f) ? _answer + Pick(-1, 1, 10, -10) : _answer;
            _autoPlan = Math.Max(0, ans).ToString();
        }
        _autoThink -= Dt;
        if (_autoThink > 0 || Tick % 14 != 0)
            return;
        string key = _typed.Length < _autoPlan.Length ? _autoPlan[_typed.Length].ToString() : "OK";
        int idx = key == "OK" ? 11 : key == "0" ? 10 : key[0] - '1';
        if (Chance(0.5f) || key == "OK")
        {
            _autoPos = PadRect(idx).Center;
            c.Pointer = _autoPos;
            c.PointerPressed = true;
            c.PointerDown = true;
            _autoClick = 1;
        }
        else
        {
            c.Typed.Add(key[0]);
        }
    }
}
