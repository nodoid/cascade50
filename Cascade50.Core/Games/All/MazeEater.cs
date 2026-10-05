using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 22 Maze Eater: a hungry muncher clears a neon maze of dots while four goblins hunt it, each in
/// its own way. Power pills turn the tables for a few seconds.
/// </summary>
public sealed class MazeEater : MiniGame
{
    public override int Number => 22;
    public override string Title => "Maze Eater";
    public override Category Category => Category.Arcade;
    public override string Tagline => "Munch every dot in the neon maze while four goblins give chase.";
    public override Color Accent => Pal.Yellow;

    public override string[] HowToPlay =>
    [
        "Eat every dot to clear the maze. Four goblins hunt you, each in its own way. Steer early: turns are remembered.",
        "Power pills turn the goblins blue: eat them for 200, 400, 800 and 1600. Grab the fruit for a bonus.",
        "The side tunnels wrap around. Each maze is faster.",
    ];

    public override string[] DesktopControls => ["ARROWS / WASD to steer."];
    public override string[] TouchControls => ["Use the stick, or swipe on the maze."];
    public override Pad Pad => Pad.Stick;

    private const int Cols = 29, Rows = 15;
    private const float T = 21;
    private const float OX = (640 - Cols * T) / 2, OY = Screen.HudHeight + (360 - Screen.HudHeight - Rows * T) / 2;

    // Left half plus the centre column; the right half is mirrored.
    private static readonly string[] Half =
    [
        "###############",
        "#o............#",
        "#.###.###.###.#",
        "#..............",
        "#.#.###.####.#.",
        "#..............",
        "#.###.####.###-",
        " .....####.#HHH",
        "#.###.####.####",
        "#..............",
        "#.#.###.####.#.",
        "#..............",
        "#.###.###.###.#",
        "#o............#",
        "###############",
    ];

    private static readonly char[,] Layout = BuildLayout();

    private static char[,] BuildLayout()
    {
        var m = new char[Cols, Rows];
        for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Cols; x++)
                m[x, y] = x <= 14 ? Half[y][x] : Half[y][Cols - 1 - x];
        return m;
    }

    private static readonly Point[] Dirs = [new(0, -1), new(-1, 0), new(0, 1), new(1, 0)];
    private static readonly Point PlayerStart = new(14, 11);
    private static readonly Point DoorAbove = new(14, 5);
    private static readonly Point FruitTile = new(14, 9);

    private static readonly Color[] GoblinColours = [Pal.Red, Pal.Pink, Pal.Cyan, Pal.Orange];
    private static readonly string[] GoblinNames = ["SNATCH", "LURK", "FLANK", "DITHER"];
    private static readonly Point[] Corners = [new(27, -2), new(1, -2), new(28, 16), new(0, 16)];
    private static readonly int[] FruitValues = [100, 300, 500, 700, 1000, 2000];

    private static readonly Dictionary<char, Color> FruitColours = new()
    {
        ['r'] = Pal.Red, ['R'] = new Color(150, 10, 20), ['g'] = Pal.Green, ['b'] = Pal.Brown, ['w'] = Pal.White,
        ['o'] = Pal.Orange, ['y'] = Pal.Yellow, ['l'] = Pal.Lime, ['d'] = Pal.Forest,
    };

    private static readonly PixelArt[] FruitArt =
    [
        new(["......gg", ".....g..", "....g.g.", "...g..g.", ".rr...rr", "rwrr.rwr", "rrrr.rrr", ".rr...rr"], FruitColours),
        new(["..gggg..", ".rgggr..", "rrwrrrr.", "rrrrwrr.", ".rwrrr..", ".rrrrr..", "..rwr...", "...r...."], FruitColours),
        new(["...b....", "..ooo...", ".ooooo..", "ooowooo.", "ooooooo.", "ooooooo.", ".ooooo..", "..ooo..."], FruitColours),
        new(["..llll..", ".ldldll.", "lldllldl", "ldllldll", "lldllldl", "ldllldll", ".ldldll.", "..llll.."], FruitColours),
    ];

    private enum GState { House, Leaving, Active, Eyes, Entering }

    private sealed class Goblin
    {
        public int Id;
        public Vector2 Pos;
        public Point Dir;
        public GState State;
        public float Release;
        public bool Frightened;
    }

    private readonly bool[,] _dots = new bool[Cols, Rows];
    private readonly Goblin[] _goblins = new Goblin[4];
    private readonly List<(Vector2 A, Vector2 B)> _wallLines = new();
    private readonly int[,] _foodDist = new int[Cols, Rows];
    private readonly int[,] _ghostDist = new int[Cols, Rows];
    private readonly Queue<Point> _queue = new();
    private Point _aiDir;
    private bool _aiDecided;

    private Vector2 _pos;
    private Point _dir, _want;
    private float _mouth;
    private int _dotsLeft, _dotsEaten;
    private float _fright, _modeTime, _ready, _dying, _clearing, _fruitTime, _roundTime;
    private bool _chase;
    private int _modeIndex, _combo, _nextLife;
    private bool _waka;
    private Vector2 _swipeFrom;
    private bool _swiping;

    protected override void Start()
    {
        Lives = 3;
        Level = 1;
        _nextLife = 10000;
        BuildWalls();
        NewMaze();
    }

    // ------------------------------------------------------------------ setup

    private void BuildWalls()
    {
        _wallLines.Clear();
        const float inset = 5;
        for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Cols; x++)
            {
                if (Layout[x, y] != '#')
                    continue;
                float px = OX + x * T, py = OY + y * T;
                // Draw an inset neon line wherever this wall faces open space.
                if (y > 0 && !IsWallRaw(x, y - 1))
                    AddLine(new Vector2(px + (IsWallRaw(x - 1, y) ? 0 : inset), py + inset), new Vector2(px + T - (IsWallRaw(x + 1, y) ? 0 : inset), py + inset));
                if (y < Rows - 1 && !IsWallRaw(x, y + 1))
                    AddLine(new Vector2(px + (IsWallRaw(x - 1, y) ? 0 : inset), py + T - inset), new Vector2(px + T - (IsWallRaw(x + 1, y) ? 0 : inset), py + T - inset));
                if (x > 0 && !IsWallRaw(x - 1, y))
                    AddLine(new Vector2(px + inset, py + (IsWallRaw(x, y - 1) ? 0 : inset)), new Vector2(px + inset, py + T - (IsWallRaw(x, y + 1) ? 0 : inset)));
                if (x < Cols - 1 && !IsWallRaw(x + 1, y))
                    AddLine(new Vector2(px + T - inset, py + (IsWallRaw(x, y - 1) ? 0 : inset)), new Vector2(px + T - inset, py + T - (IsWallRaw(x, y + 1) ? 0 : inset)));
            }
    }

    private void AddLine(Vector2 a, Vector2 b)
    {
        // Merge with a collinear neighbour to keep the line count down.
        for (int i = 0; i < _wallLines.Count; i++)
        {
            var (p, q) = _wallLines[i];
            if (Vector2.DistanceSquared(q, a) < 0.01f && ((p.X == q.X && a.X == b.X) || (p.Y == q.Y && a.Y == b.Y)))
            {
                _wallLines[i] = (p, b);
                return;
            }
        }
        _wallLines.Add((a, b));
    }

    private static bool IsWallRaw(int x, int y) => x >= 0 && y >= 0 && x < Cols && y < Rows && Layout[x, y] == '#';

    private void NewMaze()
    {
        _dotsLeft = 0;
        _dotsEaten = 0;
        for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Cols; x++)
            {
                char ch = Layout[x, y];
                _dots[x, y] = (ch == '.' || ch == 'o') && !(x == PlayerStart.X && y == PlayerStart.Y);
                if (_dots[x, y])
                    _dotsLeft++;
            }
        _fruitTime = 0;
        Status = "LEVEL " + Level;
        ResetActors();
    }

    private void ResetActors()
    {
        _pos = new Vector2(PlayerStart.X, PlayerStart.Y);
        _dir = new Point(-1, 0);
        _want = _dir;
        _fright = 0;
        _modeTime = 0;
        _modeIndex = 0;
        _chase = false;
        _ready = 2f;
        _roundTime = 0;
        float speedUp = MathF.Max(0.4f, 1 - (Level - 1) * 0.12f);
        for (int i = 0; i < 4; i++)
        {
            var g = new Goblin { Id = i, Release = new[] { 0f, 3f, 8f, 14f }[i] * speedUp };
            if (i == 0)
            {
                g.Pos = new Vector2(DoorAbove.X, DoorAbove.Y);
                g.State = GState.Active;
                g.Dir = new Point(-1, 0);
            }
            else
            {
                g.Pos = new Vector2(12 + (i - 1) * 2, 7);
                g.State = GState.House;
            }
            _goblins[i] = g;
        }
    }

    // ------------------------------------------------------------------ grid helpers

    private static int WrapX(int x) => ((x % Cols) + Cols) % Cols;

    private static char At(int x, int y) => y < 0 || y >= Rows ? '#' : Layout[WrapX(x), y];

    private static bool PlayerCan(int x, int y)
    {
        char c = At(x, y);
        return c != '#' && c != '-' && c != 'H';
    }

    private static bool GoblinCan(int x, int y, bool door)
    {
        char c = At(x, y);
        return c != '#' && (door || (c != '-' && c != 'H'));
    }

    private static Vector2 TileCentre(Vector2 tile) => new(OX + (tile.X + 0.5f) * T, OY + (tile.Y + 0.5f) * T);

    private static Point TileOf(Vector2 p) => new(WrapX((int)MathF.Round(p.X)), (int)MathF.Round(p.Y));

    private float PlayerSpeed => MathF.Min(6.4f + (Level - 1) * 0.3f, 8.4f);

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        if (_dying > 0)
        {
            _dying -= Dt;
            if (_dying <= 0)
            {
                if (Lives <= 1)
                {
                    Lives = 0;
                    EndGame(false, "The goblins caught you on level " + Level + ".");
                    return;
                }
                LoseLife();
                ResetActors();
            }
            return;
        }
        if (_clearing > 0)
        {
            _clearing -= Dt;
            if (_clearing <= 0)
            {
                Level++;
                Sound.Play(Sfx.LevelUp);
                NewMaze();
            }
            return;
        }
        ReadInput();
        if (_ready > 0)
        {
            _ready -= Dt;
            return;
        }
        _roundTime += Dt;

        UpdateModes();
        MovePlayer();
        EatStuff();
        if (_clearing > 0)
            return;
        foreach (var g in _goblins)
            MoveGoblin(g);
        Collide();
        if (_fright > 0)
            Sound.Loop(LoopSfx.Hum, true, 0.4f + 0.3f * MathF.Sin(Time * 20), 0.35f);
        if (_fruitTime > 0)
            _fruitTime -= Dt;
    }

    private void ReadInput()
    {
        if (In.AxisX < -0.5f && MathF.Abs(In.AxisX) >= MathF.Abs(In.AxisY)) _want = new Point(-1, 0);
        else if (In.AxisX > 0.5f && MathF.Abs(In.AxisX) >= MathF.Abs(In.AxisY)) _want = new Point(1, 0);
        else if (In.AxisY < -0.5f) _want = new Point(0, -1);
        else if (In.AxisY > 0.5f) _want = new Point(0, 1);

        // Swipes on the maze.
        if (In.PointerPressed)
        {
            _swipeFrom = In.Pointer;
            _swiping = true;
        }
        if (_swiping && In.PointerDown)
        {
            var d = In.Pointer - _swipeFrom;
            if (d.Length() > 16)
            {
                _want = MathF.Abs(d.X) > MathF.Abs(d.Y) ? new Point(MathF.Sign(d.X), 0) : new Point(0, MathF.Sign(d.Y));
                _swipeFrom = In.Pointer;
            }
        }
        if (In.PointerReleased || !In.PointerDown)
            _swiping = false;
    }

    private void UpdateModes()
    {
        if (_fright > 0)
        {
            _fright -= Dt;
            if (_fright <= 0)
                foreach (var g in _goblins)
                    g.Frightened = false;
            return;
        }
        _modeTime += Dt;
        float scatter = Level == 1 ? 7 : 5;
        float span = _modeIndex >= 5 ? float.MaxValue : _modeIndex % 2 == 0 ? (_modeIndex >= 4 ? 4 : scatter) : (Level == 1 ? 15 : 20);
        if (_modeTime > span)
        {
            _modeTime = 0;
            _modeIndex++;
            _chase = _modeIndex % 2 == 1 || _modeIndex >= 5;
            foreach (var g in _goblins)
                if (g.State == GState.Active)
                    g.Dir = new Point(-g.Dir.X, -g.Dir.Y);
        }
    }

    private void MovePlayer()
    {
        // Reverse at once; other turns wait for the next tile centre.
        if (_want.X == -_dir.X && _want.Y == -_dir.Y && (_want.X != 0 || _want.Y != 0))
            _dir = _want;
        float remaining = PlayerSpeed * Dt;
        bool moved = false;
        for (int guard = 0; guard < 4 && remaining > 0.0001f; guard++)
        {
            var c = new Vector2(MathF.Round(_pos.X), MathF.Round(_pos.Y));
            var dv = new Vector2(_dir.X, _dir.Y);
            float along = Vector2.Dot(_pos - c, dv);
            if (MathF.Abs(along) < 0.001f || (_dir.X == 0 && _dir.Y == 0))
            {
                _pos = c;
                int cx = (int)c.X, cy = (int)c.Y;
                if ((_want.X != 0 || _want.Y != 0) && PlayerCan(cx + _want.X, cy + _want.Y))
                    _dir = _want;
                else if (!PlayerCan(cx + _dir.X, cy + _dir.Y))
                {
                    _dir = Point.Zero;
                    break;
                }
                dv = new Vector2(_dir.X, _dir.Y);
                along = 0;
            }
            var next = along >= 0 ? c + dv : c;
            float d = Vector2.Distance(_pos, next);
            if (remaining < d)
            {
                _pos += dv * remaining;
                remaining = 0;
            }
            else
            {
                _pos = next;
                remaining -= d;
            }
            moved = true;
            WrapTunnel(ref _pos);
        }
        if (moved && (_dir.X != 0 || _dir.Y != 0))
            _mouth += Dt * 9;
    }

    private static void WrapTunnel(ref Vector2 p)
    {
        if (p.X < -0.5f) p.X += Cols;
        if (p.X > Cols - 0.5f) p.X -= Cols;
    }

    private void EatStuff()
    {
        var tile = TileOf(_pos);
        if (Vector2.Distance(_pos, new Vector2(MathF.Round(_pos.X), MathF.Round(_pos.Y))) < 0.3f && tile.Y >= 0 && tile.Y < Rows && _dots[tile.X, tile.Y])
        {
            _dots[tile.X, tile.Y] = false;
            _dotsLeft--;
            _dotsEaten++;
            var sp = TileCentre(new Vector2(tile.X, tile.Y));
            if (Layout[tile.X, tile.Y] == 'o')
            {
                AddScore(50, sp.X, sp.Y - 10, Pal.Pink);
                _fright = MathF.Max(1.5f, 7.5f - Level * 0.7f);
                _combo = 0;
                foreach (var g in _goblins)
                    if (g.State == GState.Active)
                    {
                        g.Frightened = true;
                        g.Dir = new Point(-g.Dir.X, -g.Dir.Y);
                    }
                    else if (g.State == GState.House || g.State == GState.Leaving)
                    {
                        g.Frightened = true;
                    }
                Fx.Burst(sp.X, sp.Y, Pal.Pink, 16, 90, 0.4f, 2f);
                Sound.Play(Sfx.PowerUp);
            }
            else
            {
                AddScore(10);
                _waka = !_waka;
                Sound.Play(Sfx.Tick, _waka ? 0.3f : -0.1f, 0.35f);
            }
            if (_dotsEaten == 60 || _dotsEaten == 140)
                _fruitTime = 9;
            if (Score >= _nextLife)
            {
                _nextLife += 10000;
                Lives++;
                Sound.Play(Sfx.Bonus);
                Fx.Float("EXTRA LIFE!", sp.X, sp.Y - 20, Pal.Lime);
            }
            if (_dotsLeft <= 0)
            {
                _clearing = 2.2f;
                Sound.Play(Sfx.Win, 0.2f, 0.7f);
                AddScore(500 * Level, 320, 180, Pal.Cyan);
                return;
            }
        }
        if (_fruitTime > 0 && Vector2.Distance(_pos, new Vector2(FruitTile.X, FruitTile.Y)) < 0.6f)
        {
            int v = FruitValues[Math.Min(Level - 1, FruitValues.Length - 1)];
            var sp = TileCentre(new Vector2(FruitTile.X, FruitTile.Y));
            AddScore(v, sp.X, sp.Y - 12, Pal.Lime);
            Fx.Burst(sp.X, sp.Y, Pal.Red, 18, 90, 0.5f, 2.2f);
            Sound.Play(Sfx.Pickup);
            _fruitTime = 0;
        }
    }

    private Point TargetFor(Goblin g)
    {
        if (g.State == GState.Eyes)
            return DoorAbove;
        if (!_chase)
            return Corners[g.Id];
        var p = TileOf(_pos);
        var d = _dir;
        switch (g.Id)
        {
            case 0:
                return p;
            case 1:
                return new Point(p.X + d.X * 4, p.Y + d.Y * 4);
            case 2:
            {
                var ahead = new Point(p.X + d.X * 2, p.Y + d.Y * 2);
                var red = TileOf(_goblins[0].Pos);
                return new Point(ahead.X * 2 - red.X, ahead.Y * 2 - red.Y);
            }
            default:
            {
                var me = TileOf(g.Pos);
                float dist = MathF.Sqrt((me.X - p.X) * (me.X - p.X) + (me.Y - p.Y) * (me.Y - p.Y));
                return dist > 7 ? p : Corners[3];
            }
        }
    }

    private float GoblinSpeed(Goblin g)
    {
        float baseSpeed = PlayerSpeed * MathF.Min(0.78f + (Level - 1) * 0.045f, 1.0f);
        if (g.State == GState.Eyes)
            return PlayerSpeed * 2.2f;
        if (g.State is GState.Leaving or GState.Entering or GState.House)
            return 3f;
        int ty = (int)MathF.Round(g.Pos.Y), tx = WrapX((int)MathF.Round(g.Pos.X));
        if (ty == 7 && (tx <= 3 || tx >= Cols - 4))
            return baseSpeed * 0.55f;
        return g.Frightened ? PlayerSpeed * 0.6f : baseSpeed;
    }

    private void MoveGoblin(Goblin g)
    {
        float speed = GoblinSpeed(g);
        switch (g.State)
        {
            case GState.House:
                g.Pos.Y = 7 + MathF.Sin(Time * 6 + g.Id) * 0.25f;
                if (_roundTime > g.Release)
                    g.State = GState.Leaving;
                return;
            case GState.Leaving:
            {
                var target = MathF.Abs(g.Pos.X - 14) > 0.02f ? new Vector2(14, 7) : new Vector2(14, DoorAbove.Y);
                g.Pos = Approach(g.Pos, target, speed * Dt);
                if (Vector2.Distance(g.Pos, new Vector2(14, DoorAbove.Y)) < 0.001f)
                {
                    g.State = GState.Active;
                    g.Dir = Chance(0.5f) ? new Point(-1, 0) : new Point(1, 0);
                }
                return;
            }
            case GState.Entering:
                g.Pos = Approach(g.Pos, new Vector2(14, 7), speed * 1.5f * Dt);
                if (Vector2.Distance(g.Pos, new Vector2(14, 7)) < 0.001f)
                {
                    g.Frightened = false;
                    g.State = GState.Leaving;
                }
                return;
        }

        float remaining = speed * Dt;
        for (int guard = 0; guard < 4 && remaining > 0.0001f; guard++)
        {
            var c = new Vector2(MathF.Round(g.Pos.X), MathF.Round(g.Pos.Y));
            var dv = new Vector2(g.Dir.X, g.Dir.Y);
            float along = Vector2.Dot(g.Pos - c, dv);
            if (MathF.Abs(along) < 0.001f)
            {
                g.Pos = c;
                int cx = WrapX((int)c.X), cy = (int)c.Y;
                if (g.State == GState.Eyes && cx == DoorAbove.X && cy == DoorAbove.Y)
                {
                    g.State = GState.Entering;
                    return;
                }
                g.Dir = ChooseDir(g, cx, cy);
                dv = new Vector2(g.Dir.X, g.Dir.Y);
                along = 0;
            }
            var next = along >= 0 ? c + dv : c;
            float d = Vector2.Distance(g.Pos, next);
            if (remaining < d)
            {
                g.Pos += dv * remaining;
                remaining = 0;
            }
            else
            {
                g.Pos = next;
                remaining -= d;
            }
            WrapTunnel(ref g.Pos);
        }
    }

    private Point ChooseDir(Goblin g, int cx, int cy)
    {
        var back = new Point(-g.Dir.X, -g.Dir.Y);
        if (g.Frightened && g.State == GState.Active)
        {
            int start = RandInt(0, 4);
            for (int k = 0; k < 4; k++)
            {
                var d = Dirs[(start + k) % 4];
                if (d != back && GoblinCan(cx + d.X, cy + d.Y, false))
                    return d;
            }
            return back;
        }
        var target = TargetFor(g);
        Point best = back;
        float bestDist = float.MaxValue;
        foreach (var d in Dirs)
        {
            if (d == back || !GoblinCan(cx + d.X, cy + d.Y, false))
                continue;
            float dx = cx + d.X - target.X, dy = cy + d.Y - target.Y;
            float dist = dx * dx + dy * dy;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = d;
            }
        }
        return best;
    }

    private static Vector2 Approach(Vector2 p, Vector2 target, float step)
    {
        var d = target - p;
        float len = d.Length();
        return len <= step ? target : p + d / len * step;
    }

    private void Collide()
    {
        foreach (var g in _goblins)
        {
            if (g.State is GState.Eyes or GState.Entering)
                continue;
            var d = g.Pos - _pos;
            if (MathF.Abs(d.X) > Cols / 2f)
                d.X -= MathF.Sign(d.X) * Cols;
            if (d.Length() > 0.6f)
                continue;
            if (g.Frightened)
            {
                int pts = 200 << Math.Min(_combo, 3);
                _combo++;
                var sp = TileCentre(g.Pos);
                AddScore(pts, sp.X, sp.Y - 12, Pal.Cyan);
                Fx.Burst(sp.X, sp.Y, Pal.Sky, 20, 110, 0.5f, 2.2f);
                Sound.Play(Sfx.Coin, _combo * 0.15f);
                g.State = GState.Eyes;
                g.Frightened = false;
            }
            else
            {
                _dying = 1.8f;
                Sound.Play(Sfx.Die);
                Fx.Shake(3, 0.3f);
                return;
            }
        }
    }

    // ------------------------------------------------------------------ drawing

    public override void Draw(Gfx g)
    {
        float t = Time;
        g.GradientV(0, 0, 640, 360, new Color(2, 2, 14), new Color(8, 4, 24));
        DrawMaze(g, t);

        // Fruit.
        if (_fruitTime > 0 && _dying <= 0)
        {
            var fp = TileCentre(new Vector2(FruitTile.X, FruitTile.Y));
            g.Glow(fp, 16, Pal.Red, 0.4f);
            g.PixelsCentered(FruitArt[(Level - 1) % FruitArt.Length], fp.X, fp.Y, 2.2f);
        }

        if (_dying <= 0 && _clearing <= 0)
            foreach (var gob in _goblins)
                DrawGoblin(g, TileCentre(gob.Pos), gob.Id, gob.State == GState.Eyes || gob.State == GState.Entering, gob.Frightened, _fright, gob.Dir, t, 1f);

        // Muncher.
        var pp = TileCentre(_pos);
        if (_dying > 0)
        {
            float k = 1 - _dying / 1.8f;
            float open = MathF.PI * MathF.Min(1, k * 1.3f);
            if (k < 0.8f)
                g.Pie(pp.X, pp.Y, 9, -MathF.PI / 2 + open, -MathF.PI / 2 + MathF.Tau - open, Pal.Yellow);
            else
                g.Ring(pp.X, pp.Y, (k - 0.8f) * 60, 1.5f, Pal.Yellow * (1 - (k - 0.8f) * 5));
        }
        else
        {
            float face = _dir.X == 0 && _dir.Y == 0 ? MathF.PI : MathF.Atan2(_dir.Y, _dir.X);
            DrawMuncher(g, pp, face, MathF.Abs(MathF.Sin(_mouth)) * 0.75f + 0.05f, 9);
            // Wrap ghost in the tunnel.
            if (_pos.X < 0.6f || _pos.X > Cols - 1.6f)
                DrawMuncher(g, TileCentre(_pos + new Vector2(_pos.X < 1 ? Cols : -Cols, 0)), face, MathF.Abs(MathF.Sin(_mouth)) * 0.75f, 9);
        }

        // Tunnel mouths hide sprites that wrap.
        g.Rect(0, OY + 7 * T, OX + 0.5f, T, new Color(2, 2, 14));
        g.Rect(640 - OX - 0.5f, OY + 7 * T, OX + 0.5f, T, new Color(2, 2, 14));

        if (_ready > 0 && _dying <= 0)
        {
            var rp = TileCentre(new Vector2(FruitTile.X, FruitTile.Y));
            g.TextShadow("READY!", rp.X, rp.Y - 6, 1.5f, Pal.Yellow, Align.Center);
        }
    }

    private void DrawMaze(Gfx g, float t)
    {
        var neon = _clearing > 0 && (int)(_clearing * 5) % 2 == 0 ? Pal.White : Pal.Hsv(225 + (Level - 1) * 40, 0.85f, 1f);
        foreach (var (a, b) in _wallLines)
        {
            g.Line(a, b, 8, Pal.Add(neon, 0.1f));
            g.Line(a, b, 4, Pal.Add(neon, 0.3f));
            g.Line(a, b, 1.8f, Pal.Lighten(neon, 0.35f));
        }
        // Ghost house door.
        var door = new Vector2(OX + 14 * T, OY + 6 * T + T / 2);
        g.Rect(door.X, door.Y - 1.5f, T, 3, Pal.Pink);

        float pulse = MathF2.Pulse(t, 0.5f);
        for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Cols; x++)
            {
                if (!_dots[x, y])
                    continue;
                var c = TileCentre(new Vector2(x, y));
                if (Layout[x, y] == 'o')
                {
                    g.Glow(c, 16, Pal.Pink, 0.4f + 0.4f * pulse);
                    g.Circle(c.X, c.Y, 4.5f + pulse * 1.2f, new Color(255, 200, 220));
                }
                else
                {
                    g.Circle(c.X, c.Y, 2.1f, new Color(255, 220, 180));
                }
            }
    }

    private static void DrawMuncher(Gfx g, Vector2 p, float face, float mouth, float r)
    {
        g.Glow(p, r * 2.4f, Pal.Yellow, 0.35f);
        g.Pie(p.X, p.Y, r, face + mouth, face + MathF.Tau - mouth, Pal.Yellow, 28);
        var eye = p + MathF2.FromAngle(face - MathF.PI / 2, r * 0.5f) + MathF2.FromAngle(face, r * 0.1f);
        if (MathF.Abs(MathF.Cos(face)) < 0.1f)
            eye = p + new Vector2(r * 0.35f, -r * 0.1f * MathF.Sign(MathF.Sin(face)) - r * 0.2f);
        g.Circle(eye.X, eye.Y, r * 0.13f, Pal.Black);
    }

    private static void DrawGoblin(Gfx g, Vector2 p, int id, bool eyesOnly, bool frightened, float frightLeft, Point dir, float t, float s)
    {
        float r = 9 * s;
        if (!eyesOnly)
        {
            var body = GoblinColours[id];
            if (frightened)
                body = frightLeft < 2 && (int)(t * 6) % 2 == 0 ? Pal.White : new Color(40, 60, 255);
            g.Glow(p, r * 2.4f, body, 0.35f);
            g.Pie(p.X, p.Y - 1 * s, r, MathF.PI, MathF.Tau, body, 20);
            g.Rect(p.X - r, p.Y - 1 * s, r * 2, r * 0.9f, body);
            // Wavy hem.
            int phase = (int)(t * 8) % 2;
            for (int k = 0; k < 4; k++)
            {
                float hx = p.X - r + (k + 0.5f) * r / 2;
                float hy = p.Y - 1 * s + r * 0.9f;
                g.Triangle(new Vector2(hx - r / 4, hy), new Vector2(hx + r / 4, hy), new Vector2(hx + (phase == 0 ? 0 : r / 8), hy + r * 0.35f), body);
            }
            if (frightened)
            {
                var face = frightLeft < 2 && (int)(t * 6) % 2 == 0 ? Pal.Red : Pal.Sand;
                g.Rect(p.X - r * 0.45f, p.Y - r * 0.35f, r * 0.22f, r * 0.22f, face);
                g.Rect(p.X + r * 0.23f, p.Y - r * 0.35f, r * 0.22f, r * 0.22f, face);
                for (int k = 0; k < 4; k++)
                    g.Line(p.X - r * 0.6f + k * r * 0.3f, p.Y + r * 0.3f + (k % 2) * r * 0.15f, p.X - r * 0.3f + k * r * 0.3f, p.Y + r * 0.3f + ((k + 1) % 2) * r * 0.15f, 1.1f * s, face);
                return;
            }
        }
        var look = new Vector2(dir.X, dir.Y) * r * 0.15f;
        for (int k = -1; k <= 1; k += 2)
        {
            var e = p + new Vector2(k * r * 0.38f, -r * 0.25f);
            g.Ellipse(e.X, e.Y, r * 0.28f, r * 0.34f, Pal.White);
            g.Circle(e.X + look.X * 1.4f, e.Y + look.Y * 1.4f, r * 0.15f, new Color(30, 40, 160));
        }
    }

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        float s = r.H / 70f;
        g.GradientV(r.X, r.Y, r.W, r.H, new Color(2, 2, 14), new Color(12, 6, 34));
        var neon = new Color(60, 90, 255);
        void Wall(float x0, float y0, float x1, float y1)
        {
            var a = new Vector2(x0, y0);
            var b = new Vector2(x1, y1);
            g.Line(a, b, 5 * s, Pal.Add(neon, 0.15f));
            g.Line(a, b, 1.6f * s, Pal.Lighten(neon, 0.3f));
        }
        float top = r.Y + 12 * s, bottom = r.Bottom - 12 * s;
        Wall(r.X + 4 * s, top, r.Right - 4 * s, top);
        Wall(r.X + 4 * s, bottom, r.Right - 4 * s, bottom);
        float cy = (top + bottom) / 2;
        // Dots being eaten as the muncher moves along the corridor.
        float span = r.W - 20 * s;
        float k = (time * 0.3f + 0.35f) % 1f;
        float mx = r.X + 10 * s + k * span;
        for (float x = r.X + 14 * s; x < r.Right - 8 * s; x += 10 * s)
            if (x > mx + 4 * s)
                g.Circle(x, cy, 1.8f * s, new Color(255, 220, 180));
        float pillX = r.Right - 14 * s;
        if (pillX > mx + 4 * s)
        {
            g.Glow(pillX, cy, 10 * s, Pal.Pink, 0.6f);
            g.Circle(pillX, cy, 3.5f * s, new Color(255, 200, 220));
        }
        bool scared = mx > pillX - 4 * s;
        for (int i = 0; i < 3; i++)
        {
            float gx = mx - (30 + i * 24) * s;
            if (gx > r.X - 10 * s)
                DrawGoblin(g, new Vector2(gx, cy), i, false, scared, 6, new Point(1, 0), time, s);
        }
        DrawMuncher(g, new Vector2(mx, cy), 0, MathF.Abs(MathF.Sin(time * 10)) * 0.7f + 0.05f, 9 * s);
    }

    // ------------------------------------------------------------------ autopilot

    public override void AutoPlay(Controls c)
    {
        if (_dying > 0 || _clearing > 0)
            return;
        BfsFood();
        BfsGhosts();
        // Commit to a direction until the next tile centre, unless a goblin is right in front.
        var centre = new Vector2(MathF.Round(_pos.X), MathF.Round(_pos.Y));
        bool atCentre = Vector2.Distance(_pos, centre) < 0.12f || (_dir.X == 0 && _dir.Y == 0);
        var cur = TileOf(_pos);
        if (cur.Y < 0 || cur.Y >= Rows)
            return;
        var front = new Point(WrapX(cur.X + _dir.X), cur.Y + _dir.Y);
        bool emergency = front.Y >= 0 && front.Y < Rows && _ghostDist[front.X, front.Y] <= 1 && _ghostDist[cur.X, cur.Y] <= 2;
        if (!atCentre && !emergency && _aiDecided)
        {
            c.SetDirections(_aiDir.X, _aiDir.Y);
            return;
        }
        Point best = _dir;
        float bestScore = float.MinValue;
        foreach (var d in Dirs)
        {
            int nx = WrapX(cur.X + d.X), ny = cur.Y + d.Y;
            if (!PlayerCan(nx, ny))
                continue;
            // Look a few junctions ahead: how close can goblins get, and how far is the food?
            Explore(cur.X, cur.Y, d, 0, 3, out int danger, out int food);
            float score = -food + MathF.Min(danger, 5) * 1.2f;
            if (danger <= 1)
                score -= 1000 - danger * 20;
            if (d.X == -_dir.X && d.Y == -_dir.Y)
                score -= 1.5f;
            if (score > bestScore)
            {
                bestScore = score;
                best = d;
            }
        }
        _aiDir = best;
        _aiDecided = atCentre;
        c.SetDirections(best.X, best.Y);
    }

    private void Explore(int x, int y, Point dir, int step, int depth, out int margin, out int food)
    {
        margin = 999;
        food = 999;
        int cx = x, cy = y;
        for (int k = 0; k < 12; k++)
        {
            cx = WrapX(cx + dir.X);
            cy += dir.Y;
            step++;
            food = Math.Min(food, _foodDist[cx, cy] + step);
            margin = Math.Min(margin, _ghostDist[cx, cy] - step);
            if (margin <= 0)
                return;
            int exits = 0;
            Point onward = dir;
            foreach (var e in Dirs)
                if ((e.X != -dir.X || e.Y != -dir.Y) && PlayerCan(cx + e.X, cy + e.Y))
                {
                    exits++;
                    onward = e;
                }
            if (exits >= 2)
            {
                if (depth <= 0)
                    return;
                int bestMargin = -999, bestFood = 999;
                foreach (var e in Dirs)
                {
                    if ((e.X == -dir.X && e.Y == -dir.Y) || !PlayerCan(cx + e.X, cy + e.Y))
                        continue;
                    Explore(cx, cy, e, step, depth - 1, out int m, out int f);
                    if (m > bestMargin || (m == bestMargin && f < bestFood))
                    {
                        bestMargin = m;
                        bestFood = f;
                    }
                }
                margin = Math.Min(margin, bestMargin);
                food = Math.Min(food, bestFood);
                return;
            }
            if (exits == 0)
            {
                margin = Math.Min(margin, 0);
                return;
            }
            dir = onward;
        }
    }

    private void BfsFood()
    {
        for (int x = 0; x < Cols; x++)
            for (int y = 0; y < Rows; y++)
                _foodDist[x, y] = 999;
        _queue.Clear();
        for (int x = 0; x < Cols; x++)
            for (int y = 0; y < Rows; y++)
                if (_dots[x, y])
                {
                    _foodDist[x, y] = 0;
                    _queue.Enqueue(new Point(x, y));
                }
        if (_fright > 1.5f)
            foreach (var gob in _goblins)
                if (gob.Frightened && gob.State == GState.Active)
                {
                    var p = TileOf(gob.Pos);
                    if (p.Y >= 0 && p.Y < Rows)
                    {
                        _foodDist[p.X, p.Y] = -20;
                        _queue.Enqueue(p);
                    }
                }
        Flood(_foodDist);
    }

    private void BfsGhosts()
    {
        for (int x = 0; x < Cols; x++)
            for (int y = 0; y < Rows; y++)
                _ghostDist[x, y] = 999;
        _queue.Clear();
        foreach (var gob in _goblins)
            if ((gob.State == GState.Active && (!gob.Frightened || _fright < 1.5f)) || gob.State == GState.Leaving)
            {
                var p = gob.State == GState.Leaving ? DoorAbove : TileOf(gob.Pos);
                if (p.Y >= 0 && p.Y < Rows)
                {
                    _ghostDist[p.X, p.Y] = 0;
                    _queue.Enqueue(p);
                }
            }
        Flood(_ghostDist);
    }

    private void Flood(int[,] dist)
    {
        while (_queue.Count > 0)
        {
            var p = _queue.Dequeue();
            foreach (var d in Dirs)
            {
                int nx = WrapX(p.X + d.X), ny = p.Y + d.Y;
                if (!PlayerCan(nx, ny))
                    continue;
                int v = dist[p.X, p.Y] + 1;
                if (v < dist[nx, ny])
                {
                    dist[nx, ny] = v;
                    _queue.Enqueue(new Point(nx, ny));
                }
            }
        }
    }
}
