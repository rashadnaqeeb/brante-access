using Kingmaker;
using Kingmaker.Controllers.Clicks;          // PointerMode
using Kingmaker.Controllers.Clicks.Handlers; // ClickMapObjectHandler, PlaceRestMarkerHandler
using Kingmaker.Controllers.Rest;            // RestHelper
using Kingmaker.EntitySystem.Entities;       // UnitEntityData
using Kingmaker.View.MapObjects;             // CampPlaceView
using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// Targeting for the rest camp — the HUD Rest button as a position-targeted action. Activation drives the
    /// game's OWN Rest flow: <see cref="RestHelper.TryStartRest"/> applies the gates (combat / no-camp zone /
    /// scripted no-rest, spoken by WarningReader) and, when allowed, arms the rest-marker pointer mode —
    /// exactly as clicking the bonfire does. <see cref="Active"/> is read LIVE from that pointer mode, so if
    /// resting is refused (the marker never arms) or the mode is dropped, we're simply not aiming: no stale
    /// flag, and no fighting the flow by clearing the mode and force-placing a camp where the game wouldn't.
    /// Committing places the camp through <see cref="PlaceRestMarkerHandler.OnClick"/> (its CanPlace + enemies
    /// checks and refusals); <see cref="Tick"/> then drives the spawned camp's interaction (RestPart walks the
    /// party over and the rest UI opens on arrival).
    /// </summary>
    internal sealed class RestTargeting : ITargetingMode
    {
        private int _pendingInteract; // frames left to wait for the freshly spawned camp view

        // Live state: we're aiming exactly while the game sits in the rest-marker pointer mode (which
        // TryStartRest armed). Reading the game directly means it can't go stale.
        public bool Active => Game.Instance?.ClickEventsController?.Mode == PointerMode.RestMarker;

        /// <summary>Activate (Enter on the Rest button). TryStartRest is both the gate AND what arms the
        /// marker; when it refuses it returns false after the spoken warning, so we never enter aim — the
        /// game shows no targeting cursor either. <paramref name="name"/> is the game's localized Rest label,
        /// announced as the targeting prompt.</summary>
        public void Begin(string name)
        {
            if (Game.Instance == null) return;
            if (!RestHelper.TryStartRest()) return; // blocked → warning shown, pointer mode stays Default
            // TryStartRest synchronously armed PointerMode.RestMarker (via RestContextVM.HandleRestShowRequest),
            // so Active is already true here.
            if (name != null) Tts.Speak(Loc.T("target.begin", new { name }), interrupt: true);
        }

        // Rest is a POINT target — ignore the unit. The game's marker only places on a valid point: OnClick
        // re-checks CanPlace + enemies and raises the refusal (spoken by WarningReader). A red spot just won't
        // place — we say so and stay in aim (the mode is untouched) so the player can pick another point.
        public void CommitAt(UnitEntityData unit, Vector3 point)
        {
            var h = Game.Instance?.PlaceRestMarkerHandler;
            if (h == null) return;
            if (!h.CanPlace(point)) { Tts.Speak(Loc.T("rest.no_spot"), interrupt: true); return; }
            h.CursorValid = true; // the game's mouse-hover validator sets this on a valid point; we vouch for ours
            if (!h.OnClick(null, point, 0)) return; // enemies nearby etc. → the game's warning (mode stays armed)
            // OnClick succeeded: it spawned the camp and cleared the pointer mode (so Active is false now).
            Tts.Speak(Loc.T("rest.making_camp"), interrupt: false);
            _pendingInteract = 30; // the camp view appears within a frame or two
        }

        public void Cancel()
        {
            Game.Instance?.ClickEventsController?.ClearPointerMode();
            Tts.Speak(Loc.T("target.cancelled"), interrupt: true);
        }

        /// <summary>Per-frame: once the placed camp's view exists, trigger its interaction — the same path as
        /// clicking the campfire (RestPart gathers the party and walks everyone to camp).</summary>
        public void Tick()
        {
            if (_pendingInteract <= 0) return;
            var view = CampPlaceView.PlayerPlacedInstance;
            if (view == null) { _pendingInteract--; return; }
            _pendingInteract = 0;

            var sc = Game.Instance?.SelectionCharacter;
            if (sc != null && (sc.SelectedUnits == null || sc.SelectedUnits.Count == 0))
                Game.Instance.UI?.SelectionManager?.SelectAll();
            var units = sc?.SelectedUnits;
            if (units == null || units.Count == 0) return;
            ClickMapObjectHandler.Interact(view.gameObject, units, forceOvertipInteractions: true);
        }
    }
}
