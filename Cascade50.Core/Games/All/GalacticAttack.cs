using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 11 Galactic Attack: a Galaxian-style convoy flaps in formation while aliens peel off in swooping
/// dives. Flagships dive with red escorts: shoot the escorts first for a huge bonus.
/// </summary>
public sealed class GalacticAttack : MiniGame
{
    public override int Number => 11;
    public override string Title => "Galactic Attack";
    public override Category Category => Category.Shooter;
    public override string Tagline => "The alien convoy swoops down in screaming dives. Hold your nerve.";
    public override Color Accent => Pal.Yellow;
    public override Pad Pad => Pad.Horizontal | Pad.Fire;

    public override string[] HowToPlay =>
    [
        "Aliens peel off the convoy in curving dives, firing as they come. Clear every alien to win the wave.",
        "Divers score double. A diving flagship is worth up to 800 if you hit both red escorts first.",
        "You have one shot on screen at a time, so make it count.",
    ];

    public override string[] DesktopControls => ["LEFT / RIGHT to move.", "SPACE to fire."];
    public override string[] TouchControls => ["Use the stick to move.", "Tap FIRE to shoot."];

    private const float PlayerY = 334;
    private const int Cols = 10;

    private enum State { Formation, Peel, Dive, Return }

    private sealed class Alien
    {
        public int Type, Row, Col;
        public bool Alive = true;
        public State State;
        public Vector2 Pos, Vel, PeelCentre;
        public float T, Side, TargetX, FireTimer, Wobble;
        public Alien Leader;
        public Vector2 EscortOffset;
        public int EscortsAlive, EscortsKilled;
    }

    private struct Bullet
    {
        public Vector2 Pos, Vel;
    }

    private static readonly Dictionary<char, Color>[] TypePalettes =
    [
        new() { ['b'] = new Color(60, 110, 255), ['c'] = new Color(60, 230, 255), ['w'] = Pal.White, ['r'] = Pal.Red },
        new() { ['b'] = new Color(220, 60, 230), ['c'] = new Color(150, 70, 255), ['w'] = Pal.White, ['r'] = Pal.Yellow },
        new() { ['b'] = new Color(255, 50, 50), ['c'] = new Color(255, 150, 40), ['w'] = Pal.White, ['r'] = Pal.Yellow },
        new() { ['y'] = new Color(255, 220, 50), ['r'] = new Color(255, 50, 50), ['w'] = Pal.White, ['o'] = Pal.Orange },
    ];

    private static readonly string[] BugA =
    [
        "b.........b",
        "bb...c...bb",
        "bbb.ccc.bbb",
        ".bbcwcwcbb.",
        "..bcccccb..",
        "...ccccc...",
        "....c.c....",
        "...c...c...",
    ];

    private static readonly string[] BugB =
    [
        "...........",
        "....c.c....",
        "b...ccc...b",
        "bb.cwcwc.bb",
        "bbbcccccbbb",
        ".b.ccccc.b.",
        "....c.c....",
        "...c...c...",
    ];

    private static readonly string[] FlagA =
    [
        "......y......",
        ".....yyy.....",
        "r...yyyyy...r",
        "rr.yyoooyy.rr",
        "rrryywowyyrrr",
        ".rryyyyyyyrr.",
        "..r.yy.yy.r..",
        "....y...y....",
    ];

    private static readonly string[] FlagB =
    [
        "r.....y.....r",
        "rr...yyy...rr",
        ".rr.yyyyy.rr.",
        "..ryyoooyyr..",
        "...yywowyy...",
        "....yyyyy....",
        "....yy.yy....",
        "....y...y....",
    ];

    private static readonly PixelArt[,] Art = BuildArt();

    private static readonly PixelArt Ship = new(
    [
        "......w......",
        "......w......",
        ".....rwr.....",
        ".....www.....",
        "..b..wcw..b..",
        "..b.wwcww.b..",
        "..bwwwwwwwb..",
        ".bwwwwwwwwwb.",
        "bwwbwwwwwbwwb",
        "bbb..rrr..bbb",
    ], new Dictionary<char, Color> { ['w'] = Pal.White, ['r'] = Pal.Red, ['b'] = new Color(60, 120, 255), ['c'] = Pal.Cyan });

    private static PixelArt[,] BuildArt()
    {
        var a = new PixelArt[4, 2];
        for (int t = 0; t < 3; t++)
        {
            a[t, 0] = new PixelArt(BugA, TypePalettes[t]);
            a[t, 1] = new PixelArt(BugB, TypePalettes[t]);
        }
        a[3, 0] = new PixelArt(FlagA, TypePalettes[3]);
        a[3, 1] = new PixelArt(FlagB, TypePalettes[3]);
        return a;
    }

    private static readonly Color[] TypeGlow = [Pal.Cyan, Pal.Magenta, Pal.Red, Pal.Yellow];

    private readonly List<Alien> _aliens = new();
    private readonly List<Bullet> _bullets = new();
    private float _playerX = 320;
    private bool _shotActive;
    private Vector2 _shot;
    private float _respawn, _invuln;
    private float _formX, _formDir = 1;
    private float _diveTimer;
    private int _launches;
    private int _wave;
    private float _waveBanner;
    private int _nextExtra = 7000;
    private float _flap;

    protected override void Start()
    {
        Lives = 3;
        _wave = 0;
        NextWave();
    }

    private void NextWave()
    {
        _wave++;
        Level = _wave;
        _aliens.Clear();
        _bullets.Clear();
        AddRow(0, 3, [3, 6]);
        AddRow(1, 2, [2, 3, 4, 5, 6, 7]);
        AddRow(2, 1, [1, 2, 3, 4, 5, 6, 7, 8]);
        AddRow(3, 0, [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]);
        AddRow(4, 0, [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]);
        foreach (var a in _aliens)
        {
            // Fly in from above.
            a.State = State.Return;
            a.Pos = new Vector2(320 + (a.Col - 4.5f) * 50, -30 - a.Row * 30 - Math.Abs(a.Col - 4.5f) * 8);
        }
        _diveTimer = 3f;
        _waveBanner = 2.2f;
        Status = "WAVE " + _wave;
    }

    private void AddRow(int row, int type, int[] cols)
    {
        foreach (int c in cols)
            _aliens.Add(new Alien { Type = row == 0 ? 3 : type, Row = row, Col = c });
    }

    private Vector2 Slot(Alien a)
    {
        float bob = MathF.Sin(Time * 2 + a.Col * 0.6f) * 1.5f;
        return new Vector2(320 + _formX + (a.Col - 4.5f) * 30, 52 + a.Row * 22 + bob);
    }

    private float DiveSpeed => 100 + 10 * Math.Min(_wave, 16);

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        if (_waveBanner > 0) _waveBanner -= Dt;
        _flap += Dt;
        _formX += _formDir * 18 * Dt;
        if (MathF.Abs(_formX) > 40) _formDir = -MathF.Sign(_formX);

        UpdatePlayer();
        UpdateAliens();
        UpdateBullets();
        LaunchDivers();

        if (Score >= _nextExtra)
        {
            _nextExtra += 25000;
            Lives++;
            Fx.Float("EXTRA SHIP!", 320, 200, Pal.Lime, 2f);
            Sound.Play(Sfx.Bonus);
        }

        bool any = false;
        foreach (var a in _aliens)
            if (a.Alive) { any = true; break; }
        if (!any && !IsOver)
        {
            AddScore(500 * _wave, 320, 190, Pal.Cyan);
            Sound.Play(Sfx.LevelUp);
            NextWave();
        }
    }

    private void UpdatePlayer()
    {
        if (_respawn > 0)
        {
            _respawn -= Dt;
            if (_respawn <= 0)
            {
                _invuln = 2;
                _playerX = 320;
            }
            return;
        }
        if (_invuln > 0) _invuln -= Dt;
        _playerX = MathF2.Clamp(_playerX + In.AxisX * 210 * Dt, 16, 624);
        if (In.FirePressed && !_shotActive)
        {
            _shotActive = true;
            _shot = new Vector2(_playerX, PlayerY - 12);
            Sound.Play(Sfx.Laser, 0.2f, 0.6f);
        }
        if (_shotActive)
        {
            _shot.Y -= 520 * Dt;
            if (_shot.Y < Screen.HudHeight)
                _shotActive = false;
        }
        else
        {
            // The missile sits on the nose until fired.
            _shot = new Vector2(_playerX, PlayerY - 12);
        }
    }

    private void UpdateAliens()
    {
        float playerX = _playerX;
        foreach (var a in _aliens)
        {
            if (!a.Alive) continue;
            switch (a.State)
            {
                case State.Formation:
                    a.Pos = Slot(a);
                    break;
                case State.Peel:
                {
                    a.T += Dt * 1.6f;
                    float th = (a.Side > 0 ? MathF.PI : 0) + a.Side * MathF.PI * MathF.Min(1, a.T);
                    a.Pos = a.PeelCentre + MathF2.FromAngle(th, 16);
                    if (a.T >= 1)
                    {
                        a.State = State.Dive;
                        a.T = 0;
                        a.Vel = new Vector2(-a.Side * 30, DiveSpeed * 0.6f);
                    }
                    break;
                }
                case State.Dive:
                {
                    a.T += Dt;
                    if (a.Leader != null && a.Leader.Alive && a.Leader.State == State.Dive)
                    {
                        a.Pos = a.Leader.Pos + a.EscortOffset;
                    }
                    else
                    {
                        a.TargetX = MathF2.Approach(a.TargetX, playerX, 120 * Dt);
                        float wantVx = MathF2.Clamp((a.TargetX - a.Pos.X) * 1.4f, -130, 130) + MathF.Sin(a.T * 3 + a.Wobble) * 70;
                        a.Vel.X = MathF2.Approach(a.Vel.X, wantVx, 260 * Dt);
                        a.Vel.Y = MathF2.Approach(a.Vel.Y, DiveSpeed, 120 * Dt);
                        a.Pos += a.Vel * Dt;
                    }
                    a.FireTimer -= Dt;
                    if (a.FireTimer <= 0 && a.Pos.Y > 120 && a.Pos.Y < 270 && _respawn <= 0)
                    {
                        a.FireTimer = MathF.Max(0.22f, 0.9f - _wave * 0.06f) * Rand(0.7f, 1.4f);
                        float dx = MathF2.Clamp((_playerX - a.Pos.X) * MathF.Min(0.8f, 0.3f + _wave * 0.04f), -90, 90);
                        _bullets.Add(new Bullet { Pos = a.Pos + new Vector2(0, 8), Vel = new Vector2(dx, 170 + _wave * 8) });
                        Sound.Play(Sfx.Zap, 0.5f, 0.25f);
                    }
                    if (a.Pos.Y > 375)
                    {
                        if (_wave >= 3 && a.Leader == null && Chance(MathF.Min(0.6f, 0.08f * _wave)))
                        {
                            a.Pos = new Vector2(a.Pos.X, -14);
                            a.Vel = new Vector2(0, DiveSpeed * 0.7f);
                            a.TargetX = _playerX;
                            break;
                        }
                        a.State = State.Return;
                        a.Pos = new Vector2(a.Pos.X, -14);
                        a.Leader = null;
                    }
                    break;
                }
                case State.Return:
                {
                    var slot = Slot(a);
                    var d = slot - a.Pos;
                    float len = d.Length();
                    float sp = 150 * Dt;
                    if (len <= sp)
                    {
                        a.State = State.Formation;
                        a.Pos = slot;
                    }
                    else
                        a.Pos += d / len * sp;
                    break;
                }
            }

            // Collisions with the player's missile.
            if (_shotActive && MathF.Abs(_shot.X - a.Pos.X) < (a.Type == 3 ? 13 : 11) && MathF.Abs(_shot.Y - a.Pos.Y) < 10)
            {
                _shotActive = false;
                Kill(a);
                continue;
            }
            // Ramming the player.
            if (_respawn <= 0 && _invuln <= 0 && a.State == State.Dive &&
                MathF.Abs(a.Pos.X - _playerX) < 16 && MathF.Abs(a.Pos.Y - PlayerY) < 12)
            {
                Kill(a);
                PlayerHit();
            }
        }
    }

    private void Kill(Alien a)
    {
        a.Alive = false;
        bool diving = a.State is State.Dive or State.Peel;
        int pts = a.Type switch { 0 => 30, 1 => 40, 2 => 50, _ => 60 };
        if (diving) pts *= 2;
        var col = TypeGlow[a.Type];
        if (a.Type == 3 && diving)
        {
            pts = a.EscortsKilled >= 2 ? 800 : a.EscortsKilled == 1 ? 300 : a.EscortsAlive > 0 ? 200 : 150;
            Fx.Float(pts.ToString(), a.Pos.X, a.Pos.Y - 18, Pal.Yellow, 2.5f);
            Sound.Play(pts >= 300 ? Sfx.Bonus : Sfx.Coin);
            Score += pts;
        }
        else
            AddScore(pts, a.Pos.X, a.Pos.Y - 10, col);
        if (a.Leader != null && a.Leader.Alive)
        {
            a.Leader.EscortsKilled++;
            a.Leader.EscortsAlive--;
        }
        // Escorts of a dead flagship carry on alone.
        foreach (var o in _aliens)
            if (o.Leader == a)
            {
                o.Leader = null;
                o.TargetX = o.Pos.X;
                o.Vel = new Vector2(0, DiveSpeed);
            }
        Fx.Burst(a.Pos.X, a.Pos.Y, col, 22, 140, 0.6f, 2.5f);
        Fx.Burst(a.Pos.X, a.Pos.Y, Pal.White, 8, 70, 0.3f, 2f);
        Sound.Play(a.Type == 3 ? Sfx.BigExplode : Sfx.Explode, Rand(-0.2f, 0.3f), 0.8f);
    }

    private void PlayerHit()
    {
        Fx.Explode(_playerX, PlayerY, 1.8f);
        Fx.Burst(_playerX, PlayerY, Pal.White, 30, 180, 0.9f, 2.5f);
        Sound.Play(Sfx.BigExplode);
        _bullets.Clear();
        _shotActive = false;
        if (!LoseLife())
            _respawn = 2f;
    }

    private void UpdateBullets()
    {
        for (int i = _bullets.Count - 1; i >= 0; i--)
        {
            var b = _bullets[i];
            b.Pos += b.Vel * Dt;
            _bullets[i] = b;
            if (b.Pos.Y > 365)
            {
                _bullets.RemoveAt(i);
                continue;
            }
            if (_respawn <= 0 && _invuln <= 0 && MathF.Abs(b.Pos.X - _playerX) < 9 && b.Pos.Y > PlayerY - 9 && b.Pos.Y < PlayerY + 9)
            {
                _bullets.RemoveAt(i);
                PlayerHit();
                return;
            }
        }
    }

    private void LaunchDivers()
    {
        if (_respawn > 0 || _waveBanner > 1.2f)
            return;
        int alive = 0, diving = 0;
        foreach (var a in _aliens)
        {
            if (!a.Alive) continue;
            alive++;
            if (a.State is State.Dive or State.Peel) diving++;
        }
        int maxDivers = Math.Min(9, 2 + _wave / 2 + (alive < 10 ? 2 : 0));
        _diveTimer -= Dt * (alive < 8 ? 2 : 1);
        if (_diveTimer > 0 || diving >= maxDivers)
            return;
        _diveTimer = MathF.Max(0.55f, 2.2f - 0.18f * _wave) * Rand(0.7f, 1.3f);
        _launches++;

        // Every few launches, a flagship leads a raid with its escorts.
        if (_launches % 4 == 0)
        {
            foreach (var f in _aliens)
            {
                if (!f.Alive || f.Type != 3 || f.State != State.Formation) continue;
                Launch(f);
                int escorts = 0;
                foreach (var e in _aliens)
                {
                    if (escorts >= 2 || !e.Alive || e.Type != 2 || e.State != State.Formation || Math.Abs(e.Col - f.Col) > 1)
                        continue;
                    Launch(e);
                    e.Leader = f;
                    e.EscortOffset = new Vector2(escorts == 0 ? -18 : 18, -14);
                    escorts++;
                }
                f.EscortsAlive = escorts;
                f.EscortsKilled = 0;
                Sound.Play(Sfx.Alarm, 0.3f, 0.35f);
                return;
            }
        }

        // Otherwise an alien from the edge of the convoy.
        Alien pick = null;
        float best = -1;
        foreach (var a in _aliens)
        {
            if (!a.Alive || a.State != State.Formation) continue;
            float edge = MathF.Abs(a.Col - 4.5f) + Rand(0, 3) + (a.Type == 3 ? -2 : 0);
            if (edge > best) { best = edge; pick = a; }
        }
        if (pick != null)
            Launch(pick);
    }

    private void Launch(Alien a)
    {
        a.State = State.Peel;
        a.T = 0;
        a.Side = a.Col < 5 ? -1 : 1;
        a.PeelCentre = a.Pos + new Vector2(a.Side * 16, 0);
        a.TargetX = _playerX + Rand(-60, 60);
        a.FireTimer = Rand(0.2f, 0.8f);
        a.Wobble = Rand(0, 6);
        a.Leader = null;
        Sound.Play(Sfx.Whoosh, a.Type == 3 ? -0.4f : 0.2f, 0.4f);
    }

    // ------------------------------------------------------------------ drawing

    public override void Draw(Gfx g)
    {
        float time = Time;
        Backdrops.StarsDown(g, time, 60, 11, Screen.Bounds, 150);
        g.Glow(80, 300, 160, Pal.Purple, 0.18f);
        g.Glow(560, 90, 140, Pal.Blue, 0.18f);

        int frame = (int)(_flap * 3) % 2;
        foreach (var a in _aliens)
        {
            if (!a.Alive) continue;
            var col = TypeGlow[a.Type];
            bool diving = a.State != State.Formation;
            int f = diving ? (int)(_flap * 8) % 2 : (frame + a.Col) % 2;
            g.Glow(a.Pos.X, a.Pos.Y, a.Type == 3 ? 20 : 15, col, diving ? 0.45f : 0.25f);
            g.PixelsCentered(Art[a.Type, f], a.Pos.X, a.Pos.Y, 2f);
        }

        foreach (var b in _bullets)
        {
            g.Glow(b.Pos, 8, Pal.Yellow, 0.7f);
            g.Rect(b.Pos.X - 1, b.Pos.Y - 4, 2, 7, Pal.White);
        }

        if (_respawn <= 0 && (_invuln <= 0 || (int)(_invuln * 10) % 2 == 0) && !IsOver)
        {
            g.Glow(_playerX, PlayerY + 8, 14, Pal.Orange, 0.4f + 0.2f * MathF.Sin(time * 30));
            g.Glow(_playerX, PlayerY, 26, Pal.Sky, 0.25f);
            g.PixelsCentered(Ship, _playerX, PlayerY, 2f);
        }
        if (_respawn <= 0 && !IsOver)
        {
            g.Glow(_shot, 9, Pal.Yellow, _shotActive ? 0.9f : 0.4f);
            g.Rect(_shot.X - 1, _shot.Y - 5, 2, 8, Pal.Yellow);
            if (_shotActive)
                g.Rect(_shot.X - 0.5f, _shot.Y + 3, 1, 8, Pal.Orange * 0.6f);
        }

        // Ground line and lives as ship icons.
        g.Rect(0, 349, 640, 1, Pal.Blue * 0.5f);
        for (int i = 0; i < Lives - 1 && i < 6; i++)
            g.PixelsCentered(Ship, 16 + i * 20, 355, 1f);
        // Wave flags, bottom right.
        for (int i = 0; i < Math.Min(_wave, 10); i++)
        {
            float fx = 630 - i * 9;
            g.Rect(fx, 351, 1, 8, Pal.LightGrey);
            g.Triangle(new Vector2(fx + 1, 351), new Vector2(fx + 7, 353.5f), new Vector2(fx + 1, 356), Pal.Red);
        }

        if (_waveBanner > 0)
        {
            float a = MathF.Min(1, _waveBanner);
            g.TextShadow("WAVE " + _wave, 320, 196, 3f, Pal.Yellow * a, Align.Center);
            g.Text("THE CONVOY APPROACHES", 320, 228, 1.5f, Pal.Cyan * a, Align.Center);
        }
    }

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        Backdrops.StarsDown(g, time, 40, 11, r, 50);
        float s = r.H / 70f;
        int frame = (int)(time * 3) % 2;
        float sway = MathF.Sin(time) * 8 * s;
        for (int row = 0; row < 3; row++)
            for (int c = 0; c < 6; c++)
            {
                int type = row == 0 ? (c == 1 || c == 4 ? 3 : -1) : row == 1 ? 2 : 1;
                if (type < 0) continue;
                float x = r.CenterX + (c - 2.5f) * 20 * s + sway, y = r.Y + 10 * s + row * 13 * s;
                g.Glow(x, y, 10 * s, TypeGlow[type], 0.3f);
                g.PixelsCentered(Art[type, (frame + c) % 2], x, y, 1.2f * s);
            }
        // A diving flagship with escorts.
        float k = (time * 0.45f) % 1f;
        float dx = r.CenterX + MathF.Sin(k * 6) * 40 * s, dy = r.Y + r.H * (0.25f + 0.7f * k);
        g.Glow(dx, dy, 14 * s, Pal.Yellow, 0.5f);
        g.PixelsCentered(Art[3, (int)(time * 8) % 2], dx, dy, 1.3f * s);
        g.PixelsCentered(Art[2, (int)(time * 8) % 2], dx - 13 * s, dy - 9 * s, 1.1f * s);
        g.PixelsCentered(Art[2, (int)(time * 8) % 2], dx + 13 * s, dy - 9 * s, 1.1f * s);
        float px = r.CenterX + MathF.Sin(time * 1.3f) * 30 * s;
        g.PixelsCentered(Ship, px, r.Bottom - 8 * s, 1.2f * s);
        float shotY = r.Bottom - 16 * s - (time * 80 * s) % (r.H * 0.6f);
        g.Glow(px, shotY, 5 * s, Pal.Yellow, 0.8f);
        g.Rect(px - 0.6f * s, shotY - 3 * s, 1.2f * s, 5 * s, Pal.Yellow);
    }

    public override void AutoPlay(Controls c)
    {
        // Line up under the most urgent alien, dodge bullets and divers, fire when lined up.
        float target = _playerX;
        float best = float.MaxValue;
        foreach (var a in _aliens)
        {
            if (!a.Alive || a.State == State.Return) continue;
            float lead = a.State == State.Dive ? a.Vel.X * 0.25f : 0;
            float score = MathF.Abs(a.Pos.X + lead - _playerX) - (a.State == State.Dive ? 80 : 0) - a.Pos.Y * 0.2f;
            if (score < best) { best = score; target = a.Pos.X + lead; }
        }
        foreach (var b in _bullets)
            if (b.Pos.Y > PlayerY - 110 && b.Pos.Y < PlayerY + 4)
            {
                float t = (PlayerY - b.Pos.Y) / MathF.Max(1, b.Vel.Y);
                float bx = b.Pos.X + b.Vel.X * t;
                if (MathF.Abs(bx - _playerX) < 18)
                    target = _playerX + (bx < _playerX ? 50 : -50);
            }
        foreach (var a in _aliens)
            if (a.Alive && a.State == State.Dive && a.Pos.Y > PlayerY - 70 && MathF.Abs(a.Pos.X - _playerX) < 26)
                target = _playerX + (a.Pos.X < _playerX ? 60 : -60);
        if (target < 20) target = 60;
        if (target > 620) target = 580;
        float dx = target - _playerX;
        c.SetDirections(MathF.Abs(dx) < 4 ? 0 : MathF2.Clamp(dx / 30, -1, 1), 0);
        bool lined = false;
        foreach (var a in _aliens)
            if (a.Alive && MathF.Abs(a.Pos.X - _playerX) < 10 && a.Pos.Y < PlayerY - 20) lined = true;
        c.FirePressed = !_shotActive && lined && Tick % 3 == 0;
        c.Fire = c.FirePressed;
    }
}
