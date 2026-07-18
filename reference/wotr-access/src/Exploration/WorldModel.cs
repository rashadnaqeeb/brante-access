using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.UI.MVVM._VM.ServiceWindows.LocalMap.Utils; // LocalMapModel

namespace WrathAccess.Exploration
{
    /// <summary>
    /// The one live registry of everything in the current area: a persistent <see cref="ScanItem"/> proxy
    /// per unit, map object, and local-map marker present. NOT fog-filtered — membership is "is it here"
    /// (in-area presence); <see cref="ScanItem.IsVisible"/> is a live per-item lens each consumer applies.
    ///
    /// Ticked every frame from <c>Main.OnUpdate</c>: it diffs the game's entity pools against the held set
    /// and raises <see cref="Added"/>/<see cref="Removed"/>, keeping ONE proxy instance per entity stable
    /// across frames. That stability is what lets the sonar attach a looping sound to an entity and keep
    /// it; the scanner and tile view just read <see cref="Items"/> (and filter by visibility themselves).
    /// Supersedes the old per-call WorldScan enumerator.
    /// </summary>
    internal static class WorldModel
    {
        private static readonly Dictionary<object, ScanItem> _items = new Dictionary<object, ScanItem>();
        private static readonly HashSet<object> _present = new HashSet<object>();
        private static readonly List<object> _gone = new List<object>();
        private static int _foldedVersion = -1;

        /// <summary>Every in-area item (all kinds, unfiltered by fog). Consumers apply <c>IsVisible</c>.</summary>
        public static IReadOnlyCollection<ScanItem> Items => _items.Values;

        public static event Action<ScanItem> Added;
        public static event Action<ScanItem> Removed;

        public static void Tick()
        {
            var state = Game.Instance != null ? Game.Instance.State : null;
            if (state == null) { FrontierModel.SyncArea(null); ClearAll(); return; } // no area loaded (menu/global map) → empty

            var areaName = Game.Instance.CurrentlyLoadedArea != null ? Game.Instance.CurrentlyLoadedArea.name : null;
            // Curated environmental details (scene-art things with no runtime data layer) reload on
            // area change and flow through the same registry, keyed by their entry objects.
            AreaDetails.Refresh(areaName);
            // Mod-authored environmental descriptions (room ambiance + per-asset), loaded per area the same way.
            EnvDescriptions.Refresh(areaName);
            // Frontier blobs are per-area too — entering a different area drops the stale set.
            FrontierModel.SyncArea(areaName);

            // Poll live every frame, but only build a proxy (and its capturing factory closure) for a
            // GENUINELY NEW entity — the ContainsKey guard keeps the common "already tracked" path
            // allocation-free. The kept proxy reads the entity's live state on demand, so a door opening,
            // HP change, etc. is still seen instantly; we just stop manufacturing garbage every frame.
            _present.Clear();
            foreach (var u in state.Units) { if (!_items.ContainsKey(u)) Ensure(u, () => new ProxyUnit(u)); _present.Add(u); }
            foreach (var o in state.MapObjects)
            {
                if (!_items.ContainsKey(o)) Ensure(o, () => new ProxyMapObject(o));
                _present.Add(o);
                // A trap additionally surfaces its TRIGGER AREA as its own item ("Trap zones") — keyed
                // by the trap view (a distinct object, so it can't collide with the entity keys).
                // ONLY the linked pair's mechanics half carries the real danger zone; the disarm DEVICE
                // half has just an interaction proximity radius (not a danger area) — no zone item.
                if (o is Kingmaker.View.MapObjects.Traps.TrapObjectData td
                    && td.View is Kingmaker.View.MapObjects.Traps.TrapObjectView tv
                    && tv.Settings != null && tv.Settings.ScriptZoneTrigger != null)
                {
                    var trap = td; // capture for the factory closure (only built when new)
                    if (!_items.ContainsKey(tv)) Ensure(tv, () => new ProxyTrapZone(trap));
                    _present.Add(tv);
                }
            }
            // Area effects: only PLACED ground zones (spell AoEs, terrain hazards). Skip on-unit auras
            // (Aura of Courage, etc.) — they follow a unit and there can be hundreds, so they're noise as
            // "zones"; the unit carries them (inspect the unit). View.OnUnit is the live flag (from m_OnUnit).
            foreach (var ae in state.AreaEffects)
            {
                if (ae.View != null && ae.View.OnUnit) continue;
                if (!_items.ContainsKey(ae)) Ensure(ae, () => new ProxyAreaEffect(ae));
                _present.Add(ae);
            }
            foreach (var m in LocalMapModel.Markers)
            {
                if (m == null) continue;
                try { if (!LocalMapModel.IsInCurrentArea(m.GetPosition())) continue; } catch { continue; }
                if (!_items.ContainsKey(m)) Ensure(m, () => new ProxyMarker(m));
                _present.Add(m);
            }
            foreach (var d in AreaDetails.Current)
            {
                var entry = d; // capture per-iteration for the factory closure (only built when new)
                if (!_items.ContainsKey(entry)) Ensure(entry, () => new ProxyDetail(entry));
                _present.Add(entry);
            }
            FoldFrontier();

            // Drop anything no longer in the pools (despawned, or left when the area changed).
            _gone.Clear();
            foreach (var key in _items.Keys) if (!_present.Contains(key)) _gone.Add(key);
            for (int i = 0; i < _gone.Count; i++)
            {
                var key = _gone[i];
                var item = _items[key];
                _items.Remove(key);
                Removed?.Invoke(item);
            }
        }

        /// <summary>Fold the unexplored-frontier blobs into the model. Called per tick, and directly by
        /// the L cycle right after FrontierModel.Refresh so the FIRST press already sees fresh items.</summary>
        public static void FoldFrontier()
        {
            // A recompute REUSES Blob objects for surviving openings (identity keeps the L cycle's
            // "continue from current" working) but drops vanished ones, and a mid-frame fold (the L
            // key) runs AFTER this frame's Tick already folded the old generation into _present —
            // without an eager purge of the dropped blobs the cycle would double-list for a frame
            // (heard live as "1 of 4" over 2 real blobs). Purge ONLY blobs no longer in Current, so
            // survivors keep their ScanItem. Version-gated so steady-state ticks skip the scan; the
            // purge reuses _gone (recompute is key-press work, and Tick's own _gone use starts after
            // this returns).
            if (FrontierModel.Version != _foldedVersion)
            {
                _foldedVersion = FrontierModel.Version;
                var current = FrontierModel.Current;
                _gone.Clear();
                foreach (var key in _items.Keys)
                {
                    if (!(key is FrontierModel.Blob)) continue;
                    bool live = false;
                    for (int i = 0; i < current.Count; i++)
                        if (ReferenceEquals(current[i], key)) { live = true; break; }
                    if (!live) _gone.Add(key);
                }
                for (int i = 0; i < _gone.Count; i++)
                {
                    var key = _gone[i];
                    var item = _items[key];
                    _items.Remove(key);
                    _present.Remove(key);
                    Removed?.Invoke(item);
                }
                _gone.Clear();
            }
            foreach (var f in FrontierModel.Current)
            {
                var blob = f; // capture for the factory closure
                if (!_items.ContainsKey(blob)) Ensure(blob, () => new ProxyFrontier(blob));
                _present.Add(blob);
            }
        }

        private static void Ensure(object key, Func<ScanItem> make)
        {
            if (_items.ContainsKey(key)) return;
            var item = make();
            _items[key] = item;
            Added?.Invoke(item);
        }

        private static void ClearAll()
        {
            if (_items.Count == 0) return;
            var snapshot = new List<ScanItem>(_items.Values);
            _items.Clear();
            for (int i = 0; i < snapshot.Count; i++) Removed?.Invoke(snapshot[i]);
        }
    }
}
