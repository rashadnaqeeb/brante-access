using System.Collections.Generic;
using UnityEngine; // Vector3

namespace WrathAccess.Exploration.Announce
{
    /// <summary>The thing's name (already localized / game content — passed through).</summary>
    internal sealed class NamePart : ScanAnnouncement
    {
        private readonly string _name;
        public NamePart(string name) { _name = name; }
        public override string Key => "name";
        public override Message Render(ScanAnnounceContext ctx) => Message.Raw(_name);
    }

    /// <summary>The thing's type/role word (unit faction, object category) — localized by the proxy.</summary>
    internal sealed class TypePart : ScanAnnouncement
    {
        private readonly string _type;
        public TypePart(string type) { _type = type; }
        public override string Key => "type";
        public override Message Render(ScanAnnounceContext ctx) => Message.Raw(_type);
    }

    /// <summary>A unit's current health ("HP x of y").</summary>
    internal sealed class HpPart : ScanAnnouncement
    {
        private readonly int _hp, _max;
        public HpPart(int hp, int max) { _hp = hp; _max = max; }
        public override string Key => "hp";
        public override Message Render(ScanAnnounceContext ctx)
            => Message.Localized("ui", "unit.hp", new { hp = _hp, max = _max });
    }

    /// <summary>A unit's current action ("Casting Fireball", "Attacking", "Moving") — composed and
    /// localized by the proxy from its live command; empty when idle, so it self-skips.</summary>
    internal sealed class ActionPart : ScanAnnouncement
    {
        private readonly string _text;
        public ActionPart(string text) { _text = text; }
        public override string Key => "action";
        public override Message Render(ScanAnnounceContext ctx)
            => string.IsNullOrEmpty(_text) ? Message.Empty : Message.Raw(_text);
    }

    /// <summary>A unit's condition: dead / unconscious / in combat (the proxy picks the key).</summary>
    internal sealed class ConditionPart : ScanAnnouncement
    {
        private readonly string _uiKey; // "unit.dead" / "unit.unconscious" / "unit.in_combat"
        public ConditionPart(string uiKey) { _uiKey = uiKey; }
        public override string Key => "condition";
        public override Message Render(ScanAnnounceContext ctx) => Message.Localized("ui", _uiKey);
    }

    /// <summary>An object's state flags (open / restricted / trapped), pre-localized and comma-joined.</summary>
    internal sealed class ObjectStatePart : ScanAnnouncement
    {
        private readonly IList<string> _states;
        public ObjectStatePart(IList<string> states) { _states = states; }
        public override string Key => "object_state";
        public override Message Render(ScanAnnounceContext ctx)
            => _states != null && _states.Count > 0 ? Message.Raw(string.Join(", ", _states)) : Message.Empty;
    }

    /// <summary>
    /// Where the thing is, relative to the listener — a single announcement with configurable sub-parts
    /// (direction / distance / height, in that order; an optional debug coordinate readout). Measures to
    /// the nearest part of the bounds (<paramref name="measureTo"/>); coordinates report the centre
    /// (<paramref name="posTo"/>), matching the old <c>Geo.Relative</c> behaviour.
    /// </summary>
    internal sealed class SpatialPart : ScanAnnouncement
    {
        private readonly Vector3 _from, _measureTo, _posTo;
        public SpatialPart(Vector3 from, Vector3 measureTo, Vector3 posTo)
        { _from = from; _measureTo = measureTo; _posTo = posTo; }

        public override string Key => "spatial";

        public override Message Render(ScanAnnounceContext ctx)
        {
            bool dir = ctx.ResolveBool("spatial", "direction", true);
            bool dist = ctx.ResolveBool("spatial", "distance", true);
            bool height = ctx.ResolveBool("spatial", "height", true);
            bool coords = ctx.ResolveBool("spatial", "coordinates", false);

            var bits = new List<string>(4);
            if (Geo.IsHere(_from, _measureTo))
            {
                if (dir || dist) bits.Add(Loc.T("geo.here"));
            }
            else
            {
                if (dir) bits.Add(Geo.Bearing(_from, _measureTo));
                if (dist) bits.Add(Geo.FeetStr(Geo.Distance(_from, _measureTo)));
                if (height) { var v = Geo.Vertical(_from, _measureTo); if (v != null) bits.Add(v); }
            }
            if (coords) bits.Add(Geo.Raw(_posTo)); // debug aid, off by default

            return bits.Count > 0 ? Message.Raw(string.Join(", ", bits)) : Message.Empty;
        }
    }
}
