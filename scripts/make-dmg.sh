#!/usr/bin/env bash
# make-dmg.sh — package a self-contained osx-arm64 `dotnet publish` output into
# an .app bundle and a distributable .dmg (with an Applications symlink).
#
# usage: make-dmg.sh <publish-dir> <app-name> <dmg-name> [version] [icon.icns]
#
#   publish-dir  folder containing the published apphost executable (e.g. the
#                InspectorAPI binary from `dotnet publish -r osx-arm64`)
#   app-name     bundle/app name, e.g. InspectorAPI
#   dmg-name     base name for the output dmg
#   version      bundle + file version, e.g. 1.2.0 (default: 1.0.0)
#   icon.icns    path to the app icon (default: packaging/icons/InspectorAPI.icns,
#                built on demand via make-icon.sh)
#
# The final dmg is written to <repo-root>/dist/<dmg-name>-<version>-osx-arm64.dmg
# Requires macOS tools: hdiutil, osascript, codesign (ad-hoc signing, no cert needed).
set -euo pipefail

PUBLISH_DIR="${1:?usage: make-dmg.sh <publish-dir> <app-name> <dmg-name> [version] [icon.icns]}"
APP_NAME="${2:-InspectorAPI}"
DMG_NAME="${3:-InspectorAPI}"
VERSION="${4:-1.0.0}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ICNS="${5:-$REPO_ROOT/packaging/icons/InspectorAPI.icns}"

BUNDLE_ID="com.github.jsiulian.${APP_NAME}"
MIN_MACOS="12.0"

PUBLISH_DIR="$(cd "$PUBLISH_DIR" && pwd)"
if [[ ! -f "$PUBLISH_DIR/$APP_NAME" ]]; then
  echo "error: apphost '$APP_NAME' not found in publish dir: $PUBLISH_DIR" >&2
  exit 1
fi

if [[ ! -f "$ICNS" ]]; then
  echo "icon not found at $ICNS — building it via make-icon.sh"
  bash "$SCRIPT_DIR/make-icon.sh"
fi

WORK="$(mktemp -d /tmp/inspectorapi-dmg.XXXXXX)"
trap 'rm -rf "$WORK"' EXIT

echo "==> staging $APP_NAME.app"
APP="$WORK/$APP_NAME.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUBLISH_DIR/." "$APP/Contents/MacOS/"
chmod +x "$APP/Contents/MacOS/$APP_NAME"
cp "$ICNS" "$APP/Contents/Resources/$APP_NAME.icns"

cat > "$APP/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>CFBundleName</key>
	<string>$APP_NAME</string>
	<key>CFBundleDisplayName</key>
	<string>$APP_NAME</string>
	<key>CFBundleIdentifier</key>
	<string>$BUNDLE_ID</string>
	<key>CFBundleVersion</key>
	<string>$VERSION</string>
	<key>CFBundleShortVersionString</key>
	<string>$VERSION</string>
	<key>CFBundlePackageType</key>
	<string>APPL</string>
	<key>CFBundleExecutable</key>
	<string>$APP_NAME</string>
	<key>CFBundleIconFile</key>
	<string>$APP_NAME</string>
	<key>LSMinimumSystemVersion</key>
	<string>$MIN_MACOS</string>
	<key>NSHighResolutionCapable</key>
	<true/>
	<key>NSPrincipalClass</key>
	<string>NSApplication</string>
</dict>
</plist>
EOF

echo "==> ad-hoc codesigning $APP_NAME.app"
if command -v codesign >/dev/null 2>&1; then
  codesign --force --deep --sign - "$APP"
else
  echo "warning: codesign not found — skipping signing" >&2
fi

echo "==> building dmg contents"
DMG_STAGE="$WORK/dmg"
mkdir -p "$DMG_STAGE"
cp -R "$APP" "$DMG_STAGE/"
ln -s /Applications "$DMG_STAGE/Applications"

RAW_DMG="$WORK/$DMG_NAME-raw.dmg"
hdiutil create -volname "$APP_NAME" -srcfolder "$DMG_STAGE" -ov -format UDRW "$RAW_DMG" >/dev/null

echo "==> arranging dmg window"
# Finder names a mounted disk after its MOUNTPOINT basename, so mount at a
# path ending in the volume name for the AppleScript step below to find it.
MNT="$WORK/$APP_NAME"
hdiutil attach -mountpoint "$MNT" "$RAW_DMG" >/dev/null
# Best effort: if Finder scripting is unavailable (headless session) the dmg is
# still valid, just with default icon placement.
if osascript -e "tell application \"Finder\" to get name of startup disk" >/dev/null 2>&1; then
  osascript <<EOF || echo "warning: could not customise dmg window layout" >&2
tell application "Finder"
	delay 2
	tell disk "$APP_NAME"
		open
		set current view of container window to icon view
		set toolbar visible of container window to false
		set statusbar visible of container window to false
		set the bounds of container window to {400, 100, 920, 430}
		set theViewOptions to the icon view options of container window
		set arrangement of theViewOptions to not arranged
		set icon size of theViewOptions to 128
		set background color of theViewOptions to {65535, 65535, 65535}
		set position of item "$APP_NAME.app" of container window to {150, 180}
		set position of item "Applications" of container window to {410, 180}
		update without registering applications
		delay 2
		close
	end tell
end tell
EOF
fi
hdiutil detach "$MNT" >/dev/null

mkdir -p "$REPO_ROOT/dist"
FINAL_DMG="$REPO_ROOT/dist/$DMG_NAME-$VERSION-osx-arm64.dmg"
rm -f "$FINAL_DMG"
echo "==> compressing to $FINAL_DMG"
hdiutil convert "$RAW_DMG" -format UDZO -o "$FINAL_DMG" >/dev/null

echo "==> done: $FINAL_DMG"
