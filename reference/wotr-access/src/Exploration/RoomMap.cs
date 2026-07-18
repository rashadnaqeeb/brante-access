using System;
using System.Collections.Generic;
using Kingmaker;
using Pathfinding;
using UnityEngine;
using WrathAccess.Screens;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// Splits the current area's walkable space into ROOMS for orientation ("Room 12, large hall"):
    /// rasterize the live A* recast navmesh to a grid, compute per-cell clearance (distance to the
    /// nearest wall), then persistence watershed — basins grow from clearance maxima and split where
    /// they meet across a pronounced dip (a doorway, or a doorless cave pinch; PERSIST is how deep the
    /// dip must be). Small regions merge into their biggest neighbour; survivors are numbered stably
    /// (sorted by centroid) and classified by area/elongation/clearance (passage / corridor / small
    /// room / room / large hall / stairs). Height-aware: cells never union across a height step
    /// (DyGate), and sloped cells (recast turns staircases into ramps) never union with flat ones —
    /// so stacked levels split into separate rooms and the staircase between them is its own "stairs"
    /// room, making it the obvious exit between levels. Tuned on the Shield Maze offline
    /// (rooms_proto2.py renders the floor plan; thresholds chosen by eye — the user prefers MORE
    /// rooms over fewer, for clearer space indicators). Rebuilt when the area part changes; ~100ms
    /// once per load.
    ///
    /// Consumers: Y's "where am I" appends the room; an optional announce-on-room-change watches the
    /// scan reference (cursor, else leader) with a dwell so boundary dithering doesn't flap.
    /// </summary>
    internal static class RoomMap
    {
        private const float Cell = 0.25f;        // metres per grid cell
        private const float Persist = 0.7f;      // clearance dip (m) required to split two basins
        private const float MinRoomArea = 12f;   // m^2 — smaller regions merge into a neighbour
        private const float CutFloor = 0.45f;    // cells narrower than this never union (assigned after)
        private const float SlopeT = 0.35f;      // rise/run above which a cell is sloped (stairs ~0.6-0.8)
        private const float DyGate = 0.6f;       // max height step (m) across which cells may join a room
        private const float MinStairArea = 2.5f; // m^2 — stair regions below this merge away (vs 12 for flat)
        private const float StairMinRise = 1.5f; // m a sloped region must CLIMB to count as stairs (bumps ~0.6m)
        private const float FurnitureMax = 12f;  // m^2 — interior obstacle islands up to this cast no clearance shadow
        private const float MaxCells = 1.7e6f;   // grid budget; coarsen the cell size beyond it
        private const float LevelGap = 3f;       // |y| beyond which a cell is "another floor"

        public sealed class Room
        {
            public int Id;            // stable 1..N (sorted by centroid)
            public string ClassKey;   // room.class.* locale suffix
            public float Area;        // m^2
            public Vector3 Centroid;
            public readonly List<Exit> Exits = new List<Exit>();
        }

        /// <summary>One walkable opening between two rooms — a cluster of navmesh portal edges.
        /// A room pair joined by two separate doorways yields two exits. Doors that CUT the navmesh
        /// (closed) have no connections there — the scanner adds door items to the V-cycle separately.</summary>
        public sealed class Exit
        {
            public Vector3 Position;   // the opening's centre — the cursor target
            public Vector3[] Edges;    // navmesh portal edges (flat endpoint pairs) — fallback geometry
            public Vector3[] Boundary; // the watershed boundary cells of this opening (complete; preferred)
            public Room To;
        }

        // One navmesh portal crossing the boundary between two rooms: the shared triangle edge (A→B)
        // and its midpoint. Clustering by shared ENDPOINT (not midpoint distance) keeps one wide or
        // curved opening as a single exit — adjacent border edges share a vertex however long they are.
        private struct PortalEdge { public Vector3 A, B, Mid; }

        private static string _builtFor;      // areaName|partName the grid was built for
        private static int[] _label;          // per-cell room index (-1 = not walkable), row-major [z*W+x]
        private static float[] _cellY;        // per-cell surface height (last rasterized)
        private static int _w, _h;
        private static float _cell, _x0, _z0;
        private static readonly List<Room> _rooms = new List<Room>();

        public static IReadOnlyList<Room> Rooms => _rooms;
        public static bool Ready => _label != null && _rooms.Count > 0;

        /// <summary>Grid snapshot for derived overlays (the unexplored-frontier scan): per-cell room
        /// label (-1 = unwalkable), per-cell surface height, dims. False while no map is built.</summary>
        internal static bool TryGetGrid(out int[] label, out float[] cellY, out int w, out int h)
        {
            label = _label; cellY = _cellY; w = _w; h = _h;
            return _label != null && _cellY != null && _w > 0 && _h > 0;
        }

        /// <summary>World-space centre of a grid cell — the exact inverse of RoomAt's mapping
        /// (note the +1 border offset), with the cell's rasterized surface height.</summary>
        internal static Vector3 CellCenter(int gx, int gz)
        {
            float x = _x0 + ((gx - 1) + 0.5f) * _cell;
            float z = _z0 + ((gz - 1) + 0.5f) * _cell;
            int idx = gz * _w + gx;
            float y = _cellY != null && idx >= 0 && idx < _cellY.Length ? _cellY[idx] : 0f;
            return new Vector3(x, y, z);
        }

        /// <summary>Metres per grid cell (for area/size math on the snapshot).</summary>
        internal static float CellSize => _cell;

        /// <summary>The room at a world position, or null (off-mesh, other floor, or no map yet).</summary>
        public static Room RoomAt(Vector3 pos)
        {
            if (_label == null) return null;
            int gx = (int)((pos.x - _x0) / _cell) + 1;
            int gz = (int)((pos.z - _z0) / _cell) + 1;
            if (gx < 0 || gz < 0 || gx >= _w || gz >= _h) return null;
            // Direct hit, else the nearest labeled cell within 2 cells (residual unlabeled slivers in
            // tight offshoots, and positions hugging a wall, still resolve to the obvious room).
            for (int ring = 0; ring <= 2; ring++)
                for (int dz = -ring; dz <= ring; dz++)
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        if (Math.Max(Math.Abs(dz), Math.Abs(dx)) != ring) continue;
                        int nz = gz + dz, nx = gx + dx;
                        if (nz < 0 || nx < 0 || nz >= _h || nx >= _w) continue;
                        int idx = nz * _w + nx;
                        int l = _label[idx];
                        if (l < 0 || l >= _rooms.Count) continue;
                        if (Mathf.Abs(_cellY[idx] - pos.y) > LevelGap) continue;
                        return _rooms[l];
                    }
            return null;
        }

        // ---- lifecycle ----

        private static int _retryCooldown; // frames until the next attempt while the graph is empty

        public static void Tick()
        {
            var game = Game.Instance;
            var area = game?.CurrentlyLoadedArea;
            if (area == null) { _builtFor = null; _label = null; _rooms.Clear(); return; }
            var part = Kingmaker.Blueprints.Area.AreaService.Instance?.CurrentAreaPart;
            string key = area.name + "|" + (part != null ? part.name : "");
            if (key != _builtFor)
            {
                _builtFor = key;
                _label = null;
                _rooms.Clear();
                _retryCooldown = 0;
            }
            // The nav graph STREAMS IN after the part key changes — building immediately can see an
            // empty graph (Gray Garrison: "0 rooms in 4ms", then never retried, so the whole area
            // had no rooms). Success latches via Ready; until then, retry on a cooldown.
            if (!Ready && AstarPath.active != null && --_retryCooldown <= 0)
            {
                _retryCooldown = 30; // ~half a second between attempts; an empty-graph pass is ~4ms
                try
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    Build();
                    if (_rooms.Count > 0)
                        Main.Log?.Log("[rooms] " + key + ": " + _rooms.Count + " rooms in " + sw.ElapsedMilliseconds + "ms");
                }
                catch (Exception e)
                {
                    _label = null; _rooms.Clear();
                    _retryCooldown = 300; // a real failure: back off, the next part change resets
                    Main.Log?.Warning("[rooms] build failed: " + e.Message);
                }
            }
            TickAnnounce();
        }

        // ---- announce on room change (instant; no dwell/guard) ----

        private static Room _announced;

        private static bool AnnounceEnabled =>
            WrathAccess.Settings.ModSettings.GetCategory("defaults.cursor")
                ?.Get<WrathAccess.Settings.BoolSetting>("announce_rooms")?.Get() ?? true;

        private static void TickAnnounce()
        {
            if (!Ready || !FocusMode.Active
                || ScreenManager.Current == null || ScreenManager.Current.Key != "ctx.ingame"
                || !AnnounceEnabled)
            { return; }

            var pos = Cursor.Has ? Cursor.Position.Value : Overlays.Cursor.PlayerPosition;
            var room = RoomAt(pos);
            if (room == null || room == _announced) return;

            _announced = room;
            WrathAccess.Events.EventDispatcher.Raise(new WrathAccess.Events.RoomChangedEvent(room));
        }

        public static string Describe(Room room)
        {
            string s = Loc.T("where.room", new { id = room.Id }) + ", " + Loc.T("room.class." + room.ClassKey);
            int pct = UnexploredPercent(room);
            if (pct >= 100) return s + ", " + Loc.T("room.unexplored");
            if (pct > 0) return s + ", " + Loc.T("room.unexplored_pct", new { pct });
            return s; // fully explored rooms stay quiet — no "0% unexplored" noise
        }

        /// <summary>How much of a room the game's explored layer still hides, as the SPOKEN
        /// percentage: rounded to tens (0, 10 … 100 — grid resolution doesn't honestly support
        /// finer), so callers comparing against 100 match what Describe says.</summary>
        public static int UnexploredPercent(Room room)
            => Mathf.RoundToInt(UnexploredFraction(room) * 10f) * 10;

        // Room.Id -> (fraction, cache expiry). Fraction needs a full-grid pass, so it's cached
        // briefly; callers are announcements (key presses, room-change events), never frame loops.
        private static readonly Dictionary<int, KeyValuePair<float, float>> _unexplored
            = new Dictionary<int, KeyValuePair<float, float>>();

        private static float UnexploredFraction(Room room)
        {
            if (room == null || _label == null) return 0f;
            KeyValuePair<float, float> hit;
            if (_unexplored.TryGetValue(room.Id, out hit) && Time.unscaledTime < hit.Value)
                return hit.Key;
            int label = room.Id - 1; // ids are the label indices, 1-based
            int total = 0, unexp = 0;
            for (int gz = 0; gz < _h; gz++)
                for (int gx = 0; gx < _w; gx++)
                {
                    int i = gz * _w + gx;
                    if (_label[i] != label) continue;
                    total++;
                    if (!FogExplored.IsExplored(CellCenter(gx, gz))) unexp++;
                }
            float f = total > 0 ? (float)unexp / total : 0f;
            _unexplored[room.Id] = new KeyValuePair<float, float>(f, Time.unscaledTime + 2f);
            return f;
        }

        /// <summary>Debug: speak the room count + the current room's stats; full table to Player.log.</summary>
        public static void DebugSpeak()
        {
            if (!Ready) { Tts.Speak("No room map", interrupt: true); return; }
            foreach (var r in _rooms)
                Main.Log?.Log(string.Format("[rooms] {0}: {1} area={2:0}m2 centroid=({3:0.0},{4:0.0},{5:0.0})",
                    r.Id, r.ClassKey, r.Area, r.Centroid.x, r.Centroid.y, r.Centroid.z));
            var pos = Cursor.Has ? Cursor.Position.Value : Overlays.Cursor.PlayerPosition;
            var cur = RoomAt(pos);
            Tts.Speak(_rooms.Count + " rooms" + (cur != null ? "; " + Describe(cur) : ""), interrupt: true);
        }

        // ---- the pipeline (port of rooms_proto.py) ----

        private static void Build()
        {
            _rooms.Clear();
            _label = null;
            _unexplored.Clear();

            // 1) Collect the recast navmesh triangles (world metres).
            var tris = new List<Vector3>(); // groups of 3
            var graphs = AstarPath.active.data?.graphs;
            if (graphs == null) return;
            foreach (var g in graphs)
            {
                if (!(g is NavmeshBase)) continue;
                g.GetNodes(node =>
                {
                    var t = node as TriangleMeshNode;
                    if (t == null) return;
                    tris.Add((Vector3)t.GetVertex(0));
                    tris.Add((Vector3)t.GetVertex(1));
                    tris.Add((Vector3)t.GetVertex(2));
                });
            }
            if (tris.Count < 3) return;

            // 2) Grid bounds; coarsen the cell if the area is huge.
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < tris.Count; i++)
            {
                var v = tris[i];
                if (v.x < minX) minX = v.x;
                if (v.x > maxX) maxX = v.x;
                if (v.z < minZ) minZ = v.z;
                if (v.z > maxZ) maxZ = v.z;
            }
            _cell = Cell;
            while (((maxX - minX) / _cell + 3) * ((maxZ - minZ) / _cell + 3) > MaxCells) _cell *= 1.5f;
            _x0 = minX; _z0 = minZ;
            _w = (int)((maxX - minX) / _cell) + 3;
            _h = (int)((maxZ - minZ) / _cell) + 3;
            int n = _w * _h;
            var walk = new bool[n];
            _cellY = new float[n];

            // 3) Rasterize triangles (barycentric point-in-triangle at cell centres).
            for (int i = 0; i < tris.Count; i += 3)
            {
                Vector3 a = tris[i], b2 = tris[i + 1], c = tris[i + 2];
                float ax = (a.x - _x0) / _cell + 1, az = (a.z - _z0) / _cell + 1;
                float bx = (b2.x - _x0) / _cell + 1, bz = (b2.z - _z0) / _cell + 1;
                float cx = (c.x - _x0) / _cell + 1, cz = (c.z - _z0) / _cell + 1;
                int lox = Mathf.Max(0, (int)Mathf.Min(ax, Mathf.Min(bx, cx)));
                int hix = Mathf.Min(_w - 1, (int)Mathf.Max(ax, Mathf.Max(bx, cx)) + 1);
                int loz = Mathf.Max(0, (int)Mathf.Min(az, Mathf.Min(bz, cz)));
                int hiz = Mathf.Min(_h - 1, (int)Mathf.Max(az, Mathf.Max(bz, cz)) + 1);
                float d = (bz - cz) * (ax - cx) + (cx - bx) * (az - cz);
                if (Mathf.Abs(d) < 1e-9f) continue;
                for (int gz = loz; gz <= hiz; gz++)
                    for (int gx = lox; gx <= hix; gx++)
                    {
                        float px = gx + 0.5f, pz = gz + 0.5f;
                        float l1 = ((bz - cz) * (px - cx) + (cx - bx) * (pz - cz)) / d;
                        float l2 = ((cz - az) * (px - cx) + (ax - cx) * (pz - cz)) / d;
                        float l3 = 1 - l1 - l2;
                        if (l1 >= -0.001f && l2 >= -0.001f && l3 >= -0.001f)
                        {
                            int ci = gz * _w + gx;
                            walk[ci] = true;
                            _cellY[ci] = l1 * a.y + l2 * b2.y + l3 * c.y;
                        }
                    }
            }

            // 3.5) Furniture mask: small interior unwalkable ISLANDS — crates, pillars, things you
            //      can walk all the way around — cast no clearance shadow, so the watershed never
            //      reads the pinch beside them as a doorway (user report: mid-room obstacles were
            //      splitting rooms; the four-pillar hall carved into quadrants). Only blobs fully
            //      surrounded by walkable space count; anything connected to the walls/outside
            //      (via the grid border) is real structure.
            var noShadow = new bool[n];
            {
                int[] dz8f = { -1, 1, 0, 0, -1, -1, 1, 1 };
                int[] dx8f = { 0, 0, -1, 1, -1, 1, -1, 1 };
                var visited = new bool[n];
                var stack = new Stack<int>();
                var cells = new List<int>();
                for (int i = 0; i < n; i++)
                {
                    if (walk[i] || visited[i]) continue;
                    cells.Clear();
                    bool touchesBorder = false;
                    visited[i] = true;
                    stack.Push(i);
                    while (stack.Count > 0)
                    {
                        int j = stack.Pop();
                        cells.Add(j);
                        int gz = j / _w, gx = j % _w;
                        if (gz == 0 || gx == 0 || gz == _h - 1 || gx == _w - 1) touchesBorder = true;
                        for (int k = 0; k < 8; k++)
                        {
                            int nz = gz + dz8f[k], nx = gx + dx8f[k];
                            if (nz < 0 || nx < 0 || nz >= _h || nx >= _w) continue;
                            int m = nz * _w + nx;
                            if (!walk[m] && !visited[m]) { visited[m] = true; stack.Push(m); }
                        }
                    }
                    if (!touchesBorder && cells.Count * _cell * _cell <= FurnitureMax)
                        foreach (var j in cells) noShadow[j] = true;
                }
            }

            // 4) Chamfer 3-4 distance transform → clearance in metres.
            var dist = new int[n];
            const int INF = int.MaxValue / 4;
            for (int i = 0; i < n; i++) dist[i] = (walk[i] || noShadow[i]) ? INF : 0;
            for (int gz = 0; gz < _h; gz++)
                for (int gx = 0; gx < _w; gx++)
                {
                    int i = gz * _w + gx;
                    if (dist[i] == 0) continue;
                    int best = dist[i];
                    if (gx > 0) best = Math.Min(best, dist[i - 1] + 3);
                    if (gz > 0)
                    {
                        best = Math.Min(best, dist[i - _w] + 3);
                        if (gx > 0) best = Math.Min(best, dist[i - _w - 1] + 4);
                        if (gx < _w - 1) best = Math.Min(best, dist[i - _w + 1] + 4);
                    }
                    dist[i] = best;
                }
            for (int gz = _h - 1; gz >= 0; gz--)
                for (int gx = _w - 1; gx >= 0; gx--)
                {
                    int i = gz * _w + gx;
                    if (dist[i] == 0) continue;
                    int best = dist[i];
                    if (gx < _w - 1) best = Math.Min(best, dist[i + 1] + 3);
                    if (gz < _h - 1)
                    {
                        best = Math.Min(best, dist[i + _w] + 3);
                        if (gx < _w - 1) best = Math.Min(best, dist[i + _w + 1] + 4);
                        if (gx > 0) best = Math.Min(best, dist[i + _w - 1] + 4);
                    }
                    dist[i] = best;
                }
            var clear = new float[n];
            for (int i = 0; i < n; i++) clear[i] = dist[i] * (_cell / 3f);

            // 4.5) Slope mask: cells on a sustained gradient (recast renders stairs as ramps).
            //      Close r2 absorbs small landings/speckle; open r1 drops isolated specks.
            var sloped = new bool[n];
            for (int i = 0; i < n; i++)
            {
                if (!walk[i]) continue;
                int gz = i / _w, gx = i % _w;
                float dy = 0f;
                if (gx > 0 && walk[i - 1]) dy = Math.Max(dy, Math.Abs(_cellY[i] - _cellY[i - 1]));
                if (gx < _w - 1 && walk[i + 1]) dy = Math.Max(dy, Math.Abs(_cellY[i] - _cellY[i + 1]));
                if (gz > 0 && walk[i - _w]) dy = Math.Max(dy, Math.Abs(_cellY[i] - _cellY[i - _w]));
                if (gz < _h - 1 && walk[i + _w]) dy = Math.Max(dy, Math.Abs(_cellY[i] - _cellY[i + _w]));
                sloped[i] = dy / _cell > SlopeT;
            }
            Morph(sloped, 2, dilate: true); Morph(sloped, 2, dilate: false);
            for (int i = 0; i < n; i++) sloped[i] &= walk[i];
            Morph(sloped, 1, dilate: false); Morph(sloped, 1, dilate: true);
            for (int i = 0; i < n; i++) sloped[i] &= walk[i];

            // 5) Persistence watershed: visit cells by descending clearance; basins meeting across a
            //    saddle merge unless BOTH rise at least Persist above it.
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            var keys = new float[n];
            for (int i = 0; i < n; i++) keys[i] = -clear[i];
            Array.Sort(keys, order);

            var parent = new int[n];
            var peak = new float[n];
            var seen = new bool[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            Func<int, int> find = null;
            find = a => { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; };

            int[] dz8 = { -1, 1, 0, 0, -1, -1, 1, 1 };
            int[] dx8 = { 0, 0, -1, 1, -1, 1, -1, 1 };
            for (int oi = 0; oi < n; oi++)
            {
                int i = order[oi];
                float c = clear[i];
                if (c < CutFloor) break;
                if (!walk[i]) continue;
                seen[i] = true;
                peak[i] = c;
                int gz = i / _w, gx = i % _w;
                int me = find(i);
                for (int k = 0; k < 8; k++)
                {
                    int nz = gz + dz8[k], nx = gx + dx8[k];
                    if (nz < 0 || nx < 0 || nz >= _h || nx >= _w) continue;
                    int j = nz * _w + nx;
                    if (!seen[j]) continue;
                    // never union across a height step or a flat/sloped class change — stacked
                    // levels stay apart, and staircases form their own basins
                    if (sloped[j] != sloped[i] || Math.Abs(_cellY[j] - _cellY[i]) > DyGate) continue;
                    int r = find(j);
                    if (r == me) continue;
                    // sloped basins always merge: one staircase = one region, however long
                    if (sloped[i] || Math.Min(peak[r], peak[me]) - c < Persist)
                    {
                        float pk = Math.Max(peak[r], peak[me]);
                        parent[me] = r;
                        me = r;
                        peak[r] = pk;
                    }
                }
            }

            // 6) Label basins; BFS-assign the sub-CutFloor walkable slivers to the nearest region.
            _label = new int[n];
            var regionOf = new Dictionary<int, int>();
            for (int i = 0; i < n; i++) _label[i] = -1;
            for (int i = 0; i < n; i++)
            {
                if (!seen[i]) continue;
                int r = find(i);
                int id;
                if (!regionOf.TryGetValue(r, out id)) { id = regionOf.Count; regionOf[r] = id; }
                _label[i] = id;
            }
            var q = new Queue<int>();
            for (int i = 0; i < n; i++) if (_label[i] >= 0) q.Enqueue(i);
            int[] dz4 = { -1, 1, 0, 0 };
            int[] dx4 = { 0, 0, -1, 1 };
            // 8-connected: tight rasterized offshoots (a T off an alcove) can touch the rest only
            // diagonally; 4-way flooding left them unlabeled, so they reported "no room".
            while (q.Count > 0)
            {
                int i = q.Dequeue();
                int gz = i / _w, gx = i % _w;
                for (int k = 0; k < 8; k++)
                {
                    int nz = gz + dz8[k], nx = gx + dx8[k];
                    if (nz < 0 || nx < 0 || nz >= _h || nx >= _w) continue;
                    int j = nz * _w + nx;
                    if (walk[j] && _label[j] < 0 && Math.Abs(_cellY[j] - _cellY[i]) <= DyGate)
                    { _label[j] = _label[i]; q.Enqueue(j); }
                }
            }

            // 7) Merge small regions into a neighbour. Stair regions get a smaller floor (a short
            //    flight is a real room); borders only count where the height is continuous; and a
            //    region prefers a same-class (stairs vs flat) neighbour. Isolated tinies drop (-1).
            int regions = regionOf.Count;
            var size = new int[regions];
            var slopedCells = new int[regions];
            var minY = new float[regions];
            var maxY = new float[regions];
            for (int r2 = 0; r2 < regions; r2++) { minY[r2] = float.MaxValue; maxY[r2] = float.MinValue; }
            for (int i = 0; i < n; i++)
                if (_label[i] >= 0)
                {
                    int l = _label[i];
                    size[l]++;
                    if (sloped[i]) slopedCells[l]++;
                    if (_cellY[i] < minY[l]) minY[l] = _cellY[i];
                    if (_cellY[i] > maxY[l]) maxY[l] = _cellY[i];
                }
            // Stairs = majority-sloped AND actually CLIMBING somewhere. Steep little lips and rubble
            // ramps (~0.6m rise, dead ends) read as part of their room, not phantom staircases.
            Func<int, bool> isStair = r2 => slopedCells[r2] * 2 > size[r2] && maxY[r2] - minY[r2] >= StairMinRise;
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int rid = 0; rid < regions; rid++)
                {
                    if (size[rid] == 0) continue;
                    float minArea = isStair(rid) ? MinStairArea : MinRoomArea;
                    if (size[rid] * _cell * _cell >= minArea) continue;
                    var border = new Dictionary<int, int>();
                    for (int i = 0; i < n; i++)
                    {
                        if (_label[i] != rid) continue;
                        int gz = i / _w, gx = i % _w;
                        for (int k = 0; k < 8; k++)
                        {
                            int nz = gz + dz8[k], nx = gx + dx8[k];
                            if (nz < 0 || nx < 0 || nz >= _h || nx >= _w) continue;
                            int j = nz * _w + nx;
                            int l = _label[j];
                            if (l >= 0 && l != rid && Math.Abs(_cellY[j] - _cellY[i]) <= DyGate)
                            { int cnt; border.TryGetValue(l, out cnt); border[l] = cnt + 1; }
                        }
                    }
                    int tgt = -1, btot = 0;
                    foreach (var kv in border)
                        if (isStair(kv.Key) == isStair(rid) && kv.Value > btot) { btot = kv.Value; tgt = kv.Key; }
                    if (tgt < 0)
                        foreach (var kv in border) if (kv.Value > btot) { btot = kv.Value; tgt = kv.Key; }
                    for (int i = 0; i < n; i++) if (_label[i] == rid) _label[i] = tgt; // -1 drops isolated tinies
                    if (tgt >= 0)
                    {
                        size[tgt] += size[rid];
                        slopedCells[tgt] += slopedCells[rid];
                        if (minY[rid] < minY[tgt]) minY[tgt] = minY[rid];
                        if (maxY[rid] > maxY[tgt]) maxY[tgt] = maxY[rid];
                    }
                    size[rid] = 0;
                    changed = true;
                }
            }

            // 8) Stable numbering (centroid sort) + classification.
            var stats = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                if (_label[i] < 0) continue;
                List<int> cells;
                if (!stats.TryGetValue(_label[i], out cells)) { cells = new List<int>(); stats[_label[i]] = cells; }
                cells.Add(i);
            }
            var infos = new List<KeyValuePair<int, Room>>();
            foreach (var kv in stats)
            {
                var cells = kv.Value;
                double sx = 0, sy = 0, sz = 0, sc = 0;
                foreach (var i in cells)
                {
                    sx += i % _w; sz += i / _w; sy += _cellY[i]; sc += clear[i];
                }
                int cnt = cells.Count;
                float mx = (float)(sx / cnt), mz = (float)(sz / cnt);
                // covariance for elongation
                double cxx = 0, czz = 0, cxz = 0;
                foreach (var i in cells)
                {
                    double ddx = i % _w - mx, ddz = i / _w - mz;
                    cxx += ddx * ddx; czz += ddz * ddz; cxz += ddx * ddz;
                }
                cxx /= cnt; czz /= cnt; cxz /= cnt;
                double tr = cxx + czz, det = cxx * czz - cxz * cxz;
                double disc = Math.Sqrt(Math.Max(0, tr * tr / 4 - det));
                double e1 = tr / 2 + disc, e2 = Math.Max(tr / 2 - disc, 1e-6);
                float elong = (float)Math.Sqrt(e1 / e2);
                float area = cnt * _cell * _cell;
                float meanClear = (float)(sc / cnt);
                string cls;
                if (isStair(kv.Key)) cls = "stairs";
                else if (elong > 2.6f && meanClear < 2.2f) cls = "passage";
                else if (elong > 3.2f) cls = "corridor";
                else if (area < 35f) cls = "small";
                else if (area > 220f) cls = "hall";
                else cls = "room";
                var room = new Room
                {
                    ClassKey = cls,
                    Area = area,
                    Centroid = new Vector3(_x0 + (mx - 1) * _cell, (float)(sy / cnt), _z0 + (mz - 1) * _cell),
                };
                infos.Add(new KeyValuePair<int, Room>(kv.Key, room));
            }
            infos.Sort((p1, p2) =>
            {
                int c1 = p1.Value.Centroid.z.CompareTo(p2.Value.Centroid.z);
                return c1 != 0 ? c1 : p1.Value.Centroid.x.CompareTo(p2.Value.Centroid.x);
            });
            var remap = new Dictionary<int, int>();
            for (int k = 0; k < infos.Count; k++)
            {
                infos[k].Value.Id = k + 1;
                remap[infos[k].Key] = k;
                _rooms.Add(infos[k].Value);
            }
            for (int i = 0; i < n; i++)
                _label[i] = _label[i] >= 0 && remap.ContainsKey(_label[i]) ? remap[_label[i]] : -1;

            BuildExits();
        }

        // 4-neighbourhood binary dilation/erosion passes (the slope mask's close-then-open).
        private static void Morph(bool[] m, int iters, bool dilate)
        {
            int n = _w * _h;
            var src = new bool[n];
            for (int it = 0; it < iters; it++)
            {
                Array.Copy(m, src, n);
                for (int i = 0; i < n; i++)
                {
                    int gz = i / _w, gx = i % _w;
                    if (dilate)
                        m[i] = src[i] || (gx > 0 && src[i - 1]) || (gx < _w - 1 && src[i + 1])
                            || (gz > 0 && src[i - _w]) || (gz < _h - 1 && src[i + _w]);
                    else
                        m[i] = src[i] && (gx == 0 || src[i - 1]) && (gx == _w - 1 || src[i + 1])
                            && (gz == 0 || src[i - _w]) && (gz == _h - 1 || src[i + _w]);
                }
            }
        }

        // Exits come from the REAL navmesh connectivity, not grid adjacency: every graph
        // connection between triangles that resolve to different rooms is a walkable portal. The
        // flattened grid can lie where levels stack (last-wins rasterization once cut a ramp off
        // its floor and ate the exit), and a cliff edge has no connection at all — so this is
        // exact in both directions. Portal points cluster per room pair (single-link, 2m);
        // each cluster = one Exit on both rooms.
        // The watershed boundary between every adjacent room pair: the world midpoints of cell edges
        // where one room's cells meet another's (height-continuous only — a cliff edge isn't a crossing).
        // This is the COMPLETE opening threshold, unlike navmesh portal edges (which only land where a
        // triangle edge happens to lie on the boundary). Keyed by the (min,max) room label pair.
        private static Dictionary<long, List<Vector3>> GridBoundaries()
        {
            var map = new Dictionary<long, List<Vector3>>();
            if (_label == null) return map;
            for (int z = 0; z < _h; z++)
                for (int x = 0; x < _w; x++)
                {
                    int i = z * _w + x;
                    int la = _label[i];
                    if (la < 0) continue;
                    if (x + 1 < _w) AddBoundaryCell(map, i, la, z * _w + (x + 1));   // +x neighbour
                    if (z + 1 < _h) AddBoundaryCell(map, i, la, (z + 1) * _w + x);   // +z neighbour
                }
            return map;
        }

        private static void AddBoundaryCell(Dictionary<long, List<Vector3>> map, int i, int la, int j)
        {
            int lb = _label[j];
            if (lb < 0 || lb == la) return;
            if (Math.Abs(_cellY[i] - _cellY[j]) > DyGate) return; // cliff edge over another floor: not a crossing
            long key = ((long)Math.Min(la, lb) << 32) | (uint)Math.Max(la, lb);
            List<Vector3> pts;
            if (!map.TryGetValue(key, out pts)) { pts = new List<Vector3>(); map[key] = pts; }
            int gxi = i % _w, gzi = i / _w, gxj = j % _w, gzj = j / _w;
            float cx = _x0 + ((gxi + gxj) * 0.5f - 0.5f) * _cell;
            float cz = _z0 + ((gzi + gzj) * 0.5f - 0.5f) * _cell;
            pts.Add(new Vector3(cx, (_cellY[i] + _cellY[j]) * 0.5f, cz));
        }

        private static void BuildExits()
        {
            var graphs = AstarPath.active?.data?.graphs;
            if (graphs == null) return;
            var roomOf = new Dictionary<GraphNode, Room>();
            Func<TriangleMeshNode, Room> roomFor = t =>
            {
                Room r;
                if (roomOf.TryGetValue(t, out r)) return r;
                var c = ((Vector3)t.GetVertex(0) + (Vector3)t.GetVertex(1) + (Vector3)t.GetVertex(2)) / 3f;
                r = RoomAt(c);
                roomOf[t] = r;
                return r;
            };
            var portals = new Dictionary<long, List<PortalEdge>>();
            foreach (var g in graphs)
            {
                if (!(g is NavmeshBase)) continue;
                g.GetNodes(node =>
                {
                    var t = node as TriangleMeshNode;
                    if (t == null || t.connections == null) return;
                    var ra = roomFor(t);
                    if (ra == null) return;
                    foreach (var conn in t.connections)
                    {
                        var o = conn.node as TriangleMeshNode;
                        if (o == null) continue;
                        var rb = roomFor(o);
                        if (rb == null || rb == ra) continue;
                        PortalEdge portal;
                        if (conn.shapeEdge != byte.MaxValue)
                        {
                            // the triangle edge this connection crosses (endpoints kept for clustering)
                            portal.A = (Vector3)t.GetVertex(conn.shapeEdge);
                            portal.B = (Vector3)t.GetVertex((conn.shapeEdge + 1) % 3);
                            portal.Mid = (portal.A + portal.B) * 0.5f;
                        }
                        else
                        {
                            var cb = ((Vector3)o.GetVertex(0) + (Vector3)o.GetVertex(1) + (Vector3)o.GetVertex(2)) / 3f;
                            var ca = ((Vector3)t.GetVertex(0) + (Vector3)t.GetVertex(1) + (Vector3)t.GetVertex(2)) / 3f;
                            portal.A = portal.B = portal.Mid = (ca + cb) * 0.5f; // off-mesh link: no shared edge
                        }
                        long key = ((long)Math.Min(ra.Id, rb.Id) << 32) | (uint)Math.Max(ra.Id, rb.Id);
                        List<PortalEdge> pts;
                        if (!portals.TryGetValue(key, out pts)) { pts = new List<PortalEdge>(); portals[key] = pts; }
                        pts.Add(portal);
                    }
                });
            }
            const float VertEps2 = 0.05f * 0.05f; // shared navmesh vertices are exact; small tolerance for float
            var gridBounds = GridBoundaries(); // the watershed boundary cells, keyed by room-label pair
            foreach (var kv in portals)
            {
                var a = _rooms[(int)(kv.Key >> 32) - 1];
                var b = _rooms[(int)(kv.Key & 0xFFFFFFFF) - 1];
                int la = (int)(kv.Key >> 32) - 1, lb = (int)(kv.Key & 0xFFFFFFFF) - 1; // grid labels
                long bkey = ((long)Math.Min(la, lb) << 32) | (uint)Math.Max(la, lb);
                List<Vector3> boundaryCells;
                gridBounds.TryGetValue(bkey, out boundaryCells);
                var pts = kv.Value;
                // Cluster by boundary connectivity: portals join if their edges share an endpoint (one
                // contiguous opening, however wide/curved — large+small triangles still share vertices),
                // with a 2m midpoint fallback for off-mesh links and minor gaps. Separate doorways, whose
                // edges share no vertex and sit apart, stay separate exits.
                var root = new int[pts.Count];
                for (int i = 0; i < root.Length; i++) root[i] = i;
                Func<int, int> find = null;
                find = x => { while (root[x] != x) { root[x] = root[root[x]]; x = root[x]; } return x; };
                Func<PortalEdge, PortalEdge, bool> shareVertex = (p, q) =>
                    (p.A - q.A).sqrMagnitude <= VertEps2 || (p.A - q.B).sqrMagnitude <= VertEps2
                    || (p.B - q.A).sqrMagnitude <= VertEps2 || (p.B - q.B).sqrMagnitude <= VertEps2;
                for (int i = 0; i < pts.Count; i++)
                    for (int j = i + 1; j < pts.Count; j++)
                        if (shareVertex(pts[i], pts[j]) || (pts[i].Mid - pts[j].Mid).sqrMagnitude <= 2f * 2f)
                        {
                            int ri = find(i), rj = find(j);
                            if (ri != rj) root[ri] = rj;
                        }
                var groups = new Dictionary<int, List<PortalEdge>>();
                for (int i = 0; i < pts.Count; i++)
                {
                    int r = find(i);
                    List<PortalEdge> g;
                    if (!groups.TryGetValue(r, out g)) { g = new List<PortalEdge>(); groups[r] = g; }
                    g.Add(pts[i]);
                }
                foreach (var g in groups.Values)
                {
                    Vector3 sum = Vector3.zero;
                    foreach (var e in g) sum += e.Mid;
                    var pos = sum / g.Count;
                    // The centroid of a curved portal chain can drift off the mesh; snap it back to the
                    // walkable surface — the cursor is planted RAW on this point (Home/Slash).
                    var snap = NavmeshProbe.Sample(pos.x, pos.z, pos.y);
                    if (snap.Point != Vector3.zero) pos = snap.Point;
                    // Navmesh portal edges — fallback geometry (only catches where a triangle edge lies
                    // on the boundary; used when the grid boundary missed this opening, e.g. a ramp).
                    var edges = new Vector3[g.Count * 2];
                    for (int i = 0; i < g.Count; i++) { edges[i * 2] = g[i].A; edges[i * 2 + 1] = g[i].B; }
                    // Preferred geometry: the watershed boundary cells of THIS opening — the complete
                    // threshold (including where it cuts mid-triangle), scoped to this cluster's centroid.
                    Vector3[] boundary = null;
                    if (boundaryCells != null)
                    {
                        var near = new List<Vector3>();
                        foreach (var c in boundaryCells)
                        {
                            float dx = c.x - pos.x, dz = c.z - pos.z;
                            if (dx * dx + dz * dz <= 6f * 6f) near.Add(c);
                        }
                        if (near.Count > 0) boundary = near.ToArray();
                    }
                    a.Exits.Add(new Exit { Position = pos, Edges = edges, Boundary = boundary, To = b });
                    b.Exits.Add(new Exit { Position = pos, Edges = edges, Boundary = boundary, To = a });
                }
            }
        }
    }
}
