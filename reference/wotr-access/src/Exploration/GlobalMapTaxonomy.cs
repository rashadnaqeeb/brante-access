using System.Collections.Generic;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// The world-map's scannable entity types, kept SEPARATE from the in-area <see cref="ScanTaxonomy"/>
    /// (so the in-area scanner/nav is untouched) but built from the same <see cref="ScanTaxonomy.Node"/>
    /// type so they reuse the same settings UI (the Scanner tab's Entities tree) and the same sound
    /// mechanism (<see cref="ScanSounds"/>). One "World map" group with Locations (default the "transition"
    /// ping) and Junctions (default Silent — there are a lot of them, so they stay out of the sonar until
    /// you assign a sound). The world-map sonar resolves each point's node sound via <see cref="ScanSounds"/>.
    /// </summary>
    internal static class GlobalMapTaxonomy
    {
        public static readonly ScanTaxonomy.Node Locations;
        public static readonly ScanTaxonomy.Node Junctions;

        private static readonly List<ScanTaxonomy.Node> _categories = new List<ScanTaxonomy.Node>();
        public static IReadOnlyList<ScanTaxonomy.Node> Categories => _categories;

        static GlobalMapTaxonomy()
        {
            var wm = new ScanTaxonomy.Node("worldmap", "World map", ScanTaxonomy.Silent, ScanClass.Marker);
            Locations = new ScanTaxonomy.Node("worldmap.locations", "Locations", "transition", null) { Parent = wm };
            Junctions = new ScanTaxonomy.Node("worldmap.junctions", "Junctions", ScanTaxonomy.Silent, null) { Parent = wm };
            wm.Children.Add(Locations);
            wm.Children.Add(Junctions);
            _categories.Add(wm);
        }
    }
}
