using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Cascade50.Core.Audio;
using Cascade50.Core.Capture;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 18 Invasive Action: missile defence. Warheads rain down on six cities; launch interceptors from
/// three bases to fill the sky with fireballs. Ammunition is limited each wave.
/// </summary>
public sealed class InvasiveAction : MiniGame, ICaptureHints
{
    public override int Number => 18;
    public override string Title => "Invasive Action";
    public override Category Category => Category.Shooter;
    public override string Tagline => "Defend six cities from falling warheads with a sky full of fireballs.";
    public override Color Accent => Pal.Red;

    public override string[] HowToPlay =>
    [
        "Warheads are falling on your six cities. Tap a point in the sky: the nearest base fires a missile that bursts into a fireball there.",
        "Each base holds 10 missiles a wave, so lead your targets. Fireballs set off chain reactions.",
        "Bonus for every city and missile left. Lose all six cities and it's over.",
    ];

    public override string[] DesktopControls =>
        ["Click to fire at a point.", "Or ARROWS to aim, SPACE to fire; Z / X / C fire from the left / middle / right base."];

    public override string[] TouchControls => ["Tap the sky to fire the nearest base."];
    public override Pad Pad => Pad.None;
    public int CaptureTicks => 540;

    private const float GroundY = 332;
    private static readonly float[] BaseX = [36, 320, 604];
    private static readonly float[] CityX = [100, 160, 222, 418, 480, 540];

    private static readonly Dictionary<char, Color> Colours = new()
    {
        ['b'] = new Color(40, 110, 220), ['c'] = new Color(90, 190, 255), ['y'] = Pal.Yellow, ['d'] = new Color(20, 50, 120),
    };

    private static readonly PixelArt CityArt = new(
    [
        ".....bb.........",
        ".....bb.....bb..",
        "..bb.bb.....bb..",
        "..bbbbbc..ccbbb.",
        "cbbybbbccccybbbc",
        "cbbbbybccyccbbyc",
        "cbybbbbcccccybbc",
        "dddddddddddddddd",
    ], Colours);

    private sealed class Missile
    {
        public Vector2 Start, Pos, Vel;
        public int Kind; // 0 warhead, 1 smart bomb
        public float SplitY;
        public float TargetedUntil;
    }

    private struct Interceptor
    {
        public Vector2 Start, Pos, Target;
    }

    private struct Blast
    {
        public Vector2 Pos;
        public float T, Dur, MaxR;
        public float Radius => MaxR * MathF.Sin(MathF.PI * MathF.Min(1, T / Dur));
    }

    private sealed class Bomber
    {
        public Vector2 Pos;
        public float Vx, Drop;
        public bool Satellite;
    }

    private readonly List<Missile> _missiles = new();
    private readonly List<Interceptor> _shots = new();
    private readonly List<Blast> _blasts = new();
    private readonly List<Bomber> _bombers = new();
    private readonly bool[] _cities = new bool[6];
    private readonly int[] _ammo = new int[3];
    private readonly bool[] _baseAlive = new bool[3];

    private Vector2 _cross;
    private int _toLaunch;
    private float _launchTimer, _bomberTimer, _tally, _endTimer, _waveBanner, _aiCool;
    private int _tallyMissiles, _tallyCities, _nextBonusCity;
    private bool _tallyDone;
    private bool _aiRelease;

    private int Mult => Math.Min(6, (Level + 1) / 2);

    protected override void Start()
    {
        Level = 1;
        for (int i = 0; i < 6; i++)
            _cities[i] = true;
        _cross = new Vector2(320, 180);
        _nextBonusCity = 10000;
        NewWave();
    }

    private void NewWave()
    {
        for (int i = 0; i < 3; i++)
        {
            _ammo[i] = 10;
            _baseAlive[i] = true;
        }
        _missiles.Clear();
        _shots.Clear();
        _bombers.Clear();
        _toLaunch = 12 + Level * 2;
        _launchTimer = 1.2f;
        _bomberTimer = Rand(6, 10);
        _tally = 0;
        _waveBanner = 2;
        UpdateStatus();
    }

    private int CitiesLeft()
    {
        int n = 0;
        foreach (bool c in _cities)
            if (c)
                n++;
        return n;
    }

    private void UpdateStatus() => Status = "WAVE " + Level;

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        if (_waveBanner > 0)
            _waveBanner -= Dt;
        UpdateBlasts();
        UpdateShots();

        if (_endTimer > 0)
        {
            _endTimer -= Dt;
            if (_endTimer <= 0)
                EndGame(false, "All six cities have fallen.");
            return;
        }

        if (_tally > 0)
        {
            UpdateTally();
            return;
        }

        Aim();
        Launch();
        UpdateMissiles();
        UpdateBombers();

        if (_toLaunch <= 0 && _missiles.Count == 0 && _bombers.Count == 0 && _blasts.Count == 0 && _shots.Count == 0)
        {
            _tally = 4f;
            _tallyMissiles = 0;
            _tallyCities = 0;
            _tallyDone = false;
        }
    }

    private void Aim()
    {
        float speed = 290 * Dt;
        if (In.Left || In.Right || In.Up || In.Down)
            _cross += new Vector2(In.AxisX, In.AxisY) * speed;
        if (In.PointerMoved && In.HasHover)
            _cross = In.Pointer;
        if (In.PointerPressed)
            _cross = In.Pointer;
        _cross.X = MathF2.Clamp(_cross.X, 6, 634);
        _cross.Y = MathF2.Clamp(_cross.Y, Screen.HudHeight + 6, GroundY - 22);

        int which = -2;
        if (In.KeyPressed(Keys.Z))
            which = 0;
        else if (In.KeyPressed(Keys.X))
            which = 1;
        else if (In.KeyPressed(Keys.C))
            which = 2;
        else if (In.PointerPressed || In.FirePressed)
            which = -1;
        if (which == -2)
            return;
        if (which == -1)
        {
            float best = float.MaxValue;
            for (int i = 0; i < 3; i++)
                if (_baseAlive[i] && _ammo[i] > 0 && MathF.Abs(BaseX[i] - _cross.X) < best)
                {
                    best = MathF.Abs(BaseX[i] - _cross.X);
                    which = i;
                }
        }
        if (which < 0 || !_baseAlive[which] || _ammo[which] <= 0)
        {
            Sound.Play(Sfx.Wrong, 0, 0.4f);
            return;
        }
        _ammo[which]--;
        var start = new Vector2(BaseX[which], GroundY - 14);
        _shots.Add(new Interceptor { Start = start, Pos = start, Target = _cross });
        Sound.Play(Sfx.Whoosh, 0.3f + which * 0.1f, 0.6f);
    }

    private void UpdateShots()
    {
        for (int i = _shots.Count - 1; i >= 0; i--)
        {
            var s = _shots[i];
            var to = s.Target - s.Pos;
            float step = (s.Start.X == BaseX[1] ? 520 : 400) * Dt;
            if (to.Length() <= step)
            {
                _blasts.Add(new Blast { Pos = s.Target, Dur = 1.3f, MaxR = 30 });
                Sound.Play(Sfx.Explode, Rand(-0.1f, 0.3f), 0.6f);
                _shots.RemoveAt(i);
                continue;
            }
            s.Pos += Vector2.Normalize(to) * step;
            _shots[i] = s;
            if (Chance(0.5f))
                Fx.Spark(s.Pos.X, s.Pos.Y, Rand(-10, 10), Rand(5, 20), Pal.Sky, 0.3f, 1.2f);
        }
    }

    private void UpdateBlasts()
    {
        for (int i = _blasts.Count - 1; i >= 0; i--)
        {
            var b = _blasts[i];
            b.T += Dt;
            if (b.T >= b.Dur)
                _blasts.RemoveAt(i);
            else
                _blasts[i] = b;
        }
    }

    private bool InBlast(Vector2 p, float margin = 0)
    {
        foreach (var b in _blasts)
        {
            float r = b.Radius + margin;
            if (Vector2.DistanceSquared(b.Pos, p) < r * r)
                return true;
        }
        return false;
    }

    private void Launch()
    {
        if (_toLaunch <= 0)
            return;
        _launchTimer -= Dt;
        int active = _missiles.Count;
        if (_launchTimer > 0 || active >= 3 + Level)
            return;
        _launchTimer = MathF.Max(0.8f, 2.6f - Level * 0.15f) * Rand(0.6f, 1.2f);
        int salvo = Math.Min(_toLaunch, RandInt(1, 3 + Math.Min(Level, 4) / 2));
        for (int i = 0; i < salvo; i++)
        {
            _toLaunch--;
            bool smart = Level >= 5 && Chance(0.12f + Level * 0.01f);
            SpawnMissile(new Vector2(Rand(20, 620), Screen.HudHeight), smart ? 1 : 0, Chance(MathF.Min(0.4f, (Level - 2) * 0.1f)));
        }
    }

    private void SpawnMissile(Vector2 from, int kind, bool mirv)
    {
        var target = PickTarget();
        float speed = MathF.Min(22 + Level * 6, 85) * Rand(0.85f, 1.15f) * (kind == 1 ? 0.8f : 1);
        _missiles.Add(new Missile
        {
            Start = from, Pos = from, Vel = Vector2.Normalize(target - from) * speed, Kind = kind,
            SplitY = mirv ? Rand(110, 190) : -1,
        });
    }

    private Vector2 PickTarget()
    {
        // Mostly cities and bases still standing, sometimes rubble.
        for (int tries = 0; tries < 8; tries++)
        {
            int k = RandInt(0, 9);
            if (k < 6 && _cities[k])
                return new Vector2(CityX[k] + Rand(-6, 6), GroundY - 4);
            if (k >= 6 && _baseAlive[k - 6])
                return new Vector2(BaseX[k - 6], GroundY - 8);
        }
        return new Vector2(Pick(CityX) + Rand(-6, 6), GroundY - 4);
    }

    private void UpdateMissiles()
    {
        for (int i = _missiles.Count - 1; i >= 0; i--)
        {
            var m = _missiles[i];
            if (m.Kind == 1)
            {
                // Smart bombs sidestep nearby fireballs.
                foreach (var b in _blasts)
                {
                    var away = m.Pos - b.Pos;
                    float d = away.Length();
                    if (d < b.Radius + 34 && d > 0.1f)
                        m.Pos += new Vector2(away.X / d, 0) * 60 * Dt;
                }
            }
            m.Pos += m.Vel * Dt;
            if (m.SplitY > 0 && m.Pos.Y > m.SplitY)
            {
                m.SplitY = -1;
                int n = RandInt(2, 4);
                for (int k = 0; k < n; k++)
                    SpawnMissile(m.Pos, 0, false);
                Sound.Play(Sfx.Pop, 0.5f, 0.4f);
            }
            if (InBlast(m.Pos, 2))
            {
                int pts = (m.Kind == 1 ? 125 : 25) * Mult;
                AddScore(pts, m.Pos.X, m.Pos.Y - 10, m.Kind == 1 ? Pal.Magenta : Pal.Yellow);
                _blasts.Add(new Blast { Pos = m.Pos, Dur = 0.9f, MaxR = 18 });
                Sound.Play(Sfx.Explode, Rand(0.1f, 0.5f), 0.5f);
                _missiles.RemoveAt(i);
                CheckBonusCity();
                continue;
            }
            if (m.Pos.Y >= GroundY - 6)
            {
                Impact(m.Pos);
                _missiles.RemoveAt(i);
            }
        }
    }

    private void Impact(Vector2 p)
    {
        _blasts.Add(new Blast { Pos = new Vector2(p.X, GroundY - 6), Dur = 1.1f, MaxR = 26 });
        Fx.Explode(p.X, GroundY - 8, 1.2f);
        Sound.Play(Sfx.BigExplode, Rand(-0.3f, 0), 0.8f);
        for (int c = 0; c < 6; c++)
            if (_cities[c] && MathF.Abs(CityX[c] - p.X) < 22)
            {
                _cities[c] = false;
                Fx.Burst(CityX[c], GroundY - 10, Pal.Sky, 30, 120, 1f, 2.5f, 120);
                Sound.Play(Sfx.Hurt);
                if (CitiesLeft() == 0)
                    _endTimer = 1.8f;
            }
        for (int b = 0; b < 3; b++)
            if (_baseAlive[b] && MathF.Abs(BaseX[b] - p.X) < 22)
            {
                _baseAlive[b] = false;
                _ammo[b] = 0;
                Fx.Burst(BaseX[b], GroundY - 12, Pal.Orange, 24, 110, 0.8f, 2.5f, 100);
            }
    }

    private void UpdateBombers()
    {
        if (Level >= 2 && _toLaunch > 0)
        {
            _bomberTimer -= Dt;
            if (_bomberTimer <= 0)
            {
                _bomberTimer = Rand(8, 14) / (1 + Level * 0.05f);
                bool left = Chance(0.5f);
                _bombers.Add(new Bomber
                {
                    Pos = new Vector2(left ? -20 : 660, Rand(70, 130)), Vx = (left ? 1 : -1) * Rand(40, 60),
                    Drop = Rand(1.5f, 3f), Satellite = Chance(0.5f),
                });
            }
        }
        for (int i = _bombers.Count - 1; i >= 0; i--)
        {
            var b = _bombers[i];
            b.Pos.X += b.Vx * Dt;
            b.Drop -= Dt;
            if (b.Drop <= 0 && _toLaunch > 0 && b.Pos.X > 40 && b.Pos.X < 600)
            {
                b.Drop = Rand(2f, 3.5f);
                _toLaunch--;
                SpawnMissile(b.Pos + new Vector2(0, 6), 0, false);
                Sound.Play(Sfx.Zap, -0.2f, 0.3f);
            }
            if (InBlast(b.Pos, 6))
            {
                AddScore(100 * Mult, b.Pos.X, b.Pos.Y - 12, Pal.Orange);
                Fx.Explode(b.Pos.X, b.Pos.Y, 1);
                _blasts.Add(new Blast { Pos = b.Pos, Dur = 0.9f, MaxR = 20 });
                Sound.Play(Sfx.BigExplode, 0.2f, 0.7f);
                _bombers.RemoveAt(i);
                continue;
            }
            if (b.Pos.X < -40 || b.Pos.X > 680)
                _bombers.RemoveAt(i);
            else
                Sound.Loop(LoopSfx.Hum, true, b.Satellite ? 0.6f : -0.3f, 0.3f);
        }
    }

    private void CheckBonusCity()
    {
        if (Score < _nextBonusCity)
            return;
        _nextBonusCity += 10000;
        for (int c = 0; c < 6; c++)
            if (!_cities[c])
            {
                _cities[c] = true;
                Fx.Float("BONUS CITY!", CityX[c], GroundY - 40, Pal.Lime);
                Sound.Play(Sfx.PowerUp);
                return;
            }
    }

    private void UpdateTally()
    {
        float before = _tally;
        _tally -= Dt;
        // Count leftover missiles, then the cities, one tick at a time.
        if (Tick % 4 == 0 && !_tallyDone)
        {
            int left = _ammo[0] + _ammo[1] + _ammo[2];
            if (left > 0)
            {
                for (int b = 0; b < 3; b++)
                    if (_ammo[b] > 0)
                    {
                        _ammo[b]--;
                        break;
                    }
                _tallyMissiles++;
                AddScore(5 * Mult);
                Sound.Play(Sfx.Tick, 0.4f, 0.5f);
                _tally = MathF.Max(_tally, 1.5f);
            }
            else if (_tallyCities < CitiesLeft() && Tick % 12 == 0)
            {
                _tallyCities++;
                AddScore(100 * Mult);
                Sound.Play(Sfx.Coin, _tallyCities * 0.1f, 0.6f);
                _tally = MathF.Max(_tally, 1.5f);
            }
            else if (_tallyCities >= CitiesLeft())
            {
                _tallyDone = true;
                CheckBonusCity();
            }
        }
        if (_tallyDone && _tally <= 0 && before > 0)
        {
            Level++;
            Sound.Play(Sfx.LevelUp);
            NewWave();
        }
    }

    // ------------------------------------------------------------------ drawing

    public override void Draw(Gfx g)
    {
        float t = Time;
        g.GradientV(0, 0, 640, GroundY, new Color(4, 4, 20), new Color(40, 12, 50));
        Backdrops.Stars(g, t, 0, 18, new RectF(0, 0, 640, 260), 110);
        g.GradientV(0, GroundY - 80, 640, 80, Color.Transparent, new Color(120, 30, 60) * 0.4f);
        Backdrops.Hills(g, GroundY - 14, 26, 0, new Color(34, 14, 44), 5, new RectF(0, 0, 640, GroundY + 2));
        Backdrops.Hills(g, GroundY - 2, 12, 300, new Color(52, 24, 52), 9, new RectF(0, 0, 640, GroundY + 2));

        // Ground.
        DrawGround(g, new RectF(0, 0, 640, 360), 1, t);

        // Trails and warheads.
        var trail = Pal.Hsv(350 + Level * 47, 0.8f, 1f);
        foreach (var m in _missiles)
        {
            if (m.Kind == 1)
            {
                g.Glow(m.Pos, 14, Pal.Magenta, 0.7f);
                float a = t * 6;
                g.Shape(Diamond, m.Pos, a, 6, Pal.Magenta);
                g.Shape(Diamond, m.Pos, a, 3, Pal.White);
                continue;
            }
            g.Line(m.Start, m.Pos, 5f, Pal.Add(trail, 0.12f));
            g.Line(m.Start, m.Pos, 2.4f, Pal.Add(trail, 0.3f));
            g.Line(m.Start, m.Pos, 1.2f, trail);
            g.Glow(m.Pos, 12, trail, 0.9f);
            g.Circle(m.Pos.X, m.Pos.Y, 2.2f, (Tick / 3) % 2 == 0 ? Pal.White : Pal.Yellow);
        }

        foreach (var b in _bombers)
            DrawBomber(g, b.Pos, b.Vx > 0, b.Satellite, t, 1);

        // Our missiles.
        foreach (var s in _shots)
        {
            g.Line(s.Start, s.Pos, 1.4f, Pal.Sky * 0.8f);
            g.Glow(s.Pos, 7, Pal.Sky, 0.8f);
            g.Circle(s.Pos.X, s.Pos.Y, 1.5f, Pal.White);
            float k = 4;
            g.Line(s.Target.X - k, s.Target.Y - k, s.Target.X + k, s.Target.Y + k, 1.4f, Pal.Sky);
            g.Line(s.Target.X - k, s.Target.Y + k, s.Target.X + k, s.Target.Y - k, 1.4f, Pal.Sky);
        }

        foreach (var b in _blasts)
            DrawBlast(g, b.Pos, b.Radius, t + b.Pos.X);

        // Crosshair.
        if (!IsOver && _tally <= 0)
        {
            var c = _cross;
            var col = Pal.Lerp(Pal.Lime, Pal.White, MathF2.Pulse(t, 0.6f));
            g.Ring(c.X, c.Y, 7, 1.2f, col, 20);
            g.Rect(c.X - 12, c.Y - 0.6f, 6, 1.2f, col);
            g.Rect(c.X + 6, c.Y - 0.6f, 6, 1.2f, col);
            g.Rect(c.X - 0.6f, c.Y - 12, 1.2f, 6, col);
            g.Rect(c.X - 0.6f, c.Y + 6, 1.2f, 6, col);
        }

        if (_waveBanner > 0 && _tally <= 0)
        {
            float a = MathF.Min(1, _waveBanner);
            g.TextShadow("WAVE " + Level, 320, 120, 3f, Pal.Yellow * a, Align.Center);
            g.TextShadow(Mult + "x POINTS", 320, 152, 1.5f, Pal.Orange * a, Align.Center);
        }
        if (_tally > 0)
        {
            g.Panel(RectF.Centered(320, 150, 280, 110), Pal.Panel * 0.9f, Pal.Red);
            g.TextShadow("BONUS POINTS", 320, 108, 2f, Pal.Yellow, Align.Center);
            g.Text("MISSILES", 200, 140, 1.5f, Pal.Sky);
            g.Text((_tallyMissiles * 5 * Mult).ToString(), 440, 140, 1.5f, Pal.White, Align.Right);
            g.Text("CITIES", 200, 166, 1.5f, Pal.Sky);
            g.Text((_tallyCities * 100 * Mult).ToString(), 440, 166, 1.5f, Pal.White, Align.Right);
        }
    }

    private static readonly Vector2[] Quad = new Vector2[4];
    private static readonly Vector2[] Diamond = [new(1, 0), new(0, 1), new(-1, 0), new(0, -1)];

    private void DrawGround(Gfx g, RectF r, float s, float t)
    {
        float gy = r.Y + GroundY * s;
        g.GradientV(r.X, gy, r.W, r.Bottom - gy, new Color(150, 110, 40), new Color(60, 36, 20));
        g.Rect(r.X, gy, r.W, 1.5f * s, Pal.Sand);
        for (int i = 0; i < 40; i++)
        {
            float x = r.X + ((i * 97) % 640) * s, y = gy + (6 + (i * 13) % 20) * s;
            g.Ellipse(x, y, (2 + i % 3) * s, 1 * s, new Color(110, 76, 34));
        }
        for (int b = 0; b < 3; b++)
        {
            float bx = r.X + BaseX[b] * s;
            // Mound.
            Quad[0] = new Vector2(bx - 26 * s, gy + 1);
            Quad[1] = new Vector2(bx - 14 * s, gy - 12 * s);
            Quad[2] = new Vector2(bx + 14 * s, gy - 12 * s);
            Quad[3] = new Vector2(bx + 26 * s, gy + 1);
            g.Polygon(Quad, new Color(170, 125, 50));
            g.Rect(bx - 14 * s, gy - 12 * s, 28 * s, 1.5f * s, Pal.Sand);
            if (!_baseAlive[b])
            {
                g.Circle(bx, gy - 10 * s, 6 * s, new Color(40, 30, 25));
                continue;
            }
            // Ammo pyramid.
            int shown = 0;
            for (int row = 0; row < 4 && shown < _ammo[b]; row++)
                for (int k = 0; k <= row && shown < _ammo[b]; k++, shown++)
                {
                    float mx = bx + (k - row / 2f) * 6 * s, my = gy - 30 * s + row * 5 * s;
                    g.Triangle(new Vector2(mx, my - 3 * s), new Vector2(mx - 2 * s, my + 2 * s), new Vector2(mx + 2 * s, my + 2 * s), Pal.White);
                }
            if (_ammo[b] == 0)
                g.Text("OUT", bx, gy - 26 * s, s, Pal.Red, Align.Center);
        }
        for (int c = 0; c < 6; c++)
        {
            float cx = r.X + CityX[c] * s;
            if (_cities[c])
            {
                g.Glow(cx, gy - 8 * s, 22 * s, Pal.Sky, 0.25f);
                g.PixelsCentered(CityArt, cx, gy - 8 * s, 2 * s);
            }
            else
            {
                g.Rect(cx - 12 * s, gy - 4 * s, 24 * s, 4 * s, new Color(60, 50, 50));
                g.Rect(cx - 6 * s, gy - 7 * s, 8 * s, 3 * s, new Color(70, 55, 55));
                float smoke = (t * 0.6f + c * 0.3f) % 1;
                g.Circle(cx + smoke * 6 * s, gy - 10 * s - smoke * 26 * s, (3 + smoke * 6) * s, new Color(60, 60, 70) * (0.5f * (1 - smoke)));
            }
        }
    }

    private static void DrawBlast(Gfx g, Vector2 p, float r, float t)
    {
        if (r <= 0.5f)
            return;
        var core = Pal.Hsv(t * 400, 0.6f, 1f);
        g.Glow(p, r * 2.2f, Pal.Orange, 0.6f);
        g.Circle(p.X, p.Y, r, core);
        g.Circle(p.X, p.Y, r * 0.7f, Pal.Lighten(core, 0.5f));
        g.Circle(p.X, p.Y, r * 0.35f, Pal.White);
        g.Ring(p.X, p.Y, r, 1.5f, Pal.White * 0.6f);
    }

    private static void DrawBomber(Gfx g, Vector2 p, bool right, bool satellite, float t, float s)
    {
        if (satellite)
        {
            g.Glow(p, 16 * s, Pal.Cyan, 0.4f);
            g.Rect(p.X - 14 * s, p.Y - 3 * s, 8 * s, 6 * s, new Color(40, 90, 200));
            g.Rect(p.X + 6 * s, p.Y - 3 * s, 8 * s, 6 * s, new Color(40, 90, 200));
            g.Rect(p.X - 6 * s, p.Y - 0.5f * s, 12 * s, 1 * s, Pal.Silver);
            g.Circle(p.X, p.Y, 5 * s, Pal.Silver);
            g.Circle(p.X, p.Y, 2 * s, (int)(t * 4) % 2 == 0 ? Pal.Red : Pal.Yellow);
            return;
        }
        float d = right ? 1 : -1;
        g.Glow(p, 16 * s, Pal.Red, 0.3f);
        Quad[0] = new Vector2(p.X - 12 * s * d, p.Y - 2 * s);
        Quad[1] = new Vector2(p.X + 12 * s * d, p.Y - 1 * s);
        Quad[2] = new Vector2(p.X + 12 * s * d, p.Y + 2 * s);
        Quad[3] = new Vector2(p.X - 12 * s * d, p.Y + 2 * s);
        g.Polygon(Quad, Pal.LightGrey);
        g.Triangle(new Vector2(p.X - 2 * s * d, p.Y), new Vector2(p.X - 8 * s * d, p.Y + 7 * s), new Vector2(p.X + 4 * s * d, p.Y), Pal.Grey);
        g.Triangle(new Vector2(p.X - 9 * s * d, p.Y - 1 * s), new Vector2(p.X - 13 * s * d, p.Y - 7 * s), new Vector2(p.X - 6 * s * d, p.Y - 1 * s), Pal.Grey);
        if ((int)(t * 3) % 2 == 0)
            g.Glow(p.X + 12 * s * d, p.Y, 5 * s, Pal.Red, 0.9f);
    }

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        float s = r.H / 70f;
        g.GradientV(r.X, r.Y, r.W, r.H, new Color(4, 4, 20), new Color(60, 16, 60));
        Backdrops.Stars(g, time, 0, 18, r, 40);
        float gy = r.Bottom - 10 * s;
        g.GradientV(r.X, gy, r.W, 10 * s, new Color(150, 110, 40), new Color(60, 36, 20));
        for (int i = 0; i < 4; i++)
        {
            float cx = r.X + r.W * (0.15f + i * 0.23f);
            g.Glow(cx, gy - 6 * s, 16 * s, Pal.Sky, 0.3f);
            g.PixelsCentered(CityArt, cx, gy - 5 * s, 1.4f * s);
        }
        for (int i = 0; i < 3; i++)
        {
            float k = (time * 0.35f + i * 0.33f) % 1f;
            var start = new Vector2(r.X + r.W * (0.1f + 0.35f * i), r.Y);
            var end = new Vector2(r.X + r.W * (0.8f - 0.3f * i), gy);
            var head = Vector2.Lerp(start, end, k * 0.8f);
            g.Line(start, head, 1.4f * s, Pal.Red);
            g.Glow(head, 6 * s, Pal.Red, 0.9f);
            g.Circle(head.X, head.Y, 1.3f * s, Pal.White);
        }
        float pk = (time * 0.7f) % 1f;
        var bp = new Vector2(r.CenterX + 10 * s, r.Y + 26 * s);
        DrawBlast(g, bp, 16 * s * MathF.Sin(MathF.PI * pk), time * 2);
        var bp2 = new Vector2(r.X + r.W * 0.3f, r.Y + 36 * s);
        DrawBlast(g, bp2, 12 * s * MathF.Sin(MathF.PI * ((pk + 0.5f) % 1)), time * 2 + 1);
        var cr = new Vector2(r.Right - 30 * s, r.Y + 18 * s);
        g.Ring(cr.X, cr.Y, 6 * s, 1.2f * s, Pal.Lime, 20);
        g.Rect(cr.X - 10 * s, cr.Y - 0.5f * s, 5 * s, 1 * s, Pal.Lime);
        g.Rect(cr.X + 5 * s, cr.Y - 0.5f * s, 5 * s, 1 * s, Pal.Lime);
        g.Rect(cr.X - 0.5f * s, cr.Y - 10 * s, 1 * s, 5 * s, Pal.Lime);
        g.Rect(cr.X - 0.5f * s, cr.Y + 5 * s, 1 * s, 5 * s, Pal.Lime);
    }

    // ------------------------------------------------------------------ autopilot

    public override void AutoPlay(Controls c)
    {
        if (_aiRelease)
        {
            c.PointerReleased = true;
            c.Pointer = _cross;
            _aiRelease = false;
            return;
        }
        if (_aiCool > 0)
        {
            _aiCool -= Dt;
            return;
        }
        if (_tally > 0)
            return;
        Missile pick = null;
        float lowest = 0;
        foreach (var m in _missiles)
            if (m.TargetedUntil < Time && m.Pos.Y > lowest && m.Pos.Y > 110 && m.Pos.Y < GroundY - 40)
            {
                lowest = m.Pos.Y;
                pick = m;
            }
        Vector2 aim;
        if (pick != null)
        {
            // Lead the target: iterate on the interceptor's flight time.
            aim = pick.Pos;
            for (int k = 0; k < 3; k++)
            {
                float bx = BaseX[0];
                foreach (float x in BaseX)
                    if (MathF.Abs(x - aim.X) < MathF.Abs(bx - aim.X))
                        bx = x;
                float tFlight = Vector2.Distance(new Vector2(bx, GroundY - 14), aim) / (bx == BaseX[1] ? 520 : 400);
                aim = pick.Pos + pick.Vel * (tFlight + 0.25f);
            }
            pick.TargetedUntil = Time + 2.5f;
        }
        else
        {
            Bomber bomber = null;
            foreach (var b in _bombers)
                if (b.Pos.X > 80 && b.Pos.X < 560)
                    bomber = b;
            if (bomber == null)
                return;
            aim = bomber.Pos + new Vector2(bomber.Vx * 1.0f, 0);
            _aiCool = 1.2f;
        }
        aim.Y = MathF.Min(aim.Y, GroundY - 30);
        c.Pointer = aim;
        c.PointerPressed = true;
        c.PointerDown = true;
        _aiRelease = true;
        _aiCool = MathF.Max(_aiCool, 0.3f);
    }
}
