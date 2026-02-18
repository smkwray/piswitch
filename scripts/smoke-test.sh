#!/bin/bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
RUN_DIR="$ROOT/run"
LAUNCH="$ROOT/scripts/piswitch-launcher.sh"
BIN="$ROOT/dist/bin/piswitch"
NAMESPACE="piswitch2"
LAUNCH_LOG="$RUN_DIR/launcher.log"

instances=("default" "messaging" "finder-groups")

pid_file_for() {
    local instance="$1"
    if [ "$instance" = "default" ]; then
        echo "$RUN_DIR/$NAMESPACE.pid"
    else
        echo "$RUN_DIR/$NAMESPACE-$instance.pid"
    fi
}

is_expected_pid() {
    local pid="$1"
    local instance="$2"
    local cmd

    [[ "$pid" =~ ^[0-9]+$ ]] || return 1
    kill -0 "$pid" 2>/dev/null || return 1
    cmd="$(ps -p "$pid" -o command= 2>/dev/null || true)"
    [ -n "$cmd" ] || return 1

    case "$cmd" in
        *"$BIN"*)
            if [ "$instance" = "default" ]; then
                case "$cmd" in
                    *"--instance "*) return 1 ;;
                    *) return 0 ;;
                esac
            else
                case "$cmd" in
                    *"--instance $instance"*) return 0 ;;
                    *) return 1 ;;
                esac
            fi
            ;;
        *)
            return 1
            ;;
    esac
}

mkdir -p "$RUN_DIR"
touch "$LAUNCH_LOG"

for instance in "${instances[@]}"; do
    if [ "$instance" = "default" ]; then
        "$LAUNCH"
    else
        "$LAUNCH" "$instance"
    fi
    sleep 0.2
done

for instance in "${instances[@]}"; do
    pid_file="$(pid_file_for "$instance")"
    if [ ! -f "$pid_file" ]; then
        echo "FAIL: missing pid file for $instance ($pid_file)"
        exit 1
    fi

    pid="$(tr -d '[:space:]' < "$pid_file" || true)"
    if ! is_expected_pid "$pid" "$instance"; then
        echo "FAIL: pid mismatch for $instance ($pid)"
        exit 1
    fi
done

mark="SMOKE_SECOND_PASS_$(date +%s)"
echo "$mark" >> "$LAUNCH_LOG"

for instance in "${instances[@]}"; do
    if [ "$instance" = "default" ]; then
        "$LAUNCH"
    else
        "$LAUNCH" "$instance"
    fi
    sleep 0.2
done

after_mark="$(awk -v m="$mark" '$0 == m {f=1; next} f {print}' "$LAUNCH_LOG")"

for instance in "${instances[@]}"; do
    if ! printf '%s\n' "$after_mark" | grep -q "instance=$instance trigger "; then
        echo "FAIL: second pass did not trigger existing $instance instance"
        exit 1
    fi
    if printf '%s\n' "$after_mark" | grep -q "instance=$instance spawn "; then
        echo "FAIL: second pass spawned $instance (expected trigger)"
        exit 1
    fi
done

echo "PASS: launcher spawn/trigger behavior verified for default, messaging, finder-groups"
