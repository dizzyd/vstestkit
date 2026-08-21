# Status

Build order from the design (`TESTKIT-DESIGN.md` in the anego-1.22 workspace):

- [x] **1. Endpoint** — mod skeleton, HttpListener, `cmd`, `eval`, `vstk` CLI,
      boot/teardown. Headless server only.
- [ ] **2. Suite** — `VsTestkit.Runtime`, the game-thread `SynchronizationContext`,
      `[VsTest]` runner, report, server-side helpers, per-test plots.
- [ ] **3. Client tier** — client-side attach, the `ClientMain` input adapter,
      `interact.*`, `gui.*`, screenshots.
- [ ] **4. Skill + proof** — the `vintagestory-test` skill and a real suite for one mod.
- [ ] **5. Pixel baselines** — keyed by platform + `GL_RENDERER`, once a Linux box
      reports one.

## Verified working (step 1)

Against 1.22.6, macOS arm64, install resolved through Cairn:

| | |
|---|---|
| boot to ready | ~6s, fresh world each time |
| `info` | run phase, seed, playstyle, players, loaded chunks |
| `cmd` | `/time set day` → `Success` + status message |
| `cmd --as` | rejects an unknown player with a 400, not a stack trace |
| `eval` expression | `sapi.WorldManager.Seed` → `424242` |
| `eval` statements | multi-line, real block placement and read-back |
| eval cache | 150ms first call, 0ms repeat |
| compile errors | reported against the snippet, one diagnostic, no wrapper noise |
| runtime errors | the snippet's own exception and message |
| auth | wrong token → 401 |

## Notes for later steps

- **Screenshots do not land in the data path.** `GamePaths.Screenshots` is
  `~/Pictures/Vintagestory`, outside the ephemeral run directory. Step 3 must copy
  captures out explicitly rather than assuming they are under `--dataPath`.
- **`SystemScreenshot` self-throttles at 1000ms** and fires on a later render
  stage, so the verb has to poll for the file.
- Block writes to unloaded chunks succeed silently and read back as air. The
  per-test plot allocator in step 2 should force-load its region.
