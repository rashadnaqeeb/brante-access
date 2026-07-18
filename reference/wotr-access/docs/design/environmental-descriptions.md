# Environmental Descriptions — Design & Plan

Immersive, spatially-anchored descriptions of the environment (room ambiance + notable objects)
for blind players — reliable, and scalable to a large, detail-rich game (WotR). Prototyped end-to-end
2026-07-01 on Shield Maze via the DEBUG dev server.

## Principles (validated)

1. **Anchor to world coordinates / assets, never room IDs.** RoomMap is a volatile derived layer — use
   it as an *authoring scaffold* and *runtime nav aid*, never as the content key. Any "what's in my room"
   is a runtime point-in-room query, never stored.
2. **Prose from vision; directions from coordinates.** Screenshot-authored prose carries *flavor only*;
   all bearings/distances come live from the mod's world-aligned bearing engine off the anchors. The 3D
   camera is world-aligned (verified via WorldToScreenPoint — no flip); at the default heading (yaw 135)
   north = upper-right, east = lower-right (standard isometric). Screenshot directions require the yaw, so
   capture at a **canonical yaw (135)**; but runtime never trusts a screenshot for direction.
3. **Zoom hierarchy:** area → region → room → object; the player controls verbosity.
4. **Salience:** summarize the mundane, surface the exceptional (the "50 paintings" problem).
5. **Describe unflinchingly — do NOT filter.** This is a mature, dark-fantasy game (violence, gore,
   sexual themes). Blind players are adults and deserve the same information a sighted player gets;
   sanitizing or softening is a disservice, not a courtesy. Describe what's actually there, plainly. The
   *only* exception: if a specific thing would genuinely violate the assistant's usage policy, flag it to
   the user rather than silently omitting it — so they can decide on an alternative — never quietly filter.

## Content layers (three, distinct channels)

- **Room ambiance** — OUR authored prose, ~1 per room (more only for a genuinely distinct zone),
  anchored to room-point coordinates.
- **Object visual descriptions** — OUR authored prose, **deduped by asset** (see below); a global
  `asset → description` table, each unique asset described once and reused everywhere.
- **Game examine text** — the game's OWN hand-written, localized flavor (e.g. bloodstains). Fired by the
  normal in-game examine *interaction* and spoken via GameLogReader. We do NOT re-author or re-surface it.

## Units vs. static content (dynamic vs. permanent)

The trap: **units are dynamic** — enemies, NPCs, and especially *combat corpses* may or may not be
present on any given visit (the room-32 "corpses" were kills from a fight, not set-dressing). Baking a
unit into a static room description = a stale, wrong description on the next visit.

Rules:
- **Static prose describes only PERMANENT scene elements** — architecture, furniture, map-object
  set-dressing, and decals/blood (which are part of the level, always there). It must NOT reference units.
- **Units (alive OR dead) are surfaced LIVE by the scanner** (which already reports faction, state,
  dead/alive) — never baked into authored text.
- **Describe the permanent *stage*, not the transient *actors*.** "Cushioned benches drawn up around
  bloodstained ground — a place staged to watch others suffer" is permanent and correct; "two corpses on
  the floor" is transient and wrong on revisit. Chains/cages/altars/blood convey the horror without
  depending on who's currently in them.
- **Distinguish by DATA, not appearance.** A "corpse" that is a **map object** (a placed body prop, part
  of the level) is stable → may be described / deduped by asset. A "corpse" that is a **unit** (a killed
  enemy) is dynamic → excluded from static prose, left to the live scanner. During authoring I query the
  room's contents by category (the mod's WorldModel already separates map objects/interactables from
  units) to decide case-by-case.
- **Rare exception:** a unit that is genuinely a fixed, non-interactable scene fixture we specifically
  want described (e.g. a permanently-caged prisoner). Deliberate, per-case, flagged — not the default;
  prefer describing the permanent staging around it.

## The X key (mod description) vs. game examine (kept separate)

- **X = "describe scanner target", Shift+X = "describe room"** (split 2026-07-07, user decision — the
  context-aware single key was ambiguous in practice) → OUR authored content ONLY: X speaks the focused
  scanner object's asset-visual description, Shift+X the room ambiance where the cursor stands. Always
  "extra description from the mod," distinct from — never mixed with — the game's examine text.
- **Rebind "where am I" → Z** (frees X for describe; do the rebind as part of the describe-feature build
  so X isn't a dead key in the interim).
- Live object state (open / locked / empty / trapped) stays the scanner's normal announcement — X adds the
  authored *visual*, it doesn't duplicate state.

## Asset dedupe (the affordability win)

- **Key = normalized GameObject name** — strip the `_!Visual` suffix and any trailing ` (n)` instance
  index. e.g. `luxery_chest_02`, `luxery_casket_01`, `library_scroll_02`. Human-readable and stable.
- **Mesh names are unusable** — the game batches static geometry into `Combined Mesh (root: scene) N`, so
  many distinct props share one "mesh." Do not key on mesh.
- **Global** table: describe each unique asset once; a door used 500× across every area = one description.
- Doors/containers (scanner interactables) resolve to their view GameObject → normalized name → key.

## Authoring pipeline (validated via dev server, no rebuild needed)

1. **Point selection:** per RoomMap room, farthest-point sampling over its navmesh cells (count scales
   with area) + centroid + exits → spread survey points (for *my* coverage). Output ~1 ambiance anchor
   per room. Coordinates baked; room IDs never stored.
2. **Object enumeration + content categorization:** unique normalized asset names among scanner
   interactables (per area, then global). The survey also dumps, per room, its contents split by category
   (map objects / interactables with asset names vs. **units**) so authoring can factor stable map-object
   props into the prose and exclude dynamic units (see Units vs. static content).
3. **Capture:** drive the camera — fog off (`FogOfWarArea.IsCheatOffFog`), `ScrollTo` the point, canonical
   yaw 135 (+ extra headings for rooms), zoom in / bounds-fit; tight framing for a single object (the
   GameObject name confirms the subject, so minor clutter is fine; hide-other-renderers only if ambiguous;
   offline UnityPy extraction is the fallback). Restore fog/camera after.
4. **Author:** review each room's / asset's shots → short **title** + detailed **description** (flavor
   only, no baked directions).
5. **Output:** a per-area coordinate-anchored room-description file + the global asset→description table.

## Runtime surfacing

- Scanner focus (scanner nav, or comma/period cycle) → **X** reads our description (asset-visual or room
  ambiance). Directions/distances computed live from the anchor + bearing engine.
- Game examine points: unchanged game interaction + GameLogReader.

## Tooling (BUILT 2026-07-07, smoke-tested live on Shield Maze)

- **`src/Dev/DevSurvey.cs`** (DEBUG-only, public for /eval): all survey logic compile-checked in the
  mod with direct internals access — `Rooms()` (farthest-point survey points scaling with area, exits),
  `Contents(roomId)` (objects-vs-units pre-categorized for the stage-not-actors rule, described flags),
  `Assets()` (unique keys + representative instance + described flag), `Frame(x,y,z,yaw,zoom)` /
  `Restore()` (fog + camera state saved once, restored after), `Labels()` (screen-projected names for
  everything in frame — identifies props without hide-renderer tricks), `RoomIdAt` (validation).
- **`tools/survey.py`** — the driver. `rooms` / `assets` / `validate` modes; downscales captures to
  1600px (labels rescaled to match) so vision review is cheap; incremental (skips surveyed rooms and
  described assets; `--force`/`--all` to redo). Output: `survey/<AreaBlueprint>/rooms/room_NN/`
  (shots + `data.json` with contents and per-shot labels) and `assets/<key>.png` + `index.json`.
  Requires the game running via `scripts/run-game.ps1` with a save loaded in the target area.
- Authoring is then a batch review of the survey folders (parallelizable per room) → emit the
  per-area JSON + `desc.*` locale entries → `survey.py validate`.

## Phasing

1. **(done)** Prototype: camera control, point selection, vision authoring, coordinate directions,
   asset-key discovery — validated on Shield Maze (rooms 32/28/27).
2. Schema + loaders: coord-anchored room-description file + global `asset → description` table.
3. Survey script (rooms + unique assets) for capture.
4. Surfacing: X = describe (asset / room ambiance); rebind where-am-I → Z.
5. Content pass: run the survey area-by-area, author, commit description files.

## Validated facts / gotchas

- Camera: `CameraRig.ScrollTo(worldPos)` (pan), `CameraRig.SetRotation(yaw)` (holds), `CameraRig.CameraZoom
  .CurrentNormalizePosition` (0 = zoomed in, 1 = out). `FogOfWarArea.All` + `.IsCheatOffFog` to reveal for
  authoring — restore after.
- Camera is world-aligned (no flip). The historic walkthrough east/west confusion is a *separate*,
  documented thing: the minimap renders at LMR−180, so minimap-based walkthroughs read 180° off from the
  world compass. The mod's coordinate bearings are correct.
- Asset identity = normalized GameObject name; meshes are batched (unusable).
- Screenshots at 3840×2160; `/screenshot` writes one file — copy to a unique name per capture.
