using Kingmaker.EntitySystem; // EntityDataBase
using Owlcat.Runtime.Core.Utils; // GeometryUtils
using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// Spatial readout helpers for the in-area scene. Distance uses the game's own rules metric
    /// (<see cref="GeometryUtils.MechanicsDistance"/> — horizontal, but counts large vertical gaps at
    /// half, so flying/upper-level things read correctly); bearing is the flat XZ compass (0°=N=+Z,
    /// 90°=E=+X). See the exploration-world-model memory. The raw position vector is appended for now
    /// (testing aid — likely trimmed later).
    /// </summary>
    internal static class Geo
    {
        /// <summary>
        /// An entity's LIVE world position — the view transform, which the movement agent writes every
        /// frame. NOT <c>entity.Position</c>: <see cref="UnitEntityData"/> overrides that with its data/
        /// logical position (<c>m_Position</c>), which lags the transform during/after a move (the game's
        /// own UnitMoveController treats <c>View.Transform.position</c> and <c>unit.Position</c> as
        /// distinct). Falls back to <c>entity.Position</c> only when there's no view.
        /// </summary>
        public static Vector3 Live(EntityDataBase e)
        {
            if (e == null) return Vector3.zero;
            var view = e.View;
            return view != null ? view.transform.position : e.Position;
        }

        public static float Distance(Vector3 from, Vector3 to) => GeometryUtils.MechanicsDistance(from, to);

        // World space is metres; the game measures everything the player knows (speed, reach, spell
        // ranges) in feet, at the real ratio 0.3048 m/ft (Kingmaker.Utility.Feet). So convert for any
        // spoken distance — raw metres would read as ~1/3 of the feet the player expects. The game floors
        // to whole feet; we round to the nearest foot to stay close to what's on screen.
        public const float MetresPerFoot = 0.3048f;
        public static float Feet(float metres) => metres / MetresPerFoot;
        public static string FeetStr(float metres) => Loc.T("geo.feet", new { feet = Mathf.RoundToInt(Feet(metres)) });

        // The GLOBAL MAP measures distance in MILES, where 1 world unit == 1 mile (see
        // GlobalMapMovementController.MilesTravelled), so its raw XZ distance is already the mileage — NO
        // metres/feet conversion (unlike the in-area scene above). For world-map readouts only.
        public static string MilesStr(float units) => Loc.T("geo.miles", new { miles = Mathf.RoundToInt(units) });

        private static readonly string[] Compass =
            { "north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest" };

        /// <summary>True when the two points coincide on the XZ plane (the "here" case).</summary>
        public static bool IsHere(Vector3 from, Vector3 to)
            => Mathf.Abs(to.x - from.x) < 0.05f && Mathf.Abs(to.z - from.z) < 0.05f;

        public static string Bearing(Vector3 from, Vector3 to)
        {
            float dx = to.x - from.x, dz = to.z - from.z;
            if (IsHere(from, to)) return Loc.T("geo.here");
            float deg = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg; // 0 = +Z (north), 90 = +X (east)
            if (deg < 0f) deg += 360f;
            return Loc.T("geo." + Compass[Mathf.RoundToInt(deg / 45f) % 8]);
        }

        /// <summary>"above"/"below" only past the game's own 1.5 height threshold; else null.</summary>
        public static string Vertical(Vector3 from, Vector3 to)
        {
            float dy = to.y - from.y;
            if (dy > 1.5f) return Loc.T("geo.above");
            if (dy < -1.5f) return Loc.T("geo.below");
            return null;
        }

        public static string Raw(Vector3 v) => Loc.T("geo.pos", new { x = v.x.ToString("0.0"), y = v.y.ToString("0.0"), z = v.z.ToString("0.0") });

        /// <summary>"&lt;bearing&gt;, &lt;dist&gt; ft[, above/below], pos x y z" relative to a reference point.</summary>
        public static string Relative(Vector3 from, Vector3 to) => Relative(from, to, to);

        /// <summary>As <see cref="Relative(Vector3,Vector3)"/>, but bearing/distance/verticality measure to
        /// <paramref name="measureTo"/> (the nearest part of a sized thing) while the reported "pos x y z"
        /// stays <paramref name="posTo"/> (its centre — where the cursor snaps).</summary>
        public static string Relative(Vector3 from, Vector3 measureTo, Vector3 posTo)
        {
            if (IsHere(from, measureTo)) return Loc.T("geo.here") + ", " + Raw(posTo);
            var s = Bearing(from, measureTo) + ", " + FeetStr(Distance(from, measureTo));
            var vert = Vertical(from, measureTo);
            if (vert != null) s += ", " + vert;
            return s + ", " + Raw(posTo);
        }
    }
}
