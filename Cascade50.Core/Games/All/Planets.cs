using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 31 Planets: a gravity slingshot puzzle. Fire a probe from your home world and bend its path
/// around planets, moons, repulsor stars and black holes to reach the glowing target planet.
/// Every level is generated at random and checked by simulation to be solvable.
/// </summary>
public sealed class Planets : MiniGame, Capture.ICaptureHints
{
    public override int Number => 31;
    public override string Title => "Planets";
    public override Category Category => Category.Puzzle;
    public override string Tagline => "Slingshot a space probe round the planets to reach the target world.";
    public override Color Accent => Pal.Sky;
    public override Pad Pad => Pad.None;
    public int CaptureTicks => 560;

    public override string[] HowToPlay =>
    [
        "Launch a probe from your home world and let gravity curve it to the glowing green target.",
        "Planets pull, orange stars push, black holes swallow. The dots show only the start.",
        "3 probes per level. Stardust 25, a landing 300+.",
    ];

    public override string[] DesktopControls => ["Drag back to aim, let go to fire.", "Or ARROWS aim, SPACE fires."];
    public override string[] TouchControls => ["Drag back like a slingshot,", "then let go to fire."];

    private const float G = 900f, MaxSpeed = 235f, SubDt = 1f / 120f, PullLength = 110f;
    private const int MaxFlightSteps = 120 * 9, MoonPeriodTicks = 420, LastLevel = 20;
    private const float MoonOmega = MathF2.Tau / (MoonPeriodTicks * Dt);

    private enum Kind { Planet, Moon, Repulsor, BlackHole, Target }

    private enum Outcome { Flying, Target, Crash, Lost, Swallowed }

    private struct Body
    {
        public Kind Kind;
        public Vector2 Pos;
        public float R, Mass;
        public Color Color, Color2;
        public int Style;
        public int Parent;
        public float OrbitR, Phase;
    }

    private struct Dust
    {
        public Vector2 Pos;
        public bool Taken;
        public float Twinkle;
    }

    private enum State { Aiming, Flying, Result, Cleared }

    private readonly List<Body> _bodies = new();
    private readonly List<Dust> _dust = new();
    private readonly List<Vector2> _trail = new();
    private readonly List<Vector2> _scratchPath = new();
    private readonly List<Vector2> _predict = new();
    private readonly List<(float Angle, float Power)> _solutions = new();

    private Vector2 _home;
    private const float HomeR = 17;
    private int _target;
    private int _clock, _genClock, _launchClock;
    private State _state;
    private float _stateTimer;
    private string _resultText;
    private Color _resultColour;
    private float _aimAngle, _aimPower = 0.6f;
    private bool _dragging;
    private Vector2 _dragStart;
    private Vector2 _probePos, _probeVel;
    private int _flightSteps;
    private bool _hasMoons;

    // Autopilot.
    private int _autoTicks;
    private float _autoAngle, _autoPower;
    private bool _autoPlanned;

    protected override void Start()
    {
        Level = 1;
        _dragging = false;
        _clock = 0;
        BuildLevel();
    }

    // ------------------------------------------------------------------ level generation

    private void BuildLevel()
    {
        Lives = 3;
        _state = State.Aiming;
        _trail.Clear();
        _autoPlanned = false;
        _genClock = _clock;
        int bestSols = -1;
        int seedBase = Rng.Next();
        int maxAllowed = (int)MathF2.Lerp(110, 28, MathF2.Clamp((Level - 1) / 12f, 0, 1));
        for (int attempt = 0; attempt < 24; attempt++)
        {
            Layout(new Random(seedBase + attempt));
            int sols = Solve();
            if ((sols >= 4 && sols <= maxAllowed) || (sols >= 1 && attempt >= 16))
            {
                bestSols = sols;
                break;
            }
        }
        // Rare fallback: take obstacles away until the level can be done.
        while (bestSols < 1 && _bodies.Count > 1)
        {
            for (int i = _bodies.Count - 1; i >= 0; i--)
                if (_bodies[i].Kind != Kind.Target && _bodies[i].Kind != Kind.Moon)
                {
                    RemoveBody(i);
                    break;
                }
            bestSols = Solve();
            if (_bodies.Count <= 1)
                break;
        }
        PlaceDust();
        _aimAngle = MathF2.Angle(_bodies[_target].Pos - _home);
        _aimPower = 0.6f;
    }

    private void RemoveBody(int index)
    {
        _bodies.RemoveAt(index);
        // Remove any moons of it and fix the indices.
        for (int i = _bodies.Count - 1; i >= 0; i--)
        {
            var b = _bodies[i];
            if (b.Kind == Kind.Moon)
            {
                if (b.Parent == index)
                {
                    _bodies.RemoveAt(i);
                    continue;
                }
                if (b.Parent > index)
                {
                    b.Parent--;
                    _bodies[i] = b;
                }
            }
        }
        for (int i = 0; i < _bodies.Count; i++)
            if (_bodies[i].Kind == Kind.Target)
                _target = i;
        _hasMoons = false;
        foreach (var b in _bodies)
            if (b.Kind == Kind.Moon)
                _hasMoons = true;
    }

    private static readonly Color[][] PlanetColours =
    [
        [new Color(210, 120, 60), new Color(120, 50, 30)],
        [new Color(200, 170, 110), new Color(140, 90, 60)],
        [new Color(120, 150, 230), new Color(50, 60, 150)],
        [new Color(190, 90, 200), new Color(90, 30, 110)],
        [new Color(150, 200, 210), new Color(60, 100, 130)],
        [new Color(230, 200, 90), new Color(150, 100, 40)],
    ];

    private void Layout(Random rng)
    {
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        _bodies.Clear();
        _hasMoons = false;
        _home = new Vector2(R(40, 70), R(80, 320));
        var target = new Vector2(R(540, 600), R(60, 330));
        _target = 0;
        _bodies.Add(new Body { Kind = Kind.Target, Pos = target, R = 15, Mass = 15 * 15 * 0.7f, Color = Pal.Lime, Color2 = Pal.Teal });

        int planets = 1 + Math.Min((Level + 1) / 2, 4);
        for (int i = 0; i < planets; i++)
        {
            for (int tries = 0; tries < 40; tries++)
            {
                float r = R(13, 32 - Math.Min(Level, 8));
                Vector2 p;
                if (i == 0)
                {
                    // The first planet sits across the direct line so the probe must curve.
                    float t = R(0.38f, 0.62f);
                    var line = target - _home;
                    var n = Vector2.Normalize(new Vector2(-line.Y, line.X));
                    p = _home + line * t + n * R(-20, 20);
                }
                else
                {
                    p = new Vector2(R(150, 500), R(50, 340));
                }
                if (!Free(p, r + 26))
                    continue;
                var cols = PlanetColours[rng.Next(PlanetColours.Length)];
                _bodies.Add(new Body
                {
                    Kind = Kind.Planet, Pos = p, R = r, Mass = r * r, Color = cols[0], Color2 = cols[1], Style = rng.Next(3),
                });
                break;
            }
        }

        if (Level >= 3)
        {
            int moons = Level >= 7 ? 2 : 1;
            for (int m = 0; m < moons; m++)
            {
                int parent = 1 + rng.Next(Math.Max(1, _bodies.Count - 1));
                if (parent >= _bodies.Count || _bodies[parent].Kind != Kind.Planet)
                    continue;
                var pb = _bodies[parent];
                float orbit = pb.R + R(22, 34);
                if (!FreeOrbit(pb.Pos, orbit + 8, parent))
                    continue;
                _bodies.Add(new Body
                {
                    Kind = Kind.Moon, R = 6, Mass = 36 * 3, Color = Pal.LightGrey, Color2 = Pal.Grey, Parent = parent,
                    OrbitR = orbit, Phase = R(0, MathF2.Tau) * (m == 1 ? -1 : 1),
                });
                _hasMoons = true;
            }
        }
        if (Level >= 4)
        {
            int stars = Level >= 9 ? 2 : 1;
            for (int s = 0; s < stars; s++)
                for (int tries = 0; tries < 30; tries++)
                {
                    var p = new Vector2(R(160, 500), R(50, 340));
                    if (!Free(p, 40))
                        continue;
                    _bodies.Add(new Body { Kind = Kind.Repulsor, Pos = p, R = 9, Mass = -1100, Color = Pal.Orange, Color2 = Pal.Yellow });
                    break;
                }
        }
        if (Level >= 6)
        {
            for (int tries = 0; tries < 30; tries++)
            {
                var p = new Vector2(R(180, 480), R(50, 340));
                if (!Free(p, 55))
                    continue;
                _bodies.Add(new Body { Kind = Kind.BlackHole, Pos = p, R = 7, Mass = 2600 + Level * 60, Color = Pal.Purple, Color2 = Pal.Magenta });
                break;
            }
        }
    }

    private bool Free(Vector2 p, float clearance)
    {
        if (Vector2.Distance(p, _home) < clearance + HomeR + 30)
            return false;
        if (p.Y - clearance * 0.5f < Screen.HudHeight + 6 || p.Y + clearance * 0.5f > Screen.Height - 6)
            return false;
        foreach (var b in _bodies)
        {
            float extra = b.Kind == Kind.Planet ? 40 : 0; // room for a possible moon
            if (Vector2.Distance(p, b.Pos) < clearance + b.R + extra)
                return false;
        }
        return true;
    }

    private bool FreeOrbit(Vector2 centre, float radius, int parent)
    {
        if (Vector2.Distance(centre, _home) < radius + HomeR + 20)
            return false;
        for (int i = 0; i < _bodies.Count; i++)
        {
            if (i == parent)
                continue;
            if (Vector2.Distance(centre, _bodies[i].Pos) < radius + _bodies[i].R + 8)
                return false;
        }
        return true;
    }

    private int Solve()
    {
        _solutions.Clear();
        const int angles = 84, powers = 12;
        for (int a = 0; a < angles; a++)
        {
            float angle = -1.9f + 3.8f * a / (angles - 1);
            for (int p = 0; p < powers; p++)
            {
                float power = 0.3f + 0.7f * p / (powers - 1);
                if (Simulate(angle, power, _genClock, MaxFlightSteps, null) == Outcome.Target)
                    _solutions.Add((angle, power));
            }
        }
        return _solutions.Count;
    }

    private void PlaceDust()
    {
        _dust.Clear();
        if (_solutions.Count > 0)
        {
            var sol = _solutions[_solutions.Count / 2];
            _scratchPath.Clear();
            Simulate(sol.Angle, sol.Power, _genClock, MaxFlightSteps, _scratchPath);
            int n = Math.Min(5, _scratchPath.Count / 12);
            for (int i = 1; i <= n; i++)
            {
                var p = _scratchPath[_scratchPath.Count * i / (n + 2)];
                if (p.Y > Screen.HudHeight + 8)
                    _dust.Add(new Dust { Pos = p, Twinkle = Rand(0, 6) });
            }
        }
        for (int i = 0; i < 3; i++)
            for (int tries = 0; tries < 20; tries++)
            {
                var p = new Vector2(Rand(130, 520), Rand(50, 340));
                bool ok = true;
                foreach (var b in _bodies)
                    if (Vector2.Distance(p, BodyPos(b, _clock * Dt)) < b.R + 18)
                        ok = false;
                if (!ok)
                    continue;
                _dust.Add(new Dust { Pos = p, Twinkle = Rand(0, 6) });
                break;
            }
    }

    // ------------------------------------------------------------------ physics

    private Vector2 BodyPos(Body b, float t)
    {
        if (b.Kind != Kind.Moon)
            return b.Pos;
        return _bodies[b.Parent].Pos + MathF2.FromAngle(b.Phase + MoonOmega * t, b.OrbitR);
    }

    private Vector2 LaunchPos(float angle) => _home + MathF2.FromAngle(angle, HomeR + 4);

    private Outcome StepProbe(ref Vector2 pos, ref Vector2 vel, float t)
    {
        var acc = Vector2.Zero;
        for (int i = 0; i < _bodies.Count; i++)
        {
            var b = _bodies[i];
            var bp = BodyPos(b, t);
            var d = bp - pos;
            float d2 = d.LengthSquared();
            float hit = b.Kind == Kind.BlackHole ? b.R + 3 : b.R + 2;
            if (d2 < hit * hit)
                return b.Kind == Kind.Target ? Outcome.Target : b.Kind == Kind.BlackHole ? Outcome.Swallowed : Outcome.Crash;
            float inv = G * b.Mass / ((d2 + 120) * MathF.Sqrt(d2 + 0.01f));
            acc += d * inv;
        }
        vel += acc * SubDt;
        pos += vel * SubDt;
        if (Vector2.DistanceSquared(pos, _home) < HomeR * HomeR)
            return Outcome.Crash;
        if (pos.X < -40 || pos.X > Screen.Width + 40 || pos.Y < -30 || pos.Y > Screen.Height + 40)
            return Outcome.Lost;
        return Outcome.Flying;
    }

    private Outcome Simulate(float angle, float power, int clock, int steps, List<Vector2> path)
    {
        var pos = LaunchPos(angle);
        var vel = MathF2.FromAngle(angle, power * MaxSpeed);
        float t0 = clock * Dt;
        for (int s = 0; s < steps; s++)
        {
            var o = StepProbe(ref pos, ref vel, t0 + s * SubDt);
            path?.Add(pos);
            if (o != Outcome.Flying)
                return o;
        }
        return Outcome.Flying;
    }

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        _clock++;
        for (int i = 0; i < _dust.Count; i++)
        {
            var d = _dust[i];
            d.Twinkle += Dt * 4;
            _dust[i] = d;
        }

        switch (_state)
        {
            case State.Aiming:
                UpdateAiming();
                break;
            case State.Flying:
                for (int s = 0; s < 2 && _state == State.Flying; s++)
                    StepFlight();
                break;
            case State.Result:
                _stateTimer -= Dt;
                if (_stateTimer <= 0)
                {
                    if (Lives <= 0)
                    {
                        EndGame(false, "Out of probes on level " + Level + ".");
                        return;
                    }
                    _state = State.Aiming;
                    _trail.Clear();
                }
                break;
            case State.Cleared:
                _stateTimer -= Dt;
                if (_stateTimer <= 0)
                {
                    if (Level >= LastLevel)
                    {
                        EndGame(true, "All " + LastLevel + " systems explored!");
                        return;
                    }
                    Level++;
                    BuildLevel();
                }
                break;
        }
    }

    private void UpdateAiming()
    {
        // Keyboard aiming.
        if (In.Left || In.Right)
            _aimAngle += (In.Right ? 1 : -1) * 1.1f * Dt * (In.Fire ? 0.3f : 1);
        if (In.Up || In.Down)
            _aimPower = MathF2.Clamp(_aimPower + (In.Up ? 1 : -1) * 0.5f * Dt, 0.15f, 1);
        if (In.LeftPressed || In.RightPressed || In.UpPressed || In.DownPressed)
            Sound.Play(Sfx.Tick, 0.3f, 0.3f);

        // Slingshot drag (anywhere on the field).
        if (In.PointerPressed && In.Pointer.Y > Screen.HudHeight)
        {
            _dragging = true;
            _dragStart = In.Pointer;
            Sound.Play(Sfx.Select, 0, 0.5f);
        }
        if (_dragging)
        {
            var pull = _dragStart - In.Pointer;
            if (pull.Length() > 4)
            {
                float newPower = MathF2.Clamp(pull.Length() / PullLength, 0.15f, 1);
                if ((int)(newPower * 10) != (int)(_aimPower * 10))
                    Sound.Play(Sfx.Tick, newPower - 0.5f, 0.25f);
                _aimAngle = MathF2.Angle(pull);
                _aimPower = newPower;
            }
            if (In.PointerReleased || !In.PointerDown)
            {
                _dragging = false;
                if (pull.Length() > 12)
                    Launch();
                return;
            }
        }
        if (!_dragging && (In.FirePressed || In.EnterPressed))
            Launch();
    }

    private void Launch()
    {
        _state = State.Flying;
        _launchClock = _clock;
        _flightSteps = 0;
        _probePos = LaunchPos(_aimAngle);
        _probeVel = MathF2.FromAngle(_aimAngle, _aimPower * MaxSpeed);
        _trail.Clear();
        Sound.Play(Sfx.Whoosh, _aimPower - 0.5f, 0.8f);
        Sound.Play(Sfx.Cannon, 0.3f, 0.4f);
        Fx.Burst(_probePos.X, _probePos.Y, Pal.Sky, 14, 90, 0.4f, 1.8f);
    }

    private void StepFlight()
    {
        var o = StepProbe(ref _probePos, ref _probeVel, _launchClock * Dt + _flightSteps * SubDt);
        _flightSteps++;
        if (_flightSteps % 2 == 0)
        {
            _trail.Add(_probePos);
            if (_trail.Count > 400)
                _trail.RemoveAt(0);
        }
        if (_flightSteps % 6 == 0)
            Fx.Spark(_probePos.X, _probePos.Y, -_probeVel.X * 0.15f, -_probeVel.Y * 0.15f, Pal.Sky, 0.5f, 1.4f);

        for (int i = 0; i < _dust.Count; i++)
        {
            var d = _dust[i];
            if (!d.Taken && Vector2.DistanceSquared(d.Pos, _probePos) < 12 * 12)
            {
                d.Taken = true;
                _dust[i] = d;
                AddScore(25, d.Pos.X, d.Pos.Y - 8, Pal.Yellow);
                Fx.Burst(d.Pos.X, d.Pos.Y, Pal.Yellow, 14, 70, 0.5f, 1.6f);
                Sound.Play(Sfx.Coin, Rand(0, 0.5f), 0.6f);
            }
        }

        if (o == Outcome.Flying && _flightSteps >= MaxFlightSteps)
            o = Outcome.Lost;
        if (o == Outcome.Flying)
            return;

        if (o == Outcome.Target)
        {
            var t = _bodies[_target];
            int landing = 300 + 100 * Level;
            AddScore(landing, t.Pos.X, t.Pos.Y - 30, Pal.Lime);
            int bonus = (Lives - 1) * 150;
            if (bonus > 0)
                AddScore(bonus, t.Pos.X, t.Pos.Y - 12, Pal.Cyan);
            Fx.Burst(t.Pos.X, t.Pos.Y, Pal.Lime, 40, 160, 0.9f, 2.5f);
            Fx.Burst(t.Pos.X, t.Pos.Y, Pal.White, 16, 90, 0.5f, 2f);
            Sound.Play(Sfx.LevelUp);
            _state = State.Cleared;
            _stateTimer = 2.2f;
            _resultText = "TOUCHDOWN!";
            _resultColour = Pal.Lime;
            return;
        }

        Lives--;
        _state = State.Result;
        _stateTimer = 1.6f;
        switch (o)
        {
            case Outcome.Crash:
                Fx.Explode(_probePos.X, _probePos.Y, 0.8f);
                Sound.Play(Sfx.Explode);
                _resultText = "CRASHED!";
                _resultColour = Pal.Orange;
                break;
            case Outcome.Swallowed:
                Fx.Burst(_probePos.X, _probePos.Y, Pal.Magenta, 30, 60, 0.8f, 2f);
                Sound.Play(Sfx.Warp, -0.8f);
                _resultText = "SWALLOWED!";
                _resultColour = Pal.Magenta;
                break;
            default:
                Sound.Play(Sfx.Lose, 0, 0.7f);
                _resultText = "LOST IN SPACE";
                _resultColour = Pal.Grey;
                break;
        }
        if (Lives > 0)
            Sound.Play(Sfx.Hurt, 0, 0.4f);
    }

    // ------------------------------------------------------------------ drawing

    public override void Draw(Gfx g)
    {
        Backdrops.Space(g, Time, 0, 31, Screen.Bounds);
        g.Glow(420, 120, 260, Pal.Purple, 0.13f);
        g.Glow(180, 300, 220, Pal.Blue, 0.1f);

        float t = _clock * Dt;

        // Gravity wells: faint rings.
        foreach (var b in _bodies)
        {
            if (b.Kind == Kind.Target)
                continue;
            var p = BodyPos(b, t);
            var col = b.Kind == Kind.Repulsor ? Pal.Orange : b.Kind == Kind.BlackHole ? Pal.Magenta : Pal.Sky;
            float reach = MathF.Sqrt(MathF.Abs(b.Mass)) * 2.2f;
            for (int k = 1; k <= 3; k++)
            {
                float ph = Backdrops.Mod(t * 0.25f * (b.Kind == Kind.Repulsor ? 1 : -1) + k / 3f, 1f);
                float rr = b.R + ph * reach;
                g.Ring(p.X, p.Y, rr, 1f, col * (0.12f * (1 - ph) * (b.Kind == Kind.Moon ? 0.5f : 1)));
            }
        }

        // Moon orbits.
        foreach (var b in _bodies)
            if (b.Kind == Kind.Moon)
            {
                var c = _bodies[b.Parent].Pos;
                g.Ring(c.X, c.Y, b.OrbitR, 1f, Pal.White * 0.08f, 48);
            }

        // Dust.
        foreach (var d in _dust)
            if (!d.Taken)
                DrawDust(g, d.Pos, d.Twinkle, 1f);

        foreach (var b in _bodies)
            DrawBody(g, b, BodyPos(b, t), t, 1f);

        DrawHome(g, _home, 1f, t);

        // Trail.
        for (int i = 1; i < _trail.Count; i++)
        {
            float a = (float)i / _trail.Count;
            g.Line(_trail[i - 1], _trail[i], 1.5f, Pal.Sky * (0.15f + 0.5f * a));
        }

        if (_state == State.Aiming)
            DrawAim(g);

        if (_state == State.Flying)
        {
            g.Glow(_probePos, 14, Pal.Sky, 0.8f);
            g.Circle(_probePos, 2.5f, Pal.White);
        }

        if (_state is State.Result or State.Cleared)
        {
            float a = MathF.Min(1, _stateTimer * 2);
            g.RoundRect(320 - 130, 140, 260, _state == State.Result && Lives > 0 ? 64 : 44, 10, Color.Black * (0.55f * a));
            g.TextShadow(_resultText, 320, 150, 3f, _resultColour * a, Align.Center);
            if (_state == State.Result && Lives > 0)
                g.Text(Lives + (Lives == 1 ? " PROBE LEFT" : " PROBES LEFT"), 320, 182, 1.5f, Pal.White * a, Align.Center);
        }
        if (_state == State.Aiming && Lives == 3)
        {
            g.TextShadow("LEVEL " + Level, 320, 30, 2f, Pal.Sky * 0.9f, Align.Center);
            if (Level == 1)
                g.Text(IsTouch ? "DRAG BACK AND LET GO" : "DRAG BACK, OR ARROWS + SPACE", 320, 342, 1.25f, Pal.LightGrey * 0.8f, Align.Center);
        }
    }

    private void DrawAim(Gfx g)
    {
        var dir = MathF2.FromAngle(_aimAngle);
        var start = LaunchPos(_aimAngle);

        // Elastic band behind the planet.
        var back = _home - dir * (HomeR + 6 + _aimPower * 34);
        var n = new Vector2(-dir.Y, dir.X);
        var bandCol = Pal.Lerp(Pal.Lime, Pal.Red, _aimPower);
        g.Line(_home + n * (HomeR + 3), back, 1.5f, bandCol * 0.8f);
        g.Line(_home - n * (HomeR + 3), back, 1.5f, bandCol * 0.8f);
        g.Glow(back, 8, bandCol, 0.6f);
        g.Circle(back, 3, bandCol);

        // Power arc around the home world.
        g.Arc(_home.X, _home.Y, HomeR + 9, 3, -MathF.PI / 2, -MathF.PI / 2 + MathF2.Tau * _aimPower, bandCol * 0.8f);

        // Predicted path: dotted, fading, first part only.
        _predict.Clear();
        int steps = (int)MathF2.Lerp(150, 55, MathF2.Clamp((Level - 1) / 12f, 0, 1));
        Simulate(_aimAngle, _aimPower, _clock, steps, _predict);
        for (int i = 3; i < _predict.Count; i += 5)
        {
            float a = 1 - (float)i / steps;
            g.Circle(_predict[i], 1.6f, Pal.White * (0.25f + 0.7f * a));
        }
        g.Glow(start, 10, Pal.Sky, 0.7f);
        g.Circle(start, 2.5f, Pal.White);
    }

    private static void DrawDust(Gfx g, Vector2 p, float tw, float s)
    {
        float k = 0.6f + 0.4f * MathF.Sin(tw);
        g.Glow(p, 10 * s, Pal.Yellow, 0.5f * k);
        float len = (3 + 2.5f * k) * s;
        g.Line(p.X - len, p.Y, p.X + len, p.Y, 1.1f * s, Pal.Gold * k);
        g.Line(p.X, p.Y - len, p.X, p.Y + len, 1.1f * s, Pal.Gold * k);
        g.Circle(p, 1.5f * s, Pal.White);
    }

    private static void DrawBody(Gfx g, Body b, Vector2 p, float t, float s)
    {
        float r = b.R * s;
        switch (b.Kind)
        {
            case Kind.Target:
            {
                float pulse = MathF2.Pulse(t, 1.4f);
                g.Glow(p, r * 3.2f, Pal.Lime, 0.35f + 0.2f * pulse);
                g.Circle(p, r, Pal.Forest);
                g.Circle(p.X - r * 0.12f, p.Y - r * 0.12f, r * 0.86f, new Color(60, 190, 90));
                // Continents.
                g.Ellipse(p.X - r * 0.35f, p.Y - r * 0.2f, r * 0.3f, r * 0.22f, new Color(150, 240, 120));
                g.Ellipse(p.X + r * 0.3f, p.Y + r * 0.25f, r * 0.25f, r * 0.18f, new Color(150, 240, 120));
                g.Circle(p.X - r * 0.35f, p.Y - r * 0.4f, r * 0.25f, Color.White * 0.35f);
                g.Ring(p.X, p.Y, r + 5 * s + pulse * 6 * s, 1.5f * s, Pal.Lime * (0.8f * (1 - pulse)));
                break;
            }
            case Kind.Planet:
            {
                g.Glow(p, r * 1.9f, b.Color, 0.18f);
                if (b.Style == 2)
                    DrawRings(g, p, r, b, s, false);
                g.Circle(p, r, b.Color2);
                g.Circle(p.X - r * 0.1f, p.Y - r * 0.1f, r * 0.9f, b.Color);
                if (b.Style == 0)
                {
                    // Gas giant bands.
                    for (int k = -2; k <= 2; k++)
                    {
                        float y = p.Y + k * r * 0.32f;
                        float half = MathF.Sqrt(MathF.Max(0, 1 - (k * 0.32f) * (k * 0.32f))) * r * 0.85f;
                        g.Rect(p.X - half, y - r * 0.06f, half * 2, r * 0.12f, b.Color2 * 0.6f);
                    }
                }
                else if (b.Style == 1)
                {
                    // Craters.
                    g.Circle(p.X + r * 0.3f, p.Y - r * 0.25f, r * 0.2f, b.Color2 * 0.8f);
                    g.Circle(p.X - r * 0.35f, p.Y + r * 0.3f, r * 0.14f, b.Color2 * 0.8f);
                    g.Circle(p.X + r * 0.1f, p.Y + r * 0.45f, r * 0.1f, b.Color2 * 0.8f);
                }
                // Light from the upper left, shadow lower right.
                g.Circle(p.X - r * 0.38f, p.Y - r * 0.38f, r * 0.32f, Color.White * 0.22f);
                g.Arc(p.X, p.Y, r * 0.9f, r * 0.2f, 0.1f, 1.5f, Color.Black * 0.25f);
                if (b.Style == 2)
                    DrawRings(g, p, r, b, s, true);
                break;
            }
            case Kind.Moon:
                g.Glow(p, r * 2, Pal.White, 0.15f);
                g.Circle(p, r, Pal.Grey);
                g.Circle(p.X - r * 0.15f, p.Y - r * 0.15f, r * 0.82f, Pal.LightGrey);
                g.Circle(p.X + r * 0.25f, p.Y + r * 0.1f, r * 0.25f, Pal.Grey);
                break;
            case Kind.Repulsor:
            {
                float spin = t * 1.5f;
                g.Glow(p, r * 4.5f, Pal.Orange, 0.55f);
                for (int k = 0; k < 8; k++)
                {
                    float a = spin + MathF2.Tau * k / 8;
                    float len = r * (k % 2 == 0 ? 2.3f : 1.6f) * (0.85f + 0.15f * MathF.Sin(t * 6 + k));
                    g.Line(p, p + MathF2.FromAngle(a, len), 2f * s, Pal.Yellow * 0.8f);
                }
                g.Circle(p, r, Pal.Orange);
                g.Circle(p, r * 0.65f, Pal.Yellow);
                g.Circle(p, r * 0.35f, Pal.White);
                break;
            }
            case Kind.BlackHole:
            {
                g.Glow(p, r * 6, Pal.Purple, 0.5f);
                for (int k = 0; k < 3; k++)
                {
                    float a = -t * (2 + k) + k * 2;
                    g.Arc(p.X, p.Y, r * (1.6f + k * 0.55f), 1.6f * s, a, a + 2.2f, Pal.Lerp(Pal.Magenta, Pal.Orange, k / 2f) * 0.8f);
                }
                g.Circle(p, r * 1.15f, Pal.Magenta * 0.6f);
                g.Circle(p, r, Color.Black);
                break;
            }
        }
    }

    private static void DrawRings(Gfx g, Vector2 p, float r, Body b, float s, bool front)
    {
        for (int k = 0; k < 24; k++)
        {
            float a0 = MathF2.Tau * k / 24, a1 = MathF2.Tau * (k + 1) / 24;
            if ((MathF.Sin(a0 + 0.13f) > 0) != front)
                continue;
            var q0 = new Vector2(p.X + MathF.Cos(a0) * r * 1.7f, p.Y + MathF.Sin(a0) * r * 0.45f);
            var q1 = new Vector2(p.X + MathF.Cos(a1) * r * 1.7f, p.Y + MathF.Sin(a1) * r * 0.45f);
            g.Line(q0, q1, 2.2f * s, front ? b.Color : Pal.Darken(b.Color, 0.4f));
            g.Line(q0 * 1f + new Vector2(0, 2.2f * s), q1 + new Vector2(0, 2.2f * s), 1f * s, b.Color2 * 0.8f);
        }
    }

    private static void DrawHome(Gfx g, Vector2 p, float s, float t)
    {
        float r = HomeR * s;
        g.Glow(p, r * 2.6f, Pal.Sky, 0.35f);
        g.Circle(p, r, Pal.DeepWater);
        g.Circle(p.X - r * 0.1f, p.Y - r * 0.1f, r * 0.9f, Pal.Water);
        g.Ellipse(p.X - r * 0.3f, p.Y + r * 0.1f, r * 0.35f, r * 0.45f, Pal.Grass);
        g.Ellipse(p.X + r * 0.35f, p.Y - r * 0.35f, r * 0.3f, r * 0.2f, Pal.Grass);
        // Drifting clouds.
        float cx = MathF.Sin(t * 0.5f) * r * 0.3f;
        g.Ellipse(p.X + cx, p.Y + r * 0.45f, r * 0.4f, r * 0.13f, Color.White * 0.45f);
        g.Ellipse(p.X - cx * 0.7f + r * 0.2f, p.Y - r * 0.05f, r * 0.3f, r * 0.1f, Color.White * 0.35f);
        g.Circle(p.X - r * 0.38f, p.Y - r * 0.4f, r * 0.28f, Color.White * 0.25f);
    }

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        Backdrops.Space(g, time, 0, 31, r);
        float s = r.H / 70f;
        g.Glow(r.CenterX, r.CenterY, r.W * 0.5f, Pal.Purple, 0.2f);
        var home = new Vector2(r.X + 16 * s, r.Bottom - 16 * s);
        var planet = new Vector2(r.CenterX, r.CenterY + 2 * s);
        var target = new Vector2(r.Right - 18 * s, r.Y + 18 * s);

        // A curved dotted path from home around the planet to the target.
        var c1 = new Vector2(r.CenterX - 10 * s, r.Bottom + 8 * s);
        var c2 = new Vector2(r.CenterX + 30 * s, r.Bottom - 6 * s);
        float prog = Backdrops.Mod(time * 0.35f, 1.25f);
        Vector2 probe = home;
        for (int i = 0; i <= 40; i++)
        {
            float u = i / 40f;
            var q = Bezier(home, c1, c2, target, u);
            if (u <= prog)
            {
                g.Circle(q, 1.1f * s, Pal.White * (0.35f + 0.5f * u));
                probe = q;
            }
        }
        g.Glow(r.X + r.W * 0.3f, r.Y + r.H * 0.3f, 8 * s, Pal.Yellow, 0.5f);
        DrawDust(g, new Vector2(r.X + r.W * 0.3f, r.Y + r.H * 0.28f), time * 4, s * 0.9f);
        DrawDust(g, new Vector2(r.X + r.W * 0.72f, r.Bottom - 12 * s), time * 4 + 2, s * 0.9f);

        DrawBody(g, new Body { Kind = Kind.Planet, R = 12, Color = PlanetColours[0][0], Color2 = PlanetColours[0][1], Style = 2 }, planet, time, s);
        var moon = planet + MathF2.FromAngle(time * 0.8f, 26 * s);
        DrawBody(g, new Body { Kind = Kind.Moon, R = 4.5f }, moon, time, s);
        DrawBody(g, new Body { Kind = Kind.Target, R = 9 }, target, time, s);
        DrawHome(g, home, 0.6f * s, time);
        if (prog <= 1)
        {
            g.Glow(probe, 9 * s, Pal.Sky, 0.9f);
            g.Circle(probe, 1.8f * s, Pal.White);
        }
    }

    private static Vector2 Bezier(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
    {
        float u = 1 - t;
        return u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d;
    }

    // ------------------------------------------------------------------ autopilot

    public override void AutoPlay(Controls c)
    {
        if (_state != State.Aiming)
        {
            _autoPlanned = false;
            return;
        }
        const int dragTicks = 50;
        if (!_autoPlanned)
        {
            _autoPlanned = true;
            _autoTicks = 0;
            // Mostly a known solution, sometimes a near miss.
            if (_solutions.Count > 0)
            {
                var sol = _solutions[(_solutions.Count / 2 + (3 - Lives) * 3) % _solutions.Count];
                _autoAngle = sol.Angle;
                _autoPower = sol.Power;
                if (Lives == 3 && Rng.Next(3) == 0)
                    _autoAngle += Rand(-0.08f, 0.08f);
            }
            else
            {
                _autoAngle = MathF2.Angle(_bodies[_target].Pos - _home);
                _autoPower = 0.6f;
            }
        }
        // Release so that launch happens in the same moon phase as the solver used.
        int phase = (_clock + 1 - _genClock) % MoonPeriodTicks;
        _autoTicks++;
        var start = _home;
        var end = start - MathF2.FromAngle(_autoAngle, _autoPower * PullLength);
        int releaseAt = _hasMoons ? MoonPeriodTicks : dragTicks + 10;
        int ticksToRelease = _hasMoons ? (releaseAt - phase) % MoonPeriodTicks : releaseAt - _autoTicks;
        if (!_dragging)
        {
            if (ticksToRelease <= dragTicks && ticksToRelease > 0)
            {
                c.Pointer = start;
                c.PointerPressed = c.PointerDown = true;
            }
            return;
        }
        float u = MathF2.Clamp(1 - (ticksToRelease - 1) / (float)(dragTicks - 5), 0, 1);
        c.Pointer = Vector2.Lerp(start, end, MathF2.EaseOut(u));
        c.PointerDown = true;
        if (ticksToRelease <= 0)
        {
            c.Pointer = end;
            c.PointerDown = false;
            c.PointerReleased = true;
        }
    }
}
