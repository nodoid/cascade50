using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 06 Derby Dash: a day at the races. Study the odds and form, place a bet on one of six horses,
/// then watch the race unfold. Eight races; finish with as much money as you can.
/// </summary>
public sealed class DerbyDash : MiniGame, Capture.ICaptureHints
{
    public override int Number => 6;
    public override string Title => "Derby Dash";
    public override Category Category => Category.Brain;
    public override string Tagline => "A day at the races: study the form, back a winner, beat the bookie.";
    public override Color Accent => Pal.Gold;

    public override string[] HowToPlay =>
    [
        "Start with 100 pounds. Pick a horse, set your stake and press RACE.",
        "Favourites (2/1) win often but pay little; long shots (20/1) pay big. Low form numbers mean recent wins.",
        "Score: your money after 8 races.",
    ];

    public override string[] DesktopControls => ["Click a horse or press 1-6.", "+ / - stake, ENTER to race."];
    public override string[] TouchControls => ["Tap a horse, use + / - for the stake, then RACE."];
    public override Pad Pad => Pad.None;
    public int CaptureTicks => 760;

    private const int Races = 8, Field = 6;
    private const float TrackLen = 3000;

    private static readonly string[] Names =
    [
        "Oric Flyer", "Tape Loader", "Cascade Lad", "Peek And Poke", "Byte Size", "Lucky Cassette", "Glitch Gallop",
        "Sprite Delight", "Pixel Prince", "Turbo Tilly", "Hackney Hero", "Mild Abandon", "Dobbin Deluxe", "Nag's Head",
        "Sir Trotalot", "Bolt From Blue", "Muddy Hooves", "Rainy Tuesday", "Chip Shop", "Teatime Treat", "Basic Error",
        "Syntax Sally", "Hay Presto", "Neigh Sayer",
    ];

    private static readonly string[] RaceNames =
    [
        "THE ORIC CUP", "SELLING PLATE", "CASSETTE STAKES", "BASIC HANDICAP", "MICRO MILE", "TAPE DERBY", "ROM GUINEAS",
        "GRAND FINAL",
    ];

    // Fractional odds: numerator / denominator.
    private static readonly int[,] OddsTable =
    {
        { 1, 1 }, { 6, 4 }, { 2, 1 }, { 5, 2 }, { 3, 1 }, { 7, 2 }, { 4, 1 }, { 5, 1 }, { 6, 1 }, { 8, 1 }, { 10, 1 },
        { 12, 1 }, { 16, 1 }, { 20, 1 }, { 25, 1 }, { 33, 1 },
    };

    private static readonly int[] Stakes = [1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000];

    private static readonly Color[] SilkColours =
    [
        Pal.Red, Pal.Blue, Pal.Yellow, Pal.Green, Pal.Magenta, Pal.Cyan, Pal.Orange, Pal.White, Pal.Purple, Pal.Pink,
        Pal.Lime, Pal.Navy,
    ];

    private static readonly Color[] Coats = [new(120, 70, 35), new(80, 45, 25), new(160, 100, 50), new(40, 30, 30), new(190, 180, 170), new(140, 60, 30)];

    private static readonly float[] CrowdX = new float[160];
    private static readonly int[] CrowdRow = new int[160];

    static DerbyDash()
    {
        var rng = new Random(3);
        for (int i = 0; i < CrowdX.Length; i++)
        {
            CrowdX[i] = rng.Next(0, 900);
            CrowdRow[i] = rng.Next(0, 4);
        }
    }

    private sealed class Horse
    {
        public string Name;
        public int OddsN, OddsD;
        public float P;
        public string Form;
        public Color Silk1, Silk2, Coat;
        public float FinishTime, K, Wob, WobPhase;
        public float Pos;
        public int Place;
        public bool Finished;
    }

    private enum Phase
    {
        Bet,
        Race,
        Photo,
        Result,
    }

    private readonly Horse[] _horses = new Horse[Field];
    private readonly int[] _order = new int[Field];
    private Phase _phase;
    private int _race;
    private int _money;
    private int _pick = -1;
    private int _stakeIndex = 3;
    private float _t, _cam, _phaseT;
    private string _commentary = "";
    private float _commentaryT;
    private int _commentStage;
    private bool _photo;
    private int _lastWin;
    private int _gallopTick;

    // Autoplay state.
    private int _autoClick;
    private Vector2 _autoPos;

    protected override void Start()
    {
        _money = 100;
        Score = _money;
        _race = 0;
        NextRace();
    }

    private void NextRace()
    {
        _race++;
        Level = _race;
        Status = $"RACE {_race} OF {Races}";
        _phase = Phase.Bet;
        _phaseT = 0;
        _pick = -1;
        _photo = false;

        // Six different names.
        var used = new bool[Names.Length];
        var silkUsed = new bool[SilkColours.Length];
        float total = 0;
        var strength = new float[Field];
        for (int i = 0; i < Field; i++)
        {
            strength[i] = MathF.Pow(Rand(0.25f, 1f), 2.2f);
            total += strength[i];
        }
        for (int i = 0; i < Field; i++)
        {
            int n;
            do n = RandInt(0, Names.Length); while (used[n]);
            used[n] = true;
            int s1;
            do s1 = RandInt(0, SilkColours.Length); while (silkUsed[s1]);
            silkUsed[s1] = true;
            int s2 = (s1 + RandInt(1, SilkColours.Length)) % SilkColours.Length;
            var h = new Horse
            {
                Name = Names[n], P = strength[i] / total, Silk1 = SilkColours[s1], Silk2 = SilkColours[s2],
                Coat = Coats[RandInt(0, Coats.Length)],
            };
            // Bookmaker's odds (with a margin), rounded to the usual prices.
            float dec = 0.82f / h.P - 1;
            int best = 0;
            for (int k = 0; k < OddsTable.GetLength(0); k++)
                if (MathF.Abs(OddsTable[k, 0] / (float)OddsTable[k, 1] - dec) < MathF.Abs(OddsTable[best, 0] / (float)OddsTable[best, 1] - dec))
                    best = k;
            h.OddsN = OddsTable[best, 0];
            h.OddsD = OddsTable[best, 1];
            h.Form = MakeForm(h.P);
            _horses[i] = h;
        }
        _stakeIndex = Math.Min(_stakeIndex, MaxStakeIndex());
        if (Stakes[_stakeIndex] < _money / 10 && _stakeIndex < MaxStakeIndex())
            _stakeIndex++;
    }

    private string MakeForm(float p)
    {
        var chars = new char[4];
        for (int i = 0; i < 4; i++)
        {
            float r = Rand(0, 1);
            int place = 1 + (int)(r * r * 9 * (1.2f - MathF.Min(1, p * 3)));
            chars[i] = place > 9 ? '0' : (char)('0' + Math.Clamp(place, 1, 9));
        }
        return new string(chars);
    }

    private int MaxStakeIndex()
    {
        int idx = 0;
        for (int i = 0; i < Stakes.Length; i++)
            if (Stakes[i] <= _money)
                idx = i;
        return idx;
    }

    private int Stake => Math.Min(Stakes[_stakeIndex], _money);

    private string OddsText(Horse h) => h.OddsN == h.OddsD ? "EVENS" : $"{h.OddsN}/{h.OddsD}";

    // ------------------------------------------------------------------ update

    private static RectF RowRect(int i) => new(14, 48 + i * 46, 404, 42);
    private static readonly RectF MinusRect = new(432, 214, 50, 36);
    private static readonly RectF PlusRect = new(574, 214, 50, 36);
    private static readonly RectF GoRect = new(432, 272, 192, 52);

    protected override void Update()
    {
        _phaseT += Dt;
        if (_commentaryT > 0)
            _commentaryT -= Dt;
        switch (_phase)
        {
            case Phase.Bet:
                UpdateBet();
                break;
            case Phase.Race:
                UpdateRace();
                break;
            case Phase.Photo:
                if (_phaseT > 1.8f)
                    ShowResult();
                break;
            case Phase.Result:
                if (Ui.Button(GoRect, _race >= Races || _money <= 0 ? "FINISH" : "NEXT RACE", Keys.Enter, color: Pal.Forest))
                {
                    if (_money <= 0)
                        EndGame(false, "Broke! The bookie wins.");
                    else if (_race >= Races)
                        EndGame(_money > 100, $"You finished with {_money} pounds");
                    else
                        NextRace();
                }
                break;
        }
    }

    private void UpdateBet()
    {
        if (In.PointerPressed)
            for (int i = 0; i < Field; i++)
                if (RowRect(i).Contains(In.Pointer))
                    Select(i);
        foreach (char ch in In.Typed)
            if (ch >= '1' && ch <= '6')
                Select(ch - '1');

        int max = MaxStakeIndex();
        if (Ui.Button(MinusRect, "-", Keys.OemMinus, _stakeIndex > 0) || (In.KeyPressed(Keys.Subtract) && _stakeIndex > 0))
            _stakeIndex--;
        if (Ui.Button(PlusRect, "+", Keys.OemPlus, _stakeIndex < max) || (In.KeyPressed(Keys.Add) && _stakeIndex < max))
            _stakeIndex++;
        _stakeIndex = Math.Clamp(_stakeIndex, 0, max);

        if (Ui.Button(GoRect, "RACE!", Keys.Enter, _pick >= 0, Pal.Forest, textScale: 2f))
            StartRace();
    }

    private void Select(int i)
    {
        if (_pick != i)
            Sound.Play(Sfx.Select, i * 0.08f);
        _pick = i;
    }

    private void StartRace()
    {
        _money -= Stake;
        _lastWin = -Stake;
        _betStake = Stake;
        Score = _money;
        Sound.Play(Sfx.Bell);

        // Decide the finishing order from the true chances, then plan each horse's run so it happens.
        var taken = new bool[Field];
        for (int place = 0; place < Field; place++)
        {
            float total = 0;
            for (int i = 0; i < Field; i++)
                if (!taken[i])
                    total += _horses[i].P;
            float r = Rand(0, total);
            int pickI = -1;
            for (int i = 0; i < Field; i++)
            {
                if (taken[i])
                    continue;
                pickI = i;
                r -= _horses[i].P;
                if (r <= 0)
                    break;
            }
            taken[pickI] = true;
            _order[place] = pickI;
        }
        float t = Rand(12.5f, 13.5f);
        _photo = Chance(0.3f);
        for (int place = 0; place < Field; place++)
        {
            var h = _horses[_order[place]];
            h.FinishTime = t;
            h.Place = place + 1;
            h.Pos = 0;
            h.Finished = false;
            h.K = Rand(-0.45f, 0.45f);
            h.Wob = Rand(0.02f, 0.05f);
            h.WobPhase = Rand(0, 6);
            t += place == 0 && _photo ? Rand(0.01f, 0.04f) : Rand(0.08f, 0.45f);
        }
        _t = 0;
        _cam = -80;
        _commentStage = 0;
        Say("AND THEY'RE OFF!");
        _phase = Phase.Race;
        _phaseT = 0;
    }

    private int _betStake;

    private float PositionAt(Horse h, float t)
    {
        float u = t / h.FinishTime;
        if (u >= 1)
            return TrackLen + (t - h.FinishTime) * TrackLen / h.FinishTime * MathF.Max(0.2f, 1 - (t - h.FinishTime) * 0.4f);
        float f = u + h.K * u * (1 - u) + h.Wob * MathF.Sin(u * MathF.PI * 3 + h.WobPhase) * u * (1 - u);
        return f * TrackLen;
    }

    private void Say(string text)
    {
        _commentary = text;
        _commentaryT = 3f;
    }

    private void UpdateRace()
    {
        _t += Dt;
        int leader = 0, second = 0;
        float lead = float.MinValue, sec = float.MinValue;
        bool allDone = true;
        for (int i = 0; i < Field; i++)
        {
            var h = _horses[i];
            h.Pos = PositionAt(h, _t);
            if (!h.Finished && _t >= h.FinishTime)
            {
                h.Finished = true;
                if (h.Place == 1)
                {
                    Sound.Play(_photo ? Sfx.Click : Sfx.Bell, 0.3f);
                    Fx.Shake(1.5f, 0.2f);
                }
            }
            allDone &= h.Finished;
            if (h.Pos > lead)
            {
                sec = lead;
                second = leader;
                lead = h.Pos;
                leader = i;
            }
            else if (h.Pos > sec)
            {
                sec = h.Pos;
                second = i;
            }
        }

        float camTarget = MathF.Min(lead, TrackLen + 60) - 430;
        _cam = MathF2.Lerp(_cam, camTarget, 4 * Dt);

        // Hooves and the crowd.
        if (++_gallopTick % 5 == 0)
            Sound.Play(Sfx.Step, Rand(-0.6f, -0.2f), 0.35f);
        float excite = MathF.Min(1, lead / TrackLen);
        Sound.Loop(LoopSfx.Wind, true, -0.4f + excite * 0.6f, 0.15f + excite * 0.35f);

        // Commentary.
        float prog = lead / TrackLen;
        if (_commentStage == 0 && prog > 0.2f)
        {
            Say($"{_horses[leader].Name.ToUpperInvariant()} TAKES THE EARLY LEAD");
            _commentStage++;
        }
        else if (_commentStage == 1 && prog > 0.5f)
        {
            Say($"{_horses[leader].Name.ToUpperInvariant()} LEADS FROM {_horses[second].Name.ToUpperInvariant()}");
            _commentStage++;
        }
        else if (_commentStage == 2 && prog > 0.78f)
        {
            var winner = _horses[_order[0]];
            Say(_horses[leader] == winner && _horses[_order[1]].K < 0
                ? $"{_horses[_order[1]].Name.ToUpperInvariant()} IS FLYING LATE!"
                : $"HERE COMES {winner.Name.ToUpperInvariant()}!");
            _commentStage++;
        }
        else if (_commentStage == 3 && _horses[_order[0]].Finished)
        {
            Say(_photo ? "IT'S TOO CLOSE TO CALL!" : $"{_horses[_order[0]].Name.ToUpperInvariant()} WINS IT!");
            _commentStage++;
            if (_photo)
            {
                _phase = Phase.Photo;
                _phaseT = 0;
                Sound.Play(Sfx.Click, 0.6f);
                return;
            }
        }

        if (allDone && _t > _horses[_order[Field - 1]].FinishTime + 0.8f)
            ShowResult();
    }

    private void ShowResult()
    {
        _phase = Phase.Result;
        _phaseT = 0;
        if (_pick >= 0 && _horses[_pick].Place == 1)
        {
            var h = _horses[_pick];
            int win = _betStake + (int)MathF.Floor(_betStake * h.OddsN / (float)h.OddsD);
            _money += win;
            _lastWin = win - _betStake;
            Score = _money;
            Sound.Play(Sfx.Win);
            Fx.Burst(320, 200, Pal.Gold, 40, 160, 1f, 2.5f, 120);
            Fx.Float("WINNER!", 320, 180, Pal.Gold, 2f);
        }
        else
        {
            Sound.Play(Sfx.Lose);
        }
        Say(_photo ? $"PHOTO: {_horses[_order[0]].Name.ToUpperInvariant()} BY A NOSE!" : "RESULT CONFIRMED");
    }

    // ------------------------------------------------------------------ drawing

    public override void Draw(Gfx g)
    {
        if (_phase == Phase.Bet)
            DrawBetting(g);
        else
        {
            DrawRace(g);
            if (_phase == Phase.Result)
                DrawResult(g);
        }
    }

    private void DrawBetting(Gfx g)
    {
        var v = g.Visible;
        g.GradientV(v.X, v.Y, v.W, v.H, new Color(16, 48, 30), new Color(8, 22, 16));
        Backdrops.Grid(g, v, 32, new Color(255, 255, 255) * 0.025f);
        g.TextShadow($"RACE {_race}: {RaceNames[(_race - 1) % RaceNames.Length]}", 16, 30, 1.5f, Pal.Gold);
        g.Text("ODDS", 410, 33, 1f, Pal.LightGrey, Align.Right);

        for (int i = 0; i < Field; i++)
        {
            var h = _horses[i];
            var r = RowRect(i);
            bool sel = _pick == i;
            bool hover = In != null && In.HasHover && !IsTouch && r.Contains(In.Pointer);
            if (sel)
                g.Glow(r.CenterX, r.CenterY, 220, Pal.Gold, 0.12f);
            g.Panel(r, sel ? new Color(60, 70, 30) : hover ? new Color(34, 60, 46) : new Color(22, 40, 32), sel ? Pal.Gold : new Color(60, 100, 80), 7);
            DrawSilks(g, r.X + 24, r.CenterY, 1f, h.Silk1, h.Silk2, i + 1);
            g.Text(h.Name.ToUpperInvariant(), r.X + 48, r.Y + 8, 1.5f, Pal.White);
            g.Text("FORM " + h.Form, r.X + 48, r.Y + 26, 1f, Pal.LightGrey);
            g.Text(OddsText(h), r.Right - 10, r.Y + 13, 2f, sel ? Pal.Gold : Pal.Yellow, Align.Right);
        }

        // Bet slip.
        var slip = new RectF(426, 48, 204, 284);
        g.Panel(slip, new Color(240, 232, 205), Pal.Gold, 8);
        g.Rect(slip.X + 6, slip.Y + 30, slip.W - 12, 1, new Color(160, 140, 100));
        g.Text("BET SLIP", slip.CenterX, slip.Y + 10, 1.5f, new Color(90, 50, 30), Align.Center);
        var ink = new Color(40, 30, 30);
        if (_pick >= 0)
        {
            var h = _horses[_pick];
            DrawSilks(g, slip.X + 26, slip.Y + 56, 1.1f, h.Silk1, h.Silk2, _pick + 1);
            g.TextFit(h.Name.ToUpperInvariant(), slip.X + 50, slip.Y + 44, slip.W - 58, 1.5f, ink);
            g.Text("AT " + OddsText(h), slip.X + 50, slip.Y + 60, 1.5f, new Color(150, 40, 30));
            int ret = Stake + (int)MathF.Floor(Stake * h.OddsN / (float)h.OddsD);
            g.Text("RETURNS", slip.X + 12, slip.Y + 132, 1f, new Color(110, 90, 70));
            DrawMoney(g, "", ret, slip.Right - 12, slip.Y + 128, 2f, new Color(20, 110, 40), Align.Right);
        }
        else
        {
            g.TextWrapped("PICK A HORSE", slip.X + 12, slip.Y + 50, slip.W - 24, 1.5f, new Color(130, 110, 90), 1.35f, Align.Center);
        }
        g.Text("PURSE", slip.X + 12, slip.Y + 92, 1f, new Color(110, 90, 70));
        DrawMoney(g, "", _money, slip.Right - 12, slip.Y + 88, 2f, ink, Align.Right);
        g.Text("STAKE", slip.CenterX, slip.Y + 152, 1f, new Color(110, 90, 70), Align.Center);
        DrawMoney(g, "", Stake, slip.CenterX, slip.Y + 176, 2f, ink, Align.Center);
    }

    private static readonly PixelArt Pound = new(["..XX..", ".X..X.", ".X....", "XXXX..", ".X....", ".X....", "XXXXXX"],
        new System.Collections.Generic.Dictionary<char, Color> { ['X'] = Color.White });

    /// <summary>Text with a pound sign (the Oric font has none), e.g. "WON £25".</summary>
    private static void DrawMoney(Gfx g, string prefix, int amount, float x, float y, float scale, Color c, Align align)
    {
        string num = amount.ToString();
        float w = Gfx.TextWidth(prefix, scale) + Gfx.TextWidth(num, scale) + Gfx.GlyphW * scale;
        if (align == Align.Center)
            x -= w / 2;
        else if (align == Align.Right)
            x -= w;
        x += g.Text(prefix, x, y, scale, c);
        g.Pixels(Pound, x, y, scale, false, c);
        g.Text(num, x + Gfx.GlyphW * scale, y, scale, c);
    }

    private static void DrawSilks(Gfx g, float x, float y, float s, Color a, Color b, int number)
    {
        g.Circle(x, y, 15 * s, Pal.Black * 0.4f);
        g.Pie(x, y, 14 * s, MathF.PI, MathF.PI * 2, a);
        g.Pie(x, y, 14 * s, 0, MathF.PI, b);
        g.Rect(x - 14 * s, y - 2 * s, 28 * s, 4 * s, a);
        g.Ring(x, y, 14 * s, 1.5f * s, Pal.White * 0.7f);
        g.Circle(x, y, 7 * s, Pal.White);
        g.Text(number.ToString(), x + 0.5f * s, y - 4 * s, 1f * s, Pal.Black, Align.Center);
    }

    private void DrawRace(Gfx g)
    {
        var v = g.Visible;
        float cam = _cam;
        // Sky and distant hills.
        g.GradientV(v.X, v.Y, v.W, 150, new Color(90, 160, 240), new Color(200, 230, 250));
        for (int i = 0; i < 5; i++)
        {
            float cx = Backdrops.Mod(i * 170 - cam * 0.05f - Time * 6, v.W + 160) + v.X - 80;
            float cy = 56 + (i % 3) * 14;
            g.Ellipse(cx, cy, 34, 7, Color.White * 0.8f);
            g.Ellipse(cx - 10, cy - 5, 16, 8, Color.White * 0.8f);
            g.Ellipse(cx + 12, cy - 4, 18, 7, Color.White * 0.8f);
        }
        Backdrops.Hills(g, 120, 12, cam * 0.1f, new Color(110, 160, 110), 5, new RectF(v.X, v.Y, v.W, 150));
        // Grandstand with crowd.
        g.Rect(v.X, 118, v.W, 40, new Color(70, 60, 80));
        for (float x = -Backdrops.Mod(cam * 0.5f, 18); x < v.Right; x += 18)
        {
            g.Rect(x, 112, 16, 6, new Color(180, 40, 40));
            g.Rect(x + 8, 112, 8, 6, Pal.White);
        }
        for (int i = 0; i < CrowdX.Length; i++)
        {
            float cx = Backdrops.Mod(CrowdX[i] - cam * 0.5f, v.W + 40) + v.X - 20;
            float cy = 124 + CrowdRow[i] * 8;
            float hop = _phase == Phase.Race && _t > 9 ? MathF.Abs(MathF.Sin(Time * 9 + i)) * 2 : 0;
            g.Circle(cx, cy - hop, 3, SilkColours[i % SilkColours.Length] * 0.8f);
            g.Circle(cx, cy - 4 - hop, 2, Pal.Skin * 0.9f);
        }
        // Turf with mowing stripes.
        g.Rect(v.X, 158, v.W, v.Bottom - 158, new Color(60, 150, 60));
        for (float x = -Backdrops.Mod(cam, 80); x < v.Right; x += 80)
            g.Rect(x, 172, 40, 168, new Color(70, 165, 68));
        // Rails.
        DrawRail(g, cam, 170, v);
        // Finish post.
        float fx = TrackLen - cam + 60;
        if (fx > -40 && fx < v.Right + 40)
        {
            g.Rect(fx - 2, 140, 4, 200, Pal.White);
            g.Rect(fx - 2, 140, 4, 200, Pal.White);
            for (int i = 0; i < 10; i++)
                g.Rect(fx - 2, 172 + i * 16, 4, 8, Pal.Red);
            g.Circle(fx, 140, 7, Pal.Red);
            g.Circle(fx, 140, 4, Pal.White);
        }
        // Distance markers.
        for (int m = 1; m < 6; m++)
        {
            float mx = m * 500 - cam + 60;
            if (mx > -30 && mx < v.Right + 30)
            {
                g.Rect(mx - 1, 156, 2, 16, Pal.White);
                g.RoundRect(mx - 14, 144, 28, 14, 3, Pal.White);
                g.Text((6 - m) + "F", mx, 147, 1f, Pal.Black, Align.Center);
            }
        }

        // Horses, far lane first.
        for (int i = 0; i < Field; i++)
        {
            var h = _horses[i];
            float x = h.Pos - cam + 60;
            float y = 200 + i * 24;
            float phase = h.Pos / 42f * MathF2.Tau;
            bool moving = _phase == Phase.Race || h.Pos < TrackLen + 200;
            if (x > -60 && x < v.Right + 60)
            {
                g.Ellipse(x, y + 1, 20, 3, Color.Black * 0.25f);
                DrawHorse(g, x, y, 1f, moving ? phase : 0, h.Coat, h.Silk1, h.Silk2, i + 1);
                if (i == _pick)
                {
                    float bob = MathF.Sin(Time * 6) * 2;
                    g.Triangle(new Vector2(x - 5, y - 56 + bob), new Vector2(x + 5, y - 56 + bob), new Vector2(x, y - 49 + bob), Pal.Gold);
                    g.TextShadow("YOU", x, y - 66 + bob, 1f, Pal.Gold, Align.Center);
                }
            }
            if (Tick % 3 == 0 && _phase == Phase.Race && x > 0 && x < v.Right)
                Fx.Spark(x - 12, y, Rand(-60, -20), Rand(-30, -5), new Color(120, 90, 50), 0.4f, 1.5f, 60, false);
        }
        DrawRail(g, cam, 336, v);

        // Progress strip.
        var strip = new RectF(120, 28, 400, 8);
        g.RoundRect(strip.Inflate(2, 2), 4, Color.Black * 0.5f);
        g.Rect(strip.Right - 2, strip.Y - 2, 2, 12, Pal.White);
        for (int i = 0; i < Field; i++)
        {
            var h = _horses[i];
            float px = strip.X + strip.W * MathF.Min(1, h.Pos / TrackLen);
            g.Circle(px, strip.CenterY, i == _pick ? 5 : 3.5f, h.Silk1);
            if (i == _pick)
                g.Ring(px, strip.CenterY, 6, 1.5f, Pal.Gold);
        }

        // Commentary.
        if (_commentaryT > 0 || _phase != Phase.Race)
        {
            var bar = new RectF(60, 42, 520, 22);
            g.Panel(bar, Pal.Panel * 0.9f, Pal.Gold, 6);
            g.TextFit(_commentary, bar.CenterX, bar.Y + 7, bar.W - 16, 1.25f, Pal.White, Align.Center);
        }

        if (_phase == Phase.Photo)
        {
            float flash = MathF.Max(0, 1 - _phaseT * 4);
            g.Rect(v.X, v.Y, v.W, v.H, Pal.White * (0.8f * flash));
            g.Rect(v.X, v.Y, v.W, v.H, new Color(60, 40, 20) * 0.35f);
            g.TextShadow("PHOTO FINISH", 320, 80, 3f, Pal.White, Align.Center);
        }
    }

    private static void DrawRail(Gfx g, float cam, float y, RectF v)
    {
        g.Rect(v.X, y - 10, v.W, 3, Pal.White);
        for (float x = -Backdrops.Mod(cam, 40) + v.X; x < v.Right; x += 40)
            g.Rect(x, y - 10, 3, 12, Pal.LightGrey);
    }

    private void DrawResult(Gfx g)
    {
        var p = new RectF(20, 74, 396, 254);
        g.Panel(p, Pal.Panel * 0.95f, Pal.Gold, 10);
        g.TextShadow("RESULT", p.X + 16, p.Y + 12, 2f, Pal.Gold);
        bool won = _pick >= 0 && _horses[_pick].Place == 1;
        DrawMoney(g, won ? "WON " : "LOST ", won ? _lastWin + _betStake : _betStake, p.Right - 16, p.Y + 16, 1.5f, won ? Pal.Lime : Pal.Red, Align.Right);
        for (int place = 0; place < Field; place++)
        {
            int i = _order[place];
            var h = _horses[i];
            float y = p.Y + 44 + place * 30;
            if (i == _pick)
                g.RoundRect(p.X + 8, y - 4, p.W - 16, 26, 5, Pal.Gold * 0.25f);
            g.Text((place + 1) + (place == 0 ? "ST" : place == 1 ? "ND" : place == 2 ? "RD" : "TH"), p.X + 16, y + 3, 1.5f,
                place == 0 ? Pal.Gold : Pal.LightGrey);
            DrawSilks(g, p.X + 76, y + 9, 0.7f, h.Silk1, h.Silk2, i + 1);
            g.Text(h.Name.ToUpperInvariant(), p.X + 96, y + 3, 1.5f, Pal.White);
            g.Text(OddsText(h), p.Right - 16, y + 3, 1.5f, Pal.Yellow, Align.Right);
        }
        DrawMoney(g, "PURSE ", _money, p.X + 16, p.Bottom - 8 - 12, 1.5f, Pal.White, Align.Left);
    }

    private static void DrawHorse(Gfx g, float x, float y, float s, float phase, Color coat, Color silk1, Color silk2, int number)
    {
        var dark = Pal.Darken(coat, 0.35f);
        float bob = MathF.Sin(phase * 2) * 1.5f * s;
        float by = y - 22 * s + bob;

        // Far legs (darker), then body, then near legs.
        DrawLeg(g, x + 10 * s, by + 4 * s, phase + 0.6f, s, dark, true);
        DrawLeg(g, x - 10 * s, by + 4 * s, phase + 3.4f, s, dark, false);
        // Tail.
        float tw = MathF.Sin(phase) * 3 * s;
        g.Line(x - 15 * s, by - 3 * s, x - 25 * s, by + 4 * s + tw, 3.5f * s, dark);
        g.Line(x - 22 * s, by + 2 * s + tw, x - 27 * s, by + 9 * s + tw, 2.5f * s, dark);
        // Body.
        g.Ellipse(x, by, 17 * s, 7.5f * s, coat);
        g.Ellipse(x - 2 * s, by - 3 * s, 12 * s, 3 * s, Pal.Lighten(coat, 0.15f));
        // Neck and head.
        float nod = MathF.Sin(phase * 2 + 1) * 1.5f * s;
        g.Line(new Vector2(x + 11 * s, by - 1 * s), new Vector2(x + 20 * s, by - 12 * s + nod), 8 * s, coat);
        g.RotatedRect(new Vector2(x + 24 * s, by - 11 * s + nod), 12 * s, 5.5f * s, 0.5f, coat);
        g.Circle(x + 29 * s, by - 8 * s + nod, 2.6f * s, coat);
        g.Triangle(new Vector2(x + 19 * s, by - 15 * s + nod), new Vector2(x + 22 * s, by - 15 * s + nod), new Vector2(x + 20 * s, by - 19 * s + nod), dark);
        g.Line(new Vector2(x + 12 * s, by - 5 * s), new Vector2(x + 19 * s, by - 14 * s + nod), 2.5f * s, dark);
        g.Circle(x + 23 * s, by - 13 * s + nod, 1 * s, Pal.Black);
        // Saddle cloth with number.
        g.Rect(x - 6 * s, by - 6 * s, 11 * s, 9 * s, Pal.White);
        g.Text(number.ToString(), x - 0.5f * s, by - 4.5f * s, 0.8f * s, Pal.Black, Align.Center);
        DrawLeg(g, x + 12 * s, by + 4 * s, phase, s, coat, true);
        DrawLeg(g, x - 8 * s, by + 4 * s, phase + 2.8f, s, coat, false);

        // Jockey, crouched.
        float jy = by - 10 * s;
        g.Ellipse(x + 3 * s, jy, 7 * s, 4 * s, silk1);
        g.Rect(x - 1 * s, jy - 1 * s, 8 * s, 2 * s, silk2);
        g.Line(x + 6 * s, jy + 1 * s, x + 14 * s, jy + 5 * s, 2 * s, silk2);
        g.Line(x + 1 * s, jy + 3 * s, x + 5 * s, by - 2 * s, 2.5f * s, Pal.White);
        g.Circle(x + 10 * s, jy - 4 * s, 3 * s, Pal.Skin);
        g.Pie(x + 10 * s, jy - 4.5f * s, 3.4f * s, MathF.PI, MathF.PI * 2, silk2);
        g.Rect(x + 10 * s, jy - 5 * s, 4 * s, 1.2f * s, silk2);
    }

    private static void DrawLeg(Gfx g, float hx, float hy, float phase, float s, Color c, bool front)
    {
        float swing = MathF.Sin(phase) * 0.75f;
        float bend = front ? MathF.Max(0, MathF.Cos(phase)) * 1.2f : -MathF.Max(0, -MathF.Cos(phase)) * 1.1f;
        var hip = new Vector2(hx, hy);
        var knee = hip + MathF2.FromAngle(MathF.PI / 2 - swing, 8 * s);
        var hoof = knee + MathF2.FromAngle(MathF.PI / 2 - swing + bend, 8 * s);
        g.Line(hip, knee, 3.2f * s, c);
        g.Line(knee, hoof, 2.4f * s, c);
        g.Circle(hoof.X, hoof.Y, 1.6f * s, Pal.Darken(c, 0.5f));
    }

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        float s = r.H / 70f;
        g.GradientV(r.X, r.Y, r.W, r.H * 0.45f, new Color(90, 160, 240), new Color(200, 230, 250));
        g.Rect(r.X, r.Y + r.H * 0.38f, r.W, r.H * 0.1f, new Color(70, 60, 80));
        for (int i = 0; i < 30; i++)
            g.Circle(r.X + (i * 23 % (int)r.W), r.Y + r.H * 0.42f + (i % 2) * 3 * s, 1.6f * s, SilkColours[i % SilkColours.Length] * 0.8f);
        g.Rect(r.X, r.Y + r.H * 0.48f, r.W, r.H * 0.52f, new Color(60, 150, 60));
        float cam = time * 90 * s;
        for (float x = r.X - Backdrops.Mod(cam, 40 * s); x < r.Right; x += 40 * s)
            g.Rect(x, r.Y + r.H * 0.52f, 20 * s, r.H * 0.48f, new Color(70, 165, 68));
        g.Rect(r.X, r.Y + r.H * 0.5f, r.W, 1.5f * s, Pal.White);
        Color[] a = [Pal.Red, Pal.Blue, Pal.Yellow];
        Color[] b = [Pal.White, Pal.Yellow, Pal.Green];
        for (int i = 0; i < 3; i++)
        {
            float x = r.CenterX + (i - 1) * 34 * s + MathF.Sin(time * 1.3f + i * 2) * 8 * s;
            float y = r.Y + r.H * 0.68f + i * 9 * s;
            DrawHorse(g, x, y, s * 0.75f, time * 12 + i * 1.7f, Coats[i], a[i], b[i], i + 1);
        }
        g.TextShadow("2/1", r.Right - 6 * s, r.Y + 6 * s, 1.3f * s, Pal.Gold, Align.Right);
    }

    // ------------------------------------------------------------------ autoplay

    public override void AutoPlay(Controls c)
    {
        if (_autoClick == 1)
        {
            c.Pointer = _autoPos;
            c.PointerReleased = true;
            _autoClick = 0;
            return;
        }
        if (_phase == Phase.Bet && _phaseT > 0.8f)
        {
            if (_pick < 0)
            {
                // Back one of the three favourites.
                int best = 0;
                for (int i = 1; i < Field; i++)
                    if (_horses[i].P > _horses[best].P)
                        best = i;
                int pick = Chance(0.6f) ? best : RandInt(0, Field);
                c.Typed.Add((char)('1' + pick));
                return;
            }
            int want = Math.Max(1, _money / 6);
            if (_phaseT > 1.4f && Tick % 12 == 0)
            {
                if (Stakes[_stakeIndex] < want && _stakeIndex < MaxStakeIndex() && Stakes[Math.Min(_stakeIndex + 1, Stakes.Length - 1)] <= want)
                    Click(c, PlusRect.Center);
                else if (Stakes[_stakeIndex] > want && _stakeIndex > 0)
                    Click(c, MinusRect.Center);
                else if (_phaseT > 2.2f)
                    Click(c, GoRect.Center);
            }
        }
        else if (_phase == Phase.Result && _phaseT > 2.5f && Tick % 10 == 0)
        {
            Click(c, GoRect.Center);
        }
    }

    private void Click(Controls c, Vector2 p)
    {
        c.Pointer = p;
        c.PointerPressed = true;
        c.PointerDown = true;
        _autoPos = p;
        _autoClick = 1;
    }
}
