#!/usr/bin/env python3
"""Render a tests.run response as a readable report. Exit 1 if anything failed."""
import json, sys

MARK = {
    "passed":   "ok  ",
    "failed":   "FAIL",
    "errored":  "ERR ",
    "skipped":  "skip",
    "timedout": "TIME",
    "notrun":   "----",
}


def main(src, dest):
    envelope = json.load(open(src))
    if not envelope.get("ok", True) and "result" not in envelope:
        print(f"run failed: {envelope.get('code')}: {envelope.get('error')}", file=sys.stderr)
        return 1

    r = envelope.get("result", envelope)
    lines = []

    for t in r["results"]:
        mark = MARK.get(t["status"], t["status"])
        lines.append(f"{mark}  {t['name']}  ({t['durationMs']}ms)")
        if t.get("message"):
            for m in t["message"].splitlines():
                lines.append(f"        {m}")
        for o in t.get("output") or []:
            lines.append(f"      | {o}")
        if t.get("stack"):
            for s in t["stack"].splitlines()[:8]:
                lines.append(f"      . {s.strip()}")

    counts = (f"{r['passed']} passed, {r['failed']} failed, {r['errored']} errored, "
              f"{r['skipped']} skipped")
    if r.get("timedOut"):
        counts += f", {r['timedOut']} timed out"
    if r.get("notRun"):
        counts += f", {r['notRun']} not run"

    lines.append("")
    lines.append(f"{counts}  in {r['durationMs']}ms")
    if not r.get("clientAttached"):
        lines.append("(headless: tests marked [RequiresClient] were skipped)")
    if r.get("aborted"):
        lines.append(f"ABORTED: {r.get('abortReason')}")

    text = "\n".join(lines)
    open(dest, "w").write(text + "\n")
    print(text)

    return 0 if r.get("ok") else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], sys.argv[2]))
