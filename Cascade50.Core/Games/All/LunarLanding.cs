using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Capture;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 21 Lunar Landing: rotate and thrust a lander down onto flat pads among jagged lunar mountains.
/// Land gently and upright; the camera zooms in for the touchdown. Fuel carries over.
/// </summary>
public sealed class LunarLanding : MiniGame, ICaptureHints
{
    public override int Number => 21;
    public override string Title => "Lunar Landing";
    public override Category Category => Category.Skill;
    public override string Tagline => "Bring the lander down gently on a lunar pad, with fuel to spare.";
    public override Color Accent => Pal.Silver;

    public override string[] HowToPlay =>
    [
        "Rotate the lander and fire the engine to fight gravity. Touch down on a flat pad, upright and slow.",
        "Small pads pay more (x2 to x5). Speed readouts turn green when safe.",
        "Fuel carries over between landings. Crash three landers or run dry and the mission is over.",
    ];

    public override string[] DesktopControls => ["LEFT / RIGHT to rotate, SPACE (or UP) to thrust."];
    public override string[] TouchControls => ["LEFT / RIGHT to rotate, hold THRUST to fire."];
    public override Pad Pad => Pad.Horizontal | Pad.Fire;
    public override string FireLabel => "THRUST";
    public int CaptureTicks => 480;

    private const float SafeVy = 16, SafeVx = 9, SafeAngle = 0.2f, FuelMax = 1000;
    private static readonly Vector2 ScreenCentre = new(320, 191);

    private struct Pad2
    {
        public float X0, X1, Y;
        public int Mult;
    }

    private struct Dust
    {
        public Vector2 Pos, Vel;
        public float Life, Max;
        public bool Flame;
    }

    private readonly List<Vector2> _terrain = new();
    private readonly List<Pad2> _pads = new();
    private readonly List<Dust> _dust = new();
    private readonly List<Vector3> _craters = new();

    private Vector2 _pos, _vel;
    private float _angle, _fuel, _gravity, _thrustAcc;
    private bool _thrusting;
    private float _zoom = 1;
    private Vector2 _cam = ScreenCentre;
    private float _result; // seconds left of the landed / crashed banner
    private bool _crashed;
    private string _message;
    private Color _messageColour;
    private int _landings;

    protected override void Start()
    {
        Lives = 3;
        Level = 1;
        _fuel = FuelMax;
        _landings = 0;
        NewRound();
    }

    private void NewRound()
    {
        _gravity = 20 + Math.Min(Level - 1, 10) * 1.4f;
        _thrustAcc = _gravity * 2.7f;
        BuildTerrain();
        _pos = new Vector2(Rand(80, 560), 46);
        _vel = new Vector2(Rand(-30, 30), 0);
        _angle = 0;
        _result = 0;
        _crashed = false;
        _dust.Clear();
        _zoom = 1;
        _cam = ScreenCentre;
        Status = "LANDING " + Level;
    }

    private void BuildTerrain()
    {
        _terrain.Clear();
        _pads.Clear();
        _craters.Clear();
        float shrink = MathF.Max(0.6f, 1 - (Level - 1) * 0.05f);
        int[] mults = [5, 3, 2];
        float[] widths = [26 * shrink, 38 * shrink, 58 * shrink];
        // Place the pads in three separate thirds, shuffled.
        int[] order = [0, 1, 2];
        for (int i = 2; i > 0; i--)
        {
            int j = RandInt(0, i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
        for (int k = 0; k < 3; k++)
        {
            int m = order[k];
            float w = widths[m];
            float x0 = 30 + k * 200 + Rand(0, 170 - w);
            _pads.Add(new Pad2 { X0 = x0, X1 = x0 + w, Y = Rand(210, 320), Mult = mults[m] });
        }
        _pads.Sort((a, b) => a.X0.CompareTo(b.X0));

        float x = 0, y = Rand(220, 320);
        int pad = 0;
        while (x <= 640)
        {
            if (pad < _pads.Count && x + 14 >= _pads[pad].X0)
            {
                var p = _pads[pad];
                _terrain.Add(new Vector2(p.X0, p.Y));
                _terrain.Add(new Vector2(p.X1, p.Y));
                x = p.X1;
                y = p.Y;
                pad++;
                continue;
            }
            x += Rand(8, 18);
            // Jagged walk, with the odd tall peak.
            y += Rand(-26, 26);
            if (Chance(0.08f))
                y -= Rand(30, 70);
            y = MathF2.Clamp(y, 120, 344);
            _terrain.Add(new Vector2(MathF.Min(x, 640), y));
        }
        for (int i = 0; i < 18; i++)
        {
            float cx = Rand(10, 630);
            _craters.Add(new Vector3(cx, GroundAt(cx) + Rand(8, 40), Rand(3, 9)));
        }
    }

    private float GroundAt(float x)
    {
        if (x <= _terrain[0].X)
            return _terrain[0].Y;
        for (int i = 0; i + 1 < _terrain.Count; i++)
        {
            var a = _terrain[i];
            var b = _terrain[i + 1];
            if (x >= a.X && x <= b.X)
                return b.X - a.X < 0.01f ? a.Y : MathF2.Lerp(a.Y, b.Y, (x - a.X) / (b.X - a.X));
        }
        return _terrain[^1].Y;
    }

    private int PadUnder(float x0, float x1)
    {
        for (int i = 0; i < _pads.Count; i++)
            if (x0 >= _pads[i].X0 - 1 && x1 <= _pads[i].X1 + 1)
                return i;
        return -1;
    }

    private static Vector2 Rotate(Vector2 v, float a) =>
        new(v.X * MathF.Cos(a) - v.Y * MathF.Sin(a), v.X * MathF.Sin(a) + v.Y * MathF.Cos(a));

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        UpdateDust();
        UpdateCamera();
        if (_result > 0)
        {
            _result -= Dt;
            if (_result <= 0)
            {
                if (_fuel <= 0)
                {
                    EndGame(false, "Out of fuel.");
                    return;
                }
                if (_crashed)
                {
                    if (Lives <= 1)
                    {
                        Lives = 0;
                        EndGame(false, "All three landers lost.");
                        return;
                    }
                    LoseLife();
                }
                else
                {
                    Level++;
                }
                NewRound();
            }
            return;
        }

        float rot = In.AxisX;
        _angle = MathF2.Clamp(_angle + rot * 2.4f * Dt, -MathF.PI * 0.6f, MathF.PI * 0.6f);
        _thrusting = (In.Fire || In.Up) && _fuel > 0;
        var acc = new Vector2(0, _gravity);
        if (_thrusting)
        {
            acc += Rotate(new Vector2(0, -_thrustAcc), _angle);
            _fuel = MathF.Max(0, _fuel - 30 * Dt);
            Sound.Loop(LoopSfx.Thrust, true, -0.2f, 0.6f);
            var nozzle = _pos + Rotate(new Vector2(0, 10), _angle);
            var back = Rotate(new Vector2(0, 1), _angle);
            for (int i = 0; i < 2; i++)
                _dust.Add(new Dust
                {
                    Pos = nozzle, Vel = _vel + back * Rand(80, 140) + new Vector2(Rand(-15, 15), Rand(-15, 15)),
                    Life = 0.35f, Max = 0.35f, Flame = true,
                });
            // Kick up moondust near the surface.
            float alt = GroundAt(_pos.X) - _pos.Y;
            if (alt < 50 && Chance(0.6f))
            {
                float gx = _pos.X + Rand(-10, 10);
                _dust.Add(new Dust
                {
                    Pos = new Vector2(gx, GroundAt(gx) - 1), Vel = new Vector2(Rand(-60, 60), Rand(-30, -5)),
                    Life = 0.9f, Max = 0.9f,
                });
            }
            if (_fuel <= 0)
                Sound.Play(Sfx.Alarm, 0.3f, 0.6f);
        }
        if (_fuel > 0 && _fuel < 150 && Tick % 50 == 0)
            Sound.Play(Sfx.Beep, 0.6f, 0.4f);
        _vel += acc * Dt;
        _pos += _vel * Dt;
        if (_pos.X < 8 || _pos.X > 632)
        {
            _pos.X = MathF2.Clamp(_pos.X, 8, 632);
            _vel.X = -_vel.X * 0.3f;
        }
        if (_pos.Y < Screen.HudHeight + 10)
        {
            _pos.Y = Screen.HudHeight + 10;
            _vel.Y = MathF.Max(0, _vel.Y);
        }

        CheckContact();
    }

    private void CheckContact()
    {
        var footL = _pos + Rotate(new Vector2(-9, 10), _angle);
        var footR = _pos + Rotate(new Vector2(9, 10), _angle);
        var top = _pos + Rotate(new Vector2(0, -10), _angle);
        bool touchL = footL.Y >= GroundAt(footL.X), touchR = footR.Y >= GroundAt(footR.X);
        bool touchBody = top.Y >= GroundAt(top.X) || _pos.Y + 4 >= GroundAt(_pos.X);
        if (!touchL && !touchR && !touchBody)
            return;

        int pad = PadUnder(MathF.Min(footL.X, footR.X), MathF.Max(footL.X, footR.X));
        bool gentle = _vel.Y < SafeVy && MathF.Abs(_vel.X) < SafeVx && MathF.Abs(_angle) < SafeAngle;
        if (pad >= 0 && gentle && !touchBody)
        {
            var p = _pads[pad];
            bool perfect = _vel.Y < 8 && MathF.Abs(_angle) < 0.08f;
            int points = (perfect ? 100 : 50) * p.Mult;
            _pos.Y = p.Y - 10 * MathF.Cos(_angle);
            _vel = Vector2.Zero;
            _landings++;
            var sp = ToScreen(_pos);
            AddScore(points, sp.X, sp.Y - 40, Pal.Gold);
            _fuel = MathF.Min(FuelMax, _fuel + (perfect ? 150 : 60));
            _message = perfect ? "PERFECT LANDING!" : "THE EAGLE HAS LANDED";
            _messageColour = perfect ? Pal.Gold : Pal.Lime;
            _result = 2.6f;
            _crashed = false;
            Sound.Play(Sfx.Land);
            Sound.Play(perfect ? Sfx.Bonus : Sfx.Correct);
            for (int i = 0; i < 20; i++)
                _dust.Add(new Dust { Pos = new Vector2(_pos.X + Rand(-14, 14), p.Y - 1), Vel = new Vector2(Rand(-50, 50), Rand(-25, -5)), Life = 1, Max = 1 });
            return;
        }

        // Crash.
        var s = ToScreen(_pos);
        Fx.Explode(s.X, s.Y, 1.8f);
        Fx.Burst(s.X, s.Y, Pal.Silver, 26, 150, 1.1f, 2.5f, 120);
        Fx.Burst(s.X, s.Y, Pal.Gold, 14, 110, 1f, 2f, 120);
        Sound.Play(Sfx.BigExplode);
        _message = pad < 0 ? "MISSED THE PAD" : _vel.Y >= SafeVy ? "TOO FAST!" : MathF.Abs(_angle) >= SafeAngle ? "NOT UPRIGHT!" : "TOO MUCH DRIFT!";
        _messageColour = Pal.Red;
        _result = 2.2f;
        _crashed = true;
        _vel = Vector2.Zero;
    }

    private void UpdateDust()
    {
        for (int i = _dust.Count - 1; i >= 0; i--)
        {
            var d = _dust[i];
            d.Life -= Dt;
            if (d.Life <= 0)
            {
                _dust.RemoveAt(i);
                continue;
            }
            d.Vel.Y += (d.Flame ? 0 : _gravity) * Dt;
            d.Pos += d.Vel * Dt;
            if (!d.Flame && d.Pos.Y > GroundAt(d.Pos.X))
            {
                d.Pos.Y = GroundAt(d.Pos.X);
                d.Vel *= 0.3f;
            }
            _dust[i] = d;
        }
    }

    private void UpdateCamera()
    {
        float alt = GroundAt(_pos.X) - _pos.Y;
        float want = _crashed ? _zoom : alt < 80 ? 2.2f : alt < 130 ? 1.5f : 1f;
        _zoom = MathF2.Approach(_zoom, want, 1.6f * Dt);
        float k = (_zoom - 1) / 1.2f;
        var target = Vector2.Lerp(ScreenCentre, _pos, MathF2.Clamp(k, 0, 1));
        float hw = 320 / _zoom, hh = (Screen.Height - ScreenCentre.Y) / _zoom, ht = (ScreenCentre.Y - Screen.HudHeight) / _zoom;
        target.X = MathF2.Clamp(target.X, hw, 640 - hw);
        target.Y = MathF2.Clamp(target.Y, Screen.HudHeight + ht, 360 - hh);
        _cam = target;
    }

    private Vector2 ToScreen(Vector2 w) => ScreenCentre + (w - _cam) * _zoom;

    // ------------------------------------------------------------------ drawing

    public override void Draw(Gfx g)
    {
        float t = Time;
        Backdrops.Space(g, t, 0, 21, Screen.Bounds);
        // Earth hangs in the sky.
        g.Glow(540, 74, 60, Pal.Sky, 0.35f);
        g.Circle(540, 74, 24, new Color(40, 90, 200));
        g.Ellipse(532, 68, 9, 6, new Color(60, 160, 80));
        g.Ellipse(549, 82, 7, 5, new Color(60, 160, 80));
        g.Ellipse(542, 62, 10, 2, Pal.White * 0.7f);
        g.Circle(534, 66, 10, Pal.White * 0.08f);

        DrawTerrain(g);

        // Dust and exhaust.
        foreach (var d in _dust)
        {
            var p = ToScreen(d.Pos);
            float k = d.Life / d.Max;
            if (d.Flame)
            {
                g.Glow(p, 7 * _zoom * k, Pal.Orange, 0.6f * k);
                g.Circle(p.X, p.Y, 1.4f * _zoom * k, Pal.Yellow * k);
            }
            else
            {
                g.Circle(p.X, p.Y, 1.5f * _zoom, Pal.LightGrey * (0.6f * k));
            }
        }

        if (!(_crashed && _result > 0))
            DrawLander(g, ToScreen(_pos), _angle, _zoom, _thrusting, t);

        DrawInstruments(g);

        if (_result > 0 && _message != null)
        {
            float a = MathF.Min(1, _result * 2);
            g.TextShadow(_message, 320, 110, 2.5f, _messageColour * a, Align.Center);
        }
    }

    private void DrawTerrain(Gfx g)
    {
        var rock = new Color(92, 92, 104);
        var rockDark = new Color(58, 58, 70);
        for (int i = 0; i + 1 < _terrain.Count; i++)
        {
            var a = ToScreen(_terrain[i]);
            var b = ToScreen(_terrain[i + 1]);
            if (b.X < -10 || a.X > 650)
                continue;
            float bottom = 362;
            g.Triangle(a, b, new Vector2(b.X, bottom), rockDark);
            g.Triangle(a, new Vector2(b.X, bottom), new Vector2(a.X, bottom), rockDark);
            // A lighter band along the surface.
            float band = 7 * _zoom;
            g.Triangle(a, b, b + new Vector2(0, band), rock);
            g.Triangle(a, b + new Vector2(0, band), a + new Vector2(0, band), rock);
            g.Line(a, b, 1.5f * _zoom, new Color(200, 200, 215));
        }
        foreach (var c in _craters)
        {
            var p = ToScreen(new Vector2(c.X, c.Y));
            g.Ellipse(p.X, p.Y, c.Z * _zoom, c.Z * 0.4f * _zoom, new Color(44, 44, 54));
            g.Ellipse(p.X, p.Y - c.Z * 0.12f * _zoom, c.Z * 0.8f * _zoom, c.Z * 0.25f * _zoom, new Color(36, 36, 46));
        }
        // Pads with beacon lights and multipliers.
        foreach (var p in _pads)
        {
            var a = ToScreen(new Vector2(p.X0, p.Y));
            var b = ToScreen(new Vector2(p.X1, p.Y));
            var col = p.Mult == 5 ? Pal.Red : p.Mult == 3 ? Pal.Yellow : Pal.Lime;
            g.Rect(a.X, a.Y - 1 * _zoom, b.X - a.X, 3 * _zoom, col);
            g.Glow((a + b) / 2, (b.X - a.X) * 0.7f, col, 0.25f);
            bool blink = (int)(Time * 2) % 2 == 0;
            g.Glow(a, 6 * _zoom, col, blink ? 0.9f : 0.3f);
            g.Glow(b, 6 * _zoom, col, blink ? 0.3f : 0.9f);
            g.TextShadow("x" + p.Mult, (a.X + b.X) / 2, a.Y + 6 * _zoom, 1.25f, Pal.Lighten(col, 0.6f), Align.Center);
        }
    }

    private static readonly Vector2[] Ascent = [new(-5, -11), new(5, -11), new(7, -6), new(7, -2), new(-7, -2), new(-7, -6)];
    private static readonly Vector2[] Descent = [new(-8, -2), new(8, -2), new(9, 5), new(-9, 5)];
    private static readonly Vector2[] Window = [new(-3, -9), new(1, -9), new(1, -5), new(-3, -5)];

    private static void DrawLander(Gfx g, Vector2 p, float angle, float s, bool thrust, float t)
    {
        if (thrust)
        {
            float fl = 0.75f + 0.25f * MathF.Sin(t * 45);
            var n = p + Rotate(new Vector2(0, 7), angle) * s;
            var tip = p + Rotate(new Vector2(0, 20 + 8 * fl), angle) * s;
            var l = p + Rotate(new Vector2(-3.5f, 7), angle) * s;
            var r = p + Rotate(new Vector2(3.5f, 7), angle) * s;
            g.Glow(n, 16 * s, Pal.Orange, 0.8f);
            g.Triangle(l, r, tip, Pal.Orange);
            g.Triangle(l * 0.5f + n * 0.5f, r * 0.5f + n * 0.5f, Vector2.Lerp(n, tip, 0.6f), Pal.Yellow);
        }
        g.Glow(p, 22 * s, Pal.Silver, 0.15f);
        // Legs.
        var hipL = p + Rotate(new Vector2(-7, 3), angle) * s;
        var hipR = p + Rotate(new Vector2(7, 3), angle) * s;
        var footL = p + Rotate(new Vector2(-10, 10), angle) * s;
        var footR = p + Rotate(new Vector2(10, 10), angle) * s;
        g.Line(hipL, footL, 1.4f * s, Pal.LightGrey);
        g.Line(hipR, footR, 1.4f * s, Pal.LightGrey);
        g.Line(footL - Rotate(new Vector2(2, 0), angle) * s, footL + Rotate(new Vector2(2, 0), angle) * s, 1.6f * s, Pal.White);
        g.Line(footR - Rotate(new Vector2(2, 0), angle) * s, footR + Rotate(new Vector2(2, 0), angle) * s, 1.6f * s, Pal.White);
        // Nozzle.
        g.Shape(Nozzle, p, angle, s, Pal.DarkGrey);
        g.Shape(Descent, p, angle, s, new Color(220, 170, 50));
        g.Shape(Foil, p, angle, s, new Color(250, 210, 90));
        g.Shape(Ascent, p, angle, s, new Color(200, 202, 214));
        g.Shape(Window, p, angle, s, new Color(30, 40, 70));
        g.Shape(Glint, p, angle, s, Pal.Sky);
    }

    private static readonly Vector2[] Nozzle = [new(-3, 5), new(3, 5), new(4, 8), new(-4, 8)];
    private static readonly Vector2[] Foil = [new(-8, -2), new(8, -2), new(8, 0), new(-8, 0)];
    private static readonly Vector2[] Glint = [new(-2.5f, -8.5f), new(-1, -8.5f), new(-1, -7), new(-2.5f, -7)];

    private void DrawInstruments(Gfx g)
    {
        var panel = new RectF(8, Screen.HudHeight + 6, 150, 66);
        g.Panel(panel, Pal.Panel * 0.85f, Pal.Silver * 0.6f, 6);
        float x = panel.X + 8, y = panel.Y + 7;
        float alt = MathF.Max(0, GroundAt(_pos.X) - _pos.Y - 10);
        g.Text("ALT", x, y, 1f, Pal.LightGrey);
        g.Text(((int)alt).ToString(), x + 60, y, 1f, Pal.White, Align.Right);
        bool vOk = _vel.Y < SafeVy, hOk = MathF.Abs(_vel.X) < SafeVx;
        g.Text("V.SPD", x, y + 13, 1f, Pal.LightGrey);
        g.Text(((int)_vel.Y).ToString(), x + 60, y + 13, 1f, vOk ? Pal.Lime : Pal.Red, Align.Right);
        g.Text("H.SPD", x, y + 26, 1f, Pal.LightGrey);
        g.Text(((int)MathF.Abs(_vel.X)).ToString(), x + 60, y + 26, 1f, hOk ? Pal.Lime : Pal.Red, Align.Right);
        // Attitude dial.
        var dial = new Vector2(panel.Right - 30, panel.Y + 24);
        g.Ring(dial.X, dial.Y, 14, 1.2f, Pal.Grey);
        var up = Rotate(new Vector2(0, -12), _angle);
        g.Line(dial, dial + up, 2f, MathF.Abs(_angle) < SafeAngle ? Pal.Lime : Pal.Orange);
        // Velocity arrow.
        var v = _vel / 3;
        if (v.LengthSquared() > 144)
            v = Vector2.Normalize(v) * 12;
        g.Line(dial, dial + v, 1.2f, Pal.Sky);
        // Fuel bar.
        g.Text("FUEL", x, y + 42, 1f, _fuel < 150 && (int)(Time * 4) % 2 == 0 ? Pal.Red : Pal.LightGrey);
        float fw = panel.W - 50;
        g.Rect(x + 30, y + 42, fw, 7, new Color(20, 20, 30));
        var fc = _fuel < 150 ? Pal.Red : _fuel < 400 ? Pal.Yellow : Pal.Lime;
        g.GradientH(x + 30, y + 42, fw * (_fuel / FuelMax), 7, Pal.Darken(fc, 0.3f), fc);
    }

    private static readonly float[] IconPeaks = [0.0f, 0.12f, 0.22f, 0.3f, 0.38f, 0.46f, 0.62f, 0.7f, 0.8f, 0.9f, 1.0f];
    private static readonly float[] IconHeights = [20, 34, 18, 26, 6, 6, 30, 16, 38, 22, 28];

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        float s = r.H / 70f;
        Backdrops.Space(g, time, 0, 21, r);
        g.Glow(r.Right - 24 * s, r.Y + 16 * s, 22 * s, Pal.Sky, 0.4f);
        g.Circle(r.Right - 24 * s, r.Y + 16 * s, 9 * s, new Color(40, 90, 200));
        g.Ellipse(r.Right - 27 * s, r.Y + 14 * s, 4 * s, 3 * s, new Color(60, 160, 80));
        // Mountains and a pad.
        var rock = new Color(92, 92, 104);
        float gy = r.Bottom - 12 * s;
        for (int i = 0; i + 1 < IconPeaks.Length; i++)
        {
            var a = new Vector2(r.X + IconPeaks[i] * r.W, gy - IconHeights[i] * s * 0.6f);
            var b = new Vector2(r.X + IconPeaks[i + 1] * r.W, gy - IconHeights[i + 1] * s * 0.6f);
            g.Triangle(a, b, new Vector2(b.X, r.Bottom), rock);
            g.Triangle(a, new Vector2(b.X, r.Bottom), new Vector2(a.X, r.Bottom), rock);
            g.Line(a, b, 1.2f * s, new Color(200, 200, 215));
        }
        float px0 = r.X + 0.38f * r.W, px1 = r.X + 0.46f * r.W, py = gy - 6 * s * 0.6f;
        g.Rect(px0, py - 1 * s, px1 - px0, 2.5f * s, Pal.Red);
        g.Glow((px0 + px1) / 2, py, 16 * s, Pal.Red, 0.4f);
        float k = (time * 0.25f) % 1f;
        var lp = new Vector2((px0 + px1) / 2 - 30 * s * (1 - k), r.Y + 14 * s + (py - r.Y - 26 * s) * MathF2.EaseOut(k));
        DrawLander(g, lp, 0.25f * (1 - k), 1.3f * s, true, time);
    }

    // ------------------------------------------------------------------ autopilot

    public override void AutoPlay(Controls c)
    {
        if (_result > 0)
            return;
        // Choose a pad: prefer high multipliers, but not if it is far away.
        int best = 0;
        float bestScore = float.MinValue;
        for (int i = 0; i < _pads.Count; i++)
        {
            float cx = (_pads[i].X0 + _pads[i].X1) / 2;
            float sc = _pads[i].Mult * 40 - MathF.Abs(cx - _pos.X) * 0.5f;
            if (sc > bestScore)
            {
                bestScore = sc;
                best = i;
            }
        }
        var pad = _pads[best];
        float padX = (pad.X0 + pad.X1) / 2;
        float dx = padX - _pos.X;
        float alt = pad.Y - _pos.Y - 10;

        // Keep clear of the peaks between here and the pad.
        float highest = pad.Y;
        float lo = MathF.Min(_pos.X, padX), hi = MathF.Max(_pos.X, padX);
        for (float x = lo; x <= hi; x += 6)
            if (MathF.Abs(x - padX) > (pad.X1 - pad.X0) / 2 + 4)
                highest = MathF.Min(highest, GroundAt(x));
        float clearance = highest - _pos.Y - 10;

        float vxTarget = MathF2.Clamp(dx * 0.35f, -38, 38);
        if (MathF.Abs(dx) < 3)
            vxTarget = 0;
        float vyTarget;
        if (MathF.Abs(dx) > 14 && clearance < 45)
            vyTarget = -12;
        else if (MathF.Abs(dx) > 14)
            vyTarget = MathF2.Clamp(clearance * 0.2f, 0, 30);
        else
            vyTarget = MathF2.Clamp(alt * 0.22f, 5, 40);

        float angleTarget = MathF2.Clamp((vxTarget - _vel.X) * 0.035f, -0.45f, 0.45f);
        if (alt < 22 && MathF.Abs(dx) < 14)
            angleTarget = MathF2.Clamp(angleTarget, -0.05f, 0.05f);
        float da = angleTarget - _angle;
        c.SetDirections(MathF.Abs(da) < 0.03f ? 0 : MathF2.Clamp(da * 8, -1, 1), 0);
        bool thrust = _vel.Y > vyTarget || (MathF.Abs(vxTarget - _vel.X) > 6 && MathF.Abs(_angle) > 0.2f && _vel.Y > vyTarget - 8);
        c.Fire = thrust;
        c.FirePressed = thrust && !_thrusting;
    }
}
