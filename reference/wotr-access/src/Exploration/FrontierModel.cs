using System.Collections.Generic;
using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// Finds UNEXPLORED WALKABLE space nearby — the frontier where exploration can continue. A
    /// frontier cell is a walkable RoomMap cell (navmesh-derived, height-aware — so another floor's
    /// XZ shadow never qualifies) that the game's explored layer (<see cref="FogExplored"/>, the fog
    /// G channel) says is unexplored, adjacent to an explored walkable cell — i.e. ground you could
    /// stand on today and push the fog back. That definition inherently excludes fog over
    /// unreachable dressing (not walkable) and sealed pockets (no explored neighbour). Cells cluster
    /// into BLOBS ("openings"), each surfaced as one scanner item in the "Unexplored space" category
    /// (the L / Shift+L review cycle). Recomputed lazily on demand (a key press), cached briefly —
    /// never per-frame work.
    /// </summary>
    internal static class FrontierModel
    {
        private const float CacheSec = 2f;      // key-press bursts reuse one computation
        private const float MinSpan = 1.0f;     // m — the frontier is a ~1-cell-thick RIBBON across an
                                                // opening (walls block fog AND walkability, so the fog
                                                // boundary mostly lands on unwalkable ground); threshold
                                                // its LENGTH, not area — sub-metre ribbons are specks

        internal sealed class Blob
        {
            public Vector3 Position; // a frontier cell near the blob's centroid (walkable ground)
            public float Reach;      // max centroid→cell distance (the blob's spatial extent)
            public RoomMap.Room Room; // the room the blob's ground belongs to (may be unentered), or null
        }

        private static readonly List<Blob> _blobs = new List<Blob>();
        private static readonly List<Blob> _prev = new List<Blob>(); // last generation, for identity matching
        private static float _nextAt;
        private static int _version;
        private static string _area;

        /// <summary>The cached frontier blobs. NEVER recomputes — WorldModel folds this per frame, and
        /// a full-grid recompute is key-press work (<see cref="Refresh"/>), not frame work.</summary>
        public static IReadOnlyList<Blob> Current => _blobs;

        /// <summary>Bumped on every recompute. A recompute REUSES the previous generation's Blob for
        /// an opening it finds again (identity is what the review cycle keys on — new objects every
        /// press would reset "continue from current" to nearest, sticking the cycle at 1 of N), so
        /// this signals membership may have changed, not that every held Blob is stale.</summary>
        public static int Version => _version;

        /// <summary>Per-tick area guard (WorldModel.Tick): entering a different area (or the menu)
        /// drops the old area's blobs at once — the per-frame fold would otherwise keep surfacing
        /// them at stale coordinates until the first L press refreshes.</summary>
        public static void SyncArea(string areaName)
        {
            if (_area == areaName) return;
            _area = areaName;
            _nextAt = 0f;
            if (_blobs.Count > 0) { _blobs.Clear(); _version++; }
        }

        /// <summary>Recompute the frontier if the cache is stale — called by the L cycle before it
        /// rebuilds, so repeated presses within a burst reuse one computation.</summary>
        public static void Refresh()
        {
            if (Time.unscaledTime < _nextAt) return;
            _nextAt = Time.unscaledTime + CacheSec;
            Recompute();
        }

        private static void Recompute()
        {
            _version++;
            _prev.Clear();
            _prev.AddRange(_blobs);
            _blobs.Clear();
            if (!RoomMap.TryGetGrid(out var label, out var cellY, out int w, out int h)) return;

            float cell = RoomMap.CellSize;
            if (cell <= 0f) return;
            int minCells = Mathf.Max(3, (int)(MinSpan / cell)); // ribbon LENGTH in cells

            // Pass 1: classify each walkable cell explored/unexplored via the game's explored layer.
            // (FogExplored is a 256² lookup — cheap per cell; the whole pass is on-demand only.)
            var state = new byte[label.Length]; // 0 = not walkable, 1 = explored, 2 = unexplored
            for (int gz = 0; gz < h; gz++)
                for (int gx = 0; gx < w; gx++)
                {
                    int i = gz * w + gx;
                    if (label[i] < 0) continue;
                    state[i] = FogExplored.IsExplored(RoomMap.CellCenter(gx, gz)) ? (byte)1 : (byte)2;
                }

            // Pass 2: frontier = unexplored cells 8-adjacent to an explored cell.
            var frontier = new bool[label.Length];
            for (int gz = 1; gz < h - 1; gz++)
                for (int gx = 1; gx < w - 1; gx++)
                {
                    int i = gz * w + gx;
                    if (state[i] != 2) continue;
                    for (int dz = -1; dz <= 1 && !frontier[i]; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                            if (state[i + dz * w + dx] == 1) { frontier[i] = true; break; }
                }

            // Pass 3: flood-fill frontier cells into blobs (8-way), keep the meaningful ones.
            var stack = new Stack<int>();
            var cells = new List<int>();
            for (int start = 0; start < frontier.Length; start++)
            {
                if (!frontier[start]) continue;
                cells.Clear();
                stack.Push(start);
                frontier[start] = false;
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    cells.Add(i);
                    int cz = i / w, cx = i % w;
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nz = cz + dz, nx = cx + dx;
                            if (nz < 0 || nx < 0 || nz >= h || nx >= w) continue;
                            int n = nz * w + nx;
                            if (frontier[n]) { frontier[n] = false; stack.Push(n); }
                        }
                }
                if (cells.Count < minCells) continue;

                // Centroid, then the blob CELL nearest it (so the item sits on walkable frontier ground).
                Vector3 sum = Vector3.zero;
                foreach (var i in cells) sum += RoomMap.CellCenter(i % w, i / w);
                Vector3 centroid = sum / cells.Count;
                Vector3 best = centroid; float bestD = float.MaxValue, reach = 0f;
                foreach (var i in cells)
                {
                    var p = RoomMap.CellCenter(i % w, i / w);
                    float dx2 = p.x - centroid.x, dz2 = p.z - centroid.z;
                    float d = dx2 * dx2 + dz2 * dz2;
                    if (d < bestD) { bestD = d; best = p; }
                    if (d > reach) reach = d;
                }
                AddBlob(best, Mathf.Sqrt(reach), cell);
            }
            _prev.Clear();
        }

        /// <summary>Record a blob, reusing the previous generation's object when this is the same
        /// opening (nearest old blob whose extent overlaps ours — exact when standing still, and a
        /// receding ribbon still lands within the joined extents). Fields update in place; a split
        /// opening keeps the old identity for one half, mints the other; a vanished one is simply
        /// never claimed and drops out.</summary>
        private static void AddBlob(Vector3 pos, float reach, float cell)
        {
            Blob match = null; float bestD = float.MaxValue;
            foreach (var old in _prev)
            {
                float dx = old.Position.x - pos.x, dz = old.Position.z - pos.z;
                float d = dx * dx + dz * dz;
                float tol = old.Reach + reach + 2f * cell;
                if (d <= tol * tol && d < bestD) { bestD = d; match = old; }
            }
            if (match != null) _prev.Remove(match); else match = new Blob();
            match.Position = pos; match.Reach = reach; match.Room = RoomMap.RoomAt(pos);
            _blobs.Add(match);
        }
    }

    /// <summary>One frontier blob as a scanner item ("Unexplored space" category, silent by default).
    /// Always detectable — it's the player's own map knowledge, not something fog can hide (which is
    /// also definitionally true: the blob IS the fog edge). Slash plants the cursor on it as usual.</summary>
    internal sealed class ProxyFrontier : ScanItem
    {
        private readonly FrontierModel.Blob _blob;

        public ProxyFrontier(FrontierModel.Blob blob) { _blob = blob; }

        public override string Name
        {
            get
            {
                var room = _blob.Room;
                return room != null
                    ? Loc.T("scan.unexplored_in", new { room = RoomMap.Describe(room) })
                    : Loc.T("scan.unexplored_space");
            }
        }

        public override Vector3 Position => _blob.Position;
        public override float Footprint => _blob.Reach;
        public override bool IsVisible => true;
        public override bool CurrentlySeen => true; // bypass the fog gate — this IS the fog edge

        public override IEnumerable<string> Nodes
        {
            get { yield return ScanTaxonomy.Unexplored; }
        }

        public override string Primary => ScanTaxonomy.Unexplored;
    }
}
