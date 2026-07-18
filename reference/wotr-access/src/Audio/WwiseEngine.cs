using System;
using UnityEngine;

namespace WrathAccess.Audio
{
    /// <summary>
    /// The game's native Wwise backend (3D emitters through the same engine/listener/attenuation as the
    /// game). A thin adapter over the existing <see cref="WwiseAudio"/> statics.
    /// </summary>
    internal sealed class WwiseEngine : IAudioEngine
    {
        public bool Available => WwiseAudio.Active; // bank loaded AND the engine setting isn't "classic"

        // 3D emitter at worldPos via the bank event (stem); file/pan are the NAudio inputs, ignored here.
        public void PlayOneShot(string stem, string file, Vector3 worldPos, float volume, float pan)
            => WwiseAudio.TryPost(stem, worldPos, volume);

        public IWallTones CreateWallTones(string toneSet) => new WallTones(toneSet);

        // Four looping 3D emitters, one per direction, placed at the traced wall hit points each frame.
        private sealed class WallTones : IWallTones
        {
            private static readonly string[] Dirs = { "north", "south", "east", "west" };
            private readonly WwiseAudio.Loop[] _loops = new WwiseAudio.Loop[4];

            public WallTones(string toneSet)
            {
                // Start silent at the origin; the first Update repositions + unmutes (StartLoop starts at 0
                // volume, so the placeholder position is never audible).
                for (int i = 0; i < _loops.Length; i++)
                    _loops[i] = WwiseAudio.StartLoop("walltones_" + toneSet + "_" + Dirs[i], Vector3.zero);
            }

            public void Update(Vector3[] hits, float[] volumes)
            {
                for (int i = 0; i < _loops.Length; i++)
                    WwiseAudio.UpdateLoop(_loops[i],
                        i < hits.Length ? hits[i] : Vector3.zero,
                        i < volumes.Length ? volumes[i] : 0f);
            }

            public void Dispose()
            {
                for (int i = 0; i < _loops.Length; i++) { WwiseAudio.StopLoop(_loops[i]); _loops[i] = null; }
            }
        }
    }
}
