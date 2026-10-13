#!/bin/sh
# Puts TxSuite in the desktop's application menu and in "Open with" for .bin files, for this user only (no root needed).
# Run it from the extracted folder, again after moving the folder: ./TxSuite/install-desktop.sh
# ponytail: a folder path with " ` $ or \ in it would need escaping in Exec
set -e
dir=$(cd "$(dirname "$0")" && pwd)
data=${XDG_DATA_HOME:-$HOME/.local/share}
mkdir -p "$data/applications" "$data/icons/hicolor/512x512/apps"
cp "$dir/txsuite.png" "$data/icons/hicolor/512x512/apps/txsuite.png"
# StartupWMClass: the window's X11 class (Program.cs), so the taskbar shows this entry's icon
cat > "$data/applications/txsuite.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=TxSuite
Comment=Trionic 5, 7 and 8 tuning suite
Exec="$dir/TxSuite" %f
Icon=txsuite
Terminal=false
Categories=Development;Engineering;
MimeType=application/octet-stream;
StartupWMClass=TxSuite
DESKTOP
update-desktop-database "$data/applications" 2>/dev/null || true
echo "Added $data/applications/txsuite.desktop"
