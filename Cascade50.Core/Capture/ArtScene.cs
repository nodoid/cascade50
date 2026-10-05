using System;
using Microsoft.Xna.Framework;
using Cascade50.Core.Games;
using Cascade50.Core.Graphics;
using Cascade50.Core.Scenes;

namespace Cascade50.Core.Capture;

public enum ArtKind
{
    /// <summary>App icon: the tape and a big "50", no fine text (square).</summary>
    Icon,

    /// <summary>Icon art without background, for adaptive / unplated icons.</summary>
    IconForeground,

    /// <summary>Only the icon's background (Android adaptive icon back layer).</summary>
    IconBackground,

    /// <summary>Logo, tape and a collage of games (Google Play feature graphic, Xbox titled art).</summary>
    Titled,

    /// <summary>Tape and collage with no text (Microsoft Store super hero art: the Store adds the title).</summary>
    Untitled,

    /// <summary>Plain splash (Windows / Android launch image): logo on black.</summary>
    Splash,
}

/// <summary>Marketing art and icons, drawn with the game's own renderer so everything matches.</summary>
public sealed class ArtScene : Scene
{
    private readonly ArtKind _kind;

    public ArtScene(Cascade50Game app, ArtKind kind) : base(app)
    {
        _kind = kind;
    }

    protected override void Update() { }

    public override void Draw(Gfx g)
    {
        switch (_kind)
        {
            case ArtKind.Icon:
                DrawIcon(g, true);
                break;
            case ArtKind.IconForeground:
                DrawIcon(g, false);
                break;
            case ArtKind.IconBackground:
                DrawIcon(g, true, false);
                break;
            case ArtKind.Splash:
                g.Rect(g.Visible, Color.Black);
                Brand.Logo(g, 320, 180, MathF.Min(g.Visible.W / 640f, 1f) * 7f, 0.4f, false);
                break;
            default:
                DrawCollage(g, _kind == ArtKind.Titled);
                break;
        }
    }

    /// <summary>The app icon, filling the largest square in the visible area.</summary>
    public static void DrawIcon(Gfx g, bool background, bool foreground = true)
    {
        var v = g.Visible;
        float side = MathF.Min(v.W, v.H);
        float cx = v.CenterX, cy = v.CenterY;
        float u = side / 100f; // icon units: 0..100 across
        if (background)
        {
            g.GradientV(cx - side / 2, cy - side / 2, side, side, new Color(40, 12, 80), new Color(8, 6, 30));
            // Rainbow sweep behind the tape.
            for (int i = 0; i < Pal.Rainbow.Length; i++)
            {
                float y = cy + u * (6 + i * 5.5f);
                var c = Pal.Rainbow[i];
                g.Polygon([new Vector2(cx - side / 2, y), new Vector2(cx + side / 2, y - u * 34), new Vector2(cx + side / 2, y - u * 28.5f), new Vector2(cx - side / 2, y + u * 5.5f)], c);
            }
            g.Glow(cx, cy - u * 8, u * 60, Pal.Magenta, 0.35f);
        }
        if (!foreground)
            return;
        Brand.Cassette(g, cx, cy - u * 6, u * 78, 0.3f, 0, label: false);
        // Bold rainbow lettering on the label.
        float w = u * 78, h = w * 0.63f;
        float labelTop = cy - u * 6 - h / 2 + h * 0.08f;
        float ls = w * 0.86f * 0.86f / (7 * Gfx.GlyphW);
        float lx = cx - 7 * Gfx.GlyphW * ls / 2;
        for (int i = 0; i < 7; i++)
        {
            string ch = "CASCADE"[i].ToString();
            float ly = labelTop + h * 0.62f * 0.07f;
            g.Text(ch, lx + i * Gfx.GlyphW * ls + ls * 0.6f, ly + ls * 0.6f, ls, new Color(40, 30, 50) * 0.5f);
            g.Text(ch, lx + i * Gfx.GlyphW * ls, ly, ls, Pal.Darken(Pal.Rainbow[i % 6], 0.1f));
        }
        // "50" badge.
        float bx = cx + u * 22, by = cy + u * 24, br = u * 21;
        g.Circle(bx + u * 1.2f, by + u * 1.6f, br, Color.Black * 0.5f);
        g.Glow(bx, by, br * 1.6f, Pal.Gold, 0.5f);
        g.Circle(bx, by, br, Pal.Red);
        g.Ring(bx, by, br - u * 1.2f, u * 2.4f, Pal.Gold);
        float ts = u * 2.3f;
        g.Text("50", bx + ts * 0.5f + u * 0.4f, by - Gfx.GlyphH * ts / 2 + u * 0.4f, ts, Color.Black * 0.5f, Align.Center);
        g.Text("50", bx + ts * 0.5f, by - Gfx.GlyphH * ts / 2, ts, Pal.White, Align.Center);
    }

    /// <summary>Tape and logo in front of tiles from a dozen of the games.</summary>
    private void DrawCollage(Gfx g, bool titled)
    {
        var v = g.Visible;
        Brand.Backdrop(g, 2.5f);
        var info = GameCatalog.Info;
        int[] featured = [11, 21, 22, 2, 31, 40, 48, 30, 35, 49, 4, 13, 44, 28, 17, 33];
        float tileW = 150, tileH = 86;
        int cols = (int)MathF.Ceiling(v.W / (tileW + 14)) + 1;
        int rows = (int)MathF.Ceiling(v.H / (tileH + 14)) + 1;
        int k = 0;
        for (int row = 0; row < rows; row++)
            for (int col = 0; col < cols; col++)
            {
                float x = v.X - 40 + col * (tileW + 14) + (row % 2) * 70;
                float y = v.Y - 20 + row * (tileH + 14);
                var r = new RectF(x, y, tileW, tileH);
                var game = info[GameCatalog.IndexOf(featured[k++ % featured.Length])];
                g.RoundRect(r.Inflate(3, 3), 8, game.Accent * 0.8f);
                g.SetClip(r);
                g.Rect(r, Color.Black);
                try
                {
                    game.DrawIcon(g, r, 1.7f + k);
                }
                catch (Exception)
                {
                }
                g.Offset = Vector2.Zero;
                g.SetClip(null);
            }
        // Darken towards the centre so the tape stands out.
        g.Rect(v, Color.Black * 0.35f);
        bool wide = v.W / v.H > 1.3f;
        float cw = wide ? MathF.Min(v.W * 0.36f, v.H * 0.62f / 0.63f) : MathF.Min(v.W * 0.62f, v.H * 0.5f / 0.63f);
        float cassetteY = titled ? v.CenterY + v.H * 0.1f : v.CenterY;
        float cassetteX = titled ? v.CenterX : v.CenterX + v.W * 0.18f;
        g.Glow(cassetteX, cassetteY, cw * 1.1f, Pal.Magenta, 0.5f);
        Brand.Cassette(g, cassetteX, cassetteY, cw, 0.8f, 0);
        if (titled)
        {
            float scale = MathF.Min(v.W * 0.78f / 55.5f, v.H * 0.13f / 8f);
            float ly = v.Y + v.H * (wide ? 0.17f : 0.15f);
            g.RoundRect(v.CenterX - scale * 40, ly - scale * 7, scale * 80, scale * 14, scale * 3, Color.Black * 0.55f);
            Brand.Logo(g, v.CenterX, ly, scale, 0.4f, false);
            g.TextShadow("50 ORIC-1 CLASSICS, REBORN", v.CenterX, ly + scale * 5.5f, scale * 0.3f, Pal.Cyan, Align.Center);
        }
    }
}
