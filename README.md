# vstestkit

Runs a real Vintage Story instance and drives it: place blocks, act as a player,
open GUIs, take screenshots, assert on live world state.

A green build proves very little about a mod. Most of what breaks between game
versions — Harmony patches aimed at moved methods, string and reflection lookups,
block entity behaviour — compiles perfectly and fails in-world. That is what this
is for.

**Development tool.** `/eval` compiles and runs arbitrary C# in the game process.
The endpoint stays off unless `VSTESTKIT=1` is in the environment. Never enable it
on a real server.

## Quick start

Build, boot, run, report, tear down. Non-zero exit on failure.

```bash
bash scripts/run.sh tests/selftest                      # headless
bash scripts/run.sh tests/selftest --client             # with a real client
bash scripts/run.sh ../olla/tests --mod ../olla/olla    # a mod under test
bash scripts/run.sh <tests> --filter Moisture --keep    # one test, session left up
```

Tests are plain `.cs` files, compiled inside the game by the Roslyn it already
carries. There is no build step for a suite and no .NET SDK needed on the machine
running it.

Or drive a session by hand:

```bash
bash scripts/boot.sh                # headless, fresh flat world, ~6s
bash scripts/boot.sh --client       # singleplayer: both sides, one process, ~15s
bash scripts/vstk info
bash scripts/stop.sh
```

`boot.sh` resolves the game install through **Cairn** (`scripts/cairn-env.sh`),
which knows each install's architecture and required .NET. `VINTAGE_STORY`
overrides it; `VSTK_GAME_VERSION=1.22.6` picks a specific install.

Client runs normally reuse cached credentials offline. For a first login or
recovery, use `VSTK_LOGIN=1 bash scripts/boot.sh --client`: it permits an empty
cache, allows authentication traffic, and waits up to 15 minutes for sign-in.
A new login supersedes the account's previous session.

The attached client saves its session to private `run/session.json`, outside the
ephemeral run directory. Shutdown also captures newer settings written to disk;
stale settings cannot overwrite a newer live-client snapshot.

## Where to run it

| | |
|---|---|
| **Headless Linux** | The client tier's home. `docs/linux.md` covers setup and the traps. Push with `scripts/sync-linux.sh <user@host> [--mod DIR]`, then run over SSH. |
| **macOS** | Server tier only, in practice. The client tier works but is not dependable: the display must be awake and unlocked, sleeps during long sessions, and the client has died silently mid-run. Build here, run there. |

## Sharing a box

One test box, several mods under test at once. A **slot** is one tenant of it, and
everything a session owns is keyed by the slot: the checkout, the run directory,
the game's TCP port, the virtual display, the entry in the box-wide registry.

```bash
bash scripts/sync-linux.sh dizzyd@vsclient.home --mod ../olla/olla   # -> ~/vstestkit-olla
ssh dizzyd@vsclient.home 'cd vstestkit-olla && bash scripts/run.sh mods/olla/tests --mod mods/olla/olla --client'
ssh dizzyd@vsclient.home 'cd vstestkit-olla && bash scripts/slots'   # what else is running
```

The slot name comes from the checkout directory — `vstestkit-olla` is slot `olla` —
so an SSH command line that already says which tree it is in needs nothing more.
`VSTK_SLOT`, or `--slot` on `run.sh` and `look.sh`, overrides. A plain `vstestkit`
is slot `default` and keeps `run/current`, so a single-tenant box needs no
migration.

**Each slot gets its own checkout**, rather than sharing one. `sync-linux.sh`
rsyncs `--delete`, so a shared tree means every push swaps the harness under
whoever is mid-run — and the harness is under development too. It also refuses to
push into a slot whose session is live; `--force` overrides.
Synced mods live under that checkout's `mods/<name>/`, not a shared `~/mods`
directory. Two slots can therefore run different revisions of the same mod;
a harness-only sync preserves the slot's existing mod trees.

What the registry (`~/.vstestkit`, deliberately outside every checkout) buys:

- **A slot is exclusive.** A second boot in a live slot is refused, instead of
  `run.sh` reporting `reusing live session` and loading your suite into a game
  that never loaded your mod.
- **The client tier is capped**, `VSTK_MAX_CLIENTS=3` by default. Over the cap a
  `--client` boot queues, printing who holds the slots, up to `VSTK_WAIT` seconds
  (600; `0` fails immediately). Server-tier sessions are uncapped.
- **Ports are reserved, not assumed.** A generated `serverconfig` hardcodes 42420
  and singleplayer runs a real server on a real socket, so without this the second
  session on a box dies in `startSockets` with a bare `Address already in use`.
  Each session takes the first free port at or above `VSTK_GAME_PORT_BASE`. The
  testkit's own endpoint already scanned for itself.

Liveness is `kill -0` on the recorded pid, so a game killed with `-9` or lost to a
reboot leaves nothing to tidy: the next claim reaps it. `scripts/slots reap` and
`scripts/slots release <slot>` are there for when you want it now.

Two virtual displays on one box would also collide: `xvfb` walks up from
`VSTK_XDISPLAY` (99) to a number it can actually start on, and `wayland-headless`
names its socket after the slot. A real X server (`existing`) is shared as-is — it
hosts as many client windows as the GPU has memory for, and nothing in the harness
depends on focus or on being unoccluded.

## Seeing what it drew

```bash
VSTK_HOST=dizzyd@vsclient.home bash scripts/look.sh --slot olla   # -> shots/<time>.png
bash scripts/look.sh -o /tmp/now.png                              # local session
```

Captures the running client and, for a remote box, copies the image back. This is
what makes iterating on a headless machine bearable: the alternative is asserting
about pixels nobody has looked at.

Set the scene first, because neither of these is the harness's business to guess —
a fresh client faces an arbitrary direction, and a night shot is a black
rectangle:

```bash
bash scripts/vstk cmd "/time set day"
```

and aim: `Interact.LookAt(pos)` in a test, or set the player's `Pos.Yaw/Pitch`
from `eval`.

## Verbs

```bash
vstk ping
vstk info                                  # sides attached, run phase, seed, players
vstk cmd "/time set day"                   # any chat command, structured result
vstk cmd "/gamemode creative" --as Bob     # ...as a specific player
vstk eval 'sapi.WorldManager.Seed'         # expression
vstk eval --side client '...'              # on the client thread
vstk eval -f snippet.cs                    # or from a file
vstk shot -o /tmp/x.png                    # screenshot the running client
vstk log --lines 40 --grep vstestkit
vstk stop
vstk raw <verb> '<json>'                   # tests.load, tests.run, session.save, …
```

`eval` also reads from stdin, which is the nicest way to write more than one line:

```bash
bash scripts/vstk eval <<'SNIP'
var middle = new BlockPos(sapi.WorldManager.MapSizeX / 2, 0, sapi.WorldManager.MapSizeZ / 2, 0);
int y = sapi.World.BlockAccessor.GetTerrainMapheightAt(middle);
return new { middle = middle.ToString(), surfaceY = y };
SNIP
```

Snippets compile against **every assembly currently loaded**, so the mod under
test is in scope with no configuration. Cached by snippet hash: ~150ms the first
time, ~0ms after. A snippet may be a bare expression or a statement body; the
evaluator tries expression form first, so you never have to say which.

## The test world

`boot.sh` creates a fresh world per run at a fixed seed (`VSTK_SEED`, default
424242) using the **`vstestkit-flat`** playstyle this mod declares in
`VsTestkit/worldconfig.json`.

It exists because vanilla `creativebuilding` loads only `game` + `creative` — so
survival content would not be in the world at all, and a mod like olla, which
patches farmland, could not be tested on it. `vstestkit-flat` is `superflat`
worldgen with `["game", "creative", "survival"]` loaded: instant, deterministic,
and carrying the content mods actually extend.

Terrain is the three layers from `assets/creative/worldgen/layers.json` —
claystone, then soil — with the surface at y=2.

**Precipitation is pinned to 0.** Sky-exposed farmland absorbs every hour of rain
since its last update, so advancing the calendar would wet soil regardless of what
a test did. `VSTK_WEATHER=1`, or `World.SetPrecipitation(x)`, when rain is the
subject.

### When a test needs real terrain

`vstestkit-standard` is the same creative world with `worldType: "standard"` — normal
worldgen instead of superflat.

```bash
VSTK_PLAYSTYLE=vstestkit-standard bash scripts/run.sh <tests> --mod <dir> --slot <name> --client
```

A mod that reads worldgen state has no choice: `GenTerra` and every standard generator bail
unless the savegame's `WorldType` is exactly `"standard"`, so on the flat preset
`GenRockStrataNew.strata` and the `GenMaps` layers stay **null** and such a mod looks broken
when it is fine. Cover the rest of it on flat and skip those tests there with a reason -
`Skip("…")` from inside the test, since the ground is only two blocks deep there and
`P(0, 0, 0).Y` is how a test finds that out.

Two things it needs:

- **A separate `--slot`**, and its `data/Saves` wiped first. Playstyle is chosen at world
  creation, so an existing world keeps whatever it was made with.
- **`--client`, even for server-side tests.** `boot.sh` passes `--playStyle` only on the
  client branch; a headless boot ignores `VSTK_PLAYSTYLE` and silently hands back the flat
  world.

**The world origin is the middle of the map, not 0,0.** The default map is
1024000 wide, so the middle is around `512000, 2, 512000`, and a block written at
`0,0,0` lands in an unloaded chunk and silently reads back as air. Inside a test
use `P(x,y,z)`, which is plot-relative; from `eval`, work from
`sapi.WorldManager.MapSizeX / 2`.

Not `World.DefaultSpawnPosition` — for the first moments after world creation it
throws, because `SaveGameData.DefaultSpawn` and `mapMiddleSpawnPos` are both null
and `EntityPosFromSpawnPos` dereferences the result.

## Writing tests

A suite is a directory of `.cs` files. `tests/selftest` tests the harness itself;
`../olla/tests` is a real suite against a real mod.

```csharp
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

public class Irrigation
{
    [VsTest(TimeoutMs = 90000)]
    public async Task BuriedOllaMoistensNearbyFarmland()
    {
        World.SetBlock("game:farmland-dry-medium", P(8, 0, 8));
        World.SetBlock("olla:olla-fired-red-buried", P(9, 0, 8));
        await Ticks(4);

        World.BE<olla.BlockEntityOllaFired>(P(9, 0, 8)).TryAddWater(60);

        await World.TickNow(P(9, 0, 8));   // baseline firing
        await Hours(12);
        await World.TickNow(P(9, 0, 8));   // does the work

        Assert.Greater(World.BE<BlockEntityFarmland>(P(8, 0, 8)).MoistureLevel, 0f);
    }
}
```

An accompanying `.csproj` is optional and exists only so an editor can type-check
against the mod and the game; nothing builds it to run the suite.

`bash tests/runner/run.sh` exercises runner failure paths without a game process,
using a controlled game-thread queue and separately loaded fixture assembly.

### The one rule

**Test bodies run on a game main thread and stay there.** That is what makes it
safe to touch live block entities, inventories and GUI dialogs directly. Only
`await` releases the thread.

So: never `Task.Run`, never `.Result`, never `GetAwaiter().GetResult()` — blocking
deadlocks against the very thread the continuation needs. Use `Assert.ThrowsAsync`.
If you end up off-thread, `await OnServer()` or `await OnClient()` to get back.
Helpers check their side and say so rather than racing.

### Time is three clocks

**The calendar** is free: `Hours(n)` is `Calendar.Add`.

**Tick listeners** are measured against a real-time `Stopwatch`, so `/time speed`
and `CalendarSpeedMul` accelerate the calendar and nothing else — a 5-second
listener costs 5 real seconds. `await World.TickNow(pos)` fires a block entity's
listeners on demand instead. A listener working from `Calendar.TotalHours` deltas
usually needs one firing to take a baseline and another to act, so advance the
calendar *between* two.

**Rendering** is a third clock. Block selection and held mouse buttons are handled
in a render callback, and a throttled window renders far slower than it ticks, so
anything the *renderer* must observe waits on `Frames.Wait(n)`.

And in general: **wait on ticks or frames, never wall-clock.** `await Ticks(n)`
advances only when the game loop does, so a stalled game blocks the test instead
of letting it pass by luck. `await Until(cond, maxTicks)` beats guessing a count.
`Task.Delay` in a test is a bug.

### What you get

| | |
|---|---|
| `P(x,y,z)` | position in this test's plot; `(0,0,0)` is the ground block |
| `World.` | `SetBlock`, `GetBlock`, `BlockCode`, `Block`, `Fill`, `BE<T>`, `BEOrNull<T>`, `SpawnEntity`, `Entities`, `Stack`, `GroundY`, `LoadArea`, `TickNow`, `SetPrecipitation` |
| `Player.` | `Me`, `StandNear`, `Teleport`, `Hold`, `Held`, `SetGameMode` |
| `Interact.` *(client)* | `Aim`, `UseBlock`, `BreakBlock`, `LookAt` |
| `Gui.` *(client)* | `WaitFor<T>`, `WaitGone<T>`, `Require<T>`, `Find<T>`, `IsOpen<T>`, `OpenDialogs`, `CloseDialogs` |
| `Input.` *(client)* | `Press`, `KeyDown/Up`, `Type`, `Click`, `MouseDown/Up`, `Hotkey`, `RawMouseDown/Up`, `MouseMove` |
| `Shot.Take(path)` *(client)* | screenshot to a file |
| `Visual.` *(client)* | `Match`, `Capture`, `Key`, `RendererName`, `Describe` |
| `await World.SetCalendarTo(h)` | absolute calendar position - required for anything visual |
| `World.SetTimeSpeed(s)`, `World.SetPrecipitation(p)` | let the clock or the weather run |
| waiting | `Ticks(n)`, `Frames.Wait(n)`, `Until(cond)`, `Hours(h)` |
| `Cmd("/give …")` | any chat command, as console or a named player |
| `Assert.` | `Equal`, `True`, `Greater`, `Less`, `Close`, `InRange`, `Contains`, `NotNull`, `IsType<T>`, `Throws`, `ThrowsAsync`, `Fail` |
| `Log("…")` | a line in this test's report entry |
| `OnServer()`, `OnClient()` | switch game threads |
| `Skip("why")` | skip from inside the test, for what an attribute cannot know - a world too flat to dig in, say |

`Assert.Equal` compares numeric values exactly, including mixed numeric types.
Use `Assert.Close` for a tolerance: decimal `0.1m` and binary `0.1d` are not the
same exact value.

Attributes: `[VsTest(TimeoutMs = …)]`, `[RequiresClient]` (skipped, not failed, on
a headless run), `[Skip("why")]`, `[PlotSize(n, height)]`, `[BeforeEach]`,
`[AfterEach]`.
Teardown failures fail the test too; an existing body failure remains the primary
diagnostic, with the teardown exception retained in its output.
Tests and hooks must be public, non-generic instance methods with no parameters.
Use synchronous `void` or return `Task`; unsupported signatures, including
`async void`, are rejected when the suite loads.

`TimeoutMs` limits the test body itself, not its hooks. A separate overall
watchdog allows `TimeoutMs + 30,000 ms` for setup, body and teardown together.
Timeouts are observed off the game thread, including when that thread is blocked.

Client-side helpers hop to the client thread and return you to the side you
started on, so a test can stay on the server thread and still click things.

### Plots

Every test gets its own 16x32x16 patch of world, cleared and floored before it
runs, 48 blocks from its neighbours, with the player placed in it when a client is
attached. Tests share one world — regenerating per test would dominate the run —
so isolation is spatial. Tests run **serially**, on purpose: a parallel run would
trade a few seconds for failures that depend on interleaving.

Concurrent runs and reloads are rejected. A timed-out test still owns the session
until its body and teardown finish; a timeout cannot stop C# already running on a
game thread. Restart the session if that work cannot complete.

Plot setup loads a **one-chunk margin** around the plot. `IsFullyLoadedChunk` is
`ServerChunk.NotAtEdge`, which wants all eight surrounding chunk columns; vanilla
farmland guards its tick that way and mods copy the idiom, so without the margin
such code never runs and the mod merely *looks* inert.

Client UI state is reset between tests too. A dialog left open by one test is not
just untidy: any dialog preferring an ungrabbed mouse switches world interaction
off, and the next test then aims at things and selects nothing.

### Iterating

An explicit `--client` or `--multiplayer` request must match a retained session's
mode. Stop an incompatible session first; omitting both flags allows reuse of
whichever mode is already running.

`--keep` leaves the session up. Edit the sources and reload — suites load into a
collectible context, so a changed suite replaces the old one without a restart:

```bash
bash scripts/run.sh tests/mine --keep
# edit, then:
bash scripts/vstk raw tests.load '{"path": "/abs/path/tests/mine"}'
bash scripts/vstk raw tests.run  '{"filter": "TheOneImFixing"}'
```

## Visual baselines

```csharp
[VsTest, RequiresClient]
public async Task TheOverlayLooksRight()
{
    await World.SetCalendarTo(500 * 24 + 12);   // absolute: midday on day 500
    await Ticks(10);
    ... build the scene, stand somewhere, aim ...

    await Visual.Match("overlay");
}
```

`Match` captures with the HUD hidden and compares against
`baselines/<os>-<renderer>/<name>.png`. No baseline yet means one is recorded and
the test passes with a note; `VSTK_UPDATE_BASELINES=1` overwrites deliberately. A
failure writes the capture and a diff image with differing pixels marked and
everything outside the compared region darkened.

Baselines are **per renderer** because rendering genuinely differs: macOS runs a
forward-compatible GL 4.1 context and Linux 4.6, `GLLineWidth` and `SmoothLines`
are no-ops on Mac only, and the rasterisers differ regardless. One shared baseline
would mean one machine is right and the rest are red. The recorded set here is
from the Linux box.

Only the middle half of the frame is compared by default. Sky and drifting cloud
shadows are worth tens of percent of an unchanged frame; pass a `VisualRegion`,
or `wholeFrame: true` when that is really what you mean.
A region is clipped to the image, not moved onto it. Nonpositive dimensions or
an empty intersection fail rather than reporting a match without comparing pixels.

**Pin absolute time.** Not the time of day — `/time set day` fast-forwards to the
*next* such hour, so runs land on different absolute days, and sun angle, season
and cloud shadow all follow absolute time. `World.SetCalendarTo(hours)` is the
whole fix, and time of day is `hours % 24`, so a number that merely looks like
midday will photograph midnight.

Measured on the GTX 1060: within a session an unchanged scene differs by
0.03-0.04% and one changed column of blocks by 8.6%, against a 2% tolerance.

**Across a fresh world this does not hold yet.** A baseline recorded in one
session differs by ~6% after a reboot even framed tightly on the subject: the
built blocks are pixel-identical but terrain colour differs between world
instances, and glass shows that terrain through it. So record and compare inside
one `--keep` session; a baseline committed to the repo will not survive a
restart. `STATUS.md` records what has been ruled out.

## Testing a mod that adds content

Mods in this workspace build code into `bin/<config>/Mods` but leave assets in the
source tree, so a content mod loaded by `--addModPath` alone registers **no blocks
at all**. `--mod <project-dir>` supplies both:

```bash
bash scripts/run.sh ../olla/tests --mod ../olla/olla
```

`--mods DIR` and `--origin DIR` are the explicit forms.

## The Claude Code skill

`skill/SKILL.md` is the source of truth; install it with

```bash
bash scripts/install-skill.sh
bash scripts/install-skill.sh --check    # report drift
```

which copies it to `~/.claude/skills/vintagestory-test`. Copied, not symlinked: a
symlinked skill directory is not picked up by the skill scanner, and an install
that is silently invisible beats drift for sheer unhelpfulness.

## How it talks to the game

`HttpListener` on `127.0.0.1`, gated on a per-run token. Both are published to
`<dataPath>/.vstestkit` so scripts find the session without being told the port.

Every verb marshals onto the correct game main thread via
`IEventAPI.EnqueueMainThreadTask` and waits for the result. Reading game state
straight off the HTTP thread is a race — the kind that surfaces as an intermittent
test failure rather than a crash.

In singleplayer the client-side and server-side mod loaders resolve the *same*
assembly, because `ModAssemblyLoader` uses `Assembly.UnsafeLoadFrom` into the
default load context. So one endpoint reaches both sides through the shared
statics in `Hub`. (The same fact bites mods: `ModSystem.Start()` runs once per
side, so a `Harmony.PatchAll()` there registers every patch twice.)

The client runs **offline**: the proxy variables are pointed at a closed port for
that process, which fails the session check and takes
`EnumAuthServerResponse.Offline`, so the test client never validates a session and
cannot invalidate the login you play with. `VSTK_ONLINE=1` disables that.

## Layout

```
VsTestkit/            the mod (universal side)
  src/                endpoint, dispatch, verbs, evaluator, session prep
  worldconfig.json    declares the vstestkit-flat playstyle
VsTestkit.Runtime/    the public test API: Vs, World, Player, Interact, Gui, Assert
tests/selftest/       the harness testing itself
skill/                the vintagestory-test skill
scripts/
  run.sh              build, boot, run, report, tear down
  boot.sh stop.sh     session lifecycle       vstk        talk to a session
  look.sh             screenshot, locally or over SSH
  cairn-env.sh        resolve a Cairn-managed install
  display.sh          native / existing / xvfb / wayland-headless
  registry.sh slots   box-wide slot registry: exclusion, client cap, ports
  sync-linux.sh       push repo and mods to a test box
  linux-doctor.sh     check a Linux box for what the client tier needs
  install-skill.sh    install the Claude Code skill
templates/            clientsettings.json seed for an ephemeral data path
docs/linux.md         headless Linux setup
run/<slot>/           ephemeral data path for a live session (gitignored)
~/.vstestkit/         the box-wide session registry, outside every checkout
```

## License

Copyright (C) 2026 Dave (Dizzy) Smith.

GPL-3.0-or-later. See `LICENSE`.

Worth knowing before writing tests you intend to distribute: a test suite
references **`VsTestkit.Runtime`**, and linking a GPL library generally makes the
linking work a derivative of it. For suites living alongside your own mods that is
a non-issue. If you ever want third parties to write and ship test suites under
their own terms, the thing to change is `VsTestkit.Runtime` to LGPL-3.0 — the mod,
the scripts and the runner can stay GPL, because nothing links those.
