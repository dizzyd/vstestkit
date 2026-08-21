#!/usr/bin/env bash
#
# Boot a headless test server with the testkit endpoint enabled.
#
#   bash scripts/boot.sh                 # fresh world, fixed seed, flat
#   VSTK_SEED=99 bash scripts/boot.sh    # a different world
#   VSTK_KEEP=1 bash scripts/boot.sh     # reuse the existing run dir
#
# Leaves a live server in the background. Talk to it with scripts/vstk, stop it
# with scripts/stop.sh.
#
source "$(dirname "$0")/common.sh"
resolve_vintage_story

SEED="${VSTK_SEED:-424242}"
PLAYSTYLE="${VSTK_PLAYSTYLE:-vstestkit-flat}"
CONFIG="${VSTK_CONFIG:-Debug}"
MODPATH="$VSTK_ROOT/VsTestkit/bin/$CONFIG/Mods"
DATA="$VSTK_RUN/data"

[ -f "$MODPATH/vstestkit/VsTestkit.dll" ] || die "testkit not built - run scripts/build.sh $CONFIG"

if [ -f "$VSTK_RUN/server.pid" ] && kill -0 "$(cat "$VSTK_RUN/server.pid")" 2>/dev/null; then
    die "a session is already running (pid $(cat "$VSTK_RUN/server.pid")) - stop it first with scripts/stop.sh"
fi

if [ "${VSTK_KEEP:-0}" != "1" ]; then
    rm -rf "$VSTK_RUN"
fi
mkdir -p "$DATA"

SERVER="$(vs_server_cmd)"

# Let the game write its own defaults, then change only what the harness needs.
# Hand-authoring the whole file would rot every time the schema moves.
if [ ! -f "$DATA/serverconfig.json" ]; then
    $SERVER --dataPath "$DATA" --genconfig >/dev/null 2>&1 || die "genconfig failed"
fi

python3 - "$DATA/serverconfig.json" "$SEED" "$PLAYSTYLE" <<'PY'
import json, sys, random

path, seed, playstyle = sys.argv[1], int(sys.argv[2]), sys.argv[3]
cfg = json.load(open(path))

# An ephemeral single-purpose server: off the network, no auth, no advertising.
cfg["Port"] = 42420
cfg["Ip"] = "127.0.0.1"
cfg["AdvertiseServer"] = False
cfg["Upnp"] = False
cfg["VerifyPlayerAuth"] = False
cfg["MaxClients"] = 4
cfg["ServerName"] = "vstestkit"
cfg["WelcomeMessage"] = ""
cfg["Password"] = ""
# Headless tests have no players connected, and block ticks that only run while
# someone is watching would make results depend on whether a client attached.
cfg["PassTimeWhenEmpty"] = True

wc = cfg.setdefault("WorldConfig", {})
wc["Seed"] = seed
wc["WorldName"] = "vstestkit"
wc["PlayStyle"] = playstyle
wc["PlayStyleLangCode"] = "creativebuilding"
wc["WorldType"] = "superflat"
wc["AllowCreativeMode"] = True
# Must be an object. Left null, world creation fails in a way that reads like a
# worldgen bug rather than a config one.
wc["WorldConfiguration"] = {
    "worldClimate": "superflat",
    "gameMode": "creative",
    "hoursPerDay": "2400",
    "temporalStability": "false",
    "temporalStorms": "off",
    "temporalRifts": "off",
    "snowAccum": "false",
    "loreContent": "false",
}

json.dump(cfg, open(path, "w"), indent=2)
PY

rm -f "$DATA/.vstestkit"

echo "booting  install=$VINTAGE_STORY"
echo "         data=$DATA  seed=$SEED  playstyle=$PLAYSTYLE"

# addModPath is a CommandLineParser sequence option: ONE flag followed by every
# path. Repeating the flag is a duplicate-option parse error, and the server does
# not report that - ServerProgram dereferences a null ParserResult.Value and dies
# with a bare NullReferenceException. Each path is the Mods directory itself, not
# the mod folder inside it.
MODPATHS=("$MODPATH")
if [ -n "${VSTK_EXTRA_MODS:-}" ]; then
    while IFS= read -r d; do [ -n "$d" ] && MODPATHS+=("$d"); done <<< "${VSTK_EXTRA_MODS//:/$'\n'}"
    echo "         extra mods=$VSTK_EXTRA_MODS"
fi

# Mods in this workspace build their code into bin/<config>/Mods but leave assets
# in the source tree, so a content mod loaded by --addModPath alone registers no
# blocks at all. Its assets directory has to come in as an origin.
ORIGIN_ARGS=()
if [ -n "${VSTK_EXTRA_ORIGINS:-}" ]; then
    ORIGINS=()
    while IFS= read -r d; do [ -n "$d" ] && ORIGINS+=("$d"); done <<< "${VSTK_EXTRA_ORIGINS//:/$'\n'}"
    ORIGIN_ARGS=(--addOrigin "${ORIGINS[@]}")
    echo "         origins=$VSTK_EXTRA_ORIGINS"
fi

VSTESTKIT=1 nohup $SERVER \
    --dataPath "$DATA" \
    --addModPath "${MODPATHS[@]}" \
    ${ORIGIN_ARGS[@]+"${ORIGIN_ARGS[@]}"} \
    > "$VSTK_RUN/server.out" 2>&1 &

echo $! > "$VSTK_RUN/server.pid"
PID="$(cat "$VSTK_RUN/server.pid")"

# Wait for the endpoint to publish its handshake rather than for a fixed sleep -
# world creation time varies with playstyle and machine.
TIMEOUT="${VSTK_BOOT_TIMEOUT:-180}"
for i in $(seq 1 "$TIMEOUT"); do
    if [ -f "$DATA/.vstestkit" ]; then
        read_handshake
        echo "ready    pid=$PID port=$VSTK_PORT  (${i}s)"
        echo
        echo "  bash scripts/vstk info"
        echo "  bash scripts/vstk cmd '/time set day'"
        echo "  bash scripts/vstk eval 'sapi.WorldManager.Seed'"
        echo "  bash scripts/stop.sh"
        exit 0
    fi
    if ! kill -0 "$PID" 2>/dev/null; then
        echo "server exited during boot; last output:" >&2
        tail -30 "$VSTK_RUN/server.out" >&2
        # A bad command line shows up as an unexplained NRE in the constructor,
        # because ParserResult.Value is null and nothing checks it.
        if grep -q "ServerProgram..ctor" "$VSTK_RUN/server.out" 2>/dev/null; then
            echo >&2
            echo "hint: that NullReferenceException in ServerProgram means the server" >&2
            echo "      could not parse its command line, not that the world is broken." >&2
        fi
        exit 1
    fi
    sleep 1
done

echo "timed out after ${TIMEOUT}s waiting for the endpoint; last output:" >&2
tail -30 "$VSTK_RUN/server.out" >&2
exit 1
