using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.UI.MVVM;
using Kingmaker.UI.MVVM._VM.ServiceWindows;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Resolves the active screen stack from RootUIContext each frame (poll-and-diff,
    /// robust to the VM-recreation lifecycle) and dispatches lifecycle events. The
    /// stack is ordered bottom→top by Layer; Current is the top (the focused screen).
    /// Ticked from Main.OnUpdate.
    /// </summary>
    public static class ScreenManager
    {
        private static readonly List<Screen> _registered = new List<Screen>();
        private static List<Screen> _stack = new List<Screen>();
        private static Screen _focused; // the deepest screen the navigator is currently attached to

        // The focused screen = the deepest active child of the top outer screen (== the top when no
        // child sub-screens are pushed).
        public static Screen Current => _stack.Count > 0 ? _stack[_stack.Count - 1].DeepestActiveScreen() : null;
        public static IReadOnlyList<Screen> Stack => _stack;

        /// <summary>Active screens in focus-priority order — the focused screen first (top outer screen's
        /// deepest child), then outward/down to the base context. This is the order the input claim-chain
        /// walks: a deeper screen's categories take precedence (shadow) over a shallower one's.</summary>
        public static IEnumerable<Screen> FocusedFirst()
        {
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                var chain = new List<Screen>();
                for (var s = _stack[i]; s != null; s = s.ActiveChild) chain.Add(s); // outer → deepest
                for (int j = chain.Count - 1; j >= 0; j--) yield return chain[j];    // deepest → outer
            }
        }

        public static void Register(Screen screen) => _registered.Add(screen);

        public static void Tick()
        {
            ApplyDiff(Resolve()); // poll the outer (game-driven) screens → push/pop on the persistent stack
            SyncFocus();          // focus the deepest screen (outer changes; before OnUpdate, as before)
            Current?.OnUpdate();  // may push/remove child sub-screens
            SyncFocus();          // re-sync if OnUpdate (or this frame's input) changed the child tree
            // Standardized first-focus: once the focused screen has built its content (some build lazily in
            // OnUpdate), make sure something is focused. No-op when focus already exists or the screen is
            // intentionally unfocused (exploration).
            WrathAccess.UI.Navigation.EnsureFocus();
            // After focus has settled, let the focused element observe its own live state and announce any
            // in-place change (async toggle settle, enabled/disabled flip) — the per-element OnUpdate hook.
        }

        /// <summary>Active screens, ordered bottom (low layer) → top (high layer).</summary>
        private static List<Screen> Resolve()
        {
            var active = new List<Screen>();
            for (int i = 0; i < _registered.Count; i++)
                if (SafeIsActive(_registered[i])) active.Add(_registered[i]);
            return active.OrderBy(s => s.Layer).ToList();
        }

        private static bool SafeIsActive(Screen s)
        {
            try { return s.IsActive(); }
            catch (Exception e)
            {
                Main.Log?.Error("Screen.IsActive threw for '" + s.Key + "': " + e.Message);
                return false;
            }
        }

        // Diff the polled active set against the persistent stack: pop outer screens that went inactive
        // (each with its whole child subtree, top→bottom) and push newly-active ones (bottom→top). Focus
        // is handled separately by SyncFocus so child-tree changes and outer changes go through one path.
        private static void ApplyDiff(List<Screen> desired)
        {
            for (int i = _stack.Count - 1; i >= 0; i--)
                if (!desired.Contains(_stack[i])) PopTree(_stack[i]);
            for (int i = 0; i < desired.Count; i++)
                if (!_stack.Contains(desired[i])) { var s = desired[i]; Safe(() => s.OnPush(), s, "OnPush"); }
            _stack = desired;
        }

        // An outer screen leaving the stack disposes its child subtree (deepest-first), then OnPops itself.
        private static void PopTree(Screen s)
        {
            if (s.ActiveChild != null) s.RemoveChild(s.ActiveChild);
            Safe(() => s.OnPop(), s, "OnPop");
            // Closing a screen clears its nav state (reopening starts fresh) — except screens that opt
            // out because popping isn't really closing (dialogue hiding during a cutscene gap / under
            // the pause menu) or resuming your place is the point (the log).
            if (!s.KeepStateOnPop) WrathAccess.UI.Navigation.ScreenClosed(s);
        }

        // Re-attach the navigator whenever the deepest (focused) screen changes — from an outer push/pop
        // OR a child-tree push/remove. No screen overrides OnUnfocus, so firing it after a pop is harmless.
        // Idempotent (no-op when the focused screen is unchanged).
        private static void SyncFocus()
        {
            var cur = Current;
            if (ReferenceEquals(cur, _focused)) return;
            _focused?.OnUnfocus();
            _focused = cur;
            Safe(() => cur?.OnFocus(), cur, "OnFocus"); // speaks the screen name
            // Bind the navigator; the initial-focus landing is announced by EnsureFocus once the screen's
            // content exists (handles lazy builds + build-time silent Attach uniformly).
            WrathAccess.UI.Navigation.Attach(cur);
        }

        private static void Safe(Action a, Screen s, string hook)
        {
            try { a(); }
            catch (Exception e) { Main.Log?.Error("Screen." + hook + " threw for '" + (s?.Key ?? "?") + "': " + e); }
        }

        public static void Initialize()
        {
            if (_registered.Count > 0) return;

            // Base contexts (layer 0) — mutually exclusive.
            Register(new MainMenuScreen());
            Register(new NewGameScreen()); // main-menu New Game wizard (shown instead of the sidebar)
            Register(new CharGenScreen()); // chargen / level-up (menu + in-game); layer 15, above contexts
            Register(new InGameScreen()); // exploration: unfocused (overlay owns arrows), Tab enters the HUD
            Register(new ModLogScreen()); // mod log review (channel tabs + history), opened from the HUD, layer 22
            Register(new GlobalMapScreen()); // world map: browse + cursor/scanner + the location panel as a tab stop
            Register(new GlobalMapEncounterScreen()); // world-map travel popup (encounter/discovery), FlowSheet like dialogue; layer 15
            Register(new PredicateScreen("ctx.tacticalcombat", Loc.T("screen.tactical_combat"), 0, () => RC()?.IsTacticalCombat ?? false));
            Register(new DialogueScreen()); // in-game conversation (common DialogVM); layer 15, above contexts + service windows
            Register(new LootScreen()); // loot window (container/corpse); layer 15, above contexts + service windows
            Register(new VendorScreen()); // vendor/trade window (VendorVM); layer 15, four trade tables + actions
            Register(new RestScreen()); // camping window (management/in-process/results); layer 15, same family
            Register(new GroupChangerScreen()); // party selection on area exit (Group manager); layer 16, hard modal
            Register(new FormationScreen()); // party formation window (selector + WASD editor); layer 16, Exclusive
            Register(new GameOverScreen()); // death/game-over screen (party wipe etc.); layer 18, below save/load
            Register(new EscMenuScreen()); // the in-game pause/Escape menu (EscMenuVM buttons); layer 24
            Register(new BookEventScreen()); // book event (storybook passage + choices); layer 15
            Register(new PredicateScreen("ctx.kingdom", Loc.T("screen.kingdom"), 0, () => RC()?.IsKingdom ?? false));
            Register(new PredicateScreen("ctx.citybuilder", Loc.T("screen.city"), 0, () => RC()?.IsCityBuilder ?? false));

            // Service windows (layer 10) — one at a time, via CurrentServiceWindow.
            Register(new InventoryScreen()); // inventory window (Inventory/Equipment/SmartItem), navigable; layer 10
            Register(new CharacterInfoScreen()); // character sheet (CharacterInfo window), navigable; layer 10
            RegisterServiceWindow("Mythic Path", Loc.T("screen.mythic_path"), ServiceWindowsType.Mythic);
            Register(new SpellbookScreen()); // spellbook window (known spells + add to action bar), navigable; layer 10
            Register(new JournalScreen()); // journal window (grouped quests + objectives), navigable; layer 10
            Register(new EncyclopediaScreen()); // encyclopedia window (chapters + page text + child links), navigable; layer 10
            RegisterServiceWindow("Map", Loc.T("screen.map"), ServiceWindowsType.LocalMap);

            // Overlays (can sit on top of a context/window). Settings lives on the
            // shared CommonVM, so this same screen also covers the in-game pause menu.
            Register(new SaveLoadScreen()); // save/load window (CommonVM.SaveLoadVM), layer 20
            Register(new SettingsScreen());
            // ChoiceSubmenuScreen is no longer registered — it's a CHILD screen, pushed on its opener via
            // ChoiceSubmenuScreen.Open (a dropdown's value list), and removed when a choice is made.
            Register(new KeyBindCaptureScreen()); // key-binding capture, layer 27 (raw-input passthrough)
            Register(new TutorialScreen()); // modal tutorial popup (movement/camera etc.), layer 28
            Register(new MessageModalScreen()); // generic confirm/message modal, layer 30
            Register(new InfoWindowScreen()); // game Info window (item Details / glossary info), layer 30
            Register(new InspectScreen()); // game unit Inspect window (bestiary readout), layer 30
            Register(new LockpickScreen()); // lock/disable-device tool-choice window (skill/+5/+10/destroy), layer 30
            Register(new ModMenuScreen()); // mod menu launcher (Ctrl+M): Settings, Help, Setup wizard, Discord, Patreon; layer 35
            Register(new HelpScreen()); // Help submenu (Read documentation); opens from the launcher, layer 38
            Register(new ModSettingsScreen()); // the tabbed settings browser, opened from the launcher; layer 37
            Register(new SetupWizardScreen()); // first-run setup wizard (speech engine + its settings), layer 36
            Register(new GammaScreen()); // first-launch brightness/gamma calibration (boot-time, before main menu), layer 40
            // ModKeyCaptureScreen + ModTextEntryScreen are no longer registered — both are CHILD screens
            // pushed on the current screen via their static Open(...) (key capture / text entry overlays).
            // TooltipScreen is no longer registered — each tooltip page is a CHILD screen pushed on the
            // current screen/page via TooltipScreen.Open/OpenMenu; the child stack is the drill stack.

            Main.Log?.Log("ScreenManager: " + _registered.Count + " screens registered.");
        }

        // key stays the stable English name; label is the localized spoken screen name
        private static void RegisterServiceWindow(string name, string label, params ServiceWindowsType[] types)
        {
            Register(new PredicateScreen("service." + name, label, 10, () =>
            {
                var rc = RC();
                if (rc == null) return false;
                var cur = rc.CurrentServiceWindow;
                for (int i = 0; i < types.Length; i++)
                    if (cur == types[i]) return true;
                return false;
            }));
        }

        private static RootUIContext RC()
        {
            var g = Game.Instance;
            return g != null ? g.RootUiContext : null;
        }
    }
}
