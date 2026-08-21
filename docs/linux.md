# Running the client tier on headless Linux

macOS works, but a desktop is a hostile place for an unattended test rig: the
screen sleeps, focus moves, and an occluded window is throttled. Linux removes
all three. Everything the harness does is already frame- or tick-driven and does
not depend on window focus, so nothing about the tests changes — only where they
run.

**Untested as of writing.** The macOS path is verified; the Linux paths below are
written from the game's own behaviour and the usual Mesa/wlroots setup, and are
waiting on a box. `scripts/linux-doctor.sh` checks the prerequisites and prints
what is missing.

## What the box needs

```bash
bash scripts/linux-doctor.sh
```

1. **A client install, not a server one.** `cairn-server install` provisions the
   *dedicated server*, which has no client binary and can only drive the server
   tier. The client tier needs `cairn-cli games install <version>`, which lands in
   `~/.cairn/games/<version>`.

2. **A .NET runtime.** No SDK: suites are compiled inside the game by the Roslyn
   it already carries for source mods. Cairn provisions a private runtime if the
   box has none.

3. **A display.** No offscreen mode exists — `ClientProgramArgs` has no such
   option and the client is GLFW/OpenGL. Two headless options:

   | `VSTK_DISPLAY` | needs | notes |
   |---|---|---|
   | `xvfb` | nothing | Software GL via Mesa llvmpipe, which advertises 4.5 and so clears the 4.3 the client asks for. Slow but steady. |
   | `wayland-headless` | a GPU | Hardware GL through sway's headless backend. Add `VSTK_NVIDIA=1` for the proprietary driver, which wlroots needs `--unsupported-gpu` for. |

   ```bash
   sudo apt install xvfb mesa-utils libgl1-mesa-dri   # software path
   sudo apt install sway                              # GPU path
   ```

4. **A session key.** The testkit runs the client offline and never validates
   anything, but the client still checks the key's *signature* locally before any
   network call and goes to the login screen if that fails. Copy
   `~/.cairn/session.json` from a machine that has logged in. It is never sent
   anywhere.

## Running

```bash
VSTK_DISPLAY=wayland-headless VSTK_NVIDIA=1 bash scripts/boot.sh --client
bash scripts/run.sh tests/selftest --client
```

`boot.sh` prints the strategy it chose and the display it is using. `auto`
(the default) picks `native` on macOS, `existing` if `DISPLAY` or
`WAYLAND_DISPLAY` is already set, and `xvfb` otherwise.

## Gotchas found in the game's code

- **`OPENTK_4_USE_WAYLAND`.** `RuntimeEnv` force-sets it to `0` on a Wayland
  session unless it is already set, silently dropping the client onto XWayland.
  The `wayland-headless` strategy sets it to `1`.
- **GL version negotiation.** `ClientSettings.GlContextVersion` defaults to 4.3
  and `AttemptToOpenWindow` retries downward on failure, so a weaker context
  degrades rather than crashes — but check `glxinfo -B` rather than assume.
- **`ContextFlags.ForwardCompatible` is set on Mac only.** Apple caps at GL 4.1,
  so macOS and Linux genuinely run different contexts. Combined with `GLLineWidth`
  and `SmoothLines` being no-ops on Mac, rendering differs between the two
  platforms — which is why pixel baselines have to be keyed by platform and
  `GL_RENDERER` rather than shared.
