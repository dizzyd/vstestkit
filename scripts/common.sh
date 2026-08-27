# Shared environment resolution for the vstestkit scripts. Source, do not run.

set -euo pipefail

VSTK_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

die() { echo "error: $*" >&2; exit 1; }

# ---- slots ----------------------------------------------------------------
#
# One box, several mods under test at once. A *slot* is one concurrent tenant:
# its own checkout, its own run directory, its own display, its own entry in the
# box-wide session registry. Everything ephemeral is keyed by it, so two agents
# working on two mods never see each other's session.
#
# The name is derived from the checkout directory, because that is what an SSH
# command line already carries - `cd vstestkit-olla` is slot `olla`, with no
# environment to remember to set. VSTK_SLOT overrides.

if [ -z "${VSTK_SLOT:-}" ]; then
    _dir="$(basename "$VSTK_ROOT")"
    case "$_dir" in
        vstestkit-*) VSTK_SLOT="${_dir#vstestkit-}" ;;
        *)           VSTK_SLOT="default" ;;
    esac
    unset _dir
fi

case "$VSTK_SLOT" in
    *[!a-zA-Z0-9._-]*|"") die "VSTK_SLOT '$VSTK_SLOT' must be non-empty [a-zA-Z0-9._-]" ;;
esac

# A run directory holds one ephemeral game data path plus our own bookkeeping.
# Default keeps it inside the repo so it is gitignored and easy to inspect.
# `default` stays at run/current, the way it has always been, so a session that
# was already up when slots arrived is still the one stop.sh finds.
run_dir_for_slot() {
    case "${1:-default}" in
        default) echo "$VSTK_ROOT/run/current" ;;
        *)       echo "$VSTK_ROOT/run/$1" ;;
    esac
}

VSTK_RUN="${VSTK_RUN:-$(run_dir_for_slot "$VSTK_SLOT")}"

# Box-wide, deliberately outside any checkout: the registry has to see sessions
# started from *other* slots' trees, which is the whole point of it.
VSTK_STATE="${VSTK_STATE:-$HOME/.vstestkit}"

# Re-key this process to a different slot. For scripts with a --slot flag, which
# is parsed after common.sh has already derived one. An explicitly set VSTK_RUN
# outranks it and is left alone.
set_slot() {
    case "$1" in *[!a-zA-Z0-9._-]*|"") die "slot '$1' must be non-empty [a-zA-Z0-9._-]" ;; esac
    if [ "$VSTK_RUN" = "$(run_dir_for_slot "$VSTK_SLOT")" ]; then VSTK_RUN="$(run_dir_for_slot "$1")"; fi
    VSTK_SLOT="$1"
    export VSTK_SLOT VSTK_RUN
}

# First port at or above <base> that nothing is listening on, skipping any given
# as extra arguments. Used for the game's own TCP socket, which is a fixed 42420
# in a generated serverconfig and so is the one thing a second session on a box
# cannot share.
free_tcp_port() {
    python3 - "$@" <<'PYEOF'
import socket, sys
base  = int(sys.argv[1])
taken = {int(x) for x in sys.argv[2:] if x}
for port in range(base, base + 64):
    if port in taken:
        continue
    s = socket.socket()
    try:
        # No SO_REUSEADDR: the question is whether the game will be able to bind
        # it a moment from now, and a permissive test would answer yes for a port
        # somebody is already listening on.
        s.bind(("127.0.0.1", port))
        print(port)
        sys.exit(0)
    except OSError:
        pass
    finally:
        s.close()
sys.exit(1)
PYEOF
}

# Where a slot's checkout lives on a test box. `default` keeps the plain name so
# a single-tenant box needs no migration.
remote_dir_for_slot() {
    case "${1:-default}" in
        default) echo "vstestkit" ;;
        *)       echo "vstestkit-$1" ;;
    esac
}

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

vs_client_cmd() {
  if [ -x "$VINTAGE_STORY/Vintagestory" ]; then
    echo "$VINTAGE_STORY/Vintagestory"
  else
    echo "dotnet $VINTAGE_STORY/Vintagestory.dll"
  fi
}

handshake_sides() {
  python3 -c 'import json,sys
try: print(",".join(json.load(open(sys.argv[1]))["sides"]))
except Exception: print("")' "$(handshake_file)" 2>/dev/null
}

handshake_file() { echo "$VSTK_RUN/data/.vstestkit"; }

read_handshake() {
  local f; f="$(handshake_file)"
  [ -f "$f" ] || die "no live session (missing $f) - run scripts/boot.sh first"
  VSTK_PORT="$(python3 -c 'import json,sys;print(json.load(open(sys.argv[1]))["port"])' "$f")"
  VSTK_TOKEN="$(python3 -c 'import json,sys;print(json.load(open(sys.argv[1]))["token"])' "$f")"
  VSTK_PID="$(python3 -c 'import json,sys;print(json.load(open(sys.argv[1]))["pid"])' "$f")"
}
