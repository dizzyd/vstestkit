import os
from pathlib import Path
import subprocess
import tempfile
import time
import unittest

ROOT = Path(__file__).resolve().parents[2]


class RegistryTests(unittest.TestCase):
    def test_contended_acquisition_succeeds_and_unlocks(self):
        with tempfile.TemporaryDirectory() as state:
            lock = Path(state, "registry.lock")
            lock.mkdir()
            env = dict(os.environ, VSTK_STATE=state)
            child = subprocess.Popen(
                ["bash", "-c", '''
set -euo pipefail
source scripts/registry.sh
sleep() { touch "$VSTK_STATE/waiting"; command sleep "$@"; }
lock_registry
echo acquired
unlock_registry
'''], cwd=ROOT, env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
            try:
                deadline = time.monotonic() + 5
                while not Path(state, "waiting").exists():
                    if child.poll() is not None or time.monotonic() >= deadline:
                        self.fail("lock waiter did not reach contention")
                    time.sleep(0.01)
                lock.rmdir()
                out, err = child.communicate(timeout=5)
                self.assertEqual(child.returncode, 0, err)
                self.assertEqual(out.strip(), "acquired")
                self.assertFalse(lock.exists(), "caller must reach unlock after contention")
            finally:
                if child.poll() is None:
                    child.kill()
                child.communicate()


if __name__ == "__main__":
    unittest.main()
