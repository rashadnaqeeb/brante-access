using System;
using NAudio.Wave;

namespace WrathAccess.Audio
{
    /// <summary>
    /// A single continuous sine oscillator (NAudio, independent of the game's Wwise) whose frequency and
    /// gain are set live each frame. Frequency changes are phase-continuous (a smooth glissando, no clicks);
    /// gain ramps over ~20 ms so starts/stops don't pop. Used for the slope/elevation tone — pitch tracks
    /// the cursor's height. <see cref="Set"/> updates the target pitch+loudness; <see cref="Silence"/> fades
    /// it out (holding the last pitch).
    /// </summary>
    internal sealed class ToneEngine : IDisposable
    {
        private IWavePlayer _out;
        private Osc _osc;

        public void Set(float frequency, float gain)
        {
            EnsureStarted();
            _osc.Target(frequency, gain);
        }

        public void Silence() => _osc?.Target(_osc.Freq, 0f);

        private void EnsureStarted()
        {
            if (_out != null) return;
            _osc = new Osc(44100);
            _out = new WaveOutEvent { DesiredLatency = 50, NumberOfBuffers = 4 };
            _out.Init(_osc.ToWaveProvider());
            _out.Play();
        }

        public void Dispose()
        {
            try { _out?.Stop(); _out?.Dispose(); } catch { }
            _out = null;
            _osc = null;
        }

        // The oscillator. Targets are set on the main thread (volatile floats); the audio thread reads them in
        // Read, ramping gain per-sample and advancing a continuous phase so frequency moves glissando-style.
        private sealed class Osc : ISampleProvider
        {
            private volatile float _freq = 440f;
            private volatile float _gain;
            private double _phase;
            private float _curGain;
            private readonly float _gainStep;

            public Osc(int rate)
            {
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, 1);
                _gainStep = 1f / (rate * 0.02f); // ~20 ms full-scale gain ramp
            }

            public WaveFormat WaveFormat { get; }
            public float Freq => _freq;
            public void Target(float freq, float gain) { _freq = freq; _gain = gain; }

            public int Read(float[] buffer, int offset, int count)
            {
                float sr = WaveFormat.SampleRate;
                for (int i = 0; i < count; i++)
                {
                    float g = _gain;
                    if (_curGain < g) _curGain = Math.Min(g, _curGain + _gainStep);
                    else if (_curGain > g) _curGain = Math.Max(g, _curGain - _gainStep);

                    _phase += _freq / sr;
                    if (_phase >= 1.0) _phase -= 1.0;
                    buffer[offset + i] = (float)Math.Sin(_phase * 2.0 * Math.PI) * _curGain;
                }
                return count;
            }
        }
    }
}
