import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

from support import ROOT, Sandbox


class CairnHomeTests(unittest.TestCase):
    def resolve(self, env):
        result = subprocess.run(["python3", str(ROOT / "scripts/cairn_home.py")],
                                env=env, capture_output=True, text=True, timeout=5)
        self.assertEqual(result.returncode, 0, result.stderr)
        return result.stdout.strip()

    def test_environment_pointer_and_default_precedence(self):
        with tempfile.TemporaryDirectory() as temp:
            default = Path(temp) / ".cairn"
            default.mkdir()
            env = dict(os.environ, HOME=temp, CAIRN_HOME="", CAIRN_DEFAULT_HOME="")
            self.assertEqual(self.resolve(env), str(default))
            pointed = str(Path(temp) / "Cairn Storage")
            (default / "home").write_text(f"  {pointed}\n")
            self.assertEqual(self.resolve(env), pointed)
            env["CAIRN_HOME"] = "  /explicit root  "
            self.assertEqual(self.resolve(env), "/explicit root")
            env["CAIRN_HOME"] = " \t "
            self.assertEqual(self.resolve(env), pointed)

            sandbox = Path(temp) / "sandbox"
            sandbox.mkdir()
            env["CAIRN_DEFAULT_HOME"] = str(sandbox)
            self.assertEqual(self.resolve(env), str(sandbox))
            (sandbox / "home").write_text(pointed)
            self.assertEqual(self.resolve(env), pointed)

    def test_invalid_pointers_fall_back_without_using_the_working_directory(self):
        with tempfile.TemporaryDirectory() as temp:
            default = Path(temp) / ".cairn"
            default.mkdir()
            env = dict(os.environ, HOME=temp, CAIRN_HOME="", CAIRN_DEFAULT_HOME="")
            for content in (" \n", "relative/path"):
                with self.subTest(content=content):
                    (default / "home").write_text(content)
                    self.assertEqual(self.resolve(env), str(default))

    def test_launch_login_and_diagnostics_follow_the_same_spaced_pointer(self):
        with Sandbox() as box:
            box.env.update(CAIRN_HOME="", CAIRN_DEFAULT_HOME="")
            pointed = box.base / "Cairn Storage"
            install = pointed / "games/1.22.6"
            install.mkdir(parents=True)
            (install / "VintagestoryServer.dll").touch()
            (pointed / "session.json").write_text(json.dumps({"sessionkey": "pointed"}))
            default = box.home / ".cairn"
            default.mkdir()
            (default / "home").write_text(f"{pointed}\n")

            launch = subprocess.run(
                ["bash", "-c", 'eval "$(bash scripts/cairn-env.sh 1.22.6)"; '
                 'printf "%s\\n" "$CAIRN_HOME" "$VINTAGE_STORY"'],
                cwd=box.root, env=box.env, capture_output=True, text=True, timeout=10)
            self.assertEqual(launch.returncode, 0, launch.stderr)
            self.assertEqual(launch.stdout.splitlines(), [str(pointed), str(install)])

            settings = box.base / "settings.json"
            login = subprocess.run(["python3", str(box.root / "scripts/session.py"), str(settings)],
                                   env=box.env, capture_output=True, text=True, timeout=10)
            self.assertEqual(login.returncode, 0, login.stderr)
            self.assertEqual(json.loads(settings.read_text())["stringSettings"]["sessionkey"], "pointed")

            doctor = box.run("linux-doctor.sh", DISPLAY="", WAYLAND_DISPLAY="")
            self.assertEqual(doctor.returncode, 0, doctor.stdout + doctor.stderr)


if __name__ == "__main__":
    unittest.main()
