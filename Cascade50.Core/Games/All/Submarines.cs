using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 45 Submarines: captain a destroyer and drop depth charges on the submarines cruising beneath,
/// while dodging the torpedoes and mines they send up at you. Deeper boats score more.
/// </summary>
public sealed class Submarines : MiniGame
{
    public override int Number => 45;
    public override string Title => "Submarines";
    public override Category Category => Category.Shooter;
    public override string Tagline => "Hunt the submarines below with depth charges, and dodge their torpedoes.";
    public override Color Accent => Pal.Sky;
    public override Pad Pad => Pad.Horizontal | Pad.Fire | Pad.Alt;
    public override string FireLabel => "DROP";
    public override string AltLabel => "BIG";

    public override string[] HowToPlay =>
    [
        "Steer your destroyer and drop depth charges on the submarines below. Up to 3 can sink at once.",
        "Subs fire torpedoes and mines up at you: dodge them. Each wave also gives you 3 big charges with a huge blast.",
        "Shallow subs score 50, mid 100, deep 200. Sink the whole wave for a bonus. 3 lives.",
    ];

    public override string[] DesktopControls => ["LEFT / RIGHT to steer, SPACE to drop a charge.", "X or SHIFT: big depth charge."];
    public override string[] TouchControls => ["Steer with the pad, DROP a charge, BIG for a big charge."];

    private const float SurfaceY = 100, SeabedY = 332, ShipY = SurfaceY - 9;

    private static Dictionary<char, Color> Colours(Color hull, Color dark) => new()
    {
        ['s'] = hull, ['S'] = Pal.Lighten(hull, 0.25f), ['k'] = dark, ['w'] = new Color(255, 230, 120), ['r'] = new Color(170, 30, 30),
        ['g'] = new Color(150, 160, 175), ['G'] = new Color(200, 210, 225), ['d'] = new Color(90, 95, 110), ['y'] = Pal.Yellow,
        ['W'] = Pal.White, ['n'] = new Color(40, 50, 80),
    };

    private static readonly PixelArt Ship = new(
    [
        "..............y...................",
        "..............W...................",
        ".............WWW..................",
        "...........GGGGGGG......d.........",
        "..........GGnGnGnGG....ddd........",
        "....dd...GGGGGGGGGGG...ddd...d....",
        "GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG",
        ".gggggggggggggggggggggggggggggggg.",
        "..gggggggggggggggggggggggggggggg..",
        "...rrrrrrrrrrrrrrrrrrrrrrrrrrrr...",
    ], Colours(Pal.Grey, Pal.DarkGrey));

    private static readonly string[] SubRows =
    [
        "..........kk..............",
        "..........k...............",
        "........SSSs..............",
        "........sssss.............",
        "..SSSSSSSSSSSSSSSSSSSS....",
        ".sssssssssssssssssssssss..",
        "ssssswsssswsssswsssssssss.",
        ".sssssssssssssssssssssss..",
        "..kkkkkkkkkkkkkkkkkkkk....",
    ];

    private static readonly PixelArt[] SubArt =
    [
        new(SubRows, Colours(new Color(150, 160, 170), new Color(70, 76, 90))),
        new(SubRows, Colours(new Color(70, 120, 80), new Color(30, 60, 40))),
        new(SubRows, Colours(new Color(60, 50, 70), new Color(25, 20, 30))),
    ];

    private static readonly int[] SubPoints = [50, 100, 200];

    private sealed class Sub
    {
        public float X, Y, Vx, FireTimer, SinkTimer = -1, Tilt;
        public int Type;
    }

    private struct Charge
    {
        public Vector2 Pos;
        public float Vy;
        public bool Big;
    }

    private struct Shot
    {
        public Vector2 Pos;
        public float Vy, Phase, Life;
        public bool Mine, Surfaced;
    }

    private struct Blast
    {
        public Vector2 Pos;
        public float T, Radius;
    }

    private struct Fish
    {
        public float X, Y, Speed, Size;
        public Color Colour;
    }

    private readonly List<Sub> _subs = new();
    private readonly List<Charge> _charges = new();
    private readonly List<Shot> _shots = new();
    private readonly List<Blast> _blasts = new();
    private readonly Fish[] _fish = new Fish[14];
    private float _shipX = 320, _shipVx, _respawn, _invuln, _banner, _ping;
    private int _toSink, _bigCharges, _wave;
    private float _spawnTimer;
    private bool _faceLeft;

    protected override void Start()
    {
        Lives = 3;
        for (int i = 0; i < _fish.Length; i++)
            _fish[i] = new Fish
            {
                X = Rand(0, 640), Y = Rand(130, 320), Speed = Rand(12, 30) * (Chance(0.5f) ? 1 : -1), Size = Rand(2, 4),
                Colour = Pick(Pal.Orange, Pal.Yellow, Pal.Sky, Pal.Pink, new Color(120, 220, 200)),
            };
        NewWave(1);
    }

    private void NewWave(int wave)
    {
        _wave = wave;
        Level = wave;
        _toSink = 5 + wave * 2;
        _bigCharges = 3;
        _spawnTimer = 1.5f;
        _banner = 2.2f;
        _shots.Clear();
    }

    private int SubsLeft => _toSink;

    protected override void Update()
    {
        if (_banner > 0)
            _banner -= Dt;
        if (_invuln > 0)
            _invuln -= Dt;
        Status = "WAVE " + _wave + "  SUBS " + SubsLeft;

        // Destroyer.
        if (_respawn > 0)
        {
            _respawn -= Dt;
            if (_respawn <= 0)
                _invuln = 2;
        }
        else
        {
            _shipVx = MathF2.Approach(_shipVx, In.AxisX * 150, Dt * 320);
            _shipX += _shipVx * Dt;
            if (_shipX < 36 || _shipX > 604)
            {
                _shipX = MathF2.Clamp(_shipX, 36, 604);
                _shipVx = 0;
            }
            if (MathF.Abs(_shipVx) > 10)
                _faceLeft = _shipVx < 0;
            Sound.Loop(LoopSfx.Engine, true, -0.8f + MathF.Abs(_shipVx) / 400f, 0.25f);
            if (MathF.Abs(_shipVx) > 40 && Tick % 4 == 0)
                Fx.Spark(_shipX - MathF.Sign(_shipVx) * 34, SurfaceY + 1, -_shipVx * 0.3f, -20, Pal.White, 0.5f, 1.5f, 60, false);

            int maxCharges = 3 + (_wave >= 4 ? 1 : 0);
            if (In.FirePressed && _charges.Count < maxCharges)
                Drop(false);
            if (In.AltPressed && _bigCharges > 0 && _charges.Count < maxCharges)
            {
                _bigCharges--;
                Drop(true);
            }
        }

        // Sonar ping.
        if ((_ping += Dt) > 2.2f)
        {
            _ping = 0;
            if (_subs.Count > 0)
                Sound.Play(Sfx.Beep, -0.7f, 0.18f);
        }

        SpawnSubs();
        UpdateSubs();
        UpdateCharges();
        UpdateShots();
        for (int i = _blasts.Count - 1; i >= 0; i--)
        {
            var b = _blasts[i];
            b.T += Dt;
            if (b.T > 0.6f)
                _blasts.RemoveAt(i);
            else
                _blasts[i] = b;
        }
        for (int i = 0; i < _fish.Length; i++)
        {
            ref var f = ref _fish[i];
            f.X += f.Speed * Dt;
            if (f.X < -20)
                f.X = 660;
            if (f.X > 660)
                f.X = -20;
        }
        if (Tick % 9 == 0)
            Fx.Spark(Rand(0, 640), SeabedY, Rand(-4, 4), -Rand(20, 40), new Color(150, 210, 255) * 0.6f, 4, Rand(0.8f, 1.6f), 0, false);

        if (_toSink <= 0 && _subs.Count == 0)
        {
            AddScore(200 * _wave + 75 * _bigCharges, 320, 160, Pal.Cyan);
            Sound.Play(Sfx.LevelUp);
            NewWave(_wave + 1);
        }
    }

    private void Drop(bool big)
    {
        float sx = _shipX + (_faceLeft ? 26 : -26);
        _charges.Add(new Charge { Pos = new Vector2(sx, SurfaceY - 4), Vy = 30, Big = big });
        Sound.Play(Sfx.Splash, big ? -0.5f : 0.2f, 0.6f);
        Fx.Burst(sx, SurfaceY, Pal.White, 8, 50, 0.4f, 1.5f, 120, false);
    }

    private void SpawnSubs()
    {
        int maxOn = Math.Min(6, 2 + _wave / 2);
        int active = 0;
        foreach (var sub in _subs)
            if (sub.SinkTimer < 0)
                active++;
        if (active >= maxOn || active >= _toSink)
            return;
        _spawnTimer -= Dt;
        if (_spawnTimer > 0)
            return;
        _spawnTimer = MathF.Max(0.8f, 3f - _wave * 0.2f) * Rand(0.7f, 1.3f);
        int type = RandInt(0, Math.Min(3, 1 + (_wave + 1) / 2));
        if (_wave >= 2 && Chance(0.25f))
            type = Math.Min(2, type + 1);
        float y = type switch
        {
            0 => Rand(150, 185),
            1 => Rand(200, 250),
            _ => Rand(265, 312),
        };
        // Keep lanes apart.
        foreach (var s in _subs)
            if (MathF.Abs(s.Y - y) < 18)
                y = s.Y + (y > s.Y ? 20 : -20);
        y = MathF2.Clamp(y, 145, 315);
        bool fromLeft = Chance(0.5f);
        float speed = (type == 0 ? 55 : type == 1 ? 40 : 30) * (1 + _wave * 0.08f) * Rand(0.8f, 1.2f);
        _subs.Add(new Sub
        {
            X = fromLeft ? -40 : 680, Y = y, Vx = fromLeft ? speed : -speed, Type = type,
            FireTimer = Rand(1.5f, 3.5f),
        });
    }

    private void UpdateSubs()
    {
        for (int i = _subs.Count - 1; i >= 0; i--)
        {
            var s = _subs[i];
            if (s.SinkTimer >= 0)
            {
                s.SinkTimer += Dt;
                s.Y += 32 * Dt;
                s.X += s.Vx * 0.2f * Dt;
                s.Tilt = MathF2.Approach(s.Tilt, 0.5f * MathF.Sign(s.Vx), Dt);
                if (Tick % 5 == 0)
                    Fx.Spark(s.X + Rand(-14, 14), s.Y - 6, Rand(-5, 5), -40, Pal.Sky, 1.2f, 1.6f, 0, false);
                if (s.Y > SeabedY - 4)
                {
                    Fx.Burst(s.X, SeabedY, Pal.Sand, 16, 50, 0.8f, 2f, 40, false);
                    Sound.Play(Sfx.Thud, -0.6f, 0.5f);
                    _subs.RemoveAt(i);
                }
                continue;
            }
            s.X += s.Vx * Dt;
            s.Y += MathF.Sin(Time * 0.8f + i) * 4 * Dt;
            if ((s.Vx > 0 && s.X > 690) || (s.Vx < 0 && s.X < -50))
            {
                // Escaped: it comes round again later.
                _subs.RemoveAt(i);
                continue;
            }
            if (s.X > 20 && s.X < 620 && _respawn <= 0 && (s.FireTimer -= Dt) <= 0)
            {
                s.FireTimer = MathF.Max(1.2f, 4.2f - _wave * 0.3f) * Rand(0.7f, 1.4f);
                bool mine = s.Type == 0 ? Chance(0.6f) : Chance(0.2f);
                _shots.Add(new Shot
                {
                    Pos = new Vector2(s.X, s.Y - 8), Vy = mine ? -26 - _wave : -(48 + _wave * 7 + s.Type * 8), Mine = mine,
                    Phase = Rand(0, 6), Life = 0,
                });
                Sound.Play(mine ? Sfx.Pop : Sfx.Whoosh, mine ? -0.4f : -0.6f, 0.45f);
            }
        }
    }

    private void UpdateCharges()
    {
        for (int i = _charges.Count - 1; i >= 0; i--)
        {
            var c = _charges[i];
            c.Vy = MathF.Min(c.Vy + 140 * Dt, 62 + _wave * 2);
            c.Pos.Y += c.Vy * Dt;
            c.Pos.X += MathF.Sin(c.Pos.Y * 0.05f) * 6 * Dt;
            _charges[i] = c;
            if (Tick % 6 == 0)
                Fx.Spark(c.Pos.X, c.Pos.Y - 4, Rand(-6, 6), -30, new Color(180, 230, 255) * 0.7f, 0.8f, 1.2f, 0, false);
            bool boom = c.Pos.Y >= SeabedY - 4;
            foreach (var s in _subs)
                if (s.SinkTimer < 0 && MathF.Abs(s.X - c.Pos.X) < 26 && MathF.Abs(s.Y - c.Pos.Y) < 10)
                    boom = true;
            if (boom)
            {
                _charges.RemoveAt(i);
                Explode(c.Pos, c.Big ? 66 : 32);
            }
        }
    }

    private void Explode(Vector2 p, float radius)
    {
        _blasts.Add(new Blast { Pos = p, Radius = radius });
        bool big = radius > 40;
        Fx.Burst(p.X, p.Y, Pal.White, big ? 30 : 16, big ? 160 : 90, 0.5f, 2.5f);
        Fx.Burst(p.X, p.Y, Pal.Sky, big ? 40 : 20, big ? 120 : 70, 1.2f, 2f, -60, false);
        Fx.Shake(big ? 5 : 2.5f, 0.25f);
        Sound.Play(big ? Sfx.BigExplode : Sfx.Explode, Rand(-0.5f, -0.2f), 0.8f);
        int chain = 0;
        foreach (var s in _subs)
        {
            if (s.SinkTimer >= 0)
                continue;
            if (MathF.Abs(s.X - p.X) < radius + 22 && MathF.Abs(s.Y - p.Y) < radius + 6)
            {
                s.SinkTimer = 0;
                chain++;
                _toSink--;
                int pts = SubPoints[s.Type] * chain;
                AddScore(pts, s.X, s.Y - 18, s.Type == 2 ? Pal.Gold : Pal.Yellow);
                Fx.Explode(s.X, s.Y, 1.1f);
                Sound.Play(Sfx.Hit, -0.3f, 0.8f);
            }
        }
        if (chain > 1)
            Fx.Float("CHAIN x" + chain, p.X, p.Y - 34, Pal.Lime, 2f);
        // Blasts also destroy torpedoes and mines nearby.
        for (int i = _shots.Count - 1; i >= 0; i--)
            if (Vector2.Distance(_shots[i].Pos, p) < radius + 6)
            {
                Fx.Burst(_shots[i].Pos.X, _shots[i].Pos.Y, Pal.Orange, 10, 60, 0.4f, 2f);
                AddScore(10, _shots[i].Pos.X, _shots[i].Pos.Y - 10, Pal.Orange);
                _shots.RemoveAt(i);
            }
        if (_toSink < 0)
            _toSink = 0;
    }

    private void UpdateShots()
    {
        for (int i = _shots.Count - 1; i >= 0; i--)
        {
            var s = _shots[i];
            s.Life += Dt;
            if (!s.Surfaced)
            {
                s.Pos.Y += s.Vy * Dt;
                if (s.Mine)
                    s.Pos.X += MathF.Sin(s.Life * 2 + s.Phase) * 10 * Dt;
                else if (Tick % 3 == 0)
                    Fx.Spark(s.Pos.X, s.Pos.Y + 6, Rand(-4, 4), 10, new Color(200, 240, 255) * 0.7f, 0.6f, 1.3f, 0, false);
                if (s.Pos.Y <= SurfaceY)
                {
                    s.Pos.Y = SurfaceY;
                    if (s.Mine)
                    {
                        s.Surfaced = true;
                        s.Life = 0;
                    }
                    else
                    {
                        // A torpedo breaks the surface and explodes.
                        Fx.Burst(s.Pos.X, SurfaceY, Pal.White, 14, 90, 0.6f, 2f, 160, false);
                        Fx.Burst(s.Pos.X, SurfaceY - 4, Pal.Orange, 10, 70, 0.4f, 2f);
                        Sound.Play(Sfx.Splash, -0.2f, 0.6f);
                        if (HitsShip(s.Pos.X, 30))
                        {
                            ShipHit();
                            return;
                        }
                        _shots.RemoveAt(i);
                        continue;
                    }
                }
            }
            else
            {
                // A surfaced mine bobs and drifts, then sinks away.
                s.Pos.X += MathF.Sin(s.Life + s.Phase) * 6 * Dt;
                if (HitsShip(s.Pos.X, 26))
                {
                    Fx.Explode(s.Pos.X, SurfaceY, 1);
                    ShipHit();
                    return;
                }
                if (s.Life > 5)
                {
                    _shots.RemoveAt(i);
                    continue;
                }
            }
            _shots[i] = s;
        }
    }

    private bool HitsShip(float x, float halfWidth) => _respawn <= 0 && _invuln <= 0 && MathF.Abs(x - _shipX) < halfWidth;

    private void ShipHit()
    {
        Fx.Explode(_shipX, ShipY, 2);
        Fx.Burst(_shipX, ShipY, Pal.Grey, 20, 80, 1.2f, 3f, 60, false);
        Sound.Play(Sfx.BigExplode);
        _shots.Clear();
        if (LoseLife())
            return;
        _respawn = 1.6f;
        _shipVx = 0;
    }

    // ------------------------------------------------------------------ drawing

    private static void DrawSea(Gfx g, RectF r, float t, float surfaceY, float seabedY, float s)
    {
        // Sky.
        g.GradientV(r.X, r.Y, r.W, surfaceY - r.Y, new Color(255, 170, 120), new Color(255, 220, 170));
        g.GradientV(r.X, r.Y, r.W, (surfaceY - r.Y) * 0.6f, new Color(90, 120, 200), new Color(255, 170, 120) * 0f);
        float sunX = r.X + r.W * 0.78f, sunY = surfaceY - 22 * s;
        g.Glow(sunX, sunY, 70 * s, Pal.Orange, 0.6f);
        g.Circle(sunX, sunY, 14 * s, new Color(255, 240, 200));
        for (int i = 0; i < 4; i++)
        {
            float cx = r.X + Backdrops.Mod(i * 190 * s + t * 6 * s, r.W + 120 * s) - 60 * s;
            float cy = r.Y + (14 + (i % 2) * 14) * s + 24 * s;
            g.Ellipse(cx, cy, 30 * s, 6 * s, Pal.White * 0.6f);
            g.Ellipse(cx + 12 * s, cy - 4 * s, 18 * s, 6 * s, Pal.White * 0.7f);
        }
        // Water column.
        g.GradientV(r.X, surfaceY, r.W, (seabedY - surfaceY) * 0.5f, new Color(30, 130, 170), new Color(14, 70, 130));
        g.GradientV(r.X, surfaceY + (seabedY - surfaceY) * 0.5f, r.W, (seabedY - surfaceY) * 0.5f + 1, new Color(14, 70, 130), new Color(6, 24, 66));
        // Light rays.
        for (int i = 0; i < 7; i++)
        {
            float x = r.X + (i * 0.16f + 0.04f) * r.W + MathF.Sin(t * 0.4f + i * 1.7f) * 16 * s;
            float w = (10 + (i % 3) * 8) * s;
            float lean = 40 * s + MathF.Sin(t * 0.3f + i) * 10 * s;
            float inten = 0.08f + 0.03f * MathF.Sin(t + i);
            float depth = seabedY - surfaceY;
            for (int j = 0; j < 4; j++)
            {
                float f0 = j / 4f, f1 = (j + 1) / 4f;
                float y0 = surfaceY + depth * f0, y1 = surfaceY + depth * f1;
                float l0 = x + lean * f0, l1 = x + lean * f1;
                float w0 = w * (1 + 1.5f * f0), w1 = w * (1 + 1.5f * f1);
                var c = Pal.Add(new Color(140, 220, 255), inten * (1 - f0 * 0.85f));
                g.Triangle(new Vector2(l0, y0), new Vector2(l0 + w0, y0), new Vector2(l1 + w1, y1), c);
                g.Triangle(new Vector2(l0, y0), new Vector2(l1 + w1, y1), new Vector2(l1, y1), c);
            }
        }
        // Seabed.
        g.GradientV(r.X, seabedY, r.W, r.Bottom - seabedY, new Color(120, 100, 70), new Color(50, 40, 30));
        for (int i = 0; i < 12; i++)
        {
            float x = r.X + i * r.W / 11f;
            g.Ellipse(x, seabedY + 2 * s, (16 + i * 7 % 13) * s, (5 + i % 3 * 2) * s, new Color(70, 64, 60));
        }
        for (int i = 0; i < 16; i++)
        {
            float x = r.X + (i * 0.0625f + 0.03f) * r.W;
            float h = (14 + i * 37 % 20) * s;
            var c = i % 2 == 0 ? new Color(40, 140, 70) : new Color(30, 110, 90);
            float prevX = x, prevY = seabedY;
            for (int k = 1; k <= 4; k++)
            {
                float yy = seabedY - h * k / 4;
                float xx = x + MathF.Sin(t * 1.5f + i + k * 0.7f) * 3 * s * k / 2;
                g.Line(prevX, prevY, xx, yy, 2.2f * s, c);
                prevX = xx;
                prevY = yy;
            }
        }
    }

    private static void DrawSurface(Gfx g, RectF r, float t, float surfaceY, float s)
    {
        float step = 8 * s;
        for (float x = r.X; x < r.Right; x += step)
        {
            float y0 = surfaceY + MathF.Sin(x * 0.05f / s + t * 2) * 1.5f * s;
            float y1 = surfaceY + MathF.Sin((x + step) * 0.05f / s + t * 2) * 1.5f * s;
            g.Line(x, y0, x + step, y1, 1.6f * s, new Color(220, 245, 255));
        }
        g.GradientV(r.X, surfaceY + 1, r.W, 6 * s, Pal.Add(Pal.White, 0.15f), Color.Transparent);
    }

    private void DrawSub(Gfx g, Sub s)
    {
        bool left = s.Vx < 0;
        var art = SubArt[s.Type];
        float px = 2.4f;
        if (s.SinkTimer >= 0)
        {
            // Sinking: tilt by drawing two halves offset.
            float k = MathF.Min(1, s.SinkTimer);
            g.Pixels(art, s.X - art.Width * px / 2, s.Y - art.Height * px / 2 + s.Tilt * 8, px, left, Pal.Darken(new Color(80, 80, 90), k * 0.5f));
            if (Tick % 20 < 10)
                g.Glow(s.X, s.Y, 16, Pal.Orange, 0.4f * (1 - k));
            return;
        }
        var rim = new Color(120, 190, 230) * 0.45f;
        g.PixelsCentered(art, s.X - 1, s.Y, px, left, rim);
        g.PixelsCentered(art, s.X + 1, s.Y, px, left, rim);
        g.PixelsCentered(art, s.X, s.Y - 1, px, left, rim);
        g.PixelsCentered(art, s.X, s.Y + 1, px, left, rim);
        g.PixelsCentered(art, s.X, s.Y, px, left);
        // Propeller.
        float propX = s.X + (left ? 1 : -1) * (art.Width * px / 2 + 2);
        float spin = MathF.Sin(Time * 30 + s.X);
        g.Rect(propX - 1, s.Y + 1 - 5 * spin, 2, 10 * MathF.Abs(spin) + 1, new Color(180, 160, 100));
        if (Tick % 4 == 0)
            Fx.Spark(propX + (left ? 3 : -3), s.Y + 2, s.Vx * -0.3f, Rand(-8, 4), new Color(180, 230, 255) * 0.5f, 0.6f, 1f, 0, false);
        // Lights on deep boats.
        if (s.Type == 2)
            g.Glow(s.X + (left ? -24 : 24), s.Y + 2, 14, Pal.Yellow, 0.35f);
    }

    public override void Draw(Gfx g)
    {
        DrawSea(g, g.Visible, Time, SurfaceY, SeabedY, 1);

        foreach (var f in _fish)
        {
            float dir = MathF.Sign(f.Speed);
            float wag = MathF.Sin(Time * 10 + f.X) * f.Size * 0.4f;
            g.Ellipse(f.X, f.Y, f.Size * 2, f.Size, f.Colour * 0.8f);
            g.Triangle(new Vector2(f.X - dir * f.Size * 1.6f, f.Y), new Vector2(f.X - dir * f.Size * 3, f.Y - f.Size + wag),
                new Vector2(f.X - dir * f.Size * 3, f.Y + f.Size + wag), f.Colour * 0.7f);
            g.Circle(f.X + dir * f.Size, f.Y - f.Size * 0.2f, 0.7f, Color.Black);
        }

        foreach (var s in _subs)
            DrawSub(g, s);

        foreach (var c in _charges)
        {
            float r = c.Big ? 6 : 4;
            g.Glow(c.Pos, r * 3, c.Big ? Pal.Red : Pal.Orange, 0.35f + 0.25f * MathF.Sin(Time * 14));
            g.RoundRect(c.Pos.X - r, c.Pos.Y - r * 1.2f, r * 2, r * 2.4f, r * 0.6f, c.Big ? new Color(120, 30, 30) : new Color(60, 60, 70));
            g.Rect(c.Pos.X - r, c.Pos.Y - r * 0.4f, r * 2, 1.5f, Pal.Yellow);
            g.Rect(c.Pos.X - r, c.Pos.Y + r * 0.5f, r * 2, 1.5f, Pal.Yellow);
        }

        foreach (var s in _shots)
        {
            if (s.Mine)
            {
                float bob = s.Surfaced ? MathF.Sin(s.Life * 4) * 1.5f - 3 : 0;
                var p = new Vector2(s.Pos.X, s.Pos.Y + bob);
                g.Glow(p, 14, Pal.Red, 0.3f + 0.3f * MathF.Sin(Time * 10));
                for (int k = 0; k < 6; k++)
                {
                    var d = MathF2.FromAngle(k * MathF.PI / 3 + s.Life, 7.5f);
                    g.Line(p, p + d, 1.6f, new Color(60, 60, 60));
                }
                g.Circle(p.X, p.Y, 5, new Color(40, 40, 46));
                g.Circle(p.X - 1.5f, p.Y - 1.5f, 1.5f, Pal.Grey);
                g.Circle(p.X, p.Y, 1.3f, (int)(Time * 6) % 2 == 0 ? Pal.Red : Pal.DarkGrey);
            }
            else
            {
                g.Glow(s.Pos, 12, Pal.Yellow, 0.4f);
                g.RoundRect(s.Pos.X - 2, s.Pos.Y - 7, 4, 14, 2, new Color(200, 200, 210));
                g.Rect(s.Pos.X - 2, s.Pos.Y - 7, 4, 3, Pal.Red);
                g.Rect(s.Pos.X - 3, s.Pos.Y + 5, 6, 2, new Color(120, 120, 130));
            }
        }

        foreach (var b in _blasts)
        {
            float k = b.T / 0.6f;
            g.Glow(b.Pos, b.Radius * (0.6f + k), Pal.White, 0.7f * (1 - k));
            g.Ring(b.Pos.X, b.Pos.Y, b.Radius * (0.3f + 0.9f * MathF2.EaseOut(k)), 3 * (1 - k) + 0.5f, Pal.White * (1 - k));
            g.Ring(b.Pos.X, b.Pos.Y, b.Radius * 0.6f * MathF2.EaseOut(k), 2, Pal.Sky * (1 - k));
        }

        // Destroyer.
        if (_respawn <= 0 && !IsOver && (_invuln <= 0 || (int)(_invuln * 10) % 2 == 0))
        {
            float roll = MathF.Sin(Time * 1.8f) * 1.2f;
            g.PixelsCentered(Ship, _shipX, ShipY + roll, 2f, _faceLeft);
            g.Ellipse(_shipX, SurfaceY + 2, 34, 3, Color.Black * 0.25f);
            // Smoke from the funnel.
            if (Tick % 6 == 0)
                Fx.Spark(_shipX + (_faceLeft ? 4 : -4) * 2, ShipY - 12, -_shipVx * 0.2f - 8, -18, new Color(60, 60, 70), 1.4f, 3f, -4, false);
        }
        DrawSurface(g, g.Visible, Time, SurfaceY, 1);

        // HUD: big charges and depth markers.
        g.Text("BIG", 12, 30, 1f, Pal.White);
        for (int i = 0; i < 3; i++)
        {
            bool on = i < _bigCharges;
            g.RoundRect(34 + i * 12, 28, 8, 11, 2, on ? new Color(170, 40, 40) : new Color(70, 60, 60) * 0.6f);
            if (on)
                g.Rect(34 + i * 12, 32, 8, 1.5f, Pal.Yellow);
        }
        if (!IsTouch)
        {
        g.Text("50", 632, 166, 1f, Pal.Ice * 0.6f, Align.Right);
        g.Text("100", 632, 224, 1f, Pal.Ice * 0.6f, Align.Right);
        g.Text("200", 632, 288, 1f, Pal.Ice * 0.6f, Align.Right);
        }

        if (_banner > 0)
        {
            float a = MathF.Min(1, _banner);
            g.TextShadow("WAVE " + _wave, 320, 170, 3f, Pal.White * a, Align.Center, Color.Black * (0.8f * a));
            g.TextShadow(_toSink + " SUBMARINES", 320, 200, 1.5f, Pal.Ice * a, Align.Center, Color.Black * (0.8f * a));
        }
    }

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        float s = r.H / 162f;
        float surf = r.Y + 44 * s, bed = r.Bottom - 14 * s;
        DrawSea(g, r, time, surf, bed, s);
        float shipX = r.CenterX + MathF.Sin(time * 0.6f) * 50 * s;
        // Subs.
        for (int i = 0; i < 2; i++)
        {
            float dir = i == 0 ? 1 : -1;
            float x = r.X + Backdrops.Mod(time * 22 * s * dir + i * 150 * s, r.W + 80 * s) - 40 * s;
            float y = surf + (60 + i * 40) * s;
            g.PixelsCentered(SubArt[i + 1], x, y, 2.2f * s, dir < 0);
        }
        // Falling charge and blast.
        float cycle = time % 2.4f;
        float cy = surf + cycle * 45 * s;
        if (cycle < 1.8f)
        {
            g.Glow(shipX - 20 * s, cy, 12 * s, Pal.Orange, 0.5f);
            g.RoundRect(shipX - 24 * s, cy - 5 * s, 8 * s, 10 * s, 2 * s, new Color(60, 60, 70));
        }
        else
        {
            float k = (cycle - 1.8f) / 0.6f;
            g.Glow(shipX - 20 * s, surf + 81 * s, 50 * s * (0.5f + k), Pal.White, 0.8f * (1 - k));
            g.Ring(shipX - 20 * s, surf + 81 * s, 40 * s * k, 3 * s, Pal.White * (1 - k));
        }
        g.PixelsCentered(Ship, shipX, surf - 9 * s, 2.4f * s);
        DrawSurface(g, r, time, surf, s);
    }

    public override void AutoPlay(Controls c)
    {
        float target = _shipX;
        float best = float.MaxValue;
        float sink = 60 + _wave * 2;
        foreach (var s in _subs)
        {
            if (s.SinkTimer >= 0)
                continue;
            float t = (s.Y - SurfaceY) / sink + 0.3f;
            float px = s.X + s.Vx * t;
            if (px < 30 || px > 610)
                continue;
            float d = MathF.Abs(px - _shipX) - s.Type * 40;
            if (d < best)
            {
                best = d;
                target = px - (_faceLeft ? 26 : -26);
            }
        }
        // Dodge anything about to reach the surface under us.
        foreach (var s in _shots)
        {
            float eta = s.Surfaced ? 0 : (s.Pos.Y - SurfaceY) / MathF.Max(1, -s.Vy);
            if (eta < 1.6f && MathF.Abs(s.Pos.X - _shipX) < 52)
                target = _shipX + (s.Pos.X < _shipX ? 90 : -90);
        }
        target = MathF2.Clamp(target, 40, 600);
        float dx = target - _shipX;
        c.SetDirections(MathF.Abs(dx) < 4 ? 0 : MathF.Sign(dx) * MathF.Min(1, MathF.Abs(dx) / 30), 0);
        if (MathF.Abs(dx) < 10 && best < 1000 && Tick % 12 == 0)
        {
            c.FirePressed = true;
            c.Fire = true;
        }
        if (_subs.Count >= 3 && Tick % 300 == 150)
            c.AltPressed = true;
    }
}
