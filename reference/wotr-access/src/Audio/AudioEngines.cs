using WrathAccess.Settings;

namespace WrathAccess.Audio
{
    /// <summary>
    /// Entry point to the active audio backend. <see cref="Active"/> resolves the <c>audio.engine</c>
    /// setting ("classic" = our NAudio mixer, anything else = the game's Wwise engine) and falls back to
    /// NAudio whenever Wwise isn't available (bank not yet loaded). Both engine instances are cached, so a
    /// consumer can compare the returned reference to detect a live engine swap and rebuild its voices.
    /// (Named <c>AudioEngines</c> rather than <c>Audio</c> to avoid colliding with the namespace.)
    /// </summary>
    internal static class AudioEngines
    {
        private static NAudioEngine _naudio;
        private static WwiseEngine _wwise;

        public static IAudioEngine Active
        {
            get
            {
                bool wantWwise = ModSettings.GetSetting<ChoiceSetting>("audio.engine")?.ValueId != "classic";
                if (wantWwise)
                {
                    if (_wwise == null) _wwise = new WwiseEngine();
                    if (_wwise.Available) return _wwise;
                }
                if (_naudio == null) _naudio = new NAudioEngine();
                return _naudio;
            }
        }

        /// <summary>The NAudio engine specifically, for non-positional UI/cue sounds (control chime, object /
        /// fog cues) — always stereo-centred, not part of the spatial soundscape, so they stay on our mixer
        /// regardless of the active engine, but share the one output.</summary>
        public static NAudioEngine NAudio
        {
            get { if (_naudio == null) _naudio = new NAudioEngine(); return _naudio; }
        }
    }
}
