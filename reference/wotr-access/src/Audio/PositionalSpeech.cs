using UnityEngine;
using WrathAccess.Exploration;          // Geo
using WrathAccess.Speech;               // SpeechAudio

namespace WrathAccess.Audio
{
    /// <summary>
    /// Plays rendered speech PCM through the MOD's own NAudio mixer (<see cref="NAudioEngine"/>'s shared
    /// output) so utterances OVERLAP — many combat readouts at once — instead of queuing on the screen
    /// reader. Positioned with the SAME pan/volume model as the sonar's classic path (north-up frame,
    /// distance attenuation), relative to the listener (cursor if placed, else the player). NAudio-only:
    /// Wwise plays baked WAVs, not runtime TTS PCM.
    /// </summary>
    internal static class PositionalSpeech
    {
        private const float RefDistFeet = 10f;  // distance at which volume is ~half
        private const float PanWidthFeet = 10f; // lateral crossover (matches sonar)
        private const float MinVol = 0.2f;

        /// <summary>Play rendered speech; positioned at <paramref name="worldPos"/> (pan + distance volume)
        /// or centred when null. Concurrent calls mix (no queuing).</summary>
        public static void Play(SpeechAudio audio, Vector3? worldPos)
        {
            if (audio == null) return;
            if (worldPos == null) { AudioEngines.NAudio.PlayPcm(audio, 1f, 0f); return; } // centred, still overlapping

            var from = Listener();
            var p = worldPos.Value;
            float dx = p.x - from.x, dz = p.z - from.z;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            float refDist = RefDistFeet * Geo.MetresPerFoot;
            float panWidth = PanWidthFeet * Geo.MetresPerFoot;
            float vol = Mathf.Clamp(refDist / (refDist + dist), MinVol, 1f);
            float pan = Mathf.Clamp(dx / Mathf.Max(dist, panWidth), -1f, 1f);
            AudioEngines.NAudio.PlayPcm(audio, vol, pan);
        }

        // The listener "ears" — the same reference the scanner/cursor work from (cursor if placed, else player).
        private static Vector3 Listener()
            => WrathAccess.Exploration.Cursor.Has
                ? WrathAccess.Exploration.Cursor.Position.Value
                : WrathAccess.Exploration.Overlays.Cursor.PlayerPosition;
    }
}
