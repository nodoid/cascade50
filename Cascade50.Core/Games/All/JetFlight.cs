using System;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 19 Jet Flight: fly a jet through a twisting cavern full of stalactites and on over a city of
/// skyscrapers. Hold to climb, let go to sink; grab fuel canisters before the tank runs dry.
/// </summary>
public sealed class JetFlight : MiniGame
{
    public override int Number => 19;
    public override string Title => "Jet Flight";
    public override Category Category => Category.Skill;
    public override string Tagline => "Thread a jet through twisting caverns and city towers.";
    public override Color Accent => Pal.Sky;

    public override string[] HowToPlay =>
    [
        "Hold to climb, let go to sink. The jet has momentum, so feather the throttle.",
        "Don't touch the rock, the spikes or the towers. Fuel drains all the time: fly through canisters to refill.",
        "Score by distance, plus 100 per canister. You have three jets, and the flight gets faster and tighter.",
    ];

    public override string[] DesktopControls => ["Hold SPACE (or UP, or the mouse button) to climb."];
    public override string[] TouchControls => ["Hold UP (or anywhere on the screen) to climb."];
    public override Pad Pad => Pad.Fire;
    public override string FireLabel => "UP";

    private const float ColW = 6, ShipX = 150, Top = 24, Bottom = 356;
    private const int Ring = 256;
    private const float SegmentLength = 4500;

    private struct Column
    {
        public float Top, Bottom;
        public bool City, Edge, Antenna;
        public int Seed;
    }

    private struct Canister
    {
        public float X, Y;
        public bool Taken;
    }

    private readonly Column[] _cols = new Column[Ring];
    private readonly Canister[] _cans = new Canister[8];
    private int _generated;
    private float _genCentre, _genGap, _genVel, _genTop, _genBottom, _genRoof;
    private int _genBuildingLeft, _genSeed, _spikeLeft, _spikeLen, _spikeW;
    private bool _spikeTop;
    private float _nextCan;

    private float _scroll, _speed, _y, _vy, _fuel, _invuln, _dead, _bonus, _banner;
    private bool _thrust;
    private int _segment;

    protected override void Start()
    {
        Lives = 3;
        Level = 1;
        _scroll = 0;
        _speed = 105;
        _y = 190;
        _vy = 0;
        _fuel = 1;
        _invuln = 1.5f;
        _bonus = 0;
        _generated = 0;
        _genCentre = 190;
        _genGap = 240;
        _genVel = 0;
        _genTop = 70;
        _genBottom = 310;
        _spikeLeft = 0;
        _nextCan = 500;
        _segment = 0;
        _banner = 2;
        for (int i = 0; i < _cans.Length; i++)
            _cans[i] = new Canister { X = -1000, Taken = true };
        Generate();
    }

    // ------------------------------------------------------------------ terrain

    private ref Column Col(int i) => ref _cols[((i % Ring) + Ring) % Ring];

    private void Generate()
    {
        int need = (int)((_scroll + 720) / ColW);
        while (_generated <= need)
        {
            int i = _generated++;
            float x = i * ColW;
            int seg = (int)(x / SegmentLength);
            bool city = seg % 2 == 1;
            float into = x - seg * SegmentLength;
            ref var c = ref Col(i);
            c = default;
            float tightness = MathF.Min(1, x / 24000f);

            if (!city)
            {
                float gapTarget = MathF.Max(118, 230 - tightness * 110);
                _genGap = MathF2.Approach(_genGap, gapTarget, 0.6f);
                _genVel = MathF2.Clamp(_genVel + Rand(-0.45f, 0.45f), -2.2f - tightness * 1.5f, 2.2f + tightness * 1.5f);
                _genCentre += _genVel;
                float lo = Top + 12 + _genGap / 2, hi = Bottom - 12 - _genGap / 2;
                if (_genCentre < lo) { _genCentre = lo; _genVel = MathF.Abs(_genVel); }
                if (_genCentre > hi) { _genCentre = hi; _genVel = -MathF.Abs(_genVel); }
                float wantTop = _genCentre - _genGap / 2 + Rand(-2, 2);
                float wantBottom = _genCentre + _genGap / 2 + Rand(-2, 2);
                // Open out towards the sky before the city.
                if (into > SegmentLength - 320)
                {
                    float k = (into - (SegmentLength - 320)) / 320;
                    wantTop = MathF2.Lerp(wantTop, Top, k);
                    wantBottom = MathF2.Lerp(wantBottom, 300, k);
                }
                _genTop = MathF2.Approach(_genTop, wantTop, 4);
                _genBottom = MathF2.Approach(_genBottom, wantBottom, 4);
                c.Top = _genTop;
                c.Bottom = _genBottom;

                // Stalactites and stalagmites.
                if (_spikeLeft <= 0 && into > 200 && into < SegmentLength - 200 && Chance(0.035f + tightness * 0.03f))
                {
                    _spikeTop = Chance(0.5f);
                    _spikeW = RandInt(3, 6);
                    _spikeLeft = _spikeW * 2 + 1;
                    _spikeLen = (int)MathF.Min(Rand(22, 48 + tightness * 20), _genGap - 80);
                }
                if (_spikeLeft > 0)
                {
                    int k = _spikeLeft - _spikeW - 1;
                    float len = _spikeLen * MathF.Max(0, 1 - MathF.Abs(k) / (float)(_spikeW + 1));
                    if (_spikeTop)
                        c.Top += len;
                    else
                        c.Bottom -= len;
                    _spikeLeft--;
                }
            }
            else
            {
                // Skyscrapers under open sky.
                _genTop = MathF2.Approach(_genTop, Top, 5);
                c.Top = _genTop;
                c.City = true;
                if (_genBuildingLeft <= 0)
                {
                    _genBuildingLeft = RandInt(5, 11);
                    float minRoof = 110 - tightness * 40;
                    _genRoof = into < 200 ? MathF.Max(_genBottom, 280) : Rand(minRoof, 320);
                    _genSeed = RandInt(0, 1000);
                    c.Edge = true;
                    c.Antenna = _genRoof < 200 && Chance(0.6f);
                    _genBottom = _genRoof;
                    // Keep a gap below the ceiling while it is still coming up.
                    _genRoof = MathF.Max(_genRoof, c.Top + 110);
                }
                _genBuildingLeft--;
                c.Bottom = _genRoof;
                c.Seed = _genSeed;
                _genCentre = (c.Top + c.Bottom) / 2;
                if (into > SegmentLength - 360)
                {
                    // Lead back into the cave at a sensible height.
                    _genBottom = c.Bottom;
                    _genGap = MathF.Min(300, c.Bottom - c.Top);
                }
            }

            // Fuel canisters.
            if (x >= _nextCan)
            {
                _nextCan = x + Rand(420, 700) + tightness * 300;
                for (int k = 0; k < _cans.Length; k++)
                    if (_cans[k].Taken || _cans[k].X < _scroll - 100)
                    {
                        float top = c.Top + 26, bottom = c.Bottom - 26;
                        _cans[k] = new Canister { X = x, Y = bottom > top ? Rand(top, bottom) : (c.Top + c.Bottom) / 2 };
                        break;
                    }
            }
        }
    }

    private void Bounds(float worldX, out float top, out float bottom)
    {
        float f = worldX / ColW;
        int i = (int)MathF.Floor(f);
        float k = f - i;
        ref var a = ref Col(i);
        ref var b = ref Col(i + 1);
        if (a.City || b.City)
        {
            top = MathF.Max(a.Top, b.Top);
            bottom = MathF.Min(a.Bottom, b.Bottom);
            return;
        }
        top = MathF2.Lerp(a.Top, b.Top, k);
        bottom = MathF2.Lerp(a.Bottom, b.Bottom, k);
    }

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        if (_banner > 0)
            _banner -= Dt;
        if (_dead > 0)
        {
            _dead -= Dt;
            if (_dead <= 0)
                Respawn();
            return;
        }
        if (_invuln > 0)
            _invuln -= Dt;

        _speed = MathF.Min(_speed + 2.2f * Dt, 270);
        _scroll += _speed * Dt;
        Generate();

        int seg = (int)((_scroll + ShipX) / SegmentLength);
        if (seg != _segment)
        {
            _segment = seg;
            Level = seg + 1;
            _banner = 2.5f;
            Sound.Play(Sfx.LevelUp);
        }

        // Flight.
        _thrust = (In.Fire || In.Up || In.PointerDown) && _fuel > 0;
        _vy += (_thrust ? -430 : 300) * Dt;
        _vy = MathF2.Clamp(_vy, -210, 240);
        _y += _vy * Dt;
        if (_y < Top + 8)
        {
            _y = Top + 8;
            _vy = MathF.Max(0, _vy);
        }
        _fuel = MathF.Max(0, _fuel - (_thrust ? 0.055f : 0.035f) * Dt);
        if (_thrust)
        {
            Sound.Loop(LoopSfx.Thrust, true, (_speed - 100) / 400, 0.5f);
            if (Tick % 2 == 0)
                Fx.Spark(ShipX - 17, _y + 2 + Rand(-1.5f, 1.5f), -_speed * 0.5f - Rand(40, 90), Rand(-15, 15), Pal.Orange, 0.3f, 2.2f);
        }
        Sound.Loop(LoopSfx.Wind, true, (_speed - 100) / 300, 0.25f);
        if (_fuel <= 0 && Tick % 40 == 0)
            Sound.Play(Sfx.Alarm, 0.4f, 0.4f);

        Score = (int)(_scroll / 10) + (int)_bonus;
        Status = (int)(_scroll / 10) + " m";

        // Fuel pick-ups.
        for (int k = 0; k < _cans.Length; k++)
        {
            ref var c = ref _cans[k];
            if (c.Taken)
                continue;
            float sx = c.X - _scroll;
            if (MathF.Abs(sx - ShipX) < 16 && MathF.Abs(c.Y + Bob(c.X) - _y) < 16)
            {
                c.Taken = true;
                _fuel = MathF.Min(1, _fuel + 0.32f);
                _bonus += 100;
                Fx.Float("+100 FUEL", sx, c.Y - 14, Pal.Lime);
                Fx.Burst(sx, c.Y, Pal.Lime, 18, 90, 0.5f, 2f);
                Sound.Play(Sfx.Pickup);
            }
        }

        // Collision: nose and the two tail tips.
        if (_invuln <= 0)
        {
            float tilt = Tilt;
            for (int p = 0; p < 3; p++)
            {
                var local = p == 0 ? new Vector2(16, 1) : p == 1 ? new Vector2(-13, -8) : new Vector2(-12, 6);
                var w = new Vector2(ShipX, _y) + Rotate(local, tilt);
                Bounds(_scroll + w.X, out float top, out float bottom);
                if (w.Y < top || w.Y > bottom || w.Y > Bottom)
                {
                    Crash(w);
                    return;
                }
            }
        }
        else
        {
            // Ghost through, but stay inside the playfield.
            Bounds(_scroll + ShipX, out float top, out float bottom);
            if (_y > bottom - 8)
            {
                _y = bottom - 8;
                _vy = MathF.Min(_vy, 0);
            }
        }
    }

    private float Tilt => MathF2.Clamp(_vy / 600, -0.35f, 0.4f);

    private static Vector2 Rotate(Vector2 v, float a) =>
        new(v.X * MathF.Cos(a) - v.Y * MathF.Sin(a), v.X * MathF.Sin(a) + v.Y * MathF.Cos(a));

    private static float Bob(float x) => MathF.Sin(x * 0.02f) * 4;

    private void Crash(Vector2 at)
    {
        Fx.Explode(at.X, at.Y, 1.6f);
        Fx.Burst(at.X, at.Y, Pal.Sky, 20, 140, 0.8f, 2f, 200);
        Sound.Play(Sfx.BigExplode);
        _dead = 1.5f;
        if (Lives <= 1)
        {
            Lives = 0;
            EndGame(false, _fuel <= 0 ? "Out of fuel at " + (int)(_scroll / 10) + " m." : "Crashed at " + (int)(_scroll / 10) + " m.");
            return;
        }
        LoseLife();
    }

    private void Respawn()
    {
        Bounds(_scroll + ShipX + 40, out float top, out float bottom);
        _y = (top + bottom) / 2;
        _vy = 0;
        _invuln = 2.5f;
        _fuel = MathF.Max(_fuel, 0.6f);
        _speed = MathF.Max(105, _speed * 0.85f);
    }

    // ------------------------------------------------------------------ drawing

    public override void Draw(Gfx g)
    {
        float worldX = _scroll + ShipX;
        bool cityHere = Col((int)(worldX / ColW)).City;
        DrawScene(g, _scroll, Screen.Bounds, 1f, Time);

        // Canisters.
        foreach (var c in _cans)
        {
            if (c.Taken)
                continue;
            float sx = c.X - _scroll;
            if (sx < -20 || sx > 660)
                continue;
            DrawCanister(g, sx, c.Y + Bob(c.X), 1f, Time);
        }

        // Ship.
        if (_dead <= 0 && (_invuln <= 0 || (int)(_invuln * 10) % 2 == 0))
            DrawShip(g, ShipX, _y, Tilt, 1.25f, _thrust, Time);

        // Fuel gauge.
        float fx = 12, fy = 336;
        g.RoundRect(fx - 4, fy - 4, 136, 18, 4, Color.Black * 0.55f);
        g.Text("FUEL", fx, fy + 1, 1.25f, _fuel < 0.25f && (int)(Time * 4) % 2 == 0 ? Pal.Red : Pal.White);
        g.Rect(fx + 34, fy, 92, 10, new Color(30, 30, 40));
        var fc = _fuel < 0.25f ? Pal.Red : _fuel < 0.5f ? Pal.Yellow : Pal.Lime;
        g.GradientH(fx + 34, fy, 92 * _fuel, 10, Pal.Darken(fc, 0.3f), fc);
        g.RectOutline(fx + 33, fy - 1, 94, 12, 1, Pal.LightGrey * 0.6f);

        if (_banner > 0)
        {
            float a = MathF.Min(1, _banner);
            string title = cityHere ? "CITY SKYLINE" : Level == 1 ? "THE CAVERNS" : "DEEPER CAVERNS";
            g.TextShadow("STAGE " + Level, 320, 120, 3f, Pal.Yellow * a, Align.Center);
            g.TextShadow(title, 320, 152, 1.5f, Pal.Sky * a, Align.Center);
        }
    }

    private void DrawScene(Gfx g, float scroll, RectF r, float s, float t)
    {
        int first = (int)(scroll / ColW) - 1;
        int count = (int)(r.W / (ColW * s)) + 3;
        bool city = Col(first + count / 2).City;

        // Sky / cave backdrop.
        var skyTop = city ? new Color(20, 14, 50) : new Color(10, 14, 36);
        var skyBottom = city ? new Color(150, 60, 90) : new Color(36, 18, 54);
        g.GradientV(r.X, r.Y, r.W, r.H, skyTop, skyBottom);
        if (city)
        {
            Backdrops.Stars(g, t, 0, 19, new RectF(r.X, r.Y, r.W, r.H * 0.5f), 50);
            g.Glow(r.X + r.W * 0.8f, r.Y + r.H * 0.3f, 60 * s, Pal.Pink, 0.35f);
            g.Circle(r.X + r.W * 0.8f, r.Y + r.H * 0.3f, 22 * s, new Color(255, 190, 150));
            // Distant towers (parallax).
            for (int i = 0; i < 26; i++)
            {
                float wx = i * 40 * s - (scroll * 0.25f * s) % (40 * s);
                int id = (int)(scroll * 0.25f / 40) + i;
                float h = (60 + (id * 37 % 90)) * s;
                g.Rect(r.X + wx, r.Bottom - h, 34 * s, h, new Color(50, 26, 70));
            }
        }
        else
        {
            // Two layers of far rock.
            for (int layer = 0; layer < 2; layer++)
            {
                float par = layer == 0 ? 0.2f : 0.45f;
                var col = layer == 0 ? new Color(26, 20, 52) : new Color(42, 28, 70);
                float amp = (layer == 0 ? 40 : 30) * s;
                for (int i = 0; i < 84; i++)
                {
                    float sx = i * 8 * s - (scroll * par * s) % (8 * s);
                    float wx = (scroll * par) + i * 8 - (scroll * par) % 8;
                    float spikes = MathF.Pow(MathF.Abs(MathF.Sin(wx * 0.021f + layer * 1.7f)), 6) * 70 * s;
                    float hTop = amp * (0.6f + 0.4f * MathF.Sin(wx * 0.013f + layer)) + 40 * s + spikes;
                    float hBot = amp * (0.6f + 0.4f * MathF.Sin(wx * 0.017f + 2 + layer)) + 30 * s + spikes * 0.6f;
                    g.Rect(r.X + sx, r.Y, 8.5f * s, hTop + layer * 20 * s, col);
                    g.Rect(r.X + sx, r.Bottom - hBot - layer * 20 * s, 8.5f * s, hBot + layer * 20 * s, col);
                }
            }
            // Drifting motes.
            for (int i = 0; i < 24; i++)
            {
                float mx = r.X + Backdrops.Mod(i * 83 - scroll * 0.6f * s + MathF.Sin(t + i) * 6, r.W);
                float my = r.Y + r.H * (0.2f + 0.6f * ((i * 37) % 100) / 100f) + MathF.Sin(t * 0.7f + i) * 8 * s;
                g.Circle(mx, my, 0.9f * s, Pal.Ice * 0.35f);
            }
            // Glowing crystals in the dark.
            for (int i = 0; i < 6; i++)
            {
                float sx = r.X + ((i * 157 - scroll * 0.45f * s) % (r.W + 40) + r.W + 40) % (r.W + 40) - 20;
                g.Glow(sx, r.Y + (i % 2 == 0 ? 70 : r.H - 70) * s, 40 * s, i % 3 == 0 ? Pal.Magenta : Pal.Teal, 0.3f);
            }
        }

        // Foreground rock / buildings.
        var rock = new Color(110, 70, 110);
        var rockDark = new Color(58, 36, 70);
        var edge = new Color(140, 220, 255);
        for (int k = 0; k < count; k++)
        {
            int i = first + k;
            ref var a = ref Col(i);
            ref var b = ref Col(i + 1);
            float x0 = r.X + (i * ColW - scroll) * s, x1 = x0 + ColW * s;
            float ta = r.Y + a.Top * s, tb = r.Y + b.Top * s;
            float ba = r.Y + a.Bottom * s, bb = r.Y + b.Bottom * s;
            if (a.Top > Top + 1 || b.Top > Top + 1)
            {
                g.Triangle(new Vector2(x0, r.Y), new Vector2(x1, r.Y), new Vector2(x1, tb), rockDark);
                g.Triangle(new Vector2(x0, r.Y), new Vector2(x1, tb), new Vector2(x0, ta), rockDark);
                g.Triangle(new Vector2(x0, ta - 10 * s), new Vector2(x1, tb - 10 * s), new Vector2(x1, tb), rock);
                g.Triangle(new Vector2(x0, ta - 10 * s), new Vector2(x1, tb), new Vector2(x0, ta), rock);
                g.Line(x0, ta, x1, tb, 1.6f * s, edge * 0.8f);
                if ((i * 2654435761u >> 27) < 4 && ta - r.Y > 30 * s)
                    g.Circle(x0 + 3 * s, ta - 18 * s - (i % 5) * 4 * s, 1.6f * s, rock);
            }
            if (a.City)
            {
                var wall = Pal.Hsv(230 + a.Seed % 60, 0.45f, 0.35f);
                g.Rect(x0, ba, ColW * s + 0.5f, r.Bottom - ba, wall);
                for (float wy = ba + 6 * s; wy < r.Bottom - 4 * s; wy += 9 * s)
                {
                    uint h = (uint)(i * 73856093) ^ (uint)((int)((wy - ba) / s) * 19349663) ^ (uint)(a.Seed * 83492791);
                    int hash = (int)((h >> 13) & 7);
                    if (hash < 3)
                        g.Rect(x0 + 1.5f * s, wy, 3 * s, 4 * s, hash == 0 ? new Color(255, 220, 120) : new Color(120, 160, 220) * 0.6f);
                }
                g.Rect(x0, ba, ColW * s + 0.5f, 1.5f * s, Pal.Lighten(wall, 0.5f));
                if (a.Edge)
                    g.Rect(x0, ba, 1.5f * s, r.Bottom - ba, Pal.Lighten(wall, 0.3f));
                if (a.Antenna)
                {
                    g.Rect(x0 + 2 * s, ba - 22 * s, 1.5f * s, 22 * s, Pal.Grey);
                    if ((int)(t * 2 + i) % 2 == 0)
                        g.Glow(x0 + 2.7f * s, ba - 22 * s, 7 * s, Pal.Red, 0.9f);
                }
            }
            else
            {
                g.Triangle(new Vector2(x0, ba), new Vector2(x1, bb), new Vector2(x1, r.Bottom), rockDark);
                g.Triangle(new Vector2(x0, ba), new Vector2(x1, r.Bottom), new Vector2(x0, r.Bottom), rockDark);
                g.Triangle(new Vector2(x0, ba), new Vector2(x1, bb), new Vector2(x1, bb + 10 * s), rock);
                g.Triangle(new Vector2(x0, ba), new Vector2(x1, bb + 10 * s), new Vector2(x0, ba + 10 * s), rock);
                g.Line(x0, ba, x1, bb, 1.6f * s, edge * 0.8f);
                if ((i * 2246822519u >> 27) < 4 && r.Bottom - ba > 30 * s)
                    g.Circle(x0 + 3 * s, ba + 18 * s + (i % 5) * 4 * s, 1.6f * s, rock);
            }
        }
    }

    private static void DrawCanister(Gfx g, float x, float y, float s, float t)
    {
        g.Glow(x, y, 18 * s, Pal.Lime, 0.4f + 0.2f * MathF.Sin(t * 6));
        g.RoundRect(x - 6 * s, y - 8 * s, 12 * s, 16 * s, 3 * s, new Color(210, 40, 40));
        g.Rect(x - 6 * s, y - 3 * s, 12 * s, 6 * s, Pal.Yellow);
        g.Rect(x - 2 * s, y - 11 * s, 4 * s, 3 * s, Pal.Grey);
        g.Text("F", x - 2.6f * s, y - 2.6f * s, 0.9f * s, Pal.Black);
    }

    private static readonly Vector2[] Hull = [new(14, 1), new(4, -3), new(-10, -3), new(-12, 4), new(4, 5)];
    private static readonly Vector2[] Fin = [new(-4, -3), new(-9, -10), new(-13, -10), new(-11, -3)];
    private static readonly Vector2[] Wing = [new(3, 2), new(-6, 9), new(-9, 9), new(-5, 2)];
    private static readonly Vector2[] Canopy = [new(8, -1), new(4, -5), new(-1, -5), new(0, -2)];

    private static void DrawShip(Gfx g, float x, float y, float tilt, float s, bool thrust, float t)
    {
        var p = new Vector2(x, y);
        if (thrust)
        {
            var back = p + Rotate(new Vector2(-14, 1), tilt) * s;
            float f = 0.8f + 0.2f * MathF.Sin(t * 50);
            g.Glow(back, 16 * s * f, Pal.Orange, 0.8f);
            g.Glow(back, 8 * s * f, Pal.Yellow, 0.8f);
        }
        g.Glow(p, 22 * s, Pal.Sky, 0.2f);
        g.Shape(Fin, p, tilt, s, new Color(160, 170, 190));
        g.Shape(Hull, p, tilt, s, new Color(220, 228, 240));
        g.Shape(Wing, p, tilt, s, new Color(150, 160, 180));
        g.Shape(Canopy, p, tilt, s, Pal.Sky);
        var stripe = p + Rotate(new Vector2(-2, 1.5f), tilt) * s;
        g.RotatedRect(stripe, 16 * s, 1.6f * s, tilt, Pal.Red);
    }

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        float s = r.H / 70f;
        g.GradientV(r.X, r.Y, r.W, r.H, new Color(8, 10, 22), new Color(30, 18, 50));
        // A little cave made of sine waves.
        float scroll = time * 40;
        var rock = new Color(90, 58, 96);
        var edge = new Color(140, 220, 255);
        for (float x = 0; x < r.W; x += 1.5f * s)
        {
            float wx = (x / s + scroll);
            float top = (12 + 8 * MathF.Sin(wx * 0.05f) + 5 * MathF.Sin(wx * 0.13f)) * s;
            float bot = (12 + 8 * MathF.Sin(wx * 0.04f + 2) + 5 * MathF.Sin(wx * 0.11f)) * s;
            float spike = Backdrops.Mod(wx, 90) - 45;
            if (MathF.Abs(spike) < 8)
                top += (8 - MathF.Abs(spike)) * 1.6f * s;
            g.Rect(r.X + x, r.Y, 1.7f * s, top, rock);
            g.Rect(r.X + x, r.Y + top - 1.5f * s, 1.7f * s, 1.5f * s, edge);
            g.Rect(r.X + x, r.Bottom - bot, 1.7f * s, bot, rock);
            g.Rect(r.X + x, r.Bottom - bot, 1.7f * s, 1.5f * s, edge);
        }
        DrawCanister(g, r.X + r.W * 0.75f, r.CenterY + MathF.Sin(time * 2) * 4 * s, 0.8f * s, time);
        float y = r.CenterY + MathF.Sin(time * 1.6f) * 8 * s;
        float tilt = MathF.Cos(time * 1.6f) * 0.25f;
        for (int i = 0; i < 6; i++)
        {
            float k = ((time * 3 + i / 6f) % 1f);
            g.Glow(r.X + r.W * 0.3f - 16 * s - k * 30 * s, y + 2 * s, (5 - k * 4) * s, Pal.Orange, 0.6f * (1 - k));
        }
        DrawShip(g, r.X + r.W * 0.3f, y, tilt, 1.4f * s, true, time);
    }

    // ------------------------------------------------------------------ autopilot

    public override void AutoPlay(Controls c)
    {
        if (_dead > 0)
            return;
        // Aim for the middle of the safest band ahead, looking as far as the cave allows.
        float minBottom = 0, maxTop = 0;
        for (int pass = 0; pass < 4; pass++)
        {
            float reach = pass == 0 ? _speed * 1.2f + 60 : pass == 1 ? _speed * 0.8f + 40 : pass == 2 ? _speed * 0.4f + 30 : 24;
            minBottom = float.MaxValue;
            maxTop = float.MinValue;
            for (float dx = 6; dx <= reach; dx += ColW)
            {
                Bounds(_scroll + ShipX + dx, out float top, out float bottom);
                maxTop = MathF.Max(maxTop, top);
                minBottom = MathF.Min(minBottom, bottom);
            }
            if (minBottom - maxTop > 40)
                break;
        }
        float target = (maxTop + minBottom) / 2;
        if (minBottom - maxTop > 200)
            target = minBottom - 90;
        foreach (var can in _cans)
        {
            float dx = can.X - _scroll - ShipX;
            if (!can.Taken && dx > 0 && dx < 160 && can.Y > maxTop + 14 && can.Y < minBottom - 14)
                target = can.Y;
        }
        float desiredVy = MathF2.Clamp((target - _y) * 2.8f, -180, 180);
        c.Fire = _vy > desiredVy;
        c.FirePressed = c.Fire && !_thrust;
    }
}
