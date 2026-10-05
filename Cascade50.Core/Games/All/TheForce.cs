using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 47 The Force: a psychic memory test. Six singing crystals flash a sequence that grows by one
/// note every round and plays faster; repeat it. Every few rounds a Zener card flickers in the
/// void: pick the symbol you glimpsed to win a bonus and restore a life.
/// </summary>
public sealed class TheForce : MiniGame
{
    public override int Number => 47;
    public override string Title => "The Force";
    public override Category Category => Category.Brain;
    public override string Tagline => "Six singing crystals. One growing sequence. How strong is your mind?";
    public override Color Accent => Pal.Purple;
    public override Pad Pad => Pad.None;

    public override string[] HowToPlay =>
    [
        "The crystals sing a sequence. Repeat it note for note. Each round adds a note and plays faster.",
        "A wrong crystal costs one of 3 lives and the round replays. Every 4th round a Zener card flickers: pick it for a bonus and a life back.",
        "Notes score 10 x round. Reach round 25 to ascend.",
    ];

    public override string[] DesktopControls =>
    [
        "Click a crystal, or press 1-6.",
        "Or LEFT / RIGHT round the ring and SPACE.",
        "Zener cards: click, 1-5 or arrows + SPACE.",
    ];

    public override string[] TouchControls => ["Tap the crystals in order.", "Tap the Zener card you glimpsed."];

    private const int Crystals = 6, MaxRound = 25;
    private const float Cx = 320, Cy = 186, RingR = 108;

    private static readonly Color[] CrystalColours =
    [
        new(255, 70, 90), new(255, 170, 40), new(240, 240, 70), new(60, 240, 140), new(60, 170, 255), new(200, 90, 255),
    ];

    private static readonly float[] Pitches = [-0.7f, -0.45f, -0.2f, 0.05f, 0.3f, 0.55f];

    private static readonly Vector2[] Gem =
    [
        new(0, -26), new(13, -9), new(11, 14), new(0, 26), new(-11, 14), new(-13, -9),
    ];

    private enum Phase
    {
        Ready,
        Show,
        Input,
        Success,
        Fail,
        PsyFlash,
        PsyPick,
        PsyResult,
    }

    private readonly List<int> _seq = new();
    private readonly float[] _lit = new float[Crystals];
    private Phase _phase;
    private float _timer;
    private int _showIndex, _inputIndex, _cursor = -1, _round;
    private bool _noteOn;
    private int _zener, _zenerPick = -1, _zenerCursor = 2, _psyCount;
    private bool _psyRight;
    private string _banner;
    private float _bannerTime;

    // Autoplay memory.
    private int _autoWait;
    private bool _autoRelease;

    protected override void Start()
    {
        Lives = 3;
        _seq.Clear();
        _round = 0;
        NextRound();
    }

    private Vector2 CrystalPos(int i)
    {
        float a = -MathF.PI / 2 + i * MathF2.Tau / Crystals;
        return new Vector2(Cx + MathF.Cos(a) * RingR * 1.18f, Cy + MathF.Sin(a) * RingR);
    }

    private void NextRound()
    {
        _round++;
        Level = _round;
        Status = "ROUND " + _round;
        int add = _seq.Count == 0 ? 3 : 1;
        for (int i = 0; i < add; i++)
        {
            int n;
            do n = RandInt(0, Crystals);
            while (_seq.Count >= 2 && n == _seq[^1] && n == _seq[^2]);
            _seq.Add(n);
        }
        BeginShow(1.0f);
        Banner("ROUND " + _round);
    }

    private void BeginShow(float delay)
    {
        _phase = Phase.Ready;
        _timer = delay;
        _showIndex = 0;
        _noteOn = false;
    }

    private void Banner(string s)
    {
        _banner = s;
        _bannerTime = 1.4f;
    }

    private float NoteTime => MathF.Max(0.17f, 0.52f - _round * 0.018f);
    private float GapTime => MathF.Max(0.07f, 0.2f - _round * 0.006f);

    private void Sing(int i, float strength = 1f)
    {
        _lit[i] = strength;
        Sound.Play(Sfx.Bell, Pitches[i], 0.75f);
        var p = CrystalPos(i);
        Fx.Burst(p.X, p.Y, CrystalColours[i], 10, 70, 0.5f, 2f);
    }

    protected override void Update()
    {
        for (int i = 0; i < Crystals; i++)
            _lit[i] = MathF.Max(0, _lit[i] - Dt * 3.2f);
        if (_bannerTime > 0)
            _bannerTime -= Dt;
        Sound.Loop(LoopSfx.Hum, true, -0.7f + 0.05f * MathF.Sin(Time * 0.7f), 0.18f);

        switch (_phase)
        {
            case Phase.Ready:
                if ((_timer -= Dt) <= 0)
                {
                    _phase = Phase.Show;
                    _timer = 0;
                }
                break;
            case Phase.Show:
                UpdateShow();
                break;
            case Phase.Input:
                UpdateInput();
                break;
            case Phase.Success:
                if ((_timer -= Dt) <= 0)
                {
                    if (_round >= MaxRound)
                    {
                        AddScore(1000, Cx, Cy);
                        EndGame(true, "You have ascended!");
                        return;
                    }
                    if (_round % 4 == 0)
                        BeginPsychic();
                    else
                        NextRound();
                }
                break;
            case Phase.Fail:
                if ((_timer -= Dt) <= 0)
                {
                    BeginShow(0.5f);
                    Banner("AGAIN...");
                }
                break;
            case Phase.PsyFlash:
                if ((_timer -= Dt) <= 0)
                {
                    _phase = Phase.PsyPick;
                    _zenerPick = -1;
                    Sound.Play(Sfx.Whoosh, -0.3f, 0.6f);
                }
                break;
            case Phase.PsyPick:
                UpdatePsyPick();
                break;
            case Phase.PsyResult:
                if ((_timer -= Dt) <= 0)
                    NextRound();
                break;
        }
    }

    private void UpdateShow()
    {
        _timer -= Dt;
        if (_timer > 0)
            return;
        if (_noteOn)
        {
            _noteOn = false;
            _timer = GapTime;
            _showIndex++;
            if (_showIndex >= _seq.Count)
            {
                _phase = Phase.Input;
                _inputIndex = 0;
                Sound.Play(Sfx.Pop, 0.3f, 0.4f);
            }
            return;
        }
        Sing(_seq[_showIndex]);
        _noteOn = true;
        _timer = NoteTime;
    }

    private void UpdateInput()
    {
        int chosen = -1;
        foreach (char ch in In.Typed)
            if (ch >= '1' && ch <= '6')
                chosen = ch - '1';
        if (In.RightPressed || In.DownPressed)
        {
            _cursor = _cursor < 0 ? 0 : (_cursor + 1) % Crystals;
            Sound.Play(Sfx.Tick, 0.2f, 0.4f);
        }
        if (In.LeftPressed || In.UpPressed)
        {
            _cursor = _cursor < 0 ? 0 : (_cursor + Crystals - 1) % Crystals;
            Sound.Play(Sfx.Tick, 0.2f, 0.4f);
        }
        if (In.FirePressed && _cursor >= 0)
            chosen = _cursor;
        if (In.PointerPressed)
            for (int i = 0; i < Crystals; i++)
                if (Vector2.Distance(In.Pointer, CrystalPos(i)) < 36)
                    chosen = i;
        if (chosen < 0)
            return;

        if (chosen == _seq[_inputIndex])
        {
            Sing(chosen);
            var p = CrystalPos(chosen);
            AddScore(10 * _round, p.X, p.Y - 34, CrystalColours[chosen]);
            _inputIndex++;
            if (_inputIndex >= _seq.Count)
            {
                _phase = Phase.Success;
                _timer = 0.9f;
                AddScore(50 * _round, Cx, Cy + 50, Pal.Gold);
                Sound.Play(Sfx.Correct, 0, 0.8f);
                for (int i = 0; i < Crystals; i++)
                    _lit[i] = 0.7f;
                Fx.Burst(Cx, Cy, Pal.White, 40, 200, 0.9f, 2.5f);
            }
        }
        else
        {
            Sound.Play(Sfx.Wrong);
            var p = CrystalPos(chosen);
            Fx.Burst(p.X, p.Y, Pal.Red, 24, 140, 0.6f, 3f);
            int right = _seq[_inputIndex];
            _lit[right] = 1.2f;
            if (LoseLife())
                return;
            _phase = Phase.Fail;
            _timer = 1.3f;
        }
    }

    private void BeginPsychic()
    {
        _psyCount++;
        _zener = RandInt(0, 5);
        _phase = Phase.PsyFlash;
        _timer = MathF.Max(0.22f, 0.7f - _psyCount * 0.09f);
        Status = "PSYCHIC ROUND";
        Banner("SENSE THE CARD");
        Sound.Play(Sfx.Warp, 0.4f, 0.6f);
    }

    private RectF ZenerCard(int i) => RectF.Centered(Cx + (i - 2) * 92, Cy + 6, 70, 96);

    private void UpdatePsyPick()
    {
        int chosen = -1;
        foreach (char ch in In.Typed)
            if (ch >= '1' && ch <= '5')
                chosen = ch - '1';
        if (In.LeftPressed)
        {
            _zenerCursor = (_zenerCursor + 4) % 5;
            Sound.Play(Sfx.Tick, 0.2f, 0.4f);
        }
        if (In.RightPressed)
        {
            _zenerCursor = (_zenerCursor + 1) % 5;
            Sound.Play(Sfx.Tick, 0.2f, 0.4f);
        }
        if (In.FirePressed)
            chosen = _zenerCursor;
        if (In.PointerPressed)
            for (int i = 0; i < 5; i++)
                if (ZenerCard(i).Contains(In.Pointer))
                    chosen = i;
        if (chosen < 0)
            return;
        _zenerPick = chosen;
        _psyRight = chosen == _zener;
        _phase = Phase.PsyResult;
        _timer = 1.6f;
        var r = ZenerCard(chosen);
        if (_psyRight)
        {
            AddScore(100 * _round, r.CenterX, r.Y - 10, Pal.Gold);
            Sound.Play(Sfx.Bonus);
            Fx.Burst(r.CenterX, r.CenterY, Pal.Gold, 40, 180, 0.9f, 3f);
            if (Lives < 3)
            {
                Lives++;
                Fx.Float("+1 LIFE", r.CenterX, r.Bottom + 6, Pal.Pink);
            }
            Banner("TRUE SIGHT!");
        }
        else
        {
            Sound.Play(Sfx.Wrong, -0.3f);
            Fx.Burst(r.CenterX, r.CenterY, Pal.Grey, 16, 90, 0.5f, 2f);
            Banner("THE VISION FADES");
        }
    }

    // ------------------------------------------------------------------ drawing

    private static void Cosmos(Gfx g, RectF r, float t)
    {
        Backdrops.Space(g, t, 4, 47, r);
        float s = r.H / 360f;
        g.Glow(r.X + r.W * 0.22f, r.Y + r.H * 0.3f, 220 * s, new Color(120, 30, 160), 0.35f);
        g.Glow(r.X + r.W * 0.8f, r.Y + r.H * 0.75f, 240 * s, new Color(30, 60, 170), 0.35f);
        g.Glow(r.X + r.W * 0.6f, r.Y + r.H * 0.15f, 160 * s, new Color(160, 40, 90), 0.22f);
        g.Glow(r.X + r.W * 0.12f, r.Y + r.H * 0.85f, 150 * s, new Color(20, 120, 140), 0.2f);
    }

    private static void DrawGem(Gfx g, Vector2 p, float scale, Color c, float lit, float t)
    {
        float k = MathF2.Clamp(lit, 0, 1);
        g.Glow(p, (40 + 50 * k) * scale, c, 0.25f + 0.8f * k);
        // Shadowed base and bright facets.
        var body = Pal.Lerp(Pal.Darken(c, 0.55f), Pal.Lighten(c, 0.3f), k);
        g.Shape(Gem, p, 0, scale * 1.08f, Pal.Lighten(c, 0.2f + 0.6f * k));
        g.Shape(Gem, p, 0, scale, body);
        var left = Pal.Lerp(Pal.Darken(c, 0.35f), Pal.Lighten(c, 0.55f), k);
        g.Triangle(p + new Vector2(0, -26) * scale, p + new Vector2(-13, -9) * scale, p + new Vector2(0, 2) * scale, left);
        g.Triangle(p + new Vector2(-13, -9) * scale, p + new Vector2(-11, 14) * scale, p + new Vector2(0, 2) * scale, Pal.Lerp(left, body, 0.5f));
        g.Triangle(p + new Vector2(0, -26) * scale, p + new Vector2(13, -9) * scale, p + new Vector2(0, 2) * scale, Pal.Lerp(body, Color.White, 0.15f + 0.4f * k));
        g.Triangle(p + new Vector2(-11, 14) * scale, p + new Vector2(0, 26) * scale, p + new Vector2(0, 2) * scale, Pal.Darken(body, 0.25f));
        // Sparkle.
        float tw = 0.5f + 0.5f * MathF.Sin(t * 3 + p.X);
        g.Glow(p.X - 4 * scale, p.Y - 12 * scale, 6 * scale, Color.White, 0.4f * tw + k);
        g.Circle(p.X - 4 * scale, p.Y - 12 * scale, 1.4f * scale, Color.White * (0.5f + 0.5f * tw));
        if (k > 0.05f)
            g.Glow(p, 18 * scale, Color.White, 0.6f * k);
    }

    public static void DrawZener(Gfx g, int kind, float cx, float cy, float size, Color c, float width)
    {
        switch (kind)
        {
            case 0:
                g.Ring(cx, cy, size * 0.8f, width, c, 40);
                break;
            case 1:
                g.Line(cx - size, cy, cx + size, cy, width, c);
                g.Line(cx, cy - size, cx, cy + size, width, c);
                break;
            case 2:
                for (int w = -1; w <= 1; w++)
                {
                    float y = cy + w * size * 0.55f;
                    for (int i = 0; i < 12; i++)
                    {
                        float x0 = cx - size + i * size / 6, x1 = x0 + size / 6;
                        float y0 = y + MathF.Sin(i * MathF.PI / 3) * size * 0.18f;
                        float y1 = y + MathF.Sin((i + 1) * MathF.PI / 3) * size * 0.18f;
                        g.Line(x0, y0, x1, y1, width, c);
                    }
                }
                break;
            case 3:
                g.RectOutline(cx - size * 0.8f, cy - size * 0.8f, size * 1.6f, size * 1.6f, width, c);
                break;
            default:
                for (int i = 0; i < 5; i++)
                {
                    float a0 = -MathF.PI / 2 + i * MathF2.Tau / 5, a1 = a0 + MathF2.Tau * 2 / 5;
                    g.Line(cx + MathF.Cos(a0) * size, cy + MathF.Sin(a0) * size, cx + MathF.Cos(a1) * size, cy + MathF.Sin(a1) * size, width, c);
                }
                break;
        }
    }

    private static void DrawCard(Gfx g, RectF r, int kind, float glow, bool faceUp, float t)
    {
        g.Glow(r.CenterX, r.CenterY, r.W * 0.9f, Pal.Purple, 0.25f + glow * 0.5f);
        g.RoundRect(r.Offset(0, 3), 8, Color.Black * 0.5f);
        g.Panel(r, new Color(30, 18, 60), Pal.Lerp(new Color(150, 110, 230), Pal.Gold, glow), 8);
        g.RectOutline(r.Inflate(-5, -5), 1, new Color(110, 80, 190) * 0.7f);
        if (faceUp)
        {
            var c = Pal.Lerp(new Color(170, 210, 255), Pal.Gold, glow);
            g.Glow(r.CenterX, r.CenterY, r.W * 0.5f, c, 0.4f);
            DrawZener(g, kind, r.CenterX, r.CenterY, r.W * 0.28f, c, MathF.Max(2, r.W * 0.05f));
        }
        else
        {
            float s = r.W / 70f;
            for (int i = 0; i < 3; i++)
                g.Ring(r.CenterX, r.CenterY, (8 + i * 7) * s, 1, new Color(120, 90, 200) * (0.6f - i * 0.15f));
            g.Glow(r.CenterX, r.CenterY, 14 * s, Pal.Purple, 0.5f + 0.3f * MathF.Sin(t * 3));
        }
    }

    public override void Draw(Gfx g)
    {
        Cosmos(g, g.Visible, Time);

        // Slowly turning mandala rings behind the crystals.
        for (int i = 0; i < 3; i++)
        {
            float rr = 60 + i * 45;
            float spin = Time * (0.15f + i * 0.07f) * (i % 2 == 0 ? 1 : -1);
            for (int k = 0; k < 24; k++)
            {
                float a = spin + k * MathF2.Tau / 24;
                if (k % 2 == 0)
                    g.Arc(Cx, Cy, rr, 1.2f, a, a + 0.18f, new Color(130, 100, 220) * 0.35f, 4);
            }
        }

        // The hexagram linking the crystals.
        bool psy = _phase is Phase.PsyFlash or Phase.PsyPick or Phase.PsyResult;
        float lineA = psy ? 0.15f : 0.35f;
        for (int i = 0; i < Crystals; i++)
        {
            var a = CrystalPos(i);
            var b = CrystalPos((i + 2) % Crystals);
            g.Line(a, b, 1.2f, new Color(140, 110, 240) * lineA);
            var n = CrystalPos((i + 1) % Crystals);
            g.Line(a, n, 1f, new Color(90, 80, 200) * lineA);
        }

        // Beams from the core to singing crystals.
        for (int i = 0; i < Crystals; i++)
            if (_lit[i] > 0.02f)
            {
                var p = CrystalPos(i);
                float k = MathF.Min(1, _lit[i]);
                g.GlowLine(new Vector2(Cx, Cy), p, 2.5f * k, CrystalColours[i] * k);
            }

        // The core: an eye of light.
        float pulse = MathF2.Pulse(Time, 2.4f);
        var coreC = _phase == Phase.Input ? Pal.Sky : _phase == Phase.Fail ? Pal.Red : Pal.Purple;
        g.Glow(Cx, Cy, 70 + 10 * pulse, coreC, 0.45f);
        g.Circle(Cx, Cy, 20 + 2 * pulse, new Color(20, 10, 40));
        g.Ring(Cx, Cy, 20 + 2 * pulse, 2, Pal.Lighten(coreC, 0.4f));
        g.Glow(Cx, Cy, 16, Color.White, 0.5f + 0.3f * pulse);
        g.Circle(Cx, Cy, 5 + pulse * 2, Color.White);

        if (!psy)
        {
            for (int i = 0; i < Crystals; i++)
            {
                var p = CrystalPos(i);
                float bob = MathF.Sin(Time * 1.6f + i) * 2.5f;
                DrawGem(g, p + new Vector2(0, bob), 1f, CrystalColours[i], _lit[i], Time + i);
                if (_cursor == i && _phase == Phase.Input && !IsTouch)
                {
                    g.Ring(p.X, p.Y + bob, 36, 2, Pal.White * (0.6f + 0.4f * MathF2.Pulse(Time, 0.6f)), 40);
                }
                if (!IsTouch)
                    g.Text((i + 1).ToString(), p.X, p.Y + 29, 1f, Pal.LightGrey * 0.6f, Align.Center);
            }

            // Progress beads along the bottom: how far through the sequence you are.
            int n = _seq.Count;
            float spacing = MathF.Min(16, 560f / Math.Max(1, n));
            float x0 = Cx - (n - 1) * spacing / 2;
            for (int i = 0; i < n; i++)
            {
                bool done = _phase == Phase.Input ? i < _inputIndex : _phase == Phase.Show ? i < _showIndex : _phase == Phase.Success;
                var c = done ? CrystalColours[_seq[i]] : new Color(70, 60, 120);
                if (done)
                    g.Glow(x0 + i * spacing, 349, 8, c, 0.5f);
                g.Circle(x0 + i * spacing, 349, 3.5f, c);
            }

            string hint = _phase switch
            {
                Phase.Show or Phase.Ready => "WATCH",
                Phase.Input => "REPEAT",
                Phase.Success => "WELL DONE",
                Phase.Fail => "WRONG CRYSTAL",
                _ => "",
            };
            g.TextShadow(hint, 24, 36, 2f, _phase == Phase.Input ? Pal.Sky : _phase == Phase.Fail ? Pal.Red : Pal.LightGrey);
            g.Text((_phase == Phase.Input ? _inputIndex : _phase == Phase.Show ? _showIndex : n) + " / " + n, 616, 38, 1.5f, Pal.LightGrey * 0.8f, Align.Right);
        }
        else
        {
            if (_phase == Phase.PsyFlash)
            {
                var r = RectF.Centered(Cx, Cy + 6, 90, 124);
                float flick = 0.6f + 0.4f * MathF.Sin(Time * 60);
                DrawCard(g, r, _zener, flick, true, Time);
            }
            else
            {
                for (int i = 0; i < 5; i++)
                {
                    var r = ZenerCard(i);
                    bool sel = _phase == Phase.PsyPick && i == _zenerCursor && !IsTouch;
                    bool hov = _phase == Phase.PsyPick && In != null && In.HasHover && r.Contains(In.Pointer);
                    float lift = sel || hov ? -6 : 0;
                    float glow = 0;
                    if (_phase == Phase.PsyResult && i == _zener)
                        glow = 0.6f + 0.4f * MathF.Sin(Time * 10);
                    var rr = r.Offset(0, lift);
                    DrawCard(g, rr, i, glow, true, Time);
                    if (_phase == Phase.PsyResult && i == _zenerPick && !_psyRight)
                        g.RectOutline(rr.Inflate(3, 3), 2, Pal.Red);
                    if (sel)
                        g.RectOutline(rr.Inflate(4, 4), 2, Pal.White * 0.8f);
                    if (!IsTouch)
                        g.Text((i + 1).ToString(), rr.CenterX, rr.Bottom + 6, 1.5f, Pal.LightGrey * 0.7f, Align.Center);
                }
                if (_phase == Phase.PsyPick)
                    g.TextShadow("WHICH CARD DID YOU SEE?", Cx, 316, 2f, Pal.Ice, Align.Center);
            }
        }

        if (_bannerTime > 0 && _banner != null)
        {
            float a = MathF.Min(1, _bannerTime * 2);
            float y = psy ? 64 : Cy + 34;
            g.TextShadow(_banner, Cx, y, 2.5f, Pal.Gold * a, Align.Center, Color.Black * (0.8f * a));
        }
    }

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        Cosmos(g, r, time);
        float s = r.H / 162f;
        var c = r.Center;
        g.Glow(c, 60 * s, Pal.Purple, 0.5f);
        int lit = (int)(time * 2.5f) % Crystals;
        for (int i = 0; i < Crystals; i++)
        {
            float a = -MathF.PI / 2 + i * MathF2.Tau / Crystals + time * 0.2f;
            var p = c + new Vector2(MathF.Cos(a) * 92 * s * 1.25f, MathF.Sin(a) * 58 * s);
            var q = c + new Vector2(MathF.Cos(a + MathF2.Tau / 3) * 92 * s * 1.25f, MathF.Sin(a + MathF2.Tau / 3) * 58 * s);
            g.Line(p, q, 1.2f * s, new Color(150, 120, 240) * 0.4f);
            if (i == lit)
                g.GlowLine(c, p, 2.5f * s, CrystalColours[i]);
        }
        for (int i = 0; i < Crystals; i++)
        {
            float a = -MathF.PI / 2 + i * MathF2.Tau / Crystals + time * 0.2f;
            var p = c + new Vector2(MathF.Cos(a) * 92 * s * 1.25f, MathF.Sin(a) * 58 * s);
            float k = i == lit ? 1 - (time * 2.5f % 1) : 0;
            DrawGem(g, p, 0.9f * s, CrystalColours[i], k, time + i);
        }
        g.Glow(c, 26 * s, Color.White, 0.7f);
        g.Circle(c.X, c.Y, 8 * s, Color.White);
        DrawZener(g, (int)(time / 1.5f) % 5, c.X, c.Y, 8 * s, Pal.Purple, 2 * s);
    }

    public override void AutoPlay(Controls c)
    {
        if (_autoRelease)
        {
            c.PointerReleased = true;
            _autoRelease = false;
            return;
        }
        if (_phase == Phase.Input)
        {
            if (--_autoWait > 0)
                return;
            _autoWait = 14 + RandInt(0, 10);
            int target = _seq[_inputIndex];
            // A fallible mind: mistakes creep in on long sequences.
            if (Chance(0.0025f * _seq.Count))
                target = (target + 1) % Crystals;
            c.Pointer = CrystalPos(target);
            c.PointerPressed = true;
            c.PointerDown = true;
            _autoRelease = true;
        }
        else if (_phase == Phase.PsyPick)
        {
            if (--_autoWait > 0)
                return;
            _autoWait = 30;
            int target = Chance(0.6f) ? _zener : RandInt(0, 5);
            c.Pointer = ZenerCard(target).Center;
            c.PointerPressed = true;
            c.PointerDown = true;
            _autoRelease = true;
        }
        else
        {
            _autoWait = 30;
        }
    }
}
