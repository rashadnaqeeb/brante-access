using System.Collections.Generic;
using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// A scan item's spatial extent on the XZ plane. <see cref="Center"/> is the thing's middle — where a
    /// cursor snaps. <see cref="NearestPoint"/> is the closest part of the thing to a reference point,
    /// used for the spoken distance/bearing: standing due south of a wide doorway should read "south", not
    /// "west" to its centre. Shapes: a point (default), a circle (a creature/object footprint), a polyline
    /// (a connected chain), and disjoint segments (a doorway's actual portal edges). More can be added
    /// without touching call sites.
    /// </summary>
    internal abstract class ScanBounds
    {
        /// <summary>The thing's middle (cursor target).</summary>
        public abstract Vector3 Center { get; }

        /// <summary>The closest point of the thing to <paramref name="from"/> (XZ); Y carried from the
        /// bounds so above/below still reads. Inside the bounds → returns the reference (distance 0, "here").</summary>
        public abstract Vector3 NearestPoint(Vector3 from);

        public static ScanBounds Point(Vector3 p) => new PointBounds(p);
        public static ScanBounds Circle(Vector3 c, float radius) => new CircleBounds(c, radius);
        /// <summary>A FILLED convex polygon (XZ) — e.g. a Wall area effect's rotated rectangle. Inside →
        /// distance 0 (like a circle); outside → nearest point on the perimeter. Corners in order.</summary>
        public static ScanBounds Rect(Vector3 center, IList<Vector3> corners) => new RectBounds(center, corners);
        /// <summary>A connected chain of ≥1 points; the closest point lies on its consecutive segments.</summary>
        public static ScanBounds Polyline(Vector3 center, IList<Vector3> points) => new PolylineBounds(center, points);

        /// <summary>A closed polygon footprint (a trap's polygonal trigger zone): inside means you're in
        /// it (distance 0); outside reports the nearest point on the outline. Handles concave shapes.</summary>
        public static ScanBounds Polygon(Vector3 center, IList<Vector3> points) => new PolygonBounds(center, points);
        /// <summary>DISJOINT segments as flat endpoint pairs [a0,b0,a1,b1,…] — e.g. a doorway's actual
        /// portal edges (full opening extent, not a chord). <paramref name="center"/> is the cursor target.</summary>
        public static ScanBounds Segments(Vector3 center, IList<Vector3> edgePairs) => new SegmentsBounds(center, edgePairs);
        /// <summary>A dense point cloud — e.g. an opening's watershed-boundary cell midpoints; the nearest
        /// point is the closest of them (accurate to the cell spacing). <paramref name="center"/> = cursor target.</summary>
        public static ScanBounds Cloud(Vector3 center, IList<Vector3> points) => new CloudBounds(center, points);

        // Closest point on segment a→b to `from`, on the XZ plane (Y lerped along the segment).
        protected static Vector3 ClosestOnSegment(Vector3 from, Vector3 a, Vector3 b)
        {
            float abx = b.x - a.x, abz = b.z - a.z;
            float len2 = abx * abx + abz * abz;
            if (len2 < 1e-6f) return a;
            float t = Mathf.Clamp01(((from.x - a.x) * abx + (from.z - a.z) * abz) / len2);
            return new Vector3(a.x + abx * t, Mathf.Lerp(a.y, b.y, t), a.z + abz * t);
        }

        // --- non-allocating geometry, shared by these bounds (the per-announce spoken path) AND
        // ScanItem.NearestPoint (the per-frame lenses), so a shape's "closest point" math has one source ---

        /// <summary>Closest point on a circle of radius <paramref name="r"/> about <paramref name="center"/>
        /// to <paramref name="from"/> (XZ); inside (or r≤0) → the reference point (distance 0).</summary>
        public static Vector3 NearestOnCircleXZ(Vector3 center, float r, Vector3 from)
        {
            r = Mathf.Max(0f, r);
            float dx = from.x - center.x, dz = from.z - center.z;
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            if (d <= r || d < 1e-4f) return new Vector3(from.x, center.y, from.z);
            float t = r / d;
            return new Vector3(center.x + dx * t, center.y, center.z + dz * t);
        }

        /// <summary>Closest point of a FILLED convex quad (XZ) to <paramref name="from"/>: inside → the
        /// reference point (distance 0); outside → the nearest perimeter edge. Corners in order.</summary>
        public static Vector3 NearestInQuadXZ(Vector3 from, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            if (InConvexXZ(from, a, b, c, d)) return new Vector3(from.x, a.y, from.z);
            Vector3 best = a; float bestD = float.MaxValue;
            Consider(from, a, b, ref best, ref bestD);
            Consider(from, b, c, ref best, ref bestD);
            Consider(from, c, d, ref best, ref bestD);
            Consider(from, d, a, ref best, ref bestD);
            return best;
        }

        private static void Consider(Vector3 from, Vector3 a, Vector3 b, ref Vector3 best, ref float bestD)
        {
            var p = ClosestOnSegment(from, a, b);
            float dx = from.x - p.x, dz = from.z - p.z;
            float d = dx * dx + dz * dz;
            if (d < bestD) { bestD = d; best = p; }
        }

        /// <summary>Nearest point of a CLOSED polygon (XZ): inside (even-odd crossing test, concave OK)
        /// means the query point itself; outside means the closest point on the outline. Non-allocating.</summary>
        public static Vector3 NearestInPolygonXZ(Vector3 from, IList<Vector3> pts)
        {
            int n = pts.Count;
            if (n == 0) return from;
            if (n < 3) return ClosestOnSegment(from, pts[0], pts[n - 1]);

            // Even-odd ray crossing (handles concave outlines).
            bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var pi = pts[i]; var pj = pts[j];
                if ((pi.z > from.z) != (pj.z > from.z)
                    && from.x < (pj.x - pi.x) * (from.z - pi.z) / (pj.z - pi.z) + pi.x)
                    inside = !inside;
            }
            if (inside) return new Vector3(from.x, pts[0].y, from.z);

            Vector3 best = pts[0]; float bestD = float.MaxValue;
            for (int i = 0, j = n - 1; i < n; j = i++)
                Consider(from, pts[j], pts[i], ref best, ref bestD);
            return best;
        }

        private static bool InConvexXZ(Vector3 p, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            bool pos = false, neg = false;
            Side(p, a, b, ref pos, ref neg); if (pos && neg) return false;
            Side(p, b, c, ref pos, ref neg); if (pos && neg) return false;
            Side(p, c, d, ref pos, ref neg); if (pos && neg) return false;
            Side(p, d, a, ref pos, ref neg); return !(pos && neg);
        }

        private static void Side(Vector3 p, Vector3 a, Vector3 b, ref bool pos, ref bool neg)
        {
            float cross = (b.x - a.x) * (p.z - a.z) - (b.z - a.z) * (p.x - a.x);
            if (cross > 1e-5f) pos = true; else if (cross < -1e-5f) neg = true;
        }

        private sealed class PointBounds : ScanBounds
        {
            private readonly Vector3 _p;
            public PointBounds(Vector3 p) { _p = p; }
            public override Vector3 Center => _p;
            public override Vector3 NearestPoint(Vector3 from) => _p;
        }

        private sealed class CircleBounds : ScanBounds
        {
            private readonly Vector3 _c;
            private readonly float _r;
            public CircleBounds(Vector3 c, float r) { _c = c; _r = Mathf.Max(0f, r); }
            public override Vector3 Center => _c;
            public override Vector3 NearestPoint(Vector3 from) => NearestOnCircleXZ(_c, _r, from);
        }

        // A filled convex polygon on XZ (the Wall area effect's rotated rectangle). Inside → the reference
        // point itself (distance 0, "you're in it"), matching how a circle reads; outside → nearest edge.
        private sealed class RectBounds : ScanBounds
        {
            private readonly Vector3 _center;
            private readonly Vector3[] _c; // ordered corners (XZ footprint)
            public RectBounds(Vector3 center, IList<Vector3> corners)
            {
                _center = center;
                _c = corners != null && corners.Count >= 3 ? new Vector3[corners.Count] : null;
                for (int i = 0; _c != null && i < corners.Count; i++) _c[i] = corners[i];
            }
            public override Vector3 Center => _center;
            // Area-effect walls are always 4-corner rectangles → the shared non-allocating quad helper
            // (one source with the per-frame path); a degenerate corner set falls back to the centre.
            public override Vector3 NearestPoint(Vector3 from)
                => _c != null && _c.Length == 4 ? NearestInQuadXZ(from, _c[0], _c[1], _c[2], _c[3]) : _center;
        }

        private sealed class PolygonBounds : ScanBounds
        {
            private readonly Vector3 _center;
            private readonly Vector3[] _pts; // ordered outline (closed: last wraps to first)
            public PolygonBounds(Vector3 center, IList<Vector3> points)
            {
                _center = center;
                _pts = points != null && points.Count >= 3 ? new Vector3[points.Count] : null;
                for (int i = 0; _pts != null && i < points.Count; i++) _pts[i] = points[i];
            }
            public override Vector3 Center => _center;
            public override Vector3 NearestPoint(Vector3 from)
                => _pts != null ? NearestInPolygonXZ(from, _pts) : _center;
        }

        private sealed class PolylineBounds : ScanBounds
        {
            private readonly Vector3 _center;
            private readonly Vector3[] _pts;
            public PolylineBounds(Vector3 center, IList<Vector3> points)
            {
                _center = center;
                _pts = points != null && points.Count > 0 ? new Vector3[points.Count] : new[] { center };
                for (int i = 0; points != null && i < points.Count; i++) _pts[i] = points[i];
            }
            public override Vector3 Center => _center;
            public override Vector3 NearestPoint(Vector3 from)
            {
                if (_pts.Length == 1) return _pts[0];
                Vector3 best = _pts[0];
                float bestD = float.MaxValue;
                for (int i = 0; i + 1 < _pts.Length; i++)
                {
                    var p = ClosestOnSegment(from, _pts[i], _pts[i + 1]);
                    float dx = from.x - p.x, dy = from.y - p.y, dz = from.z - p.z;
                    float d = dx * dx + dy * dy + dz * dz; // 3D so a vertically-distant chain doesn't win
                    if (d < bestD) { bestD = d; best = p; }
                }
                return best;
            }
        }

        // The closest of a dense point cloud (the opening's watershed-boundary midpoints). 3D so a
        // boundary stretch up on a ledge doesn't win over the threshold at your level.
        private sealed class CloudBounds : ScanBounds
        {
            private readonly Vector3 _center;
            private readonly Vector3[] _pts;
            public CloudBounds(Vector3 center, IList<Vector3> pts)
            {
                _center = center;
                _pts = pts != null && pts.Count > 0 ? new Vector3[pts.Count] : null;
                for (int i = 0; _pts != null && i < pts.Count; i++) _pts[i] = pts[i];
            }
            public override Vector3 Center => _center;
            public override Vector3 NearestPoint(Vector3 from)
            {
                if (_pts == null) return _center;
                Vector3 best = _pts[0];
                float bestD = float.MaxValue;
                for (int i = 0; i < _pts.Length; i++)
                {
                    float dx = from.x - _pts[i].x, dy = from.y - _pts[i].y, dz = from.z - _pts[i].z;
                    float d = dx * dx + dy * dy + dz * dz;
                    if (d < bestD) { bestD = d; best = _pts[i]; }
                }
                return best;
            }
        }

        // The closest point over a set of independent segments (each portal edge), so the full opening
        // extent is covered — not just a chord between two midpoints.
        private sealed class SegmentsBounds : ScanBounds
        {
            private readonly Vector3 _center;
            private readonly Vector3[] _pts; // flat pairs: [a0,b0,a1,b1,…]
            public SegmentsBounds(Vector3 center, IList<Vector3> pts)
            {
                _center = center;
                _pts = pts != null && pts.Count >= 2 ? new Vector3[pts.Count] : null;
                for (int i = 0; _pts != null && i < pts.Count; i++) _pts[i] = pts[i];
            }
            public override Vector3 Center => _center;
            public override Vector3 NearestPoint(Vector3 from)
            {
                if (_pts == null) return _center;
                Vector3 best = _center;
                float bestD = float.MaxValue;
                for (int i = 0; i + 1 < _pts.Length; i += 2)
                {
                    var p = ClosestOnSegment(from, _pts[i], _pts[i + 1]);
                    float dx = from.x - p.x, dy = from.y - p.y, dz = from.z - p.z;
                    float d = dx * dx + dy * dy + dz * dz; // 3D: an opening edge up on a ledge shouldn't win
                    if (d < bestD) { bestD = d; best = p; }
                }
                return best;
            }
        }
    }
}
