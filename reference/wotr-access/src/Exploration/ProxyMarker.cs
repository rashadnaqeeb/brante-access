using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.ServiceWindows.LocalMap.Utils; // ILocalMapMarker
using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// A local-map point of interest (<see cref="ILocalMapMarker"/>) — the game's own curated markers
    /// (exits, loot, quest things, named units, …), already fog/reveal-filtered. Kept in a single Points
    /// of Interest category for now so we can compare it against the entity-derived categories.
    /// </summary>
    internal sealed class ProxyMarker : ScanItem
    {
        private readonly ILocalMapMarker _marker;

        public ProxyMarker(ILocalMapMarker marker) { _marker = marker; }

        public override string Name
        {
            get
            {
                var d = _marker.GetDescription();
                return string.IsNullOrEmpty(d) ? _marker.GetMarkerType().ToString() : d;
            }
        }

        public override Vector3 Position => _marker.GetPosition();

        public override bool IsVisible => _marker.IsVisible();

        public override IEnumerable<string> Nodes { get { yield return "poi"; } }

        public override string Primary => ScanTaxonomy.Poi; // silent by default; assignable in Sounds

        // Announce-node = Primary (poi); marker part-set (name + location only).
        // Name only (the game's curated, localized description) + spatial. The marker KIND (an unlocalized
        // enum) was a dev aid the old line tacked on — and double-spoke when there was no description; we
        // drop it rather than ship a raw enum word. (A localized kind/type could be added later.)
        protected override IEnumerable<Announce.ScanAnnouncement> StateParts()
        {
            foreach (var p in NameAndType(_marker.GetDescription(), null)) yield return p;
        }
    }
}
