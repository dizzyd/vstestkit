#!/usr/bin/env bash
#
# Take a screenshot of the running session and put it somewhere local.
#
#   bash scripts/look.sh                       # local session -> shots/<time>.png
#   bash scripts/look.sh -o /tmp/now.png
#   VSTK_HOST=dizzyd@vsclient.home bash scripts/look.sh --slot olla
#
# With VSTK_HOST set it captures on that box and copies the file back, which is
# the point: iterating on a headless machine is only tolerable if you can see
# what the game is actually drawing. --slot picks which tenant of a shared box
# to photograph; without it, this tree's own slot.
#
source "$(dirname "$0")/common.sh"

OUT=""
while [ $# -gt 0 ]; do
  case "$1" in
    -o)     OUT="$2"; shift 2 ;;
    --slot) set_slot "$2"; shift 2 ;;
    *)      OUT="$1"; shift ;;
  esac
done

STAMP="$(date +%Y%m%d-%H%M%S)"
OUT="${OUT:-$VSTK_ROOT/shots/$STAMP.png}"
mkdir -p "$(dirname "$OUT")"

if [ -n "${VSTK_HOST:-}" ]; then
    REMOTE_DIR="${VSTK_REMOTE_DIR:-$(remote_dir_for_slot "$VSTK_SLOT")}"
    # $$ as well as the stamp: two slots photographed in the same second would
    # otherwise fetch each other's frame.
    REMOTE_PNG="/tmp/vstk-look-$VSTK_SLOT-$STAMP-$$.png"

    ssh "$VSTK_HOST" "cd $REMOTE_DIR && bash scripts/vstk shot -o $REMOTE_PNG" >/dev/null \
        || die "capture failed on $VSTK_HOST"
    scp -q "$VSTK_HOST:$REMOTE_PNG" "$OUT" || die "could not copy the image back"
    ssh "$VSTK_HOST" "rm -f $REMOTE_PNG" || true
else
    bash "$VSTK_ROOT/scripts/vstk" shot -o "$OUT" >/dev/null || die "capture failed"
fi

echo "$OUT"
