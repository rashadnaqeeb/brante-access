using UnityEngine;
using WrathAccess.Input; // OsKeyboard (typematic cadence)

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// Walks the cursor across an imaginary 5-ft grid with the arrow keys (one tile = a tabletop square).
    /// The cursor is a "standing position": its height follows the walkable surface, and at a level
    /// boundary it does NOT fall — it keeps its height so the player can feel the edge. Stacked levels are
    /// reached by a connected ramp or an explicit follow-down/up (<see cref="VerticalFollow"/>). The cell
    /// size comes from the sibling <see cref="GridSystem"/> (one per overlay). It re-snaps from the shared
    /// cursor each action, so a jump made elsewhere (the scanner's Home) is honoured. Tile context — the
    /// readout itself is <see cref="GridSystem"/>'s job.
    /// </summary>
    internal sealed class TileStep : MovementMode
    {
        private readonly MovementSlot _slot;
        public TileStep(MovementSlot slot) { _slot = slot; }

        public override string Name => "Tile stepping";
        public override MovementSlot Slot => _slot;
        public override AnnouncementContext Context => AnnouncementContext.Tile;

        private static float Cell(Overlay overlay)
            => overlay.Get<GridSystem>()?.CellSize ?? (5f * Geo.MetresPerFoot);

        private static float Snap(float v, float cell) => (Mathf.Floor(v / cell) + 0.5f) * cell;

        public override void OnEnter(Overlay overlay) => Resync(overlay); // snap to the nearest cell centre

        // Stepping POLLS the slot's held arrows as one vector (so Up+Right = a single diagonal step),
        // with its own typematic cadence (the user's OS delay/rate): one step on press, a pause, then
        // repeats while held. Per-action auto-repeat can't do this — two held keys would repeat
        // independently and zigzag at double speed.
        private bool _holding;
        private float _nextStep;

        public override void Tick(float dt, Overlay overlay)
        {
            // No HUD-focus gate needed: with the HUD focused the primary arrows are SHADOWED by the UI
            // category (InputManager.Held reads live bindings only), while the secondary slot keeps moving.
            if (!OverlayManager.Active) { _holding = false; return; }
            CursorKeys.HeldVector(_slot, out int dx, out int dz);
            if (dx == 0 && dz == 0) { _holding = false; return; }

            // A diagonal tile is sqrt(2) longer than a cardinal one; stretch the repeat interval to
            // match so held-diagonal GROUND speed equals cardinal (the step itself stays on-grid).
            float stretch = (dx != 0 && dz != 0) ? 1.41421356f : 1f;
            float now = Time.unscaledTime;
            if (!_holding)
            {
                _holding = true;
                _nextStep = now + OsKeyboard.InitialDelay;
                Step(dx, dz, overlay);
            }
            else if (now >= _nextStep)
            {
                _nextStep = now + OsKeyboard.RepeatInterval * stretch;
                Step(dx, dz, overlay);
            }
        }

        private void Step(int dx, int dz, Overlay overlay)
        {
            float cell = Cell(overlay);
            var p = overlay.Cursor.Position;
            float x = Snap(p.x, cell) + dx * cell, z = Snap(p.z, cell) + dz * cell, y = p.y;
            var s = NavmeshProbe.Sample(x, z, y);
            if (s.OnNavmesh) y = s.Point.y; // follow the surface; otherwise keep height (never fall)
            overlay.Cursor.Position = new Vector3(x, y, z);
            overlay.Announce(Context); // the landing readout (GridSystem composes it)
        }

        public override void Recenter(Overlay overlay)
        {
            float cell = Cell(overlay);
            var p = Cursor.PlayerPosition;
            overlay.Cursor.Position = new Vector3(Snap(p.x, cell), p.y, Snap(p.z, cell));
        }

        public override VerticalResult VerticalFollow(int dir, Overlay overlay)
        {
            float cell = Cell(overlay);
            var p = overlay.Cursor.Position;
            float x = Snap(p.x, cell), z = Snap(p.z, cell), y = p.y;
            bool found = dir < 0
                ? NavmeshProbe.FloorBelow(x, z, y, out var floor)
                : NavmeshProbe.FloorAbove(x, z, y, out floor);
            if (!found) return VerticalResult.NoSurface;
            overlay.Cursor.Position = new Vector3(x, floor.y, z);
            return VerticalResult.Moved;
        }

        // Snap the shared cursor onto a cell centre without moving it (keeps grid + shared cursor aligned).
        private static void Resync(Overlay overlay)
        {
            float cell = Cell(overlay);
            var p = overlay.Cursor.Position;
            overlay.Cursor.Position = new Vector3(Snap(p.x, cell), p.y, Snap(p.z, cell));
        }
    }
}
