#!/bin/sh
# Puts T8Suite in the desktop's application menu and in "Open with" for .bin files, for this user only (no root needed).
# Run it from the extracted folder, again after moving the folder: ./T8Suite/install-desktop.sh
# ponytail: a folder path with " ` $ or \ in it would need escaping in Exec
set -e
dir=$(cd "$(dirname "$0")" && pwd)
data=${XDG_DATA_HOME:-$HOME/.local/share}
mkdir -p "$data/applications" "$data/icons/hicolor/128x128/apps"
cp "$dir/t8suite.png" "$data/icons/hicolor/128x128/apps/t8suite.png"
# StartupWMClass: the window's X11 class (Program.cs), so the taskbar shows this entry's icon
cat > "$data/applications/t8suite.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=T8Suite
Comment=Trionic 8 tuning suite
Exec="$dir/T8Suite" %f
Icon=t8suite
Terminal=false
Categories=Development;Engineering;
MimeType=application/octet-stream;
StartupWMClass=T8App
DESKTOP
update-desktop-database "$data/applications" 2>/dev/null || true
echo "Added $data/applications/t8suite.desktop"
