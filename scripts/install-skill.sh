#!/usr/bin/env bash
#
# Install the vintagestory-test skill for Claude Code.
#
#   bash scripts/install-skill.sh
#
# Symlinks rather than copies, so the repo stays the single source of truth and
# an edit here takes effect immediately. A copy drifts, and the drifted version
# is the one that gets read.
#
source "$(dirname "$0")/common.sh"

SRC="$VSTK_ROOT/skill"
DEST="${VSTK_SKILL_DIR:-$HOME/.claude/skills}/vintagestory-test"

[ -f "$SRC/SKILL.md" ] || die "no $SRC/SKILL.md"
mkdir -p "$(dirname "$DEST")"

if [ -L "$DEST" ]; then
    rm "$DEST"
elif [ -e "$DEST" ]; then
    BACKUP="$DEST.replaced.$(date +%s)"
    mv "$DEST" "$BACKUP"
    echo "moved the existing skill aside: $BACKUP"
fi

ln -s "$SRC" "$DEST"
echo "linked $DEST -> $SRC"

# If symlinked skills ever stop being picked up, copying is the fallback:
#   cp -R "$SRC/." "$DEST/"
