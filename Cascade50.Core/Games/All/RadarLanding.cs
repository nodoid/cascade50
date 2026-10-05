using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 35 Radar Landing: air traffic control on a glowing radar scope. Steer each aircraft onto one
/// of two runway approaches, keep them apart, and land them before their fuel runs out.
/// </summary>
public sealed class RadarLanding : MiniGame, Capture.ICaptureHints
{
    public override int Number => 35;
    public override string Title => "Radar Landing";
    public override Category Category => Category.Skill;
    public override string Tagline => "Talk the traffic down: steer every flight safely on to the runways.";
    public override Color Accent => Pal.Green;
    public override Pad Pad => Pad.None;
    public int CaptureTicks => 1500;

    public override string[] HowToPlay =>
    [
        "Guide each plane on to a runway, flying in along the dashed approach line. Landings score 100 plus fuel.",
        "Planes that touch collide: game over! Running dry or leaving radar costs a life.",
    ];

    public override string[] DesktopControls => ["Drag a path from a plane.", "Click to steer the selected one.", "TAB picks a plane, ARROWS turn."];
    public override string[] TouchControls => ["Drag a path from a plane, or tap", "a spot to steer the selected one."];

    private static readonly Vector2 Centre = new(330, 192);
    private const float ScopeR = 160, TurnRate = 0.75f, CollideDist = 9, WarnDist = 26;
    private const float SweepPeriod = 2.2f;
    private static readonly Color Phosphor = new(70, 255, 110);

    private sealed class Plane
    {
        public string Name;
        public Vector2 Pos;
        public float Angle, Target, Speed, Fuel, Paint, Age;
        public Vector2 PaintPos;
        public readonly List<Vector2> Path = new();
        public readonly List<Vector2> History = new();
        public bool Heavy;
    }

    private struct Runway
    {
        public string Name;
        public Vector2 Mid;
        public float Angle;

        public Vector2 Dir => MathF2.FromAngle(Angle);
        public Vector2 Threshold => Mid - Dir * 26;
    }

    private static readonly Runway[] Runways =
    [
        new() { Name = "08", Mid = new Vector2(312, 222), Angle = -0.17f },
        new() { Name = "33", Mid = new Vector2(380, 150), Angle = -2.1f },
    ];

    private static readonly string[] Airlines = ["BA", "EI", "AF", "KL", "LH", "VS", "IB", "SK", "AZ", "LX"];

    private readonly List<Plane> _planes = new();
    private Plane _selected;
    private bool _drawing;
    private float _spawnTimer, _sweep, _alarmTimer, _historyTimer;
    private int _landed;
    private string _notice = "";
    private float _noticeTimer;
    private Color _noticeColour;
    private Vector2 _crashAt;

    // Autopilot.
    private Plane _autoPlane;
    private readonly List<Vector2> _autoRoute = new();
    private int _autoStep, _autoCooldown;

    protected override void Start()
    {
        Lives = 3;
        Level = 1;
        _planes.Clear();
        _selected = null;
        _drawing = false;
        _landed = 0;
        _noticeTimer = 0;
        _autoPlane = null;
        _autoRoute.Clear();
        _autoStep = 0;
        _autoCooldown = 0;
        _spawnTimer = 0.5f;
        Status = "LANDED 0";
    }

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        float before = _sweep;
        _sweep = Backdrops.Mod(_sweep + MathF2.Tau / SweepPeriod * Dt, MathF2.Tau);
        bool wrapped = _sweep < before;
        if (wrapped)
            Sound.Play(Sfx.Tick, -0.6f, 0.15f);
        if (_noticeTimer > 0)
            _noticeTimer -= Dt;

        Level = 1 + _landed / 5;
        SpawnPlanes();
        HandleInput();
        MovePlanes(before, wrapped);
        if (IsOver)
            return;
        CheckSeparation();
    }

    private void SpawnPlanes()
    {
        _spawnTimer -= Dt;
        int maxPlanes = Math.Min(2 + Level, 9);
        if (_spawnTimer > 0 || _planes.Count >= maxPlanes)
            return;
        _spawnTimer = MathF.Max(4f, 10f - Level * 1.1f) + Rand(0, 3);
        for (int tries = 0; tries < 12; tries++)
        {
            float bearing = Rand(0, MathF2.Tau);
            var pos = Centre + MathF2.FromAngle(bearing, ScopeR - 3);
            bool clear = true;
            foreach (var p in _planes)
                if (Vector2.Distance(p.Pos, pos) < 70)
                    clear = false;
            if (!clear)
                continue;
            float heading = bearing + MathF.PI + Rand(-0.45f, 0.45f);
            bool heavy = Chance(0.25f);
            var plane = new Plane
            {
                Name = Pick(Airlines) + RandInt(10, 99), Pos = pos, Angle = heading, Target = heading,
                Speed = (heavy ? 15 : 18) + MathF.Min(Level, 8) * 0.8f + Rand(-1, 1),
                Fuel = MathF.Max(40, 95 - Level * 4) + Rand(0, 15), Heavy = heavy, PaintPos = pos,
            };
            _planes.Add(plane);
            _selected ??= plane;
            Sound.Play(Sfx.Beep, 0.4f, 0.5f);
            Notice(plane.Name + " ENTERING", Phosphor);
            return;
        }
    }

    private Plane PlaneAt(Vector2 p, float range)
    {
        Plane best = null;
        float bd = range * range;
        foreach (var pl in _planes)
        {
            float d = Vector2.DistanceSquared(pl.Pos, p);
            if (d < bd)
            {
                bd = d;
                best = pl;
            }
        }
        return best;
    }

    private static readonly RectF TurnLeftRect = new(12, 266, 70, 36);
    private static readonly RectF TurnRightRect = new(88, 266, 70, 36);
    private static readonly RectF NextRect = new(12, 308, 146, 34);

    private void HandleInput()
    {
        if (_selected != null && !_planes.Contains(_selected))
            _selected = null;
        bool has = _selected != null;
        bool left = Ui.Button(TurnLeftRect, "< 30", Keys.None, has);
        bool right = Ui.Button(TurnRightRect, "30 >", Keys.None, has);
        bool next = Ui.Button(NextRect, "NEXT PLANE", Keys.Tab, _planes.Count > 0);
        if (next || In.UpPressed || In.DownPressed)
            CycleSelection(In.UpPressed ? -1 : 1);
        if (_selected != null)
        {
            if (left || right)
            {
                _selected.Path.Clear();
                _selected.Target = _selected.Target + (left ? -1 : 1) * MathF.PI / 6;
                Sound.Play(Sfx.Select, left ? -0.2f : 0.2f, 0.5f);
            }
            if (In.Left || In.Right)
            {
                _selected.Path.Clear();
                _selected.Target = _selected.Angle + (In.Left ? -0.6f : 0.6f);
                if (In.LeftPressed || In.RightPressed)
                    Sound.Play(Sfx.Select, 0, 0.3f);
            }
        }

        // Pointer: press on a plane to select it and draw a path; tap elsewhere to steer.
        if (In.PointerPressed)
        {
            var hit = PlaneAt(In.Pointer, 20);
            if (hit != null)
            {
                _selected = hit;
                _drawing = true;
                hit.Path.Clear();
                Sound.Play(Sfx.Select, 0.4f, 0.5f);
            }
            else if (_selected != null && Vector2.Distance(In.Pointer, Centre) < ScopeR + 10)
            {
                _selected.Path.Clear();
                _selected.Path.Add(In.Pointer);
                Sound.Play(Sfx.Beep, 0.6f, 0.3f);
            }
        }
        if (_drawing)
        {
            if (_selected == null || (!In.PointerDown && !In.PointerReleased))
            {
                _drawing = false;
            }
            else
            {
                var path = _selected.Path;
                var last = path.Count > 0 ? path[^1] : _selected.Pos;
                if (Vector2.Distance(last, In.Pointer) >= 7 && path.Count < 120)
                    path.Add(In.Pointer);
                if (In.PointerReleased)
                    _drawing = false;
            }
        }
    }

    private void CycleSelection(int dir)
    {
        if (_planes.Count == 0)
            return;
        int i = _selected == null ? -1 : _planes.IndexOf(_selected);
        i = ((i + dir) % _planes.Count + _planes.Count) % _planes.Count;
        _selected = _planes[i];
        Sound.Play(Sfx.Select, 0.3f, 0.4f);
    }

    private void MovePlanes(float sweepBefore, bool wrapped)
    {
        _historyTimer -= Dt;
        bool history = _historyTimer <= 0;
        if (history)
            _historyTimer = 0.9f;
        for (int i = _planes.Count - 1; i >= 0; i--)
        {
            var p = _planes[i];
            p.Age += Dt;
            p.Fuel -= Dt;
            p.Paint = MathF.Max(0, p.Paint - Dt / SweepPeriod);

            // Follow the path, if any.
            // Pure pursuit: drop reached (or passed) points and chase one a little way ahead.
            if (p.Path.Count > 1)
            {
                // Skip ahead to the nearest of the next few points.
                int nearest = 0;
                float nd = float.MaxValue;
                for (int k = 0; k < Math.Min(16, p.Path.Count); k++)
                {
                    float d = Vector2.DistanceSquared(p.Path[k], p.Pos);
                    if (d < nd)
                    {
                        nd = d;
                        nearest = k;
                    }
                }
                if (nearest > 0)
                    p.Path.RemoveRange(0, nearest);
            }
            while (p.Path.Count > 0 && Vector2.DistanceSquared(p.Path[0], p.Pos) < 8 * 8)
                p.Path.RemoveAt(0);
            if (p.Path.Count > 0)
            {
                var aim = p.Path[^1];
                for (int k = 0; k < p.Path.Count; k++)
                    if (Vector2.DistanceSquared(p.Path[k], p.Pos) >= 18 * 18)
                    {
                        aim = p.Path[k];
                        break;
                    }
                p.Target = MathF2.Angle(aim - p.Pos);
            }
            float diff = MathF2.WrapAngle(p.Target - p.Angle);
            float turn = TurnRate * (p.Heavy ? 0.75f : 1f) * Dt;
            p.Angle = MathF2.WrapAngle(p.Angle + MathF2.Clamp(diff, -turn, turn));
            p.Pos += MathF2.FromAngle(p.Angle, p.Speed * Dt);

            if (history)
            {
                p.History.Add(p.Pos);
                if (p.History.Count > 6)
                    p.History.RemoveAt(0);
            }

            // Painted by the sweep?
            float bearing = Backdrops.Mod(MathF2.Angle(p.Pos - Centre), MathF2.Tau);
            bool passed = wrapped ? bearing >= sweepBefore || bearing < _sweep : bearing >= sweepBefore && bearing < _sweep;
            if (passed)
            {
                p.Paint = 1;
                p.PaintPos = p.Pos;
            }

            // Landing.
            bool landed = false;
            foreach (var rw in Runways)
            {
                float align = MathF.Abs(MathF2.WrapAngle(p.Angle - rw.Angle));
                if (align < 0.45f && Vector2.Distance(p.Pos, rw.Threshold) < 9)
                {
                    int pts = 100 + (int)p.Fuel * 2;
                    AddScore(pts, p.Pos.X, p.Pos.Y - 12, Pal.Lime);
                    _landed++;
                    Status = "LANDED " + _landed;
                    Fx.Burst(p.Pos.X, p.Pos.Y, Phosphor, 18, 70, 0.6f, 1.6f);
                    Sound.Play(Sfx.Bell, 0.2f, 0.7f);
                    Notice(p.Name + " LANDED RWY " + rw.Name, Pal.Lime);
                    landed = true;
                    break;
                }
            }
            if (landed)
            {
                Remove(i);
                continue;
            }

            if (p.Fuel <= 0)
            {
                Fx.Explode(p.Pos.X, p.Pos.Y, 0.8f);
                Sound.Play(Sfx.Explode);
                Notice(p.Name + " OUT OF FUEL!", Pal.Red);
                Remove(i);
                if (LoseLife())
                    return;
                continue;
            }
            if (Vector2.Distance(p.Pos, Centre) > ScopeR + 6 && p.Age > 3)
            {
                Sound.Play(Sfx.Wrong);
                Notice(p.Name + " LEFT YOUR AIRSPACE", Pal.Orange);
                Remove(i);
                if (LoseLife())
                    return;
            }
        }
    }

    private void Remove(int i)
    {
        if (_selected == _planes[i])
            _selected = null;
        if (_autoPlane == _planes[i])
            _autoPlane = null;
        _planes.RemoveAt(i);
        if (_selected == null && _planes.Count > 0)
            _selected = _planes[0];
    }

    private void Notice(string text, Color c)
    {
        _notice = text;
        _noticeColour = c;
        _noticeTimer = 2.5f;
    }

    private void CheckSeparation()
    {
        bool warn = false;
        for (int i = 0; i < _planes.Count; i++)
            for (int j = i + 1; j < _planes.Count; j++)
            {
                float d = Vector2.Distance(_planes[i].Pos, _planes[j].Pos);
                if (d < CollideDist)
                {
                    _crashAt = (_planes[i].Pos + _planes[j].Pos) / 2;
                    Fx.Explode(_crashAt.X, _crashAt.Y, 2f);
                    Sound.Play(Sfx.BigExplode);
                    Notice("MID-AIR COLLISION!", Pal.Red);
                    Lives = 0;
                    EndGame(false, $"{_planes[i].Name} and {_planes[j].Name} collided!");
                    return;
                }
                if (d < WarnDist)
                    warn = true;
            }
        _alarmTimer -= Dt;
        if (warn && _alarmTimer <= 0)
        {
            _alarmTimer = 0.9f;
            Sound.Play(Sfx.Alarm, 0.3f, 0.45f);
        }
    }

    // ------------------------------------------------------------------ drawing

    private static void DrawScope(Gfx g, Vector2 c, float r, float sweep, float s)
    {
        g.Glow(c, r * 1.25f, Phosphor, 0.12f);
        g.Circle(c, r + 6 * s, new Color(30, 36, 34));
        g.Circle(c, r + 3 * s, new Color(10, 14, 12));
        g.Circle(c, r, new Color(2, 22, 10));
        g.Glow(c, r, new Color(20, 90, 40), 0.35f);
        for (int k = 1; k <= 4; k++)
            g.Ring(c.X, c.Y, r * k / 4f, 1f * s, Phosphor * 0.18f, 72);
        g.Line(c.X - r, c.Y, c.X + r, c.Y, 1f * s, Phosphor * 0.12f);
        g.Line(c.X, c.Y - r, c.X, c.Y + r, 1f * s, Phosphor * 0.12f);
        for (int d = 0; d < 360; d += 10)
        {
            var dir = MathF2.FromAngle((d - 90) * MathF.PI / 180);
            float len = d % 30 == 0 ? 7 : 3.5f;
            g.Line(c + dir * (r - len * s), c + dir * r, 1f * s, Phosphor * 0.45f);
        }

        // The sweep and its afterglow.
        const int trail = 16;
        for (int k = 0; k < trail; k++)
        {
            float a1 = sweep - k * 0.045f, a0 = a1 - 0.045f;
            g.Pie(c.X, c.Y, r, a0, a1, Pal.Add(Phosphor, 0.16f * (1 - k / (float)trail)), 6);
        }
        var tip = c + MathF2.FromAngle(sweep, r);
        g.Line(c, tip, 2f * s, Pal.Add(Phosphor, 0.5f));
        g.Line(c, tip, 1f * s, Pal.Add(Color.White, 0.5f));
    }

    private static void DrawRunway(Gfx g, Runway rw, float s, Vector2 offset, float scale, bool label)
    {
        var mid = offset + rw.Mid * scale;
        var dir = rw.Dir;
        var thr = offset + rw.Threshold * scale;
        // Approach line.
        for (int k = 1; k <= 9; k++)
        {
            var a = thr - dir * (k * 11 * scale);
            var b = thr - dir * ((k * 11 + 5) * scale);
            g.Line(a, b, 1.2f * s, Phosphor * (0.5f - k * 0.04f));
        }
        g.RotatedRect(mid, 60 * scale, 7 * scale, rw.Angle, Phosphor * 0.5f);
        g.RotatedRect(mid, 56 * scale, 3 * scale, rw.Angle, new Color(10, 40, 18));
        g.RotatedRect(thr, 2 * scale, 9 * scale, rw.Angle, Pal.Lime);
        g.Glow(thr, 8 * scale, Pal.Lime, 0.5f);
        if (label)
        {
            var lp = thr - dir * 22 * scale + new Vector2(-dir.Y, dir.X) * 9 * scale;
            g.Text(rw.Name, lp.X, lp.Y - 4, 1f, Phosphor, Align.Center);
        }
    }

    private static void DrawPlaneSymbol(Gfx g, Vector2 p, float angle, Color c, float s)
    {
        var f = MathF2.FromAngle(angle, 5 * s);
        var side = new Vector2(-f.Y, f.X) * 0.7f;
        g.Line(p - f, p + f, 1.5f * s, c);
        g.Line(p - side * 1.1f + f * 0.1f, p + side * 1.1f + f * 0.1f, 1.5f * s, c);
        g.Line(p - f * 0.9f - side * 0.5f, p - f * 0.9f + side * 0.5f, 1.2f * s, c);
    }

    public override void Draw(Gfx g)
    {
        // Console.
        g.GradientV(0, 0, 640, 360, new Color(28, 34, 34), new Color(12, 16, 16));
        for (int y = 30; y < 360; y += 4)
            g.Rect(0, y, 640, 1, Color.Black * 0.12f);
        DrawScope(g, Centre, ScopeR, _sweep, 1);

        foreach (var rw in Runways)
            DrawRunway(g, rw, 1, Vector2.Zero, 1, true);

        // Conflict lines.
        for (int i = 0; i < _planes.Count; i++)
            for (int j = i + 1; j < _planes.Count; j++)
                if (Vector2.Distance(_planes[i].Pos, _planes[j].Pos) < WarnDist * 1.6f)
                {
                    bool close = Vector2.Distance(_planes[i].Pos, _planes[j].Pos) < WarnDist;
                    var col = close && (int)(Time * 6) % 2 == 0 ? Pal.Red : Pal.Orange * 0.6f;
                    g.Line(_planes[i].Pos, _planes[j].Pos, 1.2f, col);
                    if (close)
                    {
                        g.Ring(_planes[i].Pos.X, _planes[i].Pos.Y, 12, 1.2f, Pal.Red);
                        g.Ring(_planes[j].Pos.X, _planes[j].Pos.Y, 12, 1.2f, Pal.Red);
                    }
                }

        foreach (var p in _planes)
        {
            bool sel = p == _selected;
            // Path.
            for (int k = 0; k < p.Path.Count; k++)
                if (k % 2 == 0)
                    g.Circle(p.Path[k], sel ? 1.4f : 1f, (sel ? Pal.Yellow : Phosphor) * 0.7f);
            // History dots fade out.
            for (int k = 0; k < p.History.Count; k++)
                g.Circle(p.History[k], 1.2f, Phosphor * (0.1f + 0.08f * k));
            // The sweep's bright paint.
            if (p.Paint > 0)
            {
                g.Glow(p.PaintPos, 14, Phosphor, 0.9f * p.Paint);
                g.Circle(p.PaintPos, 2.5f, Pal.Lighten(Phosphor, 0.6f) * p.Paint);
            }
            bool lowFuel = p.Fuel < 15;
            var col = sel ? Pal.Yellow : lowFuel && (int)(Time * 4) % 2 == 0 ? Pal.Red : Phosphor;
            DrawPlaneSymbol(g, p.Pos, p.Angle, col, 1);
            // Leader line and data tag.
            bool flipTag = p.Pos.X > Centre.X + 80;
            var tag = p.Pos + new Vector2(flipTag ? -10 : 10, -16);
            var align = flipTag ? Align.Right : Align.Left;
            g.Line(p.Pos + new Vector2(flipTag ? -4 : 4, -4), tag + new Vector2(0, 10), 1f, col * 0.5f);
            int hdg = (int)Backdrops.Mod(MathF.Round(p.Angle * 180 / MathF.PI + 90), 360);
            g.Text(p.Name, tag.X, tag.Y, 1f, col, align);
            g.Text($"{hdg:000} F{Math.Max(0, (int)p.Fuel)}", tag.X, tag.Y + 9, 1f, lowFuel ? Pal.Red : col * 0.8f, align);
            if (sel)
            {
                float pulse = MathF2.Pulse(Time, 0.8f);
                g.Ring(p.Pos.X, p.Pos.Y, 10 + pulse * 3, 1.2f, Pal.Yellow * (0.9f - pulse * 0.4f));
                if (p.Path.Count == 0)
                {
                    var aim = p.Pos + MathF2.FromAngle(p.Target, 26);
                    g.Line(p.Pos, aim, 1f, Pal.Yellow * 0.4f);
                }
            }
        }

        DrawSidePanels(g);

        if (_noticeTimer > 0)
        {
            float a = MathF.Min(1, _noticeTimer * 2);
            g.TextShadow(_notice, Centre.X, 344, 1.5f, _noticeColour * a, Align.Center);
        }
    }

    private void DrawSidePanels(Gfx g)
    {
        var panel = new RectF(8, 30, 154, 228);
        g.Panel(panel, new Color(14, 20, 18), new Color(50, 70, 60), 6);
        g.Text("SELECTED", 85, 38, 1.25f, Phosphor * 0.7f, Align.Center);
        if (_selected != null)
        {
            var p = _selected;
            int hdg = (int)Backdrops.Mod(MathF.Round(p.Angle * 180 / MathF.PI + 90), 360);
            g.Text(p.Name, 85, 54, 2.5f, Pal.Yellow, Align.Center);
            g.Text($"HDG  {hdg:000}", 20, 84, 1.5f, Phosphor);
            g.Text($"FUEL {Math.Max(0, (int)p.Fuel)}", 20, 102, 1.5f, p.Fuel < 15 ? Pal.Red : Phosphor);
            g.Text(p.Heavy ? "HEAVY" : "MEDIUM", 20, 120, 1.5f, Phosphor * 0.8f);
            g.Text(p.Path.Count > 0 ? "ON PATH" : "VECTORS", 20, 138, 1.5f, Phosphor * 0.8f);
        }
        else
        {
            g.Text("NONE", 85, 60, 2f, Phosphor * 0.5f, Align.Center);
        }
        g.Rect(18, 160, 134, 1, Phosphor * 0.3f);
        g.Text("TRAFFIC " + _planes.Count, 20, 170, 1.5f, Phosphor);
        g.Text("LANDED  " + _landed, 20, 188, 1.5f, Phosphor);
        g.Text("RUNWAYS 08 33", 20, 206, 1.25f, Phosphor * 0.7f);
        g.Text("LAND ALONG THE", 85, 226, 1f, Phosphor * 0.55f, Align.Center);
        g.Text("DASHED LINES", 85, 237, 1f, Phosphor * 0.55f, Align.Center);

        // Right: clock and a simple strip board.
        var right = new RectF(500, 30, 132, 312);
        g.Panel(right, new Color(14, 20, 18), new Color(50, 70, 60), 6);
        int secs = (int)Time;
        g.Text($"{secs / 60:00}:{secs % 60:00}", 566, 40, 2f, Pal.Orange, Align.Center);
        g.Text("STRIPS", 566, 62, 1.25f, Phosphor * 0.7f, Align.Center);
        int shown = 0;
        foreach (var p in _planes)
        {
            if (shown >= 9)
                break;
            float y = 78 + shown * 29;
            bool sel = p == _selected;
            g.RoundRect(508, y, 116, 25, 3, sel ? new Color(70, 64, 20) : new Color(24, 40, 30));
            g.Text(p.Name, 514, y + 4, 1.25f, sel ? Pal.Yellow : Phosphor);
            float fuelFrac = MathF2.Clamp(p.Fuel / 100f, 0, 1);
            g.Rect(514, y + 17, 104, 3, Color.Black * 0.5f);
            g.Rect(514, y + 17, 104 * fuelFrac, 3, p.Fuel < 15 ? Pal.Red : p.Fuel < 30 ? Pal.Orange : Phosphor);
            shown++;
        }
    }

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        g.GradientV(r.X, r.Y, r.W, r.H, new Color(28, 34, 34), new Color(12, 16, 16));
        float s = r.H / 70f;
        float rad = r.H * 0.46f;
        var c = new Vector2(r.CenterX, r.CenterY);
        float sweep = Backdrops.Mod(time * MathF2.Tau / 3f, MathF2.Tau);
        DrawScope(g, c, rad, sweep, s * 0.6f);
        float k = rad / ScopeR;
        var off = c - Centre * k;
        foreach (var rw in Runways)
            DrawRunway(g, rw, s * 0.6f, off, k, false);
        // Three blips: one on approach, two crossing.
        for (int i = 0; i < 3; i++)
        {
            Vector2 p;
            float ang;
            if (i == 0)
            {
                float u = Backdrops.Mod(time * 0.12f, 1f);
                var rw = Runways[0];
                p = off + (rw.Threshold - rw.Dir * (110 * (1 - u))) * k;
                ang = rw.Angle;
            }
            else
            {
                float a = time * 0.15f * (i == 1 ? 1 : -1) + i * 2;
                p = c + MathF2.FromAngle(a, rad * (0.55f + 0.15f * i));
                ang = a + MathF.PI / 2 * (i == 1 ? 1 : -1);
            }
            float bearing = Backdrops.Mod(MathF2.Angle(p - c), MathF2.Tau);
            float since = Backdrops.Mod(sweep - bearing, MathF2.Tau) / MathF2.Tau;
            g.Glow(p, 10 * s, Phosphor, 0.9f * (1 - since));
            DrawPlaneSymbol(g, p, ang, i == 0 ? Pal.Yellow : Phosphor, s * 0.9f);
        }
    }

    // ------------------------------------------------------------------ autopilot

    public override void AutoPlay(Controls c)
    {
        if (_autoCooldown > 0)
        {
            _autoCooldown--;
            return;
        }
        // Drawing a route: press on the plane, drag through the waypoints, release.
        if (_autoPlane != null && _autoRoute.Count > 0)
        {
            if (!_planes.Contains(_autoPlane))
            {
                _autoPlane = null;
                return;
            }
            if (_autoStep == 0)
            {
                c.Pointer = _autoPlane.Pos;
                c.PointerPressed = c.PointerDown = true;
                _autoStep = 1;
                return;
            }
            int idx = _autoStep - 1;
            if (idx < _autoRoute.Count)
            {
                c.Pointer = _autoRoute[idx];
                c.PointerDown = true;
                _autoStep++;
                return;
            }
            c.Pointer = _autoRoute[^1];
            c.PointerReleased = true;
            _autoRoute.Clear();
            _autoStep = 0;
            _autoCooldown = 20;
            return;
        }

        // Pick a plane with no route and plan an approach to the runway it is best placed for.
        foreach (var p in _planes)
        {
            if (p.Path.Count > 0 || p.Age < 1.5f)
                continue;
            Runway best = Runways[0];
            float bd = float.MaxValue;
            foreach (var rw in Runways)
            {
                var entry = rw.Threshold - rw.Dir * 70;
                float d = Vector2.Distance(p.Pos, entry);
                if (d < bd)
                {
                    bd = d;
                    best = rw;
                }
            }
            var gate = best.Threshold - best.Dir * 70;
            var final = best.Threshold - best.Dir * 30;
            // Swing wide if the plane would have to turn back on itself near the gate.
            var side = new Vector2(-best.Dir.Y, best.Dir.X);
            var start = p.Pos + MathF2.FromAngle(p.Angle, p.Speed * 0.5f);
            var toPlane = start - gate;
            _autoRoute.Clear();
            if (Vector2.Dot(toPlane, best.Dir) > -25)
            {
                Vector2 wide = gate - best.Dir * 25 + side * MathF.Sign(Vector2.Dot(toPlane, side) + 0.01f) * 40;
                AddLeg(start, wide);
                AddLeg(wide, gate);
            }
            else
            {
                AddLeg(start, gate);
            }
            AddLeg(gate, final);
            AddLeg(final, best.Threshold + best.Dir * 6);
            _autoPlane = p;
            _autoStep = 0;
            return;
        }
    }

    private void AddLeg(Vector2 a, Vector2 b)
    {
        float len = Vector2.Distance(a, b);
        int n = Math.Max(1, (int)(len / 8));
        for (int i = 1; i <= n; i++)
            _autoRoute.Add(Vector2.Lerp(a, b, i / (float)n));
    }
}
