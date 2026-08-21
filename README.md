# vstestkit

A loopback RPC endpoint inside a running Vintage Story instance, so a mod can be
driven and questioned from the outside — interactively while debugging, and (from
step 2 on) as a compiled test suite.

**Development tool.** `/eval` compiles and runs arbitrary C# in the game process.
The endpoint stays off unless `VSTESTKIT=1` is in the environment. Never enable it
on a real server.

Status: **step 2 — compiled test suites, headless server.** See `STATUS.md`.

## Quick start

Run a suite — builds, boots, runs, reports, tears down, exits non-zero on failure:

```bash
bash scripts/run.sh tests/selftest/selftest.csproj
bash scripts/run.sh path/to/mod.tests.csproj --mod ../olla/olla --filter Moisture
bash scripts/run.sh <...> --keep        # leave the session up to poke at
```

Or drive a session by hand:

```bash
bash scripts/build.sh          # builds into VsTestkit/bin/Debug/Mods
bash scripts/boot.sh           # fresh flat world, fixed seed, ~6s
bash scripts/vstk info
bash scripts/stop.sh
```

`boot.sh` resolves the game install through **Cairn** (`scripts/cairn-env.sh`),
which knows each install's architecture and required .NET. Set `VINTAGE_STORY`
to override, or `VSTK_GAME_VERSION=1.22.6` to pick a specific install.

## Seeing what it drew

```bash
VSTK_HOST=dizzyd@vsclient.home bash scripts/look.sh      # -> shots/<time>.png
bash scripts/look.sh -o /tmp/now.png                     # local session
```

Captures the running session and, for a remote box, copies the image back. This
is what makes iterating on a headless machine bearable: the alternative is
asserting about pixels you have never seen.

Two things to set before a capture is worth looking at, because neither is the
harness's business to guess:

```bash
bash scripts/vstk cmd "/time set day"    # a night shot is a black rectangle
```

and point the camera - `Interact.LookAt(pos)` in a test, since a fresh client
faces wherever it happens to face.

## Verbs

```bash
vstk ping
vstk info                                  # sides attached, run phase, seed, players
vstk cmd "/time set day"                   # any chat command, structured result
vstk cmd "/gamemode creative" --as Bob     # ...as a specific player
vstk eval 'sapi.WorldManager.Seed'         # expression
vstk eval -f snippet.cs                    # or from a file
vstk eval --side client '...'              # (step 3)
vstk shot -o /tmp/x.png                    # screenshot the running client
vstk log --lines 40 --grep vstestkit
vstk stop
vstk raw <verb> '<json>'
```

`eval` also reads a snippet from stdin, which is the nicest way to write more than
one line:

```bash
bash scripts/vstk eval <<'SNIP'
var spawn = sapi.World.DefaultSpawnPosition.AsBlockPos;
int y = sapi.World.BlockAccessor.GetTerrainMapheightAt(spawn);
return new { spawn = spawn.ToString(), surfaceY = y };
SNIP
```

Snippets are compiled by the Roslyn the game already ships to build source mods,
against **every assembly currently loaded** — so the mod under test is in scope
automatically, with no configuration. Results are cached by snippet hash: first
call ~150ms, repeats ~0ms.

A snippet may be a bare expression or a statement body; the evaluator tries
expression form first and falls back, so you never have to say which.

## The test world

`boot.sh` creates a fresh world per run at a fixed seed (`VSTK_SEED`, default
424242) using the **`vstestkit-flat`** playstyle that this mod declares in
`VsTestkit/worldconfig.json`.

It exists because vanilla `creativebuilding` loads only `game` + `creative` — so
survival content would not be in the world at all, and a mod like olla (which
patches farmland) could not be tested on it. `vstestkit-flat` is `superflat`
worldgen with `["game", "creative", "survival"]` loaded: instant, deterministic,
and with the content mods actually extend.

Terrain is the three layers from `assets/creative/worldgen/layers.json` —
claystone, then soil — with the surface at y=2.

**The world origin is the middle of the map, not 0,0.** The default map is
1024000 wide, so the middle is around `512000, 2, 512000` and a block written at
`0,0,0` lands in an unloaded chunk and silently reads back as air. Inside a test,
use `P(x,y,z)`, which is plot-relative. From `eval`, work relative to
`sapi.WorldManager.MapSizeX / 2`.

Not `World.DefaultSpawnPosition`: for the first moments after world creation it
throws, because `SaveGameData.DefaultSpawn` and `mapMiddleSpawnPos` are both null
and `EntityPosFromSpawnPos` dereferences the result.

## Layout

```
VsTestkit/          the mod (universal side)
  src/              endpoint, dispatch, verbs, evaluator
  worldconfig.json  declares the vstestkit-flat playstyle
scripts/
  cairn-env.sh      resolve a Cairn-managed install into the environment
  build.sh boot.sh stop.sh vstk
run/current/        ephemeral data path for the live session (gitignored)
```

## How it talks to the game

`HttpListener` on `127.0.0.1`, gated on a per-run token. Both are published to
`<dataPath>/.vstestkit` so scripts discover the session without being told the
port.

Every verb marshals onto the correct game main thread via
`IEventAPI.EnqueueMainThreadTask` and waits for the result. Reading game state
straight off the HTTP thread is a race — the kind that shows up as an
intermittent test failure rather than a crash.

In singleplayer (step 3) the client-side and server-side mod loaders resolve the
*same* assembly, because `ModAssemblyLoader` uses `Assembly.UnsafeLoadFrom` into
the default load context. So one endpoint reaches both sides through the shared
statics in `Hub`.

## Writing tests

A test project references `VsTestkit.Runtime` and the mod under test, and nothing
else. `tests/selftest` is the worked example.

```csharp
using static VsTestkit.Testing.Vs;

public class MoistureTests
{
    [VsTest]
    public async Task OllaWetsAdjacentFarmland()
    {
        World.SetBlock("game:soil-medium-none", P(0, 0, 0));
        World.SetBlock("olla:olla-fired-red-normal", P(0, 1, 0));

        await Ticks(200);

        var fl = BE<BlockEntityFarmland>(P(0, 0, 0));
        Assert.Greater(fl.MoistureLevel, 0.5f);
    }
}
```

### The one rule

**Test bodies run on a game main thread and stay there.** That is what makes it
safe to touch live block entities, inventories and GUI dialogs directly. Only
`await` releases the thread.

So: never `Task.Run`, never `.Result`, never a raw thread. If you find yourself
off the game thread, `await OnServer()` (or `OnClient()`) to get back. Helpers
check, and say so rather than racing.

**Wait on ticks, never on wall-clock.** `await Ticks(n)` advances only when the
game loop does, so a stalled or throttled game blocks the test instead of letting
it pass by luck. `await Until(() => ..., maxTicks)` is better still when the
timing is not exactly known. `Task.Delay` in a test is a bug.

### What you get

| | |
|---|---|
| `P(x,y,z)` | position in this test's plot; `(0,0,0)` is the ground block |
| `World.` | `SetBlock`, `GetBlock`, `BlockCode`, `Fill`, `BE<T>`, `BEOrNull<T>`, `SpawnEntity`, `Entities`, `Stack`, `GroundY`, `LoadArea` |
| `Ticks(n)`, `Until(cond)`, `Hours(h)` | waiting |
| `Cmd("/give ...")` | any chat command, as console or a named player |
| `BE<T>(pos)` | block entity, with a message naming what was actually there |
| `Assert.` | `Equal`, `True`, `Greater`, `Close`, `Contains`, `Throws`, `NotNull`, … |
| `Log("...")` | a line in this test's report entry |
| `OnServer()`, `OnClient()` | switch game threads |

Attributes: `[VsTest(TimeoutMs = ...)]`, `[RequiresClient]` (skipped, not failed,
on a headless run), `[Skip("why")]`, `[PlotSize(n, height)]`, `[BeforeEach]`,
`[AfterEach]`.

### Plots

Every test gets its own 16x32x16 patch of world, cleared and floored before it
runs, 48 blocks from its neighbours. Tests share one world — regenerating per test
would dominate the run — so isolation is spatial. Tests run **serially**, on
purpose: a parallel run would trade a few seconds for failures that depend on
interleaving.

### Iterating

`--keep` leaves the session up. Rebuild the test project and reload it into the
running game — test assemblies load into a collectible context, from a byte copy
rather than the file, so a rebuilt suite replaces the old one without a restart:

```bash
bash scripts/run.sh tests/mine.csproj --keep
# edit, then:
dotnet build tests/mine.csproj -c Debug
bash scripts/vstk raw tests.load '{"path": ".../mine.dll"}'
bash scripts/vstk raw tests.run  '{"filter": "TheOneImFixing"}'
```

## Testing a mod that adds content

Mods in this workspace build code into `bin/<config>/Mods` but leave assets in the
source tree, so a content mod loaded by `--addModPath` alone registers **no blocks
at all**. `--mod <project-dir>` handles both:

```bash
bash scripts/run.sh tests/mine.csproj --mod ../olla/olla
```

`--mods DIR` and `--origin DIR` are the explicit forms.
