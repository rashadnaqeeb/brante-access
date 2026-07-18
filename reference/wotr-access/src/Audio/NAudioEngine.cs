using System;
using System.Collections.Generic;
using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using UnityEngine;
using WrathAccess.Speech; // SpeechAudio (rendered positional speech PCM)

namespace WrathAccess.Audio
{
    /// <summary>
    /// Our own stereo audio backend (the "classic" engine). ONE shared <see cref="MixingSampleProvider"/>
    /// feeding ONE <see cref="WaveOutEvent"/> — every voice (wall tones now; one-shots/sources as they
    /// migrate) is an input on that single mixer, replacing the scattered per-consumer SfxPlayer /
    /// WallToneEngine instances that each opened their own device + feeder thread + buffer.
    /// </summary>
    internal sealed class NAudioEngine : IAudioEngine, IDisposable
    {
        public const int Rate = 44100; // mixer format; the wall-tone WAVs are authored at this rate

        private MixingSampleProvider _mixer;
        private IWavePlayer _out;

        public bool Available => true; // the default output device is always there

        private void EnsureStarted()
        {
            if (_out != null) return;
            _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2)) { ReadFully = true };
            // 100 ms buffer to ride through managed-thread (GC/CPU) pauses without underrunning — see the
            // wall-tone/GC findings; below a full-GC pause and brief silences drop into continuous tones.
            _out = new WaveOutEvent { DesiredLatency = 100, NumberOfBuffers = 4 };
            _out.Init(_mixer);
            _out.Play();
        }

        internal void Add(ISampleProvider p) { EnsureStarted(); _mixer.AddMixerInput(p); }
        internal void Remove(ISampleProvider p) { try { _mixer?.RemoveMixerInput(p); } catch { } }

        public IWallTones CreateWallTones(string toneSet)
        {
            EnsureStarted();
            var dir = Path.Combine(WrathAccess.Exploration.Overlays.OverlayAudio.Dir, "walltones", toneSet);
            return new WallTones(this, dir);
        }

        public void Dispose()
        {
            try { _out?.Stop(); _out?.Dispose(); } catch { }
            _out = null; _mixer = null;
        }

        // One-shots: decode the file once (cached), then add a self-removing OneShot voice to the shared mixer.
        // stem/worldPos are the Wwise inputs and ignored here; NAudio plays the file at (volume, pan).
        private readonly Dictionary<string, float[]> _cache = new Dictionary<string, float[]>();

        /// <summary>Non-positional (stereo-centred) one-shot — UI/cue sounds, on the same shared output.</summary>
        public void Play2D(string file, float volume) => PlayOneShot(null, file, Vector3.zero, volume, 0f);

        /// <summary>Play rendered speech PCM at (volume, pan) — positional speech, on the shared output. Not
        /// cached (each utterance is unique). NAudio-only: Wwise can't play arbitrary PCM. Ported from SfxPlayer.</summary>
        public void PlayPcm(SpeechAudio audio, float volume, float pan)
        {
            if (audio?.Pcm == null || audio.Pcm.Length == 0) return;
            try
            {
                EnsureStarted();
                var buf = DecodePcm(audio);
                // audio.Gain lets a SAPI config push past SAPI's volume ceiling; folds into the voice gain.
                if (buf != null && buf.Length > 0) _mixer.AddMixerInput(new OneShot(buf, Rate, volume * audio.Gain, pan));
            }
            catch (Exception e) { Main.Log?.Error("[naudio] speech — " + e); }
        }

        private static float[] DecodePcm(SpeechAudio audio)
        {
            var fmt = new WaveFormat(audio.SampleRate, audio.BitsPerSample, audio.Channels);
            using (var ms = new MemoryStream(audio.Pcm))
            using (var raw = new RawSourceWaveStream(ms, fmt))
            {
                ISampleProvider sp = raw.ToSampleProvider();
                if (sp.WaveFormat.SampleRate != Rate) sp = new WdlResamplingSampleProvider(sp, Rate);
                if (sp.WaveFormat.Channels == 1) sp = new MonoToStereoSampleProvider(sp);
                var all = new List<float>(Rate);
                var tmp = new float[Rate * 2];
                int n;
                while ((n = sp.Read(tmp, 0, tmp.Length)) > 0)
                    for (int i = 0; i < n; i++) all.Add(tmp[i]);
                return all.ToArray();
            }
        }

        public void PlayOneShot(string stem, string file, Vector3 worldPos, float volume, float pan)
        {
            try
            {
                EnsureStarted();
                var buf = Get(file);
                if (buf != null && buf.Length > 0) _mixer.AddMixerInput(new OneShot(buf, Rate, volume, pan));
            }
            catch (Exception e) { Main.Log?.Error("[naudio] one-shot " + file + " — " + e); }
        }

        /// <summary>Positional one-shot with the full spatial model — constant-power pan + interaural
        /// time difference + the front/back lowpass (see <see cref="Spatializer"/>). <paramref name="dxEast"/>
        /// / <paramref name="dzNorth"/> are the source offset from the listener (the cursor), in metres;
        /// the caller still owns the distance→volume curve. Returns the live voice handle: a tracked source
        /// (<see cref="SpatialSources"/>) re-sets its placement each frame so it follows the moving cursor;
        /// fire-and-forget callers (a cue anchored to a fixed reference) just ignore the return.</summary>
        public ISpatialVoice PlaySpatial(string file, float volume, float dxEast, float dzNorth, float panWidth)
        {
            try
            {
                EnsureStarted();
                var buf = Get(file);
                if (buf == null || buf.Length == 0) return null;
                var voice = new PositionalEmitter(buf, Rate);
                voice.SetPlacement(Spatializer.Cue(dxEast, dzNorth, panWidth), volume);
                _mixer.AddMixerInput(voice);
                return voice;
            }
            catch (Exception e) { Main.Log?.Error("[naudio] spatial " + file + " — " + e); return null; }
        }

        private float[] Get(string path)
        {
            if (_cache.TryGetValue(path, out var cached)) return cached;
            var buf = Decode(path);
            _cache[path] = buf;
            return buf;
        }

        // Decode a WAV, normalised to the mixer format (44.1 kHz stereo float). Ported from SfxPlayer.
        private static float[] Decode(string path)
        {
            using (var reader = new AudioFileReader(path))
            {
                ISampleProvider sp = reader;
                if (sp.WaveFormat.SampleRate != Rate) sp = new WdlResamplingSampleProvider(sp, Rate);
                if (sp.WaveFormat.Channels == 1) sp = new MonoToStereoSampleProvider(sp);
                var all = new List<float>(Rate);
                var tmp = new float[Rate * 2];
                int n;
                while ((n = sp.Read(tmp, 0, tmp.Length)) > 0)
                    for (int i = 0; i < n; i++) all.Add(tmp[i]);
                return all.ToArray();
            }
        }

        // Plays a cached interleaved-stereo buffer once with a constant-power pan; returns < count at the end
        // so the mixer auto-removes it. Ported verbatim from SfxPlayer.OneShot.
        private sealed class OneShot : ISampleProvider
        {
            private readonly float[] _buf;
            private readonly float _gainL, _gainR;
            private int _pos;

            public OneShot(float[] buf, int rate, float vol, float pan)
            {
                _buf = buf;
                float t = (pan + 1f) * 0.5f * (float)(Math.PI / 2.0);
                _gainL = vol * (float)Math.Cos(t);
                _gainR = vol * (float)Math.Sin(t);
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, 2);
            }

            public WaveFormat WaveFormat { get; }

            public int Read(float[] buffer, int offset, int count)
            {
                int remaining = _buf.Length - _pos;
                int n = Math.Min(count, remaining);
                for (int i = 0; i < n; i++)
                    buffer[offset + i] = _buf[_pos + i] * (((_pos + i) & 1) == 0 ? _gainL : _gainR);
                _pos += n;
                return n;
            }
        }

        // Minimal RBJ-cookbook high-shelf biquad (shelf slope S = 1), retunable IN PLACE with state preserved —
        // NAudio's BiQuadFilter has a HighShelf factory but no SetHighShelf, and allocating a filter per block
        // on the audio thread is off-limits (Boehm full-GC pauses underrun the output). At 0 dB the
        // coefficients reduce exactly to identity, so a "no cue" shelf is genuinely transparent.
        private struct HighShelf
        {
            private float _b0, _b1, _b2, _a1, _a2; // normalized coefficients
            private float _z1, _z2;                // transposed direct form II state

            public void Set(float rate, float cornerHz, float dbGain)
            {
                double A = Math.Pow(10.0, dbGain / 40.0);
                double w0 = 2.0 * Math.PI * cornerHz / rate;
                double cosw = Math.Cos(w0);
                double alpha = Math.Sin(w0) / 2.0 * Math.Sqrt(2.0); // S = 1
                double k = 2.0 * Math.Sqrt(A) * alpha;
                double b0 = A * ((A + 1) + (A - 1) * cosw + k);
                double b1 = -2.0 * A * ((A - 1) + (A + 1) * cosw);
                double b2 = A * ((A + 1) + (A - 1) * cosw - k);
                double a0 = (A + 1) - (A - 1) * cosw + k;
                double a1 = 2.0 * ((A - 1) - (A + 1) * cosw);
                double a2 = (A + 1) - (A - 1) * cosw - k;
                _b0 = (float)(b0 / a0); _b1 = (float)(b1 / a0); _b2 = (float)(b2 / a0);
                _a1 = (float)(a1 / a0); _a2 = (float)(a2 / a0);
            }

            public float Transform(float x)
            {
                float y = _b0 * x + _z1;
                _z1 = _b1 * x - _a1 * y + _z2;
                _z2 = _b2 * x - _a2 * y;
                return y;
            }
        }

        // A spatialised, LIVE one-shot. The cached buffer is treated as MONO (left lane — our cues are mono,
        // duplicated to stereo on decode). Signal path: rear high-shelf cut (the front/back cue) on the mono
        // source → split L/R with capped-ILD constant-power gains (Spatializer.PanGains) and a fractional ITD
        // delay on the FAR channel (a tiny ring of recent shelved samples) → a second, laterality-scaled
        // high-shelf on the far ear only (frequency-dependent head shadow). Crucially the placement is
        // re-settable while it plays: SetPlacement (main thread) writes targets and Read (audio thread) ramps
        // the current values toward them across each block (shelves retune per block, state preserved), so a
        // source tracks the moving cursor without clicks. Goes silent past the buffer end, draining the delay
        // tail, then returns 0 so the mixer auto-removes it.
        private sealed class PositionalEmitter : ISampleProvider, ISpatialVoice
        {
            private const int RingSize = 64;          // >= max ITD (~29 frames) + margin; power of two
            private const int RingMask = RingSize - 1;
            private const int TailFrames = RingSize;  // drain the delay line after the source ends
            private const float DbEps = 0.05f;        // retune threshold; below this the shelf stays put

            private readonly float[] _buf;            // interleaved stereo; left lane sampled as mono
            private readonly int _srcFrames;
            private readonly int _rate;
            private readonly float[] _ring = new float[RingSize];
            private HighShelf _rear;                  // front/back darkening, on the whole source
            private HighShelf _shadow;                // head shadow, on the far ear only

            // Targets — written by SetPlacement (main thread), read by Read (audio thread).
            private volatile float _tGainL, _tGainR, _tItd, _tRearDb, _tShadowDb;
            // Current smoothed values — audio thread only.
            private float _cGainL, _cGainR, _cItd, _cRearDb, _cShadowDb;
            private bool _primed;
            private int _frame;
            private volatile bool _finished;

            public PositionalEmitter(float[] buf, int rate)
            {
                _buf = buf;
                _srcFrames = buf.Length / 2;
                _rate = rate;
                _rear.Set(rate, Spatializer.RearCornerHz, 0f);
                _shadow.Set(rate, Spatializer.ShadowCornerHz, 0f);
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, 2);
            }

            public WaveFormat WaveFormat { get; }
            public bool Finished => _finished;

            public void SetPlacement(SpatialCue cue, float volume)
            {
                Spatializer.PanGains(cue.Pan, out float gl, out float gr);
                _tGainL = volume * gl;
                _tGainR = volume * gr;
                _tItd = cue.ItdSamples;
                _tRearDb = cue.RearShelfDb;
                _tShadowDb = cue.FarShadowDb;
            }

            public int Read(float[] buffer, int offset, int count)
            {
                int frames = count / 2;
                if (frames == 0) return 0;

                float tGainL = _tGainL, tGainR = _tGainR, tItd = _tItd, tRearDb = _tRearDb, tShadowDb = _tShadowDb;
                if (!_primed)
                {
                    _cGainL = tGainL; _cGainR = tGainR; _cItd = tItd; _cRearDb = tRearDb; _cShadowDb = tShadowDb;
                    _rear.Set(_rate, Spatializer.RearCornerHz, _cRearDb);
                    _shadow.Set(_rate, Spatializer.ShadowCornerHz, _cShadowDb);
                    _primed = true;
                }

                // Shelf gains lerp once per block (retuning per sample is too costly; filter state is preserved).
                if (Math.Abs(tRearDb - _cRearDb) > DbEps)
                {
                    _cRearDb += (tRearDb - _cRearDb) * 0.5f;
                    _rear.Set(_rate, Spatializer.RearCornerHz, _cRearDb);
                }
                if (Math.Abs(tShadowDb - _cShadowDb) > DbEps)
                {
                    _cShadowDb += (tShadowDb - _cShadowDb) * 0.5f;
                    _shadow.Set(_rate, Spatializer.ShadowCornerHz, _cShadowDb);
                }

                // Gains + ITD ramp linearly to target across the block — click-free tracking of a moving source.
                float dGainL = (tGainL - _cGainL) / frames;
                float dGainR = (tGainR - _cGainR) / frames;
                float dItd = (tItd - _cItd) / frames;

                int produced = 0;
                for (int f = 0; f < frames; f++)
                {
                    if (_frame >= _srcFrames + TailFrames) break;
                    _cGainL += dGainL; _cGainR += dGainR; _cItd += dItd;

                    // Rear cue: shelve the mono source (transparent at 0 dB — identity coefficients).
                    float dry = _frame < _srcFrames ? _buf[_frame * 2] : 0f;
                    float m = _rear.Transform(dry);
                    _ring[_frame & RingMask] = m;

                    float itdMag = _cItd < 0f ? -_cItd : _cItd;
                    int itdInt = (int)itdMag; if (itdInt > RingSize - 2) itdInt = RingSize - 2;
                    float frac = itdMag - (int)itdMag;
                    int d0 = _frame - itdInt, d1 = d0 - 1;
                    float s0 = d0 >= 0 ? _ring[d0 & RingMask] : 0f;
                    float s1 = d1 >= 0 ? _ring[d1 & RingMask] : 0f;
                    // Far ear: delayed, then head-shadowed (highs shadow more than lows). The shadow filter
                    // lives on the far-ear STREAM; when the ITD sign flips mid-flight the ears swap roles, but
                    // at that instant itd ≈ 0, gains ≈ equal, and the shadow ≈ 0 dB — no discontinuity.
                    float far = _shadow.Transform(s0 + (s1 - s0) * frac);
                    float near = m;

                    bool delayLeft = _cItd >= 0f; // +ve = source east = right ear leads, left ear lags
                    buffer[offset + produced++] = (delayLeft ? far : near) * _cGainL;
                    buffer[offset + produced++] = (delayLeft ? near : far) * _cGainR;
                    _frame++;
                }
                if (_frame >= _srcFrames + TailFrames) _finished = true;
                return produced;
            }
        }

        // Four looping mono channels summed to stereo with a fixed constant-power pan (E/W hard right/left,
        // N/S centred), added as ONE input to the shared mixer. Ported verbatim from WallToneEngine.Mixer
        // so the pan amounts + loop wraparound are byte-for-byte identical — only the output is now shared.
        private sealed class WallTones : ISampleProvider, IWallTones
        {
            private sealed class Channel
            {
                public float[] Buffer = Array.Empty<float>();
                public int Pos;
                public volatile float Volume;
                public float LeftGain = 0.70710677f;
                public float RightGain = 0.70710677f;
            }

            private readonly Channel[] _channels = { new Channel(), new Channel(), new Channel(), new Channel() };
            private readonly NAudioEngine _engine;
            public WaveFormat WaveFormat { get; }

            public WallTones(NAudioEngine engine, string setDir)
            {
                _engine = engine;
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2);
                Set(0, Path.Combine(setDir, "north.wav"), 0f);
                Set(1, Path.Combine(setDir, "south.wav"), 0f);
                Set(2, Path.Combine(setDir, "east.wav"), 1f);  // hard right
                Set(3, Path.Combine(setDir, "west.wav"), -1f); // hard left
                engine.Add(this);
            }

            private void Set(int i, string path, float pan)
            {
                var c = _channels[i];
                try { c.Buffer = ReadMono(path); } catch (Exception e) { Main.Log?.Error("[walltones] load " + path + " — " + e.Message); c.Buffer = Array.Empty<float>(); }
                c.Pos = 0; c.Volume = 0f;
                float t = (pan + 1f) * 0.5f * (float)(Math.PI / 2.0); // -1=L, +1=R, 0=centred (0.707 each)
                c.LeftGain = (float)Math.Cos(t);
                c.RightGain = (float)Math.Sin(t);
            }

            // hits unused on this engine (pan is fixed per direction); volumes drive the four channels.
            public void Update(Vector3[] hits, float[] volumes)
            {
                for (int i = 0; i < _channels.Length && i < volumes.Length; i++)
                {
                    float v = volumes[i];
                    _channels[i].Volume = v < 0f ? 0f : (v > 1f ? 1f : v);
                }
            }

            public int Read(float[] buffer, int offset, int count)
            {
                int frames = count / 2;
                for (int f = 0; f < frames; f++)
                {
                    float l = 0f, r = 0f;
                    for (int i = 0; i < _channels.Length; i++)
                    {
                        var c = _channels[i];
                        int len = c.Buffer.Length;
                        if (len == 0) continue;
                        float s = c.Buffer[c.Pos] * c.Volume;
                        c.Pos++;
                        if (c.Pos >= len) c.Pos = 0; // seamless wrap
                        l += s * c.LeftGain;
                        r += s * c.RightGain;
                    }
                    buffer[offset + f * 2] = l > 1f ? 1f : (l < -1f ? -1f : l);
                    buffer[offset + f * 2 + 1] = r > 1f ? 1f : (r < -1f ? -1f : r);
                }
                return count; // ReadFully mixer: always full (silence when all volumes are 0)
            }

            public void Dispose() => _engine.Remove(this);

            // Read a WAV fully into a mono float[] (averaging channels). WAVs are authored at Rate.
            private static float[] ReadMono(string path)
            {
                using (var reader = new AudioFileReader(path))
                {
                    int ch = reader.WaveFormat.Channels;
                    var all = new List<float>(reader.WaveFormat.SampleRate * ch);
                    var buf = new float[reader.WaveFormat.SampleRate * ch];
                    int read;
                    while ((read = reader.Read(buf, 0, buf.Length)) > 0)
                        for (int i = 0; i < read; i++) all.Add(buf[i]);
                    int frames = all.Count / ch;
                    var mono = new float[frames];
                    for (int fr = 0; fr < frames; fr++)
                    {
                        float s = 0f;
                        for (int c = 0; c < ch; c++) s += all[fr * ch + c];
                        mono[fr] = s / ch;
                    }
                    return mono;
                }
            }
        }
    }
}
