#!/usr/bin/env bash
#
# Resolve a Cairn-managed game install into the environment vstestkit needs.
#
#   eval "$(bash scripts/cairn-env.sh)"          # newest install Cairn has
#   eval "$(bash scripts/cairn-env.sh 1.22.6)"   # a specific version
#
# Cairn owns provisioning: it installs the game (and a private .NET when the box
# has none) and knows which architecture each install is. Reading its layout beats
# globbing for *.app, which is how you end up launching an x64 server on an arm64
# machine and getting "assembly architecture is not compatible".
#
# Everything here is read from the layout Cairn.Core defines (CairnPaths), so it
# stays in step with Cairn rather than duplicating install logic.
#
set -uo pipefail

WANT="${1:-}"

# CAIRN_HOME wins, then Cairn's pointer file, then the default.
if [ -z "${CAIRN_HOME:-}" ]; then
    if [ -f "$HOME/.cairn/home" ]; then
        CAIRN_HOME="$(tr -d '[:space:]' < "$HOME/.cairn/home")"
    else
        CAIRN_HOME="$HOME/.cairn"
    fi
fi

# Servers first - a machine may hold both, and a server install is the leaner
# thing to run headless. A client install contains the server binaries too, so it
# is a fine fallback on a workstation. The .app suffix is what Cairn appends on
# macOS.
find_install() {
    local v="$1"
    for cand in "$CAIRN_HOME/servers/$v" "$CAIRN_HOME/servers/$v.app" \
                "$CAIRN_HOME/games/$v"   "$CAIRN_HOME/games/$v.app"; do
        if [ -f "$cand/VintagestoryServer.dll" ]; then echo "$cand"; return 0; fi
    done
    return 1
}

if [ -n "$WANT" ]; then
    INSTALL="$(find_install "$WANT")" || {
        echo "no Vintage Story $WANT under $CAIRN_HOME/{servers,games}" >&2
        echo "install it with: cairn-cli games install $WANT" >&2
        exit 1
    }
    VERSION="$WANT"
else
    VERSION="$(ls -1 "$CAIRN_HOME/servers" "$CAIRN_HOME/games" 2>/dev/null \
               | sed 's/\.app$//' \
               | grep -E '^[0-9]+\.[0-9]+\.[0-9]+' | sort -V | tail -1)"
    [ -n "$VERSION" ] || {
        echo "no game installs under $CAIRN_HOME/{servers,games}" >&2
        echo "install one with: cairn-cli games install <version>" >&2
        exit 1
    }
    INSTALL="$(find_install "$VERSION")" || { echo "install for $VERSION is incomplete" >&2; exit 1; }
fi

# Which .NET the install actually asks for, read from its runtimeconfig rather
# than assumed from the game version - the mapping has already changed once
# (1.21 wanted 8, 1.22 wants 10) and will again.
NEED_MAJOR="$(python3 -c '
import json,sys,os
d=sys.argv[1]
for name in ("VintagestoryServer.runtimeconfig.json","Vintagestory.runtimeconfig.json"):
    p=os.path.join(d,name)
    if os.path.exists(p):
        try:
            v=json.load(open(p))["runtimeOptions"]["framework"]["version"]
            print(v.split(".")[0]); break
        except Exception: pass
else: print("")
' "$INSTALL")"

# A private runtime is optional: hostfxr falls back to the machine's own install,
# so an unset DOTNET_ROOT is a valid answer on a box that already has the right
# .NET. It can rescue an install that would not otherwise start, and cannot break
# one that already works.
DOTNET_DIR=""
if [ -n "$NEED_MAJOR" ] && [ -d "$CAIRN_HOME/runtimes" ]; then
    DOTNET_DIR="$(python3 -c '
import sys,os,re
root,major=sys.argv[1],sys.argv[2]
best=None
for name in os.listdir(root):
    m=re.match(r"^(\d+)\.(\d+)\.(\d+)-", name)
    if not m or m.group(1)!=major: continue
    key=tuple(int(g) for g in m.groups())
    if best is None or key>best[0]: best=(key,name)
print(os.path.join(root,best[1]) if best else "")
' "$CAIRN_HOME/runtimes" "$NEED_MAJOR")"
fi

emit() { printf 'export %s=%q\n' "$1" "$2"; }

emit CAIRN_HOME    "$CAIRN_HOME"
emit VINTAGE_STORY "$INSTALL"

if [ -n "$DOTNET_DIR" ]; then
    emit DOTNET_ROOT "$DOTNET_DIR"
    printf 'export PATH=%q:"$PATH"\n' "$DOTNET_DIR"
fi

{
    echo "# cairn home: $CAIRN_HOME"
    echo "# install:    $INSTALL (game $VERSION)"
    echo "# runtime:    ${DOTNET_DIR:-system dotnet (no private .NET $NEED_MAJOR under $CAIRN_HOME/runtimes)}"
} >&2
