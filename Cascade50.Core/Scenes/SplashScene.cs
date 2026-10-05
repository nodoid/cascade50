using System;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;

namespace Cascade50.Core.Scenes;

/// <summary>The animated title: the tape slides in, the reels spin up and the logo drops into place.</summary>
public sealed class SplashScene : Scene
{
    private bool _chimed;

    public SplashScene(Cascade50Game app) : base(app)
    {
    }

    public override void Enter() => App.Sound.SetMusic(true);

    protected override void Update()
    {
        if (!_chimed && Seconds > 0.9f)
        {
            _chimed = true;
            App.Sound.Play(Sfx.Start);
        }
        if (Seconds > 0.4f && (In.AnyConfirm || In.BackPressed || In.PointerReleased))
            App.ShowMenu();
        else if (Seconds > 7f)
            App.ShowMenu();
    }

    public override void Draw(Gfx g) => DrawAt(g, Seconds, In.IsTouch);

    /// <summary>Draws the title at a given moment (also used for store art).</summary>
    public static void DrawAt(Gfx g, float t, bool touch)
    {
        Brand.Backdrop(g, t);
        float slide = MathF2.EaseOut(MathF2.Clamp(t / 0.9f, 0, 1));
        float cy = 210 + (1 - slide) * 260;
        Brand.Cassette(g, 320, cy, 230, t, 0.5f + slide * 1.5f);

        float drop = MathF2.EaseOut(MathF2.Clamp((t - 0.7f) / 0.6f, 0, 1));
        if (drop > 0)
        {
            float ly = -40 + drop * 100;
            Brand.Logo(g, 320, ly, 6f, t);
        }

        float info = MathF2.Clamp((t - 1.3f) / 0.5f, 0, 1);
        if (info > 0)
        {
            g.TextShadow("50 ORIC-1 CLASSICS, REBORN", 320, 92, 1.5f, Pal.Cyan * info, Align.Center);
            g.TextShadow(Cascade50Game.Credit, 320, 318, 1.5f, Pal.White * info, Align.Center);
            g.TextShadow(Cascade50Game.BasedOn, 320, 334, 1f, Pal.LightGrey * info, Align.Center);
        }
        if (t > 1.8f && (int)(t * 2) % 2 == 0)
            g.TextShadow(touch ? "TAP TO START" : "PRESS SPACE", 320, 296, 1.5f, Pal.Yellow, Align.Center);
    }
}
