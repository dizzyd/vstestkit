#!/usr/bin/env python3
"""A bounded child process for launcher lifecycle tests, not a game simulation."""
import http.server
import json
import os
from pathlib import Path
import sys

args = sys.argv[1:]
data = Path(args[args.index("--dataPath") + 1])
data.mkdir(parents=True, exist_ok=True)
if "--genconfig" in args:
    (data / "serverconfig.json").write_text("{}")
    sys.exit(0)

stopped = False


class Handler(http.server.BaseHTTPRequestHandler):
    def do_POST(self):
        global stopped
        body = self.rfile.read(int(self.headers.get("Content-Length", 0)))
        if self.path == "/v1/tests.load":
            (data / "last-load.json").write_bytes(body)
            result = {"count": 1, "tests": []}
        elif self.path == "/v1/tests.run":
            result = {"ok": True, "passed": 1, "failed": 0, "errored": 0, "skipped": 0,
                      "durationMs": 1, "results": [{"name": "fixture", "status": "passed", "durationMs": 1}]}
        else:
            result = {"stopping": True}
            stopped = True
        self.send_response(200)
        self.end_headers()
        self.wfile.write(json.dumps({"ok": True, "result": result}).encode())

    def log_message(self, *args):
        pass


with http.server.HTTPServer(("127.0.0.1", 0), Handler) as server:
    server.timeout = 0.1
    (data / ".vstestkit").write_text(json.dumps({
        "port": server.server_port, "token": "fixture", "pid": os.getpid(), "sides": ["server"]
    }))
    (data.parent / "started.pid").write_text(str(os.getpid()))
    while not stopped:
        server.handle_request()
(data.parent / "stopped").touch()
