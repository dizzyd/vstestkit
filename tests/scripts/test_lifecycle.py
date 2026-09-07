from pathlib import Path
import subprocess
import unittest

from support import Sandbox


class LifecycleTests(unittest.TestCase):
    def test_failed_display_start_stops_peer_before_releasing_slot(self):
        with Sandbox() as box:
            result = box.run("boot.sh", "--multiplayer")
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("unknown VSTK_DISPLAY", result.stderr)
            self.assertTrue((box.run_dir / "stopped").exists(), result.stdout + result.stderr)
            self.assertFalse((box.run_dir / "peer-server.pid").exists())
            self.assertFalse((box.state / "sessions/checks.session").exists())

    def test_stop_without_game_pid_stops_owned_display(self):
        with Sandbox() as box:
            child = subprocess.Popen(["sleep", "60"])
            try:
                box.run_dir.mkdir(parents=True)
                (box.run_dir / "display.pid").write_text(str(child.pid))
                result = box.run("stop.sh")
                self.assertEqual(result.returncode, 0, result.stderr)
                child.wait(timeout=5)
                self.assertFalse((box.run_dir / "display.pid").exists())
            finally:
                if child.poll() is None:
                    child.kill()
                child.wait()


if __name__ == "__main__":
    unittest.main()
