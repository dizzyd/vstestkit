# Display strategy for the client tier. Sourced by boot.sh, not run directly.
#
# The client is GLFW/OpenGL and ClientProgramArgs has no offscreen mode, so it
# always needs *a* display. Which one is a per-machine decision:
#
#   native            macOS. A real window on a real desktop.
#   existing          Use whatever $DISPLAY / $WAYLAND_DISPLAY already points at.
#   xvfb              Virtual X server, software GL via Mesa llvmpipe. No GPU
#                     needed. Slow but perfectly steady, which is what matters -
#                     every wait in the harness is frame- or tick-driven.
#   wayland-headless  Headless compositor (sway) on a real GPU. Hardware GL.
#
# Pick with VSTK_DISPLAY; "auto" chooses native on macOS, existing if a display
# is already set, else xvfb.

VSTK_DISPLAY="${VSTK_DISPLAY:-auto}"

resolve_display_strategy() {
    if [ "$VSTK_DISPLAY" != "auto" ]; then echo "$VSTK_DISPLAY"; return; fi
    if [ "$(uname)" = "Darwin" ]; then echo "native"; return; fi
    if [ -n "${WAYLAND_DISPLAY:-}" ] || [ -n "${DISPLAY:-}" ]; then echo "existing"; return; fi
    echo "xvfb"
}

# Starts whatever the strategy needs and exports the environment for the client.
# Anything it launches gets its pid recorded so stop.sh can clean up.
start_display() {
    local strategy; strategy="$(resolve_display_strategy)"
    local w="${VSTK_SCREEN_WIDTH:-1280}" h="${VSTK_SCREEN_HEIGHT:-800}"

    case "$strategy" in
        native)
            # The client needs a real, awake display: GLFW asks for the primary
            # monitor and refuses to open a window without one, so a screen that
            # slept part way through a run kills the next boot. -u declares user
            # activity, which wakes it.
            if command -v caffeinate >/dev/null 2>&1; then
                caffeinate -u -t 1 >/dev/null 2>&1 || true
                sleep 1
            fi
            ;;

        existing)
            [ -n "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ] \
                || die "VSTK_DISPLAY=existing but neither DISPLAY nor WAYLAND_DISPLAY is set"
            ;;

        xvfb)
            command -v Xvfb >/dev/null 2>&1 \
                || die "Xvfb not found. apt install xvfb mesa-utils libgl1-mesa-dri"

            local num="${VSTK_XDISPLAY:-99}"
            Xvfb ":$num" -screen 0 "${w}x${h}x24" +extension GLX +render -noreset \
                > "$VSTK_RUN/display.out" 2>&1 &
            echo $! > "$VSTK_RUN/display.pid"

            export DISPLAY=":$num"
            # Xvfb's own GLX is ancient; Mesa's llvmpipe is what actually supplies
            # the 4.3 context the client asks for.
            export LIBGL_ALWAYS_SOFTWARE=1
            export GALLIUM_DRIVER="${GALLIUM_DRIVER:-llvmpipe}"

            # Give the server a moment to accept connections.
            for _ in $(seq 1 50); do
                xdpyinfo -display ":$num" >/dev/null 2>&1 && break
                sleep 0.1
            done
            ;;

        wayland-headless)
            command -v sway >/dev/null 2>&1 || die "sway not found. apt install sway"

            export WLR_BACKENDS=headless
            export WLR_LIBINPUT_NO_DEVICES=1
            export XDG_RUNTIME_DIR="${XDG_RUNTIME_DIR:-/run/user/$(id -u)}"

            # NVIDIA's proprietary driver is not what wlroots considers supported,
            # but it does work.
            local swayargs=""
            [ "${VSTK_NVIDIA:-0}" = "1" ] && swayargs="--unsupported-gpu"

            sway $swayargs > "$VSTK_RUN/display.out" 2>&1 &
            echo $! > "$VSTK_RUN/display.pid"
            sleep 2

            # RuntimeEnv force-sets OPENTK_4_USE_WAYLAND=0 on a Wayland session
            # unless it is already set, which silently drops the client onto
            # XWayland. Setting it to 1 keeps the native backend.
            export OPENTK_4_USE_WAYLAND=1
            ;;

        *)
            die "unknown VSTK_DISPLAY '$strategy' (native, existing, xvfb, wayland-headless)"
            ;;
    esac

    echo "$strategy"
}

# Holds the display awake for the life of the game, where that is a thing.
hold_display_awake() {
    local pid="$1"
    if [ "$(uname)" = "Darwin" ] && command -v caffeinate >/dev/null 2>&1; then
        caffeinate -d -w "$pid" >/dev/null 2>&1 &
    fi
}

stop_display() {
    local f="$VSTK_RUN/display.pid"
    [ -f "$f" ] || return 0
    kill "$(cat "$f")" 2>/dev/null || true
    rm -f "$f"
}
