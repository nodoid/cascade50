using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 12 Galactic Dog Fight: a Spacewar-style duel around a star whose gravity drags at everything.
/// Out-fly the enemy aces with limited fuel and torpedoes; hyperspace if you dare.
/// </summary>
public sealed class GalacticDogFight : MiniGame
{
    public override int Number => 12;
    public override string Title => "Galactic Dog Fight";
    public override Category Category => Category.Shooter;
    public override string Tagline => "Duel enemy aces round a hungry star. Mind the gravity.";
    public override Color Accent => Pal.Cyan;
    public override Pad Pad => Pad.Horizontal | Pad.Fire | Pad.Alt;
    public override string AltLabel => "THRUST";

    public override string[] HowToPlay =>
    [
        "Shoot down the enemy aces. The star pulls everything in: touch it and you burn. The edges wrap round.",
        "Fuel and torpedoes are limited and your shield takes one hit. Hyperspace is a random jump that may go wrong.",
    ];

    public override string[] DesktopControls => ["LEFT / RIGHT turn, UP thrust.", "SPACE fire, DOWN hyperspace."];
    public override string[] TouchControls => ["Stick turns, THRUST and FIRE.", "HYPER button to jump."];

    private const float ArenaTop = Screen.HudHeight, ArenaH = Screen.Height - Screen.HudHeight;
    private static readonly Vector2 StarPos = new(320, ArenaTop + ArenaH / 2);
    private const float G = 240000, StarR = 13;
    private const float EnemyTorpSpeed = 195;
    private const float TorpSpeed = 250, TorpLife = 1.7f;
    private const float MaxFuel = 100;
    private const int MaxTorps = 20;
    private static readonly RectF HyperRect = new(552, 28, 80, 30);

    private static readonly Vector2[] PlayerShape = [new(12, 0), new(-8, -8), new(-4, 0), new(-8, 8)];
    private static readonly Vector2[] EnemyShape = [new(11, 0), new(1, -4), new(-8, -10), new(-5, 0), new(-8, 10), new(1, 4)];

    private sealed class Ship
    {
        public bool Player, Alive = true;
        public Vector2 Pos, Vel;
        public float Angle, Fuel = MaxFuel, FireCd, Invuln, Hyper, Respawn, Flash, Dodge;
        public int Torps = MaxTorps, Hits = 1, HyperUses;
        public bool Thrusting;
        public Color Colour;
    }

    private struct Torp
    {
        public Vector2 Pos, Vel;
        public float Life;
        public bool FromPlayer;
    }

    private readonly List<Ship> _enemies = new();
    private readonly List<Torp> _torps = new();
    private Ship _player;
    private int _round;
    private float _banner, _roundGap;

    protected override void Start()
    {
        Lives = 3;
        _round = 0;
        _player = new Ship { Player = true, Colour = Pal.Cyan };
        NextRound();
    }

    private void NextRound()
    {
        _round++;
        Level = _round;
        Status = "ROUND " + _round;
        _torps.Clear();
        _enemies.Clear();
        PlaceInOrbit(_player, MathF.PI, 190);
        _player.Fuel = MaxFuel;
        _player.Torps = MaxTorps;
        _player.Invuln = 1.5f;
        _player.Alive = true;
        _player.HyperUses = 0;
        _player.Hits = 2;
        int count = _round >= 3 ? 2 : 1;
        if (_round >= 7) count = 3;
        for (int i = 0; i < count; i++)
        {
            var e = new Ship
            {
                Colour = i == 0 ? Pal.Magenta : i == 1 ? Pal.Orange : Pal.Lime,
                Hits = _round >= 6 ? 3 : _round >= 2 ? 2 : 1,
                FireCd = 1.5f + i,
            };
            PlaceInOrbit(e, count == 1 ? 0 : i * MathF2.Tau / count + MathF.PI / count, count == 1 ? 190 : 150);
            _enemies.Add(e);
        }
        _banner = 2.2f;
    }

    private static void PlaceInOrbit(Ship s, float a, float r)
    {
        s.Pos = StarPos + MathF2.FromAngle(a, r) * new Vector2(1.3f, 0.75f);
        float d = Vector2.Distance(s.Pos, StarPos);
        float v = MathF.Sqrt(G / d) * 0.95f;
        s.Vel = MathF2.FromAngle(a + MathF.PI / 2, v);
        s.Angle = a + MathF.PI / 2;
        s.Alive = true;
    }

    private float Skill => MathF.Min(1, 0.2f + _round * 0.08f);

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        if (_banner > 0) _banner -= Dt;

        bool hyperButton = Ui.Button(HyperRect, "HYPER", Keys.H, _player.Alive && _player.Hyper <= 0 && _player.Respawn <= 0, Pal.Purple, textScale: 1.5f);
        UpdatePlayer(hyperButton || In.DownPressed);
        foreach (var e in _enemies)
        {
            if (!e.Alive) continue;
            EnemyAi(e);
            Physics(e);
        }
        UpdateTorps();
        Collisions();

        if (_roundGap > 0)
        {
            _roundGap -= Dt;
            if (_roundGap <= 0)
                NextRound();
            return;
        }
        bool any = false;
        foreach (var e in _enemies)
            if (e.Alive) any = true;
        if (!any && _player.Alive)
        {
            int bonus = 500 * _round + (int)_player.Fuel * 5 + _player.Torps * 20;
            AddScore(bonus, StarPos.X, StarPos.Y - 50, Pal.Cyan);
            Fx.Float("ROUND CLEAR", StarPos.X, StarPos.Y - 80, Pal.Yellow, 2.5f);
            Sound.Play(Sfx.LevelUp);
            _roundGap = 2.5f;
        }
    }

    private void UpdatePlayer(bool hyper)
    {
        var p = _player;
        if (p.Respawn > 0)
        {
            p.Respawn -= Dt;
            if (p.Respawn <= 0)
                RespawnPlayer();
            return;
        }
        if (!p.Alive) return;
        if (p.Hyper > 0)
        {
            p.Hyper -= Dt;
            if (p.Hyper <= 0)
                ExitHyperspace(p);
            return;
        }
        if (p.Invuln > 0) p.Invuln -= Dt;
        p.Angle += In.AxisX * 4.2f * Dt;
        p.Thrusting = (In.Up || In.Alt) && p.Fuel > 0;
        if (p.Thrusting)
        {
            p.Vel += MathF2.FromAngle(p.Angle, 130 * Dt);
            p.Fuel = MathF.Max(0, p.Fuel - 9 * Dt);
            if (p.Fuel <= 0)
                Sound.Play(Sfx.Back, -0.4f, 0.5f);
        }
        Sound.Loop(LoopSfx.Thrust, p.Thrusting, 0, 0.5f);
        p.FireCd -= Dt;
        if (In.FirePressed && p.FireCd <= 0)
        {
            if (p.Torps > 0)
                Fire(p);
            else
                Sound.Play(Sfx.Click, -0.5f, 0.5f);
        }
        if (hyper && p.Alive)
            EnterHyperspace(p);
        Physics(p);
    }

    private void Fire(Ship s)
    {
        s.Torps--;
        s.FireCd = s.Player ? 0.22f : MathF.Max(0.9f, 2.0f - 0.1f * _round);
        var dir = MathF2.FromAngle(s.Angle);
        _torps.Add(new Torp { Pos = s.Pos + dir * 12, Vel = s.Vel + dir * (s.Player ? TorpSpeed : EnemyTorpSpeed), Life = TorpLife, FromPlayer = s.Player });
        Sound.Play(s.Player ? Sfx.Laser : Sfx.Zap, s.Player ? 0.1f : -0.2f, 0.5f);
        Fx.Spark(s.Pos.X + dir.X * 12, s.Pos.Y + dir.Y * 12, dir.X * 40, dir.Y * 40, s.Colour, 0.2f, 2);
    }

    private void EnterHyperspace(Ship s)
    {
        s.Hyper = 0.9f;
        s.Thrusting = false;
        Fx.Burst(s.Pos.X, s.Pos.Y, s.Colour, 24, 120, 0.5f, 2f);
        Sound.Play(Sfx.Warp);
    }

    private void ExitHyperspace(Ship s)
    {
        s.HyperUses++;
        // Find somewhere away from the star.
        for (int i = 0; i < 20; i++)
        {
            s.Pos = new Vector2(Rand(20, 620), Rand(ArenaTop + 20, Screen.Height - 20));
            if (Vector2.Distance(s.Pos, StarPos) > 90) break;
        }
        s.Vel *= 0.3f;
        Fx.Burst(s.Pos.X, s.Pos.Y, Pal.White, 20, 100, 0.4f, 2f);
        Sound.Play(Sfx.Warp, 0.5f, 0.7f);
        if (Chance(0.08f + 0.14f * (s.HyperUses - 1)))
        {
            Fx.Float("HYPERSPACE FAILURE!", s.Pos.X, s.Pos.Y - 20, Pal.Red, 1.5f);
            Destroy(s);
        }
        else
            s.Invuln = MathF.Max(s.Invuln, 0.4f);
    }

    private void RespawnPlayer()
    {
        var p = _player;
        // The orbit point furthest from the enemies.
        float bestA = 0, bestD = -1;
        for (int i = 0; i < 8; i++)
        {
            float a = i * MathF2.Tau / 8;
            var pos = StarPos + MathF2.FromAngle(a, 190) * new Vector2(1.3f, 0.75f);
            float d = float.MaxValue;
            foreach (var e in _enemies)
                if (e.Alive) d = MathF.Min(d, Vector2.Distance(pos, e.Pos));
            if (d > bestD) { bestD = d; bestA = a; }
        }
        PlaceInOrbit(p, bestA, 190);
        p.Fuel = MaxFuel;
        p.Torps = MaxTorps;
        p.Invuln = 2.5f;
        p.HyperUses = 0;
        p.Hits = 2;
        p.Alive = true;
        Sound.Play(Sfx.Start, 0, 0.6f);
    }

    private void Physics(Ship s)
    {
        if (s.Hyper > 0) return;
        if (s.Flash > 0) s.Flash -= Dt * 3;
        var toStar = StarPos - s.Pos;
        float r2 = MathF.Max(toStar.LengthSquared(), 400);
        float r = MathF.Sqrt(r2);
        s.Vel += toStar / r * MathF.Min(400, G / r2) * Dt;
        float sp = s.Vel.Length();
        if (sp > 175) s.Vel *= 175 / sp;
        s.Pos += s.Vel * Dt;
        s.Pos = Wrap(s.Pos);
        if (s.Thrusting && Tick % 2 == 0)
        {
            var back = MathF2.FromAngle(s.Angle + MathF.PI);
            var b = s.Pos + back * 7;
            Fx.Spark(b.X, b.Y, back.X * 90 + Rand(-20, 20) + s.Vel.X * 0.5f, back.Y * 90 + Rand(-20, 20) + s.Vel.Y * 0.5f,
                Chance(0.5f) ? Pal.Orange : Pal.Yellow, 0.3f, 1.8f);
        }
        // Burning in the star.
        if (r < StarR + 6 && s.Alive)
        {
            Fx.Burst(s.Pos.X, s.Pos.Y, Pal.Yellow, 20, 100, 0.5f, 2);
            Destroy(s, true);
        }
    }

    private static Vector2 Wrap(Vector2 p)
    {
        if (p.X < 0) p.X += 640;
        else if (p.X >= 640) p.X -= 640;
        if (p.Y < ArenaTop) p.Y += ArenaH;
        else if (p.Y >= Screen.Height) p.Y -= ArenaH;
        return p;
    }

    /// <summary>Shortest offset from a to b on the wrapped arena.</summary>
    private static Vector2 Delta(Vector2 a, Vector2 b)
    {
        var d = b - a;
        if (d.X > 320) d.X -= 640;
        else if (d.X < -320) d.X += 640;
        if (d.Y > ArenaH / 2) d.Y -= ArenaH;
        else if (d.Y < -ArenaH / 2) d.Y += ArenaH;
        return d;
    }

    private void EnemyAi(Ship e)
    {
        if (e.Hyper > 0)
        {
            e.Hyper -= Dt;
            if (e.Hyper <= 0) ExitHyperspace(e);
            return;
        }
        if (e.Invuln > 0) e.Invuln -= Dt;
        float skill = Skill;
        float turn = 2.4f + 2.2f * skill;
        e.FireCd -= Dt;
        var toStar = StarPos - e.Pos;
        float starD = toStar.Length();
        float want;
        bool thrust = false;
        float speed = e.Vel.Length();
        // Danger: falling into the star.
        float inward = Vector2.Dot(e.Vel, toStar / MathF.Max(1, starD));
        if (starD < 70 + MathF.Max(0, inward) * 0.6f)
        {
            var away = -toStar / MathF.Max(1, starD);
            var tangent = new Vector2(-away.Y, away.X);
            if (Vector2.Dot(tangent, e.Vel) < 0) tangent = -tangent;
            want = MathF2.Angle(away + tangent * 0.8f);
            thrust = true;
        }
        else
        {
            var target = _player;
            bool hasTarget = target.Alive && target.Hyper <= 0 && target.Respawn <= 0;
            var d = hasTarget ? Delta(e.Pos, target.Pos) : Delta(e.Pos, StarPos + new Vector2(0, 120));
            float dist = d.Length();
            // Lead the target.
            float t = dist / EnemyTorpSpeed;
            var aim = hasTarget ? d + (target.Vel - e.Vel) * t * skill : d;
            want = MathF2.Angle(aim) + MathF.Sin(Time * 1.7f + e.Colour.R) * 0.25f * (1 - skill);
            // Dodge the player's torpedoes.
            {
                foreach (var tp in _torps)
                {
                    if (!tp.FromPlayer) continue;
                    var td = Delta(tp.Pos, e.Pos);
                    if (td.LengthSquared() < 70 * 70 && Vector2.Dot(td, tp.Vel - e.Vel) > 0 && Chance(0.05f + skill * 0.25f))
                        e.Dodge = 0.4f;
                }
                // Random jinks make the aces harder to hit.
                if (e.Dodge <= 0 && Chance(Dt * (0.25f + skill * 0.5f)))
                    e.Dodge = Rand(0.25f, 0.5f);
            }
            if (hasTarget && CollisionCourse(e, target, 0.7f + skill * 0.3f))
            {
                // Too close: peel away rather than collide.
                want = MathF2.Angle(d) + MathF.PI * 0.6f;
                thrust = true;
            }
            else if (e.Dodge > 0)
            {
                e.Dodge -= Dt;
                want = e.Angle + MathF.PI / 2;
                thrust = true;
            }
            else if (dist > 170 || speed < 30)
                thrust = MathF.Abs(MathF2.WrapAngle(want - e.Angle)) < 0.6f && speed < 110;
            float tol = 0.08f + (1 - skill) * 0.18f;
            if (hasTarget && e.FireCd <= 0 && dist < 280 && MathF.Abs(MathF2.WrapAngle(want - e.Angle)) < tol)
            {
                e.Torps = 99;
                Fire(e);
            }
            // Aces in later rounds jump away when the player is right on them.
            if (_round >= 4 && hasTarget && dist < 40 && Chance(0.01f * skill))
                EnterHyperspace(e);
        }
        float diff = MathF2.WrapAngle(want - e.Angle);
        e.Angle += MathF2.Clamp(diff, -turn * Dt, turn * Dt);
        e.Thrusting = thrust;
        if (thrust)
            e.Vel += MathF2.FromAngle(e.Angle, 120 * Dt);
    }

    /// <summary>Will the two ships pass within 34 px of each other in the next few moments?</summary>
    private static bool CollisionCourse(Ship a, Ship b, float horizon)
    {
        var d = Delta(a.Pos, b.Pos);
        var v = b.Vel - a.Vel;
        float vv = v.LengthSquared();
        float t = vv < 1 ? 0 : MathF2.Clamp(-Vector2.Dot(d, v) / vv, 0, horizon);
        return (d + v * t).LengthSquared() < 34 * 34;
    }

    private void UpdateTorps()
    {
        for (int i = _torps.Count - 1; i >= 0; i--)
        {
            var t = _torps[i];
            var toStar = StarPos - t.Pos;
            float r2 = MathF.Max(toStar.LengthSquared(), 400);
            t.Vel += toStar / MathF.Sqrt(r2) * MathF.Min(400, G / r2) * 0.5f * Dt;
            t.Pos = Wrap(t.Pos + t.Vel * Dt);
            t.Life -= Dt;
            if (r2 < StarR * StarR)
            {
                Fx.Burst(t.Pos.X, t.Pos.Y, Pal.Yellow, 5, 40, 0.3f, 1.5f);
                t.Life = 0;
            }
            if (t.Life <= 0)
                _torps.RemoveAt(i);
            else
                _torps[i] = t;
        }
    }

    private bool Vulnerable(Ship s) => s.Alive && s.Hyper <= 0 && s.Respawn <= 0 && s.Invuln <= 0;

    private void Collisions()
    {
        for (int i = _torps.Count - 1; i >= 0; i--)
        {
            var t = _torps[i];
            Ship hit = null;
            if (!t.FromPlayer && Vulnerable(_player) && Delta(t.Pos, _player.Pos).LengthSquared() < 9 * 9)
                hit = _player;
            else if (t.FromPlayer)
                foreach (var e in _enemies)
                    if (Vulnerable(e) && Delta(t.Pos, e.Pos).LengthSquared() < 10 * 10) { hit = e; break; }
            if (hit == null) continue;
            _torps.RemoveAt(i);
            Damage(hit);
        }
        if (Vulnerable(_player))
            foreach (var e in _enemies)
                if (Vulnerable(e) && Delta(e.Pos, _player.Pos).LengthSquared() < 16 * 16)
                {
                    // A glancing collision: both ships bounce apart and take a hit.
                    var n = Delta(e.Pos, _player.Pos);
                    n = n.LengthSquared() < 0.01f ? Vector2.UnitX : Vector2.Normalize(n);
                    float rel = Vector2.Dot(_player.Vel - e.Vel, n);
                    if (rel < 0)
                    {
                        _player.Vel -= n * rel;
                        e.Vel += n * rel;
                    }
                    _player.Vel += n * 40;
                    e.Vel -= n * 40;
                    Fx.Burst((e.Pos.X + _player.Pos.X) / 2, (e.Pos.Y + _player.Pos.Y) / 2, Pal.White, 16, 120, 0.4f, 2);
                    Sound.Play(Sfx.Thud);
                    Damage(e);
                    Damage(_player);
                    break;
                }
    }

    private void Damage(Ship s)
    {
        s.Hits--;
        if (s.Hits > 0)
        {
            s.Flash = 1;
            s.Invuln = 0.3f;
            Fx.Burst(s.Pos.X, s.Pos.Y, Pal.White, 12, 90, 0.4f, 2);
            Sound.Play(Sfx.Hit);
            if (s.Player)
            {
                Fx.Float("SHIELD LOST", s.Pos.X, s.Pos.Y - 14, Pal.Orange, 1.5f);
                s.Invuln = 1.2f;
            }
            else
                AddScore(100, s.Pos.X, s.Pos.Y - 12, Pal.Cyan);
            return;
        }
        Destroy(s);
    }

    private void Destroy(Ship s, bool star = false)
    {
        if (!s.Alive) return;
        s.Alive = false;
        s.Thrusting = false;
        Fx.Explode(s.Pos.X, s.Pos.Y, 1.3f);
        Fx.Burst(s.Pos.X, s.Pos.Y, s.Colour, 30, 160, 0.9f, 2.5f);
        Sound.Play(Sfx.BigExplode);
        if (s.Player)
        {
            Sound.Loop(LoopSfx.Thrust, false);
            if (!LoseLife())
                s.Respawn = 2.2f;
            if (star)
                Fx.Float("BURNT UP!", s.Pos.X, s.Pos.Y - 16, Pal.Orange, 1.5f);
        }
        else
        {
            AddScore(star ? 250 * _round : 500 * _round, s.Pos.X, s.Pos.Y - 14, Pal.Yellow);
            Sound.Play(Sfx.Bonus, 0, 0.6f);
        }
    }

    // ------------------------------------------------------------------ drawing

    public override void Draw(Gfx g)
    {
        float time = Time;
        Backdrops.Space(g, time, 0, 12, Screen.Bounds);
        g.Glow(110, 90, 200, Pal.Purple, 0.22f);
        g.Glow(560, 300, 220, Pal.Teal, 0.18f);
        DrawStar(g, StarPos, 1f, time);

        foreach (var t in _torps)
        {
            var c = t.FromPlayer ? Pal.Cyan : Pal.Pink;
            float a = MathF.Min(1, t.Life * 3);
            g.Glow(t.Pos, 9, c, 0.8f * a);
            g.Circle(t.Pos.X, t.Pos.Y, 2, Color.White * a);
            var tail = t.Pos - Vector2.Normalize(t.Vel) * 7;
            if (Vector2.DistanceSquared(tail, t.Pos) < 100)
                g.Line(tail, t.Pos, 1.5f, c * (0.6f * a));
        }

        foreach (var e in _enemies)
            DrawShip(g, e, EnemyShape, time);
        DrawShip(g, _player, PlayerShape, time);

        // Fuel and torpedoes.
        float x = 12, y = IsTouch ? 38 : 334;
        g.RoundRect(x - 4, y - 6, 150, 28, 6, Color.Black * 0.45f);
        g.Text("FUEL", x, y - 1, 1f, Pal.LightGrey);
        g.RoundRect(x + 30, y - 2, 100, 8, 3, Color.Black * 0.6f);
        float k = _player.Fuel / MaxFuel;
        var fc = k > 0.3f ? Pal.Orange : (int)(time * 4) % 2 == 0 ? Pal.Red : Pal.Darken(Pal.Red, 0.4f);
        if (k > 0) g.RoundRect(x + 31, y - 1, 98 * k, 6, 2, fc);
        g.Text("TORP", x, y + 10, 1f, Pal.LightGrey);
        for (int i = 0; i < MaxTorps; i++)
            g.Rect(x + 30 + i * 5, y + 10, 3, 6, i < _player.Torps ? Pal.Cyan : Pal.DarkGrey);

        int alive = 0;
        foreach (var e in _enemies) if (e.Alive) alive++;
        if (_roundGap <= 0)
            g.Text("ACES " + alive, IsTouch ? 170 : 12, 30, 1.5f, Pal.Magenta);

        if (_banner > 0)
        {
            float a = MathF.Min(1, _banner);
            g.TextShadow("ROUND " + _round, 320, 120, 3f, Pal.Yellow * a, Align.Center);
            string sub = _enemies.Count > 1 ? _enemies.Count + " ACES" : _round >= 2 ? "A SHARPER ACE" : "ONE ACE";
            g.Text(sub, 320, 150, 1.5f, Pal.Magenta * a, Align.Center);
        }
    }

    private static void DrawStar(Gfx g, Vector2 p, float s, float time)
    {
        for (int i = 4; i >= 1; i--)
            g.Ring(p.X, p.Y, (30 + i * 28 - (time * 12) % 28) * s, 1, Pal.Orange * (0.05f * (5 - i)), 64);
        g.Glow(p.X, p.Y, 90 * s, Pal.Orange, 0.45f);
        g.Glow(p.X, p.Y, 40 * s, Pal.Yellow, 0.7f);
        for (int i = 0; i < 8; i++)
        {
            float a = time * 0.6f + i * MathF.PI / 4;
            float len = (20 + 8 * MathF.Sin(time * 3 + i * 1.7f)) * s;
            g.Line(p + MathF2.FromAngle(a, 6 * s), p + MathF2.FromAngle(a, len), 2 * s, Pal.Add(Pal.Yellow, 0.5f));
        }
        g.Circle(p.X, p.Y, 9 * s, Pal.Yellow);
        g.Circle(p.X, p.Y, 6 * s, Color.White);
    }

    private void DrawShip(Gfx g, Ship s, Vector2[] shape, float time)
    {
        if (!s.Alive || s.Hyper > 0 || s.Respawn > 0) return;
        if (s.Invuln > 0 && (int)(s.Invuln * 12) % 2 == 0) return;
        var col = s.Flash > 0 ? Color.White : s.Colour;
        // Draw on every side the ship overlaps so wrapping looks seamless.
        for (int ox = -1; ox <= 1; ox++)
            for (int oy = -1; oy <= 1; oy++)
            {
                var p = s.Pos + new Vector2(ox * 640, oy * ArenaH);
                if (p.X < -20 || p.X > 660 || p.Y < ArenaTop - 20 || p.Y > Screen.Height + 20) continue;
                if (s.Thrusting)
                {
                    var b = p + MathF2.FromAngle(s.Angle + MathF.PI, 9);
                    g.Glow(b.X, b.Y, 12 + MathF.Sin(time * 50) * 3, Pal.Orange, 0.8f);
                }
                g.Glow(p.X, p.Y, 22, col, 0.25f);
                g.Shape(shape, p, s.Angle, 1.25f, Pal.Darken(col, 0.8f));
                g.ShapeOutline(shape, p, s.Angle, 1.25f, 1.6f, col, glow: true);
                if (s.Hits > 1)
                    g.Ring(p.X, p.Y, 15, 1, col * 0.5f);
            }
    }

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        Backdrops.Space(g, time, 0, 12, r);
        float s = r.H / 70f;
        var c = r.Center;
        DrawStar(g, c, 0.7f * s, time);
        // Two ships circling and trading shots.
        float a0 = time * 0.8f;
        var p1 = c + MathF2.FromAngle(a0, 1) * new Vector2(r.W * 0.36f, r.H * 0.34f);
        var p2 = c + MathF2.FromAngle(a0 + MathF.PI, 1) * new Vector2(r.W * 0.36f, r.H * 0.34f);
        float h1 = a0 + MathF.PI / 2 + 0.3f, h2 = a0 + MathF.PI * 1.5f + 0.3f;
        var f1 = p1 + MathF2.FromAngle(h1 + MathF.PI, 8 * s);
        g.Glow(f1.X, f1.Y, 7 * s, Pal.Orange, 0.8f);
        g.Shape(PlayerShape, p1, h1, 0.9f * s, Pal.Darken(Pal.Cyan, 0.8f));
        g.ShapeOutline(PlayerShape, p1, h1, 0.9f * s, 1.2f * s, Pal.Cyan, glow: true);
        g.Shape(EnemyShape, p2, h2, 0.9f * s, Pal.Darken(Pal.Magenta, 0.8f));
        g.ShapeOutline(EnemyShape, p2, h2, 0.9f * s, 1.2f * s, Pal.Magenta, glow: true);
        for (int i = 0; i < 3; i++)
        {
            float k = (time * 1.5f + i / 3f) % 1f;
            var tp = p1 + MathF2.FromAngle(h1, (12 + k * 50) * s);
            g.Glow(tp.X, tp.Y, 5 * s, Pal.Cyan, 0.9f * (1 - k));
            g.Circle(tp.X, tp.Y, 1.2f * s, Color.White * (1 - k));
        }
    }

    public override void AutoPlay(Controls c)
    {
        var p = _player;
        if (!p.Alive || p.Respawn > 0 || p.Hyper > 0) return;
        Ship target = null;
        float best = float.MaxValue;
        foreach (var e in _enemies)
        {
            if (!e.Alive || e.Hyper > 0) continue;
            float d = Delta(p.Pos, e.Pos).Length();
            if (d < best) { best = d; target = e; }
        }
        var toStar = StarPos - p.Pos;
        float starD = toStar.Length();
        float inward = Vector2.Dot(p.Vel, toStar / MathF.Max(1, starD));
        float want;
        bool thrust = false;
        if (starD < 80 + MathF.Max(0, inward) * 0.7f)
        {
            var away = -toStar / MathF.Max(1, starD);
            var tangent = new Vector2(-away.Y, away.X);
            if (Vector2.Dot(tangent, p.Vel) < 0) tangent = -tangent;
            want = MathF2.Angle(away + tangent);
            thrust = true;
        }
        else if (target != null)
        {
            var d = Delta(p.Pos, target.Pos);
            float t = d.Length() / TorpSpeed;
            want = MathF2.Angle(d + (target.Vel - p.Vel) * t);
            float diffA = MathF.Abs(MathF2.WrapAngle(want - p.Angle));
            thrust = (d.Length() > 200 || p.Vel.Length() < 25) && diffA < 0.5f && p.Vel.Length() < 140 && p.Fuel > 20;
            foreach (var e in _enemies)
                if (e.Alive && e.Hyper <= 0 && CollisionCourse(p, e, 0.9f))
                {
                    want = MathF2.Angle(Delta(p.Pos, e.Pos)) - MathF.PI * 0.6f;
                    thrust = p.Fuel > 0;
                }
            c.FirePressed = diffA < 0.07f && d.Length() < 240 && Tick % 45 == 0;
            c.Fire = c.FirePressed;
        }
        else
            want = p.Angle;
        float diff = MathF2.WrapAngle(want - p.Angle);
        c.SetDirections(MathF.Abs(diff) < 0.04f ? 0 : MathF2.Clamp(diff * 4, -1, 1), 0);
        c.Alt = thrust;
        c.AltPressed = thrust && Tick % 30 == 0;
    }
}
