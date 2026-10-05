using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 46 Tanker: pilot a supertanker with enormous inertia down a winding channel, past buoys, rocks
/// and other shipping, and ease it into its berth before time runs out. Scrape too hard and you
/// spill oil: three spills and you are off the job.
/// </summary>
public sealed class Tanker : MiniGame
{
    public override int Number => 46;
    public override string Title => "Tanker";
    public override Category Category => Category.Skill;
    public override string Tagline => "Steer a mighty oil tanker into port. It turns like a continent.";
    public override Color Accent => Pal.Orange;
    public override Pad Pad => Pad.Stick;

    public override string[] HowToPlay =>
    [
        "Bring the tanker down the channel and stop it in the berth at the end. It is slow to speed up, slow to stop and slow to turn: plan ahead.",
        "Hard knocks spill oil: 3 spills and you are fired. Gentle bumps are free.",
        "Dock slowly and straight for the best score. 4 ports.",
    ];

    public override string[] DesktopControls => ["UP / DOWN: set the engine telegraph.", "LEFT / RIGHT: rudder (centres on release)."];
    public override string[] TouchControls => ["Stick up / down: engine telegraph.", "Stick left / right: rudder."];

    // ------------------------------------------------------------------ level data

    private sealed class Port
    {
        public string Name;
        public float Width, Wiggle, Current, Wind, Gust, Time;
        public int Segments, Rocks, Traffic;
        public bool Night;
        public Color Land, Shore, Shallow, Deep;
    }

    private static readonly Port[] Ports =
    [
        new() { Name = "OPEN SEA", Width = 150, Wiggle = 0.35f, Segments = 14, Rocks = 6, Traffic = 1, Wind = 4, Time = 150,
            Land = new Color(80, 200, 200), Shore = new Color(235, 220, 160), Shallow = new Color(40, 150, 190), Deep = new Color(16, 70, 140) },
        new() { Name = "RIVER ESTUARY", Width = 105, Wiggle = 0.5f, Segments = 16, Rocks = 8, Traffic = 2, Current = 9, Wind = 5, Time = 170,
            Land = new Color(70, 110, 50), Shore = new Color(150, 130, 90), Shallow = new Color(60, 120, 120), Deep = new Color(30, 80, 100) },
        new() { Name = "NARROW CANAL", Width = 62, Wiggle = 0.28f, Segments = 16, Rocks = 3, Traffic = 1, Wind = 3, Time = 190,
            Land = new Color(60, 130, 60), Shore = new Color(150, 150, 160), Shallow = new Color(40, 100, 110), Deep = new Color(24, 74, 96) },
        new() { Name = "STORMY NIGHT", Width = 95, Wiggle = 0.45f, Segments = 16, Rocks = 10, Traffic = 2, Wind = 10, Gust = 10, Time = 190,
            Night = true, Land = new Color(50, 56, 60), Shore = new Color(90, 90, 96), Shallow = new Color(30, 70, 100), Deep = new Color(12, 40, 72) },
    ];

    private const float ShipLen = 96, ShipHalfW = 10, MaxSpeed = 58, SegLen = 140, SlipHalf = 26, SlipLen = 140;

    private readonly List<Vector2> _pts = new();
    private readonly List<float> _wid = new();
    private readonly List<Vector2> _nrm = new();
    private readonly List<Vector3> _rocks = new();
    private readonly List<Vector3> _buoys = new(); // z: +1 green (right), -1 red (left)
    private readonly List<Vector3> _slicks = new(); // z: radius
    private readonly List<Vector4> _wake = new(); // x, y, age, side

    private sealed class Boat
    {
        public float S, Speed, Side;
        public Vector2 Pos;
        public float Heading;
        public Color Colour;
        public bool Tug;
    }

    private readonly List<Boat> _boats = new();
    private float _pathLen;
    private readonly List<float> _cum = new();

    // Tanker state.
    private Vector2 _pos, _drift, _cam;
    private float _heading, _speed, _angVel, _throttle, _rudder, _timeLeft, _grace, _dockHold, _banner, _lightning;
    private float _windAngle, _gust;
    private bool _docked;
    private float _dockedTimer;
    private string _dockMessage;
    private int _port;
    private float _autoStuck, _autoReverse;

    protected override void Start()
    {
        Lives = 3;
        _port = 0;
        BuildPort();
    }

    private Port P => Ports[_port];

    private void BuildPort()
    {
        Level = _port + 1;
        Status = P.Name;
        _pts.Clear();
        _wid.Clear();
        _rocks.Clear();
        _buoys.Clear();
        _slicks.Clear();
        _wake.Clear();
        _boats.Clear();

        var p = Vector2.Zero;
        float ang = -MathF.PI / 2, turn = 0;
        _pts.Add(p);
        _wid.Add(P.Width * 1.3f);
        for (int i = 0; i < P.Segments; i++)
        {
            turn = MathF2.Clamp(turn + Rand(-P.Wiggle, P.Wiggle) * 0.6f, -P.Wiggle, P.Wiggle);
            ang = MathF2.Clamp(ang + turn * (i < 2 ? 0.2f : 1), -MathF.PI / 2 - 1.1f, -MathF.PI / 2 + 1.1f);
            p += MathF2.FromAngle(ang, SegLen);
            _pts.Add(p);
            _wid.Add(P.Width * Rand(0.85f, 1.15f));
        }
        // Harbour basin, then the berth.
        var dir = MathF2.FromAngle(ang);
        p += dir * SegLen;
        _pts.Add(p);
        _wid.Add(P.Width * 1.25f + 30);
        p += dir * (P.Width * 1.25f + 20);
        _pts.Add(p);
        _wid.Add(SlipHalf);
        p += dir * SlipLen;
        _pts.Add(p);
        _wid.Add(SlipHalf);

        _nrm.Clear();
        _cum.Clear();
        _pathLen = 0;
        for (int i = 0; i < _pts.Count; i++)
        {
            var a = i > 0 ? _pts[i] - _pts[i - 1] : _pts[1] - _pts[0];
            var b = i < _pts.Count - 1 ? _pts[i + 1] - _pts[i] : a;
            a.Normalize();
            b.Normalize();
            var t = a + b;
            t.Normalize();
            _nrm.Add(new Vector2(-t.Y, t.X));
            if (i > 0)
                _pathLen += Vector2.Distance(_pts[i], _pts[i - 1]);
            _cum.Add(_pathLen);
        }

        int last = _pts.Count - 4;
        for (int i = 0; i < P.Rocks; i++)
        {
            int seg = RandInt(2, last);
            float t = Rand(0, 1);
            var c = Vector2.Lerp(_pts[seg], _pts[seg + 1], t);
            float w = MathF2.Lerp(_wid[seg], _wid[seg + 1], t);
            float side = Chance(0.5f) ? 1 : -1;
            var n = Vector2.Lerp(_nrm[seg], _nrm[seg + 1], t);
            n.Normalize();
            _rocks.Add(new Vector3(c + n * side * w * Rand(0.55f, 0.8f), Rand(8, 15)));
        }
        for (int i = 1; i < _pts.Count - 3; i += 2)
        {
            var n = _nrm[i];
            _buoys.Add(new Vector3(_pts[i] + n * _wid[i] * 0.86f, 1));
            _buoys.Add(new Vector3(_pts[i] - n * _wid[i] * 0.86f, -1));
        }
        for (int i = 0; i < P.Traffic; i++)
            _boats.Add(new Boat
            {
                S = _pathLen * (0.4f + 0.35f * i / Math.Max(1, P.Traffic)), Speed = Rand(18, 28), Side = 0.45f,
                Colour = Pick(new Color(40, 90, 170), new Color(200, 60, 50), new Color(230, 170, 40)), Tug = i % 2 == 1,
            });

        _pos = _pts[0] + (_pts[1] - _pts[0]) * 0.3f;
        _heading = MathF2.Angle(_pts[1] - _pts[0]);
        _speed = 18;
        _drift = Vector2.Zero;
        _angVel = 0;
        _throttle = 0.5f;
        _rudder = 0;
        _cam = _pos;
        _timeLeft = P.Time;
        _grace = 2;
        _docked = false;
        _dockHold = 0;
        _banner = 2.5f;
        _windAngle = Rand(0, MathF2.Tau);
    }

    // ------------------------------------------------------------------ geometry helpers

    /// <summary>Distance from the channel centreline (negative = inside by that much) and the nearest point.</summary>
    private float Clearance(Vector2 p, out Vector2 nearest, out int seg)
    {
        float best = float.MaxValue;
        nearest = p;
        seg = 0;
        for (int i = 0; i < _pts.Count - 1; i++)
        {
            var a = _pts[i];
            var ab = _pts[i + 1] - a;
            float t = MathF2.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0, 1);
            var q = a + ab * t;
            float d = Vector2.Distance(p, q) - MathF2.Lerp(_wid[i], _wid[i + 1], t);
            if (d < best)
            {
                best = d;
                nearest = q;
                seg = i;
            }
        }
        return best;
    }

    private Vector2 PathPoint(float s, out Vector2 dir)
    {
        s = MathF2.Clamp(s, 0, _pathLen);
        int i = 0;
        while (i < _cum.Count - 2 && _cum[i + 1] < s)
            i++;
        float len = _cum[i + 1] - _cum[i];
        float t = len > 0 ? (s - _cum[i]) / len : 0;
        dir = _pts[i + 1] - _pts[i];
        dir.Normalize();
        return Vector2.Lerp(_pts[i], _pts[i + 1], t);
    }

    private float Progress(Vector2 p)
    {
        Clearance(p, out var q, out int seg);
        return _cum[seg] + Vector2.Distance(_pts[seg], q);
    }

    private Vector2 Forward => MathF2.FromAngle(_heading);

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        if (_banner > 0)
            _banner -= Dt;
        if (_grace > 0)
            _grace -= Dt;
        if (_lightning > 0)
            _lightning -= Dt;

        if (_docked)
        {
            _dockedTimer += Dt;
            _speed = MathF2.Approach(_speed, 0, Dt * 10);
            if (_dockedTimer > 3)
            {
                if (_port >= Ports.Length - 1)
                {
                    EndGame(true, "All four cargoes delivered!");
                    return;
                }
                _port++;
                Sound.Play(Sfx.LevelUp);
                BuildPort();
            }
            return;
        }

        _timeLeft -= Dt;
        if (_timeLeft <= 0)
        {
            Sound.Play(Sfx.Alarm);
            if (LoseLife())
                return;
            BuildPort();
            _dockMessage = "OUT OF TIME";
            return;
        }

        // Controls: the telegraph steps slowly; the rudder follows the stick, slowly.
        _throttle = MathF2.Clamp(_throttle - In.AxisY * 0.45f * Dt, -0.5f, 1f);
        float rudderCmd = MathF2.Clamp(In.AxisX, -1, 1);
        _rudder = MathF2.Approach(_rudder, rudderCmd, 0.7f * Dt);

        // Engine and hull.
        float targetSpeed = _throttle * MaxSpeed;
        _speed += (targetSpeed - _speed) * (targetSpeed > _speed ? 0.11f : 0.08f) * Dt;
        float turn = _rudder * (_speed + 6 * MathF.Sign(_throttle)) / MaxSpeed * 0.26f;
        _angVel += (turn - _angVel) * 0.55f * Dt;
        _heading = MathF2.WrapAngle(_heading + _angVel * Dt);

        // Current along the channel (against you) and wind.
        Clearance(_pos, out _, out int seg);
        var flow = _pts[Math.Max(0, seg)] - _pts[Math.Min(_pts.Count - 1, seg + 1)];
        flow.Normalize();
        bool inSlip = seg >= _pts.Count - 2;
        _gust = P.Gust > 0 ? MathF.Max(0, MathF.Sin(Time * 0.7f) * MathF.Sin(Time * 1.9f + 1)) * P.Gust : 0;
        if (P.Night && Chance(0.003f))
        {
            _lightning = 0.25f;
            Sound.Play(Sfx.Thud, -0.9f, 0.8f);
        }
        _windAngle += Rand(-0.2f, 0.2f) * Dt;
        var wind = MathF2.FromAngle(_windAngle, P.Wind + _gust);
        var env = (inSlip ? Vector2.Zero : flow * P.Current) + wind * (inSlip ? 0.3f : 1);
        _drift += (env - _drift) * 0.25f * Dt;

        var vel = Forward * _speed + _drift;
        _pos += vel * Dt;

        Sound.Loop(LoopSfx.Engine, true, -0.9f + MathF.Abs(_throttle) * 0.35f, 0.3f + MathF.Abs(_throttle) * 0.2f);
        if (P.Night || P.Wind > 6)
            Sound.Loop(LoopSfx.Wind, true, -0.3f + _gust * 0.05f, 0.25f + _gust * 0.02f);

        Collide(vel);
        UpdateBoats();
        UpdateWake();
        CheckDock();

        // Camera leads the ship.
        var target = _pos + Forward * MathF2.Clamp(_speed, -20, 60) * 1.4f;
        _cam = Vector2.Lerp(_cam, target, 1 - MathF.Exp(-2.5f * Dt));
    }

    private void Collide(Vector2 vel)
    {
        var f = Forward;
        for (int k = -2; k <= 2; k++)
        {
            var p = _pos + f * (k * (ShipLen / 2 - ShipHalfW) / 2);
            // Land and quays.
            float c = Clearance(p, out var near, out _);
            if (c > -ShipHalfW)
            {
                var push = near - p;
                if (push.LengthSquared() < 0.01f)
                    push = -f;
                push.Normalize();
                Impact(p, push, c + ShipHalfW, vel, "AGROUND");
            }
            // Rocks.
            foreach (var r in _rocks)
            {
                var rc = new Vector2(r.X, r.Y);
                float d = Vector2.Distance(p, rc);
                if (d < r.Z + ShipHalfW)
                {
                    var push = p - rc;
                    push.Normalize();
                    Impact(p, push, r.Z + ShipHalfW - d, vel, "ROCKS!");
                }
            }
            // Buoys: a clang and a nudge.
            for (int i = 0; i < _buoys.Count; i++)
            {
                var b = _buoys[i];
                var bc = new Vector2(b.X, b.Y);
                if (Vector2.Distance(p, bc) < ShipHalfW + 4)
                {
                    var away = bc - p;
                    away.Normalize();
                    _buoys[i] = new Vector3(bc + away * 6, b.Z);
                    Sound.Play(Sfx.Bell, -0.4f, 0.4f);
                }
            }
            // Other ships.
            foreach (var bt in _boats)
            {
                var bf = MathF2.FromAngle(bt.Heading);
                for (int j = -1; j <= 1; j++)
                {
                    var bp = bt.Pos + bf * j * (bt.Tug ? 6 : 18);
                    float d = Vector2.Distance(p, bp);
                    if (d < ShipHalfW + 10)
                    {
                        var push = p - bp;
                        if (push.LengthSquared() < 0.01f)
                            push = Vector2.UnitX;
                        push.Normalize();
                        Impact(p, push, ShipHalfW + 10 - d, vel - bf * bt.Speed, "COLLISION!");
                    }
                }
            }
        }
    }

    private void Impact(Vector2 at, Vector2 push, float depth, Vector2 vel, string what)
    {
        _pos += push * MathF.Min(depth, 6);
        float into = -Vector2.Dot(vel, push);
        if (into <= 0)
            return;
        // Lose the speed going into the obstacle and swing away from it.
        _speed *= MathF.Max(0.55f, 1 - into * 0.015f);
        _drift -= push * Vector2.Dot(_drift, push) * 0.5f;
        var arm = at - _pos;
        _angVel += (arm.X * push.Y - arm.Y * push.X) * 0.00025f * MathF.Min(into, 20);
        var sp = ToScreen(at);
        if (into > 14 && _grace <= 0)
        {
            _grace = 2.5f;
            _slicks.Add(new Vector3(at, 18));
            Fx.Burst(sp.X, sp.Y, new Color(40, 30, 40), 24, 60, 1.2f, 3.5f, 0, false);
            Fx.Burst(sp.X, sp.Y, Pal.Orange, 12, 80, 0.4f, 2f);
            Fx.Float("OIL SPILL!", sp.X, sp.Y - 20, Pal.Orange, 2f);
            Fx.Float(what, sp.X, sp.Y - 40, Pal.White);
            Sound.Play(Sfx.Crack, -0.6f);
            Sound.Play(Sfx.Alarm, 0, 0.5f);
            if (LoseLife())
                return;
            Sound.Play(Sfx.Hurt, -0.5f);
        }
        else if (into > 3 && Tick % 8 == 0)
        {
            Sound.Play(Sfx.Thud, -0.3f, MathF.Min(1, into / 12));
            Fx.Burst(sp.X, sp.Y, Pal.White, 4, 40, 0.4f, 1.5f, 0, false);
        }
    }

    private float SpawnS => _cum[_pts.Count - 3] - 60;

    private void UpdateBoats()
    {
        foreach (var b in _boats)
        {
            // Outbound traffic keeps to its own starboard side.
            // Give way to the tanker rather than ploughing into it.
            var ahead = b.Pos + MathF2.FromAngle(b.Heading, 34);
            bool blocked = false;
            for (int k = -2; k <= 2; k++)
                if (Vector2.Distance(ahead, _pos + Forward * (k * 20)) < 30)
                    blocked = true;
            if (!blocked)
            {
                b.S -= b.Speed * Dt;
                b.Side = MathF2.Approach(b.Side, 0.45f, Dt * 0.1f);
            }
            else
            {
                // Back off and edge over to starboard.
                b.S = MathF.Min(SpawnS, b.S + b.Speed * 0.4f * Dt);
                b.Side = MathF2.Approach(b.Side, 0.85f, Dt * 0.3f);
            }
            if (b.S < 40)
            {
                if (Progress(_pos) < SpawnS - 450)
                {
                    b.S = SpawnS;
                    b.Pos = Vector2.Zero;
                }
            }
            var c = PathPoint(b.S, out var dir);
            var n = new Vector2(-dir.Y, dir.X);
            Clearance(c, out _, out int seg);
            float w = _wid[Math.Min(seg, _wid.Count - 1)];
            var target = c - n * w * b.Side;
            b.Heading = MathF2.Angle(-dir);
            b.Pos = Vector2.Lerp(b.Pos == Vector2.Zero ? target : b.Pos, target, 0.1f);
        }
        for (int i = 0; i < _slicks.Count; i++)
        {
            var s = _slicks[i];
            if (s.Z < 46)
                _slicks[i] = new Vector3(s.X, s.Y, s.Z + Dt * 4);
        }
    }

    private void UpdateWake()
    {
        for (int i = _wake.Count - 1; i >= 0; i--)
        {
            var w = _wake[i];
            w.Z += Dt;
            if (w.Z > 3.5f)
                _wake.RemoveAt(i);
            else
                _wake[i] = w;
        }
        if (Tick % 6 == 0 && MathF.Abs(_speed) > 4)
        {
            var stern = _pos - Forward * ShipLen * 0.5f;
            _wake.Add(new Vector4(stern.X, stern.Y, 0, MathF.Abs(_speed)));
            var bow = _pos + Forward * ShipLen * 0.48f;
            _wake.Add(new Vector4(bow.X, bow.Y, 0, -MathF.Abs(_speed)));
        }
    }

    private void CheckDock()
    {
        int n = _pts.Count;
        var slipStart = _pts[n - 2];
        var slipEnd = _pts[n - 1];
        var axis = slipEnd - slipStart;
        axis.Normalize();
        float along = Vector2.Dot(_pos - slipStart, axis);
        float side = MathF.Abs(Vector2.Dot(_pos - slipStart, new Vector2(-axis.Y, axis.X)));
        float misalign = MathF.Abs(MathF2.WrapAngle(_heading - MathF2.Angle(axis)));
        bool inBerth = along > SlipLen - ShipLen / 2 - 34 && side < SlipHalf && misalign < 0.3f;
        if (inBerth && MathF.Abs(_speed) < 9)
        {
            _dockHold += Dt;
            if (_dockHold > 1.2f)
                Dock(misalign);
        }
        else
        {
            _dockHold = 0;
        }
    }

    private void Dock(float misalign)
    {
        _docked = true;
        _dockedTimer = 0;
        int time = (int)(_timeLeft * 8);
        int align = (int)(MathF2.Clamp(1 - misalign / 0.3f, 0, 1) * 400);
        int gentle = (int)(MathF2.Clamp(1 - MathF.Abs(_speed) / 9, 0, 1) * 300);
        int total = 500 * Level + time + align + gentle;
        AddScore(total, 320, 150, Pal.Gold);
        _dockMessage = "TIME " + time + "  ALIGN " + align + "  GENTLE " + gentle;
        Sound.Play(Sfx.Bonus);
        Sound.Play(Sfx.Bell, 0.3f);
        var sp = ToScreen(_pos);
        Fx.Burst(sp.X, sp.Y, Pal.Gold, 40, 160, 1f, 2.5f);
    }

    // ------------------------------------------------------------------ drawing

    private Vector2 ToScreen(Vector2 w) => w - _cam + new Vector2(320, 196);

    private float Dim => P.Night ? (_lightning > 0 ? 0.9f : 0.32f) : 1f;

    private Color D(Color c) => Pal.Lerp(Color.Black, c, Dim);

    private void Band(Gfx g, float extra, float scale, Color c, RectF view)
    {
        for (int i = 0; i < _pts.Count - 1; i++)
        {
            var a = _pts[i];
            var b = _pts[i + 1];
            float wa = _wid[i] * scale + extra, wb = _wid[i + 1] * scale + extra;
            float minX = MathF.Min(a.X, b.X) - MathF.Max(wa, wb), maxX = MathF.Max(a.X, b.X) + MathF.Max(wa, wb);
            float minY = MathF.Min(a.Y, b.Y) - MathF.Max(wa, wb), maxY = MathF.Max(a.Y, b.Y) + MathF.Max(wa, wb);
            if (maxX < view.X || minX > view.Right || maxY < view.Y || minY > view.Bottom)
                continue;
            var na = _nrm[i];
            var nb = _nrm[i + 1];
            // Use the segment's own normal at the slip ends so the quay is square.
            if (i >= _pts.Count - 3)
            {
                var d = b - a;
                d.Normalize();
                na = nb = new Vector2(-d.Y, d.X);
            }
            g.Triangle(a + na * wa, b + nb * wb, b - nb * wb, c);
            g.Triangle(a + na * wa, b - nb * wb, a - na * wa, c);
            if (i < _pts.Count - 3)
                g.Circle(b.X, b.Y, wb, c);
        }
    }

    public override void Draw(Gfx g)
    {
        var view = new RectF(_cam.X - 340, _cam.Y - 210, 680, 420);
        var saved = g.Offset;
        g.Offset = saved + new Vector2(320, 196) - _cam;
        var vis = g.Visible;
        var worldVis = new RectF(vis.X + _cam.X - 320, vis.Y + _cam.Y - 196, vis.W, vis.H);

        // Land, shore, shallows, deep channel.
        g.Rect(worldVis, D(P.Land));
        if (_port == 0)
        {
            // Open sea: sandy islets on the shoals.
            var rng = new Random(46);
            for (int i = 0; i < 60; i++)
            {
                float x = (float)rng.NextDouble() * 1800 - 900, y = -(float)rng.NextDouble() * (_pathLen + 400) + 200;
                g.Ellipse(x, y, 30 + (float)rng.NextDouble() * 60, 20 + (float)rng.NextDouble() * 30, D(new Color(230, 215, 160)));
                g.Ellipse(x + 6, y - 4, 16 + (float)rng.NextDouble() * 20, 10, D(new Color(60, 160, 70)));
            }
        }
        else
        {
            var rng = new Random(_port * 31);
            for (int i = 0; i < 90; i++)
            {
                float x = (float)rng.NextDouble() * 1800 - 900, y = -(float)rng.NextDouble() * (_pathLen + 400) + 200;
                var c = _port == 2 ? new Color(40, 100, 50) : _port == 1 ? new Color(90, 120, 60) : new Color(70, 74, 80);
                g.Circle(x, y, 8 + (float)rng.NextDouble() * 14, D(c));
            }
        }
        Band(g, 16, 1, D(P.Shore), view);
        Band(g, 0, 1, D(P.Shallow), view);
        Band(g, -8, 0.7f, D(P.Deep), view);

        // Ripples, anchored to the world.
        for (int i = 0; i < 70; i++)
        {
            float hx = (i * 0.618034f) % 1 * 680, hy = (i * 0.7548777f) % 1 * 420;
            float px = hx + MathF.Ceiling((view.X - hx) / 680) * 680;
            float py = hy + MathF.Ceiling((view.Y - hy) / 420) * 420 + MathF.Sin(Time + i) * 3;
            if (Clearance(new Vector2(px, py), out _, out _) < -6)
                g.Line(px, py, px + 10, py, 1.2f, D(new Color(150, 210, 240)) * 0.35f);
        }

        // Quay and berth.
        int n = _pts.Count;
        var s0 = _pts[n - 2];
        var s1 = _pts[n - 1];
        var ax = s1 - s0;
        ax.Normalize();
        var nn = new Vector2(-ax.Y, ax.X);
        float qa = MathF2.Angle(ax);
        g.RotatedRect(s0 + ax * SlipLen / 2 + nn * (SlipHalf + 14), SlipLen + 30, 28, qa, D(new Color(150, 150, 155)));
        g.RotatedRect(s0 + ax * SlipLen / 2 - nn * (SlipHalf + 14), SlipLen + 30, 28, qa, D(new Color(150, 150, 155)));
        g.RotatedRect(s1 + ax * 16, 36, 2 * SlipHalf + 90, qa, D(new Color(130, 130, 136)));
        // Bollards and the target box.
        for (int i = 0; i < 5; i++)
        {
            var bp = s0 + ax * (20 + i * 28);
            g.Circle(bp.X + nn.X * (SlipHalf + 4), bp.Y + nn.Y * (SlipHalf + 4), 2.5f, D(Pal.DarkGrey));
            g.Circle(bp.X - nn.X * (SlipHalf + 4), bp.Y - nn.Y * (SlipHalf + 4), 2.5f, D(Pal.DarkGrey));
        }
        var berth = s1 - ax * (ShipLen / 2 + 8);
        float pulse = 0.5f + 0.5f * MathF.Sin(Time * 4);
        g.RotatedRect(berth, ShipLen + 10, SlipHalf * 2 - 8, qa, Pal.Add(Pal.Lime, 0.1f + 0.1f * pulse));
        var c0 = berth - ax * (ShipLen / 2 + 5);
        var c1 = berth + ax * (ShipLen / 2 + 5);
        var w = nn * (SlipHalf - 4);
        g.Line(c0 + w, c1 + w, 1.5f, Pal.Lime * (0.5f + 0.5f * pulse));
        g.Line(c0 - w, c1 - w, 1.5f, Pal.Lime * (0.5f + 0.5f * pulse));
        g.Line(c0 + w, c0 - w, 1.5f, Pal.Lime * (0.5f + 0.5f * pulse));
        g.Line(c1 + w, c1 - w, 1.5f, Pal.Lime * (0.5f + 0.5f * pulse));
        // Storage tanks on the quay.
        for (int i = -1; i <= 1; i += 2)
        {
            var tp = s1 + ax * 70 + nn * i * 70;
            g.Circle(tp.X, tp.Y, 30, D(new Color(200, 200, 205)));
            g.Ring(tp.X, tp.Y, 30, 2, D(new Color(150, 150, 160)));
            g.Circle(tp.X, tp.Y, 8, D(new Color(170, 170, 180)));
        }

        // Oil slicks.
        foreach (var s in _slicks)
        {
            g.Ellipse(s.X, s.Y, s.Z * 1.3f, s.Z, new Color(14, 10, 16) * 0.75f);
            g.Ellipse(s.X - s.Z * 0.2f, s.Y - s.Z * 0.2f, s.Z * 0.7f, s.Z * 0.45f, Pal.Add(Pal.Purple, 0.25f));
            g.Ellipse(s.X + s.Z * 0.3f, s.Y + s.Z * 0.1f, s.Z * 0.5f, s.Z * 0.3f, Pal.Add(Pal.Teal, 0.2f));
        }

        // Wake.
        foreach (var wk in _wake)
        {
            float k = wk.Z / 3.5f;
            float r = 4 + wk.Z * 7;
            g.Ring(wk.X, wk.Y, r, 1.5f, D(Pal.White) * (0.45f * (1 - k)));
        }

        // Rocks.
        foreach (var r in _rocks)
        {
            float foam = MathF.Sin(Time * 2 + r.X) * 1.5f;
            g.Circle(r.X, r.Y, r.Z + 4 + foam, D(Pal.White) * 0.5f);
            g.Circle(r.X, r.Y, r.Z, D(new Color(90, 85, 80)));
            g.Circle(r.X - r.Z * 0.3f, r.Y - r.Z * 0.3f, r.Z * 0.5f, D(new Color(130, 125, 118)));
        }

        // Buoys.
        foreach (var b in _buoys)
        {
            var col = b.Z > 0 ? new Color(40, 200, 80) : new Color(230, 50, 50);
            bool on = (int)(Time * 1.5f + b.X * 0.01f) % 2 == 0;
            g.Circle(b.X, b.Y, 5.5f, D(Pal.White) * 0.6f);
            g.Circle(b.X, b.Y, 4, D(col));
            if (on)
                g.Glow(b.X, b.Y, P.Night ? 30 : 12, col, P.Night ? 0.9f : 0.5f);
        }

        foreach (var bt in _boats)
            DrawBoat(g, bt);

        DrawTanker(g, _pos, _heading, 1, _rudder, P.Night, Dim);

        g.Offset = saved;

        if (P.Night)
        {
            // Rain and a flash of lightning.
            for (int i = 0; i < 80; i++)
            {
                float x = Backdrops.Mod(i * 53.7f + Time * 140, 680) - 20;
                float y = Backdrops.Mod(i * 91.3f + Time * 420, 360);
                g.Line(x, y, x - 4, y + 10, 1, new Color(160, 180, 220) * 0.35f);
            }
            if (_lightning > 0)
                g.Rect(g.Visible, Pal.Add(Pal.White, _lightning * 1.2f));
        }

        DrawHud(g);
    }

    private static readonly Vector2[] HullShape = [new(-48, -10), new(36, -10), new(48, 0), new(36, 10), new(-48, 10)];
    private static readonly Vector2[] DeckShape = [new(-44, -7.5f), new(34, -7.5f), new(43, 0), new(34, 7.5f), new(-44, 7.5f)];

    private static void DrawTanker(Gfx g, Vector2 p, float heading, float s, float rudder, bool night, float dim)
    {
        Color Dm(Color c) => Pal.Lerp(Color.Black, c, dim);
        var f = MathF2.FromAngle(heading);
        var n = new Vector2(-f.Y, f.X);
        g.Shape(HullShape, p + new Vector2(3, 4) * s, heading, s, Color.Black * 0.35f);
        g.Shape(HullShape, p, heading, s, Dm(new Color(150, 40, 36)));
        g.Shape(DeckShape, p, heading, s, Dm(new Color(60, 110, 70)));
        // Pipework and hatches.
        for (int i = -1; i <= 1; i++)
            g.Line(p + (f * -26 + n * i * 3) * s, p + (f * 34 + n * i * 3) * s, 1.2f * s, Dm(new Color(210, 200, 170)));
        for (int i = 0; i < 6; i++)
            g.Circle(p.X + (f.X * (-20 + i * 10)) * s, p.Y + (f.Y * (-20 + i * 10)) * s, 2.6f * s, Dm(new Color(90, 140, 100)));
        // Bridge and funnel at the stern.
        g.RotatedRect(p - f * 37 * s, 12 * s, 17 * s, heading, Dm(new Color(240, 240, 235)));
        g.RotatedRect(p - f * 35 * s, 3 * s, 15 * s, heading, Dm(new Color(60, 70, 90)));
        g.Circle(p.X - f.X * 44 * s, p.Y - f.Y * 44 * s, 3.2f * s, Dm(new Color(30, 30, 34)));
        // Rudder indicator at the very stern.
        var stern = p - f * 48 * s;
        g.Line(stern, stern - MathF2.FromAngle(heading - rudder * 0.6f, 7 * s), 2 * s, Dm(new Color(60, 60, 60)));
        if (night)
        {
            // Navigation lights and a searchlight.
            var bow = p + f * 46 * s;
            var a = bow + f * 260 * s + n * 70 * s;
            var b = bow + f * 260 * s - n * 70 * s;
            g.Triangle(bow, a, b, Pal.Add(new Color(255, 240, 200), 0.12f));
            g.Glow(bow + f * 120 * s, 70 * s, new Color(255, 240, 200), 0.18f);
            g.Glow(p + n * 10 * s, 10 * s, Pal.Green, 0.9f);
            g.Glow(p - n * 10 * s, 10 * s, Pal.Red, 0.9f);
            g.Glow(p - f * 37 * s, 16 * s, Pal.White, 0.6f);
        }
    }

    private void DrawBoat(Gfx g, Boat b)
    {
        float s = b.Tug ? 0.4f : 0.62f;
        var f = MathF2.FromAngle(b.Heading);
        g.Shape(HullShape, b.Pos + new Vector2(2, 3), b.Heading, s, Color.Black * 0.35f);
        g.Shape(HullShape, b.Pos, b.Heading, s, D(b.Tug ? new Color(40, 40, 50) : b.Colour));
        if (!b.Tug)
        {
            for (int i = 0; i < 4; i++)
                g.RotatedRect(b.Pos + f * (-14 + i * 10), 8, 10, b.Heading, D(Pal.Rainbow[(i + (int)b.Speed) % 6]));
            g.RotatedRect(b.Pos - f * 24, 7, 11, b.Heading, D(Pal.White));
        }
        else
        {
            g.RotatedRect(b.Pos, 10, 6, b.Heading, D(new Color(230, 200, 60)));
        }
        if (P.Night)
            g.Glow(b.Pos, 18, Pal.Yellow, 0.5f);
    }

    private void DrawHud(Gfx g)
    {
        // Engine telegraph.
        // On phones the stick sits bottom-left, so the gauges move to the top.
        bool top = IsTouch;
        var tp = top ? new RectF(8, 30, 78, 120) : new RectF(8, 230, 78, 120);
        g.Panel(tp, new Color(14, 20, 40) * 0.88f, new Color(120, 140, 180), 8);
        g.Text("ENGINE", tp.CenterX, tp.Y + 6, 1f, Pal.LightGrey, Align.Center);
        float barX = tp.X + 12, barY = tp.Y + 20, barH = 88;
        g.Rect(barX, barY, 10, barH, new Color(30, 36, 60));
        float zeroY = barY + barH * (1f / 1.5f);
        float ty = barY + barH * ((1 - _throttle) / 1.5f);
        g.Rect(barX, MathF.Min(ty, zeroY), 10, MathF.Abs(ty - zeroY), _throttle >= 0 ? Pal.Lime : Pal.Orange);
        float actual = MathF2.Clamp(_speed / MaxSpeed, -0.5f, 1);
        float ay = barY + barH * ((1 - actual) / 1.5f);
        g.Rect(barX - 3, ay - 1, 16, 2, Pal.White);
        string[] marks = ["FULL", "HALF", "SLOW", "STOP", "ASTN"];
        float[] vals = [1f, 0.6f, 0.3f, 0, -0.5f];
        for (int i = 0; i < marks.Length; i++)
        {
            float my = barY + barH * ((1 - vals[i]) / 1.5f);
            bool sel = MathF.Abs(_throttle - vals[i]) < 0.15f;
            g.Rect(barX + 11, my, 4, 1, Pal.Grey);
            g.Text(marks[i], barX + 18, my - 3, 1f, sel ? Pal.Yellow : Pal.Grey);
        }

        // Rudder.
        var rp = top ? new RectF(90, 30, 104, 60) : new RectF(90, 290, 104, 60);
        g.Panel(rp, new Color(14, 20, 40) * 0.88f, new Color(120, 140, 180), 8);
        g.Text("RUDDER", rp.CenterX, rp.Y + 5, 1f, Pal.LightGrey, Align.Center);
        var hub = new Vector2(rp.CenterX, rp.Bottom - 10);
        g.Arc(hub.X, hub.Y, 34, 5, MathF.PI + 0.35f, MathF.PI * 1.5f - 0.05f, Pal.Red * 0.8f);
        g.Arc(hub.X, hub.Y, 34, 5, MathF.PI * 1.5f + 0.05f, MathF.PI * 2 - 0.35f, Pal.Green * 0.8f);
        float ra = -MathF.PI / 2 + _rudder * (MathF.PI / 2 - 0.35f);
        g.Line(hub, hub + MathF2.FromAngle(ra, 32), 2.5f, Pal.White);
        float ca = -MathF.PI / 2 + In.AxisX * (MathF.PI / 2 - 0.35f);
        g.Circle(hub.X + MathF.Cos(ca) * 38, hub.Y + MathF.Sin(ca) * 38, 2.5f, Pal.Yellow);
        g.Circle(hub.X, hub.Y, 4, Pal.LightGrey);

        // Speed, time and distance.
        var ip = top ? new RectF(90, 94, 150, 38) : new RectF(198, 312, 150, 38);
        g.Panel(ip, new Color(14, 20, 40) * 0.88f, new Color(120, 140, 180), 8);
        float knots = _speed * 0.25f;
        g.Text(knots.ToString("0.0") + " KN", ip.X + 8, ip.Y + 7, 1.5f, Pal.White);
        int dist = (int)MathF.Max(0, _pathLen - Progress(_pos));
        g.Text(dist + " M TO BERTH", ip.X + 8, ip.Y + 24, 1f, Pal.Sky);
        int secs = (int)MathF.Ceiling(_timeLeft);
        var tc = secs < 20 && Tick % 30 < 15 ? Pal.Red : Pal.Yellow;
        g.TextShadow(secs / 60 + ":" + (secs % 60).ToString("00"), 312, 30, 2f, tc, Align.Center);

        // Wind arrow.
        var wc = new Vector2(612, 320);
        g.Circle(wc.X, wc.Y, 20, new Color(14, 20, 40) * 0.8f);
        g.Ring(wc.X, wc.Y, 20, 1.5f, new Color(120, 140, 180));
        var wd = MathF2.FromAngle(_windAngle);
        float ws = MathF.Min(16, 6 + (P.Wind + _gust));
        g.Line(wc - wd * ws * 0.6f, wc + wd * ws * 0.6f, 2, Pal.Ice);
        g.Triangle(wc + wd * ws * 0.9f, wc + wd * ws * 0.4f + new Vector2(-wd.Y, wd.X) * 4, wc + wd * ws * 0.4f - new Vector2(-wd.Y, wd.X) * 4, Pal.Ice);
        g.Text("WIND", wc.X, wc.Y + 23, 1f, Pal.LightGrey, Align.Center);

        // Mini chart.
        var mp = new RectF(560, 30, 72, 120);
        g.Panel(mp, new Color(14, 20, 40) * 0.85f, new Color(120, 140, 180), 6);
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (var p in _pts)
        {
            minX = MathF.Min(minX, p.X);
            maxX = MathF.Max(maxX, p.X);
            minY = MathF.Min(minY, p.Y);
            maxY = MathF.Max(maxY, p.Y);
        }
        float sc = MathF.Min((mp.W - 16) / MathF.Max(1, maxX - minX), (mp.H - 16) / MathF.Max(1, maxY - minY));
        Vector2 M(Vector2 p) => new(mp.CenterX + (p.X - (minX + maxX) / 2) * sc, mp.CenterY + (p.Y - (minY + maxY) / 2) * sc);
        for (int i = 0; i < _pts.Count - 1; i++)
            g.Line(M(_pts[i]), M(_pts[i + 1]), MathF.Max(2, _wid[i] * sc * 2), P.Shallow);
        g.Circle(M(_pts[^1]).X, M(_pts[^1]).Y, 2.5f, Pal.Lime);
        var me = M(_pos);
        g.Glow(me, 6, Pal.Orange, 0.8f);
        g.Circle(me.X, me.Y, 2, Pal.White);

        if (_banner > 0)
        {
            float a = MathF.Min(1, _banner);
            g.TextShadow("PORT " + (_port + 1) + ": " + P.Name, 320, 120, 2.5f, Pal.White * a, Align.Center, Color.Black * (0.8f * a));
            if (_dockMessage == "OUT OF TIME")
                g.TextShadow("OUT OF TIME - TRY AGAIN", 320, 146, 1.5f, Pal.Orange * a, Align.Center);
            else
                g.TextShadow("DOCK IN THE GREEN BERTH", 320, 146, 1.5f, Pal.Lime * a, Align.Center);
        }
        if (_docked)
        {
            g.TextShadow("DOCKED!", 320, 110, 3.5f, Pal.Lime, Align.Center);
            if (_dockMessage != null)
                g.TextShadow(_dockMessage, 320, 146, 1.5f, Pal.White, Align.Center);
        }
        else if (_dockHold > 0)
        {
            g.TextShadow("HOLD STEADY...", 320, 120, 2f, Pal.Lime, Align.Center);
        }
    }

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        float s = r.H / 162f;
        g.Rect(r, new Color(16, 70, 140));
        // Shoreline, buoys and the berth.
        g.Rect(r.X, r.Y, r.W * 0.18f, r.H, new Color(235, 220, 160));
        g.Rect(r.X, r.Y, r.W * 0.15f, r.H, new Color(60, 150, 70));
        g.Rect(r.Right - r.W * 0.18f, r.Y, r.W * 0.18f, r.H, new Color(235, 220, 160));
        g.Rect(r.Right - r.W * 0.15f, r.Y, r.W * 0.15f, r.H, new Color(60, 150, 70));
        for (int i = 0; i < 8; i++)
        {
            float wy = r.Y + Backdrops.Mod(i * 40 * s + time * 20 * s, r.H);
            g.Line(r.X + r.W * (0.25f + (i % 3) * 0.2f), wy, r.X + r.W * (0.25f + (i % 3) * 0.2f) + 12 * s, wy, 1.2f * s, new Color(150, 210, 240) * 0.4f);
        }
        for (int i = 0; i < 3; i++)
        {
            float by = r.Y + Backdrops.Mod(i * 60 * s + time * 20 * s, r.H + 20 * s) - 10 * s;
            g.Circle(r.X + r.W * 0.24f, by, 4 * s, new Color(230, 50, 50));
            g.Circle(r.Right - r.W * 0.24f, by, 4 * s, new Color(40, 200, 80));
            if ((int)(time * 2 + i) % 2 == 0)
            {
                g.Glow(r.X + r.W * 0.24f, by, 12 * s, Pal.Red, 0.6f);
                g.Glow(r.Right - r.W * 0.24f, by, 12 * s, Pal.Green, 0.6f);
            }
        }
        var p = new Vector2(r.CenterX + MathF.Sin(time * 0.5f) * 20 * s, r.CenterY + 10 * s);
        float h = -MathF.PI / 2 + MathF.Sin(time * 0.5f + 1) * 0.2f;
        for (int i = 1; i < 5; i++)
            g.Ring(p.X - MathF.Cos(h) * (60 + i * 14) * s, p.Y - MathF.Sin(h) * (60 + i * 14) * s, (4 + i * 5) * s, 1.5f * s, Pal.White * (0.5f - i * 0.1f));
        DrawTanker(g, p, h, 1.35f * s, MathF.Sin(time * 0.5f), false, 1);
    }

    public override void AutoPlay(Controls c)
    {
        if (_docked)
            return;
        float prog = Progress(_pos);
        float remaining = _pathLen - prog;
        // Aim at a point ahead on the centreline, dodging rocks.
        float look = MathF2.Clamp(110 + _speed * 2.2f, 90, 220);
        var target = PathPoint(MathF.Min(_pathLen - 30, prog + look), out var dir);
        var n = new Vector2(-dir.Y, dir.X);
        Clearance(target, out _, out int tseg);
        if (remaining > SlipLen + 450)
            target += n * _wid[tseg] * 0.18f;
        foreach (var r in _rocks)
        {
            var rc = new Vector2(r.X, r.Y);
            float ahead = Vector2.Dot(rc - _pos, Forward);
            if (ahead > 0 && ahead < 260)
            {
                float side = Vector2.Dot(rc - target, n);
                if (MathF.Abs(side) < r.Z + 34)
                    target -= n * MathF.Sign(side == 0 ? 1 : side) * (r.Z + 34 - MathF.Abs(side));
            }
        }
        foreach (var b in _boats)
        {
            float ahead = Vector2.Dot(b.Pos - _pos, Forward);
            if (ahead > 0 && ahead < 260)
            {
                float side = Vector2.Dot(b.Pos - target, n);
                if (MathF.Abs(side) < 40)
                    target -= n * MathF.Sign(side == 0 ? 1 : side) * (40 - MathF.Abs(side));
            }
        }
        float want = MathF2.Angle(target - _pos);
        float err = MathF2.WrapAngle(want - _heading);
        float steer = MathF2.Clamp(err * 4.5f - _angVel * 9, -1, 1);

        // Speed plan: slow for the berth and in narrow water.
        float cruise = MaxSpeed * (P.Width < 80 ? 0.5f : 0.68f);
        float desired = MathF.Min(cruise, MathF.Sqrt(MathF.Max(0, remaining - ShipLen * 0.5f - 25)) * 1.25f);
        if (remaining < SlipLen + 10)
            desired = MathF.Min(desired, 9);
        float desiredThrottle = MathF2.Clamp(desired / MaxSpeed + (desired - _speed) * 0.03f, -0.5f, 1);
        // Wedged? Back off for a while.
        if (MathF.Abs(_speed) < 2.5f && _throttle > 0.3f && remaining > SlipLen)
            _autoStuck += Dt;
        else if (_autoReverse <= 0)
            _autoStuck = 0;
        if (_autoStuck > 2.5f)
        {
            _autoReverse = 6;
            _autoStuck = 0;
        }
        if (_autoReverse > 0)
        {
            _autoReverse -= Dt;
            desiredThrottle = -0.5f;
            steer = -steer;
        }
        float dy = MathF.Abs(desiredThrottle - _throttle) < 0.04f ? 0 : -MathF.Sign(desiredThrottle - _throttle);
        c.SetDirections(steer, dy);
    }
}
