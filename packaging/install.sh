#!/usr/bin/env bash
# Convenience installer for the wipelog systemd user service.
#
# Run from the repo root after publishing the daemon, e.g.:
#   dotnet publish src/Wipelog.Daemon -c Release --no-self-contained -o ~/bin/wipelog
# so the binary actually exists at the path baked into the unit file.

set -euo pipefail

src_unit="$(dirname "$0")/systemd/wipelog.service"
dest_dir="${XDG_CONFIG_HOME:-$HOME/.config}/systemd/user"
dest_unit="$dest_dir/wipelog.service"

if [[ ! -f "$src_unit" ]]; then
    echo "error: $src_unit not found — run from the repo root?" >&2
    exit 1
fi

mkdir -p "$dest_dir"

cat <<'EOF'
Combat-log directory: the WoW _retail_/Logs folder inside your Wine prefix.
Tip: the GUI's "Detect WoW logs" button scans common launcher layouts.
Common locations:
  Wine:     $HOME/.wine/drive_c/Program Files (x86)/World of Warcraft/_retail_/Logs
  Lutris:   $HOME/Games/world-of-warcraft/drive_c/Program Files (x86)/World of Warcraft/_retail_/Logs
  Heroic:   $HOME/Games/Heroic/Prefixes/default/world-of-warcraft/.../Logs
EOF
read -rp "Combat-log directory: " log_dir
if [[ -z "$log_dir" ]]; then
    echo "error: combat-log directory is required" >&2
    exit 1
fi

read -rp "Output directory for clips [\$HOME/Videos/wow]: " out_dir
out_dir="${out_dir:-$HOME/Videos/wow}"

cp "$src_unit" "$dest_unit"

# Rewrite the --log-dir and --output args. Use a delimiter that won't appear in
# typical Linux paths.
python3 - "$dest_unit" "$log_dir" "$out_dir" <<'PY'
import re, sys, pathlib
path = pathlib.Path(sys.argv[1])
log_dir, out_dir = sys.argv[2], sys.argv[3]
text = path.read_text()
text = re.sub(r'--log-dir "[^"]*"', f'--log-dir "{log_dir}"', text)
text = re.sub(r'--output "[^"]*"', f'--output "{out_dir}"', text)
path.write_text(text)
PY

systemctl --user daemon-reload
systemctl --user enable --now wipelog.service

# Freedesktop integration: register the .desktop entry and hicolor icons
# so Wipelog shows up in app menus with the right icon. Best-effort —
# the systemd service works fine without these.
pkg_dir="$(cd "$(dirname "$0")" && pwd)"
data_dir="${XDG_DATA_HOME:-$HOME/.local/share}"
desktop_dir="$data_dir/applications"
icons_root="$data_dir/icons/hicolor"

mkdir -p "$desktop_dir"
cp "$pkg_dir/wipelog.desktop" "$desktop_dir/wipelog.desktop"

if [[ -d "$pkg_dir/icons/hicolor" ]]; then
    cp -r "$pkg_dir/icons/hicolor/." "$icons_root/"
fi

update-desktop-database "$desktop_dir" 2>/dev/null || true
gtk-update-icon-cache -f "$icons_root" 2>/dev/null || true

echo
echo "Tailing the journal for 5s — Ctrl-C to stop early."
timeout 5 journalctl --user -u wipelog.service -n 20 -f || true

echo
systemctl --user --no-pager status wipelog.service || true
