using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WrathAccess.Settings
{
    /// <summary>
    /// The mod's settings tree + JSON persistence (ported from SayTheSpire2). Build the <see cref="Root"/>
    /// tree, then <see cref="Initialize"/> with a persistent directory: it loads <c>settings.json</c>
    /// (a flat <c>{ fullPath: value }</c> map), applies saved values over the in-code defaults, and saves
    /// back. Every <c>Set</c> auto-saves (<see cref="MarkDirty"/>). Keys from another mod version are
    /// preserved across save (forward-compat); a corrupt file falls back to defaults without overwriting
    /// blindly. Stored under <c>Application.persistentDataPath</c> so it survives a mod redeploy.
    /// </summary>
    public static class ModSettings
    {
        public static RootCategorySetting Root { get; } = new RootCategorySetting();

        private static bool _dirty;
        private static string _path;
        private static readonly Dictionary<string, Setting> _byPath = new Dictionary<string, Setting>();
        private static readonly Dictionary<string, JToken> _unknownKeys = new Dictionary<string, JToken>();

        public static void Initialize(string settingsDir)
        {
            try { Directory.CreateDirectory(settingsDir); }
            catch (Exception e) { Main.Log?.Error("[settings] could not create dir: " + e.Message); }
            _path = Path.Combine(settingsDir, "settings.json");
            Reindex();
            Load();
            Save(); // normalize the file (defaults + preserved unknown keys)
            Main.Log?.Log("[settings] initialized at " + _path);
        }

        public static void MarkDirty() { _dirty = true; SaveIfDirty(); }
        public static void SaveIfDirty() { if (_dirty) Save(); }

        public static T GetSetting<T>(string path) where T : Setting
            => (_byPath.TryGetValue(path, out var s) ? s : null) as T;

        /// <summary>Resolve a CATEGORY by dotted path, walking segment-wise from Root — the flat index
        /// holds leaves only, and category paths never contain dotted keys (unlike action-key leaves).</summary>
        public static CategorySetting GetCategory(string path)
        {
            CategorySetting cat = Root;
            foreach (var seg in path.Split('.'))
            {
                cat = cat != null ? cat.Get<CategorySetting>(seg) : null;
                if (cat == null) return null;
            }
            return cat;
        }

        /// <summary>Rebuild the flat FullPath → Setting index. Call after the tree is built or changes.</summary>
        public static void Reindex()
        {
            _byPath.Clear();
            Walk(Root);
        }

        private static void Walk(CategorySetting cat)
        {
            foreach (var c in cat.Children)
            {
                if (c is CategorySetting cc) Walk(cc);
                else _byPath[c.FullPath] = c; // flat: dotted keys (e.g. action keys) are stored verbatim
            }
        }

        public static void Load()
        {
            _unknownKeys.Clear();
            if (_path == null || !File.Exists(_path)) return;
            try
            {
                var doc = JsonConvert.DeserializeObject<Dictionary<string, JToken>>(File.ReadAllText(_path));
                if (doc == null) return;
                foreach (var kv in doc)
                {
                    if (_byPath.TryGetValue(kv.Key, out var s)) s.LoadValue(kv.Value);
                    else _unknownKeys[kv.Key] = kv.Value; // unknown (other version) — keep it across save
                }
            }
            catch (Exception e) { Main.Log?.Error("[settings] load failed (using defaults): " + e.Message); }
        }

        /// <summary>Apply previously-unknown saved keys to settings that now exist (e.g. dynamic overlay
        /// subtrees created after the initial Load). Call after creating the new settings + Reindex.</summary>
        public static void ReapplyUnknown()
        {
            if (_unknownKeys.Count == 0) return;
            var applied = new List<string>();
            foreach (var kv in _unknownKeys)
                if (_byPath.TryGetValue(kv.Key, out var s)) { s.LoadValue(kv.Value); applied.Add(kv.Key); }
            foreach (var k in applied) _unknownKeys.Remove(k);
        }

        // ---- unknown-key access (schema migrations read old paths out of the preserved set) ----

        public static List<string> UnknownPaths() => new List<string>(_unknownKeys.Keys);

        public static bool TryGetUnknown(string path, out JToken token) => _unknownKeys.TryGetValue(path, out token);

        public static void RemoveUnknownWhere(Func<string, bool> predicate)
        {
            var doomed = new List<string>();
            foreach (var k in _unknownKeys.Keys)
                if (predicate(k)) doomed.Add(k);
            foreach (var k in doomed) _unknownKeys.Remove(k);
        }

        private static int _batchDepth;

        /// <summary>Suppress the per-change file writes while mutating MANY settings (the Reset
        /// buttons touch hundreds — a save each was a visible stall); one save when the batch ends.
        /// Direct Save() calls inside the batch defer too.</summary>
        public static void Batch(Action action)
        {
            _batchDepth++;
            try { action(); }
            finally { _batchDepth--; if (_batchDepth == 0) SaveIfDirty(); }
        }

        public static void Save()
        {
            if (_batchDepth > 0) { _dirty = true; return; } // deferred to the batch end
            _dirty = false;
            if (_path == null) return;
            try
            {
                var dict = new Dictionary<string, object>();
                foreach (var kv in _byPath)
                {
                    var boxed = kv.Value.BoxedValue;
                    if (boxed != null) dict[kv.Key] = boxed;
                }
                foreach (var kv in _unknownKeys)
                    if (!dict.ContainsKey(kv.Key)) dict[kv.Key] = kv.Value;
                File.WriteAllText(_path, JsonConvert.SerializeObject(dict, Formatting.Indented));
            }
            catch (Exception e) { Main.Log?.Error("[settings] save failed: " + e.Message); }
        }
    }
}
