using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Cascade50.Core.Audio;
using Cascade50.Core.Graphics;
using Cascade50.Core.Input;

namespace Cascade50.Core.Games.All;

/// <summary>
/// 14 Hangman: the classic word game, made kinder. Our hero hangs from a bunch of balloons over a
/// duck pond; every wrong letter pops one. Ten words a game; lose three and it is over.
/// </summary>
public sealed class Hangman : MiniGame
{
    public override int Number => 14;
    public override string Title => "Hangman";
    public override Category Category => Category.Brain;
    public override string Tagline => "Guess the word before the balloons pop and he lands in the pond.";
    public override Color Accent => Pal.Pink;
    public override Pad Pad => Pad.None;
    public override bool TextEntry => true;

    public override string[] HowToPlay =>
    [
        "Guess the hidden word one letter at a time. Each wrong letter pops a balloon: lose them all and he drops in the pond.",
        "Ten words a game; lose three and it is over. Solve quickly and keep a streak for bonuses.",
    ];

    public override string[] DesktopControls => ["Type letters, or click the keys."];
    public override string[] TouchControls => ["Tap the letters on the keyboard."];

    private const int WordsPerGame = 10;
    private const int Balloons = 6;
    private const float KeyW = 45, KeyH = 32, KeyGap = 3.5f, KeyTop = 284;

    private static readonly (string Category, string Words)[] Lists =
    [
        ("ANIMALS", "ELEPHANT GIRAFFE KANGAROO PENGUIN DOLPHIN OCTOPUS HEDGEHOG SQUIRREL BADGER OTTER BEAVER TORTOISE CROCODILE " +
                    "ALLIGATOR CHEETAH LEOPARD PANTHER JAGUAR GORILLA CHIMPANZEE BABOON ZEBRA HIPPO RHINO CAMEL LLAMA ALPACA HAMSTER " +
                    "RABBIT FERRET WEASEL TOAD FROG NEWT LIZARD IGUANA PYTHON COBRA FALCON EAGLE OSTRICH FLAMINGO PEACOCK PELICAN " +
                    "PUFFIN SPARROW ROBIN PARROT TOUCAN WALRUS WHALE SHARK JELLYFISH LOBSTER BUTTERFLY BEETLE SPIDER SCORPION KOALA " +
                    "PANDA RACCOON MEERKAT LEMUR SLOTH ARMADILLO"),
        ("COUNTRIES", "FRANCE GERMANY SPAIN PORTUGAL ITALY GREECE NORWAY SWEDEN FINLAND DENMARK ICELAND IRELAND SCOTLAND WALES " +
                      "ENGLAND POLAND AUSTRIA HUNGARY BELGIUM NETHERLANDS SWITZERLAND CROATIA ROMANIA BULGARIA TURKEY EGYPT MOROCCO " +
                      "KENYA NIGERIA GHANA ETHIOPIA TANZANIA CANADA MEXICO BRAZIL ARGENTINA CHILE PERU COLOMBIA VENEZUELA CUBA " +
                      "JAMAICA JAPAN CHINA INDIA PAKISTAN NEPAL THAILAND VIETNAM MALAYSIA INDONESIA PHILIPPINES AUSTRALIA MONGOLIA " +
                      "KOREA UKRAINE ESTONIA LATVIA LITHUANIA"),
        ("FOOD", "SANDWICH CRUMPET SCONE PANCAKE WAFFLE TOAST PORRIDGE MUFFIN BISCUIT CUSTARD TRIFLE PUDDING JELLY BANANA APPLE " +
                 "CHERRY STRAWBERRY RASPBERRY BLUEBERRY PINEAPPLE MANGO PAPAYA COCONUT LEMON ORANGE GRAPEFRUIT CARROT POTATO TOMATO " +
                 "CUCUMBER BROCCOLI CABBAGE SPINACH PARSNIP TURNIP ONION GARLIC MUSHROOM PEPPER SAUSAGE BURGER PIZZA PASTA " +
                 "SPAGHETTI LASAGNE NOODLES RISOTTO CURRY OMELETTE CHEESE YOGHURT BUTTER DOUGHNUT CHOCOLATE TOFFEE POPCORN " +
                 "PRETZEL KEBAB"),
        ("SPACE", "PLANET COMET ASTEROID METEOR GALAXY NEBULA QUASAR PULSAR SUPERNOVA ROCKET SHUTTLE SATELLITE ASTRONAUT " +
                  "COSMONAUT TELESCOPE ORBIT GRAVITY ECLIPSE MERCURY VENUS EARTH MARS JUPITER SATURN URANUS NEPTUNE PLUTO MOON " +
                  "SUNSPOT CRATER LAUNCHPAD COUNTDOWN SPACESUIT CAPSULE STATION METEORITE CONSTELLATION ZODIAC STARDUST WORMHOLE " +
                  "LIGHTYEAR UNIVERSE COSMOS ROVER LANDER PROBE AURORA HORIZON EQUINOX SOLSTICE"),
        ("SPORT", "FOOTBALL CRICKET RUGBY TENNIS BADMINTON SQUASH HOCKEY NETBALL BASKETBALL VOLLEYBALL BASEBALL ROUNDERS GOLF " +
                  "SNOOKER DARTS BOWLS ROWING SAILING CANOEING SWIMMING DIVING CYCLING ARCHERY FENCING BOXING JUDO KARATE " +
                  "WRESTLING SKIING SNOWBOARDING SKATING CURLING BOBSLEIGH MARATHON SPRINT HURDLES JAVELIN DISCUS TRIATHLON " +
                  "GYMNASTICS SURFING CLIMBING HANDBALL POLO LACROSSE REFEREE GOALKEEPER STADIUM TROPHY"),
        ("RETRO COMPUTING", "ORIC CASSETTE JOYSTICK KEYBOARD MONITOR PRINTER MODEM FLOPPY DISKETTE PIXEL SPRITE BASIC ASSEMBLER " +
                            "COMPILER PROGRAM LISTING CURSOR MEMORY KILOBYTE MEGABYTE NIBBLE PROCESSOR SILICON CIRCUIT TRANSISTOR " +
                            "MICROCHIP SOFTWARE HARDWARE ARCADE HIGHSCORE LOADING SYNTHESISER SOUNDCHIP INTERPRETER VARIABLE " +
                            "SUBROUTINE GOSUB POKE PEEK TAPE CARTRIDGE CONSOLE MAINFRAME TERMINAL DEBUGGER BITMAP PALETTE " +
                            "CHARACTER SCANLINE FIRMWARE MOTHERBOARD CHIPTUNE EMULATOR RESISTOR CAPACITOR SOLDER MICRO TELETEXT"),
        ("WEATHER", "THUNDER LIGHTNING RAINBOW DRIZZLE BLIZZARD HAILSTONE SNOWFLAKE HURRICANE TORNADO MONSOON BREEZE SUNSHINE " +
                    "CLOUDBURST HEATWAVE FROST ICICLE PUDDLE UMBRELLA FORECAST SHOWER TYPHOON"),
    ];

    private static readonly List<(string Word, int Cat)> AllWords = BuildWords();

    private static List<(string, int)> BuildWords()
    {
        var list = new List<(string, int)>();
        for (int c = 0; c < Lists.Length; c++)
            foreach (var w in Lists[c].Words.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (w.Length >= 4)
                    list.Add((w, c));
        return list;
    }

    private static readonly Color[] BalloonColours = [Pal.Red, Pal.Yellow, Pal.Lime, Pal.Sky, Pal.Magenta, Pal.Orange];

    private static readonly Dictionary<char, Color> KidColours = new()
    {
        ['h'] = new Color(120, 70, 30), ['s'] = Pal.Skin, ['e'] = Color.Black, ['r'] = Pal.Red, ['b'] = new Color(40, 90, 200),
        ['w'] = Pal.White, ['k'] = new Color(30, 30, 40), ['m'] = new Color(160, 60, 60),
    };

    private static readonly PixelArt KidHappy = new(
    [
        "...hhhhh...",
        "..hhhhhhh..",
        "..sssssss..",
        "..sesssess.",
        "..sssssss..",
        "..ssmmmss..",
        "...sssss...",
        ".s.rrrrr.s.",
        ".srrrwrrrs.",
        "..rrrwrrr..",
        "...rrrrr...",
        "...bbbbb...",
        "...bb.bb...",
        "...bb.bb...",
        "...kk.kk...",
    ], KidColours);

    private static readonly PixelArt KidWorried = new(
    [
        "...hhhhh...",
        "..hhhhhhh..",
        "..sssssss..",
        "..seessee..",
        "..sssssss..",
        "..sssmsss..",
        "...sssss...",
        ".s.rrrrr.s.",
        ".srrrwrrrs.",
        "..rrrwrrr..",
        "...rrrrr...",
        "...bbbbb...",
        "..bb...bb..",
        ".bb.....bb.",
        ".kk.....kk.",
    ], KidColours);

    private const string Frequency = "EAIRTONSLCUDPMHGBFYWKVXZJQ";

    private enum Phase { Playing, Solved, Lost }

    private readonly bool[] _guessed = new bool[26];
    private readonly bool[] _usedWord = new bool[512];
    private readonly float[] _revealAt = new float[32];
    private readonly float[] _popAt = new float[Balloons];
    private string _word = "";
    private int _cat;
    private int _wrong;
    private int _wordIndex;
    private int _streak;
    private Phase _phase;
    private float _phaseTimer, _wordTime;
    private float _fall, _fallVel, _splash;
    private float _shakeWrong;
    private int _apTimer;

    protected override void Start()
    {
        Lives = 3;
        Level = 1;
        _wordIndex = 0;
        _streak = 0;
        Array.Clear(_usedWord);
        NextWord();
    }

    private void NextWord()
    {
        _wordIndex++;
        Level = _wordIndex;
        Status = $"WORD {_wordIndex} OF {WordsPerGame}";
        // Shorter words first, longer (harder) ones later.
        int minLen = _wordIndex <= 3 ? 4 : _wordIndex <= 7 ? 6 : 8;
        int maxLen = _wordIndex <= 3 ? 7 : _wordIndex <= 7 ? 9 : 14;
        int pick = -1;
        for (int tries = 0; tries < 400 && pick < 0; tries++)
        {
            int i = RandInt(0, AllWords.Count);
            int len = AllWords[i].Word.Length;
            if (!_usedWord[i] && len >= minLen && len <= maxLen)
                pick = i;
        }
        if (pick < 0) pick = RandInt(0, AllWords.Count);
        _usedWord[pick] = true;
        (_word, _cat) = AllWords[pick];
        Array.Clear(_guessed);
        for (int i = 0; i < _revealAt.Length; i++) _revealAt[i] = -1;
        for (int i = 0; i < Balloons; i++) _popAt[i] = -1;
        _wrong = 0;
        _phase = Phase.Playing;
        _wordTime = 0;
        _fall = 0;
        _fallVel = 0;
        _splash = 0;
        Sound.Play(Sfx.Card, 0.2f);
    }

    // ------------------------------------------------------------------ update

    protected override void Update()
    {
        if (_shakeWrong > 0) _shakeWrong -= Dt * 3;
        if (_splash > 0) _splash -= Dt;

        // The keyboard is always shown (used keys are disabled).
        char clicked = '\0';
        for (int i = 0; i < 26; i++)
        {
            var rect = KeyRect(i);
            char ch = (char)('A' + i);
            bool used = _guessed[i];
            var col = !used ? Pal.PanelLight : _word.IndexOf(ch) >= 0 ? new Color(60, 210, 100) : new Color(235, 70, 85);
            if (Ui.Button(rect, ch.ToString(), enabled: !used, color: col, textScale: 2f) && clicked == '\0')
                clicked = ch;
        }

        switch (_phase)
        {
            case Phase.Playing:
                _wordTime += Dt;
                foreach (char t in In.Typed)
                    if (t >= 'A' && t <= 'Z')
                    {
                        Guess(t);
                        if (_phase != Phase.Playing) break;
                    }
                if (clicked != '\0' && _phase == Phase.Playing)
                    Guess(clicked);
                break;
            case Phase.Lost:
                // He drops into the pond.
                _fallVel += 500 * Dt;
                float before = _fall;
                _fall = MathF.Min(_fall + _fallVel * Dt, 1000);
                if (KidY() > WaterY && KidY(before) <= WaterY)
                {
                    _splash = 1.2f;
                    Sound.Play(Sfx.Splash);
                    Fx.Burst(KidX(), WaterY, Pal.Sky, 30, 140, 0.8f, 2.5f, 240, false);
                    Fx.Burst(KidX(), WaterY, Pal.White, 14, 100, 0.6f, 2f, 240, false);
                }
                goto case Phase.Solved;
            case Phase.Solved:
                _phaseTimer -= Dt;
                if (_phaseTimer <= 0 || (_phaseTimer < 1.6f && (In.EnterPressed || In.PointerPressed || In.FirePressed)))
                {
                    if (Lives <= 0)
                    {
                        EndGame(false, "Three words lost: splash!");
                        return;
                    }
                    if (_wordIndex >= WordsPerGame)
                    {
                        EndGame(true, "All ten words done!");
                        return;
                    }
                    NextWord();
                }
                break;
        }
    }

    private void Guess(char ch)
    {
        int i = ch - 'A';
        if (_guessed[i]) return;
        _guessed[i] = true;
        int hits = 0;
        for (int k = 0; k < _word.Length; k++)
            if (_word[k] == ch)
            {
                hits++;
                _revealAt[k] = Time + hits * 0.08f;
                var p = TilePos(k);
                Fx.Burst(p.X, p.Y, Pal.Gold, 10, 80, 0.4f, 2);
            }
        if (hits > 0)
        {
            int pts = 10 * hits;
            var p = TilePos(_word.IndexOf(ch));
            AddScore(pts, p.X, p.Y - 26, Pal.Lime);
            Sound.Play(Sfx.Correct, MathF.Min(0.6f, hits * 0.15f), 0.7f);
            if (Solved())
                Win();
        }
        else
        {
            int b = Balloons - 1 - _wrong;
            _popAt[b] = Time;
            var bp = BalloonPos(b, Time);
            Fx.Burst(bp.X, bp.Y, BalloonColours[b], 20, 140, 0.5f, 2.2f);
            Fx.Burst(bp.X, bp.Y, Pal.White, 6, 60, 0.3f, 1.5f);
            _wrong++;
            _shakeWrong = 1;
            Sound.Play(Sfx.Pop, Rand(-0.1f, 0.2f));
            Sound.Play(Sfx.Wrong, 0, 0.35f);
            if (_wrong >= Balloons)
                Lose();
        }
    }

    private bool Solved()
    {
        foreach (char c in _word)
            if (!_guessed[c - 'A']) return false;
        return true;
    }

    private void Win()
    {
        _phase = Phase.Solved;
        _phaseTimer = 2.6f;
        _streak++;
        int balloonsLeft = Balloons - _wrong;
        int speed = Math.Max(0, (int)(45 - _wordTime)) * 4;
        int bonus = 100 + balloonsLeft * 30 + speed + (_streak > 1 ? 50 * (_streak - 1) : 0);
        AddScore(bonus, 450, 196, Pal.Cyan);
        Sound.Play(Sfx.Bonus);
        for (int i = 0; i < 5; i++)
            Fx.Burst(Rand(300, 600), Rand(60, 160), Pal.Rainbow[i], 18, 120, 1f, 2.2f, 120);
    }

    private void Lose()
    {
        _phase = Phase.Lost;
        _phaseTimer = 3.2f;
        _streak = 0;
        Lives--;
        for (int k = 0; k < _word.Length; k++)
            if (_revealAt[k] < 0) _revealAt[k] = Time + 0.5f + k * 0.05f;
        Sound.Play(Sfx.Lose);
    }

    // ------------------------------------------------------------------ layout

    private static RectF KeyRect(int i)
    {
        int row = i / 13, col = i % 13;
        float total = 13 * KeyW + 12 * KeyGap;
        return new RectF(320 - total / 2 + col * (KeyW + KeyGap), KeyTop + row * (KeyH + 4), KeyW, KeyH);
    }

    private const float SceneCX = 128, WaterY = 252;

    private float TileW => MathF.Min(28, 360f / MathF.Max(1, _word.Length));

    private Vector2 TilePos(int k)
    {
        float w = TileW;
        float x0 = 450 - w * _word.Length / 2 + w / 2;
        return new Vector2(x0 + k * w, 150);
    }

    /// <summary>Height of the hero: he sinks as the balloons pop.</summary>
    private float KidY(float? fall = null) => 150 + _wrong * 9 + (fall ?? _fall) + MathF.Sin(Time * 1.6f) * 3;

    private float KidX() => SceneCX + MathF.Sin(Time * 0.7f) * 8;

    private Vector2 BalloonPos(int i, float time)
    {
        float kx = KidX(), ky = KidY();
        float a = -MathF.PI / 2 + (i - (Balloons - 1) / 2f) * 0.32f;
        float len = 62 + (i % 2) * 10;
        var p = new Vector2(kx, ky - 22) + MathF2.FromAngle(a, len);
        p.X += MathF.Sin(time * 1.5f + i) * 3;
        p.Y += MathF.Sin(time * 2.1f + i * 1.3f) * 2;
        return p;
    }

    // ------------------------------------------------------------------ drawing

    public override void Draw(Gfx g)
    {
        float time = Time;
        DrawScene(g, new RectF(0, 0, 640, 360), time, true);

        // Word panel.
        var panel = new RectF(262, 32, 370, 238);
        g.Panel(panel, new Color(20, 24, 56) * 0.88f, new Color(255, 140, 190) * 0.8f, 12);
        string cat = Lists[_cat].Category;
        g.Text("CATEGORY", panel.CenterX, panel.Y + 12, 1f, Pal.LightGrey, Align.Center);
        g.TextShadow(cat, panel.CenterX, panel.Y + 26, 2f, Pal.Gold, Align.Center);

        float w = TileW;
        float shake = _shakeWrong > 0 ? MathF.Sin(_shakeWrong * 40) * 4 * _shakeWrong : 0;
        for (int k = 0; k < _word.Length; k++)
        {
            var p = TilePos(k) + new Vector2(shake, 0);
            bool shown = _revealAt[k] >= 0 && time >= _revealAt[k];
            bool missed = _phase == Phase.Lost && !_guessed[_word[k] - 'A'];
            g.RoundRect(p.X - w / 2 + 2, p.Y - 16, w - 4, 32, 4, shown ? (missed ? new Color(110, 30, 40) : new Color(40, 50, 110)) : new Color(30, 34, 70));
            g.Rect(p.X - w / 2 + 3, p.Y + 18, w - 6, 2, Pal.LightGrey);
            if (shown)
            {
                float k2 = MathF.Min(1, (time - _revealAt[k]) * 6);
                float sc = MathF.Min(3f, w / 7.5f) * (0.6f + 0.4f * MathF2.EaseOut(k2)) + (1 - k2) * 0.6f;
                var col = missed ? Pal.Pink : _phase == Phase.Solved ? Pal.Lerp(Pal.Gold, Pal.White, MathF2.Pulse(time + k * 0.1f, 0.6f)) : Pal.White;
                g.Text(_word[k].ToString(), p.X + 0.5f, p.Y - Gfx.GlyphH * sc / 2, sc, col, Align.Center);
            }
        }

        // Status line.
        float sy = panel.Y + 168;
        if (_phase == Phase.Playing)
        {
            g.Text("BALLOONS", panel.X + 18, sy, 1f, Pal.LightGrey);
            for (int i = 0; i < Balloons; i++)
            {
                bool alive = i < Balloons - _wrong;
                var c = alive ? BalloonColours[i] : Pal.DarkGrey;
                g.Ellipse(panel.X + 24 + i * 16, sy + 22, 5.5f, 7, c);
                if (alive) g.Circle(panel.X + 22 + i * 16, sy + 19, 1.5f, Color.White * 0.7f);
            }
            g.Text("TIME", panel.Right - 18, sy, 1f, Pal.LightGrey, Align.Right);
            g.Text(((int)_wordTime).ToString(), panel.Right - 18, sy + 14, 2f, _wordTime < 45 ? Pal.Cyan : Pal.Grey, Align.Right);
            if (_streak >= 2)
                g.Text("STREAK x" + _streak, panel.CenterX, sy + 18, 1.5f, Pal.Lime, Align.Center);
        }
        else if (_phase == Phase.Solved)
        {
            g.TextShadow(_streak >= 3 ? "ON FIRE!" : "SOLVED!", panel.CenterX, sy + 4, 2.5f, Pal.Lime, Align.Center);
            if (_streak >= 2) g.Text("STREAK x" + _streak, panel.CenterX, sy + 32, 1.5f, Pal.Gold, Align.Center);
        }
        else
        {
            g.TextShadow("SPLASH!", panel.CenterX, sy + 4, 2.5f, Pal.Sky, Align.Center);
            g.Text(Lives > 0 ? Lives + (Lives == 1 ? " MISS LEFT" : " MISSES LEFT") : "NO MISSES LEFT", panel.CenterX, sy + 32, 1.5f, Pal.Pink, Align.Center);
        }
    }

    private void DrawScene(Gfx g, RectF r, float time, bool live)
    {
        float sx = r.W / 640f, sy = r.H / 360f;
        Vector2 P(float x, float y) => new(r.X + x * sx, r.Y + y * sy);
        // Sky.
        g.GradientV(r.X, r.Y, r.W, r.H * 0.75f, new Color(90, 160, 240), new Color(255, 190, 200));
        g.Rect(r.X, r.Y + r.H * 0.75f, r.W, r.H * 0.25f, new Color(255, 190, 200));
        var sun = P(60, 70);
        g.Glow(sun.X, sun.Y, 70 * sx, Pal.Gold, 0.6f);
        g.Circle(sun.X, sun.Y, 18 * sx, new Color(255, 240, 170));
        // Clouds.
        for (int i = 0; i < 4; i++)
        {
            float cx = Backdrops.Mod(i * 190 + time * (8 + i * 3), 760) - 60;
            var c = P(cx, 50 + i * 22 + (i % 2) * 10);
            float s = (0.8f + 0.2f * (i % 3)) * sx;
            g.Circle(c.X, c.Y, 14 * s, Color.White * 0.9f);
            g.Circle(c.X + 14 * s, c.Y + 4 * s, 11 * s, Color.White * 0.9f);
            g.Circle(c.X - 14 * s, c.Y + 5 * s, 10 * s, Color.White * 0.9f);
            g.Rect(c.X - 14 * s, c.Y + 4 * s, 28 * s, 10 * s, Color.White * 0.9f);
        }
        // Hills and the pond.
        Backdrops.Hills(g, r.Y + 232 * sy, 18 * sy, 40, new Color(110, 190, 90), 5, r);
        var pond = P(SceneCX, WaterY + 12);
        g.Ellipse(pond.X, pond.Y, 120 * sx, 26 * sy, new Color(60, 140, 60));
        g.Ellipse(pond.X, pond.Y, 108 * sx, 20 * sy, new Color(40, 120, 200));
        g.Ellipse(pond.X - 10 * sx, pond.Y - 4 * sy, 80 * sx, 9 * sy, new Color(90, 170, 240));
        for (int i = 0; i < 3; i++)
        {
            float k = (time * 0.4f + i / 3f) % 1f;
            g.Ring(pond.X + (i - 1) * 40 * sx, pond.Y + 4 * sy, (4 + k * 18) * sx, 1 * sx, Color.White * (0.4f * (1 - k)), 24);
        }
        // A duck.
        float dx = pond.X + MathF.Sin(time * 0.3f) * 70 * sx, dy = pond.Y + 2 * sy;
        g.Ellipse(dx, dy, 8 * sx, 4.5f * sy, Pal.Yellow);
        g.Circle(dx + 6 * sx, dy - 5 * sy, 3.5f * sx, Pal.Yellow);
        g.Triangle(new Vector2(dx + 9 * sx, dy - 6 * sy), new Vector2(dx + 13 * sx, dy - 5 * sy), new Vector2(dx + 9 * sx, dy - 4 * sy), Pal.Orange);
        g.Circle(dx + 7 * sx, dy - 6 * sy, 0.8f * sx, Color.Black);
        // Reeds.
        for (int i = 0; i < 6; i++)
        {
            var b = P(SceneCX - 112 + i * 6 + (i > 2 ? 200 : 0), WaterY + 14);
            g.Line(b, b + new Vector2(MathF.Sin(time + i) * 2 * sx, -22 * sy), 1.5f * sx, new Color(60, 120, 40));
            g.Ellipse(b.X + MathF.Sin(time + i) * 2 * sx, b.Y - 22 * sy, 1.8f * sx, 4 * sy, new Color(110, 70, 40));
        }

        if (live)
        {
            DrawHero(g, time);
        }
        else
        {
            // Icon: a fixed little scene.
            var kid = P(200, 215);
            for (int i = 0; i < 4; i++)
            {
                var bp = kid + new Vector2((i - 1.5f) * 34 * sx + MathF.Sin(time * 1.5f + i) * 4 * sx, -150 * sy - (i % 2) * 18 * sy);
                g.Line(kid + new Vector2(0, -30 * sy), bp + new Vector2(0, 26 * sx), 1.5f, Color.White * 0.8f);
                DrawBalloon(g, bp, 26 * sx, BalloonColours[i], time + i);
            }
            g.PixelsCentered(KidHappy, kid.X, kid.Y + MathF.Sin(time * 1.6f) * 3 * sy, 5.5f * sx);
        }
    }

    private void DrawHero(Gfx g, float time)
    {
        float kx = KidX(), ky = KidY();
        var hands = new Vector2(kx, ky - 16);
        if (_phase != Phase.Lost || _fall < 1)
            for (int i = 0; i < Balloons; i++)
            {
                if (i >= Balloons - _wrong) continue;
                var bp = BalloonPos(i, time);
                g.Line(hands, bp + new Vector2(0, 11), 1, Color.White * 0.85f);
                DrawBalloon(g, bp, 11, BalloonColours[i], time + i);
            }
        if (KidY() < WaterY + 6)
        {
            bool worried = _wrong >= 3 || _phase == Phase.Lost;
            g.PixelsCentered(worried ? KidWorried : KidHappy, kx, ky, 2.4f, false);
        }
        else
        {
            // Bobbing in the pond with a rubber ring.
            float by = WaterY + 4 + MathF.Sin(time * 3) * 1.5f;
            g.Ellipse(kx, by + 6, 15, 5, new Color(255, 120, 60));
            g.Ellipse(kx, by + 6, 8, 2.5f, new Color(40, 120, 200));
            g.Circle(kx, by - 6, 8, Pal.Skin);
            g.Ellipse(kx, by - 11, 8, 4, new Color(120, 70, 30));
            g.Circle(kx - 3, by - 6, 1, Color.Black);
            g.Circle(kx + 3, by - 6, 1, Color.Black);
            g.Ellipse(kx, by - 2, 2, 1.5f, new Color(160, 60, 60));
        }
        if (_phase == Phase.Solved)
        {
            g.Glow(kx, ky - 50, 60, Pal.Gold, 0.3f * MathF2.Pulse(time, 0.8f));
        }
    }

    private static void DrawBalloon(Gfx g, Vector2 p, float r, Color c, float t)
    {
        g.Ellipse(p.X, p.Y, r * 0.85f, r, Pal.Darken(c, 0.15f));
        g.Ellipse(p.X - r * 0.1f, p.Y - r * 0.1f, r * 0.7f, r * 0.85f, c);
        g.Ellipse(p.X - r * 0.35f, p.Y - r * 0.4f, r * 0.18f, r * 0.28f, Color.White * 0.75f);
        g.Triangle(p + new Vector2(0, r * 0.9f), p + new Vector2(-r * 0.2f, r * 1.15f), p + new Vector2(r * 0.2f, r * 1.15f), Pal.Darken(c, 0.2f));
    }

    public override void DrawIcon(Gfx g, RectF r, float time)
    {
        DrawScene(g, r, time, false);
        float s = r.H / 70f;
        // A word with blanks.
        const string word = "ORIC";
        float tw = 11 * s;
        float x0 = r.X + r.W * 0.56f;
        float y = r.Y + r.H * 0.6f;
        g.RoundRect(x0 - 4 * s, y - 4 * s, tw * word.Length + 8 * s, 20 * s, 3 * s, new Color(20, 24, 56) * 0.85f);
        bool showI = (int)(time * 0.8f) % 2 == 1;
        for (int i = 0; i < word.Length; i++)
        {
            float x = x0 + i * tw;
            g.Rect(x + 1 * s, y + 12 * s, tw - 2 * s, 1.2f * s, Pal.White);
            if (i != 2 || showI)
                g.Text(word[i].ToString(), x + tw / 2, y + 1 * s, 1.4f * s, Pal.Gold, Align.Center);
        }
    }

    public override void AutoPlay(Controls c)
    {
        if (_phase != Phase.Playing)
        {
            if (_phaseTimer < 1.2f && Tick % 20 == 0)
                c.EnterPressed = true;
            return;
        }
        if (++_apTimer < 36)
            return;
        _apTimer = 0;
        // A proper solver: count letters across every word in this category that fits the pattern.
        Span<int> counts = stackalloc int[26];
        int matches = 0;
        foreach (var (w, cat) in AllWords)
        {
            if (cat != _cat || w.Length != _word.Length) continue;
            bool ok = true;
            for (int k = 0; k < w.Length && ok; k++)
            {
                bool known = _guessed[_word[k] - 'A'];
                if (known && w[k] != _word[k]) ok = false;
                if (!known && _guessed[w[k] - 'A']) ok = false;
            }
            if (!ok) continue;
            matches++;
            for (int k = 0; k < w.Length; k++)
                if (!_guessed[w[k] - 'A']) counts[w[k] - 'A']++;
        }
        int best = -1;
        if (matches > 0)
            for (int i = 0; i < 26; i++)
                if (counts[i] > 0 && (best < 0 || counts[i] > counts[best])) best = i;
        if (best < 0)
            foreach (char f in Frequency)
                if (!_guessed[f - 'A']) { best = f - 'A'; break; }
        // Make the odd human mistake so the balloons get a workout.
        if (Chance(0.25f))
            foreach (char f in Frequency)
                if (!_guessed[f - 'A'] && _word.IndexOf(f) < 0 && Chance(0.5f)) { best = f - 'A'; break; }
        if (best >= 0)
            c.Typed.Add((char)('A' + best));
    }
}
