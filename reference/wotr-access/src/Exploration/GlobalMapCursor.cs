using System.IO;
using System.Linq;
using Kingmaker.Globalmap.View;
using UnityEngine;
using WrathAccess.Audio;
using WrathAccess.Exploration.Overlays; // OverlayAudio
using WrathAccess.Input;
using WrathAccess.Settings;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// The world-map MOVEMENT cursor — free-roam over the XZ plane with WASD/arrows (the analogue of the
    /// in-area cursor; isolated, no navmesh). A SEPARATE point from the in-area <see cref="Cursor"/> so it
    /// never pollutes it. Each slot (primary WASD, secondary Shift+WASD) has its own world-map movement
    /// type: <b>continuous</b> glides at the slot's miles/sec speed; <b>tiled</b> steps on the OS typematic
    /// cadence by the world-map tile size, announcing each landing (for tracking discrete positions, e.g.
    /// army movement) — mirroring the in-area <see cref="Overlays.TileStep"/>. Crossing onto/off a point
    /// plays the same object enter/leave cue as in-area. <b>Enter</b> acts on the point under it; <b>C</b>
    /// recenters on the party; <b>K</b> reads it; <b>/</b> jumps to the review cursor.
    /// </summary>
    internal static class GlobalMapCursor
    {
        private const float Padding = 0.4f; // a little extra reach past each point's footprint so it's easy to land on

        private static Vector3? _pos;
        private static GlobalMapPointView _inside; // the point the cursor is on (for the enter/leave cue)
        private static GlobalMapPointView _spoken; // last point the idle-settle readout spoke (null = armed)
        private static bool _baselined;            // don't fire the cue on the first tick / on entering the map

        // Per-slot typematic state for tiled stepping (one step on press, a pause, then repeats while held).
        private sealed class TiledState { public bool Holding; public float NextStep; }
        private static readonly TiledState _primaryTiled = new TiledState();
        private static readonly TiledState _secondaryTiled = new TiledState();

        public static void Reset()
        {
            _pos = null; _inside = null; _spoken = null; _baselined = false;
            _primaryTiled.Holding = false; _secondaryTiled.Holding = false;
        }

        /// <summary>The cursor's point — its placed position, else the party's.</summary>
        public static Vector3 Position => _pos ?? GlobalMapModel.TravelerPos;

        // Per-frame movement + the object enter/leave cue. InputManager.Held respects the claim chain, so the
        // worldmap.cursor* keys read as held only while the cursor (not the focused list) owns the arrows.
        public static void Tick(float dt)
        {
            // Tied to the engaged overlay (Ctrl+O): the cursor runs only when an overlay is active on the
            // world map and no location panel is open — same as the in-area cursor lives under an overlay.
            // Otherwise pause (keep _pos), re-baselining the cue so it doesn't fire spuriously on resume.
            if (!OverlayManager.Active || OverlayManager.CurrentScope != OverlayScope.WorldMap
                || WrathAccess.Screens.GlobalMapScreen.PanelActive || !GlobalMapModel.Interactive)
            {
                _inside = null; _spoken = null; _baselined = false;
                _primaryTiled.Holding = false; _secondaryTiled.Holding = false;
                return;
            }

            // Each slot moves per its own world-map movement type (continuous glide / tiled step / none).
            // `continuous` = a slot glided this frame; `tiled` = a tiled slot is held (stepping cadence is
            // managed inside). Held slots are additive.
            bool continuous = false, tiled = false;
            MoveSlot("worldmap.cursor", "primary", dt, _primaryTiled, ref continuous, ref tiled);
            MoveSlot("worldmap.secondary", "secondary", dt, _secondaryTiled, ref continuous, ref tiled);

            var inside = NearestWithin();

            // Object cue: same wavs + shared volume as the in-area ObjectCueSystem. Fires on a change of the
            // point we're inside — enter when arriving on one (incl. a discrete tiled jump), leave to none.
            if (!_baselined) { _inside = inside; _spoken = inside; _baselined = true; return; }
            if (inside != _inside) { PlayCue(inside != null); _inside = inside; }

            // Continuous idle-settle: gliding is too fast to narrate, so once the keys are released, speak the
            // point the cursor sits on once (quiet over nothing), like in-area's idle hover announce. TILED
            // instead announces each step itself (and sets _spoken there), so it's excluded here: we only
            // re-arm (_spoken = null) on continuous motion, and never speak the settle on a tiled frame.
            if (continuous || inside == null) _spoken = null;
            else if (!tiled && inside != _spoken) { Tts.Speak(GlobalMapActions.InPlace(inside)); _spoken = inside; }
        }

        // One slot's movement this frame, dispatched on its world-map movement type. Continuous glides _pos
        // by its held arrows (+Z north, +X east) × speed; tiled defers to the typematic stepper; none/idle
        // does nothing (and clears the slot's hold so the next press re-arms the typematic first step).
        private static void MoveSlot(string prefix, string slot, float dt, TiledState st,
            ref bool continuous, ref bool tiled)
        {
            int dx = 0, dz = 0;
            if (InputManager.Held(prefix + "Up")) dz += 1;
            if (InputManager.Held(prefix + "Down")) dz -= 1;
            if (InputManager.Held(prefix + "Right")) dx += 1;
            if (InputManager.Held(prefix + "Left")) dx -= 1;

            string mode = ModeOf(slot);
            if (mode == "none" || (dx == 0 && dz == 0)) { st.Holding = false; return; }

            if (mode == "tiled") { tiled = true; TiledStep(dx, dz, st); return; }

            continuous = true;
            if (!_pos.HasValue) _pos = GlobalMapModel.TravelerPos; // plant at the party on first move
            _pos = _pos.Value + new Vector3(dx, 0f, dz).normalized * (Speed(slot) * dt);
        }

        // This cursor slot's settings on the ENGAGED overlay (per-overlay world-map mode/speed) — so cycling
        // overlays drives the cursor, like the in-area cursor reads its overlay's slots.
        private static CategorySetting SlotCat(string slot)
            => OverlayManager.ActiveOverlay?.Cursor?.Slot(slot == "primary" ? MovementSlot.Primary : MovementSlot.Secondary);

        // This slot's world-map movement type (continuous / tiled / none), defaulting to continuous.
        private static string ModeOf(string slot)
            => SlotCat(slot)?.Get<ChoiceSetting>("worldmap_mode")?.Current?.Id ?? "continuous";

        // The slot's world-map glide speed in miles/sec. The global map equates 1 world unit with 1 mile (see
        // GlobalMapMovementController), so this is also units/sec — no conversion needed when gliding _pos.
        private static float Speed(string slot)
            => SlotCat(slot)?.Get<IntSetting>("worldmap_speed")?.Get() ?? 18;

        // Typematic tiled stepping (mirrors the in-area TileStep cadence): one step on first press, a pause
        // of the OS initial delay, then repeats while held. Diagonals stretch the interval by sqrt(2) so the
        // held-diagonal ground speed matches cardinal.
        private static void TiledStep(int dx, int dz, TiledState st)
        {
            float stretch = (dx != 0 && dz != 0) ? 1.41421356f : 1f;
            float now = Time.unscaledTime;
            if (!st.Holding) { st.Holding = true; st.NextStep = now + OsKeyboard.InitialDelay; DoTiledStep(dx, dz); }
            else if (now >= st.NextStep) { st.NextStep = now + OsKeyboard.RepeatInterval * stretch; DoTiledStep(dx, dz); }
        }

        // Snap onto the world-map tile grid (cell centres) and step one tile in the held direction, then read
        // the landing: the point we're on (name + state), else the bearing + miles from the party.
        private static void DoTiledStep(int dx, int dz)
        {
            float cell = TileSize();
            if (!_pos.HasValue) _pos = GlobalMapModel.TravelerPos;
            var p = _pos.Value;
            _pos = new Vector3(Snap(p.x, cell) + dx * cell, 0f, Snap(p.z, cell) + dz * cell);

            var on = NearestWithin();
            if (on != null) { Tts.Speak(GlobalMapActions.InPlace(on)); _spoken = on; }
            else { Tts.Speak(GlobalMapActions.PositionAt(Position)); _spoken = null; }
        }

        private static float Snap(float v, float cell) => (Mathf.Floor(v / cell) + 0.5f) * cell;

        // The world-map tile size in MILES (== world units), shared with the in-area grid's settings home.
        private static float TileSize()
            => ModSettings.GetSetting<IntSetting>("defaults.grid.worldmap_cell_size")?.Get() ?? 2;

        private static void PlayCue(bool enter)
        {
            float vol = (ModSettings.GetSetting<IntSetting>("audio.volumes.object")?.Get() ?? 100) / 100f * OverlayAudio.Master;
            AudioEngines.NAudio.Play2D(Path.Combine(OverlayAudio.Dir, enter ? "object_enter.wav" : "object_exit.wav"), vol);
        }

        // ---- on-demand keys ----
        public static void Recenter() { _pos = GlobalMapModel.TravelerPos; Settle(); }

        public static void JumpToReview()
        {
            var p = GlobalMapScanner.SelectedPosition;
            if (!p.HasValue) { Tts.Speak(Loc.T("worldmap.scan_none")); return; }
            _pos = p.Value;
            Settle();
        }

        // K: read what the cursor is on (manual readout).
        public static void Announce()
        {
            var p = NearestWithin();
            if (p != null) Tts.Speak(GlobalMapActions.InPlace(p));
            else Tts.Speak(Loc.T("worldmap.cursor_empty"));
        }

        // Snap-and-read (recenter / jump-to-review): speak the point we land on and baseline the cue +
        // idle readout so the next Tick doesn't repeat it.
        private static void Settle()
        {
            var p = NearestWithin();
            _inside = p; _spoken = p;
            if (p != null) Tts.Speak(GlobalMapActions.InPlace(p));
            else Tts.Speak(Loc.T("worldmap.cursor_empty"));
        }

        public static void Interact()
        {
            // Only act in the pure global-map mode. Under a rest / dialog / book-event / battle overlay the
            // overlay owns input — acting here (Go → HandleClick) restarts the journey that re-fires the
            // event, the infinite loop. (Travel "pauses" under those overlays too, so we can't rely on
            // TravelPaused alone.)
            if (!GlobalMapModel.Interactive) return;
            // Mid-journey pause (the game's move-helper Continue): Enter resumes travel, like the game's own
            // primary travel input. Otherwise act on the point under the cursor.
            if (GlobalMapModel.TravelPaused) { GlobalMapActions.ResumeTravel(); return; }
            var p = NearestWithin();
            if (p != null) GlobalMapActions.Go(p);
            else Tts.Speak(Loc.T("worldmap.cursor_empty"));
        }

        // The nearest point whose OWN footprint contains the cursor, or null. Each point uses its real
        // clickable radius (below) rather than one fixed circle — a fixed 8-unit radius was far larger than a
        // location's icon, so the cursor read "on" many overlapping points and exact selection was hard.
        private static GlobalMapPointView NearestWithin()
        {
            GlobalMapPointView best = null;
            float bd = float.MaxValue;
            var c = Position;
            foreach (var pt in GlobalMapModel.Locations.Concat(GlobalMapModel.Junctions))
            {
                if (pt == null) continue;
                float d = Geo.Distance(c, pt.transform.position);
                if (d <= PointRadius(pt) && d < bd) { bd = d; best = pt; }
            }
            return best;
        }

        // A point's real clickable radius: the SphereCollider the game gives it (GlobalMapPointView.OnEnable —
        // a LOCATION's is its icon's half-width `renderer.bounds.extents.x`; a WAYPOINT's is 0.5), read live
        // from the collider's world bounds (scale-adjusted) so it matches the game's actual click target,
        // plus a small <see cref="Padding"/> so it's comfortable to land on. A modest fallback covers the rare
        // frame before the collider is built.
        private static float PointRadius(GlobalMapPointView pt)
        {
            var col = pt.GetComponent<Collider>();
            if (col != null)
            {
                var e = col.bounds.extents;
                float r = Mathf.Max(e.x, e.z);
                if (r > 0.01f) return r + Padding;
            }
            return 1.5f;
        }
    }
}
