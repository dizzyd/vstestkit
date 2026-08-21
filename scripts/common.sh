# Shared environment resolution for the vstestkit scripts. Source, do not run.

set -euo pipefail

VSTK_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# A run directory holds one ephemeral game data path plus our own bookkeeping.
# Default keeps it inside the repo so it is gitignored and easy to inspect.
VSTK_RUN="${VSTK_RUN:-$VSTK_ROOT/run/current}"

die() { echo "error: $*" >&2; exit 1; }

resolve_vintage_story() {
  if [ -n "${VINTAGE_STORY:-}" ]; then
    [ -f "$VINTAGE_STORY/VintagestoryServer.dll" ] \
      || die "VINTAGE_STORY=$VINTAGE_STORY has no VintagestoryServer.dll"
    return
  fi
  # Cairn owns provisioning and knows each install's architecture. Globbing for
  # *.app instead is how you launch an x64 server on an arm64 machine.
  eval "$(bash "$VSTK_ROOT/scripts/cairn-env.sh" "${VSTK_GAME_VERSION:-}")"
}

# Prefer the native apphost: it selects the right architecture and honours
# DOTNET_ROOT, including a private runtime Cairn provisioned.
vs_server_cmd() {
  if [ -x "$VINTAGE_STORY/VintagestoryServer" ]; then
    echo "$VINTAGE_STORY/VintagestoryServer"
  else
    echo "dotnet $VINTAGE_STORY/VintagestoryServer.dll"
  fi
}

handshake_file() { echo "$VSTK_RUN/data/.vstestkit"; }

read_handshake() {
  local f; f="$(handshake_file)"
  [ -f "$f" ] || die "no live session (missing $f) - run scripts/boot.sh first"
  VSTK_PORT="$(python3 -c 'import json,sys;print(json.load(open(sys.argv[1]))["port"])' "$f")"
  VSTK_TOKEN="$(python3 -c 'import json,sys;print(json.load(open(sys.argv[1]))["token"])' "$f")"
  VSTK_PID="$(python3 -c 'import json,sys;print(json.load(open(sys.argv[1]))["pid"])' "$f")"
}
