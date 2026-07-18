#!/usr/bin/env python3
"""Survey an area through the WrathAccess dev server for environmental-description authoring.

Drives src/Dev/DevSurvey.cs (DEBUG builds only) over http://127.0.0.1:8771 — the game must be
running with a save loaded in the target area. See docs/design/environmental-descriptions.md.

Modes:
  rooms      Capture every RoomMap room (or --room N ...): survey points x headings, one
             downscaled screenshot each + a data.json per room (room meta, categorized contents,
             per-shot screen-space labels). Output: survey/<AreaBlueprint>/rooms/room_NN/.
  assets     Capture one framed shot per unique undescribed asset (--all includes described).
             Output: survey/<AreaBlueprint>/assets/<key>.png + index.json.
  validate   Check authored content: every anchor in assets/descriptions/<AreaBlueprint>.json
             resolves to a room live (RoomIdAt), every key has desc.<key>.title/.body locale text,
             and orphaned desc.* locale keys are reported.
  areas      List every area blueprint in the game (survey targets for --area).

--area <BlueprintName> teleports the session into that area first (the cheat transfer path — no
save in the area needed) and waits for it to load. Captures hide the game HUD automatically.

Camera/fog state is saved on the first frame and restored at the end (also on Ctrl+C).
Screenshots are downscaled (Pillow, if available) to --max-width for cheap vision review.
"""

import argparse
import json
import shutil
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent


class Dev:
    """Thin client for the dev server; call() evaluates a C# expression and parses the JSON result."""

    def __init__(self, base):
        self.base = base.rstrip("/")

    def http(self, path, body=None):
        req = urllib.request.Request(
            self.base + path,
            data=body.encode("utf-8") if body is not None else None,
            method="POST" if body is not None else "GET",
        )
        with urllib.request.urlopen(req, timeout=90) as r:
            return r.read().decode("utf-8", "replace")

    def call(self, expr):
        out = self.http("/eval", expr)
        for line in out.splitlines():
            if line.startswith("=> "):
                payload = json.loads(line[3:])
                if isinstance(payload, dict) and payload.get("error"):
                    raise RuntimeError(f"{expr}: {payload['error']}")
                return payload
        raise RuntimeError(f"eval failed: {expr}\n{out.strip()}")

    def screenshot(self, dest, max_width):
        out = self.http("/screenshot").strip()
        src = Path(out.splitlines()[-1])
        if not src.is_file():
            raise RuntimeError(f"screenshot failed: {out}")
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(src, dest)
        return downscale(dest, max_width)

    def health(self):
        try:
            return self.http("/health").strip() == "ok"
        except (urllib.error.URLError, OSError):
            return False


def downscale(path, max_width):
    """Resize to max_width; returns the applied scale factor (1.0 = untouched)."""
    if not max_width:
        return 1.0
    try:
        from PIL import Image  # UnityPy already pulls Pillow in; degrade gracefully without it
    except ImportError:
        return 1.0
    with Image.open(path) as im:
        if im.width <= max_width:
            return 1.0
        h = round(im.height * max_width / im.width)
        im.resize((max_width, h), Image.LANCZOS).save(path)
        return max_width / im.width


def brightness(path):
    try:
        from PIL import Image, ImageStat
    except ImportError:
        return None
    with Image.open(path) as im:
        return ImageStat.Stat(im.convert("L")).mean[0]


def frame_and_shoot(dev, dest, x, y, z, yaw, zoom, settle, max_width):
    """Frame, capture, and return labels with coordinates RESCALED to the saved image's pixels."""
    dev.call(f"WrathAccess.Dev.DevSurvey.Frame({x}f, {y}f, {z}f, {yaw}f, {zoom}f)")
    time.sleep(settle)  # scroll is immediate; zoom smooths over a few frames
    scale = dev.screenshot(dest, max_width)
    # A capture racing scene streaming (right after a save load) — or a minimized game window —
    # produces an all-black frame. Retry with growing waits; a stuck-black capture is loudly flagged.
    for attempt in range(4):
        b = brightness(dest)
        if b is None or b >= 0.5:
            break
        time.sleep(2 + 2 * attempt)
        scale = dev.screenshot(dest, max_width)
    else:
        print(f"  WARNING: {dest.name} still black after retries (window minimized? scene not loaded?)")
    labels = dev.call("WrathAccess.Dev.DevSurvey.Labels()")
    labels["w"] = round(labels["w"] * scale)
    labels["h"] = round(labels["h"] * scale)
    for l in labels["labels"]:
        l["sx"] = round(l["sx"] * scale)
        l["sy"] = round(l["sy"] * scale)
    return labels


def survey_rooms(dev, args, area, out_dir):
    rooms = dev.call(f"WrathAccess.Dev.DevSurvey.Rooms({args.max_points})")["rooms"]
    if args.room:
        rooms = [r for r in rooms if r["id"] in args.room]
    print(f"{len(rooms)} rooms to capture in {area['blueprint']} ({area['display']})")
    for room in rooms:
        rid = room["id"]
        room_dir = out_dir / "rooms" / f"room_{rid:02d}"
        if not args.force and (room_dir / "data.json").is_file():
            print(f"  room {rid}: already surveyed (--force to redo)")
            continue
        shots = []
        for pi, pt in enumerate(room["points"]):
            for yaw in args.yaw:
                name = f"p{pi}_y{int(yaw)}.png"
                labels = frame_and_shoot(dev, room_dir / name, pt["x"], pt["y"], pt["z"],
                                         yaw, args.zoom, args.settle, args.max_width)
                shots.append({"file": name, "point": pt, "yaw": yaw, "zoom": args.zoom,
                              "screen": {"w": labels["w"], "h": labels["h"]},
                              "labels": labels["labels"]})
        contents = dev.call(f"WrathAccess.Dev.DevSurvey.Contents({rid})")
        data = {"area": area, "room": room, "shots": shots, "contents": contents}
        (room_dir / "data.json").write_text(json.dumps(data, indent=2), encoding="utf-8")
        print(f"  room {rid} ({room['cls']}, {room['area']:.0f} m2): "
              f"{len(shots)} shots, {len(contents['objects'])} objects, {len(contents['units'])} units")


def survey_assets(dev, args, area, out_dir):
    entries = dev.call("WrathAccess.Dev.DevSurvey.Assets()")["assets"]
    todo = [e for e in entries if args.all or not e["entry"]["described"]]
    print(f"{len(todo)} of {len(entries)} unique assets to capture in {area['blueprint']}")
    asset_dir = out_dir / "assets"
    index = []
    for e in todo:
        a = e["entry"]
        safe = "".join(c if c.isalnum() or c in "._-" else "_" for c in a["asset"])
        dest = asset_dir / f"{safe}.png"
        if not args.force and dest.is_file():
            print(f"  {a['asset']}: already captured")
            continue
        labels = frame_and_shoot(dev, dest, a["x"], a["y"], a["z"],
                                 args.yaw[0], args.asset_zoom, args.settle, args.max_width)
        index.append({"asset": a["asset"], "name": a["name"], "file": dest.name,
                      "count": e["count"], "pos": {"x": a["x"], "y": a["y"], "z": a["z"]},
                      "labels": labels["labels"]})
        print(f"  {a['asset']} ({e['count']}x): {a['name']}")
    if index:
        merged = index
        idx_path = asset_dir / "index.json"
        if idx_path.is_file():
            old = json.loads(idx_path.read_text(encoding="utf-8"))
            keep = {i["asset"] for i in index}
            merged = [o for o in old if o["asset"] not in keep] + index
        idx_path.write_text(json.dumps(merged, indent=2), encoding="utf-8")


def validate(dev, args, area):
    desc_path = REPO / "assets" / "descriptions" / f"{area['blueprint']}.json"
    ui_path = REPO / "assets" / "locale" / "enGB" / "ui.json"
    ui = json.loads(ui_path.read_text(encoding="utf-8"))
    problems = 0
    anchors = []
    if desc_path.is_file():
        anchors = json.loads(desc_path.read_text(encoding="utf-8")).get("rooms", [])
    else:
        print(f"note: {desc_path.name} does not exist yet")
    used = set()
    for a in anchors:
        rid = dev.call(f"WrathAccess.Dev.DevSurvey.RoomIdAt({a['x']}f, {a['y']}f, {a['z']}f)")["id"]
        ok_room = rid > 0
        ok_loc = f"desc.{a['key']}.title" in ui and f"desc.{a['key']}.body" in ui
        used.add(a["key"])
        if not ok_room or not ok_loc:
            problems += 1
        status = f"room {rid}" if ok_room else "NO ROOM (anchor off-mesh?)"
        loc = "locale ok" if ok_loc else "MISSING desc.*.title/.body"
        print(f"  {a['key']}: {status}, {loc}")
    for key in sorted(ui):
        if key.startswith("desc.") and key.endswith(".title"):
            k = key[len("desc."):-len(".title")]
            if k not in used and k != "none":
                print(f"  orphan locale prose (no anchor in {area['blueprint']}): desc.{k}.*")
    print("validate: " + ("OK" if problems == 0 else f"{problems} problem(s)"))
    return problems


def enter_area(dev, blueprint, timeout=180):
    """Teleport into an area by blueprint name and wait until it's loaded and room-mapped."""
    cur = dev.call("WrathAccess.Dev.DevSurvey.Status()")
    if cur["area"] != blueprint:
        dev.call(f'WrathAccess.Dev.DevSurvey.EnterArea("{blueprint}")')
        print(f"entering {blueprint}...")
    deadline = time.time() + timeout
    while time.time() < deadline:
        s = dev.call("WrathAccess.Dev.DevSurvey.Status()")
        if s["area"] == blueprint and not s["loading"] and s["roomMap"]:
            time.sleep(3)  # let WorldModel fill and on-enter scripting settle
            return
        time.sleep(2)
    sys.exit(f"timed out entering {blueprint} (status: {s})")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("mode", choices=["rooms", "assets", "validate", "areas"])
    ap.add_argument("--area", help="teleport to this area blueprint first (no save needed)")
    ap.add_argument("--url", default="http://127.0.0.1:8771")
    ap.add_argument("--out", default=str(REPO / "survey"), help="output root (default: <repo>/survey)")
    ap.add_argument("--room", type=int, action="append", help="limit rooms mode to these room ids")
    ap.add_argument("--yaw", type=float, action="append", help="capture headings (default: 135)")
    ap.add_argument("--zoom", type=float, default=0.5, help="room-shot zoom, 0=in 1=out")
    ap.add_argument("--asset-zoom", type=float, default=0.15, help="asset-shot zoom (tight)")
    ap.add_argument("--max-points", type=int, default=4, help="max survey points per room")
    ap.add_argument("--max-width", type=int, default=1600, help="downscale shots to this width (0 = keep 4K)")
    ap.add_argument("--settle", type=float, default=0.8, help="seconds between framing and capture")
    ap.add_argument("--all", action="store_true", help="assets mode: include already-described assets")
    ap.add_argument("--force", action="store_true", help="recapture rooms/assets that already have output")
    args = ap.parse_args()
    args.yaw = args.yaw or [135.0]

    dev = Dev(args.url)
    if not dev.health():
        sys.exit("dev server not reachable — launch a DEBUG build of the game (scripts/run-game.ps1) "
                 "and load any save (--area teleports from there)")
    if args.mode == "areas":
        for a in dev.call("WrathAccess.Dev.DevSurvey.Areas()")["areas"]:
            print(f"{a['blueprint']}\t{a['display']}")
        return
    if args.area:
        enter_area(dev, args.area)
    area = dev.call("WrathAccess.Dev.DevSurvey.Area()")
    out_dir = Path(args.out) / area["blueprint"]

    if args.mode == "validate":
        sys.exit(1 if validate(dev, args, area) else 0)
    try:
        if args.mode == "rooms":
            survey_rooms(dev, args, area, out_dir)
        else:
            survey_assets(dev, args, area, out_dir)
    finally:
        dev.call("WrathAccess.Dev.DevSurvey.Restore()")
        print("camera/fog restored")


if __name__ == "__main__":
    main()
