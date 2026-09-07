import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]


class Sandbox:
    def __enter__(self):
        self.temp = tempfile.TemporaryDirectory()
        self.base = Path(self.temp.name)
        self.root = self.base / "vstestkit-checks"
        self.root.mkdir()
        shutil.copytree(ROOT / "scripts", self.root / "scripts")
        shutil.copytree(ROOT / "templates", self.root / "templates")
        mod = self.root / "VsTestkit/bin/Debug/Mods/vstestkit"
        mod.mkdir(parents=True)
        (mod / "VsTestkit.dll").touch()
        self.install = self.base / "install"
        self.install.mkdir()
        (self.install / "VintagestoryServer.dll").touch()
        shutil.copy(ROOT / "tests/scripts/fixtures/game.py", self.install / "VintagestoryServer")
        (self.install / "VintagestoryServer").chmod(0o755)
        self.home = self.base / "home"
        self.home.mkdir()
        self.cairn = self.base / "cairn"
        self.cairn.mkdir()
        (self.cairn / "session.json").write_text(json.dumps({"sessionkey": "fixture", "playername": "checks"}))
        self.run_dir = self.root / "run/checks"
        self.state = self.base / "state"
        self.env = dict(os.environ, HOME=str(self.home), CAIRN_HOME=str(self.cairn),
                        VINTAGE_STORY=str(self.install), VSTK_RUN=str(self.run_dir),
                        VSTK_STATE=str(self.state), VSTK_SLOT="checks", VSTK_BOOT_TIMEOUT="5",
                        VSTK_DISPLAY="invalid-test-display", PYTHONDONTWRITEBYTECODE="1")
        return self

    def run(self, script, *args, **env):
        return subprocess.run(["bash", str(self.root / "scripts" / script), *args],
                              env=dict(self.env, **env), cwd=self.root, capture_output=True,
                              text=True, timeout=70)

    def __exit__(self, *exc):
        try:
            self.run("stop.sh")
        finally:
            self.temp.cleanup()
