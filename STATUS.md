# Status

Build order from the design (`TESTKIT-DESIGN.md` in the anego-1.22 workspace):

- [x] **1. Endpoint** — mod skeleton, HttpListener, `cmd`, `eval`, `vstk` CLI,
      boot/teardown. Headless server only.
- [x] **2. Suite** — `VsTestkit.Runtime`, the game-thread `SynchronizationContext`,
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

## Verified working (step 2)

`tests/selftest` is 13 tests covering the harness itself; 11 run, 2 are skipped to
exercise the skip paths. Three consecutive cold boots, all green, ~3.5s per run.

| | |
|---|---|
| thread discipline | a test asserts it is still on the server main thread after two awaits |
| — and that it matters | sabotaging the context reinstall makes that test fail immediately, with a message naming the cause |
| plot isolation | one test asserts a neighbour's block did not leak into its plot |
| failure messages | a test asserts the assertion text names both expected and actual |
| skip paths | `[RequiresClient]` and `[Skip]` both skip rather than fail |
| hot reload | a new test added to a **live** session, rebuilt, reloaded and run without a restart |
| real mod | olla loaded via `--mod`, its blocks registered and visible |
| exit codes | green 0, failing 1 |

## Notes for later steps

- **Screenshots do not land in the data path.** `GamePaths.Screenshots` is
  `~/Pictures/Vintagestory`, outside the ephemeral run directory. Step 3 must copy
  captures out explicitly rather than assuming they are under `--dataPath`.
- **`SystemScreenshot` self-throttles at 1000ms** and fires on a later render
  stage, so the verb has to poll for the file.
- Block writes to unloaded chunks succeed silently and read back as air. `Plots`
  force-loads and then refuses to hand out a plot whose ground height is 0.
- `--addModPath` and `--addOrigin` are CommandLineParser *sequence* options: one
  flag, many values. Repeating the flag is a parse error, and the server reports
  it as a bare `NullReferenceException` in `ServerProgram..ctor` because nothing
  checks `ParserResult.Value` for null. `boot.sh` prints a hint when it sees that.
