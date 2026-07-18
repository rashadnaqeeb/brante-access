using UnityEngine;
using WrathAccess.Input;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// Turn-based path feedback: the instant the cursor stops after moving (movement keys released, position
    /// no longer changing), speak whether the acting unit has a path to that spot and how long the walk is —
    /// "Path, 25 feet" (+ "beyond remaining movement" when it overruns this turn's budget), or "No path" when
    /// the spot is unreachable (the pathfinder only gets a partial route toward it). Armed by a small cursor
    /// delta and fired once per stop, so an idle cursor never repeats; silent outside turn-based combat. Uses
    /// the game's own pathfinder via <see cref="CombatMode.TryPathInfo"/> — the same path a move would walk.
    /// </summary>
    internal sealed class PathInfoSystem : OverlaySystem
    {
        public override string Name => "Path info";
        public override string Key => "path";

        // Fires when the cursor STOPS after moving, so "when moving" would suppress it — Off/Continuous only.
        public override System.Collections.Generic.IReadOnlyList<OverlayMode> SupportedModes => OverlayModes.OffContinuous;

        private Vector3 _last;
        private bool _has;     // _last is valid
        private bool _armed;   // cursor moved since the last announce

        private const float DeltaSqr = 0.0001f; // arm on a ~0.01-unit move (the cursor never drifts on its own)

        // Reachable means the path actually arrives at the spot; the pathfinder's fallback partial path
        // ends well short. Half a tile of slack covers node snapping.
        private const float ReachToleranceMeters = 1.0f;

        public override void OnExit(Overlay overlay) { _has = false; _armed = false; }

        public override void Tick(float dt, Overlay overlay)
        {
            if (!OverlayManager.Active || !ShouldPlay(overlay) || !CombatMode.InTurnBased) { _has = false; _armed = false; return; }
            if (Targeting.Aiming) { _has = false; _armed = false; return; } // aiming: the AoE preview owns cursor stops
            if (WrathAccess.UI.Navigation.HasFocus) return; // HUD owns the arrows — freeze, don't fire

            var p = overlay.Cursor.Position;
            if (!_has) { _has = true; _last = p; _armed = false; return; }
            if ((p - _last).sqrMagnitude > DeltaSqr) { _last = p; _armed = true; return; }
            if (!_armed || MoveKeyHeld()) return; // not moved yet / still driving
            _armed = false;
            Announce(p);
        }

        private static void Announce(Vector3 dest)
        {
            if (!CombatMode.TryPathInfo(dest, out float len, out float gap, out float moveAction, out float total)
                || gap > ReachToleranceMeters)
            {
                Tts.Speak(Message.Localized("ui", "path.none").Resolve());
                return;
            }
            int feet = Mathf.RoundToInt(len / Geo.MetresPerFoot);
            string line = Message.Localized("ui", "path.distance", new { feet }).Resolve();
            // Mirror the game's break markers: within the move action → plain; past it but reachable →
            // it costs the standard action too; past everything → not reachable this turn.
            if (len > total + 0.05f) line += ", " + Message.Localized("ui", "path.beyond").Resolve();
            else if (len > moveAction + 0.05f) line += ", " + Message.Localized("ui", "path.uses_standard").Resolve();
            Tts.Speak(line);
        }

        private static bool MoveKeyHeld()
            => InputManager.Held("explore.cursorUp") || InputManager.Held("explore.cursorDown")
            || InputManager.Held("explore.cursorLeft") || InputManager.Held("explore.cursorRight")
            || InputManager.Held("explore.secondaryUp") || InputManager.Held("explore.secondaryDown")
            || InputManager.Held("explore.secondaryLeft") || InputManager.Held("explore.secondaryRight");
    }
}
