import json
import os
from pathlib import Path
import shutil
import stat
import subprocess
import time
import unittest

from support import Sandbox


class LoginTests(unittest.TestCase):
    def test_empty_login_recovery_survives_stop_and_fresh_boot(self):
        with Sandbox() as box:
            (box.cairn / "session.json").unlink()
            shutil.copy(box.install / "VintagestoryServer", box.install / "Vintagestory")
            env = {"VSTK_DISPLAY": "existing", "DISPLAY": ":fixture", "HTTPS_PROXY": "", "ALL_PROXY": ""}
            refused = box.run("boot.sh", "--client", **env)
            self.assertNotEqual(refused.returncode, 0)
            self.assertFalse((box.run_dir / "server.pid").exists())

            boot = box.run("boot.sh", "--client", VSTK_LOGIN="1", **env)
            self.assertEqual(boot.returncode, 0, boot.stdout + boot.stderr)
            initial = json.loads((box.run_dir / "data/initial-settings.json").read_text())
            self.assertFalse(initial.get("stringSettings", {}).get("sessionkey"))
            proxy = json.loads((box.run_dir / "data/initial-proxy.json").read_text())
            self.assertNotIn("http://127.0.0.1:9", proxy.values())
            stop = box.run("stop.sh")
            self.assertEqual(stop.returncode, 0, stop.stdout + stop.stderr)
            kept = box.root / "run/session.json"
            self.assertEqual(json.loads(kept.read_text())["sessionkey"], "manual-fixture")
            self.assertEqual(stat.S_IMODE(kept.stat().st_mode), 0o600)

            boot = box.run("boot.sh", "--client", **env)
            self.assertEqual(boot.returncode, 0, boot.stdout + boot.stderr)
            initial = json.loads((box.run_dir / "data/initial-settings.json").read_text())
            self.assertEqual(initial["stringSettings"]["sessionkey"], "manual-fixture")
            proxy = json.loads((box.run_dir / "data/initial-proxy.json").read_text())
            self.assertEqual(proxy["HTTPS_PROXY"], "http://127.0.0.1:9")

    def test_stale_disk_settings_cannot_replace_live_snapshot(self):
        with Sandbox() as box:
            settings = box.base / "settings.json"
            settings.write_text(json.dumps({"stringSettings": {"sessionkey": "old"}}))
            old = time.time() - 10
            os.utime(settings, (old, old))
            kept = box.base / "kept.json"
            kept.write_text(json.dumps({"sessionkey": "new"}))
            result = subprocess.run(["python3", str(box.root / "scripts/session.py"), "--capture", str(settings), str(kept)],
                                    env=box.env, capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(json.loads(kept.read_text())["sessionkey"], "new")


if __name__ == "__main__":
    unittest.main()
