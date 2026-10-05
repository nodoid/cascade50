using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 50 Universe: a space trader. Buy low, warp across eight star systems and sell high while
/// famines, booms and wars push prices about. Pirates and police make the journey interesting.
/// Forty days to build the biggest fortune you can.
/// </summary>
public sealed class Universe : MiniGame, Capture.ICaptureHints
{
    public override int Number => 50;
    public override string Title => "Universe";
    public override Category Category => Category.Brain;
    public override string Tagline => "Trade across the stars. Forty days to make your fortune.";
    public override Color Accent => Pal.Cyan;
    public override Pad Pad => Pad.None;
    public int CaptureTicks => 60 * 6;

    public override string[] HowToPlay =>
    [
        "Buy goods cheap, warp to another star and sell them dear. News of famines, booms and wars moves prices.",
        "Warping costs fuel and days. Pirates may attack; police seize contraband. Upgrade your hold and lasers.",
        "After 40 days your score is your net worth.",
    ];

    public override string[] DesktopControls =>
    [
        "Click stars and buttons, or:",
        "1-6 pick a good, B buy, S sell, M max, L sell all.",
        "LEFT / RIGHT pick a star, W warp, F fuel, H hold, G lasers, R wait.",
    ];

    public override string[] TouchControls => ["Tap a star to pick it, then WARP.", "Tap BUY / SELL to trade."];

    private const int Days = 40, Goods = 6;

    private static readonly string[] GoodNames = ["FOOD", "ORE", "TECH", "MEDICINE", "LUXURIES", "CONTRA"];
    private static readonly float[] BasePrice = [22, 48, 165, 115, 260, 430];
    private static readonly Color[] GoodColours = [Pal.Lime, Pal.Sand, Pal.Cyan, Pal.Pink, Pal.Gold, Pal.Red];

    private enum Econ
    {
        Farming,
        Mining,
        Industry,
        HighTech,
        Frontier,
    }

    private static readonly string[] EconNames = ["FARMING", "MINING", "INDUSTRY", "HI-TECH", "FRONTIER"];
    private static readonly Color[] EconColours = [new(90, 220, 110), new(220, 160, 90), new(160, 170, 200), new(90, 200, 255), new(240, 90, 90)];

    private static readonly float[,] EconFactor =
    {
        { 0.45f, 1.25f, 1.3f, 1.1f, 1.1f, 1.0f },
        { 1.35f, 0.45f, 1.2f, 1.2f, 1.0f, 1.0f },
        { 1.2f, 1.35f, 0.55f, 0.9f, 1.05f, 0.9f },
        { 1.3f, 1.1f, 0.8f, 0.55f, 1.35f, 1.15f },
        { 1.45f, 0.9f, 1.5f, 1.55f, 0.7f, 0.55f },
    };

    private sealed class Star
    {
        public string Name;
        public Vector2 Pos;
        public Econ Econ;
        public float Law;
        public readonly float[] Price = new float[Goods];
        public int Event = -1, EventDays;
        public int EventGood;
    }

    private static readonly string[] EventNames = ["FAMINE", "BOOM", "WAR", "PLAGUE", "GLUT"];

    private static readonly string[] StarNames = ["SOL PRIME", "VEGA", "ALTAIR", "RIGEL", "DENEB", "KEPLER", "ORION GATE", "TAU NOVA"];

    private readonly Star[] _stars = new Star[8];
    private readonly int[] _cargo = new int[Goods];
    private readonly float[] _paid = new float[Goods];
    private readonly List<string> _news = new();
    private int _here, _dest = -1, _sel, _day, _hold, _fuel, _laser, _credits;
    private const int MaxFuel = 10;

    private enum Mode
    {
        Market,
        Warp,
        Pirates,
        Police,
        Message,
    }

    private Mode _mode;
    private float _modeTime;
    private string _msgTitle, _msgBody;
    private Color _msgColour;
    private int _warpTo, _warpDays, _warpFuel;

    private static readonly RectF MapRect = new(6, 26, 300, 232);
    private static readonly RectF MarketRect = new(312, 26, 322, 232);

    protected override void Start()
    {
        var econs = new[] { Econ.Farming, Econ.Mining, Econ.Industry, Econ.HighTech, Econ.Frontier, Econ.Farming, Econ.Mining, Econ.Industry };
        for (int i = econs.Length - 1; i > 0; i--)
        {
            int j = RandInt(0, i + 1);
            (econs[i], econs[j]) = (econs[j], econs[i]);
        }
        // Scatter the stars, keeping them apart.
        for (int i = 0; i < _stars.Length; i++)
        {
            Vector2 p;
            int tries = 0;
            bool ok;
            do
            {
                p = new Vector2(Rand(MapRect.X + 32, MapRect.Right - 32), Rand(MapRect.Y + 30, MapRect.Bottom - 46));
                ok = true;
                for (int k = 0; k < i; k++)
                    if (Vector2.Distance(p, _stars[k].Pos) < 62)
                        ok = false;
            }
            while (!ok && ++tries < 200);
            var s = new Star { Name = StarNames[i], Pos = p, Econ = econs[i], Law = econs[i] == Econ.Frontier ? 0 : Rand(0.2f, 0.45f) };
            for (int g = 0; g < Goods; g++)
                s.Price[g] = Target(s, g) * Rand(0.9f, 1.1f);
            _stars[i] = s;
        }
        _here = 0;
        for (int i = 0; i < Goods; i++)
        {
            _cargo[i] = 0;
            _paid[i] = 0;
        }
        _credits = 1000;
        _hold = 20;
        _fuel = MaxFuel;
        _laser = 0;
        _day = 1;
        _news.Clear();
        AddNews("WELCOME, TRADER. 40 DAYS TO MAKE YOUR FORTUNE.");
        _dest = NearestOther();
        _mode = Mode.Market;
        UpdateScore();
    }

    private int NearestOther()
    {
        int best = -1;
        float bd = float.MaxValue;
        for (int i = 0; i < _stars.Length; i++)
            if (i != _here)
            {
                float d = Vector2.Distance(_stars[i].Pos, _stars[_here].Pos);
                if (d < bd)
                {
                    bd = d;
                    best = i;
                }
            }
        return best;
    }

    private static float EventFactor(Star s, int g) => s.Event switch
    {
        0 => g == 0 ? 2.6f : g == 3 ? 1.5f : 1,
        1 => g == 4 ? 2.2f : g == 2 ? 1.5f : 1,
        2 => g == 3 ? 2.1f : g == 2 ? 1.8f : g == 5 ? 1.8f : g == 0 ? 1.4f : 1,
        3 => g == 3 ? 3f : 1,
        4 => g == s.EventGood ? 0.4f : 1,
        _ => 1,
    };

    private static float Target(Star s, int g) => BasePrice[g] * EconFactor[(int)s.Econ, g] * EventFactor(s, g);

    private int Price(int star, int g) => Math.Max(1, (int)MathF.Round(_stars[star].Price[g]));

    private int Used()
    {
        int n = 0;
        for (int g = 0; g < Goods; g++)
            n += _cargo[g];
        return n;
    }

    private int NetWorth()
    {
        long v = _credits;
        for (int g = 0; g < Goods; g++)
            v += (long)_cargo[g] * Price(_here, g);
        return (int)Math.Clamp(v, 0, int.MaxValue);
    }

    private void UpdateScore()
    {
        Score = NetWorth();
        Status = "DAY " + Math.Min(_day, Days) + " / " + Days;
        Level = 0;
    }

    private void AddNews(string s)
    {
        _news.Insert(0, s);
        if (_news.Count > 3)
            _news.RemoveAt(_news.Count - 1);
    }

    private float Dist(int a, int b) => Vector2.Distance(_stars[a].Pos, _stars[b].Pos);
    private int FuelCost(int a, int b) => Math.Max(1, (int)MathF.Round(Dist(a, b) / 42));
    private int DayCost(int a, int b) => Math.Max(1, (int)MathF.Round(Dist(a, b) / 55));
    private int FuelPrice => _stars[_here].Econ == Econ.Mining ? 14 : _stars[_here].Econ == Econ.Frontier ? 30 : 20;
    private int HoldCost => 400 + (_hold - 20) * 45;
    private int LaserCost => 500 * (_laser + 1);

    // ------------------------------------------------------------------ the passage of time

    private void PassDays(int n)
    {
        for (int d = 0; d < n; d++)
        {
            _day++;
            foreach (var s in _stars)
            {
                if (s.Event >= 0 && --s.EventDays <= 0)
                {
                    AddNews(s.Name + ": THE " + EventNames[s.Event] + " IS OVER.");
                    s.Event = -1;
                }
                for (int g = 0; g < Goods; g++)
                {
                    float t = Target(s, g);
                    s.Price[g] += (t - s.Price[g]) * 0.3f + t * Rand(-0.08f, 0.08f);
                    s.Price[g] = MathF.Max(1, s.Price[g]);
                }
            }
            if (Chance(0.32f))
            {
                var s = _stars[RandInt(0, _stars.Length)];
                if (s.Event < 0)
                {
                    s.Event = RandInt(0, EventNames.Length);
                    s.EventGood = RandInt(0, Goods - 1);
                    s.EventDays = RandInt(3, 7);
                    string what = s.Event == 4 ? GoodNames[s.EventGood] + " GLUT" : EventNames[s.Event];
                    AddNews(s.Name + ": " + what + "! " + EventHint(s));
                }
            }
        }
    }

    private static string EventHint(Star s) => s.Event switch
    {
        0 => "FOOD NEEDED.",
        1 => "LUXURIES WANTED.",
        2 => "ARMS AND MEDICINE.",
        3 => "MEDICINE URGENT.",
        _ => "PRICES CRASH.",
    };

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        _modeTime += Dt;
        switch (_mode)
        {
            case Mode.Market:
                UpdateMarket();
                break;
            case Mode.Warp:
                Sound.Loop(LoopSfx.Hum, true, MathF.Min(1, _modeTime) - 0.5f, 0.4f);
                if (_modeTime > 1.7f)
                    Arrive();
                break;
            case Mode.Pirates:
                UpdatePirates();
                break;
            case Mode.Police:
                UpdatePolice();
                break;
            case Mode.Message:
                if (Ui.Button(new RectF(260, 236, 120, 36), "OK", Keys.Enter, color: new Color(30, 90, 120)) || In.FirePressed)
                {
                    SetMode(Mode.Market);
                    if (_day > Days)
                        Finish();
                }
                break;
        }
        UpdateScore();
    }

    private void SetMode(Mode m)
    {
        _mode = m;
        _modeTime = 0;
    }

    private void Message(string title, string body, Color colour)
    {
        _msgTitle = title;
        _msgBody = body;
        _msgColour = colour;
        SetMode(Mode.Message);
    }

    private RectF BuyRect(int g) => new(MarketRect.X + 206, RowY(g) - 2, 54, 29);
    private RectF SellRect(int g) => new(MarketRect.X + 264, RowY(g) - 2, 54, 29);
    private static float RowY(int g) => MarketRect.Y + 42 + g * 31;

    private void UpdateMarket()
    {
        // Trading.
        foreach (char ch in In.Typed)
            if (ch >= '1' && ch <= '6')
            {
                _sel = ch - '1';
                Sound.Play(Sfx.Tick, 0.3f, 0.4f);
            }
        for (int g = 0; g < Goods; g++)
        {
            bool canBuy = _credits >= Price(_here, g) && Used() < _hold;
            if (Ui.Button(BuyRect(g), "BUY", Keys.None, canBuy, new Color(30, 100, 70), _sel == g && !IsTouch, 1.5f, "buy" + g))
                Buy(g, 1);
            if (Ui.Button(SellRect(g), "SELL", Keys.None, _cargo[g] > 0, new Color(110, 60, 40), false, 1.5f, "sell" + g))
                Sell(g, 1);
        }
        if (In.KeyPressed(Keys.B))
            Buy(_sel, 1);
        if (In.KeyPressed(Keys.S))
            Sell(_sel, 1);
        if (In.KeyPressed(Keys.M))
            Buy(_sel, 999);
        if (In.KeyPressed(Keys.L))
            Sell(_sel, 999);

        // Choosing a destination.
        if (In.PointerPressed)
            for (int i = 0; i < _stars.Length; i++)
                if (i != _here && Vector2.Distance(In.Pointer, _stars[i].Pos) < 18)
                {
                    _dest = i;
                    Sound.Play(Sfx.Select, 0.2f, 0.5f);
                }
        if (In.LeftPressed || In.RightPressed)
        {
            int step = In.RightPressed ? 1 : -1;
            int d = _dest < 0 ? _here : _dest;
            do d = (d + step + _stars.Length) % _stars.Length;
            while (d == _here);
            _dest = d;
            Sound.Play(Sfx.Select, 0.2f, 0.5f);
        }

        // Bottom bar.
        const float by = 292, bh = 30;
        bool canWarp = _dest >= 0 && FuelCost(_here, _dest) <= _fuel;
        string warpLabel = _dest >= 0 ? "WARP" : "PICK A STAR";
        if (Ui.Button(new RectF(8, by, 112, bh), warpLabel, Keys.W, canWarp, new Color(30, 80, 150)))
            StartWarp();
        int need = MaxFuel - _fuel;
        bool canFuel = need > 0 && _credits >= FuelPrice;
        if (Ui.Button(new RectF(126, by, 118, bh), "FUEL " + FuelPrice * Math.Max(1, Math.Min(need, _credits / FuelPrice)), Keys.F, canFuel, new Color(120, 90, 30)))
            BuyFuel();
        if (Ui.Button(new RectF(250, by, 136, bh), "HOLD+10 " + HoldCost, Keys.H, _credits >= HoldCost && _hold < 80, new Color(70, 60, 120)))
        {
            _credits -= HoldCost;
            _hold += 10;
            Sound.Play(Sfx.PowerUp);
            AddNews("CARGO HOLD EXPANDED TO " + _hold + ".");
        }
        if (Ui.Button(new RectF(392, by, 136, bh), _laser >= 3 ? "LASERS MAX" : "LASER " + LaserCost, Keys.G, _laser < 3 && _credits >= LaserCost, new Color(120, 40, 60)))
        {
            _credits -= LaserCost;
            _laser++;
            Sound.Play(Sfx.PowerUp, 0.3f);
            AddNews("LASERS UPGRADED TO MARK " + _laser + ".");
        }
        if (Ui.Button(new RectF(534, by, 98, bh), "WAIT", Keys.R, true, new Color(50, 60, 80)))
        {
            PassDays(1);
            Sound.Play(Sfx.Tick, -0.3f);
            if (_day > Days)
                Finish();
        }
    }

    private void Buy(int g, int n)
    {
        int p = Price(_here, g);
        int can = Math.Min(n, Math.Min(_hold - Used(), _credits / p));
        if (can <= 0)
        {
            Sound.Play(Sfx.Wrong, 0, 0.5f);
            return;
        }
        _paid[g] = (_paid[g] * _cargo[g] + p * can) / (_cargo[g] + can);
        _cargo[g] += can;
        _credits -= p * can;
        Sound.Play(Sfx.Coin, -0.2f, 0.6f);
        Fx.Float("-" + p * can, BuyRect(g).CenterX, BuyRect(g).Y, Pal.Orange, 1.2f);
    }

    private void Sell(int g, int n)
    {
        int can = Math.Min(n, _cargo[g]);
        if (can <= 0)
            return;
        int p = Price(_here, g);
        _cargo[g] -= can;
        _credits += p * can;
        bool profit = p >= _paid[g];
        if (_cargo[g] == 0)
            _paid[g] = 0;
        Sound.Play(profit ? Sfx.Coin : Sfx.Pop, profit ? 0.4f : -0.3f, 0.7f);
        Fx.Float("+" + p * can, SellRect(g).CenterX, SellRect(g).Y, profit ? Pal.Lime : Pal.Grey, 1.2f);
    }

    private void BuyFuel()
    {
        int need = MaxFuel - _fuel;
        int n = Math.Min(need, _credits / FuelPrice);
        if (n <= 0)
            return;
        _fuel += n;
        _credits -= n * FuelPrice;
        Sound.Play(Sfx.Pickup, -0.2f);
    }

    private void StartWarp()
    {
        _warpTo = _dest;
        _warpFuel = FuelCost(_here, _dest);
        _warpDays = DayCost(_here, _dest);
        _fuel -= _warpFuel;
        SetMode(Mode.Warp);
        Sound.Play(Sfx.Warp);
    }

    private void Arrive()
    {
        PassDays(_warpDays);
        _here = _warpTo;
        _dest = NearestOther();
        Sound.Play(Sfx.Whoosh, -0.3f);
        if (_day > Days)
        {
            Message("JOURNEY'S END", "THE 40 DAYS ARE UP. TIME TO COUNT YOUR CREDITS.", Pal.Gold);
            return;
        }
        // Encounters on arrival.
        float pirateChance = 0.12f + Used() * 0.003f + (_stars[_here].Econ == Econ.Frontier ? 0.15f : 0);
        if (Chance(pirateChance))
        {
            SetMode(Mode.Pirates);
            Sound.Play(Sfx.Alarm);
            return;
        }
        if (Chance(_stars[_here].Law))
        {
            SetMode(Mode.Police);
            Sound.Play(Sfx.Beep, 0.4f);
            return;
        }
        SetMode(Mode.Market);
    }

    private static RectF ChoiceRect(int i) => new(170 + i * 104, 236, 96, 36);

    private void UpdatePirates()
    {
        int tribute = Math.Max(50, _credits / 6);
        if (Ui.Button(ChoiceRect(0), "FIGHT", Keys.D1, true, new Color(140, 40, 50)))
        {
            float win = 0.3f + 0.17f * _laser;
            if (Chance(win))
            {
                int loot = RandInt(150, 450) * (1 + _laser);
                _credits += loot;
                Sound.Play(Sfx.Explode);
                Fx.Explode(320, 140, 1.5f);
                Message("VICTORY!", "THE PIRATES ARE DESTROYED. YOU SALVAGE " + loot + " CREDITS.", Pal.Lime);
            }
            else
            {
                int lost = LoseCargo(0.5f);
                int cr = _credits / 10;
                _credits -= cr;
                Sound.Play(Sfx.Hurt);
                Fx.Shake(5, 0.4f);
                Message("DEFEATED", "THEY TAKE " + lost + " CARGO AND " + cr + " CREDITS.", Pal.Red);
            }
        }
        if (Ui.Button(ChoiceRect(1), "FLEE", Keys.D2, true, new Color(40, 80, 140)))
        {
            if (Chance(0.55f))
            {
                Sound.Play(Sfx.Whoosh);
                Message("ESCAPED", "YOU OUTRUN THE PIRATES.", Pal.Sky);
            }
            else
            {
                int lost = LoseCargo(0.25f);
                Sound.Play(Sfx.Hit);
                Fx.Shake(3, 0.3f);
                Message("CAUGHT", "THEY BLAST YOUR HOLD: " + lost + " CARGO LOST.", Pal.Orange);
            }
        }
        if (Ui.Button(ChoiceRect(2), "PAY " + tribute, Keys.D3, true, new Color(110, 90, 30)))
        {
            if (_credits >= tribute)
            {
                _credits -= tribute;
                Sound.Play(Sfx.Coin, -0.5f);
                Message("PAID OFF", "THE PIRATES TAKE " + tribute + " CREDITS AND LEAVE.", Pal.Gold);
            }
            else
            {
                int lost = LoseCargo(0.4f);
                _credits = 0;
                Message("NOT ENOUGH", "THEY TAKE EVERYTHING YOU HAVE AND " + lost + " CARGO.", Pal.Red);
            }
        }
    }

    private int LoseCargo(float fraction)
    {
        int lost = 0;
        for (int g = 0; g < Goods; g++)
        {
            int n = (int)MathF.Ceiling(_cargo[g] * fraction);
            _cargo[g] -= n;
            lost += n;
        }
        return lost;
    }

    private void UpdatePolice()
    {
        int contra = _cargo[5];
        if (Ui.Button(ChoiceRect(0), "COMPLY", Keys.D1, true, new Color(40, 80, 140)))
        {
            if (contra == 0)
            {
                Sound.Play(Sfx.Correct);
                Message("ALL CLEAR", "THE POLICE SCAN FINDS NOTHING. SAFE TRADING, CAPTAIN.", Pal.Sky);
            }
            else
            {
                int fine = Math.Min(_credits, contra * 60);
                _credits -= fine;
                _cargo[5] = 0;
                Sound.Play(Sfx.Wrong);
                Message("CONTRABAND!", contra + " UNITS SEIZED AND A FINE OF " + fine + ".", Pal.Red);
            }
        }
        if (Ui.Button(ChoiceRect(1), "FLEE", Keys.D2, true, new Color(140, 40, 50)))
        {
            if (Chance(0.5f))
            {
                Sound.Play(Sfx.Whoosh);
                Message("GOT AWAY", "YOU GIVE THE PATROL THE SLIP.", Pal.Sky);
            }
            else
            {
                int fine = Math.Min(_credits, 150 + contra * 100);
                _credits -= fine;
                _cargo[5] = 0;
                Sound.Play(Sfx.Wrong);
                Message("ARRESTED", "FINED " + fine + " FOR RESISTING. CONTRABAND SEIZED.", Pal.Red);
            }
        }
    }

    private void Finish()
    {
        // Sell everything at local prices for the final reckoning.
        Score = NetWorth();
        EndGame(Score > 1000, "Net worth: " + Score + " credits");
    }

    // ------------------------------------------------------------------ drawing

    private static void Nebula(Gfx g, RectF r, float t)
    {
        Backdrops.Space(g, t, 2, 50, r);
        float s = r.H / 360f;
        g.Glow(r.X + r.W * 0.3f, r.Y + r.H * 0.4f, 200 * s, new Color(60, 30, 140), 0.35f);
        g.Glow(r.X + r.W * 0.7f, r.Y + r.H * 0.65f, 220 * s, new Color(20, 90, 150), 0.3f);
        g.Glow(r.X + r.W * 0.55f, r.Y + r.H * 0.2f, 140 * s, new Color(150, 40, 100), 0.2f);
    }

    private static void DrawPlanet(Gfx g, Vector2 p, float r, Color c, float t, int seed)
    {
        g.Glow(p, r * 3, c, 0.35f);
        g.Circle(p.X, p.Y, r, Pal.Darken(c, 0.35f));
        g.Circle(p.X - r * 0.25f, p.Y - r * 0.25f, r * 0.75f, c);
        g.Circle(p.X - r * 0.4f, p.Y - r * 0.4f, r * 0.3f, Pal.Lighten(c, 0.5f));
        if (seed % 3 == 0)
            g.Arc(p.X, p.Y, r * 1.6f, 1.2f, MathF.PI * 0.9f + t * 0.1f, MathF.PI * 2.1f + t * 0.1f, Pal.Lighten(c, 0.3f) * 0.7f);
    }

    private void Header(Gfx g, RectF r, string title)
    {
        g.Panel(r, new Color(8, 14, 34) * 0.9f, new Color(60, 140, 200), 8);
        g.GradientH(r.X + 2, r.Y + 2, r.W - 4, 14, new Color(30, 90, 140), new Color(8, 14, 34) * 0f);
        g.Text(title, r.X + 8, r.Y + 5, 1f, Pal.Cyan);
    }

    public override void Draw(Gfx g)
    {
        Nebula(g, g.Visible, Time);
        DrawMap(g);
        DrawMarket(g);
        DrawBar(g);

        if (_mode == Mode.Warp)
            DrawWarp(g, g.Visible, _modeTime / 1.7f, _stars[_warpTo].Name);
        else if (_mode is Mode.Pirates or Mode.Police or Mode.Message)
            DrawModal(g);
    }

    private void DrawMap(Gfx g)
    {
        Header(g, MapRect, "GALAXY CHART");
        var here = _stars[_here];
        // Jump range.
        float range = _fuel * 42 + 21;
        g.Ring(here.Pos.X, here.Pos.Y, range, 1, new Color(60, 140, 200) * 0.35f, 64);
        g.SetClip(MapRect.Inflate(-2, -2));
        for (int i = 0; i < _stars.Length; i++)
            if (i != _here && FuelCost(_here, i) <= _fuel)
                g.Line(here.Pos, _stars[i].Pos, 1, new Color(60, 140, 200) * 0.25f);
        if (_dest >= 0)
        {
            var d = _stars[_dest];
            bool ok = FuelCost(_here, _dest) <= _fuel;
            var dir = d.Pos - here.Pos;
            float len = dir.Length();
            dir /= len;
            for (float k = Backdrops.Mod(Time * 30, 10); k < len; k += 10)
                g.Line(here.Pos + dir * k, here.Pos + dir * MathF.Min(len, k + 5), 1.6f, ok ? Pal.Cyan : Pal.Red);
        }
        for (int i = 0; i < _stars.Length; i++)
        {
            var s = _stars[i];
            var c = EconColours[(int)s.Econ];
            DrawPlanet(g, s.Pos, i == _here ? 7 : 5.5f, c, Time, i);
            if (i == _here)
            {
                g.Ring(s.Pos.X, s.Pos.Y, 12 + 2 * MathF2.Pulse(Time, 1.2f), 1.5f, Pal.White);
            }
            if (i == _dest)
                g.Ring(s.Pos.X, s.Pos.Y, 11, 1.5f, Pal.Cyan * (0.6f + 0.4f * MathF2.Pulse(Time, 0.6f)));
            if (s.Event >= 0)
            {
                bool blink = (int)(Time * 3) % 2 == 0;
                g.Circle(s.Pos.X + 9, s.Pos.Y - 9, 4, blink ? Pal.Red : Pal.Orange);
                g.Text("!", s.Pos.X + 9, s.Pos.Y - 12, 1f, Pal.White, Align.Center);
            }
            g.Text(s.Name, s.Pos.X, s.Pos.Y + (i == _here ? 16 : 11), 1f, i == _here ? Pal.White : Pal.LightGrey * 0.85f, Align.Center);
        }
        g.SetClip(null);
        // Destination details.
        var info = new RectF(MapRect.X + 6, MapRect.Bottom - 30, MapRect.W - 12, 24);
        g.RoundRect(info, 5, new Color(20, 40, 70) * 0.85f);
        if (_dest >= 0)
        {
            var d = _stars[_dest];
            g.Text(d.Name, info.X + 6, info.Y + 4, 1.5f, EconColours[(int)d.Econ]);
            string sub = EconNames[(int)d.Econ] + (d.Event >= 0 ? "  " + (d.Event == 4 ? "GLUT" : EventNames[d.Event]) : "");
            g.Text(sub, info.X + 6, info.Y + 16, 1f, d.Event >= 0 ? Pal.Orange : Pal.Grey);
            int fc = FuelCost(_here, _dest);
            g.Text("FUEL " + fc + "  DAYS " + DayCost(_here, _dest), info.Right - 6, info.Y + 8, 1.5f, fc <= _fuel ? Pal.Cyan : Pal.Red, Align.Right);
        }
    }

    private void DrawMarket(Gfx g)
    {
        var here = _stars[_here];
        Header(g, MarketRect, "MARKET: " + here.Name + "  (" + EconNames[(int)here.Econ] + ")");
        if (here.Event >= 0)
            g.Text(here.Event == 4 ? "GLUT!" : EventNames[here.Event] + "!", MarketRect.Right - 8, MarketRect.Y + 5, 1f, Pal.Orange, Align.Right);
        float hx = MarketRect.X + 8;
        string there = _dest >= 0 ? _stars[_dest].Name : "";
        if (there.Length > 5)
            there = there[..5];
        g.Text("GOOD", hx, MarketRect.Y + 26, 1f, Pal.Grey);
        g.Text("HERE", MarketRect.X + 132, MarketRect.Y + 26, 1f, Pal.Grey, Align.Right);
        g.Text(there, MarketRect.X + 168, MarketRect.Y + 26, 1f, Pal.Sky * 0.8f, Align.Right);
        g.Text("HELD", MarketRect.X + 198, MarketRect.Y + 26, 1f, Pal.Grey, Align.Right);
        for (int gd = 0; gd < Goods; gd++)
        {
            float y = RowY(gd);
            if (gd == _sel && !IsTouch)
                g.RoundRect(MarketRect.X + 4, y - 3, MarketRect.W - 8, 31, 4, new Color(40, 70, 120) * 0.6f);
            else if (gd % 2 == 0)
                g.RoundRect(MarketRect.X + 4, y - 3, MarketRect.W - 8, 31, 4, new Color(255, 255, 255) * 0.03f);
            g.Circle(hx + 3, y + 12, 3, GoodColours[gd]);
            g.Text(GoodNames[gd], hx + 10, y + 7, 1.5f, Pal.White);
            int p = Price(_here, gd);
            float avg = 0;
            for (int s = 0; s < _stars.Length; s++)
                avg += _stars[s].Price[gd];
            avg /= _stars.Length;
            var pc = p < avg * 0.8f ? Pal.Lime : p > avg * 1.2f ? Pal.Orange : Pal.White;
            g.Text(p.ToString(), MarketRect.X + 132, y + 7, 1.5f, pc, Align.Right);
            if (_dest >= 0)
            {
                int q = Price(_dest, gd);
                var qc = q > p * 1.15f ? Pal.Lime : q < p * 0.87f ? Pal.Red * 0.9f : Pal.Sky * 0.8f;
                g.Text(q.ToString(), MarketRect.X + 168, y + 9, 1f, qc, Align.Right);
            }
            g.Text(_cargo[gd].ToString(), MarketRect.X + 198, y + 7, 1.5f, _cargo[gd] > 0 ? Pal.Gold : Pal.DarkGrey, Align.Right);
        }
    }

    private void DrawBar(Gfx g)
    {
        var r = new RectF(6, 262, 628, 96);
        g.Panel(r, new Color(8, 14, 34) * 0.9f, new Color(60, 140, 200), 8);
        float y = 265;
        g.Text("CREDITS", 14, y, 1f, Pal.Grey);
        g.Text(_credits.ToString("N0"), 14, y + 9, 1.5f, Pal.Gold);
        g.Text("FUEL", 120, y, 1f, Pal.Grey);
        for (int i = 0; i < MaxFuel; i++)
            g.Rect(120 + i * 8, y + 10, 6, 9, i < _fuel ? (_fuel <= 2 ? Pal.Red : Pal.Lime) : new Color(40, 50, 70));
        g.Text("HOLD", 216, y, 1f, Pal.Grey);
        g.Text(Used() + "/" + _hold, 216, y + 9, 1.5f, Used() >= _hold ? Pal.Orange : Pal.White);
        g.Text("LASERS", 282, y, 1f, Pal.Grey);
        g.Text(_laser == 0 ? "NONE" : "MK " + _laser, 282, y + 9, 1.5f, Pal.Pink);
        g.Text("DAYS LEFT", 350, y, 1f, Pal.Grey);
        int left = Math.Max(0, Days - _day + 1);
        g.Text(left.ToString(), 350, y + 9, 1.5f, left <= 5 ? Pal.Red : Pal.Cyan);
        g.Text("NEWS", 420, y, 1f, Pal.Grey);
        if (_news.Count > 0)
        {
            string n = _news[0];
            g.SetClip(new RectF(420, y + 8, 208, 14));
            float w = Gfx.TextWidth(n, 1f);
            float x = w > 206 ? 420 - Backdrops.Mod(Time * 30, w + 60) + 60 : 420;
            g.Text(n, x, y + 10, 1f, Pal.Ice);
            if (w > 206)
                g.Text(n, x + w + 60, y + 10, 1f, Pal.Ice);
            g.SetClip(null);
        }
        if (_news.Count > 1)
        {
            g.Text(_news[1], 14, 328, 1f, Pal.LightGrey * 0.6f);
        }
        if (_news.Count > 2)
            g.Text(_news[2], 14, 340, 1f, Pal.LightGrey * 0.4f);
    }

    private static void DrawWarp(Gfx g, RectF r, float k, string name)
    {
        float a = MathF.Min(1, k * 12) * MathF.Min(1, (1 - k) * 8);
        g.Rect(r, new Color(2, 4, 16) * a);
        g.Glow(r.CenterX, r.CenterY, r.W * 0.6f, new Color(60, 30, 140), 0.4f * a);
        var c = new Vector2(r.CenterX, r.CenterY);
        var rng = new Random(9);
        float speed = MathF2.EaseInOut(MathF2.Clamp(k * 1.3f, 0, 1));
        for (int i = 0; i < 160; i++)
        {
            float ang = (float)rng.NextDouble() * MathF2.Tau;
            float d0 = (float)rng.NextDouble();
            float d = Backdrops.Mod(d0 + k * (0.6f + 2.5f * speed), 1);
            float r0 = d * d * r.W * 0.7f;
            float r1 = r0 + (8 + 160 * speed) * d;
            var dir = MathF2.FromAngle(ang);
            var col = i % 5 == 0 ? Pal.Cyan : i % 7 == 0 ? Pal.Pink : Pal.White;
            g.Line(c + dir * r0, c + dir * r1, 1 + d * 2, col * (a * MathF.Min(1, d * 3)));
        }
        g.Glow(c, 120 * (0.5f + speed), Pal.Cyan, 0.5f * a);
        g.Glow(c, 40, Pal.White, a * (0.4f + 0.6f * MathF2.Pulse(k * 3, 1)));
        if (k > 0.85f)
            g.Rect(r, Pal.Add(Pal.White, (k - 0.85f) * 5));
        g.TextShadow("WARPING TO " + name, c.X, r.Bottom - 60 * r.H / 360, 2f * r.H / 360, Pal.White * a, Align.Center);
    }

    private static readonly Vector2[] Fighter = [new(16, 0), new(-6, -12), new(-12, -4), new(-12, 4), new(-6, 12)];

    private void DrawModal(Gfx g)
    {
        g.Rect(g.Visible, Color.Black * 0.55f);
        var r = new RectF(150, 70, 340, 214);
        g.Glow(r.CenterX, r.CenterY, 260, _mode == Mode.Pirates ? Pal.Red : _mode == Mode.Police ? Pal.Blue : _msgColour, 0.25f);
        g.Panel(r, new Color(10, 16, 38), _mode == Mode.Pirates ? Pal.Red : _mode == Mode.Police ? Pal.Sky : _msgColour, 10);
        float k = MathF.Min(1, _modeTime * 3);
        if (_mode == Mode.Pirates)
        {
            g.TextShadow("PIRATES!", r.CenterX, r.Y + 14, 3f, Pal.Red * k, Align.Center);
            for (int i = 0; i < 3; i++)
            {
                var p = new Vector2(r.CenterX - 60 + i * 60, r.Y + 80 + MathF.Sin(Time * 2 + i) * 5);
                g.Glow(p, 26, Pal.Red, 0.4f);
                g.Shape(Fighter, p, MathF.PI / 2, 1.3f, new Color(80, 30, 40));
                g.ShapeOutline(Fighter, p, MathF.PI / 2, 1.3f, 1.5f, Pal.Red, true);
            }
            g.TextWrapped("A PIRATE GANG DEMANDS YOUR CARGO. LASERS MK " + _laser + ".", r.X + 20, r.Y + 120, r.W - 40, 1.5f, Pal.White, 1.35f, Align.Center);
        }
        else if (_mode == Mode.Police)
        {
            g.TextShadow("POLICE PATROL", r.CenterX, r.Y + 14, 3f, Pal.Sky * k, Align.Center);
            var p = new Vector2(r.CenterX, r.Y + 80);
            bool blink = (int)(Time * 6) % 2 == 0;
            g.Glow(p + new Vector2(-14, -8), 16, blink ? Pal.Red : Pal.Blue, 0.9f);
            g.Glow(p + new Vector2(14, -8), 16, blink ? Pal.Blue : Pal.Red, 0.9f);
            g.Shape(Fighter, p, MathF.PI / 2, 1.8f, new Color(200, 210, 230));
            g.ShapeOutline(Fighter, p, MathF.PI / 2, 1.8f, 1.5f, Pal.Sky, true);
            string body = _cargo[5] > 0 ? "STAND BY FOR A CARGO SCAN. YOU ARE CARRYING " + _cargo[5] + " CONTRABAND." : "STAND BY FOR A ROUTINE CARGO SCAN.";
            g.TextWrapped(body, r.X + 20, r.Y + 120, r.W - 40, 1.5f, Pal.White, 1.35f, Align.Center);
        }
        else
        {
            g.TextShadow(_msgTitle, r.CenterX, r.Y + 22, 3f, _msgColour * k, Align.Center);
            g.TextWrapped(_msgBody, r.X + 20, r.Y + 76, r.W - 40, 1.5f, Pal.White, 1.4f, Align.Center);
        }
    }

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        Nebula(g, r, time);
        float s = r.H / 162f;
        var pts = new Vector2[5];
        for (int i = 0; i < 5; i++)
            pts[i] = new Vector2(r.X + r.W * (0.12f + i * 0.19f), r.Y + r.H * (0.3f + 0.4f * ((i * 37) % 5) / 4f));
        for (int i = 0; i < 4; i++)
            g.Line(pts[i], pts[i + 1], 1.2f * s, new Color(60, 140, 200) * 0.5f);
        for (int i = 0; i < 5; i++)
            DrawPlanet(g, pts[i], (7 + i % 2 * 3) * s, EconColours[i], time, i);
        // A trader warping along the route.
        float t = time * 0.35f % 1 * 4;
        int seg = (int)t;
        var p = Vector2.Lerp(pts[seg], pts[seg + 1], t - seg);
        var d = pts[seg + 1] - pts[seg];
        float ang = MathF.Atan2(d.Y, d.X);
        for (int i = 1; i < 6; i++)
            g.Circle(p.X - MathF.Cos(ang) * i * 5 * s, p.Y - MathF.Sin(ang) * i * 5 * s, (2.5f - i * 0.35f) * s, Pal.Cyan * (0.8f - i * 0.13f));
        g.Glow(p, 16 * s, Pal.Cyan, 0.8f);
        g.Shape(Fighter, p, ang, 0.7f * s, new Color(30, 50, 90));
        g.ShapeOutline(Fighter, p, ang, 0.7f * s, 1.2f * s, Pal.Cyan, true);
        g.TextShadow("+" + (int)(time * 97 % 900 + 100) + " CR", r.Right - 8 * s, r.Y + 10 * s, 1.5f * s, Pal.Gold, Align.Right);
    }

    // ------------------------------------------------------------------ autoplay

    private int _autoWait;

    public override void AutoPlay(Controls c)
    {
        if (--_autoWait > 0)
            return;
        _autoWait = 22;
        switch (_mode)
        {
            case Mode.Message:
                c.PressedKeys.Add(Keys.Enter);
                return;
            case Mode.Pirates:
                c.PressedKeys.Add(_laser >= 2 ? Keys.D1 : Chance(0.5f) ? Keys.D2 : Keys.D3);
                return;
            case Mode.Police:
                c.PressedKeys.Add(_cargo[5] > 0 && Chance(0.5f) ? Keys.D2 : Keys.D1);
                return;
            case Mode.Warp:
                return;
        }
        // Browse the market for a moment before trading.
        if (_modeTime < 2.2f)
            return;

        // Sell anything worth selling here.
        for (int gd = 0; gd < Goods; gd++)
            if (_cargo[gd] > 0 && (Price(_here, gd) >= _paid[gd] * 1.05f || _day >= Days))
            {
                Tap(c, SellRect(gd), gd);
                return;
            }
        if (_fuel < MaxFuel && _credits >= FuelPrice && (_fuel < 4 || _credits > 3000))
        {
            c.PressedKeys.Add(Keys.F);
            return;
        }
        if (_credits > HoldCost * 3 && _hold < 60)
        {
            c.PressedKeys.Add(Keys.H);
            return;
        }
        if (_laser < 2 && _credits > LaserCost * 4)
        {
            c.PressedKeys.Add(Keys.G);
            return;
        }
        // Best trade to a reachable star.
        int bestStar = -1, bestGood = -1;
        float bestProfit = 0;
        for (int s = 0; s < _stars.Length; s++)
        {
            if (s == _here || FuelCost(_here, s) > _fuel)
                continue;
            for (int gd = 0; gd < Goods; gd++)
            {
                int p = Price(_here, gd);
                int units = Math.Min(_hold - Used(), _credits / p);
                float profit = (Price(s, gd) - p) * units / (float)DayCost(_here, s) - (gd == 5 ? units * 20 : 0);
                if (profit > bestProfit)
                {
                    bestProfit = profit;
                    bestStar = s;
                    bestGood = gd;
                }
            }
        }
        if (bestStar >= 0 && _dest != bestStar)
        {
            Tap(c, RectF.Centered(_stars[bestStar].Pos.X, _stars[bestStar].Pos.Y, 4, 4), -1);
            return;
        }
        if (bestGood >= 0 && Used() < _hold && _credits >= Price(_here, bestGood) && _day < Days)
        {
            _sel = bestGood;
            c.PressedKeys.Add(Keys.M);
            return;
        }
        if (_dest < 0 || FuelCost(_here, _dest) > _fuel)
        {
            if (_credits >= FuelPrice)
                c.PressedKeys.Add(Keys.F);
            else
                c.PressedKeys.Add(Keys.R);
            return;
        }
        c.PressedKeys.Add(Keys.W);
    }

    private void Tap(Controls c, RectF r, int sel)
    {
        if (sel >= 0)
            _sel = sel;
        // Ui buttons need a press and a release; use the keyboard path for a single-tick action instead.
        if (r.W > 10)
        {
            c.PressedKeys.Add(Keys.L);
            return;
        }
        c.Pointer = r.Center;
        c.PointerPressed = true;
        c.PointerDown = true;
    }
}
