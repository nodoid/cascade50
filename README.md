# Cascade 50

**Fifty Oric-1 classics, reborn.** A MonoGame remake of Cascade Games' 1983 *Cassette 50* tape for the Oric-1, for
Android, iOS, macOS and Windows.

The originals were short, text-only BASIC listings. Every game here keeps its title and idea but is rebuilt from scratch
as a proper modern-retro game: vector and pixel-art graphics with glows and particles, synthesised chiptune sound,
difficulty that ramps up, best scores, and controls for keyboard, mouse, game pad and touch.

By PFJ, based on the Cascade Games originals. Copyright © 2026 Paul F Johnson, released under the
[DILLIGAF License](LICENSE).

## The games

| | | | | |
|---|---|---|---|---|
| 01 Attacker | 11 Galactic Attack | 21 Lunar Landing | 31 Planets | 41 Space Mission |
| 02 Barrel Jump | 12 Galactic Dog Fight | 22 Maze Eater | 32 Plasma Bolt | 42 Space Search |
| 03 Black Hole | 13 Ghosts | 23 Motorway | 33 Pontoon | 43 Space Ship |
| 04 Boggles | 14 Hangman | 24 Nim | 34 Psion Attack | 44 Star Trek |
| 05 Cannonball Battle | 15 High Rise | 25 Noughts and Crosses | 35 Radar Landing | 45 Submarines |
| 06 Derby Dash | 16 Inferno | 26 Old Bones | 36 Rats | 46 Tanker |
| 07 Do Your Sums | 17 Intruder | 27 Orbitter | 37 Rocket Launch | 47 The Force |
| 08 Dynamite | 18 Invasive Action | 28 Overtake | 38 Sitting Target | 48 Thin Ice |
| 09 Exchange | 19 Jet Flight | 29 Parachute | 39 Ski Jump | 49 Tunnel Escape |
| 10 Force Field | 20 Jet Mobile | 30 Phaser | 40 Smash the Windows | 50 Universe |

## How it's built

- **No content pipeline.** Everything is generated at start-up: one atlas texture (a white texel, an anti-aliased disc,
  a glow, and the Oric ROM font magnified 8x), every sound effect and the menu music (`Audio/Synth.cs`, `Audio/Music.cs`).
- **Resolution independent.** Games draw on a 640x360 virtual playfield with vector shapes (`Graphics/Gfx.cs`), batched
  through one `BasicEffect`. Extra screen width or height becomes margin: phones put the touch controls in the side
  margins, tablets get a control strip under the playfield.
- **One file per game** in `Cascade50.Core/Games/All/`, each a `MiniGame` (see [docs/GAME_GUIDE.md](docs/GAME_GUIDE.md)).
  The framework provides the menu, info pages, score bar, pause, game over, best scores and touch controls.

| Project | Target |
|---|---|
| `Cascade50.Core` | The whole app (net10.0): framework, scenes, the fifty games, store capture |
| `Cascade50.Android` | Android 6+ |
| `Cascade50.iOS` | iOS 15+ (UIScene life cycle) |
| `Cascade50.DesktopGL` | macOS (also runs on Linux and Windows) |
| `Cascade50.WindowsDX` | Windows (DirectX), packaged as MSIX for the Microsoft Store |
| `Cascade50.Tests` | xUnit soak tests: every game auto-played and button-mashed for minutes, on both input schemes |

Bundle ID (iOS and macOS) and Android package name: `uk.co.allthejohnsons.cascade50`.

## Controls

| | Desktop | Phone / tablet |
|---|---|---|
| Move | Cursor keys or WASD (or a game pad) | On-screen stick |
| Fire / action | `SPACE`, `Z` or `Enter` (pad A) | FIRE button |
| Second action | `X`, `Shift` or right click (pad B) | ALT button |
| Pointer games | Mouse | Tap and drag the playfield |
| Pause | `P` / `Esc` (pad Start) | The pause button, or Back on Android |
| Full screen | `F11` or `Alt`+`Enter` | Always |

## Build and run

```sh
dotnet test Cascade50.Tests                    # soak tests
dotnet run --project Cascade50.DesktopGL       # play on the Mac
```

Release builds go to `release/` (git-ignored):

```sh
tools/build_release.sh       # Android .aab/.apk (key ~/keys/cascade50-upload.jks, password in the Keychain) and iOS .ipa
tools/build_desktop.sh       # macOS universal .app + App Store .pkg   (tools/build_desktop.sh dev: locally runnable)
tools/build_windows.sh       # Windows x64 + arm64 .msix/.zip, built and tested in the Parallels "Windows 11" VM
```

Signing identities and the Microsoft Store identity are read from `signing.local.props`, which is not in the
repository: copy `signing.local.props.example` and fill in your own. Provisioning profiles (`devel-cascade50`,
`rel-cascade50`, `devel-cascade50-mac`, `rel-cascade50-macos`) and the Android upload key are read from `~/Downloads`,
`~/keys` and the Keychain, and are never committed.

## Store assets

The store material (listing copy, privacy policy, support page, screenshots, previews and art) is generated into
`stores/`, which is not in the repository. To regenerate it:

```sh
dotnet run --project Cascade50.DesktopGL -- --art art/rendered    # icon, splash and store art, drawn by the game
python3 tools/make_icons.py                                      # every platform icon and launch image
tools/capture_store.sh                                           # screenshots at every store size + app previews
CASCADE50_SUPPORT_EMAIL=you@example.com python3 tools/make_support_page.py   # stores/Cascade50-Support.html
python3 tools/check_listing.py                                   # character limits
```

## Licence

The Cascade 50 code, tools and documentation are released under the **[DILLIGAF License](LICENSE)**. It can't cover
what isn't ours: the original *Cassette 50* and its game names, the Oric ROM font, and third-party packages such as
MonoGame, which keep their own licences.
