#!/usr/bin/env bash
# Stop the running session, gracefully if the endpoint still answers.
source "$(dirname "$0")/common.sh"

PIDFILE="$VSTK_RUN/server.pid"
[ -f "$PIDFILE" ] || { echo "no session"; exit 0; }
PID="$(cat "$PIDFILE")"

if [ -f "$(handshake_file)" ]; then
    bash "$VSTK_ROOT/scripts/vstk" stop >/dev/null 2>&1 || true
fi

for i in $(seq 1 30); do
    kill -0 "$PID" 2>/dev/null || { rm -f "$PIDFILE"; echo "stopped"; exit 0; }
    sleep 1
done

echo "did not stop gracefully, killing $PID" >&2
kill -9 "$PID" 2>/dev/null || true
rm -f "$PIDFILE"
