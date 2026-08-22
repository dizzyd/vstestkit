#!/usr/bin/env bash
#
# Install the vintagestory-test skill for Claude Code.
#
#   bash scripts/install-skill.sh          install or update
#   bash scripts/install-skill.sh --check  report drift, install nothing
#
# Copies rather than symlinks. Symlinked skill directories are not picked up by
# the skill scanner - a symlinked install is silently invisible, which is a worse
# failure than drift because nothing tells you.
#
# The cost of copying is that the installed copy can fall behind the repo, so
# --check exists to catch that, and this script is safe to re-run any time.
#
source "$(dirname "$0")/common.sh"

SRC="$VSTK_ROOT/skill"
DEST="${VSTK_SKILL_DIR:-$HOME/.claude/skills}/vintagestory-test"

[ -f "$SRC/SKILL.md" ] || die "no $SRC/SKILL.md"

if [ "${1:-}" = "--check" ]; then
    [ -f "$DEST/SKILL.md" ] || { echo "not installed: $DEST"; exit 1; }
    if diff -q "$SRC/SKILL.md" "$DEST/SKILL.md" >/dev/null; then
        echo "up to date: $DEST"
        exit 0
    fi
    echo "installed skill differs from the repo; re-run without --check" >&2
    diff -u "$DEST/SKILL.md" "$SRC/SKILL.md" | head -40 >&2
    exit 1
fi

# A previous symlinked install has to go, or the copy lands inside the repo.
[ -L "$DEST" ] && rm "$DEST"

mkdir -p "$DEST"
cp "$SRC/SKILL.md" "$DEST/SKILL.md"
echo "installed $DEST/SKILL.md"
