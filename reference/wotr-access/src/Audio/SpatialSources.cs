using System;
using System.Collections.Generic;
using UnityEngine;

namespace WrathAccess.Audio
{
    /// <summary>
    /// Keeps positional one-shots alive as scene SOURCES: while a voice is still audible, it's re-spatialised
    /// every frame against the moving listener (the cursor), so pan / gain / ITD / front-back filter follow the
    /// cursor instead of freezing at fire time. Ticked on the main thread (where game state + settings are safe
    /// to read); the per-voice updates are smoothed inside <see cref="ISpatialVoice"/> so movement never clicks.
    ///
    /// A source is described by three live functions so the caller keeps all the geometry/volume maths:
    /// <paramref name="listener"/> (where it's heard from now), <paramref name="sourceAt"/> (the source world
    /// point given the listener — lets a wall track its nearest point, a unit just return its centre), and
    /// <paramref name="gain"/> (distance → volume, including the system's own falloff + volume setting).
    /// Self-cleaning: when a voice reports Finished (drained) it's dropped; a throwing function drops it too.
    /// </summary>
    internal static class SpatialSources
    {
        private sealed class Src
        {
            public ISpatialVoice Voice;
            public Func<Vector3> Listener;
            public Func<Vector3, Vector3> SourceAt;
            public Func<float, float> Gain;
            public float PanWidth;
        }

        // Main-thread only (Play is called from the overlay tick, Tick from the frame loop) — no lock needed.
        private static readonly List<Src> _live = new List<Src>();

        /// <summary>Fire a tracked positional one-shot. Returns immediately; the voice is then re-placed each
        /// frame until it finishes. No-op if the engine couldn't start the voice.</summary>
        public static void Play(string file, Func<Vector3> listener, Func<Vector3, Vector3> sourceAt,
            Func<float, float> gain, float panWidth)
        {
            if (listener == null || sourceAt == null || gain == null) return;
            try
            {
                var c = listener();
                var s = sourceAt(c);
                float dx = s.x - c.x, dz = s.z - c.z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                var voice = AudioEngines.NAudio.PlaySpatial(file, gain(dist), dx, dz, panWidth);
                if (voice == null) return;
                _live.Add(new Src { Voice = voice, Listener = listener, SourceAt = sourceAt, Gain = gain, PanWidth = panWidth });
            }
            catch (Exception e) { Main.Log?.Error("[spatial-src] play " + file + " — " + e); }
        }

        /// <summary>Re-spatialise every live source against the current listener. Drops finished voices.</summary>
        public static void Tick()
        {
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var src = _live[i];
                if (src.Voice.Finished) { _live.RemoveAt(i); continue; }
                try
                {
                    var c = src.Listener();
                    var s = src.SourceAt(c);
                    float dx = s.x - c.x, dz = s.z - c.z;
                    float dist = Mathf.Sqrt(dx * dx + dz * dz);
                    src.Voice.SetPlacement(Spatializer.Cue(dx, dz, src.PanWidth), src.Gain(dist));
                }
                catch { _live.RemoveAt(i); } // a stale/destroyed source — let the voice drain on its own
            }
        }
    }
}
