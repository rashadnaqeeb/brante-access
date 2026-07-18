using Kingmaker;
using Kingmaker.GameModes;
using Kingmaker.Sound;               // DefaultListener (the game's single Wwise listener)
using Owlcat.Runtime.Core.Registry;  // ObjectRegistry
using UnityEngine;
using WrathAccess.Settings;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// The "virtual head" — move the EARS, not the camera (user-designed). The game's 3D audio is
    /// heard from a single Wwise listener (<see cref="DefaultListener"/>), normally snapped to the
    /// camera by <c>AudioListenerPositionController</c> every LateUpdate — which puts footsteps,
    /// combat, barks and ambience in a camera-relative frame that disagrees with our sonification
    /// (sonar/wall tones pan from the cursor, compass-stable). This component runs at +10000, AFTER
    /// the game's controller, and re-snaps the listener onto our reference point (the cursor, else
    /// the leader; or always the leader) at ear height with a fixed NORTH-facing orientation (+Z,
    /// matching Geo's compass) — one spatial frame for everything. While we write, we win; the
    /// moment we stop — cutscenes, dialogs, rest, or the "camera" setting — the game's own per-frame
    /// snap restores vanilla camera audio with zero cleanup. The camera itself is never touched, so
    /// visuals and scripted sequences are unaffected.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    internal sealed class ListenerAnchor : MonoBehaviour
    {
        // Height above the anchor, user-tunable: the game's attenuation curves are calibrated for a
        // listener hanging well above the field (the camera boom), so ear-height made medium-range
        // things sound too close. Straight up keeps the compass symmetric (no north/south bias).
        private static float HeightMetres =>
            (ModSettings.GetSetting<IntSetting>("audio.listener_height")?.Get() ?? 35) * Geo.MetresPerFoot;

        private void LateUpdate()
        {
            var game = Game.Instance;
            if (game == null || game.CurrentlyLoadedArea == null) return;

            var choice = ModSettings.GetSetting<ChoiceSetting>("audio.listener")?.ValueId ?? "cursor";
            if (choice == "camera") return; // vanilla: leave the game's camera snap in charge

            // Cutscenes/dialogs/rest are framed and mixed for the camera, and the player isn't
            // navigating — fall back by simply not overriding.
            if (game.CutsceneLock.Active) return;
            var mode = game.CurrentMode;
            if (mode == GameModeType.Cutscene || mode == GameModeType.Dialog || mode == GameModeType.Rest) return;

            var listener = ObjectRegistry<DefaultListener>.Instance?.MaybeSingle;
            if (listener == null) return;

            var anchor = (choice == "cursor" && Cursor.Has)
                ? Cursor.Position.Value
                : Overlays.Cursor.PlayerPosition; // the TB-aware reference (acting unit in turn-based)
            listener.transform.SetPositionAndRotation(anchor + Vector3.up * HeightMetres, Quaternion.identity);
        }
    }
}
