using System;
using UnityEngine;

namespace WrathAccess.Audio
{
    /// <summary>
    /// The audio backend. Two implementations — <see cref="NAudioEngine"/> (our own stereo mixer) and
    /// <see cref="WwiseEngine"/> (the game's native 3D engine) — selected by the <c>audio.engine</c>
    /// setting. Consumers route all audio through <see cref="Audio.Engine"/> rather than branching on the
    /// backend themselves; each engine spatializes in the way that's right for it.
    ///
    /// The surface grows as consumers migrate. So far: wall tones (the one construct that's spatialized
    /// engine-specifically — NAudio uses a fixed compass pan, Wwise places 3D emitters at the hit points,
    /// and we deliberately preserve both). One-shots and generic sound sources come next.
    /// </summary>
    internal interface IAudioEngine
    {
        /// <summary>Whether this backend can actually play right now (device open / bank loaded).</summary>
        bool Available { get; }

        /// <summary>Fire-and-forget positional one-shot. The caller computes the stereo placement and passes
        /// everything; each engine uses what it needs — NAudio plays <paramref name="file"/> at (volume, pan);
        /// Wwise posts a 3D emitter at <paramref name="worldPos"/> with volume (stem = bank event). Keeping
        /// the NAudio (volume, pan) authoritative means the stereo sound is unchanged; Wwise attenuates on top.</summary>
        void PlayOneShot(string stem, string file, Vector3 worldPos, float volume, float pan);

        /// <summary>The four directional wall-tone voices for a tone set, spatialized this engine's way.</summary>
        IWallTones CreateWallTones(string toneSet);
    }

    /// <summary>Four directional wall-tone voices (order N, S, E, W), driven every frame with the trace
    /// hit point + a 0..1 proximity volume per direction. Dispose stops + releases the voices.</summary>
    internal interface IWallTones : IDisposable
    {
        void Update(Vector3[] hits, float[] volumes); // both length 4, indices 0=N 1=S 2=E 3=W
    }
}
