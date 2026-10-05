using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Capture;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 16 Inferno: a tower block is on fire and the flames spread from window to window. Drive the fire
/// engine along the street, angle the ladder and spray arcing water to douse them, and catch anyone
/// who jumps in the safety net.
/// </summary>
public sealed class Inferno : MiniGame, ICaptureHints
{
    public override int Number => 16;
    public override string Title => "Inferno";
    public override Category Category => Category.Arcade;
    public override string Tagline => "Douse the blazing tower block and catch the people who jump.";
    public override Color Accent => Pal.Orange;

    public override string[] HowToPlay =>
    [
        "Fire is spreading through the tower. Drive the engine, angle the ladder and spray water to put out burning windows.",
        "Trapped people will jump: catch them in the net. Lose three people, or let 15 windows burn, and the game is over.",
        "Fires score 50, catches 200. The tank refills when you stop spraying.",
    ];

    public override string[] DesktopControls => ["LEFT / RIGHT to drive, UP / DOWN to angle the ladder.", "Hold SPACE to spray water."];
    public override string[] TouchControls => ["Stick drives and angles the ladder.", "Hold SPRAY to spray water."];
    public override Pad Pad => Pad.Stick | Pad.Fire;
    public override string FireLabel => "SPRAY";
    public int CaptureTicks => 660;

    private const int Cols = 6, Rows = 6, MaxBlaze = 15;
    private const float BX = 150, BW = 340, RoofY = 40, WinW = 36, WinH = 26, FloorH = 40, FirstRowY = 54;
    private const float StreetY = 318, NetY = 296, LadderLen = 66, Gravity = 320, WaterSpeed = 345;

    private static readonly Dictionary<char, Color> Colours = new()
    {
        ['h'] = Pal.Brown, ['s'] = Pal.Skin, ['c'] = Pal.Sky, ['b'] = Pal.Navy, ['w'] = Pal.White,
    };

    private static readonly PixelArt[] PersonArt =
    [
        new(["s.hhh.s", "s.sss.s", ".csssc.", "..ccc..", "..ccc..", "..bbb..", "..b.b..", "..b.b.."], Colours),
        new(["..hhh..", "..sss..", "s.sss.s", ".ccccc.", "..ccc..", "..bbb..", "..b.b..", "..b.b.."], Colours),
        new(["s.hhh.s", "s.sss.s", ".csssc.", "..ccc..", "..ccc..", "..bbb..", ".b...b.", "b.....b"], Colours),
        new(["..hhh..", "..sss..", "..sss..", ".ccccc.", "s.ccc.s", "..bbb..", ".b...b.", "b.....b"], Colours),
    ];

    private struct Drop
    {
        public Vector2 Pos, Vel;
    }

    private struct Puff
    {
        public Vector2 Pos, Vel;
        public float Life, Max, Size;
        public bool Steam;
    }

    private sealed class Person
    {
        public Vector2 Pos, Vel;
        public int State; // 0 waiting at window, 1 falling, 2 bouncing off the net, 3 running away
        public float Timer;
        public int C, R;
        public bool Caught;
    }

    private struct Tower
    {
        public float X, W, H;
        public int Seed;
    }

    private static readonly Tower[] Skyline = BuildSkyline();

    private readonly float[,] _heat = new float[Cols, Rows];
    private readonly bool[,] _charred = new bool[Cols, Rows];
    private readonly float[,] _wet = new float[Cols, Rows];
    private readonly bool[,] _lit = new bool[Cols, Rows];
    private readonly List<Drop> _water = new();
    private readonly List<Puff> _smoke = new();
    private readonly List<Person> _people = new();

    private float _engX, _engVel, _angle;
    private int _facing;
    private float _tank;
    private bool _dry, _spraying;
    private float _levelTime, _igniteTimer, _personTimer, _splashCd, _banner, _alarmCd;
    private int _blaze, _fires, _catches;

    private static Tower[] BuildSkyline()
    {
        var rng = new Random(16);
        var list = new List<Tower>();
        float x = -10;
        while (x < 650)
        {
            float w = 30 + (float)rng.NextDouble() * 50;
            list.Add(new Tower { X = x, W = w, H = 60 + (float)rng.NextDouble() * 130, Seed = rng.Next() });
            x += w + (float)rng.NextDouble() * 6;
        }
        return list.ToArray();
    }

    protected override void Start()
    {
        Lives = 3;
        Level = 1;
        _engX = 320;
        _facing = 1;
        _angle = 1.25f;
        _tank = 1;
        _dry = false;
        Array.Clear(_heat);
        Array.Clear(_charred);
        Array.Clear(_wet);
        for (int c = 0; c < Cols; c++)
            for (int r = 0; r < Rows; r++)
                _lit[c, r] = Chance(0.45f);
        _water.Clear();
        _smoke.Clear();
        _people.Clear();
        _levelTime = 0;
        _igniteTimer = 2.5f;
        _personTimer = 5;
        _banner = 2;
        Ignite(RandInt(0, 3), RandInt(1, Rows));
        Ignite(RandInt(3, Cols), RandInt(0, 3));
        Ignite(RandInt(0, Cols), RandInt(0, Rows));
        CountBlaze();
    }

    private static RectF Window(int c, int r)
    {
        float cell = BW / Cols;
        return new RectF(BX + c * cell + (cell - WinW) / 2, FirstRowY + r * FloorH, WinW, WinH);
    }

    private Vector2 Pivot => new(_engX - _facing * 16, 298);
    private Vector2 Dir => new(_facing * MathF.Cos(_angle), -MathF.Sin(_angle));
    private Vector2 Nozzle => Pivot + Dir * LadderLen;

    private void Ignite(int c, int r)
    {
        if (_heat[c, r] > 0)
            return;
        _heat[c, r] = 0.15f;
        _wet[c, r] = 0;
        var w = Window(c, r);
        Fx.Burst(w.CenterX, w.CenterY, Pal.Orange, 10, 60, 0.5f, 2f, -40);
        Sound.Play(Sfx.Fuse, Rand(-0.3f, 0.3f), 0.5f);
    }

    private void CountBlaze()
    {
        _blaze = 0;
        for (int c = 0; c < Cols; c++)
            for (int r = 0; r < Rows; r++)
                if (_heat[c, r] > 0)
                    _blaze++;
        Status = "ABLAZE " + _blaze + "/" + MaxBlaze;
    }

    protected override void Update()
    {
        if (_banner > 0)
            _banner -= Dt;
        _levelTime += Dt;
        if (_levelTime > 40)
        {
            _levelTime = 0;
            Level++;
            _banner = 2.5f;
            AddScore(100 * Level, 320, 120, Pal.Cyan);
            Sound.Play(Sfx.LevelUp);
        }

        DriveEngine();
        Spray();
        UpdateFire();
        UpdatePeople();
        UpdateSmoke();
        CountBlaze();

        if (_blaze >= MaxBlaze)
        {
            Sound.Play(Sfx.BigExplode);
            Fx.Shake(6, 0.6f);
            EndGame(false, "The tower is lost to the flames!");
            return;
        }
        if (_blaze >= MaxBlaze - 3 && (_alarmCd -= Dt) <= 0)
        {
            Sound.Play(Sfx.Alarm, 0, 0.4f);
            _alarmCd = 2.5f;
        }
    }

    private void DriveEngine()
    {
        float target = In.AxisX * 210;
        _engVel = MathF2.Approach(_engVel, target, 900 * Dt);
        _engX += _engVel * Dt;
        if (_engX < 40 || _engX > 600)
        {
            _engX = MathF2.Clamp(_engX, 40, 600);
            _engVel = 0;
        }
        // The ladder always leans towards the tower.
        if (_engX < 312)
            _facing = 1;
        else if (_engX > 328)
            _facing = -1;
        _angle = MathF2.Clamp(_angle - In.AxisY * 1.5f * Dt, 0.35f, MathF.PI / 2);
        if (MathF.Abs(_engVel) > 10)
            Sound.Loop(LoopSfx.Engine, true, -0.5f + MathF.Abs(_engVel) / 400, 0.35f);
    }

    private void Spray()
    {
        _spraying = In.Fire && !_dry;
        if (_spraying)
        {
            _tank -= 0.12f * Dt;
            if (_tank <= 0)
            {
                _tank = 0;
                _dry = true;
                Sound.Play(Sfx.Wrong, -0.3f, 0.6f);
            }
            var n = Nozzle;
            for (int i = 0; i < 3; i++)
            {
                float a = MathF.Atan2(Dir.Y, Dir.X) + Rand(-0.035f, 0.035f);
                _water.Add(new Drop { Pos = n, Vel = MathF2.FromAngle(a, WaterSpeed * Rand(0.94f, 1.04f)) + new Vector2(_engVel * 0.3f, 0) });
            }
            Sound.Loop(LoopSfx.Wind, true, 0.6f, 0.55f);
        }
        else
        {
            _tank = MathF.Min(1, _tank + 0.3f * Dt);
            if (_dry && _tank > 0.3f)
                _dry = false;
        }
        if (_splashCd > 0)
            _splashCd -= Dt;

        for (int i = _water.Count - 1; i >= 0; i--)
        {
            var d = _water[i];
            d.Vel.Y += Gravity * Dt;
            d.Pos += d.Vel * Dt;
            bool remove = false;
            if (d.Pos.Y > StreetY + 4 || d.Pos.X < -10 || d.Pos.X > 650)
            {
                remove = true;
                if (d.Pos.Y > StreetY && Chance(0.15f))
                    Fx.Spark(d.Pos.X, StreetY + 2, Rand(-30, 30), Rand(-60, -20), Pal.Sky, 0.3f, 1.2f, 200);
            }
            else if (d.Pos.X >= BX && d.Pos.X < BX + BW && d.Pos.Y >= FirstRowY && d.Pos.Y < FirstRowY + Rows * FloorH)
            {
                int c = (int)((d.Pos.X - BX) / (BW / Cols)), r = (int)((d.Pos.Y - FirstRowY) / FloorH);
                if (c >= 0 && c < Cols && r >= 0 && r < Rows && _heat[c, r] > 0 && Window(c, r).Inflate(3, 3).Contains(d.Pos))
                {
                    remove = true;
                    Douse(c, r, d.Pos);
                }
            }
            if (remove)
                _water.RemoveAt(i);
            else
                _water[i] = d;
        }
    }

    private void Douse(int c, int r, Vector2 at)
    {
        _heat[c, r] -= 0.009f;
        if (Chance(0.25f) && _smoke.Count < 400)
            _smoke.Add(new Puff { Pos = at, Vel = new Vector2(Rand(-15, 15), Rand(-40, -20)), Life = 1.2f, Max = 1.2f, Size = Rand(3, 6), Steam = true });
        if (_splashCd <= 0)
        {
            Sound.Play(Sfx.Splash, Rand(-0.2f, 0.4f), 0.35f);
            _splashCd = 0.18f;
        }
        if (_heat[c, r] <= 0)
        {
            _heat[c, r] = 0;
            _charred[c, r] = true;
            _wet[c, r] = 5;
            _fires++;
            var w = Window(c, r);
            AddScore(50, w.CenterX, w.Y - 4, Pal.Sky);
            Sound.Play(Sfx.Pop, 0.2f, 0.7f);
            for (int i = 0; i < 10; i++)
                _smoke.Add(new Puff { Pos = new Vector2(w.CenterX + Rand(-12, 12), w.CenterY), Vel = new Vector2(Rand(-20, 20), Rand(-50, -20)), Life = 1.6f, Max = 1.6f, Size = Rand(4, 8), Steam = true });
        }
    }

    private void UpdateFire()
    {
        float grow = 0.07f + 0.02f * Level;
        float spread = 0.045f + 0.022f * (Level - 1);
        for (int c = 0; c < Cols; c++)
            for (int r = 0; r < Rows; r++)
            {
                if (_wet[c, r] > 0)
                    _wet[c, r] -= Dt;
                float h = _heat[c, r];
                if (h <= 0)
                    continue;
                _heat[c, r] = MathF.Min(1, h + grow * Dt);
                if (h > 0.5f)
                {
                    TrySpread(c, r - 1, spread * 1.8f * h);
                    TrySpread(c, r + 1, spread * 0.6f * h);
                    TrySpread(c - 1, r, spread * h);
                    TrySpread(c + 1, r, spread * h);
                }
                if (_smoke.Count < 400 && Chance(h * 0.12f))
                {
                    var w = Window(c, r);
                    _smoke.Add(new Puff
                    {
                        Pos = new Vector2(w.CenterX + Rand(-10, 10), w.Y + 2), Vel = new Vector2(Rand(-6, 10), Rand(-38, -22)),
                        Life = 2.2f, Max = 2.2f, Size = Rand(4, 7),
                    });
                }
            }

        _igniteTimer -= Dt;
        if (_igniteTimer <= 0)
        {
            _igniteTimer = MathF.Max(1.8f, 5.5f - Level * 0.5f) * Rand(0.7f, 1.3f);
            for (int tries = 0; tries < 10; tries++)
            {
                int c = RandInt(0, Cols), r = RandInt(0, Rows);
                if (_heat[c, r] <= 0 && _wet[c, r] <= 0)
                {
                    Ignite(c, r);
                    break;
                }
            }
        }
    }

    private void TrySpread(int c, int r, float rate)
    {
        if (c < 0 || r < 0 || c >= Cols || r >= Rows || _heat[c, r] > 0 || _wet[c, r] > 0)
            return;
        if (Chance(rate * Dt))
            Ignite(c, r);
    }

    private void UpdatePeople()
    {
        _personTimer -= Dt;
        int waiting = 0;
        foreach (var p in _people)
            if (p.State <= 1)
                waiting++;
        if (_personTimer <= 0 && waiting < 1 + Level / 2)
        {
            _personTimer = Rand(4.5f, 8.5f) / (1 + 0.08f * Level);
            for (int tries = 0; tries < 12; tries++)
            {
                int c = RandInt(0, Cols), r = RandInt(0, Rows - 1);
                if (_heat[c, r] < 0.25f || Occupied(c, r))
                    continue;
                var w = Window(c, r);
                _people.Add(new Person { C = c, R = r, Pos = new Vector2(w.CenterX, w.Bottom), Timer = MathF.Max(2.2f, 5f - 0.3f * Level) });
                Sound.Play(Sfx.Bell, 0.5f, 0.5f);
                break;
            }
        }

        for (int i = _people.Count - 1; i >= 0; i--)
        {
            var p = _people[i];
            switch (p.State)
            {
                case 0:
                    if (_heat[p.C, p.R] <= 0)
                    {
                        // Fire out before they jumped: rescued from inside.
                        AddScore(100, p.Pos.X, p.Pos.Y - 20, Pal.Lime);
                        Sound.Play(Sfx.Correct, 0, 0.6f);
                        _people.RemoveAt(i);
                        continue;
                    }
                    p.Timer -= Dt;
                    if (p.Timer <= 0)
                    {
                        p.State = 1;
                        p.Vel = new Vector2(Rand(-35, 35), -70);
                        Sound.Play(Sfx.Whoosh, 0.3f, 0.6f);
                    }
                    break;
                case 1:
                {
                    float prevY = p.Pos.Y;
                    p.Vel.Y += 260 * Dt;
                    p.Pos += p.Vel * Dt;
                    p.Pos.X = MathF2.Clamp(p.Pos.X, 8, 632);
                    if (prevY < NetY && p.Pos.Y >= NetY && MathF.Abs(p.Pos.X - _engX) < 34)
                    {
                        p.State = 2;
                        p.Caught = true;
                        p.Pos.Y = NetY;
                        p.Vel = new Vector2(p.Pos.X < 320 ? -70 : 70, -160);
                        _catches++;
                        AddScore(200, p.Pos.X, NetY - 24, Pal.Gold);
                        Sound.Play(Sfx.Bounce, 0.2f);
                        Fx.Burst(p.Pos.X, NetY, Pal.Gold, 12, 70, 0.4f, 2f);
                    }
                    else if (p.Pos.Y >= StreetY)
                    {
                        Fx.Burst(p.Pos.X, StreetY, Pal.LightGrey, 14, 60, 0.6f, 2.5f, 0, false);
                        Fx.Float("LOST!", p.Pos.X, StreetY - 30, Pal.Red);
                        Sound.Play(Sfx.Thud);
                        _people.RemoveAt(i);
                        if (Lives <= 1)
                        {
                            Lives = 0;
                            EndGame(false, "Three people were lost.");
                            return;
                        }
                        LoseLife();
                        continue;
                    }
                    break;
                }
                case 2:
                    p.Vel.Y += 300 * Dt;
                    p.Pos += p.Vel * Dt;
                    if (p.Pos.Y >= StreetY && p.Vel.Y > 0)
                    {
                        p.Pos.Y = StreetY;
                        p.State = 3;
                        Sound.Play(Sfx.Land, 0, 0.5f);
                    }
                    break;
                default:
                    p.Pos.X += (p.Vel.X < 0 ? -1 : 1) * 90 * Dt;
                    if (p.Pos.X < -20 || p.Pos.X > 660)
                    {
                        _people.RemoveAt(i);
                        continue;
                    }
                    break;
            }
        }
    }

    private bool Occupied(int c, int r)
    {
        foreach (var p in _people)
            if (p.State == 0 && p.C == c && p.R == r)
                return true;
        return false;
    }

    private void UpdateSmoke()
    {
        for (int i = _smoke.Count - 1; i >= 0; i--)
        {
            var s = _smoke[i];
            s.Life -= Dt;
            if (s.Life <= 0)
            {
                _smoke.RemoveAt(i);
                continue;
            }
            s.Pos += s.Vel * Dt;
            s.Vel.X += 6 * Dt;
            s.Size += 6 * Dt;
            _smoke[i] = s;
        }
    }

    // ------------------------------------------------------------------ drawing

    public override void Draw(Gfx g)
    {
        float fire = _blaze / (float)MaxBlaze;
        g.GradientV(0, 0, 640, StreetY, new Color(8, 8, 28), Pal.Lerp(new Color(40, 22, 50), new Color(110, 40, 30), fire));
        Backdrops.Stars(g, Time, 0, 16, new RectF(0, 0, 640, 200), 70);
        g.Glow(560, 64, 50, Pal.Ice, 0.25f);
        g.Circle(560, 64, 16, new Color(230, 230, 210));
        g.Circle(554, 60, 4, new Color(200, 200, 185));
        g.Circle(564, 70, 3, new Color(205, 205, 190));
        g.Glow(320, 180, 320, Pal.Orange, 0.08f + 0.25f * fire);

        // Distant city.
        foreach (var t in Skyline)
        {
            g.Rect(t.X, StreetY - t.H, t.W, t.H, new Color(18, 16, 40));
            int lights = (int)(t.W / 9);
            for (int k = 0; k < lights * 6; k++)
            {
                int hash = (t.Seed >> (k % 24)) ^ (k * 2654435);
                if ((hash & 7) != 0)
                    continue;
                float lx = t.X + 4 + (k % lights) * 9, ly = StreetY - t.H + 8 + (k / lights) * 12;
                if (ly < StreetY - 4)
                    g.Rect(lx, ly, 3, 4, new Color(120, 110, 60));
            }
        }

        DrawBuilding(g);

        // Smoke and steam.
        foreach (var s in _smoke)
        {
            float k = s.Life / s.Max;
            var col = s.Steam ? new Color(220, 230, 240) * (0.22f * k) : new Color(46, 40, 46) * (0.3f * k);
            g.Circle(s.Pos.X, s.Pos.Y, s.Size * 1.4f, col * 0.5f);
            g.Circle(s.Pos.X, s.Pos.Y, s.Size, col);
        }

        // Street.
        g.Rect(0, StreetY - 6, 640, 8, new Color(90, 90, 100));
        g.Rect(0, StreetY - 6, 640, 1, new Color(140, 140, 150));
        g.GradientV(0, StreetY + 2, 640, 360 - StreetY, new Color(36, 36, 44), new Color(16, 16, 22));
        for (int i = 0; i < 12; i++)
            g.Rect(i * 60 + 10, 346, 30, 2, new Color(200, 190, 120) * 0.6f);
        g.Glow(320, StreetY + 10, 260, Pal.Orange, 0.1f * fire);

        // Running / bouncing people behind the engine.
        foreach (var p in _people)
            if (p.State >= 2)
                g.PixelsCentered(PersonArt[p.State == 2 ? 2 : (int)(Time * 8) % 2], p.Pos.X, p.Pos.Y - 8, 2f, p.Vel.X < 0);

        DrawEngine(g, _engX, _facing, _angle, 1f, Time, _spraying);

        // Water.
        foreach (var d in _water)
        {
            g.Glow(d.Pos, 6, Pal.Sky, 0.3f);
            g.Circle(d.Pos.X, d.Pos.Y, 1.7f, Pal.Ice);
        }

        // Falling people (in front of everything).
        foreach (var p in _people)
            if (p.State == 1)
            {
                g.Glow(p.Pos.X, p.Pos.Y - 8, 14, Pal.White, 0.2f);
                g.PixelsCentered(PersonArt[2 + (int)(Time * 10) % 2], p.Pos.X, p.Pos.Y - 8, 2f);
                // Landing marker on the street.
                float t = MathF2.Pulse(Time, 0.4f);
                g.Ellipse(p.Pos.X, StreetY + 4, 12, 3, Pal.Red * (0.3f + 0.4f * t));
            }

        // Water gauge.
        g.Text("WATER", 10, 336, 1f, _dry ? Pal.Red : Pal.Sky);
        g.Rect(44, 337, 80, 6, Color.Black * 0.6f);
        g.GradientH(44, 337, 80 * _tank, 6, Pal.Water, Pal.Sky);
        g.RectOutline(43, 336, 82, 8, 1, Pal.LightGrey * 0.6f);
        g.Text("SAVED " + _catches, 630, 336, 1f, Pal.Gold, Align.Right);

        if (_banner > 0)
        {
            float a = MathF.Min(1, _banner);
            g.TextShadow("LEVEL " + Level, 320, 130, 3f, Pal.Yellow * a, Align.Center);
            if (Level > 1)
                g.TextShadow("THE FIRE SPREADS FASTER", 320, 160, 1.5f, Pal.Orange * a, Align.Center);
        }
    }

    private void DrawBuilding(Gfx g)
    {
        float top = RoofY, bottom = StreetY - 6;
        // Roof furniture.
        g.Rect(BX + 40, top - 14, 30, 14, new Color(70, 50, 50));
        g.Rect(BX + 36, top - 16, 38, 3, new Color(90, 70, 65));
        g.Rect(BX + BW - 60, top - 26, 2, 26, Pal.Grey);
        if ((int)(Time * 1.5f) % 2 == 0)
        {
            g.Glow(BX + BW - 59, top - 27, 8, Pal.Red, 0.8f);
            g.Circle(BX + BW - 59, top - 27, 1.5f, Pal.Red);
        }
        // Facade.
        g.GradientV(BX, top, BW, bottom - top, new Color(112, 52, 46), new Color(70, 30, 32));
        for (float y = top + 4; y < bottom; y += 6)
            g.Rect(BX, y, BW, 1, Color.Black * 0.12f);
        g.Rect(BX - 4, top - 2, BW + 8, 5, new Color(150, 110, 95));
        for (int r = 0; r < Rows; r++)
            g.Rect(BX - 2, FirstRowY + r * FloorH + WinH + 4, BW + 4, 3, new Color(140, 100, 88));
        g.GradientH(BX, top, 10, bottom - top, Color.Black * 0.3f, Color.Transparent);
        g.GradientH(BX + BW - 10, top, 10, bottom - top, Color.Transparent, Color.Black * 0.3f);

        // Neon sign and entrance.
        float signY = FirstRowY + Rows * FloorH - 6;
        g.Rect(BX + BW / 2 - 40, signY + 4, 80, bottom - signY - 4, new Color(30, 30, 50));
        g.GradientV(BX + BW / 2 - 34, signY + 8, 68, bottom - signY - 8, new Color(60, 80, 110), new Color(30, 40, 60));
        g.Rect(BX + BW / 2 - 1, signY + 8, 2, bottom - signY - 8, new Color(20, 20, 30));
        g.Glow(BX + 60, signY + 10, 30, Pal.Pink, 0.35f);
        g.Text("HOTEL", BX + 60, signY + 6, 1f, Pal.Pink, Align.Center);

        for (int c = 0; c < Cols; c++)
            for (int r = 0; r < Rows; r++)
                DrawWindow(g, Window(c, r), _heat[c, r], _charred[c, r], _wet[c, r], _lit[c, r], Time + c * 1.7f + r * 0.9f, 1f);

        // People waiting at windows.
        foreach (var p in _people)
            if (p.State == 0)
            {
                var w = Window(p.C, p.R);
                g.PixelsCentered(PersonArt[(int)(Time * 6) % 2], w.CenterX, w.Bottom - 8, 2f);
                bool flash = p.Timer < 1.2f && (int)(Time * 8) % 2 == 0;
                g.RoundRect(w.CenterX - 17, w.Y - 12, 34, 11, 3, flash ? Pal.Red : Pal.White);
                g.Text("HELP!", w.CenterX, w.Y - 10, 1f, flash ? Pal.White : Pal.Red, Align.Center);
            }
    }

    private static void DrawWindow(Gfx g, RectF w, float heat, bool charred, float wet, bool lit, float t, float s)
    {
        g.Rect(w.X - 2 * s, w.Y - 2 * s, w.W + 4 * s, w.H + 4 * s, new Color(170, 140, 120));
        if (heat > 0)
        {
            g.GradientV(w.X, w.Y, w.W, w.H, new Color(255, 200, 60), new Color(200, 40, 10));
            g.Glow(w.CenterX, w.CenterY, (26 + 30 * heat) * s, Pal.Orange, 0.35f + 0.4f * heat);
            // Soot above the window.
            g.GradientV(w.X, w.Y - 14 * s, w.W, 12 * s, Color.Transparent, Color.Black * (0.45f * heat));
            for (int i = 0; i < 4; i++)
            {
                float fx = w.X + w.W * (0.15f + i * 0.23f);
                float flick = 0.7f + 0.3f * MathF.Sin(t * 9 + i * 2.1f) + 0.15f * MathF.Sin(t * 23 + i);
                float fh = (8 + 26 * heat) * flick * s;
                float bw = 6 * s;
                g.Triangle(new Vector2(fx - bw, w.Bottom - 4 * s), new Vector2(fx + bw, w.Bottom - 4 * s), new Vector2(fx + MathF.Sin(t * 5 + i) * 3 * s, w.Bottom - 4 * s - fh - 6 * s), Pal.Orange);
                g.Triangle(new Vector2(fx - bw * 0.5f, w.Bottom - 3 * s), new Vector2(fx + bw * 0.5f, w.Bottom - 3 * s), new Vector2(fx + MathF.Sin(t * 6 + i) * 2 * s, w.Bottom - 3 * s - fh * 0.6f - 2 * s), Pal.Yellow);
            }
            g.Glow(w.CenterX, w.Y, 18 * s * (0.5f + heat), Pal.Yellow, 0.3f * heat);
            // Window bars.
            g.Rect(w.CenterX - 0.75f * s, w.Y, 1.5f * s, w.H, new Color(60, 20, 10) * 0.7f);
        }
        else if (charred)
        {
            g.GradientV(w.X, w.Y, w.W, w.H, new Color(30, 26, 26), new Color(14, 12, 14));
            g.GradientV(w.X, w.Y - 12 * s, w.W, 12 * s, Color.Transparent, Color.Black * 0.4f);
            g.Rect(w.CenterX - 0.75f * s, w.Y, 1.5f * s, w.H, new Color(60, 50, 50));
            if (wet > 0)
                for (int i = 0; i < 3; i++)
                {
                    float dy = ((t * 30 + i * 9) % (w.H + 8)) * s;
                    g.Rect(w.X + (6 + i * 11) * s, w.Bottom + dy * 0.3f, 1.2f * s, 3 * s, Pal.Sky * 0.7f);
                }
        }
        else
        {
            if (lit)
                g.GradientV(w.X, w.Y, w.W, w.H, new Color(250, 220, 140), new Color(200, 150, 70));
            else
                g.GradientV(w.X, w.Y, w.W, w.H, new Color(40, 60, 100), new Color(20, 26, 50));
            g.Rect(w.CenterX - 0.75f * s, w.Y, 1.5f * s, w.H, new Color(170, 140, 120));
            g.Rect(w.X, w.Y + w.H * 0.45f, w.W, 1.2f * s, new Color(170, 140, 120));
            g.Line(w.X + 4 * s, w.Bottom - 4 * s, w.X + 12 * s, w.Y + 4 * s, 1.5f * s, Color.White * 0.12f);
        }
    }

    private static void DrawEngine(Gfx g, float x, int facing, float angle, float s, float time, bool spraying)
    {
        float baseY = 300;
        // Ladder behind the body.
        var pivot = new Vector2(x - facing * 16 * s, baseY - 2 * s);
        var dir = new Vector2(facing * MathF.Cos(angle), -MathF.Sin(angle));
        var perp = new Vector2(-dir.Y, dir.X) * 2.6f * s;
        var tip = pivot + dir * LadderLen * s;
        g.Line(pivot + perp, tip + perp, 1.6f * s, Pal.Silver);
        g.Line(pivot - perp, tip - perp, 1.6f * s, Pal.Silver);
        for (float k = 6; k < LadderLen; k += 6)
        {
            var p = pivot + dir * k * s;
            g.Line(p + perp, p - perp, 1.1f * s, Pal.LightGrey);
        }
        g.Line(tip, tip + dir * 6 * s, 3.5f * s, Pal.Gold);
        if (spraying)
            g.Glow(tip + dir * 6 * s, 10 * s, Pal.Sky, 0.6f);

        // Body.
        g.RoundRect(x - 38 * s, baseY + 2 * s, 76 * s, 22 * s, 3 * s, new Color(200, 24, 24));
        g.GradientV(x - 38 * s, baseY + 2 * s, 76 * s, 8 * s, new Color(240, 70, 60), new Color(200, 24, 24));
        g.Rect(x - 38 * s, baseY + 14 * s, 76 * s, 3 * s, Pal.White);
        for (int i = 0; i < 4; i++)
            g.Rect(x - 30 * s + i * 11 * s + (facing < 0 ? 22 * s : 0), baseY + 5 * s, 8 * s, 7 * s, new Color(150, 10, 10));
        // Cab.
        float cx = x + facing * 30 * s;
        g.RoundRect(cx - 10 * s, baseY - 8 * s, 20 * s, 32 * s, 3 * s, new Color(220, 30, 30));
        g.Rect(cx + (facing > 0 ? -1 : -8) * s, baseY - 4 * s, 9 * s, 9 * s, new Color(140, 200, 240));
        g.Rect(cx + facing * 9 * s - 1.5f * s, baseY + 12 * s, 3 * s, 4 * s, Pal.Yellow);
        g.Glow(cx + facing * 12 * s, baseY + 14 * s, 14 * s, Pal.Yellow, 0.4f);
        // Light bar.
        bool blink = (int)(time * 6) % 2 == 0;
        g.Rect(cx - 6 * s, baseY - 11 * s, 5 * s, 3 * s, blink ? Pal.Blue : new Color(20, 30, 80));
        g.Rect(cx + 1 * s, baseY - 11 * s, 5 * s, 3 * s, !blink ? Pal.Blue : new Color(20, 30, 80));
        g.Glow(cx + (blink ? -4 : 4) * s, baseY - 10 * s, 26 * s, Pal.Blue, 0.6f);
        // Turntable.
        g.Circle(pivot.X, pivot.Y, 5 * s, Pal.Grey);
        g.Circle(pivot.X, pivot.Y, 2.5f * s, Pal.DarkGrey);
        // Wheels.
        for (int i = -1; i <= 1; i += 2)
        {
            g.Circle(x + i * 24 * s, baseY + 25 * s, 7 * s, new Color(20, 20, 24));
            g.Circle(x + i * 24 * s, baseY + 25 * s, 3 * s, Pal.Silver);
        }
        // Safety net held across the top.
        float ny = NetY + 2;
        g.Line(x - 30 * s, baseY + 2 * s, x - 34 * s, ny - 2, 2 * s, Pal.Silver);
        g.Line(x + 30 * s, baseY + 2 * s, x + 34 * s, ny - 2, 2 * s, Pal.Silver);
        g.Ellipse(x, ny, 36 * s, 4 * s, Pal.Gold * 0.85f);
        g.Ellipse(x, ny, 30 * s, 2.5f * s, new Color(120, 90, 20));
        for (int i = -3; i <= 3; i++)
            g.Line(x + i * 9 * s, ny - 3 * s, x + i * 9 * s, ny + 3 * s, 0.8f * s, Pal.Gold);
    }

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        g.GradientV(r.X, r.Y, r.W, r.H, new Color(10, 8, 30), new Color(90, 34, 30));
        float s = r.H / 70f;
        g.Glow(r.CenterX, r.CenterY, r.W * 0.6f, Pal.Orange, 0.25f);
        // Tower.
        float bw = 70 * s, bx = r.CenterX - bw / 2 + 10 * s, by = r.Y + 6 * s, bh = r.H;
        g.GradientV(bx, by, bw, bh, new Color(112, 52, 46), new Color(70, 30, 32));
        g.Rect(bx - 2 * s, by - 2 * s, bw + 4 * s, 3 * s, new Color(150, 110, 95));
        for (int c = 0; c < 3; c++)
            for (int row = 0; row < 3; row++)
            {
                var w = new RectF(bx + 6 * s + c * 22 * s, by + 6 * s + row * 17 * s, 14 * s, 10 * s);
                bool burning = (c + row) % 2 == 0 || (c == 1 && row == 1);
                float heat = burning ? 0.6f + 0.4f * MathF.Sin(time * 2 + c + row) : 0;
                DrawWindow(g, w, MathF.Max(0, heat), !burning && row == 2, 0, c == 2, time + c * 1.3f + row, s * 0.45f);
            }
        // Engine and water arc.
        float ex = r.X + 26 * s + MathF.Sin(time * 0.8f) * 4 * s;
        float gy = r.Bottom - 6 * s;
        g.Rect(r.X, gy, r.W, 6 * s, new Color(30, 30, 38));
        var nozzle = new Vector2(ex + 6 * s, gy - 22 * s);
        for (int i = 0; i < 14; i++)
        {
            float k = (i / 14f + time * 1.4f) % 1f;
            var p = nozzle + new Vector2(k * 46 * s, -k * 46 * s + k * k * 26 * s);
            g.Glow(p, 4 * s, Pal.Sky, 0.5f);
            g.Circle(p.X, p.Y, 1.2f * s, Pal.Ice);
        }
        g.Line(ex - 6 * s, gy - 10 * s, nozzle.X, nozzle.Y, 2.4f * s, Pal.Silver);
        g.RoundRect(ex - 14 * s, gy - 12 * s, 28 * s, 9 * s, 2 * s, new Color(210, 28, 28));
        g.Rect(ex - 14 * s, gy - 7 * s, 28 * s, 1.5f * s, Pal.White);
        g.RoundRect(ex + 8 * s, gy - 16 * s, 8 * s, 13 * s, 2 * s, new Color(230, 34, 34));
        g.Rect(ex + 11 * s, gy - 14 * s, 4 * s, 4 * s, Pal.Sky);
        g.Glow(ex + 12 * s, gy - 17 * s, 9 * s, (int)(time * 5) % 2 == 0 ? Pal.Blue : Pal.Red, 0.8f);
        g.Circle(ex - 8 * s, gy - 2 * s, 3 * s, Color.Black);
        g.Circle(ex + 9 * s, gy - 2 * s, 3 * s, Color.Black);
        // A jumper.
        float jy = r.Y + 20 * s + ((time * 30) % 40) * s;
        g.PixelsCentered(PersonArt[2 + (int)(time * 8) % 2], bx + bw - 6 * s, jy, 0.9f * s);
    }

    /// <summary>Where the engine must stand for the stream at ladder angle a (leaning f) to pass through (wx, wy).</summary>
    private static float AimX(int f, float a, float wx, float wy)
    {
        float nx = -f * 16 + f * MathF.Cos(a) * LadderLen, ny = 298 - MathF.Sin(a) * LadderLen;
        float vy = MathF.Sin(a) * WaterSpeed;
        float disc = vy * vy - 2 * Gravity * (ny - wy);
        if (ny <= wy || disc < 0)
            return float.NaN;
        float t = (vy - MathF.Sqrt(disc)) / Gravity;
        return wx - (nx + f * MathF.Cos(a) * WaterSpeed * t);
    }

    public override void AutoPlay(Controls c)
    {
        float targetX = _engX;
        bool wantSpray = false;
        float wantAngle = MathF.PI / 2;

        Person faller = null;
        foreach (var p in _people)
            if (p.State == 1 && (faller == null || p.Pos.Y > faller.Pos.Y))
                faller = p;
        if (faller != null)
        {
            // Predict where they cross the net.
            float a = 130, b = faller.Vel.Y, cc = faller.Pos.Y - NetY;
            float t = (-b + MathF.Sqrt(MathF.Max(0, b * b - 4 * a * cc))) / (2 * a);
            targetX = faller.Pos.X + faller.Vel.X * t;
        }
        else
        {
            // Pick the most urgent fire: people first, then the hottest, preferring nearby ones.
            float best = float.MinValue;
            int bc = -1, br = 0;
            for (int col = 0; col < Cols; col++)
                for (int row = 0; row < Rows; row++)
                {
                    if (_heat[col, row] <= 0)
                        continue;
                    var w = Window(col, row);
                    float score = _heat[col, row] + (Occupied(col, row) ? 3 : 0) - MathF.Abs(w.CenterX - _engX) / 300 + row * 0.05f;
                    if (score > best)
                    {
                        best = score;
                        bc = col;
                        br = row;
                    }
                }
            if (bc >= 0)
            {
                var w = Window(bc, br);
                float bestCost = float.MaxValue;
                int bestF = 0;
                for (float a = 0.35f; a <= MathF.PI / 2 + 0.001f; a += 0.04f)
                    for (int f = -1; f <= 1; f += 2)
                    {
                        float req = AimX(f, a, w.CenterX, w.CenterY);
                        if (float.IsNaN(req) || req < 40 || req > 600 || (f > 0 ? req > 316 : req < 324))
                            continue;
                        float cost = MathF.Abs(req - _engX) + MathF.Abs(a - _angle) * 60;
                        if (cost < bestCost)
                        {
                            bestCost = cost;
                            targetX = req;
                            wantAngle = a;
                            bestF = f;
                        }
                    }
                wantSpray = bestF == _facing && MathF.Abs(targetX - _engX) < 10 && MathF.Abs(wantAngle - _angle) < 0.06f;
            }
        }
        float dx = targetX - _engX;
        float ax = MathF.Abs(dx) < 4 ? 0 : MathF2.Clamp(dx / 30, -1, 1);
        float ay = _angle < wantAngle - 0.02f ? -1 : _angle > wantAngle + 0.02f ? 1 : 0;
        c.SetDirections(ax, ay);
        c.Fire = wantSpray && (!_dry);
        c.FirePressed = c.Fire && !_spraying;
    }
}
