#!/usr/bin/env bash
#
# Boot a test session with the testkit endpoint enabled.
#
#   bash scripts/boot.sh                 # headless server
#   bash scripts/boot.sh --client        # singleplayer client: BOTH sides, one process
#   VSTK_SEED=99 bash scripts/boot.sh    # a different world
#   VSTK_KEEP=1 bash scripts/boot.sh     # reuse the existing run dir
#   VSTK_SLOT=olla bash scripts/boot.sh  # a named slot on a shared box
#
# --client opens a real window on your desktop. There is no offscreen mode: the
# client is GLFW/OpenGL and ClientProgramArgs has no headless option.
#
# Leaves the game running in the background. Talk to it with scripts/vstk, stop
# it with scripts/stop.sh.
#
source "$(dirname "$0")/common.sh"
source "$(dirname "$0")/display.sh"
source "$(dirname "$0")/registry.sh"
resolve_vintage_story

MODE=server
[ "${1:-}" = "--client" ] && MODE=client

SEED="${VSTK_SEED:-424242}"
PLAYSTYLE="${VSTK_PLAYSTYLE:-vstestkit-flat}"
CONFIG="${VSTK_CONFIG:-Debug}"
MODPATH="$VSTK_ROOT/VsTestkit/bin/$CONFIG/Mods"
DATA="$VSTK_RUN/data"

[ -f "$MODPATH/vstestkit/VsTestkit.dll" ] || die "testkit not built - run scripts/build.sh $CONFIG"

if [ -f "$VSTK_RUN/server.pid" ] && kill -0 "$(cat "$VSTK_RUN/server.pid")" 2>/dev/null; then
    die "a session is already running (pid $(cat "$VSTK_RUN/server.pid")) - stop it first with scripts/stop.sh"
fi

# Take the slot before touching anything, and give it back if the boot does not
# finish - a slot held by a game that never started is the failure that turns a
# shared box into a queue nobody can drain.
VSTK_CLAIMED=0
BOOTED=0
on_exit() {
    [ "$VSTK_CLAIMED" = "1" ] || return 0
    [ "$BOOTED" = "1" ] && return 0
    # A boot that gave up waiting may still have left a live game behind. The
    # slot belongs to that process until stop.sh takes it down; releasing here
    # would let the next boot rm -rf the run directory underneath it.
    if [ -f "$VSTK_RUN/server.pid" ] && kill -0 "$(cat "$VSTK_RUN/server.pid")" 2>/dev/null; then
        return 0
    fi
    release_slot
}
trap on_exit EXIT

claim_slot "$MODE"
reserve_game_port

if [ "${VSTK_KEEP:-0}" != "1" ]; then
    rm -rf "$VSTK_RUN"
fi
mkdir -p "$DATA"

SERVER="$(vs_server_cmd)"

# Let the game write its own defaults, then change only what the harness needs.
# Hand-authoring the whole file would rot every time the schema moves.
if [ ! -f "$DATA/serverconfig.json" ]; then
    $SERVER --dataPath "$DATA" --genconfig >/dev/null 2>&1 || die "genconfig failed"
fi

VSTK_MODE="$MODE" VSTK_GAME_PORT="$VSTK_GAME_PORT" \
    python3 "$VSTK_ROOT/scripts/writeconfig.py" "$DATA" "$SEED" "$PLAYSTYLE" \
    || die "could not write serverconfig"

if [ "$MODE" = "client" ]; then
    # Partial is fine: SettingsBase.Load applies every default first, then
    # overlays the file, so unlisted keys keep their normal values.
    cp "$VSTK_ROOT/templates/clientsettings.json" "$DATA/clientsettings.json"

    # An ephemeral data path has no login, so the client would stop at the sign-in
    # screen. Merge just the auth keys across, the way Cairn does between packs.
    python3 "$VSTK_ROOT/scripts/session.py" "$DATA/clientsettings.json" \
        || die "cannot start a client without a login"
fi

rm -f "$DATA/.vstestkit"

echo "booting  install=$VINTAGE_STORY  mode=$MODE  slot=$VSTK_SLOT"
echo "         data=$DATA  seed=$SEED  playstyle=$PLAYSTYLE  gameport=$VSTK_GAME_PORT"

# addModPath and addOrigin are CommandLineParser sequence options: ONE flag
# followed by every path. Repeating the flag is a duplicate-option parse error,
# and the game does not report it - ServerProgram dereferences a null
# ParserResult.Value and dies with a bare NullReferenceException. Each path is
# the Mods directory itself, not the mod folder inside it.
MODPATHS=("$MODPATH")
if [ -n "${VSTK_EXTRA_MODS:-}" ]; then
    while IFS= read -r d; do [ -n "$d" ] && MODPATHS+=("$d"); done <<< "${VSTK_EXTRA_MODS//:/$'\n'}"
    echo "         extra mods=$VSTK_EXTRA_MODS"
fi

# Mods in this workspace build code into bin/<config>/Mods but leave assets in
# the source tree, so a content mod loaded by --addModPath alone registers no
# blocks at all. Its assets directory has to come in as an origin.
ORIGIN_ARGS=()
if [ -n "${VSTK_EXTRA_ORIGINS:-}" ]; then
    ORIGINS=()
    while IFS= read -r d; do [ -n "$d" ] && ORIGINS+=("$d"); done <<< "${VSTK_EXTRA_ORIGINS//:/$'\n'}"
    ORIGIN_ARGS=(--addOrigin "${ORIGINS[@]}")
    echo "         origins=$VSTK_EXTRA_ORIGINS"
fi

if [ "$MODE" = "client" ]; then
    CLIENT="$(vs_client_cmd)"

    start_display
    echo "         display=$VSTK_DISPLAY_STRATEGY${DISPLAY:+ DISPLAY=$DISPLAY}${WAYLAND_DISPLAY:+ WAYLAND_DISPLAY=$WAYLAND_DISPLAY}"

    # Run the test client OFFLINE, so it never touches your real login.
    #
    # On startup the client checks the cached session key locally (an RSA
    # signature, which a superseded key still passes) and then asks
    # auth3.vintagestory.at whether it is live. Three outcomes, from
    # SessionManager.ValidateSessionKeyWithServer:
    #
    #   valid   -> plays
    #   invalid -> NULLS the cached key and shows the login screen. Signing in
    #              there mints a new session, which supersedes the one your Cairn
    #              packs use - so playing normally then needs a re-auth.
    #   request fails -> EnumAuthServerResponse.Offline, and DoGameInitStage3
    #              carries on into the world regardless.
    #
    # The third branch is the one we want, and the URL is hardcoded, so the lever
    # is the network. VSWebClient is a plain HttpClient and honours the standard
    # proxy variables; pointing them at a closed port fails the request in this
    # process only. Nothing leaves the machine, no session is ever validated, and
    # the key Cairn tracks stays live.
    #
    # A locally valid key is still required, or the client goes straight to the
    # login screen without asking anyone - but it need not be a *live* one.
    if [ "${VSTK_ONLINE:-0}" != "1" ]; then
        export HTTPS_PROXY="http://127.0.0.1:9" https_proxy="http://127.0.0.1:9"
        export ALL_PROXY="http://127.0.0.1:9"   all_proxy="http://127.0.0.1:9"
        echo "         network=offline (auth untouched; VSTK_ONLINE=1 to allow)"
    fi

    VSTESTKIT=1 nohup $CLIENT \
        --dataPath "$DATA" \
        --openWorld vstestkit \
        --playStyle "$PLAYSTYLE" \
        --addModPath "${MODPATHS[@]}" \
        ${ORIGIN_ARGS[@]+"${ORIGIN_ARGS[@]}"} \
        > "$VSTK_RUN/server.out" 2>&1 &
else
    VSTESTKIT=1 nohup $SERVER \
        --dataPath "$DATA" \
        --addModPath "${MODPATHS[@]}" \
        ${ORIGIN_ARGS[@]+"${ORIGIN_ARGS[@]}"} \
        > "$VSTK_RUN/server.out" 2>&1 &
fi

echo $! > "$VSTK_RUN/server.pid"
PID="$(cat "$VSTK_RUN/server.pid")"
adopt_slot_pid "$PID"

# Hold the display awake for as long as the game lives, where the platform has
# such a notion. Tied to the pid rather than wrapping the launch, so stop.sh
# still owns the process directly.
[ "$MODE" = "client" ] && hold_display_awake "$PID"

# The client tier needs both sides attached. The handshake is written when the
# first side comes up and rewritten when the second joins, so waiting for the
# file alone would return before the client is usable.
WANT_SIDE=server
[ "$MODE" = "client" ] && WANT_SIDE=client

# Signing in by hand needs the window to stay up well past a normal boot.
TIMEOUT="${VSTK_BOOT_TIMEOUT:-300}"
[ "${VSTK_LOGIN:-0}" = "1" ] && TIMEOUT=900
for i in $(seq 1 "$TIMEOUT"); do
    if [ -f "$DATA/.vstestkit" ] && [[ ",$(handshake_sides)," == *",$WANT_SIDE,"* ]]; then
        read_handshake
        BOOTED=1
        echo "ready    pid=$PID port=$VSTK_PORT slot=$VSTK_SLOT sides=$(handshake_sides)  (${i}s)"
        echo
        echo "  bash scripts/vstk info"
        echo "  bash scripts/run.sh <tests.csproj>"
        echo "  bash scripts/stop.sh"
        exit 0
    fi
    if ! kill -0 "$PID" 2>/dev/null; then
        # Check the specific, common failures before dumping a screenful of
        # shader chatter that says nothing about why it stopped.
        if grep -q "Server validation response: Bad" "$DATA/Logs/client-main.log" 2>/dev/null; then
            REASON="$(grep -o "Server says: [a-z]*" "$DATA/Logs/client-main.log" | tail -1)"
            echo "the auth server rejected the session key (${REASON:-no reason given})," >&2
            echo "so the client stopped at the login screen." >&2
            echo >&2
            echo "Every Vintage Story login supersedes the previous one, so a key that is" >&2
            echo "merely old is dead even though it still looks valid locally - the check" >&2
            echo "before this one only verifies a signature." >&2
            echo >&2
            echo "Fix: log in once, then let this capture it." >&2
            echo "  VSTK_LOGIN=1 bash scripts/boot.sh --client" >&2
            echo "     waits at the login screen instead of timing out; sign in there and" >&2
            echo "     scripts/stop.sh saves the session to run/session.json for every" >&2
            echo "     later run." >&2
            exit 1
        fi

        if grep -q "GetPrimaryMonitor" "$VSTK_RUN/server.out" 2>/dev/null; then
            echo "the client could not find a monitor, so GLFW refused to open a window." >&2
            echo >&2
            echo "This is a real display being unavailable - a locked or sleeping screen," >&2
            echo "or a relaunch before the previous window finished closing. Wake the" >&2
            echo "display and try again. The client tier needs a real one; there is no" >&2
            echo "offscreen mode on macOS." >&2
            exit 1
        fi

        echo "game exited during boot; last output:" >&2
        tail -30 "$VSTK_RUN/server.out" >&2
        # A bad command line shows up as an unexplained NRE in the constructor,
        # because ParserResult.Value is null and nothing checks it.
        if grep -q "ProgramArgs\|Program..ctor" "$VSTK_RUN/server.out" 2>/dev/null; then
            echo >&2
            echo "hint: a NullReferenceException in ClientProgram/ServerProgram means the" >&2
            echo "      game could not parse its command line, not that the world is broken." >&2
        fi
        exit 1
    fi
    sleep 1
done

echo "timed out after ${TIMEOUT}s waiting for side '$WANT_SIDE'; last output:" >&2
tail -30 "$VSTK_RUN/server.out" >&2
exit 1
