#!/usr/bin/env bash
#
# Push the testkit to a Linux test box.
#
#   bash scripts/sync-linux.sh dizzyd@vsclient.home
#
# The mod is plain IL targeting net10.0, so the build from a workstation runs
# unchanged on the box - which is what lets the box get away with a runtime and
# no SDK. Test suites are compiled in-game from the .cs files, so they only need
# to be copied, never built.
#
source "$(dirname "$0")/common.sh"

HOST=""; MODS=()
while [ $# -gt 0 ]; do
  case "$1" in
    --mod) MODS+=("$(cd "$2" && pwd)"); shift 2 ;;
    *)     HOST="$1"; shift ;;
  esac
done
HOST="${HOST:-${VSTK_HOST:-}}"
[ -n "$HOST" ] || die "usage: sync-linux.sh <user@host> [--mod <dir>]..."
DEST="${VSTK_REMOTE_DIR:-vstestkit}"

resolve_vintage_story
dotnet build "$VSTK_ROOT/VsTestkit/VsTestkit.csproj" -c "${VSTK_CONFIG:-Debug}" -v quiet --nologo \
    || die "build failed; not shipping a stale mod"

# run/ is the box's own ephemeral state and must not be overwritten from here.
rsync -a --delete \
    --exclude 'run/' --exclude 'obj/' --exclude '.git/' \
    "$VSTK_ROOT/" "$HOST:$DEST/"

# A mod under test ships the same way: built IL plus its unbuilt assets, and its
# tests as sources for the game to compile.
for mod in ${MODS[@]+"${MODS[@]}"}; do
    name="$(basename "$(dirname "$mod")")"
    dotnet build "$mod"/*.csproj -c "${VSTK_CONFIG:-Debug}" -v quiet --nologo >/dev/null \
        || die "build failed for $mod"
    ssh "$HOST" "mkdir -p mods/$name"
    rsync -a --delete --exclude 'obj/' "$(dirname "$mod")/" "$HOST:mods/$name/"
    echo "  mod $name -> $HOST:mods/$name"
done

echo "synced to $HOST:$DEST"
echo
echo "  ssh $HOST 'cd $DEST && bash scripts/linux-doctor.sh'"
echo "  ssh $HOST 'cd $DEST && bash scripts/run.sh tests/selftest --client'"
