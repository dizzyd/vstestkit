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
#
# Both virtual strategies are per-slot: xvfb walks up from VSTK_XDISPLAY (99) to
# the first display number it can actually start on, and wayland-headless names
# its socket after the slot. `existing` is shared by construction - a real X
# server hosts as many client windows as the GPU has memory for, and nothing in
# the harness depends on focus or on being unoccluded.

VSTK_DISPLAY="${VSTK_DISPLAY:-auto}"

resolve_display_strategy() {
    if [ "$VSTK_DISPLAY" != "auto" ]; then echo "$VSTK_DISPLAY"; return; fi
    if [ "$(uname)" = "Darwin" ]; then echo "native"; return; fi
    if [ -n "${WAYLAND_DISPLAY:-}" ] || [ -n "${DISPLAY:-}" ]; then echo "existing"; return; fi
    echo "xvfb"
}

# Starts whatever the strategy needs and exports the environment for the client.
# Anything it launches gets its pid recorded so stop.sh can clean up.
#
# Reports through VSTK_DISPLAY_STRATEGY rather than stdout, and so must be called
# directly: `$(start_display)` runs it in a subshell, where every export it makes
# - DISPLAY above all - dies with that subshell. Only the `existing` strategy
# survives that, because there the variables were already in the environment,
# which is why it went unnoticed.
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

            # A fixed :99 is one slot's display, and on a box running several
            # slots the second Xvfb simply loses. Walk upward until one starts
            # and answers - claiming a number and launching cannot be made
            # atomic across processes, so treat losing the race as a retry
            # rather than as a boot failure.
            local num="" n
            for n in $(seq "${VSTK_XDISPLAY:-99}" $(( ${VSTK_XDISPLAY:-99} + 15 ))); do
                [ -e "/tmp/.X$n-lock" ] && continue
                Xvfb ":$n" -screen 0 "${w}x${h}x24" +extension GLX +render -noreset \
                    > "$VSTK_RUN/display.out" 2>&1 &
                local xpid=$!
                local ok=0
                for _ in $(seq 1 50); do
                    kill -0 "$xpid" 2>/dev/null || break
                    if xdpyinfo -display ":$n" >/dev/null 2>&1; then ok=1; break; fi
                    sleep 0.1
                done
                if [ "$ok" = "1" ]; then
                    echo "$xpid" > "$VSTK_RUN/display.pid"
                    num="$n"
                    break
                fi
                kill "$xpid" 2>/dev/null || true
            done
            [ -n "$num" ] || die "no free X display in ${VSTK_XDISPLAY:-99}..$(( ${VSTK_XDISPLAY:-99} + 15 )); see $VSTK_RUN/display.out"

            export DISPLAY=":$num"
            # Xvfb's own GLX is ancient; Mesa's llvmpipe is what actually supplies
            # the 4.3 context the client asks for.
            export LIBGL_ALWAYS_SOFTWARE=1
            export GALLIUM_DRIVER="${GALLIUM_DRIVER:-llvmpipe}"
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

            # Name the socket after the slot. Left unset, wlroots takes the first
            # free wayland-N for the compositor while the client, having no
            # WAYLAND_DISPLAY either, defaults to wayland-0 - so on a box running
            # two slots both clients land on whichever compositor started first.
            export WAYLAND_DISPLAY="wayland-vstk-$VSTK_SLOT"

            sway $swayargs > "$VSTK_RUN/display.out" 2>&1 &
            echo $! > "$VSTK_RUN/display.pid"

            for _ in $(seq 1 100); do
                [ -S "$XDG_RUNTIME_DIR/$WAYLAND_DISPLAY" ] && break
                sleep 0.1
            done
            [ -S "$XDG_RUNTIME_DIR/$WAYLAND_DISPLAY" ] \
                || die "sway did not create $XDG_RUNTIME_DIR/$WAYLAND_DISPLAY; see $VSTK_RUN/display.out"

            # RuntimeEnv force-sets OPENTK_4_USE_WAYLAND=0 on a Wayland session
            # unless it is already set, which silently drops the client onto
            # XWayland. Setting it to 1 keeps the native backend.
            export OPENTK_4_USE_WAYLAND=1
            ;;

        *)
            die "unknown VSTK_DISPLAY '$strategy' (native, existing, xvfb, wayland-headless)"
            ;;
    esac

    VSTK_DISPLAY_STRATEGY="$strategy"
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
