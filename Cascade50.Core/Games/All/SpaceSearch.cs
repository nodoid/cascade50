using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 42 Space Search: hunt cloaked alien motherships on a 10x10 tactical grid. Sonar probes report the
/// range to the nearest ship; triangulate, then fire a torpedo. Later sectors have more ships, and
/// ships that slip away after a miss.
/// </summary>
public sealed class SpaceSearch : MiniGame, Cascade50.Core.Capture.ICaptureHints
{
    public override int Number => 42;
    public override string Title => "Space Search";
    public override Category Category => Category.Puzzle;
    public override string Tagline => "Triangulate the cloaked alien mothership, then fire a torpedo.";
    public override Color Accent => Pal.Lime;

    public override string[] HowToPlay =>
    [
        "A probe shows the range to the nearest cloaked ship. Where the rings cross, the ship is hiding.",
        "Torpedo it before you run out. Later ships slip away after a miss, and come in fleets.",
        "Spare probes and torpedoes score bonuses.",
    ];

    public override string[] DesktopControls => ["Click a square, or ARROWS to aim.", "SPACE probe, X torpedo."];
    public override string[] TouchControls => ["Pick PROBE or TORPEDO,", "then tap a square."];
    public int CaptureTicks => 330;

    public override Pad Pad => Pad.None;

    // ------------------------------------------------------------------ layout

    private const int N = 10;
    private const float Cell = 28;
    private const float GridX = 46, GridY = 42;
    private static readonly RectF GridRect = new(GridX, GridY, N * Cell, N * Cell);
    private static readonly RectF ProbeButton = new(350, 296, 132, 50);
    private static readonly RectF TorpButton = new(492, 296, 136, 50);

    private struct Reading
    {
        public int X, Y;
        public float Range;
        public bool Stale;
    }

    private enum Mark : byte { None, Miss, Wreck }

    private readonly List<Point> _ships = new();
    private readonly List<Reading> _readings = new();
    private readonly Mark[,] _marks = new Mark[N, N];
    private int _probes, _torps;
    private bool _torpMode;
    private Point _cursor = new(4, 4);
    private float _busy;
    private int _sector;
    private bool _jumpy;
    private int _startShips;

    // Animation.
    private Vector2 _pulseAt;
    private float _pulseT = -1, _pulseRange;
    private Vector2 _torpFrom, _torpTo;
    private float _torpT = -1;
    private bool _torpPending;
    private Point _torpCell;
    private float _revealT;
    private bool _failed;
    private float _banner;
    private string _bannerText;
    private Color _bannerColour;
    private float _shiftFlash;

    private static readonly Dictionary<char, Color> Colours = new()
    {
        ['g'] = new Color(120, 255, 140), ['G'] = new Color(40, 140, 70), ['r'] = Pal.Red, ['w'] = Pal.White, ['k'] = new Color(20, 40, 30),
    };

    private static readonly PixelArt Mothership = new(
    [
        "....gggggg....",
        "..gGwwwwwwGg..",
        ".gGGGGGGGGGGg.",
        "gGrGGrGGrGGrGg",
        ".gGGGGGGGGGGg.",
        "..gg.gggg.gg..",
        ".g....gg....g.",
    ], Colours);

    protected override void Start()
    {
        Lives = 3;
        _sector = 1;
        NewSector();
    }

    private void NewSector()
    {
        Level = _sector;
        int ships = Math.Min(4, 1 + (_sector - 1) / 2);
        _jumpy = _sector % 2 == 0 || _sector >= 5;
        _ships.Clear();
        while (_ships.Count < ships)
        {
            var p = new Point(RandInt(0, N), RandInt(0, N));
            if (!_ships.Contains(p))
                _ships.Add(p);
        }
        _startShips = ships;
        _readings.Clear();
        Array.Clear(_marks);
        _probes = Math.Max(ships + 1, 4 + ships * 2 - Math.Max(0, (_sector - 4) / 2));
        _torps = ships + (_sector >= 8 ? 1 : 2);
        _torpMode = false;
        _failed = false;
        _revealT = 0;
        _busy = 0.8f;
        Banner($"SECTOR {_sector}: {ships} SHIP{(ships > 1 ? "S" : "")}{(_jumpy ? ", EVASIVE" : "")}", Pal.Lime, 2.4f);
        UpdateStatus();
    }

    private void UpdateStatus() => Status = $"SECTOR {_sector}  SHIPS {_ships.Count}";

    private void Banner(string text, Color c, float t = 1.6f)
    {
        _bannerText = text;
        _bannerColour = c;
        _banner = t;
    }

    private static Vector2 CellCentre(int x, int y) => new(GridX + (x + 0.5f) * Cell, GridY + (y + 0.5f) * Cell);

    private static bool CellAt(Vector2 p, out Point cell)
    {
        cell = new Point((int)MathF.Floor((p.X - GridX) / Cell), (int)MathF.Floor((p.Y - GridY) / Cell));
        return cell.X >= 0 && cell.Y >= 0 && cell.X < N && cell.Y < N;
    }

    private float NearestRange(int x, int y)
    {
        float best = float.MaxValue;
        foreach (var s in _ships)
            best = MathF.Min(best, MathF.Sqrt((s.X - x) * (s.X - x) + (s.Y - y) * (s.Y - y)));
        return MathF.Round(best * 10) / 10;
    }

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        if (_banner > 0)
            _banner -= Dt;
        if (_shiftFlash > 0)
            _shiftFlash -= Dt;
        if (_pulseT >= 0)
        {
            _pulseT += Dt;
            if (_pulseT > 1.2f)
                _pulseT = -1;
        }
        if (_torpT >= 0)
        {
            _torpT += Dt / 0.7f;
            if (Tick % 2 == 0)
            {
                var p = Vector2.Lerp(_torpFrom, _torpTo, MathF.Min(1, _torpT));
                Fx.Spark(p.X, p.Y, Rand(-20, 20), Rand(10, 40), Pal.Orange, 0.4f, 1.8f);
            }
            if (_torpT >= 1)
            {
                _torpT = -1;
                if (_torpPending)
                    Detonate(_torpCell);
            }
        }

        // Mode buttons (always drawn).
        bool ready = _busy <= 0 && _revealT <= 0;
        bool probeBtn = Ui.Button(ProbeButton, $"PROBE {_probes}", Keys.D1, _probes > 0 && ready, Pal.Darken(Pal.Teal, 0.2f), !_torpMode);
        bool torpBtn = Ui.Button(TorpButton, $"TORPEDO {_torps}", Keys.D2, _torps > 0 && ready, Pal.Darken(Pal.Red, 0.45f), _torpMode);

        // End of sector (cleared or failed): show the result, then move on.
        if (_revealT > 0)
        {
            _revealT -= Dt;
            if (_revealT <= 0)
            {
                if (_failed && LoseLife())
                {
                    EndGame(false, "The aliens slipped away");
                    return;
                }
                if (!_failed)
                    _sector++;
                NewSector();
            }
            return;
        }

        if (probeBtn)
            _torpMode = false;
        if (torpBtn)
            _torpMode = true;
        if (_probes == 0)
            _torpMode = true;

        // Cursor: hover, arrows.
        if (In.HasHover && In.PointerMoved && !IsTouch && CellAt(In.Pointer, out var hc))
            _cursor = hc;
        if (In.LeftPressed) _cursor.X = Math.Max(0, _cursor.X - 1);
        if (In.RightPressed) _cursor.X = Math.Min(N - 1, _cursor.X + 1);
        if (In.UpPressed) _cursor.Y = Math.Max(0, _cursor.Y - 1);
        if (In.DownPressed) _cursor.Y = Math.Min(N - 1, _cursor.Y + 1);

        if (_busy > 0)
        {
            _busy -= Dt;
            return;
        }

        if (In.PointerPressed && CellAt(In.Pointer, out var tc))
        {
            _cursor = tc;
            if (_torpMode)
                Torpedo(tc);
            else
                Probe(tc);
        }
        else if (In.FirePressed)
        {
            if (_probes > 0)
                Probe(_cursor);
            else
                Torpedo(_cursor);
        }
        else if (In.AltPressed)
        {
            Torpedo(_cursor);
        }
    }

    private void Probe(Point c)
    {
        if (_probes <= 0)
        {
            Sound.Play(Sfx.Wrong, 0, 0.5f);
            return;
        }
        _probes--;
        float range = NearestRange(c.X, c.Y);
        _readings.Add(new Reading { X = c.X, Y = c.Y, Range = range });
        _pulseAt = CellCentre(c.X, c.Y);
        _pulseRange = range;
        _pulseT = 0;
        _busy = 0.6f;
        Sound.Play(Sfx.Zap, 0.5f, 0.5f);
        Sound.Play(Sfx.Beep, MathF2.Clamp(0.8f - range * 0.12f, -0.8f, 0.8f));
        if (range == 0)
        {
            Banner("DIRECT CONTACT!", Pal.Yellow);
            Sound.Play(Sfx.Alarm, 0.4f, 0.6f);
        }
        if (_probes == 0)
        {
            _torpMode = true;
            Banner("OUT OF PROBES - TORPEDOES ONLY", Pal.Orange);
        }
    }

    private void Torpedo(Point c)
    {
        if (_torps <= 0 || _torpT >= 0)
            return;
        if (_marks[c.X, c.Y] != Mark.None)
        {
            Sound.Play(Sfx.Wrong, 0, 0.5f);
            return;
        }
        _torps--;
        _torpFrom = new Vector2(GridX + N * Cell / 2, 352);
        _torpTo = CellCentre(c.X, c.Y);
        _torpT = 0;
        _torpPending = true;
        _torpCell = c;
        _busy = 1.1f;
        Sound.Play(Sfx.Cannon, 0.3f, 0.7f);
        Sound.Play(Sfx.Whoosh, 0.2f, 0.6f);
    }

    private void Detonate(Point c)
    {
        _torpPending = false;
        var p = CellCentre(c.X, c.Y);
        int hit = _ships.IndexOf(c);
        if (hit >= 0)
        {
            _ships.RemoveAt(hit);
            _marks[c.X, c.Y] = Mark.Wreck;
            Fx.Explode(p.X, p.Y, 1.8f);
            Fx.Burst(p.X, p.Y, Pal.Lime, 30, 150, 0.9f);
            Sound.Play(Sfx.BigExplode);
            AddScore(200 * _sector, p.X, p.Y - 12, Pal.Lime);
            Banner("DIRECT HIT!", Pal.Lime);
            StaleAll();
            UpdateStatus();
            if (_ships.Count == 0)
            {
                int bonus = (_probes * 50 + _torps * 100) * _sector;
                if (bonus > 0)
                    AddScore(bonus, GridRect.CenterX, GridRect.CenterY + 30, Pal.Gold);
                Banner("SECTOR CLEAR!", Pal.Gold, 2.5f);
                Sound.Play(Sfx.LevelUp);
                _revealT = 2.6f;
            }
            return;
        }
        _marks[c.X, c.Y] = Mark.Miss;
        Fx.Burst(p.X, p.Y, Pal.Orange, 18, 90, 0.5f);
        Sound.Play(Sfx.Explode, 0.3f, 0.6f);
        Banner("MISS", Pal.Orange, 1);

        if (_jumpy && _ships.Count > 0)
        {
            bool moved = false;
            for (int i = 0; i < _ships.Count; i++)
            {
                if (!Chance(0.65f))
                    continue;
                var s = _ships[i];
                for (int tries = 0; tries < 8; tries++)
                {
                    var n = new Point(s.X + RandInt(-1, 2), s.Y + RandInt(-1, 2));
                    if (n == s || n.X < 0 || n.Y < 0 || n.X >= N || n.Y >= N || _ships.Contains(n) || _marks[n.X, n.Y] == Mark.Wreck)
                        continue;
                    _ships[i] = n;
                    moved = true;
                    break;
                }
            }
            if (moved)
            {
                StaleAll();
                _shiftFlash = 1.2f;
                Banner("CONTACT SHIFTED!", Pal.Magenta, 1.6f);
                Sound.Play(Sfx.Warp, 0.2f, 0.7f);
            }
        }
        if (_torps == 0)
        {
            _failed = true;
            _revealT = 3;
            Banner("OUT OF TORPEDOES!", Pal.Red, 3);
            Sound.Play(Sfx.Lose);
        }
    }

    private void StaleAll()
    {
        for (int i = 0; i < _readings.Count; i++)
        {
            var r = _readings[i];
            r.Stale = true;
            _readings[i] = r;
        }
    }

    // ------------------------------------------------------------------ drawing

    private static readonly string Letters = "ABCDEFGHIJ";

    public override void Draw(Gfx g)
    {
        g.GradientV(0, 0, 640, 360, new Color(2, 14, 14), new Color(4, 24, 22));
        Backdrops.Stars(g, Time, 2, 42, Screen.Bounds, 60);
        DrawGrid(g, GridRect, Time, 1);

        // Probe readings: heat-coloured squares with the range, and a ring through every square at that range.
        foreach (var r in _readings)
        {
            var c = CellCentre(r.X, r.Y);
            float a = r.Stale ? 0.3f : 1;
            var col = Heat(r.Range);
            g.Rect(GridX + r.X * Cell + 1, GridY + r.Y * Cell + 1, Cell - 2, Cell - 2, col * (0.45f * a));
            if (r.Range > 0)
                g.Ring(c.X, c.Y, r.Range * Cell, r.Stale ? 1 : 1.6f, col * (0.75f * a), 64);
            g.Text(r.Range.ToString("0.0"), c.X, c.Y - 4, 1, Pal.White * (0.4f + 0.6f * a), Align.Center);
        }
        // Torpedo results.
        for (int x = 0; x < N; x++)
            for (int y = 0; y < N; y++)
            {
                var c = CellCentre(x, y);
                if (_marks[x, y] == Mark.Miss)
                {
                    g.Line(c.X - 6, c.Y - 6, c.X + 6, c.Y + 6, 2, Pal.Orange);
                    g.Line(c.X - 6, c.Y + 6, c.X + 6, c.Y - 6, 2, Pal.Orange);
                }
                else if (_marks[x, y] == Mark.Wreck)
                {
                    g.Glow(c, 18, Pal.Orange, 0.4f + 0.2f * MathF.Sin(Time * 6 + x));
                    g.PixelsCentered(Mothership, c.X, c.Y, 1.8f, false, new Color(70, 60, 50));
                }
            }

        // Sonar pulse.
        if (_pulseT >= 0)
        {
            float k = _pulseT / 1.2f;
            float rad = MathF.Max(6, _pulseRange * Cell) * MathF2.EaseOut(MathF.Min(1, k * 1.5f));
            g.Ring(_pulseAt.X, _pulseAt.Y, rad, 3, Heat(_pulseRange) * (1 - k), 64);
            g.Glow(_pulseAt, 30, Heat(_pulseRange), 0.8f * (1 - k));
        }

        // Ships revealed at the end of a failed sector, shimmering when cloaked.
        if (_revealT > 0 && _failed)
            foreach (var s in _ships)
            {
                var c = CellCentre(s.X, s.Y);
                g.Glow(c, 20, Pal.Lime, 0.5f);
                g.PixelsCentered(Mothership, c.X, c.Y, 1.8f);
            }
        if (_shiftFlash > 0)
            g.Rect(GridRect, Pal.Magenta * (_shiftFlash * 0.12f));

        // Torpedo in flight.
        if (_torpT >= 0)
        {
            var p = Vector2.Lerp(_torpFrom, _torpTo, MathF.Min(1, _torpT));
            g.Glow(p, 14, Pal.Orange, 0.9f);
            g.Circle(p.X, p.Y, 3, Pal.White);
        }

        // Cursor.
        if (_revealT <= 0)
        {
            var cr = new RectF(GridX + _cursor.X * Cell, GridY + _cursor.Y * Cell, Cell, Cell);
            var cc = _torpMode ? Pal.Red : Pal.Lime;
            float pulse = 0.6f + 0.4f * MathF.Sin(Time * 8);
            g.RectOutline(cr, 2, cc * pulse);
            g.Glow(cr.CenterX, cr.CenterY, 22, cc, 0.25f);
            if (_torpMode)
            {
                g.Line(cr.CenterX - 9, cr.CenterY, cr.CenterX + 9, cr.CenterY, 1, cc);
                g.Line(cr.CenterX, cr.CenterY - 9, cr.CenterX, cr.CenterY + 9, 1, cc);
            }
        }

        DrawPanel(g);

        if (_banner > 0 && _bannerText != null)
        {
            float a = MathF.Min(1, _banner * 2);
            g.RoundRect(GridRect.CenterX - 130, GridRect.CenterY - 16, 260, 32, 6, Color.Black * (0.6f * a));
            g.TextFit(_bannerText, GridRect.CenterX, GridRect.CenterY - 8, 250, 2, _bannerColour * a, Align.Center);
        }
    }

    private static Color Heat(float range)
    {
        float k = MathF2.Clamp(range / 8f, 0, 1);
        return Pal.Hsv(0 + k * 220, 0.85f, 1);
    }

    private static void DrawGrid(Gfx g, RectF r, float time, float s)
    {
        var line = new Color(30, 140, 110);
        g.Rect(r, new Color(4, 30, 26) * 0.9f);
        g.Glow(r.CenterX, r.CenterY, r.W * 0.75f, Pal.Teal, 0.18f);
        float cell = r.W / N;
        for (int i = 0; i <= N; i++)
        {
            g.Rect(r.X + i * cell - 0.5f * s, r.Y, 1 * s, r.H, line * (i % 5 == 0 ? 0.9f : 0.5f));
            g.Rect(r.X, r.Y + i * cell - 0.5f * s, r.W, 1 * s, line * (i % 5 == 0 ? 0.9f : 0.5f));
        }
        // Radar sweep.
        float a = time * 1.4f;
        var c = r.Center;
        float rad = r.W * 0.71f;
        for (int k = 0; k < 10; k++)
            g.Pie(c.X, c.Y, rad, a - (k + 1) * 0.06f, a - k * 0.06f, Pal.Lime * (0.07f * (1 - k / 10f)), 3);
        g.Line(c, c + MathF2.FromAngle(a, rad), 1.2f * s, Pal.Lime * 0.5f);
        g.RectOutline(r.Inflate(2 * s, 2 * s), 1.5f * s, Pal.Lime * 0.7f);
    }

    private void DrawPanel(Gfx g)
    {
        // The sweep spills beyond the grid: cover it with the panel backgrounds.
        g.GradientV(GridRect.Right + 3, 22, 640 - GridRect.Right - 3, 338, new Color(2, 16, 16), new Color(4, 24, 22));
        g.GradientV(0, GridRect.Bottom + 3, GridRect.Right + 3, 360 - GridRect.Bottom - 3, new Color(3, 20, 19), new Color(4, 24, 22));
        g.Rect(-200, 0, GridX - 3 + 200, 360, new Color(2, 18, 17));
        g.Rect(0, 0, GridRect.Right + 3, GridY - 3, new Color(2, 16, 16));
        for (int i = 0; i < N; i++)
        {
            g.Text(Letters[i].ToString(), GridX + (i + 0.5f) * Cell, GridY - 12, 1, Pal.Lime * 0.8f, Align.Center);
            g.Text(i.ToString(), GridX - 10, GridY + (i + 0.5f) * Cell - 4, 1, Pal.Lime * 0.8f, Align.Center);
        }
        g.Rect(GridRect.Right + 3, GridY - 2, 1, GridRect.H + 4, Pal.Lime * 0.2f);

        var p = new RectF(350, 34, 278, 252);
        g.Panel(p, new Color(4, 28, 24) * 0.92f, Pal.Lime * 0.6f, 8);
        g.Text($"SECTOR {_sector}", p.X + 12, p.Y + 10, 2, Pal.Lime);
        g.Text(_jumpy ? "EVASIVE" : "STATIC", p.Right - 12, p.Y + 14, 1, _jumpy ? Pal.Magenta : Pal.Teal, Align.Right);

        g.Text("SHIPS", p.X + 12, p.Y + 40, 1.5f, Pal.LightGrey);
        for (int i = 0; i < _startShips; i++)
        {
            bool alive = i < _ships.Count;
            g.PixelsCentered(Mothership, p.X + 112 + i * 40, p.Y + 46, 1.6f, false, alive ? null : new Color(60, 60, 60));
        }
        g.Text("PROBES", p.X + 12, p.Y + 66, 1.5f, Pal.LightGrey);
        for (int i = 0; i < _probes; i++)
        {
            float x = p.X + 112 + i * 16, y = p.Y + 72;
            g.Circle(x, y, 5, Pal.Cyan);
            g.Ring(x, y, 7, 1, Pal.Cyan * 0.6f);
        }
        g.Text("TORPEDOES", p.X + 12, p.Y + 92, 1.5f, Pal.LightGrey);
        for (int i = 0; i < _torps; i++)
        {
            float x = p.X + 166 + i * 20, y = p.Y + 98;
            g.RoundRect(x - 3, y - 7, 6, 14, 3, Pal.Orange);
            g.Triangle(new Vector2(x - 3, y - 6), new Vector2(x + 3, y - 6), new Vector2(x, y - 11), Pal.Red);
        }

        // Range log.
        g.Rect(p.X + 10, p.Y + 116, p.W - 20, 1, Pal.Lime * 0.3f);
        g.Text("SONAR LOG", p.X + 12, p.Y + 124, 1, Pal.Lime * 0.8f);
        int shown = 0;
        for (int i = _readings.Count - 1; i >= 0 && shown < 5; i--, shown++)
        {
            var r = _readings[i];
            float y = p.Y + 140 + shown * 18;
            var col = r.Stale ? Pal.Grey : Heat(r.Range);
            g.Rect(p.X + 12, y, 10, 10, col);
            g.Text($"{Letters[r.X]}{r.Y}  RANGE {r.Range:0.0}{(r.Stale ? "  OLD" : "")}", p.X + 30, y + 1, 1.5f, r.Stale ? Pal.Grey : Pal.White);
        }
        if (_readings.Count == 0)
            g.Text(IsTouch ? "TAP A SQUARE TO PROBE" : "CLICK A SQUARE TO PROBE", p.X + 12, p.Y + 142, 1, Pal.LightGrey);

        string mode = _torpMode ? "MODE: TORPEDO" : "MODE: PROBE";
        g.Text(mode, p.CenterX, p.Bottom - 16, 1, _torpMode ? Pal.Red : Pal.Cyan, Align.Center);
    }

    // ------------------------------------------------------------------ icon

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        float s = r.H / 70f;
        g.GradientV(r.X, r.Y, r.W, r.H, new Color(2, 14, 14), new Color(4, 26, 24));
        float size = r.H - 10 * s;
        var gr = new RectF(r.CenterX - size / 2, r.Y + 5 * s, size, size);
        g.Rect(gr, new Color(4, 30, 26));
        float cell = size / N;
        for (int i = 0; i <= N; i++)
        {
            g.Rect(gr.X + i * cell, gr.Y, 0.6f * s, gr.H, new Color(30, 140, 110) * 0.6f);
            g.Rect(gr.X, gr.Y + i * cell, gr.W, 0.6f * s, new Color(30, 140, 110) * 0.6f);
        }
        // Three probe rings converging on the hidden ship.
        var ship = new Vector2(gr.X + 6.5f * cell, gr.Y + 3.5f * cell);
        Vector2[] probes = [new(gr.X + 2.5f * cell, gr.Y + 6.5f * cell), new(gr.X + 8.5f * cell, gr.Y + 8.5f * cell), new(gr.X + 3.5f * cell, gr.Y + 1.5f * cell)];
        for (int i = 0; i < probes.Length; i++)
        {
            float d = Vector2.Distance(probes[i], ship);
            float k = MathF2.Clamp((time * 0.6f - i * 0.4f) % 2.4f, 0, 1);
            var col = Heat(d / cell);
            g.Rect(probes[i].X - cell / 2, probes[i].Y - cell / 2, cell, cell, col * 0.6f);
            g.Ring(probes[i].X, probes[i].Y, d * k, 1.2f * s, col * 0.9f, 48);
        }
        float pulse = 0.5f + 0.5f * MathF.Sin(time * 3);
        g.Glow(ship, 14 * s, Pal.Lime, 0.3f + 0.4f * pulse);
        g.PixelsCentered(Mothership, ship.X, ship.Y, 0.9f * s, false, Pal.Lime * (0.25f + 0.5f * pulse));
        // Sweep.
        float a = time * 1.4f;
        g.Line(gr.Center, gr.Center + MathF2.FromAngle(a, size * 0.5f), 1 * s, Pal.Lime * 0.6f);
        g.RectOutline(gr, 1 * s, Pal.Lime * 0.7f);
    }

    // ------------------------------------------------------------------ autopilot

    private int _autoStep;

    public override void AutoPlay(Controls c)
    {
        if (_busy > 0 || _revealT > 0 || _torpT >= 0)
            return;
        _autoStep++;
        if (_autoStep % 40 != 0)
            return;
        // Score every square against the fresh readings.
        Point best = new(-1, -1);
        int bestScore = -1;
        int perfect = 0;
        int fresh = 0;
        foreach (var r in _readings)
            if (!r.Stale)
                fresh++;
        for (int x = 0; x < N; x++)
            for (int y = 0; y < N; y++)
            {
                if (_marks[x, y] != Mark.None)
                    continue;
                bool ok = true;
                int exact = 0;
                foreach (var r in _readings)
                {
                    if (r.Stale)
                        continue;
                    float d = MathF.Round(MathF.Sqrt((x - r.X) * (x - r.X) + (y - r.Y) * (y - r.Y)) * 10) / 10;
                    if (d < r.Range - 0.05f)
                        ok = false;
                    else if (MathF.Abs(d - r.Range) < 0.05f)
                        exact++;
                }
                if (!ok)
                    continue;
                if (exact == fresh && fresh > 0)
                    perfect++;
                int score = exact * 10 + RandInt(0, 3);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = new Point(x, y);
                }
            }
        bool confident = fresh > 0 && perfect == 1 && bestScore >= fresh * 10;
        bool shoot = confident || _probes == 0 || (fresh >= 3 && bestScore >= 20);
        Point target;
        if (shoot && best.X >= 0)
        {
            target = best;
        }
        else
        {
            // Probe somewhere informative: far from the previous probes.
            target = new Point(RandInt(1, N - 1), RandInt(1, N - 1));
            float far = -1;
            for (int k = 0; k < 12; k++)
            {
                var cand = new Point(RandInt(0, N), RandInt(0, N));
                float dmin = 99;
                foreach (var r in _readings)
                    if (!r.Stale)
                        dmin = MathF.Min(dmin, MathF.Abs(cand.X - r.X) + MathF.Abs(cand.Y - r.Y));
                if (dmin > far)
                {
                    far = dmin;
                    target = cand;
                }
            }
            shoot = false;
        }
        _cursor = target;
        c.Pointer = CellCentre(target.X, target.Y);
        if (shoot)
        {
            c.AltPressed = true;
            c.Alt = true;
        }
        else
        {
            c.FirePressed = true;
            c.Fire = true;
        }
    }
}
