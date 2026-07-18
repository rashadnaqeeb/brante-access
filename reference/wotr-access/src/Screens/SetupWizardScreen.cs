using System;
using System.Collections.Generic;
using Kingmaker.UI; // UISoundType
using WrathAccess.Exploration; // ScanSounds, ScanTaxonomy (sonar include toggles)
using WrathAccess.Settings;
using WrathAccess.Speech;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// First-run setup wizard (and re-runnable later): walks a new player through the few high-impact,
    /// preference-driven choices in plain language, reusing the REAL settings controls so the menu stays
    /// the single source of truth. Graph-native (no longer on the <see cref="WizardScreen"/> shell, which
    /// remains for the game-VM wizards): a roadmap stop (one jump-target per active phase, each with a
    /// LIVE one-line summary of its current choice), the current step's content under the step title as
    /// context — each chunk (help text, option list, settings tree, test button) its OWN Tab-stop, like
    /// the old loose-element layout; content keys carry the step so a page turn re-keys the page — and
    /// Back/Next stops.
    /// Advancing plays the page-turn and lands focus on the new page's content (<c>FocusStop</c>). The
    /// phase set is dynamic — the engine-settings step drops out for a paramless engine, the voice steps
    /// appear only in positional mode. Mod-pushed (static open flag); Escape closes.
    /// </summary>
    public sealed class SetupWizardScreen : Screen
    {
        public SetupWizardScreen() { Wrap = true; }

        private enum Step { Backend, HandlerSettings, Navigation, WallTones, Sonar, EventFeedback, EnemyVoice, AllyVoice, UnitlessVoice }

        private static bool s_open;
        private static bool s_sonarInit;  // apply the sonar phase's recommended defaults once per wizard open
        private static Step s_step;

        public override string Key => "ctx.setupwizard";
        public override string ScreenName => Loc.T("screen.setup_wizard");
        public override int Layer => 36;       // above the mod menu (35); a modal over the main menu
        public override bool Exclusive => true; // owns the keyboard while open
        public override bool IsActive() => s_open;

        public static void Open() { s_open = true; s_sonarInit = false; s_step = Step.Backend; }
        private static void Close()
        {
            s_open = false;
            // First-run: remember the wizard's been shown so it won't auto-launch on future boots. Any
            // dismissal (Finish, backing off the first step, or Escape) counts; re-run from the Ctrl+M menu.
            ModSettings.GetSetting<BoolSetting>("wizard.completed")?.Set(true);
        }

        // Open landing goes to the page content, not the roadmap (the roadmap stays first in Tab
        // order). Declarative: an OnPush FocusStop request would be cleared by the attach that follows.
        public override object InitialFocusStop => "content";

        private static string TitleFor(Step step)
        {
            switch (step)
            {
                case Step.Backend: return Loc.T("wizard.speech.backend_title");
                case Step.HandlerSettings: return Loc.T("wizard.speech.settings_title", new { name = SelectedHandlerLabel() });
                case Step.Navigation: return Loc.T("wizard.nav.title");
                case Step.WallTones: return Loc.T("wizard.walltones.title");
                case Step.Sonar: return Loc.T("wizard.sonar.title");
                case Step.EventFeedback: return Loc.T("wizard.events.title");
                case Step.EnemyVoice: return Loc.T("wizard.events.enemy_voice_title");
                case Step.AllyVoice: return Loc.T("wizard.events.ally_voice_title");
                case Step.UnitlessVoice: return Loc.T("wizard.events.unitless_voice_title");
                default: return "";
            }
        }


        public override void Build(GraphBuilder b)
        {
            if (!s_open) return;

            // The roadmap: one jump-target per active phase, with a live summary of its current choice —
            // the "see what to revisit and jump straight back" path. Keys are the step names (stable), so
            // roadmap focus survives page turns; jumping moves focus to the content anyway.
            b.BeginStop("steps").PushContext(Loc.T("wizard.steps"), "list");
            foreach (var step in ActiveSteps())
            {
                var s = step; // capture for the live closures
                b.AddItem(ControlId.Structural("wiz:step:" + s),
                    GraphNodes.Tab(() => RoadmapLabel(s), () => s_step == s, () => JumpTo(s)));
            }
            b.PopContext();

            // The current page, under the step title as context; keys carry the step so a page turn
            // re-keys the whole page (GoTo/JumpTo land focus here via FocusStop).
            string k = "wiz:" + s_step + ":";
            b.BeginStop("content").PushContext(TitleFor(s_step));
            BuildContent(b, k);
            b.PopContext();

            // Footer: Back then Next/Finish (labels live).
            b.BeginStop("back").AddItem(ControlId.Structural("wiz:back"),
                GraphNodes.Button(() => Loc.T("wizard.back"), () => GoTo(-1)));
            b.BeginStop("next").AddItem(ControlId.Structural("wiz:next"),
                GraphNodes.Button(() => IsLastStep() ? Loc.T("wizard.finish") : Loc.T("wizard.next"), () => GoTo(+1)));
        }

        private void BuildContent(GraphBuilder b, string k)
        {
            switch (s_step)
            {
                case Step.Backend: BuildBackendStep(b, k); break;
                case Step.HandlerSettings: BuildHandlerSettingsStep(b, k); break;
                case Step.Navigation: BuildNavigationStep(b, k); break;
                case Step.WallTones: BuildWallTonesStep(b, k); break;
                case Step.Sonar: BuildSonarStep(b, k); break;
                case Step.EventFeedback: BuildEventFeedbackStep(b, k); break;
                case Step.EnemyVoice: BuildVoiceStep(b, k, EnemySlot, "wizard.events.enemy_voice_help"); break;
                case Step.AllyVoice: BuildVoiceStep(b, k, AllySlot, "wizard.events.ally_voice_help"); break;
                case Step.UnitlessVoice: BuildVoiceStep(b, k, UnitlessSlot, "wizard.events.unitless_voice_help"); break;
            }
        }

        private static string RoadmapLabel(Step step)
        {
            var title = TitleFor(step);
            var summary = SummaryFor(step);
            return string.IsNullOrEmpty(summary) ? title : title + ": " + summary;
        }

        private static string SummaryFor(Step step)
        {
            switch (step)
            {
                case Step.Backend: return SelectedHandlerLabel();
                case Step.Navigation:
                    var m = PrimaryMode();
                    return m == "continuous" ? Loc.T("wizard.nav.continuous")
                         : m == "tiled" ? Loc.T("wizard.nav.tiled") : "";
                case Step.EventFeedback: return Loc.T("wizard.events." + CurrentMode());
                default: return "";
            }
        }

        // ---- Step 1: choose the speech engine (select + sample) ----

        private static void BuildBackendStep(GraphBuilder b, string k)
        {
            b.AddItem(ControlId.Structural(k + "help"), GraphNodes.Text(() => Loc.T("wizard.speech.backend_help")));
            b.BeginStop(k + "options"); // the engine list is its own Tab-stop after the help text
            foreach (var h in SpeechManager.Handlers)
            {
                if (!CanUse(h)) continue; // only engines that actually load on this machine
                var handler = h;          // capture for the live closures
                b.AddItem(ControlId.Structural(k + "engine:" + h.Key),
                    GraphNodes.ChoiceOption(
                        () => handler.Label,
                        () => HandlerChoice()?.ValueId == handler.Key,
                        () => SelectBackend(handler.Key)));
            }
        }

        private static void SelectBackend(string key)
        {
            HandlerChoice()?.Set(key); // selects "like normal" — writes the default config's handler
            var h = SpeechManager.ResolveHandler(key);
            // The sample IS the feedback: interrupt purges the generic handler-changed confirm (and any
            // backlog from rapid switching), so you hear THIS engine demonstrate itself right where you
            // chose it. (Clipboard has no audio — its "sample" lands on the clipboard, which is its nature.)
            Tts.Speak(Loc.T("wizard.speech.sample", new { name = h?.Label ?? key }), interrupt: true);
        }

        // ---- Step 2: that engine's own settings (the real controls) ----

        private static void BuildHandlerSettingsStep(GraphBuilder b, string k)
        {
            var sub = SelectedHandlerParams();
            if (sub == null || sub.Children.Count == 0)
            {
                b.AddItem(ControlId.Structural(k + "help"), GraphNodes.Text(() => Loc.T("wizard.step_unavailable")));
                return;
            }
            foreach (var s in sub.Children) ModSettingNodes.Emit(b, s, k);
        }

        // ---- Step 3: exploration movement (one choice → several cursor settings) ----

        private static void BuildNavigationStep(GraphBuilder b, string k)
        {
            b.AddItem(ControlId.Structural(k + "help"), GraphNodes.Text(() => Loc.T("wizard.nav.help")));
            b.BeginStop(k + "options");
            b.AddItem(ControlId.Structural(k + "continuous"), GraphNodes.ChoiceOption(
                () => Loc.T("wizard.nav.continuous"), () => PrimaryMode() == "continuous", ApplyContinuous));
            b.AddItem(ControlId.Structural(k + "tiled"), GraphNodes.ChoiceOption(
                () => Loc.T("wizard.nav.tiled"), () => PrimaryMode() == "tiled", ApplyTiled));
        }

        // Continuous: both cursors glide. In-area — primary 15 ft/s, secondary 30 ft/s. World map — both
        // glide too, primary 8 mi/s, secondary 45 mi/s.
        private static void ApplyContinuous() => ModSettings.Batch(() =>
        {
            SetMode("primary", "continuous"); SetSpeed("primary", 15);
            SetMode("secondary", "continuous"); SetSpeed("secondary", 30);
            SetWorldMapMode("primary", "continuous"); SetWorldMapSpeed("primary", 8);
            SetWorldMapMode("secondary", "continuous"); SetWorldMapSpeed("secondary", 45);
        });

        // Tiled: primary steps a grid (speed unused), secondary glides. In-area — secondary 15 ft/s. World
        // map — primary steps a 2-mile grid, secondary glides at 8 mi/s.
        private static void ApplyTiled() => ModSettings.Batch(() =>
        {
            SetMode("primary", "tiled");
            SetMode("secondary", "continuous"); SetSpeed("secondary", 15);
            SetWorldMapMode("primary", "tiled"); SetWorldMapTileSize(2);
            SetWorldMapMode("secondary", "continuous"); SetWorldMapSpeed("secondary", 8);
        });

        // The Default overlay's cursor slots (defaults.cursor.<slot>, see OverlaySettingsRegistry): a mode
        // write re-resolves the Default overlay live and speed is read live, so this applies immediately and
        // persists. We touch only the Default overlay — any custom overlays are left alone.
        private static string PrimaryMode()
            => ModSettings.GetSetting<ChoiceSetting>("defaults.cursor.primary.mode")?.ValueId;

        private static void SetMode(string slot, string mode)
            => ModSettings.GetSetting<ChoiceSetting>("defaults.cursor." + slot + ".mode")?.Set(mode);

        private static void SetSpeed(string slot, int feet)
            => ModSettings.GetSetting<IntSetting>("defaults.cursor." + slot + ".speed")?.Set(feet);

        // The same Default-overlay cursor slots also carry the SEPARATE world-map cursor settings (read live
        // by GlobalMapCursor): movement type + glide speed in miles/sec; the tiled tile size lives on the
        // grid system. Writes apply immediately and persist, just like the in-area pair above.
        private static void SetWorldMapMode(string slot, string mode)
            => ModSettings.GetSetting<ChoiceSetting>("defaults.cursor." + slot + ".worldmap_mode")?.Set(mode);

        private static void SetWorldMapSpeed(string slot, int miles)
            => ModSettings.GetSetting<IntSetting>("defaults.cursor." + slot + ".worldmap_speed")?.Set(miles);

        private static void SetWorldMapTileSize(int miles)
            => ModSettings.GetSetting<IntSetting>("defaults.grid.worldmap_cell_size")?.Set(miles);

        // ---- Step: wall tones (a simplified subset of the Exploration wall-tones settings) ----

        // The real Default-overlay wall-tone settings, just the two that matter for a first pass: the mode
        // and the range (the "distance" out to which a wall is sounded). Bound live (same controls and
        // labels as the Exploration tab) — no preset, so the user simply turns it on and picks a distance.
        private static void BuildWallTonesStep(GraphBuilder b, string k)
        {
            b.AddItem(ControlId.Structural(k + "help"), GraphNodes.Text(() => Loc.T("wizard.walltones.help")));
            b.BeginStop(k + "options");
            var mode = ModSettings.GetSetting<ChoiceSetting>("defaults.walltones.mode");
            var range = ModSettings.GetSetting<IntSetting>("defaults.walltones.range");
            if (mode != null) ModSettingNodes.Emit(b, mode, k);
            if (range != null) ModSettingNodes.Emit(b, range, k);
        }

        // ---- Step: sonar (enable + which unit factions / the world map are swept) ----

        // The sonar's per-faction "include" is really its taxonomy node's sound (non-silent = swept), so each
        // include toggle flips the node between its default faction sound and Silent. Enabled / world-map are
        // the plain overlay-enable flags. We apply the recommended starting set ONCE on first entry (allies and
        // the world map off — they're the noisy ones; see the help text), then bind live so the user can tune.
        private void BuildSonarStep(GraphBuilder b, string k)
        {
            if (!s_sonarInit) { ApplySonarDefaults(); s_sonarInit = true; }
            b.AddItem(ControlId.Structural(k + "help"), GraphNodes.Text(() => Loc.T("wizard.sonar.help")));
            b.BeginStop(k + "options");
            b.AddItem(ControlId.Structural(k + "enabled"), SonarFlagToggle("wizard.sonar.enabled", "defaults.sonar.mode"));
            b.AddItem(ControlId.Structural(k + "enemy"), SonarIncludeToggle("wizard.sonar.enemy", ScanTaxonomy.UnitsEnemies));
            b.AddItem(ControlId.Structural(k + "neutral"), SonarIncludeToggle("wizard.sonar.neutral", ScanTaxonomy.UnitsNeutrals));
            b.AddItem(ControlId.Structural(k + "ally"), SonarIncludeToggle("wizard.sonar.ally", ScanTaxonomy.UnitsParty));
            b.AddItem(ControlId.Structural(k + "worldmap"), SonarFlagToggle("wizard.sonar.worldmap", "defaults.worldmap_sonar.mode"));
        }

        // The wizard's recommended sonar baseline: sonar on, enemies + neutrals swept, allies and world-map
        // locations off (those flood the densest scenes / the whole map).
        private static void ApplySonarDefaults() => ModSettings.Batch(() =>
        {
            ModSettings.GetSetting<ChoiceSetting>("defaults.sonar.mode")?.Set("continuous");
            SetSonarInclude(ScanTaxonomy.UnitsEnemies, true);
            SetSonarInclude(ScanTaxonomy.UnitsNeutrals, true);
            SetSonarInclude(ScanTaxonomy.UnitsParty, false);
            ModSettings.GetSetting<ChoiceSetting>("defaults.worldmap_sonar.mode")?.Set("off");
        });

        // A simplified on/off over the system's play mode (on => Continuous, off => Off); the full
        // Off / When moving / Continuous choice lives on the Exploration/Sonar tabs.
        private static NodeVtable SonarFlagToggle(string labelKey, string modePath)
        {
            var s = ModSettings.GetSetting<ChoiceSetting>(modePath);
            return GraphNodes.Toggle(() => Loc.T(labelKey),
                () => s != null && s.ValueId != "off",
                () => s?.Set(s.ValueId == "off" ? "continuous" : "off"));
        }

        // A faction-include toggle: included == the taxonomy node sounds (resolves to a non-silent stem).
        private static NodeVtable SonarIncludeToggle(string labelKey, string nodeKey)
            => GraphNodes.Toggle(() => Loc.T(labelKey),
                () => ScanSounds.Resolve(nodeKey) != null,
                () => SetSonarInclude(nodeKey, ScanSounds.Resolve(nodeKey) == null));

        // Turn a unit-faction node's sonar sound on (its default faction stem) or off (Silent).
        private static void SetSonarInclude(string nodeKey, bool on)
        {
            string id = on ? (ScanTaxonomy.Get(nodeKey)?.DefaultSound ?? ScanTaxonomy.Silent) : ScanTaxonomy.Silent;
            var setting = ScanSounds.SoundSetting(nodeKey);
            if (setting is ChoiceSetting c) c.Set(id);
            else if (setting is NullableChoiceSetting nc) nc.SetExplicit(id);
        }

        // ---- Step: event feedback (one mode choice → events + log + SAPI voices). "Events" not "combat":
        //      damage / healing / spellcasts etc. fire in AND out of combat. ----

        private const string EnemySlot = "wizard.enemy_config"; // persisted ids of the wizard's three SAPI configs
        private const string AllySlot = "wizard.ally_config";
        private const string UnitlessSlot = "wizard.unitless_config";
        // Every event source bucket the wizard drives. "unitless" = sourceless events (time passing, party-wide
        // notices); without it those fall through to the default voice. The positional preset gives it its own
        // voice (below); screen-reader / log modes set all four the same way via this list.
        private static readonly string[] SourceBuckets = { "party", "enemy", "neutral", "unitless" };

        private static void BuildEventFeedbackStep(GraphBuilder b, string k)
        {
            b.AddItem(ControlId.Structural(k + "help"), GraphNodes.Text(() => Loc.T("wizard.events.help")));
            b.BeginStop(k + "options");
            b.AddItem(ControlId.Structural(k + "positional"), GraphNodes.ChoiceOption(
                () => Loc.T("wizard.events.positional"), () => CurrentMode() == "positional", ApplyPositional));
            b.AddItem(ControlId.Structural(k + "screen_reader"), GraphNodes.ChoiceOption(
                () => Loc.T("wizard.events.screen_reader"), () => CurrentMode() == "screen_reader", ApplyScreenReader));
            b.AddItem(ControlId.Structural(k + "log"), GraphNodes.ChoiceOption(
                () => Loc.T("wizard.events.log"), () => CurrentMode() == "log", ApplyLog));
        }

        // Positional: events spoken spatially, with DISTINCT enemy vs ally voices for clarity; the duplicate
        // Combat + Magic game-log groups go off (the events cover them, incl. the spellcast events to come).
        // Neutrals share the ally voice (everything that isn't an enemy reads in the "ally" voice). Sourceless
        // (unitless) events get their own voice too — they have no map position, so they read NON-positionally
        // (the dispatcher never speaks a sourceless event spatially regardless), distinct from the unit voices.
        private static void ApplyPositional() => ModSettings.Batch(() =>
        {
            var enemy = EnsureConfig(EnemySlot, "wizard.events.enemy_config_name");
            var ally = EnsureConfig(AllySlot, "wizard.events.ally_config_name");
            var unitless = EnsureConfig(UnitlessSlot, "wizard.events.unitless_config_name");
            SetEventSource("enemy", enabled: true, config: enemy, positional: true);
            SetEventSource("party", enabled: true, config: ally, positional: true);
            SetEventSource("neutral", enabled: true, config: ally, positional: true);
            SetEventSource("unitless", enabled: true, config: unitless, positional: false);
            SetLogGroup("combat", false);
            SetLogGroup("magic", false);
        });

        // Screen reader: events through your normal (default) config, non-positional; Combat + Magic log
        // stays off (the events convey it, no need to double up).
        private static void ApplyScreenReader() => ModSettings.Batch(() =>
        {
            foreach (var b in SourceBuckets) SetEventSource(b, enabled: true, config: "default", positional: false);
            SetLogGroup("combat", false);
            SetLogGroup("magic", false);
        });

        // Game log: no event speech — the screen reader reads the game's own log instead, so Combat + Magic
        // log groups go on and the events go off (no duplication).
        private static void ApplyLog() => ModSettings.Batch(() =>
        {
            foreach (var b in SourceBuckets) SetEventSource(b, enabled: false, config: "default", positional: false);
            SetLogGroup("combat", true);
            SetLogGroup("magic", true);
        });

        // Which mode the live settings reflect (read off the enemy bucket) — a heuristic for the radio
        // "selected" marker + roadmap summary; the menu's per-source controls remain the source of truth.
        private static string CurrentMode()
        {
            var b = ModSettings.GetCategory("events.settings.enemy");
            if (!(b?.Get<BoolSetting>("enabled")?.Get() ?? true)) return "log";
            bool positional = b?.Get<BoolSetting>("positional")?.Get() ?? true;
            var cfg = b?.Get<ChoiceSetting>("speech_config")?.ValueId;
            return positional && !string.IsNullOrEmpty(cfg) && cfg != "default" ? "positional" : "screen_reader";
        }

        // Set one source bucket's global event output (the per-source defaults the events inherit).
        private static void SetEventSource(string bucket, bool enabled, string config, bool positional)
        {
            var b = ModSettings.GetCategory("events.settings." + bucket);
            b?.Get<BoolSetting>("enabled")?.Set(enabled);
            b?.Get<ChoiceSetting>("speech_config")?.Set(config);
            b?.Get<BoolSetting>("positional")?.Set(positional);
        }

        // Turn a whole log message group (combat / magic) on or off — the Default overlay's log defaults.
        private static void SetLogGroup(string group, bool on)
        {
            var cat = ModSettings.GetCategory("defaults.log." + group);
            if (cat == null) return;
            foreach (var child in cat.Children)
                if (child is BoolSetting b) b.Set(on);
        }

        // Reuse the slot's remembered SAPI config if it still exists, else create a fresh SAPI config and
        // remember its id (so re-runs reuse/re-tune the same enemy/ally voices, not pile up duplicates).
        private static string EnsureConfig(string slotPath, string nameKey)
        {
            var slot = ModSettings.GetSetting<StringSetting>(slotPath);
            var id = slot?.Get();
            if (!string.IsNullOrEmpty(id))
                foreach (var existing in SpeechConfigRegistry.Ids())
                    if (existing == id) return id;
            id = SpeechConfigRegistry.Add();
            SpeechConfigRegistry.SetName(id, Loc.T(nameKey));
            // An additional config's handler is a NullableChoiceSetting (inherits the default config).
            SpeechConfigRegistry.Get(id)?.Tree?.Get<NullableChoiceSetting>("handler")?.SetExplicit("sapi");
            slot?.Set(id);
            return id;
        }

        // ---- Steps: tune the enemy / ally / sourceless voices (each config's tree + a test line) ----

        private static void BuildVoiceStep(GraphBuilder b, string k, string slotPath, string helpKey)
        {
            b.AddItem(ControlId.Structural(k + "help"), GraphNodes.Text(() => Loc.T(helpKey)));
            var sapi = ConfigParams(slotPath);
            if (sapi != null)
            {
                b.BeginStop(k + "settings");
                foreach (var s in sapi.Children) ModSettingNodes.Emit(b, s, k);
            }
            b.BeginStop(k + "test").AddItem(ControlId.Structural(k + "test"),
                GraphNodes.Button(() => Loc.T("wizard.events.test"), () => TestVoice(slotPath)));
        }

        private static CategorySetting ConfigParams(string slotPath)
        {
            var id = ModSettings.GetSetting<StringSetting>(slotPath)?.Get();
            return string.IsNullOrEmpty(id) ? null : SpeechConfigRegistry.Get(id)?.Tree?.Get<CategorySetting>("sapi");
        }

        private static void TestVoice(string slotPath)
        {
            var id = ModSettings.GetSetting<StringSetting>(slotPath)?.Get();
            if (!string.IsNullOrEmpty(id)) SpeechConfigRegistry.Get(id)?.Speak(Loc.T("wizard.events.test_line"), interrupt: true);
        }

        // ---- navigation ----

        // The active step sequence (dynamic — the engine-settings step drops out for a paramless engine,
        // and future phases just append here).
        private static Step[] ActiveSteps()
        {
            var steps = new List<Step> { Step.Backend };
            if (HasHandlerSettings()) steps.Add(Step.HandlerSettings);
            steps.Add(Step.Navigation);
            steps.Add(Step.WallTones);
            steps.Add(Step.Sonar);
            steps.Add(Step.EventFeedback);
            // tune the three SAPI voices — enemy, ally, and the sourceless (unitless) voice
            if (CurrentMode() == "positional") { steps.Add(Step.EnemyVoice); steps.Add(Step.AllyVoice); steps.Add(Step.UnitlessVoice); }
            return steps.ToArray();
        }

        // Jump straight to a phase from the roadmap — any active phase, forward or back (the wizard's phases
        // are independent settings, no gating); this is the "go back and adjust later" path.
        private static void JumpTo(Step step)
        {
            if (s_step == step) return;
            s_step = step;
            UiSound.Play(UISoundType.BookPageTurn);
            Navigation.FocusStop("content"); // land on the new page (its keys carry the step)
        }

        // Step by ±1 through the active sequence; stepping off either end leaves the wizard.
        private static void GoTo(int delta)
        {
            var steps = ActiveSteps();
            int i = Array.IndexOf(steps, s_step) + delta;
            // Stepping forward off the end = Finish: play the same fanfare the chargen wizard plays on
            // completion. Backing off the front (i < 0) is a cancel, so no sound there.
            if (i >= steps.Length) { UiSound.Play(UISoundType.ChargenCompleteClick); Close(); return; }
            if (i < 0) { Close(); return; }
            s_step = steps[i];
            UiSound.Play(UISoundType.BookPageTurn);
            Navigation.FocusStop("content");
        }

        private static bool IsLastStep()
        {
            var steps = ActiveSteps();
            return Array.IndexOf(steps, s_step) == steps.Length - 1;
        }

        // Escape (ui.back) closes the wizard, like the mod menu.
        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => Close());
        }

        // ---- helpers ----

        private static ChoiceSetting HandlerChoice() => SpeechManager.Default?.Tree?.Get<ChoiceSetting>("handler");

        private static bool CanUse(ISpeechHandler h)
        {
            try { return h.Detect(); } catch { return false; }
        }

        // The concrete engine the default config resolves to (resolves "auto" to a real handler).
        private static string SelectedHandlerLabel()
            => SpeechManager.ResolveHandler(SpeechManager.Default?.HandlerKey)?.Label
               ?? HandlerChoice()?.Current?.Label ?? "";

        private static CategorySetting SelectedHandlerParams()
        {
            var h = SpeechManager.ResolveHandler(SpeechManager.Default?.HandlerKey);
            return h != null ? SpeechManager.Default?.Tree?.Get<CategorySetting>(h.Key) : null;
        }

        private static bool HasHandlerSettings()
        {
            var sub = SelectedHandlerParams();
            return sub != null && sub.Children.Count > 0;
        }
    }
}
