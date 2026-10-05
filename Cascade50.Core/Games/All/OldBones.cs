using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 26 Old Bones: a fossil dig. Skeletons of different shapes lie hidden in the rock layers.
/// Each empty hole tells you how many bone pieces touch it. Find every fossil before your digs run out.
/// </summary>
public sealed class OldBones : MiniGame
{
    public override int Number => 26;
    public override string Title => "Old Bones";
    public override Category Category => Category.Puzzle;
    public override string Tagline => "Dig for dinosaur fossils, using clues in the soil.";
    public override Color Accent => Pal.Sand;
    public override Pad Pad => Pad.None;

    public override string[] HowToPlay =>
    [
        "Fossils lie hidden in the ground. An empty hole shows how many bone pieces touch it, diagonals too.",
        "Fossils never touch each other. Find them all before your digs run out.",
        "Rocks break your shovel: 3 digs! Spare digs score a bonus.",
    ];

    public override string[] DesktopControls => ["Click a square to dig.", "Or ARROWS to move, SPACE to dig."];
    public override string[] TouchControls => ["Tap a square to dig it."];

    private const int MaxW = 16, MaxH = 10;
    private const float AreaX = 10, AreaY = 30, AreaW = 450, AreaH = 322;

    private enum Kind : byte { Soil, Bone, Rock }

    private sealed class Fossil
    {
        public string Name;
        public readonly List<Point> Cells = new();
        public int Found;
        public bool Complete;
        public float CompleteTime;
        public Point[] Shape; // normalised for the panel
    }

    private struct Flyer
    {
        public Vector2 From, To;
        public float T, Delay;
    }

    private static readonly (string Name, Point[] Cells)[] Shapes =
    [
        ("FISH", [new(0, 0), new(1, 0), new(2, 0)]),
        ("AMMONITE", [new(0, 0), new(1, 0), new(0, 1)]),
        ("TRILOBITE", [new(0, 0), new(1, 0), new(2, 0), new(1, 1)]),
        ("RAPTOR", [new(0, 0), new(0, 1), new(0, 2), new(1, 2)]),
        ("SKULL", [new(0, 0), new(1, 0), new(0, 1), new(1, 1)]),
        ("PLESIOSAUR", [new(0, 0), new(1, 0), new(2, 0), new(3, 0), new(4, 0)]),
        ("PTEROSAUR", [new(1, 0), new(0, 1), new(1, 1), new(2, 1), new(1, 2)]),
        ("STEGOSAUR", [new(0, 0), new(1, 0), new(1, 1), new(2, 1), new(3, 1)]),
        ("T. REX", [new(0, 0), new(1, 0), new(1, 1), new(2, 1), new(3, 1), new(3, 2)]),
        ("DIPLODOCUS", [new(0, 0), new(1, 0), new(2, 0), new(3, 0), new(4, 0), new(4, 1)]),
        ("TRICERATOPS", [new(0, 0), new(1, 0), new(2, 0), new(0, 1), new(1, 1), new(2, 1)]),
    ];

    private readonly Kind[,] _kind = new Kind[MaxW, MaxH];
    private readonly bool[,] _dug = new bool[MaxW, MaxH];
    private readonly int[,] _num = new int[MaxW, MaxH];
    private readonly int[,] _fossilOf = new int[MaxW, MaxH];
    private readonly float[,] _dugTime = new float[MaxW, MaxH];
    private readonly List<Fossil> _fossils = new();
    private readonly List<Flyer> _flyers = new();
    private readonly List<Point> _stack = new();
    private int _w, _h;
    private int _site;
    private int _digs;
    private int _cx, _cy;
    private int _hx = -1, _hy;
    private float _banner;
    private string _bannerText = "";
    private float _siteDone;
    private float _shovelBreak;
    private float _digFlash;

    // Autoplay.
    private int _apStage;
    private float _apWait;
    private Point _apTarget;

    protected override void Start()
    {
        _site = 0;
        NewSite();
    }

    private void NewSite()
    {
        _site++;
        Level = _site;
        (int w, int h, int[] sizes, int rocks, int spare) = _site switch
        {
            1 => (12, 8, new[] { 3, 4, 5 }, 0, 14),
            2 => (12, 8, new[] { 3, 4, 5, 5 }, 2, 13),
            3 => (13, 9, new[] { 3, 4, 4, 5, 6 }, 3, 13),
            4 => (14, 9, new[] { 3, 4, 5, 5, 6 }, 5, 12),
            5 => (15, 10, new[] { 3, 4, 4, 5, 6, 6 }, 6, 12),
            _ => (16, 10, new[] { 3, 4, 5, 5, 6, 6 }, 6 + _site - 6, Math.Max(6, 17 - _site)),
        };
        _w = w;
        _h = h;
        for (int attempt = 0; attempt < 50; attempt++)
            if (TryLayout(sizes, rocks))
                break;
        int bones = 0;
        foreach (var f in _fossils)
            bones += f.Cells.Count;
        _digs = bones + spare;
        _cx = _w / 2;
        _cy = _h / 2;
        _siteDone = 0;
        _banner = 2.2f;
        _bannerText = "SITE " + _site;
        Status = "DIG SITE " + _site;
        _flyers.Clear();
        _apStage = 0;
        _apWait = 0.6f;
    }

    private bool TryLayout(int[] sizes, int rocks)
    {
        _fossils.Clear();
        for (int x = 0; x < MaxW; x++)
            for (int y = 0; y < MaxH; y++)
            {
                _kind[x, y] = Kind.Soil;
                _dug[x, y] = false;
                _fossilOf[x, y] = -1;
                _num[x, y] = 0;
            }
        foreach (int size in sizes)
        {
            bool placed = false;
            for (int tries = 0; tries < 300 && !placed; tries++)
            {
                var options = new List<int>();
                for (int i = 0; i < Shapes.Length; i++)
                    if (Shapes[i].Cells.Length == size)
                        options.Add(i);
                var (name, baseCells) = Shapes[options[RandInt(0, options.Count)]];
                var cells = Transform(baseCells, RandInt(0, 8));
                int maxX = 0, maxY = 0;
                foreach (var c in cells)
                {
                    maxX = Math.Max(maxX, c.X);
                    maxY = Math.Max(maxY, c.Y);
                }
                if (maxX >= _w || maxY >= _h)
                    continue;
                int ox = RandInt(0, _w - maxX), oy = RandInt(0, _h - maxY);
                bool ok = true;
                foreach (var c in cells)
                    for (int dx = -1; dx <= 1 && ok; dx++)
                        for (int dy = -1; dy <= 1 && ok; dy++)
                        {
                            int x = ox + c.X + dx, y = oy + c.Y + dy;
                            if (x >= 0 && y >= 0 && x < _w && y < _h && _kind[x, y] == Kind.Bone)
                                ok = false;
                        }
                if (!ok)
                    continue;
                var f = new Fossil { Name = name, Shape = cells };
                foreach (var c in cells)
                {
                    var p = new Point(ox + c.X, oy + c.Y);
                    f.Cells.Add(p);
                    _kind[p.X, p.Y] = Kind.Bone;
                    _fossilOf[p.X, p.Y] = _fossils.Count;
                }
                _fossils.Add(f);
                placed = true;
            }
            if (!placed)
                return false;
        }
        for (int i = 0; i < rocks; i++)
            for (int tries = 0; tries < 100; tries++)
            {
                int x = RandInt(0, _w), y = RandInt(0, _h);
                if (_kind[x, y] == Kind.Soil)
                {
                    _kind[x, y] = Kind.Rock;
                    break;
                }
            }
        for (int x = 0; x < _w; x++)
            for (int y = 0; y < _h; y++)
            {
                int n = 0;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        if ((dx != 0 || dy != 0) && Inside(x + dx, y + dy) && _kind[x + dx, y + dy] == Kind.Bone)
                            n++;
                _num[x, y] = n;
            }
        return true;
    }

    private static Point[] Transform(Point[] cells, int t)
    {
        var res = new Point[cells.Length];
        int minX = int.MaxValue, minY = int.MaxValue;
        for (int i = 0; i < cells.Length; i++)
        {
            int x = cells[i].X, y = cells[i].Y;
            for (int r = 0; r < (t & 3); r++)
                (x, y) = (-y, x);
            if ((t & 4) != 0)
                x = -x;
            res[i] = new Point(x, y);
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
        }
        for (int i = 0; i < res.Length; i++)
            res[i] = new Point(res[i].X - minX, res[i].Y - minY);
        return res;
    }

    private bool Inside(int x, int y) => x >= 0 && y >= 0 && x < _w && y < _h;

    // ------------------------------------------------------------------ geometry

    private float CellSize => MathF.Min(34, MathF.Min(AreaW / _w, AreaH / _h));
    private float GridX => AreaX + (AreaW - CellSize * _w) / 2;
    private float GridY => AreaY + (AreaH - CellSize * _h) / 2;
    private Vector2 CellCentre(int x, int y) => new(GridX + (x + 0.5f) * CellSize, GridY + (y + 0.5f) * CellSize);

    private bool CellAt(Vector2 p, out int x, out int y)
    {
        x = (int)MathF.Floor((p.X - GridX) / CellSize);
        y = (int)MathF.Floor((p.Y - GridY) / CellSize);
        return Inside(x, y);
    }

    private static readonly RectF PanelRect = new(468, 30, 162, 322);

    private Vector2 FossilSlot(int i) => new(PanelRect.X + 28, PanelRect.Y + 96 + i * 38);

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        if (_banner > 0)
            _banner -= Dt;
        if (_shovelBreak > 0)
            _shovelBreak -= Dt;
        if (_digFlash > 0)
            _digFlash -= Dt;
        for (int i = _flyers.Count - 1; i >= 0; i--)
        {
            var f = _flyers[i];
            f.T += Dt;
            if (f.T - f.Delay >= 0.7f)
            {
                Fx.Burst(f.To.X, f.To.Y, Pal.Gold, 6, 50, 0.4f, 1.5f);
                _flyers.RemoveAt(i);
            }
            else
                _flyers[i] = f;
        }

        if (_siteDone > 0)
        {
            _siteDone -= Dt;
            if (_siteDone <= 0)
                NewSite();
            return;
        }

        _hx = -1;
        if ((In.HasHover || In.PointerDown) && CellAt(In.Pointer, out int hx, out int hy))
        {
            _hx = hx;
            _hy = hy;
        }

        if (In.LeftPressed) _cx = (_cx + _w - 1) % _w;
        if (In.RightPressed) _cx = (_cx + 1) % _w;
        if (In.UpPressed) _cy = (_cy + _h - 1) % _h;
        if (In.DownPressed) _cy = (_cy + 1) % _h;
        if (In.LeftPressed || In.RightPressed || In.UpPressed || In.DownPressed)
            Sound.Play(Sfx.Tick, 0.3f, 0.3f);
        if (In.PointerMoved && _hx >= 0)
        {
            _cx = _hx;
            _cy = _hy;
        }

        if (In.PointerPressed && CellAt(In.Pointer, out int px, out int py))
        {
            _cx = px;
            _cy = py;
            Dig(px, py);
        }
        else if (In.FirePressed || In.EnterPressed)
            Dig(_cx, _cy);
    }

    private void Dig(int x, int y)
    {
        if (_dug[x, y])
        {
            Sound.Play(Sfx.Wrong, 0, 0.3f);
            return;
        }
        var c = CellCentre(x, y);
        _digs--;
        _digFlash = 0.3f;
        Reveal(x, y);
        switch (_kind[x, y])
        {
            case Kind.Rock:
                _digs = Math.Max(0, _digs - 2);
                _shovelBreak = 1.2f;
                Sound.Play(Sfx.Crack, -0.4f);
                Sound.Play(Sfx.Hit, -0.5f, 0.7f);
                Fx.Burst(c.X, c.Y, Pal.Grey, 20, 120, 0.5f, 2.5f, 200, false);
                Fx.Burst(c.X, c.Y, Pal.Yellow, 8, 90, 0.3f, 1.5f);
                Fx.Float("SHOVEL BROKEN! -3", c.X, c.Y - 10, Pal.Red);
                Fx.Shake(4, 0.3f);
                break;
            case Kind.Bone:
            {
                var f = _fossils[_fossilOf[x, y]];
                f.Found++;
                AddScore(10, c.X, c.Y - 8, Pal.Ice);
                Sound.Play(Sfx.Pickup, -0.2f + f.Found * 0.08f, 0.6f);
                Fx.Burst(c.X, c.Y, new Color(240, 230, 200), 12, 70, 0.5f, 2f);
                if (f.Found == f.Cells.Count)
                    CompleteFossil(f);
                break;
            }
            default:
                Sound.Play(Sfx.Thud, Rand(-0.3f, 0.1f), 0.6f);
                Fx.Burst(c.X, c.Y, new Color(150, 100, 60), 10, 70, 0.4f, 2f, 160, false);
                if (_num[x, y] == 0)
                    Flood(x, y);
                break;
        }

        bool allFound = true;
        foreach (var f in _fossils)
            allFound &= f.Complete;
        if (allFound)
        {
            int bonus = _digs * 25 + 200 * _site;
            AddScore(bonus, AreaX + AreaW / 2, 180, Pal.Gold);
            Sound.Play(Sfx.LevelUp);
            _bannerText = "SITE CLEARED!";
            _banner = 2.4f;
            _siteDone = 2.6f;
            return;
        }
        if (_digs <= 0)
        {
            _digs = 0;
            EndGame(false, "Out of digs!");
        }
    }

    private void Reveal(int x, int y)
    {
        if (_dug[x, y])
            return;
        _dug[x, y] = true;
        _dugTime[x, y] = Time;
    }

    private void Flood(int sx, int sy)
    {
        _stack.Clear();
        _stack.Add(new Point(sx, sy));
        while (_stack.Count > 0)
        {
            var p = _stack[^1];
            _stack.RemoveAt(_stack.Count - 1);
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    int x = p.X + dx, y = p.Y + dy;
                    if (!Inside(x, y) || _dug[x, y] || _kind[x, y] == Kind.Bone)
                        continue;
                    Reveal(x, y);
                    if (_kind[x, y] == Kind.Soil && _num[x, y] == 0)
                        _stack.Add(new Point(x, y));
                }
        }
    }

    private void CompleteFossil(Fossil f)
    {
        f.Complete = true;
        f.CompleteTime = Time;
        int idx = _fossils.IndexOf(f);
        AddScore(50 * f.Cells.Count, CellCentre(f.Cells[0].X, f.Cells[0].Y).X, CellCentre(f.Cells[0].X, f.Cells[0].Y).Y - 20, Pal.Gold);
        Fx.Float(f.Name + "!", AreaX + AreaW / 2, 60, Pal.Sand, 2f);
        Sound.Play(Sfx.Bonus);
        for (int i = 0; i < f.Cells.Count; i++)
        {
            var c = CellCentre(f.Cells[i].X, f.Cells[i].Y);
            Fx.Burst(c.X, c.Y, Pal.Gold, 10, 80, 0.6f, 2f);
            _flyers.Add(new Flyer { From = c, To = FossilSlot(idx), T = 0, Delay = i * 0.08f });
        }
        // The diggers clear the soil around a finished fossil for free.
        foreach (var p in f.Cells)
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    int x = p.X + dx, y = p.Y + dy;
                    if (Inside(x, y) && !_dug[x, y] && _kind[x, y] != Kind.Bone)
                    {
                        Reveal(x, y);
                        if (_kind[x, y] == Kind.Soil && _num[x, y] == 0)
                            Flood(x, y);
                    }
                }
    }

    // ------------------------------------------------------------------ drawing

    private static readonly Color[] Strata =
    [
        new(92, 64, 40), new(120, 80, 46), new(150, 98, 54), new(176, 124, 70), new(160, 120, 80),
        new(134, 112, 92), new(118, 104, 96), new(100, 92, 90), new(86, 80, 82), new(74, 70, 76),
    ];

    private static Color StratumColour(float t)
    {
        t = MathF2.Clamp(t, 0, 0.999f) * (Strata.Length - 1);
        int i = (int)t;
        return Pal.Lerp(Strata[i], Strata[Math.Min(i + 1, Strata.Length - 1)], t - i);
    }

    private static float Hash(int x, int y, int k)
    {
        float v = MathF.Sin(x * 127.1f + y * 311.7f + k * 74.7f) * 43758.547f;
        return v - MathF.Floor(v);
    }

    public override void Draw(Gfx g)
    {
        // Sky and grass at the top of the quarry, then the rock face.
        g.GradientV(0, 0, 640, 360, new Color(60, 44, 30), new Color(30, 24, 22));
        float cs = CellSize, gx = GridX, gy = GridY;
        var frame = new RectF(gx - 6, gy - 6, cs * _w + 12, cs * _h + 12);
        g.RoundRect(frame.Offset(3, 4), 6, Color.Black * 0.5f);
        g.RoundRect(frame, 6, new Color(46, 34, 24));
        // Grass lip along the top.
        g.Rect(frame.X, frame.Y - 2, frame.W, 6, new Color(50, 120, 50));
        for (float x = frame.X; x < frame.Right; x += 4)
            g.Line(x, frame.Y + 2, x + 1, frame.Y - 3 - Hash((int)x, 0, 1) * 3, 1.2f, new Color(80, 160, 70));

        for (int y = 0; y < _h; y++)
            for (int x = 0; x < _w; x++)
                DrawCell(g, x, y, gx + x * cs, gy + y * cs, cs);

        // Completed fossils: a glowing skeleton outline.
        foreach (var f in _fossils)
        {
            if (!f.Complete)
                continue;
            float age = Time - f.CompleteTime;
            float glow = 0.25f + 0.5f * MathF.Max(0, 1 - age) + 0.08f * MathF2.Pulse(Time, 2);
            foreach (var p in f.Cells)
            {
                var c = CellCentre(p.X, p.Y);
                g.Glow(c.X, c.Y, cs * 0.9f, Pal.Gold, glow * 0.35f);
            }
        }

        // Cursor.
        if (_siteDone <= 0 && !IsOver)
        {
            int cx = _hx >= 0 ? _hx : _cx, cy = _hx >= 0 ? _hy : _cy;
            float pulse = MathF2.Pulse(Time, 0.9f);
            var col = _dug[cx, cy] ? Pal.Grey : Pal.Yellow;
            g.RectOutline(gx + cx * cs + 1, gy + cy * cs + 1, cs - 2, cs - 2, 2, col * (0.6f + 0.4f * pulse));
            g.Glow(gx + (cx + 0.5f) * cs, gy + (cy + 0.5f) * cs, cs, col, 0.15f);
        }

        // Flying bones heading to the panel.
        foreach (var f in _flyers)
        {
            float t = MathF2.Clamp((f.T - f.Delay) / 0.7f, 0, 1);
            if (f.T < f.Delay)
                continue;
            var p = Vector2.Lerp(f.From, f.To, MathF2.EaseInOut(t)) + new Vector2(0, -MathF.Sin(t * MathF.PI) * 40);
            g.Glow(p, 10, Pal.Gold, 0.6f);
            DrawBoneBit(g, p.X, p.Y, 8, t * 8);
        }

        DrawPanel(g);

        if (_banner > 0)
        {
            float a = MathF.Min(1, _banner * 2);
            float tw = Gfx.TextWidth(_bannerText, 3f) + 30;
            g.RoundRect(AreaX + AreaW / 2 - tw / 2, 156, tw, 46, 10, Color.Black * (0.65f * a));
            g.TextShadow(_bannerText, AreaX + AreaW / 2, 168, 3f, Pal.Sand * a, Align.Center);
        }
    }

    private void DrawCell(Gfx g, int x, int y, float px, float py, float cs)
    {
        var strat = StratumColour((y + 0.5f) / _h);
        float h1 = Hash(x, y, 1), h2 = Hash(x, y, 2);
        if (!_dug[x, y])
        {
            var top = Pal.Lighten(strat, 0.12f + h1 * 0.06f);
            g.Rect(px, py, cs, cs, Pal.Darken(strat, 0.45f));
            g.Rect(px + 1, py + 1, cs - 2, cs - 2, Pal.Lighten(strat, 0.3f));
            g.Rect(px + 2.5f, py + 2.5f, cs - 3.5f, cs - 3.5f, Pal.Darken(strat, 0.2f));
            g.GradientV(px + 2.5f, py + 2.5f, cs - 5, cs - 5, top, strat);
            // Pebbles and grains.
            for (int i = 0; i < 3; i++)
            {
                float ox = Hash(x, y, 10 + i), oy = Hash(x, y, 20 + i);
                float pr = 0.8f + Hash(x, y, 30 + i) * 1.6f;
                float qx = px + 5 + ox * (cs - 10), qy = py + 5 + oy * (cs - 10);
                g.Circle(qx, qy + 0.6f, pr, Pal.Darken(strat, 0.35f));
                g.Circle(qx, qy, pr, Pal.Lerp(strat, i == 0 ? Pal.LightGrey : Pal.Sand, 0.35f));
            }
            if (h2 > 0.7f)
                g.Line(px + 4, py + 4 + (cs - 8) * h1, px + cs - 4, py + 5 + (cs - 8) * h1, 0.8f, Pal.Darken(strat, 0.3f));
            return;
        }
        float age = MathF2.Clamp((Time - _dugTime[x, y]) / 0.25f, 0, 1);
        var hole = Pal.Darken(strat, 0.68f);
        g.Rect(px, py, cs, cs, hole);
        g.GradientV(px, py, cs, cs * 0.4f, Color.Black * 0.45f, Color.Transparent);
        g.Rect(px, py, 2, cs, Color.Black * 0.25f);
        var c = new Vector2(px + cs / 2, py + cs / 2);
        switch (_kind[x, y])
        {
            case Kind.Rock:
                g.Ellipse(c.X + 1, c.Y + 2, cs * 0.38f, cs * 0.3f, Color.Black * 0.4f);
                g.Ellipse(c.X, c.Y, cs * 0.38f, cs * 0.32f, new Color(110, 110, 118));
                g.Ellipse(c.X - cs * 0.08f, c.Y - cs * 0.08f, cs * 0.22f, cs * 0.16f, new Color(150, 150, 160));
                g.Line(c.X - cs * 0.15f, c.Y - cs * 0.1f, c.X + cs * 0.05f, c.Y + cs * 0.12f, 1, new Color(60, 60, 66));
                g.Line(c.X + cs * 0.05f, c.Y + cs * 0.12f, c.X + cs * 0.2f, c.Y + cs * 0.05f, 1, new Color(60, 60, 66));
                break;
            case Kind.Bone:
            {
                var fo = _fossils[_fossilOf[x, y]];
                g.Rect(px + 1, py + 1, cs - 2, cs - 2, Pal.Lerp(new Color(200, 170, 120), hole, 0.35f));
                var bone = fo.Complete ? new Color(250, 240, 210) : new Color(226, 214, 186);
                float w = cs * 0.2f;
                // Bones join their dug neighbours of the same fossil.
                int id = _fossilOf[x, y];
                if (Same(x + 1, y, id)) g.Line(c.X, c.Y, c.X + cs / 2 + 0.5f, c.Y, w, bone);
                if (Same(x - 1, y, id)) g.Line(c.X, c.Y, c.X - cs / 2 - 0.5f, c.Y, w, bone);
                if (Same(x, y + 1, id)) g.Line(c.X, c.Y, c.X, c.Y + cs / 2 + 0.5f, w, bone);
                if (Same(x, y - 1, id)) g.Line(c.X, c.Y, c.X, c.Y - cs / 2 - 0.5f, w, bone);
                // A vertebra across the line of the spine (or a knuckle at a lone end).
                bool horiz = Same(x + 1, y, id) || Same(x - 1, y, id);
                bool vert = Same(x, y + 1, id) || Same(x, y - 1, id);
                float grow = 0.6f + 0.4f * age;
                if (horiz && !vert)
                    g.RoundRect(c.X - w * 0.55f, c.Y - cs * 0.3f * grow, w * 1.1f, cs * 0.6f * grow, w * 0.5f, bone);
                else if (vert && !horiz)
                    g.RoundRect(c.X - cs * 0.3f * grow, c.Y - w * 0.55f, cs * 0.6f * grow, w * 1.1f, w * 0.5f, bone);
                g.Circle(c.X, c.Y, w * 1.1f * grow, bone);
                g.Circle(c.X, c.Y, w * 0.45f, Pal.Darken(bone, 0.25f));
                g.Circle(c.X - w * 0.45f, c.Y - w * 0.45f, w * 0.3f, Color.White * 0.7f);
                break;
            }
            default:
            {
                int n = _num[x, y];
                if (n > 0)
                {
                    var nc = n switch { 1 => Pal.Sky, 2 => Pal.Lime, 3 => Pal.Yellow, 4 => Pal.Orange, _ => Pal.Red };
                    g.Glow(c.X, c.Y, cs * 0.5f, nc, 0.18f * age);
                    float sc = cs >= 30 ? 2f : 1.5f;
                    g.Text(n.ToString(), c.X + 0.5f, c.Y - 4 * sc + 1, sc, nc * age, Align.Center);
                }
                else
                {
                    g.Circle(c.X - cs * 0.15f, c.Y + cs * 0.1f, 1.2f, Pal.Lighten(hole, 0.15f));
                    g.Circle(c.X + cs * 0.18f, c.Y - cs * 0.12f, 0.9f, Pal.Lighten(hole, 0.15f));
                }
                break;
            }
        }
    }

    private bool Same(int x, int y, int id) => Inside(x, y) && _dug[x, y] && _fossilOf[x, y] == id;

    private static void DrawBoneBit(Gfx g, float x, float y, float size, float spin)
    {
        var d = MathF2.FromAngle(spin, size * 0.5f);
        var col = new Color(250, 240, 210);
        g.Line(new Vector2(x, y) - d, new Vector2(x, y) + d, size * 0.3f, col);
        var n = new Vector2(-d.Y, d.X) * 0.35f;
        foreach (float s in new[] { -1f, 1f })
        {
            var e = new Vector2(x, y) + d * s;
            g.Circle(e.X + n.X, e.Y + n.Y, size * 0.2f, col);
            g.Circle(e.X - n.X, e.Y - n.Y, size * 0.2f, col);
        }
    }

    private void DrawPanel(Gfx g)
    {
        var p = PanelRect;
        g.Panel(p, new Color(34, 26, 18) * 0.95f, new Color(150, 120, 80), 10);
        g.TextShadow("SITE " + _site, p.CenterX, p.Y + 10, 2f, Pal.Sand, Align.Center);

        // Digs left, with a shovel.
        float sy = p.Y + 38;
        float shake = _shovelBreak > 0 ? MathF.Sin(Time * 60) * 2 * _shovelBreak : 0;
        DrawShovel(g, p.X + 24 + shake, sy + 14, 1f, _shovelBreak > 0);
        var dc = _digs <= 3 ? Pal.Red : _digs <= 8 ? Pal.Orange : Pal.White;
        if (_digFlash > 0)
            dc = Pal.Lerp(dc, Pal.Yellow, _digFlash * 3);
        g.Text(_digs.ToString(), p.X + 52, sy + 2, 3f, dc);
        g.Text("DIGS LEFT", p.X + 52 + Gfx.TextWidth(_digs.ToString(), 3) + 6, sy + 12, 1f, Pal.LightGrey);

        g.Rect(p.X + 10, p.Y + 72, p.W - 20, 1, Pal.Sand * 0.4f);

        for (int i = 0; i < _fossils.Count; i++)
        {
            var f = _fossils[i];
            var slot = FossilSlot(i);
            float cell = 7;
            int mw = 0, mh = 0;
            foreach (var c in f.Shape)
            {
                mw = Math.Max(mw, c.X + 1);
                mh = Math.Max(mh, c.Y + 1);
            }
            float ox = slot.X - mw * cell / 2, oy = slot.Y - mh * cell / 2;
            if (f.Complete)
                g.Glow(slot.X, slot.Y, 18, Pal.Gold, 0.3f + 0.3f * MathF.Max(0, 1 - (Time - f.CompleteTime)));
            foreach (var c in f.Shape)
                g.RoundRect(ox + c.X * cell + 0.5f, oy + c.Y * cell + 0.5f, cell - 1, cell - 1, 1.2f,
                    f.Complete ? new Color(250, 240, 210) : Color.Black * 0.45f);
            g.Text(f.Complete ? f.Name : "? ? ?", p.X + 56, slot.Y - 7, 1f, f.Complete ? Pal.Sand : Pal.Grey);
            // Progress pips.
            for (int k = 0; k < f.Cells.Count; k++)
                g.Circle(p.X + 59 + k * 8, slot.Y + 7, 2.4f, k < f.Found ? Pal.Gold : Color.Black * 0.4f);
        }
    }

    private static void DrawShovel(Gfx g, float x, float y, float k, bool broken)
    {
        var wood = new Color(170, 110, 60);
        var steel = new Color(190, 200, 210);
        g.Line(x - 9 * k, y - 11 * k, x + 2 * k, y + 2 * k, 2.4f * k, wood);
        g.Line(x - 12 * k, y - 12 * k, x - 7 * k, y - 14 * k, 2.4f * k, wood);
        if (broken)
        {
            g.Shape(BladeShape, new Vector2(x + 7 * k, y + 9 * k), 0.785f + 0.5f, 7 * k, Pal.Grey);
            g.Glow(x + 4, y + 6, 14, Pal.Red, 0.4f);
        }
        else
        {
            g.Shape(BladeShape, new Vector2(x + 4 * k, y + 6 * k), 0.785f, 7 * k, steel);
        }
    }

    private static readonly Vector2[] BladeShape =
    [
        new(-0.6f, -0.7f), new(0.6f, -0.7f), new(1.1f, 0), new(0.6f, 0.75f), new(-0.6f, 0.75f),
    ];

    // ------------------------------------------------------------------ icon

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        float s = r.H / 70f;
        // A cross-section through the rock layers.
        for (int i = 0; i < 7; i++)
        {
            float y0 = r.Y + 6 * s + i * 9.2f * s;
            var col = StratumColour(i / 6.5f);
            g.Rect(r.X, y0, r.W, 9.6f * s, col);
            for (float x = r.X; x < r.Right; x += 9 * s)
                g.Circle(x + Hash((int)x, i, 1) * 8 * s, y0 + Hash((int)x, i, 2) * 8 * s, (0.6f + Hash((int)x, i, 3)) * s, Pal.Darken(col, 0.3f));
        }
        g.Rect(r.X, r.Y, r.W, 6 * s, new Color(50, 120, 50));
        for (float x = r.X; x < r.Right; x += 3 * s)
            g.Line(x, r.Y + 6 * s, x + s, r.Y + (2 + Hash((int)x, 0, 4) * 2) * s, 0.8f * s, new Color(90, 170, 70));
        // An excavated pit with the skeleton in it.
        float bx = r.CenterX - 46 * s, by = r.Y + 44 * s;
        g.RoundRect(bx - 12 * s, by - 30 * s, 100 * s, 44 * s, 6 * s, Color.Black * 0.35f);
        g.Glow(r.CenterX, by - 8 * s, 70 * s, Pal.Gold, 0.18f + 0.06f * MathF2.Pulse(time, 2));
        var bone = new Color(250, 240, 210);
        float wave = MathF.Sin(time * 2) * 1.5f * s;
        // Tail, back, neck and head.
        var pts = new Vector2[]
        {
            new(bx - 8 * s, by + 4 * s), new(bx + 4 * s, by), new(bx + 18 * s, by - 4 * s), new(bx + 34 * s, by - 5 * s),
            new(bx + 48 * s, by - 3 * s), new(bx + 58 * s, by - 10 * s), new(bx + 64 * s, by - 19 * s + wave), new(bx + 70 * s, by - 24 * s + wave),
        };
        for (int i = 0; i + 1 < pts.Length; i++)
            g.Line(pts[i], pts[i + 1], 2.4f * s, bone);
        for (int i = 1; i < pts.Length - 1; i++)
            g.Circle(pts[i].X, pts[i].Y, 1.8f * s, bone);
        // Ribs.
        for (int i = 0; i < 5; i++)
        {
            float t = 0.15f + i * 0.14f;
            var p = Vector2.Lerp(pts[2], pts[4], t * 1.3f);
            g.Line(p, p + new Vector2(-1.5f * s, 9 * s - MathF.Abs(i - 2) * 1.5f * s), 1.3f * s, bone);
        }
        // Legs.
        foreach (float lx in new[] { 14f, 22f, 44f, 52f })
        {
            var hip = new Vector2(bx + lx * s, by - 3 * s);
            g.Line(hip, hip + new Vector2(2 * s, 7 * s), 1.8f * s, bone);
            g.Line(hip + new Vector2(2 * s, 7 * s), hip + new Vector2(0, 13 * s), 1.6f * s, bone);
        }
        g.Ellipse(bx + 74 * s, by - 26 * s + wave, 5.5f * s, 3.6f * s, bone);
        g.Circle(bx + 75 * s, by - 27 * s + wave, 1 * s, Pal.Black);
        // A clue in the soil and the shovel.
        g.RoundRect(r.X + 10 * s, r.Bottom - 22 * s, 14 * s, 14 * s, 2 * s, Pal.Darken(StratumColour(0.9f), 0.6f));
        g.Text("2", r.X + 17.5f * s, r.Bottom - 19 * s, 1.1f * s, Pal.Lime, Align.Center);
        float dig = MathF.Abs(MathF.Sin(time * 3.5f));
        DrawShovel(g, r.CenterX + 50 * s, r.Bottom - 22 * s + dig * 3 * s, 1.1f * s, false);
    }

    // ------------------------------------------------------------------ autoplay

    public override void AutoPlay(Controls c)
    {
        if (_siteDone > 0 || IsOver)
            return;
        if (_apWait > 0)
        {
            _apWait -= Dt;
            return;
        }
        if (_apStage == 0)
        {
            _apTarget = ChooseDig();
            c.Pointer = CellCentre(_apTarget.X, _apTarget.Y);
            c.PointerPressed = c.PointerDown = true;
            _apStage = 1;
        }
        else
        {
            c.Pointer = CellCentre(_apTarget.X, _apTarget.Y);
            c.PointerReleased = true;
            _apStage = 0;
            _apWait = 0.55f;
        }
    }

    private readonly bool[,] _safe = new bool[MaxW, MaxH];
    private readonly bool[,] _sure = new bool[MaxW, MaxH];

    /// <summary>Plays like a careful digger, using only what is visible.</summary>
    private Point ChooseDig()
    {
        Array.Clear(_safe);
        Array.Clear(_sure);
        // Deduce from the numbers (repeat a few times so deductions feed each other).
        for (int pass = 0; pass < 3; pass++)
            for (int x = 0; x < _w; x++)
                for (int y = 0; y < _h; y++)
                {
                    if (!_dug[x, y] || _kind[x, y] != Kind.Soil || _num[x, y] == 0)
                        continue;
                    int known = 0, unknown = 0;
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if ((dx == 0 && dy == 0) || !Inside(nx, ny))
                                continue;
                            if ((_dug[nx, ny] && _kind[nx, ny] == Kind.Bone) || _sure[nx, ny])
                                known++;
                            else if (!_dug[nx, ny] && !_safe[nx, ny])
                                unknown++;
                        }
                    int left = _num[x, y] - known;
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (!Inside(nx, ny) || _dug[nx, ny] || _safe[nx, ny] || _sure[nx, ny])
                                continue;
                            if (left == 0)
                                _safe[nx, ny] = true;
                            else if (left == unknown)
                                _sure[nx, ny] = true;
                        }
                }
        // Fossils never touch: cells around a finished fossil are already cleared, and diagonal
        // neighbours of a found bone that are not in line with it are usually empty.
        Point best = new(-1, -1);
        float bestScore = float.MinValue;
        for (int x = 0; x < _w; x++)
            for (int y = 0; y < _h; y++)
            {
                if (_dug[x, y] || _safe[x, y])
                    continue;
                float score = Rand(0, 0.2f);
                if (_sure[x, y])
                    score += 100;
                // Next to a found bone of an unfinished fossil.
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                    if (Inside(nx, ny) && _dug[nx, ny] && _kind[nx, ny] == Kind.Bone && !_fossils[_fossilOf[nx, ny]].Complete)
                        score += 6;
                }
                // Probability from neighbouring numbers.
                bool info = false;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (!Inside(nx, ny) || !_dug[nx, ny] || _kind[nx, ny] != Kind.Soil)
                            continue;
                        info = true;
                        int known = 0, unknown = 0;
                        for (int ex = -1; ex <= 1; ex++)
                            for (int ey = -1; ey <= 1; ey++)
                            {
                                int mx = nx + ex, my = ny + ey;
                                if ((ex == 0 && ey == 0) || !Inside(mx, my))
                                    continue;
                                if (_dug[mx, my] && _kind[mx, my] == Kind.Bone) known++;
                                else if (!_dug[mx, my] && !_safe[mx, my]) unknown++;
                            }
                        if (unknown > 0)
                            score += 3f * (_num[nx, ny] - known) / unknown;
                    }
                if (!info)
                    score += 0.8f; // fresh ground: average odds, and may open up a clearing
                if (score > bestScore)
                {
                    bestScore = score;
                    best = new Point(x, y);
                }
            }
        if (best.X < 0)
            for (int x = 0; x < _w; x++)
                for (int y = 0; y < _h; y++)
                    if (!_dug[x, y])
                        return new Point(x, y);
        return best.X < 0 ? new Point(0, 0) : best;
    }
}
