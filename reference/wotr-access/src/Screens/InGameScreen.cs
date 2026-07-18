using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings; // UIStrings (game-localized TB control names)
using Kingmaker.EntitySystem.Entities; // UnitEntityData
using Kingmaker.UI.Common; // UIUtility.GetServiceWindowsLabel
using Kingmaker.UI.MVVM._VM.ActionBar;
using Kingmaker.UI.MVVM._VM.IngameMenu; // IngameMenuVM (the compass-corner menu bar)
using Kingmaker.UI.MVVM._VM.ServiceWindows; // ServiceWindowsVM, ServiceWindowsType
using Kingmaker.UI.UnitSettings; // MechanicActionBarSlotEmpty
using Kingmaker.UnitLogic; // GetRider / IsSummoned extensions
using Kingmaker.UnitLogic.Parts; // UnitPartInsideAnotherCreature
using WrathAccess.Exploration; // CombatMode
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The in-game (exploration) context as a navigable screen, graph-native. It's UNFOCUSED by default —
    /// the overlay owns the arrows for spatial navigation — and <b>Tab enters the HUD</b>, then Tab cycles
    /// its stops (Tab off the end returns to exploration). Stops, in order: the <b>Action bar</b> (the
    /// selected unit's usable slots, then the Abilities / Spells / Items groups as collapsible tree
    /// sections), the turn-based <b>Turn</b> panel (only in TB combat), the <b>Menu</b> control list, and
    /// the <b>Windows</b> list. Everything renders live per frame — a character swap or an item being
    /// consumed just re-renders, and focus follows slot/unit IDENTITY across the change (the old
    /// signature/rebuild/restore machinery is gone).
    /// </summary>
    public sealed class InGameScreen : Screen
    {
        public override string Key => "ctx.ingame";
        public override string ScreenName => Loc.T("screen.game");
        public override int Layer => 0;
        public override bool StartUnfocused => true; // exploration owns the arrows; Tab brings up the HUD
        public override bool AllowsTypeahead => false; // letters stay exploration hotkeys (scanner, status…)

        // Both categories are always live in-game; HUD focus decides which one wins SHARED chords
        // (arrows, Enter, Space, Escape, Backspace). Focused: UI first — arrows navigate the HUD —
        // while unshared exploration keys (Shift+arrows, scanner, party) keep working underneath.
        // Unfocused: Exploration first — arrows move the cursor — while unshared UI keys (Tab) still
        // reach the navigator, which is how Tab ENTERS the HUD.
        // Always-live base in-game keys (InGame: Escape → pause menu) sit alongside the control-gated
        // Exploration set. Order matters for SHARED chords — Escape is bound in both InGame (cancel/menu)
        // and UI (ui.back): Focused → UI first (Escape backs out, arrows nav the HUD); Unfocused → InGame
        // before UI so Escape opens the menu, Exploration first so arrows move the cursor.
        private static readonly WrathAccess.Input.InputCategory[] FocusedCats =
            { WrathAccess.Input.InputCategory.UI, WrathAccess.Input.InputCategory.InGame, WrathAccess.Input.InputCategory.Exploration, WrathAccess.Input.InputCategory.Windows };
        private static readonly WrathAccess.Input.InputCategory[] UnfocusedCats =
            { WrathAccess.Input.InputCategory.Exploration, WrathAccess.Input.InputCategory.InGame, WrathAccess.Input.InputCategory.UI, WrathAccess.Input.InputCategory.Windows };
        // While we DON'T have control (cutscene, dialogue/storybook, loading, full-screen menus —
        // ControlState.HasControl), drop Exploration so movement/scanner/party go dead, but KEEP InGame
        // (Escape still opens the pause menu) and UI (the HUD stays reachable). Overlay audio idles in
        // parallel (OverlayManager.Active is gated on control too).
        private static readonly WrathAccess.Input.InputCategory[] NoControlCats =
            { WrathAccess.Input.InputCategory.InGame, WrathAccess.Input.InputCategory.UI };
        public override IReadOnlyList<WrathAccess.Input.InputCategory> InputCategories
            => !ControlState.HasControl ? NoControlCats
             : Navigation.HasFocus ? FocusedCats : UnfocusedCats;
        public override bool IsActive() => Game.Instance?.RootUiContext?.IsInGame ?? false;

        public override void OnUpdate()
        {
            // Hold/Stop/Stealth/AI are global party commands: announce their settled state changes whether
            // or not the HUD is focused (the hotkeys fire from unfocused exploration).
            PartyStateWatch.Tick();
        }


        public override void Build(GraphBuilder b)
        {
            BuildActionBar(b);
            BuildTurnPanel(b);
            BuildMenuBar(b);
            BuildWindows(b);
        }

        // ----- the action bar (stop 1) -----

        // The main slot list, then the game's Ability / Spell / Item groups as collapsible tree sections
        // (collapsed = just the header; expanded = every spell/ability/item the unit has — the source the
        // game drags onto the bar, so e.g. Charge is reachable + usable here). Slots key by their VM, so
        // focus follows a slot across renders and survives the game repopulating around it.
        private void BuildActionBar(GraphBuilder b)
        {
            b.PushContext(Loc.T("hud.action_bar"));
            var vm = ActionBar();

            b.SetRegion("bar:main");
            int i = 0, usable = 0;
            if (vm != null)
                foreach (var slot in vm.Slots)
                {
                    if (!Usable(slot)) { i++; continue; }
                    b.AddItem(ControlId.Referenced(slot, "bar:main:" + i), ActionBarNodes.Slot(slot));
                    i++; usable++;
                }
            if (usable == 0)
                b.AddItem(ControlId.Structural("bar:none"), GraphNodes.Text(() => Loc.T("hud.no_actions")));

            AddGroup(b, vm?.GroupAbilities, "hud.abilities");
            AddGroup(b, vm?.GroupSpells, "hud.spells");
            AddGroup(b, vm?.GroupItems, "hud.items");
            b.PopContext();
        }

        // One collapsible group (Abilities / Spells / Items): the group's usable slots under a folding
        // header (persistent expansion; collapsed by default like the game's fold-outs). Its own region,
        // so Ctrl+arrows jump between the bar's sections.
        private static void AddGroup(GraphBuilder b, List<ActionBarSlotVM> slots, string labelKey)
        {
            if (slots == null) return;
            bool any = false;
            foreach (var slot in slots) if (Usable(slot)) { any = true; break; }
            if (!any) return;

            b.SetRegion("bar:" + labelKey);
            b.BeginGroup(ControlId.Structural("bar:" + labelKey), GraphNodes.Group(() => Loc.T(labelKey)));
            int i = 0;
            foreach (var slot in slots)
            {
                if (!Usable(slot)) { i++; continue; }
                b.AddItem(ControlId.Referenced(slot, "bar:" + labelKey + ":" + i), ActionBarNodes.Slot(slot));
                i++;
            }
            b.EndGroup();
        }

        // ----- the turn-based Turn panel (stop 2; only in TB combat) -----

        // One vertical list: the status line and controls (End turn, Five foot step) at the top and the
        // initiative order beneath (Enter on a unit = delay your turn until after them). Out of combat
        // nothing is emitted, so the Tab cycle skips it entirely. Entries key by UNIT, so units joining,
        // dying, or delaying just re-render and focus follows the unit.
        private void BuildTurnPanel(GraphBuilder b)
        {
            if (!CombatMode.InTurnBased) return;
            b.BeginStop("turn").PushContext(Loc.T("hud.turn"), "list");

            b.AddItem(ControlId.Structural("turn:status"), GraphNodes.Text(() => CombatMode.StatusLine()));
            b.AddItem(ControlId.Structural("turn:end"), GraphNodes.Button(
                () => Loc.T("turn.end"), CombatMode.EndTurn));
            // The game's own localized control name + a live on/off state; the engage/step feedback is
            // CombatMode's own, so no generic click.
            b.AddItem(ControlId.Structural("turn:fivefoot"), GraphNodes.Button(
                () => (string)UIStrings.Instance.TurnBasedTexts.FiveFeetActionName + ", "
                    + Loc.T(CombatMode.FiveFootEngaged ? "value.on" : "value.off"),
                CombatMode.ToggleFiveFoot,
                () => CombatMode.FiveFootAvailable || CombatMode.FiveFootEngaged,
                sound: null));

            foreach (var u in InitiativeUnits())
            {
                var unit = u; // capture for the live closures
                b.AddItem(ControlId.Referenced(unit, "turn:u:" + unit.UniqueId), new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new List<NodeAnnouncement>
                    {
                        new NodeAnnouncement(() => RenderInitiative(unit)),
                    },
                    SearchText = () => unit.CharacterName,
                    // Enter = delay the acting unit's turn until after this unit (the game's tracker
                    // interaction; CombatMode gates it on CanDelay and raises the game's own modals).
                    OnActivate = () => CombatMode.DelayAfter(unit),
                });
            }
            b.PopContext();
        }

        // One initiative row — live text, so the active marker follows the turn without a rebuild.
        private static string RenderInitiative(UnitEntityData u)
        {
            string s = u.CharacterName + ", " + u.CombatState.Initiative;
            if (Game.Instance?.TurnBasedCombatController?.CurrentTurn?.Rider == u)
                s += ", " + Loc.T("combat.active_marker");
            return s;
        }

        // The units in initiative order, mirroring the game's own tracker (InitiativeTrackerVM.UpdateUnits):
        // SortedUnits is kept sorted by the game (surprise-round actors first, then initiative descending);
        // skip unprepared units and mounts (the rider represents the pair), and hide invisible units unless
        // they're the current unit, summoned, or inside another creature that isn't hidden.
        private static List<UnitEntityData> InitiativeUnits()
        {
            var list = new List<UnitEntityData>();
            if (!CombatMode.InTurnBased) return list;
            var tb = Game.Instance?.TurnBasedCombatController;
            if (tb == null) return list;
            var current = tb.CurrentTurn?.Rider;
            foreach (var u in tb.SortedUnits)
            {
                if (u == null || !u.CombatState.Prepared || u.GetRider() != null) continue;
                if (!u.IsVisibleForPlayer && u != current && !u.IsSummoned())
                {
                    var inside = u.Get<UnitPartInsideAnotherCreature>();
                    if (inside == null || (bool)inside.Owner.State.Features.Hidden) continue;
                }
                list.Add(u);
            }
            return list;
        }

        // ----- the menu-bar controls (stop 3) -----

        // The game-menu CONTROLS as one Tab-stop vertical list — a deliberate split from the game: the
        // window-openers, Log, and game menu live in the Windows list instead, so this holds only controls.
        // Order is the user's: pause, hold, stop, stealth, AI, select all, formation, rest, skip time,
        // turn-based, inspect, reset camera. Each drives the live IngameMenuVM method (+ the game's own
        // button-click sound); toggles carry the live on/off value part.
        private void BuildMenuBar(GraphBuilder b)
        {
            b.BeginStop("menu").PushContext(Loc.T("hud.menu"), "list");
            int i = 0;
            void Act(string key, System.Action call, System.Func<bool> enabled = null)
                => b.AddItem(ControlId.Structural("menu:" + i++), GraphNodes.Button(
                    () => Loc.T(key), () => { MenuClick(); call(); }, enabled, sound: null));

            // Toggles play their own switch sound and re-announce on press — like every other toggle.
            void Toggle(string key, System.Func<bool> on, System.Action call,
                System.Func<bool> enabled = null, bool announceOnActivate = true)
                => b.AddItem(ControlId.Structural("menu:" + i++), GraphNodes.Toggle(
                    () => Loc.T(key), on, call, enabled, announceOnActivate: announceOnActivate));

            // Pause is a toggle (paused / running); hidden in turn-based combat (the game shows it only
            // when !IsPauseButtonEnable, verified live) → grey it then.
            Toggle("hudmenu.pause", () => Game.Instance.IsPaused, () => IngameMenu()?.Pause(),
                () => !(IngameMenu()?.IsPauseButtonEnable.Value ?? false));
            // Hold / Stop / Stealth / AI reflect the game's own selection state, read LIVE from the source
            // (IsHold/IsStop = "all selected units …") rather than the IngameMenuVM reactive (which can lag
            // a frame). The settled state CHANGE is announced by PartyStateWatch (one watcher for hotkeys +
            // buttons + game-caused flips), so activation doesn't re-read the optimistic pre-settle value.
            Toggle("hudmenu.hold", () => Game.Instance?.UI?.SelectionManager?.IsHold() ?? false,
                () => IngameMenu()?.HandleHoldStateSwitched(), announceOnActivate: false);
            Toggle("hudmenu.stop", () => Game.Instance?.UI?.SelectionManager?.IsStop() ?? false,
                () => IngameMenu()?.HandleStopStateSwitched(), announceOnActivate: false);
            // Stealth + AI: the action-bar ControlCharactersVM toggles (sneak / AI control), grouped here
            // with the other selection-command toggles; the hotkeys (Ctrl+S / Ctrl+D) route through the
            // same ControlCharactersVM handlers.
            Toggle("hudmenu.stealth", PartySelection.IsStealthOn,
                () => ActionBar()?.ControlCharactersVM?.OnStealthClick(), announceOnActivate: false);
            Toggle("hudmenu.ai", PartySelection.IsAiOn,
                () => ActionBar()?.ControlCharactersVM?.OnAiClick(), announceOnActivate: false);
            Act("hudmenu.select_all", () => IngameMenu()?.SelectAll());
            Act("hudmenu.formation", () => IngameMenu()?.OpenFormation());
            // Rest = a targeted action (like an action-bar slot): Enter arms the game's rest-marker mode,
            // then the cursor aims + Enter places the camp. Labelled with the game's own Rest text.
            System.Func<string> restName = () => TextUtil.StripRichText(UIStrings.Instance.InGameMenuTexts.RestText);
            b.AddItem(ControlId.Structural("menu:" + i++), GraphNodes.Button(restName,
                () => { MenuClick(); Targeting.BeginRest(restName()); }, sound: null));
            Act("hudmenu.skip_time", () => IngameMenu()?.SkipTime());
            Toggle("hudmenu.turn_based", () => IngameMenu()?.IsTurnBasedEnabled.Value ?? false,
                () => IngameMenu()?.SwitchTBM(), () => !(IngameMenu()?.IsTurnBasedLocked.Value ?? false));
            Toggle("hudmenu.inspect", () => IngameMenu()?.IsInspectActive.Value ?? false,
                () => IngameMenu()?.OnInspectToggle());
            Act("hudmenu.reset_camera", () => Game.Instance.UI.GetCameraRig().SetCameraRotateDefault());
            b.PopContext();
        }

        // ----- the Windows list (stop 4) -----

        // Service-window buttons (the game's bottom bar): activating one calls the game's own open path
        // (HandleOpenWindowOfType, which creates the menu + toggles the window), labeled with the game's
        // own localized name. Our log review + the game menu (the gear) live here too, after the service
        // windows (the deliberate split: window-like destinations here, controls in the menu above).
        private void BuildWindows(GraphBuilder b)
        {
            b.BeginStop("windows").PushContext(Loc.T("hud.windows"), "list");
            int i = 0;
            foreach (var type in ServiceButtons)
            {
                var t = type; // capture for the live closures
                b.AddItem(ControlId.Structural("win:" + i), GraphNodes.Button(
                    () => UIUtility.GetServiceWindowsLabel(t),
                    () => ServiceWindows()?.HandleOpenWindowOfType(t)));
                i++;
            }
            b.AddItem(ControlId.Structural("win:log"), GraphNodes.Button(
                () => Loc.T("hud.log"), ModLogScreen.Open));
            b.AddItem(ControlId.Structural("win:menu"), GraphNodes.Button(
                () => Loc.T("hud.game_menu"),
                () => Kingmaker.PubSubSystem.EventBus.RaiseEvent(
                    delegate(Kingmaker.PubSubSystem.IEscMenuHandler h) { h.HandleOpen(); })));
            b.PopContext();
        }

        // ----- shared helpers -----

        // A real, usable action-bar slot: backed by a non-empty, non-bad mechanic.
        private static bool Usable(ActionBarSlotVM slot)
        {
            var m = slot?.MechanicActionBarSlot;
            return m != null && !(m is MechanicActionBarSlotEmpty) && !m.IsBad();
        }

        // The service windows we expose (in-game). Mythic / Equipment / SmartItem are conditional — added
        // later with their availability checks.
        private static readonly ServiceWindowsType[] ServiceButtons =
        {
            ServiceWindowsType.CharacterInfo, ServiceWindowsType.Inventory, ServiceWindowsType.Spellbook,
            ServiceWindowsType.Journal, ServiceWindowsType.Encyclopedia, ServiceWindowsType.LocalMap,
        };

        private static ServiceWindowsVM ServiceWindows()
        {
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            return rc?.InGameVM?.StaticPartVM?.ServiceWindowsVM;
        }

        private static ActionBarVM ActionBar()
        {
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            return rc?.InGameVM?.StaticPartVM?.ActionBarVM;
        }

        private static IngameMenuVM IngameMenu()
        {
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            return rc?.InGameVM?.StaticPartVM?.IngameMenuVM;
        }

        // The game's button-click sound (what each OwlcatButton plays on press) — fired alongside the VM
        // method so our menu-bar activations match a real press.
        private static void MenuClick() => Kingmaker.UI.UISoundController.Instance?.PlayButtonClickSound();
    }
}
