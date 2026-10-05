using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Cascade50.Core.Graphics;

/// <summary>
/// <see cref="Gfx"/> on the GPU: one atlas texture, one BasicEffect, and a big vertex batch that is
/// flushed only when it fills up or the clip changes.
/// </summary>
public sealed class GpuGfx : Gfx, IDisposable
{
    private const int MaxQuads = 8192;

    private readonly GraphicsDevice _device;
    private readonly Texture2D _atlas;
    private readonly BasicEffect _effect;
    private readonly VertexPositionColorTexture[] _vertices = new VertexPositionColorTexture[MaxQuads * 4];
    private readonly short[] _indices = new short[MaxQuads * 6];
    private readonly RasterizerState _scissor = new() { CullMode = CullMode.None, ScissorTestEnable = true };
    private int _quads;
    private float _scale = 1;
    private Vector2 _origin;
    private int _targetW, _targetH;

    public GpuGfx(GraphicsDevice device)
    {
        _device = device;
        _atlas = new Texture2D(device, Atlas.Size, Atlas.Size, false, SurfaceFormat.Color);
        _atlas.SetData(Atlas.BuildPixels());
        _effect = new BasicEffect(device)
        {
            TextureEnabled = true,
            VertexColorEnabled = true,
            LightingEnabled = false,
            Texture = _atlas,
            World = Matrix.Identity,
            View = Matrix.Identity,
        };
        for (int i = 0, v = 0; i < MaxQuads; i++, v += 4)
        {
            _indices[i * 6] = (short)v;
            _indices[i * 6 + 1] = (short)(v + 1);
            _indices[i * 6 + 2] = (short)(v + 2);
            _indices[i * 6 + 3] = (short)v;
            _indices[i * 6 + 4] = (short)(v + 2);
            _indices[i * 6 + 5] = (short)(v + 3);
        }
    }

    /// <summary>
    /// Starts a frame on a render surface of the given size: the 640x360 playfield is scaled to fit
    /// and centred, and <see cref="Gfx.Visible"/> covers the whole surface.
    /// </summary>
    public void Begin(int width, int height)
    {
        Layout(width, height);
        _effect.Projection = Matrix.CreateOrthographicOffCenter(0, width, height, 0, 0, 1);
        _device.BlendState = BlendState.AlphaBlend;
        _device.SamplerStates[0] = SamplerState.LinearClamp;
        _device.DepthStencilState = DepthStencilState.None;
        _device.RasterizerState = _scissor;
        _device.ScissorRectangle = new Rectangle(0, 0, width, height);
        _quads = 0;
    }

    /// <summary>Computes the virtual-to-real mapping for a surface without touching the device.</summary>
    public void Layout(int width, int height)
    {
        _targetW = width;
        _targetH = height;
        float needW = Screen.Width + 2 * (width / (float)height >= 1.95f ? SideReserve : 0);
        _scale = MathF.Min(width / needW, (float)height / Screen.Height);
        _origin = new Vector2((width - Screen.Width * _scale) / 2, (height - Screen.Height * _scale) / 2);
        // On tall (tablet) screens keep the playfield at the top, leaving room underneath for touch controls.
        if (AnchorTop)
            _origin.Y = MathF.Min(_origin.Y, MathF.Max(0, height - Screen.Height * _scale - 130 * _scale));
        PixelScale = _scale;
        Visible = new RectF(-_origin.X / _scale, -_origin.Y / _scale, width / _scale, height / _scale);
        Offset = Vector2.Zero;
    }

    public void End() => Flush();

    /// <summary>
    /// Virtual pixels kept free at each side on wide (phone) screens so the touch controls sit beside the
    /// playfield rather than over it. Set while a game with touch controls is running.
    /// </summary>
    public float SideReserve { get; set; }

    /// <summary>Phones and tablets: put spare height below the playfield rather than above and below it.</summary>
    public bool AnchorTop { get; set; }

    /// <summary>Converts a virtual position to real pixels on the current surface.</summary>
    public Vector2 ToReal(Vector2 v) => v * _scale + _origin;

    /// <summary>Converts real pixels on the current surface to virtual coordinates.</summary>
    public Vector2 ToVirtual(Vector2 real) => (real - _origin) / _scale;

    public override void SetClip(RectF? clip)
    {
        Flush();
        if (clip is RectF c)
        {
            var a = ToReal(new Vector2(c.X + Offset.X, c.Y + Offset.Y));
            var b = ToReal(new Vector2(c.Right + Offset.X, c.Bottom + Offset.Y));
            int x0 = Math.Clamp((int)MathF.Floor(a.X), 0, _targetW), y0 = Math.Clamp((int)MathF.Floor(a.Y), 0, _targetH);
            int x1 = Math.Clamp((int)MathF.Ceiling(b.X), 0, _targetW), y1 = Math.Clamp((int)MathF.Ceiling(b.Y), 0, _targetH);
            _device.ScissorRectangle = new Rectangle(x0, y0, Math.Max(0, x1 - x0), Math.Max(0, y1 - y0));
        }
        else
        {
            _device.ScissorRectangle = new Rectangle(0, 0, _targetW, _targetH);
        }
    }

    protected override void Emit(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, Vector2 t0, Vector2 t1, Vector2 t2, Vector2 t3,
        Color c0, Color c1, Color c2, Color c3)
    {
        if (_quads >= MaxQuads)
            Flush();
        int v = _quads * 4;
        _vertices[v] = new VertexPositionColorTexture(new Vector3(p0 * _scale + _origin, 0), c0, t0);
        _vertices[v + 1] = new VertexPositionColorTexture(new Vector3(p1 * _scale + _origin, 0), c1, t1);
        _vertices[v + 2] = new VertexPositionColorTexture(new Vector3(p2 * _scale + _origin, 0), c2, t2);
        _vertices[v + 3] = new VertexPositionColorTexture(new Vector3(p3 * _scale + _origin, 0), c3, t3);
        _quads++;
    }

    private void Flush()
    {
        if (_quads == 0)
            return;
        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, _vertices, 0, _quads * 4, _indices, 0, _quads * 2);
        }
        _quads = 0;
    }

    public void Dispose()
    {
        _atlas.Dispose();
        _effect.Dispose();
        _scissor.Dispose();
    }
}
