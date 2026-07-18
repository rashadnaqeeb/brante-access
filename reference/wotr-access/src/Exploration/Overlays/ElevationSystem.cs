using UnityEngine;
using WrathAccess.Audio;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// Slope feedback: a continuous tone whose pitch tracks the cursor's cumulative elevation as it glides
    /// over the navmesh, so climbing glides the pitch up (to a cap) and descending glides it down (to a
    /// floor). Because pitch integrates the height change, the *rate* the pitch moves reflects the slope's
    /// steepness — a steep ramp climbs fast, a gentle one drifts. The tone is silent on flat ground and
    /// resets to centre when the slope ends, so each ascent/descent reads on its own. Self-gates on the
    /// overlay being active.
    /// </summary>
    internal sealed class ElevationSystem : AudioSystem
    {
        public override string Name => "Slope";
        public override string Key => "slope";

        private readonly ToneEngine _tone = new ToneEngine();
        private bool _has;
        private Vector3 _prev;
        private float _pitch;            // cumulative semitones from centre, clamped to ±range
        private float _silence = 999f;   // seconds since the last real elevation change

        private const float BaseHz = 392f;     // centre pitch (~G4)
        private const float JitterFt = 0.005f; // per-frame change below this is numerical noise on flat ground
        private const float TeleportFt = 10f;  // per-frame jump above this = recenter/teleport, not a slope/step
        private const float HoldSec = 0.25f;   // keep voicing this long after the last change (so a brief step
                                               // or a single stair sustains instead of a one-frame blip)
        private const float ToneGain = 0.35f;  // a comfortable continuous-tone level (× the volume setting)

        protected override void RegisterAudioSettings(WrathAccess.Settings.CategorySetting cat)
        {
            cat.Add(new WrathAccess.Settings.IntSetting("sensitivity", "Slope sensitivity (semitones/foot)", 2, 1, 12, 1, "overlay.slope.sensitivity"));
            cat.Add(new WrathAccess.Settings.IntSetting("range", "Pitch range (semitones)", 18, 6, 36, 2, "overlay.slope.range"));
        }

        public override void OnExit(Overlay overlay) { _tone.Silence(); _has = false; _pitch = 0f; _silence = 999f; }

        public override void Tick(float dt, Overlay overlay)
        {
            // Silent without control (cutscene): same as the other spatial-audio overlays.
            if (!OverlayManager.Active || !ShouldPlay(overlay) || !WrathAccess.ControlState.HasControl) { _tone.Silence(); _has = false; _pitch = 0f; _silence = 999f; return; }

            var p = overlay.Cursor.Position;
            if (!_has) { _prev = p; _has = true; _silence = 999f; _tone.Silence(); return; }
            float dyFeet = (p.y - _prev.y) / Geo.MetresPerFoot;
            _prev = p;

            // A real elevation change this frame (above flat-ground noise, below a teleport/recenter jump):
            // fold it into the cumulative pitch — even a sub-second step nudges it. Otherwise count silence.
            if (Mathf.Abs(dyFeet) > JitterFt && Mathf.Abs(dyFeet) < TeleportFt)
            {
                int range = Int("range", 18);
                _pitch = Mathf.Clamp(_pitch + dyFeet * Int("sensitivity", 2), -range, range);
                _silence = 0f;
            }
            else _silence += dt;

            // Voice for HoldSec after the last change (so a brief step sustains, not blips); once it's been
            // quiet past the fade, snap pitch back to centre while inaudible so the next slope starts fresh.
            bool voiced = _silence < HoldSec;
            if (_silence > HoldSec + 0.1f) _pitch = 0f;
            _tone.Set(BaseHz * Mathf.Pow(2f, _pitch / 12f), voiced ? EffectiveVolume * ToneGain : 0f);
        }
    }
}
