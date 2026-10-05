using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 34 Psion Attack: a segmented Psion worm snakes down through a field of crystals. Shoot a
/// segment and it splits, leaving a crystal behind; spiders bounce round your zone and fleas
/// drop new crystals. Each wave is faster, with more heads.
/// </summary>
public sealed class PsionAttack : MiniGame, Capture.ICaptureHints
{
    public override int Number => 34;
    public override string Title => "Psion Attack";
    public override Category Category => Category.Shooter;
    public override string Tagline => "Blast the psychic worm before it snakes down through the crystals.";
    public override Color Accent => Pal.Magenta;
    public int CaptureTicks => 480;

    public override string[] HowToPlay =>
    [
        "The Psion worm weaves down, turning at every crystal. Shoot a segment to split the worm; it leaves a crystal.",
        "Heads score 100, body 10. Spiders bounce near you (up to 900); fleas drop crystals (200).",
        "Each wave is faster with more heads. Extra life every 12,000.",
    ];

    public override string[] DesktopControls => ["ARROWS / WASD move, SPACE fire."];
    public override string[] TouchControls => ["Stick moves, hold FIRE to shoot."];
    public override Pad Pad => Pad.Stick | Pad.Fire;

    private const int Cell = 16, Cols = 40, Rows = 21, ZoneTop = 16;
    private const float Top = 24;

    private sealed class Segment
    {
        public int X, Y, PX, PY, Dx, Dy, Delay;
        public bool Alive;
        public Segment Prev;
    }

    private static readonly Dictionary<char, Color> Colours = new()
    {
        ['p'] = Pal.Purple, ['P'] = Pal.Magenta, ['c'] = Pal.Cyan, ['w'] = Pal.White, ['y'] = Pal.Yellow,
        ['o'] = Pal.Orange, ['r'] = Pal.Red, ['k'] = new Color(40, 10, 60), ['l'] = Pal.Lime, ['g'] = Pal.Green,
    };

    private static readonly PixelArt[] SpiderArt =
    [
        new(["o..........o", ".o..oooo..o.", "..oorwwroo..", "oooooooooooo", "..oooooooo..", ".o.o....o.o.", "o..o....o..o"], Colours),
        new(["............", "oo..oooo..oo", "..oorwwroo..", "oooooooooooo", "o.oooooooo.o", ".o.o....o.o.", "...o....o..."], Colours),
    ];

    private static readonly PixelArt FleaArt = new(["..ll..", ".llll.", "lgwwgl", "llllll", ".l..l.", "l....l"], Colours);

    private static readonly PixelArt GunnerArt = new(["...cc...", "...ww...", "..cwwc..", ".cccccc.", "cccPPccc", "cc.PP.cc"], Colours);

    private readonly int[,] _crystals = new int[Cols, Rows];
    private readonly List<Segment> _segments = new();
    private Vector2 _pos;
    private Vector2 _shot;
    private bool _shotLive, _alive;
    private float _respawn, _invuln;
    private int _stepTicks, _stepTimer;
    private float _stepFrac;
    private int _nextLife;
    private float _banner;

    private bool _spider;
    private Vector2 _spiderPos, _spiderVel;
    private float _spiderTimer, _spiderTurn;

    private bool _flea;
    private Vector2 _fleaPos;
    private int _fleaHp;
    private float _fleaTimer;

    protected override void Start()
    {
        Lives = 3;
        Level = 0;
        _nextLife = 12000;
        _spider = false;
        _flea = false;
        _shotLive = false;
        _invuln = 0;
        _stepTimer = 0;
        Array.Clear(_crystals);
        for (int i = 0; i < 42; i++)
        {
            int x = RandInt(0, Cols), y = RandInt(1, Rows - 2);
            _crystals[x, y] = 4;
        }
        _pos = new Vector2(320, Top + (Rows - 1) * Cell + Cell / 2f);
        _alive = true;
        _spiderTimer = 6;
        _fleaTimer = 8;
        NextWave();
    }

    private void NextWave()
    {
        Level++;
        _banner = 1.8f;
        _stepTicks = Math.Max(3, 7 - (Level - 1) / 2);
        int heads = Math.Min(Level - 1, 6);
        SpawnWorm(12 - heads, heads);
        if (Level > 1)
            Sound.Play(Sfx.LevelUp, 0, 0.7f);
    }

    private void SpawnWorm(int length, int extraHeads)
    {
        _segments.Clear();
        int dir = Chance(0.5f) ? 1 : -1;
        int startX = Cols / 2;
        Segment prev = null;
        for (int i = 0; i < length; i++)
        {
            var s = new Segment { X = startX, Y = 0, PX = startX, PY = 0, Dx = dir, Dy = 1, Delay = i, Alive = true, Prev = prev };
            _segments.Add(s);
            prev = s;
        }
        for (int h = 0; h < extraHeads; h++)
        {
            int x = RandInt(2, Cols - 2);
            _segments.Add(new Segment { X = x, Y = 0, PX = x, PY = 0, Dx = Chance(0.5f) ? 1 : -1, Dy = 1, Delay = RandInt(0, 30), Alive = true });
        }
    }

    private static bool InGrid(int x, int y) => x >= 0 && y >= 0 && x < Cols && y < Rows;

    private static Vector2 CellCentre(float x, float y) => new(x * Cell + Cell / 2f, Top + y * Cell + Cell / 2f);

    private Vector2 SegPos(Segment s) =>
        CellCentre(MathF2.Lerp(s.PX, s.X, _stepFrac), MathF2.Lerp(s.PY, s.Y, _stepFrac));

    private static bool IsHead(Segment s) => s.Prev == null || !s.Prev.Alive || s.Prev.Delay > 0 ||
                                             Math.Abs(s.Prev.PX - s.PX) + Math.Abs(s.Prev.PY - s.PY) > 1;

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        if (_banner > 0)
            _banner -= Dt;

        UpdatePlayer();
        StepWorm();
        UpdateShot();
        UpdateSpider();
        UpdateFlea();
        if (IsOver)
            return;
        CheckPlayerHit();

        bool any = false;
        foreach (var s in _segments)
            any |= s.Alive;
        if (!any && _alive)
            NextWave();

        while (Score >= _nextLife)
        {
            _nextLife += 12000;
            Lives++;
            Sound.Play(Sfx.Bonus);
            Fx.Float("EXTRA LIFE!", 320, 200, Pal.Lime, 2f);
        }
    }

    private void UpdatePlayer()
    {
        if (!_alive)
        {
            _respawn -= Dt;
            if (_respawn <= 0)
            {
                _alive = true;
                _invuln = 2f;
                _pos = new Vector2(320, Top + (Rows - 1) * Cell + Cell / 2f);
            }
            return;
        }
        if (_invuln > 0)
            _invuln -= Dt;
        var move = new Vector2(In.AxisX, In.AxisY);
        if (move.LengthSquared() > 1)
            move.Normalize();
        var d = move * 190 * Dt;
        float minY = Top + ZoneTop * Cell + 6, maxY = Top + Rows * Cell - 7;
        bool stuck = BlockedAt(_pos);
        var nx = new Vector2(MathF2.Clamp(_pos.X + d.X, 7, 633), _pos.Y);
        if (stuck || !BlockedAt(nx))
            _pos = nx;
        var ny = new Vector2(_pos.X, MathF2.Clamp(_pos.Y + d.Y, minY, maxY));
        if (stuck || !BlockedAt(ny))
            _pos = ny;

        if (In.Fire && !_shotLive)
        {
            _shotLive = true;
            _shot = _pos + new Vector2(0, -8);
            Sound.Play(Sfx.Laser, Rand(0.4f, 0.6f), 0.3f);
        }
    }

    private bool BlockedAt(Vector2 p)
    {
        int cx0 = (int)((p.X - 5) / Cell), cx1 = (int)((p.X + 5) / Cell);
        int cy0 = (int)((p.Y - 5 - Top) / Cell), cy1 = (int)((p.Y + 5 - Top) / Cell);
        for (int x = cx0; x <= cx1; x++)
            for (int y = cy0; y <= cy1; y++)
                if (InGrid(x, y) && _crystals[x, y] > 0)
                    return true;
        return false;
    }

    private void StepWorm()
    {
        _stepTimer++;
        _stepFrac = MathF.Min(1, _stepTimer / (float)_stepTicks);
        if (_stepTimer < _stepTicks)
            return;
        _stepTimer = 0;
        _stepFrac = 0;
        bool stepped = false;
        foreach (var s in _segments)
        {
            if (!s.Alive)
                continue;
            s.PX = s.X;
            s.PY = s.Y;
            if (s.Delay > 0)
            {
                s.Delay--;
                continue;
            }
            stepped = true;
            int nx = s.X + s.Dx;
            if (InGrid(nx, s.Y) && _crystals[nx, s.Y] == 0)
            {
                s.X = nx;
                continue;
            }
            // Blocked: drop (or climb, once in the player zone) a row and turn round.
            int ny = s.Y + s.Dy;
            if (ny >= Rows || (s.Dy < 0 && ny < ZoneTop))
            {
                s.Dy = -s.Dy;
                ny = s.Y + s.Dy;
            }
            s.Y = ny;
            s.Dx = -s.Dx;
        }
        if (stepped && Tick % (_stepTicks * 2) < _stepTicks)
            Sound.Play(Sfx.Step, -0.3f + Level * 0.03f, 0.25f);
    }

    private void UpdateShot()
    {
        if (!_shotLive)
            return;
        // Move in small steps so nothing is skipped at speed.
        for (int k = 0; k < 4 && _shotLive; k++)
        {
            _shot.Y -= 760 * Dt / 4;
            if (_shot.Y < Top)
            {
                _shotLive = false;
                return;
            }
            int cx = (int)(_shot.X / Cell), cy = (int)((_shot.Y - Top) / Cell);
            if (InGrid(cx, cy) && _crystals[cx, cy] > 0)
            {
                _crystals[cx, cy]--;
                var c = CellCentre(cx, cy);
                Fx.Burst(c.X, c.Y, CrystalColour(cx, cy), 5, 60, 0.3f, 1.5f);
                if (_crystals[cx, cy] == 0)
                {
                    AddScore(1);
                    Sound.Play(Sfx.Crack, Rand(0, 0.4f), 0.4f);
                }
                else
                {
                    Sound.Play(Sfx.Tick, 0.6f, 0.3f);
                }
                _shotLive = false;
                return;
            }
            foreach (var s in _segments)
            {
                if (!s.Alive || s.Delay > 0)
                    continue;
                var p = SegPos(s);
                if (Vector2.DistanceSquared(p, _shot) > 8.5f * 8.5f)
                    continue;
                bool head = IsHead(s);
                s.Alive = false;
                AddScore(head ? 100 : 10, p.X, p.Y - 8, head ? Pal.Yellow : Pal.Cyan);
                Fx.Burst(p.X, p.Y, Pal.Magenta, 14, 110, 0.5f, 2.2f);
                Fx.Burst(p.X, p.Y, Pal.Cyan, 8, 70, 0.4f, 1.8f);
                Sound.Play(head ? Sfx.Explode : Sfx.Pop, Rand(-0.1f, 0.3f), 0.6f);
                // A crystal grows where it fell (not on the bottom row).
                if (InGrid(s.X, s.Y) && s.Y < Rows - 1 && Vector2.DistanceSquared(CellCentre(s.X, s.Y), _pos) > 24 * 24)
                    _crystals[s.X, s.Y] = 4;
                _shotLive = false;
                return;
            }
            if (_spider && Vector2.DistanceSquared(_spiderPos, _shot) < 12 * 12)
            {
                float dist = MathF.Abs(_spiderPos.Y - _pos.Y);
                int pts = dist < 30 ? 900 : dist < 60 ? 600 : 300;
                AddScore(pts, _spiderPos.X, _spiderPos.Y - 10, Pal.Orange);
                Fx.Explode(_spiderPos.X, _spiderPos.Y, 0.8f);
                Sound.Play(Sfx.BigExplode, 0.3f, 0.6f);
                _spider = false;
                _spiderTimer = Rand(4, 8);
                _shotLive = false;
                return;
            }
            if (_flea && Vector2.DistanceSquared(_fleaPos, _shot) < 10 * 10)
            {
                _fleaHp--;
                _shotLive = false;
                if (_fleaHp <= 0)
                {
                    AddScore(200, _fleaPos.X, _fleaPos.Y - 8, Pal.Lime);
                    Fx.Burst(_fleaPos.X, _fleaPos.Y, Pal.Lime, 18, 110, 0.5f, 2f);
                    Sound.Play(Sfx.Explode, 0.5f, 0.5f);
                    _flea = false;
                    _fleaTimer = Rand(4, 9);
                }
                else
                {
                    Sound.Play(Sfx.Hit, 0.4f, 0.4f);
                }
                return;
            }
        }
    }

    private void UpdateSpider()
    {
        if (!_spider)
        {
            _spiderTimer -= Dt;
            if (_spiderTimer <= 0)
            {
                _spider = true;
                bool left = Chance(0.5f);
                _spiderPos = new Vector2(left ? -10 : 650, Top + (ZoneTop + 1) * Cell);
                _spiderVel = new Vector2((left ? 1 : -1) * (60 + Level * 6), 90);
                _spiderTurn = 0.4f;
            }
            return;
        }
        Sound.Loop(LoopSfx.Engine, true, 0.6f + 0.3f * MathF.Sin(Time * 20), 0.25f);
        _spiderPos += _spiderVel * Dt;
        float minY = Top + (ZoneTop - 2) * Cell, maxY = Top + Rows * Cell - 8;
        if (_spiderPos.Y < minY)
        {
            _spiderPos.Y = minY;
            _spiderVel.Y = MathF.Abs(_spiderVel.Y);
        }
        if (_spiderPos.Y > maxY)
        {
            _spiderPos.Y = maxY;
            _spiderVel.Y = -MathF.Abs(_spiderVel.Y);
        }
        _spiderTurn -= Dt;
        if (_spiderTurn <= 0)
        {
            _spiderTurn = Rand(0.3f, 0.9f);
            _spiderVel.Y = Pick(-1f, 1f) * Rand(70, 140);
            if (Chance(0.25f))
                _spiderVel.Y = 0;
        }
        // It munches crystals it crawls over.
        int cx = (int)(_spiderPos.X / Cell), cy = (int)((_spiderPos.Y - Top) / Cell);
        if (InGrid(cx, cy) && _crystals[cx, cy] > 0 && Chance(0.05f))
        {
            _crystals[cx, cy] = 0;
            Sound.Play(Sfx.Crack, -0.5f, 0.3f);
        }
        if (_spiderPos.X < -30 || _spiderPos.X > 670)
        {
            _spider = false;
            _spiderTimer = Rand(3, 7);
        }
    }

    private void UpdateFlea()
    {
        if (!_flea)
        {
            if (Level < 2)
                return;
            int inZone = 0;
            for (int x = 0; x < Cols; x++)
                for (int y = ZoneTop; y < Rows; y++)
                    if (_crystals[x, y] > 0)
                        inZone++;
            _fleaTimer -= Dt * (inZone < 5 ? 2 : 1);
            if (_fleaTimer <= 0)
            {
                _flea = true;
                _fleaHp = 2;
                _fleaPos = CellCentre(RandInt(0, Cols), -1);
                Sound.Play(Sfx.Whoosh, 0.5f, 0.5f);
            }
            return;
        }
        int before = (int)((_fleaPos.Y - Top) / Cell);
        _fleaPos.Y += (_fleaHp == 2 ? 170 : 300) * Dt;
        int after = (int)((_fleaPos.Y - Top) / Cell);
        if (after != before && InGrid((int)(_fleaPos.X / Cell), after) && after < Rows - 1 && Chance(0.3f))
            _crystals[(int)(_fleaPos.X / Cell), after] = 4;
        if (Tick % 3 == 0)
            Fx.Spark(_fleaPos.X, _fleaPos.Y - 6, 0, -20, Pal.Lime, 0.3f, 1.5f);
        if (_fleaPos.Y > Screen.Height + 10)
        {
            _flea = false;
            _fleaTimer = Rand(5, 10);
        }
    }

    private void CheckPlayerHit()
    {
        if (!_alive || _invuln > 0)
            return;
        bool hit = _spider && Vector2.DistanceSquared(_spiderPos, _pos) < 14 * 14;
        hit |= _flea && Vector2.DistanceSquared(_fleaPos, _pos) < 11 * 11;
        if (!hit)
            foreach (var s in _segments)
                if (s.Alive && s.Delay == 0 && Vector2.DistanceSquared(SegPos(s), _pos) < 12 * 12)
                {
                    hit = true;
                    break;
                }
        if (!hit)
            return;

        _alive = false;
        _respawn = 2f;
        _shotLive = false;
        Fx.Explode(_pos.X, _pos.Y, 1.4f);
        Fx.Burst(_pos.X, _pos.Y, Pal.Cyan, 30, 160, 0.8f, 2.4f);
        Sound.Play(Sfx.BigExplode);
        _spider = false;
        _spiderTimer = 4;
        _flea = false;
        // Damaged crystals heal (5 points each) and the worm regroups at the top.
        for (int x = 0; x < Cols; x++)
            for (int y = 0; y < Rows; y++)
                if (_crystals[x, y] is > 0 and < 4)
                {
                    _crystals[x, y] = 4;
                    AddScore(5);
                }
        int bodies = 0, heads = 0;
        foreach (var s in _segments)
            if (s.Alive)
            {
                if (IsHead(s)) heads++;
                else bodies++;
            }
        if (heads > 0)
            SpawnWorm(bodies + 1, heads - 1);
        LoseLife();
    }

    // ------------------------------------------------------------------ drawing

    private Color CrystalColour(int x, int y)
    {
        float hue = 180 + (Level * 47) % 140 + ((x * 7 + y * 13) % 5) * 8;
        return Pal.Hsv(hue, 0.65f, 1f);
    }

    private static void DrawBackground(Gfx g, RectF r, float time)
    {
        g.GradientV(r.X, r.Y, r.W, r.H, new Color(26, 6, 46), new Color(8, 4, 30));
        float s = r.W / 640f;
        for (int i = 0; i < 4; i++)
        {
            float x = r.X + r.W * (0.5f + 0.42f * MathF.Sin(time * (0.15f + i * 0.05f) + i * 1.7f));
            float y = r.Y + r.H * (0.5f + 0.4f * MathF.Cos(time * (0.12f + i * 0.04f) + i * 2.3f));
            var col = i % 2 == 0 ? Pal.Magenta : Pal.Cyan;
            g.Glow(x, y, 220 * s, col, 0.1f);
        }
        // Psychic ripples.
        for (int k = 0; k < 3; k++)
        {
            float ph = Backdrops.Mod(time * 0.15f + k / 3f, 1f);
            g.Ring(r.CenterX, r.CenterY, ph * r.W * 0.7f, 2 * s, Pal.Purple * (0.25f * (1 - ph)), 64);
        }
    }

    private static void DrawCrystal(Gfx g, Vector2 c, float size, Color col, int hp, float time, float seed)
    {
        float k = 0.45f + 0.55f * hp / 4f;
        float h = size * 0.55f * k, w = size * 0.36f * k;
        float tw = 0.75f + 0.25f * MathF.Sin(time * 3 + seed);
        g.Glow(c, size * 0.9f, col, 0.18f * tw);
        var top = new Vector2(c.X, c.Y - h);
        var bottom = new Vector2(c.X, c.Y + h);
        var left = new Vector2(c.X - w, c.Y - h * 0.15f);
        var right = new Vector2(c.X + w, c.Y - h * 0.15f);
        g.Triangle(top, left, bottom, Pal.Darken(col, 0.35f));
        g.Triangle(top, right, bottom, Pal.Darken(col, 0.05f));
        g.Triangle(top, left, new Vector2(c.X, c.Y - h * 0.15f), Pal.Lighten(col, 0.35f));
        g.Line(top, new Vector2(c.X, c.Y + h * 0.4f), 0.8f, Color.White * (0.5f * tw));
    }

    private static void DrawSegment(Gfx g, Vector2 p, float r, bool head, int dx, float time, int index)
    {
        float pulse = 0.85f + 0.15f * MathF.Sin(time * 8 - index * 0.7f);
        g.Glow(p, r * 2.2f, head ? Pal.Magenta : Pal.Purple, 0.4f * pulse);
        g.Circle(p, r, head ? new Color(200, 40, 200) : new Color(120, 40, 200));
        g.Circle(p.X - r * 0.2f, p.Y - r * 0.2f, r * 0.7f, head ? Pal.Magenta : new Color(160, 80, 240));
        g.Ring(p.X, p.Y, r * 0.95f, r * 0.18f, Pal.Cyan * (0.6f * pulse), 14);
        g.Circle(p.X - r * 0.35f, p.Y - r * 0.35f, r * 0.2f, Color.White * 0.6f);
        if (head)
        {
            float ex = dx * r * 0.3f;
            g.Circle(p.X + ex - r * 0.3f, p.Y - r * 0.1f, r * 0.24f, Pal.White);
            g.Circle(p.X + ex + r * 0.3f, p.Y - r * 0.1f, r * 0.24f, Pal.White);
            g.Circle(p.X + ex - r * 0.25f + dx * r * 0.06f, p.Y - r * 0.08f, r * 0.12f, Pal.Black);
            g.Circle(p.X + ex + r * 0.35f + dx * r * 0.06f, p.Y - r * 0.08f, r * 0.12f, Pal.Black);
            // Psychic antennae.
            float wob = MathF.Sin(time * 10) * r * 0.2f;
            var a1 = new Vector2(p.X - r * 0.5f + wob, p.Y - r * 1.5f);
            var a2 = new Vector2(p.X + r * 0.5f - wob, p.Y - r * 1.5f);
            g.Line(new Vector2(p.X - r * 0.3f, p.Y - r * 0.7f), a1, 1f, Pal.Cyan);
            g.Line(new Vector2(p.X + r * 0.3f, p.Y - r * 0.7f), a2, 1f, Pal.Cyan);
            g.Glow(a1, r * 0.6f, Pal.Cyan, 0.8f);
            g.Glow(a2, r * 0.6f, Pal.Cyan, 0.8f);
        }
    }

    public override void Draw(Gfx g)
    {
        DrawBackground(g, Screen.Bounds, Time);
        // The gunner's zone.
        float zy = Top + ZoneTop * Cell;
        g.GradientV(0, zy, 640, 360 - zy, Pal.Cyan * 0.04f, Pal.Cyan * 0.1f);
        for (int x = 0; x < 640; x += 12)
            g.Rect(x, zy, 6, 1, Pal.Cyan * 0.35f);

        for (int x = 0; x < Cols; x++)
            for (int y = 0; y < Rows; y++)
                if (_crystals[x, y] > 0)
                    DrawCrystal(g, CellCentre(x, y), Cell, CrystalColour(x, y), _crystals[x, y], Time, x * 3 + y);

        // Worm: joints first, then the segments.
        int idx = 0;
        foreach (var s in _segments)
        {
            if (!s.Alive || s.Delay > 0)
                continue;
            if (!IsHead(s))
            {
                var a = SegPos(s);
                var b = SegPos(s.Prev);
                if (Vector2.DistanceSquared(a, b) < 20 * 20)
                    g.Line(a, b, 5, new Color(90, 30, 150));
            }
        }
        foreach (var s in _segments)
        {
            idx++;
            if (!s.Alive || s.Delay > 0)
                continue;
            DrawSegment(g, SegPos(s), 6.8f, IsHead(s), s.Dx, Time, idx);
        }

        if (_flea)
        {
            g.Glow(_fleaPos, 14, Pal.Lime, 0.5f);
            g.PixelsCentered(FleaArt, _fleaPos.X, _fleaPos.Y, 2.2f);
        }
        if (_spider)
        {
            g.Glow(_spiderPos, 20, Pal.Orange, 0.4f);
            g.PixelsCentered(SpiderArt[(int)(Time * 10) % 2], _spiderPos.X, _spiderPos.Y, 2.2f);
        }

        if (_shotLive)
        {
            g.Glow(_shot, 10, Pal.Cyan, 0.9f);
            g.Rect(_shot.X - 1, _shot.Y - 6, 2, 10, Pal.White);
        }

        if (_alive && !IsOver && (_invuln <= 0 || (int)(_invuln * 12) % 2 == 0))
        {
            g.Glow(_pos, 18, Pal.Cyan, 0.45f);
            g.PixelsCentered(GunnerArt, _pos.X, _pos.Y, 2f);
        }

        if (_banner > 0)
        {
            float a = MathF.Min(1, _banner * 1.5f);
            g.TextShadow("WAVE " + Level, 320, 140, 3f, Pal.Lerp(Pal.Magenta, Pal.Cyan, MathF2.Pulse(Time, 0.6f)) * a, Align.Center);
        }
    }

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        DrawBackground(g, r, time);
        float s = r.H / 70f;
        float cell = 10 * s;
        // Crystals.
        for (int i = 0; i < 9; i++)
        {
            float x = r.X + ((i * 37) % 13 + 0.5f) * r.W / 13;
            float y = r.Y + ((i * 5) % 4 + 0.6f) * r.H / 6;
            DrawCrystal(g, new Vector2(x, y), cell * 1.2f, Pal.Hsv(190 + i * 15, 0.65f, 1), 4, time, i);
        }
        // A worm weaving across.
        for (int k = 8; k >= 0; k--)
        {
            float u = time * 0.8f - k * 0.13f;
            float x = r.CenterX + MathF.Sin(u * 0.7f) * r.W * 0.36f;
            float y = r.Y + r.H * 0.5f + MathF.Sin(u * 3.2f) * 9 * s;
            int dir = MathF.Cos(time * 0.8f * 0.7f) >= 0 ? 1 : -1;
            DrawSegment(g, new Vector2(x, y), 5.2f * s, k == 0, dir, time, k);
        }
        // The gunner firing up.
        float gx = r.CenterX + MathF.Sin(time * 1.3f) * 30 * s;
        float gy = r.Bottom - 8 * s;
        float sy = gy - Backdrops.Mod(time * 120 * s, r.H);
        g.Glow(gx, sy, 6 * s, Pal.Cyan, 0.9f);
        g.Rect(gx - 0.6f * s, sy - 3 * s, 1.2f * s, 6 * s, Pal.White);
        g.Glow(gx, gy, 12 * s, Pal.Cyan, 0.4f);
        g.PixelsCentered(GunnerArt, gx, gy, 1.3f * s);
        g.PixelsCentered(SpiderArt[(int)(time * 8) % 2], r.X + 22 * s + MathF.Sin(time * 2) * 10 * s, r.Bottom - 14 * s + MathF.Cos(time * 3) * 5 * s, 1.3f * s);
    }

    // ------------------------------------------------------------------ autopilot

    public override void AutoPlay(Controls c)
    {
        if (!_alive)
            return;
        // Aim at the lowest worm segment, keep clear of the spider and anything close.
        float tx = _pos.X;
        float lowest = -1;
        foreach (var s in _segments)
        {
            if (!s.Alive || s.Delay > 0)
                continue;
            var p = SegPos(s);
            if (p.Y > lowest)
            {
                lowest = p.Y;
                tx = p.X + s.Dx * 10;
            }
        }
        float ty = Top + (Rows - 1) * Cell + 4;
        var avoid = Vector2.Zero;
        void Avoid(Vector2 p, float range)
        {
            var d = _pos - p;
            float len = d.Length();
            if (len < range)
                avoid += d / (len + 1) * (range - len);
        }
        if (_spider)
            Avoid(_spiderPos, 70);
        if (_flea)
            Avoid(_fleaPos + new Vector2(0, 30), 30);
        foreach (var s in _segments)
            if (s.Alive && s.Delay == 0)
                Avoid(SegPos(s), 40);
        float mx = MathF2.Clamp((tx - _pos.X) / 20f, -1, 1);
        float my = MathF2.Clamp((ty - _pos.Y) / 20f, -1, 1);
        if (avoid.LengthSquared() > 1)
        {
            mx = MathF2.Clamp(avoid.X * 0.2f, -1, 1);
            my = MathF2.Clamp(avoid.Y * 0.2f, -1, 1);
        }
        c.SetDirections(mx, my);
        c.Fire = true;
        c.FirePressed = Tick % 4 == 0;
    }
}
