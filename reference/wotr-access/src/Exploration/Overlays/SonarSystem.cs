using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using WrathAccess.Audio;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// A staggered sonar sweep. Rather than looping every visible sonifiable thing at once — which
    /// phantom-centres two same-type sounds at left+right into a single averaged source — it pings them one
    /// at a time, ordered left→right, each positioned by distance (volume) and bearing (pan), then rests and
    /// repeats. The per-ping gap shrinks as the crowd grows: <c>gap = clamp(K/count, gap_min, gap_max)</c>,
    /// so a few things feel spacious and many compress toward the audible floor — the sweep lengthens with
    /// count but nothing is ever dropped (the scanner remains the tool for exact enumeration). Visible-but-
    /// distant things stay in the sweep (quiet, by distance). Self-gates on the overlay being active.
    /// </summary>
    internal sealed class SonarSystem : AudioSystem
    {
        public override string Name => "Sonar";
        public override string Key => "sonar";

        private readonly List<ScanItem> _sweep = new List<ScanItem>(); // ordered snapshot for the current sweep
        private int _index;   // next thing in _sweep to ping
        private float _timer; // seconds until the next ping / until the end-of-sweep rest elapses

        private const float SpreadSec = 0.75f;   // K: the per-ping gap at one thing (then clamped by gap_min/max)
        private const float MinVol = 0.08f;      // floor so far-but-visible things stay audible
        private const float PanWidthFeet = 10f;  // pan crossover (lateral close, bearing far)

        protected override void RegisterAudioSettings(WrathAccess.Settings.CategorySetting cat)
        {
            cat.Add(new WrathAccess.Settings.ChoiceSetting("review_sound", "Review cursor sound",
                ReviewSoundChoices(), ReviewSoundDefault(), "overlay.sonar.review_sound"));
            cat.Add(new WrathAccess.Settings.IntSetting("ref_distance", "Reference distance (feet)", 10, 1, 60, 1, "overlay.sonar.ref_distance"));
            cat.Add(new WrathAccess.Settings.IntSetting("max_distance", "Maximum distance (feet)", 40, 10, 120, 5, "overlay.sonar.max_distance"));
            cat.Add(new WrathAccess.Settings.IntSetting("gap_min", "Minimum ping gap (ms)", 100, 30, 400, 10, "overlay.sonar.gap_min"));
            cat.Add(new WrathAccess.Settings.IntSetting("gap_max", "Maximum ping gap (ms)", 200, 50, 600, 10, "overlay.sonar.gap_max"));
            cat.Add(new WrathAccess.Settings.IntSetting("rest", "Rest between sweeps (ms)", 400, 0, 1500, 50, "overlay.sonar.rest"));
        }

        // The review-sound dropdown: the wavs at the audio root (assets/audio/*.wav — where
        // review.wav lives) plus Silent. User-dropped files appear under their raw stem.
        private static System.Collections.Generic.List<WrathAccess.Settings.Choice> ReviewSoundChoices()
        {
            var choices = new System.Collections.Generic.List<WrathAccess.Settings.Choice>
            {
                new WrathAccess.Settings.Choice("silent", "Silent", "choice.silent"),
            };
            try
            {
                var stems = new System.Collections.Generic.List<string>();
                foreach (var f in Directory.GetFiles(OverlayAudio.Dir, "*.wav"))
                    stems.Add(Path.GetFileNameWithoutExtension(f));
                stems.Sort(System.StringComparer.OrdinalIgnoreCase);
                foreach (var s in stems) choices.Add(new WrathAccess.Settings.Choice(s, s, "sound." + s));
            }
            catch (System.Exception e)
            {
                Main.Log?.Warning("[sonar] couldn't list review sounds: " + e.Message);
            }
            return choices;
        }

        private static string ReviewSoundDefault()
        {
            try { if (File.Exists(Path.Combine(OverlayAudio.Dir, "review.wav"))) return "review"; }
            catch { }
            return "silent";
        }

        /// <summary>The review-cursor ping (the scanner's selection landing): the chosen root-level
        /// sound positioned at the reviewed thing — same distance/pan model as the sweep, relative to
        /// the given reference (the movement cursor). NOT gated on Enabled: it's selection feedback,
        /// not part of the sweep; pick Silent to turn it off.</summary>
        public void PlayReview(ScanItem item, Vector3 from)
        {
            if (item == null) return;
            var stem = Settings?.Get<WrathAccess.Settings.ChoiceSetting>("review_sound")?.ValueId;
            if (string.IsNullOrEmpty(stem) || stem == "silent") return;

            // Anchored to the reference you reviewed from (the review cursor), so it does NOT chase the
            // movement cursor — a deliberate "this thing, relative to where you looked" cue. One-shot.
            var np = item.NearestPoint(from);
            float dx = np.x - from.x, dz = np.z - from.z;
            AudioEngines.NAudio.PlaySpatial(Path.Combine(OverlayAudio.Dir, stem + ".wav"),
                VolumeFor(Mathf.Sqrt(dx * dx + dz * dz)), dx, dz, PanWidthM);
        }

        // The sweep's distance→volume curve (reads the live ref-distance + system volume settings).
        private float VolumeFor(float dist)
        {
            float refDist = Int("ref_distance", 10) * Geo.MetresPerFoot;
            return Mathf.Clamp(refDist / (refDist + dist), MinVol, 1f) * EffectiveVolume;
        }

        private static float PanWidthM => PanWidthFeet * Geo.MetresPerFoot;

        public override void OnExit(Overlay overlay) => ResetSweep();

        public override void Tick(float dt, Overlay overlay)
        {
            // Silent without control (cutscene): the overlay stays engaged, but the sonar shouldn't sweep
            // over a scripted scene. ResetSweep so it starts fresh when control returns.
            if (!OverlayManager.Active || !ShouldPlay(overlay) || !WrathAccess.ControlState.HasControl) { ResetSweep(); return; }

            _timer -= dt;
            if (_timer > 0f) return;

            // Whole snapshot fired (or none yet) → start a fresh sweep.
            if (_index >= _sweep.Count)
            {
                Snapshot(overlay);
                _index = 0;
                if (_sweep.Count == 0) { _timer = RestSec; return; } // nothing visible — idle, recheck after a rest
            }

            FirePing(_sweep[_index++], overlay); // positioned live, in case the cursor moved during the sweep
            _timer = _index >= _sweep.Count ? RestSec : GapSec(_sweep.Count);
        }

        private void ResetSweep() { _sweep.Clear(); _index = 0; _timer = 0f; }

        // Visible sonifiable things within the sense radius of the cursor, ordered left→right by lateral
        // offset so the pan glides across the sweep (two same-type things read as "left … right", not a
        // centred average). The radius cap stops a far-but-revealed thing from flooring at min volume and
        // sounding deceptively close — out past it, it simply drops from the sweep.
        private void Snapshot(Overlay overlay)
        {
            var c = overlay.Cursor.Position;
            float maxDist = Int("max_distance", 40) * Geo.MetresPerFoot;
            _sweep.Clear();
            foreach (var it in WorldModel.Items)
            {
                if (ScanSounds.Resolve(it.Primary) == null) continue; // no sound configured for this thing
                var np = it.NearestPoint(c); // distance to the nearest part of the actual shape
                float dx = np.x - c.x, dz = np.z - c.z;
                if (dx * dx + dz * dz > maxDist * maxDist) continue;
                // Known + (a party member sees it now, OR a remembered thing under fog with a clear line of
                // sight from the cursor). Anything currently in sight always pings — in combat it'd be jarring
                // for an enemy your party plainly sees to go silent because a table sits between it and the
                // cursor — while a remembered thing behind a wall isn't pinged straight through it. Shared with
                // the review cycles via ScanItem.DetectableFrom so the two stay consistent. (Sweep is
                // distance-capped, so this only runs on nearby candidates.)
                if (!it.DetectableFrom(c)) continue;
                _sweep.Add(it);
            }
            _sweep.Sort((a, b) => (a.Position.x - c.x).CompareTo(b.Position.x - c.x));
        }

        private void FirePing(ScanItem item, Overlay overlay)
        {
            if (!item.IsVisible) return; // went away since the snapshot
            var snd = ScanSounds.Resolve(item.Primary); // live: the user's per-node pick
            if (snd == null) return;

            // A LIVE source: heard from the moving cursor, positioned at the nearest point on the item's
            // actual shape (recomputed as you move, so a wall reads along its length). SpatialSources re-pans
            // and re-attenuates it every frame until the ping finishes — it no longer freezes at fire time.
            WrathAccess.Audio.SpatialSources.Play(
                Path.Combine(OverlayAudio.Dir, "interactables", snd + ".wav"),
                () => overlay.Cursor.Position,
                c => item.NearestPoint(c),
                VolumeFor,
                PanWidthM);
        }

        // gap = clamp(K/count, gap_min, gap_max): spacious for a few, compressing toward the floor as the
        // crowd grows, so the whole sweep lengthens with count but pings stay individually audible.
        private float GapSec(int count)
            => Mathf.Clamp(SpreadSec / Mathf.Max(1, count), Int("gap_min", 100) / 1000f, Int("gap_max", 200) / 1000f);

        private float RestSec => Int("rest", 400) / 1000f;
    }
}
