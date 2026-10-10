#!/bin/sh
# Puts T5Suite in the desktop's application menu and in "Open with" for .bin files, for this user only (no root needed).
# Run it from the extracted folder, again after moving the folder: ./T5Suite/install-desktop.sh
# ponytail: a folder path with " ` $ or \ in it would need escaping in Exec
set -e
dir=$(cd "$(dirname "$0")" && pwd)
data=${XDG_DATA_HOME:-$HOME/.local/share}
mkdir -p "$data/applications" "$data/icons/hicolor/128x128/apps"
cp "$dir/t5suite.png" "$data/icons/hicolor/128x128/apps/t5suite.png"
# StartupWMClass: the window's X11 class (Program.cs), so the taskbar shows this entry's icon
cat > "$data/applications/t5suite.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=T5Suite
Comment=Trionic 5 tuning suite
Exec="$dir/T5Suite" %f
Icon=t5suite
Terminal=false
Categories=Development;Engineering;
MimeType=application/octet-stream;
StartupWMClass=T5App
DESKTOP
update-desktop-database "$data/applications" 2>/dev/null || true
echo "Added $data/applications/t5suite.desktop"
