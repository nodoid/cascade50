#!/bin/zsh
# Renders every store screenshot and app preview into stores/, natively at each store's size.
# The renderer is resolution independent, so the Mac build draws the iPhone, iPad, Android, Mac and
# Windows images directly (phone and tablet shots use the touch layout). Previews need ffmpeg.
#
#   tools/capture_store.sh            screenshots and previews
#   tools/capture_store.sh stills     screenshots only
set -euo pipefail
cd "$(dirname "$0")/.."

# Store order: the title, the menu, then a spread of games (number = position on the tape).
SHOTS=(splash menu g11 g22 g02 g40 g21 g44 g32 g49)
NAMES=(title menu galactic-attack maze-eater barrel-jump smash-the-windows lunar-landing star-trek plasma-bolt tunnel-escape)

dotnet build Cascade50.DesktopGL -c Release -v q
run() { dotnet Cascade50.DesktopGL/bin/Release/net10.0/Cascade50.dll "$@"; }
tmp=$(mktemp -d)
only=${(j:,:)SHOTS}

run --capture $tmp/mobile --mobile --only $only --size 2868x1320,2688x1242,2752x2064,1920x1080,2560x1600
run --capture $tmp/desktop --only $only --size 2880x1800,1440x900,3840x2160,1920x1080,1366x768

place() { # place <source dir> <dest dir>
  rm -rf $2 && mkdir -p $2
  for i in {1..${#SHOTS}}; do
    src=$(ls $1/${SHOTS[$i]}*.png | head -1)
    cp $src $2/$(printf %02d $i)-${NAMES[$i]}.png
  done
}
place $tmp/mobile/2868x1320 stores/app-store/iphone-6.9in-2868x1320
place $tmp/mobile/2688x1242 stores/app-store/iphone-6.5in-2688x1242
place $tmp/mobile/2752x2064 stores/app-store/ipad-13in-2752x2064
place $tmp/mobile/1920x1080 stores/google-play/phone-screenshots-1920x1080
place $tmp/mobile/2560x1600 stores/google-play/tablet-screenshots-2560x1600
place $tmp/desktop/2880x1800 stores/mac-app-store/screenshots-2880x1800
place $tmp/desktop/1440x900 stores/mac-app-store/screenshots-1440x900
place $tmp/desktop/3840x2160 stores/microsoft-store/screenshots-3840x2160
place $tmp/desktop/1920x1080 stores/microsoft-store/screenshots-1920x1080
place $tmp/desktop/1366x768 stores/microsoft-store/screenshots-1366x768
rm -rf $tmp

if [[ ${1:-} != stills ]]; then
  run --video stores/app-store/app-preview-iphone-1920x886.mp4 --size 1920x886 --mobile
  run --video stores/app-store/app-preview-ipad-1600x1200.mp4 --size 1600x1200 --mobile
  run --video stores/mac-app-store/app-preview-1920x1080.mp4 --size 1920x1080
  run --video stores/google-play/promo-video-1920x1080.mp4 --size 1920x1080 --mobile
fi
run --catalog stores/games.json
echo "store assets done"
