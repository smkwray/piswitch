#!/bin/sh
set -eu

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
EXAMPLES="$ROOT/config/examples"
INSTANCES="$ROOT/config/instances"

mkdir -p "$INSTANCES"

copy_if_missing() {
  name="$1"
  src="$EXAMPLES/$name.json"
  dst="$INSTANCES/$name.json"

  if [ ! -f "$src" ]; then
    echo "missing example: $src" >&2
    return 1
  fi

  if [ -f "$dst" ]; then
    echo "kept existing $dst"
  else
    cp "$src" "$dst"
    echo "created $dst"
  fi
}

copy_if_missing default
copy_if_missing messaging
copy_if_missing finder-groups
