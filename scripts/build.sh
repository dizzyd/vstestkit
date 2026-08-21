#!/usr/bin/env bash
# Builds the testkit mod into bin/<config>/Mods/vstestkit.
source "$(dirname "$0")/common.sh"
resolve_vintage_story

CONFIG="${1:-Debug}"
cd "$VSTK_ROOT"
dotnet build VsTestkit/VsTestkit.csproj -c "$CONFIG"
echo
echo "mod path (pass this to --addModPath): $VSTK_ROOT/VsTestkit/bin/$CONFIG/Mods"
