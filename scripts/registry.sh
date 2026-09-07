# Box-wide session registry. Sourced by boot.sh / stop.sh / slots, not run.
#
# The problem it solves: several agents testing several mods on one box. Each
# holds a *slot*, and the registry is what makes a slot exclusive and what caps
# how many client-tier sessions run at once. It lives at $VSTK_STATE, outside
# every checkout, because a per-tree registry cannot see the tenant next door.
#
# One file per live session, plain key=value so listing it needs nothing:
#
#     slot=olla
#     tree=/home/dizzyd/vstestkit-olla
#     mode=client
#     pid=41233
#     gameport=42421
#     started=2026-08-26T20:31:02Z
#
# Liveness is `kill -0 pid`, the same test boot.sh already applies to its own
# pidfile, so a session killed with -9 or lost to a reboot leaves nothing to
# clean up by hand: the next claim reaps it. A held lock is *not* how a session
# is tracked - flock is not on macOS, and an fd held across nohup into a
# backgrounded game is a thing nobody should have to reason about later.

VSTK_MAX_CLIENTS="${VSTK_MAX_CLIENTS:-3}"   # concurrent client-tier sessions
VSTK_WAIT="${VSTK_WAIT:-600}"               # seconds to queue for one; 0 = fail

sessions_dir() { echo "$VSTK_STATE/sessions"; }
session_file() { echo "$(sessions_dir)/$1.session"; }

# mkdir is the atomic primitive both platforms have. The critical section is a
# few file operations, so a lock that outlives a minute belonged to something
# that died holding it and there is no honest reason to keep waiting on it.
lock_registry() {
    local d="$VSTK_STATE/registry.lock" i=0
    mkdir -p "$VSTK_STATE"
    while ! mkdir "$d" 2>/dev/null; do
        if [ -n "$(find "$d" -maxdepth 0 -mmin +1 2>/dev/null)" ]; then
            rm -rf "$d"
            continue
        fi
        sleep 0.2
        i=$((i + 1))
        [ "$i" -gt 150 ] && die "could not take the registry lock ($d)"
    done
    return 0
}

unlock_registry() { rm -rf "$VSTK_STATE/registry.lock"; }

session_field() { grep "^$2=" "$1" 2>/dev/null | head -1 | cut -d= -f2- ; }

session_live() {
    local pid; pid="$(session_field "$1" pid)"
    [ -n "$pid" ] && kill -0 "$pid" 2>/dev/null
}

# Caller holds the lock.
reap_sessions() {
    local f
    for f in "$(sessions_dir)"/*.session; do
        [ -e "$f" ] || continue
        session_live "$f" || rm -f "$f"
    done
}

# Caller holds the lock. Slot names of live sessions, one per line; with an
# argument, only those in that mode.
live_slots() {
    local f want="${1:-}"
    for f in "$(sessions_dir)"/*.session; do
        [ -e "$f" ] || continue
        [ -n "$want" ] && [ "$(session_field "$f" mode)" != "$want" ] && continue
        session_field "$f" slot
    done
}

# Caller holds the lock. One key's value across every live session.
live_field() {
    local f
    for f in "$(sessions_dir)"/*.session; do
        [ -e "$f" ] || continue
        session_field "$f" "$1"
    done
}

# Caller holds the lock.
set_field() {
    local f; f="$(session_file "$VSTK_SLOT")"
    [ -e "$f" ] || return 0
    grep -v "^$1=" "$f" > "$f.tmp" || true
    echo "$1=$2" >> "$f.tmp"
    mv "$f.tmp" "$f"
}

count_lines() { printf '%s\n' "$1" | grep -c . || true; }

write_session() {
    local mode="$1" f; f="$(session_file "$VSTK_SLOT")"
    mkdir -p "$(sessions_dir)"
    {
        echo "slot=$VSTK_SLOT"
        echo "tree=$VSTK_ROOT"
        echo "mode=$mode"
        echo "pid=$$"
        echo "started=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    } > "$f"
}

# Takes the slot for this tree, queueing if the box is already running its
# maximum number of client-tier sessions. Dies if the slot itself is busy -
# that is a mistake to fix, not a queue to join.
claim_slot() {
    local mode="$1" busy="" holders="" n=0 waited=0 announced=0

    while :; do
        lock_registry
        reap_sessions

        busy=""; holders=""; n=0
        local f; f="$(session_file "$VSTK_SLOT")"
        if [ -e "$f" ]; then
            busy="slot '$VSTK_SLOT' is already live (pid $(session_field "$f" pid), $(session_field "$f" mode) tier, tree $(session_field "$f" tree)).
  stop it:        cd $(session_field "$f" tree) && bash scripts/stop.sh
  or use another: VSTK_SLOT=<name> ..."
        elif [ "$mode" = "client" ]; then
            holders="$(live_slots client)"
            n="$(count_lines "$holders")"
        fi

        if [ -z "$busy" ] && { [ "$mode" != "client" ] || [ "$n" -lt "$VSTK_MAX_CLIENTS" ]; }; then
            write_session "$mode"
            unlock_registry
            VSTK_CLAIMED=1
            return 0
        fi
        unlock_registry

        [ -n "$busy" ] && die "$busy"

        # Over the client cap. Two clients on one GPU is fine; six is a box that
        # swaps instead of running tests, and a run that waits its turn beats one
        # that fails a suite for want of VRAM.
        [ "$VSTK_WAIT" = "0" ] && die "the box is running its maximum $VSTK_MAX_CLIENTS client sessions ($(echo $holders)); VSTK_MAX_CLIENTS raises it, VSTK_WAIT queues"
        [ "$waited" -ge "$VSTK_WAIT" ] && die "waited ${VSTK_WAIT}s for a client slot; $VSTK_MAX_CLIENTS still in use ($(echo $holders))"

        # stderr, not stdout: run.sh boots with >/dev/null, and a queue that
        # says nothing for ten minutes reads as a hang.
        if [ $((waited % 15)) = 0 ] || [ "$announced" = 0 ]; then
            echo "waiting   for a client slot - $n/$VSTK_MAX_CLIENTS in use ($(echo $holders))" >&2
            announced=1
        fi
        sleep 5
        waited=$((waited + 5))
    done
}

# The claim records the claiming shell so the cap holds across the boot itself;
# once the game is up, the game is what the entry tracks.
adopt_slot_pid() {
    lock_registry
    set_field pid "$1"
    unlock_registry
}

# Picks the TCP port the game itself will listen on and publishes it, so a
# concurrent boot picking at the same moment sees it taken. A generated
# serverconfig hardcodes 42420 for both tiers - singleplayer runs a real server
# on a real socket - so without this the second session on a box dies in
# ServerMain.startSockets with a bare "Address already in use".
reserve_game_port() {
    local base="${VSTK_GAME_PORT_BASE:-42420}" port=""
    lock_registry
    reap_sessions
    port="$(free_tcp_port "$base" $(live_field gameport) || true)"
    [ -n "$port" ] && set_field gameport "$port"
    unlock_registry

    [ -n "$port" ] || die "no free game port in $base..$((base + 63))"
    VSTK_GAME_PORT="$port"
}

release_slot() {
    lock_registry
    rm -f "$(session_file "$VSTK_SLOT")"
    unlock_registry
    VSTK_CLAIMED=0
}
