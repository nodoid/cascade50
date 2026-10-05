using System;
using Microsoft.Xna.Framework;

namespace Cascade50.Core.Graphics;

/// <summary>
/// The single texture every primitive is drawn from, generated at start-up (no content pipeline):
/// a white texel for flat shapes, an anti-aliased disc, a soft glow, and the Oric font magnified
/// eight times so it stays crisp under bilinear filtering. All texels are premultiplied.
/// </summary>
public static class Atlas
{
    public const int Size = 1024;

    private const int DiscX = 16, DiscSize = 256;
    private const int GlowX = 288, GlowSize = 256;
    private const float DiscRadius = 126f;
    private const int FontY = 300, FontScale = 8, CellPad = 2, GlyphsPerRow = 16;
    private const int GlyphPxW = OricFont.GlyphWidth * FontScale, GlyphPxH = OricFont.GlyphHeight * FontScale;
    private const int CellW = GlyphPxW + CellPad * 2, CellH = GlyphPxH + CellPad * 2;

    /// <summary>Quad half-size multiplier so the disc's anti-aliased rim lands on the radius.</summary>
    public const float DiscPad = DiscSize / 2f / DiscRadius;

    public static readonly Vector2 WhiteUv = new(4f / Size, 4f / Size);
    public static readonly Vector4 Disc = Region(DiscX, 0, DiscSize, DiscSize);
    public static readonly Vector4 GlowTex = Region(GlowX, 0, GlowSize, GlowSize);

    private static Vector4 Region(int x, int y, int w, int h) =>
        new((float)x / Size, (float)y / Size, (float)(x + w) / Size, (float)(y + h) / Size);

    /// <summary>Characters the Oric font lacks, drawn in its style: pound, times and divide.</summary>
    private static readonly (char ch, byte[] rows)[] Extra =
    [
        ('£', [0x0C, 0x12, 0x10, 0x3C, 0x10, 0x10, 0x3E, 0x00]),
        ('×', [0x00, 0x22, 0x14, 0x08, 0x14, 0x22, 0x00, 0x00]),
        ('÷', [0x00, 0x08, 0x00, 0x3E, 0x00, 0x08, 0x00, 0x00]),
    ];

    public static bool HasGlyph(char c) => (c > ' ' && c <= '~') || ExtraIndex(c) >= 0;

    private static int ExtraIndex(char c)
    {
        for (int i = 0; i < Extra.Length; i++)
            if (Extra[i].ch == c)
                return i;
        return -1;
    }

    public static Vector4 Glyph(char c)
    {
        int index = c - OricFont.FirstChar;
        int extra = ExtraIndex(c);
        if (extra >= 0)
            index = OricFont.CharCount + extra;
        else if (index < 0 || index >= OricFont.CharCount)
            index = 0;
        int x = (index % GlyphsPerRow) * CellW + CellPad;
        int y = FontY + (index / GlyphsPerRow) * CellH + CellPad;
        return Region(x, y, GlyphPxW, GlyphPxH);
    }

    /// <summary>Builds the atlas pixels (premultiplied RGBA).</summary>
    public static Color[] BuildPixels()
    {
        var data = new Color[Size * Size];

        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                data[y * Size + x] = Color.White;

        float c = DiscSize / 2f;
        for (int y = 0; y < DiscSize; y++)
            for (int x = 0; x < DiscSize; x++)
            {
                float d = MathF.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c));
                float a = Math.Clamp(DiscRadius + 0.75f - d, 0f, 1.5f) / 1.5f;
                data[y * Size + DiscX + x] = Color.White * a;
            }

        float g = GlowSize / 2f;
        for (int y = 0; y < GlowSize; y++)
            for (int x = 0; x < GlowSize; x++)
            {
                float d = MathF.Sqrt((x + 0.5f - g) * (x + 0.5f - g) + (y + 0.5f - g) * (y + 0.5f - g)) / g;
                float a = d >= 1 ? 0 : MathF.Pow(1 - d, 2.2f);
                data[y * Size + GlowX + x] = Color.White * a;
            }

        for (int i = 0; i < OricFont.CharCount; i++)
        {
            char ch = (char)(OricFont.FirstChar + i);
            int ox = (i % GlyphsPerRow) * CellW + CellPad;
            int oy = FontY + (i / GlyphsPerRow) * CellH + CellPad;
            for (int y = 0; y < GlyphPxH; y++)
                for (int x = 0; x < GlyphPxW; x++)
                    if (OricFont.IsSet(ch, x / FontScale, y / FontScale))
                        data[(oy + y) * Size + ox + x] = Color.White;
        }
        for (int e = 0; e < Extra.Length; e++)
        {
            int i = OricFont.CharCount + e;
            int ox = (i % GlyphsPerRow) * CellW + CellPad;
            int oy = FontY + (i / GlyphsPerRow) * CellH + CellPad;
            for (int y = 0; y < GlyphPxH; y++)
                for (int x = 0; x < GlyphPxW; x++)
                    if ((Extra[e].rows[y / FontScale] & (1 << (OricFont.GlyphWidth - 1 - x / FontScale))) != 0)
                        data[(oy + y) * Size + ox + x] = Color.White;
        }
        return data;
    }
}
