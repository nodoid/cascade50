using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;
using Cascade50.Core.Graphics;

namespace Cascade50.Core.Input;

/// <summary>Polls MonoGame's keyboard, mouse, game pad and touch panel into <see cref="Controls"/>.</summary>
public sealed class InputManager
{
    private readonly bool _isMobile;
    private KeyboardState _prevKeys;
    private MouseState _prevMouse;
    private GamePadState _prevPad;
    private bool _prevFire, _prevAlt;
    private int _pointerTouchId = -1;
    private readonly TouchPad _pad = new();
    private bool _first = true;

    public InputManager(bool isMobile)
    {
        _isMobile = isMobile;
        Controls.IsTouch = isMobile;
        if (isMobile)
            TouchPanel.EnabledGestures = GestureType.None;
    }

    public Controls Controls { get; } = new();
    public TouchPad TouchPad => _pad;

    /// <param name="toVirtual">Window pixels to virtual pixels.</param>
    /// <param name="visible">The visible virtual area (for laying out the touch controls).</param>
    /// <param name="pad">Touch controls to show this frame, or null for none.</param>
    /// <param name="textMode">Letter keys type text instead of steering (word games).</param>
    public void Update(Func<Vector2, Vector2> toVirtual, RectF visible, Pad? pad, bool textMode, bool isActive)
    {
        var c = Controls;
        var keys = Keyboard.GetState();
        var gp = GamePad.GetState(PlayerIndex.One);
        if (_first)
        {
            _prevKeys = keys;
            _prevPad = gp;
            _prevMouse = Mouse.GetState();
            _first = false;
        }
        bool Down(Keys k) => keys.IsKeyDown(k);
        bool Hit(Keys k) => keys.IsKeyDown(k) && !_prevKeys.IsKeyDown(k);
        bool PadDown(Buttons b) => gp.IsButtonDown(b);
        bool PadHit(Buttons b) => gp.IsButtonDown(b) && !_prevPad.IsButtonDown(b);

        c.PressedKeys.Clear();
        c.HeldKeys.Clear();
        c.Typed.Clear();
        foreach (var k in keys.GetPressedKeys())
        {
            c.HeldKeys.Add(k);
            if (_prevKeys.IsKeyDown(k))
                continue;
            c.PressedKeys.Add(k);
            if (k >= Keys.A && k <= Keys.Z)
                c.Typed.Add((char)('A' + (k - Keys.A)));
            else if (k >= Keys.D0 && k <= Keys.D9)
                c.Typed.Add((char)('0' + (k - Keys.D0)));
            else if (k >= Keys.NumPad0 && k <= Keys.NumPad9)
                c.Typed.Add((char)('0' + (k - Keys.NumPad0)));
            else if (k == Keys.OemMinus || k == Keys.Subtract)
                c.Typed.Add('-');
        }

        // ---- directions ----
        float x = 0, y = 0;
        if (Down(Keys.Left) || (!textMode && Down(Keys.A))) x -= 1;
        if (Down(Keys.Right) || (!textMode && Down(Keys.D))) x += 1;
        if (Down(Keys.Up) || (!textMode && Down(Keys.W))) y -= 1;
        if (Down(Keys.Down) || (!textMode && Down(Keys.S))) y += 1;
        var stick = gp.ThumbSticks.Left;
        if (MathF.Abs(stick.X) > 0.25f) x = stick.X;
        if (MathF.Abs(stick.Y) > 0.25f) y = -stick.Y;
        if (PadDown(Buttons.DPadLeft)) x = -1;
        if (PadDown(Buttons.DPadRight)) x = 1;
        if (PadDown(Buttons.DPadUp)) y = -1;
        if (PadDown(Buttons.DPadDown)) y = 1;

        bool fire = Down(Keys.Space) || Down(Keys.Enter) || (!textMode && Down(Keys.Z)) || PadDown(Buttons.A);
        bool alt = (!textMode && Down(Keys.X)) || Down(Keys.LeftShift) || Down(Keys.RightShift) || PadDown(Buttons.B) || PadDown(Buttons.X);

        c.EnterPressed = Hit(Keys.Enter);
        c.BackspacePressed = Hit(Keys.Back);
        c.BackPressed = Hit(Keys.Escape) || PadHit(Buttons.Back);
        c.PausePressed = (!textMode && Hit(Keys.P)) || PadHit(Buttons.Start);

        // ---- pointer: mouse on desktop ----
        bool pDown = false, pPressed = false, pReleased = false, pMoved = false;
        var pPos = c.Pointer;
        c.Wheel = 0;
        if (!_isMobile)
        {
            var m = Mouse.GetState();
            var v = toVirtual(new Vector2(m.X, m.Y));
            bool inWindow = isActive && m.X >= 0 && m.Y >= 0;
            pMoved = m.X != _prevMouse.X || m.Y != _prevMouse.Y;
            pPos = v;
            c.HasHover = inWindow;
            if (inWindow)
            {
                pDown = m.LeftButton == ButtonState.Pressed;
                pPressed = pDown && _prevMouse.LeftButton == ButtonState.Released;
                pReleased = !pDown && _prevMouse.LeftButton == ButtonState.Pressed;
                if (m.RightButton == ButtonState.Pressed)
                    alt = true;
                c.Wheel = (m.ScrollWheelValue - _prevMouse.ScrollWheelValue) / 120f;
            }
            else if (_prevMouse.LeftButton == ButtonState.Pressed)
            {
                pReleased = true;
            }
            _prevMouse = m;
        }

        // ---- touch ----
        _pad.Layout(visible, pad);
        _pad.BeginFrame();
        if (_isMobile)
        {
            var touches = TouchPanel.GetState();
            bool pointerSeen = false;
            foreach (var t in touches)
            {
                var v = toVirtual(t.Position);
                bool ended = t.State == TouchLocationState.Released || t.State == TouchLocationState.Invalid;
                if (_pad.Handle(t.Id, v, t.State == TouchLocationState.Pressed, ended))
                    continue;
                if (_pointerTouchId == -1 && t.State == TouchLocationState.Pressed)
                    _pointerTouchId = t.Id;
                if (t.Id != _pointerTouchId)
                    continue;
                pointerSeen = true;
                pMoved = Vector2.DistanceSquared(v, pPos) > 0.01f;
                pPos = v;
                if (ended)
                {
                    pReleased = true;
                    _pointerTouchId = -1;
                }
                else
                {
                    pDown = true;
                    pPressed = t.State == TouchLocationState.Pressed;
                }
            }
            if (!pointerSeen && _pointerTouchId != -1)
            {
                // The finger vanished without a release event.
                pReleased = true;
                _pointerTouchId = -1;
            }
            _pad.EndFrame();
            if (_pad.StickActive)
            {
                x = _pad.StickX;
                y = _pad.StickY;
            }
            fire |= _pad.FireDown;
            alt |= _pad.AltDown;
        }

        // ---- combine ----
        x = Math.Clamp(x, -1, 1);
        y = Math.Clamp(y, -1, 1);
        c.SetDirections(x, y);
        if (Hit(Keys.Left) || (!textMode && Hit(Keys.A)) || PadHit(Buttons.DPadLeft)) c.LeftPressed = true;
        if (Hit(Keys.Right) || (!textMode && Hit(Keys.D)) || PadHit(Buttons.DPadRight)) c.RightPressed = true;
        if (Hit(Keys.Up) || (!textMode && Hit(Keys.W)) || PadHit(Buttons.DPadUp)) c.UpPressed = true;
        if (Hit(Keys.Down) || (!textMode && Hit(Keys.S)) || PadHit(Buttons.DPadDown)) c.DownPressed = true;

        c.Fire = fire;
        c.FirePressed = fire && !_prevFire;
        c.FireReleased = !fire && _prevFire;
        c.Alt = alt;
        c.AltPressed = alt && !_prevAlt;
        _prevFire = fire;
        _prevAlt = alt;

        if (pPressed)
            c.PointerStart = pPos;
        c.PointerDown = pDown;
        c.PointerPressed = pPressed;
        c.PointerReleased = pReleased;
        c.PointerMoved = pMoved;
        c.Pointer = pPos;

        _prevKeys = keys;
        _prevPad = gp;
    }

    /// <summary>Forgets held buttons (after a pause or scene change) so nothing fires by accident.</summary>
    public void Reset()
    {
        _prevFire = true;
        _prevAlt = true;
        _pad.Release();
    }
}

/// <summary>
/// The on-screen controls for phones and tablets: a stick on the left and FIRE / ALT buttons on the
/// right, placed in the margins beside the playfield where there is room.
/// </summary>
public sealed class TouchPad
{
    public const float StickRadius = 50;
    public const float FireRadius = 40;
    public const float AltRadius = 32;

    private Pad? _pad;
    private int _stickId = -1, _fireId = -1, _altId = -1;
    private bool _stickSeen, _fireSeen, _altSeen;
    private Vector2 _stickPos;

    public Vector2 StickCenter { get; private set; }
    public Vector2 FireCenter { get; private set; }
    public Vector2 AltCenter { get; private set; }
    public Pad? Current => _pad;

    public bool StickActive => _stickId != -1;
    public float StickX { get; private set; }
    public float StickY { get; private set; }
    public bool FireDown => _fireId != -1;
    public bool AltDown => _altId != -1;

    public string FireLabel { get; set; } = "FIRE";
    public string AltLabel { get; set; } = "ALT";

    public void Layout(RectF visible, Pad? pad)
    {
        if (pad != _pad)
            Release();
        _pad = pad;
        float side = MathF.Max(0, (visible.W - Screen.Width) / 2);
        float below = MathF.Max(0, visible.Bottom - Screen.Height);
        if (below >= 100)
        {
            // Tablets: a control strip under the playfield.
            float y = Screen.Height + below / 2;
            StickCenter = new Vector2(visible.X + 90, y);
            FireCenter = new Vector2(visible.Right - 90, y);
            AltCenter = new Vector2(visible.Right - 200, y);
        }
        else
        {
            // Phones: in the side margins, overlapping the playfield's corners when they are narrow.
            float sx = visible.X + (side >= 90 ? side / 2 : 58);
            float fx = visible.Right - (side >= 90 ? side / 2 : 54);
            float by = visible.Bottom - 70;
            StickCenter = new Vector2(sx, by);
            FireCenter = new Vector2(fx, by);
            AltCenter = side > 90 ? new Vector2(fx, by - 96) : new Vector2(fx - 88, by + 24);
        }
    }

    internal void BeginFrame()
    {
        _stickSeen = _fireSeen = _altSeen = false;
    }

    /// <summary>Returns true when the touch belongs to a control.</summary>
    internal bool Handle(int id, Vector2 p, bool began, bool ended)
    {
        if (_pad is not Pad pad)
            return false;
        bool hasStick = (pad & Pad.Stick) != 0;
        if (id == _stickId || (began && hasStick && _stickId == -1 && Vector2.Distance(p, StickCenter) < StickRadius * 1.8f))
        {
            _stickId = ended ? -1 : id;
            _stickSeen = !ended;
            _stickPos = p;
            var d = (p - StickCenter) / StickRadius;
            if (d.Length() > 1)
                d.Normalize();
            StickX = (pad & Pad.Horizontal) != 0 ? d.X : 0;
            StickY = (pad & Pad.Vertical) != 0 ? d.Y : 0;
            if (MathF.Abs(StickX) < 0.18f) StickX = 0;
            if (MathF.Abs(StickY) < 0.18f) StickY = 0;
            if (ended)
                StickX = StickY = 0;
            return true;
        }
        if (id == _fireId || (began && (pad & Pad.Fire) != 0 && _fireId == -1 && Vector2.Distance(p, FireCenter) < FireRadius * 1.5f))
        {
            _fireId = ended ? -1 : id;
            _fireSeen = !ended;
            return true;
        }
        if (id == _altId || (began && (pad & Pad.Alt) != 0 && _altId == -1 && Vector2.Distance(p, AltCenter) < AltRadius * 1.5f))
        {
            _altId = ended ? -1 : id;
            _altSeen = !ended;
            return true;
        }
        return false;
    }

    internal void EndFrame()
    {
        if (!_stickSeen) { _stickId = -1; StickX = StickY = 0; }
        if (!_fireSeen) _fireId = -1;
        if (!_altSeen) _altId = -1;
    }

    public void Release()
    {
        _stickId = _fireId = _altId = -1;
        StickX = StickY = 0;
    }

    public void Draw(Gfx g)
    {
        if (_pad is not Pad pad)
            return;
        if ((pad & Pad.Stick) != 0)
        {
            var c = StickCenter;
            g.Circle(c.X, c.Y, StickRadius + 4, Color.Black * 0.35f);
            g.Ring(c.X, c.Y, StickRadius, 2.5f, Color.White * 0.35f);
            var arrow = Color.White * 0.45f;
            if ((pad & Pad.Horizontal) != 0)
            {
                Arrow(g, c + new Vector2(-StickRadius + 12, 0), MathF.PI, arrow);
                Arrow(g, c + new Vector2(StickRadius - 12, 0), 0, arrow);
            }
            if ((pad & Pad.Vertical) != 0)
            {
                Arrow(g, c + new Vector2(0, -StickRadius + 12), -MathF.PI / 2, arrow);
                Arrow(g, c + new Vector2(0, StickRadius - 12), MathF.PI / 2, arrow);
            }
            var knob = c + new Vector2(StickX, StickY) * StickRadius * 0.8f;
            g.Circle(knob.X, knob.Y, 20, (StickActive ? Pal.Accent : Color.White) * 0.55f);
        }
        if ((pad & Pad.Fire) != 0)
            Button(g, FireCenter, FireRadius, FireLabel, FireDown, Pal.Red);
        if ((pad & Pad.Alt) != 0)
            Button(g, AltCenter, AltRadius, AltLabel, AltDown, Pal.Blue);
    }

    private static void Button(Gfx g, Vector2 c, float r, string label, bool down, Color color)
    {
        g.Circle(c.X, c.Y, r + 4, Color.Black * 0.35f);
        g.Circle(c.X, c.Y, r, color * (down ? 0.75f : 0.45f));
        g.Ring(c.X, c.Y, r, 2.5f, Color.White * 0.5f);
        if (down)
            g.Glow(c.X, c.Y, r * 1.6f, color, 0.5f);
        g.TextFit(label, c.X, c.Y - 6, r * 1.7f, 1.5f, Color.White * 0.9f, Align.Center);
    }

    private static void Arrow(Gfx g, Vector2 tip, float angle, Color c)
    {
        var back = MathF2.FromAngle(angle + MathF.PI, 10);
        var side = MathF2.FromAngle(angle + MathF.PI / 2, 7);
        g.Triangle(tip, tip + back + side, tip + back - side, c);
    }
}
