import os
import unittest

from support import Sandbox


class ModeTests(unittest.TestCase):
    def test_explicit_mode_must_match_live_session(self):
        with Sandbox() as box:
            boot = box.run("boot.sh")
            self.assertEqual(boot.returncode, 0, boot.stdout + boot.stderr)
            suite = box.root / "suite.dll"
            suite.touch()
            pid = int((box.run_dir / "server.pid").read_text())
            for live, flag in [("server", "--client"), ("client", "--multiplayer"),
                               ("multiplayer", "--client"), ("", "--client")]:
                with self.subTest(live=live, requested=flag):
                    (box.run_dir / "session.mode").write_text(live)
                    result = box.run("run.sh", str(suite), flag)
                    self.assertNotEqual(result.returncode, 0)
                    self.assertIn("was requested", result.stderr)
                    self.assertIn("stop.sh", result.stderr)
                    os.kill(pid, 0)  # refusing reuse must not terminate the existing tenant


if __name__ == "__main__":
    unittest.main()
