#!/usr/bin/env python3
"""Builds stores/Cascade50-Support.html: a self-contained support page (images embedded) from the games' own
descriptions (stores/games.json, written by `Cascade50 --catalog`) and the 1280x720 screenshots in art/shots/.

    dotnet run --project Cascade50.DesktopGL -- --catalog stores/games.json
    dotnet run --project Cascade50.DesktopGL -- --capture art/shots --size 1280x720
    python3 tools/make_support_page.py
"""
import base64, html, io, json, os
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
os.chdir(ROOT)
games = json.load(open('stores/games.json'))
SHOTS = 'art/shots/1280x720'
# Contact address for the page: set CASCADE50_SUPPORT_EMAIL (kept out of the repository).
EMAIL = os.environ.get('CASCADE50_SUPPORT_EMAIL', 'support@example.com')
CATEGORY = {'Arcade': 'Arcade', 'Shooter': 'Shooter', 'Skill': 'Skill', 'Puzzle': 'Puzzle', 'Brain': 'Brain'}


def img(path, width):
    if not os.path.exists(path):
        return ''
    im = Image.open(path).convert('RGB')
    im.thumbnail((width, width))
    b = io.BytesIO()
    im.save(b, 'JPEG', quality=80, optimize=True)
    return 'data:image/jpeg;base64,' + base64.b64encode(b.getvalue()).decode()


def shot(prefix):
    if not os.path.isdir(SHOTS):
        return None
    for f in sorted(os.listdir(SHOTS)):
        if f.startswith(prefix):
            return os.path.join(SHOTS, f)
    return None


e = html.escape
cards = []
for g in games:
    n = g['number']
    s = shot(f'g{n:02d}')
    pic = f'<img loading="lazy" src="{img(s, 480)}" alt="{e(g["title"])} screenshot">' if s else ''
    how = ''.join(f'<p>{e(p)}</p>' for p in g['howToPlay'])
    desk = ''.join(f'<li>{e(c)}</li>' for c in g['desktopControls'])
    touch = ''.join(f'<li>{e(c)}</li>' for c in g['touchControls'])
    cards.append(f'''
    <article class="game" id="game-{n}" style="--game:{g["accent"]}">
      {pic}
      <div class="body">
        <p class="num">{n:02d} · {e(CATEGORY.get(g["category"], g["category"]))}</p>
        <h3>{e(g["title"])}</h3>
        <p class="tag">{e(g["tagline"])}</p>
        <details><summary>How to play</summary>{how}
          <h4>Keyboard and mouse</h4><ul>{desk}</ul>
          <h4>Touch</h4><ul>{touch}</ul>
        </details>
      </div>
    </article>''')

hero = img('art/rendered/titled-hero-art-1920x1080.png', 1200) or img('art/icon-1024.png', 600)
icon = img('art/icon-1024.png', 160)
menu = shot('menu')
menu_img = f'<figure><img src="{img(menu, 900)}" alt="The game menu"><figcaption>Pick any of the fifty from the menu</figcaption></figure>' if menu else ''

page = f'''<!doctype html>
<html lang="en-GB">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Cascade 50 Support</title>
<meta name="description" content="Help and support for Cascade 50: fifty Oric-1 classics remade for iPhone, iPad, Android, Mac and Windows. Controls, how to play every game, answers to common questions and contact details.">
<link rel="icon" href="{icon}">
<style>
:root {{
  --bg: #F5F5F7; --pane: #FFFFFF; --sidebar: #E8E9ED; --text: #1D1D1F; --muted: #6E6E73;
  --accent: #6A1BC0; --accent-2: #C0156A; --border: #D9D9DE; --selected: #E6DAFB;
}}
@media (prefers-color-scheme: dark) {{
  :root:not([data-theme="light"]) {{
    --bg: #0B0A16; --pane: #16142A; --sidebar: #24213F; --text: #EDEDF0; --muted: #A9A6C0;
    --accent: #FFE628; --accent-2: #28E6F0; --border: #322E55; --selected: #2E2A55;
  }}
}}
:root[data-theme="dark"] {{
  --bg: #0B0A16; --pane: #16142A; --sidebar: #24213F; --text: #EDEDF0; --muted: #A9A6C0;
  --accent: #FFE628; --accent-2: #28E6F0; --border: #322E55; --selected: #2E2A55;
}}
* {{ box-sizing: border-box; }}
html {{ scroll-behavior: smooth; }}
body {{ margin: 0; background: var(--bg); color: var(--text);
  font: 16px/1.6 -apple-system, BlinkMacSystemFont, "Segoe UI", system-ui, Roboto, "Helvetica Neue", Arial, sans-serif; }}
a {{ color: var(--accent); }}
.wrap {{ max-width: 1080px; margin: 0 auto; padding: 0 16px; }}
header {{ background: var(--pane); border-bottom: 1px solid var(--border); }}
header .wrap {{ padding-top: 36px; padding-bottom: 28px; position: relative; }}
.brand {{ display: flex; align-items: center; gap: 16px; }}
.brand img {{ width: 72px; height: 72px; border-radius: 16px; }}
.eyebrow {{ color: var(--accent-2); font-weight: 600; letter-spacing: .02em; margin: 0; }}
h1 {{ font-size: clamp(28px, 5vw, 40px); line-height: 1.15; margin: 0; }}
.lead {{ font-size: 18px; color: var(--muted); margin: 16px 0 0; max-width: 48em; }}
.hero {{ width: 100%; border-radius: 14px; margin-top: 22px; display: block; }}
nav.toc {{ display: flex; flex-wrap: wrap; gap: 8px; margin-top: 22px; }}
nav.toc a {{ text-decoration: none; color: var(--text); background: var(--sidebar); border-radius: 999px; padding: 6px 14px; font-size: 14px; }}
nav.toc a:hover {{ background: var(--selected); }}
.theme-toggle {{ position: absolute; top: 16px; right: 16px; background: var(--sidebar); color: var(--text); border: 0; border-radius: 999px; padding: 6px 12px; font: inherit; font-size: 14px; cursor: pointer; }}
main {{ padding: 8px 0 48px; }}
section {{ padding-top: 36px; }}
h2 {{ font-size: 24px; margin: 0 0 12px; }}
h3 {{ font-size: 18px; margin: 2px 0 4px; }}
h4 {{ font-size: 14px; margin: 12px 0 2px; color: var(--muted); text-transform: uppercase; letter-spacing: .04em; }}
p {{ margin: 8px 0; }}
ul {{ padding-left: 22px; margin: 6px 0; }}
.cards {{ display: grid; grid-template-columns: repeat(auto-fit, minmax(260px, 1fr)); gap: 16px; margin-top: 16px; }}
.card {{ background: var(--pane); border: 1px solid var(--border); border-radius: 14px; padding: 18px 20px; }}
.card h3 {{ color: var(--accent-2); margin-top: 0; }}
.card p {{ color: var(--muted); }}
.games {{ display: grid; grid-template-columns: repeat(auto-fill, minmax(300px, 1fr)); gap: 16px; margin-top: 16px; }}
.game {{ background: var(--pane); border: 1px solid var(--border); border-top: 4px solid var(--game); border-radius: 14px; overflow: hidden; }}
.game img {{ width: 100%; display: block; aspect-ratio: 16 / 9; object-fit: cover; background: #000; }}
.game .body {{ padding: 12px 16px 14px; }}
.game .num {{ margin: 0; font-size: 13px; color: var(--muted); }}
.game .tag {{ color: var(--muted); margin: 0 0 6px; }}
details summary {{ cursor: pointer; color: var(--accent); font-weight: 600; }}
.table-wrap {{ overflow-x: auto; }}
table {{ border-collapse: collapse; width: 100%; background: var(--pane); border-radius: 12px; overflow: hidden; }}
th, td {{ text-align: left; vertical-align: top; padding: 10px 14px; border-bottom: 1px solid var(--border); }}
th {{ background: var(--sidebar); font-size: 14px; }}
kbd {{ background: var(--sidebar); border: 1px solid var(--border); border-bottom-width: 2px; border-radius: 5px; padding: 0 6px; font: 13px/1.6 ui-monospace, Menlo, monospace; }}
figure {{ margin: 18px 0; }}
figure img {{ width: 100%; border-radius: 12px; display: block; }}
figcaption {{ color: var(--muted); font-size: 14px; margin-top: 6px; }}
.faq h3 {{ margin-top: 20px; }}
footer {{ color: var(--muted); font-size: 14px; border-top: 1px solid var(--border); padding: 24px 0 40px; }}
</style>
</head>
<body>
<header>
  <div class="wrap">
    <button class="theme-toggle" id="theme" type="button" aria-label="Switch between light and dark">◐ Theme</button>
    <div class="brand"><img src="{icon}" alt=""><div><p class="eyebrow">Support</p><h1>Cascade 50</h1></div></div>
    <p class="lead">Fifty classic Oric-1 games from Cascade's famous 1983 <em>Cassette 50</em> tape, rebuilt from scratch with
      new graphics, sound and gameplay. Here you'll find the controls, how to play every game, answers to common
      questions, and how to get in touch.</p>
    <img class="hero" src="{hero}" alt="Cascade 50: the cassette surrounded by scenes from the games">
    <nav class="toc" aria-label="On this page">
      <a href="#about">About</a><a href="#controls">Controls</a><a href="#games">The 50 games</a>
      <a href="#faq">Questions</a><a href="#privacy">Privacy</a><a href="#contact">Contact</a>
    </nav>
  </div>
</header>
<main class="wrap">
  <section id="about">
    <h2>About Cascade 50</h2>
    <p>In 1983 Cascade Games sold a cassette with fifty games on it for the Oric-1. They were short BASIC programs,
      mostly in plain text. Cascade 50 keeps every title and idea from the tape and makes each one a proper game:
      smooth retro graphics, glows and explosions, chiptune sound effects, difficulty that builds as you play, and a
      best score to beat.</p>
    {menu_img}
    <div class="cards">
      <div class="card"><h3>Fifty games, one app</h3><p>Shoot-'em-ups, platformers, racing, puzzles, card games, word
        games and strategy. Pick any game from the menu; each one has its own instructions and best score.</p></div>
      <div class="card"><h3>Everywhere</h3><p>iPhone, iPad, Android phones and tablets, Macs (macOS 12 or later, Apple
        silicon or Intel) and Windows 10 and 11 PCs. It plays in landscape and needs no internet connection.</p></div>
      <div class="card"><h3>No catches</h3><p>No adverts, no in-app purchases, no accounts and no tracking. Your best
        scores stay on your device.</p></div>
    </div>
  </section>

  <section id="controls">
    <h2>Controls</h2>
    <p>Every game's own controls are on its information page (choose a game in the menu) and below.</p>
    <div class="table-wrap"><table>
      <thead><tr><th></th><th>iPhone, iPad and Android</th><th>Mac and Windows</th></tr></thead>
      <tbody>
        <tr><td><strong>Move</strong></td><td>The on-screen stick, bottom left.</td><td>Cursor keys or <kbd>W</kbd><kbd>A</kbd><kbd>S</kbd><kbd>D</kbd>.</td></tr>
        <tr><td><strong>Fire / action</strong></td><td>The big button, bottom right (its label says what it does).</td><td><kbd>Space</kbd>, <kbd>Z</kbd> or <kbd>Enter</kbd>.</td></tr>
        <tr><td><strong>Second action</strong></td><td>The smaller button, where a game has one.</td><td><kbd>X</kbd>, <kbd>Shift</kbd> or the right mouse button.</td></tr>
        <tr><td><strong>Tap and drag games</strong></td><td>Tap or drag the playfield directly.</td><td>Click or drag with the mouse. Most also work from the keyboard.</td></tr>
        <tr><td><strong>Pause</strong></td><td>The pause button, top right, or Back on Android.</td><td><kbd>P</kbd> or <kbd>Esc</kbd>.</td></tr>
        <tr><td><strong>Full screen</strong></td><td>Always full screen.</td><td><kbd>F11</kbd> or <kbd>Alt</kbd>+<kbd>Enter</kbd>. The window can also be resized.</td></tr>
      </tbody>
    </table></div>
    <p>Game controllers work on the Mac and Windows: the left stick or d-pad moves, <strong>A</strong> fires, <strong>B</strong> is the second action and <strong>Start</strong> pauses.</p>
  </section>

  <section id="games">
    <h2>The 50 games</h2>
    <p>In the order they appeared on the original tape. Open "How to play" for the rules and controls.</p>
    <div class="games">{''.join(cards)}
    </div>
  </section>

  <section id="faq" class="faq">
    <h2>Questions</h2>
    <h3>Where are my best scores kept?</h3>
    <p>On your device, in the app's own storage. They are removed if you uninstall the app.</p>
    <h3>How do I turn the music or sound effects off?</h3>
    <p>Open <strong>Options</strong> at the top right of the menu.</p>
    <h3>Can I pause a game?</h3>
    <p>Yes: the pause button at the top right, <kbd>P</kbd> or <kbd>Esc</kbd>, or Back on Android. From the pause menu you can resume, restart, read the instructions or go back to the menu. Switching to another app pauses the game too.</p>
    <h3>The touch controls are in the way.</h3>
    <p>On most phones they sit in the margins beside the game; on tablets they sit underneath it. Games you play by tapping the screen don't show them at all.</p>
    <h3>Are these the original programs?</h3>
    <p>No. They are new games, written from scratch, that keep the names and ideas of the 1983 originals. No code, graphics or sound from the tape is used.</p>
  </section>

  <section id="privacy">
    <h2>Privacy</h2>
    <p>Cascade 50 doesn't collect, store, share or sell any personal information. It has no accounts, advertising,
      analytics or tracking, and never connects to the internet. The only thing it saves is a small file on your device
      with your best scores and settings.</p>
  </section>

  <section id="contact">
    <h2>Contact</h2>
    <p>Found a bug, or got a question the page doesn't answer? Email <a href="mailto:{EMAIL}?subject=Cascade%2050">{EMAIL}</a>.
      Please say which device you're using and which game.</p>
  </section>
</main>
<footer><div class="wrap">Cascade 50, by PFJ, based on the Cascade Games originals. <em>Cassette 50</em> and its game names belong to their rights holders.</div></footer>
<script>
(function () {{
  var root = document.documentElement, button = document.getElementById('theme');
  try {{ var saved = localStorage.getItem('theme'); if (saved) root.setAttribute('data-theme', saved); }} catch (e) {{}}
  button.addEventListener('click', function () {{
    var dark = root.getAttribute('data-theme') ? root.getAttribute('data-theme') === 'dark'
      : window.matchMedia('(prefers-color-scheme: dark)').matches;
    var next = dark ? 'light' : 'dark';
    root.setAttribute('data-theme', next);
    try {{ localStorage.setItem('theme', next); }} catch (e) {{}}
  }});
}})();
</script>
</body>
</html>
'''
open('stores/Cascade50-Support.html', 'w').write(page)
print(f'stores/Cascade50-Support.html ({len(page) // 1024} KB)')
