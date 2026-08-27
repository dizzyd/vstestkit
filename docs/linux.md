# Running the client tier on headless Linux

macOS works, but a desktop is a hostile place for an unattended test rig: the
screen sleeps, focus moves, and an occluded window is throttled. Linux removes
all three. Everything the harness does is already frame- or tick-driven and does
not depend on window focus, so nothing about the tests changes — only where they
run.

**Verified** on Ubuntu 24.04 with a passed-through GTX 1060: 22 passed, client
tier included, fully headless over SSH, hardware GL 4.6. `scripts/linux-doctor.sh`
checks a box and prints what is missing.

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

3. **A display *with a monitor on it*.** No offscreen mode exists —
   `ClientProgramArgs` has no such option and the client is GLFW/OpenGL.

   **The trap:** a headless X server on a GPU with nothing plugged in serves GLX
   perfectly — `glxinfo` reports hardware GL 4.6 — and reports **zero RandR
   monitors**. GLFW enumerates RandR outputs and calls `glfwGetPrimaryMonitor()`,
   which returns NULL, and the client dies before opening a window with

   ```
   System.ArgumentNullException: Value cannot be null. (Parameter 'handle')
      at OpenTK.Windowing.Desktop.MonitorInfo..ctor(MonitorHandle handle)
      at OpenTK.Windowing.Desktop.Monitors.GetPrimaryMonitor()
   ```

   which says nothing about monitors. A healthy `glxinfo` proves nothing here;
   check `xrandr --listmonitors`.

   On NVIDIA, force an output (this is what the verified box uses, with a real
   Xorg on the GPU and no cable attached):

   ```
   Section "Device"
       Identifier "nvidia"
       Driver     "nvidia"
       BusID      "PCI:1:0:0"
       Option     "AllowEmptyInitialConfiguration" "true"
       Option     "ConnectedMonitor" "DFP-0"
       Option     "ModeValidation" "NoEdidModes, AllowNonEdidModes, NoMaxPClkCheck"
   EndSection
   ```

   with a `Monitor` section supplying a modeline and the `Screen` referencing it.
   `AllowEmptyInitialConfiguration` alone gets X started; `ConnectedMonitor` is
   what makes it usable by a GUI app.

   If there is no GPU, the alternatives:

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

With a real X server on the GPU, `existing` is the strategy and there is nothing
to configure:

```bash
bash scripts/sync-linux.sh dizzyd@vsclient.home     # from the workstation
ssh dizzyd@vsclient.home 'cd vstestkit && bash scripts/run.sh tests/selftest --client'
```

If more than one mod is under test on the box at a time, give each a **slot** —
`sync-linux.sh --mod ../olla/olla` pushes to `~/vstestkit-olla` and everything run
from that tree keys itself to slot `olla`. `scripts/slots` says what is live,
`VSTK_MAX_CLIENTS` (3) caps concurrent client sessions, and each session reserves
its own game port instead of assuming 42420. README's "Sharing a box" has the
whole of it.

The box's ceiling is memory and VRAM, not the harness: a client session is a full
game process, and the verified box has 15 GB and a 6 GB card.

Otherwise name a strategy:

```bash
VSTK_DISPLAY=xvfb bash scripts/boot.sh --client              # software GL
VSTK_DISPLAY=wayland-headless VSTK_NVIDIA=1 bash scripts/boot.sh --client
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

## Seeing the screen from elsewhere

```bash
VSTK_HOST=dizzyd@vsclient.home bash scripts/look.sh --slot olla
```

Captures on the box and copies the PNG back. Verified against this VM: a scene
built through the endpoint, aimed at, and photographed, hardware-rendered on the
1060 with no display attached.

## The verified box

| | |
|---|---|
| host | Ubuntu 24.04.4, 8 cores, 15 GB, GTX 1060 6GB passed through |
| driver | NVIDIA 580.173.02 |
| X | `headless-x.service`, Xorg on `PCI:1:0:0`, `DISPLAY=:0`, no cable |
| GL | `4.6.0 NVIDIA 580.173.02`, renderer `NVIDIA GeForce GTX 1060 6GB/PCIe/SSE2` |
| provisioning | `cairn-cli games install 1.22.6`, `cairn-cli runtimes install 10` |
| result | 22 passed / 1 skipped, ~16s a run, ~34s first boot |

No SDK, no Xvfb, no compositor. The renderer string above is the key a pixel
baseline would be filed under.
