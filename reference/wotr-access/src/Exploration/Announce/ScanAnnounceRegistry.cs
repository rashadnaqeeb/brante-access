using System.Collections.Generic;
using WrathAccess.Settings;

namespace WrathAccess.Exploration.Announce
{
    /// <summary>
    /// Builds the scan-announcement settings over the unified <see cref="ScanTaxonomy"/> (the proxy
    /// parallel to <c>AnnouncementRegistry</c>, kept separate so scan items needn't be UI elements):
    /// <list type="bullet">
    /// <item><c>proxy_announce.{part}</c> — global per-part settings (the inherited base): an <c>enabled</c>
    /// toggle, plus the spatial part's distance/direction/height/coordinates sub-toggles.</item>
    /// <item><c>proxy_elem.{nodeKey}.{part}.enabled</c> — per-entity-type tri-state overrides, one per
    /// taxonomy node for the parts that node's <see cref="ScanClass"/> offers. Node → category → global
    /// inheritance is resolved in <see cref="ScanAnnounceContext"/>. Only the whole-part <c>enabled</c> is
    /// per-node; the spatial sub-toggles stay global (configured once).</item>
    /// </list>
    /// Registered pre-load with the other static categories, so persistence restores it normally.
    /// </summary>
    internal static class ScanAnnounceRegistry
    {
        private struct Opt { public string Key; public bool Def; public Opt(string k, bool d) { Key = k; Def = d; } }

        // Each global part and the settings it carries (the spatial sub-toggles live here, global-only).
        private static readonly (string part, Opt[] opts)[] Parts =
        {
            ("name",         new[] { new Opt("enabled", true) }),
            ("type",         new[] { new Opt("enabled", true) }),
            ("action",       new[] { new Opt("enabled", true) }),
            ("hp",           new[] { new Opt("enabled", true) }),
            ("condition",    new[] { new Opt("enabled", true) }),
            ("object_state", new[] { new Opt("enabled", true) }),
            ("spatial",      new[] { new Opt("enabled", true), new Opt("distance", true),
                                     new Opt("direction", true), new Opt("height", true),
                                     new Opt("coordinates", false) }),
        };

        // The parts a node offers, by its class (subcategories inherit the category's class).
        private static string[] PartsFor(ScanClass cls)
        {
            switch (cls)
            {
                case ScanClass.Unit: return new[] { "name", "type", "action", "hp", "condition", "spatial" };
                case ScanClass.Marker: return new[] { "name", "spatial" };
                default: return new[] { "name", "type", "object_state", "spatial" }; // Object
            }
        }

        // English fallbacks (real labels come from the "settings" locale table by these keys).
        private static readonly Dictionary<string, string> PartLabel = new Dictionary<string, string>
        {
            { "name", "Name" }, { "type", "Type" }, { "action", "Action" }, { "hp", "Health" },
            { "condition", "Condition" }, { "object_state", "Object state" }, { "spatial", "Location" },
        };
        private static readonly Dictionary<string, string> OptLabel = new Dictionary<string, string>
        {
            { "enabled", "Announce" }, { "distance", "Distance" }, { "direction", "Direction" },
            { "height", "Height difference" }, { "coordinates", "Coordinates (debug)" },
        };

        public static void RegisterDefaults()
        {
            // Globals (the inherited base) — full schema per part.
            var globals = new Dictionary<string, BoolSetting>(); // "part.opt" -> global
            foreach (var (part, opts) in Parts)
            {
                var cat = ModSettingsRegistry.EnsureCategory(
                    "proxy_announce." + part, "Scan announcements/" + PartLabel[part], "/proxyann." + part);
                foreach (var o in opts)
                {
                    if (cat.GetByKey(o.Key) == null)
                        cat.Add(new BoolSetting(o.Key, OptLabel[o.Key], o.Def, "proxyann." + o.Key));
                    globals[part + "." + o.Key] = cat.Get<BoolSetting>(o.Key);
                }
            }

            // Per-entity-type overrides: a category per taxonomy node, holding one tri-state per part its
            // class offers (the whole-part "enabled", keyed by the part, labelled with the part). Only the
            // enabled flag is per-node; spatial sub-toggles stay global. node→category→global resolution
            // lives in ScanAnnounceContext; the part tri-state path is proxy_elem.<nodeKey>.<part>.
            foreach (var node in ScanTaxonomy.AllNodes())
            {
                var cat = EnsureNodeCat(node);
                foreach (var part in PartsFor(node.Class))
                    if (cat.GetByKey(part) == null)
                        cat.Add(new NullableBoolSetting(part, PartLabel[part],
                            globals[part + ".enabled"], "proxyann." + part));
            }
        }

        // proxy_elem.<nodeKey>, with label/loc paths walking the node hierarchy so each segment
        // (category, subcategory) gets its taxonomy loc key.
        private static CategorySetting EnsureNodeCat(ScanTaxonomy.Node node)
        {
            var labels = new List<string> { "Scan overrides" };
            var locs = new List<string>();
            string cum = "";
            foreach (var seg in node.Key.Split('.'))
            {
                cum = cum.Length == 0 ? seg : cum + "." + seg;
                var n = ScanTaxonomy.Get(cum);
                labels.Add(n != null ? n.Label : seg);
                locs.Add("taxonomy." + cum);
            }
            return ModSettingsRegistry.EnsureCategory(
                "proxy_elem." + node.Key, string.Join("/", labels), "/" + string.Join("/", locs));
        }
    }
}
