using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 05 Cannonball Battle: an artillery duel against the computer across destructible hills.
/// Set the angle and power, allow for the wind and lob cannonballs over the hill.
/// </summary>
public sealed class CannonballBattle : MiniGame, Capture.ICaptureHints
{
    public override int Number => 5;
    public override string Title => "Cannonball Battle";
    public override Category Category => Category.Skill;
    public override string Tagline => "Out-gun the computer in an artillery duel across crumbling hills.";
    public override Color Accent => Pal.Sky;

    public override string[] HowToPlay =>
    [
        "Lob cannonballs at the enemy, allowing for the wind. Shots blast craters in the hills.",
        "First to 3 hits wins the round. Hits score 500, near misses 50, round wins 1000 more.",
        "The enemy gets sharper. Lose a round and it's over.",
    ];

    public override string[] DesktopControls => ["ARROWS aim, SPACE fires.", "Or drag from your cannon."];
    public override string[] TouchControls => ["Stick aims, FIRE shoots.", "Or drag from your cannon."];
    public override Pad Pad => Pad.Stick | Pad.Fire;
    public int CaptureTicks => 600;

    private const float Gravity = 260, BlastR = 22, HitR = 24;
    private const int HitsToWin = 3;
    private const float ShotClock = 25;

    private enum Phase
    {
        Banner,
        PlayerAim,
        CpuAim,
        Flight,
        After,
    }

    private sealed class Gun
    {
        public float X, Y, Angle = 50, Power = 60, Recoil, Flash;
        public int Hits; // hits taken
        public bool Left;
        public float ShowAngle;
    }

    private readonly float[] _ground = new float[641];
    private readonly Gun _you = new() { Left = true };
    private readonly Gun _cpu = new() { Left = false };
    private readonly List<Vector2> _smoke = new();
    private Phase _phase;
    private float _phaseT;
    private bool _playerShot;
    private Vector2 _ball, _ballVel;
    private float _wind;
    private int _round, _cpuShots;
    private float _cpuTargetAngle, _cpuTargetPower;
    private float _clock;
    private bool _dragging;
    private string _banner;
    private float _bannerT;
    private bool _roundOver;
    private float _cloudX;
    private float _autoAngle = -1, _autoPower;

    protected override void Start()
    {
        _round = 0;
        NewRound();
    }

    private void NewRound()
    {
        _round++;
        Level = _round;
        _you.Hits = _cpu.Hits = 0;
        _cpuShots = 0;
        _roundOver = false;
        GenerateTerrain();
        _you.Angle = 45;
        _you.Power = 55;
        _cpu.Angle = 45;
        _cpu.Power = 55;
        NewWind();
        SetBanner("ROUND " + _round, 1.8f);
        _phase = Phase.Banner;
        _phaseT = 0;
        _playerShot = false;
    }

    private void GenerateTerrain()
    {
        float p1 = Rand(0, 10), p2 = Rand(0, 10), p3 = Rand(0, 10);
        float hill = Rand(70, 110) + Math.Min(_round, 6) * 8;
        float hillX = Rand(260, 380), hillW = Rand(70, 120);
        _you.X = Rand(70, 140);
        _cpu.X = Rand(500, 570);
        for (int x = 0; x <= 640; x++)
        {
            float h = 300 - 18 * MathF.Sin(x * 0.011f + p1) - 10 * MathF.Sin(x * 0.027f + p2) - 4 * MathF.Sin(x * 0.07f + p3);
            float d = (x - hillX) / hillW;
            h -= hill * MathF.Exp(-d * d);
            _ground[x] = MathF2.Clamp(h, 90, 344);
        }
        // Flatten pads for the cannons.
        foreach (var gun in new[] { _you, _cpu })
        {
            int cx = (int)gun.X;
            float y = _ground[cx];
            for (int x = Math.Max(0, cx - 40); x <= Math.Min(640, cx + 40); x++)
            {
                float w = MathF2.Clamp((40 - MathF.Abs(x - cx)) / 26f, 0, 1);
                _ground[x] = MathF2.Lerp(_ground[x], y, MathF2.EaseInOut(w));
            }
            gun.Y = y;
        }
    }

    private void NewWind()
    {
        float max = 25 + Math.Min(_round, 6) * 12;
        _wind = MathF.Round(Rand(-max, max));
    }

    private void SetBanner(string text, float time)
    {
        _banner = text;
        _bannerT = time;
    }

    private float GroundAt(float x) => _ground[(int)MathF2.Clamp(x, 0, 640)];

    private static Vector2 Muzzle(Gun g)
    {
        float a = MathHelper.ToRadians(g.Left ? g.Angle : 180 - g.Angle);
        return new Vector2(g.X, g.Y - 11) + new Vector2(MathF.Cos(a), -MathF.Sin(a)) * 20;
    }

    private static Vector2 LaunchVelocity(Gun g)
    {
        float a = MathHelper.ToRadians(g.Left ? g.Angle : 180 - g.Angle);
        float speed = 90 + g.Power * 3.3f;
        return new Vector2(MathF.Cos(a), -MathF.Sin(a)) * speed;
    }

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        _phaseT += Dt;
        if (_bannerT > 0)
            _bannerT -= Dt;
        _cloudX += _wind * 0.4f * Dt;
        SettleGun(_you);
        SettleGun(_cpu);
        Sound.Loop(LoopSfx.Wind, true, 0, MathF.Min(0.5f, MathF.Abs(_wind) / 120f));

        switch (_phase)
        {
            case Phase.Banner:
                if (_phaseT > 1.8f)
                    StartTurn(true);
                break;
            case Phase.PlayerAim:
                PlayerAim();
                break;
            case Phase.CpuAim:
                CpuAim();
                break;
            case Phase.Flight:
                Flight();
                break;
            case Phase.After:
                if (_phaseT > 1.1f)
                {
                    if (_roundOver)
                    {
                        if (_cpu.Hits >= HitsToWin)
                            NewRound();
                        else
                            EndGame(false, $"Defeated in round {_round}");
                    }
                    else
                    {
                        StartTurn(!_playerShot);
                    }
                }
                break;
        }
    }

    private void SettleGun(Gun gun)
    {
        {
            gun.Recoil = MathF.Max(0, gun.Recoil - Dt * 3);
            gun.Flash = MathF.Max(0, gun.Flash - Dt * 6);
            // Settle onto the ground after craters.
            float gy = GroundAt(gun.X);
            if (gun.Y < gy)
                gun.Y = MathF.Min(gy, gun.Y + 120 * Dt);
            gun.ShowAngle = MathF2.Approach(gun.ShowAngle, gun.Angle, 120 * Dt);
        }
    }

    private void StartTurn(bool player)
    {
        _phaseT = 0;
        if (player)
        {
            _phase = Phase.PlayerAim;
            _clock = ShotClock;
            _autoAngle = -1;
            SetBanner("YOUR SHOT", 1f);
        }
        else
        {
            _phase = Phase.CpuAim;
            PlanCpuShot();
        }
    }

    private void PlayerAim()
    {
        _clock -= Dt;
        if (_clock <= 0)
        {
            SetBanner("TOO SLOW!", 1.2f);
            Sound.Play(Sfx.Wrong);
            _playerShot = true;
            _phase = Phase.After;
            _phaseT = 0.3f;
            return;
        }
        if (_clock < 5 && (int)(_clock * 60) % 60 == 0)
            Sound.Play(Sfx.Tick, 0.3f, 0.6f);

        // Keyboard / stick.
        float fine = 1;
        _you.Angle = MathF2.Clamp(_you.Angle - In.AxisX * 40 * fine * Dt, 5, 175);
        _you.Power = MathF2.Clamp(_you.Power - In.AxisY * 35 * Dt, 5, 100);
        if ((MathF.Abs(In.AxisX) > 0.3f || MathF.Abs(In.AxisY) > 0.3f) && Tick % 6 == 0)
            Sound.Play(Sfx.Tick, -0.4f, 0.15f);

        // Drag to aim.
        if (In.PointerPressed)
            _dragging = true;
        if (_dragging && In.PointerDown)
        {
            var d = In.Pointer - new Vector2(_you.X, _you.Y - 11);
            if (d.Length() > 8)
            {
                float a = MathHelper.ToDegrees(MathF.Atan2(-d.Y, d.X));
                _you.Angle = MathF2.Clamp(a, 5, 175);
                _you.Power = MathF2.Clamp(d.Length() / 1.6f, 5, 100);
            }
        }
        bool fire = In.FirePressed;
        if (_dragging && In.PointerReleased)
        {
            _dragging = false;
            if (Vector2.Distance(In.Pointer, In.PointerStart) > 10 || Vector2.Distance(In.Pointer, new Vector2(_you.X, _you.Y)) > 30)
                fire = true;
        }
        if (!In.PointerDown && !In.PointerReleased)
            _dragging = false;
        if (fire)
            Fire(_you);
    }

    private void PlanCpuShot()
    {
        // Search a few angles for the power that lands nearest the player, then add an error that
        // shrinks with every shot and every round.
        float bestErr = float.MaxValue;
        float bestA = 45, bestP = 60;
        for (int ai = 0; ai < 6; ai++)
        {
            float a = 35 + ai * 7;
            for (float p = 10; p <= 100; p += 1.5f)
            {
                float x = SimulateLanding(_cpu, a, p);
                float err = MathF.Abs(x - _you.X);
                if (err < bestErr)
                {
                    bestErr = err;
                    bestA = a;
                    bestP = p;
                }
            }
        }
        float sigma = MathF.Max(0.6f, (9f - _round * 1.1f) * MathF.Pow(0.72f, _cpuShots));
        float gauss = (Rand(-1, 1) + Rand(-1, 1) + Rand(-1, 1)) / 1.5f;
        _cpuTargetAngle = bestA + Rand(-1, 1) * sigma * 0.3f;
        _cpuTargetPower = MathF2.Clamp(bestP + gauss * sigma, 5, 100);
        _cpuShots++;
    }

    private float SimulateLanding(Gun g, float angle, float power)
    {
        float oa = g.Angle, op = g.Power;
        g.Angle = angle;
        g.Power = power;
        var p = Muzzle(g);
        var v = LaunchVelocity(g);
        g.Angle = oa;
        g.Power = op;
        const float dt = Dt / 2;
        for (int i = 0; i < 1800; i++)
        {
            v.Y += Gravity * dt;
            v.X += _wind * dt;
            p += v * dt;
            if (p.X < 0)
                return -200;
            if (p.X > 640)
                return 840;
            if (p.Y >= GroundAt(p.X))
                return p.X;
        }
        return p.X;
    }

    private void CpuAim()
    {
        if (_phaseT < 0.6f)
            return;
        _cpu.Angle = MathF2.Approach(_cpu.Angle, _cpuTargetAngle, 50 * Dt);
        _cpu.Power = MathF2.Approach(_cpu.Power, _cpuTargetPower, 60 * Dt);
        if (Tick % 6 == 0 && (_cpu.Angle != _cpuTargetAngle || _cpu.Power != _cpuTargetPower))
            Sound.Play(Sfx.Tick, -0.5f, 0.12f);
        if (_cpu.Angle == _cpuTargetAngle && _cpu.Power == _cpuTargetPower && _phaseT > 1.4f)
            Fire(_cpu);
    }

    private void Fire(Gun g)
    {
        _playerShot = g == _you;
        _ball = Muzzle(g);
        _ballVel = LaunchVelocity(g);
        g.Recoil = 1;
        g.Flash = 1;
        _phase = Phase.Flight;
        _phaseT = 0;
        _smoke.Clear();
        Sound.Play(Sfx.Cannon, Rand(-0.1f, 0.1f));
        Fx.Shake(2, 0.15f);
        var dir = Vector2.Normalize(_ballVel);
        for (int i = 0; i < 10; i++)
        {
            var v = dir * Rand(20, 80) + new Vector2(Rand(-20, 20), Rand(-20, 20));
            Fx.Spark(_ball.X, _ball.Y, v.X, v.Y, Pal.LightGrey * 0.6f, 0.9f, 3.5f, -10, false);
        }
        Fx.Burst(_ball.X, _ball.Y, Pal.Orange, 10, 80, 0.25f, 2f);
    }

    private void Flight()
    {
        // Two sub-steps for accuracy.
        for (int s = 0; s < 2; s++)
        {
            float dt = Dt / 2;
            _ballVel.Y += Gravity * dt;
            _ballVel.X += _wind * dt;
            _ball += _ballVel * dt;
            if (_ball.X < -10 || _ball.X > 650)
            {
                SetBanner("OUT OF RANGE", 1f);
                Sound.Play(Sfx.Whoosh, -0.3f, 0.5f);
                EndShot();
                return;
            }
            var target = _playerShot ? _cpu : _you;
            if (Vector2.Distance(_ball, new Vector2(target.X, target.Y - 8)) < 11 || (_ball.X >= 0 && _ball.X <= 640 && _ball.Y >= GroundAt(_ball.X)))
            {
                Impact();
                return;
            }
        }
        if (Tick % 2 == 0)
        {
            _smoke.Add(_ball);
            if (_smoke.Count > 60)
                _smoke.RemoveAt(0);
        }
        Sound.Loop(LoopSfx.Hum, true, MathF2.Clamp(-_ballVel.Y / 400f, -0.8f, 0.8f), 0.15f);
    }

    private void Impact()
    {
        var p = _ball;
        // Crater.
        for (int x = (int)MathF.Max(0, p.X - BlastR); x <= (int)MathF.Min(640, p.X + BlastR); x++)
        {
            float dx = x - p.X;
            float h = MathF.Sqrt(MathF.Max(0, BlastR * BlastR - dx * dx));
            if (p.Y - h <= _ground[x] + 2)
                _ground[x] = MathF.Min(352, MathF.Max(_ground[x], p.Y + h * 0.8f));
        }
        Fx.Explode(p.X, p.Y, 1.1f);
        Fx.Burst(p.X, p.Y, Pal.Brown, 24, 140, 0.9f, 2.5f, 300, false);
        Fx.Burst(p.X, p.Y, Pal.Grass, 8, 110, 0.7f, 2f, 300, false);

        var target = _playerShot ? _cpu : _you;
        float dist = Vector2.Distance(p, new Vector2(target.X, target.Y - 8));
        if (dist < HitR)
        {
            target.Hits++;
            Sound.Play(Sfx.BigExplode);
            Fx.Explode(target.X, target.Y - 6, 1.8f);
            Fx.Shake(6, 0.4f);
            if (_playerShot)
            {
                AddScore(500, target.X, target.Y - 40, Pal.Yellow);
                SetBanner("DIRECT HIT!", 1.2f);
            }
            else
            {
                SetBanner("YOU'RE HIT!", 1.2f);
                Sound.Play(Sfx.Hurt);
            }
            if (target.Hits >= HitsToWin)
            {
                _roundOver = true;
                if (_playerShot)
                {
                    AddScore(1000 * _round, 320, 120, Pal.Cyan);
                    SetBanner("ROUND WON!", 2f);
                    Sound.Play(Sfx.Win);
                }
            }
        }
        else
        {
            Sound.Play(Sfx.Explode, Rand(-0.2f, 0.2f));
            if (_playerShot && dist < 60)
            {
                AddScore(50, p.X, p.Y - 30, Pal.Orange);
                SetBanner("CLOSE!", 0.8f);
            }
        }
        EndShot();
    }

    private void EndShot()
    {
        _phase = Phase.After;
        _phaseT = 0;
        if (!_roundOver)
            NewWind();
    }

    // ------------------------------------------------------------------ drawing

    public override void Draw(Gfx g)
    {
        var v = g.Visible;
        DrawSky(g, v, Time, _cloudX, 1f);

        // Terrain.
        for (int x = 0; x < 640; x += 2)
        {
            float y = MathF.Min(_ground[x], _ground[x + 1]);
            g.GradientV(x, y, 2.05f, 360 - y + 2, new Color(120, 85, 50), new Color(55, 35, 25));
            g.Rect(x, y, 2.05f, 4, new Color(70, 170, 60));
            g.Rect(x, y, 2.05f, 1.5f, new Color(140, 220, 90));
            int hsh = (x * 7919) % 97;
            if (hsh < 12)
                g.Rect(x, y + 10 + hsh * 3, 2, 2, new Color(85, 58, 38));
            else if (hsh > 90)
                g.Rect(x, y + 24 + (hsh - 90) * 6, 3, 2, new Color(150, 115, 80));
        }
        g.Rect(v.X, 359, v.W, v.Bottom - 359, new Color(55, 35, 25));

        // Smoke trail.
        for (int i = 0; i < _smoke.Count; i++)
        {
            float k = i / (float)_smoke.Count;
            g.Circle(_smoke[i].X, _smoke[i].Y, 1 + k * 2, Pal.LightGrey * (0.4f * k));
        }

        DrawGun(g, _you, new Color(60, 120, 255), 1.25f);
        DrawGun(g, _cpu, new Color(230, 50, 50), 1.25f);

        // Aiming guide.
        if (_phase == Phase.PlayerAim)
        {
            var p = Muzzle(_you);
            var vel = LaunchVelocity(_you);
            for (int i = 0; i < 14; i++)
            {
                float t = i * 0.035f;
                var q = p + vel * t + new Vector2(0, 0.5f * Gravity * t * t);
                g.Circle(q.X, q.Y, 1.6f, Pal.White * (0.8f - i * 0.05f));
            }
            g.TextShadow($"ANGLE {_you.Angle:0}", _you.X, _you.Y + 12 > 340 ? _you.Y - 60 : _you.Y + 8, 1f, Pal.White, Align.Center);
            g.TextShadow($"POWER {_you.Power:0}", _you.X, (_you.Y + 12 > 340 ? _you.Y - 60 : _you.Y + 8) + 10, 1f, Pal.Yellow, Align.Center);
            if (_clock < 10)
                g.TextShadow(((int)MathF.Ceiling(_clock)).ToString(), 320, 64, 2f, _clock < 5 ? Pal.Red : Pal.White, Align.Center);
        }

        // Ball.
        if (_phase == Phase.Flight)
        {
            if (_ball.Y < Screen.HudHeight + 4)
            {
                g.Triangle(new Vector2(_ball.X - 5, 32), new Vector2(_ball.X + 5, 32), new Vector2(_ball.X, 25), Pal.Yellow);
            }
            else
            {
                g.Glow(_ball, 10, Pal.Orange, 0.5f);
                g.Circle(_ball.X, _ball.Y, 3.5f, new Color(30, 30, 35));
                g.Circle(_ball.X - 1, _ball.Y - 1, 1.2f, Pal.Grey);
            }
        }

        DrawHud(g);

        if (_bannerT > 0 && _banner != null)
        {
            float a = MathF.Min(1, _bannerT * 2);
            g.TextShadow(_banner, 320, 92, 2.5f, Pal.Yellow * a, Align.Center);
        }
    }

    private void DrawHud(Gfx g)
    {
        // Hits.
        var left = new RectF(8, 28, 112, 22);
        g.Panel(left, Pal.Panel * 0.85f, new Color(60, 120, 255), 6);
        g.Text("YOU", left.X + 8, left.Y + 7, 1f, Pal.White);
        for (int i = 0; i < HitsToWin; i++)
            Pip(g, left.X + 52 + i * 18, left.CenterY, i < _cpu.Hits);
        var right = new RectF(520, 28, 112, 22);
        g.Panel(right, Pal.Panel * 0.85f, new Color(230, 50, 50), 6);
        g.Text("CPU", right.X + 8, right.Y + 7, 1f, Pal.White);
        for (int i = 0; i < HitsToWin; i++)
            Pip(g, right.X + 52 + i * 18, right.CenterY, i < _you.Hits);

        // Wind.
        var w = new RectF(260, 28, 120, 22);
        g.Panel(w, Pal.Panel * 0.85f, Pal.PanelLight, 6);
        g.Text("WIND", w.X + 8, w.Y + 7, 1f, Pal.LightGrey);
        float len = MathF.Abs(_wind) / 100f * 34;
        float cx = w.X + 74, cy = w.CenterY;
        if (MathF.Abs(_wind) < 1)
        {
            g.Text("CALM", cx - 6, w.Y + 7, 1f, Pal.Lime);
        }
        else
        {
            float d = MathF.Sign(_wind);
            var col = MathF.Abs(_wind) > 60 ? Pal.Red : MathF.Abs(_wind) > 30 ? Pal.Orange : Pal.Lime;
            g.Line(cx - d * len / 2 - d * 4, cy, cx + d * len / 2, cy, 2.5f, col);
            g.Triangle(new Vector2(cx + d * len / 2 + d * 6, cy), new Vector2(cx + d * len / 2, cy - 5), new Vector2(cx + d * len / 2, cy + 5), col);
            g.Text(MathF.Abs(_wind).ToString("0"), w.Right - 8, w.Y + 7, 1f, Pal.White, Align.Right);
        }
    }

    private static void Pip(Gfx g, float x, float y, bool on)
    {
        if (on)
        {
            g.Glow(x, y, 10, Pal.Orange, 0.6f);
            g.Circle(x, y, 5, Pal.Orange);
            g.Circle(x - 1.5f, y - 1.5f, 1.5f, Pal.Yellow);
        }
        else
        {
            g.Circle(x, y, 5, Pal.DarkGrey);
        }
    }

    private static void DrawSky(Gfx g, RectF r, float time, float cloudX, float s)
    {
        g.GradientV(r.X, r.Y, r.W, r.H * 0.6f, new Color(40, 50, 120), new Color(240, 140, 90));
        g.Rect(r.X, r.Y + r.H * 0.6f, r.W, r.H * 0.4f, new Color(240, 140, 90));
        float sx = r.X + r.W * 0.5f, sy = r.Y + r.H * 0.5f;
        g.Glow(sx, sy, 110 * s, Pal.Orange, 0.6f);
        g.Circle(sx, sy, 26 * s, new Color(255, 210, 120));
        Backdrops.Hills(g, r.Y + r.H * 0.62f, 26 * s, 0, new Color(150, 80, 110), 8, r);
        Backdrops.Hills(g, r.Y + r.H * 0.7f, 20 * s, 300, new Color(110, 60, 95), 2, r);
        for (int i = 0; i < 4; i++)
        {
            float cx = r.X + Backdrops.Mod(i * 190 * s + cloudX * s, r.W + 120 * s) - 60 * s;
            float cy = r.Y + r.H * (0.2f + 0.08f * i);
            var c = new Color(255, 200, 190) * 0.55f;
            g.Ellipse(cx, cy, 30 * s, 6 * s, c);
            g.Ellipse(cx + 10 * s, cy - 4 * s, 16 * s, 6 * s, c);
        }
    }

    private static void DrawGun(Gfx g, Gun gun, Color flag, float s)
    {
        float x = gun.X, y = gun.Y;
        float face = gun.Left ? 1 : -1;
        float a = MathHelper.ToRadians(gun.Left ? gun.ShowAngle : 180 - gun.ShowAngle);
        var dir = new Vector2(MathF.Cos(a), -MathF.Sin(a));
        var pivot = new Vector2(x - face * gun.Recoil * 3 * s, y - 9 * s);
        // Flag pole.
        g.Rect(x - face * 12 * s, y - 30 * s, 1.5f * s, 24 * s, Pal.LightGrey);
        g.Triangle(new Vector2(x - face * 12 * s, y - 30 * s), new Vector2(x - face * 12 * s, y - 22 * s),
            new Vector2(x - face * (12 - 10) * s, y - 26 * s + MathF.Sin(x) * s), flag);
        // Barrel.
        g.Circle(pivot.X, pivot.Y, 5 * s, new Color(40, 40, 50));
        g.RotatedRect(pivot + dir * 8 * s, 19 * s, 7 * s, a, new Color(40, 40, 50));
        g.RotatedRect(pivot + dir * 3 * s, 9 * s, 9 * s, a, new Color(55, 55, 66));
        g.RotatedRect(pivot + dir * 9 * s - new Vector2(-dir.Y, dir.X) * 1.5f * s, 15 * s, 1.5f * s, a, new Color(120, 120, 135));
        if (gun.Flash > 0)
        {
            var m = pivot + dir * 20 * s;
            g.Glow(m, 26 * s * gun.Flash, Pal.Yellow, 1);
        }
        // Carriage.
        g.RoundRect(x - 10 * s, y - 11 * s, 20 * s, 7 * s, 2 * s, new Color(130, 80, 40));
        g.Rect(x - 10 * s, y - 11 * s, 20 * s, 2 * s, flag);
        g.Circle(x - 5 * s, y - 4 * s, 5 * s, new Color(70, 45, 25));
        g.Circle(x - 5 * s, y - 4 * s, 2 * s, new Color(170, 120, 60));
        g.Circle(x + 6 * s, y - 4 * s, 5 * s, new Color(70, 45, 25));
        g.Circle(x + 6 * s, y - 4 * s, 2 * s, new Color(170, 120, 60));
    }

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        float s = r.H / 70f;
        DrawSky(g, r, time, time * 10, s * 0.6f);
        // Hills.
        for (float x = r.X; x < r.Right; x += 2 * s)
        {
            float u = (x - r.X) / r.W;
            float h = r.Bottom - (10 + 8 * MathF.Sin(u * 9) + 26 * MathF.Exp(-MathF.Pow((u - 0.5f) / 0.16f, 2))) * s;
            g.GradientV(x, h, 2.1f * s, r.Bottom - h, new Color(120, 85, 50), new Color(55, 35, 25));
            g.Rect(x, h, 2.1f * s, 2.5f * s, new Color(90, 190, 70));
        }
        float y0 = r.Bottom - (10 + 8 * MathF.Sin(0.12f * 9)) * s;
        float y1 = r.Bottom - (10 + 8 * MathF.Sin(0.88f * 9)) * s;
        var a = new Gun { X = r.X + r.W * 0.12f, Y = y0, Left = true, ShowAngle = 52 };
        var b = new Gun { X = r.X + r.W * 0.88f, Y = y1, Left = false, ShowAngle = 45 };
        DrawGun(g, a, new Color(60, 120, 255), s * 0.9f);
        DrawGun(g, b, new Color(230, 50, 50), s * 0.9f);
        // Cannonball arc.
        float t = (time * 0.6f) % 1;
        var p0 = new Vector2(a.X + 10 * s, y0 - 18 * s);
        var p1 = new Vector2(b.X, y1 - 6 * s);
        for (int i = 0; i < 16; i++)
        {
            float u = t - i * 0.025f;
            if (u < 0)
                break;
            var q = Vector2.Lerp(p0, p1, u) + new Vector2(0, -4 * u * (1 - u) * 30 * s);
            g.Circle(q.X, q.Y, (1.8f - i * 0.1f) * s, Pal.LightGrey * (0.6f - i * 0.035f));
        }
        var bp = Vector2.Lerp(p0, p1, t) + new Vector2(0, -4 * t * (1 - t) * 30 * s);
        g.Glow(bp, 8 * s, Pal.Orange, 0.6f);
        g.Circle(bp.X, bp.Y, 2.8f * s, new Color(30, 30, 35));
        if (t > 0.9f)
            g.Glow(p1, 30 * s * (t - 0.9f) * 10, Pal.Yellow, 1);
    }

    // ------------------------------------------------------------------ autoplay

    public override void AutoPlay(Controls c)
    {
        if (_phase != Phase.PlayerAim)
            return;
        if (_autoAngle < 0)
        {
            // Work out a shot like the computer does, with a human-sized error.
            float bestErr = float.MaxValue;
            for (int ai = 0; ai < 5; ai++)
            {
                float a = 38 + ai * 8;
                for (float p = 10; p <= 100; p += 2)
                {
                    float x = SimulateLanding(_you, a, p);
                    float err = MathF.Abs(x - _cpu.X);
                    if (err < bestErr)
                    {
                        bestErr = err;
                        _autoAngle = a;
                        _autoPower = p;
                    }
                }
            }
            _autoPower += Rand(-4, 4);
        }
        float da = _autoAngle - _you.Angle, dp = _autoPower - _you.Power;
        c.SetDirections(MathF.Abs(da) > 1 ? -MathF.Sign(da) : 0, MathF.Abs(dp) > 1 ? -MathF.Sign(dp) : 0);
        if (MathF.Abs(da) <= 1 && MathF.Abs(dp) <= 1 && _phaseT > 1.2f)
        {
            c.FirePressed = true;
            c.Fire = true;
        }
    }
}
