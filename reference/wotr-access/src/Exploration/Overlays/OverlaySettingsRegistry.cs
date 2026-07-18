using System;
using System.Collections.Generic;
using System.Linq;
using WrathAccess.Settings;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// Builds the data-driven overlays from settings and supports adding/removing them at runtime.
    ///
    /// SETTINGS MODEL (the redesign): system TUNABLES are SHARED — registered once under
    /// <c>defaults.&lt;system&gt;</c> (surfaced by the Sonar / Log / Exploration tabs) with audio volumes
    /// under <c>audio.volumes</c> (Audio tab). An overlay's subtree (<c>overlays.&lt;id&gt;</c>) holds only
    /// COMPOSITION: a hidden display name, the cursor slots, and per system an <c>enabled</c> toggle +
    /// hidden <c>customized</c> flag. Whole-subtree inheritance: Customize() materializes the overlay's own
    /// full copy of a system's tree (same RegisterSettings schema, seeded from the current defaults) under
    /// <c>custom</c>, and the system then reads ONLY that copy; ResetSystem() drops it. The first id in the
    /// list is the standard overlay (Ctrl+O lands on it first). Old per-overlay tunables are migrated once
    /// (standard overlay's values seed the defaults). Called from <c>Main.BuildSettings</c>.
    /// </summary>
    internal static class OverlaySettingsRegistry
    {
        private static readonly Func<OverlaySystem>[] SystemTypes =
        {
            () => new GridSystem(),
            () => new SpatialSystem(),
            () => new SonarSystem(),
            () => new ElevationSystem(),
            () => new WallToneSystem(),
            () => new FogSystem(),
            () => new ObjectCueSystem(),
            () => new PathInfoSystem(),
            () => new AoePreviewSystem(),
            () => new LogSystem(),
            () => new GlobalMapSonarSystem(), // WorldMap-scoped: sweeps map points under the engaged overlay
        };

        // One prototype per system type (for Key/Name/schema; never bound or ticked).
        private static readonly List<OverlaySystem> Prototypes = BuildPrototypes();
        private static List<OverlaySystem> BuildPrototypes()
        {
            var list = new List<OverlaySystem>();
            foreach (var make in SystemTypes) list.Add(make());
            return list;
        }

        /// <summary>The system keys in declaration order (the order nodes render everywhere).</summary>
        public static IEnumerable<string> SystemKeys()
        {
            foreach (var p in Prototypes) yield return p.Key;
        }

        /// <summary>The system's raw display name, or null if the key isn't a system.</summary>
        public static string SystemName(string key)
        {
            foreach (var p in Prototypes) if (p.Key == key) return p.Name;
            return null;
        }

        private static Func<OverlaySystem> FactoryFor(string key)
        {
            for (int i = 0; i < Prototypes.Count; i++)
                if (Prototypes[i].Key == key) return SystemTypes[i];
            return null;
        }

        private static CategorySetting DefaultsFor(string key)
            => ModSettings.Root.Get<CategorySetting>("defaults")?.Get<CategorySetting>(key);

        private static CategorySetting SystemCat(string id, string key)
            => ModSettings.Root.Get<CategorySetting>("overlays")?.Get<CategorySetting>(id)?.Get<CategorySetting>(key);

        private static readonly Choice[] ModeChoices =
        {
            new Choice("none", "None", "choice.mode.none"),
            new Choice("continuous", "Continuous", "choice.mode.continuous"),
            new Choice("tiled", "Tiled", "choice.mode.tiled"),
        };

        // The invisible Default overlay's out-of-box composition (which systems are on). Stored as
        // defaults.<system>.enabled, user-editable from the Sonar/Log/Exploration tabs.
        private static readonly HashSet<string> DefaultOn =
            new HashSet<string> { "grid", "sonar", "fog", "object", "path", "aoe", "log", "worldmap_sonar" };

        private static readonly Dictionary<string, Overlay> _objects = new Dictionary<string, Overlay>();

        /// <summary>Pre-load (in BuildSettings): create only the overlays category + the id list, so Load
        /// can apply the saved list. The overlay subtrees are built afterwards (see <see cref="BuildOverlays"/>),
        /// because we don't know the user's ids until the list has loaded.</summary>
        public static void Register()
        {
            ModSettingsRegistry.EnsureCategory("overlays", "Overlays", "category.overlays");
            EnsureList();
            RegisterDefaults();
        }

        // The SHARED settings (the settings redesign): every system tunable once under
        // defaults.<system> (surfaced by the Sonar / Log / Exploration tabs), and every audio system
        // volume once under audio.volumes (the Audio tab). Overlays hold only composition (enabled +
        // cursor) plus optional whole-subtree overrides.
        private static void RegisterDefaults()
        {
            foreach (var proto in Prototypes)
            {
                var cat = ModSettingsRegistry.EnsureCategory("defaults." + proto.Key,
                    "Defaults/" + proto.Name, "system." + proto.Key);
                if (cat.Children.Count == 0)
                {
                    // The Default overlay's composition lives here too (it has no subtree of its own).
                    // NOT part of RegisterSettings, so per-overlay custom copies don't duplicate it.
                    cat.Add(ModeSetting(proto));
                    proto.RegisterSettings(cat);
                }
            }
            // The Default overlay's cursor (mode + speed per slot) — surfaced on the Exploration tab.
            var cursorCat = ModSettingsRegistry.EnsureCategory("defaults.cursor", "Defaults/Cursor", "overlay.cursor");
            if (cursorCat.GetByKey("announce_rooms") == null)
                cursorCat.Add(new BoolSetting("announce_rooms", "Announce room changes", true,
                    "overlay.cursor.announce_rooms"));
            BuildSlotSettings("defaults.cursor.primary", "Defaults/Cursor/Primary", "overlay.cursor.primary", "tiled", 15, "continuous", 18);
            BuildSlotSettings("defaults.cursor.secondary", "Defaults/Cursor/Secondary", "overlay.cursor.secondary", "none", 15, "continuous", 45);

            // The world-map tiled cursor's tile size, in MILES (== world units on the global map). Lives on
            // the in-area grid system's defaults — beside "Tile size (feet)" on the Exploration tab — but
            // only here (NOT in GridSystem.RegisterSettings, like the `enabled` flag), so per-overlay custom
            // grid copies don't get a dead duplicate: the global map has no overlays and always reads this.
            var grid = ModSettingsRegistry.EnsureCategory("defaults.grid", "Defaults/Grid", "system.grid");
            if (grid.GetByKey("worldmap_cell_size") == null)
                grid.Add(new IntSetting("worldmap_cell_size", "World map tile size (miles)", 2, 1, 50, 1,
                    "overlay.grid.worldmap_cell_size"));

            var volumes = ModSettingsRegistry.EnsureCategory("audio.volumes", "Audio/System volumes", "audio.volumes");
            foreach (var proto in Prototypes)
                if (proto is AudioSystem && volumes.GetByKey(proto.Key) == null)
                    volumes.Add(new IntSetting(proto.Key, proto.Name + " volume", 40, 0, 100, 5,
                        "audio.volumes." + proto.Key)); // 40% default — the mod sounds were too loud at 100% (master stays 100%)

            // The audio listener anchor (the "virtual head" — see ListenerAnchor): where the game's
            // 3D audio is heard from. Default = the same reference the sonification pans from, so
            // game audio and our sounds share one spatial frame; "camera" is the vanilla behaviour.
            var audio = ModSettingsRegistry.EnsureCategory("audio", "Audio", "category.audio");
            if (audio.GetByKey("engine") == null)
                audio.Add(new ChoiceSetting("engine", "Sound playback",
                    new System.Collections.Generic.List<Choice>
                    {
                        new Choice("classic", "Classic (flat stereo panning)", "audio.engine.classic"),
                        new Choice("wwise", "Game audio engine (3D, occlusion-capable)", "audio.engine.wwise"),
                    }, "classic", "audio.engine"));
            // Classic-engine spatial cues (see Spatializer), each toggleable so they can be A/B'd by ear.
            if (audio.GetByKey("itd") == null)
                audio.Add(new BoolSetting("itd", "Sharpen left/right (time-delay panning)", true, "audio.itd"));
            if (audio.GetByKey("front_back_filter") == null)
                audio.Add(new BoolSetting("front_back_filter", "Muffle sounds behind you (front/back cue)", true, "audio.front_back_filter"));
            if (audio.GetByKey("head_shadow") == null)
                audio.Add(new BoolSetting("head_shadow", "Ear shadowing (more natural left/right)", true, "audio.head_shadow"));
            if (audio.GetByKey("listener") == null)
                audio.Add(new ChoiceSetting("listener", "Game audio heard from",
                    new System.Collections.Generic.List<Choice>
                    {
                        new Choice("cursor", "Cursor (falls back to party)", "audio.listener.cursor"),
                        new Choice("party", "Party leader", "audio.listener.party"),
                        new Choice("camera", "Camera (game default)", "audio.listener.camera"),
                    }, "cursor", "audio.listener"));
            if (audio.GetByKey("listener_height") == null)
                audio.Add(new IntSetting("listener_height", "Listener height (feet)", 35, 0, 80, 5,
                    "audio.listener_height"));
        }

        private static CategorySetting BuildSlotSettings(string path, string labelPath, string locKey,
            string defaultMode, int defaultSpeed, string defaultWorldMapMode, int defaultWorldMapSpeed)
        {
            var cat = ModSettingsRegistry.EnsureCategory(path, labelPath, locKey);
            if (cat.GetByKey("mode") == null)
                cat.Add(new ChoiceSetting("mode", "Movement mode", ModeChoices, defaultMode, "overlay.movement_mode"));
            if (cat.GetByKey("speed") == null)
                cat.Add(new IntSetting("speed", "Speed (feet/sec)", defaultSpeed, 1, 60, 1, "overlay.speed"));
            // World-map cursor for this slot (a SEPARATE system from the in-area one above, but reusing this
            // settings home): its movement type — continuous glide vs typematic tiled stepping (same
            // none/continuous/tiled choices) — and its glide speed in MILES/sec. The global map equates 1
            // world unit with 1 mile (GlobalMapMovementController.MilesTravelled), so units == miles. Read by
            // GlobalMapCursor; tiled steps by the world-map tile size on defaults.grid.
            if (cat.GetByKey("worldmap_mode") == null)
                cat.Add(new ChoiceSetting("worldmap_mode", "World map movement type", ModeChoices, defaultWorldMapMode, "overlay.worldmap_mode"));
            if (cat.GetByKey("worldmap_speed") == null)
                cat.Add(new IntSetting("worldmap_speed", "World map speed (miles/sec)", defaultWorldMapSpeed, 1, 100, 1, "overlay.worldmap_speed"));
            return cat;
        }

        /// <summary>Post-load (after ModSettings.Initialize): build every overlay in the now-loaded list,
        /// re-apply their saved values onto the freshly-created subtrees, and publish the live set.</summary>
        public static void BuildOverlays()
        {
            _objects.Clear();
            foreach (var id in Ids()) _objects[id] = BuildOverlayObject(id);
            ModSettings.Reindex();
            ModSettings.ReapplyUnknown();    // applies enabled/cursor/customized flags saved before the subtrees existed
            MaterializeCustomizedSubtrees(); // flagged systems get their custom copies (seeded from defaults)
            ModSettings.Reindex();
            ModSettings.ReapplyUnknown();    // applies the saved custom.* values over the seeds
            MigrateLegacyTunables();         // pre-redesign per-overlay tunables -> shared defaults/volumes
            MigrateEnabledToMode();          // pre-mode-refactor `enabled` bool -> `mode` choice
            Publish();
            ModSettings.Save();              // normalize the file (applied keys now persisted as known)
        }

        // ---- whole-subtree customization ----

        public static bool IsCustomized(string id, string sysKey)
            => SystemCat(id, sysKey)?.Get<BoolSetting>("customized")?.Get() == true;

        /// <summary>Give the overlay its own full copy of the system settings, seeded from the
        /// current shared defaults. The system reads the copy from then on (whole-subtree semantics).</summary>
        public static void Customize(string id, string sysKey)
        {
            var sCat = SystemCat(id, sysKey);
            if (sCat == null || sCat.Get<CategorySetting>("custom") != null) return;
            CreateCustomTree(sCat, sysKey);
            sCat.Get<BoolSetting>("customized")?.Set(true);
            ModSettings.Reindex();
            ModSettings.MarkDirty();
        }

        /// <summary>Drop the overlay copy — the system follows the shared defaults again.</summary>
        public static void ResetSystem(string id, string sysKey)
        {
            var sCat = SystemCat(id, sysKey);
            if (sCat == null) return;
            var custom = sCat.Get<CategorySetting>("custom");
            if (custom != null) sCat.Remove(custom);
            sCat.Get<BoolSetting>("customized")?.Set(false);
            ModSettings.Reindex();
            ModSettings.MarkDirty();
        }

        private static void MaterializeCustomizedSubtrees()
        {
            foreach (var id in Ids())
                foreach (var proto in Prototypes)
                {
                    var sCat = SystemCat(id, proto.Key);
                    if (sCat != null && sCat.Get<BoolSetting>("customized")?.Get() == true
                        && sCat.Get<CategorySetting>("custom") == null)
                        CreateCustomTree(sCat, proto.Key);
                }
        }

        private static void CreateCustomTree(CategorySetting sysCat, string key)
        {
            var custom = new CategorySetting("custom", "Custom settings", localizationKey: "overlay.custom");
            sysCat.Add(custom);
            FactoryFor(key)?.Invoke().RegisterSettings(custom); // the SAME schema as the defaults tree
            var defaults = DefaultsFor(key);
            if (defaults != null) CopyValues(defaults, custom);
        }

        private static void CopyValues(CategorySetting from, CategorySetting to)
        {
            foreach (var src in from.Children)
            {
                var dst = to.GetByKey(src.Key);
                if (dst == null) continue;
                if (src is CategorySetting fc && dst is CategorySetting tc) CopyValues(fc, tc);
                else if (!(src is CategorySetting) && !(dst is CategorySetting))
                {
                    var v = src.BoxedValue;
                    if (v != null) { try { dst.LoadValue(v); } catch { } }
                }
            }
        }

        // One-shot: settings saved before the redesign hold tunables at overlays.<id>.<sys>.<key>.
        // Seed the shared defaults (and audio volumes) from the STANDARD overlay values, then purge
        // every overlay legacy tunable key (non-standard divergence is discarded by design).
        private static void MigrateLegacyTunables()
        {
            var ids = Ids();
            if (ids.Count == 0) return;
            var std = ids[0];
            foreach (var proto in Prototypes)
            {
                string prefix = "overlays." + std + "." + proto.Key + ".";
                foreach (var path in ModSettings.UnknownPaths())
                {
                    if (!path.StartsWith(prefix)) continue;
                    var rest = path.Substring(prefix.Length);
                    if (rest == "mode" || rest == "enabled" || rest == "customized" || rest.StartsWith("custom.")) continue;
                    string target = rest == "volume"
                        ? "audio.volumes." + proto.Key
                        : "defaults." + proto.Key + "." + rest;
                    var setting = ModSettings.GetSetting<Setting>(target);
                    if (setting != null && ModSettings.TryGetUnknown(path, out var tok))
                    {
                        try { setting.LoadValue(tok); } catch { }
                    }
                }
            }
            foreach (var id in ids)
                foreach (var proto in Prototypes)
                {
                    string prefix = "overlays." + id + "." + proto.Key + ".";
                    ModSettings.RemoveUnknownWhere(p =>
                        p.StartsWith(prefix) && !p.EndsWith(".enabled") && !p.Contains(".custom"));
                }
        }

        // Build the per-system "mode" choice (replaces the old per-overlay "enabled" bool): the system's
        // SupportedModes as choices, defaulting to Continuous for the on-by-default set, else Off.
        private static ChoiceSetting ModeSetting(OverlaySystem sys)
        {
            var choices = sys.SupportedModes.Select(OverlayModes.Choice).ToList();
            var def = DefaultOn.Contains(sys.Key) ? OverlayMode.Continuous : OverlayMode.Off;
            if (!sys.SupportedModes.Contains(def)) def = sys.SupportedModes.Count > 0 ? sys.SupportedModes[0] : OverlayMode.Off;
            // "Active when", NOT "mode": this is the audio playback gate (Off / When moving /
            // Continuous), and it shared a loc key with the cursor slots' "Movement mode" — the JSON
            // duplicate made every SYSTEM display "Movement mode", nonsense for cursor-independent
            // systems like the log (whose choices are just Off/Continuous).
            return new ChoiceSetting("mode", "Active when", choices, OverlayModes.Id(def), "overlay.play_mode");
        }

        // Pre-mode-refactor composition flag: map each saved `<sys>.enabled` bool to the new `<sys>.mode`
        // choice (true => Continuous, false => Off), then drop the orphaned key — keeps existing testers'
        // on/off setup across the upgrade.
        private static void MigrateEnabledToMode()
        {
            foreach (var path in ModSettings.UnknownPaths())
            {
                if (!path.EndsWith(".enabled")) continue;
                if (!path.StartsWith("overlays.") && !path.StartsWith("defaults.")) continue;
                var modePath = path.Substring(0, path.Length - ".enabled".Length) + ".mode";
                var mode = ModSettings.GetSetting<ChoiceSetting>(modePath);
                if (mode == null || !ModSettings.TryGetUnknown(path, out var tok)) continue;
                bool on; try { on = tok.ToObject<bool>(); } catch { on = true; }
                mode.Set(OverlayModes.Id(on ? OverlayMode.Continuous : OverlayMode.Off));
            }
            ModSettings.RemoveUnknownWhere(p =>
                p.EndsWith(".enabled") && (p.StartsWith("overlays.") || p.StartsWith("defaults.")));
        }

        // ---- runtime add / remove ----

        /// <summary>Append a new overlay (generic config) and make it live. Returns its id.</summary>
        public static string Add()
        {
            var ids = Ids();
            var id = NewId(ids);
            // Create the name first (so the object reads it), then build the subtree + object.
            var oCat = ModSettingsRegistry.EnsureCategory("overlays." + id, "Overlays/Overlay");
            if (oCat.Get<StringSetting>("name") == null)
                oCat.Add(new StringSetting("name", "Name", "Overlay " + (ids.Count + 1), "overlay.name") { Hidden = true });

            ids.Add(id);
            ListSetting().Set(string.Join(",", ids));
            _objects[id] = BuildOverlayObject(id);
            ModSettings.Reindex();
            Publish();
            ModSettings.MarkDirty();
            return id;
        }

        /// <summary>The user-added overlay ids in cycle order (the invisible Default precedes them all).</summary>
        public static IReadOnlyList<string> OverlayIds() => Ids();

        /// <summary>The live display name of an overlay (its hidden name setting), falling back to the id.</summary>
        public static string OverlayName(string id)
            => NameSetting(id)?.Get() ?? id;

        /// <summary>Rename an overlay (persists, and updates the live object's spoken name).</summary>
        public static void SetOverlayName(string id, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            NameSetting(id)?.Set(name); // StringSetting.Set auto-saves
            if (_objects.TryGetValue(id, out var o)) o.Name = name;
        }

        private static StringSetting NameSetting(string id)
            => ModSettings.Root.Get<CategorySetting>("overlays")?.Get<CategorySetting>(id)?.Get<StringSetting>("name");

        /// <summary>Remove an overlay (the invisible Default always remains). Returns true if removed.</summary>
        public static bool Remove(string id)
        {
            var ids = Ids();
            if (!ids.Contains(id)) return false;
            ids.Remove(id);
            ListSetting().Set(string.Join(",", ids));

            var overlays = ModSettings.Root.Get<CategorySetting>("overlays");
            var sub = overlays?.GetByKey(id);
            if (sub != null) overlays.Remove(sub);
            _objects.Remove(id);

            ModSettings.Reindex();
            Publish();
            ModSettings.MarkDirty();
            return true;
        }

        // ---- building ----

        private static Overlay BuildOverlayObject(string id)
        {
            var oCat = ModSettingsRegistry.EnsureCategory("overlays." + id, "Overlays/Overlay");

            var nameSetting = oCat.Get<StringSetting>("name");
            if (nameSetting == null)
            {
                nameSetting = new StringSetting("name", "Name", "Overlay", "overlay.name") { Hidden = true };
                oCat.Add(nameSetting);
            }
            oCat.LabelProvider = () => nameSetting.Get(); // the menu node reads the live name

            var overlay = new Overlay(nameSetting.Get());

            foreach (var make in SystemTypes)
            {
                var sys = make();
                var sCat = ModSettingsRegistry.EnsureCategory("overlays." + id + "." + sys.Key,
                    "Overlays/" + nameSetting.Get() + "/" + sys.Name);
                // Composition only: mode + the customized flag. Tunables live in the shared
                // defaults, or in a custom subtree materialized by Customize().
                if (sCat.GetByKey("mode") == null) // new overlays start as a copy of the Default's mode
                {
                    var modeSetting = ModeSetting(sys);
                    var defMode = DefaultsFor(sys.Key)?.Get<ChoiceSetting>("mode")?.Current?.Id;
                    if (defMode != null) modeSetting.Set(defMode);
                    sCat.Add(modeSetting);
                }
                if (sCat.GetByKey("customized") == null)
                    sCat.Add(new BoolSetting("customized", "Customized", false) { Hidden = true });
                sys.Bind(sCat, DefaultsFor(sys.Key));
                overlay.With(sys);
            }

            var primaryCat = BuildSlot(id, "primary", "Primary", DefaultSlotMode("primary", "tiled"));
            var secondaryCat = BuildSlot(id, "secondary", "Secondary", DefaultSlotMode("secondary", "none"));
            overlay.Cursor.SetSlots(primaryCat, secondaryCat);
            WireModeChange(primaryCat, overlay);
            WireModeChange(secondaryCat, overlay);
            return overlay;
        }

        private static CategorySetting BuildSlot(string id, string key, string label, string defaultMode)
        {
            var cat = ModSettingsRegistry.EnsureCategory("overlays." + id + ".cursor." + key,
                "Overlays/_/Cursor/" + label); // overlay name segment is irrelevant (already labelled)
            if (cat.GetByKey("mode") == null)
                cat.Add(new ChoiceSetting("mode", "Movement mode", ModeChoices, defaultMode, "overlay.movement_mode"));
            if (cat.GetByKey("speed") == null)
                cat.Add(new IntSetting("speed", "Speed (feet/sec)", 15, 1, 60, 1, "overlay.speed"));
            // World-map cursor for this slot (read by GlobalMapCursor off the active overlay) — same as the
            // defaults' slots (BuildSlotSettings), so each overlay's world-map cursor is configured per-overlay.
            if (cat.GetByKey("worldmap_mode") == null)
                cat.Add(new ChoiceSetting("worldmap_mode", "World map movement type", ModeChoices, "continuous", "overlay.worldmap_mode"));
            if (cat.GetByKey("worldmap_speed") == null)
                cat.Add(new IntSetting("worldmap_speed", "World map speed (miles/sec)", key == "secondary" ? 45 : 18, 1, 100, 1, "overlay.worldmap_speed"));
            return cat;
        }

        private static void WireModeChange(CategorySetting slotCat, Overlay overlay)
        {
            var mode = slotCat?.Get<ChoiceSetting>("mode");
            if (mode != null) mode.Changed += _ => overlay.Cursor.ResolveModes();
        }

        // The invisible Default overlay: always first in the Ctrl+O cycle, never in the Overlays tab.
        // Its tunables AND composition are the shared defaults themselves — systems bind the defaults
        // category as their per-overlay category too, so `enabled` resolves to defaults.<sys>.enabled.
        internal static Overlay DefaultOverlay => _defaultOverlay;

        private static Overlay _defaultOverlay;

        private static Overlay BuildDefaultOverlay()
        {
            var overlay = new Overlay(Message.Localized("ui", "overlay.default_name").Resolve());
            foreach (var make in SystemTypes)
            {
                var sys = make();
                var d = DefaultsFor(sys.Key);
                sys.Bind(d, d);
                overlay.With(sys);
            }
            var primary = ModSettings.Root.Get<CategorySetting>("defaults")?.Get<CategorySetting>("cursor")?.Get<CategorySetting>("primary");
            var secondary = ModSettings.Root.Get<CategorySetting>("defaults")?.Get<CategorySetting>("cursor")?.Get<CategorySetting>("secondary");
            overlay.Cursor.SetSlots(primary, secondary);
            WireModeChange(primary, overlay);
            WireModeChange(secondary, overlay);
            return overlay;
        }

        private static void Publish()
        {
            if (_defaultOverlay == null) _defaultOverlay = BuildDefaultOverlay();
            var list = new List<Overlay> { _defaultOverlay };
            list.AddRange(Ids().Select(id => _objects.TryGetValue(id, out var o) ? o : null)
                .Where(o => o != null));
            OverlayManager.SetOverlays(list);
        }

        // ---- list helpers ----

        private static string DefaultSlotMode(string slot, string fallback)
            => ModSettings.Root.Get<CategorySetting>("defaults")?.Get<CategorySetting>("cursor")
                ?.Get<CategorySetting>(slot)?.Get<ChoiceSetting>("mode")?.Current?.Id ?? fallback;

        // The list starts EMPTY: the invisible Default overlay is always present, and users add
        // explicit overlays only when they want a deviating lens.
        private static string DefaultList => "";

        private static void EnsureList()
        {
            var overlays = ModSettings.Root.Get<CategorySetting>("overlays");
            if (overlays != null && overlays.Get<StringSetting>("list") == null)
                overlays.Add(new StringSetting("list", "Overlay list", DefaultList, "overlay.list") { Hidden = true });
        }

        private static StringSetting ListSetting()
            => ModSettings.Root.Get<CategorySetting>("overlays")?.Get<StringSetting>("list");

        private static List<string> Ids()
        {
            var raw = ListSetting()?.Get() ?? DefaultList;
            return raw.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToList();
        }

        private static string NewId(List<string> ids)
        {
            int n = 1;
            while (ids.Contains("overlay_" + n) || _objects.ContainsKey("overlay_" + n)) n++;
            return "overlay_" + n;
        }

    }
}
