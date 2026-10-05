using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 33 Pontoon: the British blackjack, played against the banker for 15 hands. Buy, twist or
/// stick; Pontoon and the Five Card Trick beat 21, and the banker wins ties.
/// </summary>
public sealed class Pontoon : MiniGame, Capture.ICaptureHints
{
    public override int Number => 33;
    public override string Title => "Pontoon";
    public override Category Category => Category.Brain;
    public override string Tagline => "Buy, twist or stick: beat the banker at the British blackjack.";
    public override Color Accent => Pal.Gold;
    public override Pad Pad => Pad.None;
    public int CaptureTicks => 600;

    public override string[] HowToPlay =>
    [
        "Get closer to 21 than the banker without going bust. Aces count 1 or 11, pictures 10.",
        "BUY adds your stake again for a card; TWIST is free but then no more buying. STICK needs 16+.",
        "Pontoon (ace + ten) and five cards are paid double. Banker wins ties. 15 hands.",
    ];

    public override string[] DesktopControls => ["Click buttons, or B buy, T twist,", "S stick, 1-4 stake, D deal."];
    public override string[] TouchControls => ["Tap the buttons to bet and play."];

    private const int Hands = 15, StartChips = 100, MinStake = 5;
    private const float CardW = 50, CardH = 72;
    private static readonly int[] Stakes = [5, 10, 25, 50];
    private static readonly Vector2 DeckPos = new(588, 82);

    private sealed class Card
    {
        public int Rank; // 1..13
        public int Suit; // 0 hearts, 1 diamonds, 2 clubs, 3 spades
        public Vector2 From, To;
        public float Move; // 0..1 travel from the shoe
        public float Flip; // 0 = back, 1 = face
        public bool FaceUp, Bought;
    }

    private enum State { Betting, Dealing, Player, Banker, Result }

    private readonly List<Card> _deck = new();
    private readonly List<Card> _player = new();
    private readonly List<Card> _banker = new();
    private readonly List<(bool Player, bool FaceUp, bool Bought)> _dealQueue = new();

    private State _state;
    private int _chips, _stake, _baseStake, _hand;
    private bool _twisted, _checkPending;
    private float _timer;
    private string _message = "", _subMessage = "";
    private Color _messageColour;
    private int _lastStake = 10;
    private float _potPulse;

    // Autopilot: a pointer click spread over two ticks.
    private int _autoWait;
    private RectF? _autoClick;
    private int _autoPhase;

    protected override void Start()
    {
        _chips = StartChips;
        _lastStake = 10;
        _autoClick = null;
        _autoPhase = 0;
        _autoWait = 0;
        Score = _chips;
        _hand = 0;
        NewDeck();
        StartBetting();
    }

    private void NewDeck()
    {
        _deck.Clear();
        for (int s = 0; s < 4; s++)
            for (int r = 1; r <= 13; r++)
                _deck.Add(new Card { Rank = r, Suit = s });
        for (int i = _deck.Count - 1; i > 0; i--)
        {
            int j = RandInt(0, i + 1);
            (_deck[i], _deck[j]) = (_deck[j], _deck[i]);
        }
        Sound.Play(Sfx.Shuffle);
    }

    private void StartBetting()
    {
        _hand++;
        Status = $"HAND {_hand} OF {Hands}";
        _player.Clear();
        _banker.Clear();
        _state = State.Betting;
        _stake = 0;
        _twisted = false;
        _checkPending = false;
        _message = "PLACE YOUR STAKE";
        _subMessage = "";
        _messageColour = Pal.Gold;
        if (_deck.Count < 15)
            NewDeck();
    }

    // ------------------------------------------------------------------ hand values

    private static int Total(List<Card> hand, bool countHidden = true)
    {
        int total = 0, aces = 0;
        foreach (var c in hand)
        {
            if (!countHidden && !c.FaceUp)
                continue;
            int v = c.Rank >= 10 ? 10 : c.Rank;
            total += v;
            if (c.Rank == 1)
                aces++;
        }
        if (aces > 0 && total + 10 <= 21)
            total += 10;
        return total;
    }

    private static bool Soft(List<Card> hand)
    {
        int total = 0;
        bool ace = false;
        foreach (var c in hand)
        {
            total += c.Rank >= 10 ? 10 : c.Rank;
            ace |= c.Rank == 1;
        }
        return ace && total + 10 <= 21;
    }

    private static bool IsPontoon(List<Card> hand) => hand.Count == 2 && Total(hand) == 21;
    private static bool IsFiveCardTrick(List<Card> hand) => hand.Count == 5 && Total(hand) <= 21;

    /// <summary>Ranks a finished hand: bust 0, plain totals 16..21, five card trick 30, pontoon 40.</summary>
    private static int Rank(List<Card> hand)
    {
        if (IsPontoon(hand)) return 40;
        if (IsFiveCardTrick(hand)) return 30;
        int t = Total(hand);
        return t > 21 ? 0 : t;
    }

    // ------------------------------------------------------------------ dealing

    private Vector2 SlotPos(bool player, int index, int count)
    {
        float spacing = 56;
        float width = (count - 1) * spacing;
        return new Vector2(330 - width / 2 + index * spacing, player ? 224 : 82);
    }

    private void Relayout(List<Card> hand, bool player)
    {
        for (int i = 0; i < hand.Count; i++)
        {
            var target = SlotPos(player, i, hand.Count);
            if (hand[i].Move >= 1)
            {
                hand[i].From = hand[i].To;
                hand[i].Move = 0.6f;
            }
            hand[i].To = target;
        }
    }

    private void Deal(bool toPlayer, bool faceUp, bool bought = false)
    {
        var c = _deck[^1];
        _deck.RemoveAt(_deck.Count - 1);
        c.From = DeckPos;
        c.Move = 0;
        c.Flip = 0;
        c.FaceUp = faceUp;
        c.Bought = bought;
        var hand = toPlayer ? _player : _banker;
        hand.Add(c);
        Relayout(hand, toPlayer);
        c.From = DeckPos;
        c.Move = 0;
        Sound.Play(Sfx.Card, Rand(-0.2f, 0.2f), 0.8f);
    }

    private bool Animating
    {
        get
        {
            foreach (var c in _player)
                if (c.Move < 1 || (c.FaceUp && c.Flip < 1))
                    return true;
            foreach (var c in _banker)
                if (c.Move < 1 || (c.FaceUp && c.Flip < 1))
                    return true;
            return false;
        }
    }

    private void AnimateCards()
    {
        AnimateHand(_player);
        AnimateHand(_banker);
    }

    private void AnimateHand(List<Card> hand)
    {
            foreach (var c in hand)
            {
                if (c.Move < 1)
                    c.Move = MathF.Min(1, c.Move + Dt * 3.6f);
                else if (c.FaceUp && c.Flip < 1)
                {
                    float before = c.Flip;
                    c.Flip = MathF.Min(1, c.Flip + Dt * 4.5f);
                    if (before < 0.5f && c.Flip >= 0.5f)
                        Sound.Play(Sfx.Card, 0.5f, 0.35f);
                }
            }
    }

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        AnimateCards();
        _potPulse = MathF.Max(0, _potPulse - Dt * 2);

        switch (_state)
        {
            case State.Betting:
                UpdateBetting();
                break;
            case State.Dealing:
                UpdateDealing();
                break;
            case State.Player:
                UpdatePlayer();
                break;
            case State.Banker:
                UpdateBanker();
                break;
            case State.Result:
                UpdateResult();
                break;
        }
        Score = Math.Max(0, _chips);
    }

    private static RectF StakeRect(int i) => new(150 + i * 66, 304, 58, 38);
    private static readonly RectF DealRect = new(424, 304, 110, 38);
    private static readonly RectF BuyRect = new(170, 304, 96, 38);
    private static readonly RectF TwistRect = new(276, 304, 96, 38);
    private static readonly RectF StickRect = new(382, 304, 96, 38);
    private static readonly RectF NextRect = new(255, 304, 150, 38);

    private void UpdateBetting()
    {
        for (int i = 0; i < Stakes.Length; i++)
        {
            bool ok = Stakes[i] <= _chips;
            var col = ChipColour(Stakes[i]);
            if (Ui.Button(StakeRect(i), Stakes[i].ToString(), Keys.D1 + i, ok, Pal.Darken(col, 0.45f), _lastStake == Stakes[i]))
            {
                _lastStake = Stakes[i];
                Sound.Play(Sfx.Coin, i * 0.2f, 0.6f);
            }
        }
        if (_lastStake > _chips)
            _lastStake = Stakes[0];
        bool deal = Ui.Button(DealRect, "DEAL", Keys.D, _lastStake <= _chips, Pal.Darken(Pal.Green, 0.4f)) || In.EnterPressed || In.FirePressed;
        if (!deal)
            return;
        _stake = _baseStake = _lastStake;
        _chips -= _stake;
        _potPulse = 1;
        Sound.Play(Sfx.Coin, 0, 0.8f);
        _state = State.Dealing;
        _timer = 0;
        _message = "";
        _dealQueue.Clear();
        _dealQueue.Add((true, true, false));
        _dealQueue.Add((false, true, false));
        _dealQueue.Add((true, true, false));
        _dealQueue.Add((false, false, false));
    }

    private void UpdateDealing()
    {
        _timer -= Dt;
        if (_timer > 0)
            return;
        if (_dealQueue.Count > 0)
        {
            var d = _dealQueue[0];
            _dealQueue.RemoveAt(0);
            Deal(d.Player, d.FaceUp, d.Bought);
            _timer = 0.28f;
            return;
        }
        if (Animating)
            return;

        // Banker's pontoon is shown at once and wins.
        if (IsPontoon(_banker))
        {
            RevealBanker();
            Finish(false, "BANKER'S PONTOON!", "The banker wins the hand.");
            return;
        }
        if (IsPontoon(_player))
        {
            _message = "PONTOON!";
            _messageColour = Pal.Gold;
            Sound.Play(Sfx.Bonus);
            Fx.Burst(330, 224, Pal.Gold, 30, 140, 0.8f, 2.5f);
            StartBanker();
            return;
        }
        _state = State.Player;
        _message = "";
    }

    private void UpdatePlayer()
    {
        int total = Total(_player);
        bool canBuy = !_twisted && _player.Count < 5 && _chips >= _baseStake;
        bool canTwist = _player.Count < 5;
        bool canStick = total >= 16;
        bool buy = Ui.Button(BuyRect, "BUY " + _baseStake, Keys.B, canBuy, Pal.Darken(Pal.Gold, 0.5f));
        bool twist = Ui.Button(TwistRect, "TWIST", Keys.T, canTwist, Pal.Darken(Pal.Sky, 0.5f));
        bool stick = Ui.Button(StickRect, "STICK", Keys.S, canStick, Pal.Darken(Pal.Red, 0.45f));
        if (Animating)
            return;
        if (_checkPending)
        {
            _checkPending = false;
            CheckPlayerHand();
            return;
        }

        if (buy && canBuy)
        {
            _chips -= _baseStake;
            _stake += _baseStake;
            _potPulse = 1;
            Sound.Play(Sfx.Coin, 0.3f, 0.7f);
            Deal(true, true, true);
        }
        else if (twist && canTwist)
        {
            _twisted = true;
            Deal(true, true);
        }
        else if (stick && canStick)
        {
            Sound.Play(Sfx.Select);
            StartBanker();
            return;
        }
        else
        {
            return;
        }
        _checkPending = true;
    }

    private void CheckPlayerHand()
    {
        int t = Total(_player);
        if (t > 21)
        {
            Sound.Play(Sfx.Thud);
            Finish(false, "BUST!", "Over 21 with " + t + ".");
        }
        else if (_player.Count == 5)
        {
            _message = "FIVE CARD TRICK!";
            _messageColour = Pal.Gold;
            Sound.Play(Sfx.Bonus);
            Fx.Burst(330, 224, Pal.Gold, 30, 140, 0.8f, 2.5f);
            StartBanker();
        }
    }

    private void StartBanker()
    {
        _state = State.Banker;
        _timer = 0.6f;
        RevealBanker();
    }

    private void RevealBanker()
    {
        foreach (var c in _banker)
            c.FaceUp = true;
    }

    private void UpdateBanker()
    {
        if (Animating)
            return;
        _timer -= Dt;
        if (_timer > 0)
            return;
        int total = Total(_banker);
        int playerRank = Rank(_player);
        // The banker twists below 17, and keeps going against a five card trick or pontoon.
        bool needMore = playerRank == 40 ? false : playerRank == 30 || total < 17;
        if (total < 21 && _banker.Count < 5 && needMore)
        {
            Deal(false, true);
            _timer = 0.55f;
            return;
        }

        int bankerRank = Rank(_banker);
        if (bankerRank == 0)
            Finish(true, "BANKER BUSTS!", "Banker went over with " + total + ".");
        else if (playerRank > bankerRank)
            Finish(true, playerRank == 40 ? "PONTOON PAYS DOUBLE!" : playerRank == 30 ? "FIVE CARD TRICK WINS!" : "YOU WIN!",
                $"{Describe(_player)} beats {Describe(_banker)}.");
        else
            Finish(false, playerRank == bankerRank ? "BANKER WINS TIES" : "BANKER WINS",
                playerRank == bankerRank ? $"Both have {Describe(_player)}." : $"{Describe(_banker)} beats {Describe(_player)}.");
    }

    private static string Describe(List<Card> hand)
    {
        int r = Rank(hand);
        return r == 40 ? "Pontoon" : r == 30 ? "Five cards" : Total(hand).ToString();
    }

    private void Finish(bool won, string message, string sub)
    {
        _state = State.Result;
        _timer = 0;
        _message = message;
        _subMessage = sub;
        if (won)
        {
            int rank = Rank(_player);
            int pay = rank >= 30 ? _stake * 3 : _stake * 2;
            _chips += pay;
            _messageColour = Pal.Lime;
            Fx.Float("+" + (pay - _stake), 120, 170, Pal.Lime, 2f);
            Fx.Burst(120, 180, Pal.Gold, 24, 120, 0.7f, 2.2f, 60);
            Sound.Play(rank >= 30 ? Sfx.Win : Sfx.Correct);
        }
        else
        {
            _messageColour = Pal.Red;
            Fx.Float("-" + _stake, 120, 170, Pal.Red, 2f);
            Sound.Play(Sfx.Wrong);
        }
        _stake = 0;
        Score = Math.Max(0, _chips);
    }

    private void UpdateResult()
    {
        _timer += Dt;
        bool last = _hand >= Hands || _chips < MinStake;
        string label = last ? "FINISH" : "NEXT HAND";
        bool next = Ui.Button(NextRect, label, Keys.N, _timer > 0.6f, Pal.Darken(Pal.Green, 0.4f)) ||
                    (_timer > 0.6f && (In.EnterPressed || In.FirePressed));
        if (!next)
            return;
        if (last)
        {
            if (_chips < MinStake)
                EndGame(false, "The banker cleaned you out!");
            else
                EndGame(_chips > StartChips, $"You finished with {_chips} chips.");
            return;
        }
        StartBetting();
    }

    // ------------------------------------------------------------------ drawing

    private static Color ChipColour(int value) => value switch
    {
        >= 50 => new Color(40, 40, 50),
        >= 25 => new Color(30, 150, 70),
        >= 10 => new Color(40, 90, 210),
        _ => new Color(210, 40, 50),
    };

    public override void Draw(Gfx g)
    {
        DrawTable(g, Screen.Bounds);

        // The shoe.
        g.RoundRect(DeckPos.X - 32, DeckPos.Y - 44, 64, 88, 8, new Color(60, 30, 20));
        for (int i = 0; i < Math.Min(6, _deck.Count / 6 + 1); i++)
            DrawBack(g, DeckPos.X - i * 0.8f, DeckPos.Y - i * 1.2f, 1f, 1f);
        g.Text(_deck.Count.ToString(), DeckPos.X, DeckPos.Y + 46, 1f, Pal.White * 0.6f, Align.Center);

        // Hand labels.
        g.TextShadow("BANKER", 60, 54, 1.5f, Pal.Ice, Align.Center);
        if (_banker.Count > 0)
        {
            bool allUp = true;
            foreach (var c in _banker)
                allUp &= c.FaceUp && c.Flip >= 1;
            string bt = allUp ? TotalText(_banker) : "?";
            g.TextShadow(bt, 60, 70, 2.5f, Total(_banker) > 21 && allUp ? Pal.Red : Pal.White, Align.Center);
        }
        g.TextShadow("YOU", 60, 196, 1.5f, Pal.Ice, Align.Center);
        if (_player.Count > 0)
            g.TextShadow(TotalText(_player), 60, 212, 2.5f, Total(_player) > 21 ? Pal.Red : Pal.Yellow, Align.Center);

        foreach (var c in _banker)
            DrawCardAt(g, c);
        foreach (var c in _player)
            DrawCardAt(g, c);

        // Pot and bank.
        DrawChipPile(g, 120, 168, _stake, _potPulse);
        if (_stake > 0)
            g.TextShadow("STAKE " + _stake, 120, 178, 1.25f, Pal.Gold, Align.Center);
        DrawChipPile(g, 586, 268, _chips, 0);
        g.TextShadow("CHIPS", 586, 278, 1.25f, Pal.Ice, Align.Center);
        g.TextShadow(_chips.ToString(), 586, 290, 1.5f, Pal.Gold, Align.Center);

        // Messages between the hands.
        if (_message.Length > 0)
        {
            float w = MathF.Max(Gfx.TextWidth(_message, 2f), Gfx.TextWidth(_subMessage, 1.25f)) + 24;
            g.RoundRect(330 - w / 2, 136, w, _subMessage.Length > 0 ? 40 : 26, 8, Color.Black * 0.55f);
            g.TextShadow(_message, 330, 141, 2f, _messageColour, Align.Center);
            if (_subMessage.Length > 0)
                g.Text(_subMessage, 330, 162, 1.25f, Pal.White, Align.Center);
        }
        if (_state == State.Player && !Animating)
        {
            int t = Total(_player);
            string hint = t < 16 ? "UNDER 16: YOU MUST TWIST OR BUY" : _player.Count == 4 ? "ONE MORE FOR A FIVE CARD TRICK" : "";
            if (hint.Length > 0)
                g.Text(hint, 330, 286, 1f, Pal.White * 0.7f, Align.Center);
        }
    }

    private static string TotalText(List<Card> hand)
    {
        if (IsPontoon(hand))
            return "21";
        int t = Total(hand);
        return Soft(hand) && t < 21 ? $"{t - 10}/{t}" : t.ToString();
    }

    private static void DrawTable(Gfx g, RectF r)
    {
        g.GradientV(r.X, r.Y, r.W, r.H, new Color(10, 70, 40), new Color(6, 40, 24));
        g.Glow(r.CenterX, r.CenterY, r.W * 0.55f, new Color(40, 140, 80), 0.35f);
        // Printed table markings.
        float s = r.H / 360f;
        g.Arc(r.CenterX, r.Y - 120 * s, 330 * s, 1.5f * s, 0.35f, MathF.PI - 0.35f, Pal.Gold * 0.35f, 64);
        g.Arc(r.CenterX, r.Y - 120 * s, 338 * s, 1f * s, 0.35f, MathF.PI - 0.35f, Pal.Gold * 0.2f, 64);
        Backdrops.Vignette(g, r, 0.55f);
    }

    private void DrawCardAt(Gfx g, Card c)
    {
        float m = MathF2.EaseInOut(c.Move);
        var p = Vector2.Lerp(c.From, c.To, m);
        float lift = MathF.Sin(m * MathF.PI) * 10;
        // Flip: the card narrows to its edge, then widens showing the face.
        float flip = c.FaceUp ? c.Flip : 0;
        float sx = MathF.Abs(MathF.Cos(flip * MathF.PI));
        bool face = flip >= 0.5f;
        g.RoundRect(p.X - CardW / 2 * sx + 3, p.Y - CardH / 2 + 4 + lift * 0.3f, CardW * sx, CardH, 5, Color.Black * 0.35f);
        if (face)
            DrawFace(g, p.X, p.Y - lift, sx, c.Rank, c.Suit, 1f);
        else
            DrawBack(g, p.X, p.Y - lift, sx, 1f);
        if (face && c.Bought && sx > 0.9f)
        {
            g.Circle(p.X + CardW / 2 - 4, p.Y - lift - CardH / 2 + 4, 7, Pal.Gold);
            g.Text("B", p.X + CardW / 2 - 4, p.Y - lift - CardH / 2 + 0.5f, 1f, Pal.Black, Align.Center);
        }
    }

    private static void DrawBack(Gfx g, float cx, float cy, float sx, float s)
    {
        float w = CardW * s * sx, h = CardH * s;
        g.RoundRect(cx - w / 2, cy - h / 2, w, h, 5 * s, Pal.White);
        g.RoundRect(cx - w / 2 + 2.5f * s * sx, cy - h / 2 + 2.5f * s, w - 5 * s * sx, h - 5 * s, 4 * s, new Color(120, 20, 40));
        // Lattice pattern.
        float iw = w - 9 * s * sx, ih = h - 9 * s;
        for (int i = 0; i < 6; i++)
        {
            float y = cy - ih / 2 + ih * (i + 0.5f) / 6;
            for (int j = 0; j < 4; j++)
            {
                float x = cx - iw / 2 + iw * (j + 0.5f) / 4;
                float d = 3.2f * s;
                g.Triangle(new Vector2(x, y - d), new Vector2(x + d * sx, y), new Vector2(x, y + d), new Color(170, 50, 70));
                g.Triangle(new Vector2(x, y - d), new Vector2(x - d * sx, y), new Vector2(x, y + d), new Color(170, 50, 70));
            }
        }
        g.RectOutline(cx - iw / 2, cy - ih / 2, iw, ih, 1 * s, Pal.Gold * 0.7f);
    }

    private static readonly Dictionary<char, Color> CrownColours = new() { ['g'] = Pal.Gold, ['r'] = Pal.Red, ['w'] = Pal.White };
    private static readonly PixelArt Crown = new(["g..g..g", "gg.g.gg", "ggggggg", "grgwgrg", "ggggggg"], CrownColours);

    private static readonly float[][] Pips =
    [
        [],
        [0, 0],
        [0, -1, 0, 1],
        [0, -1, 0, 0, 0, 1],
        [-1, -1, 1, -1, -1, 1, 1, 1],
        [-1, -1, 1, -1, -1, 1, 1, 1, 0, 0],
        [-1, -1, 1, -1, -1, 0, 1, 0, -1, 1, 1, 1],
        [-1, -1, 1, -1, -1, 0, 1, 0, -1, 1, 1, 1, 0, -0.5f],
        [-1, -1, 1, -1, -1, 0, 1, 0, -1, 1, 1, 1, 0, -0.5f, 0, 0.5f],
        [-1, -1, 1, -1, -1, -0.33f, 1, -0.33f, -1, 0.33f, 1, 0.33f, -1, 1, 1, 1, 0, 0],
        [-1, -1, 1, -1, -1, -0.33f, 1, -0.33f, -1, 0.33f, 1, 0.33f, -1, 1, 1, 1, 0, -0.66f, 0, 0.66f],
    ];

    private static string RankName(int rank) => rank switch
    {
        1 => "A",
        11 => "J",
        12 => "Q",
        13 => "K",
        _ => rank.ToString(),
    };

    private static void DrawFace(Gfx g, float cx, float cy, float sx, int rank, int suit, float s)
    {
        float w = CardW * s * sx, h = CardH * s;
        g.RoundRect(cx - w / 2, cy - h / 2, w, h, 5 * s, new Color(250, 248, 240));
        var col = suit < 2 ? new Color(210, 30, 40) : new Color(25, 25, 35);
        if (sx < 0.85f)
        {
            DrawSuit(g, cx, cy, 16 * s * sx, suit, col);
            return;
        }
        float left = cx - w / 2, top = cy - h / 2;
        string name = RankName(rank);
        float ts = name.Length > 1 ? 1.25f : 1.5f;
        g.Text(name, left + 7 * s, top + 4 * s, ts * s, col, Align.Center);
        DrawSuit(g, left + 7 * s, top + 20 * s, 7 * s, suit, col);
        g.Text(name, cx + w / 2 - 7 * s, cy + h / 2 - 15 * s, ts * s, col, Align.Center);
        DrawSuit(g, cx + w / 2 - 7 * s, cy + h / 2 - 20 * s, 7 * s, suit, col);

        if (rank == 1)
        {
            DrawSuit(g, cx, cy, 26 * s, suit, col);
        }
        else if (rank <= 10)
        {
            var pips = Pips[rank];
            for (int i = 0; i < pips.Length; i += 2)
                DrawSuit(g, cx + pips[i] * 9 * s, cy + pips[i + 1] * 22 * s, 10 * s, suit, col);
        }
        else
        {
            // Court card: framed portrait panel.
            var frame = new RectF(cx - 15 * s, cy - 24 * s, 30 * s, 48 * s);
            var tint = suit < 2 ? new Color(250, 220, 210) : new Color(215, 225, 250);
            g.Rect(frame, Pal.Gold);
            g.Rect(frame.Inflate(-1.5f * s, -1.5f * s), tint);
            g.PixelsCentered(Crown, cx, cy - 13 * s, 2.2f * s);
            g.Circle(cx, cy - 1 * s, 6 * s, Pal.Skin);
            g.Rect(cx - 3 * s, cy - 2 * s, 1.5f * s, 1.5f * s, Pal.Black);
            g.Rect(cx + 1.5f * s, cy - 2 * s, 1.5f * s, 1.5f * s, Pal.Black);
            g.Triangle(new Vector2(cx - 11 * s, cy + 22 * s), new Vector2(cx + 11 * s, cy + 22 * s), new Vector2(cx, cy + 5 * s), col);
            g.Text(name, cx, cy + 10 * s, 1.25f * s, Pal.Gold, Align.Center);
        }
    }

    private static void DrawSuit(Gfx g, float x, float y, float size, int suit, Color col)
    {
        float s = size;
        switch (suit)
        {
            case 0: // hearts
                g.Circle(x - s * 0.24f, y - s * 0.12f, s * 0.27f, col);
                g.Circle(x + s * 0.24f, y - s * 0.12f, s * 0.27f, col);
                g.Triangle(new Vector2(x - s * 0.5f, y - s * 0.04f), new Vector2(x + s * 0.5f, y - s * 0.04f), new Vector2(x, y + s * 0.5f), col);
                break;
            case 1: // diamonds
                g.Triangle(new Vector2(x, y - s * 0.5f), new Vector2(x + s * 0.36f, y), new Vector2(x - s * 0.36f, y), col);
                g.Triangle(new Vector2(x, y + s * 0.5f), new Vector2(x + s * 0.36f, y), new Vector2(x - s * 0.36f, y), col);
                break;
            case 2: // clubs
                g.Circle(x, y - s * 0.22f, s * 0.22f, col);
                g.Circle(x - s * 0.23f, y + s * 0.08f, s * 0.22f, col);
                g.Circle(x + s * 0.23f, y + s * 0.08f, s * 0.22f, col);
                g.Triangle(new Vector2(x, y), new Vector2(x - s * 0.2f, y + s * 0.5f), new Vector2(x + s * 0.2f, y + s * 0.5f), col);
                break;
            default: // spades
                g.Circle(x - s * 0.24f, y + s * 0.1f, s * 0.26f, col);
                g.Circle(x + s * 0.24f, y + s * 0.1f, s * 0.26f, col);
                g.Triangle(new Vector2(x - s * 0.5f, y + s * 0.04f), new Vector2(x + s * 0.5f, y + s * 0.04f), new Vector2(x, y - s * 0.5f), col);
                g.Triangle(new Vector2(x, y + s * 0.1f), new Vector2(x - s * 0.2f, y + s * 0.5f), new Vector2(x + s * 0.2f, y + s * 0.5f), col);
                break;
        }
    }

    private static void DrawChip(Gfx g, float x, float y, Color col, float s = 1)
    {
        g.Ellipse(x, y + 1.5f * s, 11 * s, 5 * s, Pal.Darken(col, 0.5f));
        g.Ellipse(x, y, 11 * s, 5 * s, col);
        for (int k = 0; k < 6; k++)
        {
            float a = MathF2.Tau * k / 6;
            g.Ellipse(x + MathF.Cos(a) * 8.5f * s, y + MathF.Sin(a) * 3.8f * s, 1.6f * s, 0.9f * s, Pal.White * 0.85f);
        }
        g.Ellipse(x, y, 6 * s, 2.6f * s, Pal.Lighten(col, 0.25f));
    }

    private static readonly int[] ChipValues = [50, 25, 10, 5];

    private static void DrawChipPile(Gfx g, float x, float baseY, int amount, float pulse)
    {
        if (amount <= 0)
            return;
        // Break the amount into chip stacks, biggest value on the left.
        int stacks = 0, rest = amount;
        foreach (int v in ChipValues)
        {
            if (rest / v > 0)
                stacks++;
            rest %= v;
        }
        float sx = x - (stacks - 1) * 12;
        int stack = 0;
        foreach (int v in ChipValues)
        {
            int n = amount / v;
            amount -= n * v;
            if (n == 0)
                continue;
            n = Math.Min(n, 14);
            float px = sx + stack * 24;
            for (int i = 0; i < n; i++)
                DrawChip(g, px, baseY - i * 3.2f - pulse * 4, ChipColour(v));
            stack++;
        }
        if (pulse > 0)
            g.Glow(x - 10, baseY - 10, 40, Pal.Gold, pulse * 0.4f);
    }

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        DrawTable(g, r);
        float s = r.H / 70f * 0.7f;
        // A pontoon: the ace of spades, and the king of hearts flipping over.
        float flip = Backdrops.Mod(time * 0.5f, 2f);
        float f = MathF2.Clamp(flip, 0, 1);
        float sx = MathF.Abs(MathF.Cos(f * MathF.PI));
        float cy = r.CenterY + 2 * s;
        float ax = r.CenterX - 30 * s, kx = r.CenterX + 26 * s;
        g.RoundRect(ax - CardW * s / 2 + 3 * s, cy - CardH * s / 2 + 3 * s, CardW * s, CardH * s, 5 * s, Color.Black * 0.35f);
        DrawFace(g, ax, cy, 1, 1, 3, s);
        g.RoundRect(kx - CardW * s * sx / 2 + 3 * s, cy - CardH * s / 2 + 3 * s, CardW * s * sx, CardH * s, 5 * s, Color.Black * 0.35f);
        if (f >= 0.5f)
            DrawFace(g, kx, cy, sx, 13, 0, s);
        else
            DrawBack(g, kx, cy, sx, s);
        DrawChip(g, r.CenterX + 70 * s, r.Bottom - 12 * s, ChipColour(25), s * 1.2f);
        DrawChip(g, r.CenterX + 70 * s, r.Bottom - 16 * s, ChipColour(10), s * 1.2f);
        DrawChip(g, r.CenterX + 70 * s, r.Bottom - 20 * s, ChipColour(5), s * 1.2f);
        if (f >= 1)
        {
            float a = MathF.Min(1, (flip - 1) * 3);
            g.TextShadow("PONTOON!", r.CenterX, r.Y + 3 * s, 1.8f * s, Pal.Gold * a, Align.Center);
            g.Glow(r.CenterX - 18 * s, cy, 60 * s, Pal.Gold, 0.3f * a);
        }
    }

    // ------------------------------------------------------------------ autopilot

    public override void AutoPlay(Controls c)
    {
        if (_autoClick is RectF rect)
        {
            c.Pointer = rect.Center;
            if (_autoPhase == 0)
            {
                c.PointerPressed = c.PointerDown = true;
                _autoPhase = 1;
            }
            else
            {
                c.PointerReleased = true;
                _autoClick = null;
                _autoPhase = 0;
                _autoWait = 30;
            }
            return;
        }
        if (_autoWait > 0)
        {
            _autoWait--;
            return;
        }
        switch (_state)
        {
            case State.Betting:
                int want = _chips >= 150 ? 25 : _chips >= 30 ? 10 : 5;
                if (_lastStake != want)
                    _autoClick = StakeRect(Array.IndexOf(Stakes, want));
                else
                    _autoClick = DealRect;
                break;
            case State.Player:
                if (Animating)
                    break;
                int t = Total(_player);
                if (!_twisted && _player.Count == 2 && t <= 11 && _chips >= _baseStake && !Soft(_player))
                    _autoClick = BuyRect;
                else if (t < 16 || (t == 16 && Soft(_player)) || (_player.Count == 4 && t <= 15))
                    _autoClick = TwistRect;
                else
                    _autoClick = StickRect;
                break;
            case State.Result:
                if (_timer > 1.2f)
                    _autoClick = NextRect;
                break;
        }
    }
}
