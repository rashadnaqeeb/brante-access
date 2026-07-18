using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Kingmaker;
using Kingmaker.GameModes; // GameModeType
using Kingmaker.Modding; // OwlcatModification (the game's native mod system)
using Kingmaker.PubSubSystem; // EventBus, INewServiceWindowUIHandler (service-window hotkeys)
using Kingmaker.UI.MVVM._VM.ServiceWindows; // ServiceWindowsType
using UnityEngine;
using WrathAccess.Audio;
using WrathAccess.Exploration.Overlays;
using WrathAccess.Input;
using WrathAccess.Screens;
using WrathAccess.UI; // NavDirection

namespace WrathAccess
{
    /// <summary>
    /// Entry point for the game's NATIVE mod system (Kingmaker.Modding — no Unity Mod Manager). The game
    /// loads every assembly under <c>Modifications/WrathAccess/Assemblies/</c> and invokes the
    /// <c>[OwlcatModificationEnterPoint]</c> method during boot (GameStarter, before the main menu),
    /// passing our <c>OwlcatModification</c>. Install = copy the mod folder + list "WrathAccess" in
    /// <c>OwlcatModificationManagerSettings.json</c> (+ prism.dll next to Wrath.exe) — pure file
    /// operations, no third-party installer. The native system has no per-frame hook, so we spawn our own
    /// persistent <see cref="Ticker"/> MonoBehaviour to drive the input/screen/overlay loops.
    /// </summary>
    public static class Main
    {
        public static ModLogger Log;

        /// <summary>Master switch, flipped from the game's Modifications window (OnSetEnabled).</summary>
        public static bool Enabled = true;

        /// <summary>The mod's install folder (assets live at its root, the DLL under Assemblies/).</summary>
        public static string ModDir { get; private set; }

        private static Harmony _harmony;

        [OwlcatModificationEnterPoint]
        public static void Load(OwlcatModification modification)
        {
            Log = new ModLogger();
            ModDir = modification.Path;
            // Wire the game's enable/disable plumbing to our master switch.
            modification.IsEnabled = () => Enabled;
            modification.OnSetEnabled = enabled =>
            {
                Enabled = enabled;
                if (!enabled) FocusMode.Set(false);
                Log.Log("WrathAccess " + (enabled ? "enabled" : "disabled"));
            };

            try
            {
                WrathAccess.Localization.LocalizationManager.Initialize(); // wire Message's resolver early
                _harmony = new Harmony("WrathAccess");
                _harmony.PatchAll(Assembly.GetExecutingAssembly());
                // Verify the pointer-cursor patch actually attached (this class of bug has bitten us before).
                var tick = HarmonyLib.AccessTools.Method(typeof(Kingmaker.Controllers.Clicks.PointerController), "Tick");
                var pinfo = tick != null ? Harmony.GetPatchInfo(tick) : null;
                Log.Log("[patch] PointerController.Tick found=" + (tick != null)
                    + " postfixes=" + (pinfo?.Postfixes?.Count ?? 0));
                RegisterInput();
                // Mod settings tree (the mod menu's tabs are its top-level categories). Built before load
                // so saved config (settings.json under the persistent data dir) overrides the defaults.
                BuildSettings();
                WrathAccess.Settings.ModSettings.Initialize(
                    System.IO.Path.Combine(UnityEngine.Application.persistentDataPath, "WrathAccess"));
                // Speech comes up AFTER settings load so the persisted handler/backend choices apply.
                WrathAccess.Speech.SpeechManager.Initialize();
                // Overlays are built AFTER load: the saved overlay-id list (incl. user-added ones) is only
                // known once settings have loaded, then their saved values are re-applied to the new subtrees.
                WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.BuildOverlays();
                // Build the saved additional speech configs + re-apply their values (same post-load pattern).
                WrathAccess.Speech.SpeechConfigRegistry.BuildConfigs();
                // Event settings (built after the config roster so the speech-config pickers list them).
                WrathAccess.Events.EventRegistry.RegisterDefaults();
                ScreenManager.Initialize();
                // Game-log reading (barks, rolls, loot, …) lives in the overlays' Log system — see
                // Overlays.LogSystem (per-overlay, per-message-type toggles) fed by the LogFeed Harmony tap.
                WarningReader.Initialize(); // speak the game's "can't do that" warnings (refusal reasons)
                WrathAccess.Events.EventBusAdapter.Initialize(); // turn game damage/buff events into mod events
                DialogVisibility.Initialize(); // track when the dialogue window is actually shown/clickable

                // The native mod system has no per-frame callback — drive our loops from our own
                // persistent MonoBehaviour (survives scene loads via DontDestroyOnLoad).
                var ticker = new GameObject("WrathAccess.Ticker");
                UnityEngine.Object.DontDestroyOnLoad(ticker);
                ticker.AddComponent<Ticker>();
                // The virtual audio head (+10000: its LateUpdate must land AFTER the game's camera
                // snap so the listener override wins while active).
                ticker.AddComponent<WrathAccess.Exploration.ListenerAnchor>();

#if DEBUG
                // Dev-only in-process HTTP driver (eval/speech/health), gated again behind WRATHACCESS_DEV=1.
                // Compiled out entirely in Release — see src/Dev/.
                WrathAccess.Dev.DevServer.Instance.Start();
#endif

                Log.Log("WrathAccess initialized. " + BuildStamp());
                Tts.Speak(Loc.T("app.loaded"));
            }
            catch (Exception e)
            {
                Log.Error("Initialization failed: " + e);
            }
        }

        // The loaded DLL's build time + path, logged at startup so we can confirm from Player.log which
        // build is actually running (the game loads the assembly at boot — code changes need a restart).
        private static string BuildStamp()
        {
            try
            {
                var path = Assembly.GetExecutingAssembly().Location;
                return "Build " + System.IO.File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm:ss") + " (" + path + ")";
            }
            catch { return "Build ?"; }
        }

        /// <summary>Our per-frame driver (the native mod system has no update hook of its own). The
        /// negative execution order runs us BEFORE the game's scripts each frame — UMM's dispatcher got
        /// that position implicitly by being created at injection time, before any game object existed;
        /// created mid-boot we'd otherwise run after them, adding a frame of input latency.</summary>
        [DefaultExecutionOrder(-10000)]
        private sealed class Ticker : MonoBehaviour
        {
            private void Update() { try { OnFrame(); } catch (Exception e) { Log?.Error("[tick] " + e); } }
        }

        // Focus mode starts ON (no hotkey ritual every launch) — but the game's Keyboard doesn't exist
        // yet at our entry point (GameStarter), so engage on the first frame it does. One-shot: a later
        // manual toggle-off stays off.
        private static bool _bootFocusPending = true;

        // Auto-open the setup wizard once when we first reach the main menu, but only until the user has
        // been through it once (gated on the persisted wizard.completed flag). Per-launch one-shot.
        private static bool _setupWizardPending = true;

        private static bool WizardAlreadyShown()
            => WrathAccess.Settings.ModSettings.GetSetting<WrathAccess.Settings.BoolSetting>("wizard.completed")?.Get() ?? false;

        private static void OnFrame()
        {
#if DEBUG
            // Run queued dev-eval jobs on the main thread even when the mod is toggled off, so the dev
            // driver stays usable. Inert unless WRATHACCESS_DEV=1.
            WrathAccess.Dev.DevServer.Instance.Pump();
#endif
            if (!Enabled) return;
            if (_bootFocusPending && Game.Instance?.Keyboard != null)
            {
                _bootFocusPending = false;
                FocusMode.Set(true); // silent: the first screen announcement signals we're live
            }
            WrathAccess.Localization.LocalizationManager.Tick(); // pick up a live game-language swap
            FocusMode.Tick(); // re-acquire the hotkey-suppression scope if the game rebuilt its keyboard
            WrathAccess.Audio.WwiseAudio.Tick(); // generate+load our Wwise bank once the engine is up
            InputManager.Tick();
            // First-run experience: the instant we reach the main menu (after the boot "loaded" readout),
            // open the setup wizard — but only until it's been shown once (wizard.completed, set on dismissal).
            // Done BEFORE ScreenManager.Tick so the wizard lands on top in the SAME frame and the menu (and its
            // Continue button) is never read out first; gated on the game's IsMainMenu rather than our menu
            // screen being Current (which would already have announced).
            if (_setupWizardPending && !WizardAlreadyShown() && (Game.Instance?.RootUiContext?.IsMainMenu ?? false))
            {
                _setupWizardPending = false;
                WrathAccess.Screens.SetupWizardScreen.Open();
            }
            ScreenManager.Tick();
            WrathAccess.UI.Navigation.TickTypeahead(); // typed letters → type-ahead search (after dispatch)
            TickPause(); // announce the game's pause state whenever it changes (ours OR the game's own)
            TickControl(); // chime when a cutscene/scripted event takes or returns control of the party
            WrathAccess.Exploration.CombatMode.TickTurn(); // announce whose turn it is in turn-based combat
            WrathAccess.Exploration.WorldModel.Tick(); // refresh the area entity registry before consumers read it
            WrathAccess.Exploration.FogExplored.Tick(UnityEngine.Time.unscaledDeltaTime); // read the game's explored layer (fog G channel)
            WrathAccess.Exploration.RoomMap.Tick(); // (re)build the room segmentation on area-part change
            WrathAccess.Exploration.Targeting.Tick(); // per-frame upkeep for targeting modes (e.g. finish a pending rest)
            // Unscaled delta: the cursor is a real-time UI element — it must keep moving while the game is
            // paused (the game-scaled dt is 0 when paused, which froze continuous-mode movement).
            // Ticks the active overlay: movement modes (glide) update the cursor, then systems (sonar,
            // wall tones, fog/object cues) read the fresh position.
            OverlayManager.Tick(UnityEngine.Time.unscaledDeltaTime); // also drives the world-map sonar (a WorldMap overlay system)
            WrathAccess.Exploration.GlobalMapCursor.Tick(UnityEngine.Time.unscaledDeltaTime); // world-map cursor; gated on the engaged overlay
            WrathAccess.Audio.SpatialSources.Tick(); // re-spatialise live one-shots against the cursors just moved above
            WrathAccess.Events.EventBusAdapter.Tick(); // reconcile this frame's buff churn into gain/loss events
            WrathAccess.Events.EventDispatcher.Tick(); // flush this frame's queued events (damage, buffs, room changes)
        }

        // Space's exploration job: toggle the game's pause. Only when focus mode owns the keyboard AND
        // we're in plain exploration — focus off lets the game's own Space pause, and on a UI screen the
        // navigator consumed Space for the tooltip (so this never fires there). Uses the game's own
        // PauseBind so combat/global-map are handled exactly as the native binding does.
        private static void TogglePauseIfExploring()
        {
            if (!FocusMode.Active) return;
            var current = ScreenManager.Current;
            if (current == null || current.Key != "ctx.ingame") return;
            if (WrathAccess.UI.Navigation.HasFocus) return; // focused in the HUD → Space is a UI key, not pause
            // Just toggle; TickPause announces the resulting state change (PauseBind is async, and the game
            // also pauses on its own — e.g. combat start — so we react to the state, not this keypress).
            Game.Instance?.PauseBind();
        }

        // Announce pause/unpause whenever the game's real pause state flips during gameplay — catching the
        // game's own auto-pauses (e.g. combat start), not just our Space toggle. Gated on the active screen
        // being a play context, so menus/dialogue that auto-pause don't redundantly say "Paused" (they
        // announce themselves). Nullable baseline: don't fire on the first sample, and reset out of play so
        // re-entry is silent.
        private static bool? _wasPaused;
        private static void TickPause()
        {
            var game = Game.Instance;
            var key = ScreenManager.Current?.Key;
            bool inPlay = game != null
                && (key == "ctx.ingame" || key == "ctx.tacticalcombat" || key == "ctx.globalmap");
            if (!inPlay) { _wasPaused = null; return; }
            bool paused = game.IsPaused;
            if (_wasPaused.HasValue && paused != _wasPaused.Value)
                Tts.Speak(Loc.T(paused ? "pause.paused" : "pause.unpaused"));
            _wasPaused = paused;
        }

        // Chime when control of the party is lost to / regained from a cutscene or scripted event. The
        // authoritative signal is Game.Instance.CutsceneLock.Active (set by the cutscene "Lock Controls"
        // command) — the same flag the game uses to hide the HUD and stop camera-follow. Gated to being in a
        // local area (menus leave the lock false, so opening windows never chimes); nullable baseline so we
        // don't chime on the first sample or across area loads (entering mid-intro-cutscene chimes "gained"
        // only when it ends).
        private static bool _hadControl = true;   // last CHIMED state; default in control, so losing it (intro cutscene) chimes
        private static bool _pendingControl = true;
        private static float _pendingSince;
        private const float ChimeDebounceSeconds = 0.5f; // a state must hold this long before we chime it
        private static void TickControl()
        {
            var game = Game.Instance;
            if (game == null || game.RootUiContext == null || !game.RootUiContext.IsInGame) { _hadControl = true; _pendingControl = true; return; }
            bool hasControl = ControlState.HasControl; // single source of truth — see ControlState
            float now = UnityEngine.Time.unscaledTime;

            // Debounce the chime: only act on a state held for ChimeDebounceSeconds, so the brief
            // ClickEventsController blips between cutscene beats don't replay the chime (the audio stutter).
            if (hasControl != _pendingControl) { _pendingControl = hasControl; _pendingSince = now; }
            if (_hadControl != _pendingControl && now - _pendingSince >= ChimeDebounceSeconds)
            {
                WrathAccess.Audio.AudioEngines.NAudio.Play2D(System.IO.Path.Combine(OverlayAudio.Dir, _pendingControl ? "control_gained.wav" : "control_lost.wav"), OverlayAudio.Master);
                _hadControl = _pendingControl;
            }
        }

        // Game modes where a quick-save is sensible — normal play, paused, world/global map, kingdom, and
        // service/fullscreen windows. Not dialogue, cutscenes, rest, turn-based combat, loading, etc. This
        // approximates the game's own save-allowed set without reproducing its keybinding registration.
        private static readonly HashSet<GameModeType> QuickSaveModes = new HashSet<GameModeType>
        {
            GameModeType.Default, GameModeType.Pause, GameModeType.GlobalMap,
            GameModeType.Kingdom, GameModeType.KingdomSettlement, GameModeType.FullScreenUi,
        };

        // F5 quick-save. Only while focus mode owns the keyboard: F5 is the game's own quick-save key, but
        // focus mode suppresses the game's keyboard, so we stand in for it here; with focus off the game's
        // native F5 saves and ours must not fire (no double-save). Calls the game's own MakeQuickSave.
        private static void QuickSave()
        {
            if (!FocusMode.Active) return;
            var game = Game.Instance;
            if (game == null) return;
            if (!QuickSaveModes.Contains(game.CurrentMode)) { Tts.Speak(Loc.T("save.cant")); return; }
            try { game.MakeQuickSave(); Tts.Speak(Loc.T("save.saving")); }
            catch (Exception e) { Log.Error("[quicksave] " + e); Tts.Speak(Loc.T("save.failed")); }
        }

        // Open (toggle) a service window the game's own way: the EventBus reaches whichever ServiceWindowsVM
        // is live — the in-area one (InGameVM) or the world-map one (GlobalMapVM) — so one call works in both.
        private static void OpenWindow(ServiceWindowsType type)
            => EventBus.RaiseEvent(delegate(INewServiceWindowUIHandler h) { h.HandleOpenWindowOfType(type); });

        // Flip the game's real-time-with-pause <-> turn-based combat mode. We only flip the EnableTurnBasedMode
        // setting — the game's GameSettingsController hooks its OnValueChanged and applies every state change
        // (combat mode switch, etc.), so we mirror the game's own toggle exactly rather than driving state.
        private static void ToggleCombatMode()
        {
            var rc = Game.Instance?.RootUiContext;
            if (rc == null || !rc.IsInGame) return; // only in a loaded area (where combat happens)
            var setting = Kingmaker.Settings.SettingsRoot.Game.TurnBased.EnableTurnBasedMode;
            bool nowTurnBased = !setting; // the mode we're switching TO
            // The game's own mode-change cue for the target mode, then flip (it confirms + propagates).
            UiSound.Play(nowTurnBased ? Kingmaker.UI.UISoundType.ChangeModeTBM : Kingmaker.UI.UISoundType.ChangeModeRTWP);
            setting.SetValueAndConfirm(nowTurnBased);
            Tts.Speak(Message.Localized("ui", nowTurnBased ? "combat.mode_turn_based" : "combat.mode_real_time").Resolve());
        }

        /// <summary>
        /// Temporary proof-of-life bindings for the input/speech/suppression slice.
        /// These get replaced by real navigation + a rebindable action set.
        /// </summary>
        // The mod settings tree (its top-level categories become the mod menu's tabs): Input — every
        // input action as a rebindable BindingSetting — and UI — announcement-verbosity toggles the
        // AnnouncementComposer consults live (off → that part is no longer spoken anywhere).
        private static void BuildSettings()
        {
            var bindings = new WrathAccess.Settings.CategorySetting("bindings", "Input", localizationKey: "category.input");
            // One collapsible group per input category - the same chord may legitimately appear in two
            // groups (stack-order shadowing resolves it); duplicates are only prevented WITHIN a group.
            var inputCats = new[]
            {
                (InputCategory.Global, "global", "Global"),
                (InputCategory.UI, "ui", "Menus and UI"),
                (InputCategory.Exploration, "explore", "Exploration"),
                (InputCategory.InGame, "ingame", "In game"),
                (InputCategory.WorldMap, "worldmap", "World map"),
                (InputCategory.Windows, "windows", "Service windows"),
            };
            foreach (var (cat, key, label) in inputCats)
            {
                var catGroup = new WrathAccess.Settings.CategorySetting(key, label, localizationKey: "input." + key);
                // Ungrouped actions sit at the category root; grouped ones nest in named sub-trees
                // (cursor / party / combat / scanner / overlays). Display order: the sub-trees first
                // (alphabetical by localized label), then the root rebind rows (alphabetical too).
                var leaves = new System.Collections.Generic.List<WrathAccess.Settings.BindingSetting>();
                var subGroups = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<WrathAccess.Settings.BindingSetting>>();
                foreach (var a in InputManager.Actions)
                {
                    if (a.Category != cat) continue;
                    var row = new WrathAccess.Settings.BindingSetting(a);
                    if (a.Group == null) { leaves.Add(row); continue; }
                    if (!subGroups.TryGetValue(a.Group, out var rows))
                        subGroups[a.Group] = rows = new System.Collections.Generic.List<WrathAccess.Settings.BindingSetting>();
                    rows.Add(row);
                }
                foreach (var g in System.Linq.Enumerable.OrderBy(subGroups.Keys,
                    k => WrathAccess.Localization.LocalizationManager.GetOrDefault("settings", "input.group." + k, k),
                    StringComparer.CurrentCultureIgnoreCase))
                {
                    var sub = new WrathAccess.Settings.CategorySetting(g, g, localizationKey: "input.group." + g);
                    foreach (var b in System.Linq.Enumerable.OrderBy(subGroups[g], r => r.Label, StringComparer.CurrentCultureIgnoreCase))
                        sub.Add(b);
                    catGroup.Add(sub);
                }
                foreach (var b in System.Linq.Enumerable.OrderBy(leaves, r => r.Label, StringComparer.CurrentCultureIgnoreCase))
                    catGroup.Add(b);
                bindings.Add(catGroup);
            }
            WrathAccess.Settings.ModSettings.Root.Add(bindings);

            // UI = per-announcement settings (global toggles) + per-element-type overrides, discovered
            // by reflection. Creates "announcements" + "ui" categories under the settings Root.
            WrathAccess.UI.Announcements.AnnouncementRegistry.RegisterDefaults();
            // The graph announcer consults the same announcement settings (per control type + per kind).
            WrathAccess.UI.Graph.GraphAnnouncer.PartFilter = (type, part) =>
                WrathAccess.UI.Announcements.AnnouncementRegistry.PartEnabled(type?.Key, part.Kind);
            // Group headers speak their expanded/collapsed state (localized; same words the old tree used).
            WrathAccess.UI.Graph.GraphAnnouncer.ExpandedStateText = expanded =>
                Loc.T(expanded ? "role.expanded" : "role.collapsed");
            WrathAccess.UI.Graph.GraphAnnouncer.PositionText = (index, count) =>
                Loc.T("nav.position", new { index, count });

            // Scan-item (proxy) announcements: their own parallel pipeline (NOT UI elements) — global
            // per-part toggles + per-proxy-type overrides. Creates "proxy_announce" + "proxy_elem".
            WrathAccess.Exploration.Announce.ScanAnnounceRegistry.RegisterDefaults();

            // Audio = settings-wide master volume (every overlay sound system scales by it).
            var audio = new WrathAccess.Settings.CategorySetting("audio", "Audio", localizationKey: "category.audio");
            audio.Add(new WrathAccess.Settings.IntSetting("master_volume", "Master volume", 100, 0, 100, 5, "audio.master_volume"));
            WrathAccess.Settings.ModSettings.Root.Add(audio);

            // Internal wizard state (hidden): the ids of the three speech configs the setup wizard created for
            // positional events (a distinct enemy voice + ally voice + sourceless/unitless voice), so a re-run
            // reuses/re-tunes them instead of piling up duplicates.
            var wizard = new WrathAccess.Settings.CategorySetting("wizard", "Wizard", localizationKey: "category.wizard");
            wizard.Add(new WrathAccess.Settings.StringSetting("enemy_config", "Enemy event speech config", "", "wizard.enemy_config") { Hidden = true });
            wizard.Add(new WrathAccess.Settings.StringSetting("ally_config", "Ally event speech config", "", "wizard.ally_config") { Hidden = true });
            wizard.Add(new WrathAccess.Settings.StringSetting("unitless_config", "Sourceless event speech config", "", "wizard.unitless_config") { Hidden = true });
            // First-run flag: set when the user dismisses the wizard; gates the launch auto-open below.
            wizard.Add(new WrathAccess.Settings.BoolSetting("completed", "Setup wizard shown", false, "wizard.completed") { Hidden = true });
            WrathAccess.Settings.ModSettings.Root.Add(wizard);
            // The shared sonar-sound taxonomy (global, like the volumes): per-node sound picks the
            // sonar/object cues resolve live. Shown on the Sonar tab.
            WrathAccess.Exploration.ScanSounds.RegisterSettings();

            // Overlays = the data-driven area-overlay configs (composition per overlay + shared defaults); also
            // builds the live Overlay objects and installs them in OverlayManager.
            WrathAccess.Exploration.Overlays.OverlaySettingsRegistry.Register();

            // Speech tab: the handler dropdown (Prism / SAPI / Clipboard, auto by default) + each
            // handler's own settings subtree, all rendered by the settings treeview.
            var speech = new WrathAccess.Settings.CategorySetting("speech", "Speech", localizationKey: "category.speech");
            WrathAccess.Speech.SpeechManager.RegisterSettings(speech);
            WrathAccess.Settings.ModSettings.Root.Add(speech);
            // The additional-speech-config roster (advanced; the events system speaks through these).
            // Pre-load like overlays: create the id list now so Load restores it; subtrees built after.
            WrathAccess.Speech.SpeechConfigRegistry.Register();
        }

        private static void RegisterInput()
        {
            // ---- Global: always live, even when focus mode is off (handlers self-gate as needed) ----
            InputManager.Register("toggle_focus", "Toggle focus mode", InputCategory.Global, () =>
            {
                FocusMode.Toggle();
                Tts.Speak(Loc.T(FocusMode.Active ? "focus.on" : "focus.off"));
                if (FocusMode.Active) WrathAccess.UI.Navigation.AnnounceCurrent();
            }).AddBinding(KeyCode.A, ctrl: true, shift: true);

            InputManager.Register("speak_test", "Speak test", InputCategory.Global, () =>
                Tts.Speak("Wrath Access input and speech are working."))
                .AddBinding(KeyCode.T, ctrl: true, shift: true);

            // Quick save (the game's own MakeQuickSave). Self-gates to focus mode + a save-allowed mode.
            InputManager.Register("game.quickSave", "Quick save", InputCategory.Global, QuickSave)
                .AddBinding(KeyCode.F5);

            // Mod menu - Ctrl+M, available everywhere (fires in either focus mode). The setup wizard is
            // reached from here (and auto-runs on first launch); no dedicated hotkey.
            InputManager.Register("mod.menu", "Open mod menu", InputCategory.Global,
                () => WrathAccess.Screens.ModMenuScreen.Toggle()).AddBinding(KeyCode.M, ctrl: true);

            // Panic recovery: force speech back to Prism (best backend) from ANYWHERE, so a blind player who
            // switched to a broken engine/backend and went silent can always get a voice back without needing
            // to navigate a menu they can't hear.
            InputManager.Register("speech.resetPrism", "Reset speech to Prism", InputCategory.Global,
                () => WrathAccess.Speech.SpeechManager.ResetToPrism()).AddBinding(KeyCode.F8);

            // ---- UI: screen/menu navigation (dispatched into the active navigator) ----
            InputManager.Register("ui.up", "Navigate up", InputCategory.UI).AddBinding(KeyCode.UpArrow).Repeating();
            InputManager.Register("ui.down", "Navigate down", InputCategory.UI).AddBinding(KeyCode.DownArrow).Repeating();
            InputManager.Register("ui.left", "Navigate left", InputCategory.UI).AddBinding(KeyCode.LeftArrow).Repeating();
            InputManager.Register("ui.right", "Navigate right", InputCategory.UI).AddBinding(KeyCode.RightArrow).Repeating();
            InputManager.Register("ui.next", "Next region (Tab)", InputCategory.UI).AddBinding(KeyCode.Tab).Repeating();
            InputManager.Register("ui.prev", "Previous region (Shift+Tab)", InputCategory.UI).AddBinding(KeyCode.Tab, shift: true).Repeating();
            InputManager.Register("ui.activate", "Activate control", InputCategory.UI)
                .AddBinding(KeyCode.Return).AddBinding(KeyCode.KeypadEnter);
            InputManager.Register("ui.secondary", "Secondary action", InputCategory.UI).AddBinding(KeyCode.Backspace);
            InputManager.Register("ui.back", "Back / close", InputCategory.UI).AddBinding(KeyCode.Escape);
            // Space + F1 (one action, two bindings): while a type-ahead search is live, Space extends
            // the search buffer (the SPACE key is reserved, not the action), so F1 still reads tooltips.
            InputManager.Register("ui.tooltip", "Read tooltip", InputCategory.UI)
                .AddBinding(KeyCode.Space).AddBinding(KeyCode.F1);
            // Home/End jump to the first/last item: a list's ends, a tree's current depth's ends, a
            // FlowSheet's very first/last cell regardless of region (and a live search's first/last hit).
            InputManager.Register("ui.home", "Jump to first item", InputCategory.UI).AddBinding(KeyCode.Home);
            InputManager.Register("ui.end", "Jump to last item", InputCategory.UI).AddBinding(KeyCode.End);
            // Ctrl+Up/Down jump between regions of a FlowSheet (handled only when focus is inside one).
            InputManager.Register("ui.regionPrev", "Previous sheet region", InputCategory.UI)
                .AddBinding(KeyCode.UpArrow, ctrl: true).Repeating();
            InputManager.Register("ui.regionNext", "Next sheet region", InputCategory.UI)
                .AddBinding(KeyCode.DownArrow, ctrl: true).Repeating();

            // ---- Exploration: the in-game world (live in ctx.ingame; shared chords - arrows, Enter,
            // Space, Escape, Backspace - are won by the HUD while it's focused, by these while not) ----
            // The cursor arrows have NO press handlers: movement modes POLL the held keys each frame as
            // one combined vector (CursorKeys), so held diagonals move diagonally instead of zigzagging.
            InputManager.Register("explore.cursorUp", "Move cursor up", InputCategory.Exploration)
                .AddBinding(KeyCode.UpArrow).AddBinding(KeyCode.W).Repeating().Grouped("cursor");
            InputManager.Register("explore.cursorDown", "Move cursor down", InputCategory.Exploration)
                .AddBinding(KeyCode.DownArrow).AddBinding(KeyCode.S).Repeating().Grouped("cursor");
            InputManager.Register("explore.cursorLeft", "Move cursor left", InputCategory.Exploration)
                .AddBinding(KeyCode.LeftArrow).AddBinding(KeyCode.A).Repeating().Grouped("cursor");
            InputManager.Register("explore.cursorRight", "Move cursor right", InputCategory.Exploration)
                .AddBinding(KeyCode.RightArrow).AddBinding(KeyCode.D).Repeating().Grouped("cursor");
            InputManager.Register("explore.secondaryUp", "Secondary cursor up", InputCategory.Exploration)
                .AddBinding(KeyCode.UpArrow, shift: true).AddBinding(KeyCode.W, shift: true).Grouped("cursor");
            InputManager.Register("explore.secondaryDown", "Secondary cursor down", InputCategory.Exploration)
                .AddBinding(KeyCode.DownArrow, shift: true).AddBinding(KeyCode.S, shift: true).Grouped("cursor");
            InputManager.Register("explore.secondaryLeft", "Secondary cursor left", InputCategory.Exploration)
                .AddBinding(KeyCode.LeftArrow, shift: true).AddBinding(KeyCode.A, shift: true).Grouped("cursor");
            InputManager.Register("explore.secondaryRight", "Secondary cursor right", InputCategory.Exploration)
                .AddBinding(KeyCode.RightArrow, shift: true).AddBinding(KeyCode.D, shift: true).Grouped("cursor");
            // Our "left click": interact with the thing under the cursor.
            InputManager.Register("explore.interact", "Interact at cursor", InputCategory.Exploration,
                WrathAccess.Exploration.Scanner.InteractAtCursor)
                .AddBinding(KeyCode.Return).AddBinding(KeyCode.KeypadEnter);
            // The game's pause toggle, matching its own Space binding. (A move queued while paused only
            // walks once unpaused.)
            InputManager.Register("explore.pause", "Toggle pause", InputCategory.Exploration,
                TogglePauseIfExploring).AddBinding(KeyCode.Space);
            // Y: "where am I" — the location's name (current section when it has one), indoors, and the
            // leader's compass region within the section's map bounds.
            InputManager.Register("explore.whereAmI", "Where am I", InputCategory.Exploration,
                WrathAccess.Exploration.Scanner.AnnounceWhereAmI).AddBinding(KeyCode.Z);
            // X: describe the focused scanner object (its asset description); Shift+X: describe the
            // current room — our OWN authored descriptions, distinct from the game's examine text.
            InputManager.Register("explore.describe", "Describe scanner target", InputCategory.Exploration,
                WrathAccess.Exploration.Scanner.DescribeTarget).AddBinding(KeyCode.X);
            InputManager.Register("explore.describeRoom", "Describe room", InputCategory.Exploration,
                WrathAccess.Exploration.Scanner.DescribeRoom).AddBinding(KeyCode.X, shift: true);
            // Space: skip the current cutscene (the game's own skip — its Enter binding; we use Space so
            // Enter-after-dialogue can't blow through a scene). InGame so it's live while a cutscene holds
            // control; in normal play the Exploration Space (pause) shadows it.
            InputManager.Register("game.skipCutscene", "Skip cutscene", InputCategory.InGame,
                WrathAccess.Exploration.CutsceneSkip.Request).AddBinding(KeyCode.Space);
            // InGame (not Exploration): this must keep working when we DON'T have control — opening the
            // pause menu mid-cutscene/dialogue is a base in-game key, so it isn't gated off with movement.
            InputManager.Register("explore.cancelTargeting", "Cancel targeting / game menu", InputCategory.InGame,
                () =>
                {
                    if (WrathAccess.Exploration.Targeting.Aiming) { WrathAccess.Exploration.Targeting.Cancel(); return; }
                    // Nothing to cancel → Escape opens the game's pause menu, exactly like the game's
                    // own Esc key (the handler toggles, and EscMenuScreen takes over while it's open).
                    Kingmaker.PubSubSystem.EventBus.RaiseEvent(
                        delegate(Kingmaker.PubSubSystem.IEscMenuHandler h) { h.HandleOpen(); });
                })
                .AddBinding(KeyCode.Escape);

            // Party selection - drives the game's real selection, which decides move-to-cursor's
            // single-vs-formation behaviour. Ctrl+A = whole party; Ctrl+1..6 = a single member.
            InputManager.Register("party.selectAll", "Select whole party", InputCategory.Exploration,
                WrathAccess.Exploration.PartySelection.SelectWholeParty).AddBinding(KeyCode.A, ctrl: true).Grouped("party");
            for (int i = 0; i < 6; i++)
            {
                int idx = i; // capture per-iteration for the closure
                InputManager.Register("party.select" + (i + 1), "Select party member " + (i + 1),
                    InputCategory.Exploration, () => WrathAccess.Exploration.PartySelection.SelectMember(idx))
                    .AddBinding(KeyCode.Alpha1 + i, ctrl: true).Grouped("party");
            }
            // Hold position (H) / Stop (G): the game's own party commands (the in-game menu's Hold/Stop
            // buttons), toggled across the current selection. Both keys are free in our exploration set.
            InputManager.Register("party.hold", "Hold position", InputCategory.Exploration,
                WrathAccess.Exploration.PartySelection.ToggleHold).AddBinding(KeyCode.H).Grouped("party");
            InputManager.Register("party.stop", "Stop", InputCategory.Exploration,
                WrathAccess.Exploration.PartySelection.Stop).AddBinding(KeyCode.G).Grouped("party");
            // Stealth (Ctrl+S) / AI control (Ctrl+D): the action-bar sneak + AI toggles, on the selection.
            InputManager.Register("party.stealth", "Toggle stealth", InputCategory.Exploration,
                WrathAccess.Exploration.PartySelection.ToggleStealth).AddBinding(KeyCode.S, ctrl: true).Grouped("party");
            InputManager.Register("party.ai", "Toggle AI control", InputCategory.Exploration,
                WrathAccess.Exploration.PartySelection.ToggleAi).AddBinding(KeyCode.D, ctrl: true).Grouped("party");

            // Formation editor cursor (WASD), live only while the formation window's field is focused (the
            // FormationScreen claims the Formation category then). W/S = forward/back (north/south), A/D =
            // left/right (west/east). Routed to the focused screen's editor state (FocusedField).
            InputManager.Register("formation.cursorUp", "Formation: move cursor forward", InputCategory.Formation,
                () => WrathAccess.Screens.FormationScreen.FocusedField?.MoveStep(0, 1))
                .AddBinding(KeyCode.W).Repeating().Grouped("formation");
            InputManager.Register("formation.cursorDown", "Formation: move cursor back", InputCategory.Formation,
                () => WrathAccess.Screens.FormationScreen.FocusedField?.MoveStep(0, -1))
                .AddBinding(KeyCode.S).Repeating().Grouped("formation");
            InputManager.Register("formation.cursorLeft", "Formation: move cursor left", InputCategory.Formation,
                () => WrathAccess.Screens.FormationScreen.FocusedField?.MoveStep(-1, 0))
                .AddBinding(KeyCode.A).Repeating().Grouped("formation");
            InputManager.Register("formation.cursorRight", "Formation: move cursor right", InputCategory.Formation,
                () => WrathAccess.Screens.FormationScreen.FocusedField?.MoveStep(1, 0))
                .AddBinding(KeyCode.D).Repeating().Grouped("formation");
            // Continuous free glide (Shift+WASD): no-op handlers, polled via InputManager.Held in
            // FormationField.Tick (like the overlay "play while held" keys) — glides + cues + reads on release.
            InputManager.Register("formation.glideUp", "Formation: glide cursor forward", InputCategory.Formation,
                () => { }).AddBinding(KeyCode.W, shift: true).Grouped("formation");
            InputManager.Register("formation.glideDown", "Formation: glide cursor back", InputCategory.Formation,
                () => { }).AddBinding(KeyCode.S, shift: true).Grouped("formation");
            InputManager.Register("formation.glideLeft", "Formation: glide cursor left", InputCategory.Formation,
                () => { }).AddBinding(KeyCode.A, shift: true).Grouped("formation");
            InputManager.Register("formation.glideRight", "Formation: glide cursor right", InputCategory.Formation,
                () => { }).AddBinding(KeyCode.D, shift: true).Grouped("formation");
            // Comma / Shift+Comma: jump the cursor to the next / previous member.
            InputManager.Register("formation.cycleNext", "Formation: next member", InputCategory.Formation,
                () => WrathAccess.Screens.FormationScreen.FocusedField?.CycleMember(1))
                .AddBinding(KeyCode.Comma).Repeating().Grouped("formation");
            InputManager.Register("formation.cyclePrev", "Formation: previous member", InputCategory.Formation,
                () => WrathAccess.Screens.FormationScreen.FocusedField?.CycleMember(-1))
                .AddBinding(KeyCode.Comma, shift: true).Repeating().Grouped("formation");
            // Slash: move the cursor onto the reviewed member (like exploration's plant-cursor-on-review).
            InputManager.Register("formation.cursorToReview", "Formation: cursor to reviewed member", InputCategory.Formation,
                () => WrathAccess.Screens.FormationScreen.FocusedField?.JumpToReviewed())
                .AddBinding(KeyCode.Slash).Grouped("formation");
            // C: jump the cursor to the formation centre (0, 0).
            InputManager.Register("formation.center", "Formation: cursor to centre", InputCategory.Formation,
                () => WrathAccess.Screens.FormationScreen.FocusedField?.CenterCursor())
                .AddBinding(KeyCode.C).Grouped("formation");
            // Ctrl+1..6: grab that party member straight away (then move + Enter to place).
            for (int i = 0; i < 6; i++)
            {
                int idx = i; // capture per-iteration for the closure
                InputManager.Register("formation.pickMember" + (i + 1), "Formation: pick up member " + (i + 1),
                    InputCategory.Formation,
                    () => WrathAccess.Screens.FormationScreen.FocusedField?.PickMember(idx))
                    .AddBinding(KeyCode.Alpha1 + i, ctrl: true).Grouped("formation");
            }

            // Ctrl+T: toggle the game's combat mode (real-time-with-pause <-> turn-based). Ctrl+T is free
            // in normal play (the game's Ctrl+T "LocalTeleport" is a cheat-only binding).
            InputManager.Register("combat.toggleMode", "Toggle turn-based / real-time", InputCategory.Exploration,
                ToggleCombatMode).AddBinding(KeyCode.T, ctrl: true).Grouped("combat");
            // Turn-based status: the acting unit's action economy + remaining movement.
            InputManager.Register("combat.status", "Combat status: actions and movement", InputCategory.Exploration,
                WrathAccess.Exploration.CombatMode.AnnounceStatus).AddBinding(KeyCode.R).Grouped("combat");

            // Exploration scanner: a categorized, distance-sorted list of things in the current area.
            InputManager.Register("scan.itemNext", "Scanner: next item", InputCategory.Exploration,
                WrathAccess.Exploration.Scanner.NextItem).AddBinding(KeyCode.PageDown).Repeating().Grouped("scanner");
            InputManager.Register("scan.itemPrev", "Scanner: previous item", InputCategory.Exploration,
                WrathAccess.Exploration.Scanner.PrevItem).AddBinding(KeyCode.PageUp).Repeating().Grouped("scanner");
            InputManager.Register("scan.categoryNext", "Scanner: next category", InputCategory.Exploration,
                WrathAccess.Exploration.Scanner.NextCategory).AddBinding(KeyCode.PageDown, ctrl: true).Repeating().Grouped("scanner");
            InputManager.Register("scan.categoryPrev", "Scanner: previous category", InputCategory.Exploration,
                WrathAccess.Exploration.Scanner.PrevCategory).AddBinding(KeyCode.PageUp, ctrl: true).Repeating().Grouped("scanner");
            InputManager.Register("scan.subcategoryNext", "Scanner: next subcategory", InputCategory.Exploration,
                WrathAccess.Exploration.Scanner.NextSubcategory).AddBinding(KeyCode.PageDown, shift: true).Repeating().Grouped("scanner");
            InputManager.Register("scan.subcategoryPrev", "Scanner: previous subcategory", InputCategory.Exploration,
                WrathAccess.Exploration.Scanner.PrevSubcategory).AddBinding(KeyCode.PageUp, shift: true).Repeating().Grouped("scanner");
            // Home and Slash: plant the movement cursor ON the review target (the explicit opt-in jump).
            InputManager.Register("scan.cursorToItem", "Move cursor to review target", InputCategory.Exploration,
                WrathAccess.Exploration.Scanner.CursorToSelected)
                .AddBinding(KeyCode.Home).AddBinding(KeyCode.Slash).Grouped("scanner");
            InputManager.Register("scan.announceCursor", "Announce cursor position", InputCategory.Exploration,
                WrathAccess.Exploration.Scanner.AnnounceCursor).AddBinding(KeyCode.K).Grouped("scanner");
            InputManager.Register("scan.announceParty", "Announce party", InputCategory.Exploration,
                WrathAccess.Exploration.Scanner.AnnounceParty).AddBinding(KeyCode.K, shift: true).Grouped("scanner");
            InputManager.Register("scan.interact", "Scanner: interact with item", InputCategory.Exploration,
                WrathAccess.Exploration.Scanner.InteractSelected).AddBinding(KeyCode.I).Grouped("scanner");
            // Inspect: open the game's unit Inspect window (only if the unit actually has one). Y inspects the
            // review-cursor unit; ' inspects the unit the movement cursor is over. (Where am I moved to X.)
            InputManager.Register("inspect.review", "Inspect review-cursor unit", InputCategory.Exploration,
                WrathAccess.Exploration.Inspect.Review).AddBinding(KeyCode.Y).Grouped("scanner");
            InputManager.Register("inspect.cursor", "Inspect unit under cursor", InputCategory.Exploration,
                WrathAccess.Exploration.Inspect.AtCursor).AddBinding(KeyCode.Quote).Grouped("scanner");
            InputManager.Register("scan.moveToCursor", "Scanner: move to cursor", InputCategory.Exploration,
                WrathAccess.Exploration.Scanner.MoveToCursor).AddBinding(KeyCode.Backspace).Grouped("scanner");
            InputManager.Register("scan.debugShowAll", "Scanner: toggle show all (debug)", InputCategory.Exploration,
                WrathAccess.Exploration.Scanner.ToggleDebugShowAll).AddBinding(KeyCode.F11).Grouped("scanner");
            InputManager.Register("scan.debugDumpNames", "Scanner: dump object names to log (debug)", InputCategory.Exploration,
                WrathAccess.Exploration.Scanner.DumpObjectNames).AddBinding(KeyCode.F10).Grouped("scanner");
            InputManager.Register("scan.debugAreaParts", "Read area parts (debug)", InputCategory.Exploration,
                WrathAccess.Exploration.Scanner.DebugDumpAreaParts).AddBinding(KeyCode.F9).Grouped("scanner");

            // Buffers (review channels — see WrathAccess.Buffers): Alt+Left/Right cycle between buffers,
            // Alt+Up/Down move through the current buffer's lines. Alt+arrows are otherwise unused; live in
            // the Exploration category (in a game, exploring or HUD-focused). v1 = the selected-unit and
            // review-unit buffers (name, HP, AC, then all buffs/debuffs).
            InputManager.Register("buffer.bufferPrev", "Buffer: previous buffer", InputCategory.Exploration,
                WrathAccess.Buffers.BufferControls.PrevBuffer).AddBinding(KeyCode.LeftArrow, alt: true).Repeating().Grouped("buffers");
            InputManager.Register("buffer.bufferNext", "Buffer: next buffer", InputCategory.Exploration,
                WrathAccess.Buffers.BufferControls.NextBuffer).AddBinding(KeyCode.RightArrow, alt: true).Repeating().Grouped("buffers");
            InputManager.Register("buffer.itemPrev", "Buffer: previous line", InputCategory.Exploration,
                WrathAccess.Buffers.BufferControls.PrevItem).AddBinding(KeyCode.UpArrow, alt: true).Repeating().Grouped("buffers");
            InputManager.Register("buffer.itemNext", "Buffer: next line", InputCategory.Exploration,
                WrathAccess.Buffers.BufferControls.NextItem).AddBinding(KeyCode.DownArrow, alt: true).Repeating().Grouped("buffers");
            WrathAccess.Buffers.BufferManager.Instance.RegisterDefaults();

            // Camera (low-vision aid): pan / rotate / follow, replicating the game's own camera controls.
            // Alt+WASD pan, Alt+Q/E rotate, Alt+F follow the selected character. Exploration category, so they
            // are live in an area while we have control and dead in cutscene / dialogue / menus / world map -
            // exactly where the game itself lets the camera move. Pan/rotate repeat while held.
            InputManager.Register("camera.panUp", "Camera: pan up", InputCategory.Exploration,
                WrathAccess.Exploration.CameraControls.PanUp).AddBinding(KeyCode.W, alt: true).Repeating().Grouped("camera");
            InputManager.Register("camera.panDown", "Camera: pan down", InputCategory.Exploration,
                WrathAccess.Exploration.CameraControls.PanDown).AddBinding(KeyCode.S, alt: true).Repeating().Grouped("camera");
            InputManager.Register("camera.panLeft", "Camera: pan left", InputCategory.Exploration,
                WrathAccess.Exploration.CameraControls.PanLeft).AddBinding(KeyCode.A, alt: true).Repeating().Grouped("camera");
            InputManager.Register("camera.panRight", "Camera: pan right", InputCategory.Exploration,
                WrathAccess.Exploration.CameraControls.PanRight).AddBinding(KeyCode.D, alt: true).Repeating().Grouped("camera");
            InputManager.Register("camera.rotateLeft", "Camera: rotate left", InputCategory.Exploration,
                WrathAccess.Exploration.CameraControls.RotateLeft).AddBinding(KeyCode.Q, alt: true).Repeating().Grouped("camera");
            InputManager.Register("camera.rotateRight", "Camera: rotate right", InputCategory.Exploration,
                WrathAccess.Exploration.CameraControls.RotateRight).AddBinding(KeyCode.E, alt: true).Repeating().Grouped("camera");
            InputManager.Register("camera.follow", "Camera: follow selected character", InputCategory.Exploration,
                WrathAccess.Exploration.CameraControls.Follow).AddBinding(KeyCode.F, alt: true).Grouped("camera");

            // Service-window hotkeys (InputCategory.Windows): open character sheet / inventory / spellbook /
            // journal directly. Live in an area (while we have control) AND on the world map, via the game's
            // own EventBus open path, which routes to whichever ServiceWindowsVM is active (in-area or
            // global-map) and toggles the window. Ctrl chords, to avoid the game's plain-letter hotkeys.
            InputManager.Register("window.character", "Open character sheet", InputCategory.Windows,
                () => OpenWindow(ServiceWindowsType.CharacterInfo)).AddBinding(KeyCode.C, ctrl: true);
            InputManager.Register("window.inventory", "Open inventory", InputCategory.Windows,
                () => OpenWindow(ServiceWindowsType.Inventory)).AddBinding(KeyCode.I, ctrl: true);
            InputManager.Register("window.spellbook", "Open spellbook", InputCategory.Windows,
                () => OpenWindow(ServiceWindowsType.Spellbook)).AddBinding(KeyCode.B, ctrl: true);
            InputManager.Register("window.journal", "Open journal", InputCategory.Windows,
                () => OpenWindow(ServiceWindowsType.Journal)).AddBinding(KeyCode.J, ctrl: true);

            // World-map scanner (InputCategory.WorldMap — isolated from the in-area scanner): a categorised,
            // nearest-first browse of the map's revealed points. Same physical keys as the in-area scanner,
            // but they only fire on the world-map screen, routed to the separate world-map systems.
            InputManager.Register("worldmap.scanNext", "World map: next point", InputCategory.WorldMap,
                WrathAccess.Exploration.GlobalMapScanner.NextItem).AddBinding(KeyCode.PageDown).Repeating().Grouped("worldmap");
            InputManager.Register("worldmap.scanPrev", "World map: previous point", InputCategory.WorldMap,
                WrathAccess.Exploration.GlobalMapScanner.PrevItem).AddBinding(KeyCode.PageUp).Repeating().Grouped("worldmap");
            InputManager.Register("worldmap.catNext", "World map: next category", InputCategory.WorldMap,
                WrathAccess.Exploration.GlobalMapScanner.NextCategory).AddBinding(KeyCode.PageDown, ctrl: true).Repeating().Grouped("worldmap");
            InputManager.Register("worldmap.catPrev", "World map: previous category", InputCategory.WorldMap,
                WrathAccess.Exploration.GlobalMapScanner.PrevCategory).AddBinding(KeyCode.PageUp, ctrl: true).Repeating().Grouped("worldmap");
            InputManager.Register("worldmap.interact", "World map: travel to / enter point", InputCategory.WorldMap,
                WrathAccess.Exploration.GlobalMapScanner.Interact).AddBinding(KeyCode.I).Grouped("worldmap");
            // World-map review cursor (single-key cycles, like the in-area Comma/Period/N/M/B): b = all
            // points nearest-first, m = the current location's connected points; Shift reverses.
            InputManager.Register("worldmap.reviewAllNext", "World map: next point (review)", InputCategory.WorldMap,
                WrathAccess.Exploration.GlobalMapScanner.AllNext).AddBinding(KeyCode.B).Repeating().Grouped("worldmap");
            InputManager.Register("worldmap.reviewAllPrev", "World map: previous point (review)", InputCategory.WorldMap,
                WrathAccess.Exploration.GlobalMapScanner.AllPrev).AddBinding(KeyCode.B, shift: true).Repeating().Grouped("worldmap");
            InputManager.Register("worldmap.reviewConnNext", "World map: next connected point", InputCategory.WorldMap,
                WrathAccess.Exploration.GlobalMapScanner.ConnectedNext).AddBinding(KeyCode.M).Repeating().Grouped("worldmap");
            InputManager.Register("worldmap.reviewConnPrev", "World map: previous connected point", InputCategory.WorldMap,
                WrathAccess.Exploration.GlobalMapScanner.ConnectedPrev).AddBinding(KeyCode.M, shift: true).Repeating().Grouped("worldmap");
            InputManager.Register("worldmap.reviewReachNext", "World map: next reachable location", InputCategory.WorldMap,
                WrathAccess.Exploration.GlobalMapScanner.ReachableNext).AddBinding(KeyCode.N).Repeating().Grouped("worldmap");
            InputManager.Register("worldmap.reviewReachPrev", "World map: previous reachable location", InputCategory.WorldMap,
                WrathAccess.Exploration.GlobalMapScanner.ReachablePrev).AddBinding(KeyCode.N, shift: true).Repeating().Grouped("worldmap");

            // Ctrl+O cycles overlays here too (the in-area overlay.cycle is in the Exploration category, which
            // the world-map screen doesn't claim) — same OverlayManager, now engaged on the world map.
            InputManager.Register("worldmap.cycleOverlay", "World map: cycle overlay", InputCategory.WorldMap,
                OverlayManager.Cycle).AddBinding(KeyCode.O, ctrl: true).Grouped("worldmap");

            // World-map army cycles (. = enemy/demon, , = ally/crusader); Shift reverses. Inert until the
            // crusade is active (Act 2+) — no armies on the Act-1 map, so these just report "no ... armies".
            InputManager.Register("worldmap.armyEnemyNext", "World map: next enemy army", InputCategory.WorldMap,
                WrathAccess.Exploration.GlobalMapScanner.EnemyNext).AddBinding(KeyCode.Period).Repeating().Grouped("worldmap");
            InputManager.Register("worldmap.armyEnemyPrev", "World map: previous enemy army", InputCategory.WorldMap,
                WrathAccess.Exploration.GlobalMapScanner.EnemyPrev).AddBinding(KeyCode.Period, shift: true).Repeating().Grouped("worldmap");
            InputManager.Register("worldmap.armyAllyNext", "World map: next ally army", InputCategory.WorldMap,
                WrathAccess.Exploration.GlobalMapScanner.AllyNext).AddBinding(KeyCode.Comma).Repeating().Grouped("worldmap");
            InputManager.Register("worldmap.armyAllyPrev", "World map: previous ally army", InputCategory.WorldMap,
                WrathAccess.Exploration.GlobalMapScanner.AllyPrev).AddBinding(KeyCode.Comma, shift: true).Repeating().Grouped("worldmap");

            // World-map MOVEMENT cursor (WASD/arrows; no press handlers — GlobalMapCursor polls the held
            // vector each frame). Enter acts on a point under it, C recenters, K reads it, / jumps to review.
            InputManager.Register("worldmap.cursorUp", "World map: move cursor up", InputCategory.WorldMap)
                .AddBinding(KeyCode.UpArrow).AddBinding(KeyCode.W).Repeating().Grouped("worldmap");
            InputManager.Register("worldmap.cursorDown", "World map: move cursor down", InputCategory.WorldMap)
                .AddBinding(KeyCode.DownArrow).AddBinding(KeyCode.S).Repeating().Grouped("worldmap");
            InputManager.Register("worldmap.cursorLeft", "World map: move cursor left", InputCategory.WorldMap)
                .AddBinding(KeyCode.LeftArrow).AddBinding(KeyCode.A).Repeating().Grouped("worldmap");
            InputManager.Register("worldmap.cursorRight", "World map: move cursor right", InputCategory.WorldMap)
                .AddBinding(KeyCode.RightArrow).AddBinding(KeyCode.D).Repeating().Grouped("worldmap");
            // Secondary cursor (Shift+WASD/arrows): the same cursor point at the secondary slot's speed.
            InputManager.Register("worldmap.secondaryUp", "World map: secondary cursor up", InputCategory.WorldMap)
                .AddBinding(KeyCode.UpArrow, shift: true).AddBinding(KeyCode.W, shift: true).Repeating().Grouped("worldmap");
            InputManager.Register("worldmap.secondaryDown", "World map: secondary cursor down", InputCategory.WorldMap)
                .AddBinding(KeyCode.DownArrow, shift: true).AddBinding(KeyCode.S, shift: true).Repeating().Grouped("worldmap");
            InputManager.Register("worldmap.secondaryLeft", "World map: secondary cursor left", InputCategory.WorldMap)
                .AddBinding(KeyCode.LeftArrow, shift: true).AddBinding(KeyCode.A, shift: true).Repeating().Grouped("worldmap");
            InputManager.Register("worldmap.secondaryRight", "World map: secondary cursor right", InputCategory.WorldMap)
                .AddBinding(KeyCode.RightArrow, shift: true).AddBinding(KeyCode.D, shift: true).Repeating().Grouped("worldmap");
            InputManager.Register("worldmap.cursorInteract", "World map: act on cursor", InputCategory.WorldMap,
                WrathAccess.Exploration.GlobalMapCursor.Interact).AddBinding(KeyCode.Return).AddBinding(KeyCode.KeypadEnter).Grouped("worldmap");
            InputManager.Register("worldmap.cursorRecenter", "World map: cursor to party", InputCategory.WorldMap,
                WrathAccess.Exploration.GlobalMapCursor.Recenter).AddBinding(KeyCode.C).Grouped("worldmap");
            InputManager.Register("worldmap.cursorAnnounce", "World map: read cursor", InputCategory.WorldMap,
                WrathAccess.Exploration.GlobalMapCursor.Announce).AddBinding(KeyCode.K).Grouped("worldmap");
            InputManager.Register("worldmap.cursorToReview", "World map: cursor to review point", InputCategory.WorldMap,
                WrathAccess.Exploration.GlobalMapCursor.JumpToReview).AddBinding(KeyCode.Slash).Grouped("worldmap");
            // Escape → game menu, as an input action so it works while the cursor (unfocused) owns the keys
            // (the screen's GetActions Back only fires when focused) — mirrors the in-game screen.
            InputManager.Register("worldmap.escape", "World map: open menu", InputCategory.WorldMap,
                () => Kingmaker.PubSubSystem.EventBus.RaiseEvent(delegate (Kingmaker.PubSubSystem.IEscMenuHandler h) { h.HandleOpen(); }))
                .AddBinding(KeyCode.Escape).Grouped("worldmap");

            // Area overlays: swappable spatial views. Arrows drive the active overlay's cursor (see the
            // explore.cursor* actions above).
            InputManager.Register("overlay.cycle", "Cycle area overlay", InputCategory.Exploration,
                OverlayManager.Cycle).AddBinding(KeyCode.O, ctrl: true).Grouped("overlays");
            // Per-system play mode: Ctrl+F1/F2 cycle wall-tones / sonar through Off / When moving / Continuous
            // on the engaged overlay; Shift+F1/F2 force the system on while held (polled in OverlayManager.Tick).
            InputManager.Register("overlay.cycleWalltones", "Wall tones: cycle mode", InputCategory.Exploration,
                () => OverlayManager.CycleMode("walltones")).AddBinding(KeyCode.F1, ctrl: true).Grouped("overlays");
            InputManager.Register("overlay.cycleSonar", "Sonar: cycle mode", InputCategory.Exploration,
                () => OverlayManager.CycleMode("sonar")).AddBinding(KeyCode.F2, ctrl: true).Grouped("overlays");
            InputManager.Register("overlay.holdWalltones", "Wall tones: play while held", InputCategory.Exploration,
                () => { }).AddBinding(KeyCode.F1, shift: true).Grouped("overlays"); // polled via InputManager.Held
            InputManager.Register("overlay.holdSonar", "Sonar: play while held", InputCategory.Exploration,
                () => { }).AddBinding(KeyCode.F2, shift: true).Grouped("overlays"); // polled via InputManager.Held
            InputManager.Register("overlay.recenter", "Overlay: recenter on player", InputCategory.Exploration,
                OverlayManager.Recenter).AddBinding(KeyCode.C).Grouped("overlays");
            InputManager.Register("overlay.announce", "Overlay: announce cursor", InputCategory.Exploration,
                OverlayManager.AnnounceCurrent).AddBinding(KeyCode.Keypad5).Grouped("overlays");
            InputManager.Register("overlay.descend", "Overlay: follow surface down", InputCategory.Exploration,
                () => OverlayManager.VerticalFollow(-1)).AddBinding(KeyCode.Period, ctrl: true).Grouped("overlays");
            InputManager.Register("overlay.ascend", "Overlay: follow surface up", InputCategory.Exploration,
                () => OverlayManager.VerticalFollow(1)).AddBinding(KeyCode.Comma, ctrl: true).Grouped("overlays");

            // The review cursor: cycle nearby things by group — closest first from the movement cursor,
            // which NEVER moves (look around while holding position). Shift = cycle backward. The landing
            // becomes the scanner selection, so I interacts with it and Home plants the cursor on it.
            InputManager.Register("review.nextParty", "Review next party member", InputCategory.Exploration,
                () => WrathAccess.Exploration.Scanner.CycleReview(WrathAccess.Exploration.ReviewGroup.Party, 1))
                .AddBinding(KeyCode.Comma).Repeating().Grouped("review");
            InputManager.Register("review.prevParty", "Review previous party member", InputCategory.Exploration,
                () => WrathAccess.Exploration.Scanner.CycleReview(WrathAccess.Exploration.ReviewGroup.Party, -1))
                .AddBinding(KeyCode.Comma, shift: true).Repeating().Grouped("review");
            InputManager.Register("review.nextEnemy", "Review next enemy", InputCategory.Exploration,
                () => WrathAccess.Exploration.Scanner.CycleReview(WrathAccess.Exploration.ReviewGroup.Enemies, 1))
                .AddBinding(KeyCode.Period).Repeating().Grouped("review");
            InputManager.Register("review.prevEnemy", "Review previous enemy", InputCategory.Exploration,
                () => WrathAccess.Exploration.Scanner.CycleReview(WrathAccess.Exploration.ReviewGroup.Enemies, -1))
                .AddBinding(KeyCode.Period, shift: true).Repeating().Grouped("review");
            InputManager.Register("review.nextNeutral", "Review next neutral", InputCategory.Exploration,
                () => WrathAccess.Exploration.Scanner.CycleReview(WrathAccess.Exploration.ReviewGroup.Neutrals, 1))
                .AddBinding(KeyCode.N).Repeating().Grouped("review");
            InputManager.Register("review.prevNeutral", "Review previous neutral", InputCategory.Exploration,
                () => WrathAccess.Exploration.Scanner.CycleReview(WrathAccess.Exploration.ReviewGroup.Neutrals, -1))
                .AddBinding(KeyCode.N, shift: true).Repeating().Grouped("review");
            InputManager.Register("review.nextOther", "Review next object", InputCategory.Exploration,
                () => WrathAccess.Exploration.Scanner.CycleReview(WrathAccess.Exploration.ReviewGroup.Others, 1))
                .AddBinding(KeyCode.M).Repeating().Grouped("review");
            // L / Shift+L: cycle the unexplored-space frontier (where the map can still grow). Then
            // Slash plants the cursor there as with any reviewed thing; the review cue tones as usual.
            InputManager.Register("review.nextUnexplored", "Review next unexplored space", InputCategory.Exploration,
                () => WrathAccess.Exploration.Scanner.CycleReview(WrathAccess.Exploration.ReviewGroup.Unexplored, 1))
                .AddBinding(KeyCode.L).Repeating().Grouped("review");
            InputManager.Register("review.prevUnexplored", "Review previous unexplored space", InputCategory.Exploration,
                () => WrathAccess.Exploration.Scanner.CycleReview(WrathAccess.Exploration.ReviewGroup.Unexplored, -1))
                .AddBinding(KeyCode.L, shift: true).Repeating().Grouped("review");
            InputManager.Register("review.prevOther", "Review previous object", InputCategory.Exploration,
                () => WrathAccess.Exploration.Scanner.CycleReview(WrathAccess.Exploration.ReviewGroup.Others, -1))
                .AddBinding(KeyCode.M, shift: true).Repeating().Grouped("review");
            InputManager.Register("review.nextPoi", "Review next point of interest", InputCategory.Exploration,
                () => WrathAccess.Exploration.Scanner.CycleReview(WrathAccess.Exploration.ReviewGroup.Poi, 1))
                .AddBinding(KeyCode.B).Repeating().Grouped("review");
            InputManager.Register("review.prevPoi", "Review previous point of interest", InputCategory.Exploration,
                () => WrathAccess.Exploration.Scanner.CycleReview(WrathAccess.Exploration.ReviewGroup.Poi, -1))
                .AddBinding(KeyCode.B, shift: true).Repeating().Grouped("review");
            InputManager.Register("review.nextExit", "Review next room exit", InputCategory.Exploration,
                () => WrathAccess.Exploration.Scanner.CycleRoomExits(1))
                .AddBinding(KeyCode.V).Repeating().Grouped("review");
            InputManager.Register("review.prevExit", "Review previous room exit", InputCategory.Exploration,
                () => WrathAccess.Exploration.Scanner.CycleRoomExits(-1))
                .AddBinding(KeyCode.V, shift: true).Repeating().Grouped("review");
        }
    }
}
