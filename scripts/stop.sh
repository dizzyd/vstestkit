#!/usr/bin/env bash
# Stop the running session, gracefully if the endpoint still answers, and give
# this slot back to the box.
source "$(dirname "$0")/common.sh"
source "$(dirname "$0")/display.sh"
source "$(dirname "$0")/registry.sh"

PIDFILE="$VSTK_RUN/server.pid"
if [ ! -f "$PIDFILE" ]; then
    # No game of ours, but a registry entry can outlive one that was killed by
    # hand. Clearing it here is what keeps `scripts/slots` honest.
    release_slot
    echo "no session (slot $VSTK_SLOT released)"
    exit 0
fi
PID="$(cat "$PIDFILE")"

if [ -f "$(handshake_file)" ]; then
    bash "$VSTK_ROOT/scripts/vstk" stop >/dev/null 2>&1 || true
fi

finish() {
    stop_display
    rm -f "$PIDFILE"
    release_slot
}

for i in $(seq 1 30); do
    kill -0 "$PID" 2>/dev/null || { finish; echo "stopped   slot=$VSTK_SLOT"; exit 0; }
    sleep 1
done

echo "did not stop gracefully, killing $PID" >&2
kill -9 "$PID" 2>/dev/null || true
finish
