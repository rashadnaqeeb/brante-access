using WrathAccess.Settings;

namespace WrathAccess.Speech
{
    /// <summary>
    /// One speech backend (ported from SayTheSpire2, then made config-driven). Handlers self-describe
    /// (key + label), report whether they'd work on this machine (<see cref="Detect"/>), load/unload their
    /// native engine, and speak. They are now PARAM-DRIVEN: instead of reading a single cached settings
    /// subtree, each speak/render call is handed the <see cref="SpeechConfig"/>'s handler params subtree
    /// to apply, so any number of configs (the default + the user's additional ones) can drive the same
    /// handler with different voices/rates. <see cref="Output"/> drives speech AND braille where supported;
    /// <see cref="Speak"/> is voice only. <see cref="SpeechManager"/> owns load-on-demand + fallback.
    /// </summary>
    public interface ISpeechHandler
    {
        string Key { get; }
        string Label { get; }
        /// <summary>Localization key for the handler's display label ("" = use the raw label).</summary>
        string LocalizationKey { get; }

        /// <summary>Populate this handler's params schema into <paramref name="into"/> — a fresh subtree
        /// per config (no live-state wiring; params are applied at speak time). No-op for paramless
        /// handlers (clipboard).</summary>
        void BuildSettings(CategorySetting into);

        bool Detect();
        bool Load();
        void Unload();

        /// <summary>Speak, applying the params in <paramref name="config"/> (this handler's subtree within
        /// a SpeechConfig; null = defaults). The handler caches the last-applied subtree, so repeated calls
        /// with the same config don't re-run expensive applies (SAPI voice select, Prism backend rebind).</summary>
        bool Speak(string text, bool interrupt, CategorySetting config);
        bool Output(string text, bool interrupt, CategorySetting config);
        bool Silence();

        /// <summary>Whether this handler can render speech to PCM (for world-positioned playback through
        /// the spatial audio pipeline). SAPI can; Prism (screen-reader passthrough) and clipboard can't —
        /// this is the capability behind a config's <c>SupportsPositional</c>.</summary>
        bool SupportsAudioRender { get; }

        /// <summary>Render <paramref name="text"/> to PCM applying <paramref name="config"/>'s params.
        /// Null when unsupported/failed. Independent of the live speech path (must never cut, or be cut by,
        /// spoken announcements).</summary>
        SpeechAudio RenderToAudio(string text, CategorySetting config);
    }

    /// <summary>A rendered utterance: raw PCM + its format (16-bit signed little-endian).</summary>
    public sealed class SpeechAudio
    {
        public byte[] Pcm;
        public int SampleRate = 22050;
        public int Channels = 1;
        public int BitsPerSample = 16;

        /// <summary>Playback gain multiplier applied when this is mixed (see <c>NAudioEngine.PlayPcm</c>). Lets a
        /// config push past SAPI's volume ceiling (100) — rendered speech is otherwise quiet, especially
        /// after positional attenuation and the constant-power-pan centre loss. 1 = as rendered.</summary>
        public float Gain = 1f;
    }
}
