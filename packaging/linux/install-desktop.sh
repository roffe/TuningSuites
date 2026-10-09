#!/bin/sh
# Puts T7Suite in the desktop's application menu and in "Open with" for .bin files, for this user only (no root needed).
# Run it from the extracted folder, again after moving the folder: ./T7Suite/install-desktop.sh
# ponytail: a folder path with " ` $ or \ in it would need escaping in Exec
set -e
dir=$(cd "$(dirname "$0")" && pwd)
data=${XDG_DATA_HOME:-$HOME/.local/share}
mkdir -p "$data/applications" "$data/icons/hicolor/128x128/apps"
cp "$dir/t7suite.png" "$data/icons/hicolor/128x128/apps/t7suite.png"
# StartupWMClass: the window's X11 class (Program.cs), so the taskbar shows this entry's icon
cat > "$data/applications/t7suite.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=T7Suite
Comment=Trionic 7 tuning suite
Exec="$dir/T7Suite" %f
Icon=t7suite
Terminal=false
Categories=Development;Engineering;
MimeType=application/octet-stream;
StartupWMClass=T7App
DESKTOP
update-desktop-database "$data/applications" 2>/dev/null || true
echo "Added $data/applications/t7suite.desktop"
