#!/usr/bin/env bash
#
# Checks a Linux box for what the client tier needs, and says what to install.
# Run it on the test machine before the first boot.
#
# Exits non-zero if something required is missing.
#
set -uo pipefail

FAIL=0
ok()   { printf '  ok    %s\n' "$*"; }
warn() { printf '  warn  %s\n' "$*"; }
bad()  { printf '  MISS  %s\n' "$*"; FAIL=1; }

echo "Vintage Story testkit - Linux readiness"
echo

echo "System"
echo "  $(uname -srm)"
[ -r /etc/os-release ] && echo "  $(. /etc/os-release; echo "$PRETTY_NAME")"
echo "  cores: $(nproc 2>/dev/null || echo '?')"
echo

echo "Game install (Cairn)"
CAIRN_HOME="${CAIRN_HOME:-$HOME/.cairn}"
if [ -d "$CAIRN_HOME/games" ] && ls "$CAIRN_HOME/games" >/dev/null 2>&1; then
    ok "client installs: $(ls "$CAIRN_HOME/games" | tr '\n' ' ')"
else
    # cairn-server installs a dedicated server, which has no client binary and
    # cannot drive the client tier.
    bad "no client install under $CAIRN_HOME/games"
    echo "        cairn-cli games install 1.22.6"
    [ -d "$CAIRN_HOME/servers" ] && \
      echo "        (you have servers/ - that is the dedicated server, server tier only)"
fi
echo

echo ".NET"
if command -v dotnet >/dev/null 2>&1; then
    ok "dotnet $(dotnet --version 2>/dev/null || echo '(runtime only)')"
else
    # Cairn can provision a private runtime, so this is not necessarily fatal.
    warn "no dotnet on PATH; Cairn's private runtime is used if present"
    ls -d "$CAIRN_HOME"/runtimes/* 2>/dev/null | sed 's/^/        /'
fi
echo "  note: test suites are compiled in-game, so no SDK is required"
echo

echo "Display"
if [ -n "${WAYLAND_DISPLAY:-}" ]; then ok "WAYLAND_DISPLAY=$WAYLAND_DISPLAY"
elif [ -n "${DISPLAY:-}" ];      then ok "DISPLAY=$DISPLAY"
else                                  warn "no display set; VSTK_DISPLAY=xvfb or wayland-headless"
fi
command -v Xvfb  >/dev/null 2>&1 && ok "Xvfb"  || warn "Xvfb absent      (apt install xvfb)"
command -v sway  >/dev/null 2>&1 && ok "sway"  || warn "sway absent      (apt install sway)"
command -v glxinfo >/dev/null 2>&1 && ok "glxinfo" || warn "glxinfo absent (apt install mesa-utils)"
echo

echo "OpenGL (the client asks for 4.3; a lower context makes it downgrade or fail)"
if command -v glxinfo >/dev/null 2>&1 && [ -n "${DISPLAY:-}" ]; then
    glxinfo -B 2>/dev/null | grep -E "OpenGL renderer|OpenGL core profile version|OpenGL version" \
        | sed 's/^/  /' || warn "glxinfo produced nothing"
elif command -v Xvfb >/dev/null 2>&1 && command -v glxinfo >/dev/null 2>&1; then
    echo "  probing under a temporary Xvfb..."
    Xvfb :98 -screen 0 1280x800x24 +extension GLX +render -noreset >/dev/null 2>&1 &
    XPID=$!
    sleep 2
    DISPLAY=:98 LIBGL_ALWAYS_SOFTWARE=1 glxinfo -B 2>/dev/null \
        | grep -E "OpenGL renderer|OpenGL core profile version|OpenGL version" | sed 's/^/  /' \
        || warn "no GL under Xvfb (apt install libgl1-mesa-dri)"
    kill $XPID 2>/dev/null || true
else
    warn "cannot probe GL without glxinfo"
fi
echo

echo "NVIDIA"
if command -v nvidia-smi >/dev/null 2>&1; then
    nvidia-smi --query-gpu=name,driver_version --format=csv,noheader 2>/dev/null | sed 's/^/  /'
    echo "  for hardware GL: VSTK_DISPLAY=wayland-headless VSTK_NVIDIA=1"
else
    warn "no nvidia-smi; software rendering (VSTK_DISPLAY=xvfb) is the fallback"
fi
echo

echo "Login"
if [ -s "$CAIRN_HOME/session.json" ]; then
    ok "Cairn session store present"
else
    # The client checks the key's signature locally before any network call, and
    # goes straight to the login screen if that fails - even offline.
    bad "no $CAIRN_HOME/session.json"
    echo "        The client needs a locally valid session key even though the"
    echo "        testkit runs it offline and never validates it. Copy the file"
    echo "        from a machine that has logged in."
fi
echo

[ "$FAIL" = "0" ] && echo "Ready." || echo "Missing prerequisites above."
exit $FAIL
