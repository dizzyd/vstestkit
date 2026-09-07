#!/usr/bin/env bash
# Stop the running session, gracefully if the endpoint still answers, and give
# this slot back to the box.
source "$(dirname "$0")/common.sh"
source "$(dirname "$0")/display.sh"
source "$(dirname "$0")/registry.sh"

PEER_PIDFILE="$VSTK_RUN/peer-server.pid"

# In a multiplayer session the headless peer is a second process. It holds no display
# but it does hold the game port and the slot, so leaving it behind makes the next
# boot fail with a port already in use.
stop_peer() {
    [ -f "$PEER_PIDFILE" ] || return 0

    local peer; peer="$(cat "$PEER_PIDFILE")"

    if [ -f "$(server_handshake_file)" ]; then
        read_server_handshake 2>/dev/null || true
        curl -sS -H "X-Vstk-Token: ${VSTK_PEER_TOKEN:-}" -X POST \
             --data-binary '{}' "http://127.0.0.1:${VSTK_PEER_PORT:-0}/v1/stop" \
             >/dev/null 2>&1 || true
    fi

    for _ in $(seq 1 20); do
        kill -0 "$peer" 2>/dev/null || break
        sleep 1
    done

    kill -9 "$peer" 2>/dev/null || true
    rm -f "$PEER_PIDFILE"
}

PIDFILE="$VSTK_RUN/server.pid"
if [ ! -f "$PIDFILE" ]; then
    stop_display
    stop_peer
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
    stop_peer
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
