using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 39 Ski Jump: tuck down the in-run, time the take-off, balance against the gusts and land a
/// telemark. Three jumps on each of three hills; distance plus style points from five judges.
/// </summary>
public sealed class SkiJump : MiniGame, Cascade50.Core.Capture.ICaptureHints
{
    public override int Number => 39;
    public override string Title => "Ski Jump";
    public override Category Category => Category.Skill;
    public override string Tagline => "Fly for distance and style on three ever bigger ski jumps.";
    public override Color Accent => Pal.Sky;

    public override string[] HowToPlay =>
    [
        "Hold DOWN to tuck on the in-run, then press FIRE right at the take-off edge.",
        "In the air, use UP / DOWN to keep your balance against the gusts.",
        "Press FIRE just before touchdown to land a telemark. Distance plus style from 5 judges.",
    ];

    public override string[] DesktopControls => ["DOWN tuck / lean, UP lean back.", "SPACE to jump and to land."];
    public override string[] TouchControls => ["Arrows to tuck and to balance.", "FIRE to jump and to land."];
    public int CaptureTicks => 610;

    public override Pad Pad => Pad.Vertical | Pad.Fire;
    public override string FireLabel => "JUMP";

    // ------------------------------------------------------------------ hills

    private sealed class Hill
    {
        public string Name;
        public float K, Hs, Speed, Lift, PerMetre, KPoints, Gust;
    }

    private static readonly Hill[] Hills =
    [
        new() { Name = "NORMAL HILL", K = 90, Hs = 106, Speed = 25f, Lift = 0.0038f, PerMetre = 2f, KPoints = 60, Gust = 0.55f },
        new() { Name = "LARGE HILL", K = 120, Hs = 137, Speed = 26.5f, Lift = 0.005f, PerMetre = 1.8f, KPoints = 60, Gust = 0.8f },
        new() { Name = "SKI FLYING", K = 185, Hs = 215, Speed = 28.5f, Lift = 0.0066f, PerMetre = 1.2f, KPoints = 120, Gust = 1.05f },
    ];

    private const float Grav = 9.81f, Drag = 0.0012f;
    private const float S = 5;               // pixels per metre
    private const float HillStep = 0.5f;         // hill table resolution
    private static readonly float TableTan = MathF.Tan(11 * MathF.PI / 180), InrunTan = MathF.Tan(35 * MathF.PI / 180);
    private const float TableLen = 8, InrunStartX = -79;
    private const int JumpsPerHill = 3;

    private float[] _depth = Array.Empty<float>();
    private float[] _arc = Array.Empty<float>();

    private void BuildHill(Hill h)
    {
        int n = (int)(h.K * 2.1f / HillStep) + 2;
        _depth = new float[n];
        _arc = new float[n];
        float tmax = 37 * MathF.PI / 180;
        float y = 3, arc = 0;
        for (int i = 0; i < n; i++)
        {
            float x = i * HillStep;
            _depth[i] = y;
            _arc[i] = arc;
            float t;
            if (x < 0.45f * h.K)
                t = tmax * (0.25f + 0.75f * MathF.Pow(x / (0.45f * h.K), 0.7f));
            else if (x < 1.05f * h.K)
                t = tmax;
            else if (x < 1.5f * h.K)
                t = tmax * (1 - (x - 1.05f * h.K) / (0.45f * h.K));
            else
                t = 0;
            float dy = MathF.Tan(t) * HillStep;
            y += dy;
            arc += MathF.Sqrt(HillStep * HillStep + dy * dy);
        }
    }

    private float MaxX => (_depth.Length - 2) * HillStep;

    /// <summary>Surface depth (metres, down positive) of the in-run (x &lt; 0) or the landing hill.</summary>
    private float Surface(float x)
    {
        if (x < 0)
        {
            if (x >= -TableLen)
                return x * TableTan;
            return -TableLen * TableTan + (x + TableLen) * InrunTan;
        }
        float f = x / HillStep;
        int i = Math.Min((int)f, _depth.Length - 2);
        float k = MathF.Min(1, f - i);
        return MathF2.Lerp(_depth[i], _depth[i + 1], k);
    }

    private float SlopeAngle(float x) => MathF.Atan2(Surface(x + 0.3f) - Surface(x - 0.3f), 0.6f);

    private float ArcAt(float x)
    {
        if (x <= 0)
            return 0;
        float f = x / HillStep;
        int i = Math.Min((int)f, _arc.Length - 2);
        return MathF2.Lerp(_arc[i], _arc[i + 1], MathF.Min(1, f - i));
    }

    private float XForArc(float d)
    {
        for (int i = 1; i < _arc.Length; i++)
            if (_arc[i] >= d)
                return (i - 1 + (d - _arc[i - 1]) / MathF.Max(0.001f, _arc[i] - _arc[i - 1])) * HillStep;
        return MaxX;
    }

    private static float Ground(float x) => 3 + x * 0.62f;   // natural hillside under the in-run (x < 0)

    // ------------------------------------------------------------------ state

    private enum Phase { Ready, Inrun, Flight, Landed, Results }

    private enum Pose { Tuck, Flight, Land, Telemark, Fall }

    private Phase _phase;
    private float _phaseTime;
    private int _hill, _jump;
    private Hill H => Hills[_hill];
    private float _x, _y, _v, _vx, _vy;
    private float _lean, _leanV;
    private float _takeoff;        // quality 0..1
    private bool _jumped;
    private float _lateWindow;
    private float _telemarkH = -1;
    private bool _fell;
    private float _spin;
    private float _wind, _gustP1, _gustP2, _gustKick;
    private float _leanSum, _flightTime;
    private float _distance;
    private float _distPoints;
    private readonly float[] _judges = new float[5];
    private int _judgeHi, _judgeLo;
    private float _style, _total;
    private string _landText, _takeoffText;
    private float _camX, _camY;
    private float _best;
    private float _tuck;
    private readonly float[] _hillBest = new float[3];
    private int _lastDecade;

    protected override void Start()
    {
        _hill = 0;
        _jump = 0;
        BuildHill(H);
        NewJump();
    }

    private void NewJump()
    {
        Level = _hill + 1;
        Status = $"{H.Name} K{H.K:0}  JUMP {_jump + 1}/{JumpsPerHill}";
        _phase = Phase.Ready;
        _phaseTime = 0;
        _x = InrunStartX;
        _y = Surface(_x);
        _v = 0;
        _lean = 0;
        _leanV = 0;
        _jumped = false;
        _lateWindow = 0;
        _telemarkH = -1;
        _fell = false;
        _spin = 0;
        _takeoff = 0;
        _leanSum = 0;
        _flightTime = 0;
        _distance = 0;
        _landText = null;
        _takeoffText = null;
        _tuck = 0;
        float range = 1.2f + _hill * 0.6f;
        _wind = MathF.Round(Rand(-range, range) * 10) / 10;
        _gustP1 = Rand(0, 6);
        _gustP2 = Rand(0, 6);
        _camX = _x + 20;
        _camY = _y + 10;
        _lastDecade = 0;
    }

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        _phaseTime += Dt;
        switch (_phase)
        {
            case Phase.Ready:
                _tuck = MathF2.Approach(_tuck, In.Down ? 1 : 0, Dt * 4);
                if (_phaseTime > 1.6f || (_phaseTime > 0.4f && In.FirePressed))
                {
                    _phase = Phase.Inrun;
                    _phaseTime = 0;
                    Sound.Play(Sfx.Whoosh, -0.5f, 0.6f);
                    Sound.Play(Sfx.Beep, 0.4f);
                }
                break;
            case Phase.Inrun:
                UpdateInrun();
                break;
            case Phase.Flight:
                UpdateFlight();
                break;
            case Phase.Landed:
                UpdateLanded();
                break;
            case Phase.Results:
                UpdateResults();
                break;
        }
        UpdateCamera();
    }

    private void UpdateInrun()
    {
        _tuck = MathF2.Approach(_tuck, In.Down ? 1 : 0, Dt * 4);
        float vt = H.Speed / 0.95f * (0.93f + 0.07f * _tuck);
        _v += 0.012f * (vt * vt - _v * _v) * Dt;
        float ang = SlopeAngle(_x);
        _x += _v * MathF.Cos(ang) * Dt;
        _y = Surface(_x);
        Sound.Loop(LoopSfx.Wind, true, -0.6f + _v / 40, 0.2f + _v / 60);
        if (Tick % 3 == 0)
            Fx.Spark(Sx(_x - 1), Sy(_y), -_v * 2, -Rand(5, 20), Pal.White * 0.8f, 0.4f, 1.5f, 60, false);

        if (In.FirePressed && _x > -26)
        {
            float d = MathF.Abs(_x);
            _takeoff = MathF2.Clamp(1 - MathF.Max(0, d - 0.9f) / 5f, 0, 1);
            _takeoffText = d < 0.9f ? "PERFECT TAKE-OFF" : d < 3 ? "GOOD TAKE-OFF" : "EARLY!";
            Launch(1.2f + 2.3f * _takeoff);
            return;
        }
        if (_x >= 0)
        {
            // Off the edge without jumping: a short window to still push off, late.
            _takeoff = 0;
            _takeoffText = "LATE!";
            _lateWindow = 0.22f;
            Launch(0.4f);
        }
    }

    private void Launch(float impulse)
    {
        float ang = MathF.Atan(TableTan);
        _vx = _v * MathF.Cos(ang);
        _vy = _v * MathF.Sin(ang) - impulse;
        _jumped = impulse > 1;
        _phase = Phase.Flight;
        _phaseTime = 0;
        _lean = (1 - _takeoff) * 0.5f * (Chance(0.5f) ? 1 : -1);
        _leanV = 0;
        Sound.Play(Sfx.Jump, 0.2f * _takeoff);
        Sound.Play(Sfx.Whoosh, 0.2f, 0.7f);
        Fx.Burst(Sx(_x), Sy(_y), Pal.White, 14, 60, 0.5f, 1.8f, 40, false);
    }

    private void UpdateFlight()
    {
        _flightTime += Dt;
        if (_lateWindow > 0)
        {
            _lateWindow -= Dt;
            if (In.FirePressed)
            {
                _takeoff = 0.6f * MathF.Max(0, _lateWindow / 0.22f);
                _vy -= 0.8f + 2.3f * _takeoff;
                _takeoffText = "A BIT LATE";
                _lateWindow = 0;
                Sound.Play(Sfx.Jump);
            }
        }

        // Balance: gusts push you about, UP / DOWN corrects, and it is slightly unstable.
        float g = H.Gust * (0.7f * MathF.Sin(_flightTime * 1.4f + _gustP1) + 0.5f * MathF.Sin(_flightTime * 3.1f + _gustP2));
        if (_gustKick != 0)
        {
            g += _gustKick;
            _gustKick = MathF2.Approach(_gustKick, 0, Dt * 4);
        }
        else if (Chance(0.008f + _hill * 0.004f))
        {
            _gustKick = Rand(-1, 1) * (1.5f + _hill * 0.7f);
            Sound.Play(Sfx.Whoosh, Rand(-0.2f, 0.4f), 0.4f);
        }
        if (!_fell)
        {
            _leanV += (g + 0.9f * _lean) * Dt;
            // Up leans back (negative), down leans forward (positive).
            _leanV += In.AxisY * 3.2f * Dt;
            _leanV *= 1 - 2.2f * Dt;
            _lean += _leanV * Dt;
            _leanSum += MathF.Abs(_lean) * Dt;
            if (MathF.Abs(_lean) > 1.7f)
            {
                Fall();
            }
        }
        else
        {
            _spin += 6 * Dt;
        }

        float q = _fell ? 0 : MathF2.Clamp(1 - 0.55f * _lean * _lean, 0, 1);
        float v2 = _vx * _vx + _vy * _vy;
        _vx += -Drag * v2 * (1 + (_lean < 0 ? -_lean * 0.6f : 0)) * Dt;
        _vy += (Grav - H.Lift * q * (1 + 0.06f * _wind) * v2) * Dt;
        _x += _vx * Dt;
        _y += _vy * Dt;
        Sound.Loop(LoopSfx.Wind, true, -0.3f + MathF.Sqrt(v2) / 50, 0.35f);

        float h = Surface(_x) - _y;
        if (!_fell && _telemarkH < 0 && In.FirePressed && _flightTime > 0.25f)
        {
            _telemarkH = h;
            Sound.Play(Sfx.Step, 0.3f, 0.6f);
        }
        // Distance markers whoosh by.
        int dec = (int)(ArcAt(_x) / 10);
        if (dec != _lastDecade && _x > 0)
        {
            _lastDecade = dec;
            Sound.Play(Sfx.Tick, -0.4f + dec * 0.04f, 0.25f);
        }

        if (h <= 0 || _x >= MaxX - 1)
        {
            _y = Surface(_x);
            Touchdown();
        }
    }

    private void Fall()
    {
        _fell = true;
        _landText = "FALL!";
        Sound.Play(Sfx.Hurt);
    }

    private void Touchdown()
    {
        _distance = MathF.Round(ArcAt(_x) * 2) / 2;
        _phase = Phase.Landed;
        _phaseTime = 0;
        float landDeduct;
        if (_fell || MathF.Abs(_lean) > 1.15f)
        {
            if (!_fell)
                Fall();
            _fell = true;
            landDeduct = 7;
            Fx.Burst(Sx(_x), Sy(_y), Pal.White, 40, 140, 0.9f, 2.5f, 120, false);
            Fx.Shake(4, 0.4f);
            Sound.Play(Sfx.Thud);
            Sound.Play(Sfx.Crack, -0.3f);
        }
        else if (_telemarkH < 0)
        {
            landDeduct = 2.5f;
            _landText = "NO TELEMARK";
            Sound.Play(Sfx.Land);
        }
        else if (_telemarkH > 4.5f)
        {
            landDeduct = 1.5f;
            _landText = "TELEMARK TOO EARLY";
            Sound.Play(Sfx.Land);
        }
        else
        {
            landDeduct = 0;
            _landText = "TELEMARK!";
            Sound.Play(Sfx.Land, 0.3f);
        }
        if (!_fell)
            Fx.Burst(Sx(_x), Sy(_y), Pal.White, 24, 90, 0.7f, 2f, 80, false);

        // Judges.
        float flight = MathF.Min(5, _flightTime > 0 ? _leanSum / _flightTime * 6 : 0);
        float take = (1 - _takeoff) * 1.5f;
        float baseScore = 19.5f - flight - take - landDeduct;
        _judgeHi = 0;
        _judgeLo = 0;
        for (int i = 0; i < 5; i++)
        {
            _judges[i] = MathF2.Clamp(MathF.Round((baseScore + Rand(-0.8f, 0.7f)) * 2) / 2, 0, 20);
            if (_judges[i] > _judges[_judgeHi])
                _judgeHi = i;
            if (_judges[i] <= _judges[_judgeLo])
                _judgeLo = i;
        }
        if (_judgeLo == _judgeHi)
            _judgeLo = (_judgeHi + 1) % 5;
        _style = 0;
        for (int i = 0; i < 5; i++)
            if (i != _judgeHi && i != _judgeLo)
                _style += _judges[i];
        _distPoints = MathF.Max(0, H.KPoints + (_distance - H.K) * H.PerMetre);
        _total = _distPoints + _style;
        if (_distance > _hillBest[_hill])
            _hillBest[_hill] = _distance;
        _best = MathF.Max(_best, _distance);
        if (_distance >= H.K)
        {
            Sound.Play(Sfx.Bonus, 0, 0.6f);
            Sound.Play(Sfx.Bell, 0.2f, 0.5f);
        }
    }

    private void UpdateLanded()
    {
        float ang = SlopeAngle(_x);
        float slopeAcc = Grav * MathF.Sin(ang) - (_fell ? 6 : 2.5f) - 0.002f * _v * _v;
        if (_phaseTime < Dt * 1.5f)
            _v = MathF.Sqrt(_vx * _vx + _vy * _vy) * 0.8f;
        _v = MathF.Max(0, _v + slopeAcc * Dt);
        _x = MathF.Min(MaxX - 1, _x + _v * MathF.Cos(ang) * Dt);
        _y = Surface(_x);
        if (_fell)
            _spin += _v * 0.25f * Dt;
        if (_v > 3 && Tick % 2 == 0)
            Fx.Spark(Sx(_x), Sy(_y), -_v * 3, -Rand(10, 30), Pal.White, 0.5f, 2, 80, false);
        Sound.Loop(LoopSfx.Wind, _v > 2, -0.6f + _v / 50, 0.2f);
        if (_phaseTime > 1.4f)
        {
            _phase = Phase.Results;
            _phaseTime = 0;
            Sound.Play(Sfx.Card, 0, 0.6f);
        }
    }

    private void UpdateResults()
    {
        float ang = SlopeAngle(_x);
        _v = MathF.Max(0, _v + (Grav * MathF.Sin(ang) - 3) * Dt);
        _x = MathF.Min(MaxX - 1, _x + _v * MathF.Cos(ang) * Dt);
        _y = Surface(_x);
        // Judges' cards go up one at a time.
        for (int i = 0; i < 5; i++)
        {
            float t = 0.5f + i * 0.3f;
            if (_phaseTime >= t && _phaseTime - Dt < t)
                Sound.Play(Sfx.Card, i * 0.1f, 0.6f);
        }
        if (_phaseTime >= 2.2f && _phaseTime - Dt < 2.2f)
        {
            int pts = (int)MathF.Round(_total);
            AddScore(pts, 320, 120, Pal.Gold);
            Sound.Play(_total >= 120 ? Sfx.Win : Sfx.Coin, 0, 0.6f);
        }
        if (_phaseTime > 5.5f || (_phaseTime > 2.4f && In.FirePressed))
        {
            _jump++;
            if (_jump >= JumpsPerHill)
            {
                if (_hill + 1 >= Hills.Length)
                {
                    EndGame(true, $"Longest jump {_best:0.0} m");
                    return;
                }
                _jump = 0;
                _hill++;
                BuildHill(H);
                Sound.Play(Sfx.LevelUp);
            }
            NewJump();
        }
    }

    private void UpdateCamera()
    {
        float tx = _x + (_phase == Phase.Flight ? 12 : 8), ty = _y + 4;
        if (_phase == Phase.Ready)
        {
            _camX = tx;
            _camY = ty;
            return;
        }
        _camX += (tx - _camX) * MathF.Min(1, Dt * 5);
        _camY += (ty - _camY) * MathF.Min(1, Dt * 5);
    }

    private float Sx(float x) => 250 + (x - _camX) * S;
    private float Sy(float y) => 200 + (y - _camY) * S;

    // ------------------------------------------------------------------ drawing

    private static readonly Vector2[] Quad = new Vector2[4];

    public override void Draw(Gfx g)
    {
        // Sky and mountains.
        g.GradientV(0, 0, 640, 360, new Color(70, 130, 220), new Color(200, 225, 250));
        g.Glow(520, 70, 120, Pal.Yellow, 0.35f);
        g.Circle(520, 70, 16, new Color(255, 250, 220));
        float px = _camX * S;
        DrawMountains(g, Screen.Bounds, 215 - _camY * 0.4f, px * 0.05f, 1, new Color(160, 178, 210), 3);
        DrawMountains(g, Screen.Bounds, 265 - _camY * 0.8f, px * 0.12f, 0.8f, new Color(110, 135, 178), 8);

        // Trees behind the hill.
        for (int i = -10; i < 60; i++)
        {
            float wx = i * 9 + ((i * 37) % 5);
            float sx = Sx(wx);
            if (sx < -20 || sx > 660 || wx > MaxX)
                continue;
            float wy = wx < 0 ? Ground(wx) : Surface(wx);
            DrawTree(g, sx, Sy(wy) - 2, 1 + ((i * 13) % 4) * 0.15f);
        }

        // Natural slope under the in-run, then the landing hill.
        const float stepPx = 6;
        for (float sx = 0; sx < 640; sx += stepPx)
        {
            float x0 = _camX + (sx - 250) / S, x1 = _camX + (sx + stepPx - 250) / S;
            float y0 = x0 < 0 ? Ground(x0) : Surface(x0), y1 = x1 < 0 ? Ground(x1) : Surface(x1);
            float s0 = Sy(y0), s1 = Sy(y1);
            if (s0 > 370 && s1 > 370)
                continue;
            Quad[0] = new Vector2(sx, s0);
            Quad[1] = new Vector2(sx + stepPx + 0.5f, s1);
            Quad[2] = new Vector2(sx + stepPx + 0.5f, 370);
            Quad[3] = new Vector2(sx, 370);
            g.Polygon(Quad, new Color(236, 242, 252));
            g.Line(sx, s0, sx + stepPx + 0.5f, s1, 2, Pal.White);
            Quad[0] = new Vector2(sx, s0 + 14);
            Quad[1] = new Vector2(sx + stepPx + 0.5f, s1 + 14);
            g.Polygon(Quad, new Color(205, 220, 240));
        }

        DrawInrun(g);
        DrawMarkers(g);
        DrawCrowd(g);
        DrawSkierNow(g);
        DrawHud(g);
    }

    private static void DrawMountains(Gfx g, RectF r, float baseY, float scroll, float scale, Color col, int seed)
    {
        float s = r.H / 360f;
        float spacing = 110 * scale * s;
        float off = Backdrops.Mod(scroll * s, spacing);
        int first = (int)MathF.Floor(scroll * s / spacing);
        for (int i = -1; i < r.W / spacing + 2; i++)
        {
            int id = first + i;
            uint h = (uint)(id * 2654435761u + seed * 97u);
            float cx = r.X + i * spacing - off + ((h >> 5) % 40) * s * scale;
            float ht = (60 + (h >> 9) % 70) * scale * s;
            float w = ht * (1.1f + ((h >> 13) % 5) * 0.12f);
            float top = r.Y + baseY * s - ht;
            g.Triangle(new Vector2(cx - w, r.Y + baseY * s), new Vector2(cx + w, r.Y + baseY * s), new Vector2(cx, top), col);
            float c = 0.32f;
            g.Triangle(new Vector2(cx - w * c, top + ht * c), new Vector2(cx + w * c, top + ht * c), new Vector2(cx, top), Pal.Lighten(col, 0.75f));
            g.Triangle(new Vector2(cx - w * c, top + ht * c), new Vector2(cx - w * c * 0.4f, top + ht * c * 1.3f), new Vector2(cx - w * c * 0.1f, top + ht * c), Pal.Lighten(col, 0.75f));
            g.Triangle(new Vector2(cx + w * c * 0.2f, top + ht * c), new Vector2(cx + w * c * 0.6f, top + ht * c * 1.25f), new Vector2(cx + w * c, top + ht * c), Pal.Lighten(col, 0.75f));
        }
        g.Rect(r.X, r.Y + baseY * s, r.W, r.H * 2, col);
    }

    private static void DrawTree(Gfx g, float x, float baseY, float s)
    {
        var dark = new Color(30, 80, 60);
        g.Rect(x - 1.2f * s, baseY - 4 * s, 3.8f * s, 5 * s, new Color(80, 50, 30));
        for (int k = 0; k < 3; k++)
        {
            float y = baseY - 3 * s - k * 6 * s;
            float w = (9 - k * 2.2f) * s;
            g.Triangle(new Vector2(x - w, y), new Vector2(x + w, y), new Vector2(x, y - 9 * s), dark);
            g.Triangle(new Vector2(x - w * 0.6f, y - 2 * s), new Vector2(x + w * 0.2f, y - 2 * s), new Vector2(x - w * 0.1f, y - 7 * s), Pal.White * 0.8f);
        }
    }

    private void DrawInrun(Gfx g)
    {
        // Supports.
        for (float x = InrunStartX; x <= 0; x += 10)
        {
            float top = Surface(x), bot = Ground(x);
            g.Rect(Sx(x) - 1.5f, Sy(top), 3, (bot - top) * S + 4, new Color(120, 120, 135));
        }
        // Deck.
        for (float x = InrunStartX; x < 0; x += 2)
        {
            float x1 = MathF.Min(0, x + 2);
            var a = new Vector2(Sx(x), Sy(Surface(x)));
            var b = new Vector2(Sx(x1), Sy(Surface(x1)));
            g.Line(a + new Vector2(0, 3), b + new Vector2(0, 3), 6, new Color(90, 90, 110));
            g.Line(a, b, 2.5f, Pal.White);
            g.Line(a + new Vector2(0, 1.5f), b + new Vector2(0, 1.5f), 0.8f, new Color(120, 160, 220));
        }
        // Start hut and the edge marker.
        float hx = Sx(InrunStartX - 3), hy = Sy(Surface(InrunStartX));
        g.Rect(hx - 10, hy - 20, 18, 22, new Color(150, 40, 40));
        g.Triangle(new Vector2(hx - 13, hy - 20), new Vector2(hx + 11, hy - 20), new Vector2(hx - 1, hy - 30), new Color(90, 30, 30));
        g.Rect(Sx(0) - 1, Sy(0) - 8, 2, 8, Pal.Red);
        if (_phase == Phase.Inrun && _x > -30)
            g.Glow(Sx(0), Sy(0), 14, Pal.Red, 0.6f + 0.3f * MathF.Sin(Time * 20));
    }

    private void DrawMarkers(Gfx g)
    {
        // Distance markers every 10 m, the K-point and hill size lines, and wind flags.
        for (int d = 20; d < H.Hs + 30; d += 10)
        {
            float x = XForArc(d);
            float sx = Sx(x), sy = Sy(Surface(x));
            if (sx < -20 || sx > 660)
                continue;
            g.Line(sx - 2, sy + 1, sx + 6, sy + 5, 1.5f, new Color(60, 100, 200) * 0.7f);
            g.Text(d.ToString(), sx, sy + 8, 1, new Color(40, 70, 160), Align.Center);
        }
        DrawLine(g, H.K, Pal.Red, "K");
        DrawLine(g, H.Hs, Pal.Green, "HS");
        if (_hillBest[_hill] > 0)
            DrawLine(g, _hillBest[_hill], Pal.Gold, "BEST");

        for (int i = 1; i < 8; i++)
        {
            float x = H.K * 0.2f * i;
            float sx = Sx(x), sy = Sy(Surface(x));
            if (sx < -20 || sx > 660)
                continue;
            DrawFlag(g, sx, sy, _wind, Time + i);
        }
    }

    private void DrawLine(Gfx g, float d, Color c, string label)
    {
        float x = XForArc(d);
        float sx = Sx(x), sy = Sy(Surface(x));
        if (sx < -30 || sx > 670)
            return;
        float ang = SlopeAngle(x);
        var along = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
        var across = new Vector2(-along.Y, along.X);
        g.GlowLine(new Vector2(sx, sy) + across * 1, new Vector2(sx, sy) + across * 14, 1.5f, c);
        g.Text(label, sx + 4, sy - 14, 1, c);
    }

    private static void DrawFlag(Gfx g, float x, float y, float wind, float t)
    {
        g.Rect(x - 0.6f, y - 16, 1.2f, 16, Pal.DarkGrey);
        float dir = wind >= 0 ? -1 : 1;     // head wind blows back towards the jumpers
        float len = 4 + MathF.Min(8, MathF.Abs(wind) * 4);
        float wave = MathF.Sin(t * 8) * 1.5f;
        var c = wind >= 0 ? Pal.Green : Pal.Red;
        g.Triangle(new Vector2(x, y - 16), new Vector2(x, y - 10), new Vector2(x + dir * len, y - 13 + wave), c);
    }

    private void DrawCrowd(Gfx g)
    {
        float x0 = H.K * 1.55f, x1 = H.K * 2.05f;
        if (Sx(x0) > 660)
            return;
        float y = Surface(x0);
        float sy = Sy(y);
        float sxa = Sx(x0), sxb = Sx(x1);
        g.Rect(sxa, sy - 14, sxb - sxa, 3, Pal.Red);
        for (int r = 0; r < 3; r++)
            for (float sx = sxa + 3; sx < sxb; sx += 6)
            {
                int k = (int)(sx * 7 + r * 13) & 7;
                float bob = MathF.Abs(MathF.Sin(Time * 6 + k + r)) * 2 * (_phase >= Phase.Landed ? 1.5f : 0.4f);
                var col = Pal.Oric[1 + k % 7];
                float yy = sy - 18 - r * 6 - bob;
                g.Rect(sx - 2, yy, 4, 6, col);
                g.Circle(sx, yy - 2, 2, Pal.Skin);
            }
        g.Rect(sxa, sy - 11, sxb - sxa, 8, new Color(30, 40, 120));
        g.Text("CASCADE 50", (sxa + sxb) / 2, sy - 10, 1, Pal.White, Align.Center);
    }

    private void DrawSkierNow(Gfx g)
    {
        var p = new Vector2(Sx(_x), Sy(_y));
        if (_phase == Phase.Flight)
        {
            float h = MathF.Max(0, Surface(_x) - _y);
            float a = MathF.Max(0, 1 - h / 25);
            g.Ellipse(Sx(_x + h * 0.3f), Sy(Surface(_x + h * 0.3f)) + 1, 12 * (0.6f + a * 0.4f), 3, new Color(80, 100, 150) * (0.5f * a));
        }
        float slope = SlopeAngle(_x);
        switch (_phase)
        {
            case Phase.Ready:
            case Phase.Inrun:
                DrawSkier(g, p, slope, 0, _tuck > 0.5f ? Pose.Tuck : Pose.Land, 1, Time);
                break;
            case Phase.Flight:
            {
                float dir = MathF.Atan2(_vy, _vx);
                if (_fell)
                    DrawSkier(g, p, dir + _spin, 0, Pose.Fall, 1, Time);
                else
                    DrawSkier(g, p, dir * 0.6f + 0.12f, _lean, _telemarkH >= 0 ? Pose.Telemark : Pose.Flight, 1, Time);
                break;
            }
            default:
                DrawSkier(g, p, _fell ? slope + _spin : slope, 0, _fell ? Pose.Fall : _landText == "TELEMARK!" && _phaseTime < 1.2f ? Pose.Telemark : Pose.Land, 1, Time);
                break;
        }
    }

    private static void DrawSkier(Gfx g, Vector2 p, float ang, float lean, Pose pose, float s, float time)
    {
        float k = S * 3f * s;      // pixels per metre for the figure
        var dir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
        var up = new Vector2(dir.Y, -dir.X);
        var suit = new Color(230, 40, 50);
        var suit2 = new Color(255, 210, 40);
        var ski = new Color(30, 40, 90);
        float lw = 3f * s;

        if (pose == Pose.Flight)
        {
            // V-style: skis spread, body laid forward over them.
            var d2 = new Vector2(MathF.Cos(ang - 0.18f), MathF.Sin(ang - 0.18f));
            g.Line(p - d2 * 0.8f * k, p + d2 * 1.7f * k, lw * 0.9f, Pal.Darken(ski, -0.2f));
            g.Line(p - dir * 0.8f * k + up * 0.5f, p + dir * 1.7f * k + up * 0.5f, lw, ski);
            float rel = 0.42f - lean * 0.28f;
            var bd = new Vector2(MathF.Cos(ang - rel), MathF.Sin(ang - rel));
            var hip = p + bd * 0.8f * k;
            var head = p + bd * 1.6f * k;
            g.Line(p, hip, 4f * s, suit);
            g.Line(hip, head, 5f * s, suit);
            g.Line(hip + up * 0.3f, head + up * 0.3f, 1.6f * s, suit2);
            g.Line(hip - bd * 0.1f * k, hip - bd * 0.6f * k + up * 0.15f * k, 2.2f * s, suit2);
            g.Circle(head.X, head.Y, 3.3f * s, suit2);
            g.Circle(head.X + bd.X * 2f * s, head.Y + bd.Y * 1.2f * s, 1.6f * s, Pal.Black);
            return;
        }
        if (pose == Pose.Fall)
        {
            g.Line(p - dir * 1.2f * k, p + dir * 1.2f * k, lw, ski);
            var d2 = new Vector2(MathF.Cos(ang + 1.3f), MathF.Sin(ang + 1.3f));
            g.Line(p - d2 * 1.1f * k, p + d2 * 1.1f * k, lw, ski);
            var head = p + up * 1.2f * k;
            g.Line(p, head, 5f * s, suit);
            g.Line(p + up * 0.6f * k - dir * 0.6f * k, p + up * 0.6f * k + dir * 0.6f * k, 2.2f * s, suit2);
            g.Circle(head.X, head.Y, 3.3f * s, suit2);
            return;
        }
        // On the snow: skis along the slope.
        if (pose == Pose.Telemark)
        {
            g.Line(p - dir * 0.6f * k, p + dir * 1.9f * k, lw, ski);
            g.Line(p - dir * 1.5f * k + up * 0.3f, p + dir * 1.0f * k + up * 0.3f, lw, Pal.Darken(ski, -0.2f));
            var hip = p + up * 0.75f * k;
            g.Line(p + dir * 0.5f * k, p + dir * 0.4f * k + up * 0.4f * k, 3.6f * s, suit);
            g.Line(p + dir * 0.4f * k + up * 0.4f * k, hip, 3.8f * s, suit);
            g.Line(p - dir * 0.5f * k, p - dir * 0.2f * k + up * 0.35f * k, 3.6f * s, suit);
            g.Line(p - dir * 0.2f * k + up * 0.35f * k, hip, 3.8f * s, suit);
            var head = hip + up * 0.85f * k;
            g.Line(hip, head, 5f * s, suit);
            // Arms out for balance.
            var sh = hip + up * 0.6f * k;
            g.Line(sh, sh + dir * 0.6f * k + up * 0.25f * k, 2.2f * s, suit2);
            g.Line(sh, sh - dir * 0.6f * k + up * 0.25f * k, 2.2f * s, suit2);
            g.Circle(head.X, head.Y, 3.3f * s, suit2);
            return;
        }
        g.Line(p - dir * 1.0f * k, p + dir * 1.5f * k, lw, ski);
        g.Circle(p.X + dir.X * 1.5f * k + up.X * 0.6f, p.Y + dir.Y * 1.5f * k + up.Y * 0.6f, lw * 0.6f, ski);
        if (pose == Pose.Tuck)
        {
            var knee = p + dir * 0.35f * k + up * 0.45f * k;
            var hip = p - dir * 0.2f * k + up * 0.55f * k;
            var sh = p + dir * 0.45f * k + up * 0.75f * k;
            g.Line(p, knee, 3.8f * s, suit);
            g.Line(knee, hip, 4f * s, suit);
            g.Line(hip, sh, 3.6f * s, suit);
            g.Line(sh, hip - dir * 0.3f * k + up * 0.2f * k, 2.2f * s, suit2);
            var head = sh + dir * 0.25f * k + up * 0.12f * k;
            g.Circle(head.X, head.Y, 3.3f * s, suit2);
            g.Circle(head.X + dir.X * 2f * s, head.Y + dir.Y * 1.2f * s, 1.6f * s, Pal.Black);
        }
        else
        {
            var knee = p + dir * 0.15f * k + up * 0.45f * k;
            var hip = p + up * 0.85f * k;
            var head = hip + up * 0.9f * k + dir * 0.1f * k;
            g.Line(p, knee, 3.8f * s, suit);
            g.Line(knee, hip, 4f * s, suit);
            g.Line(hip, head, 5f * s, suit);
            var sh = hip + up * 0.6f * k;
            g.Line(sh, sh + dir * 0.4f * k - up * 0.35f * k, 2.2f * s, suit2);
            g.Circle(head.X, head.Y, 3.3f * s, suit2);
        }
    }

    private void DrawHud(Gfx g)
    {
        // Wind.
        var wp = new RectF(8, 28, 128, 40);
        g.Panel(wp, Pal.Panel * 0.85f, Pal.Sky * 0.6f, 6);
        g.Text("WIND", wp.X + 8, wp.Y + 6, 1, Pal.LightGrey);
        string ws = (_wind >= 0 ? "+" : "") + _wind.ToString("0.0") + " M/S";
        g.Text(ws, wp.X + 8, wp.Y + 18, 1.5f, _wind >= 0 ? Pal.Lime : Pal.Red);
        float ax = wp.Right - 20, ay = wp.Y + 20, adir = _wind >= 0 ? -1 : 1;
        g.Line(ax - 9 * adir, ay, ax + 9 * adir, ay, 2, Pal.White);
        g.Triangle(new Vector2(ax + 9 * adir, ay - 4), new Vector2(ax + 9 * adir, ay + 4), new Vector2(ax + 14 * adir, ay), Pal.White);

        // Speed / distance.
        var sp = new RectF(504, 28, 128, 40);
        g.Panel(sp, Pal.Panel * 0.85f, Pal.Sky * 0.6f, 6);
        if (_phase is Phase.Ready or Phase.Inrun)
        {
            g.Text("SPEED KM/H", sp.X + 8, sp.Y + 6, 1, Pal.LightGrey);
            g.Text((_v * 3.6f).ToString("0.0"), sp.X + 8, sp.Y + 18, 1.5f, Pal.White);
        }
        else
        {
            g.Text("DISTANCE M", sp.X + 8, sp.Y + 6, 1, Pal.LightGrey);
            float d = _phase == Phase.Flight ? MathF.Max(0, ArcAt(_x)) : _distance;
            g.Text(d.ToString("0.0"), sp.X + 8, sp.Y + 18, 1.5f, d >= H.K ? Pal.Gold : Pal.White);
        }

        if (_phase == Phase.Ready)
        {
            g.TextShadow(H.Name, 320, 90, 3, Pal.White, Align.Center);
            g.TextShadow($"K-POINT {H.K:0} M   JUMP {_jump + 1} OF {JumpsPerHill}", 320, 124, 1.5f, Pal.Yellow, Align.Center);
            g.TextShadow("HOLD DOWN TO TUCK", 320, 300, 1.5f, Pal.White, Align.Center);
        }

        // Take-off timing meter.
        if (_phase == Phase.Inrun && _x > -30)
        {
            var m = new RectF(220, 300, 200, 18);
            g.Panel(m, Pal.Panel * 0.85f, Pal.White * 0.6f, 5);
            float zx = m.Right - 30;
            g.Rect(zx - 10, m.Y + 3, 20, m.H - 6, Pal.Green * 0.7f);
            g.Rect(zx - 3, m.Y + 3, 6, m.H - 6, Pal.Lime);
            float mx = zx + _x / 30 * (zx - m.X - 6);
            g.Rect(mx - 1.5f, m.Y - 3, 3, m.H + 6, Pal.White);
            g.TextShadow("PRESS FIRE AT THE EDGE!", 320, 284, 1.5f, Pal.Yellow, Align.Center);
        }

        // Balance meter in the air.
        if (_phase == Phase.Flight && !_fell)
        {
            var m = new RectF(220, 318, 200, 20);
            g.Panel(m, Pal.Panel * 0.85f, Pal.White * 0.6f, 5);
            float cx = m.CenterX, half = m.W / 2 - 6;
            g.Rect(cx - half * 0.35f, m.Y + 4, half * 0.7f, m.H - 8, Pal.Green * 0.6f);
            g.Rect(m.X + 6, m.Y + 4, half * 0.3f, m.H - 8, Pal.Red * 0.5f);
            g.Rect(m.Right - 6 - half * 0.3f, m.Y + 4, half * 0.3f, m.H - 8, Pal.Red * 0.5f);
            float mx = cx + MathF2.Clamp(_lean / 1.7f, -1, 1) * half;
            g.Rect(mx - 2, m.Y - 3, 4, m.H + 6, Pal.White);
            g.Text("BACK", m.X - 4, m.Y + 6, 1, Pal.LightGrey, Align.Right);
            g.Text("FWD", m.Right + 4, m.Y + 6, 1, Pal.LightGrey);
            float h = Surface(_x) - _y;
            if (_telemarkH < 0 && h < 5 && _flightTime > 1)
                g.TextShadow("LAND! PRESS FIRE", 320, 296, 1.5f, (int)(Time * 6) % 2 == 0 ? Pal.Yellow : Pal.White, Align.Center);
            else if (_takeoffText != null && _flightTime < 1.2f)
                g.TextShadow(_takeoffText, 320, 80, 2, _takeoff > 0.8f ? Pal.Lime : Pal.Orange, Align.Center);
        }

        if (_phase == Phase.Landed)
        {
            g.TextShadow(_distance.ToString("0.0") + " M", 320, 80, 4, _distance >= H.K ? Pal.Gold : Pal.White, Align.Center);
            if (_landText != null)
                g.TextShadow(_landText, 320, 118, 2, _fell ? Pal.Red : _landText == "TELEMARK!" ? Pal.Lime : Pal.Orange, Align.Center);
        }
        if (_phase == Phase.Results)
            DrawResults(g);
    }

    private void DrawResults(Gfx g)
    {
        var p = new RectF(110, 72, 420, 250);
        g.Panel(p, Pal.Panel * 0.94f, Pal.Sky, 10);
        g.TextShadow(_distance.ToString("0.0") + " M", p.CenterX, p.Y + 12, 3, _distance >= H.K ? Pal.Gold : Pal.White, Align.Center);
        if (_landText != null)
            g.Text(_landText, p.CenterX, p.Y + 42, 1.5f, _fell ? Pal.Red : _landText == "TELEMARK!" ? Pal.Lime : Pal.Orange, Align.Center);

        // Five judges holding up their cards.
        for (int i = 0; i < 5; i++)
        {
            float jx = p.X + 52 + i * 79, jy = p.Y + 150;
            float shown = MathF2.Clamp((_phaseTime - 0.5f - i * 0.3f) * 5, 0, 1);
            // Judge.
            g.Rect(jx - 9, jy, 18, 22, new Color(40, 50, 90));
            g.Circle(jx, jy - 6, 7, Pal.Skin);
            g.Rect(jx - 8, jy - 15, 16, 4, Pal.Darken(Pal.Red, 0.2f));
            g.Rect(jx - 5, jy - 19, 10, 5, Pal.Darken(Pal.Red, 0.2f));
            g.Circle(jx - 2.5f, jy - 6, 1, Pal.Black);
            g.Circle(jx + 2.5f, jy - 6, 1, Pal.Black);
            if (shown <= 0)
                continue;
            float cy = jy - 18 - 34 * MathF2.EaseOut(shown);
            g.Line(jx + 8, jy + 6, jx + 6, cy + 24, 3, Pal.Skin);
            bool dropped = i == _judgeHi || i == _judgeLo;
            var card = new RectF(jx - 26, cy - 4, 52, 28);
            g.RoundRect(card.Offset(1, 2), 3, Color.Black * 0.4f);
            g.RoundRect(card, 3, dropped ? new Color(170, 170, 180) : Pal.White);
            g.Text(_judges[i].ToString("0.0"), card.CenterX, card.Y + 7, 2, dropped ? Pal.Grey : Pal.Black, Align.Center);
            if (dropped && _phaseTime > 2.2f)
                g.Line(card.X + 4, card.Bottom - 4, card.Right - 4, card.Y + 4, 1.5f, Pal.Red);
        }

        if (_phaseTime > 2.2f)
        {
            g.Text($"DISTANCE {_distPoints:0.0}  +  STYLE {_style:0.0}", p.CenterX, p.Y + 196, 1.5f, Pal.LightGrey, Align.Center);
            g.TextShadow($"{_total:0.0} POINTS", p.CenterX, p.Y + 218, 2.5f, Pal.Gold, Align.Center);
        }
    }

    // ------------------------------------------------------------------ icon

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        float s = r.H / 70f;
        g.GradientV(r.X, r.Y, r.W, r.H, new Color(70, 130, 220), new Color(210, 230, 250));
        g.Glow(r.Right - 20 * s, r.Y + 14 * s, 30 * s, Pal.Yellow, 0.4f);
        DrawMountains(g, r, 200, time * 4, 0.6f, new Color(150, 170, 205), 3);
        // Landing slope, from upper left to lower right.
        Quad[0] = new Vector2(r.X, r.Y + r.H * 0.55f);
        Quad[1] = new Vector2(r.Right, r.Bottom - 6 * s);
        Quad[2] = new Vector2(r.Right, r.Bottom);
        Quad[3] = new Vector2(r.X, r.Bottom);
        g.Polygon(Quad, new Color(236, 242, 252));
        g.Line(Quad[0], Quad[1], 1.5f * s, Pal.White);
        for (int i = 0; i < 5; i++)
        {
            float x = r.X + r.W * (0.25f + i * 0.17f);
            float y = r.Y + r.H * 0.55f + (x - r.X) / r.W * (r.H * 0.45f - 6 * s);
            DrawTree(g, x - 8 * s, y - 2 * s, 0.6f * s);
            DrawFlag(g, x, y, 1.2f, time + i);
        }
        // K-line.
        float kx = r.X + r.W * 0.72f, ky = r.Y + r.H * 0.55f + 0.72f * (r.H * 0.45f - 6 * s);
        g.GlowLine(new Vector2(kx, ky), new Vector2(kx - 3 * s, ky + 8 * s), 1.2f * s, Pal.Red);
        // In-run.
        g.Line(r.X - 2, r.Y + 10 * s, r.X + r.W * 0.18f, r.Y + r.H * 0.42f, 3 * s, new Color(90, 90, 110));
        g.Line(r.X - 2, r.Y + 8.5f * s, r.X + r.W * 0.18f, r.Y + r.H * 0.40f, 1.5f * s, Pal.White);
        // Jumper soaring in V-style.
        float t = (time * 0.3f) % 1;
        var p = new Vector2(r.X + r.W * (0.25f + 0.5f * t), r.Y + r.H * (0.38f + 0.12f * t) + MathF.Sin(time * 2) * 2 * s);
        g.Glow(p, 18 * s, Pal.White, 0.3f);
        DrawSkier(g, p, 0.25f, 0.1f * MathF.Sin(time * 3), Pose.Flight, 0.42f * s, time);
    }

    // ------------------------------------------------------------------ autopilot

    private float _autoJitter;

    public override void AutoPlay(Controls c)
    {
        switch (_phase)
        {
            case Phase.Ready:
            case Phase.Inrun:
            {
                c.SetDirections(0, 1);
                float next = _x + _v * Dt;
                if (_phase == Phase.Inrun && next > -0.6f - _autoJitter && _x < 0)
                {
                    c.FirePressed = true;
                    _autoJitter = Rand(-0.5f, 2.5f);
                }
                break;
            }
            case Phase.Flight:
            {
                float want = MathF2.Clamp(-_lean * 2.4f - _leanV * 0.9f, -1, 1);
                c.SetDirections(0, want);
                float h = Surface(_x) - _y;
                if (h < 2.2f && _flightTime > 0.5f && _telemarkH < 0)
                    c.FirePressed = true;
                break;
            }
            case Phase.Results:
                if (_phaseTime > 3.4f)
                    c.FirePressed = true;
                break;
        }
    }
}
