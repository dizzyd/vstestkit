#!/usr/bin/env python3
"""
Carry the Vintage Story login into the ephemeral test data path.

A fresh data path has no session, so the client stops at the login screen and the
run never starts. Cairn already solved this for packs: it merges a handful of
named auth keys between data paths, rather than copying clientsettings.json
wholesale, so each pack keeps its own preferences but shares one login. Same
approach here, same key list (Cairn.Core/Launch/ClientSession.cs), and the same
source of truth - Cairn's own session store at $CAIRN_HOME/session.json.

Freshness matters more than location. Picking the first plausible file finds a
stale login as readily as a live one, and a stale session is worse than none: the
client accepts the settings and then prompts for a password anyway. So candidates
are ranked by modification time, exactly as Cairn's PackData.CaptureLatest does.

Usage:
  session.py <target-clientsettings.json> [source-clientsettings.json]
  session.py --capture <clientsettings.json> <dest-session.json>

Prints where the session came from, never what it contains.
"""
import json, os, stat, sys

# The auth-bearing keys, all inside "stringSettings".
KEYS = [
    "sessionkey",
    "sessionsignature",
    "playeruid",
    "mptoken",
    "entitlements",
    "useremail",
    "playername",
]

# sessionkey is the one that actually proves a login. playername in particular
# survives signing out, so treating any other key as evidence would let a
# logged-out source look like a good session.
CREDENTIAL = "sessionkey"


def load(path):
    try:
        with open(path) as f:
            return json.load(f)
    except Exception:
        return None


def session_of(path):
    root = load(path)
    if not isinstance(root, dict):
        return None
    strings = root.get("stringSettings")
    if not isinstance(strings, dict):
        return None
    if not strings.get(CREDENTIAL):
        return None
    return {k: strings[k] for k in KEYS if isinstance(strings.get(k), str)}


def kept_session_path():
    """
    Where a session logged in from inside the harness is kept.

    Deliberately outside run/current, which boot.sh deletes: a login typed at the
    game's own screen would otherwise be thrown away with the run directory, and
    have to be typed again next time.
    """
    return os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                        "run", "session.json")


def flat_session(path):
    """Read a flat {key: value} session file - Cairn's session.json shape."""
    values = load(path)
    if not isinstance(values, dict) or not values.get(CREDENTIAL):
        return None
    kept = {k: v for k, v in values.items() if k in KEYS and isinstance(v, str)}
    try:
        return os.path.getmtime(path), path, kept
    except OSError:
        return None


def capture(settings_path, dest):
    """Save the session out of a finished run, so a manual login is not wasted."""
    values = session_of(settings_path)
    if not values:
        return 1

    existing = load(dest)
    if isinstance(existing, dict) and existing.get(CREDENTIAL) == values.get(CREDENTIAL):
        return 0  # unchanged, leave the mtime alone

    os.makedirs(os.path.dirname(dest), exist_ok=True)
    with open(dest, "w") as f:
        json.dump(values, f, indent=2)
    os.chmod(dest, stat.S_IRUSR | stat.S_IWUSR)
    print(f"session captured to {dest}")
    return 0


def cairn_home():
    home = os.path.expanduser("~")
    if os.environ.get("CAIRN_HOME"):
        return os.environ["CAIRN_HOME"]
    pointer = os.path.join(home, ".cairn", "home")
    if os.path.isfile(pointer):
        try:
            root = open(pointer).read().strip()
            if root:
                return root
        except OSError:
            pass
    return os.path.join(home, ".cairn")


def cairn_session():
    """Cairn's own store, which is the same flat shape."""
    return flat_session(os.path.join(cairn_home(), "session.json"))


def candidates():
    home = os.path.expanduser("~")

    # The path the game picks on its own (Cairn's GameInstall.DefaultDataPath).
    if sys.platform == "darwin":
        yield os.path.join(home, "Library", "Application Support", "VintagestoryData", "clientsettings.json")
    elif sys.platform.startswith("win"):
        yield os.path.join(os.environ.get("APPDATA", ""), "VintagestoryData", "clientsettings.json")
    else:
        yield os.path.join(home, ".config", "VintagestoryData", "clientsettings.json")

    # Every Cairn pack. Cairn carries the session between them, so the one
    # launched most recently holds the live login.
    packs = os.path.join(cairn_home(), "packs")
    if os.path.isdir(packs):
        for pack in sorted(os.listdir(packs)):
            yield os.path.join(packs, pack, "data", "clientsettings.json")

    yield os.path.join(home, ".vintagestorydev", "clientsettings.json")


def newest():
    """
    The most recently written source that actually holds a session - Cairn's
    store included, on equal terms.

    Cairn's session.json is the canonical store, but it is only refreshed when
    Cairn launches something, so a pack played since then is newer. Cairn itself
    resolves this by taking whichever is newest (PackData.CaptureLatest), and so
    does this.
    """
    best = cairn_session()

    # A session logged in from inside the harness counts too, and is usually the
    # freshest thing there is.
    kept = flat_session(kept_session_path())
    if kept and (best is None or kept[0] > best[0]):
        best = kept

    for path in candidates():
        values = session_of(path)
        if not values:
            continue
        try:
            at = os.path.getmtime(path)
        except OSError:
            continue
        if best is None or at > best[0]:
            best = (at, path, values)

    return best


def apply(target, values, source):
    root = load(target) or {}
    root.setdefault("stringSettings", {}).update(values)
    with open(target, "w") as f:
        json.dump(root, f, indent=2)
    os.chmod(target, stat.S_IRUSR | stat.S_IWUSR)

    who = values.get("playername") or "?"
    print(f"session carried from {source} (as {who})")
    return 0


def main(target, explicit=None):
    if explicit:
        values = session_of(explicit)
        if not values:
            print(f"{explicit} holds no session", file=sys.stderr)
            return 1
        return apply(target, values, explicit)

    found = newest()
    if found:
        _, path, values = found
        return apply(target, values, path)

    print("no logged-in session found; the client will stop at the login screen.\n"
          "Log in once in the normal game or through Cairn, or point "
          "VSTK_CLIENT_SETTINGS at a settings file that is logged in.", file=sys.stderr)
    return 1


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "--capture":
        sys.exit(capture(sys.argv[2], sys.argv[3]))
    sys.exit(main(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else None))
