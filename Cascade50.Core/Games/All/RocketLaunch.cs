using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 37 Rocket Launch: fly a three-stage rocket from the pad to orbit. Throttle without overheating,
/// steer inside the flight corridor, dodge everything in the sky and drop each stage as it runs dry.
/// </summary>
public sealed class RocketLaunch : MiniGame, Cascade50.Core.Capture.ICaptureHints
{
    public override int Number => 37;
    public override string Title => "Rocket Launch";
    public override Category Category => Category.Skill;
    public override string Tagline => "Throttle, steer and stage your rocket all the way to orbit.";
    public override Color Accent => Pal.Orange;

    public override string[] HowToPlay =>
    [
        "Hold FIRE to throttle up. Let go before the engine temperature reaches the red.",
        "Stay in the flight corridor and dodge birds, planes, balloons and space junk.",
        "Drop each stage the moment its fuel runs dry. Score = altitude, plus an orbit bonus.",
    ];

    public override string[] DesktopControls => ["SPACE throttle, ARROWS steer.", "X (or SPACE when empty) to stage."];
    public override string[] TouchControls => ["Hold FIRE to throttle, arrows steer.", "Tap STAGE when the fuel is empty."];
    public int CaptureTicks => 720;

    public override Pad Pad => Pad.Horizontal | Pad.Fire | Pad.Alt;
    public override string FireLabel => "FIRE";
    public override string AltLabel => "STAGE";

    // ------------------------------------------------------------------ tuning

    private const float BaseY = 312;          // screen y of the rocket's base
    private const float PxPerKm = 55;
    private const float OrbitKm = 100;
    private static readonly float[] StageH = [26, 20, 16];
    private static readonly float[] StageW = [16, 13, 11];
    private static readonly float[] StageThrust = [0.5f, 0.58f, 0.66f];
    private static readonly float[] StageFuel = [17, 16, 16];
    private const float NoseH = 15;
    private const float PadX = 320;

    private enum Phase { Countdown, Flying, Orbit, Lost }

    private enum Kind { Bird, Plane, Balloon, Satellite, Debris }

    private struct Hazard
    {
        public Kind Kind;
        public float Alt, X, Vx, Phase, Spin, R;
        public bool Alive;
    }

    private struct Dropped
    {
        public int Stage;
        public Vector2 Pos;
        public float Vy, Vx, Angle, Spin, Life;
    }

    private static readonly Dictionary<char, Color> Colours = new()
    {
        ['k'] = new Color(30, 30, 40), ['w'] = Pal.White, ['r'] = Pal.Red, ['b'] = new Color(60, 110, 200),
        ['c'] = Pal.Sky, ['g'] = Pal.LightGrey,
    };

    private static readonly PixelArt[] BirdArt =
    [
        new(["k.......k", ".k.....k.", "..kk.kk..", "....k....", "........."], Colours),
        new([".........", "....k....", "..kk.kk..", ".k.....k.", "k.......k"], Colours),
    ];

    private static readonly PixelArt PlaneArt = new(
    [
        "ww..................",
        "www......ww.........",
        "wwwwwwwwwwwwwwwwwc..",
        "rrrrrrrrrrrrrrrrrrww",
        "gggggggggggggggggww.",
        "........ww..........",
    ], Colours);

    private static readonly Vector2[] NoseShape = [new(-5.5f, 0), new(5.5f, 0), new(2.5f, -9), new(0, -15), new(-2.5f, -9)];
    private static readonly Vector2[] FinL = [new(-8, 0), new(-14, 4), new(-14, -4), new(-8, -12)];
    private static readonly Vector2[] FinR = [new(8, 0), new(8, -12), new(14, -4), new(14, 4)];
    private static readonly Vector2[] FlameOuter = [new(-7, 0), new(7, 0), new(0, 1)];
    private static readonly Vector2[] DebrisShape = [new(-6, -3), new(-1, -7), new(6, -4), new(7, 3), new(1, 7), new(-5, 5)];

    private static readonly Vector2[] StarPos;
    private static readonly float[] StarMag;

    static RocketLaunch()
    {
        var rng = new Random(37);
        StarPos = new Vector2[140];
        StarMag = new float[140];
        for (int i = 0; i < StarPos.Length; i++)
        {
            StarPos[i] = new Vector2((float)rng.NextDouble() * 640, (float)rng.NextDouble() * 360);
            StarMag[i] = 0.3f + 0.7f * (float)rng.NextDouble();
        }
    }

    // ------------------------------------------------------------------ state

    private readonly List<Hazard> _hazards = new();
    private readonly List<Dropped> _dropped = new();
    private Phase _phase;
    private float _phaseTime;
    private int _mission;
    private float _alt, _v, _x, _vx;
    private float _temp, _shutdown, _stress;
    private int _stage, _hull;
    private float _fuel;
    private float _emptyTime;      // seconds since the current stage ran dry (-1 = not empty)
    private bool _throttle;
    private float _thrust;         // actual thrust 0..1 (smoothed)
    private float _nextSpawn;
    private float _scoredAlt;
    private float _alarmTimer, _promptTimer;
    private float _banner;
    private string _bannerText;
    private Color _bannerColour;
    private float _hitFlash, _invuln;
    private float _fairingOpen;
    private bool _autoThrottle;

    protected override void Start()
    {
        Lives = 3;
        _mission = 1;
        NewMission();
    }

    private void NewMission()
    {
        Level = _mission;
        _phase = Phase.Countdown;
        _phaseTime = 0;
        _alt = 0;
        _v = 0;
        _x = 320;
        _vx = 0;
        _temp = 0.1f;
        _shutdown = 0;
        _stress = 0;
        _stage = 0;
        _hull = 3;
        _fuel = StageFuel[0];
        _emptyTime = -1;
        _thrust = 0;
        _nextSpawn = 1.2f;
        _scoredAlt = 0;
        _fairingOpen = 0;
        _invuln = 0;
        _hazards.Clear();
        _dropped.Clear();
        UpdateStatus();
    }

    private void UpdateStatus() => Status = $"MISSION {_mission}  STAGE {_stage + 1}/3";

    private float Gravity => 0.2f * (1 + 0.05f * (_mission - 1));

    private float CorridorCentre(float a)
    {
        float m = 1 + (_mission - 1) * 0.15f;
        return 320 + 115 * MathF.Sin(a * 0.055f * m) + 55 * MathF.Sin(a * 0.16f * m + _mission) - 55 * MathF.Sin(_mission) * MathF.Min(1, a * 0.2f);
    }

    private float CorridorHalf(float a) => MathF.Max(68, 130 - a * 0.35f - (_mission - 1) * 6);

    private float ScreenY(float a) => BaseY - (a - _alt) * PxPerKm;

    private float RocketHeight()
    {
        float h = NoseH;
        for (int s = _stage; s < 3; s++)
            h += StageH[s];
        return h;
    }

    private void Banner(string text, Color colour, float seconds = 1.6f)
    {
        _bannerText = text;
        _bannerColour = colour;
        _banner = seconds;
    }

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        _phaseTime += Dt;
        if (_banner > 0)
            _banner -= Dt;
        if (_hitFlash > 0)
            _hitFlash -= Dt;
        if (_invuln > 0)
            _invuln -= Dt;

        switch (_phase)
        {
            case Phase.Countdown:
                UpdateCountdown();
                break;
            case Phase.Flying:
                UpdateFlight();
                break;
            case Phase.Orbit:
                UpdateOrbit();
                break;
            case Phase.Lost:
                _alt += _v * Dt;
                _v = MathF.Max(0, _v - 0.5f * Dt);
                if (_phaseTime > 2.2f)
                    NewMission();
                break;
        }
        UpdateHazards();
        UpdateDropped();
    }

    private void UpdateCountdown()
    {
        int before = (int)MathF.Ceiling(3 - (_phaseTime - Dt));
        int now = (int)MathF.Ceiling(3 - _phaseTime);
        if (now != before && now > 0)
            Sound.Play(Sfx.Beep, 0.2f, 0.7f);
        // Vent steam on the pad.
        if (Tick % 4 == 0)
            Fx.Spark(_x + Rand(-12, 12), BaseY - 30, Rand(-30, 30), Rand(-10, 10), Pal.White * 0.6f, 0.7f, 3, 0, false);
        if (_phaseTime >= 3)
        {
            _phase = Phase.Flying;
            _phaseTime = 0;
            Sound.Play(Sfx.Start);
            Sound.Play(Sfx.Whoosh, -0.6f);
            Banner("LIFT OFF!", Pal.Yellow);
            Fx.Shake(3, 0.6f);
            for (int i = 0; i < 30; i++)
                Fx.Spark(_x + Rand(-10, 10), BaseY + 4, Rand(-160, 160), Rand(-20, 10), Pal.LightGrey * 0.7f, 1.4f, Rand(4, 7), 0, false);
        }
    }

    private void UpdateFlight()
    {
        // Steering.
        _vx += In.AxisX * 420 * Dt;
        _vx *= 1 - 2.6f * Dt;
        _x += _vx * Dt;
        if (_x < 24 || _x > 616)
        {
            _x = MathF2.Clamp(_x, 24, 616);
            _vx = 0;
        }

        // Throttle and engine temperature.
        bool hasFuel = _fuel > 0;
        _throttle = In.Fire && hasFuel && _shutdown <= 0;
        if (_shutdown > 0)
            _shutdown -= Dt;
        float target = !hasFuel || _shutdown > 0 ? 0 : _throttle ? 1 : 0.5f;
        _thrust = MathF2.Approach(_thrust, target, 3 * Dt);
        if (_throttle)
            _temp += (0.21f + 0.02f * _stage) * Dt;
        else
            _temp -= (_shutdown > 0 ? 0.45f : 0.3f) * Dt;
        _temp = MathF2.Clamp(_temp, 0, 1.05f);
        if (_temp >= 1)
        {
            _shutdown = 1.6f;
            _temp = 0.95f;
            Sound.Play(Sfx.Crack, -0.4f);
            Sound.Play(Sfx.Hurt);
            Banner("ENGINE OVERHEAT!", Pal.Red);
            Fx.Burst(_x, BaseY, Pal.Orange, 30, 120, 0.6f);
            Damage("OVERHEATED");
            if (_phase != Phase.Flying)
                return;
        }
        if (_temp > 0.8f)
        {
            _alarmTimer -= Dt;
            if (_alarmTimer <= 0)
            {
                Sound.Play(Sfx.Alarm, 0.3f, 0.45f);
                _alarmTimer = 0.45f;
            }
        }

        // Fuel.
        if (hasFuel)
        {
            _fuel -= _thrust * Dt;
            if (_fuel <= 0)
            {
                _fuel = 0;
                _emptyTime = 0;
                Sound.Play(Sfx.Fuse, -0.5f, 0.6f);
                if (_stage < 2)
                    Banner("STAGE NOW!", Pal.Cyan, 1.2f);
            }
        }
        else if (_emptyTime >= 0)
        {
            _emptyTime += Dt;
        }

        // Staging: ALT any time, or FIRE once the tank is dry.
        if (_stage < 2 && (In.AltPressed || (In.FirePressed && _fuel <= 0)))
            Jettison();
        if (_fuel <= 0 && _stage < 2)
        {
            _promptTimer -= Dt;
            if (_promptTimer <= 0)
            {
                Sound.Play(Sfx.Tick, 0.5f, 0.6f);
                _promptTimer = 0.3f;
            }
        }

        // Vertical motion.
        float drag = 0.16f * MathF.Exp(-_alt / 30) + 0.08f;
        float thrust = _thrust * StageThrust[_stage];
        _v += (thrust - Gravity - drag * _v) * Dt;
        if (_alt <= 0 && _v < 0)
            _v = 0;
        _alt = MathF.Max(0, _alt + _v * Dt);
        if (_alt > 1 && _v < -0.25f)
        {
            Banner("STALLED!", Pal.Red);
            Explode("THE ROCKET STALLED");
            return;
        }

        // Score for every 100 m of new height.
        while (_alt > _scoredAlt + 0.1f)
        {
            _scoredAlt += 0.1f;
            AddScore(1);
        }

        // Exhaust particles.
        if (_thrust > 0.05f)
        {
            float atm = MathF.Max(0, 1 - _alt / 50);
            var tilt = Tilt;
            var basePos = Local(0, 2, tilt);
            if (Tick % 2 == 0 && atm > 0.05f)
                Fx.Spark(basePos.X + Rand(-4, 4), basePos.Y + 6, Rand(-15, 15) - _vx * 0.3f, 40 + _v * PxPerKm * 0.6f,
                    Pal.LightGrey * (0.5f * atm), 1.2f, Rand(3, 6), 0, false);
            Fx.Spark(basePos.X + Rand(-3, 3), basePos.Y + 4, Rand(-20, 20), 120 + _v * PxPerKm * 0.4f,
                Pal.Lerp(Pal.Yellow, Pal.Orange, Rand(0, 1)), 0.25f, 2f * _thrust + 0.5f);
        }

        // Flight corridor.
        float off = MathF.Abs(_x - CorridorCentre(_alt)) - CorridorHalf(_alt);
        if (off > 0 && _alt > 0.5f)
        {
            _stress += Dt * 0.45f;
            if (Tick % 20 == 0)
                Sound.Play(Sfx.Beep, 0.6f, 0.45f);
            if (_stress >= 1)
            {
                Banner("RANGE SAFETY!", Pal.Red);
                Explode("YOU FLEW OFF COURSE");
                return;
            }
        }
        else
        {
            _stress = MathF.Max(0, _stress - Dt * 0.3f);
        }

        // Damage smoke.
        if (_hull < 3 && Tick % (_hull == 1 ? 3 : 7) == 0)
        {
            var sp = Local(Rand(-4, 4), -RocketHeight() * 0.5f, Tilt);
            Fx.Spark(sp.X, sp.Y, Rand(-10, 10), 60, Pal.DarkGrey, 0.8f, 3, 0, false);
        }

        // Hazards.
        SpawnHazards();
        CheckCollisions();
        if (_phase != Phase.Flying)
            return;

        // Atmosphere.
        float air = MathF.Max(0, 1 - _alt / 45);
        Sound.Loop(LoopSfx.Thrust, _thrust > 0.05f, -0.3f + _stage * 0.25f + _thrust * 0.2f, 0.25f + 0.55f * _thrust);
        Sound.Loop(LoopSfx.Wind, air > 0.05f && _v > 0.2f, _v * 0.2f - 0.4f, air * MathF.Min(1, _v) * 0.5f);

        if (_alt >= OrbitKm * (1 + 0.05f * (_mission - 1)))
            ReachOrbit();
    }

    private float OrbitTarget => OrbitKm * (1 + 0.05f * (_mission - 1));

    private void Jettison()
    {
        float waste = _fuel / StageFuel[_stage];
        var pos = Local(0, -StageH[_stage] / 2, Tilt);
        _dropped.Add(new Dropped
        {
            Stage = _stage, Pos = pos, Vy = 0, Vx = _vx * 0.5f + Rand(-12, 12), Angle = Tilt, Spin = Rand(-1.5f, 1.5f), Life = 4,
        });
        Sound.Play(Sfx.Cannon, 0.2f);
        Sound.Play(Sfx.Whoosh, 0.3f, 0.6f);
        Fx.Shake(2.5f, 0.25f);
        var sep = Local(0, -StageH[_stage], Tilt);
        Fx.Burst(sep.X, sep.Y, Pal.White, 18, 110, 0.5f, 2.2f);
        Fx.Burst(sep.X, sep.Y, Pal.Orange, 10, 70, 0.4f, 2f);

        if (_fuel > 0)
        {
            Banner(waste > 0.25f ? "TOO EARLY!" : "A LITTLE EARLY", Pal.Orange);
            Sound.Play(Sfx.Wrong, 0, 0.5f);
        }
        else if (_emptyTime < 0.7f)
        {
            AddScore(300, sep.X + 40, sep.Y, Pal.Cyan);
            Banner("PERFECT STAGING", Pal.Cyan);
            Sound.Play(Sfx.Bonus);
        }
        else if (_emptyTime < 1.6f)
        {
            AddScore(100, sep.X + 40, sep.Y, Pal.Cyan);
            Banner("GOOD STAGING", Pal.Lime);
            Sound.Play(Sfx.Correct, 0, 0.6f);
        }
        else
        {
            Banner("LATE STAGING", Pal.Orange);
        }

        _stage++;
        _fuel = StageFuel[_stage];
        _emptyTime = -1;
        _temp *= 0.4f;
        _shutdown = 0;
        _thrust = 0.3f;
        UpdateStatus();
    }

    private void ReachOrbit()
    {
        _phase = Phase.Orbit;
        _phaseTime = 0;
        int bonus = 1500 + 500 * _mission + _hull * 200 + (int)(_fuel * 20);
        AddScore(bonus, _x, BaseY - 120, Pal.Gold);
        Banner("ORBIT ACHIEVED!", Pal.Gold, 3);
        Sound.Play(Sfx.LevelUp);
        Sound.Play(Sfx.Bonus, 0.3f);
        _hazards.Clear();
    }

    private void UpdateOrbit()
    {
        // Coast while the fairing opens and the payload deploys.
        _v = MathF2.Approach(_v, 0.6f, Dt);
        _alt += _v * Dt;
        _vx *= 1 - 3 * Dt;
        _x += (320 - _x) * Dt;
        _fairingOpen = MathF.Min(1, _fairingOpen + Dt * 0.7f);
        if (_phaseTime > 0.8f && _phaseTime - Dt <= 0.8f)
            Sound.Play(Sfx.Pop, 0.3f);
        if (_phaseTime > 4.2f)
        {
            _mission++;
            Sound.Play(Sfx.PowerUp);
            NewMission();
            Banner($"MISSION {_mission}: HEAVIER PAYLOAD", Pal.Yellow, 2.5f);
        }
    }

    private float Tilt => MathF2.Clamp(_vx / 420f, -0.35f, 0.35f);

    private Vector2 Local(float lx, float ly, float tilt)
    {
        float c = MathF.Cos(tilt), s = MathF.Sin(tilt);
        return new Vector2(_x + lx * c - ly * s, BaseY + lx * s + ly * c);
    }

    // ------------------------------------------------------------------ hazards

    private void SpawnHazards()
    {
        float ahead = _alt + 6.2f;
        while (_nextSpawn < ahead)
        {
            float a = _nextSpawn;
            float density = 1 + 0.22f * (_mission - 1);
            float gap;
            if (a < 1.5f)
                gap = 1.2f;
            else if (a < 13)
            {
                if (Chance(0.55f) || a < 3)
                {
                    // A small flock of birds.
                    int n = RandInt(2, 5);
                    float x = Rand(60, 580), dir = Chance(0.5f) ? 1 : -1;
                    for (int i = 0; i < n; i++)
                        Add(Kind.Bird, a + (i % 2) * 0.12f + i * 0.05f, x - dir * i * 16, dir * Rand(30, 50), 7);
                }
                else
                {
                    float dir = Chance(0.5f) ? 1 : -1;
                    Add(Kind.Plane, a, dir > 0 ? -60 : 700, dir * Rand(110, 150), 12);
                }
                gap = Rand(1.2f, 2.2f);
            }
            else if (a < 42)
            {
                Add(Kind.Balloon, a, Rand(50, 590), Rand(-12, 12), 11);
                gap = Rand(1.8f, 3.2f);
            }
            else if (a < 62)
            {
                if (Chance(0.6f))
                    Add(Kind.Debris, a, Rand(50, 590), Rand(-50, 50), 8);
                gap = Rand(3, 5);
            }
            else
            {
                if (Chance(0.45f))
                    Add(Kind.Satellite, a, Rand(60, 580), Rand(-35, 35), 13);
                else
                    Add(Kind.Debris, a, Rand(40, 600), Rand(-70, 70), 8);
                gap = Rand(1.6f, 2.8f);
            }
            _nextSpawn += gap / density;
        }
    }

    private void Add(Kind kind, float alt, float x, float vx, float r) =>
        _hazards.Add(new Hazard { Kind = kind, Alt = alt, X = x, Vx = vx, R = r, Phase = Rand(0, 6), Spin = Rand(-2, 2), Alive = true });

    private void UpdateHazards()
    {
        for (int i = _hazards.Count - 1; i >= 0; i--)
        {
            var h = _hazards[i];
            h.X += h.Vx * Dt;
            h.Phase += Dt;
            if (h.Kind == Kind.Balloon)
                h.Alt += 0.02f * Dt;
            if (h.Kind == Kind.Bird)
                h.Alt += MathF.Sin(h.Phase * 3) * 0.02f * Dt;
            if (h.Kind is Kind.Bird or Kind.Balloon or Kind.Satellite or Kind.Debris)
            {
                if (h.X < 20 || h.X > 620)
                    h.Vx = MathF.Abs(h.Vx) * (h.X < 20 ? 1 : -1);
            }
            _hazards[i] = h;
            if (!h.Alive || ScreenY(h.Alt) > 400 || h.X < -100 || h.X > 740)
                _hazards.RemoveAt(i);
        }
    }

    private void CheckCollisions()
    {
        if (_invuln > 0)
            return;
        var tilt = Tilt;
        float height = RocketHeight();
        for (int i = 0; i < _hazards.Count; i++)
        {
            var h = _hazards[i];
            var hp = new Vector2(h.X, ScreenY(h.Alt));
            if (hp.Y < BaseY - height - 30 || hp.Y > BaseY + 20)
                continue;
            bool hit = false;
            for (int k = 0; k < 4 && !hit; k++)
            {
                var p = Local(0, -height * (k + 0.5f) / 4, tilt);
                if (MathF2.Circles(p, StageW[_stage] * 0.45f, hp, h.R * 0.8f))
                    hit = true;
            }
            if (!hit)
                continue;
            h.Alive = false;
            _hazards[i] = h;
            var col = h.Kind switch { Kind.Bird => Pal.Grey, Kind.Plane => Pal.White, Kind.Balloon => Pal.White, Kind.Satellite => Pal.Gold, _ => Pal.Silver };
            Fx.Burst(hp.X, hp.Y, col, 18, 120, 0.6f, 2.5f);
            Fx.Explode(hp.X, hp.Y, h.Kind == Kind.Plane ? 1.2f : 0.6f);
            Sound.Play(h.Kind == Kind.Balloon ? Sfx.Pop : Sfx.Explode, Rand(-0.2f, 0.2f));
            Damage(h.Kind switch
            {
                Kind.Bird => "BIRD STRIKE",
                Kind.Plane => "MID-AIR COLLISION",
                Kind.Balloon => "HIT A WEATHER BALLOON",
                _ => "HIT BY SPACE JUNK",
            });
            if (_phase != Phase.Flying)
                return;
        }
    }

    private void Damage(string reason)
    {
        _hull--;
        _hitFlash = 0.3f;
        _invuln = 1.3f;
        Fx.Shake(4, 0.3f);
        Sound.Play(Sfx.Hit);
        if (_hull <= 0)
        {
            Banner(reason, Pal.Red);
            Explode(reason);
        }
        else
        {
            Banner(reason + "!", Pal.Orange, 1.2f);
        }
    }

    private void Explode(string reason)
    {
        float h = RocketHeight();
        for (int k = 0; k < 4; k++)
        {
            var p = Local(0, -h * k / 4, Tilt);
            Fx.Explode(p.X, p.Y, 1.3f);
        }
        Sound.Play(Sfx.BigExplode);
        Sound.StopLoops();
        _phase = Phase.Lost;
        _phaseTime = 0;
        if (LoseLife())
            EndGame(false, reason);
    }

    private void UpdateDropped()
    {
        float scroll = _v * PxPerKm;
        for (int i = _dropped.Count - 1; i >= 0; i--)
        {
            var d = _dropped[i];
            d.Vy += 40 * Dt;
            d.Pos.Y += (d.Vy + scroll) * Dt;
            d.Pos.X += d.Vx * Dt;
            d.Angle += d.Spin * Dt;
            d.Life -= Dt;
            if (Tick % 3 == 0 && d.Life > 3)
                Fx.Spark(d.Pos.X, d.Pos.Y, Rand(-10, 10), Rand(-10, 10), Pal.Orange, 0.4f, 1.5f);
            _dropped[i] = d;
            if (d.Pos.Y > 420 || d.Life <= 0)
                _dropped.RemoveAt(i);
        }
    }

    // ------------------------------------------------------------------ drawing

    private static readonly float[] SkyKeys = [0, 8, 20, 38, 58, 80];

    private static readonly Color[] SkyCols =
    [
        new(150, 205, 250), new(80, 145, 235), new(40, 85, 195), new(16, 34, 110), new(6, 10, 42), new(2, 2, 12),
    ];

    private static Color SkyAt(float a)
    {
        if (a <= SkyKeys[0])
            return SkyCols[0];
        for (int i = 1; i < SkyKeys.Length; i++)
            if (a < SkyKeys[i])
                return Pal.Lerp(SkyCols[i - 1], SkyCols[i], (a - SkyKeys[i - 1]) / (SkyKeys[i] - SkyKeys[i - 1]));
        return SkyCols[^1];
    }

    public override void Draw(Gfx g)
    {
        DrawSky(g, _alt, Screen.Bounds, Time);
        DrawGround(g);
        DrawClouds(g, false);
        DrawCorridor(g);

        foreach (var d in _dropped)
            DrawStageBody(g, d.Stage, d.Pos, d.Angle, true);

        DrawHazards(g);
        if (_phase != Phase.Lost && (_invuln <= 0 || (int)(_invuln * 12) % 2 == 0))
            DrawRocket(g);
        DrawClouds(g, true);
        DrawHud(g);

        if (_phase == Phase.Countdown)
        {
            int n = (int)MathF.Ceiling(3 - _phaseTime);
            float k = 1 - (_phaseTime % 1);
            g.TextShadow(n.ToString(), 320, 120 - k * 6, 5 + k * 2, Pal.Yellow, Align.Center);
            g.TextShadow("HOLD FIRE TO CLIMB", 320, 180, 2, Pal.White, Align.Center);
        }
        if (_banner > 0 && _bannerText != null)
        {
            float a = MathF.Min(1, _banner * 2);
            g.TextShadow(_bannerText, 320, 76, 2.5f, _bannerColour * a, Align.Center);
        }
        if (_phase == Phase.Flying && _fuel <= 0 && _stage < 2 && (int)(Time * 4) % 2 == 0)
            g.TextShadow(IsTouch ? "TAP STAGE!" : "PRESS FIRE TO STAGE!", _x, BaseY - RocketHeight() - 34, 1.5f, Pal.Cyan, Align.Center);
        if (_hitFlash > 0)
            g.Rect(0, 0, 640, 360, Pal.Red * (_hitFlash * 0.5f));
    }

    private static void DrawSky(Gfx g, float alt, RectF r, float time)
    {
        g.GradientV(r.X, r.Y, r.W, r.H, SkyAt(alt + 14), SkyAt(alt - 4));
        float starA = MathF2.Clamp((alt - 22) / 30, 0, 1);
        if (starA > 0)
        {
            float scroll = alt * 4;
            for (int i = 0; i < StarPos.Length; i++)
            {
                float x = r.X + StarPos[i].X / 640 * r.W;
                float y = r.Y + Backdrops.Mod(StarPos[i].Y + scroll * StarMag[i], 360) / 360 * r.H;
                float tw = 0.6f + 0.4f * MathF.Sin(time * (1 + StarMag[i] * 3) + i);
                g.Circle(x, y, 0.5f + StarMag[i], Pal.White * (starA * tw * StarMag[i]));
                if (StarMag[i] > 0.9f)
                    g.Glow(x, y, 5, Pal.Sky, 0.3f * starA * tw);
            }
        }
        // Earth's limb rising as we leave the atmosphere.
        if (alt > 30)
        {
            float k = MathF2.Clamp((alt - 30) / 70, 0, 1);
            float rad = MathF2.Lerp(3200, 900, k) * r.W / 640;
            float top = r.Bottom - (6 + 36 * k) * r.H / 360;
            g.Glow(r.CenterX, top + rad, rad + 40 * r.H / 360, Pal.Sky, 0.5f * k);
            g.Circle(r.CenterX, top + rad, rad, new Color(20, 70, 150));
            g.Circle(r.CenterX, top + rad + 6 * r.H / 360, rad, new Color(16, 50, 110));
        }
    }

    private void DrawGround(Gfx g)
    {
        float gy = ScreenY(0) + 6;
        if (gy > 380)
            return;
        // Distant mountains and the launch complex.
        Backdrops.Hills(g, gy - 20, 18, 0, new Color(90, 130, 170), 4, new RectF(0, gy - 60, 640, 80));
        Backdrops.Hills(g, gy - 6, 10, 200, new Color(60, 120, 80), 9, new RectF(0, gy - 40, 640, 60));
        g.GradientV(0, gy, 640, 60, Pal.Grass, Pal.Forest);
        g.Rect(PadX - 50, gy - 4, 100, 6, Pal.Grey);
        g.Rect(PadX - 50, gy - 4, 100, 2, Pal.LightGrey);
        // Gantry tower.
        float tx = PadX - 34;
        g.Rect(tx - 6, gy - 110, 3, 106, Pal.Red);
        g.Rect(tx + 6, gy - 110, 3, 106, Pal.Red);
        for (int i = 0; i < 9; i++)
        {
            float y0 = gy - 110 + i * 12;
            g.Line(tx - 5, y0, tx + 8, y0 + 12, 1.2f, Pal.Darken(Pal.Red, 0.3f));
            g.Line(tx + 8, y0, tx - 5, y0 + 12, 1.2f, Pal.Darken(Pal.Red, 0.3f));
        }
        g.Rect(tx - 8, gy - 114, 20, 4, Pal.LightGrey);
        if (_phase == Phase.Countdown)
            g.Rect(tx + 8, gy - 70, PadX - tx - 14, 2, Pal.LightGrey);
        g.Glow(tx + 1, gy - 116, 6, Pal.Red, (int)(Time * 2) % 2 == 0 ? 0.9f : 0.2f);
    }

    private void DrawClouds(Gfx g, bool front)
    {
        for (int i = 0; i < 26; i++)
        {
            uint h = (uint)(i * 2654435761u);
            bool isFront = (h >> 7) % 4 == 0;
            if (isFront != front)
                continue;
            float a = 1.4f + i * 0.62f + ((h >> 3) % 100) / 200f;
            float y = ScreenY(a);
            if (y < -40 || y > 400)
                continue;
            float x = 30 + (h >> 11) % 580;
            float size = 14 + (h >> 17) % 18;
            float alpha = front ? 0.45f : 0.85f;
            bool cirrus = a > 10;
            if (cirrus)
            {
                g.Ellipse(x, y, size * 3, size * 0.25f, Pal.White * (alpha * 0.5f));
                g.Ellipse(x + size, y - 3, size * 2, size * 0.2f, Pal.White * (alpha * 0.4f));
                continue;
            }
            var shade = new Color(200, 215, 235) * alpha;
            var lit = Pal.White * alpha;
            g.Ellipse(x, y + size * 0.3f, size * 2.2f, size * 0.5f, shade);
            g.Circle(x - size, y, size * 0.7f, lit);
            g.Circle(x, y - size * 0.3f, size, lit);
            g.Circle(x + size * 0.9f, y, size * 0.75f, lit);
        }
    }

    private void DrawCorridor(Gfx g)
    {
        if (_phase == Phase.Countdown || _phase == Phase.Orbit)
            return;
        const float step = 10;
        for (float y = Screen.HudHeight; y < 360; y += step)
        {
            float a = _alt + (BaseY - y) / PxPerKm;
            if (a < 0.4f)
                continue;
            float c = CorridorCentre(a), hw = CorridorHalf(a);
            float l = c - hw, r = c + hw;
            g.Rect(0, y, MathF.Max(0, l), step, Pal.Red * 0.07f);
            g.Rect(r, y, MathF.Max(0, 640 - r), step, Pal.Red * 0.07f);
            float a2 = _alt + (BaseY - y - step) / PxPerKm;
            float c2 = CorridorCentre(a2), hw2 = CorridorHalf(a2);
            bool dash = ((int)((a * PxPerKm) / step) & 1) == 0;
            if (dash)
            {
                g.GlowLine(new Vector2(l, y + step), new Vector2(c2 - hw2, y), 1.2f, Pal.Cyan * 0.8f);
                g.GlowLine(new Vector2(r, y + step), new Vector2(c2 + hw2, y), 1.2f, Pal.Cyan * 0.8f);
            }
        }
    }

    private void DrawHazards(Gfx g)
    {
        foreach (var h in _hazards)
        {
            float y = ScreenY(h.Alt);
            if (y < 0 || y > 380)
                continue;
            DrawHazard(g, h.Kind, h.X, y, h.Vx, h.Phase, h.Spin, 1, Time);
        }
    }

    private static void DrawHazard(Gfx g, Kind kind, float x, float y, float vx, float phase, float spin, float s, float time)
    {
        switch (kind)
        {
            case Kind.Bird:
                g.PixelsCentered(BirdArt[(int)(phase * 6) % 2], x, y, 1.8f * s, vx < 0);
                break;
            case Kind.Plane:
                g.Line(x - MathF.Sign(vx) * 22 * s, y - 1 * s, x - MathF.Sign(vx) * 160 * s, y - 1 * s, 2.5f * s, Pal.White * 0.35f);
                g.PixelsCentered(PlaneArt, x, y, 2f * s, vx < 0);
                g.Glow(x + MathF.Sign(vx) * 18 * s, y, 4 * s, (int)(time * 3) % 2 == 0 ? Pal.Red : Pal.Green, 0.9f);
                break;
            case Kind.Balloon:
            {
                float sway = MathF.Sin(phase * 1.5f) * 3 * s;
                g.Line(x + sway, y + 9 * s, x, y + 22 * s, 1, Pal.LightGrey);
                g.Rect(x - 3 * s, y + 22 * s, 6 * s, 5 * s, Pal.Orange);
                g.Circle(x + sway, y, 11 * s, new Color(235, 235, 240));
                g.Circle(x + sway - 3 * s, y - 4 * s, 4 * s, Pal.White);
                g.Ellipse(x + sway, y + 9 * s, 3 * s, 2 * s, new Color(200, 200, 210));
                break;
            }
            case Kind.Satellite:
            {
                float ang = phase * spin * 0.3f;
                var c = new Vector2(x, y);
                var d = MathF2.FromAngle(ang, 14 * s);
                g.Line(c - d, c + d, 1.2f * s, Pal.Silver);
                g.RotatedRect(c + d * 1.25f, 12 * s, 7 * s, ang, new Color(40, 70, 170));
                g.RotatedRect(c - d * 1.25f, 12 * s, 7 * s, ang, new Color(40, 70, 170));
                g.RotatedRect(c + d * 1.25f, 12 * s, 1 * s, ang, Pal.Sky * 0.6f);
                g.RotatedRect(c - d * 1.25f, 12 * s, 1 * s, ang, Pal.Sky * 0.6f);
                g.RotatedRect(c, 9 * s, 9 * s, ang, Pal.Gold);
                g.RotatedRect(c, 5 * s, 5 * s, ang, Pal.Darken(Pal.Gold, 0.3f));
                g.Glow(c.X, c.Y - 5 * s, 5 * s, Pal.Red, (int)(time * 2 + phase) % 2 == 0 ? 0.9f : 0.1f);
                break;
            }
            default:
            {
                var c = new Vector2(x, y);
                g.Glow(c, 12 * s, Pal.Orange, 0.15f);
                g.Shape(DebrisShape, c, phase * spin, s, new Color(110, 105, 100));
                g.Shape(DebrisShape, c + new Vector2(-1, -1) * s, phase * spin, s * 0.6f, new Color(160, 155, 150));
                break;
            }
        }
    }

    private void DrawRocket(Gfx g)
    {
        float tilt = Tilt;
        // Exhaust flame.
        if (_thrust > 0.02f && _phase == Phase.Flying)
        {
            float flick = 0.85f + 0.1f * MathF.Sin(Time * 50) + 0.07f * MathF.Sin(Time * 83);
            float len = (12 + 30 * _thrust) * flick;
            float w = StageW[_stage] * 0.4f;
            var b = Local(0, 1, tilt);
            g.Glow(b.X, b.Y + len * 0.4f, len * 1.2f, Pal.Orange, 0.6f * _thrust + 0.2f);
            DrawFlame(g, tilt, w * 1.2f, len, Pal.Orange);
            DrawFlame(g, tilt, w * 0.8f, len * 0.7f, Pal.Yellow);
            DrawFlame(g, tilt, w * 0.4f, len * 0.4f, Pal.White);
        }
        // Stack.
        float y = 0;
        for (int s = _stage; s < 3; s++)
        {
            DrawStageBody(g, s, Local(0, y - StageH[s] / 2, tilt), tilt, false);
            y -= StageH[s];
        }
        // Nose fairing: opens in orbit.
        float w3 = StageW[2];
        if (_fairingOpen <= 0)
        {
            g.Shape(NoseShape, Local(0, y, tilt), tilt, 1, Pal.White);
            g.Shape(NoseShape, Local(1, y, tilt), tilt, 0.6f, Pal.LightGrey);
        }
        else
        {
            float o = _fairingOpen;
            g.Shape(NoseShape, Local(-w3 * 0.5f - o * 30, y - o * 10, tilt), tilt - o * 2, 0.8f, Pal.White);
            g.Shape(NoseShape, Local(w3 * 0.5f + o * 30, y - o * 10, tilt), tilt + o * 2, 0.8f, Pal.White);
            // Satellite payload unfolding.
            var p = Local(0, y - 6 - o * 18, tilt);
            float span = 4 + o * 18;
            g.Rect(p.X - span - 2, p.Y - 3, span, 6, new Color(50, 80, 190));
            g.Rect(p.X + 2, p.Y - 3, span, 6, new Color(50, 80, 190));
            g.Rect(p.X - 4, p.Y - 5, 8, 10, Pal.Gold);
            g.Glow(p.X, p.Y, 20, Pal.Gold, 0.4f * o);
        }
    }

    private void DrawFlame(Gfx g, float tilt, float halfW, float len, Color c)
    {
        var a = Local(-halfW, 0, tilt);
        var b = Local(halfW, 0, tilt);
        var tip = Local(0, len, tilt);
        g.Triangle(a, b, tip, c);
    }

    private static void DrawStageBody(Gfx g, int stage, Vector2 centre, float angle, bool spent)
    {
        float w = StageW[stage], h = StageH[stage];
        var body = spent ? new Color(170, 170, 180) : new Color(236, 238, 245);
        var up = MathF2.FromAngle(angle - MathF.PI / 2, 1);
        var side = MathF2.FromAngle(angle, 1);
        if (stage == 0)
        {
            var finBase = centre - up * (h / 2);
            g.Shape(FinL, finBase, angle, 1, Pal.Red);
            g.Shape(FinR, finBase, angle, 1, Pal.Red);
        }
        g.RotatedRect(centre, w, h, angle, body);
        // Shading strip and bands.
        g.RotatedRect(centre + side * (w * 0.3f), w * 0.3f, h, angle, Color.Black * 0.12f);
        g.RotatedRect(centre - side * (w * 0.3f), w * 0.15f, h, angle, Pal.White * 0.5f);
        var band = stage switch { 0 => Pal.Red, 1 => new Color(40, 60, 140), _ => Pal.Red };
        g.RotatedRect(centre + up * (h / 2 - 3), w, 3, angle, band);
        g.RotatedRect(centre - up * (h / 2 - 2), w + 1, 2, angle, Pal.DarkGrey);
        if (stage == 1)
            g.RotatedRect(centre, w * 0.5f, 4, angle, new Color(40, 60, 140));
        // Engine bell.
        var bell = centre - up * (h / 2 + 2);
        g.RotatedRect(bell, w * 0.6f, 4, angle, Pal.Grey);
    }

    private void DrawHud(Gfx g)
    {
        // Right panel: gauges.
        var panel = new RectF(560, 30, 72, 196);
        g.Panel(panel, Pal.Panel * 0.8f, Pal.Darken(Pal.Orange, 0.4f), 6);
        g.Text("ALT KM", panel.CenterX, panel.Y + 6, 1, Pal.LightGrey, Align.Center);
        g.Text(_alt.ToString("0.0"), panel.CenterX, panel.Y + 16, 1.5f, Pal.White, Align.Center);
        g.Text("SPEED", panel.CenterX, panel.Y + 34, 1, Pal.LightGrey, Align.Center);
        g.Text((_v * 1000).ToString("0"), panel.CenterX, panel.Y + 44, 1.5f, Pal.White, Align.Center);

        // Temperature gauge.
        float gx = panel.X + 12, gy = panel.Y + 70, gh = 96;
        g.Text("TEMP", gx + 4, gy - 10, 1, Pal.LightGrey, Align.Center);
        g.RoundRect(gx - 1, gy - 1, 10, gh + 2, 3, Pal.Black);
        g.Rect(gx, gy, 8, gh * 0.2f, Pal.Red * 0.35f);
        float t = MathF2.Clamp(_temp, 0, 1);
        var tc = t > 0.8f ? Pal.Red : t > 0.6f ? Pal.Orange : Pal.Lime;
        g.GradientV(gx, gy + gh * (1 - t), 8, gh * t, tc, Pal.Darken(tc, 0.4f));
        g.Rect(gx - 2, gy + gh * 0.2f, 12, 1, Pal.White);
        g.Circle(gx + 4, gy + gh + 6, 6, tc);
        if (t > 0.8f)
            g.Glow(gx + 4, gy + gh * 0.5f, 26, Pal.Red, 0.4f + 0.3f * MathF.Sin(Time * 20));

        // Fuel gauge.
        float fx = panel.X + 46;
        float f = MathF2.Clamp(_fuel / StageFuel[_stage], 0, 1);
        g.Text("FUEL", fx + 4, gy - 10, 1, Pal.LightGrey, Align.Center);
        g.RoundRect(fx - 1, gy - 1, 10, gh + 2, 3, Pal.Black);
        var fc = f < 0.15f ? Pal.Orange : Pal.Cyan;
        g.GradientV(fx, gy + gh * (1 - f), 8, gh * f, fc, Pal.Darken(fc, 0.4f));
        if (_fuel <= 0 && (int)(Time * 4) % 2 == 0)
            g.Text("DRY", fx + 4, gy + gh + 3, 1, Pal.Orange, Align.Center);
        else
            g.Text("S" + (_stage + 1), fx + 4, gy + gh + 3, 1, Pal.Cyan, Align.Center);

        // Hull.
        var hp = new RectF(560, 232, 72, 26);
        g.Panel(hp, Pal.Panel * 0.8f, Pal.Darken(Pal.Orange, 0.4f), 6);
        g.Text("HULL", hp.X + 6, hp.Y + 9, 1, Pal.LightGrey);
        for (int i = 0; i < 3; i++)
            g.RoundRect(hp.X + 34 + i * 12, hp.Y + 7, 9, 12, 2, i < _hull ? Pal.Lime : Pal.DarkGrey);

        // Left: altitude progress to orbit.
        float bx = 14, by = 40, bh = 290;
        float target = OrbitTarget;
        g.RoundRect(bx - 3, by - 3, 10, bh + 6, 4, Pal.Black * 0.5f);
        float pa = MathF2.Clamp(_alt / target, 0, 1);
        g.GradientV(bx, by + bh * (1 - pa), 4, bh * pa, Pal.Gold, Pal.Orange);
        g.Text("ORBIT", bx + 2, by - 13, 1, Pal.Gold, Align.Center);
        g.Rect(bx - 3, by, 12, 1, Pal.Gold);
        g.Triangle(new Vector2(bx + 8, by + bh * (1 - pa)), new Vector2(bx + 14, by + bh * (1 - pa) - 4), new Vector2(bx + 14, by + bh * (1 - pa) + 4), Pal.White);

        // Off course warning.
        if (_stress > 0.02f && _phase == Phase.Flying)
        {
            var sr = new RectF(220, 300, 200, 22);
            g.Panel(sr, Pal.Panel * 0.8f, Pal.Red, 5);
            g.Rect(sr.X + 4, sr.Y + 4, (sr.W - 8) * MathF.Min(1, _stress), sr.H - 8, Pal.Red * 0.6f);
            if ((int)(Time * 5) % 2 == 0)
                g.Text("OFF COURSE!", sr.CenterX, sr.Y + 6, 1.5f, Pal.White, Align.Center);
        }
    }

    // ------------------------------------------------------------------ icon

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        float s = r.H / 70f;
        g.GradientV(r.X, r.Y, r.W, r.H, new Color(2, 3, 18), new Color(40, 90, 200));
        for (int i = 0; i < 40; i++)
        {
            float x = r.X + StarPos[i].X / 640 * r.W;
            float y = r.Y + Backdrops.Mod(StarPos[i].Y + time * 12 * StarMag[i], 360) / 360 * r.H * 0.7f;
            g.Circle(x, y, (0.4f + StarMag[i] * 0.5f) * s, Pal.White * (0.8f * StarMag[i] * (1 - y / r.Bottom * 0.3f)));
        }
        // Earth's limb.
        float rad = r.W * 1.6f;
        g.Glow(r.CenterX, r.Bottom - 10 * s + rad, rad + 14 * s, Pal.Sky, 0.7f);
        g.Circle(r.CenterX, r.Bottom - 10 * s + rad, rad, new Color(20, 80, 170));
        g.Circle(r.CenterX + 10 * s, r.Bottom - 6 * s + rad, rad, new Color(16, 60, 130));
        // A satellite drifting by.
        var sat = new Vector2(r.X + r.W * 0.8f, r.Y + r.H * 0.25f + MathF.Sin(time) * 2 * s);
        DrawHazard(g, Kind.Satellite, sat.X, sat.Y, 0, time, 1, s * 0.7f, time);
        // Rocket climbing, slightly tilted, with a smoke trail.
        float tilt = 0.25f + 0.05f * MathF.Sin(time * 1.3f);
        var b = new Vector2(r.X + r.W * 0.42f, r.Y + r.H * 0.66f);
        var up = MathF2.FromAngle(tilt - MathF.PI / 2, 1);
        var side = MathF2.FromAngle(tilt, 1);
        for (int i = 0; i < 9; i++)
        {
            float k = i / 9f;
            var p = b - up * (8 + i * 6) * s + side * MathF.Sin(time * 3 + i) * s;
            g.Circle(p.X, p.Y, (2 + i * 0.9f) * s, Pal.White * (0.35f * (1 - k)));
        }
        float flick = 0.85f + 0.15f * MathF.Sin(time * 40);
        g.Glow(b + -up * 8 * s, 22 * s, Pal.Orange, 0.9f);
        g.Triangle(b - side * 4 * s, b + side * 4 * s, b - up * 22 * s * flick, Pal.Orange);
        g.Triangle(b - side * 2.5f * s, b + side * 2.5f * s, b - up * 13 * s * flick, Pal.Yellow);
        g.Shape(FinL, b, tilt, 0.7f * s, Pal.Red);
        g.Shape(FinR, b, tilt, 0.7f * s, Pal.Red);
        var body = new Color(236, 238, 245);
        g.RotatedRect(b + up * 10 * s, 10 * s, 20 * s, tilt, body);
        g.RotatedRect(b + up * 17 * s, 10 * s, 2.5f * s, tilt, Pal.Red);
        g.RotatedRect(b + up * 27 * s, 8.5f * s, 14 * s, tilt, body);
        g.RotatedRect(b + up * 30 * s, 8.5f * s, 2.5f * s, tilt, new Color(40, 60, 140));
        g.RotatedRect(b + up * 18 * s + side * 3 * s, 3 * s, 36 * s, tilt, Color.Black * 0.12f);
        g.Shape(NoseShape, b + up * 34 * s, tilt, 0.75f * s, Pal.White);
        // A spent stage tumbling away below.
        float t = (time * 0.45f) % 1;
        var d = new Vector2(r.X + r.W * 0.3f - t * 30 * s, r.Y + r.H * 0.8f + t * 40 * s);
        g.RotatedRect(d, 9 * s, 16 * s, 0.3f + t * 5, new Color(170, 170, 180) * (1 - t * 0.7f));
        g.Glow(d, 9 * s, Pal.Orange, 0.6f * (1 - t));
    }

    // ------------------------------------------------------------------ autopilot

    public override void AutoPlay(Controls c)
    {
        if (_phase != Phase.Flying)
        {
            c.Fire = true;
            return;
        }
        // Throttle: run hot, back off before the red.
        if (_temp > 0.74f)
            _autoThrottle = false;
        else if (_temp < 0.5f)
            _autoThrottle = true;
        c.Fire = _autoThrottle;

        // Stage as soon as the tank is dry.
        if (_fuel <= 0 && _stage < 2 && _emptyTime > 0.15f)
        {
            c.FirePressed = true;
            c.Fire = true;
        }

        // Steer for the corridor, choosing the clearest line through anything ahead.
        float nose = BaseY - RocketHeight();
        float speed = MathF.Max(0.2f, _v) * PxPerKm;
        float centre = CorridorCentre(_alt + 1f), half = CorridorHalf(_alt + 1f);
        float target = _x, bestCost = float.MaxValue;
        for (float cx = _x - 160; cx <= _x + 160; cx += 8)
        {
            if (cx < 30 || cx > 610)
                continue;
            float cost = MathF.Abs(cx - _x) * 0.15f + MathF.Abs(cx - centre) * 0.08f;
            if (MathF.Abs(cx - centre) > half - 18)
                cost += 400;
            foreach (var h in _hazards)
            {
                float y = ScreenY(h.Alt);
                if (y > BaseY + 10 || y < nose - 260)
                    continue;
                float t = MathF.Max(0, (y < nose ? nose - y : 0) / speed);
                float hx = h.X + h.Vx * t;
                float reach = MathF.Abs(cx - _x) / 140f;
                float clear = MathF.Abs(hx - cx) - (14 + h.R);
                if (clear < 16)
                    cost += 600 / (t + 0.3f) * (reach > t ? 1.5f : 1);
                // Sweeping across a hazard on the way there.
                float lo = MathF.Min(cx, _x) - 12 - h.R, hi = MathF.Max(cx, _x) + 12 + h.R;
                if (t < 0.6f && hx > lo && hx < hi)
                    cost += 200;
            }
            if (cost < bestCost)
            {
                bestCost = cost;
                target = cx;
            }
        }
        float d = target - _x - _vx * 0.3f;
        c.SetDirections(MathF2.Clamp(d / 30, -1, 1), 0);
    }
}
