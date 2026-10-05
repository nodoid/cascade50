#!/bin/zsh
# Builds the mobile store packages into release/:
#   release/android/*.aab  (upload to Google Play)
#   release/android/*.apk  (for side-loading / testing)
#   release/ios/*.ipa      (upload to App Store Connect)
# Android signing uses ~/keys/cascade50-upload.jks; its password is read from the macOS Keychain
# (service "Cascade 50 Android upload keystore"). iOS signing uses Cascade50DistSigningKey from
# signing.local.props and the rel-cascade50 profile (installed locally, never committed).
set -euo pipefail
cd "$(dirname "$0")/.."

export CASCADE50_KEYSTORE_PASS="$(security find-generic-password -a cascade50-upload -s 'Cascade 50 Android upload keystore' -w)"
SDK="${ANDROID_HOME:-$HOME/Library/Android/sdk}"

rm -rf release/android release/ios
for format in aab apk; do
  dotnet publish Cascade50.Android/Cascade50.Android.csproj -c Release -f net10.0-android \
    -p:AndroidPackageFormat=$format -p:AndroidSdkDirectory="$SDK" -o release/android/$format
done
mkdir -p release/android/out
mv release/android/aab/*-Signed.aab release/android/apk/*-Signed.apk release/android/out/
rm -rf release/android/aab release/android/apk
mv release/android/out/* release/android/ && rmdir release/android/out
dotnet publish Cascade50.iOS/Cascade50.iOS.csproj -c Release -f net10.0-ios -o release/ios

ls -la release/android release/ios/*.ipa
