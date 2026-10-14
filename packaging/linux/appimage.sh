#!/usr/bin/env bash
# TxSuite as an AppImage of this checkout, one file that runs on any desktop Linux: make appimage, or
#   bash packaging/linux/appimage.sh [linux-x64|linux-arm64]
# It publishes TxSuite self-contained (PUBLISHED names a publish folder already made, as CI's), lays out the AppDir and packs it with
# appimagetool and the AppImage runtime, both pinned releases checked by their sha256 and kept in ~/.cache/txsuite-appimage. The
# AppImage lands in out/TxSuite-<version>-<rid>.AppImage, the version as the build has it (the nearest Txsuite_v tag, -<sha> after
# it), or where OUTPUT says.
set -euo pipefail
cd "$(dirname "$0")/../.."

rid=${1:-linux-x64}
case "$rid" in
  linux-x64) arch=x86_64 ;;
  linux-arm64) arch=aarch64 ;;
  *) echo "usage: $0 [linux-x64|linux-arm64]" >&2; exit 2 ;;
esac
version=$(dotnet msbuild TxSuite/TxSuite.csproj -t:VersionFromGit -getProperty:InformationalVersion)
output=${OUTPUT:-out/TxSuite-$version-$rid.AppImage}

# the pinned tools: appimagetool for this machine, the runtime for the AppImage's
cache=${XDG_CACHE_HOME:-$HOME/.cache}/txsuite-appimage
fetch() { # url sha256 file
  if [ ! -f "$3" ] || ! echo "$2  $3" | sha256sum -c --status; then
    mkdir -p "$(dirname "$3")"
    curl -fsSLo "$3" "$1"
    echo "$2  $3" | sha256sum -c --status || { echo "$3: checksum mismatch" >&2; rm -f "$3"; exit 1; }
  fi
}
case "$(uname -m)" in
  x86_64) tool_sha=ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0 ;;
  aarch64) tool_sha=f0837e7448a0c1e4e650a93bb3e85802546e60654ef287576f46c71c126a9158 ;;
  *) echo "appimagetool doesn't run on $(uname -m)" >&2; exit 1 ;;
esac
case "$arch" in
  x86_64) runtime_sha=2fca8b443c92510f1483a883f60061ad09b46b978b2631c807cd873a47ec260d ;;
  aarch64) runtime_sha=00cbdfcf917cc6c0ff6d3347d59e0ca1f7f45a6df1a428a0d6d8a78664d87444 ;;
esac
tool=$cache/appimagetool-1.9.1-$(uname -m).AppImage
runtime=$cache/runtime-20251108-$arch
fetch "https://github.com/AppImage/appimagetool/releases/download/1.9.1/appimagetool-$(uname -m).AppImage" "$tool_sha" "$tool"
fetch "https://github.com/AppImage/type2-runtime/releases/download/20251108/runtime-$arch" "$runtime_sha" "$runtime"
chmod +x "$tool"

# the publish and the AppDir (a few hundred MB) under out/, not in /tmp, which can be RAM
mkdir -p out
work=$(mktemp -d "$PWD/out/.appimage.XXXXXX")
trap 'rm -rf "$work"' EXIT
published=${PUBLISHED:-}
if [ -z "$published" ]; then
  published=$work/publish
  dotnet publish TxSuite/TxSuite.csproj -c Release -r "$rid" --self-contained -o "$published"
fi

# the AppDir: the program in usr/bin, its desktop entry and icon at the top (the entry as install-desktop.sh writes it), AppRun starts it
app=$work/TxSuite.AppDir
mkdir -p "$app/usr/bin"
cp -a "$published/." "$app/usr/bin/"
cp packaging/linux/TxSuite/txsuite.png "$app/txsuite.png"
ln -s txsuite.png "$app/.DirIcon"
cat > "$app/txsuite.desktop" <<'DESKTOP'
[Desktop Entry]
Type=Application
Name=TxSuite
Comment=Trionic 5, 7 and 8 tuning suite
Exec=TxSuite %f
Icon=txsuite
Terminal=false
Categories=Development;Engineering;
MimeType=application/octet-stream;
StartupWMClass=TxSuite
DESKTOP
cat > "$app/AppRun" <<'APPRUN'
#!/bin/sh
here=$(dirname "$(readlink -f "$0")")
exec "$here/usr/bin/TxSuite" "$@"
APPRUN
chmod +x "$app/AppRun"

mkdir -p "$(dirname "$output")"
# extracted to run: CI's runners and some desktops have no FUSE for appimagetool's own AppImage
APPIMAGE_EXTRACT_AND_RUN=1 ARCH=$arch "$tool" --no-appstream --runtime-file "$runtime" "$app" "$output" > "$work/appimagetool.log" 2>&1 \
  || { cat "$work/appimagetool.log" >&2; exit 1; }
echo "$output"
