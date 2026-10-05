using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Cascade50.Core.Input;

/// <summary>Which on-screen touch controls a game needs (desktop players use the keyboard / mouse).</summary>
[Flags]
public enum Pad
{
    /// <summary>No touch pad: the game is played by tapping / dragging the playfield.</summary>
    None = 0,
    Horizontal = 1,
    Vertical = 2,
    Stick = Horizontal | Vertical,
    Fire = 4,
    Alt = 8,
}

/// <summary>
/// One tick's view of the player's input, combined from keyboard, mouse, game pad and touch.
/// Directions: cursor keys / WASD / d-pad / left stick / touch stick.
/// Fire: SPACE, Z, Enter, pad A, touch FIRE button. Alt: X, Shift, right mouse button, pad B, touch ALT button.
/// The pointer is the mouse or the first finger not on a touch control, in virtual pixels.
/// </summary>
public sealed class Controls
{
    internal readonly HashSet<Keys> PressedKeys = new();
    internal readonly HashSet<Keys> HeldKeys = new();

    public bool Left { get; set; }
    public bool Right { get; set; }
    public bool Up { get; set; }
    public bool Down { get; set; }
    public bool LeftPressed { get; set; }
    public bool RightPressed { get; set; }
    public bool UpPressed { get; set; }
    public bool DownPressed { get; set; }

    /// <summary>-1 (left) .. 1 (right); analog on sticks, else -1 / 0 / 1.</summary>
    public float AxisX { get; set; }

    /// <summary>-1 (up) .. 1 (down).</summary>
    public float AxisY { get; set; }

    public bool Fire { get; set; }
    public bool FirePressed { get; set; }
    public bool FireReleased { get; set; }
    public bool Alt { get; set; }
    public bool AltPressed { get; set; }

    /// <summary>Mouse button / finger held on the playfield.</summary>
    public bool PointerDown { get; set; }

    /// <summary>Click / tap started this tick.</summary>
    public bool PointerPressed { get; set; }

    /// <summary>Click / tap released this tick (the end of a tap or drag).</summary>
    public bool PointerReleased { get; set; }

    /// <summary>Pointer position in virtual pixels (last known position when not down).</summary>
    public Vector2 Pointer { get; set; }

    /// <summary>Where the current (or last) press started.</summary>
    public Vector2 PointerStart { get; set; }

    /// <summary>The mouse moved this tick (desktop), or a finger moved while down.</summary>
    public bool PointerMoved { get; set; }

    /// <summary>True when the pointer is a mouse hovering over the window (desktop).</summary>
    public bool HasHover { get; set; }

    /// <summary>Mouse wheel movement this tick (positive = away from the player).</summary>
    public float Wheel { get; set; }

    /// <summary>Letters A-Z and digits typed this tick (keyboard only).</summary>
    public List<char> Typed { get; } = new();

    public bool EnterPressed { get; set; }
    public bool BackspacePressed { get; set; }

    /// <summary>Esc, the Android back button or pad Back.</summary>
    public bool BackPressed { get; set; }

    /// <summary>P or pad Start.</summary>
    public bool PausePressed { get; set; }

    /// <summary>The device is a phone or tablet (touch controls are shown).</summary>
    public bool IsTouch { get; set; }

    /// <summary>A key went down this tick (desktop keyboards only).</summary>
    public bool KeyPressed(Keys key) => PressedKeys.Contains(key);

    public bool KeyDown(Keys key) => HeldKeys.Contains(key);

    /// <summary>Any confirm-style input: fire, Enter or a tap.</summary>
    public bool AnyConfirm => FirePressed || EnterPressed || PointerPressed;

    /// <summary>Clears every input (used before scripted input is applied).</summary>
    public void Clear()
    {
        Left = Right = Up = Down = false;
        LeftPressed = RightPressed = UpPressed = DownPressed = false;
        AxisX = AxisY = 0;
        Fire = FirePressed = FireReleased = Alt = AltPressed = false;
        PointerDown = PointerPressed = PointerReleased = PointerMoved = false;
        Wheel = 0;
        Typed.Clear();
        EnterPressed = BackspacePressed = BackPressed = PausePressed = false;
        PressedKeys.Clear();
        HeldKeys.Clear();
    }

    /// <summary>Copies the "held" state so that Pressed flags can be derived by scripted input.</summary>
    internal void SetDirections(float x, float y)
    {
        bool l = x < -0.35f, r = x > 0.35f, u = y < -0.35f, d = y > 0.35f;
        LeftPressed = l && !Left;
        RightPressed = r && !Right;
        UpPressed = u && !Up;
        DownPressed = d && !Down;
        Left = l;
        Right = r;
        Up = u;
        Down = d;
        AxisX = x;
        AxisY = y;
    }
}
