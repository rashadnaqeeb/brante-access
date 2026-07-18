using System;
using System.Collections.Generic;
using WrathAccess.Settings;

namespace WrathAccess.Speech
{
    /// <summary>
    /// Windows SAPI 5 driven directly over COM through <see cref="ComDispatch"/> (manual IDispatch —
    /// Unity's Mono implements neither System.Speech's registry internals nor managed COM activation).
    /// Param-driven: rate / volume / voice are read from the speaking <see cref="SpeechConfig"/> and
    /// applied to the SpVoice each call (voice selection — the expensive part — only re-runs when the
    /// chosen voice actually changes). Also the audio RENDERER for world-positioned speech:
    /// <see cref="RenderToAudio"/> synthesizes on a second, independent SpVoice (so rendering never
    /// disturbs live speech, and works even when the active live handler is Prism/NVDA).
    /// </summary>
    public class SapiHandler : ISpeechHandler
    {
        // SpeechVoiceSpeakFlags
        private const int SVSFlagsAsync = 1;
        private const int SVSFPurgeBeforeSpeak = 2;
        // SpeechAudioFormatType for the render stream
        private const int SAFT22kHz16BitMono = 22;
        private const int RenderSampleRate = 22050;

        private ComDispatch _voice;        // live speech
        private ComDispatch _renderVoice;  // render-to-memory (independent of live speech)
        private string _liveVoiceName;     // last voice selected on _voice (skip redundant SelectVoice)
        private string _renderVoiceName;   // last voice selected on _renderVoice
        private static List<Choice> _voiceChoices; // enumerated once

        public string Key => "sapi";
        public string Label => "SAPI";
        public string LocalizationKey => "speech.sapi";

        public void BuildSettings(CategorySetting into)
        {
            into.Add(new IntSetting("rate", "Rate", 2, -10, 10, 1, "speech.sapi.rate"));
            into.Add(new IntSetting("volume", "Volume", 100, 0, 100, 5, "speech.sapi.volume"));
            // Playback gain above SAPI's 100 ceiling, applied only to RENDERED speech (events / positional)
            // when mixed through the mod's own audio — which is otherwise quiet. 100 = no boost.
            into.Add(new IntSetting("boost", "Volume boost", 100, 100, 400, 10, "speech.sapi.boost"));
            var voices = VoiceChoices();
            into.Add(new ChoiceSetting("voice", "Voice", voices, voices[0].Id, "speech.sapi.voice"));
        }

        private static List<Choice> VoiceChoices()
        {
            if (_voiceChoices != null) return _voiceChoices;
            var voices = new List<Choice>();
            try { foreach (var name in VoiceNames()) voices.Add(new Choice(name, name)); }
            catch (Exception ex) { Main.Log?.Warning("[speech] Failed to enumerate SAPI voices: " + ex.Message); }
            if (voices.Count == 0) voices.Add(new Choice("default", "Default"));
            _voiceChoices = voices;
            return _voiceChoices;
        }

        public bool Detect()
        {
            try
            {
                var probe = ComDispatch.Create("SAPI.SpVoice");
                if (probe == null) { Main.Log?.Log("[speech] SAPI: SpVoice COM object could not be created (SAPI not available?)."); return false; }
                probe.Dispose();
                return true;
            }
            catch (Exception ex)
            {
                Main.Log?.Log("[speech] SAPI: Detect failed: " + ex.GetType().Name + " " + ex.Message);
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
                    Main.Log?.Error("[speech] SapiHandler: SAPI.SpVoice not available.");
                    return false;
                }
                Main.Log?.Log("[speech] SAPI handler loaded (manual COM).");
                return true;
            }
            catch (Exception ex)
            {
                Main.Log?.Error("[speech] SapiHandler failed to load: " + ex);
                _voice?.Dispose();
                _voice = null;
                return false;
            }
        }

        public void Unload()
        {
            _voice?.Dispose();
            _voice = null;
            _renderVoice?.Dispose();
            _renderVoice = null;
            _liveVoiceName = null;
            _renderVoiceName = null;
        }

        public bool Speak(string text, bool interrupt, CategorySetting config)
        {
            if (_voice == null) return false;
            try
            {
                Apply(_voice, config, ref _liveVoiceName);
                _voice.Call("Speak", text, interrupt ? SVSFlagsAsync | SVSFPurgeBeforeSpeak : SVSFlagsAsync);
                return true;
            }
            catch (Exception ex)
            {
                Main.Log?.Error("[speech] SapiHandler.Speak failed: " + ex.Message);
                return false;
            }
        }

        public bool Output(string text, bool interrupt, CategorySetting config) => Speak(text, interrupt, config);

        public bool Silence()
        {
            if (_voice == null) return false;
            try
            {
                // The standard SAPI "stop": purge the queue with an empty async utterance.
                _voice.Call("Speak", string.Empty, SVSFlagsAsync | SVSFPurgeBeforeSpeak);
                return true;
            }
            catch { return false; }
        }

        // ---- render-to-audio (for world-positioned speech) ----

        public bool SupportsAudioRender => true;

        public SpeechAudio RenderToAudio(string text, CategorySetting config)
        {
            if (string.IsNullOrEmpty(text)) return null;
            ComDispatch stream = null, format = null;
            try
            {
                // Lazily create the render voice — independent of Load(), so rendering works even when
                // SAPI isn't the active live handler.
                if (_renderVoice == null)
                {
                    _renderVoice = ComDispatch.Create("SAPI.SpVoice");
                    if (_renderVoice == null) return null;
                }
                Apply(_renderVoice, config, ref _renderVoiceName);

                // Fresh memory stream per utterance, pinned to a known PCM format. Without
                // AllowAudioOutputFormatChangesOnNextSet=false SAPI rewrites the stream's format to the
                // engine default on assignment, and we'd mis-read the samples.
                stream = ComDispatch.Create("SAPI.SpMemoryStream");
                if (stream == null) return null;
                format = (ComDispatch)stream.Get("Format");
                format.Set("Type", SAFT22kHz16BitMono);
                _renderVoice.Set("AllowAudioOutputFormatChangesOnNextSet", false);
                _renderVoice.SetRef("AudioOutputStream", stream);

                _renderVoice.Call("Speak", text, 0); // synchronous — the data is complete on return

                var data = stream.Call("GetData") as byte[];
                if (data == null || data.Length == 0) return null;
                int boost = config?.Get<IntSetting>("boost")?.Get() ?? 100;
                return new SpeechAudio
                {
                    Pcm = data,
                    SampleRate = RenderSampleRate,
                    Channels = 1,
                    BitsPerSample = 16,
                    Gain = boost / 100f,
                };
            }
            catch (Exception ex)
            {
                Main.Log?.Error("[speech] SapiHandler.RenderToAudio failed: " + ex.Message);
                return null;
            }
            finally
            {
                format?.Dispose();
                stream?.Dispose();
            }
        }

        // ---- shared voice plumbing ----

        /// <summary>Apply a config's rate/volume/voice to a SpVoice. Rate/volume are cheap (set every
        /// call); voice selection enumerates the registry, so it's skipped unless the name changed.</summary>
        private void Apply(ComDispatch voice, CategorySetting config, ref string lastVoiceName)
        {
            int rate = config?.Get<IntSetting>("rate")?.Get() ?? 2;
            int volume = config?.Get<IntSetting>("volume")?.Get() ?? 100;
            string name = config?.Get<ChoiceSetting>("voice")?.Current?.Id;
            try { voice.Set("Rate", rate); } catch { }
            try { voice.Set("Volume", volume); } catch { }
            if (!string.IsNullOrEmpty(name) && name != "default" && name != lastVoiceName)
            {
                try { SelectVoice(voice, name); lastVoiceName = name; }
                catch (Exception ex) { Main.Log?.Error("[speech] Voice select failed: " + ex.Message); }
            }
        }

        private static IEnumerable<string> VoiceNames()
        {
            var probe = ComDispatch.Create("SAPI.SpVoice");
            if (probe == null) yield break;
            try
            {
                var tokens = (ComDispatch)probe.Call("GetVoices", string.Empty, string.Empty);
                if (tokens == null) yield break;
                try
                {
                    int count = Convert.ToInt32(tokens.Get("Count"));
                    for (int i = 0; i < count; i++)
                    {
                        string name = null;
                        var token = (ComDispatch)tokens.Call("Item", i);
                        if (token != null)
                        {
                            try { name = token.Call("GetDescription", 0) as string; }
                            finally { token.Dispose(); }
                        }
                        if (!string.IsNullOrEmpty(name)) yield return name;
                    }
                }
                finally { tokens.Dispose(); }
            }
            finally { probe.Dispose(); }
        }

        private static void SelectVoice(ComDispatch voice, string description)
        {
            var tokens = (ComDispatch)voice.Call("GetVoices", string.Empty, string.Empty);
            if (tokens == null) return;
            try
            {
                int count = Convert.ToInt32(tokens.Get("Count"));
                for (int i = 0; i < count; i++)
                {
                    var token = (ComDispatch)tokens.Call("Item", i);
                    if (token == null) continue;
                    if (token.Call("GetDescription", 0) as string == description)
                    {
                        voice.SetRef("Voice", token); // putref — SAPI object-valued property
                        token.Dispose();
                        return;
                    }
                    token.Dispose();
                }
            }
            finally { tokens.Dispose(); }
        }
    }
}
