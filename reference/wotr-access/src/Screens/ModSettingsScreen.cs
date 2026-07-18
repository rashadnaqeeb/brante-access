using System.Collections.Generic;
using System.Linq;
using WrathAccess.Settings;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The mod's settings screen — the tabbed category browser. Opened from the <see cref="ModMenuScreen"/>
    /// launcher (Settings entry), not bound directly to a key. Mod-pushed: <see cref="IsActive"/> reads a
    /// static flag <see cref="Open"/> sets. Graph-native: a CATEGORIES tab stop, the selected category's
    /// settings tree (content keys carry the tab, so switching re-keys content only and expansion is
    /// remembered per tab), then the two Reset stops. Being immediate-mode, the structural tabs are LIVE
    /// from their registries: adding/removing/renaming overlays or speech configs, and Customize/Reset on
    /// an overlay system, simply render differently next frame — the old in-place tree-mutation machinery
    /// (id→node maps, insert-before-add-button, per-mutation focus juggling) is gone; after an Add we just
    /// point focus at the new node's key. Escape closes.
    /// </summary>
    public sealed class ModSettingsScreen : Screen
    {
        private static bool s_open;
        public static void Open() { s_open = true; }
        public static void CloseMenu() { s_open = false; }

        public override string Key => "overlay.modsettings";
        public override string ScreenName => Message.Localized("ui", "screen.settings").Resolve();
        public override int Layer => 37; // above the mod-menu launcher (35), so it stacks on top of it
        public override bool IsActive() => s_open;

        private int _active; // the selected category tab (view state)

        // Explicit tabs (the settings Root holds bindings/announcements/ui, which don't map 1:1 to tabs:
        // the UI tab composes the global announcement settings + the per-element-type overrides).
        // Alphabetical by label, so the tab list is easy to scan.
        private static readonly (string key, string label, string loc)[] Tabs =
        {
            ("audio", "Audio", "category.audio"),
            ("events", "Events", "category.events"),
            ("exploration", "Exploration", "category.exploration"),
            ("input", "Input", "category.input"),
            ("log", "Log", "category.log"),
            ("overlays", "Overlays", "category.overlays"),
            ("scanner", "Scanner", "category.scanner"),
            ("speech", "Speech", "category.speech"),
            ("ui", "UI", "category.ui"),
        };

        // Focus mode is owned by the ModMenuScreen launcher (always open beneath us), so we don't touch it.
        public override void OnPush() { _active = 0; }

        // Escape closes the whole menu.
        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => CloseMenu());
        }

        // Localized menu string ("settings" table) with the English fallback.
        private static string L(string key, string fallback)
            => WrathAccess.Localization.LocalizationManager.GetOrDefault("settings", key, fallback);


        public override void Build(GraphBuilder b)
        {
            // The category tabs.
            b.BeginStop("tabs").PushContext(L("menu.categories", "Categories"), "list");
            for (int i = 0; i < Tabs.Length; i++)
            {
                int idx = i;
                b.AddItem(ControlId.Structural("modset:tab:" + Tabs[i].key),
                    GraphNodes.Tab(TabLabelFunc(i), () => _active == idx, () => _active = idx));
            }
            b.PopContext();

            // The selected category's settings tree. Keys are settings PATHS under a per-tab prefix, so a
            // tab switch re-keys the content (tab focus survives) and expansion is remembered per tab.
            string tabKey = _active >= 0 && _active < Tabs.Length ? Tabs[_active].key : null;
            b.BeginStop("content");
            BuildTab(b, tabKey, "modset:" + tabKey + ":");

            // Two standing stops after the tree: reset THIS tab, and reset everything.
            b.BeginStop("resettab").AddItem(ControlId.Structural("modset:resettab"),
                GraphNodes.Button(
                    () => Message.Localized("settings", "reset.tab", new { name = ActiveTabLabel() }).Resolve(),
                    ResetActiveTab));
            b.BeginStop("resetall").AddItem(ControlId.Structural("modset:resetall"),
                GraphNodes.Button(
                    () => Message.Localized("settings", "reset.all").Resolve(),
                    ResetAllSettings));
        }

        private static System.Func<string> TabLabelFunc(int i) => () => L(Tabs[i].loc, Tabs[i].label);

        private void BuildTab(GraphBuilder b, string key, string k)
        {
            if (key == "input")
            {
                var bindings = ModSettings.Root.Get<CategorySetting>("bindings");
                if (bindings != null)
                    foreach (var s in bindings.Children) ModSettingNodes.Emit(b, s, k);
            }
            else if (key == "ui")
            {
                var annRoot = ModSettings.Root.Get<CategorySetting>("announcements");

                // Announcements: a verbosity preset + a plain on/off per announcement type — the 90%
                // case, first. The full per-type detail (suffix etc.) lives under Per-element overrides.
                if (annRoot != null)
                {
                    b.BeginGroup(ControlId.Structural(k + "announcements"),
                        GraphNodes.Group(() => L("ui.announcements", "Announcements")));
                    b.AddItem(ControlId.Structural(k + "verbosity"), BuildVerbosityDropdown(annRoot));
                    foreach (var child in annRoot.Children)
                    {
                        if (!(child is CategorySetting annCat) || annCat.Hidden) continue;
                        var enabled = annCat.Get<BoolSetting>("enabled");
                        if (enabled != null)
                        {
                            var en = enabled;
                            b.AddItem(ControlId.Structural(k + "ann." + annCat.Key),
                                GraphNodes.Toggle(() => annCat.Label, en.Get, () => en.Set(!en.Get())));
                        }
                    }
                    b.EndGroup();
                }

                // Per-element overrides, tucked at the bottom: the Global node carries each announcement
                // type's FULL settings (suffix punctuation etc.); then every element type's tri-state
                // inherit/on/off overrides, alphabetical.
                b.BeginGroup(ControlId.Structural(k + "overrides"),
                    GraphNodes.Group(() => L("ui.element_overrides", "Per-element overrides")));
                if (annRoot != null)
                {
                    b.BeginGroup(ControlId.Structural(k + "overrides.global"),
                        GraphNodes.Group(() => L("global.group", "Global")));
                    foreach (var s in annRoot.Children) ModSettingNodes.Emit(b, s, k + "g.");
                    b.EndGroup();
                }
                var ui = ModSettings.Root.Get<CategorySetting>("ui");
                if (ui != null)
                    foreach (var s in ui.Children.OrderBy(c => c.Label, System.StringComparer.CurrentCultureIgnoreCase))
                        ModSettingNodes.Emit(b, s, k + "o.");
                b.EndGroup();
            }
            else if (key == "overlays")
            {
                // Each overlay is a root group (Cursor + systems + Rename/Remove); the Add button at the
                // bottom appends one. All LIVE from the registry — an add/remove just renders differently.
                var overlays = ModSettings.Root.Get<CategorySetting>("overlays");
                foreach (var id in WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.OverlayIds())
                {
                    var oc = overlays?.Get<CategorySetting>(id);
                    if (oc != null) EmitOverlay(b, oc, id, k);
                }
                b.AddItem(ControlId.Structural(k + "add"),
                    GraphNodes.Button(() => L("overlay.add", "Add overlay"), () => AddOverlay(k)));
            }
            else if (key == "audio")
            {
                // Flat: master volume, then every system volume at the root (they're all just volumes —
                // no "System volumes" grouping; the storage paths under audio.volumes.* are unchanged).
                var audio = ModSettings.Root.Get<CategorySetting>("audio");
                if (audio != null)
                    foreach (var s in audio.Children)
                    {
                        if (s is CategorySetting volumes && volumes.Key == "volumes")
                            foreach (var v in volumes.Children) ModSettingNodes.Emit(b, v, k);
                        else
                            ModSettingNodes.Emit(b, s, k);
                    }
            }
            else if (key == "speech")
            {
                // The DEFAULT config (handler dropdown + each handler's subtree) first, then the advanced
                // "Additional speech configurations" group — each config a clone of the same schema with
                // Rename/Remove, and an Add button at the bottom. All live from the registry.
                var speech = ModSettings.Root.Get<CategorySetting>("speech");
                if (speech != null)
                    foreach (var s in speech.Children)
                    {
                        if (s is CategorySetting cs && cs.Key == "additional") continue; // rendered specially below
                        ModSettingNodes.Emit(b, s, k);
                    }

                b.BeginGroup(ControlId.Structural(k + "additional"),
                    GraphNodes.Group(() => L("speech.additional", "Additional speech configurations")));
                foreach (var id in WrathAccess.Speech.SpeechConfigRegistry.Ids())
                {
                    var cc = SpeechConfigCat(id);
                    if (cc != null) EmitSpeechConfig(b, cc, id, k);
                }
                b.AddItem(ControlId.Structural(k + "addconfig"),
                    GraphNodes.Button(() => L("speech.config.add", "Add speech configuration"), () => AddSpeechConfig(k)));
                b.EndGroup();
            }
            else if (key == "events")
            {
                // Two groups, both rendered generically: "Event settings" (per-source global defaults) and
                // "Individual event customization" (per-event, per-source, inheriting those globals).
                var events = ModSettings.Root.Get<CategorySetting>("events");
                if (events != null)
                    foreach (var s in events.Children) ModSettingNodes.Emit(b, s, k);
            }
            else if (key == "scanner")
            {
                // One unified system: per-entity-type settings (sound + announcements, mirroring the
                // taxonomy) under Entities, then the sonar's own tunables under Sonar.
                b.BeginGroup(ControlId.Structural(k + "entities"),
                    GraphNodes.Group(() => L("scanner.entities", "Entities")));

                // The global announcement base every entity inherits.
                var paRoot = ModSettings.Root.Get<CategorySetting>("proxy_announce");
                if (paRoot != null)
                {
                    b.BeginGroup(ControlId.Structural(k + "anndefaults"),
                        GraphNodes.Group(() => L("scanner.ann_defaults", "Announcement defaults")));
                    foreach (var p in paRoot.Children)
                    {
                        // One-toggle parts read flat; the spatial part (sub-toggles) keeps its subgroup.
                        if (p is CategorySetting pc && pc.Children.Count == 1
                            && pc.Get<BoolSetting>("enabled") is BoolSetting en)
                        {
                            var e = en;
                            var cat = pc;
                            b.AddItem(ControlId.Structural(k + "annd." + pc.Key),
                                GraphNodes.Toggle(() => cat.Label, e.Get, () => e.Set(!e.Get())));
                        }
                        else
                            ModSettingNodes.Emit(b, p, k + "annd.");
                    }
                    b.EndGroup();
                }

                foreach (var cat in WrathAccess.Exploration.ScanTaxonomy.Categories)
                    EmitEntityNode(b, cat, k + "ent.");
                // World-map entity types (separate taxonomy) share this tree — same sound dropdowns.
                foreach (var cat in WrathAccess.Exploration.GlobalMapTaxonomy.Categories)
                    EmitEntityNode(b, cat, k + "went.");
                b.EndGroup();

                var d = SystemDefaults("sonar");
                if (d != null && ModSettingNodes.HasVisibleLeaf(d))
                {
                    b.BeginGroup(ControlId.Structural(k + "sonar"),
                        GraphNodes.Group(() => L("category.sonar", "Sonar")));
                    foreach (var s in d.Children) ModSettingNodes.Emit(b, s, k + "son.");
                    b.EndGroup();
                }
            }
            else if (key == "log")
            {
                // The shared log message-type tree, configured once for all overlays — with a preset
                // dropdown up top (same idiom as the UI verbosity presets) for the 90% cases.
                var d = SystemDefaults("log");
                if (d != null)
                {
                    b.AddItem(ControlId.Structural(k + "preset"), BuildLogPresetDropdown(d));
                    foreach (var s in d.Children) ModSettingNodes.Emit(b, s, k);
                }
            }
            else if (key == "exploration")
            {
                // The Default overlay's cursor (mode + speed per slot) first, then the remaining shared
                // system defaults, one collapsible group per system (empty ones are skipped by Emit).
                var cursor = ModSettings.Root.Get<CategorySetting>("defaults")?.Get<CategorySetting>("cursor");
                if (cursor != null) ModSettingNodes.Emit(b, cursor, k);
                foreach (var sysKey in new[] { "grid", "spatial", "slope", "walltones", "object", "fog", "path" })
                {
                    var d = SystemDefaults(sysKey);
                    if (d != null) ModSettingNodes.Emit(b, d, k);
                }
            }
        }

        // Verbosity presets: each names the announcement types it turns OFF (everything else on).
        // The dropdown derives its state from the live toggles — hand-edits read as "Custom".
        private static readonly (string label, string loc, string[] off)[] VerbosityPresets =
        {
            ("Verbose", "preset.verbose", new string[0]),
            ("Standard", "preset.standard", new[] { "position" }),
            ("Concise", "preset.concise", new[] { "role", "tooltip", "position" }),
        };

        private static NodeVtable BuildVerbosityDropdown(CategorySetting annRoot)
        {
            var visible = annRoot.Children.OfType<CategorySetting>().Where(c => !c.Hidden)
                .Select(c => (Key: c.Key, Enabled: c.Get<BoolSetting>("enabled")))
                .Where(t => t.Enabled != null).ToList();

            var labels = VerbosityPresets.Select(p => L(p.loc, p.label)).ToList();
            labels.Add(L("preset.custom", "Custom"));

            int Current()
            {
                for (int i = 0; i < VerbosityPresets.Length; i++)
                {
                    var off = VerbosityPresets[i].off;
                    bool match = true;
                    foreach (var t in visible)
                        if (t.Enabled.Get() == System.Array.IndexOf(off, t.Key) >= 0) { match = false; break; }
                    if (match) return i;
                }
                return VerbosityPresets.Length; // Custom
            }

            void Apply(int idx)
            {
                if (idx < 0 || idx >= VerbosityPresets.Length) return; // choosing Custom = keep as-is
                var off = VerbosityPresets[idx].off;
                foreach (var t in visible)
                    t.Enabled.Set(System.Array.IndexOf(off, t.Key) < 0);
            }

            // "Custom" is a derived display state, not a choice — mark it virtual.
            return ModSettingNodes.ChoiceDropdown(L("ui.verbosity", "Verbosity"), labels, Current, Apply,
                selectableCount: VerbosityPresets.Length);
        }

        // Log presets — explicit NAMED sets (no automatic mode switching yet, user spec 2026-07-08):
        // - Default Realtime With Pause: combat + magic silenced (the events system speaks those),
        //   saving throws off, every other message type on.
        // - Default Turn Based: everything on.
        // - Nothing: everything off.
        // Same derived-state idiom as the verbosity dropdown: the current selection is computed from
        // the live toggles; hand-edits read back as "Custom". Only the on/off toggles are touched.
        private static NodeVtable BuildLogPresetDropdown(CategorySetting logRoot)
        {
            // Group key ("" = root-level, i.e. Other messages) -> toggle.
            var toggles = new List<KeyValuePair<string, BoolSetting>>();
            foreach (var s in logRoot.Children)
            {
                if (s is BoolSetting rb) toggles.Add(new KeyValuePair<string, BoolSetting>("", rb));
                else if (s is CategorySetting sub)
                    foreach (var c in sub.Children)
                        if (c is BoolSetting gb) toggles.Add(new KeyValuePair<string, BoolSetting>(sub.Key, gb));
            }

            bool RtwpValue(string group, BoolSetting t)
                => group != "combat" && group != "magic" && !(group == "checks" && t.Key == "saves");

            var labels = new List<string>
            {
                L("preset.default_rtwp", "Default Realtime With Pause"),
                L("preset.default_tb", "Default Turn Based"),
                L("preset.nothing", "Nothing"),
                L("preset.custom", "Custom"),
            };

            int Current()
            {
                bool rtwp = true, all = true, none = true;
                foreach (var t in toggles)
                {
                    bool v = t.Value.Get();
                    if (v != RtwpValue(t.Key, t.Value)) rtwp = false;
                    if (!v) all = false;
                    if (v) none = false;
                }
                return rtwp ? 0 : all ? 1 : none ? 2 : 3;
            }

            void Apply(int idx)
            {
                if (idx < 0 || idx > 2) return; // choosing Custom = keep as-is
                ModSettings.Batch(() =>
                {
                    foreach (var t in toggles)
                        t.Value.Set(idx == 0 ? RtwpValue(t.Key, t.Value) : idx == 1);
                });
            }

            return ModSettingNodes.ChoiceDropdown(L("log.preset", "Preset"), labels, Current, Apply,
                selectableCount: 3);
        }

        private static CategorySetting SystemDefaults(string key)
            => ModSettings.Root.Get<CategorySetting>("defaults")?.Get<CategorySetting>(key);

        // ---- overlays: live from the registry; Customize/Reset just render differently next frame ----

        // One overlay = a group (live name; Cursor + systems + Rename/Remove).
        private static void EmitOverlay(GraphBuilder b, CategorySetting oCat, string id, string k)
        {
            string ok = k + "ov." + id + ".";
            b.BeginGroup(ControlId.Structural(ok + "group"), GraphNodes.Group(
                () => WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.OverlayName(id)));
            foreach (var c in oCat.Children) // hidden "name" is skipped by Emit
            {
                if (c is CategorySetting sysCat
                    && WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.SystemName(sysCat.Key) != null)
                    EmitSystemNode(b, id, sysCat, ok);
                else
                    ModSettingNodes.Emit(b, c, ok);
            }
            b.AddItem(ControlId.Structural(ok + "rename"),
                GraphNodes.Button(() => L("overlay.rename", "Rename overlay"), () => RenameOverlay(id)));
            b.AddItem(ControlId.Structural(ok + "remove"),
                GraphNodes.Button(() => L("overlay.remove", "Remove overlay"), () => RemoveOverlay(id)));
            b.EndGroup();
        }

        // One system inside an overlay: play-mode dropdown + whole-subtree inheritance. "Following
        // defaults" shows just a Customize button (the tunables live on the Sonar/Log/Exploration tabs);
        // Customize materializes the overlay's own full copy (seeded from the current defaults) — which
        // simply RENDERS next frame; Reset drops it again. The group label carries the live state.
        private static void EmitSystemNode(GraphBuilder b, string id, CategorySetting sysCat, string ok)
        {
            var key = sysCat.Key;
            string sk = ok + key + ".";
            b.BeginGroup(ControlId.Structural(sk + "group"), GraphNodes.Group(
                () => L("system." + key, WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.SystemName(key))
                    + ", " + (WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.IsCustomized(id, key)
                        ? L("overlay.state_customized", "customized")
                        : L("overlay.state_default", "following defaults"))));

            var mode = sysCat.Get<ChoiceSetting>("mode"); // the play-mode dropdown (Off / When moving / Continuous)
            if (mode != null) ModSettingNodes.Emit(b, mode, sk);

            if (WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.IsCustomized(id, key))
            {
                var custom = sysCat.Get<CategorySetting>("custom");
                if (custom != null)
                    foreach (var c in custom.Children) ModSettingNodes.Emit(b, c, sk);
                b.AddItem(ControlId.Structural(sk + "reset"),
                    GraphNodes.Button(() => L("overlay.reset_defaults", "Reset to defaults"), () =>
                    {
                        WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.ResetSystem(id, key);
                        Tts.Speak(L("overlay.state_default", "following defaults"));
                        Navigation.FocusNode(ControlId.Structural(sk + "group"), announce: false);
                    }));
            }
            else
            {
                b.AddItem(ControlId.Structural(sk + "customize"),
                    GraphNodes.Button(() => L("overlay.customize", "Customize for this overlay"), () =>
                    {
                        WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.Customize(id, key);
                        Tts.Speak(L("overlay.state_customized", "customized"));
                        Navigation.FocusNode(ControlId.Structural(sk + "group"), announce: false);
                    }));
            }
            b.EndGroup();
        }

        private static void AddOverlay(string k)
        {
            var id = WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.Add();
            Tts.Speak(L("overlay.added", "Overlay added"));
            Navigation.FocusNode(ControlId.Structural(k + "ov." + id + ".group"));
        }

        private static void RemoveOverlay(string id)
        {
            if (!WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.Remove(id)) return;
            // The node vanishes next render; nearest-survivor reconciliation lands on the neighbor.
            Tts.Speak(L("overlay.removed", "Overlay removed"));
        }

        private static void RenameOverlay(string id)
        {
            var current = WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.OverlayName(id);
            ModTextEntryScreen.Open(L("overlay.rename", "Rename overlay"), current, name =>
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.SetOverlayName(id, name);
                Tts.Speak(L("overlay.renamed", "Renamed to") + " " + name); // group label reads live
            });
        }

        private string ActiveTabLabel()
            => _active >= 0 && _active < Tabs.Length ? L(Tabs[_active].loc, Tabs[_active].label) : "";

        // The settings subtrees each tab renders — what its Reset button restores. Overlays are special:
        // reset = remove every overlay (back to the empty default list); the shared system defaults they
        // followed live on the other tabs.
        private static string[] ResetRootsFor(string key)
        {
            switch (key)
            {
                case "audio": return new[] { "audio" };
                case "exploration": return new[]
                {
                    "defaults.cursor", "defaults.grid", "defaults.spatial", "defaults.slope",
                    "defaults.walltones", "defaults.object", "defaults.fog", "defaults.path",
                };
                case "input": return new[] { "bindings" };
                case "log": return new[] { "defaults.log" };
                case "scanner": return new[] { "defaults.sonar", "sounds", "proxy_announce", "proxy_elem" };
                case "speech": return new[] { "speech" };
                case "ui": return new[] { "announcements", "ui" };
                default: return new string[0];
            }
        }

        private void ResetActiveTab()
        {
            var key = _active >= 0 && _active < Tabs.Length ? Tabs[_active].key : null;
            if (key == null) return;
            ModSettings.Batch(() =>
            {
                if (key == "overlays") RemoveAllOverlays();
                else
                    foreach (var path in ResetRootsFor(key))
                        ModSettings.GetCategory(path)?.ResetToDefault();
            });
            Tts.Speak(Message.Localized("settings", "reset.tab_done", new { name = ActiveTabLabel() }).Resolve());
        }

        private void ResetAllSettings()
        {
            // The first-launch flag is internal bookkeeping, not a user setting — preserve it so resetting
            // doesn't re-trigger the setup wizard on the next main menu.
            bool wizardShown = ModSettings.GetSetting<BoolSetting>("wizard.completed")?.Get() ?? false;
            ModSettings.Batch(() =>
            {
                RemoveAllOverlays();
                foreach (var s in ModSettings.Root.Children) s.ResetToDefault();
                ModSettings.GetSetting<BoolSetting>("wizard.completed")?.Set(wizardShown);
            });
            Tts.Speak(Message.Localized("settings", "reset.all_done").Resolve());
        }

        private static void RemoveAllOverlays()
        {
            var ids = new List<string>(WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.OverlayIds());
            foreach (var id in ids) WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.Remove(id);
        }

        // ---- additional speech configs (same live-registry pattern as overlays) ----

        private static CategorySetting SpeechConfigCat(string id)
            => ModSettings.Root.Get<CategorySetting>("speech")?.Get<CategorySetting>("additional")?.Get<CategorySetting>(id);

        // One config = a group (the cloned handler/params schema; the hidden name is skipped) + Rename/Remove.
        private static void EmitSpeechConfig(GraphBuilder b, CategorySetting cc, string id, string k)
        {
            string ck = k + "cfg." + id + ".";
            b.BeginGroup(ControlId.Structural(ck + "group"), GraphNodes.Group(
                () => WrathAccess.Speech.SpeechConfigRegistry.Name(id)));
            foreach (var s in cc.Children) ModSettingNodes.Emit(b, s, ck); // the hidden "name" is skipped
            b.AddItem(ControlId.Structural(ck + "rename"),
                GraphNodes.Button(() => L("speech.config.rename", "Rename"), () => RenameSpeechConfig(id)));
            b.AddItem(ControlId.Structural(ck + "remove"),
                GraphNodes.Button(() => L("speech.config.remove", "Remove"), () => RemoveSpeechConfig(id)));
            b.EndGroup();
        }

        private static void AddSpeechConfig(string k)
        {
            var id = WrathAccess.Speech.SpeechConfigRegistry.Add();
            Tts.Speak(L("speech.config.added", "Speech configuration added"));
            Navigation.FocusNode(ControlId.Structural(k + "cfg." + id + ".group"));
        }

        private static void RemoveSpeechConfig(string id)
        {
            if (!WrathAccess.Speech.SpeechConfigRegistry.Remove(id)) return;
            Tts.Speak(L("speech.config.removed", "Speech configuration removed"));
        }

        private static void RenameSpeechConfig(string id)
        {
            var current = WrathAccess.Speech.SpeechConfigRegistry.Name(id);
            ModTextEntryScreen.Open(L("speech.config.rename", "Rename"), current, name =>
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                WrathAccess.Speech.SpeechConfigRegistry.SetName(id, name); // group label reads live
                Tts.Speak(L("overlay.renamed", "Renamed to") + " " + name);
            });
        }

        // One entity node of the Scanner tab's Entities tree: its sound dropdown + an Announcements
        // subgroup (the per-part tri-states), then its subcategories recursively. Mirrors ScanTaxonomy.
        private static void EmitEntityNode(GraphBuilder b, WrathAccess.Exploration.ScanTaxonomy.Node node, string k)
        {
            string nk = k + node.Key + ".";
            b.BeginGroup(ControlId.Structural(nk + "group"), GraphNodes.Group(() => L(node.LocKey, node.Label)));

            var sound = WrathAccess.Exploration.ScanSounds.SoundSetting(node.Key);
            if (sound is ChoiceSetting plainSound)
                b.AddItem(ControlId.Structural(nk + "sound"),
                    ModSettingNodes.ChoiceSettingDropdown(plainSound, L("scanner.sound", "Sound")));
            else if (sound is NullableChoiceSetting inheritSound)
                b.AddItem(ControlId.Structural(nk + "sound"),
                    ModSettingNodes.NullableChoiceDropdown(inheritSound, L("scanner.sound", "Sound")));

            var annCat = ModSettings.GetCategory("proxy_elem." + node.Key);
            if (annCat != null)
            {
                bool any = false;
                foreach (var c in annCat.Children) if (c is NullableBoolSetting) { any = true; break; }
                if (any)
                {
                    b.BeginGroup(ControlId.Structural(nk + "ann"),
                        GraphNodes.Group(() => L("scanner.announcements", "Announcements")));
                    foreach (var c in annCat.Children)
                        if (c is NullableBoolSetting nb)
                            b.AddItem(ControlId.Structural(nk + "ann." + nb.Key), ModSettingNodes.OverrideToggle(nb));
                    b.EndGroup();
                }
            }

            foreach (var child in node.Children) EmitEntityNode(b, child, nk);
            b.EndGroup();
        }

    }
}
