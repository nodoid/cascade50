using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 41 Space Mission: a rescue shuttle in an asteroid field. Drift up to stranded astronauts, pick
/// them up gently, ferry up to four at a time back to the mothership before the oxygen runs out,
/// while mines and drones close in.
/// </summary>
public sealed class SpaceMission : MiniGame
{
    public override int Number => 41;
    public override string Title => "Space Mission";
    public override Category Category => Category.Arcade;
    public override string Tagline => "Rescue the stranded astronauts before their oxygen runs out.";
    public override Color Accent => Pal.Cyan;

    public override string[] HowToPlay =>
    [
        "Fly to the drifting astronauts and touch them gently to pick them up. Too fast and they bounce off.",
        "Carry up to 4 back to the mothership's bay. Rescue them all before the oxygen runs out.",
        "Shoot mines, drones and rocks.",
    ];

    public override string[] DesktopControls => ["ARROWS fire the thrusters.", "SPACE fires the laser."];
    public override string[] TouchControls => ["The stick fires the thrusters.", "FIRE shoots the laser."];
    public override Pad Pad => Pad.Stick | Pad.Fire;

    // ------------------------------------------------------------------ world

    private const float WorldW = 1800, WorldH = 1100;
    private static readonly RectF Hull = new(40, 450, 240, 180);
    private static readonly RectF Bay = new(190, 510, 90, 60);
    private const int Capacity = 4;
    private const float ShipR = 9;

    private struct Rock
    {
        public Vector2 Pos, Vel;
        public float R, Angle, Spin;
        public int Shape;
    }

    private struct Astro
    {
        public Vector2 Pos, Vel;
        public float Angle, Spin, Bounce;
    }

    private struct Mine
    {
        public Vector2 Pos, Vel;
        public float Phase;
    }

    private struct Drone
    {
        public Vector2 Pos, Vel;
        public float Cool, Phase;
        public int Hp;
    }

    private struct Shot
    {
        public Vector2 Pos, Vel;
        public float Life;
    }

    private static readonly Vector2[][] RockShapes;
    private static readonly Vector2[] StarPos;
    private static readonly float[] StarDepth;

    static SpaceMission()
    {
        var rng = new Random(41);
        RockShapes = new Vector2[6][];
        for (int s = 0; s < RockShapes.Length; s++)
        {
            int n = 10;
            RockShapes[s] = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                float a = i * MathF2.Tau / n;
                float rr = 0.78f + 0.22f * (float)rng.NextDouble();
                RockShapes[s][i] = MathF2.FromAngle(a, rr);
            }
        }
        StarPos = new Vector2[220];
        StarDepth = new float[220];
        for (int i = 0; i < StarPos.Length; i++)
        {
            StarPos[i] = new Vector2((float)rng.NextDouble() * 1000, (float)rng.NextDouble() * 700);
            StarDepth[i] = 0.1f + 0.5f * (float)rng.NextDouble();
        }
    }

    private static readonly Dictionary<char, Color> Colours = new()
    {
        ['w'] = Pal.White, ['g'] = Pal.LightGrey, ['o'] = Pal.Orange, ['y'] = Pal.Gold, ['b'] = new Color(60, 80, 140),
    };

    private static readonly PixelArt[] AstroArt =
    [
        new([
            "..www...",
            ".wooow..",
            ".woyow..",
            "w.www.w.",
            ".wwwww..",
            "..www...",
            ".ww.ww..",
            ".w...w..",
        ], Colours),
        new([
            "..www...",
            ".wooow..",
            ".woyow..",
            "..www...",
            "wwwwwww.",
            "..www...",
            ".ww.ww..",
            "ww...ww.",
        ], Colours),
    ];

    private static readonly Vector2[] ShipShape = [new(13, 0), new(-6, 8), new(-9, 6), new(-9, -6), new(-6, -8)];
    private static readonly Vector2[] ShipCockpit = [new(8, 0), new(1, 3.5f), new(1, -3.5f)];

    // ------------------------------------------------------------------ state

    private readonly List<Rock> _rocks = new();
    private readonly List<Astro> _astros = new();
    private readonly List<Mine> _mines = new();
    private readonly List<Drone> _drones = new();
    private readonly List<Shot> _shots = new();
    private readonly List<Shot> _enemyShots = new();
    private Vector2 _pos, _vel;
    private float _angle;
    private int _shield;
    private int _aboard;
    private int _mission;
    private float _oxygen, _oxygenMax;
    private float _invuln, _respawn, _fireCool, _unload;
    private float _droneTimer;
    private float _banner;
    private string _bannerText;
    private Color _bannerColour;
    private bool _thrusting;
    private Vector2 _cam;
    private float _missionEnd;
    private float _lowO2Beep;

    protected override void Start()
    {
        Lives = 3;
        _mission = 1;
        NewMission();
    }

    private void NewMission()
    {
        Level = _mission;
        _rocks.Clear();
        _astros.Clear();
        _mines.Clear();
        _drones.Clear();
        _shots.Clear();
        _enemyShots.Clear();
        _aboard = 0;
        _missionEnd = 0;
        int astronauts = Math.Min(10, 4 + _mission);
        for (int i = 0; i < astronauts; i++)
        {
            var p = RandomSpot(400);
            _astros.Add(new Astro { Pos = p, Vel = MathF2.FromAngle(Rand(0, MathF2.Tau), Rand(4, 14)), Angle = Rand(0, 6), Spin = Rand(-1, 1) });
        }
        int rocks = 14 + _mission * 3;
        for (int i = 0; i < rocks; i++)
        {
            var p = RandomSpot(380);
            float r = Pick(12f, 18f, 26f, 34f);
            _rocks.Add(new Rock
            {
                Pos = p, Vel = MathF2.FromAngle(Rand(0, MathF2.Tau), Rand(8, 30 + _mission * 4)), R = r, Angle = Rand(0, 6),
                Spin = Rand(-1, 1), Shape = RandInt(0, RockShapes.Length),
            });
        }
        int mines = 2 + _mission * 2;
        for (int i = 0; i < mines; i++)
            _mines.Add(new Mine { Pos = RandomSpot(500), Phase = Rand(0, 6) });
        _droneTimer = MathF.Max(6, 20 - _mission * 2);
        _oxygenMax = 40 + astronauts * 9;
        _oxygen = _oxygenMax;
        Spawn();
        Banner($"MISSION {_mission}: RESCUE {astronauts}", Pal.Cyan, 2.5f);
        UpdateStatus();
    }

    private Vector2 RandomSpot(float clearOfDock)
    {
        for (int tries = 0; ; tries++)
        {
            var p = new Vector2(Rand(60, WorldW - 60), Rand(60, WorldH - 60));
            if (Vector2.Distance(p, Bay.Center) > clearOfDock || tries > 30)
                return p;
        }
    }

    private void Spawn()
    {
        _pos = new Vector2(Bay.Right + 40, Bay.CenterY);
        _vel = Vector2.Zero;
        _angle = 0;
        _shield = 3;
        _invuln = 2;
        _respawn = 0;
        _cam = _pos;
    }

    private void UpdateStatus() => Status = $"MISSION {_mission}  ABOARD {_aboard}/{Capacity}  LEFT {_astros.Count + _aboard}";

    private void Banner(string text, Color c, float t = 1.6f)
    {
        _bannerText = text;
        _bannerColour = c;
        _banner = t;
    }

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        if (_banner > 0)
            _banner -= Dt;
        if (_invuln > 0)
            _invuln -= Dt;

        UpdateRocks();
        UpdateAstros();

        if (_missionEnd > 0)
        {
            _missionEnd -= Dt;
            _vel *= 1 - 2 * Dt;
            _pos += _vel * Dt;
            if (_missionEnd <= 0)
            {
                _mission++;
                NewMission();
            }
            UpdateCamera();
            return;
        }

        if (_respawn > 0)
        {
            _respawn -= Dt;
            if (_respawn <= 0)
                Spawn();
            UpdateCamera();
            return;
        }

        // Oxygen.
        _oxygen -= Dt;
        if (_oxygen < 15)
        {
            _lowO2Beep -= Dt;
            if (_lowO2Beep <= 0)
            {
                Sound.Play(Sfx.Alarm, 0.2f, 0.4f);
                _lowO2Beep = 1;
            }
        }
        if (_oxygen <= 0)
        {
            Banner("OUT OF OXYGEN!", Pal.Red);
            _oxygen = 40;
            Crash();
            if (IsOver)
                return;
        }

        UpdateShip();
        if (_respawn > 0)
        {
            UpdateCamera();
            return;
        }
        UpdateShots();
        UpdateMines();
        UpdateDrones();
        UpdateCamera();

        if (_astros.Count == 0 && _aboard == 0 && _missionEnd <= 0)
        {
            int bonus = (int)_oxygen * 10 + 500 * _mission;
            AddScore(bonus, _pos.X - _cam.X + 320, 120, Pal.Gold);
            Banner("MISSION COMPLETE!", Pal.Gold, 3);
            Sound.Play(Sfx.LevelUp);
            _missionEnd = 3;
        }
    }

    private void UpdateShip()
    {
        var input = new Vector2(In.AxisX, In.AxisY);
        if (input.LengthSquared() > 1)
            input.Normalize();
        _thrusting = input.LengthSquared() > 0.05f;
        if (_thrusting)
        {
            _vel += input * 300 * Dt;
            float target = MathF.Atan2(input.Y, input.X);
            float diff = MathF2.WrapAngle(target - _angle);
            _angle += MathF.Sign(diff) * MathF.Min(MathF.Abs(diff), 9 * Dt);
            if (Tick % 2 == 0)
            {
                var back = _pos - MathF2.FromAngle(_angle, 10);
                Fx.Spark(back.X - _cam.X + 320, back.Y - _cam.Y + 191, -input.X * 90 + Rand(-20, 20), -input.Y * 90 + Rand(-20, 20),
                    Pal.Lerp(Pal.Cyan, Pal.White, Rand(0, 1)), 0.3f, 1.8f);
            }
        }
        Sound.Loop(LoopSfx.Thrust, _thrusting, 0.2f, 0.45f);
        _vel *= 1 - 0.55f * Dt;
        if (_vel.Length() > 270)
            _vel = Vector2.Normalize(_vel) * 270;
        _pos += _vel * Dt;

        // Edges of the sector act as a soft force field.
        if (_pos.X < 20 || _pos.X > WorldW - 20)
        {
            _vel.X = -_vel.X * 0.5f;
            _pos.X = MathF2.Clamp(_pos.X, 20, WorldW - 20);
            Sound.Play(Sfx.Zap, -0.4f, 0.3f);
        }
        if (_pos.Y < 20 || _pos.Y > WorldH - 20)
        {
            _vel.Y = -_vel.Y * 0.5f;
            _pos.Y = MathF2.Clamp(_pos.Y, 20, WorldH - 20);
            Sound.Play(Sfx.Zap, -0.4f, 0.3f);
        }

        // The mothership's hull is solid apart from the bay.
        bool inBay = Bay.Contains(_pos);
        if (!inBay && Hull.Inflate(ShipR, ShipR).Contains(_pos))
        {
            var h = Hull.Inflate(ShipR, ShipR);
            float l = _pos.X - h.X, r = h.Right - _pos.X, t = _pos.Y - h.Y, b = h.Bottom - _pos.Y;
            float m = MathF.Min(MathF.Min(l, r), MathF.Min(t, b));
            if (m == l) { _pos.X = h.X; _vel.X = -MathF.Abs(_vel.X) * 0.4f; }
            else if (m == r && !(_pos.Y > Bay.Y && _pos.Y < Bay.Bottom)) { _pos.X = h.Right; _vel.X = MathF.Abs(_vel.X) * 0.4f; }
            else if (m == t) { _pos.Y = h.Y; _vel.Y = -MathF.Abs(_vel.Y) * 0.4f; }
            else if (m == b) { _pos.Y = h.Bottom; _vel.Y = MathF.Abs(_vel.Y) * 0.4f; }
        }
        if (inBay)
        {
            // Tractor beam pulls the shuttle in and the crew get off one at a time.
            _vel *= 1 - 3 * Dt;
            _vel += (Bay.Center - _pos) * 2 * Dt;
            if (_aboard > 0)
            {
                _unload -= Dt;
                if (_unload <= 0)
                {
                    _aboard--;
                    _unload = 0.35f;
                    AddScore(250 * _mission, Bay.CenterX - _cam.X + 320, Bay.Y - _cam.Y + 180, Pal.Lime);
                    Sound.Play(Sfx.Coin, 0.1f * _aboard);
                    UpdateStatus();
                    if (_aboard == 0)
                        Banner("CREW SAFE!", Pal.Lime);
                }
            }
            if (_shield < 3 && Tick % 40 == 0)
            {
                _shield++;
                Sound.Play(Sfx.PowerUp, 0.4f, 0.5f);
            }
        }

        // Laser.
        if (_fireCool > 0)
            _fireCool -= Dt;
        if ((In.FirePressed || In.Fire) && _fireCool <= 0)
        {
            _fireCool = 0.18f;
            var dir = MathF2.FromAngle(_angle, 1);
            _shots.Add(new Shot { Pos = _pos + dir * 13, Vel = dir * 560 + _vel * 0.5f, Life = 0.7f });
            Sound.Play(Sfx.Laser, 0.2f, 0.5f);
        }

        // Rocks.
        for (int i = 0; i < _rocks.Count; i++)
        {
            var rk = _rocks[i];
            if (!MathF2.Circles(_pos, ShipR, rk.Pos, rk.R * 0.85f))
                continue;
            var n = Vector2.Normalize(_pos - rk.Pos + new Vector2(0.01f, 0));
            _pos = rk.Pos + n * (rk.R * 0.85f + ShipR + 1);
            float vn = Vector2.Dot(_vel - rk.Vel, n);
            if (vn < 0)
                _vel -= n * vn * 1.6f;
            Sound.Play(Sfx.Thud, 0, 0.6f);
            if (MathF.Abs(vn) > 40)
                Hit("HULL DAMAGE");
            if (_respawn > 0)
                return;
        }
    }

    private void Hit(string why)
    {
        if (_invuln > 0)
            return;
        _shield--;
        _invuln = 1.2f;
        Fx.Shake(4, 0.3f);
        Sound.Play(Sfx.Hurt);
        Fx.Burst(_pos.X - _cam.X + 320, _pos.Y - _cam.Y + 191, Pal.Orange, 14, 100, 0.5f);
        if (_shield <= 0)
        {
            Banner(why, Pal.Red);
            Crash();
        }
    }

    private void Crash()
    {
        var sp = ToScreen(_pos);
        Fx.Explode(sp.X, sp.Y, 1.6f);
        Sound.Play(Sfx.BigExplode);
        Sound.StopLoops();
        // Anyone on board is thrown clear, to be picked up again.
        for (int i = 0; i < _aboard; i++)
            _astros.Add(new Astro { Pos = _pos, Vel = MathF2.FromAngle(i * 1.7f, 40), Angle = i, Spin = 1.5f });
        _aboard = 0;
        UpdateStatus();
        _respawn = 2;
        if (LoseLife())
            EndGame(false, "The rescue mission failed");
    }

    private void UpdateRocks()
    {
        for (int i = 0; i < _rocks.Count; i++)
        {
            var r = _rocks[i];
            r.Pos += r.Vel * Dt;
            r.Angle += r.Spin * Dt;
            r.Pos.X = Backdrops.Mod(r.Pos.X + 60, WorldW + 120) - 60;
            r.Pos.Y = Backdrops.Mod(r.Pos.Y + 60, WorldH + 120) - 60;
            _rocks[i] = r;
        }
    }

    private void UpdateAstros()
    {
        for (int i = _astros.Count - 1; i >= 0; i--)
        {
            var a = _astros[i];
            a.Pos += a.Vel * Dt;
            a.Vel *= 1 - 0.2f * Dt;
            if (a.Vel.Length() < 6)
                a.Vel += MathF2.FromAngle(a.Angle, 2) * Dt;
            a.Angle += a.Spin * Dt;
            if (a.Bounce > 0)
                a.Bounce -= Dt;
            if (a.Pos.X < 40 || a.Pos.X > WorldW - 40)
                a.Vel.X = -a.Vel.X;
            if (a.Pos.Y < 40 || a.Pos.Y > WorldH - 40)
                a.Vel.Y = -a.Vel.Y;
            a.Pos = new Vector2(MathF2.Clamp(a.Pos.X, 40, WorldW - 40), MathF2.Clamp(a.Pos.Y, 40, WorldH - 40));

            if (_respawn <= 0 && _missionEnd <= 0 && a.Bounce <= 0 && MathF2.Circles(_pos, ShipR, a.Pos, 9))
            {
                float rel = (_vel - a.Vel).Length();
                if (_aboard >= Capacity)
                {
                    a.Bounce = 1;
                    a.Vel = Vector2.Normalize(a.Pos - _pos + new Vector2(0.01f, 0)) * 40;
                    Fx.Float("SHUTTLE FULL!", ToScreen(a.Pos).X, ToScreen(a.Pos).Y - 14, Pal.Orange);
                    Sound.Play(Sfx.Wrong, 0, 0.5f);
                }
                else if (rel > 95)
                {
                    a.Bounce = 1;
                    a.Vel = Vector2.Normalize(a.Pos - _pos + new Vector2(0.01f, 0)) * MathF.Min(140, rel * 0.6f);
                    a.Spin = Rand(-4, 4);
                    Fx.Float("TOO FAST!", ToScreen(a.Pos).X, ToScreen(a.Pos).Y - 14, Pal.Orange);
                    Sound.Play(Sfx.Bounce, -0.3f);
                }
                else
                {
                    _aboard++;
                    var sp = ToScreen(a.Pos);
                    Fx.Burst(sp.X, sp.Y, Pal.Cyan, 16, 60, 0.5f);
                    Fx.Float("ABOARD!", sp.X, sp.Y - 14, Pal.Cyan);
                    Sound.Play(Sfx.Pickup);
                    AddScore(50);
                    _astros.RemoveAt(i);
                    UpdateStatus();
                    if (_aboard == Capacity)
                        Banner("FULL! RETURN TO THE MOTHERSHIP", Pal.Lime, 2);
                    continue;
                }
            }
            _astros[i] = a;
        }
    }

    private void UpdateShots()
    {
        for (int i = _shots.Count - 1; i >= 0; i--)
        {
            var s = _shots[i];
            s.Pos += s.Vel * Dt;
            s.Life -= Dt;
            bool gone = s.Life <= 0;
            for (int k = 0; k < _rocks.Count && !gone; k++)
            {
                var r = _rocks[k];
                if (!MathF2.Circles(s.Pos, 2, r.Pos, r.R * 0.85f))
                    continue;
                gone = true;
                var sp = ToScreen(r.Pos);
                Fx.Burst(sp.X, sp.Y, new Color(170, 150, 130), (int)r.R, r.R * 4, 0.6f, 2, 0, false);
                Sound.Play(Sfx.Explode, 0.5f - r.R / 40, 0.6f);
                _rocks.RemoveAt(k);
                AddScore(r.R > 20 ? 20 : 40, sp.X, sp.Y - 10, Pal.Silver);
                if (r.R > 15)
                    for (int j = 0; j < 2; j++)
                        _rocks.Add(new Rock
                        {
                            Pos = r.Pos, Vel = r.Vel + MathF2.FromAngle(Rand(0, MathF2.Tau), Rand(30, 60)), R = r.R * 0.55f,
                            Angle = Rand(0, 6), Spin = Rand(-2, 2), Shape = RandInt(0, RockShapes.Length),
                        });
            }
            for (int k = 0; k < _mines.Count && !gone; k++)
                if (MathF2.Circles(s.Pos, 2, _mines[k].Pos, 10))
                {
                    gone = true;
                    var sp = ToScreen(_mines[k].Pos);
                    Fx.Explode(sp.X, sp.Y, 1);
                    Sound.Play(Sfx.Explode);
                    AddScore(50, sp.X, sp.Y - 10, Pal.Red);
                    _mines.RemoveAt(k);
                }
            for (int k = 0; k < _drones.Count && !gone; k++)
                if (MathF2.Circles(s.Pos, 2, _drones[k].Pos, 11))
                {
                    gone = true;
                    var d = _drones[k];
                    d.Hp--;
                    var sp = ToScreen(d.Pos);
                    if (d.Hp <= 0)
                    {
                        Fx.Explode(sp.X, sp.Y, 1.2f);
                        Sound.Play(Sfx.BigExplode, 0.3f);
                        AddScore(100, sp.X, sp.Y - 10, Pal.Magenta);
                        _drones.RemoveAt(k);
                    }
                    else
                    {
                        Fx.Burst(sp.X, sp.Y, Pal.Magenta, 8, 60, 0.3f);
                        Sound.Play(Sfx.Hit, 0.3f, 0.6f);
                        _drones[k] = d;
                    }
                }
            if (gone)
                _shots.RemoveAt(i);
            else
                _shots[i] = s;
        }

        for (int i = _enemyShots.Count - 1; i >= 0; i--)
        {
            var s = _enemyShots[i];
            s.Pos += s.Vel * Dt;
            s.Life -= Dt;
            if (MathF2.Circles(s.Pos, 3, _pos, ShipR))
            {
                _enemyShots.RemoveAt(i);
                Hit("SHOT DOWN");
                if (_respawn > 0)
                    return;
                continue;
            }
            if (s.Life <= 0)
                _enemyShots.RemoveAt(i);
            else
                _enemyShots[i] = s;
        }
    }

    private void UpdateMines()
    {
        for (int i = _mines.Count - 1; i >= 0; i--)
        {
            var m = _mines[i];
            m.Phase += Dt;
            float d = Vector2.Distance(m.Pos, _pos);
            if (d < 170)
                m.Vel += Vector2.Normalize(_pos - m.Pos) * (40 + _mission * 6) * Dt;
            m.Vel *= 1 - 0.6f * Dt;
            m.Pos += m.Vel * Dt;
            if (d < ShipR + 9)
            {
                var sp = ToScreen(m.Pos);
                Fx.Explode(sp.X, sp.Y, 1.2f);
                Sound.Play(Sfx.Explode);
                _mines.RemoveAt(i);
                _invuln = 0;
                Hit("MINE!");
                if (_respawn > 0)
                    return;
                continue;
            }
            _mines[i] = m;
        }
    }

    private void UpdateDrones()
    {
        _droneTimer -= Dt;
        if (_droneTimer <= 0 && _drones.Count < 1 + _mission)
        {
            _droneTimer = MathF.Max(5, 16 - _mission * 1.5f) + Rand(0, 4);
            // Arrive from a far corner of the sector.
            var p = new Vector2(_pos.X < WorldW / 2 ? WorldW - 30 : 30, Rand(60, WorldH - 60));
            _drones.Add(new Drone { Pos = p, Cool = 2, Hp = 2, Phase = Rand(0, 6) });
            Sound.Play(Sfx.Warp, -0.3f, 0.4f);
            Banner("DRONE INBOUND!", Pal.Magenta, 1.2f);
        }
        for (int i = 0; i < _drones.Count; i++)
        {
            var d = _drones[i];
            d.Phase += Dt;
            var to = _pos - d.Pos;
            float dist = to.Length();
            var want = dist > 1 ? to / dist * (dist > 140 ? 110 + _mission * 8 : -30) : Vector2.Zero;
            want += new Vector2(MathF.Sin(d.Phase * 1.3f), MathF.Cos(d.Phase)) * 40;
            d.Vel += (want - d.Vel) * 1.4f * Dt;
            d.Pos += d.Vel * Dt;
            d.Cool -= Dt;
            if (d.Cool <= 0 && dist < 320 && _missionEnd <= 0)
            {
                d.Cool = MathF.Max(1.1f, 2.6f - _mission * 0.2f);
                var aim = Vector2.Normalize(to + _vel * (dist / 220) * 0.6f);
                _enemyShots.Add(new Shot { Pos = d.Pos, Vel = aim * 220, Life = 2.2f });
                Sound.Play(Sfx.Zap, 0.2f, 0.35f);
            }
            if (dist < ShipR + 11)
            {
                d.Vel = -d.Vel;
                Hit("RAMMED BY A DRONE");
            }
            _drones[i] = d;
        }
    }

    private void UpdateCamera()
    {
        var target = _pos + _vel * 0.35f;
        _cam += (target - _cam) * MathF.Min(1, Dt * 4);
        _cam.X = MathF2.Clamp(_cam.X, 320, WorldW - 320);
        _cam.Y = MathF2.Clamp(_cam.Y, 191 - 22, WorldH - 169);
    }

    private Vector2 ToScreen(Vector2 w) => new(w.X - _cam.X + 320, w.Y - _cam.Y + 191);

    // ------------------------------------------------------------------ drawing

    public override void Draw(Gfx g)
    {
        DrawSpace(g, Screen.Bounds, _cam, Time);
        DrawMothership(g, ToScreen(Hull.Center) - new Vector2(Hull.W / 2, Hull.H / 2), 1, Time, _respawn <= 0 && Bay.Contains(_pos));

        foreach (var r in _rocks)
        {
            var p = ToScreen(r.Pos);
            if (p.X < -50 || p.X > 690 || p.Y < -50 || p.Y > 410)
                continue;
            DrawRock(g, p, r.R, r.Angle, r.Shape);
        }

        foreach (var m in _mines)
        {
            var p = ToScreen(m.Pos);
            if (p.X < -20 || p.X > 660 || p.Y < 0 || p.Y > 380)
                continue;
            float blink = (int)(m.Phase * (Vector2.Distance(m.Pos, _pos) < 170 ? 8 : 2)) % 2;
            for (int k = 0; k < 8; k++)
            {
                var d = MathF2.FromAngle(k * MathF2.Tau / 8 + m.Phase * 0.5f, 10);
                g.Line(p, p + d, 2, Pal.Grey);
            }
            g.Circle(p.X, p.Y, 7, new Color(80, 80, 95));
            g.Circle(p.X - 2, p.Y - 2, 2.5f, new Color(130, 130, 145));
            g.Circle(p.X, p.Y, 2.2f, blink > 0 ? Pal.Red : Pal.Darken(Pal.Red, 0.6f));
            if (blink > 0)
                g.Glow(p, 16, Pal.Red, 0.6f);
        }

        foreach (var a in _astros)
        {
            var p = ToScreen(a.Pos);
            if (p.X < -20 || p.X > 660 || p.Y < 0 || p.Y > 380)
                continue;
            g.Glow(p, 22, Pal.Cyan, 0.25f + 0.15f * MathF.Sin(Time * 4 + a.Angle));
            g.PixelsCentered(AstroArt[(int)(Time * 2 + a.Angle) % 2], p.X, p.Y, 2f);
        }

        foreach (var d in _drones)
        {
            var p = ToScreen(d.Pos);
            g.Glow(p, 22, Pal.Magenta, 0.4f);
            g.Circle(p.X, p.Y, 10, new Color(70, 20, 80));
            g.Ring(p.X, p.Y, 10, 2, Pal.Magenta);
            for (int k = 0; k < 3; k++)
            {
                var o = MathF2.FromAngle(d.Phase * 3 + k * MathF2.Tau / 3, 13);
                g.Circle(p.X + o.X, p.Y + o.Y, 2.5f, Pal.Pink);
            }
            var eye = Vector2.Normalize(_pos - d.Pos + new Vector2(0.01f, 0)) * 3;
            g.Circle(p.X + eye.X, p.Y + eye.Y, 3.2f, Pal.Red);
            g.Glow(p.X + eye.X, p.Y + eye.Y, 8, Pal.Red, 0.6f);
        }

        foreach (var s in _shots)
        {
            var p = ToScreen(s.Pos);
            var tail = p - Vector2.Normalize(s.Vel) * 10;
            g.GlowLine(tail, p, 1.6f, Pal.Lime);
        }
        foreach (var s in _enemyShots)
        {
            var p = ToScreen(s.Pos);
            g.Glow(p, 9, Pal.Magenta, 0.8f);
            g.Circle(p.X, p.Y, 2.4f, Pal.Pink);
        }

        if (_respawn <= 0 && !IsOver && (_invuln <= 0 || (int)(_invuln * 12) % 2 == 0))
            DrawShip(g, ToScreen(_pos), _angle, 1, _thrusting, Time, _aboard);

        DrawPointers(g);
        DrawHud(g);
    }

    private static void DrawSpace(Gfx g, RectF r, Vector2 cam, float time)
    {
        g.GradientV(r.X, r.Y, r.W, r.H, new Color(4, 4, 18), new Color(14, 6, 30));
        float s = r.W / 640f;
        g.Glow(r.X + Backdrops.Mod(500 - cam.X * 0.05f, r.W + 200) - 100, r.Y + r.H * 0.3f, 200 * s, Pal.Purple, 0.25f);
        g.Glow(r.X + Backdrops.Mod(120 - cam.X * 0.04f, r.W + 300) - 150, r.Y + r.H * 0.75f, 160 * s, Pal.Teal, 0.2f);
        for (int i = 0; i < StarPos.Length; i++)
        {
            float d = StarDepth[i];
            float x = r.X + Backdrops.Mod(StarPos[i].X - cam.X * d, 1000) / 1000 * r.W * 1.0f;
            float y = r.Y + Backdrops.Mod(StarPos[i].Y - cam.Y * d, 700) / 700 * r.H;
            float tw = 0.6f + 0.4f * MathF.Sin(time * 2 + i);
            g.Circle(x, y, (0.4f + d * 1.6f) * s, Pal.White * (tw * (0.3f + d)));
        }
        // A ringed planet far away.
        float px = r.X + Backdrops.Mod(r.W * 0.8f - cam.X * 0.02f, r.W + 160) - 80, py = r.Y + r.H * 0.25f;
        g.Glow(px, py, 60 * s, Pal.Orange, 0.25f);
        g.Circle(px, py, 24 * s, new Color(150, 90, 60));
        g.Ellipse(px, py - 4 * s, 22 * s, 6 * s, new Color(180, 120, 80));
        g.Ellipse(px, py, 44 * s, 6 * s, new Color(200, 170, 120) * 0.5f);
    }

    private static void DrawRock(Gfx g, Vector2 p, float r, float angle, int shape)
    {
        var pts = RockShapes[shape];
        var dark = new Color(66, 58, 54);
        float c = MathF.Cos(angle), s = MathF.Sin(angle);
        Vector2 prev = default, first = default;
        for (int i = 0; i <= pts.Length; i++)
        {
            var a = pts[i % pts.Length];
            var pa = p + new Vector2(a.X * c - a.Y * s, a.X * s + a.Y * c) * r;
            if (i == 0)
                first = pa;
            else
                g.Triangle(p, prev, i == pts.Length ? first : pa, dark);
            prev = pa;
        }
        g.Circle(p.X - r * 0.1f, p.Y - r * 0.12f, r * 0.72f, new Color(112, 98, 86));
        g.Circle(p.X - r * 0.2f, p.Y - r * 0.25f, r * 0.42f, new Color(140, 124, 108));
        var cr = new Vector2(c * 0.3f - s * 0.2f, s * 0.3f + c * 0.2f) * r;
        g.Circle(p.X + cr.X, p.Y + cr.Y, r * 0.16f, new Color(84, 74, 68));
        g.Circle(p.X - cr.Y * 0.8f, p.Y + cr.X * 0.8f, r * 0.11f, new Color(84, 74, 68));
    }

    private static void DrawShip(Gfx g, Vector2 p, float angle, float s, bool thrust, float time, int aboard)
    {
        if (thrust)
        {
            var back = p - MathF2.FromAngle(angle, 10 * s);
            float f = 0.8f + 0.2f * MathF.Sin(time * 40);
            g.Glow(back, 14 * s, Pal.Cyan, 0.8f);
            g.Shape(FlameShape, back, angle + MathF.PI, s * f, Pal.Cyan);
            g.Shape(FlameShape, back, angle + MathF.PI, s * f * 0.55f, Pal.White);
        }
        g.Glow(p, 22 * s, Pal.Sky, 0.25f);
        g.Shape(ShipShape, p, angle, s, new Color(220, 225, 235));
        g.Shape(ShipShape, p + MathF2.FromAngle(angle + MathF.PI / 2, 1.5f * s), angle, s * 0.75f, new Color(160, 170, 190));
        g.Shape(ShipCockpit, p, angle, s, Pal.Cyan);
        var wing = MathF2.FromAngle(angle + MathF.PI / 2, 7 * s);
        var aft = MathF2.FromAngle(angle, -5 * s);
        g.Circle(p.X + wing.X + aft.X, p.Y + wing.Y + aft.Y, 1.6f * s, (int)(time * 3) % 2 == 0 ? Pal.Red : Pal.Darken(Pal.Red, 0.5f));
        g.Circle(p.X - wing.X + aft.X, p.Y - wing.Y + aft.Y, 1.6f * s, (int)(time * 3) % 2 == 0 ? Pal.Green : Pal.Darken(Pal.Green, 0.5f));
        // Little lights for the crew on board.
        for (int i = 0; i < aboard; i++)
        {
            var o = MathF2.FromAngle(angle, (-3 + i * 2.5f) * s);
            g.Circle(p.X + o.X, p.Y + o.Y, 1 * s, Pal.Gold);
        }
    }

    private static readonly Vector2[] FlameShape = [new(0, -4), new(14, 0), new(0, 4)];

    private static void DrawMothership(Gfx g, Vector2 topLeft, float s, float time, bool docking)
    {
        float x = topLeft.X, y = topLeft.Y, w = Hull.W * s, h = Hull.H * s;
        if (x > 700 || x + w < -60 || y > 400 || y + h < -60)
            return;
        var hull = new Color(90, 100, 125);
        g.Glow(x + w * 0.5f, y + h * 0.5f, w * 0.8f, Pal.Sky, 0.12f);
        // Main hull with a tapering nose and engines at the back.
        g.GradientV(x, y + h * 0.15f, w * 0.85f, h * 0.7f, Pal.Lighten(hull, 0.2f), Pal.Darken(hull, 0.4f));
        g.Triangle(new Vector2(x + w * 0.85f, y + h * 0.15f), new Vector2(x + w, y + h * 0.35f), new Vector2(x + w * 0.85f, y + h * 0.35f), Pal.Lighten(hull, 0.15f));
        g.Triangle(new Vector2(x + w * 0.85f, y + h * 0.85f), new Vector2(x + w * 0.85f, y + h * 0.65f), new Vector2(x + w, y + h * 0.65f), Pal.Darken(hull, 0.3f));
        g.Rect(x + w * 0.85f, y + h * 0.15f, w * 0.15f, h * 0.2f, Pal.Lighten(hull, 0.1f));
        g.Rect(x + w * 0.85f, y + h * 0.65f, w * 0.15f, h * 0.2f, Pal.Darken(hull, 0.2f));
        g.Rect(x + w * 0.15f, y, w * 0.4f, h * 0.15f, Pal.Darken(hull, 0.15f));
        g.Rect(x + w * 0.2f, y + h * 0.85f, w * 0.35f, h * 0.15f, Pal.Darken(hull, 0.35f));
        for (int i = 0; i < 6; i++)
            g.Rect(x + w * (0.08f + i * 0.12f), y + h * 0.15f, 1.5f * s, h * 0.7f, Color.Black * 0.2f);
        for (int i = 0; i < 8; i++)
        {
            bool on = ((int)(time * 3) + i) % 4 != 0;
            g.Rect(x + w * (0.1f + i * 0.08f), y + h * 0.25f, 4 * s, 3 * s, on ? Pal.Gold : Pal.DarkGrey);
        }
        // Engines.
        for (int i = 0; i < 3; i++)
        {
            float ey = y + h * (0.3f + i * 0.2f);
            g.Rect(x - 8 * s, ey - 6 * s, 10 * s, 12 * s, Pal.DarkGrey);
            g.Glow(x - 10 * s, ey, 18 * s, Pal.Orange, 0.5f + 0.2f * MathF.Sin(time * 9 + i));
        }
        // The docking bay.
        float bx = x + (Bay.X - Hull.X) * s, by = y + (Bay.Y - Hull.Y) * s, bw = Bay.W * s, bh = Bay.H * s;
        g.Rect(bx, by, bw + 4 * s, bh, new Color(10, 14, 30));
        g.GradientH(bx, by, bw + 4 * s, bh, Pal.Lime * 0.05f, Pal.Lime * (docking ? 0.35f : 0.12f));
        for (int i = 0; i < 4; i++)
        {
            bool on = ((int)(time * 6) - i) % 4 == 0;
            g.Circle(bx + bw - i * 18 * s, by + 3 * s, 1.8f * s, on ? Pal.Lime : Pal.Darken(Pal.Lime, 0.6f));
            g.Circle(bx + bw - i * 18 * s, by + bh - 3 * s, 1.8f * s, on ? Pal.Lime : Pal.Darken(Pal.Lime, 0.6f));
        }
        if (docking)
            g.Glow(bx + bw * 0.5f, by + bh * 0.5f, bw * 0.6f, Pal.Lime, 0.4f);
        if (s >= 1)
            g.Text("DOCK", bx + bw * 0.5f, by + bh * 0.5f - 4, 1, Pal.Lime * 0.8f, Align.Center);
    }

    private void DrawPointers(Gfx g)
    {
        // Edge arrows: astronauts in cyan, the dock in green while carrying crew.
        var area = new RectF(14, 62, 612, 282);
        foreach (var a in _astros)
            EdgeArrow(g, ToScreen(a.Pos), area, Pal.Cyan);
        if (_aboard > 0 || _astros.Count == 0)
            EdgeArrow(g, ToScreen(Bay.Center), area, Pal.Lime);
    }

    private static void EdgeArrow(Gfx g, Vector2 p, RectF area, Color c)
    {
        if (area.Contains(p))
            return;
        var centre = area.Center;
        var d = p - centre;
        float k = MathF.Min(MathF.Abs(area.W / 2 / (d.X == 0 ? 0.001f : d.X)), MathF.Abs(area.H / 2 / (d.Y == 0 ? 0.001f : d.Y)));
        var e = centre + d * k;
        float ang = MathF.Atan2(d.Y, d.X);
        var tip = e;
        var l = e - MathF2.FromAngle(ang - 0.5f, 10);
        var r = e - MathF2.FromAngle(ang + 0.5f, 10);
        g.Glow(e, 10, c, 0.5f);
        g.Triangle(tip, l, r, c);
    }

    private void DrawHud(Gfx g)
    {
        // Oxygen.
        var o = new RectF(8, 28, 150, 22);
        g.Panel(o, Pal.Panel * 0.85f, Pal.Cyan * 0.5f, 5);
        g.Text("O2", o.X + 6, o.Y + 7, 1, Pal.LightGrey);
        float k = MathF2.Clamp(_oxygen / _oxygenMax, 0, 1);
        var oc = _oxygen < 15 ? Pal.Red : k < 0.4f ? Pal.Orange : Pal.Cyan;
        g.RoundRect(o.X + 24, o.Y + 6, 118, 10, 3, Color.Black * 0.6f);
        g.RoundRect(o.X + 24, o.Y + 6, 118 * k, 10, 3, oc);
        if (_oxygen < 15 && (int)(Time * 4) % 2 == 0)
            g.Glow(o.CenterX, o.CenterY, 50, Pal.Red, 0.4f);

        // Shield and crew.
        var sh = new RectF(164, 28, 96, 22);
        g.Panel(sh, Pal.Panel * 0.85f, Pal.Cyan * 0.5f, 5);
        g.Text("SHIELD", sh.X + 6, sh.Y + 7, 1, Pal.LightGrey);
        for (int i = 0; i < 3; i++)
            g.RoundRect(sh.X + 50 + i * 14, sh.Y + 5, 10, 12, 2, i < _shield ? Pal.Lime : Pal.DarkGrey);
        var cr = new RectF(266, 28, 82, 22);
        g.Panel(cr, Pal.Panel * 0.85f, Pal.Cyan * 0.5f, 5);
        for (int i = 0; i < Capacity; i++)
        {
            var col = i < _aboard ? Pal.Gold : Pal.DarkGrey;
            g.Circle(cr.X + 12 + i * 19, cr.Y + 8, 3.5f, col);
            g.Rect(cr.X + 9 + i * 19, cr.Y + 12, 6, 6, col);
        }

        // Radar.
        var rad = new RectF(536, 28, 96, 59);
        g.Panel(rad, new Color(4, 20, 16) * 0.9f, Pal.Lime * 0.5f, 5);
        float sx = (rad.W - 6) / WorldW, sy = (rad.H - 6) / WorldH;
        var o0 = new Vector2(rad.X + 3, rad.Y + 3);
        g.Rect(o0.X + Hull.X * sx, o0.Y + Hull.Y * sy, Hull.W * sx, Hull.H * sy, Pal.Sky * 0.6f);
        foreach (var r in _rocks)
            g.Rect(o0.X + r.Pos.X * sx - 0.5f, o0.Y + r.Pos.Y * sy - 0.5f, 1.2f, 1.2f, Pal.Grey * 0.6f);
        foreach (var m in _mines)
            g.Rect(o0.X + m.Pos.X * sx - 0.75f, o0.Y + m.Pos.Y * sy - 0.75f, 1.5f, 1.5f, Pal.Red);
        foreach (var d in _drones)
            g.Rect(o0.X + d.Pos.X * sx - 1, o0.Y + d.Pos.Y * sy - 1, 2, 2, Pal.Magenta);
        foreach (var a in _astros)
            g.Circle(o0.X + a.Pos.X * sx, o0.Y + a.Pos.Y * sy, 1.4f, Pal.Cyan);
        g.RectOutline(o0.X + (_cam.X - 320) * sx, o0.Y + (_cam.Y - 169) * sy, 640 * sx, 338 * sy, 0.6f, Pal.White * 0.4f);
        if ((int)(Time * 4) % 2 == 0)
            g.Circle(o0.X + _pos.X * sx, o0.Y + _pos.Y * sy, 1.8f, Pal.White);

        if (_banner > 0 && _bannerText != null)
            g.TextShadow(_bannerText, 320, 100, 2, _bannerColour * MathF.Min(1, _banner * 2), Align.Center);
    }

    // ------------------------------------------------------------------ icon

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        float s = r.H / 70f;
        DrawSpace(g, r, new Vector2(time * 30, 0), time);
        DrawRock(g, new Vector2(r.X + r.W * 0.75f, r.Y + r.H * 0.3f), 11 * s, time * 0.4f, 1);
        DrawRock(g, new Vector2(r.X + r.W * 0.88f, r.Y + r.H * 0.78f), 7 * s, -time * 0.6f, 3);
        DrawRock(g, new Vector2(r.X + r.W * 0.12f, r.Y + r.H * 0.2f), 6 * s, time * 0.7f, 4);
        // An astronaut waving, and the shuttle easing in.
        var ap = new Vector2(r.X + r.W * 0.62f, r.Y + r.H * 0.58f + MathF.Sin(time * 1.5f) * 2 * s);
        g.Glow(ap, 14 * s, Pal.Cyan, 0.4f);
        g.PixelsCentered(AstroArt[(int)(time * 2) % 2], ap.X, ap.Y, 1.3f * s);
        float k = (time * 0.25f) % 1;
        var sp = new Vector2(r.X + r.W * (0.15f + 0.3f * MathF2.EaseOut(k)), r.Y + r.H * 0.6f + MathF.Sin(time) * 3 * s);
        DrawShip(g, sp, 0.05f * MathF.Sin(time), 0.85f * s, k < 0.7f, time, 2);
        // A laser bolt at a mine.
        var mp = new Vector2(r.X + r.W * 0.4f, r.Y + r.H * 0.25f);
        g.Glow(mp, 8 * s, Pal.Red, 0.4f + 0.4f * MathF.Sin(time * 8));
        g.Circle(mp.X, mp.Y, 4 * s, new Color(80, 80, 95));
        g.Circle(mp.X, mp.Y, 1.4f * s, Pal.Red);
    }

    // ------------------------------------------------------------------ autopilot

    public override void AutoPlay(Controls c)
    {
        if (_respawn > 0 || _missionEnd > 0)
            return;
        Vector2 target;
        bool goHome = _aboard >= Capacity || (_aboard > 0 && _astros.Count == 0) || (_aboard > 0 && _oxygen < 20);
        if (goHome)
        {
            // Line up outside the bay, then drift in.
            var mouth = new Vector2(Bay.Right + 50, Bay.CenterY);
            bool lined = _pos.X < Bay.Right + 70 && MathF.Abs(_pos.Y - Bay.CenterY) < 20;
            target = lined ? Bay.Center : mouth;
        }
        else
        {
            target = _pos;
            float best = float.MaxValue;
            foreach (var a in _astros)
            {
                if (a.Bounce > 0)
                    continue;
                float d = Vector2.DistanceSquared(a.Pos, _pos);
                if (d < best)
                {
                    best = d;
                    target = a.Pos + a.Vel * 0.3f;
                }
            }
            if (best == float.MaxValue)
                target = new Vector2(WorldW / 2, WorldH / 2);
        }
        var to = target - _pos;
        float dist = to.Length();
        var desired = dist > 1 ? to / dist * MathF.Min(190, dist * 1.4f + 10) : Vector2.Zero;
        // Steer around rocks, mines and drones.
        foreach (var r in _rocks)
        {
            var d = _pos - r.Pos;
            float l = d.Length();
            if (l < r.R + 55 && l > 0.01f)
                desired += d / l * (r.R + 55 - l) * 5;
        }
        foreach (var m in _mines)
        {
            var d = _pos - m.Pos;
            float l = d.Length();
            if (l < 90 && l > 0.01f)
                desired += d / l * (90 - l) * 4;
        }
        var dv = desired - _vel;
        if (dv.Length() > 12)
        {
            dv.Normalize();
            c.SetDirections(dv.X, dv.Y);
        }
        // Shoot anything ahead.
        var fwd = MathF2.FromAngle(_angle, 1);
        bool shoot = false;
        foreach (var m in _mines)
            shoot |= Ahead(m.Pos, fwd, 220);
        foreach (var d in _drones)
            shoot |= Ahead(d.Pos, fwd, 300);
        foreach (var r in _rocks)
            shoot |= Ahead(r.Pos, fwd, 120);
        foreach (var a in _astros)
            if (Ahead(a.Pos, fwd, 60))
                shoot = false;
        c.Fire = shoot;
        c.FirePressed = shoot && Tick % 10 == 0;
    }

    private bool Ahead(Vector2 p, Vector2 fwd, float range)
    {
        var d = p - _pos;
        float l = d.Length();
        return l < range && l > 1 && Vector2.Dot(d / l, fwd) > 0.92f;
    }
}
