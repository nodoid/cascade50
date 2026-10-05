using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Cascade50.Core.Graphics;

public enum Align
{
    Left,
    Center,
    Right,
}

/// <summary>
/// Everything a scene can draw, in virtual pixels (the playfield is 640x360). Shapes are vector,
/// so they stay sharp at any resolution. Draw order is call order. Colours are premultiplied:
/// use <c>colour * alpha</c> for translucency and <see cref="Pal.Add"/> for additive glows.
/// </summary>
public abstract class Gfx
{
    /// <summary>Added to every coordinate (used for screen shake and scrolling).</summary>
    public Vector2 Offset;

    /// <summary>The area of virtual space visible on the display; at least the 640x360 playfield.</summary>
    public RectF Visible { get; protected set; } = Screen.Bounds;

    /// <summary>Real pixels per virtual pixel (useful for hairlines).</summary>
    public float PixelScale { get; protected set; } = 1f;

    /// <summary>Restricts drawing to a virtual rectangle (null removes the clip).</summary>
    public abstract void SetClip(RectF? clip);

    protected abstract void Emit(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3,
        Vector2 t0, Vector2 t1, Vector2 t2, Vector2 t3, Color c0, Color c1, Color c2, Color c3);

    // ------------------------------------------------------------------ rectangles

    public void Rect(float x, float y, float w, float h, Color c)
    {
        if (w <= 0 || h <= 0)
            return;
        var o = Offset;
        var t = Atlas.WhiteUv;
        Emit(new(x + o.X, y + o.Y), new(x + w + o.X, y + o.Y), new(x + w + o.X, y + h + o.Y), new(x + o.X, y + h + o.Y),
            t, t, t, t, c, c, c, c);
    }

    public void Rect(RectF r, Color c) => Rect(r.X, r.Y, r.W, r.H, c);

    /// <summary>Rectangle shaded from <paramref name="top"/> to <paramref name="bottom"/>.</summary>
    public void GradientV(float x, float y, float w, float h, Color top, Color bottom)
    {
        var o = Offset;
        var t = Atlas.WhiteUv;
        Emit(new(x + o.X, y + o.Y), new(x + w + o.X, y + o.Y), new(x + w + o.X, y + h + o.Y), new(x + o.X, y + h + o.Y),
            t, t, t, t, top, top, bottom, bottom);
    }

    /// <summary>Rectangle shaded from <paramref name="left"/> to <paramref name="right"/>.</summary>
    public void GradientH(float x, float y, float w, float h, Color left, Color right)
    {
        var o = Offset;
        var t = Atlas.WhiteUv;
        Emit(new(x + o.X, y + o.Y), new(x + w + o.X, y + o.Y), new(x + w + o.X, y + h + o.Y), new(x + o.X, y + h + o.Y),
            t, t, t, t, left, right, right, left);
    }

    public void RectOutline(float x, float y, float w, float h, float thickness, Color c)
    {
        Rect(x, y, w, thickness, c);
        Rect(x, y + h - thickness, w, thickness, c);
        Rect(x, y + thickness, thickness, h - 2 * thickness, c);
        Rect(x + w - thickness, y + thickness, thickness, h - 2 * thickness, c);
    }

    public void RectOutline(RectF r, float thickness, Color c) => RectOutline(r.X, r.Y, r.W, r.H, thickness, c);

    /// <summary>Filled rectangle with rounded corners.</summary>
    public void RoundRect(float x, float y, float w, float h, float radius, Color c)
    {
        radius = MathF.Min(radius, MathF.Min(w, h) / 2);
        if (radius < 0.5f)
        {
            Rect(x, y, w, h, c);
            return;
        }
        Rect(x + radius, y, w - 2 * radius, h, c);
        Rect(x, y + radius, radius, h - 2 * radius, c);
        Rect(x + w - radius, y + radius, radius, h - 2 * radius, c);
        Pie(x + radius, y + radius, radius, MathF.PI, MathF.PI * 1.5f, c);
        Pie(x + w - radius, y + radius, radius, MathF.PI * 1.5f, MathF.PI * 2, c);
        Pie(x + w - radius, y + h - radius, radius, 0, MathF.PI * 0.5f, c);
        Pie(x + radius, y + h - radius, radius, MathF.PI * 0.5f, MathF.PI, c);
    }

    public void RoundRect(RectF r, float radius, Color c) => RoundRect(r.X, r.Y, r.W, r.H, radius, c);

    /// <summary>A panel: rounded fill with a 1.5px border.</summary>
    public void Panel(RectF r, Color fill, Color border, float radius = 8)
    {
        RoundRect(r, radius, border);
        RoundRect(r.Inflate(-1.5f, -1.5f), MathF.Max(0, radius - 1.5f), fill);
    }

    // ------------------------------------------------------------------ circles and glows

    /// <summary>Anti-aliased filled circle.</summary>
    public void Circle(float cx, float cy, float r, Color c) => Ellipse(cx, cy, r, r, c);

    public void Circle(Vector2 p, float r, Color c) => Ellipse(p.X, p.Y, r, r, c);

    public void Ellipse(float cx, float cy, float rx, float ry, Color c)
    {
        if (rx <= 0 || ry <= 0)
            return;
        // The disc texture has a soft one-texel rim; pad slightly so the rim sits on the radius.
        float px = rx * Atlas.DiscPad, py = ry * Atlas.DiscPad;
        TexQuad(cx - px, cy - py, px * 2, py * 2, Atlas.Disc, c);
    }

    /// <summary>Soft radial glow, drawn additively. Radius is where it fades out completely.</summary>
    public void Glow(float cx, float cy, float r, Color c, float intensity = 1f)
    {
        if (r <= 0)
            return;
        TexQuad(cx - r, cy - r, r * 2, r * 2, Atlas.GlowTex, Pal.Add(c, intensity));
    }

    public void Glow(Vector2 p, float r, Color c, float intensity = 1f) => Glow(p.X, p.Y, r, c, intensity);

    /// <summary>Circle outline.</summary>
    public void Ring(float cx, float cy, float r, float thickness, Color c, int segments = 0)
    {
        if (segments <= 0)
            segments = Math.Clamp((int)(r * 0.8f), 12, 72);
        float r0 = r - thickness / 2, r1 = r + thickness / 2;
        var t = Atlas.WhiteUv;
        var o = Offset;
        for (int i = 0; i < segments; i++)
        {
            float a0 = MathF2.Tau * i / segments, a1 = MathF2.Tau * (i + 1) / segments;
            var d0 = new Vector2(MathF.Cos(a0), MathF.Sin(a0));
            var d1 = new Vector2(MathF.Cos(a1), MathF.Sin(a1));
            var c0 = new Vector2(cx, cy) + o;
            Emit(c0 + d0 * r0, c0 + d0 * r1, c0 + d1 * r1, c0 + d1 * r0, t, t, t, t, c, c, c, c);
        }
    }

    /// <summary>Filled pie slice from angle <paramref name="a0"/> to <paramref name="a1"/> (radians, clockwise on screen).</summary>
    public void Pie(float cx, float cy, float r, float a0, float a1, Color c, int segments = 0)
    {
        if (segments <= 0)
            segments = Math.Clamp((int)(r * MathF.Abs(a1 - a0) * 0.25f), 3, 48);
        var t = Atlas.WhiteUv;
        var center = new Vector2(cx, cy) + Offset;
        for (int i = 0; i < segments; i++)
        {
            float b0 = a0 + (a1 - a0) * i / segments, b1 = a0 + (a1 - a0) * (i + 1) / segments;
            var p0 = center + new Vector2(MathF.Cos(b0), MathF.Sin(b0)) * r;
            var p1 = center + new Vector2(MathF.Cos(b1), MathF.Sin(b1)) * r;
            Emit(center, p0, p1, p1, t, t, t, t, c, c, c, c);
        }
    }

    /// <summary>Thick arc from angle a0 to a1.</summary>
    public void Arc(float cx, float cy, float r, float thickness, float a0, float a1, Color c, int segments = 0)
    {
        if (segments <= 0)
            segments = Math.Clamp((int)(r * MathF.Abs(a1 - a0) * 0.3f), 3, 64);
        float r0 = r - thickness / 2, r1 = r + thickness / 2;
        var t = Atlas.WhiteUv;
        var center = new Vector2(cx, cy) + Offset;
        for (int i = 0; i < segments; i++)
        {
            float b0 = a0 + (a1 - a0) * i / segments, b1 = a0 + (a1 - a0) * (i + 1) / segments;
            var d0 = new Vector2(MathF.Cos(b0), MathF.Sin(b0));
            var d1 = new Vector2(MathF.Cos(b1), MathF.Sin(b1));
            Emit(center + d0 * r0, center + d0 * r1, center + d1 * r1, center + d1 * r0, t, t, t, t, c, c, c, c);
        }
    }

    // ------------------------------------------------------------------ lines and polygons

    public void Line(float x0, float y0, float x1, float y1, float width, Color c) =>
        Line(new Vector2(x0, y0), new Vector2(x1, y1), width, c);

    public void Line(Vector2 a, Vector2 b, float width, Color c)
    {
        var d = b - a;
        float len = d.Length();
        if (len < 0.0001f)
            return;
        var n = new Vector2(-d.Y, d.X) / len * (width / 2);
        var t = Atlas.WhiteUv;
        a += Offset;
        b += Offset;
        Emit(a + n, b + n, b - n, a - n, t, t, t, t, c, c, c, c);
    }

    /// <summary>A neon line: a solid core with an additive glow around it.</summary>
    public void GlowLine(Vector2 a, Vector2 b, float width, Color c)
    {
        Line(a, b, width * 4, Pal.Add(c, 0.18f));
        Line(a, b, width * 2, Pal.Add(c, 0.3f));
        Line(a, b, width, c);
    }

    /// <summary>Connected line segments; set <paramref name="closed"/> to join the last point to the first.</summary>
    public void Path(IReadOnlyList<Vector2> points, float width, Color c, bool closed = false)
    {
        for (int i = 0; i + 1 < points.Count; i++)
            Line(points[i], points[i + 1], width, c);
        if (closed && points.Count > 2)
            Line(points[^1], points[0], width, c);
        // Round the joints.
        if (width > 2.5f)
            foreach (var p in points)
                Circle(p.X, p.Y, width / 2, c);
    }

    public void Triangle(Vector2 a, Vector2 b, Vector2 c, Color col)
    {
        var t = Atlas.WhiteUv;
        var o = Offset;
        Emit(a + o, b + o, c + o, c + o, t, t, t, t, col, col, col, col);
    }

    /// <summary>Filled convex polygon (triangle fan from the first point).</summary>
    public void Polygon(IReadOnlyList<Vector2> points, Color c)
    {
        for (int i = 1; i + 1 < points.Count; i++)
            Triangle(points[0], points[i], points[i + 1], c);
    }

    private readonly List<Vector2> _scratch = new();

    /// <summary>
    /// Draws a convex shape given in local coordinates (pointing along +X), rotated by
    /// <paramref name="angle"/> radians, scaled and moved to <paramref name="pos"/>.
    /// </summary>
    public void Shape(IReadOnlyList<Vector2> local, Vector2 pos, float angle, float scale, Color c)
    {
        Transform(local, pos, angle, scale);
        Polygon(_scratch, c);
    }

    /// <summary>Outline of a shape in local coordinates (see <see cref="Shape"/>).</summary>
    public void ShapeOutline(IReadOnlyList<Vector2> local, Vector2 pos, float angle, float scale, float width, Color c, bool glow = false)
    {
        Transform(local, pos, angle, scale);
        if (glow)
        {
            for (int i = 0; i < _scratch.Count; i++)
                GlowLine(_scratch[i], _scratch[(i + 1) % _scratch.Count], width, c);
        }
        else
        {
            var copy = _scratch.ToArray();
            Path(copy, width, c, true);
        }
    }

    private void Transform(IReadOnlyList<Vector2> local, Vector2 pos, float angle, float scale)
    {
        _scratch.Clear();
        float cos = MathF.Cos(angle) * scale, sin = MathF.Sin(angle) * scale;
        foreach (var p in local)
            _scratch.Add(new Vector2(pos.X + p.X * cos - p.Y * sin, pos.Y + p.X * sin + p.Y * cos));
    }

    /// <summary>A rectangle of size w x h centred on <paramref name="center"/> and rotated.</summary>
    public void RotatedRect(Vector2 center, float w, float h, float angle, Color c)
    {
        var ax = MathF2.FromAngle(angle, w / 2);
        var ay = MathF2.FromAngle(angle + MathF.PI / 2, h / 2);
        var t = Atlas.WhiteUv;
        center += Offset;
        Emit(center - ax - ay, center + ax - ay, center + ax + ay, center - ax + ay, t, t, t, t, c, c, c, c);
    }

    // ------------------------------------------------------------------ text (the Oric ROM font)

    /// <summary>Width of a glyph cell at scale 1 (the Oric font is 6x8).</summary>
    public const float GlyphW = OricFont.GlyphWidth;

    public const float GlyphH = OricFont.GlyphHeight;

    public static float TextWidth(string text, float scale) => (text?.Length ?? 0) * GlyphW * scale;

    /// <summary>
    /// Draws text in the Oric font. Scale 1 is 6x8 virtual pixels per character; use at least 1.5 for
    /// anything the player must read on a phone. Returns the width drawn.
    /// </summary>
    public float Text(string text, float x, float y, float scale, Color c, Align align = Align.Left)
    {
        if (string.IsNullOrEmpty(text))
            return 0;
        float w = TextWidth(text, scale);
        if (align == Align.Center)
            x -= w / 2;
        else if (align == Align.Right)
            x -= w;
        float gw = GlyphW * scale, gh = GlyphH * scale;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (!Atlas.HasGlyph(ch))
                continue;
            var uv = Atlas.Glyph(ch);
            TexQuad(x + i * gw, y, gw, gh, uv, c);
        }
        return w;
    }

    /// <summary>Text with a dark drop shadow, for titles over busy backgrounds.</summary>
    public float TextShadow(string text, float x, float y, float scale, Color c, Align align = Align.Left, Color? shadow = null)
    {
        float d = MathF.Max(1, scale * 0.75f);
        Text(text, x + d, y + d, scale, shadow ?? Color.Black * 0.8f, align);
        return Text(text, x, y, scale, c, align);
    }

    /// <summary>Text scaled down (never up past <paramref name="maxScale"/>) so it fits <paramref name="maxWidth"/>.</summary>
    public float TextFit(string text, float x, float y, float maxWidth, float maxScale, Color c, Align align = Align.Left)
    {
        float scale = maxScale;
        float w = TextWidth(text, 1);
        if (w * scale > maxWidth && w > 0)
            scale = maxWidth / w;
        return Text(text, x, y, scale, c, align);
    }

    /// <summary>Word-wrapped text. '\n' starts a new line. Returns the height used.</summary>
    public float TextWrapped(string text, float x, float y, float width, float scale, Color c, float lineSpacing = 1.35f, Align align = Align.Left)
    {
        var lines = Wrap(text, width, scale);
        float lh = GlyphH * scale * lineSpacing;
        float ax = align switch { Align.Center => x + width / 2, Align.Right => x + width, _ => x };
        for (int i = 0; i < lines.Count; i++)
            Text(lines[i], ax, y + i * lh, scale, c, align);
        return lines.Count * lh;
    }

    /// <summary>Splits text into lines no wider than <paramref name="width"/>.</summary>
    public static List<string> Wrap(string text, float width, float scale)
    {
        var lines = new List<string>();
        int perLine = Math.Max(1, (int)(width / (GlyphW * scale)));
        foreach (var para in (text ?? "").Split('\n'))
        {
            var line = "";
            foreach (var word in para.Split(' '))
            {
                var w = word;
                while (w.Length > perLine)
                {
                    if (line.Length > 0)
                    {
                        lines.Add(line);
                        line = "";
                    }
                    lines.Add(w[..perLine]);
                    w = w[perLine..];
                }
                if (line.Length == 0)
                    line = w;
                else if (line.Length + 1 + w.Length <= perLine)
                    line += " " + w;
                else
                {
                    lines.Add(line);
                    line = w;
                }
            }
            lines.Add(line);
        }
        return lines;
    }

    // ------------------------------------------------------------------ pixel art

    /// <summary>
    /// Draws pixel art given as rows of characters; each character is looked up in
    /// <paramref name="palette"/> (characters not in it, such as '.' or ' ', are transparent).
    /// (x, y) is the top-left corner and <paramref name="px"/> the size of one art pixel.
    /// </summary>
    public void Pixels(PixelArt art, float x, float y, float px, bool flipX = false, Color? tint = null)
    {
        foreach (var run in art.Runs)
        {
            float rx = flipX ? art.Width - run.X - run.Length : run.X;
            Rect(x + rx * px, y + run.Y * px, run.Length * px + 0.02f, px + 0.02f, tint ?? run.Color);
        }
    }

    /// <summary>Pixel art centred on a point.</summary>
    public void PixelsCentered(PixelArt art, float cx, float cy, float px, bool flipX = false, Color? tint = null) =>
        Pixels(art, cx - art.Width * px / 2, cy - art.Height * px / 2, px, flipX, tint);

    // ------------------------------------------------------------------ helpers

    protected void TexQuad(float x, float y, float w, float h, Vector4 uv, Color c)
    {
        var o = Offset;
        x += o.X;
        y += o.Y;
        Emit(new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h),
            new(uv.X, uv.Y), new(uv.Z, uv.Y), new(uv.Z, uv.W), new(uv.X, uv.W), c, c, c, c);
    }
}

/// <summary>Pixel art parsed once into horizontal runs of colour (see <see cref="Gfx.Pixels"/>).</summary>
public sealed class PixelArt
{
    public readonly struct Run
    {
        public readonly int X, Y, Length;
        public readonly Color Color;

        public Run(int x, int y, int length, Color color)
        {
            X = x;
            Y = y;
            Length = length;
            Color = color;
        }
    }

    public PixelArt(string[] rows, IReadOnlyDictionary<char, Color> palette)
    {
        var runs = new List<Run>();
        Height = rows.Length;
        for (int y = 0; y < rows.Length; y++)
        {
            string row = rows[y];
            Width = Math.Max(Width, row.Length);
            int x = 0;
            while (x < row.Length)
            {
                if (!palette.TryGetValue(row[x], out var col))
                {
                    x++;
                    continue;
                }
                int start = x;
                while (x < row.Length && row[x] == row[start])
                    x++;
                runs.Add(new Run(start, y, x - start, col));
            }
        }
        Runs = runs.ToArray();
    }

    public int Width { get; }
    public int Height { get; }
    public Run[] Runs { get; }
}

/// <summary>A renderer that draws nothing: used by the tests to run every game headless.</summary>
public sealed class NullGfx : Gfx
{
    public long Primitives;

    public override void SetClip(RectF? clip) { }

    protected override void Emit(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, Vector2 t0, Vector2 t1, Vector2 t2, Vector2 t3,
        Color c0, Color c1, Color c2, Color c3)
    {
        if (float.IsNaN(p0.X) || float.IsNaN(p2.Y))
            throw new InvalidOperationException("NaN coordinate drawn");
        Primitives++;
    }
}
