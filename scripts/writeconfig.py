#!/usr/bin/env python3
"""Patch a generated serverconfig.json into an ephemeral test server."""
import json, os, sys

data, seed, playstyle = sys.argv[1], int(sys.argv[2]), sys.argv[3]
mode = os.environ.get("VSTK_MODE", "server")
path = os.path.join(data, "serverconfig.json")
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
# Headless runs have no players connected, and block ticks that only run while
# someone is watching would make results depend on whether a client attached.
cfg["PassTimeWhenEmpty"] = True

wc = cfg.setdefault("WorldConfig", {})
wc["Seed"] = seed
wc["WorldName"] = "vstestkit"
wc["PlayStyle"] = playstyle
wc["PlayStyleLangCode"] = playstyle
wc["WorldType"] = "superflat"
wc["AllowCreativeMode"] = True

# --openWorld <name> resolves to Saves/<name>.vcdbs. In singleplayer the client
# runs its own server against this same config, so the savegame has to be named
# to match or the client quietly creates a second world beside ours.
wc["SaveFileLocation"] = os.path.join(data, "Saves", "vstestkit.vcdbs")

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
