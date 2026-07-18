using WrathAccess.Settings; // ModSettings, BoolSetting, NullableBoolSetting

namespace WrathAccess.Exploration.Announce
{
    /// <summary>
    /// One addressable piece of a scan item's spoken line (name, type, hp, condition, object state,
    /// spatial). The proxy parallel to the UI <c>Announcement</c> family — deliberately NOT the same
    /// pipeline (scan items aren't focusable UI elements), but the same shape: each part has a stable
    /// <see cref="Key"/> used for its enable/sub-toggle settings, carries its own data, and renders to a
    /// <see cref="Message"/> (empty ⇒ self-skips). Composed by <see cref="ScanAnnounceComposer"/>.
    /// </summary>
    internal abstract class ScanAnnouncement
    {
        /// <summary>Settings key (e.g. "name", "spatial") — matches the registry's part keys.</summary>
        public abstract string Key { get; }

        /// <summary>Rendered text. <see cref="ScanAnnounceContext"/> resolves per-part sub-toggles.</summary>
        public abstract Message Render(ScanAnnounceContext ctx);
    }

    /// <summary>
    /// Resolves a scan announcement's setting with the per-entity-type inherit chain: the announce-node's
    /// own override, then its parent category's, then the global default — most specific wins. Overrides
    /// live at <c>proxy_elem.{nodeKey}.{part}.{setting}</c> (tri-state NullableBool: inherit/on/off);
    /// globals at <c>proxy_announce.{part}.{setting}</c>. A null/unknown node ⇒ globals only.
    /// </summary>
    internal sealed class ScanAnnounceContext
    {
        private readonly string _node;
        public ScanAnnounceContext(string nodeKey) { _node = nodeKey; }

        public bool ResolveBool(string part, string setting, bool def)
        {
            // Only the whole-part "enabled" is overridable per node; walk node → category, first explicit
            // override wins. Other settings (the spatial sub-toggles) are global-only.
            if (setting == "enabled")
                for (var n = ScanTaxonomy.Get(_node); n != null; n = n.Parent)
                {
                    var ov = ModSettings.GetSetting<NullableBoolSetting>("proxy_elem." + n.Key + "." + part);
                    if (ov != null && ov.IsOverridden) return ov.LocalValue.Value;
                }
            var global = ModSettings.GetSetting<BoolSetting>("proxy_announce." + part + "." + setting);
            return global != null ? global.Get() : def;
        }
    }
}
