#!/usr/bin/env bash
# make-icon.sh — build packaging/icons/InspectorAPI.icns from the app icon PNG.
# Requires macOS tools: sips, iconutil.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC_PNG="$REPO_ROOT/InspectorAPI/InspectorAPI.Desktop/Assets/icon.png"
ICONSET_DIR="$REPO_ROOT/packaging/icons/InspectorAPI.iconset"
OUT_ICNS="$REPO_ROOT/packaging/icons/InspectorAPI.icns"

if [[ ! -f "$SRC_PNG" ]]; then
  echo "error: source icon not found: $SRC_PNG" >&2
  exit 1
fi

rm -rf "$ICONSET_DIR"
mkdir -p "$ICONSET_DIR"

# name size  (iconutil requires all ten variants)
sizes=(
  "16   icon_16x16"
  "32   icon_16x16@2x"
  "32   icon_32x32"
  "64   icon_32x32@2x"
  "128  icon_128x128"
  "256  icon_128x128@2x"
  "256  icon_256x256"
  "512  icon_256x256@2x"
  "512  icon_512x512"
  "1024 icon_512x512@2x"
)

for entry in "${sizes[@]}"; do
  read -r size name <<< "$entry"
  sips -z "$size" "$size" "$SRC_PNG" --out "$ICONSET_DIR/$name.png" >/dev/null
done

iconutil -c icns "$ICONSET_DIR" -o "$OUT_ICNS"
rm -rf "$ICONSET_DIR"

echo "built $OUT_ICNS"
