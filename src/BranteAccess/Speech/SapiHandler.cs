using System;

namespace BranteAccess.Speech
{
    /// <summary>
    /// Windows SAPI 5 driven directly over COM through <see cref="ComDispatch"/> (manual IDispatch -
    /// Unity's Mono implements neither System.Speech's registry internals nor managed COM
    /// activation). Fallback behind Prism. Rate and volume come from the host config via the
    /// provider delegates (cheap, applied every call). Ported from wotr-access with the voice
    /// picker and render-to-PCM path dropped (no positional speech in Brante).
    /// </summary>
    internal sealed class SapiHandler : ISpeechHandler
    {
        // SpeechVoiceSpeakFlags
        private const int SVSFlagsAsync = 1;
        private const int SVSFPurgeBeforeSpeak = 2;

        private readonly Func<int> _rate;
        private readonly Func<int> _volume;
        private ComDispatch _voice;

        public SapiHandler(Func<int> rate, Func<int> volume)
        {
            _rate = rate;
            _volume = volume;
        }

        public string Key => "sapi";

        public bool Detect()
        {
            try
            {
                var probe = ComDispatch.Create("SAPI.SpVoice");
                if (probe == null) { HostLog.Info("[speech] SAPI: SpVoice COM object could not be created."); return false; }
                probe.Dispose();
                return true;
            }
            catch (Exception ex)
            {
                HostLog.Info("[speech] SAPI: Detect failed: " + ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }

        public bool Load()
        {
            try
            {
                _voice = ComDispatch.Create("SAPI.SpVoice");
                if (_voice == null)
                {
                    HostLog.Error("[speech] SapiHandler: SAPI.SpVoice not available.");
                    return false;
                }
                HostLog.Info("[speech] SAPI handler loaded (manual COM).");
                return true;
            }
            catch (Exception ex)
            {
                HostLog.Error("[speech] SapiHandler failed to load: " + ex);
                _voice?.Dispose();
                _voice = null;
                return false;
            }
        }

        public void Unload()
        {
            _voice?.Dispose();
            _voice = null;
        }

        public bool Speak(string text, bool interrupt)
        {
            if (_voice == null) return false;
            try
            {
                Apply();
                _voice.Call("Speak", text, interrupt ? SVSFlagsAsync | SVSFPurgeBeforeSpeak : SVSFlagsAsync);
                return true;
            }
            catch (Exception ex)
            {
                HostLog.Error("[speech] SapiHandler.Speak failed: " + ex.Message);
                return false;
            }
        }

        public bool Output(string text, bool interrupt) => Speak(text, interrupt);

        public bool Silence()
        {
            if (_voice == null) return false;
            try
            {
                // The standard SAPI "stop": purge the queue with an empty async utterance.
                _voice.Call("Speak", string.Empty, SVSFlagsAsync | SVSFPurgeBeforeSpeak);
                return true;
            }
            catch (Exception ex)
            {
                HostLog.Error("[speech] SapiHandler.Silence failed: " + ex.Message);
                return false;
            }
        }

        // Rate/volume are cheap property puts - applied every call so config edits take effect live.
        private void Apply()
        {
            try { _voice.Set("Rate", _rate()); } catch (Exception ex) { HostLog.Warning("[speech] SAPI rate set failed: " + ex.Message); }
            try { _voice.Set("Volume", _volume()); } catch (Exception ex) { HostLog.Warning("[speech] SAPI volume set failed: " + ex.Message); }
        }
    }
}
