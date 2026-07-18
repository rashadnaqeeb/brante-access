using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// Mod-authored environmental descriptions (see <c>docs/design/environmental-descriptions.md</c>). Two
    /// stores: per-area ROOM-ambiance anchors (<c>assets/descriptions/&lt;AreaBlueprintName&gt;.json</c>) and a
    /// GLOBAL asset→description table (<c>assets/descriptions/_assets.json</c>, keyed by normalized GameObject
    /// name so one entry covers every instance of that prop). Read on demand by the Describe key (X): a focused
    /// MAP-OBJECT with an asset description reads that; otherwise the room the cursor is standing in.
    ///
    /// Anchors are world COORDINATES (never room IDs — RoomMap is used only to test "am I in this room"), so
    /// descriptions survive segmentation changes. Prose lives in the locale table (<c>desc.*</c> keys) so it
    /// localizes like everything else; the JSON holds coordinates + keys only. UNITS are never described here —
    /// they're dynamic (the scanner reports them live); static prose describes only the permanent scene.
    /// </summary>
    internal static class EnvDescriptions
    {
        internal sealed class RoomEntry { public float x { get; set; } public float y { get; set; } public float z { get; set; } public string key { get; set; } }
        private sealed class AreaFile { public List<RoomEntry> rooms { get; set; } }

        private static readonly List<RoomEntry> _rooms = new List<RoomEntry>();
        private static readonly Dictionary<string, string> _assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static string _loadedArea;
        private static bool _assetsLoaded;

        /// <summary>Reload the per-area room anchors when the area changes (null/empty clears). Called beside
        /// <see cref="AreaDetails.Refresh"/> in WorldModel.Tick.</summary>
        public static void Refresh(string areaName)
        {
            LoadAssetsOnce();
            if (areaName == _loadedArea) return;
            _loadedArea = areaName;
            _rooms.Clear();
            if (string.IsNullOrEmpty(areaName)) return;
            try
            {
                var path = Path.Combine(Main.ModDir ?? "", "assets", "descriptions", areaName + ".json");
                if (!File.Exists(path)) return;
                var parsed = JsonConvert.DeserializeObject<AreaFile>(File.ReadAllText(path));
                if (parsed?.rooms != null)
                    foreach (var r in parsed.rooms)
                        if (r != null && !string.IsNullOrEmpty(r.key)) _rooms.Add(r);
                Main.Log?.Log("[desc] " + areaName + ": " + _rooms.Count + " room descriptions");
            }
            catch (Exception e) { Main.Log?.Warning("[desc] load failed for " + areaName + ": " + e.Message); }
        }

        private static void LoadAssetsOnce()
        {
            if (_assetsLoaded) return;
            _assetsLoaded = true;
            try
            {
                var path = Path.Combine(Main.ModDir ?? "", "assets", "descriptions", "_assets.json");
                if (!File.Exists(path)) return;
                var parsed = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
                if (parsed != null)
                    foreach (var kv in parsed)
                    {
                        var k = NormalizeAssetKey(kv.Key);
                        if (!string.IsNullOrEmpty(k) && !string.IsNullOrEmpty(kv.Value)) _assets[k] = kv.Value;
                    }
                Main.Log?.Log("[desc] " + _assets.Count + " asset descriptions");
            }
            catch (Exception e) { Main.Log?.Warning("[desc] asset load failed: " + e.Message); }
        }

        /// <summary>Normalize a GameObject/prefab name to a stable dedupe key: drop "(Clone)", the "_!Visual"
        /// marker, and a trailing Unity instance index " (3)"; lowercase. KEEPS the "_NN" variant suffix
        /// (chest_01 ≠ chest_02). e.g. "luxery_chest_02_!Visual (3)" → "luxery_chest_02".</summary>
        public static string NormalizeAssetKey(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var s = raw.Replace("(Clone)", "");
            s = Regex.Replace(s, @"_?!Visual", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\s*\(\d+\)\s*$", ""); // trailing " (19)"
            s = s.Trim().ToLowerInvariant();
            return s.Length > 0 ? s : null;
        }

        /// <summary>Whether the global asset table already has text for this key — the survey tooling
        /// (DevSurvey/tools/survey.py) skips described assets on incremental re-runs.</summary>
        internal static bool HasAssetText(string key)
            => !string.IsNullOrEmpty(key) && _assets.ContainsKey(key);

        /// <summary>The Describe-target (X) readout: the focused map-object's asset description. Units
        /// and undescribed objects get the "nothing described" line — the ROOM is Shift+X's job, so X
        /// stays unambiguously "about the thing I have focused".</summary>
        public static string DescribeItem(ScanItem reviewed)
        {
            if (reviewed == null) return Loc.T("scan.no_item");
            if (!reviewed.IsUnit)
            {
                var key = reviewed.AssetKey;
                if (!string.IsNullOrEmpty(key) && _assets.TryGetValue(key, out var atext) && !string.IsNullOrEmpty(atext))
                    return atext;
            }
            return Loc.T("desc.none");
        }

        /// <summary>The Describe-room (Shift+X) readout: the ambiance of the room the cursor stands in.</summary>
        public static string DescribeRoom(Vector3 cursor)
            => RoomTextAt(cursor) ?? Loc.T("desc.none");

        // The nearest room anchor that resolves to the SAME room the cursor is standing in (RoomMap as a live
        // membership test, not a stored key). One anchor per room today, so this is effectively a direct hit.
        private static string RoomTextAt(Vector3 cursor)
        {
            if (_rooms.Count == 0) return null;
            var here = RoomMap.RoomAt(cursor);
            if (here == null) return null;
            RoomEntry best = null; float bestD = float.MaxValue;
            foreach (var r in _rooms)
            {
                var rp = new Vector3(r.x, r.y, r.z);
                if (!ReferenceEquals(RoomMap.RoomAt(rp), here)) continue;
                float dx = rp.x - cursor.x, dz = rp.z - cursor.z, d = dx * dx + dz * dz;
                if (best == null || d < bestD) { best = r; bestD = d; }
            }
            if (best == null) return null;
            string title = Loc.T("desc." + best.key + ".title");
            string body = Loc.T("desc." + best.key + ".body");
            return string.IsNullOrEmpty(title) ? body : title + ". " + body;
        }
    }
}
