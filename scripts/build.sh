#!/bin/sh
set -eu

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
cd "$ROOT"

echo "Building PiSwitch..."
swift build -c release

mkdir -p "$ROOT/dist/bin"
cp "$ROOT/.build/release/PiSwitch" "$ROOT/dist/bin/piswitch"
chmod +x "$ROOT/dist/bin/piswitch"
codesign --force --sign - "$ROOT/dist/bin/piswitch" >/dev/null 2>&1

echo "Build complete"
echo "Binary: $ROOT/dist/bin/piswitch"
echo "Launcher: $ROOT/scripts/piswitch-launcher.sh"
