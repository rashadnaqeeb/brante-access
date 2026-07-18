using UnityEngine;
using WrathAccess.Settings;

namespace WrathAccess.Audio
{
    /// <summary>The stereo placement cues for one source, heard from the cursor (our virtual listener).</summary>
    internal struct SpatialCue
    {
        public float Pan;          // -1 (hard left/west) .. +1 (hard right/east) lateral fraction
        public float ItdSamples;   // interaural delay; magnitude = samples, sign = +east / -west (far ear delayed)
        public float RearShelfDb;  // high-shelf gain on the WHOLE source: 0 ahead/at the side .. negative behind
        public float FarShadowDb;  // high-shelf gain on the FAR EAR only (head shadow): 0 centred .. negative at the side
    }

    /// <summary>A playing positional voice whose placement can be re-set live (from the main thread) as the
    /// listener (cursor) moves — so a one-shot still tracks pan/gain/ITD/filter while it's audible, instead
    /// of freezing at fire time. Updates are smoothed inside the voice so a moving source never clicks.</summary>
    internal interface ISpatialVoice
    {
        bool Finished { get; }                       // drained — safe to drop from tracking
        void SetPlacement(SpatialCue cue, float volume);
    }

    /// <summary>
    /// Turns a source's listener-relative position into stereo cues, in the top-down XZ plane where
    /// +x is east (right) and +z is north (ahead/away). Independent perceptual channels:
    ///
    ///  - <b>east/west → capped ILD + ITD (+ far-ear shadow).</b> A constant-power pan whose interaural level
    ///    difference is CAPPED (~12 dB) instead of driving the far ear to silence — a real head shadows the far
    ///    ear by ~8–20 dB, never −∞, and the interaural TIME difference (far ear lags up to ~0.66 ms, Woodworth
    ///    spherical-head curve) needs a far-ear signal to exist at all. Below ~1.5 kHz ITD is the dominant
    ///    localisation cue and the brain resolves it far finer than a sample, so together these snap left/right
    ///    into place and externalise it — especially on headphones. Optionally the far ear also gets a mild
    ///    high-shelf cut scaled by laterality (frequency-dependent head shadow: highs shadow more than lows),
    ///    which reads as a head between two ears rather than a mixer pan.
    ///  - <b>distance → gain.</b> Left to the caller (each sound has its own falloff curve); this only does
    ///    direction. Gain is a magnitude, so it can't tell front from back — that's the next channel's job.
    ///  - <b>north/south → timbre.</b> Stereo can't pan front/back, so sources <i>behind</i> the listener get a
    ///    high-shelf CUT ramping to −10 dB at due-south — darker/quieter = behind, bright = ahead (the audiogame
    ///    convention). A shelf, not a lowpass mix: our cues are bright and narrowband (review.wav has ~no energy
    ///    below 1 kHz), so a lowpass erases them and a parallel dry/wet blend comb-filters; a shelf darkens
    ///    broadband sounds and merely quietens bright ones, minimum-phase, nothing ever disappears.
    ///
    /// The extra cues are individually toggleable (the Audio tab) so they can be A/B'd by ear.
    /// </summary>
    internal static class Spatializer
    {
        public const int Rate = NAudioEngine.Rate;

        // Max interaural delay ≈ head width / speed of sound ≈ 0.22 m / 343 m/s ≈ 0.66 ms.
        private const float MaxItdSeconds = 0.00066f;
        private static float MaxItdSamples => MaxItdSeconds * Rate; // ~29 @ 44.1 kHz
        private const float WoodworthMax = Mathf.PI / 2f + 1f;      // (θ + sin θ) at θ = 90°

        // Interaural level difference at hard side, in dB. Finite on purpose: the far ear must keep signal for
        // the ITD to be audible, and a 100%/0% pan reads as "inside one ear" — the opposite of external.
        private const float MaxIldDb = 12f;

        // Perceptual expansion of the lateral fraction: |lat|^exp with exp < 1 steepens the response near the
        // centre (a small x offset pans noticeably) and leaves the extremes unchanged — lining the cursor up on
        // a source by ear needs the sharp null at zero, not linear geometry. All lateral cues (ILD, ITD, ear
        // shadow) share the expanded fraction so they keep agreeing on the angle.
        private const float PanExponent = 0.82f;

        // Far-ear head shadow: extra high-shelf cut on the far ear only, scaled by laterality.
        public const float ShadowCornerHz = 1500f; // shadow acts above ~1–2 kHz (long waves diffract around the head)
        private const float ShadowMaxDb = 8f;      // at hard side, far-ear highs sit ~MaxIld+8 dB below the near ear

        // Rear (front/back) cue: high-shelf cut ramping in over the rear hemisphere.
        public const float RearCornerHz = 3000f;
        private const float RearMaxCutDb = 10f;    // due-south: −10 dB above the corner (bright cues ≈ −10 dB overall)

        public static bool ItdEnabled => ModSettings.GetSetting<BoolSetting>("audio.itd")?.Get() ?? true;
        public static bool FilterEnabled => ModSettings.GetSetting<BoolSetting>("audio.front_back_filter")?.Get() ?? true;
        public static bool ShadowEnabled => ModSettings.GetSetting<BoolSetting>("audio.head_shadow")?.Get() ?? true;

        /// <summary>Direction cues for a source offset from the listener (metres). <paramref name="panWidth"/>
        /// is the lateral crossover — within it, pan tracks absolute sideways offset; beyond it, pure bearing.</summary>
        public static SpatialCue Cue(float dxEast, float dzNorth, float panWidth)
        {
            float dist = Mathf.Sqrt(dxEast * dxEast + dzNorth * dzNorth);
            float lat = dist > 1e-4f ? Mathf.Clamp(dxEast / Mathf.Max(dist, panWidth), -1f, 1f) : 0f;
            if (lat != 0f) lat = Mathf.Sign(lat) * Mathf.Pow(Mathf.Abs(lat), PanExponent); // centre expansion

            var cue = new SpatialCue { Pan = lat };

            if (ItdEnabled)
            {
                // Woodworth spherical-head model: ITD ∝ θ + sin θ (lat ≈ sin θ), normalised to 1 at the side —
                // a touch more delay at mid angles than the plain sin taper.
                float s = Mathf.Abs(lat);
                float theta = Mathf.Asin(Mathf.Clamp01(s));
                cue.ItdSamples = MaxItdSamples * ((theta + s) / WoodworthMax) * Mathf.Sign(lat);
            }

            if (ShadowEnabled) cue.FarShadowDb = -ShadowMaxDb * Mathf.Abs(lat);

            // Front/back: only the rear hemisphere is darkened (matches "south of the listener" exactly),
            // the shelf cut ramping in linearly from the due-side line to its maximum at due-south.
            if (FilterEnabled && dist > 1e-4f)
            {
                float northFrac = Mathf.Clamp(dzNorth / dist, -1f, 1f); // +1 ahead .. -1 behind
                if (northFrac < 0f) cue.RearShelfDb = RearMaxCutDb * northFrac; // negative → a cut
            }
            return cue;
        }

        /// <summary>Stereo gains for a lateral pan fraction: constant-power taper with the interaural level
        /// difference capped at <see cref="MaxIldDb"/> (linear in dB with |pan|), power-normalised so loudness
        /// stays constant across the arc. Centre = 0.707/0.707, matching the old constant-power law.</summary>
        public static void PanGains(float pan, out float gainL, out float gainR)
        {
            float mag = Mathf.Clamp01(Mathf.Abs(pan));
            float far = Mathf.Pow(10f, -MaxIldDb * mag / 20f);
            float norm = 1f / Mathf.Sqrt(1f + far * far);
            float near = norm; far *= norm;
            if (pan >= 0f) { gainR = near; gainL = far; }
            else { gainL = near; gainR = far; }
        }
    }
}
