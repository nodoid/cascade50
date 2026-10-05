using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Cascade50.Core.Graphics;

/// <summary>
/// Per-game eye candy: particles, floating score text and screen shake. Every game owns one
/// (<c>Fx</c>); the framework updates it after the game and draws it on top of the game.
/// </summary>
public sealed class Fx
{
    private struct Particle
    {
        public Vector2 Pos, Vel;
        public float Life, MaxLife, Size, Gravity, Drag;
        public Color Color;
        public bool Glow;
    }

    private struct Floater
    {
        public string Text;
        public Vector2 Pos;
        public float Life;
        public Color Color;
        public float Scale;
    }

    private readonly List<Particle> _particles = new();
    private readonly List<Floater> _floaters = new();
    private readonly Random _rng = new(1234);
    private float _shake, _shakeTime;

    public int ParticleCount => _particles.Count;

    /// <summary>Current shake displacement (the framework applies it to the game's drawing).</summary>
    public Vector2 ShakeOffset { get; private set; }

    public void Clear()
    {
        _particles.Clear();
        _floaters.Clear();
        _shake = 0;
        _shakeTime = 0;
        ShakeOffset = Vector2.Zero;
    }

    /// <summary>An explosion of <paramref name="count"/> glowing sparks.</summary>
    public void Burst(float x, float y, Color color, int count = 24, float speed = 120, float life = 0.7f, float size = 2.5f, float gravity = 0, bool glow = true)
    {
        for (int i = 0; i < count && _particles.Count < 2000; i++)
        {
            float a = (float)_rng.NextDouble() * MathF2.Tau;
            float s = speed * (0.25f + 0.75f * (float)_rng.NextDouble());
            float l = life * (0.5f + 0.5f * (float)_rng.NextDouble());
            _particles.Add(new Particle
            {
                Pos = new Vector2(x, y), Vel = MathF2.FromAngle(a, s), Life = l, MaxLife = l,
                Size = size * (0.6f + 0.8f * (float)_rng.NextDouble()), Gravity = gravity, Drag = 1.5f, Color = color, Glow = glow,
            });
        }
    }

    /// <summary>A big, multi-coloured explosion with a shake.</summary>
    public void Explode(float x, float y, float power = 1f)
    {
        Burst(x, y, Pal.Yellow, (int)(18 * power), 160 * power, 0.6f, 3f);
        Burst(x, y, Pal.Orange, (int)(18 * power), 110 * power, 0.8f, 3.5f);
        Burst(x, y, Pal.Red, (int)(12 * power), 70 * power, 1.0f, 4f);
        Shake(3 * power, 0.25f);
    }

    /// <summary>One particle with an explicit velocity (trails, exhaust, sparks).</summary>
    public void Spark(float x, float y, float vx, float vy, Color color, float life = 0.4f, float size = 2f, float gravity = 0, bool glow = true)
    {
        if (_particles.Count >= 2000)
            return;
        _particles.Add(new Particle
        {
            Pos = new Vector2(x, y), Vel = new Vector2(vx, vy), Life = life, MaxLife = life, Size = size, Gravity = gravity,
            Drag = 0.5f, Color = color, Glow = glow,
        });
    }

    /// <summary>Text that floats up and fades, e.g. "+100".</summary>
    public void Float(string text, float x, float y, Color color, float scale = 1.5f)
    {
        _floaters.Add(new Floater { Text = text, Pos = new Vector2(x, y), Life = 1f, Color = color, Scale = scale });
    }

    public void Shake(float amount, float seconds = 0.2f)
    {
        _shake = MathF.Max(_shake, amount);
        _shakeTime = MathF.Max(_shakeTime, seconds);
    }

    public void Update(float dt)
    {
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Life -= dt;
            if (p.Life <= 0)
            {
                _particles.RemoveAt(i);
                continue;
            }
            p.Vel.Y += p.Gravity * dt;
            p.Vel *= MathF.Max(0, 1 - p.Drag * dt);
            p.Pos += p.Vel * dt;
            _particles[i] = p;
        }
        for (int i = _floaters.Count - 1; i >= 0; i--)
        {
            var f = _floaters[i];
            f.Life -= dt * 0.9f;
            f.Pos.Y -= 30 * dt;
            if (f.Life <= 0)
                _floaters.RemoveAt(i);
            else
                _floaters[i] = f;
        }
        if (_shakeTime > 0)
        {
            _shakeTime -= dt;
            ShakeOffset = new Vector2(((float)_rng.NextDouble() * 2 - 1) * _shake, ((float)_rng.NextDouble() * 2 - 1) * _shake);
            if (_shakeTime <= 0)
            {
                _shake = 0;
                ShakeOffset = Vector2.Zero;
            }
        }
    }

    public void Draw(Gfx g)
    {
        foreach (var p in _particles)
        {
            float k = p.Life / p.MaxLife;
            if (p.Glow)
            {
                g.Glow(p.Pos, p.Size * 3f, p.Color, 0.6f * k);
                g.Circle(p.Pos.X, p.Pos.Y, p.Size * (0.4f + 0.6f * k), Pal.Lighten(p.Color, 0.5f) * k);
            }
            else
            {
                g.Circle(p.Pos.X, p.Pos.Y, p.Size, p.Color * MathF.Min(1, k * 2));
            }
        }
        foreach (var f in _floaters)
        {
            float a = MathF.Min(1, f.Life * 2);
            g.TextShadow(f.Text, f.Pos.X, f.Pos.Y, f.Scale, f.Color * a, Align.Center, Color.Black * (0.8f * a));
        }
    }
}
