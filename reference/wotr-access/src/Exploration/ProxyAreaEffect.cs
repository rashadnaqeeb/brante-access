using System.Collections.Generic;
using Kingmaker.EntitySystem.Entities; // AreaEffectEntityData
using Kingmaker.UnitLogic.Abilities.Components.AreaEffects; // AbilityAreaEffectBuff
using Kingmaker.View.MapObjects.SriptZones; // ScriptZoneCylinder, ScriptZoneBox (the runtime shapes)
using UnityEngine; // Vector3, Mathf

namespace WrathAccess.Exploration
{
    /// <summary>
    /// A live area effect — a spell AoE (stinking cloud, web, bless…) or a placed environmental effect —
    /// surfaced from <c>Game.Instance.State.AreaEffects</c>. Classified ONCE at creation (the blueprint and
    /// source ability are fixed for the effect's life) into a hazard or a buff zone, and for hazards split
    /// spell vs terrain by whether a spell cast it:
    /// - applies a HARMFUL buff (<see cref="AbilityAreaEffectBuff"/> → <c>Buff.Harmful</c>) → hazard;
    /// - applies a beneficial buff → buff zone;
    /// - no buff (a damage/movement field) → hazard unless it only targets allies.
    /// The effect is a ZONE, so <see cref="Footprint"/> is the blueprint radius and distance/bearing report
    /// the nearest EDGE — the bit that tells the player they're about to step into it.
    /// </summary>
    internal sealed class ProxyAreaEffect : ProxyEntity
    {
        private readonly AreaEffectEntityData _ae;
        private readonly string _node; // cached classification

        public ProxyAreaEffect(AreaEffectEntityData ae) : base(ae) { _ae = ae; _node = Classify(ae); }

        // The casting spell's name (Stinking Cloud) when there is one; otherwise the generic kind word so a
        // nameless terrain hazard still announces as something.
        public override string Name
        {
            get
            {
                var spell = _ae.Context?.SourceAbility?.Name;
                return string.IsNullOrEmpty(spell) ? TypeWord : spell;
            }
        }

        // Dynamic like a unit (not reveal-latched): show it while it's active, in game, and not fogged.
        public override bool IsVisible => _ae.IsInGame && !_ae.IsEnded && !_ae.IsInFogOfWar;

        // The thing's actual footprint, from the live runtime shape. The game's persistent area effects are
        // Cylinder (a circle) or Wall (a ScriptZoneBox — a rotated rectangle, NOT a circle); AllArea has no
        // footprint. We use the real geometry so the spoken distance/bearing report the nearest EDGE of the
        // zone (the bit you're about to step into), not a circle approximation of a wall.
        // Spoken path (per-announce): the real shape as a ScanBounds. Cylinder → circle; Wall → its rotated
        // rectangle; AllArea / not-yet-attached → the base circle(Footprint).
        public override ScanBounds Bounds
        {
            get
            {
                var shape = _ae.View != null ? _ae.View.Shape : null;
                if (shape is ScriptZoneCylinder cyl) return ScanBounds.Circle(Position, cyl.Radius);
                if (TryCorners(out var p0, out var p1, out var p2, out var p3))
                    return ScanBounds.Rect(Position, new[] { p0, p1, p2, p3 });
                return base.Bounds;
            }
        }

        // Per-frame path (sonar / cues / grid / cursor): the same geometry, NON-ALLOCATING — via the shared
        // ScanBounds statics, so the sound/cue track the actual zone (a wall reads along its length).
        public override Vector3 NearestPoint(Vector3 from)
        {
            var shape = _ae.View != null ? _ae.View.Shape : null;
            if (shape is ScriptZoneCylinder cyl) return ScanBounds.NearestOnCircleXZ(Position, cyl.Radius, from);
            if (TryCorners(out var p0, out var p1, out var p2, out var p3))
                return ScanBounds.NearestInQuadXZ(from, p0, p1, p2, p3);
            return base.NearestPoint(from);
        }

        // The wall's four world-space footprint corners (the rotated rectangle), or false for any other shape.
        private bool TryCorners(out Vector3 p0, out Vector3 p1, out Vector3 p2, out Vector3 p3)
        {
            p0 = p1 = p2 = p3 = default;
            var view = _ae.View;
            if (view == null || !(view.Shape is ScriptZoneBox box)) return false;
            var t = view.transform;
            var b = box.Bounds; // local; the wall is Size long × 5 ft deep
            Vector3 c = b.center, e = b.extents;
            p0 = t.TransformPoint(c + new Vector3(-e.x, 0f, -e.z));
            p1 = t.TransformPoint(c + new Vector3( e.x, 0f, -e.z));
            p2 = t.TransformPoint(c + new Vector3( e.x, 0f,  e.z));
            p3 = t.TransformPoint(c + new Vector3(-e.x, 0f,  e.z));
            return true;
        }

        // Scalar footprint for the systems that still approximate as a circle (sonar / cues / tile grid):
        // the true radius for a cylinder; half the longer side for a wall (a ballpark extent — those systems
        // are circle-based, so a wall is only ever approximate there; the scanner above uses the real shape).
        public override float Footprint
        {
            get
            {
                var shape = _ae.View != null ? _ae.View.Shape : null;
                if (shape is ScriptZoneCylinder cyl) return cyl.Radius;
                if (shape is ScriptZoneBox box) { var e = box.Bounds.extents; return Mathf.Max(e.x, e.z); }
                return _ae.Blueprint != null ? _ae.Blueprint.Size.Meters : 0f; // fallback (unattached / AllArea)
            }
        }

        public override IEnumerable<string> Nodes { get { yield return _node; } }
        public override string Primary => _node;

        private string TypeWord => _node == ScanTaxonomy.BuffZones
            ? Loc.T("areaeffect.buffzone") : Loc.T("areaeffect.hazard");

        private static string Classify(AreaEffectEntityData ae)
        {
            if (!IsHazard(ae)) return ScanTaxonomy.BuffZones;
            // Spell-cast effects have a source ability; placed environmental ones don't.
            return ae.Context?.SourceAbility != null ? ScanTaxonomy.HazardsSpell : ScanTaxonomy.HazardsTerrain;
        }

        private static bool IsHazard(AreaEffectEntityData ae)
        {
            var bp = ae.Blueprint;
            if (bp == null) return true;
            // A harmful applied buff = hazard; a beneficial one = buff zone (the precise signal).
            foreach (var c in bp.ComponentsArray)
                if (c is AbilityAreaEffectBuff b && b.Buff != null)
                    return b.Buff.Harmful;
            // No buff component (a damage or movement field): ally-only is beneficial, enemy/any is a hazard.
            return bp.CanTargetEnemies || !bp.CanTargetAllies;
        }
    }
}
