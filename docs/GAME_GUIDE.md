# Writing a Cascade 50 game

Each of the fifty games is one file, `Cascade50.Core/Games/All/<Name>.cs`, holding one `sealed class <Name> : MiniGame`
(namespace `Cascade50.Core.Games.All`). `Attacker.cs` is the reference implementation: read it first and match its quality.

The originals were short, text-only Oric BASIC listings. These remakes keep each game's title and idea but are proper
modern-retro arcade games: smooth vector + pixel-art graphics, glows and particles, synthesised sound, difficulty that
ramps up, and a real reason to try again.

## The contract (`Games/MiniGame.cs`)

| Member | Notes |
|---|---|
| `Number`, `Title` | Fixed by the catalog (`Games/GameCatalog.cs`); keep the stub's values. |
| `Category` | `Arcade`, `Shooter`, `Skill`, `Puzzle` or `Brain` (cards, words, numbers, strategy). |
| `Tagline` | One sentence, at most 80 characters. Used on the menu, info screen and in store copy. |
| `HowToPlay` | 2-4 short paragraphs: the goal, the rules, scoring, a tip. |
| `DesktopControls` / `TouchControls` | One line per control. The first line is shown on the GET READY banner. |
| `Pad` | Touch controls the game needs: `Pad.Horizontal`, `Vertical`, `Stick` (both), `Fire`, `Alt`, or `Pad.None` for games played by tapping / dragging the playfield. |
| `FireLabel`, `AltLabel` | Text on the touch buttons (e.g. "JUMP", "THRUST"). Keep to ~5 characters. |
| `TextEntry` | `true` for word games: letter keys type instead of steering. |
| `Accent` | Theme colour for the tile, borders and HUD title. |
| `Start()` | Reset everything. Set `Lives` (or leave -1 to hide), `Level`. |
| `Update()` | One tick at 60 Hz (`Dt` = 1/60 s). Read `In`, call `Ui.Button(...)`. |
| `Draw(Gfx g)` | Draw the 640x360 playfield. **The top 22 px (`Screen.HudHeight`) are covered by the score bar**: play in `Screen.Play` (y 22..360). |
| `DrawIcon(Gfx g, RectF r, float time)` | A small animated picture of the game for the menu tile (about 140x70) and info screen (288x162). Scale everything from `r`; it is clipped. Make it look great: it is the shop window. |
| `AutoPlay(Controls c)` | Drives the game for screenshots and soak tests: set `c` (already cleared) like a decent player. Use `c.SetDirections(x, y)`, `c.Fire`, `c.FirePressed`, `c.Pointer` + `c.PointerPressed` / `PointerDown` / `PointerReleased`, `c.Typed.Add('A')`, `c.EnterPressed`. It should play well enough that a screenshot after ~7 s shows the game in full swing. For `Ui` buttons, aim the pointer at the button and press on one tick, release on the next. |

Helpers on `MiniGame`: `Score`, `AddScore(points, x, y)` (floats "+n"), `Lives`, `LoseLife()` (ends the game at 0),
`Level`, `Status` (overrides the HUD centre text, e.g. "ROUND 3 OF 10"), `EndGame(won, message)`, `Time`, `Tick`,
`Rng`, `Rand(a, b)`, `RandInt(a, b)`, `Chance(p)`, `Pick(...)`, `IsTouch`, `Fx`, `Sound`.

The framework handles pausing (P / Esc / pause button), the GET READY banner, game over, best scores, PLAY AGAIN and
the on-screen touch controls. A game only ever calls `EndGame` when the game is finished. Score must never be negative.
Every game must end eventually (lives, timer, rounds...), and should get harder as it goes.

## Input (`Input/Controls.cs`)

`In.Left/Right/Up/Down` (held), `In.LeftPressed...` (this tick), `In.AxisX/AxisY` (-1..1, analog on sticks),
`In.Fire/FirePressed/FireReleased`, `In.Alt/AltPressed`, pointer: `In.Pointer` (virtual px), `In.PointerDown`,
`In.PointerPressed`, `In.PointerReleased`, `In.PointerStart`, `In.PointerMoved`, `In.HasHover` (desktop mouse),
`In.Typed` (chars A-Z, 0-9, '-'), `In.EnterPressed`, `In.BackspacePressed`, `In.KeyPressed(Keys.X)`.
Desktop keys: arrows/WASD, SPACE/Z/Enter = fire, X/Shift/right-click = alt. Don't use P or Esc (pause).

Design every game so it works on **both** keyboard and touch. Pointer games (`Pad.None`) also work with the mouse;
if a pointer game can be played from the keyboard too, do it (e.g. move a cursor with the arrows, fire with SPACE).

## UI buttons (`UI/Ui.cs`)

`if (Ui.Button(new RectF(x, y, w, h), "TWIST", Keys.T)) ...` in `Update()`, every tick the button should exist.
Returns true on the tick it is clicked / tapped or its key pressed; the framework draws it. Minimum touch size 44x30;
labels at scale 1.5 (default). Optional `color`, `selected`, `enabled`, `textScale`, `id`.

## Drawing (`Graphics/Gfx.cs`, `Pal.cs`, `Fx.cs`, `Backdrops.cs`)

Virtual pixels, floats, call order = draw order. Shapes: `Rect`, `GradientV/H`, `RectOutline`, `RoundRect`, `Panel`,
`Circle`, `Ellipse`, `Ring`, `Pie`, `Arc`, `Glow` (additive soft light: use it a lot, it's what makes things look
modern), `Line`, `GlowLine` (neon), `Path`, `Triangle`, `Polygon` (convex), `Shape(points, pos, angle, scale, colour)`
(rotated convex shape in local coords), `ShapeOutline(..., glow: true)`, `RotatedRect`.
Text (the Oric ROM font, 6x8 per glyph at scale 1): `Text(s, x, y, scale, colour, Align)`, `TextShadow`, `TextFit`,
`TextWrapped`, `Gfx.TextWidth`. Use scale >= 1.5 for anything the player must read (phones), 1.0 only for small labels.
Pixel art: build `static readonly PixelArt` once from string rows + a `Dictionary<char, Color>`; draw with
`g.Pixels(art, x, y, pixelSize, flipX)` / `PixelsCentered`.

Colours are **premultiplied**: translucent = `colour * 0.5f`. Never `new Color(r, g, b, a)` with a < 255.
`Pal` has the Oric eight (`Red, Green, Yellow, Blue, Magenta, Cyan, White, Black`) plus `Orange, Pink, Purple, Lime,
Teal, Sky, Navy, Night, Brown, Sand, Grass, Forest, Grey, LightGrey, DarkGrey, Gold, Silver, Ice, Water, DeepWater,
Skin, Panel, PanelLight, Accent`, `Pal.Hsv`, `Pal.Lerp`, `Pal.Lighten/Darken`, `Pal.Rainbow`, `Pal.Cycle(t)`.

`Fx` (drawn on top of the game automatically): `Fx.Burst(x, y, colour, count, speed, life, size, gravity)`,
`Fx.Explode(x, y, power)`, `Fx.Spark(x, y, vx, vy, colour, life, size)`, `Fx.Float(text, x, y, colour)`,
`Fx.Shake(amount, seconds)`.

`Backdrops`: `Space(g, time, scrollSpeed, seed, Screen.Bounds)`, `StarsDown`, `Sky`, `Hills`, `Grid`, `Vignette`.
`MathF2`: `Clamp, Lerp, Approach, FromAngle, Angle, WrapAngle, Circles, Pulse, EaseOut, EaseInOut, Tau`.

Do not allocate per frame in hot paths (no LINQ in Update/Draw, reuse lists).

## Sound (`Audio/SoundBank.cs`)

`Sound.Play(Sfx.X, pitch -1..1, volume 0..1)`. Effects: `Click, Select, Back, Start, Shoot, Laser, Zap, Explode,
BigExplode, Hit, Hurt, Die, Jump, Land, Bounce, Coin, Pickup, PowerUp, Bonus, LevelUp, Win, Lose, GameOver, Correct,
Wrong, Tick, Beep, Card, Shuffle, Splash, Crack, Whoosh, Thud, Pop, Warp, Alarm, Bell, Cannon, Step, Fuse`.
Loops: call `Sound.Loop(LoopSfx.Engine|Thrust|Wind|Hum, on, pitch, volume)` **every tick** while it should sound; it
stops by itself when no longer called. Vary pitch for repeated sounds. `EndGame` plays the win / game-over jingle.

## Checking your work

```sh
dotnet build Cascade50.Core
dotnet test Cascade50.Tests --filter "FullyQualifiedName~GameTests"   # soak tests: autoplay + random input, both input modes
dotnet run --project Cascade50.DesktopGL -- --capture /tmp/shots --size 1280x720 --only g05    # screenshot after autoplay
dotnet run --project Cascade50.DesktopGL -- --capture /tmp/shots --size 2868x1320 --mobile --only g05
dotnet run --project Cascade50.DesktopGL -- --capture /tmp/shots --size 1280x720 --only i05     # info page: big icon + instructions
```

`--only` takes comma-separated prefixes (`g05,g06,i05`). Shots land in `/tmp/shots/<W>x<H>/`. A shot of `gNN` is taken
after 7 s of `AutoPlay` (implement `Capture.ICaptureHints.CaptureTicks` to change it). The instructions panel scrolls,
but aim for it to fit without scrolling at 1280x720.

Then look at the PNGs. A game is done when it builds without warnings, passes the tests, looks good in its
screenshot and icon, and is fun.
