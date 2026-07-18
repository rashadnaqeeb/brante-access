using System;
using System.Collections.Generic;
using System.IO;
using Kingmaker.Controllers; // FogOfWarController
using Newtonsoft.Json;
using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// Curated environmental details: things that exist only as scene art — paintings, murals, signs —
    /// with no entity/part/marker behind them, so no data layer the mod can read at runtime. Sighted
    /// players get them for free; we curate them per area as a JSON file of positions + locale keys
    /// (<c>assets/details/&lt;AreaBlueprintName&gt;.json</c>, entries <c>{x,y,z,key}</c> spoken via
    /// <c>detail.&lt;key&gt;</c> in the ui table). Positions are extracted offline from the scene
    /// bundles (safe: the game is patch-frozen). Each entry becomes a <see cref="ProxyDetail"/> scan
    /// item: listed under Points of Interest, B-cycled by the review cursor, fog-gated like anything
    /// a sighted player can only see once revealed. First data file: the Shield Maze's hint paintings
    /// (their ORDER encodes the colored-button puzzle) + color notes at the buttons themselves.
    /// </summary>
    internal static class AreaDetails
    {
        internal sealed class Entry
        {
            public float x { get; set; }
            public float y { get; set; }
            public float z { get; set; }
            public string key { get; set; }
        }

        private static readonly List<Entry> _entries = new List<Entry>();
        private static string _loadedArea;

        /// <summary>The current area's curated entries (stable objects — WorldModel keys on them).</summary>
        public static IReadOnlyList<Entry> Current => _entries;

        /// <summary>Reload when the area changes. Null/empty name (menus) clears.</summary>
        public static void Refresh(string areaName)
        {
            if (areaName == _loadedArea) return;
            _loadedArea = areaName;
            _entries.Clear();
            if (string.IsNullOrEmpty(areaName)) return;
            try
            {
                var path = Path.Combine(Main.ModDir ?? "", "assets", "details", areaName + ".json");
                if (!File.Exists(path)) return;
                var parsed = JsonConvert.DeserializeObject<List<Entry>>(File.ReadAllText(path));
                if (parsed != null)
                    foreach (var e in parsed)
                        if (e != null && !string.IsNullOrEmpty(e.key)) _entries.Add(e);
                Main.Log?.Log("[details] " + areaName + ": " + _entries.Count + " curated details");
            }
            catch (Exception e)
            {
                Main.Log?.Warning("[details] failed to load for " + areaName + ": " + e.Message);
            }
        }
    }

    /// <summary>One curated environmental detail (see <see cref="AreaDetails"/>): a fixed point with a
    /// mod-localized description. Points of Interest membership/primary, so the B review cycle and the
    /// poi sonar pick cover it; visible once out of the fog of war, like the art it describes.</summary>
    internal sealed class ProxyDetail : ScanItem
    {
        private readonly AreaDetails.Entry _entry;

        public ProxyDetail(AreaDetails.Entry entry) { _entry = entry; }

        public override string Name => Loc.T("detail." + _entry.key);

        public override Vector3 Position => new Vector3(_entry.x, _entry.y, _entry.z);

        public override bool IsVisible
        {
            get { try { return !FogOfWarController.IsInFogOfWar(Position); } catch { return false; } }
        }

        public override IEnumerable<string> Nodes
        {
            get { yield return "poi"; }
        }

        public override string Primary => ScanTaxonomy.Poi;
    }
}
