using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 15 High Rise: a crane swings each new floor over the tower; drop it at the right moment. Any
/// overhang is sliced off, so the tower narrows unless your drops are perfect. Build into space.
/// </summary>
public sealed class HighRise : MiniGame, Capture.ICaptureHints
{
    public override int Number => 15;
    public override string Title => "High Rise";
    public override Category Category => Category.Skill;
    public override string Tagline => "Stack the floors as the crane swings. Build up into space.";
    public override Color Accent => Pal.Orange;
    public override Pad Pad => Pad.Fire;
    public override string FireLabel => "DROP";

    public override string[] HowToPlay =>
    [
        "Drop each floor from the swinging crane onto the tower. Any overhang is sliced off and falls away.",
        "A perfect drop keeps the full width; three in a row widen the tower. Miss it altogether and the game ends.",
    ];

    public override string[] DesktopControls => ["SPACE or click to drop."];
    public override string[] TouchControls => ["Tap DROP or the screen to drop."];

    public int CaptureTicks => 2700;

    private const float FloorH = 18;
    private const float StartW = 120;
    private const float TopScreenY = 236;
    private const float JibY = 42;
    private const float Gravity = 1300;

    private struct Floor
    {
        public float X, W;
        public int Seed;
        public bool Perfect;
    }

    private struct Debris
    {
        public Vector2 Pos, Vel;
        public float W, Angle, Spin;
        public int Seed;
    }

    private static readonly Vector2[] StarField = MakeStars();
    private static readonly Vector3[] Skyline = MakeSkyline();

    private static Vector2[] MakeStars()
    {
        var rng = new Random(5);
        var a = new Vector2[120];
        for (int i = 0; i < a.Length; i++)
            a[i] = new Vector2((float)rng.NextDouble() * 640, (float)rng.NextDouble() * 360);
        return a;
    }

    private static Vector3[] MakeSkyline()
    {
        var rng = new Random(9);
        var a = new Vector3[22];
        for (int i = 0; i < a.Length; i++)
            a[i] = new Vector3(i * 30 + (float)rng.NextDouble() * 10 - 10, 30 + (float)rng.NextDouble() * 80, 22 + (float)rng.NextDouble() * 14);
        return a;
    }

    private readonly List<Floor> _floors = new();
    private readonly List<Debris> _debris = new();
    private float _cam, _camTarget;
    private float _phase;
    private float _blockW;
    private bool _falling, _missed;
    private Vector2 _blockPos;
    private float _blockVy;
    private float _overTimer;
    private int _perfectRun;
    private float _flash;
    private string _milestone = "";
    private float _milestoneTimer;
    private float _apOffset;
    private float _wind;

    protected override void Start()
    {
        Level = 1;
        _floors.Clear();
        _debris.Clear();
        _floors.Add(new Floor { X = 320 - StartW / 2, W = StartW, Seed = 1 });
        _blockW = StartW;
        _phase = Rand(0, 6);
        _cam = _camTarget = 0;
        Status = "FLOOR 0";
        NewBlock();
    }

    private int Height => _floors.Count - 1;

    private float SwingSpeed => MathF.Min(4.4f, 1.8f + Height * 0.04f);

    private float TrolleyX => 320 + MathF.Sin(_phase) * 215 + MathF.Sin(_phase * 2.3f) * _wind;

    private float Sway => MathF.Cos(_phase) * 5;

    private float BlockX => BlockXAt(_phase);

    private float BlockXAt(float p) => 320 + MathF.Sin(p) * 215 + MathF.Sin(p * 2.3f) * _wind + MathF.Cos(p) * 5 - _blockW / 2;

    /// <summary>Screen y of a world height (world y grows upwards from the ground).</summary>
    private float ScreenY(float worldY) => 340 - (worldY - _cam);

    private float TopWorldY => _floors.Count * FloorH;

    private float HangY => JibY + 64;

    private void NewBlock()
    {
        _falling = false;
        _apOffset = Rand(-6, 6);
        if (Chance(0.55f)) _apOffset = 0;
        _wind = Height > 30 ? MathF.Min(30, (Height - 30) * 1.2f) : 0;
    }

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        if (_flash > 0) _flash -= Dt * 2;
        if (_milestoneTimer > 0) _milestoneTimer -= Dt;
        _cam = MathF2.Lerp(_cam, _camTarget, 4 * Dt);

        if (_missed)
        {
            _blockVy += Gravity * Dt;
            _blockPos.Y += _blockVy * Dt;
            UpdateDebris();
            _overTimer -= Dt;
            if (_overTimer <= 0)
                EndGame(false, $"Tower complete at {Height} floors.");
            return;
        }

        if (!_falling)
        {
            _phase += SwingSpeed * Dt;
            if (In.FirePressed || In.PointerPressed)
            {
                _falling = true;
                _blockPos = new Vector2(BlockX, HangY);
                _blockVy = 0;
                Sound.Play(Sfx.Whoosh, 0.2f, 0.5f);
            }
        }
        else
        {
            _blockVy += Gravity * Dt;
            _blockPos.Y += _blockVy * Dt;
            float topY = ScreenY(TopWorldY);
            if (_blockPos.Y + FloorH >= topY)
                Land(topY);
        }
        UpdateDebris();
        Sound.Loop(LoopSfx.Wind, Height > 20, -0.4f + MathF.Min(0.6f, Height * 0.01f), MathF.Min(0.4f, Height * 0.008f));
    }

    private void Land(float topY)
    {
        var top = _floors[^1];
        float l = _blockPos.X, r = _blockPos.X + _blockW;
        float ol = MathF.Max(l, top.X), or = MathF.Min(r, top.X + top.W);
        if (or - ol <= 0.5f)
        {
            // Missed the tower completely.
            _missed = true;
            _overTimer = 1.8f;
            _blockPos.Y = topY - FloorH;
            _debris.Add(new Debris
            {
                Pos = new Vector2(l + _blockW / 2, topY - FloorH / 2), Vel = new Vector2(l < top.X ? -60 : 60, 0), W = _blockW,
                Spin = l < top.X ? -2.5f : 2.5f, Seed = _floors.Count,
            });
            _blockW = 0;
            Sound.Play(Sfx.Lose);
            Fx.Shake(4, 0.4f);
            return;
        }
        float cx = (ol + or) / 2;
        bool perfect = MathF.Abs(l - top.X) < 3.5f;
        int points;
        if (perfect)
        {
            _perfectRun++;
            l = top.X;
            float w = MathF.Min(top.W, _blockW);
            if (_perfectRun % 3 == 0)
            {
                // Reward a streak: the tower grows back a little.
                float grow = MathF.Min(10, StartW - w);
                if (grow > 0)
                {
                    w += grow;
                    l -= grow / 2;
                    Fx.Float("WIDER!", cx, topY - 40, Pal.Lime, 1.5f);
                }
            }
            _floors.Add(new Floor { X = l, W = w, Seed = _floors.Count * 7919, Perfect = true });
            _blockW = w;
            points = 25 + 25 * Math.Min(_perfectRun, 8);
            _flash = 1;
            Fx.Burst(cx, topY, Pal.White, 24, 160, 0.5f, 2f);
            Fx.Burst(l, topY, Pal.Gold, 8, 80, 0.4f, 2);
            Fx.Burst(l + w, topY, Pal.Gold, 8, 80, 0.4f, 2);
            Fx.Float(_perfectRun > 1 ? "PERFECT x" + _perfectRun : "PERFECT!", cx, topY - 26, Pal.Gold, 2f);
            Sound.Play(Sfx.Bell, MathF.Min(0.8f, -0.2f + _perfectRun * 0.1f), 0.8f);
        }
        else
        {
            _perfectRun = 0;
            float w = or - ol;
            _floors.Add(new Floor { X = ol, W = w, Seed = _floors.Count * 7919 });
            // The overhang breaks off and tumbles.
            bool leftCut = l < top.X;
            float cutW = _blockW - w;
            float cutX = leftCut ? l + cutW / 2 : or + cutW / 2;
            _debris.Add(new Debris
            {
                Pos = new Vector2(cutX, topY - FloorH / 2), Vel = new Vector2(leftCut ? -50 : 50, -20), W = cutW,
                Spin = (leftCut ? -1 : 1) * Rand(1.5f, 3.5f), Seed = _floors.Count,
            });
            _blockW = w;
            points = 10;
            Sound.Play(Sfx.Crack, Rand(-0.2f, 0.2f), 0.8f);
            Fx.Burst(leftCut ? ol : or, topY - FloorH / 2, Pal.LightGrey, 10, 80, 0.4f, 1.8f, 200, false);
        }
        Sound.Play(Sfx.Thud, 0, 0.7f);
        points += Height / 5 * 5;
        AddScore(points, cx, topY - 8, Pal.Yellow);
        Status = "FLOOR " + Height;
        Level = 1 + Height / 10;
        _camTarget = MathF.Max(0, TopWorldY - (340 - TopScreenY));
        Milestones();
        NewBlock();
    }

    private void Milestones()
    {
        string m = Height switch
        {
            10 => "TEN STOREYS!",
            20 => "ABOVE THE CLOUDS",
            35 => "NIGHT FALLS",
            50 => "INTO SPACE!",
            75 => "SKY SCRAPER? STAR SCRAPER!",
            100 => "ONE HUNDRED FLOORS!",
            _ => null,
        };
        if (m == null) return;
        _milestone = m;
        _milestoneTimer = 2.5f;
        Sound.Play(Sfx.LevelUp);
        AddScore(Height * 10, 320, 180, Pal.Cyan);
    }

    private void UpdateDebris()
    {
        for (int i = _debris.Count - 1; i >= 0; i--)
        {
            var d = _debris[i];
            d.Vel.Y += Gravity * 0.8f * Dt;
            d.Pos += d.Vel * Dt;
            d.Angle += d.Spin * Dt;
            if (d.Pos.Y > 420)
            {
                _debris.RemoveAt(i);
                continue;
            }
            _debris[i] = d;
        }
    }

    // ------------------------------------------------------------------ drawing

    /// <summary>0 = bright day ... 1 = deep space, from the camera height.</summary>
    private static float Altitude(float cam) => MathF2.Clamp(cam / 1000f, 0, 1.2f);

    private static void SkyColours(float alt, out Color top, out Color bottom)
    {
        var dayT = new Color(70, 150, 240); var dayB = new Color(190, 225, 255);
        var dusT = new Color(80, 60, 150); var dusB = new Color(255, 150, 90);
        var nigT = new Color(8, 10, 40); var nigB = new Color(40, 40, 100);
        var spaT = new Color(2, 2, 10); var spaB = new Color(12, 6, 30);
        if (alt < 0.35f) { float k = alt / 0.35f; top = Pal.Lerp(dayT, dusT, k); bottom = Pal.Lerp(dayB, dusB, k); }
        else if (alt < 0.7f) { float k = (alt - 0.35f) / 0.35f; top = Pal.Lerp(dusT, nigT, k); bottom = Pal.Lerp(dusB, nigB, k); }
        else { float k = MathF.Min(1, (alt - 0.7f) / 0.3f); top = Pal.Lerp(nigT, spaT, k); bottom = Pal.Lerp(nigB, spaB, k); }
    }

    private static float Darkness(float alt) => MathF2.Clamp((alt - 0.25f) / 0.4f, 0, 1);

    public override void Draw(Gfx g)
    {
        float time = Time;
        float alt = Altitude(_cam);
        SkyColours(alt, out var top, out var bottom);
        g.GradientV(0, 0, 640, 360, top, bottom);
        float dark = Darkness(alt);

        // Stars fade in with height.
        if (dark > 0.2f)
        {
            for (int i = 0; i < StarField.Length; i++)
            {
                float x = StarField[i].X;
                float y = Backdrops.Mod(StarField[i].Y + _cam * 0.05f, 360);
                float tw = 0.6f + 0.4f * MathF.Sin(time * (1 + i % 3) + i);
                g.Circle(x, y, 0.6f + (i % 4) * 0.3f, Color.White * ((dark - 0.2f) * tw));
            }
        }
        // Sun, then moon, then a planet.
        if (alt < 0.6f)
        {
            float sy = 90 + alt * 400;
            g.Glow(520, sy, 90, Pal.Gold, 0.5f * (1 - alt));
            g.Circle(520, sy, 22, Pal.Lerp(new Color(255, 245, 190), Pal.Orange, alt * 1.6f));
        }
        if (alt > 0.5f)
        {
            float k = MathF.Min(1, (alt - 0.5f) * 3);
            g.Glow(110, 90, 60, new Color(160, 180, 255), 0.35f * k);
            g.Circle(110, 90, 15, new Color(230, 235, 255) * k);
            g.Circle(116, 86, 13, top * k);
        }
        if (alt > 0.9f)
        {
            float k = MathF.Min(1, (alt - 0.9f) * 4);
            g.Glow(500, 120, 90, Pal.Orange, 0.3f * k);
            g.Circle(500, 120, 34, new Color(200, 120, 60) * k);
            g.Ellipse(500, 120, 60, 7, new Color(230, 190, 140) * (0.6f * k));
            g.Circle(500, 120, 26, new Color(230, 150, 80) * k);
        }

        // Clouds drift past around 300-700 world units up.
        for (int i = 0; i < 6; i++)
        {
            float wy = 220 + i * 90;
            float y = ScreenY(wy);
            if (y < -40 || y > 400) continue;
            float x = Backdrops.Mod(i * 137 + time * (10 + i * 4), 760) - 60;
            var c = Pal.Lerp(Color.White, new Color(255, 190, 170), MathF.Min(1, alt * 2)) * (0.85f - dark * 0.5f);
            g.Ellipse(x, y, 40, 12, c);
            g.Ellipse(x + 22, y - 6, 24, 12, c);
            g.Ellipse(x - 20, y - 4, 20, 9, c);
        }

        // City skyline and ground.
        float gy = ScreenY(0);
        if (gy < 420)
        {
            for (int i = 0; i < Skyline.Length; i++)
            {
                float x = Skyline[i].X, h = Skyline[i].Y, w = Skyline[i].Z;
                if (MathF.Abs(x + w / 2 - 320) < 80) continue;
                var bc = Pal.Lerp(new Color(120, 140, 175), new Color(30, 30, 60), dark);
                g.Rect(x, gy - h, w, h, bc);
                for (int wy = 0; wy < (int)(h / 10) - 1; wy++)
                    for (int wx = 0; wx < (int)(w / 7); wx++)
                        if (((i * 31 + wy * 7 + wx * 13) % 5) < 2)
                            g.Rect(x + 3 + wx * 7, gy - h + 5 + wy * 10, 3, 4, Pal.Lerp(new Color(170, 190, 220), Pal.Gold, dark));
            }
            g.Rect(0, gy, 640, 400 - gy, new Color(60, 120, 60));
            g.Rect(0, gy, 640, 3, new Color(90, 160, 80));
            g.Rect(200, gy + 2, 240, 10, new Color(90, 90, 100));
        }

        // The tower.
        for (int i = 0; i < _floors.Count; i++)
        {
            float y = ScreenY((i + 1) * FloorH);
            if (y > 380 || y < -FloorH) continue;
            DrawFloor(g, _floors[i].X, y, _floors[i].W, i, dark, time);
        }
        if (_flash > 0 && _floors.Count > 0)
        {
            var f = _floors[^1];
            g.Glow(f.X + f.W / 2, ScreenY(TopWorldY) + FloorH / 2, f.W, Pal.Gold, 0.6f * _flash);
        }

        foreach (var d in _debris)
        {
            g.RotatedRect(d.Pos, d.W, FloorH - 1, d.Angle, FloorColour(d.Seed, dark));
            g.RotatedRect(d.Pos + MathF2.FromAngle(d.Angle + MathF.PI / 2, FloorH / 2 - 2), d.W, 2, d.Angle, Color.Black * 0.3f);
        }

        DrawCrane(g, time, dark);

        if (_milestoneTimer > 0)
        {
            float a = MathF.Min(1, _milestoneTimer);
            g.TextShadow(_milestone, 320, 300, 2.5f, Pal.Cyan * a, Align.Center);
        }
        // Height gauge on the right.
        g.TextShadow(Height + "F", 628, 54, 2f, Color.White, Align.Right);
        if (_perfectRun >= 2)
            g.Text("PERFECT RUN " + _perfectRun, 628, 74, 1f, Pal.Gold, Align.Right);
    }

    private static Color FloorColour(int i, float dark)
    {
        var a = (i % 3) switch { 0 => new Color(220, 120, 70), 1 => new Color(200, 100, 60), _ => new Color(230, 140, 80) };
        if (i / 25 % 2 == 1) a = (i % 2 == 0) ? new Color(90, 150, 200) : new Color(80, 135, 185);
        return Pal.Lerp(a, Pal.Darken(a, 0.55f), dark);
    }

    private static void DrawFloor(Gfx g, float x, float y, float w, int i, float dark, float time)
    {
        var c = FloorColour(i, dark);
        g.Rect(x, y, w, FloorH, c);
        g.Rect(x, y, w, 2, Pal.Lighten(c, 0.25f));
        g.Rect(x, y + FloorH - 2, w, 2, Pal.Darken(c, 0.35f));
        g.Rect(x + w - 4, y + 2, 4, FloorH - 4, Pal.Darken(c, 0.2f));
        g.Rect(x, y + 2, 2, FloorH - 4, Pal.Lighten(c, 0.12f));
        // Windows: lit ones glow as night falls.
        int n = Math.Max(1, (int)((w - 4) / 11));
        float gap = (w - n * 7) / (n + 1);
        for (int k = 0; k < n; k++)
        {
            float wx = x + gap + k * (7 + gap);
            int h = (i * 73 + k * 151) % 7;
            bool lit = h < 3 || (h == 3 && (int)(time * 0.3f + i) % 3 == 0);
            var glass = Pal.Lerp(new Color(150, 200, 240), new Color(30, 40, 70), dark);
            var col = lit ? Pal.Lerp(glass, new Color(255, 220, 120), dark) : glass;
            g.Rect(wx, y + 5, 7, 8, col);
            if (lit && dark > 0.3f)
                g.Glow(wx + 3.5f, y + 9, 8, Pal.Gold, 0.25f * dark);
        }
    }

    private void DrawCrane(Gfx g, float time, float dark)
    {
        var steel = Pal.Lerp(Pal.Yellow, new Color(150, 130, 40), dark);
        // The jib spans the top of the view.
        g.Rect(0, JibY - 4, 640, 8, steel);
        for (int i = 0; i < 32; i++)
            g.Line(i * 20, JibY - 4, i * 20 + 10, JibY + 4, 1.5f, Pal.Darken(steel, 0.4f));
        g.Rect(0, JibY + 3, 640, 1.5f, Pal.Darken(steel, 0.5f));
        float tx = TrolleyX;
        // Trolley.
        g.RoundRect(tx - 12, JibY - 2, 24, 10, 3, new Color(60, 60, 70));
        g.Circle(tx - 6, JibY + 8, 2.5f, Pal.DarkGrey);
        g.Circle(tx + 6, JibY + 8, 2.5f, Pal.DarkGrey);
        if (!_falling && !_missed)
        {
            float sway = Sway;
            var hook = new Vector2(tx + sway, HangY - 10);
            g.Line(tx, JibY + 8, hook.X, hook.Y, 1.2f, Pal.DarkGrey);
            g.Line(hook.X, hook.Y, tx + sway - _blockW / 2 + 4, HangY, 1, Pal.DarkGrey);
            g.Line(hook.X, hook.Y, tx + sway + _blockW / 2 - 4, HangY, 1, Pal.DarkGrey);
            DrawFloor(g, tx + sway - _blockW / 2, HangY, _blockW, _floors.Count, dark, time);
            // A drop guide shadow on the tower.
            float topY = ScreenY(TopWorldY);
            g.Rect(tx + sway - _blockW / 2, topY - 2, _blockW, 2, Color.White * 0.25f);
        }
        else
        {
            g.Line(tx, JibY + 8, tx, HangY - 10, 1.2f, Pal.DarkGrey);
            if (_falling && !_missed)
                DrawFloor(g, _blockPos.X, _blockPos.Y, _blockW, _floors.Count, dark, time);
        }
    }

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        float s = r.H / 70f;
        g.GradientV(r.X, r.Y, r.W, r.H, new Color(40, 40, 120), new Color(255, 150, 90));
        g.Glow(r.Right - 20 * s, r.Y + 18 * s, 25 * s, Pal.Gold, 0.5f);
        g.Circle(r.Right - 20 * s, r.Y + 18 * s, 7 * s, new Color(255, 230, 170));
        float cx = r.CenterX;
        float fh = 6 * s;
        int floors = 8;
        for (int i = 0; i < floors; i++)
        {
            float w = (40 - i * 2.5f) * s;
            float off = MathF.Sin(i * 1.7f) * 2 * s;
            float y = r.Bottom - (i + 1) * fh;
            var c = FloorColour(i, 0.4f);
            g.Rect(cx - w / 2 + off, y, w, fh, c);
            g.Rect(cx - w / 2 + off, y, w, 1 * s, Pal.Lighten(c, 0.25f));
            int n = (int)(w / (5 * s));
            for (int k = 0; k < n; k++)
                g.Rect(cx - w / 2 + off + 2 * s + k * 5 * s, y + 1.8f * s, 2.4f * s, 2.6f * s, (i + k) % 3 == 0 ? Pal.Gold : new Color(60, 70, 110));
        }
        // Crane and a swinging floor.
        g.Rect(r.X, r.Y + 6 * s, r.W, 3 * s, Pal.Yellow);
        float tx = cx + MathF.Sin(time * 1.8f) * r.W * 0.3f;
        float hy = r.Y + 20 * s;
        g.Line(tx, r.Y + 9 * s, tx, hy, 1, Pal.DarkGrey);
        float bw = 22 * s;
        var bc = FloorColour(floors, 0.4f);
        g.Rect(tx - bw / 2, hy, bw, fh, bc);
        g.Rect(tx - bw / 2 + 2 * s, hy + 1.8f * s, 2.4f * s, 2.6f * s, Pal.Gold);
        g.Rect(tx + bw / 2 - 4.4f * s, hy + 1.8f * s, 2.4f * s, 2.6f * s, Pal.Gold);
    }

    public override void AutoPlay(Controls c)
    {
        if (_falling || _missed) return;
        var top = _floors[^1];
        // The block falls straight down, so drop when it is over the tower (plus a little human error).
        // Update advances the swing before reading the button, so judge the next two positions.
        float target = top.X + _apOffset;
        float d0 = MathF.Abs(BlockXAt(_phase) - target);
        float d1 = MathF.Abs(BlockXAt(_phase + SwingSpeed * Dt) - target);
        float d2 = MathF.Abs(BlockXAt(_phase + 2 * SwingSpeed * Dt) - target);
        if (d1 <= d0 && d1 <= d2 && d1 < 10)
        {
            c.FirePressed = true;
            c.Fire = true;
        }
    }
}
