#!/bin/bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BIN="$ROOT/dist/bin/piswitch"
RUN_DIR="$ROOT/run"
INSTANCE="${1:-default}"
LOG_FILE="$RUN_DIR/launcher.log"

mkdir -p "$RUN_DIR"

export PISWITCH_HOME="$ROOT"
DEBUG_LOGS="${PISWITCH_DEBUG_LOGS:-}"

if [ ! -x "$BIN" ]; then
    log_line() { :; }
    exit 1
fi

HASH_FILE="$ROOT/dist/bin/.piswitch-hash"
if [ -f "$HASH_FILE" ]; then
    HASH="$(cat "$HASH_FILE")"
elif command -v shasum >/dev/null 2>&1; then
    HASH="$(shasum -a 256 "$BIN" | awk '{print substr($1,1,10)}')"
else
    HASH="$(date +%s)"
fi
NAMESPACE="piswitch-$HASH"

export PISWITCH_NAMESPACE="$NAMESPACE"

if [ "$DEBUG_LOGS" = "1" ] || [ "$DEBUG_LOGS" = "true" ] || [ "$DEBUG_LOGS" = "yes" ] || [ "$DEBUG_LOGS" = "on" ]; then
    log_line() {
        printf '%s pid=%s instance=%s %s\n' "$(date '+%Y-%m-%dT%H:%M:%S%z')" "$$" "$INSTANCE" "$1" >> "$LOG_FILE"
    }
else
    log_line() { :; }
fi

if [ "$INSTANCE" = "default" ]; then
    PID_FILE="$RUN_DIR/$NAMESPACE.pid"
    TRIGGER_FILE="$RUN_DIR/$NAMESPACE-trigger"
else
    PID_FILE="$RUN_DIR/$NAMESPACE-$INSTANCE.pid"
    TRIGGER_FILE="$RUN_DIR/$NAMESPACE-trigger-$INSTANCE"
fi

is_expected_instance_pid() {
    local pid="$1"
    [[ "$pid" =~ ^[0-9]+$ ]] || return 1
    kill -0 "$pid" 2>/dev/null || return 1
}

if [ -f "$PID_FILE" ]; then
    PID="$(tr -d '[:space:]' < "$PID_FILE" || true)"
    if is_expected_instance_pid "$PID"; then
        log_line "trigger pid=$PID file=$TRIGGER_FILE"
        printf '%s\n' "$$" > "$TRIGGER_FILE"
        exit 0
    fi
    log_line "stale-pid pid=$PID file=$PID_FILE"
    rm -f "$PID_FILE"
fi

if [ "$INSTANCE" = "default" ]; then
    log_line "spawn default"
    nohup "$BIN" >/dev/null 2>&1 &
else
    log_line "spawn instance=$INSTANCE"
    nohup "$BIN" --instance "$INSTANCE" >/dev/null 2>&1 &
fi
