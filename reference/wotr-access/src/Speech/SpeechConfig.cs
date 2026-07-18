using WrathAccess.Settings;

namespace WrathAccess.Speech
{
    /// <summary>
    /// A named, reusable way to speak — a handler choice plus that handler's params (SAPI rate/volume/
    /// voice, Prism backend), held in a settings subtree. The DEFAULT config is the root Speech settings
    /// (drives all UI/announcement/dialogue speech); the user can add more (advanced) for the events
    /// system to speak specific things through — e.g. enemy damage in a different voice. An event "speaks
    /// through" a config: the config resolves its handler (loading on demand), applies its params, and
    /// speaks or renders.
    /// </summary>
    public sealed class SpeechConfig
    {
        /// <summary>The config's settings subtree: a "handler" choice + one subnode per handler.</summary>
        public CategorySetting Tree { get; }

        // The config this one inherits from (the default config) — null for the default config itself, which
        // is the terminal base. An inheriting config's "inherit" settings fall back to this config's values.
        private readonly SpeechConfig _base;

        public SpeechConfig(CategorySetting tree, SpeechConfig baseConfig = null) { Tree = tree; _base = baseConfig; }

        /// <summary>The chosen handler key ("auto" = best available). The default config's handler is a
        /// plain choice; an inheriting config's is a NullableChoiceSetting whose EffectiveId already
        /// resolves through the base.</summary>
        public string HandlerKey
        {
            get
            {
                var plain = Tree?.Get<ChoiceSetting>("handler");
                if (plain != null) return plain.Current?.Id ?? "auto";
                var nullable = Tree?.Get<NullableChoiceSetting>("handler");
                return nullable?.EffectiveId ?? "auto";
            }
        }

        // The resolved, loaded handler for this config (load-on-demand + auto/fallback via SpeechManager).
        private ISpeechHandler Handler => SpeechManager.ResolveHandler(HandlerKey);

        // This handler's params for this config (null for paramless handlers like clipboard). The default
        // config returns its raw subtree; an inheriting config returns a RESOLVED snapshot — each setting is
        // its explicit override if set, else the value inherited from the base config — so the handler reads
        // plain settings and never sees "inherit".
        private CategorySetting Params(ISpeechHandler h)
        {
            if (h == null) return null;
            var mine = Tree?.Get<CategorySetting>(h.Key);
            return _base == null ? mine : Resolve(mine, _base.Tree?.Get<CategorySetting>(h.Key), h);
        }

        // Merge an inheriting config's param subtree over the base's: walk the base (canonical) subtree and,
        // per setting, take the explicit override (nullable setting overridden) or the base's value.
        private static CategorySetting Resolve(CategorySetting mine, CategorySetting baseSub, ISpeechHandler h)
        {
            if (baseSub == null) return mine; // nothing to inherit from
            var resolved = new CategorySetting(h.Key, h.Label);
            foreach (var b in baseSub.Children)
            {
                switch (b)
                {
                    case IntSetting bi:
                        int v = mine?.Get<NullableIntSetting>(bi.Key) is NullableIntSetting ni && ni.IsOverridden
                            ? ni.LocalValue.Value : bi.Get();
                        resolved.Add(new IntSetting(bi.Key, bi.Label, v, bi.Min, bi.Max, bi.Step, bi.LocalizationKey));
                        break;
                    case ChoiceSetting bc:
                        var mc = mine?.Get<NullableChoiceSetting>(bc.Key);
                        string id = mc != null && mc.IsOverridden ? mc.LocalValue : bc.ValueId;
                        resolved.Add(new ChoiceSetting(bc.Key, bc.Label, bc.Choices, id, bc.LocalizationKey));
                        break;
                }
            }
            return resolved;
        }

        /// <summary>Can speech through this config be positioned in the world (handler renders to PCM)?
        /// SAPI yes, Prism/clipboard no. The "use positional" CHOICE is a separate, event-side option.</summary>
        public bool SupportsPositional => Handler?.SupportsAudioRender ?? false;

        public bool Speak(string text, bool interrupt = false)
        {
            var h = Handler;
            return h != null && h.Speak(text, interrupt, Params(h));
        }

        public bool Output(string text, bool interrupt = false)
        {
            var h = Handler;
            return h != null && h.Output(text, interrupt, Params(h));
        }

        public bool Silence()
        {
            var h = Handler;
            return h != null && h.Silence();
        }

        /// <summary>Render through this config's handler for world-positioned playback, applying its
        /// voice. Falls back to any render-capable handler when this config's can't render.</summary>
        public SpeechAudio RenderToAudio(string text)
        {
            var h = Handler;
            if (h != null && h.SupportsAudioRender) return h.RenderToAudio(text, Params(h));
            return SpeechManager.RenderToAudioFallback(text);
        }
    }
}
