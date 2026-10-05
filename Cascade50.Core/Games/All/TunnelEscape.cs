using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 49 Tunnel Escape: race down a twisting neon tube drawn in true perspective. Spin the tunnel
/// round your craft to slip through the gaps in barriers and gates, grab energy orbs, and see
/// how far you get as the speed keeps climbing.
/// </summary>
public sealed class TunnelEscape : MiniGame, Capture.ICaptureHints
{
    public override int Number => 49;
    public override string Title => "Tunnel Escape";
    public override Category Category => Category.Skill;
    public override string Tagline => "Spin through a twisting neon tunnel at ever-rising speed.";
    public override Color Accent => Pal.Cyan;
    public override Pad Pad => Pad.Horizontal;
    public int CaptureTicks => 60 * 10;

    public override string[] HowToPlay =>
    [
        "Race down the tunnel. Steer round the tube to slip through the gaps in barriers and gates.",
        "Each crash costs one of 3 shields. Every 8 energy orbs restore a shield.",
        "Score 1 per metre and 25 per orb. The speed keeps rising and each zone twists harder.",
    ];

    public override string[] DesktopControls => ["LEFT / RIGHT (or A / D) to steer round the tunnel."];
    public override string[] TouchControls => ["Use the left / right pad to steer round the tunnel."];

    private const int Sectors = 16, Rings = 30;
    private const float R = 1f, Focal = 180f, PlayerZ = 1.4f, Cx = 320, Cy = 178, NearZ = 0.22f;
    private const float SectorAngle = MathF.PI * 2 / Sectors;
    private const float MetresPerUnit = 4f;

    private static readonly Color[] ZoneColours =
    [
        new(40, 230, 240), new(240, 60, 220), new(150, 255, 70), new(255, 150, 30), new(130, 110, 255), new(255, 60, 70),
        new(255, 230, 60),
    ];

    private static readonly string[] ZoneNames = ["CYAN RUN", "MAGENTA MAZE", "ACID GREEN", "SOLAR FLARE", "ULTRAVIOLET", "RED SHIFT", "GOLDEN MILE"];

    private struct Obstacle
    {
        public float D;
        public int Mask;
        public float Spin, Base;
        public bool Passed;
    }

    private struct Orb
    {
        public float D, Angle;
        public bool Taken, Passed;
    }

    private readonly List<Obstacle> _obs = new();
    private readonly List<Orb> _orbs = new();
    private readonly Vector2[,] _pts = new Vector2[Rings + 1, Sectors + 1];
    private readonly float[] _ringZ = new float[Rings + 1];
    private readonly int[] _ringIndex = new int[Rings + 1];

    private float _dist, _speed, _ang, _angVel, _nextSpawn, _invuln, _flash, _zoneBanner, _metreScore;
    private int _zone, _orbCount;
    private float _lastGapAngle;

    protected override void Start()
    {
        Lives = 3;
        Level = 1;
        _dist = 0;
        _speed = 7;
        _ang = 0;
        _nextSpawn = 9;
        _lastGapAngle = MathF.PI / 2;
        _zone = 0;
        _zoneBanner = 2.5f;
        Status = ZoneNames[0];
    }

    // ------------------------------------------------------------------ geometry

    private float Twist => 1.2f + _zone * 0.45f;

    private Vector2 Curve(float d)
    {
        float a = MathF.Min(Twist, 3.6f);
        return new Vector2(MathF.Sin(d * 0.045f) * a * 2.2f + MathF.Sin(d * 0.11f + 1) * a * 0.6f,
            MathF.Sin(d * 0.037f + 2) * a * 1.4f + MathF.Cos(d * 0.09f) * a * 0.4f);
    }

    /// <summary>Tunnel centre at depth z relative to the camera, looking along the current tangent.</summary>
    private Vector2 Offset(float z)
    {
        var c0 = Curve(_dist);
        var tangent = (Curve(_dist + 0.5f) - c0) / 0.5f;
        return Curve(_dist + z) - c0 - tangent * z;
    }

    private Vector2 Project(Vector2 off, float angle, float radius, float z)
    {
        float sa = angle - _ang + MathF.PI / 2;
        float k = Focal / z;
        return new Vector2(Cx + (off.X + MathF.Cos(sa) * radius) * k, Cy + (off.Y + MathF.Sin(sa) * radius) * k);
    }

    private Color ZoneColour(float extra = 0)
    {
        float zf = _zone + extra;
        int i = (int)zf;
        return Pal.Lerp(ZoneColours[i % ZoneColours.Length], ZoneColours[(i + 1) % ZoneColours.Length], zf - i);
    }

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        float metres = _dist * MetresPerUnit;
        int zone = (int)(metres / 800);
        if (zone != _zone)
        {
            _zone = zone;
            Level = zone + 1;
            _zoneBanner = 2.5f;
            Status = ZoneNames[zone % ZoneNames.Length];
            Sound.Play(Sfx.LevelUp);
        }
        if (_zoneBanner > 0)
            _zoneBanner -= Dt;
        if (_invuln > 0)
            _invuln -= Dt;
        if (_flash > 0)
            _flash -= Dt * 2.5f;

        _speed = MathF.Min(24, 7 + Time * 0.075f);
        float rate = 4.2f + _speed * 0.06f;
        _angVel = MathF2.Approach(_angVel, -In.AxisX * rate, Dt * 30);
        _ang = MathF2.WrapAngle(_ang + _angVel * Dt);

        float step = _speed * Dt;
        _dist += step;
        _metreScore += step * MetresPerUnit;
        while (_metreScore >= 1)
        {
            _metreScore -= 1;
            Score++;
        }
        Sound.Loop(LoopSfx.Engine, true, -0.4f + _speed / 40f, 0.35f);
        Sound.Loop(LoopSfx.Wind, true, _speed / 30f - 0.3f, 0.25f);

        while (_nextSpawn < _dist + Rings + 2)
            Spawn();

        // Collisions as things pass the craft.
        for (int i = 0; i < _obs.Count; i++)
        {
            var o = _obs[i];
            if (!o.Passed && o.D - _dist <= PlayerZ)
            {
                o.Passed = true;
                if (Blocked(o, _ang) && _invuln <= 0)
                    Crash(o);
                else
                    Sound.Play(Sfx.Whoosh, MathF.Min(0.8f, _speed / 30f), 0.5f);
                _obs[i] = o;
            }
        }
        for (int i = 0; i < _orbs.Count; i++)
        {
            var o = _orbs[i];
            if (!o.Passed && o.D - _dist <= PlayerZ)
            {
                o.Passed = true;
                if (MathF.Abs(MathF2.WrapAngle(o.Angle - _ang)) < 0.36f)
                {
                    o.Taken = true;
                    _orbCount++;
                    var p = ShipPos();
                    AddScore(25, p.X, p.Y - 26, Pal.Gold);
                    Fx.Burst(p.X, p.Y - 10, Pal.Gold, 14, 90, 0.4f, 2f);
                    Sound.Play(Sfx.Coin, MathF.Min(0.6f, _orbCount % 8 * 0.08f));
                    if (_orbCount % 8 == 0 && Lives < 3)
                    {
                        Lives++;
                        Sound.Play(Sfx.PowerUp);
                        Fx.Float("SHIELD +1", p.X, p.Y - 44, Pal.Cyan);
                    }
                }
                _orbs[i] = o;
            }
        }
        while (_obs.Count > 0 && _obs[0].D < _dist)
            _obs.RemoveAt(0);
        while (_orbs.Count > 0 && _orbs[0].D < _dist)
            _orbs.RemoveAt(0);
    }

    private bool Blocked(in Obstacle o, float angle)
    {
        // The craft is a little wider than a point: check either side of it.
        for (int k = -1; k <= 1; k++)
        {
            float a = angle + k * 0.11f - o.Base - o.Spin * Time;
            int s = (int)MathF.Floor(Backdrops.Mod(a, MathF2.Tau) / SectorAngle) % Sectors;
            if ((o.Mask & (1 << s)) != 0)
                return true;
        }
        return false;
    }

    private void Crash(in Obstacle o)
    {
        var p = ShipPos();
        Fx.Explode(p.X, p.Y - 10, 1.4f);
        Fx.Burst(p.X, p.Y - 10, ZoneColour(), 30, 220, 0.8f, 2.5f);
        Sound.Play(Sfx.BigExplode);
        _flash = 1;
        _invuln = 1.6f;
        if (LoseLife())
            return;
        Sound.Play(Sfx.Hurt);
    }

    private void Spawn()
    {
        float d = _nextSpawn;
        float gap = MathF.Max(3.6f, 8.5f - _zone * 0.7f) * (0.8f + 0.4f * (float)Rng.NextDouble());
        _nextSpawn += gap;
        // Gaps must be reachable: limit how far round the next opening can be.
        float reach = (4.2f) * (gap / MathF.Max(7, _speed + 3)) * 0.75f;
        int type = RandInt(0, Math.Min(5, 2 + _zone));
        var o = new Obstacle { D = d };
        float centre = _lastGapAngle + Rand(-reach, reach);
        int cs = (int)MathF.Floor(Backdrops.Mod(centre, MathF2.Tau) / SectorAngle);
        switch (type)
        {
            case 0:
            {
                // A block or two in the way.
                int w = RandInt(2, 4);
                int at = RandInt(0, Sectors);
                for (int i = 0; i < w; i++)
                    o.Mask |= 1 << ((at + i) % Sectors);
                if (_zone >= 1 && Chance(0.5f))
                    for (int i = 0; i < w; i++)
                        o.Mask |= 1 << ((at + 8 + i) % Sectors);
                break;
            }
            case 1:
            case 3:
            {
                // A gate: everything closed except a gap.
                int gw = Math.Max(3, 6 - _zone / 2);
                o.Mask = (1 << Sectors) - 1;
                for (int i = 0; i < gw; i++)
                    o.Mask &= ~(1 << ((cs - gw / 2 + i + Sectors) % Sectors));
                _lastGapAngle = (cs + 0.5f) * SectorAngle;
                break;
            }
            case 2:
            {
                // Alternating teeth.
                int phase = RandInt(0, 4);
                for (int s = 0; s < Sectors; s++)
                    if ((s + phase) / 2 % 2 == 0)
                        o.Mask |= 1 << s;
                break;
            }
            default:
            {
                // A slowly turning half-wall.
                int at = RandInt(0, Sectors);
                for (int i = 0; i < Sectors / 2 - 1; i++)
                    o.Mask |= 1 << ((at + i) % Sectors);
                o.Spin = Rand(0.5f, 1.1f) * (Chance(0.5f) ? 1 : -1);
                o.Base = Rand(0, MathF2.Tau);
                break;
            }
        }
        // Never block the whole ring.
        if (o.Mask == (1 << Sectors) - 1)
            o.Mask &= ~(1 << cs);
        _obs.Add(o);

        // Orbs in the gaps between obstacles.
        if (Chance(0.75f))
        {
            float a = Chance(0.5f) ? _lastGapAngle : Rand(0, MathF2.Tau);
            int n = RandInt(1, 4);
            for (int i = 0; i < n; i++)
                _orbs.Add(new Orb { D = d + gap * (0.35f + i * 0.15f), Angle = a + i * 0.12f });
        }
    }

    // ------------------------------------------------------------------ drawing

    private Vector2 ShipPos()
    {
        var off = Offset(PlayerZ);
        return Project(off, _ang, R * 0.9f, PlayerZ);
    }

    public override void Draw(Gfx g)
    {
        var zc = ZoneColour();
        g.Rect(g.Visible, new Color(2, 2, 10));

        // Ring positions, far to near.
        int first = (int)MathF.Floor(_dist) + 1;
        for (int k = 0; k <= Rings; k++)
        {
            int idx = first + k;
            float z = idx - _dist;
            if (z < NearZ)
                z = NearZ;
            _ringZ[k] = z;
            _ringIndex[k] = idx;
            var off = Offset(z);
            for (int s = 0; s <= Sectors; s++)
                _pts[k, s] = Project(off, s * SectorAngle, R, z);
        }

        // Light at the end of the tunnel.
        var vanish = Project(Offset(Rings), 0, 0, Rings);
        g.Glow(vanish, 120, zc, 0.35f);
        g.Glow(vanish, 40, Pal.White, 0.4f);

        for (int k = Rings - 1; k >= 0; k--)
        {
            float z = _ringZ[k];
            float fog = MathF2.Clamp(1 - z / Rings, 0, 1);
            fog *= fog;
            for (int s = 0; s < Sectors; s++)
            {
                bool odd = ((_ringIndex[k] + s) & 1) == 1;
                bool band = _ringIndex[k] % 8 == 0;
                var fill = Pal.Darken(zc, band ? 0.45f : odd ? 0.74f : 0.9f) * (0.25f + 0.75f * fog);
                var a = _pts[k, s];
                var b = _pts[k, s + 1];
                var c = _pts[k + 1, s + 1];
                var d = _pts[k + 1, s];
                g.Triangle(a, b, c, fill);
                g.Triangle(a, c, d, fill);
            }
            var line = zc * (0.15f + 0.85f * fog);
            float w = MathF.Max(0.6f, 2.2f * fog);
            if (_ringIndex[k] % 8 == 0)
            {
                for (int s = 0; s < Sectors; s++)
                    g.Line(_pts[k, s], _pts[k, s + 1], w * 4, Pal.Add(zc, 0.35f * fog));
                line = Pal.Lighten(zc, 0.5f) * (0.2f + 0.8f * fog);
            }
            for (int s = 0; s < Sectors; s++)
            {
                g.Line(_pts[k, s], _pts[k, s + 1], w, line);
                if (s % 2 == 0)
                    g.Line(_pts[k, s], _pts[k + 1, s], w * 0.7f, line * 0.6f);
            }
        }

        // Obstacles and orbs, far to near.
        int oi = _obs.Count - 1, bi = _orbs.Count - 1;
        while (oi >= 0 || bi >= 0)
        {
            if (bi < 0 || (oi >= 0 && _obs[oi].D >= _orbs[bi].D))
                DrawObstacle(g, _obs[oi--], zc);
            else
                DrawOrb(g, _orbs[bi--]);
        }

        // Speed streaks.
        for (int i = 0; i < 36; i++)
        {
            float h1 = (i * 0.618034f) % 1, h2 = (i * 0.414214f + 0.3f) % 1;
            float span = 14;
            float z = span - Backdrops.Mod(_dist * 1.4f + h2 * span, span) + 0.5f;
            float a = h1 * MathF2.Tau + _ang;
            float rad = 0.35f + 0.55f * ((i * 0.7548f) % 1);
            var off = Offset(z);
            var p0 = Project(off, a, rad, z);
            var p1 = Project(Offset(z + 0.6f), a, rad, z + 0.6f);
            float fade = MathF2.Clamp(1 - z / span, 0, 1);
            g.Line(p0, p1, 1.2f, Pal.Add(Pal.Lighten(zc, 0.5f), 0.6f * fade));
        }

        DrawShip(g);

        if (_flash > 0)
            g.Rect(g.Visible, Pal.Add(Pal.Red, 0.5f * _flash));

        // Speed and orb gauge.
        g.Text("SPEED", 18, 32, 1f, Pal.LightGrey * 0.8f);
        g.Text((int)(_speed * MetresPerUnit * 3.6f) + " KM/H", 18, 42, 1.5f, zc);
        g.Text("ORBS", 622, 32, 1f, Pal.LightGrey * 0.8f, Align.Right);
        for (int i = 0; i < 8; i++)
        {
            bool on = i < _orbCount % 8;
            float x = 622 - (7 - i) * 10 - 4;
            if (on)
                g.Glow(x, 48, 7, Pal.Gold, 0.6f);
            g.Circle(x, 48, 3, on ? Pal.Gold : Pal.DarkGrey);
        }

        if (_zoneBanner > 0)
        {
            float a = MathF.Min(1, _zoneBanner);
            g.TextShadow("ZONE " + (_zone + 1), 320, 96, 2f, Pal.White * a, Align.Center, Color.Black * (0.8f * a));
            g.TextShadow(ZoneNames[_zone % ZoneNames.Length], 320, 116, 3f, zc * a, Align.Center, Color.Black * (0.8f * a));
        }
    }

    private void DrawObstacle(Gfx g, in Obstacle o, Color zc)
    {
        float z = o.D - _dist;
        if (z < NearZ + 0.05f || z > Rings)
            return;
        float fog = MathF2.Clamp(1 - z / Rings, 0, 1);
        float z2 = z + 0.35f;
        var off = Offset(z);
        var off2 = Offset(z2);
        var face = Pal.Lerp(Pal.Lighten(zc, 0.35f), Color.White, 0.1f) * (0.3f + 0.7f * fog);
        var back = Pal.Darken(zc, 0.55f) * (0.3f + 0.7f * fog);
        var edge = Pal.Add(Color.White, 0.6f * fog);
        float rot = o.Base + o.Spin * Time;
        for (int s = 0; s < Sectors; s++)
        {
            if ((o.Mask & (1 << s)) == 0)
                continue;
            float a0 = s * SectorAngle + rot, a1 = a0 + SectorAngle;
            const float inner = 0.58f;
            // Back face for depth.
            var b0 = Project(off2, a0, R, z2);
            var b1 = Project(off2, a1, R, z2);
            var b2 = Project(off2, a1, R * inner, z2);
            var b3 = Project(off2, a0, R * inner, z2);
            g.Triangle(b0, b1, b2, back);
            g.Triangle(b0, b2, b3, back);
            var p0 = Project(off, a0, R, z);
            var p1 = Project(off, a1, R, z);
            var p2 = Project(off, a1, R * inner, z);
            var p3 = Project(off, a0, R * inner, z);
            // Inner side wall.
            g.Triangle(p3, p2, b2, Pal.Darken(zc, 0.3f) * (0.3f + 0.7f * fog));
            g.Triangle(p3, b2, b3, Pal.Darken(zc, 0.3f) * (0.3f + 0.7f * fog));
            g.Triangle(p0, p1, p2, face);
            g.Triangle(p0, p2, p3, face);
            var m = (p0 + p1 + p2 + p3) / 4;
            var i0 = Vector2.Lerp(p0, m, 0.3f);
            var i1 = Vector2.Lerp(p1, m, 0.3f);
            var i2 = Vector2.Lerp(p2, m, 0.3f);
            var i3 = Vector2.Lerp(p3, m, 0.3f);
            var inset = Pal.Darken(zc, 0.25f) * (0.3f + 0.7f * fog);
            g.Triangle(i0, i1, i2, inset);
            g.Triangle(i0, i2, i3, inset);
            float w = MathF.Max(0.8f, 3f * fog);
            g.Line(p2, p3, w, edge);
            g.Line(p2, p3, w * 3, Pal.Add(zc, 0.3f * fog));
            g.Line(p0, p3, w * 0.6f, Pal.Darken(zc, 0.5f));
        }
    }

    private void DrawOrb(Gfx g, in Orb o)
    {
        if (o.Taken)
            return;
        float z = o.D - _dist;
        if (z < NearZ + 0.05f || z > Rings)
            return;
        float fog = MathF2.Clamp(1 - z / Rings, 0, 1);
        var p = Project(Offset(z), o.Angle, R * 0.78f, z);
        float size = 0.09f * Focal / z;
        float pulse = 0.8f + 0.2f * MathF.Sin(Time * 8 + o.D);
        g.Glow(p, size * 4, Pal.Gold, 0.7f * fog);
        g.Circle(p.X, p.Y, size * pulse, Pal.Lerp(Pal.Gold, Color.White, 0.4f) * fog);
    }

    private void DrawShip(Gfx g)
    {
        if (IsOver)
            return;
        if (_invuln > 0 && (int)(_invuln * 12) % 2 == 0)
            return;
        var p = ShipPos();
        float lean = MathF2.Clamp(-_angVel * 0.06f, -0.4f, 0.4f);
        var zc = ZoneColour();
        DrawCraft(g, p, 1f, lean, zc, Time);
    }

    private static readonly Vector2[] Hull = [new(0, -16), new(18, 8), new(6, 4), new(0, 10), new(-6, 4), new(-18, 8)];
    private static readonly Vector2[] Wing = [new(0, -16), new(18, 8), new(0, 2), new(-18, 8)];

    private static void DrawCraft(Gfx g, Vector2 p, float s, float lean, Color zc, float t)
    {
        // Engine flare.
        float flick = 0.8f + 0.2f * MathF.Sin(t * 50);
        g.Glow(p.X, p.Y + 8 * s, 28 * s * flick, Pal.Orange, 0.6f);
        g.Glow(p.X, p.Y + 8 * s, 10 * s, Pal.White, 0.8f);
        g.Glow(p.X, p.Y, 40 * s, zc, 0.25f);
        float angle = lean;
        g.Shape(Wing, p + new Vector2(0, 3 * s), angle, s * 1.05f, Color.Black * 0.5f);
        g.Shape(Wing, p, angle, s, new Color(30, 40, 70));
        g.ShapeOutline(Wing, p, angle, s, 1.5f * s, zc, true);
        g.Shape(Hull, p, angle, s * 0.55f, new Color(200, 220, 255));
        g.Circle(p.X + MathF.Sin(lean) * 2, p.Y - 4 * s, 2.5f * s, Pal.Cyan);
    }

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        g.Rect(r, new Color(2, 2, 10));
        var c = r.Center;
        float s = r.H / 162f;
        var zc = ZoneColours[(int)(time / 3) % ZoneColours.Length];
        g.Glow(c, 70 * s, zc, 0.4f);
        float scroll = time * 3 % 1;
        for (int k = 14; k >= 0; k--)
        {
            float z = k + 1 - scroll;
            float rad = 0.95f * r.W / 2 / z * 1.4f;
            float bend = MathF.Sin(time * 0.8f + k * 0.25f) * 12 * s * (k / 6f);
            float fog = 1 - z / 15;
            float rot = time * 0.6f;
            for (int i = 0; i < Sectors; i++)
            {
                float a0 = rot + i * SectorAngle, a1 = a0 + SectorAngle;
                var p0 = new Vector2(c.X + bend + MathF.Cos(a0) * rad, c.Y + MathF.Sin(a0) * rad * 0.8f);
                var p1 = new Vector2(c.X + bend + MathF.Cos(a1) * rad, c.Y + MathF.Sin(a1) * rad * 0.8f);
                g.Line(p0, p1, MathF.Max(0.6f, 2.2f * fog * s), zc * fog);
                if (k == 4 && i % 4 != 0 && i < 9)
                {
                    var q0 = new Vector2(c.X + bend + MathF.Cos(a0) * rad * 0.6f, c.Y + MathF.Sin(a0) * rad * 0.48f);
                    var q1 = new Vector2(c.X + bend + MathF.Cos(a1) * rad * 0.6f, c.Y + MathF.Sin(a1) * rad * 0.48f);
                    g.Triangle(p0, p1, q1, Pal.Lighten(zc, 0.3f) * fog);
                    g.Triangle(p0, q1, q0, Pal.Lighten(zc, 0.3f) * fog);
                }
            }
        }
        g.Glow(c, 16 * s, Color.White, 0.6f);
        DrawCraft(g, new Vector2(c.X, r.Bottom - 30 * s), 1.6f * s, MathF.Sin(time * 2) * 0.12f, zc, time);
    }

    public override void AutoPlay(Controls c)
    {
        // Look at the next obstacle and head for the nearest open sector.
        float target = _ang;
        bool found = false;
        foreach (var o in _obs)
        {
            float z = o.D - _dist;
            if (o.Passed || z < PlayerZ - 0.1f)
                continue;
            if (z > PlayerZ + 9)
                break;
            float tHit = (z - PlayerZ) / _speed;
            float best = float.MaxValue;
            float rot = o.Base + o.Spin * (Time + tHit);
            for (int s = 0; s < Sectors; s++)
            {
                float a = s * SectorAngle + SectorAngle / 2 + rot;
                bool blocked = false;
                for (int k = -1; k <= 1; k++)
                {
                    int ss = (int)MathF.Floor(Backdrops.Mod(a + k * 0.16f - rot, MathF2.Tau) / SectorAngle) % Sectors;
                    if ((o.Mask & (1 << ss)) != 0)
                        blocked = true;
                }
                if (blocked)
                    continue;
                float d = MathF.Abs(MathF2.WrapAngle(a - _ang));
                if (d < best)
                {
                    best = d;
                    target = a;
                }
            }
            found = true;
            break;
        }
        if (!found)
            foreach (var o in _orbs)
                if (!o.Passed && o.D - _dist > PlayerZ)
                {
                    target = o.Angle;
                    break;
                }
        float delta = MathF2.WrapAngle(target - _ang);
        float x = MathF.Abs(delta) < 0.04f ? 0 : -MathF.Sign(delta) * MathF.Min(1, MathF.Abs(delta) * 3);
        c.SetDirections(x, 0);
    }
}
