#!/usr/bin/env bash
#
# Build a test project, run it against a live game, report.
#
#   bash scripts/run.sh tests/selftest/selftest.csproj
#   bash scripts/run.sh path/to/mod.tests.csproj --filter Moisture
#   bash scripts/run.sh <...> --keep      reuse/leave a session running
#   bash scripts/run.sh <...> --mod DIR   load a mod project (code + assets)
#   bash scripts/run.sh <...> --mods DIR  a built Mods directory
#   bash scripts/run.sh <...> --origin DIR  an extra assets directory
#
# Exits non-zero if anything failed, so this drops into CI as-is.
#
source "$(dirname "$0")/common.sh"
resolve_vintage_story

TARGET=""; FILTER=""; KEEP=0; EXTRA_MODS=""; EXTRA_ORIGINS=""
CONFIG="${VSTK_CONFIG:-Debug}"

while [ $# -gt 0 ]; do
  case "$1" in
    --filter) FILTER="$2"; shift 2 ;;
    --keep)   KEEP=1; shift ;;
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

if [ "${TARGET##*.}" = "csproj" ]; then
    dotnet build "$VSTK_ROOT/VsTestkit/VsTestkit.csproj" -c "$CONFIG" -v quiet --nologo >/dev/null \
        || die "testkit build failed"
    dotnet build "$TARGET" -c "$CONFIG" -v quiet --nologo || die "test project build failed"

    ASM="$(python3 - "$TARGET" "$CONFIG" <<'PY'
import os, re, sys
proj, config = sys.argv[1], sys.argv[2]
text = open(proj).read()
name = (re.search(r"<AssemblyName>(.*?)</AssemblyName>", text) or [None, os.path.splitext(os.path.basename(proj))[0]])[1]
out  = (re.search(r"<OutputPath>(.*?)</OutputPath>", text) or [None, os.path.join("bin", config)])[1]
out  = out.replace("\\", "/").replace("$(Configuration)", config)
print(os.path.abspath(os.path.join(os.path.dirname(proj), out, name + ".dll")))
PY
)"
else
    ASM="$(cd "$(dirname "$TARGET")" && pwd)/$(basename "$TARGET")"
fi

[ -f "$ASM" ] || die "built test assembly not found at $ASM"

# ---- session --------------------------------------------------------------

STARTED=0
if [ -f "$(handshake_file)" ] && [ -f "$VSTK_RUN/server.pid" ] \
   && kill -0 "$(cat "$VSTK_RUN/server.pid")" 2>/dev/null; then
    echo "reusing live session"
else
    [ -n "$EXTRA_MODS" ] && export VSTK_EXTRA_MODS="$EXTRA_MODS"
    [ -n "$EXTRA_ORIGINS" ] && export VSTK_EXTRA_ORIGINS="$EXTRA_ORIGINS"
    bash "$VSTK_ROOT/scripts/boot.sh" >/dev/null || die "boot failed"
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
