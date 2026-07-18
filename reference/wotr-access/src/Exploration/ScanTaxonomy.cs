using System.Collections.Generic;

namespace WrathAccess.Exploration
{
    /// <summary>Which announcement part-set a category uses (set on the category; subcategories inherit).</summary>
    internal enum ScanClass { Unit, Object, Marker }

    /// <summary>
    /// THE canonical two-level classification of scannable things — one tree that drives scanner
    /// navigation, sonar sounds, announcements, and the settings UI. Replaces the old split between the
    /// flat <c>ScanCategory</c> enum (nav) and the half-nested <c>SonarTaxonomy</c> (sounds).
    ///
    /// Shape: top-level CATEGORIES (Units, Containers, Doors, …), each with zero or more SUBCATEGORIES
    /// (Units → Party/Enemies/Neutrals; Containers → Chests/Corpses/…). Every category also has an implicit
    /// "All" entry (the category node itself) — browsing the union and serving as the inherit base for its
    /// children. Node keys are dotted: "units", "units.enemies", "containers.corpse". An item reports the
    /// (sub)categories it belongs to (<see cref="ScanItem.Nodes"/>, many-to-many), one state-aware node
    /// that SOUNDS (<see cref="ScanItem.Primary"/>), and one stable node it ANNOUNCES as; all are keys here.
    ///
    /// The scanner navigation and sonar sounds (<see cref="ScanSounds"/>) run on this tree; the old flat
    /// <c>ScanCategory</c> and the separate <c>SonarTaxonomy</c> are gone. (Announcements move onto it next.)
    /// </summary>
    internal static class ScanTaxonomy
    {
        /// <summary>A sound pick meaning "play nothing".</summary>
        public const string Silent = "silent";
        /// <summary>A child sound pick meaning "use the parent category's pick".</summary>

        // Node-key constants — what ScanItem.Nodes / .Primary return (avoids magic strings).
        public const string Units = "units";
        public const string UnitsParty = "units.party";
        public const string UnitsEnemies = "units.enemies";
        public const string UnitsNeutrals = "units.neutrals";
        public const string Containers = "containers";
        public const string ContainersChest = "containers.chest";
        public const string ContainersCorpse = "containers.corpse";
        public const string ContainersEnvironment = "containers.environment";
        public const string ContainersSingle = "containers.single";
        public const string ContainersStash = "containers.stash";
        public const string ContainersOther = "containers.other";
        public const string Doors = "doors";
        public const string DoorsOpen = "doors.open";
        public const string Exits = "exits";
        public const string SearchPoints = "searchpoints";
        public const string Traps = "traps";
        public const string TrapZones = "trapzones";
        public const string Mechanisms = "mechanisms";
        public const string Hazards = "hazards";
        public const string Unexplored = "unexplored";
        public const string HazardsSpell = "hazards.spell";
        public const string HazardsTerrain = "hazards.terrain";
        public const string BuffZones = "buffzones";
        public const string Scenery = "scenery";
        public const string Poi = "poi";

        /// <summary>True for nodes that mark a real interactive thing (cursor targeting cares about these
        /// regardless of what sound — if any — the user assigned). POI/Scenery are not interactive.</summary>
        public static bool IsInteractive(string key) => key != null && key != Poi && key != Scenery;

        internal sealed class Node
        {
            public string Key { get; }            // dotted full key: "units", "units.enemies"
            public string Label { get; }          // English fallback (localised via LocKey)
            public string DefaultSound { get; }    // wav stem, or Silent
            public Node Parent { get; internal set; }
            public List<Node> Children { get; } = new List<Node>();

            private readonly ScanClass? _class;     // set on categories; subcategories inherit the parent's

            public Node(string key, string label, string defaultSound, ScanClass? cls)
            {
                Key = key; Label = label; DefaultSound = defaultSound; _class = cls;
            }

            public bool IsCategory => Parent == null;
            public bool IsBranch => Children.Count > 0;

            /// <summary>Loc key for the spoken/settings label ("taxonomy.units", "taxonomy.units.enemies").</summary>
            public string LocKey => "taxonomy." + Key;

            /// <summary>The announcement part-set class — the category's own, inherited by subcategories.</summary>
            public ScanClass Class => _class ?? Parent?.Class ?? ScanClass.Object;
        }

        private static readonly List<Node> _categories = new List<Node>();
        private static readonly Dictionary<string, Node> _byKey = new Dictionary<string, Node>();

        /// <summary>Top-level categories in navigation / display order.</summary>
        public static IReadOnlyList<Node> Categories => _categories;

        /// <summary>The node for a key, or null.</summary>
        public static Node Get(string key) => key != null && _byKey.TryGetValue(key, out var n) ? n : null;

        /// <summary>The subcategory cycle for a category: its "All" entry (the category itself) then its
        /// children. A leaf category yields just itself.</summary>
        public static IReadOnlyList<Node> NavSubcategories(Node category)
        {
            var list = new List<Node> { category };
            if (category != null) list.AddRange(category.Children);
            return list;
        }

        /// <summary>Every node, categories then their children, in declaration order (settings/locale walks).</summary>
        public static IEnumerable<Node> AllNodes()
        {
            foreach (var c in _categories)
            {
                yield return c;
                foreach (var s in c.Children) yield return s;
            }
        }

        static ScanTaxonomy()
        {
            Cat("units", "Units", ScanClass.Unit, Silent,
                Sub("party", "Party", "units-ally"),
                Sub("enemies", "Enemies", "units-enemy"),
                Sub("neutrals", "Neutrals", "units-neutral"));

            Cat("containers", "Containers", ScanClass.Object, "loot-generic",
                Sub("chest", "Chests", "loot-chest"),
                Sub("corpse", "Corpses", "loot-corpse"),
                Sub("environment", "Environment", "loot-environment"),
                Sub("single", "Single slot", "loot-single"),
                Sub("stash", "Player chest", "loot-stash"),
                Sub("other", "Other containers", "loot-generic"));

            Cat("doors", "Doors", ScanClass.Object, "door",
                Sub("open", "Open doors", "door_open"));

            Cat("exits", "Exits", ScanClass.Object, "transition");
            Cat("searchpoints", "Search points", ScanClass.Object, "unknown");
            Cat("traps", "Traps", ScanClass.Object, "trap");
            // The trap trigger AREAS (where not to step) — separate from the disarm devices above.
            Cat("trapzones", "Trap zones", ScanClass.Object, "trap-zone");
            Cat("mechanisms", "Mechanisms", ScanClass.Object, "mechanism");

            // Live area effects (Game.Instance.State.AreaEffects): harmful zones (spell AoEs like stinking
            // cloud, and placed terrain effects) vs beneficial buff zones. See ProxyAreaEffect for the split.
            Cat("hazards", "Hazards", ScanClass.Object, "hazard-zone",
                Sub("spell", "Spell effects", "hazard-zone"),
                Sub("terrain", "Terrain", "hazard-zone"));
            Cat("buffzones", "Buff zones", ScanClass.Object, "buff-zone");
            // Frontier blobs — where walkable unexplored ground borders explored ground (FrontierModel).
            Cat("unexplored", "Unexplored space", ScanClass.Object, Silent);

            Cat("scenery", "Scenery", ScanClass.Object, Silent);
            Cat("poi", "Points of interest", ScanClass.Marker, Silent);
        }

        // ---- builders ----

        // A pending subcategory (leaf name + label + default sound); the category prepends its key.
        private struct SubDef { public string Leaf, Label, Sound; }
        private static SubDef Sub(string leaf, string label, string sound)
            => new SubDef { Leaf = leaf, Label = label, Sound = sound };

        private static void Cat(string key, string label, ScanClass cls, string sound, params SubDef[] subs)
        {
            var cat = new Node(key, label, sound, cls);
            Register(cat);
            foreach (var s in subs)
            {
                var child = new Node(key + "." + s.Leaf, s.Label, s.Sound, null) { Parent = cat };
                cat.Children.Add(child);
                Register(child);
            }
            _categories.Add(cat);
        }

        private static void Register(Node n) => _byKey[n.Key] = n;
    }
}
