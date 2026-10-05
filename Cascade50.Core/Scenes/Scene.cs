using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Scenes;

/// <summary>A full screen of the app, updated at 60 ticks per second.</summary>
public abstract class Scene
{
    protected Scene(Cascade50Game app)
    {
        App = app;
    }

    protected Cascade50Game App { get; }
    protected Controls In => App.Input.Controls;

    /// <summary>Ticks since the scene was entered.</summary>
    protected int Ticks { get; private set; }

    protected float Seconds => Ticks / 60f;

    /// <summary>Touch controls to show (phones and tablets), or null.</summary>
    public virtual Pad? TouchPad => null;

    /// <summary>Letter keys type text instead of steering.</summary>
    public virtual bool TextMode => false;

    public virtual void Enter() { }
    public virtual void Leave() { }

    /// <summary>The app lost focus (pause the game).</summary>
    public virtual void Deactivated() { }

    public void Tick()
    {
        Ticks++;
        Update();
    }

    protected abstract void Update();

    /// <summary>Draws the scene, including the Ui buttons registered this tick.</summary>
    public abstract void Draw(Gfx g);
}
