using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 04 Boggles: goggle-eyed Boggles pop out of nine holes. Bop them before they duck back down,
/// but leave the friendly one in the top hat and the ones carrying bombs alone.
/// </summary>
public sealed class Boggles : MiniGame, Capture.ICaptureHints
{
    public override int Number => 4;
    public override string Title => "Boggles";
    public override Category Category => Category.Arcade;
    public override string Tagline => "Bop the goggle-eyed Boggles as they pop up. Mind the bombs!";
    public override Color Accent => Pal.Lime;

    public override string[] HowToPlay =>
    [
        "Bop each Boggle before it hides: 10 points, more as your combo builds. Golden Boggles score 50.",
        "Don't bop a bomb carrier (loses 5 seconds) or the friend in the hat (loses 50).",
        "Reach the target in 60 seconds to start a faster round.",
    ];

    public override string[] DesktopControls => ["Click, or 1-9 as on a numpad.", "ARROWS + SPACE also work."];
    public override string[] TouchControls => ["Tap the Boggles."];
    public override Pad Pad => Pad.None;
    public int CaptureTicks => 900;

    private enum Kind
    {
        Normal,
        Gold,
        Bomb,
        Hat,
    }

    private enum State
    {
        Empty,
        Rising,
        Up,
        Sinking,
        Bopped,
    }

    private struct HoleData
    {
        public State State;
        public Kind Kind;
        public float T;      // time in the current state
        public float Rise;   // 0 hidden .. 1 fully up
        public float Stay;
        public float Cooldown;
        public int Colour;
        public bool Seen;    // the autoplayer has noticed it
    }

    private const float RoundTime = 60;
    private static readonly float[] HoleX = [200, 320, 440];
    private static readonly float[] HoleY = [142, 224, 306];
    private const float RiseHeight = 50;

    private static readonly Color[] BodyColours = [new(150, 80, 220), new(60, 190, 110), new(240, 120, 60), new(70, 150, 240)];

    private readonly HoleData[] _holes = new HoleData[9];
    private int _round;
    private float _timeLeft, _spawnTimer, _roundBanner, _roundEnd;
    private int _roundScore, _target, _combo, _bestCombo;
    private int _cursor = 4;
    private bool _showCursor;
    private float _malletT;
    private Vector2 _malletPos;
    private int _lastTickSecond;
    private int _autoTarget = -1;
    private float _autoDelay;

    protected override void Start()
    {
        _round = 0;
        NextRound();
    }

    private void NextRound()
    {
        _round++;
        Level = _round;
        _timeLeft = RoundTime;
        _roundScore = 0;
        _target = 400 + (_round - 1) * 350 + (_round - 1) * (_round - 1) * 120;
        _spawnTimer = 1.2f;
        _roundBanner = 2.2f;
        _roundEnd = 0;
        _combo = 0;
        for (int i = 0; i < 9; i++)
            _holes[i] = new HoleData { Cooldown = Rand(0, 1) };
        Status = "ROUND " + _round;
    }

    private static Vector2 HolePos(int i) => new(HoleX[i % 3], HoleY[i / 3]);

    private float Progress => 1 - _timeLeft / RoundTime;

    protected override void Update()
    {
        if (_malletT > 0)
            _malletT -= Dt;

        if (_roundEnd > 0)
        {
            _roundEnd -= Dt;
            if (_roundEnd <= 0)
            {
                if (_roundScore >= _target)
                    NextRound();
                else
                    EndGame(false, $"Missed the target by {_target - _roundScore}");
            }
            return;
        }

        if (_roundBanner > 0)
        {
            _roundBanner -= Dt;
            return;
        }

        _timeLeft -= Dt;
        int sec = (int)MathF.Ceiling(_timeLeft);
        if (sec != _lastTickSecond)
        {
            _lastTickSecond = sec;
            if (sec <= 5 && sec > 0)
                Sound.Play(Sfx.Tick, 0.5f, 0.8f);
        }
        if (_timeLeft <= 0)
        {
            _timeLeft = 0;
            _roundEnd = 2.2f;
            for (int i = 0; i < 9; i++)
                if (_holes[i].State is State.Rising or State.Up)
                    SetState(i, State.Sinking);
            Sound.Play(_roundScore >= _target ? Sfx.LevelUp : Sfx.Lose);
            if (_roundScore >= _target)
                AddScore(100 * _round, 320, 150, Pal.Cyan);
            return;
        }

        UpdateHoles();
        Spawn();
        HandleInput();
    }

    private void SetState(int i, State s)
    {
        _holes[i].State = s;
        _holes[i].T = 0;
    }

    private void UpdateHoles()
    {
        for (int i = 0; i < 9; i++)
        {
            ref var h = ref _holes[i];
            h.T += Dt;
            switch (h.State)
            {
                case State.Empty:
                    h.Cooldown -= Dt;
                    break;
                case State.Rising:
                    h.Rise = MathF.Min(1, h.Rise + Dt * 7);
                    if (h.Rise >= 1)
                        SetState(i, State.Up);
                    break;
                case State.Up:
                    if (h.Kind == Kind.Bomb && Tick % 20 == 0)
                    {
                        var p = HolePos(i);
                        Fx.Spark(p.X + 26, p.Y - 50, Rand(-30, 30), Rand(-60, -20), Pal.Yellow, 0.3f, 1.5f);
                    }
                    if (h.T > h.Stay)
                    {
                        SetState(i, State.Sinking);
                        if (h.Kind is Kind.Normal or Kind.Gold)
                        {
                            if (_combo >= 3)
                                Fx.Float("COMBO LOST", HolePos(i).X, HolePos(i).Y - 60, Pal.Grey, 1f);
                            _combo = 0;
                        }
                    }
                    break;
                case State.Sinking:
                    h.Rise = MathF.Max(0, h.Rise - Dt * 5);
                    if (h.Rise <= 0)
                    {
                        SetState(i, State.Empty);
                        h.Cooldown = Rand(0.3f, 0.9f);
                    }
                    break;
                case State.Bopped:
                    if (h.T > 0.45f)
                        h.Rise = MathF.Max(0, h.Rise - Dt * 6);
                    if (h.Rise <= 0)
                    {
                        SetState(i, State.Empty);
                        h.Cooldown = Rand(0.3f, 0.8f);
                    }
                    break;
            }
        }
    }

    private void Spawn()
    {
        _spawnTimer -= Dt;
        if (_spawnTimer > 0)
            return;
        float speed = 1 + (_round - 1) * 0.18f + Progress * 0.5f;
        _spawnTimer = MathF.Max(0.22f, 0.95f / speed) * Rand(0.6f, 1.2f);

        int active = 0;
        for (int i = 0; i < 9; i++)
            if (_holes[i].State != State.Empty)
                active++;
        if (active >= 3 + _round / 2)
            return;

        int start = RandInt(0, 9);
        for (int k = 0; k < 9; k++)
        {
            int i = (start + k) % 9;
            ref var h = ref _holes[i];
            if (h.State != State.Empty || h.Cooldown > 0)
                continue;
            float r = Rand(0, 1);
            float bomb = MathF.Min(0.25f, 0.1f + _round * 0.025f), hat = 0.1f, gold = 0.06f;
            h.Kind = r < bomb ? Kind.Bomb : r < bomb + hat ? Kind.Hat : r < bomb + hat + gold ? Kind.Gold : Kind.Normal;
            h.Stay = MathF.Max(0.5f, 1.35f / speed) * Rand(0.8f, 1.2f);
            if (h.Kind == Kind.Gold)
                h.Stay *= 0.7f;
            h.Colour = RandInt(0, BodyColours.Length);
            h.Rise = 0;
            h.Seen = false;
            SetState(i, State.Rising);
            Sound.Play(h.Kind == Kind.Bomb ? Sfx.Fuse : Sfx.Pop, Rand(-0.2f, 0.4f), 0.5f);
            return;
        }
    }

    private void HandleInput()
    {
        int whack = -1;

        if (In.PointerPressed)
        {
            for (int i = 0; i < 9; i++)
            {
                var p = HolePos(i);
                if (MathF.Abs(In.Pointer.X - p.X) < 55 && In.Pointer.Y > p.Y - 78 && In.Pointer.Y < p.Y + 30)
                {
                    whack = i;
                    break;
                }
            }
            _showCursor = false;
            _malletPos = In.Pointer;
            _malletT = 0.25f;
            if (whack < 0)
                Sound.Play(Sfx.Whoosh, 0.3f, 0.3f);
        }

        // Number keys laid out like a numpad: 7 8 9 on the top row.
        foreach (char ch in In.Typed)
            if (ch >= '1' && ch <= '9')
            {
                int n = ch - '1';
                whack = (2 - n / 3) * 3 + n % 3;
                _showCursor = false;
            }

        // Cursor keys + fire.
        if (In.LeftPressed || In.RightPressed || In.UpPressed || In.DownPressed)
        {
            if (_showCursor)
            {
                int cx = _cursor % 3, cy = _cursor / 3;
                if (In.LeftPressed) cx = Math.Max(0, cx - 1);
                if (In.RightPressed) cx = Math.Min(2, cx + 1);
                if (In.UpPressed) cy = Math.Max(0, cy - 1);
                if (In.DownPressed) cy = Math.Min(2, cy + 1);
                _cursor = cy * 3 + cx;
                Sound.Play(Sfx.Select, 0.3f, 0.3f);
            }
            _showCursor = true;
        }
        if (In.FirePressed && !In.PointerPressed)
        {
            whack = _cursor;
            _showCursor = true;
        }

        if (whack >= 0)
        {
            var p = HolePos(whack);
            if (!In.PointerPressed)
            {
                _malletPos = new Vector2(p.X + 10, p.Y - 40);
                _malletT = 0.25f;
            }
            Whack(whack);
        }
    }

    private void Whack(int i)
    {
        ref var h = ref _holes[i];
        var p = HolePos(i);
        bool hittable = (h.State is State.Rising or State.Up or State.Sinking) && h.Rise > 0.3f;
        if (!hittable)
        {
            Sound.Play(Sfx.Thud, -0.4f, 0.5f);
            Fx.Burst(p.X, p.Y, Pal.Brown, 8, 50, 0.3f, 2f, 120, false);
            if (_combo >= 3)
                Fx.Float("COMBO LOST", p.X, p.Y - 50, Pal.Grey, 1f);
            _combo = 0;
            return;
        }
        float top = p.Y - h.Rise * RiseHeight - 10;
        SetState(i, State.Bopped);
        switch (h.Kind)
        {
            case Kind.Normal:
            case Kind.Gold:
            {
                _combo++;
                _bestCombo = Math.Max(_bestCombo, _combo);
                int mult = Math.Min(5, 1 + _combo / 5);
                int pts = (h.Kind == Kind.Gold ? 50 : 10) * mult;
                _roundScore += pts;
                AddScore(pts, p.X, top - 20, h.Kind == Kind.Gold ? Pal.Gold : Pal.Yellow);
                var col = h.Kind == Kind.Gold ? Pal.Gold : BodyColours[h.Colour];
                Fx.Burst(p.X, top, col, 16, 120, 0.5f, 2.5f, 200);
                Fx.Burst(p.X, top, Pal.White, 6, 80, 0.3f, 1.8f);
                Sound.Play(h.Kind == Kind.Gold ? Sfx.Coin : Sfx.Hit, MathF.Min(0.8f, _combo * 0.04f), 0.9f);
                if (_combo > 0 && _combo % 5 == 0 && mult > 1)
                {
                    Fx.Float("COMBO x" + mult, p.X, top - 40, Pal.Cyan, 2f);
                    Sound.Play(Sfx.Bonus, 0.2f, 0.7f);
                }
                Fx.Shake(1.5f, 0.1f);
                break;
            }
            case Kind.Bomb:
                Fx.Explode(p.X, top, 1.3f);
                Sound.Play(Sfx.Explode);
                _timeLeft = MathF.Max(0.5f, _timeLeft - 5);
                Fx.Float("-5 SECONDS", p.X, top - 30, Pal.Red, 1.5f);
                _combo = 0;
                break;
            case Kind.Hat:
            {
                int loss = Math.Min(Score, 50);
                Score -= loss;
                _roundScore = Math.Max(0, _roundScore - 50);
                Fx.Float("OUCH! -50", p.X, top - 30, Pal.Pink, 1.5f);
                Fx.Burst(p.X, top, Pal.Sky, 12, 80, 0.4f, 2f);
                Sound.Play(Sfx.Wrong);
                _combo = 0;
                break;
            }
        }
    }

    // ------------------------------------------------------------------ drawing

    public override void Draw(Gfx g)
    {
        DrawMeadow(g, g.Visible, Time, 1f);

        for (int i = 0; i < 9; i++)
        {
            var p = HolePos(i);
            ref var h = ref _holes[i];
            DrawHoleBack(g, p, 1f);
            if (h.State != State.Empty && h.Rise > 0)
            {
                g.SetClip(new RectF(p.X - 60, Screen.HudHeight, 120, p.Y + 5 - Screen.HudHeight));
                float bob = h.State == State.Bopped ? 0 : MathF.Sin(Time * 9 + i) * 1.2f;
                var look = (In?.Pointer ?? new Vector2(320, 200)) - new Vector2(p.X, p.Y - 40);
                DrawBoggle(g, p.X, p.Y + 30 - h.Rise * RiseHeight + bob, 1f, h.Kind, h.Colour, h.State == State.Bopped, look, Time + i);
                g.SetClip(Screen.Bounds);
            }
            DrawHoleFront(g, p, 1f);
            if (!IsTouch)
            {
                int key = (2 - i / 3) * 3 + i % 3 + 1;
                g.Text(key.ToString(), p.X + 48, p.Y + 10, 1f, Pal.White * 0.45f);
            }
            if (_showCursor && i == _cursor && !IsTouch)
            {
                float pulse = 0.6f + 0.4f * MathF.Sin(Time * 8);
                g.Ring(p.X, p.Y - 20, 52, 2, Pal.Yellow * pulse);
            }
        }

        DrawHud(g);

        // Mallet.
        bool hover = In != null && In.HasHover && !IsTouch && !_showCursor;
        if (_malletT > 0 || hover)
        {
            var mp = _malletT > 0 ? _malletPos : In.Pointer;
            float swing = _malletT > 0 ? MathF.Max(0, (_malletT - 0.15f) / 0.1f) : 1;
            DrawMallet(g, mp, -0.2f - swing * 0.8f, 1f);
        }

        if (_roundBanner > 0)
        {
            float a = MathF.Min(1, _roundBanner * 1.5f);
            g.Panel(RectF.Centered(320, 196, 300, 70), Pal.Panel * (0.9f * a), Pal.Lime * a, 10);
            g.TextShadow("ROUND " + _round, 320, 170, 3f, Pal.Yellow * a, Align.Center);
            g.TextShadow("TARGET " + _target, 320, 206, 1.5f, Pal.White * a, Align.Center);
        }
        if (_roundEnd > 0)
        {
            bool ok = _roundScore >= _target;
            g.Panel(RectF.Centered(320, 196, 320, 70), Pal.Panel * 0.9f, ok ? Pal.Lime : Pal.Red, 10);
            g.TextShadow(ok ? "TARGET HIT!" : "TIME UP!", 320, 172, 3f, ok ? Pal.Lime : Pal.Red, Align.Center);
            g.TextShadow("BEST COMBO " + _bestCombo, 320, 206, 1.5f, Pal.White, Align.Center);
        }
    }

    private void DrawHud(Gfx g)
    {
        // Timer bar.
        var bar = new RectF(150, 30, 340, 12);
        g.RoundRect(bar.Inflate(2, 2), 6, Color.Black * 0.5f);
        float k = _timeLeft / RoundTime;
        var tc = k < 0.15f ? ((Tick / 8) % 2 == 0 ? Pal.Red : Pal.Orange) : Pal.Lerp(Pal.Orange, Pal.Lime, k * 1.5f);
        g.RoundRect(new RectF(bar.X, bar.Y, MathF.Max(6, bar.W * k), bar.H), 5, tc);
        g.Rect(bar.X + 3, bar.Y + 2, MathF.Max(0, bar.W * k - 6), 2, Pal.White * 0.35f);
        g.TextShadow(((int)MathF.Ceiling(_timeLeft)).ToString(), bar.Right + 10, bar.Y, 1.5f, Pal.White);

        // Target progress.
        g.TextShadow("TARGET", 16, 30, 1f, Pal.LightGrey);
        g.TextShadow($"{Math.Min(_roundScore, 99999)}/{_target}", 16, 40, 1.5f, _roundScore >= _target ? Pal.Lime : Pal.White);

        if (_combo >= 2)
        {
            int mult = Math.Min(5, 1 + _combo / 5);
            g.TextShadow("COMBO " + _combo, 624, 30, 1.5f, Pal.Cyan, Align.Right);
            g.TextShadow("x" + mult, 624, 44, 1.5f, Pal.Yellow, Align.Right);
        }
    }

    private static void DrawMeadow(Gfx g, RectF r, float time, float s)
    {
        g.GradientV(r.X, r.Y, r.W, r.H * 0.45f, new Color(90, 170, 255), new Color(190, 230, 255));
        g.Glow(r.X + r.W * 0.86f, r.Y + r.H * 0.25f, 60 * s, Pal.Yellow, 0.5f);
        g.Circle(r.X + r.W * 0.86f, r.Y + r.H * 0.25f, 14 * s, new Color(255, 245, 180));
        // Clouds.
        for (int i = 0; i < 3; i++)
        {
            float cx = r.X + Backdrops.Mod(i * 230 * s + time * 8 * s, r.W + 120 * s) - 60 * s;
            float cy = r.Y + r.H * 0.12f + i * 13 * s;
            g.Ellipse(cx, cy, 26 * s, 8 * s, Color.White * 0.85f);
            g.Ellipse(cx - 12 * s, cy - 4 * s, 13 * s, 9 * s, Color.White * 0.85f);
            g.Ellipse(cx + 9 * s, cy - 5 * s, 15 * s, 10 * s, Color.White * 0.85f);
        }
        Backdrops.Hills(g, r.Y + r.H * 0.36f, 14 * s, 0, new Color(70, 160, 80), 4, r);
        g.GradientV(r.X, r.Y + r.H * 0.38f, r.W, r.H * 0.62f, new Color(70, 170, 70), new Color(30, 110, 45));
        // Grass tufts.
        var rng = new Random(11);
        int n = (int)(70 * r.W / 640);
        for (int i = 0; i < n; i++)
        {
            float x = r.X + (float)rng.NextDouble() * r.W;
            float y = r.Y + r.H * (0.42f + 0.58f * (float)rng.NextDouble());
            float sway = MathF.Sin(time * 2 + i) * 1.2f * s;
            var c = new Color(110, 200, 90);
            g.Line(x, y, x - 2 * s + sway, y - 5 * s, 1.2f * s, c);
            g.Line(x, y, x + sway, y - 7 * s, 1.2f * s, c);
            g.Line(x, y, x + 2 * s + sway, y - 5 * s, 1.2f * s, c);
            if (i % 9 == 0)
            {
                g.Circle(x + sway, y - 7 * s, 1.8f * s, i % 2 == 0 ? Pal.Yellow : Pal.Pink);
            }
        }
    }

    private static void DrawHoleBack(Gfx g, Vector2 p, float s)
    {
        g.Ellipse(p.X, p.Y + 4 * s, 52 * s, 20 * s, new Color(110, 70, 35));
        g.Ellipse(p.X, p.Y + 2 * s, 48 * s, 17 * s, new Color(150, 100, 50));
        g.Ellipse(p.X, p.Y, 40 * s, 12 * s, new Color(30, 18, 10));
        g.Ellipse(p.X, p.Y + 2 * s, 34 * s, 8 * s, new Color(12, 6, 4));
    }

    private static void DrawHoleFront(Gfx g, Vector2 p, float s)
    {
        // The front lip of the hole, over the bottom of the Boggle.
        const int seg = 14;
        var prev = Vector2.Zero;
        for (int i = 0; i <= seg; i++)
        {
            float a = MathF.PI * i / seg;
            var q = new Vector2(p.X + MathF.Cos(a) * 41 * s, p.Y + 1 * s + MathF.Sin(a) * 12 * s);
            if (i > 0)
                g.Line(prev, q, 5 * s, new Color(165, 112, 58));
            prev = q;
        }
        g.Circle(p.X - 30 * s, p.Y + 12 * s, 2.5f * s, new Color(120, 80, 40));
        g.Circle(p.X + 22 * s, p.Y + 14 * s, 2f * s, new Color(120, 80, 40));
    }

    private static void DrawBoggle(Gfx g, float x, float y, float s, Kind kind, int colour, bool bopped, Vector2 look, float t)
    {
        var body = kind switch
        {
            Kind.Gold => Pal.Gold,
            Kind.Hat => new Color(90, 200, 230),
            Kind.Bomb => new Color(200, 60, 90),
            _ => BodyColours[colour],
        };
        var dark = Pal.Darken(body, 0.35f);
        float sy = bopped ? 0.82f : 1f;
        if (kind == Kind.Gold)
            g.Glow(x, y, 46 * s, Pal.Gold, 0.5f + 0.2f * MathF.Sin(t * 8));

        // Furry body.
        for (int i = 0; i < 9; i++)
        {
            float a = MathF.PI + i * MathF.PI / 8;
            g.Circle(x + MathF.Cos(a) * 20 * s, y + MathF.Sin(a) * 20 * s * sy, 7 * s, dark);
        }
        g.Ellipse(x, y + 8 * s, 25 * s, 30 * s * sy, dark);
        g.Ellipse(x, y + 6 * s, 23 * s, 28 * s * sy, body);
        g.Ellipse(x, y + 18 * s, 13 * s, 14 * s * sy, Pal.Lighten(body, 0.35f));
        g.Ellipse(x - 7 * s, y - 10 * s, 8 * s, 5 * s, Pal.Lighten(body, 0.25f));

        // Goggles.
        float ey = y - 4 * s * sy;
        g.Rect(x - 23 * s, ey - 2 * s, 46 * s, 4 * s, new Color(60, 50, 40));
        for (int side = -1; side <= 1; side += 2)
        {
            float ex = x + side * 10 * s;
            g.Circle(ex, ey, 10 * s, new Color(170, 140, 70));
            g.Circle(ex, ey, 8 * s, Pal.White);
            if (bopped)
            {
                g.Line(ex - 4 * s, ey - 4 * s, ex + 4 * s, ey + 4 * s, 2 * s, Pal.Black);
                g.Line(ex - 4 * s, ey + 4 * s, ex + 4 * s, ey - 4 * s, 2 * s, Pal.Black);
            }
            else
            {
                var d = look.LengthSquared() > 1 ? Vector2.Normalize(look) * 3.5f * s : Vector2.Zero;
                g.Circle(ex + d.X, ey + d.Y, 3.8f * s, Pal.Black);
                g.Circle(ex + d.X - 1.2f * s, ey + d.Y - 1.2f * s, 1.2f * s, Pal.White);
            }
            g.Circle(ex - 3 * s, ey - 4 * s, 1.6f * s, Pal.White * 0.8f);
        }

        // Mouth.
        float my = y + 10 * s * sy;
        if (bopped)
            g.Ellipse(x, my, 5 * s, 4 * s, new Color(60, 10, 20));
        else if (kind == Kind.Hat)
        {
            g.Arc(x, my - 4 * s, 7 * s, 2 * s, 0.3f, MathF.PI - 0.3f, new Color(40, 20, 30));
        }
        else
        {
            g.Ellipse(x, my, 8 * s, 4 * s, new Color(60, 10, 20));
            g.Rect(x - 5 * s, my - 4 * s, 3 * s, 3 * s, Pal.White);
            g.Rect(x + 2 * s, my - 4 * s, 3 * s, 3 * s, Pal.White);
        }

        if (kind == Kind.Hat)
        {
            float hy = y - 22 * s * sy;
            g.Ellipse(x, hy, 20 * s, 4 * s, new Color(30, 30, 40));
            g.Rect(x - 12 * s, hy - 20 * s, 24 * s, 20 * s, new Color(30, 30, 40));
            g.Rect(x - 12 * s, hy - 6 * s, 24 * s, 4 * s, Pal.Red);
            g.Circle(x + 8 * s, hy - 4 * s, 3 * s, Pal.Yellow);
            g.Circle(x - 22 * s, y + 10 * s, 2.5f * s, Pal.Pink * 0.8f);
            g.Circle(x + 22 * s, y + 10 * s, 2.5f * s, Pal.Pink * 0.8f);
        }
        if (kind == Kind.Bomb && !bopped)
        {
            float bx = x + 22 * s, by = y + 12 * s;
            g.Circle(bx, by, 10 * s, new Color(30, 30, 40));
            g.Circle(bx - 3 * s, by - 3 * s, 3 * s, new Color(90, 90, 110));
            g.Rect(bx - 3 * s, by - 13 * s, 6 * s, 4 * s, new Color(90, 90, 110));
            g.Line(bx, by - 13 * s, bx + 5 * s, by - 19 * s, 1.5f * s, Pal.Sand);
            float f = 0.7f + 0.3f * MathF.Sin(t * 30);
            g.Glow(bx + 5 * s, by - 19 * s, 9 * s * f, Pal.Orange, 0.9f);
            g.Circle(bx + 5 * s, by - 19 * s, 2 * s, Pal.Yellow);
            g.Ellipse(x + 14 * s, y + 14 * s, 5 * s, 4 * s, body);
        }
        if (kind == Kind.Gold)
            for (int i = 0; i < 3; i++)
            {
                float a = t * 2 + i * 2.1f;
                float sx = x + MathF.Cos(a) * 30 * s, sy2 = y + MathF.Sin(a) * 22 * s;
                g.Glow(sx, sy2, 6 * s, Pal.White, 0.8f);
            }
        if (bopped)
            for (int i = 0; i < 4; i++)
            {
                float a = t * 6 + i * MathF.PI / 2;
                float sx = x + MathF.Cos(a) * 20 * s, sy2 = y - 30 * s + MathF.Sin(a) * 5 * s;
                g.Glow(sx, sy2, 6 * s, Pal.Yellow, 0.8f);
                g.Circle(sx, sy2, 2 * s, Pal.Yellow);
            }
    }

    private static void DrawMallet(Gfx g, Vector2 p, float angle, float s)
    {
        // p is the striking face; the handle points down-right.
        var dir = MathF2.FromAngle(angle + MathF.PI / 2);
        var handleEnd = p + dir * 34 * s;
        var headC = p + MathF2.FromAngle(angle, 0) * 0;
        g.Line(p + new Vector2(2, 3) * s, handleEnd + new Vector2(2, 3) * s, 4 * s, Color.Black * 0.3f);
        g.Line(p, handleEnd, 4 * s, new Color(150, 100, 50));
        g.RotatedRect(headC, 28 * s, 15 * s, angle, new Color(170, 40, 40));
        g.RotatedRect(headC, 24 * s, 11 * s, angle, Pal.Red);
        g.RotatedRect(headC - dir * 3 * s, 20 * s, 3 * s, angle, Pal.Lighten(Pal.Red, 0.4f));
    }

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        float s = r.H / 70f;
        DrawMeadow(g, r, time, s * 0.6f);
        float hs = s * 0.55f;
        for (int i = 0; i < 3; i++)
        {
            var p = new Vector2(r.CenterX + (i - 1) * 48 * s, r.Y + r.H * 0.86f);
            DrawHoleBack(g, p, hs);
            float phase = (time * 0.8f + i * 0.37f) % 1;
            float rise = MathF.Min(1, MathF.Sin(phase * MathF.PI) * 1.6f);
            if (rise > 0.45f)
            {
                var kind = i == 1 ? Kind.Gold : i == 2 ? Kind.Bomb : Kind.Normal;
                DrawBoggle(g, p.X, p.Y + 4 * hs - rise * 30 * hs, hs, kind, 0, false, new Vector2(MathF.Sin(time * 2), 0.3f), time + i);
            }
            DrawHoleFront(g, p, hs);
        }
        float sw = (time * 1.3f) % 1;
        DrawMallet(g, new Vector2(r.CenterX + 10 * s, r.Y + r.H * 0.34f), -0.2f - MathF.Max(0, 1 - sw * 4) * 0.9f, s * 0.6f);
    }

    // ------------------------------------------------------------------ autoplay

    public override void AutoPlay(Controls c)
    {
        if (_autoTarget == -2)
        {
            c.PointerReleased = true;
            _autoTarget = -1;
            return;
        }
        // Notice new Boggles and react after a short, human delay.
        int best = -1;
        for (int i = 0; i < 9; i++)
        {
            var h = _holes[i];
            if ((h.State is State.Rising or State.Up) && (h.Kind is Kind.Normal or Kind.Gold) && h.Rise > 0.6f)
            {
                if (!_holes[i].Seen)
                {
                    _holes[i].Seen = true;
                    _autoDelay = MathF.Max(_autoDelay, Rand(0.3f, 0.5f));
                }
                if (best < 0 || h.Kind == Kind.Gold)
                    best = i;
            }
        }
        if (_autoDelay > 0)
        {
            _autoDelay -= Dt;
            return;
        }
        if (best >= 0)
        {
            var p = HolePos(best);
            c.Pointer = new Vector2(p.X + Rand(-10, 10), p.Y - 30);
            c.PointerPressed = true;
            c.PointerDown = true;
            _autoTarget = -2;
            _autoDelay = Rand(0.2f, 0.35f);
        }
        else
        {
            c.Pointer = new Vector2(320, 200);
        }
    }
}
