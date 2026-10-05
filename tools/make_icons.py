#!/usr/bin/env python3
"""Builds every platform icon, launch image and store graphic from the art the game renders itself.

    dotnet run --project Cascade50.DesktopGL -- --art art/rendered    # renders the icon and store art
    python3 tools/make_icons.py                                      # run from the repo root
"""
import json, os, shutil, subprocess
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
os.chdir(ROOT)
ART = 'art/rendered'


def load(name):
    return Image.open(os.path.join(ART, name)).convert('RGBA')


icon = load('icon-1024.png')
foreground = load('icon-foreground-1024.png')
background = load('icon-background-1024.png')
splash = load('splash-1240x600.png')


def save(im, path, rgb=False):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    (im.convert('RGB') if rgb else im).save(path, optimize=True)


def sized(im, size):
    return im.resize((size, size), Image.LANCZOS)


def on_canvas(im, w, h, scale, bg=(0, 0, 0, 255)):
    """im centred on a w x h canvas, scaled to `scale` of the shorter side."""
    c = Image.new('RGBA', (w, h), bg)
    s = int(min(w, h) * scale)
    r = im.resize((s, int(s * im.height / im.width)), Image.LANCZOS)
    c.alpha_composite(r, ((w - r.width) // 2, (h - r.height) // 2))
    return c


def round_mask(im):
    m = Image.new('L', im.size, 0)
    ImageDraw.Draw(m).ellipse((0, 0, im.width - 1, im.height - 1), fill=255)
    out = Image.new('RGBA', im.size, (0, 0, 0, 0))
    out.paste(im, (0, 0), m)
    return out


# ---------------------------------------------------------------- iOS
ios = 'Cascade50.iOS/AppIcon.xcassets/AppIcon.appiconset'
os.makedirs(ios, exist_ok=True)
images = []
for idiom, pt, scales in [('iphone', 20, (2, 3)), ('iphone', 29, (2, 3)), ('iphone', 40, (2, 3)), ('iphone', 60, (2, 3)),
                          ('ipad', 20, (1, 2)), ('ipad', 29, (1, 2)), ('ipad', 40, (1, 2)), ('ipad', 76, (1, 2)),
                          ('ipad', 83.5, (2,)), ('ios-marketing', 1024, (1,))]:
    for s in scales:
        px = int(pt * s)
        name = f'icon_{px}x{px}.png'
        save(sized(icon, px), os.path.join(ios, name), rgb=True)
        images.append({'filename': name, 'idiom': idiom, 'scale': f'{s}x', 'size': f'{pt:g}x{pt:g}'})
json.dump({'images': images, 'info': {'author': 'xcode', 'version': 1}}, open(os.path.join(ios, 'Contents.json'), 'w'), indent=2)
json.dump({'info': {'author': 'xcode', 'version': 1}}, open('Cascade50.iOS/AppIcon.xcassets/Contents.json', 'w'), indent=2)
for s in (1, 2, 3):
    save(splash.resize((310 * s, 150 * s), Image.LANCZOS), f'Cascade50.iOS/Resources/LaunchLogo{"" if s == 1 else f"@{s}x"}.png', rgb=True)

# ---------------------------------------------------------------- Android
res = 'Cascade50.Android/Resources'
for folder, px in [('mdpi', 48), ('hdpi', 72), ('xhdpi', 96), ('xxhdpi', 144), ('xxxhdpi', 192)]:
    save(sized(icon, px), f'{res}/mipmap-{folder}/icon.png')
    save(round_mask(sized(icon, px)), f'{res}/mipmap-{folder}/icon_round.png')
    # Adaptive icons: 108dp layers; the artwork sits in the central 72dp safe zone.
    layer = px * 108 // 48
    fg = Image.new('RGBA', (layer, layer), (0, 0, 0, 0))
    art = foreground.resize((int(layer * 0.62),) * 2, Image.LANCZOS)
    fg.alpha_composite(art, ((layer - art.width) // 2, (layer - art.height) // 2))
    save(fg, f'{res}/mipmap-{folder}/icon_foreground.png')
    save(sized(background, layer), f'{res}/mipmap-{folder}/icon_background.png')
save(splash, f'{res}/drawable-nodpi/splash.png')

# ---------------------------------------------------------------- Windows
ico = sized(icon, 256)
ico.save('Cascade50.WindowsDX/Icon.ico', sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
shutil.copy('Cascade50.WindowsDX/Icon.ico', 'Cascade50.DesktopGL/Icon.ico')
assets = 'Cascade50.WindowsDX/Windows/Assets'
for name, size in [('Square44x44Logo', 44), ('Square71x71Logo', 71), ('Square150x150Logo', 150), ('Square310x310Logo', 310),
                   ('StoreLogo', 50), ('Square44x44Logo.targetsize-44_altform-unplated', 44)]:
    save(sized(icon, size), f'{assets}/{name}.png')
save(on_canvas(splash, 310, 150, 1.0), f'{assets}/Wide310x150Logo.png')
save(on_canvas(splash, 620, 300, 1.0), f'{assets}/SplashScreen.png')

# ---------------------------------------------------------------- macOS
iconset = 'art/Cascade50.iconset'
os.makedirs(iconset, exist_ok=True)
for s in [16, 32, 128, 256, 512]:
    sized(icon, s).save(f'{iconset}/icon_{s}x{s}.png')
    sized(icon, s * 2).save(f'{iconset}/icon_{s}x{s}@2x.png')
subprocess.run(['iconutil', '-c', 'icns', iconset, '-o', 'Cascade50.DesktopGL/Cascade50.icns'], check=True)
shutil.rmtree(iconset)

# ---------------------------------------------------------------- store graphics
save(icon, 'stores/app-store/icon-1024.png', rgb=True)
save(sized(icon, 512), 'stores/google-play/icon-512.png')
shutil.copy(f'{ART}/feature-graphic-1024x500.png', 'stores/google-play/feature-graphic-1024x500.png')
ms = 'stores/microsoft-store'
save(sized(icon, 300), f'{ms}/store-logo-300x300.png')
for name in ['super-hero-art-3840x2160', 'super-hero-art-1920x1080', 'box-art-2160x2160', 'poster-art-1440x2160']:
    os.makedirs(ms, exist_ok=True)
    shutil.copy(f'{ART}/{name}.png', f'{ms}/{name}.png')
for name in ['titled-hero-art-3840x2160', 'titled-hero-art-1920x1080', 'branded-key-art-584x800',
             'featured-promotional-square-art-2160x2160', 'featured-promotional-square-art-1080x1080']:
    os.makedirs(f'{ms}/xbox', exist_ok=True)
    shutil.copy(f'{ART}/{name}.png', f'{ms}/xbox/{name}.png')
shutil.copy(f'{ART}/icon-1024.png', 'art/icon-1024.png')
print('icons done')
