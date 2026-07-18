using System.Collections.Generic;
using System.Text;
using Kingmaker.Controllers; // FogOfWarController
using UnityEngine;
using WrathAccess.Settings;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// The tiled-space lens: describes the cell the cursor is in — direction + distance from the player to
    /// the cell centre, walkable / edge / slope, fog, and any visible things standing on it. Owns the cell
    /// size (which <see cref="TileStep"/> reads). Each fragment is an individually-toggleable setting;
    /// they're still composed into one Tile-context line. The elevation change ("up 5 ft") is computed
    /// against the last cell it described, so a step reports the slope and a re-announce doesn't.
    /// </summary>
    internal sealed class GridSystem : OverlaySystem
    {
        public override string Name => "Grid";
        public override string Key => "grid";

        // A readout (announces the cursor's cell on demand) — it has no continuous playback, so "when
        // moving" doesn't apply; Off/Continuous only.
        public override System.Collections.Generic.IReadOnlyList<OverlayMode> SupportedModes => OverlayModes.OffContinuous;

        public float CellSize => Int("cell_size", 5) * Geo.MetresPerFoot; // world metres per tile
        private float Half => CellSize * 0.5f;
        private const float LevelGap = 3f; // |Δy| treated as a different level

        private float? _lastHeight; // surface height at the last described cell (for slope deltas)

        public override void RegisterSettings(CategorySetting cat)
        {
            cat.Add(new IntSetting("cell_size", "Tile size (feet)", 5, 1, 30, 1, "overlay.grid.cell_size"));
            cat.Add(new BoolSetting("bearing", "Announce direction & distance", true, "overlay.grid.bearing"));
            cat.Add(new BoolSetting("terrain", "Announce terrain", true, "overlay.grid.terrain"));
            cat.Add(new BoolSetting("contents", "Announce contents", true, "overlay.grid.contents"));
            cat.Add(new BoolSetting("fog", "Announce fog of war", true, "overlay.grid.fog"));
            cat.Add(new BoolSetting("raw", "Announce raw coordinates", false, "overlay.grid.raw"));
        }

        public override void OnExit(Overlay overlay) => _lastHeight = null;

        public override IEnumerable<OverlayAnnouncement> Announce(OverlayContext ctx)
        {
            if (!Enabled || ctx.Want != AnnouncementContext.Tile) yield break;

            var centre = ctx.Cursor;            // already snapped to a cell centre by TileStep
            float fromY = _lastHeight ?? centre.y;
            var s = NavmeshProbe.Sample(centre.x, centre.z, centre.y);

            var sb = new StringBuilder();

            if (Bool("bearing", true))
            {
                sb.Append(Geo.IsHere(ctx.Reference, centre) ? Loc.T("geo.here")
                    : Geo.Bearing(ctx.Reference, centre) + ", " + Geo.FeetStr(Geo.Distance(ctx.Reference, centre)));
                var vert = Geo.Vertical(ctx.Reference, centre);
                if (vert != null) sb.Append(", ").Append(vert);
            }

            if (Bool("terrain", true)) Append(sb, Terrain(s, fromY, centre));

            if (Bool("fog", true) && FogOfWarController.IsInFogOfWar(centre)) Append(sb, Loc.T("grid.fog_of_war"));

            if (Bool("contents", true))
            {
                var contents = Contents(centre);
                if (contents.Count > 0) Append(sb, string.Join(", ", contents.ToArray()));
            }

            if (Bool("raw", false)) Append(sb, Geo.Raw(centre));

            _lastHeight = centre.y;
            if (sb.Length > 0) yield return new OverlayAnnouncement(AnnouncementContext.Tile, Message.Raw(sb.ToString()));
        }

        private static void Append(StringBuilder sb, string frag)
        {
            if (string.IsNullOrEmpty(frag)) return;
            if (sb.Length > 0) sb.Append("; ");
            sb.Append(frag);
        }

        private string Terrain(NavmeshProbe.Surface s, float fromY, Vector3 centre)
        {
            if (!s.OnNavmesh)
            {
                if (NavmeshProbe.FloorBelow(centre.x, centre.z, centre.y, out var below))
                {
                    float drop = centre.y - below.y;
                    if (Geo.Feet(drop) >= 2f) return Loc.T("grid.edge_drop", new { drop = Geo.FeetStr(drop) });
                }
                return Loc.T("grid.not_walkable");
            }

            float dy = s.Point.y - fromY;
            if (Geo.Feet(Mathf.Abs(dy)) >= 1f)
                return Loc.T(dy > 0f ? "grid.walkable_up" : "grid.walkable_down", new { height = Geo.FeetStr(Mathf.Abs(dy)) });
            return Loc.T("grid.walkable");
        }

        // Visible things whose footprint overlaps this cell and that are on roughly this level.
        private List<string> Contents(Vector3 centre)
        {
            var names = new List<string>();
            foreach (var item in WorldModel.Items)
            {
                if (!item.IsVisible) continue;
                if (Mathf.Abs(item.Position.y - centre.y) > LevelGap) continue;
                if (!OverlapsTile(item, centre)) continue;
                var name = string.IsNullOrEmpty(item.Name) ? Loc.T("scan.object_fallback") : item.Name;
                if (!names.Contains(name)) names.Add(name);
            }
            return names;
        }

        // The item's real footprint overlaps this tile when the closest point of its shape to the tile
        // centre lands within the tile square — so a wall marks the cells along its length, not a circle.
        private bool OverlapsTile(ScanItem item, Vector3 centre)
        {
            var np = item.NearestPoint(centre);
            return Mathf.Abs(np.x - centre.x) <= Half && Mathf.Abs(np.z - centre.z) <= Half;
        }
    }
}
