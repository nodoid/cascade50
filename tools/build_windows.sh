#!/bin/zsh
# Builds the Windows version inside the Parallels "Windows 11" VM and packages it:
#   release/windows/Cascade50-<version>-<x64|arm64>.msix  (upload both to Partner Center; the Store signs them)
#   release/windows/Cascade50-<version>-<x64|arm64>.zip   (the same game unpackaged, for testing on a PC)
#
# The VM shares the Mac's Downloads folder (Z:\Downloads), so the source is staged in a temporary
# folder there, built and tested with the Windows .NET SDK in C:\Build\Cascade50, and the published
# builds are copied back. Pass "capture" to also record the Microsoft Store screenshots with the
# Windows build (written to release/windows/capture).
set -euo pipefail
cd "$(dirname "$0")/.."

VM=${CASCADE50_VM:-"Windows 11"}
VERSION=$(dotnet msbuild Cascade50.WindowsDX/Cascade50.WindowsDX.csproj -getProperty:Version)
# Store identity from signing.local.props (Partner Center > Product identity).
IDENTITY=$(dotnet msbuild Cascade50.WindowsDX/Cascade50.WindowsDX.csproj -getProperty:Cascade50StoreIdentity)
PUBLISHER=$(dotnet msbuild Cascade50.WindowsDX/Cascade50.WindowsDX.csproj -getProperty:Cascade50StorePublisher)
STORE_NAME=$(dotnet msbuild Cascade50.WindowsDX/Cascade50.WindowsDX.csproj -getProperty:Cascade50StoreDisplayName)
STAGE="$HOME/Downloads/cascade50-vm-transfer"
WSTAGE='Z:\Downloads\cascade50-vm-transfer'
out=release/windows

prlctl status "$VM" | grep -q running || prlctl resume "$VM" || prlctl start "$VM"
rm -rf "$STAGE" && mkdir -p "$STAGE"
git archive --format=tar -o "$STAGE/src.tar" HEAD

prlctl exec "$VM" --current-user powershell -NoProfile -Command "
  \$ErrorActionPreference = 'Stop'
  Remove-Item -Recurse -Force C:\Build\Cascade50, C:\Build\out -ErrorAction SilentlyContinue
  New-Item -ItemType Directory -Force C:\Build\Cascade50 | Out-Null
  tar -xf $WSTAGE\src.tar -C C:\Build\Cascade50
  Set-Location C:\Build\Cascade50
  dotnet test Cascade50.Tests
  if (\$LASTEXITCODE -ne 0) { throw 'tests failed' }
  foreach (\$a in 'x64','arm64') {
    dotnet publish Cascade50.WindowsDX -c Release -r win-\$a --self-contained -o C:\Build\out\\\$a -p:DebugType=none
    if (\$LASTEXITCODE -ne 0) { throw 'publish failed' }
    Remove-Item C:\Build\out\\\$a\createdump.exe -ErrorAction SilentlyContinue
    Compress-Archive -Force -Path C:\Build\out\\\$a\* -DestinationPath $WSTAGE\win-\$a.zip
  }
"

if [[ ${1:-} == capture ]]; then
  prlctl exec "$VM" --current-user powershell -NoProfile -Command "
    Remove-Item -Recurse -Force C:\Build\cap -ErrorAction SilentlyContinue
    Start-Process -Wait -FilePath C:\Build\out\arm64\Cascade50.exe -ArgumentList '--capture','C:\Build\cap','stills'
    Copy-Item -Recurse -Force C:\Build\cap $WSTAGE\capture
  "
fi

rm -rf $out && mkdir -p $out
for arch in x64 arm64; do
  cp "$STAGE/win-$arch.zip" "$out/Cascade50-$VERSION-$arch.zip"
  pkg=$(mktemp -d)/package
  mkdir -p $pkg && (cd $pkg && unzip -q "$OLDPWD/$out/Cascade50-$VERSION-$arch.zip")
  mkdir -p $pkg/Assets
  cp Cascade50.WindowsDX/Windows/Assets/*.png $pkg/Assets/
  sed -e "s/\$(VERSION)/$VERSION.0/" -e "s/\$(ARCH)/$arch/" -e "s/\$(IDENTITY)/$IDENTITY/" -e "s/\$(PUBLISHER)/$PUBLISHER/" \
    -e "s/\$(STORE_NAME)/$STORE_NAME/" Cascade50.WindowsDX/Windows/AppxManifest.xml > $pkg/AppxManifest.xml
  python3 tools/make_msix.py $pkg "$out/Cascade50-$VERSION-$arch.msix"
  rm -rf "$(dirname $pkg)"
done
[[ -d "$STAGE/capture" ]] && cp -R "$STAGE/capture" "$out/capture"
rm -rf "$STAGE"
echo "Windows: $out/Cascade50-$VERSION-{x64,arm64}.msix (version $VERSION.0)"
ls -la $out
