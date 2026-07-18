using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BranteAccess.Core;
using BranteAccess.Core.Modularity;

namespace BranteAccess.Speech
{
    /// <summary>
    /// The host-owned speech funnel (implements the Core ISpeech the module speaks through).
    /// Handlers resolve load-on-demand in preference order Prism, SAPI, Clipboard; the config can
    /// pin one. House rules enforced at this boundary: rich text is stripped, speech never
    /// interrupts unless the caller opts in, and every failed utterance is logged.
    /// BRANTE_NO_SPEECH=1 skips all backend work (headless runs) - spoken lines still reach the
    /// dev tap so /speech captures them.
    /// </summary>
    public sealed class SpeechPipeline : ISpeech
    {
        private readonly List<ISpeechHandler> _handlers;
        private readonly HashSet<string> _loaded = new HashSet<string>();
        private readonly HashSet<string> _failed = new HashSet<string>();
        private readonly ConfigEntry<string> _handlerChoice;
        private readonly bool _noSpeech;

        /// <summary>Dev-only tap: every line spoken through the pipeline, post-strip, with its
        /// interrupt flag (so a driver can see speech policy it cannot hear). Set by the dev
        /// server; null in a normal run.</summary>
        public Action<string, bool> Observer;

        public SpeechPipeline(ConfigFile config)
        {
            _noSpeech = Environment.GetEnvironmentVariable("BRANTE_NO_SPEECH") == "1";
            _handlerChoice = config.Bind("Speech", "Handler", "auto",
                "Speech backend: auto (first available of prism, sapi, clipboard) or a specific one.");
            var rate = config.Bind("Speech", "SapiRate", 2, "SAPI rate, -10..10. Used by the sapi handler only.");
            var volume = config.Bind("Speech", "SapiVolume", 100, "SAPI volume, 0..100. Used by the sapi handler only.");
            _handlers = new List<ISpeechHandler>
            {
                new PrismHandler(),
                new SapiHandler(() => rate.Value, () => volume.Value),
                new ClipboardHandler(),
            };
            if (_noSpeech) HostLog.Info("[speech] BRANTE_NO_SPEECH=1: backends disabled, dev tap still live.");
        }

        public void Speak(string text, bool interrupt = false) => Say(text, interrupt, output: false);

        public void Output(string text, bool interrupt = false) => Say(text, interrupt, output: true);

        private void Say(string text, bool interrupt, bool output)
        {
            if (string.IsNullOrEmpty(text)) return;
            text = TextUtil.StripRichText(text); // game labels are TMP rich text
            if (string.IsNullOrEmpty(text)) return;
            Observer?.Invoke(text, interrupt);
            if (_noSpeech) return;

            var handler = Resolve();
            if (handler == null) return; // Resolve already logged the total failure
            bool ok = output ? handler.Output(text, interrupt) : handler.Speak(text, interrupt);
            if (!ok)
                HostLog.Error("[speech] utterance FAILED on handler '" + handler.Key + "': " + text);
        }

        public void Silence()
        {
            if (_noSpeech) return;
            Resolve()?.Silence();
        }

        /// <summary>The handler for the config choice: a specific key pins it (falling back to auto
        /// if it cannot load); auto walks the preference order. Null only when nothing loads.</summary>
        private ISpeechHandler Resolve()
        {
            var key = _handlerChoice.Value;
            if (!string.IsNullOrEmpty(key) && key != "auto")
            {
                foreach (var h in _handlers)
                    if (h.Key == key)
                        return EnsureLoaded(h) ? h : ResolveAuto();
                HostLog.Error("[speech] Unknown handler in config: '" + key + "', using auto.");
            }
            return ResolveAuto();
        }

        private ISpeechHandler ResolveAuto()
        {
            foreach (var h in _handlers)
                if (EnsureLoaded(h)) return h;
            HostLog.Error("[speech] No speech handler could be loaded!");
            return null;
        }

        private bool EnsureLoaded(ISpeechHandler handler)
        {
            if (_loaded.Contains(handler.Key)) return true;
            if (_failed.Contains(handler.Key)) return false; // don't re-probe a dead backend per utterance
            try
            {
                if (!handler.Detect()) { HostLog.Info("[speech] " + handler.Key + ": not detected on this machine."); _failed.Add(handler.Key); return false; }
                if (!handler.Load()) { HostLog.Info("[speech] " + handler.Key + ": detected but failed to load."); _failed.Add(handler.Key); return false; }
                _loaded.Add(handler.Key);
                HostLog.Info("[speech] handler loaded: " + handler.Key);
                return true;
            }
            catch (Exception ex)
            {
                HostLog.Error("[speech] Handler " + handler.Key + " failed: " + ex.Message);
                _failed.Add(handler.Key);
                return false;
            }
        }

        public void Shutdown()
        {
            foreach (var h in _handlers)
            {
                if (!_loaded.Contains(h.Key)) continue;
                try { h.Unload(); } catch (Exception ex) { HostLog.Warning("[speech] Unload of " + h.Key + " failed: " + ex.Message); }
            }
            _loaded.Clear();
        }
    }
}
