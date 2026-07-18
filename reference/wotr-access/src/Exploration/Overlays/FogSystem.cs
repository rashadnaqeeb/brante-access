using System.IO;
using Kingmaker.Controllers; // FogOfWarController
using WrathAccess.Audio;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// A one-shot cue when the cursor crosses the fog-of-war boundary (enter / exit). Self-gates on
    /// <see cref="OverlayManager.Active"/>; the baseline resets when inactive so re-activating doesn't fire
    /// a spurious cue. (The spoken "fog of war" status lives in the tile readout for now.)
    /// </summary>
    internal sealed class FogSystem : AudioSystem
    {
        public override string Name => "Fog cue";
        public override string Key => "fog";

        // A crossing event — "when moving" can't differ from "continuous" (you only cross by moving).
        public override System.Collections.Generic.IReadOnlyList<OverlayMode> SupportedModes => OverlayModes.OffContinuous;

        private bool? _wasFogged; // null = no baseline yet (don't fire on the first sample)

        public override void OnExit(Overlay overlay) => _wasFogged = null;

        public override void Tick(float dt, Overlay overlay)
        {
            // Silent without control (cutscene): same as the other spatial-audio overlays.
            if (!OverlayManager.Active || !ShouldPlay(overlay) || !WrathAccess.ControlState.HasControl) { _wasFogged = null; return; }

            var c = overlay.Cursor.Position;
            bool fogged = FogOfWarController.IsInFogOfWar(c);
            if (_wasFogged.HasValue && fogged != _wasFogged.Value)
                AudioEngines.NAudio.Play2D(Path.Combine(OverlayAudio.Dir, fogged ? "fog_enter.wav" : "fog_exit.wav"), EffectiveVolume);
            _wasFogged = fogged;
        }
    }
}
