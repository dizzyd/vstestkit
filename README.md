# vstestkit

A loopback RPC endpoint inside a running Vintage Story instance, so a mod can be
driven and questioned from the outside — interactively while debugging, and (from
step 2 on) as a compiled test suite.

**Development tool.** `/eval` compiles and runs arbitrary C# in the game process.
The endpoint stays off unless `VSTESTKIT=1` is in the environment. Never enable it
on a real server.

Status: **step 1 — endpoint, headless server only.** See `STATUS.md` for what is
built and what comes next.

## Quick start

```bash
bash scripts/build.sh          # builds into VsTestkit/bin/Debug/Mods
bash scripts/boot.sh           # fresh flat world, fixed seed, ~6s
bash scripts/vstk info
bash scripts/stop.sh
```

`boot.sh` resolves the game install through **Cairn** (`scripts/cairn-env.sh`),
which knows each install's architecture and required .NET. Set `VINTAGE_STORY`
to override, or `VSTK_GAME_VERSION=1.22.6` to pick a specific install.

## Verbs

```bash
vstk ping
vstk info                                  # sides attached, run phase, seed, players
vstk cmd "/time set day"                   # any chat command, structured result
vstk cmd "/gamemode creative" --as Bob     # ...as a specific player
vstk eval 'sapi.WorldManager.Seed'         # expression
vstk eval -f snippet.cs                    # or from a file
vstk eval --side client '...'              # (step 3)
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
1024000 wide, so spawn is around `512000, 3, 512000` and a block written at
`0,0,0` lands in an unloaded chunk and silently reads back as air. Work relative
to `sapi.World.DefaultSpawnPosition`.

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
