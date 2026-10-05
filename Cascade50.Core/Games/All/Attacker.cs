using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 01 Attacker: rows of marching aliens creep down the screen. Shoot them all before they land,
/// hiding behind crumbling bunkers. Each wave marches faster.
/// </summary>
public sealed class Attacker : MiniGame
{
    public override int Number => 1;
    public override string Title => "Attacker";
    public override Category Category => Category.Shooter;
    public override string Tagline => "Hold the line against the marching alien horde.";
    public override Color Accent => Pal.Green;

    public override string[] HowToPlay =>
    [
        "Aliens march side to side, dropping lower at each edge. Shoot them all before they land.",
        "Bunkers give cover but crumble under fire from both sides.",
        "Aliens score 10 to 30, the mystery saucer up to 300. Each wave marches faster.",
    ];

    public override string[] DesktopControls => ["LEFT / RIGHT (or A / D) to move, SPACE to fire.", "P or ESC to pause."];
    public override string[] TouchControls => ["Use the stick to move and FIRE to shoot."];
    public override Pad Pad => Pad.Horizontal | Pad.Fire;

    private const int Cols = 10, Rows = 5;
    private const float PlayerY = 334, GroundY = 346;
    private const int BunkerCell = 3, BunkerW = 12, BunkerH = 8;

    private static readonly Dictionary<char, Color> Colours = new()
    {
        ['g'] = Pal.Green, ['G'] = Pal.Lime, ['c'] = Pal.Cyan, ['m'] = Pal.Magenta, ['p'] = Pal.Pink,
        ['y'] = Pal.Yellow, ['w'] = Pal.White, ['r'] = Pal.Red, ['o'] = Pal.Orange,
    };

    private static readonly PixelArt[][] AlienArt =
    [
        [
            new(["...mm...", "..mmmm..", ".mmmmmm.", "mm.mm.mm", "mmmmmmmm", "..m..m..", ".m.mm.m.", "m.m..m.m"], Colours),
            new(["...mm...", "..mmmm..", ".mmmmmm.", "mm.mm.mm", "mmmmmmmm", ".m.mm.m.", "m......m", ".m....m."], Colours),
        ],
        [
            new(["..c.....c..", "...c...c...", "..ccccccc..", ".cc.ccc.cc.", "ccccccccccc", "c.ccccccc.c", "c.c.....c.c", "...cc.cc..."], Colours),
            new(["..c.....c..", "c..c...c..c", "c.ccccccc.c", "ccc.ccc.ccc", "ccccccccccc", ".ccccccccc.", "..c.....c..", ".c.......c."], Colours),
        ],
        [
            new(["....gggg....", ".gggggggggg.", "gggggggggggg", "ggg..gg..ggg", "gggggggggggg", "...gg..gg...", "..gg.gg.gg..", "gg........gg"], Colours),
            new(["....gggg....", ".gggggggggg.", "gggggggggggg", "ggg..gg..ggg", "gggggggggggg", "..ggg..ggg..", ".gg..gg..gg.", "..gg....gg.."], Colours),
        ],
    ];

    private static readonly PixelArt Ship = new(
    [
        "......w......",
        ".....www.....",
        ".....www.....",
        ".ggggggggggg.",
        "ggggggggggggg",
        "ggGGGGGGGGGgg",
        "ggggggggggggg",
    ], Colours);

    private static readonly PixelArt Saucer = new(
    [
        ".....rrrrrr.....",
        "...rrrrrrrrrr...",
        "..rrrrrrrrrrrr..",
        ".rr.rr.rr.rr.rr.",
        "rrrrrrrrrrrrrrrr",
        "..rrr..rr..rrr..",
        "...r........r...",
    ], Colours);

    private struct Alien
    {
        public bool Alive;
        public int Type;
    }

    private struct Shot
    {
        public Vector2 Pos;
        public float Speed;
        public bool Zigzag;
    }

    private readonly Alien[,] _aliens = new Alien[Rows, Cols];
    private readonly List<Shot> _shots = new();
    private readonly List<Shot> _bombs = new();
    private readonly bool[][,] _bunkers = new bool[4][,];
    private float _playerX;
    private float _gridX, _gridY, _dir;
    private int _stepTimer, _animFrame, _alive;
    private float _respawn;
    private float _saucerX = -100, _saucerDir;
    private int _saucerTimer;
    private float _waveBanner;
    private int _shotsFired;

    protected override void Start()
    {
        Lives = 3;
        Level = 1;
        _playerX = 320;
        NewWave();
    }

    private void NewWave()
    {
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                _aliens[r, c] = new Alien { Alive = true, Type = r == 0 ? 0 : r < 3 ? 1 : 2 };
        _alive = Rows * Cols;
        _gridX = 100;
        _gridY = 52 + Math.Min(Level - 1, 5) * 8;
        _dir = 1;
        _shots.Clear();
        _bombs.Clear();
        for (int b = 0; b < 4; b++)
        {
            var cells = new bool[BunkerW, BunkerH];
            for (int x = 0; x < BunkerW; x++)
                for (int y = 0; y < BunkerH; y++)
                {
                    bool corner = (y == 0 && (x < 2 || x >= BunkerW - 2)) || (y == 1 && (x == 0 || x == BunkerW - 1));
                    bool arch = y >= BunkerH - 3 && x >= 4 && x < BunkerW - 4;
                    cells[x, y] = !corner && !arch;
                }
            _bunkers[b] = cells;
        }
        _saucerTimer = 600 + RandInt(0, 600);
        _waveBanner = 2f;
    }

    private static float AlienX(float gridX, int c) => gridX + c * 36;
    private static float AlienY(float gridY, int r) => gridY + r * 26;
    private static float BunkerX(int b) => 92 + b * 140;
    private const float BunkerY = 280;

    protected override void Update()
    {
        if (_waveBanner > 0)
            _waveBanner -= Dt;

        // Player.
        if (_respawn > 0)
        {
            _respawn -= Dt;
        }
        else
        {
            _playerX = MathF2.Clamp(_playerX + In.AxisX * 190 * Dt, 20, 620);
            if (In.FirePressed && _shots.Count < 1 + (Level >= 4 ? 1 : 0))
            {
                _shots.Add(new Shot { Pos = new Vector2(_playerX, PlayerY - 8), Speed = 420 });
                _shotsFired++;
                Sound.Play(Sfx.Shoot);
                Fx.Spark(_playerX, PlayerY - 8, 0, -60, Pal.White, 0.15f, 3);
            }
        }

        MarchAliens();
        MoveShots();
        AlienFire();
        MoveSaucer();

        if (_alive == 0)
        {
            AddScore(100 * Level, 320, 180, Pal.Cyan);
            Level++;
            Sound.Play(Sfx.LevelUp);
            NewWave();
        }
    }

    private void MarchAliens()
    {
        // Fewer aliens and later waves march faster, as in the arcade.
        int interval = Math.Max(2, (int)(4 + _alive * 0.9f) - (Level - 1) * 3);
        if (++_stepTimer < interval)
            return;
        _stepTimer = 0;
        _animFrame ^= 1;
        Sound.Play(Sfx.Step, _animFrame == 0 ? -0.6f : -0.8f, 0.6f);

        float minX = float.MaxValue, maxX = float.MinValue;
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                if (_aliens[r, c].Alive)
                {
                    minX = MathF.Min(minX, AlienX(_gridX, c));
                    maxX = MathF.Max(maxX, AlienX(_gridX, c));
                }
        float step = 6 * _dir;
        if ((maxX + step > 612 && _dir > 0) || (minX + step < 28 && _dir < 0))
        {
            _dir = -_dir;
            _gridY += 10;
        }
        else
        {
            _gridX += step;
        }

        // Landed?
        for (int r = Rows - 1; r >= 0; r--)
            for (int c = 0; c < Cols; c++)
                if (_aliens[r, c].Alive)
                {
                    float y = AlienY(_gridY, r);
                    if (y + 8 >= BunkerY)
                        ChewBunkers(AlienX(_gridX, c), y + 8);
                    if (y + 12 >= PlayerY)
                    {
                        Fx.Explode(_playerX, PlayerY, 2);
                        Sound.Play(Sfx.BigExplode);
                        Lives = 0;
                        EndGame(false, "The aliens have landed!");
                        return;
                    }
                }
    }

    private void MoveShots()
    {
        for (int i = _shots.Count - 1; i >= 0; i--)
        {
            var s = _shots[i];
            s.Pos.Y -= s.Speed * Dt;
            _shots[i] = s;
            bool remove = s.Pos.Y < Screen.HudHeight;
            if (!remove && HitBunker(s.Pos))
                remove = true;
            if (!remove && HitAlien(s.Pos))
                remove = true;
            if (!remove && _saucerX > -50 && MathF.Abs(s.Pos.X - _saucerX) < 16 && MathF.Abs(s.Pos.Y - 40) < 8)
            {
                int points = Pick(50, 100, 150, 300);
                if (_shotsFired % 15 == 0)
                    points = 300;
                AddScore(points, _saucerX, 40, Pal.Red);
                Fx.Explode(_saucerX, 40, 1.4f);
                Sound.Play(Sfx.BigExplode);
                _saucerX = -100;
                remove = true;
            }
            for (int j = _bombs.Count - 1; j >= 0 && !remove; j--)
                if (Vector2.DistanceSquared(_bombs[j].Pos, s.Pos) < 30)
                {
                    Fx.Burst(s.Pos.X, s.Pos.Y, Pal.White, 8, 60, 0.3f);
                    _bombs.RemoveAt(j);
                    remove = true;
                }
            if (remove)
                _shots.RemoveAt(i);
        }

        for (int i = _bombs.Count - 1; i >= 0; i--)
        {
            var b = _bombs[i];
            b.Pos.Y += b.Speed * Dt;
            _bombs[i] = b;
            bool remove = b.Pos.Y > GroundY;
            if (remove)
                Fx.Burst(b.Pos.X, GroundY, Pal.Green, 5, 40, 0.3f);
            if (!remove && HitBunker(b.Pos))
                remove = true;
            if (!remove && _respawn <= 0 && MathF.Abs(b.Pos.X - _playerX) < 11 && b.Pos.Y > PlayerY - 6 && b.Pos.Y < PlayerY + 8)
            {
                remove = true;
                Fx.Explode(_playerX, PlayerY, 1.5f);
                Sound.Play(Sfx.BigExplode);
                _bombs.Clear();
                if (!LoseLife())
                    _respawn = 1.5f;
                return;
            }
            if (remove)
                _bombs.RemoveAt(i);
        }
    }

    private bool HitAlien(Vector2 p)
    {
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
            {
                if (!_aliens[r, c].Alive)
                    continue;
                float x = AlienX(_gridX, c), y = AlienY(_gridY, r);
                if (MathF.Abs(p.X - x) < 13 && p.Y > y - 2 && p.Y < y + 18)
                {
                    _aliens[r, c].Alive = false;
                    _alive--;
                    int type = _aliens[r, c].Type;
                    int points = type == 0 ? 30 : type == 1 ? 20 : 10;
                    AddScore(points, x, y);
                    var colour = type == 0 ? Pal.Magenta : type == 1 ? Pal.Cyan : Pal.Green;
                    Fx.Burst(x, y + 8, colour, 20, 130, 0.6f, 2.5f);
                    Fx.Burst(x, y + 8, Pal.White, 6, 60, 0.3f, 2f);
                    Sound.Play(Sfx.Explode, Rand(-0.2f, 0.3f), 0.8f);
                    return true;
                }
            }
        return false;
    }

    private bool HitBunker(Vector2 p)
    {
        for (int b = 0; b < 4; b++)
        {
            float bx = BunkerX(b), by = BunkerY;
            int cx = (int)((p.X - bx) / BunkerCell), cy = (int)((p.Y - by) / BunkerCell);
            if (p.X < bx || p.Y < by || cx >= BunkerW || cy >= BunkerH)
                continue;
            if (!_bunkers[b][cx, cy])
                continue;
            // Blast a small crater.
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x >= 0 && y >= 0 && x < BunkerW && y < BunkerH && (dx == 0 || dy == 0 || Chance(0.5f)))
                        _bunkers[b][x, y] = false;
                }
            Fx.Burst(p.X, p.Y, Pal.Green, 6, 50, 0.3f, 1.5f, 0, false);
            Sound.Play(Sfx.Crack, Rand(-0.5f, 0), 0.4f);
            return true;
        }
        return false;
    }

    private void ChewBunkers(float x, float y)
    {
        for (int b = 0; b < 4; b++)
        {
            float bx = BunkerX(b);
            for (int cx = 0; cx < BunkerW; cx++)
                for (int cy = 0; cy < BunkerH; cy++)
                {
                    float px = bx + cx * BunkerCell, py = BunkerY + cy * BunkerCell;
                    if (MathF.Abs(px - x) < 14 && py < y)
                        _bunkers[b][cx, cy] = false;
                }
        }
    }

    private void AlienFire()
    {
        float chance = 0.012f + Level * 0.006f + (50 - _alive) * 0.0006f;
        if (_bombs.Count >= 2 + Level || !Chance(chance))
            return;
        // A random column fires from its lowest alien; sometimes the one above the player.
        int col = Chance(0.4f) ? (int)MathF2.Clamp(MathF.Round((_playerX - _gridX) / 36), 0, Cols - 1) : RandInt(0, Cols);
        for (int r = Rows - 1; r >= 0; r--)
            if (_aliens[r, col].Alive)
            {
                _bombs.Add(new Shot
                {
                    Pos = new Vector2(AlienX(_gridX, col), AlienY(_gridY, r) + 18),
                    Speed = 110 + Level * 12,
                    Zigzag = Chance(0.5f),
                });
                Sound.Play(Sfx.Zap, 0.4f, 0.25f);
                return;
            }
    }

    private void MoveSaucer()
    {
        if (_saucerX > -50)
        {
            _saucerX += _saucerDir * 80 * Dt;
            Sound.Loop(LoopSfx.Hum, true, 0.3f + 0.2f * MathF.Sin(Time * 12), 0.5f);
            if (_saucerX < -40 || _saucerX > 680)
                _saucerX = -100;
        }
        else if (--_saucerTimer <= 0)
        {
            _saucerDir = Chance(0.5f) ? 1 : -1;
            _saucerX = _saucerDir > 0 ? -30 : 670;
            _saucerTimer = 900 + RandInt(0, 600);
        }
    }

    public override void Draw(Gfx g)
    {
        Backdrops.Space(g, Time, 0, 3, Screen.Bounds);
        // Distant planet.
        g.Glow(560, 120, 90, Pal.Purple, 0.3f);
        g.Circle(560, 120, 34, new Color(40, 20, 70));
        g.Ellipse(552, 112, 22, 18, new Color(60, 30, 100));

        // Ground.
        g.GradientV(0, GroundY, 640, 360 - GroundY, Pal.Forest, Pal.Black);
        g.Rect(0, GroundY, 640, 2, Pal.Green);

        // Bunkers.
        for (int b = 0; b < 4; b++)
        {
            float bx = BunkerX(b);
            for (int x = 0; x < BunkerW; x++)
                for (int y = 0; y < BunkerH; y++)
                    if (_bunkers[b][x, y])
                        g.Rect(bx + x * BunkerCell, BunkerY + y * BunkerCell, BunkerCell, BunkerCell,
                            Pal.Lerp(Pal.Lime, Pal.Green, y / (float)BunkerH));
        }

        // Aliens.
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
            {
                if (!_aliens[r, c].Alive)
                    continue;
                var art = AlienArt[_aliens[r, c].Type][_animFrame];
                float x = AlienX(_gridX, c), y = AlienY(_gridY, r);
                var col = _aliens[r, c].Type == 0 ? Pal.Magenta : _aliens[r, c].Type == 1 ? Pal.Cyan : Pal.Green;
                g.Glow(x, y + 8, 18, col, 0.25f);
                g.PixelsCentered(art, x, y + 8, 2.2f);
            }

        // Saucer.
        if (_saucerX > -50)
        {
            g.Glow(_saucerX, 40, 30, Pal.Red, 0.5f);
            g.PixelsCentered(Saucer, _saucerX, 40, 2f);
            for (int i = 0; i < 4; i++)
                g.Circle(_saucerX - 9 + i * 6, 43, 1.2f, (int)(Time * 10 + i) % 2 == 0 ? Pal.Yellow : Pal.White);
        }

        // Shots and bombs.
        foreach (var s in _shots)
        {
            g.Glow(s.Pos, 10, Pal.Cyan, 0.7f);
            g.Rect(s.Pos.X - 1, s.Pos.Y - 6, 2, 10, Pal.White);
        }
        foreach (var b in _bombs)
        {
            g.Glow(b.Pos, 9, Pal.Orange, 0.6f);
            if (b.Zigzag)
            {
                float k = (int)(b.Pos.Y / 4) % 2 == 0 ? 2 : -2;
                g.Line(b.Pos.X - k, b.Pos.Y - 6, b.Pos.X + k, b.Pos.Y - 2, 1.6f, Pal.Yellow);
                g.Line(b.Pos.X + k, b.Pos.Y - 2, b.Pos.X - k, b.Pos.Y + 2, 1.6f, Pal.Yellow);
            }
            else
            {
                g.Rect(b.Pos.X - 1.5f, b.Pos.Y - 5, 3, 8, Pal.Orange);
            }
        }

        // Player.
        if (_respawn <= 0 || (int)(_respawn * 10) % 2 == 0)
        {
            if (!IsOver)
            {
                g.Glow(_playerX, PlayerY, 24, Pal.Green, 0.35f);
                g.PixelsCentered(Ship, _playerX, PlayerY, 2f);
            }
        }

        if (_waveBanner > 0)
        {
            float a = MathF.Min(1, _waveBanner);
            g.TextShadow("WAVE " + Level, 320, 200, 3f, Pal.Yellow * a, Align.Center);
        }
    }

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        Backdrops.Space(g, time, 0, 3, r);
        float s = r.H / 70f;
        int frame = (int)(time * 2) % 2;
        float sway = MathF.Sin(time * 1.5f) * 10 * s;
        for (int row = 0; row < 3; row++)
            for (int c = 0; c < 5; c++)
            {
                float x = r.CenterX - 48 * s + c * 24 * s + sway;
                float y = r.Y + 12 * s + row * 15 * s;
                var col = row == 0 ? Pal.Magenta : row == 1 ? Pal.Cyan : Pal.Green;
                g.Glow(x, y, 10 * s, col, 0.3f);
                g.PixelsCentered(AlienArt[row][frame], x, y, 1.3f * s);
            }
        float px = r.CenterX + MathF.Sin(time * 0.9f) * 30 * s;
        g.PixelsCentered(Ship, px, r.Bottom - 9 * s, 1.4f * s);
        float shotY = r.Bottom - 16 * s - ((time * 60 * s) % (40 * s));
        g.Glow(px, shotY, 6 * s, Pal.Cyan, 0.8f);
        g.Rect(px - 0.7f * s, shotY - 3 * s, 1.4f * s, 6 * s, Pal.White);
    }

    public override void AutoPlay(Controls c)
    {
        // Track the nearest column of aliens and keep firing; dodge bombs that are close.
        float target = _playerX;
        float best = float.MaxValue;
        for (int r = 0; r < Rows; r++)
            for (int col = 0; col < Cols; col++)
                if (_aliens[r, col].Alive)
                {
                    float x = AlienX(_gridX, col);
                    float d = MathF.Abs(x - _playerX) - r * 4;
                    if (d < best)
                    {
                        best = d;
                        target = x;
                    }
                }
        foreach (var b in _bombs)
            if (b.Pos.Y > PlayerY - 70 && MathF.Abs(b.Pos.X - _playerX) < 16)
                target = _playerX + (b.Pos.X < _playerX ? 40 : -40);
        float dx = target - _playerX;
        c.SetDirections(MathF.Abs(dx) < 3 ? 0 : MathF.Sign(dx), 0);
        c.FirePressed = Tick % 10 == 0;
        c.Fire = c.FirePressed;
    }
}
