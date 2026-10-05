using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 13 Ghosts: a night in a haunted hallway. Ghosts drift out of the dark towards you; hold them in
/// your torch beam until they fade away. Catch battery orbs to keep the torch alive until dawn.
/// </summary>
public sealed class Ghosts : MiniGame, Capture.ICaptureHints
{
    public override int Number => 13;
    public override string Title => "Ghosts";
    public override Category Category => Category.Arcade;
    public override string Tagline => "Hold back the ghosts with your torch until dawn breaks.";
    public override Color Accent => new(140, 220, 255);
    public override Pad Pad => Pad.Horizontal;

    public override string[] HowToPlay =>
    [
        "Ghosts drift out of the dark. Keep the torch on one until it fades away. Survive till 6 AM.",
        "Green ghosts dodge the light and big poltergeists need a long stare.",
        "The battery drains: shine on gold orbs to recharge.",
    ];

    public override string[] DesktopControls => ["LEFT / RIGHT to swing the torch,", "or aim with the mouse."];
    public override string[] TouchControls => ["Use the stick to swing the torch,", "or touch where to shine it."];

    public int CaptureTicks => 760;

    private static readonly Vector2 Hand = new(340, 322);
    private static readonly Vector2 Vp = new(320, 128);
    private const float BeamLen = 330;
    private const int Hours = 6;

    private enum Kind { Wisp, Dodger, Banshee, Poltergeist }

    private sealed class Ghost
    {
        public Kind Kind;
        public Vector2 Pos;
        public float Hp, MaxHp, Speed, Phase, Age, Lit, DodgeTimer, DodgeCd, Fade;
        public float DodgeDir;
        public bool Dead;
        public float Size => Kind switch { Kind.Poltergeist => 36, Kind.Banshee => 17, Kind.Dodger => 18, _ => 17 };
    }

    private sealed class Orb
    {
        public Vector2 Pos, Home;
        public float Age, Lit, Phase;
        public bool Collected, Dead;
    }

    private readonly List<Ghost> _ghosts = new();
    private readonly List<Orb> _orbs = new();
    private readonly List<Vector2> _pts = new();
    private float _aim = -MathF.PI / 2, _aimTarget;
    private bool _pointerAim;
    private float _battery = 100;
    private int _hour;
    private int _toSpawn;
    private float _spawnTimer, _orbTimer, _banner, _hourGap;
    private float _lightning, _lightningTimer = 6, _hurtFlash;
    private int _combo;
    private float _comboTimer;
    private float _flicker;

    protected override void Start()
    {
        Lives = 3;
        _hour = 0;
        _battery = 100;
        NextHour();
    }

    private static string HourName(int h) => h == 0 ? "MIDNIGHT" : h + " AM";

    private void NextHour()
    {
        Level = _hour + 1;
        Status = HourName(_hour);
        _toSpawn = 10 + _hour * 4;
        _spawnTimer = 2f;
        _orbTimer = 4;
        _banner = 2.4f;
        if (_hour >= 2)
            _toSpawn += 1; // the poltergeist
    }

    private float Difficulty => 1 + _hour * 0.1f;

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        float dt = Dt;
        if (_banner > 0) _banner -= dt;
        if (_hurtFlash > 0) _hurtFlash -= dt * 2;
        if (_comboTimer > 0 && (_comboTimer -= dt) <= 0) _combo = 0;

        Aim();

        // Battery.
        _battery = MathF.Max(0, _battery - dt * (2.6f + _hour * 0.25f));
        float power = Power;
        _flicker = _battery < 15 && Chance(0.08f) ? Rand(0.3f, 0.8f) : 1;

        // Lightning.
        _lightning = MathF.Max(0, _lightning - dt * 3);
        _lightningTimer -= dt;
        if (_lightningTimer <= 0)
        {
            _lightningTimer = Rand(9, 16);
            _lightning = 1;
            Sound.Play(Sfx.Crack, -0.8f, 0.6f);
            Sound.Play(Sfx.Thud, -0.9f, 0.7f);
        }
        Sound.Loop(LoopSfx.Wind, true, -0.5f, 0.35f);

        Spawn();
        UpdateGhosts(power);
        UpdateOrbs(power);

        if (_hourGap > 0)
        {
            _hourGap -= dt;
            if (_hourGap <= 0)
            {
                _hour++;
                if (_hour >= Hours)
                {
                    AddScore(1000 * Lives, 320, 200, Pal.Yellow);
                    EndGame(true, "Dawn breaks! The ghosts melt away.");
                    return;
                }
                Sound.Play(Sfx.Bell, -0.4f);
                NextHour();
            }
        }
        else if (_toSpawn <= 0 && _ghosts.Count == 0)
        {
            _hourGap = 2.5f;
            AddScore(200 * (_hour + 1), 320, 200, Pal.Cyan);
            Sound.Play(Sfx.LevelUp);
        }
    }

    /// <summary>Beam strength 0..1: falls away as the battery runs flat (but never quite to nothing).</summary>
    private float Power => _battery <= 0 ? 0.25f : MathF.Min(1, 0.45f + _battery / 50f);

    private float HalfWidth => 0.12f + 0.12f * Power;

    private float Length => BeamLen * (0.35f + 0.65f * Power);

    private void Aim()
    {
        float axis = In.AxisX;
        if (MathF.Abs(axis) > 0.1f)
        {
            _pointerAim = false;
            _aim += axis * 2.8f * Dt;
        }
        if ((In.PointerDown || (In.HasHover && In.PointerMoved)) && In.Pointer.Y < Hand.Y - 4)
        {
            _pointerAim = true;
            _aimTarget = MathF2.Angle(In.Pointer - Hand);
        }
        if (_pointerAim && MathF.Abs(axis) <= 0.1f)
        {
            float d = MathF2.WrapAngle(_aimTarget - _aim);
            float step = 7 * Dt;
            _aim += MathF.Abs(d) < step ? d : MathF.Sign(d) * step;
        }
        _aim = MathF2.Clamp(_aim, -MathF.PI + 0.12f, -0.12f);
    }

    private void Spawn()
    {
        if (_hourGap > 0) return;
        _spawnTimer -= Dt;
        if (_toSpawn > 0 && _spawnTimer <= 0)
        {
            _toSpawn--;
            _spawnTimer = MathF.Max(0.75f, 2.1f - 0.22f * _hour) * Rand(0.7f, 1.3f);
            if (_toSpawn > 4 && Chance(0.25f + 0.08f * _hour))
                _spawnTimer = 0.25f;
            var kind = Kind.Wisp;
            bool boss = _hour >= 2 && _toSpawn == 3;
            if (boss) kind = Kind.Poltergeist;
            else if (_hour >= 1 && Chance(0.3f)) kind = Kind.Dodger;
            else if (_hour >= 3 && Chance(0.25f)) kind = Kind.Banshee;
            // From the far end, the side doors or out of the walls.
            Vector2 pos = RandInt(0, 4) switch
            {
                0 => new Vector2(Rand(-20, 30), Rand(120, 230)),
                1 => new Vector2(Rand(610, 660), Rand(120, 230)),
                2 => new Vector2(Rand(270, 370), Rand(120, 160)),
                _ => Chance(0.5f) ? new Vector2(150, 190) : new Vector2(490, 190),
            };
            var g = new Ghost { Kind = kind, Pos = pos, Phase = Rand(0, 6), DodgeDir = Chance(0.5f) ? 1 : -1 };
            switch (kind)
            {
                case Kind.Wisp: g.MaxHp = 0.8f; g.Speed = 28; break;
                case Kind.Dodger: g.MaxHp = 1.0f; g.Speed = 32; break;
                case Kind.Banshee: g.MaxHp = 0.5f; g.Speed = 58; break;
                case Kind.Poltergeist: g.MaxHp = 4f + _hour * 0.3f; g.Speed = 16; break;
            }
            g.Speed *= Difficulty;
            g.Hp = g.MaxHp;
            _ghosts.Add(g);
            if (kind == Kind.Banshee) Sound.Play(Sfx.Alarm, 0.6f, 0.3f);
            else if (kind == Kind.Poltergeist)
            {
                Sound.Play(Sfx.Warp, -0.8f);
                Fx.Shake(3, 0.5f);
            }
            else Sound.Play(Sfx.Whoosh, -0.6f, 0.3f);
        }

        _orbTimer -= Dt;
        if (_orbTimer <= 0 && _orbs.Count < 2)
        {
            _orbTimer = Rand(5, 8) + _hour * 0.4f;
            var home = new Vector2(Rand(110, 530), Rand(110, 230));
            _orbs.Add(new Orb { Pos = home + new Vector2(0, -60), Home = home, Phase = Rand(0, 6) });
        }
    }

    private bool InBeam(Vector2 p, float radius, out float centre)
    {
        var d = p - Hand;
        float dist = d.Length();
        centre = 0;
        if (dist > Length + radius || dist < 1) return false;
        float off = MathF2.WrapAngle(MathF2.Angle(d) - _aim);
        float allow = HalfWidth + MathF.Atan(radius / dist) * 0.8f;
        centre = off;
        return MathF.Abs(off) < allow;
    }

    private void UpdateGhosts(float power)
    {
        bool sizzle = false;
        for (int i = 0; i < _ghosts.Count; i++)
        {
            var g = _ghosts[i];
            g.Age += Dt;
            g.Fade = MathF.Min(1, g.Fade + Dt * 1.5f);
            var to = Hand - g.Pos;
            float dist = to.Length();
            var dir = to / MathF.Max(1, dist);
            var perp = new Vector2(-dir.Y, dir.X);
            bool lit = InBeam(g.Pos, g.Size, out float off) && _flicker > 0.5f;
            g.Lit = lit ? MathF.Min(1, g.Lit + Dt * 6) : MathF.Max(0, g.Lit - Dt * 4);

            float speed = g.Speed * (lit ? 0.35f : 1);
            if (g.Kind == Kind.Poltergeist && lit) speed = g.Speed * 0.6f;
            var vel = dir * speed + perp * MathF.Sin(g.Age * 2 + g.Phase) * 22;
            if (g.Kind == Kind.Dodger)
            {
                g.DodgeCd -= Dt;
                if (lit && g.DodgeCd <= 0)
                {
                    g.DodgeTimer = 0.45f;
                    g.DodgeCd = 1.6f / Difficulty;
                    // Away from the beam's centre line.
                    var beamPerp = MathF2.FromAngle(_aim + MathF.PI / 2);
                    g.DodgeDir = off >= 0 ? 1 : -1;
                    g.Pos += beamPerp * g.DodgeDir * 2;
                    Sound.Play(Sfx.Whoosh, 0.5f, 0.3f);
                }
                if (g.DodgeTimer > 0)
                {
                    g.DodgeTimer -= Dt;
                    vel = MathF2.FromAngle(_aim + MathF.PI / 2) * g.DodgeDir * 190 + dir * 10;
                }
            }
            if (g.Kind == Kind.Banshee)
                vel += perp * MathF.Sin(g.Age * 7 + g.Phase) * 60;
            g.Pos += vel * Dt;
            g.Pos.X = MathF2.Clamp(g.Pos.X, -30, 670);
            g.Pos.Y = MathF2.Clamp(g.Pos.Y, 60, 360);

            if (lit)
            {
                g.Hp -= Dt * power;
                sizzle = true;
                if (Tick % 3 == 0)
                    Fx.Spark(g.Pos.X + Rand(-g.Size, g.Size) * 0.7f, g.Pos.Y + Rand(-g.Size, g.Size) * 0.5f, Rand(-10, 10), Rand(-50, -20),
                        GhostColour(g.Kind), 0.5f, 1.8f);
                if (g.Kind == Kind.Poltergeist)
                    Fx.Shake(1.2f, 0.05f);
                if (g.Hp <= 0)
                {
                    Pop(g);
                    continue;
                }
            }
            else
                g.Hp = MathF.Min(g.MaxHp, g.Hp + Dt * 0.15f);

            if (dist < 26 + g.Size * 0.5f)
            {
                g.Dead = true;
                _hurtFlash = 1;
                Fx.Burst(g.Pos.X, g.Pos.Y, GhostColour(g.Kind), 30, 160, 0.7f, 3);
                Sound.Play(Sfx.Hurt);
                _combo = 0;
                LoseLife();
                if (IsOver) return;
            }
        }
        if (sizzle)
            Sound.Loop(LoopSfx.Hum, true, 0.3f, 0.35f);
        _ghosts.RemoveAll(g => g.Dead);
    }

    private void Pop(Ghost g)
    {
        g.Dead = true;
        _combo++;
        _comboTimer = 3;
        int pts = g.Kind switch { Kind.Poltergeist => 500, Kind.Banshee => 150, Kind.Dodger => 100, _ => 50 };
        pts *= Math.Min(_combo, 5);
        AddScore(pts, g.Pos.X, g.Pos.Y - g.Size - 6, _combo > 1 ? Pal.Cyan : Pal.Yellow);
        var c = GhostColour(g.Kind);
        Fx.Burst(g.Pos.X, g.Pos.Y, c, g.Kind == Kind.Poltergeist ? 60 : 24, g.Kind == Kind.Poltergeist ? 220 : 120, 0.8f, 2.5f);
        Fx.Burst(g.Pos.X, g.Pos.Y, Pal.White, 10, 60, 0.4f, 2);
        Sound.Play(g.Kind == Kind.Poltergeist ? Sfx.BigExplode : Sfx.Pop, Rand(-0.1f, 0.3f) + _combo * 0.05f, 0.8f);
        if (g.Kind == Kind.Poltergeist)
        {
            Fx.Shake(5, 0.5f);
            Fx.Float("POLTERGEIST BANISHED!", 320, 160, Pal.Magenta, 2f);
        }
    }

    private void UpdateOrbs(float power)
    {
        foreach (var o in _orbs)
        {
            o.Age += Dt;
            if (o.Collected)
            {
                var d = Hand - o.Pos;
                if (d.Length() < 14)
                {
                    o.Dead = true;
                    _battery = MathF.Min(100, _battery + 40);
                    Fx.Burst(Hand.X, Hand.Y, Pal.Gold, 20, 100, 0.5f, 2);
                    Fx.Float("BATTERY +40", Hand.X, Hand.Y - 40, Pal.Gold);
                    Sound.Play(Sfx.PowerUp);
                }
                else
                    o.Pos += Vector2.Normalize(d) * 420 * Dt;
                continue;
            }
            o.Pos = Vector2.Lerp(o.Pos, o.Home + new Vector2(MathF.Sin(o.Age * 0.9f + o.Phase) * 70, MathF.Sin(o.Age * 1.7f) * 18), 2 * Dt);
            if (InBeam(o.Pos, 10, out _))
            {
                o.Lit += Dt * power;
                if (o.Lit > 0.3f)
                {
                    o.Collected = true;
                    AddScore(20, o.Pos.X, o.Pos.Y - 12, Pal.Gold);
                    Sound.Play(Sfx.Pickup, 0.3f);
                }
            }
            if (o.Age > 11) o.Dead = true;
        }
        _orbs.RemoveAll(o => o.Dead);
    }

    private static Color GhostColour(Kind k) => k switch
    {
        Kind.Dodger => new Color(120, 255, 150),
        Kind.Banshee => new Color(255, 130, 200),
        Kind.Poltergeist => new Color(190, 110, 255),
        _ => new Color(190, 230, 255),
    };

    // ------------------------------------------------------------------ drawing

    public override void Draw(Gfx g)
    {
        float time = Time;
        float amb = 0.32f + _lightning * 0.5f;
        DrawHallway(g, time, amb, new RectF(0, 0, 640, 360));

        // The torch beam: layered additive cones.
        float len = Length * _flicker;
        float half = HalfWidth;
        float p = Power * _flicker;
        var warm = new Color(255, 236, 200);
        Beam(g, Hand, _aim, len, half, p);
        var tip = Hand + MathF2.FromAngle(_aim, len * 0.92f);
        g.Glow(tip.X, tip.Y, 60 * p + 10, warm, 0.35f * p);
        g.Glow(Hand.X, Hand.Y, 26, warm, 0.8f * p);

        foreach (var o in _orbs)
            DrawOrb(g, o, time);
        // Far ghosts first.
        for (int pass = 0; pass < 2; pass++)
            foreach (var gh in _ghosts)
                if ((gh.Pos.Y < 200) == (pass == 0))
                    DrawGhost(g, gh, time, MathF.Max(gh.Lit, _lightning * 0.8f));

        DrawPlayer(g, time);

        if (_hurtFlash > 0)
        {
            g.Rect(0, 0, 640, 360, new Color(120, 0, 30) * (0.45f * _hurtFlash));
        }
        Backdrops.Vignette(g, Screen.Bounds, 0.7f);
        DrawBattery(g, time);

        if (_combo >= 2)
            g.Text("COMBO x" + Math.Min(_combo, 5), 628, 30, 1.5f, Pal.Cyan, Align.Right);
        if (_banner > 0)
        {
            float a = MathF.Min(1, _banner);
            g.TextShadow(HourName(_hour), 320, 70, 3f, new Color(200, 230, 255) * a, Align.Center);
            g.Text(_hour == 0 ? "SURVIVE UNTIL 6 AM" : (Hours - _hour) + (Hours - _hour == 1 ? " HOUR" : " HOURS") + " UNTIL DAWN", 320, 100, 1.5f, Pal.LightGrey * a, Align.Center);
        }
        if (_hourGap > 0)
            g.TextShadow("THE CLOCK STRIKES...", 320, 160, 2f, Pal.Gold * MathF.Min(1, _hourGap), Align.Center);
    }

    /// <summary>A soft torch beam built from many faint additive wedges.</summary>
    private static void Beam(Gfx g, Vector2 o, float aim, float len, float half, float power)
    {
        var warm = new Color(255, 236, 200);
        for (int wi = 0; wi < 12; wi++)
        {
            float w = half * (1.6f - wi * 0.125f);
            for (int li = 0; li < 6; li++)
            {
                float l = len * (1 - li * 0.13f);
                g.Triangle(o, o + MathF2.FromAngle(aim - w, l), o + MathF2.FromAngle(aim + w, l), Pal.Add(warm, 0.0095f * power));
            }
        }
    }

    private void DrawHallway(Gfx g, float time, float amb, RectF r)
    {
        // Positions are given for the 640x360 view and mapped into r.
        float sx = r.W / 640f, sy = r.H / 360f;
        Vector2 P(float x, float y) => new(r.X + x * sx, r.Y + y * sy);
        Color A(Color c) => Pal.Darken(c, 1 - amb);

        var bl = P(250, 96); var br = P(390, 96); var bbl = P(250, 176); var bbr = P(390, 176);
        g.Rect(r, A(new Color(30, 20, 50)));
        // Ceiling.
        _pts.Clear(); _pts.Add(P(0, 0)); _pts.Add(P(640, 0)); _pts.Add(br); _pts.Add(bl);
        g.Polygon(_pts, A(new Color(28, 22, 46)));
        // Floor.
        _pts.Clear(); _pts.Add(bbl); _pts.Add(bbr); _pts.Add(P(640, 360)); _pts.Add(P(0, 360));
        g.Polygon(_pts, A(new Color(62, 38, 30)));
        for (int i = -6; i <= 6; i++)
            g.Line(P(320 + i * 11.6f, 176), P(320 + i * 58, 360), 1.2f * sx, A(new Color(40, 24, 18)));
        // Carpet runner.
        _pts.Clear(); _pts.Add(P(300, 176)); _pts.Add(P(340, 176)); _pts.Add(P(430, 360)); _pts.Add(P(210, 360));
        g.Polygon(_pts, A(new Color(110, 26, 40)));
        // Walls.
        _pts.Clear(); _pts.Add(P(0, 0)); _pts.Add(bl); _pts.Add(bbl); _pts.Add(P(0, 360));
        g.Polygon(_pts, A(new Color(46, 34, 70)));
        _pts.Clear(); _pts.Add(br); _pts.Add(P(640, 0)); _pts.Add(P(640, 360)); _pts.Add(bbr);
        g.Polygon(_pts, A(new Color(46, 34, 70)));
        // Wallpaper stripes converge on the far wall.
        for (int i = 1; i < 7; i++)
        {
            float k = i / 7f;
            float x = MathF2.Lerp(0, 250, k * k * 0.3f + k * 0.7f);
            float t0 = MathF2.Lerp(0, 96, x / 250), b0 = MathF2.Lerp(360, 176, x / 250);
            g.Line(P(x, t0), P(x, b0), 1 * sx, A(new Color(60, 46, 90)));
            g.Line(P(640 - x, t0), P(640 - x, b0), 1 * sx, A(new Color(60, 46, 90)));
        }
        // Dado rail.
        g.Line(P(0, 250), bbl + (P(250, 150) - bbl) * 0.6f, 2 * sx, A(new Color(90, 64, 50)));
        g.Line(P(640, 250), bbr + (P(390, 150) - bbr) * 0.6f, 2 * sx, A(new Color(90, 64, 50)));
        // Side doors.
        DoorQuad(g, P(130, 96), P(175, 112), P(175, 236), P(130, 262), A(new Color(70, 40, 26)), A(new Color(20, 12, 10)));
        DoorQuad(g, P(510, 96), P(465, 112), P(465, 236), P(510, 262), A(new Color(70, 40, 26)), A(new Color(20, 12, 10)));
        // Portraits.
        DoorQuad(g, P(40, 70), P(85, 82), P(85, 150), P(40, 156), A(new Color(150, 110, 40)), A(new Color(40, 50, 60)));
        DoorQuad(g, P(600, 70), P(555, 82), P(555, 150), P(600, 156), A(new Color(150, 110, 40)), A(new Color(60, 40, 50)));
        // Portrait eyes follow you.
        float eyeX = MathF.Sin(time * 0.5f) * 1.5f;
        g.Circle(r.X + (58 + eyeX) * sx, r.Y + 106 * sy, 1.5f * sx, A(Pal.LightGrey));
        g.Circle(r.X + (68 + eyeX) * sx, r.Y + 107 * sy, 1.5f * sx, A(Pal.LightGrey));
        // Far wall with a window and the moon.
        g.Rect(bl.X, bl.Y, br.X - bl.X, bbl.Y - bl.Y, A(new Color(36, 28, 60)));
        var win = new RectF(r.X + 290 * sx, r.Y + 108 * sy, 60 * sx, 48 * sy);
        g.GradientV(win.X, win.Y, win.W, win.H, new Color(20, 30, 70), new Color(40, 50, 100));
        g.Glow(win.X + win.W * 0.7f, win.Y + win.H * 0.3f, 30 * sx, new Color(160, 180, 255), 0.5f + _lightning);
        g.Circle(win.X + win.W * 0.7f, win.Y + win.H * 0.3f, 7 * sx, new Color(230, 235, 255));
        g.Rect(win.X + win.W / 2 - sx, win.Y, 2 * sx, win.H, new Color(20, 14, 30));
        g.Rect(win.X, win.Y + win.H / 2 - sy, win.W, 2 * sy, new Color(20, 14, 30));
        g.RectOutline(win.X, win.Y, win.W, win.H, 2 * sx, new Color(20, 14, 30));
        // Moonlight on the floor.
        _pts.Clear(); _pts.Add(P(292, 178)); _pts.Add(P(348, 178)); _pts.Add(P(370, 220)); _pts.Add(P(276, 220));
        g.Polygon(_pts, Pal.Add(new Color(120, 140, 255), 0.06f + _lightning * 0.2f));
        if (_lightning > 0)
            g.Rect(r, Pal.Add(new Color(180, 200, 255), _lightning * 0.25f));
    }

    private void DoorQuad(Gfx g, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color frame, Color fill)
    {
        _pts.Clear(); _pts.Add(a); _pts.Add(b); _pts.Add(c); _pts.Add(d);
        g.Polygon(_pts, frame);
        var m = (a + b + c + d) / 4;
        _pts.Clear();
        _pts.Add(Vector2.Lerp(a, m, 0.15f)); _pts.Add(Vector2.Lerp(b, m, 0.15f));
        _pts.Add(Vector2.Lerp(c, m, 0.15f)); _pts.Add(Vector2.Lerp(d, m, 0.15f));
        g.Polygon(_pts, fill);
    }

    private void DrawGhost(Gfx g, Ghost gh, float time, float lit)
    {
        float s = gh.Size * (0.7f + 0.5f * MathF2.Clamp((gh.Pos.Y - 110) / 220, 0, 1));
        float bob = MathF.Sin(gh.Age * 3 + gh.Phase) * 3;
        var p = gh.Pos + new Vector2(0, bob);
        var c = GhostColour(gh.Kind);
        float health = gh.Hp / gh.MaxHp;
        // In the dark only a faint shape and glowing eyes show.
        float a = (0.26f + 0.74f * lit) * (0.35f + 0.65f * health) * gh.Fade;
        DrawGhostShape(g, p, s, c, a, gh.Age + gh.Phase, gh.Kind, lit);
        if (gh.Kind == Kind.Poltergeist && lit > 0.1f)
        {
            // Health ring.
            g.Arc(p.X, p.Y, s + 8, 3, -MathF.PI / 2, -MathF.PI / 2 + MathF2.Tau * health, Pal.Magenta * lit);
        }
    }

    private static void DrawGhostShape(Gfx g, Vector2 p, float s, Color c, float a, float t, Kind kind, float lit)
    {
        g.Glow(p.X, p.Y, s * 2.4f, c, 0.25f * a + 0.15f * lit);
        var body = Pal.Lighten(c, 0.3f) * (a * 0.85f);
        g.Circle(p.X, p.Y - s * 0.2f, s, body);
        g.Rect(p.X - s, p.Y - s * 0.2f, s * 2, s * 0.9f, body);
        // Wavy hem.
        for (int i = 0; i < 4; i++)
        {
            float x = p.X - s + s * 0.25f + i * s * 0.5f;
            float y = p.Y + s * 0.7f + MathF.Sin(t * 6 + i * 1.6f) * s * 0.12f;
            g.Circle(x, y, s * 0.26f, body);
        }
        // Face.
        float eye = kind == Kind.Poltergeist ? 0.24f : 0.2f;
        var eyeCol = kind == Kind.Poltergeist ? Pal.Red : Color.Black;
        if (lit < 0.3f)
        {
            // Glowing eyes in the dark.
            g.Glow(p.X - s * 0.35f, p.Y - s * 0.3f, s * 0.6f, c, 0.7f);
            g.Glow(p.X + s * 0.35f, p.Y - s * 0.3f, s * 0.6f, c, 0.7f);
            g.Circle(p.X - s * 0.35f, p.Y - s * 0.3f, s * 0.12f, Pal.Lighten(c, 0.6f));
            g.Circle(p.X + s * 0.35f, p.Y - s * 0.3f, s * 0.12f, Pal.Lighten(c, 0.6f));
        }
        else
        {
            g.Ellipse(p.X - s * 0.35f, p.Y - s * 0.3f, s * eye, s * eye * 1.3f, eyeCol * a);
            g.Ellipse(p.X + s * 0.35f, p.Y - s * 0.3f, s * eye, s * eye * 1.3f, eyeCol * a);
            // A shocked "o" mouth when caught in the light.
            g.Ellipse(p.X, p.Y + s * 0.2f, s * 0.16f, s * 0.22f, Color.Black * (a * lit));
        }
    }

    private static void DrawOrb(Gfx g, Orb o, float time)
    {
        float pulse = MathF2.Pulse(time + o.Phase, 0.8f);
        float fade = o.Age > 9 ? (int)(o.Age * 8) % 2 : 1;
        g.Glow(o.Pos, 22 + pulse * 6, Pal.Gold, 0.55f * fade);
        g.RoundRect(o.Pos.X - 5, o.Pos.Y - 8, 10, 16, 2, Pal.Gold * fade);
        g.Rect(o.Pos.X - 2, o.Pos.Y - 10, 4, 2, Pal.LightGrey * fade);
        g.Rect(o.Pos.X - 3, o.Pos.Y - 5, 6, 3, Pal.White * (0.8f * fade));
        g.Text("+", o.Pos.X + 0.5f, o.Pos.Y - 1, 1, Color.Black * fade, Align.Center);
    }

    private void DrawPlayer(Gfx g, float time)
    {
        // A figure seen from behind, torch held out.
        var c = new Color(18, 14, 30);
        g.Ellipse(320, 362, 46, 16, Color.Black * 0.5f);
        g.RoundRect(296, 326, 48, 40, 14, c);
        g.Circle(320, 316, 13, c);
        // Hair.
        g.Ellipse(320, 312, 13, 10, new Color(60, 36, 24));
        // Torch arm.
        var dir = MathF2.FromAngle(_aim);
        var shoulder = new Vector2(332, 336);
        var hand = Hand + dir * 4;
        g.Line(shoulder, hand, 7, c);
        g.RotatedRect(hand, 14, 6, _aim, new Color(70, 70, 90));
        var lens = hand + dir * 7;
        g.Circle(lens.X, lens.Y, 3.5f, Pal.Lighten(new Color(255, 236, 200), 0.2f) * (Power * _flicker));
    }

    private void DrawBattery(Gfx g, float time)
    {
        float x = 14, y = IsTouch ? 36 : 326;
        g.RoundRect(x - 4, y - 6, 96, 30, 6, Color.Black * 0.55f);
        g.Text("TORCH", x, y - 2, 1f, Pal.LightGrey);
        g.RectOutline(x, y + 8, 64, 12, 1.5f, Pal.LightGrey);
        g.Rect(x + 64, y + 11, 3, 6, Pal.LightGrey);
        float k = _battery / 100f;
        var col = k > 0.5f ? Pal.Lime : k > 0.2f ? Pal.Gold : ((int)(time * 4) % 2 == 0 ? Pal.Red : Pal.Darken(Pal.Red, 0.5f));
        if (k > 0) g.Rect(x + 2, y + 10, 60 * k, 8, col);
        if (_battery <= 0)
            g.Text("FLAT!", x + 72, y - 2, 1f, Pal.Red);
    }

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        DrawHallway(g, time, 0.4f, r);
        float s = r.H / 70f;
        var hand = new Vector2(r.CenterX, r.Bottom - 4 * s);
        float aim = -MathF.PI / 2 + MathF.Sin(time * 0.9f) * 0.6f;
        var warm = new Color(255, 236, 200);
        Beam(g, hand, aim, r.H * 1.1f, 0.22f, 1.2f);
        g.Glow(hand.X, hand.Y, 10 * s, warm, 0.8f);
        // Three ghosts; the one in the beam is revealed.
        for (int i = 0; i < 3; i++)
        {
            float gx = r.CenterX + (i - 1) * r.W * 0.3f + MathF.Sin(time * 0.7f + i * 2) * 6 * s;
            float gy = r.Y + r.H * 0.42f + MathF.Sin(time * 1.3f + i) * 4 * s;
            var p = new Vector2(gx, gy);
            float off = MathF.Abs(MathF2.WrapAngle(MathF2.Angle(p - hand) - aim));
            float lit = off < 0.3f ? 1 : 0;
            var kind = i == 0 ? Kind.Dodger : i == 2 ? Kind.Banshee : Kind.Wisp;
            DrawGhostShape(g, p, 9 * s, GhostColour(kind), 0.25f + 0.75f * lit, time + i, kind, lit);
        }
        Backdrops.Vignette(g, r, 0.6f);
    }

    public override void AutoPlay(Controls c)
    {
        // Shine on the most urgent ghost; fetch batteries when nothing is close.
        Vector2? target = null;
        float best = float.MaxValue;
        foreach (var gh in _ghosts)
        {
            float d = Vector2.Distance(gh.Pos, Hand) / MathF.Max(10, gh.Speed) - (gh.Lit > 0.5f ? 1.5f : 0);
            if (d < best) { best = d; target = gh.Pos; }
        }
        if (_battery < 60 && (best > 3 || target == null))
            foreach (var o in _orbs)
                if (!o.Collected) { target = o.Pos; break; }
        if (target == null) return;
        float want = MathF2.Angle(target.Value - Hand);
        float diff = MathF2.WrapAngle(want - _aim);
        c.SetDirections(MathF.Abs(diff) < 0.03f ? 0 : MathF2.Clamp(diff * 4, -1, 1), 0);
    }
}
