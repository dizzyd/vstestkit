---
name: vintagestory-test
description: Run in-game tests for Vintage Story mods with vstestkit — drive a live game, assert on real world state, take screenshots. Use when asked to test a mod's behaviour in-world, reproduce an in-game bug, verify a port still works, or check a change beyond "it compiles".
---

# In-game testing with vstestkit

`~/src/anego-1.22/vstestkit` runs a real Vintage Story instance and drives it: place
blocks, act as a player, open GUIs, take screenshots, assert on live world state.

A green build proves nothing about a mod. Most of what breaks between game versions —
Harmony patches aimed at moved methods, string and reflection lookups, block entity
behaviour — compiles perfectly and fails in-world. That is what this is for.

## Run a suite

```bash
cd ~/src/anego-1.22/vstestkit

bash scripts/run.sh ../olla/tests --mod ../olla/olla        # headless, server-side
bash scripts/run.sh tests/selftest --client                 # with a real client
bash scripts/run.sh <tests> --filter Moisture --keep        # one test, session left up
```

Tests are **plain `.cs` files compiled inside the game** by the Roslyn it already
carries. No build step, no SDK on the test machine. `--mod <dir>` supplies both the
mod's code and its assets — mods here build code to `bin/<config>/Mods` but leave assets
in the source tree, and a content mod loaded without its assets registers no blocks at
all.

Exit code is non-zero on failure. The report lands in `run/current/results/`.

## Where to run it

| | |
|---|---|
| **`dizzyd@vsclient.home`** | Primary. Headless Ubuntu, GTX 1060, hardware GL. Push with `scripts/sync-linux.sh dizzyd@vsclient.home`, then run over SSH. |
| **macOS** | Interactive debugging. The client opens a real window; the display must be awake and must stay awake. |

## Writing a test

```csharp
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

public class MoistureTests
{
    [VsTest(TimeoutMs = 90000)]
    public async Task OllaWetsAdjacentFarmland()
    {
        World.SetBlock("game:farmland-dry-medium", P(8, 0, 8));
        World.SetBlock("olla:olla-fired-red-buried", P(9, 0, 8));
        await Ticks(4);

        World.BE<olla.BlockEntityOllaFired>(P(9, 0, 8)).TryAddWater(60);
        await Hours(12);

        Assert.Greater(World.BE<BlockEntityFarmland>(P(8, 0, 8)).MoistureLevel, 0f);
    }
}
```

`P(x,y,z)` is a position in this test's private plot; `(0,0,0)` is its ground block.
Each test gets a cleared 16x32x16 plot 48 blocks from its neighbours, and tests run
serially.

Attributes: `[VsTest(TimeoutMs=)]`, `[RequiresClient]` (skipped, not failed, headless),
`[Skip("why")]`, `[PlotSize(n, height)]`, `[BeforeEach]`, `[AfterEach]`.

Helpers: `World.` (SetBlock, GetBlock, BlockCode, BE&lt;T&gt;, Fill, SpawnEntity, Entities,
Stack, LoadArea, SetPrecipitation) · `Player.` (StandNear, Teleport, Hold, SetGameMode) ·
`Interact.` (Aim, UseBlock, BreakBlock, LookAt) · `Gui.` (WaitFor&lt;T&gt;, Require&lt;T&gt;,
OpenDialogs, CloseDialogs) · `Input.` (Press, Click, Hotkey, MouseDown) · `Shot.Take` ·
`Ticks`, `Frames.Wait`, `Until`, `Hours`, `Cmd`, `Log` · `Assert.` incl. `ThrowsAsync`.

## The rules that actually bite

**Test bodies run on a game main thread and stay there.** That is what makes touching a
live block entity safe. Never `Task.Run`, never `.Result`, never `GetAwaiter().GetResult()`
— blocking deadlocks the thread the continuation needs. Use `Assert.ThrowsAsync`. If you
end up off-thread, `await OnServer()` / `await OnClient()`.

**Wait on ticks or frames, never wall-clock.** `Task.Delay` in a test is a bug. Use
`await Ticks(n)` for game logic and `await Frames.Wait(n)` for anything the *renderer*
has to observe — block selection and held mouse buttons are handled in a render callback,
and a throttled window renders far slower than it ticks. `await Until(cond, maxTicks)`
beats guessing a count.

**A mod that guards on `IsFullyLoadedChunk` needs its neighbours loaded.** That is
`ServerChunk.NotAtEdge`, which wants all eight surrounding chunk columns. Plot setup loads
a one-chunk margin for this; without it such code never runs and the mod merely *looks*
inert.

**Weather is off by default.** Sky-exposed farmland absorbs every hour of rain since its
last update, so advancing the calendar would wet soil regardless of what the test did.
`VSTK_WEATHER=1`, or `World.SetPrecipitation(x)`, when rain is the subject.

**Block entity tick listeners run on a real-time clock, and speeding time up does not
touch them.** `RegisterGameTickListener` intervals are measured against
`ServerMain.totalUnpausedTime`, a `Stopwatch` — so `/time speed` and `CalendarSpeedMul`
change how fast the *calendar* moves and nothing else. A 5-second listener costs 5 real
seconds regardless.

Use `await World.TickNow(pos)` to fire a block entity's listeners on demand. A listener
that works from `Calendar.TotalHours` deltas needs one firing to take a baseline and
another to act, so advance the calendar *between* two:

```csharp
await World.TickNow(ollaPos);   // baseline
await Hours(12);                // Calendar.Add - instant
await World.TickNow(ollaPos);   // does the work
```

That took olla's suite from 83s to 3.5s. Wait the interval out instead only when the
*scheduling* is what you are testing.

## Seeing the screen

```bash
VSTK_HOST=dizzyd@vsclient.home bash scripts/look.sh     # -> shots/<time>.png
```

Captures the running client and copies the PNG back. Set the scene first — a fresh client
faces an arbitrary direction, and a night shot is a black rectangle:

```bash
bash scripts/vstk cmd "/time set day"
```

## Poking a live session

```bash
bash scripts/boot.sh --client            # or without --client for headless
bash scripts/vstk info
bash scripts/vstk cmd "/give ..."
bash scripts/vstk eval 'sapi.World.Blocks.Count(b => b.Code?.Domain == "olla")'
bash scripts/vstk eval --side client 'capi.World.Player.Entity.Pos.AsBlockPos.ToString()'
bash scripts/stop.sh
```

`eval` compiles C# against everything loaded, so the mod under test is in scope with no
setup. It is the fastest way to answer "what does the game actually think is there".

With `--keep`, rebuild a suite and reload it into the running session without restarting:
test assemblies load into a collectible context.

## Reference

`vstestkit/README.md` for the full API, `docs/linux.md` for the headless box and its
traps, `STATUS.md` for what is built and what each step verified.
