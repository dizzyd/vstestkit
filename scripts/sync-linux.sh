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

HOST="${1:-${VSTK_HOST:-}}"
[ -n "$HOST" ] || die "usage: sync-linux.sh <user@host>"
DEST="${VSTK_REMOTE_DIR:-vstestkit}"

resolve_vintage_story
dotnet build "$VSTK_ROOT/VsTestkit/VsTestkit.csproj" -c "${VSTK_CONFIG:-Debug}" -v quiet --nologo \
    || die "build failed; not shipping a stale mod"

# run/ is the box's own ephemeral state and must not be overwritten from here.
rsync -a --delete \
    --exclude 'run/' --exclude 'obj/' --exclude '.git/' \
    "$VSTK_ROOT/" "$HOST:$DEST/"

echo "synced to $HOST:$DEST"
echo
echo "  ssh $HOST 'cd $DEST && bash scripts/linux-doctor.sh'"
echo "  ssh $HOST 'cd $DEST && bash scripts/run.sh tests/selftest --client'"
