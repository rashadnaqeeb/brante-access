using Kingmaker.View; // ObstacleAnalyzer
using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// Reads the walkable navmesh (the A* recast graph) at a point: is this spot walkable, what height is
    /// the surface, which connected level (Area) is it, and — for the multi-level case — is there another
    /// walkable floor above/below this column. All the spatial truth the tile view needs; see the
    /// exploration-world-model memory (navmesh = A* RecastGraph; world units are metres).
    /// </summary>
    internal static class NavmeshProbe
    {
        /// <summary>A walkable surface sample at one column.</summary>
        internal struct Surface
        {
            public bool OnNavmesh; // the queried XZ is itself walkable (snapped node sits ~on it)
            public Vector3 Point;  // the snapped navmesh point (valid when OnNavmesh)
            public uint Area;      // connected-component id of that node (distinguishes stacked levels)
            public Surface(bool on, Vector3 p, uint area) { OnNavmesh = on; Point = p; Area = area; }
            public static readonly Surface None = new Surface(false, Vector3.zero, 0u);
        }

        // How far (XZ, metres) the snapped node may sit from the query before we call the query off-mesh.
        // Generous enough that a tile centre microscopically off the mesh still reads as walkable.
        private const float OnMeshXZ = 0.35f;

        // Ground/terrain layer mask, copied verbatim from GeometryUtils.TryProjectToGround — the layers
        // the game itself treats as "ground" for vertical projection.
        private const int GroundMask = 2099457;

        /// <summary>
        /// Sample the walkable surface at column (x, z), preferring the level near <paramref name="seedY"/>.
        /// ObstacleAnalyzer.GetNearestNode does the heavy lifting: XZ-nearest with a 3 m height correction
        /// that re-snaps to the Area matching the true 3D-nearest, so we stay on the current floor when
        /// surfaces stack.
        /// </summary>
        public static Surface Sample(float x, float z, float seedY)
        {
            var info = ObstacleAnalyzer.GetNearestNode(new Vector3(x, seedY, z));
            if (info.node == null) return Surface.None;
            float dx = x - info.position.x, dz = z - info.position.z;
            bool onMesh = dx * dx + dz * dz <= OnMeshXZ * OnMeshXZ;
            return new Surface(onMesh, info.position, info.node.Area);
        }

        /// <summary>Nearest walkable floor strictly below <paramref name="fromY"/> at (x, z).</summary>
        public static bool FloorBelow(float x, float z, float fromY, out Vector3 floor)
            => CastVertical(x, z, fromY - 0.1f, down: true, out floor);

        /// <summary>Nearest walkable floor above <paramref name="fromY"/> at (x, z).</summary>
        public static bool FloorAbove(float x, float z, float fromY, out Vector3 floor)
            => CastVertical(x, z, fromY + 0.1f, down: false, out floor);

        private static bool CastVertical(float x, float z, float startY, bool down, out Vector3 floor)
        {
            floor = Vector3.zero;
            var from = new Vector3(x, startY, z);
            var to = new Vector3(x, down ? -5000f : 5000f, z);
            if (!Physics.Linecast(from, to, out var hit, GroundMask)) return false;
            // Only count it if that physics floor is actually walkable navmesh, not just any collider
            // (a roof, a railing, a prop). Seeds at the hit height so it resolves that floor's level.
            if (!ObstacleAnalyzer.IsPointInsideNavMesh(hit.point)) return false;
            floor = hit.point;
            return true;
        }
    }
}
