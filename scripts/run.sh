#!/usr/bin/env bash
#
# Build a test project, run it against a live game, report.
#
#   bash scripts/run.sh tests/selftest              # sources, compiled in-game
#   bash scripts/run.sh tests/selftest/selftest.csproj
#   bash scripts/run.sh path/to/mod.tests.csproj --filter Moisture
#   bash scripts/run.sh <...> --client    run the client tier too
#   bash scripts/run.sh <...> --keep      reuse/leave a session running
#   bash scripts/run.sh <...> --slot N    run in a named slot on a shared box
#   bash scripts/run.sh <...> --multiplayer  server and client as two processes
#   bash scripts/run.sh <...> --mod DIR   load a mod project (code + assets)
#   bash scripts/run.sh <...> --mods DIR  a built Mods directory
#   bash scripts/run.sh <...> --origin DIR  an extra assets directory
#
# Exits non-zero if anything failed, so this drops into CI as-is.
#
source "$(dirname "$0")/common.sh"
resolve_vintage_story

TARGET=""; FILTER=""; KEEP=0; EXTRA_MODS=""; EXTRA_ORIGINS=""; CLIENT_MODE=0; MULTIPLAYER=0
CONFIG="${VSTK_CONFIG:-Debug}"

while [ $# -gt 0 ]; do
  case "$1" in
    --filter) FILTER="$2"; shift 2 ;;
    --keep)   KEEP=1; shift ;;
    --client) CLIENT_MODE=1; shift ;;
    # Two processes: a headless server and a client joined to it over a socket. The
    # suite runs in the client, and Remote.Eval reaches the server.
    --multiplayer) CLIENT_MODE=1; MULTIPLAYER=1; shift ;;
    --slot)   set_slot "$2"; shift 2 ;;
    --mods)   EXTRA_MODS="${EXTRA_MODS:+$EXTRA_MODS:}$2"; shift 2 ;;
    --origin) EXTRA_ORIGINS="${EXTRA_ORIGINS:+$EXTRA_ORIGINS:}$2"; shift 2 ;;
    # Convenience for this workspace's layout: a mod project directory holds its
    # code under bin/<config>/Mods and its assets, unbuilt, beside it.
    --mod)
      MODDIR="$(cd "$2" && pwd)"
      [ -d "$MODDIR/bin/$CONFIG/Mods" ] || die "no $MODDIR/bin/$CONFIG/Mods - build the mod first"
      EXTRA_MODS="${EXTRA_MODS:+$EXTRA_MODS:}$MODDIR/bin/$CONFIG/Mods"
      [ -d "$MODDIR/assets" ] && EXTRA_ORIGINS="${EXTRA_ORIGINS:+$EXTRA_ORIGINS:}$MODDIR/assets"
      shift 2 ;;
    -*)       die "unknown option $1" ;;
    *)        TARGET="$1"; shift ;;
  esac
done

[ -n "$TARGET" ] || die "usage: run.sh <test.csproj|test.dll> [--filter X] [--keep] [--mods DIR]"

# ---- build ----------------------------------------------------------------

# A directory of .cs files is compiled inside the game by the testkit, so a box
# with only a .NET runtime - which is what Cairn provisions - can still run a
# suite. A csproj is built here and loaded as an assembly.
if [ -d "$TARGET" ]; then
    dotnet build "$VSTK_ROOT/VsTestkit/VsTestkit.csproj" -c "$CONFIG" -v quiet --nologo >/dev/null 2>&1 \
        || echo "note: testkit not rebuilt (no SDK?); using the existing build" >&2
    ASM="$(cd "$TARGET" && pwd)"

elif [ "${TARGET##*.}" = "csproj" ]; then
    dotnet build "$VSTK_ROOT/VsTestkit/VsTestkit.csproj" -c "$CONFIG" -v quiet --nologo >/dev/null \
        || die "testkit build failed"
    dotnet build "$TARGET" -c "$CONFIG" -v quiet --nologo || die "test project build failed"

    ASM="$(dotnet msbuild "$TARGET" -nologo -getProperty:TargetPath -p:Configuration="$CONFIG")" \
        || die "could not resolve the built test assembly"
else
    ASM="$(cd "$(dirname "$TARGET")" && pwd)/$(basename "$TARGET")"
fi

[ -e "$ASM" ] || die "nothing to run at $ASM"

# ---- session --------------------------------------------------------------

STARTED=0
if [ -f "$(handshake_file)" ] && [ -f "$VSTK_RUN/server.pid" ] \
   && kill -0 "$(cat "$VSTK_RUN/server.pid")" 2>/dev/null; then
    if [ "$CLIENT_MODE" = "1" ]; then
        REQUESTED_MODE=client
        [ "$MULTIPLAYER" = "1" ] && REQUESTED_MODE=multiplayer
        LIVE_MODE="$(cat "$VSTK_RUN/session.mode" 2>/dev/null || true)"
        [ "$LIVE_MODE" = "$REQUESTED_MODE" ] || die \
            "slot '$VSTK_SLOT' is ${LIVE_MODE:-of unknown mode}, but $REQUESTED_MODE was requested; stop it with scripts/stop.sh first"
    fi
    echo "reusing live session in slot $VSTK_SLOT"
else
    [ -n "$EXTRA_MODS" ] && export VSTK_EXTRA_MODS="$EXTRA_MODS"
    [ -n "$EXTRA_ORIGINS" ] && export VSTK_EXTRA_ORIGINS="$EXTRA_ORIGINS"
    BOOT_ARGS=()
    if [ "$MULTIPLAYER" = "1" ]; then
        BOOT_ARGS+=(--multiplayer)
    elif [ "$CLIENT_MODE" = "1" ]; then
        BOOT_ARGS+=(--client)
    fi
    bash "$VSTK_ROOT/scripts/boot.sh" ${BOOT_ARGS[@]+"${BOOT_ARGS[@]}"} >/dev/null || die "boot failed"
    STARTED=1
fi

cleanup() {
    if [ "$KEEP" = "1" ]; then
        read_handshake 2>/dev/null && echo "session left running on port $VSTK_PORT (scripts/stop.sh to end it)"
    elif [ "$STARTED" = "1" ]; then
        bash "$VSTK_ROOT/scripts/stop.sh" >/dev/null 2>&1 || true
    fi
}
trap cleanup EXIT

# ---- run ------------------------------------------------------------------

RESULTS="$VSTK_RUN/results"
mkdir -p "$RESULTS"

bash "$VSTK_ROOT/scripts/vstk" raw tests.load "$(python3 -c 'import json,sys;print(json.dumps({"path":sys.argv[1]}))' "$ASM")" \
    > "$RESULTS/load.json" || { cat "$RESULTS/load.json"; die "could not load $ASM"; }

BODY='{}'
[ -n "$FILTER" ] && BODY="$(python3 -c 'import json,sys;print(json.dumps({"filter":sys.argv[1]}))' "$FILTER")"

bash "$VSTK_ROOT/scripts/vstk" raw tests.run "$BODY" > "$RESULTS/results.json" || true

python3 "$VSTK_ROOT/scripts/report.py" "$RESULTS/results.json" "$RESULTS/results.txt"
STATUS=$?

echo
echo "report: $RESULTS/results.txt"
exit $STATUS
