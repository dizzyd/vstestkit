#!/usr/bin/env python3
"""Patch a generated serverconfig.json into an ephemeral test server."""
import json, os, sys

data, seed, playstyle = sys.argv[1], int(sys.argv[2]), sys.argv[3]
mode = os.environ.get("VSTK_MODE", "server")
path = os.path.join(data, "serverconfig.json")
cfg = json.load(open(path))

# The playstyle as VsTestkit itself declares it, so the server and the client
# would build the same world.
styles = json.load(open(os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "VsTestkit", "worldconfig.json")))
style = next((p for p in styles.get("playstyles", []) if p.get("code") == playstyle), {})

# An ephemeral single-purpose server: off the network, no auth, no advertising.
#
# The port is reserved per session rather than fixed, because a box running two
# slots at once has two servers on it - singleplayer included, which runs a real
# one on a real socket. A fixed 42420 kills the second in startSockets with a
# bare "Address already in use" and a crash log that says nothing about ports.
cfg["Port"] = int(os.environ.get("VSTK_GAME_PORT", "42420"))
cfg["Ip"] = "127.0.0.1"
cfg["AdvertiseServer"] = False
cfg["Upnp"] = False
cfg["VerifyPlayerAuth"] = False
cfg["MaxClients"] = 4
cfg["ServerName"] = "vstestkit"
# Left as generated: an empty welcome message is still broadcast, and an empty
# chat line is a confusing thing to see in a test log.
cfg["Password"] = ""
# Headless runs have no players connected, and block ticks that only run while
# someone is watching would make results depend on whether a client attached.
cfg["PassTimeWhenEmpty"] = True

wc = cfg.setdefault("WorldConfig", {})
wc["Seed"] = seed
wc["WorldName"] = "vstestkit"
wc["PlayStyle"] = playstyle
wc["PlayStyleLangCode"] = playstyle
# The playstyle's own world type, not a hardcoded one.
#
# Forcing superflat here meant a server-created world was always flat whatever
# playstyle was asked for, so a standard world could only be had by letting the
# *client* create it - and the client picks its own random seed, with no command
# line option to pin it. Every --client run therefore generated a different
# world, which is why threshold tests drifted between runs. The server does
# honour WorldConfig.Seed, so taking the type from the playstyle lets the server
# create the world and the seed mean something.
wc["WorldType"] = style.get("worldType", "superflat")
wc["AllowCreativeMode"] = True

# --openWorld <name> resolves to Saves/<name>.vcdbs. In singleplayer the client
# runs its own server against this same config, so the savegame has to be named
# to match or the client quietly creates a second world beside ours.
wc["SaveFileLocation"] = os.path.join(data, "Saves", "vstestkit.vcdbs")

# Must be an object. Left null, world creation fails in a way that reads like a
# worldgen bug rather than a config one.
wc["WorldConfiguration"] = dict(style.get("worldConfig") or {
    "worldClimate": "superflat",
    "gameMode": "creative",
    # Vanilla creativebuilding uses 2400 to make time effectively stand still,
    # but that breaks every time-of-day command: "/time set day" sets hour 12,
    # which on a 2400-hour day is the middle of the night. Lighting then depends
    # on where the calendar happens to be, which ruins visual baselines and
    # confuses anything else that cares about daylight.
    "hoursPerDay": "24",
    "temporalStability": "false",
    "temporalStorms": "off",
    "temporalRifts": "off",
    "snowAccum": "false",
    "loreContent": "false",
})

json.dump(cfg, open(path, "w"), indent=2)
