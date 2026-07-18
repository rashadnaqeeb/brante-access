using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.Globalmap.Blueprints; // BlueprintGlobalMapPoint
using Kingmaker.Globalmap.View;
using Kingmaker.PubSubSystem; // IEscMenuHandler
using Kingmaker.UI.MVVM._VM.GlobalMap.Message; // GlobalMapEnterMessageVM
using UnityEngine;
using WrathAccess.Exploration; // GlobalMapModel, GlobalMapActions, GlobalMapScanner, GlobalMapEnterPanel, Geo
using WrathAccess.Input; // InputCategory
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The world map (global map) base context, graph-native. Browse a Tab-stop location list (arrow,
    /// Enter selects), the isolated <see cref="GlobalMapScanner"/> review cursor (PageUp/Down + b/m/n +
    /// . , armies), and the free movement cursor (WASD). Selecting a node (Enter / I / a list item →
    /// <see cref="GlobalMapActions.Go"/>) triggers the game's REAL node-select, and its location panel
    /// (<see cref="GlobalMapEnterMessageVM"/>) then appears as the FIRST <b>tab stop</b> here —
    /// description + Travel/Enter/Manage/Close — which the player tabs to and acts on. The game
    /// disposes+recreates that VM on open, so the panel is location-keyed with a short grace before
    /// dropping (the recreate churn doesn't flicker it) and its texts/labels are captured per location
    /// (the actions still resolve the LIVE VM each press).
    /// </summary>
    public sealed class GlobalMapScreen : Screen
    {
        public override string Key => "ctx.globalmap";
        public override string ScreenName => Loc.T("screen.world_map");
        public override int Layer => 0; // base context, like ctx.ingame

        public override bool IsActive() => GlobalMapModel.Active;

        // Starts unfocused: arrows/WASD drive the movement cursor and Tab enters the lists — like the in-game
        // screen. Category order flips with focus; the scanner/review/cursor letter+page keys are WorldMap-only.
        public override bool StartUnfocused => true;
        private static readonly IReadOnlyList<InputCategory> Focused = new[] { InputCategory.UI, InputCategory.WorldMap, InputCategory.Windows };
        private static readonly IReadOnlyList<InputCategory> Unfocused = new[] { InputCategory.WorldMap, InputCategory.UI, InputCategory.Windows };
        // Without control (a world-map book event / dialogue), drop Windows so the service-window hotkeys go
        // dead there too, exactly as they do in an area (see InGameScreen). WorldMap/UI stay for browsing.
        private static readonly IReadOnlyList<InputCategory> FocusedNoControl = new[] { InputCategory.UI, InputCategory.WorldMap };
        private static readonly IReadOnlyList<InputCategory> UnfocusedNoControl = new[] { InputCategory.WorldMap, InputCategory.UI };
        public override IReadOnlyList<InputCategory> InputCategories
        {
            get
            {
                bool ctrl = ControlState.HasControl;
                return Navigation.HasFocus ? (ctrl ? Focused : FocusedNoControl)
                                           : (ctrl ? Unfocused : UnfocusedNoControl);
            }
        }

        public override bool AllowsTypeahead => false; // letters are world-map hotkeys (b/m/n, i), not type-ahead

        /// <summary>True while the location panel tab stop is shown (grace-stable). The world-map cursor +
        /// sonar system check this and freeze, so they don't run while the player reads/acts on the panel.</summary>
        public static bool PanelActive { get; private set; }

        // The location list's ORDER, frozen at map entry (nearest-first from where you arrived) — live
        // distance sorting would shuffle the list under the cursor as the traveler moves. The SET still
        // reads live each render; only the ordering is a per-entry presentation choice (as before).
        private List<GlobalMapPointView> _order;

        // The location panel's grace-stabilized state: which location it's for (null = none), the capture
        // of its location-stable texts/labels, and the drop deadline absorbing the game's transient nulls.
        private BlueprintGlobalMapPoint _panelLoc;
        private float _panelClearAt;
        private bool _wasPaused; // last frame's travel-pause state (announce on the transition)
        private string _lore, _desc, _acceptLabel, _manageLabel, _closeLabel;
        private bool _acceptEnabled, _hasSettlement;

        public override void OnPush()
        {
            _order = null; ClearPanelState();
            GlobalMapScanner.Reset(); GlobalMapCursor.Reset(); // the sonar is an overlay system now (resets on overlay exit)
        }
        public override void OnPop() { _order = null; ClearPanelState(); }

        private void ClearPanelState() { _panelLoc = null; _panelClearAt = 0f; PanelActive = false; _wasPaused = false; }

        public override void OnUpdate()
        {
            SyncPanel();
            SyncTravelPause();
        }

        // The game pauses travel mid-journey on a discovery/event (its move-helper shows Continue). Announce
        // the pause once on the transition so the player knows to resume (Enter on the cursor → resume); the
        // discovery line itself is read by the Log overlay system. Skip when a location panel is up (a user
        // select also pauses travel, but the panel already has focus).
        private void SyncTravelPause()
        {
            bool paused = GlobalMapModel.TravelPaused;
            if (paused && !_wasPaused && !PanelActive) Tts.Speak(Loc.T("worldmap.travel_paused"));
            _wasPaused = paused;
        }


        public override void Build(GraphBuilder b)
        {
            if (!GlobalMapModel.Active) return;
            BuildPanel(b);    // when open: the FIRST stop, so Tab from the map lands on it directly
            BuildLocations(b);
        }

        private void BuildLocations(GraphBuilder b)
        {
            if (_order == null)
            {
                var from = GlobalMapModel.TravelerPos;
                _order = GlobalMapModel.Locations.OrderBy(p => Geo.Distance(from, p.transform.position)).ToList();
            }
            // Live set ∩ frozen order: locations key by their view, labels/actions read live.
            var live = new HashSet<GlobalMapPointView>(GlobalMapModel.Locations);
            b.BeginStop("locations").PushContext(Loc.T("worldmap.locations"), "list");
            int i = 0;
            foreach (var p in _order)
            {
                if (p == null || !live.Contains(p)) { i++; continue; }
                var pv = p; // capture per-iteration for the closures
                b.AddItem(ControlId.Referenced(pv, "loc:" + i), GraphNodes.Button(
                    () => GlobalMapActions.Label(pv), () => GlobalMapActions.Go(pv)));
                i++;
            }
            b.PopContext();
        }

        // ---- the location panel, as a tab stop synced to the game's live GlobalMapEnterMessageVM ----

        private static GlobalMapEnterMessageVM PanelVm()
        {
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            return rc?.GlobalMapVM?.GlobalMapEnterMessageVM?.Value;
        }

        // Track the panel VM as it comes and goes — keyed on the LOCATION so the game's open-time
        // dispose+recreate churn (same location) doesn't re-key it, with a short grace before dropping to
        // absorb the transient nulls. Captures the location-stable texts/labels at open (the VM instance
        // is churned under us; the ACTIONS resolve the live VM each press).
        private void SyncPanel()
        {
            var vm = PanelVm();
            var loc = vm != null && vm.Location != null ? vm.Location.Blueprint : null;
            if (loc != null)
            {
                _panelClearAt = 0f;
                if (loc != _panelLoc) OpenPanel(loc, vm);
                return;
            }
            if (_panelLoc == null) return;
            if (_panelClearAt == 0f) _panelClearAt = Time.unscaledTime + 0.25f;
            else if (Time.unscaledTime >= _panelClearAt) { ClearPanelState(); }
        }

        private void OpenPanel(BlueprintGlobalMapPoint loc, GlobalMapEnterMessageVM vm)
        {
            _panelLoc = loc;
            _panelClearAt = 0f;
            PanelActive = true;

            // Location lore first (what the place is), then the game-panel body (travel time / enter
            // confirmation / closed or restricted reason). Both are location-stable, captured here.
            _lore = GlobalMapEnterPanel.LocationDescription(vm);
            GlobalMapEnterPanel.Compute(vm, out _desc, out _acceptEnabled);
            _acceptLabel = TextUtil.StripRichText(GlobalMapEnterPanel.AcceptLabel(vm));
            _hasSettlement = GlobalMapEnterPanel.HasSettlement(vm);
            _manageLabel = TextUtil.StripRichText(GlobalMapEnterPanel.ManageLabel());
            _closeLabel = TextUtil.StripRichText(GlobalMapEnterPanel.CloseLabel());

            Tts.Speak(Loc.T("worldmap.selected", new { name = GlobalMapEnterPanel.Title(vm) })); // signal it's there
        }

        private void BuildPanel(GraphBuilder b)
        {
            if (_panelLoc == null) return;
            string k = "panel:" + _panelLoc.GetHashCode() + ":"; // re-keys per location
            b.BeginStop("panel").PushContext(Loc.T("worldmap.panel"), "list"); // "Location options" on Tab-in

            if (!string.IsNullOrWhiteSpace(_lore))
                b.AddItem(ControlId.Structural(k + "lore"), GraphNodes.Text(() => _lore));
            if (!string.IsNullOrWhiteSpace(_desc))
                b.AddItem(ControlId.Structural(k + "desc"), GraphNodes.Text(() => TextUtil.StripRichText(_desc)));

            // Each action fires the game's button-click sound + the VM method the real OwlcatButton is
            // wired to (Accept/AlternativeAction/Close) — same behavior as pressing the button (see
            // GlobalMapEnterMessagePCView), resolving the LIVE VM each press, never a stale capture.
            b.AddItem(ControlId.Structural(k + "accept"), GraphNodes.Button(
                () => _acceptLabel, AcceptLive, () => _acceptEnabled, sound: null));
            if (_hasSettlement)
                b.AddItem(ControlId.Structural(k + "manage"), GraphNodes.Button(
                    () => _manageLabel, () => { PlayClick(); PanelVm()?.AlternativeAction(); }, sound: null));
            b.AddItem(ControlId.Structural(k + "close"), GraphNodes.Button(
                () => _closeLabel, () => { PlayClick(); PanelVm()?.Close(); }, sound: null));
            b.PopContext();
        }

        // The default button-click sound the OwlcatButton plays on a left-click (UISoundController, exactly as
        // the game does it), so our VM-driven actions sound identical to a real button press.
        private static void PlayClick() => Kingmaker.UI.UISoundController.Instance?.PlayButtonClickSound();

        // Travel / Enter on the LIVE VM (what the Accept OwlcatButton is wired to), with its click sound.
        // Confirm the outcome for the player case; a selected crusade army gets the game's own "set
        // destination" warning, so stay quiet there to avoid doubling it.
        private static void AcceptLive()
        {
            var vm = PanelVm();
            if (vm == null) return;
            PlayClick();
            var army = Game.Instance.GlobalMapController != null ? Game.Instance.GlobalMapController.SelectedArmy : null;
            bool entering = vm.IsCurrentLocation;
            var name = GlobalMapEnterPanel.Title(vm);
            vm.Accept();
            if (army == null) Tts.Speak(Loc.T(entering ? "worldmap.entering" : "worldmap.traveling", new { name }));
        }

        // Escape opens the game menu (the game's own EscManager is muted while focus mode owns the keyboard).
        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "hud.game_menu"),
                _ => EventBus.RaiseEvent(delegate (IEscMenuHandler h) { h.HandleOpen(); }));
        }
    }
}
