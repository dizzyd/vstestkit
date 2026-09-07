#!/usr/bin/env bash
#
# Push the testkit to a Linux test box.
#
#   bash scripts/sync-linux.sh dizzyd@vsclient.home
#   bash scripts/sync-linux.sh dizzyd@vsclient.home --mod ../olla/olla   # slot olla
#
# The mod is plain IL targeting net10.0, so the build from a workstation runs
# unchanged on the box - which is what lets the box get away with a runtime and
# no SDK. Test suites are compiled in-game from the .cs files, so they only need
# to be copied, never built.
#
# Each slot gets its own checkout on the box - ~/vstestkit-olla, ~/vstestkit-
# packrat - and the slot name is taken from the mod being synced unless --slot
# says otherwise. One shared tree would mean each push swapping the harness
# under whoever else is mid-run, and the harness is under development too.
#
source "$(dirname "$0")/common.sh"

HOST=""; MODS=(); SLOT=""; FORCE=0
while [ $# -gt 0 ]; do
  case "$1" in
    --mod)   MODS+=("$(cd "$2" && pwd)"); shift 2 ;;
    --slot)  SLOT="$2"; shift 2 ;;
    --force) FORCE=1; shift ;;
    -*)      die "unknown option $1" ;;
    *)       HOST="$1"; shift ;;
  esac
done
HOST="${HOST:-${VSTK_HOST:-}}"
[ -n "$HOST" ] || die "usage: sync-linux.sh <user@host> [--slot NAME] [--mod <dir>]... [--force]"

# A sync with one mod names itself after that mod, which is the common case and
# saves an agent from having to remember a second flag.
if [ -z "$SLOT" ]; then
  if [ "${#MODS[@]}" = 1 ]; then SLOT="$(basename "$(dirname "${MODS[0]}")")"; else SLOT="default"; fi
fi
case "$SLOT" in *[!a-zA-Z0-9._-]*|"") die "slot '$SLOT' must be non-empty [a-zA-Z0-9._-]" ;; esac

DEST="${VSTK_REMOTE_DIR:-$(remote_dir_for_slot "$SLOT")}"

# rsync --delete into a tree whose game is mid-run replaces the suite underneath
# it. The loaded DLL survives (rsync renames, so the running process keeps its
# inode) but the .cs files a later tests.load reads do not.
LIVE="$(ssh "$HOST" "f=\$HOME/.vstestkit/sessions/$SLOT.session
  if [ -e \"\$f\" ]; then p=\$(grep '^pid=' \"\$f\" | cut -d= -f2)
    if kill -0 \"\$p\" 2>/dev/null; then echo \"\$p\"; fi
  fi" 2>/dev/null || true)"
if [ -n "$LIVE" ] && [ "$FORCE" != "1" ]; then
    die "slot '$SLOT' has a live session on $HOST (pid $LIVE).
  stop it:  ssh $HOST 'cd $DEST && bash scripts/stop.sh'
  see all:  ssh $HOST 'cd $DEST && bash scripts/slots'
  or pass --force to push over it anyway"
fi

resolve_vintage_story
dotnet build "$VSTK_ROOT/VsTestkit/VsTestkit.csproj" -c "${VSTK_CONFIG:-Debug}" -v quiet --nologo \
    || die "build failed; not shipping a stale mod"

# Run state and synchronized mods belong to this remote slot, not the local tree.
rsync -a --delete \
    --exclude 'run/' --exclude '/mods/' --exclude 'obj/' --exclude '.git/' \
    "$VSTK_ROOT/" "$HOST:$DEST/"

# A mod under test ships the same way: built IL plus its unbuilt assets, and its
# tests as sources for the game to compile.
for mod in ${MODS[@]+"${MODS[@]}"}; do
    name="$(basename "$(dirname "$mod")")"
    dotnet build "$mod"/*.csproj -c "${VSTK_CONFIG:-Debug}" -v quiet --nologo >/dev/null \
        || die "build failed for $mod"
    mod_dest="$DEST/mods/$name"
    ssh "$HOST" "mkdir -p $(printf '%q' "$mod_dest")"
    rsync -a --delete --exclude 'obj/' "$(dirname "$mod")/" "$HOST:$mod_dest/"
    echo "  mod $name -> $HOST:$mod_dest"
done

echo "synced to $HOST:$DEST  (slot $SLOT)"
echo
echo "  ssh $HOST 'cd $DEST && bash scripts/linux-doctor.sh'"
if [ "${#MODS[@]}" = 1 ]; then
    name="$(basename "$(dirname "${MODS[0]}")")"
    echo "  ssh $HOST 'cd $DEST && bash scripts/run.sh mods/$name/tests --mod mods/$name/$(basename "${MODS[0]}") --client'"
else
    echo "  ssh $HOST 'cd $DEST && bash scripts/run.sh tests/selftest --client'"
fi
echo "  ssh $HOST 'cd $DEST && bash scripts/slots'"
