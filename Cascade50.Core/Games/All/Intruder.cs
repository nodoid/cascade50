using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 17 Intruder: a maze of electrified rooms patrolled by robots that shoot back. Blast them, then
/// escape through an open door before the unstoppable guardian comes bouncing in after you.
/// </summary>
public sealed class Intruder : MiniGame
{
    public override int Number => 17;
    public override string Title => "Intruder";
    public override Category Category => Category.Shooter;
    public override string Tagline => "Shoot your way through robot-guarded rooms of electrified walls.";
    public override Color Accent => Pal.Cyan;

    public override string[] HowToPlay =>
    [
        "Each room is patrolled by robots. You shoot the way you last moved. The walls are electrified: never touch them.",
        "Leave by any open door. Stay too long and the bouncing guardian arrives: it cannot be killed, so run!",
        "Robots score 50, with a bonus for clearing a room. They grow faster and sharper as you go.",
    ];

    public override string[] DesktopControls => ["ARROWS / WASD to move (8 ways).", "SPACE to shoot the way you face."];
    public override string[] TouchControls => ["Stick to move, FIRE to shoot the way you face."];
    public override Pad Pad => Pad.Stick | Pad.Fire;

    private const float X0 = 20, Y0 = 34, X1 = 620, Y1 = 350, T = 4;
    private const float CellW = (X1 - X0) / 5, CellH = (Y1 - Y0) / 3, MidY = (Y0 + Y1) / 2, MidX = (X0 + X1) / 2;
    private const float DoorV = 30, DoorH = 36;
    private const float PlayerSpeed = 82, PW = 5, PH = 9;
    private const int GridStep = 8, GW = (int)((X1 - X0) / GridStep), GH = (int)((Y1 - Y0) / GridStep) + 1;

    private static readonly Dictionary<char, Color> Colours = new()
    {
        ['g'] = Pal.Lime, ['G'] = Pal.Green, ['w'] = Pal.White, ['y'] = Pal.Yellow, ['k'] = Pal.Black,
    };

    private static readonly PixelArt[] PlayerArt =
    [
        new(["..gg..", "..gg..", ".gGGg.", "g.GG.g", "g.GG.g", "..gg..", ".g..g.", ".g..g.", ".g..g."], Colours),
        new(["..gg..", "..gg..", ".gGGg.", "g.GG.g", ".gGG.g", "..gg..", ".g..g.", "g....g", "g....g"], Colours),
    ];

    private static readonly PixelArt[] RobotArt =
    [
        new(["..wwww..", ".wwwwww.", "ww....ww", ".wwwwww.", "..wwww..", "wwwwwwww", "w.wwww.w", "w.wwww.w", "..w..w..", "..w..w..", ".ww..ww."], Colours),
        new(["..wwww..", ".wwwwww.", "ww....ww", ".wwwwww.", "..wwww..", "wwwwwwww", "w.wwww.w", "w.wwww.w", "..w..w..", ".w....w.", "ww....ww"], Colours),
    ];

    private static readonly Color[] RobotColours = [Pal.Yellow, Pal.Red, Pal.Cyan, Pal.Lime, Pal.Magenta, Pal.Orange, Pal.White, Pal.Sky];

    private struct Wall
    {
        public RectF R;
        public bool Barrier;
    }

    private sealed class Robot
    {
        public Vector2 Pos;
        public float Cool, Walk, Phase;
        public bool Dead;
    }

    private struct Bullet
    {
        public Vector2 Pos, Vel;
        public bool Enemy;
        public Robot Owner;
    }

    private readonly List<Wall> _walls = new();
    private readonly List<Wall> _oldWalls = new();
    private readonly List<Robot> _robots = new();
    private readonly List<Bullet> _bullets = new();
    private readonly int[] _field = new int[GW * GH];
    private readonly Queue<int> _queue = new();

    private Vector2 _pos, _facing;
    private int _entry; // 0 left, 1 right, 2 top, 3 bottom
    private int _exitTarget;
    private float _roomTime, _shootCool, _dying, _transition, _alert, _guardianTime, _clearBanner;
    private int _transitionDir;
    private bool _guardian;
    private Vector2 _guardianPos;
    private int _roomRobots, _rooms;
    private float _walkAnim, _wallFlash;

    protected override void Start()
    {
        Lives = 3;
        Level = 1;
        _rooms = 0;
        _entry = 0;
        BuildRoom(_walls, 0);
        EnterRoom();
    }

    // ------------------------------------------------------------------ rooms

    private void BuildRoom(List<Wall> walls, int entry)
    {
        for (int attempt = 0; attempt < 30; attempt++)
        {
            walls.Clear();
            void Add(float x, float y, float w, float h, bool barrier = false) =>
                walls.Add(new Wall { R = new RectF(x, y, w, h), Barrier = barrier });

            Add(X0 - T / 2, Y0 - T / 2, MidX - DoorH - X0 + T / 2, T);
            Add(MidX + DoorH, Y0 - T / 2, X1 - MidX - DoorH + T / 2, T);
            Add(X0 - T / 2, Y1 - T / 2, MidX - DoorH - X0 + T / 2, T);
            Add(MidX + DoorH, Y1 - T / 2, X1 - MidX - DoorH + T / 2, T);
            Add(X0 - T / 2, Y0 - T / 2, T, MidY - DoorV - Y0 + T / 2);
            Add(X0 - T / 2, MidY + DoorV, T, Y1 - MidY - DoorV + T / 2);
            Add(X1 - T / 2, Y0 - T / 2, T, MidY - DoorV - Y0 + T / 2);
            Add(X1 - T / 2, MidY + DoorV, T, Y1 - MidY - DoorV + T / 2);
            switch (entry)
            {
                case 0: Add(X0 - T / 2, MidY - DoorV, T, DoorV * 2, true); break;
                case 1: Add(X1 - T / 2, MidY - DoorV, T, DoorV * 2, true); break;
                case 2: Add(MidX - DoorH, Y0 - T / 2, DoorH * 2, T, true); break;
                default: Add(MidX - DoorH, Y1 - T / 2, DoorH * 2, T, true); break;
            }
            // Berzerk-style: each inner pillar grows a wall one cell long in a random direction.
            for (int i = 1; i <= 4; i++)
                for (int j = 1; j <= 2; j++)
                {
                    float x = X0 + i * CellW, y = Y0 + j * CellH;
                    switch (RandInt(0, 4))
                    {
                        case 0: Add(x - T / 2, y - CellH, T, CellH + T / 2); break;
                        case 1: Add(x - T / 2, y - T / 2, T, CellH + T / 2); break;
                        case 2: Add(x - CellW, y - T / 2, CellW + T / 2, T); break;
                        default: Add(x - T / 2, y - T / 2, CellW + T / 2, T); break;
                    }
                }
            if (ReachableExits(walls, entry) >= 2)
                return;
        }
    }

    private int ReachableExits(List<Wall> walls, int entry)
    {
        int count = 0;
        var start = EntryPoint(entry);
        for (int e = 0; e < 4; e++)
        {
            if (e == entry)
                continue;
            BuildField(walls, e);
            if (FieldAt(start) < int.MaxValue)
                count++;
        }
        return count;
    }

    private static Vector2 EntryPoint(int side) => side switch
    {
        0 => new Vector2(X0 + 16, MidY),
        1 => new Vector2(X1 - 16, MidY),
        2 => new Vector2(MidX, Y0 + 18),
        _ => new Vector2(MidX, Y1 - 18),
    };

    private static Vector2 ExitPoint(int side) => side switch
    {
        0 => new Vector2(X0 - 20, MidY),
        1 => new Vector2(X1 + 20, MidY),
        2 => new Vector2(MidX, Y0 - 20),
        _ => new Vector2(MidX, Y1 + 20),
    };

    private void EnterRoom()
    {
        _pos = EntryPoint(_entry);
        _facing = _entry switch { 0 => new Vector2(1, 0), 1 => new Vector2(-1, 0), 2 => new Vector2(0, 1), _ => new Vector2(0, -1) };
        _bullets.Clear();
        _robots.Clear();
        _roomTime = 0;
        _guardian = false;
        _alert = 2;
        _clearBanner = 0;
        int n = Math.Min(4 + Level, 11);
        _roomRobots = n;
        int cellOfEntry = CellIndex(_pos);
        for (int k = 0, tries = 0; k < n && tries < 400; tries++)
        {
            var p = new Vector2(Rand(X0 + 22, X1 - 22), Rand(Y0 + 22, Y1 - 22));
            if (CellIndex(p) == cellOfEntry || Vector2.Distance(p, _pos) < 150 || HitsWall(_walls, p, 14, 16))
                continue;
            bool clash = false;
            foreach (var r in _robots)
                if (Vector2.Distance(r.Pos, p) < 34)
                    clash = true;
            if (clash)
                continue;
            _robots.Add(new Robot { Pos = p, Cool = Rand(1.5f, 3f), Phase = Rand(0, 6) });
            k++;
        }
        _guardianTime = MathF.Max(7f, 15f - Level * 0.6f);
        // Choose a reachable exit for the autopilot.
        _exitTarget = -1;
        foreach (int e in new[] { RandInt(0, 4), 0, 1, 2, 3 })
        {
            if (e == _entry)
                continue;
            BuildField(_walls, e);
            if (FieldAt(_pos) < int.MaxValue)
            {
                _exitTarget = e;
                break;
            }
        }
        Status = "ROOM " + (_rooms + 1);
        Sound.Play(Sfx.Alarm, 0.3f, 0.35f);
    }

    private static int CellIndex(Vector2 p) =>
        (int)MathF2.Clamp((p.X - X0) / CellW, 0, 4) + 5 * (int)MathF2.Clamp((p.Y - Y0) / CellH, 0, 2);

    private static bool HitsWall(List<Wall> walls, Vector2 p, float hw, float hh)
    {
        var box = new RectF(p.X - hw, p.Y - hh, hw * 2, hh * 2);
        foreach (var w in walls)
            if (w.R.Intersects(box))
                return true;
        return false;
    }

    private static bool PointInWall(List<Wall> walls, Vector2 p)
    {
        foreach (var w in walls)
            if (w.R.Contains(p))
                return true;
        return false;
    }

    private bool LineClear(Vector2 a, Vector2 b)
    {
        float len = Vector2.Distance(a, b);
        int steps = (int)(len / 4) + 1;
        for (int i = 1; i < steps; i++)
            if (PointInWall(_walls, Vector2.Lerp(a, b, i / (float)steps)))
                return false;
        return true;
    }

    private void BuildField(List<Wall> walls, int exit)
    {
        Array.Fill(_field, int.MaxValue);
        _queue.Clear();
        for (int gx = 0; gx < GW; gx++)
            for (int gy = 0; gy < GH; gy++)
            {
                var p = new Vector2(X0 + gx * GridStep + GridStep / 2f, Y0 + gy * GridStep + GridStep / 2f);
                bool edge = exit switch
                {
                    0 => gx == 0, 1 => gx == GW - 1, 2 => gy == 0, _ => gy == GH - 1,
                };
                if (edge && !HitsWall(walls, p, PW + 5, PH + 5))
                {
                    bool inDoor = exit <= 1 ? MathF.Abs(p.Y - MidY) < DoorV - PH : MathF.Abs(p.X - MidX) < DoorH - PW;
                    if (inDoor)
                    {
                        _field[gx + gy * GW] = 0;
                        _queue.Enqueue(gx + gy * GW);
                    }
                }
            }
        while (_queue.Count > 0)
        {
            int i = _queue.Dequeue();
            int gx = i % GW, gy = i / GW;
            for (int d = 0; d < 4; d++)
            {
                int nx = gx + (d == 0 ? -1 : d == 1 ? 1 : 0), ny = gy + (d == 2 ? -1 : d == 3 ? 1 : 0);
                if (nx < 0 || ny < 0 || nx >= GW || ny >= GH)
                    continue;
                int j = nx + ny * GW;
                if (_field[j] != int.MaxValue)
                    continue;
                var p = new Vector2(X0 + nx * GridStep + GridStep / 2f, Y0 + ny * GridStep + GridStep / 2f);
                if (HitsWall(walls, p, PW + 5, PH + 5))
                {
                    _field[j] = int.MaxValue - 1;
                    continue;
                }
                _field[j] = _field[i] + 1;
                _queue.Enqueue(j);
            }
        }
    }

    private int FieldAt(Vector2 p)
    {
        int gx = (int)((p.X - X0) / GridStep), gy = (int)((p.Y - Y0) / GridStep);
        if (p.X < X0 || p.Y < Y0 || p.X >= X1 || p.Y >= Y1)
            return 0; // already in a doorway
        gx = Math.Clamp(gx, 0, GW - 1);
        gy = Math.Clamp(gy, 0, GH - 1);
        int v = _field[gx + gy * GW];
        return v >= int.MaxValue - 1 ? int.MaxValue : v;
    }

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        if (_alert > 0)
            _alert -= Dt;
        if (_clearBanner > 0)
            _clearBanner -= Dt;
        if (_wallFlash > 0)
            _wallFlash -= Dt;

        if (_transition > 0)
        {
            _transition -= Dt * 1.6f;
            if (_transition <= 0)
            {
                _transition = 0;
                EnterRoom();
            }
            return;
        }

        if (_dying > 0)
        {
            _dying -= Dt;
            if (_dying <= 0)
            {
                if (Lives <= 1)
                {
                    Lives = 0;
                    EndGame(false, "The robots got you.");
                    return;
                }
                LoseLife();
                _entry = 0;
                BuildRoom(_walls, _entry);
                EnterRoom();
            }
            return;
        }

        _roomTime += Dt;
        MovePlayer();
        if (_dying > 0 || _transition > 0)
            return;
        MoveRobots();
        MoveBullets();
        MoveGuardian();
    }

    private void MovePlayer()
    {
        var move = new Vector2(In.AxisX, In.AxisY);
        if (move.LengthSquared() > 0.1f)
        {
            if (move.LengthSquared() > 1)
                move.Normalize();
            // Facing snaps to the nearest of the eight directions.
            float a = MathF.Round(MathF.Atan2(move.Y, move.X) / (MathF.PI / 4)) * (MathF.PI / 4);
            _facing = MathF2.FromAngle(a);
            _pos += move * PlayerSpeed * Dt;
            _walkAnim += Dt;
        }
        if (HitsWall(_walls, _pos, PW, PH))
        {
            Electrocute();
            return;
        }

        // Out through a door?
        int exit = _pos.X < X0 - 4 ? 0 : _pos.X > X1 + 4 ? 1 : _pos.Y < Y0 - 4 ? 2 : _pos.Y > Y1 + 4 ? 3 : -1;
        if (exit >= 0)
        {
            LeaveRoom(exit);
            return;
        }

        if (_shootCool > 0)
            _shootCool -= Dt;
        if ((In.FirePressed || In.Fire) && _shootCool <= 0 && CountBullets(false) < 2)
        {
            _bullets.Add(new Bullet { Pos = _pos + _facing * 8, Vel = _facing * 330 });
            _shootCool = 0.28f;
            Sound.Play(Sfx.Laser, 0.3f, 0.6f);
        }
    }

    private int CountBullets(bool enemy)
    {
        int n = 0;
        foreach (var b in _bullets)
            if (b.Enemy == enemy)
                n++;
        return n;
    }

    private void LeaveRoom(int exit)
    {
        _rooms++;
        Level = Math.Min(_rooms + 1, 99);
        _transitionDir = exit;
        _transition = 1;
        _entry = exit switch { 0 => 1, 1 => 0, 2 => 3, _ => 2 };
        _oldWalls.Clear();
        _oldWalls.AddRange(_walls);
        BuildRoom(_walls, _entry);
        _robots.Clear();
        _bullets.Clear();
        _guardian = false;
        Sound.Play(Sfx.Warp, 0, 0.6f);
    }

    private void Electrocute()
    {
        _dying = 1.4f;
        _wallFlash = 0.4f;
        Sound.Play(Sfx.Zap, -0.4f);
        Sound.Play(Sfx.Die);
        Fx.Burst(_pos.X, _pos.Y, Pal.Cyan, 30, 140, 0.7f, 2.5f);
        Fx.Burst(_pos.X, _pos.Y, Pal.Lime, 20, 90, 0.9f, 2f);
        Fx.Shake(4, 0.4f);
    }

    private void MoveRobots()
    {
        float speed = MathF.Min(14 + 4.5f * Level, 58);
        bool smart = Level >= 4;
        float fireRate = 1 + 0.15f * Level;
        for (int i = 0; i < _robots.Count; i++)
        {
            var r = _robots[i];
            if (r.Dead)
                continue;
            r.Phase += Dt;
            var to = _pos - r.Pos;
            float dist = to.Length();

            // Walk in bursts towards the player.
            r.Walk -= Dt;
            if (r.Walk < -0.7f)
                r.Walk = Rand(0.6f, 1.6f);
            if (r.Walk > 0 && _roomTime > 0.8f && dist > 4)
            {
                var step = new Vector2(MathF.Abs(to.X) > 4 ? MathF.Sign(to.X) : 0, MathF.Abs(to.Y) > 4 ? MathF.Sign(to.Y) : 0);
                if (step != Vector2.Zero)
                    step.Normalize();
                var next = r.Pos + step * speed * Dt;
                if (smart && HitsWall(_walls, next, 8, 10))
                {
                    var alt = r.Pos + new Vector2(step.X, 0) * speed * Dt;
                    next = !HitsWall(_walls, alt, 8, 10) ? alt : r.Pos + new Vector2(0, step.Y) * speed * Dt;
                    if (HitsWall(_walls, next, 8, 10))
                        next = r.Pos;
                }
                r.Pos = next;
            }
            if (HitsWall(_walls, r.Pos, 6, 9))
            {
                KillRobot(r, true);
                continue;
            }
            if (MathF.Abs(to.X) < 9 && MathF.Abs(to.Y) < 14)
            {
                KillRobot(r, false);
                Electrocute();
                return;
            }

            // Fire along one of the eight directions when lined up.
            r.Cool -= Dt;
            if (r.Cool <= 0 && _roomTime > 1.2f && CountBullets(true) < 1 + Level / 2)
            {
                float ax = MathF.Abs(to.X), ay = MathF.Abs(to.Y);
                bool lined = ax < 10 || ay < 10 || MathF.Abs(ax - ay) < 14;
                if (lined && dist < 420)
                {
                    var dir = new Vector2(ax < 10 ? 0 : MathF.Sign(to.X), ay < 10 ? 0 : MathF.Sign(to.Y));
                    dir.Normalize();
                    _bullets.Add(new Bullet { Pos = r.Pos + dir * 12, Vel = dir * MathF.Min(110 + 12 * Level, 260), Enemy = true, Owner = r });
                    r.Cool = Rand(1.6f, 3.2f) / fireRate;
                    Sound.Play(Sfx.Zap, 0.5f, 0.35f);
                }
                else
                {
                    r.Cool = 0.2f;
                }
            }
        }
        // Robots that bump into each other both blow up.
        for (int i = 0; i < _robots.Count; i++)
            for (int j = i + 1; j < _robots.Count; j++)
                if (!_robots[i].Dead && !_robots[j].Dead && Vector2.DistanceSquared(_robots[i].Pos, _robots[j].Pos) < 12 * 12)
                {
                    KillRobot(_robots[i], true);
                    KillRobot(_robots[j], true);
                }
    }

    private void KillRobot(Robot r, bool accident)
    {
        if (r.Dead)
            return;
        r.Dead = true;
        var col = RobotColours[(Level - 1) % RobotColours.Length];
        AddScore(50, r.Pos.X, r.Pos.Y - 14, col);
        Fx.Burst(r.Pos.X, r.Pos.Y, col, 22, 130, 0.6f, 2.5f);
        Fx.Burst(r.Pos.X, r.Pos.Y, Pal.White, 8, 60, 0.3f, 2f);
        Sound.Play(accident ? Sfx.Zap : Sfx.Explode, Rand(-0.2f, 0.3f), 0.8f);
        if (accident)
            _wallFlash = 0.25f;
        int alive = 0;
        foreach (var o in _robots)
            if (!o.Dead)
                alive++;
        if (alive == 0)
        {
            int bonus = 10 * _roomRobots * Math.Min(Level, 10);
            AddScore(bonus, 320, 120, Pal.Gold);
            _clearBanner = 2;
            Sound.Play(Sfx.Bonus);
        }
    }

    private void MoveBullets()
    {
        for (int i = _bullets.Count - 1; i >= 0; i--)
        {
            var b = _bullets[i];
            b.Pos += b.Vel * Dt;
            bool remove = false;
            if (PointInWall(_walls, b.Pos) || b.Pos.X < 0 || b.Pos.X > 640 || b.Pos.Y < Screen.HudHeight || b.Pos.Y > 360)
            {
                remove = true;
                Fx.Burst(b.Pos.X, b.Pos.Y, b.Enemy ? Pal.Orange : Pal.Lime, 6, 50, 0.25f, 1.5f);
            }
            if (!remove)
                foreach (var r in _robots)
                    if (!r.Dead && r != b.Owner && MathF.Abs(b.Pos.X - r.Pos.X) < 9 && MathF.Abs(b.Pos.Y - r.Pos.Y) < 12)
                    {
                        KillRobot(r, b.Enemy);
                        remove = true;
                        break;
                    }
            if (!remove && b.Enemy && MathF.Abs(b.Pos.X - _pos.X) < PW + 2 && MathF.Abs(b.Pos.Y - _pos.Y) < PH + 2)
            {
                _bullets.RemoveAt(i);
                Electrocute();
                return;
            }
            if (remove)
                _bullets.RemoveAt(i);
            else
                _bullets[i] = b;
        }
        // Shots that meet cancel out.
        for (int i = _bullets.Count - 1; i >= 0; i--)
            for (int j = i - 1; j >= 0; j--)
                if (_bullets[j].Enemy != _bullets[i].Enemy && Vector2.DistanceSquared(_bullets[j].Pos, _bullets[i].Pos) < 49)
                {
                    Fx.Burst(_bullets[i].Pos.X, _bullets[i].Pos.Y, Pal.White, 10, 70, 0.3f, 1.8f);
                    Sound.Play(Sfx.Pop, 0.4f, 0.5f);
                    _bullets.RemoveAt(i);
                    _bullets.RemoveAt(j);
                    i--;
                    break;
                }
    }

    private void MoveGuardian()
    {
        if (!_guardian)
        {
            if (_roomTime > _guardianTime)
            {
                _guardian = true;
                _guardianPos = EntryPoint(_entry);
                _alert = 2.5f;
                Sound.Play(Sfx.Alarm, -0.2f, 0.8f);
            }
            return;
        }
        float speed = MathF.Min(40 + 4 * Level, 92);
        var to = _pos - _guardianPos;
        if (to.LengthSquared() > 1)
            _guardianPos += Vector2.Normalize(to) * speed * Dt;
        float bounce = MathF.Abs(MathF.Sin(Time * 6)) * 14;
        if (Vector2.Distance(_pos, _guardianPos - new Vector2(0, bounce)) < 13)
        {
            Electrocute();
            Fx.Float("GOTCHA!", _pos.X, _pos.Y - 30, Pal.Yellow);
        }
        if ((int)(Time * 3) % 2 == 0 && Tick % 20 == 0)
            Sound.Play(Sfx.Bounce, 0.6f, 0.3f);
    }

    // ------------------------------------------------------------------ drawing

    public override void Draw(Gfx g)
    {
        g.GradientV(0, 0, 640, 360, new Color(4, 6, 20), new Color(10, 6, 30));
        Backdrops.Grid(g, new RectF(X0, Y0, X1 - X0, Y1 - Y0), 20, new Color(20, 30, 70) * 0.5f);
        g.Glow(320, 190, 330, Pal.Hsv(220 + Level * 37, 0.8f, 0.6f), 0.12f);
        Backdrops.Vignette(g, Screen.Bounds, 0.5f);

        if (_transition > 0)
        {
            float k = MathF2.EaseInOut(1 - _transition);
            var dir = _transitionDir switch { 0 => new Vector2(-1, 0), 1 => new Vector2(1, 0), 2 => new Vector2(0, -1), _ => new Vector2(0, 1) };
            var size = new Vector2(640, 340);
            var o = g.Offset;
            g.Offset = o - dir * size * k;
            DrawWalls(g, _oldWalls, 0);
            g.Offset = o + dir * size * (1 - k);
            DrawWalls(g, _walls, 0);
            var ep = EntryPoint(_entry);
            g.PixelsCentered(PlayerArt[0], ep.X, ep.Y, 2f);
            g.Offset = o;
            return;
        }

        DrawWalls(g, _walls, _wallFlash);

        // Robots.
        var col = RobotColours[(Level - 1) % RobotColours.Length];
        foreach (var r in _robots)
        {
            if (r.Dead)
                continue;
            g.Glow(r.Pos, 18, col, 0.3f);
            int frame = r.Walk > 0 ? (int)(r.Phase * 6) % 2 : 0;
            g.PixelsCentered(RobotArt[frame], r.Pos.X, r.Pos.Y, 2f, false, col);
            // Scanning eye.
            float scan = MathF.Sin(r.Phase * 4) * 3f;
            var eye = Pal.Cycle(r.Phase, 2);
            g.Rect(r.Pos.X - 2f + scan, r.Pos.Y - 7f, 4f, 2f, eye);
            g.Glow(r.Pos.X + scan, r.Pos.Y - 6f, 6, eye, 0.7f);
        }

        // Bullets.
        foreach (var b in _bullets)
        {
            var c = b.Enemy ? Pal.Orange : Pal.Lime;
            var dir = Vector2.Normalize(b.Vel);
            g.Glow(b.Pos, 10, c, 0.6f);
            g.Line(b.Pos - dir * 6, b.Pos + dir * 2, 2f, Pal.Lighten(c, 0.5f));
        }

        // Player.
        if (_dying > 0)
        {
            if ((int)(_dying * 20) % 2 == 0)
            {
                var flash = Pal.Cycle(_dying * 3, 3);
                g.Glow(_pos, 26, flash, 0.8f);
                g.PixelsCentered(PlayerArt[(int)(_dying * 10) % 2], _pos.X, _pos.Y, 2f, false, flash);
            }
        }
        else
        {
            g.Glow(_pos, 16, Pal.Lime, 0.25f);
            g.PixelsCentered(PlayerArt[(int)(_walkAnim * 8) % 2], _pos.X, _pos.Y, 2.2f, _facing.X < 0);
            // Aim pip.
            g.Circle(_pos.X + _facing.X * 12, _pos.Y + _facing.Y * 14, 1.5f, Pal.Lime * 0.8f);
        }

        // Guardian.
        if (_guardian)
        {
            float bounce = MathF.Abs(MathF.Sin(Time * 6)) * 14;
            DrawGuardian(g, _guardianPos.X, _guardianPos.Y - bounce, 1f, Time);
            g.Ellipse(_guardianPos.X, _guardianPos.Y + 10, 9 - bounce * 0.3f, 2.5f, Color.Black * 0.5f);
        }

        if (_alert > 0 && !IsOver && _dying <= 0)
        {
            float a = MathF.Min(1, _alert) * ((int)(Time * 6) % 2 == 0 ? 1 : 0.6f);
            g.TextShadow(_guardian ? "THE GUARDIAN IS COMING!" : "INTRUDER ALERT!", 320, 42, 2f, (_guardian ? Pal.Yellow : Pal.Red) * a, Align.Center);
        }
        if (_clearBanner > 0)
            g.TextShadow("ROOM CLEARED", 320, 170, 2.5f, Pal.Gold * MathF.Min(1, _clearBanner), Align.Center);
    }

    private void DrawWalls(Gfx g, List<Wall> walls, float flash)
    {
        float t = Time;
        foreach (var w in walls)
        {
            var c = w.Barrier ? Pal.Orange : Pal.Lerp(new Color(40, 110, 255), Pal.White, flash * 2);
            g.Rect(w.R.Inflate(7, 7), Pal.Add(c, 0.05f));
            g.Rect(w.R.Inflate(3, 3), Pal.Add(c, 0.14f));
            g.Rect(w.R.Inflate(1.2f, 1.2f), Pal.Add(c, 0.3f));
            g.Rect(w.R, Pal.Lighten(c, 0.35f));
            // Crackling electricity along the wall.
            bool horiz = w.R.W > w.R.H;
            float len = horiz ? w.R.W : w.R.H;
            int seed = (int)(w.R.X * 7 + w.R.Y * 13);
            int sparks = (int)(len / 60) + 1;
            for (int i = 0; i < sparks; i++)
            {
                float phase = (t * 1.7f + i * 0.37f + seed * 0.011f) % 1f;
                if (phase > 0.25f)
                    continue;
                float along = ((seed * 31 + i * 97 + (int)(t * 1.7f + i * 0.37f) * 53) % 1000) / 1000f * MathF.Max(0, len - 22);
                var p = horiz ? new Vector2(w.R.X + along, w.R.CenterY) : new Vector2(w.R.CenterX, w.R.Y + along);
                var d = horiz ? new Vector2(1, 0) : new Vector2(0, 1);
                var n = new Vector2(-d.Y, d.X);
                var a = p;
                for (int k = 1; k <= 4; k++)
                {
                    var b = p + d * k * 5 + n * (((k * 7 + i + (int)(t * 30)) % 3) - 1) * 3;
                    g.Line(a, b, 1.2f, Pal.Add(Pal.Ice, 0.9f));
                    a = b;
                }
                g.Glow(p + d * 10, 10, Pal.Sky, 0.5f);
            }
        }
    }

    private static void DrawGuardian(Gfx g, float x, float y, float s, float time)
    {
        g.Glow(x, y, 26 * s, Pal.Yellow, 0.6f);
        g.Circle(x, y, 11 * s, Pal.Yellow);
        g.Circle(x - 2 * s, y - 3 * s, 5 * s, Pal.Lighten(Pal.Yellow, 0.4f));
        g.Circle(x - 4 * s, y - 3 * s, 1.8f * s, Pal.Black);
        g.Circle(x + 4 * s, y - 3 * s, 1.8f * s, Pal.Black);
        g.Arc(x, y + 1 * s, 6 * s, 1.8f * s, 0.3f, MathF.PI - 0.3f, Pal.Black);
        _ = time;
    }

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        g.GradientV(r.X, r.Y, r.W, r.H, new Color(4, 6, 20), new Color(14, 8, 36));
        float s = r.H / 70f;
        var blue = new Color(60, 130, 255);
        void WallH(float x, float y, float w)
        {
            g.Rect(x - 2 * s, y - 3 * s, w + 4 * s, 6 * s, Pal.Add(blue, 0.2f));
            g.Rect(x, y - 1.2f * s, w, 2.4f * s, Pal.Lighten(blue, 0.4f));
        }
        void WallV(float x, float y, float h)
        {
            g.Rect(x - 3 * s, y - 2 * s, 6 * s, h + 4 * s, Pal.Add(blue, 0.2f));
            g.Rect(x - 1.2f * s, y, 2.4f * s, h, Pal.Lighten(blue, 0.4f));
        }
        WallH(r.X + 4 * s, r.Y + 5 * s, r.W - 8 * s);
        WallH(r.X + 4 * s, r.Bottom - 5 * s, r.W * 0.4f);
        WallH(r.Right - r.W * 0.4f - 4 * s, r.Bottom - 5 * s, r.W * 0.4f);
        WallV(r.X + 4 * s, r.Y + 5 * s, 18 * s);
        WallV(r.X + 4 * s, r.Bottom - 23 * s, 18 * s);
        WallV(r.CenterX + 12 * s, r.Y + 5 * s, 34 * s);
        WallV(r.Right - 4 * s, r.Y + 5 * s, r.H - 10 * s);

        float px = r.X + 30 * s, py = r.CenterY + 6 * s;
        g.Glow(px, py, 14 * s, Pal.Lime, 0.4f);
        g.PixelsCentered(PlayerArt[(int)(time * 6) % 2], px, py, 1.6f * s);
        float bx = px + 10 * s + ((time * 70 * s) % (60 * s));
        g.Glow(bx, py - 2 * s, 8 * s, Pal.Lime, 0.8f);
        g.Line(bx - 6 * s, py - 2 * s, bx + 2 * s, py - 2 * s, 2 * s, Pal.Lighten(Pal.Lime, 0.5f));

        for (int i = 0; i < 2; i++)
        {
            float rx = r.CenterX + (i == 0 ? -4 : 34) * s, ry = r.CenterY + (i == 0 ? 6 : -10) * s;
            var col = i == 0 ? Pal.Yellow : Pal.Red;
            g.Glow(rx, ry, 14 * s, col, 0.4f);
            g.PixelsCentered(RobotArt[(int)(time * 4 + i) % 2], rx, ry, 1.4f * s, false, col);
            float scan = MathF.Sin(time * 4 + i) * 2 * s;
            g.Rect(rx - 1.4f * s + scan, ry - 4.9f * s, 2.8f * s, 1.4f * s, Pal.Cycle(time + i, 2));
        }
        float gb = MathF.Abs(MathF.Sin(time * 5)) * 10 * s;
        DrawGuardian(g, r.Right - 22 * s, r.Bottom - 18 * s - gb, s * 0.9f, time);
    }

    // ------------------------------------------------------------------ autopilot

    public override void AutoPlay(Controls c)
    {
        if (_transition > 0 || _dying > 0)
            return;

        // Dodge enemy bullets heading our way.
        foreach (var b in _bullets)
        {
            if (!b.Enemy)
                continue;
            var rel = _pos - b.Pos;
            var dir = Vector2.Normalize(b.Vel);
            float along = Vector2.Dot(rel, dir);
            float side = rel.X * dir.Y - rel.Y * dir.X;
            if (along > 0 && along < 150 && MathF.Abs(side) < 13)
            {
                var perp = new Vector2(-dir.Y, dir.X) * (side >= 0 ? -1 : 1);
                for (int k = 0; k < 2; k++, perp = -perp)
                    if (!HitsWall(_walls, _pos + perp * 10, PW + 2, PH + 2))
                    {
                        c.SetDirections(perp.X, perp.Y);
                        return;
                    }
            }
        }

        // Shoot a robot that is lined up in clear sight.
        Robot target = null;
        float best = float.MaxValue;
        Vector2 aim = default;
        foreach (var r in _robots)
        {
            if (r.Dead)
                continue;
            var to = r.Pos - _pos;
            float ax = MathF.Abs(to.X), ay = MathF.Abs(to.Y);
            bool lined = ax < 6 || ay < 6 || MathF.Abs(ax - ay) < 8;
            float d = to.Length();
            if (lined && d < 300 && d < best && LineClear(_pos, r.Pos))
            {
                best = d;
                target = r;
                aim = new Vector2(ax < 6 ? 0 : MathF.Sign(to.X), ay < 6 ? 0 : MathF.Sign(to.Y));
            }
        }
        if (target != null && _shootCool <= 0 && CountBullets(false) < 2 && (!_guardian || best < 60))
        {
            float dot = Vector2.Dot(Vector2.Normalize(aim), _facing);
            if (dot > 0.99f)
            {
                c.Fire = true;
                c.FirePressed = true;
                return;
            }
            if (!HitsWall(_walls, _pos + Vector2.Normalize(aim) * 3, PW + 2, PH + 2))
            {
                c.SetDirections(aim.X, aim.Y);
                return;
            }
        }

        // Head for the exit, following the distance field.
        if (_exitTarget < 0)
            return;
        int here = FieldAt(_pos);
        Vector2 bestDir = Vector2.Zero;
        int bestVal = here;
        for (int d = 0; d < 8; d++)
        {
            var dir = MathF2.FromAngle(d * MathF.PI / 4);
            var p = _pos + dir * GridStep;
            int v = FieldAt(p);
            if (v < bestVal && !HitsWall(_walls, _pos + dir * 4, PW + 1, PH + 1))
            {
                bestVal = v;
                bestDir = dir;
            }
        }
        if (here == 0 || (here == int.MaxValue && bestDir == Vector2.Zero))
            bestDir = Vector2.Normalize(ExitPoint(_exitTarget) - _pos);
        // Linger a little early on so there is something to shoot.
        if (_roomTime < 2.5f && target == null && _robots.Count > 0)
            bestDir *= 0.5f;
        c.SetDirections(bestDir.X, bestDir.Y);
    }
}
