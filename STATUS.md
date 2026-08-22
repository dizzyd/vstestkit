# Status

Steps 1–4 are done and verified on both macOS and headless Linux; step 5 is not
started. Each section below records what that step actually proved, and the traps
found doing it — they are history, not a plan.

Build order from the design (`TESTKIT-DESIGN.md` in the anego-1.22 workspace,
which is outside this repo):

- [x] **1. Endpoint** — mod skeleton, HttpListener, `cmd`, `eval`, `vstk` CLI,
      boot/teardown. Headless server only.
- [x] **2. Suite** — `VsTestkit.Runtime`, the game-thread `SynchronizationContext`,
      `[VsTest]` runner, report, server-side helpers, per-test plots.
- [x] **3. Client tier** — client-side attach, the `ClientMain` input adapter,
      `Interact`, `Gui`, `Input`, `Shot`.
- [x] **4. Skill + proof** — the `vintagestory-test` skill and a real suite for olla.
- [~] **5. Visual baselines** — built and reliable *within a session*; a baseline
      does not yet survive a fresh world. See below.

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

## Verified working (step 3)

`--client` boots a singleplayer client, both sides in one process, ~12s to a
usable world. 22 passed / 1 skipped, repeatedly, **with the window unfocused** -
which is the condition that matters, since an unattended run never has focus.
Headless still 11 passed / 12 skipped.

Six things had to be right, and each was invisible until it wasn't:

| | |
|---|---|
| **pitch is offset by pi** | A level view is pitch ~3.1416, clamped to [1.5858, 4.6974]. Inverting `GetViewVector` naively gives `asin(dy)`, near zero and outside the range entirely. The other solution, `pi - asin(dy)`, lands mid-range; yaw is then `atan2(dx, dz)`. |
| **angles live on the entity** | `MouseYaw`/`MousePitch` are *derived from* `EntityPlayer.Pos` each frame while focused, so writing only to them is discarded next frame. Both are set. |
| **the pick ray follows the cursor when ungrabbed** | And an unfocused window leaves the cursor at 0,0, so the camera aims correctly and selects nothing. Grabbing centres the cursor as a side effect, which is the only reason a focused window works. `LookAt` centres it explicitly. |
| **clicks need `UpdateMouseButtonState`** | `OnMouseDownRaw` is the literal windowing entry point but only reaches the world through the key bindings, gated on `AllowCharacterControl`. Clicks sent that way look delivered and do nothing. |
| **selection and clicks are observed on *frames*** | Both live in `SystemMouseInWorldInteractions.OnFinalizeFrame`. An occluded window renders far slower than it ticks, so `Aim` polls and `Click` holds for frames, not ticks. |
| **an incoming chat message steals the mouse** | Auto-chat opens and focuses the chat dialog, which counts as an open Dialog, ungrabs the mouse and swallows the next click. One server message mid-test was enough. `autoChat`/`autoChatOpenSelected` are off in the template. |

Also: client UI state leaks between tests where world state does not, so plot
preparation closes open dialogs first; and `caffeinate` keeps the display awake
for the session, since GLFW refuses to open a window without a monitor and a
slept screen otherwise kills the next boot.

## Verified working (step 4)

`~/.claude/skills/vintagestory-test` documents the workflow, and `olla/tests` is a
real suite against a real mod: **7 passed**, on macOS and on the Linux box, one
command.

```bash
bash scripts/run.sh ../olla/tests --mod ../olla/olla
```

The suite covers the mod's behaviour (buried + watered olla moistens adjacent
farmland, consumes water, does nothing unburied or empty, respects its 5x5) and,
most usefully, calls the protected method the mod's Harmony patch postfixes. That
patch names `BlockEntitySoilNutrition.GetNearbyWaterDistance`, which is where the
method moved in 1.22 — aim it at a method that is not there and it attaches to
nothing, compiles, and silently does nothing. That is the failure this whole
harness exists to catch, and there is now a test for it.

Writing it against a real mod found two harness bugs that the self-tests could
not have:

- **Plots did not load their neighbouring chunk columns.** `IsFullyLoadedChunk` is
  `ServerChunk.NotAtEdge`, which requires `NeighboursLoaded == 511` — all eight.
  Vanilla farmland guards its tick that way and mods copy the idiom, so olla's
  block entity never ticked at all and the mod read as inert. `World.LoadArea`
  now takes a one-chunk margin.
- **Weather made moisture tests meaningless.** Sky-exposed farmland pulls in every
  hour of rain since its last update, so advancing the calendar wet the soil
  whatever the test did — including farmland deliberately placed out of range.
  Precipitation is now pinned to 0 unless `VSTK_WEATHER=1`.

Both were invisible in the self-tests, which never advance the calendar against a
block entity. Three of the seven olla tests would have passed vacuously.

## Step 5: what works, and what does not

**Within one session it is solid.** Repeated full runs on `vsclient.home`, 25
passed each time: an unchanged scene differs by **0.026–0.038%** and one changed
column of blocks by **8.6%**, against a 2% tolerance. Recording, comparing, diff
images, renderer-keyed paths and region cropping all behave.

**Across a fresh world it is not usable yet.** A baseline recorded in one session
and compared after a reboot differs by ~30% full-frame, or ~6% with the region
tightened onto the subject — still over tolerance, and the gap to the 8.6% signal
is too small to raise the tolerance into. Same seed, same pinned date, same plot:
the *built blocks* come out pixel-identical, but terrain colour differs (green in
one world, yellow in the next) and glass panes disagree because they show that
terrain through them.

Ruled out along the way: the calendar (pinned absolutely, verified at hour 12 of
day 500 in both), season and time-of-day, cloud rendering, weather, plot
placement, chunk-mesh settling, and the client not being told about a server-side
time change. Chunk meshes bake season at tesselation time, which is why the date
is now pinned at RunGame before any client has chunks — that fixed a different
instance of the same class of problem but not this one.

So the feature is honest for interactive use — record and compare inside one
`--keep` session while changing a renderer — and not yet for a baseline committed
to the repo. No baselines are committed for that reason.

Everything below was found by a comparison that should have been identical and
was not. Each is a way a rendered world quietly changes underneath a test:

| | |
|---|---|
| sky and cloud shadows | 35% of an unchanged frame. Comparison takes a region, defaulting to the middle half; clouds off in the template. |
| plot slots from a counter | A test sat in a different plot under `--filter` than in a full run, so the horizon differed. Slots now hash the test name — FNV-1a, because `string.GetHashCode` is randomised per process. |
| `hoursPerDay: 2400` | Inherited from vanilla creativebuilding. Silently breaks time-of-day commands: `/time set day` sets hour 12, which on a 2400-hour day is the middle of the night. Now 24. |
| the calendar advancing | 42% of an unchanged frame, all of it ground, because the season had moved and recoloured the grass. The clock is frozen at startup. |
| `/time set day` | Fast-forwards to the *next* such hour, so runs land on different absolute days and cloud shadow follows absolute time — 18% of the frame. Visual tests pin `World.SetCalendarTo`. |
| a plausible-looking constant | `12000.5` is hour 0.5 of day 500, not midday. Both baselines were black rectangles full of stars, and one still passed because the capture matched it. |
| capturing too early | Chunk meshing runs on its own threads; the first test of a run caught terrain part-built, worth 2% against a 0.5% tolerance. 20 ticks and 30 frames of settling now. |

The tolerance is 2% from measurement rather than taste: 0.5% would have sat within
a factor of two of NVIDIA's noise floor and flaked, which it duly did.

Tests are run on Linux only. The macOS client tier works but is not dependable —
the display must stay awake and the client has died silently mid-run.

## Notes for later steps

- **Screenshots do not land in the data path.** `GamePaths.Screenshots` is
  `~/Pictures/Vintagestory`, outside the ephemeral run directory. Step 3 must copy
  captures out explicitly rather than assuming they are under `--dataPath`.
- **`SystemScreenshot` self-throttles at 1000ms** and fires on a later render
  stage, so the verb has to poll for the file.
- Block writes to unloaded chunks succeed silently and read back as air. `Plots`
  force-loads and then refuses to hand out a plot whose ground height is 0.
- The client tier needs an awake, unlocked display. `boot.sh` wakes it with
  `caffeinate -u` and holds it awake with `caffeinate -d -w <pid>`, and explains
  the GLFW "no monitor" crash if it still happens.
- Vintage Story has more than one type sharing a short name across namespaces -
  the chest dialog is `Vintagestory.API.Client.GuiDialogBlockEntityInventory`,
  and `Vintagestory.GameContent` has a different one. A stray `using` binds the
  wrong type and `OfType<T>` matches nothing. `Gui` now says so explicitly.
- **Tick listeners run on a real-time clock.** `RegisterGameTickListener` intervals
  are compared against `ServerMain.totalUnpausedTime`, a `Stopwatch`, so `/time
  speed` and `CalendarSpeedMul` accelerate the calendar and nothing else — a
  5-second listener still costs 5 real seconds. `World.TickNow(pos)` rewinds a
  block entity's listeners by exactly their interval so they fire on the next
  tick with the dt they expect. Olla's suite went from 83s to 3.5s.
- `--addModPath` and `--addOrigin` are CommandLineParser *sequence* options: one
  flag, many values. Repeating the flag is a parse error, and the server reports
  it as a bare `NullReferenceException` in `ServerProgram..ctor` because nothing
  checks `ParserResult.Value` for null. `boot.sh` prints a hint when it sees that.
