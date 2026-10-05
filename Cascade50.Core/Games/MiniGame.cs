using System;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;
using Cascade50.Core.UI;

namespace Cascade50.Core.Games;

public enum Category
{
    Arcade,
    Shooter,
    Skill,
    Puzzle,
    Brain,
}

/// <summary>
/// One of the fifty games. The framework creates a fresh instance for every play, calls
/// <see cref="Start"/>, then <see cref="Update"/> 60 times a second and <see cref="Draw"/> every frame.
/// It draws the score bar (top <see cref="Screen.HudHeight"/> pixels), handles pausing, game over,
/// high scores and the touch controls.
/// </summary>
public abstract class MiniGame
{
    public const float Dt = 1f / 60f;

    // ------------------------------------------------------------------ description

    /// <summary>Position in the menu (1..50).</summary>
    public abstract int Number { get; }

    public abstract string Title { get; }
    public abstract Category Category { get; }

    /// <summary>One short sentence for the menu and store copy.</summary>
    public abstract string Tagline { get; }

    /// <summary>Paragraphs for the How to Play screen (rules, scoring, tips).</summary>
    public abstract string[] HowToPlay { get; }

    /// <summary>Control lines for keyboard / mouse players.</summary>
    public abstract string[] DesktopControls { get; }

    /// <summary>Control lines for phone / tablet players.</summary>
    public abstract string[] TouchControls { get; }

    /// <summary>On-screen touch controls this game needs.</summary>
    public virtual Pad Pad => Pad.Stick | Pad.Fire;

    public virtual string FireLabel => "FIRE";
    public virtual string AltLabel => "ALT";

    /// <summary>Letter keys type text rather than steer (WASD / Z / X / P are released).</summary>
    public virtual bool TextEntry => false;

    /// <summary>Accent colour for the menu tile and info screen.</summary>
    public virtual Color Accent => Pal.Accent;

    // ------------------------------------------------------------------ state shown by the framework

    public int Score { get; protected set; }

    /// <summary>Lives left; negative hides the lives display.</summary>
    public int Lives { get; protected set; } = -1;

    /// <summary>Level / round / wave shown in the score bar when above zero.</summary>
    public int Level { get; protected set; }

    /// <summary>Optional text for the middle of the score bar (defaults to the title).</summary>
    public string Status { get; protected set; }

    public bool IsOver { get; private set; }
    public bool Won { get; private set; }
    public string EndMessage { get; private set; }

    /// <summary>Ticks since Start (60 per second).</summary>
    public int Tick { get; private set; }

    /// <summary>Seconds since Start.</summary>
    public float Time => Tick * Dt;

    // ------------------------------------------------------------------ services

    public Controls In { get; private set; }
    public Ui Ui { get; private set; }
    public ISound Sound { get; private set; }
    public Random Rng { get; private set; }
    public Fx Fx { get; } = new();

    /// <summary>True on phones and tablets.</summary>
    public bool IsTouch => In?.IsTouch ?? false;

    internal void Begin(Controls input, Ui ui, ISound sound, int seed)
    {
        In = input;
        Ui = ui;
        Sound = sound;
        Rng = new Random(seed);
        Score = 0;
        Lives = -1;
        Level = 0;
        Status = null;
        IsOver = false;
        Won = false;
        EndMessage = null;
        Tick = 0;
        Fx.Clear();
        Start();
    }

    internal void Step()
    {
        if (IsOver)
        {
            Fx.Update(Dt);
            return;
        }
        Tick++;
        Update();
        Fx.Update(Dt);
    }

    // ------------------------------------------------------------------ to implement

    /// <summary>Resets everything for a new game.</summary>
    protected abstract void Start();

    /// <summary>One tick (1/60 s) of play. Read <see cref="In"/>, call <see cref="Ui"/> buttons here.</summary>
    protected abstract void Update();

    /// <summary>Draws the playfield (0..640 x 0..360; the top 22 px are covered by the score bar).</summary>
    public abstract void Draw(Gfx g);

    /// <summary>
    /// Draws a small animated picture of the game into <paramref name="r"/> (menu tiles are about
    /// 148x70, the info screen about 256x144). Drawing is clipped to the rectangle.
    /// </summary>
    public abstract void DrawIcon(Gfx g, RectF r, float time);

    /// <summary>
    /// Plays the game automatically for screenshots and the soak tests: set fields on
    /// <paramref name="c"/> (already cleared) as a reasonable player would. The default mashes buttons.
    /// </summary>
    public virtual void AutoPlay(Controls c)
    {
        float t = Tick * Dt;
        c.SetDirections(MathF.Sin(t * 1.3f), MathF.Sin(t * 0.7f + 1));
        c.Fire = (Tick / 8) % 2 == 0;
        c.FirePressed = Tick % 16 == 0;
        if (Tick % 45 == 0)
        {
            c.Pointer = new Vector2(Rng.Next(40, 600), Rng.Next(40, 340));
            c.PointerPressed = true;
            c.PointerDown = true;
        }
        else if (Tick % 45 == 3)
        {
            c.PointerReleased = true;
        }
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Ends the game; the framework shows the result and saves the high score.</summary>
    protected void EndGame(bool won = false, string message = null)
    {
        if (IsOver)
            return;
        IsOver = true;
        Won = won;
        EndMessage = message;
        Sound?.StopLoops();
        Sound?.Play(won ? Sfx.Win : Sfx.GameOver);
    }

    internal void Abort(string message) => EndGame(false, message);

    /// <summary>Adds points and floats "+n" at (x, y).</summary>
    protected void AddScore(int points, float x, float y, Color? color = null)
    {
        Score += points;
        if (points != 0)
            Fx.Float((points > 0 ? "+" : "") + points, x, y, color ?? Pal.Yellow);
    }

    protected void AddScore(int points) => Score += points;

    /// <summary>Loses a life (with sound and shake). Ends the game and returns true when none are left.</summary>
    protected bool LoseLife()
    {
        Lives--;
        Fx.Shake(5, 0.35f);
        if (Lives <= 0)
        {
            Lives = 0;
            EndGame();
            return true;
        }
        Sound?.Play(Sfx.Die);
        return false;
    }

    protected float Rand(float min, float max) => min + (float)Rng.NextDouble() * (max - min);
    protected int RandInt(int min, int maxExclusive) => Rng.Next(min, maxExclusive);
    protected bool Chance(float p) => Rng.NextDouble() < p;
    protected T Pick<T>(params T[] items) => items[Rng.Next(items.Length)];
}
