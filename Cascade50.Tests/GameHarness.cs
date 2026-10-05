using System;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Games;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;
using Cascade50.Core.UI;

namespace Cascade50.Tests;

/// <summary>Runs a game headless: no window, no GPU, no audio.</summary>
internal sealed class GameHarness
{
    public readonly Controls Input = new();
    public readonly NullSound Sound = new();
    public readonly NullGfx Gfx = new();
    public readonly Ui Ui;
    public MiniGame Game { get; private set; }
    private readonly int _index;
    private readonly Random _rng;

    public GameHarness(int index, int seed, bool touch = false)
    {
        Ui = new Ui(Sound);
        Input.IsTouch = touch;
        _index = index;
        Game = GameCatalog.Create(index);
        _rng = new Random(seed);
        Game.Begin(Input, Ui, Sound, seed);
    }

    /// <summary>One tick driven by the game's own autopilot.</summary>
    public void AutoTick()
    {
        Input.Clear();
        Game.AutoPlay(Input);
        Ui.BeginFrame(Input);
        Game.Step();
    }

    /// <summary>One tick of random button mashing, taps and typing.</summary>
    public void RandomTick()
    {
        Input.Clear();
        Input.SetDirections(_rng.Next(3) - 1, _rng.Next(3) - 1);
        Input.Fire = _rng.Next(2) == 0;
        Input.FirePressed = _rng.Next(6) == 0;
        Input.Alt = _rng.Next(4) == 0;
        Input.AltPressed = _rng.Next(10) == 0;
        Input.Pointer = new Vector2(_rng.Next(-20, 660), _rng.Next(-20, 380));
        Input.PointerDown = _rng.Next(3) == 0;
        Input.PointerPressed = _rng.Next(8) == 0;
        Input.PointerReleased = _rng.Next(8) == 0;
        if (_rng.Next(10) == 0)
            Input.Typed.Add((char)('A' + _rng.Next(26)));
        if (_rng.Next(12) == 0)
            Input.Typed.Add((char)('0' + _rng.Next(10)));
        Input.EnterPressed = _rng.Next(20) == 0;
        Input.BackspacePressed = _rng.Next(30) == 0;
        Ui.BeginFrame(Input);
        Game.Step();
    }

    public void Draw()
    {
        Game.Draw(Gfx);
        Ui.Draw(Gfx);
        Game.Fx.Draw(Gfx);
        Game.DrawIcon(Gfx, new RectF(0, 0, 148, 70), Game.Time);
    }

    /// <summary>Restarts the game when it ends, with a fresh instance like PLAY AGAIN.</summary>
    public void RestartIfOver(int seed)
    {
        if (!Game.IsOver)
            return;
        Game = GameCatalog.Create(_index);
        Game.Begin(Input, Ui, Sound, seed);
    }
}
