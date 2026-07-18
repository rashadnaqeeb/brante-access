#!/usr/bin/env python3
"""Wait for a WrathAccess dev-server state, polling fast (0.3s) and exiting the moment it's ready.

  python tools/gamewait.py health          # dev server answering
  python tools/gamewait.py menu            # main menu reached
  python tools/gamewait.py ingame          # an area is loaded (ctx.ingame)
  python tools/gamewait.py loadsave        # POST latest-save load, then wait for ingame

Exit 0 when reached, 1 on timeout (--timeout seconds, default 240).
"""

import argparse
import subprocess
import sys
import time
import urllib.request

BASE = "http://127.0.0.1:8771"


def http(path, body=None, timeout=5):
    req = urllib.request.Request(BASE + path, data=body.encode() if body else None,
                                 method="POST" if body else "GET")
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return r.read().decode("utf-8", "replace")


def screen():
    try:
        return http("/eval", "WrathAccess.Dev.DevApi.Screen()")
    except OSError:
        return ""


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("state", choices=["health", "menu", "ingame", "loadsave"])
    ap.add_argument("--timeout", type=float, default=240)
    args = ap.parse_args()

    if args.state == "loadsave":
        print(http("/loadsave", "latest", timeout=120).strip())
        args.state = "ingame"

    def health():
        try:
            return "ok" in http("/health", timeout=2)
        except OSError:
            return False

    check = {
        "health": health,
        "menu": lambda: "ctx.mainmenu" in screen(),
        "ingame": lambda: "ctx.ingame" in screen(),
    }[args.state]

    def game_alive():
        out = subprocess.run(["tasklist", "/FI", "IMAGENAME eq Wrath.exe", "/NH"],
                             capture_output=True, text=True).stdout
        return "Wrath.exe" in out

    start = time.time()
    grace = start + 20  # allow a just-started launcher a moment to spawn the process
    while time.time() - start < args.timeout:
        if check():
            print(f"{args.state} ready after {time.time() - start:.1f}s")
            return 0
        # Fail FAST when the game process is gone — polling a dead game "waits forever".
        if time.time() > grace and not game_alive():
            print(f"game process not running — aborting wait for {args.state}", file=sys.stderr)
            return 2
        time.sleep(0.3)
    print(f"timeout waiting for {args.state}", file=sys.stderr)
    return 1


if __name__ == "__main__":
    sys.exit(main())
