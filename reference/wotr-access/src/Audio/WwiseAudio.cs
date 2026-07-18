using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Kingmaker.Sound;              // DefaultListener (volume scaling target)
using Owlcat.Runtime.Core.Registry; // ObjectRegistry
using UnityEngine;
using WrathAccess.Settings;

namespace WrathAccess.Audio
{
    /// <summary>
    /// Plays the mod's wavs as REAL Wwise 3D emitters — the same audio engine, listener (the virtual
    /// head), distance attenuation, and mixer buses as the game's own sounds, so sonification and
    /// game audio share one spatial frame. A soundbank is generated at boot from every wav under
    /// assets/audio (<see cref="WwiseBank"/>) and loaded in-memory; each wav is postable as event
    /// "wa_&lt;stem&gt;" on a pooled, positioned emitter object. Per-call volume rides
    /// SetGameObjectOutputBusVolume (emitter→listener), so the mod's volume settings apply without
    /// bank-side RTPCs. If the bank fails to load, <see cref="TryPost"/> returns false and callers
    /// fall back to the classic Unity path.
    /// </summary>
    internal static class WwiseAudio
    {
        private const int PoolSize = 8;

        private static bool _attempted;
        private static bool _ready;
        private static readonly List<GameObject> _pool = new List<GameObject>();
        private static int _next;
        // Qualified stems in the bank (relative path, '_'-joined) + bare-filename aliases for the
        // unique ones, so existing call sites can keep passing "loot-chest" etc.
        private static readonly HashSet<string> _known = new HashSet<string>();
        private static readonly Dictionary<string, string> _alias = new Dictionary<string, string>();

        public static bool Ready => _ready;

        /// <summary>Bank loaded AND the engine setting isn't "classic" — callers that maintain
        /// ongoing state (loops) check this each frame to pick/switch their playback path.</summary>
        public static bool Active => _ready && Enabled;

        private static bool Enabled =>
            ModSettings.GetSetting<ChoiceSetting>("audio.engine")?.ValueId != "classic";

        /// <summary>Lazy init: the Wwise engine comes up during game boot; load our bank once it's there.</summary>
        public static void Tick()
        {
            if (_attempted) return;
            if (!AkSoundEngine.IsInitialized()) return;
            _attempted = true;
            try
            {
                Load();
            }
            catch (Exception e)
            {
                Main.Log?.Error("[wwise] bank load failed: " + e);
                _ready = false;
            }
        }

        private static void Load()
        {
            var wavs = new List<KeyValuePair<string, byte[]>>();
            var loops = new HashSet<string>();
            var bareCount = new Dictionary<string, int>();
            var dir = Exploration.Overlays.OverlayAudio.Dir;
            if (!Directory.Exists(dir)) { Main.Log?.Warning("[wwise] no audio dir: " + dir); return; }
            foreach (var f in Directory.GetFiles(dir, "*.wav", SearchOption.AllDirectories))
            {
                // Qualified stem: the path under assets/audio, '_'-joined ("walltones_1_north") —
                // tone sets reuse bare names across folders, so bare stems alone collide.
                var rel = f.Substring(dir.Length).TrimStart('\\', '/');
                var stem = rel.Substring(0, rel.Length - 4).ToLowerInvariant()
                    .Replace('\\', '_').Replace('/', '_');
                if (_known.Contains(stem)) continue;
                _known.Add(stem);
                wavs.Add(new KeyValuePair<string, byte[]>(stem, File.ReadAllBytes(f)));
                if (stem.StartsWith("walltones_")) loops.Add(stem); // continuous tones loop forever
                var bare = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                bareCount[bare] = (bareCount.TryGetValue(bare, out int c) ? c : 0) + 1;
                if (!_alias.ContainsKey(bare)) _alias[bare] = stem;
            }
            foreach (var kv in bareCount)
                if (kv.Value > 1) _alias.Remove(kv.Key); // ambiguous bare names get no alias
            if (wavs.Count == 0) return;

            var bank = WwiseBank.Build(wavs, "wrathaccess", loops, out uint bankId);
            var handle = GCHandle.Alloc(bank, GCHandleType.Pinned);
            try
            {
                // MemoryCopy: Wwise takes its own copy, the managed array can be collected after.
                var result = AkSoundEngine.LoadBankMemoryCopy(handle.AddrOfPinnedObject(), (uint)bank.Length, out uint loadedId);
                if (result != AKRESULT.AK_Success)
                {
                    Main.Log?.Error("[wwise] LoadBankMemoryCopy: " + result);
                    return;
                }
                Main.Log?.Log("[wwise] bank loaded: " + wavs.Count + " sounds, " + bank.Length + " bytes, id " + loadedId);
            }
            finally { handle.Free(); }

            for (int i = 0; i < PoolSize; i++)
            {
                var go = new GameObject("WrathAccess.Emitter" + i);
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<AkGameObj>(); // registers with Wwise and tracks the transform
                _pool.Add(go);
            }
            _ready = true;
        }

        // A stem as passed by a caller → the qualified stem in the bank (exact, else bare alias).
        private static string ResolveStem(string stem)
        {
            if (string.IsNullOrEmpty(stem)) return null;
            stem = stem.ToLowerInvariant();
            if (_known.Contains(stem)) return stem;
            return _alias.TryGetValue(stem, out var q) ? q : null;
        }

        /// <summary>Post the wav's event at a world position with a 0..1 volume. Returns false when
        /// the Wwise path isn't available/enabled — caller falls back to the classic path.</summary>
        public static bool TryPost(string stem, Vector3 position, float volume)
        {
            if (!_ready || !Enabled) return false;
            stem = ResolveStem(stem);
            if (stem == null) return false;
            var go = _pool[_next];
            _next = (_next + 1) % _pool.Count;
            go.transform.position = position;
            // Push the position explicitly — AkGameObj syncs in its own Update, which may run after us.
            AkSoundEngine.SetObjectPosition(go, position, Vector3.forward, Vector3.up);

            var listener = ObjectRegistry<DefaultListener>.Instance?.MaybeSingle;
            if (listener != null)
                AkSoundEngine.SetGameObjectOutputBusVolume(go, listener.gameObject, Mathf.Clamp01(volume));

            uint playing = AkSoundEngine.PostEvent("wa_" + stem, go);
            // Kick the audio thread NOW instead of waiting for the integration's end-of-frame
            // RenderAudio — posted events otherwise sit in the queue for up to a whole frame,
            // which reads as audible lag next to the old direct-to-mixer path.
            if (playing != 0) AkSoundEngine.RenderAudio();
            return playing != 0; // 0 = invalid event/unloaded bank → let the caller fall back
        }

        // ---- looping emitters (wall tones etc.): a dedicated positioned object per voice ----

        /// <summary>A live looping voice on its own emitter; reposition/volume per frame, stop to end.</summary>
        public sealed class Loop
        {
            internal GameObject Go;
            internal uint PlayingId;
        }

        /// <summary>Start a looping wav (bank-side infinite loop) at a position, or null when the
        /// Wwise path isn't available/enabled — caller falls back to its classic engine.</summary>
        public static Loop StartLoop(string stem, Vector3 position)
        {
            if (!_ready || !Enabled) return null;
            stem = ResolveStem(stem);
            if (stem == null) return null;
            var go = new GameObject("WrathAccess.Loop." + stem);
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<AkGameObj>();
            go.transform.position = position;
            AkSoundEngine.SetObjectPosition(go, position, Vector3.forward, Vector3.up);
            // Start SILENT — the first buffer renders before the caller's per-frame update runs,
            // and a full-volume blip at the start position would be audible.
            var l = ObjectRegistry<DefaultListener>.Instance?.MaybeSingle;
            if (l != null) AkSoundEngine.SetGameObjectOutputBusVolume(go, l.gameObject, 0f);
            uint playing = AkSoundEngine.PostEvent("wa_" + stem, go);
            if (playing == 0) { UnityEngine.Object.Destroy(go); return null; }
            AkSoundEngine.RenderAudio();
            return new Loop { Go = go, PlayingId = playing };
        }

        /// <summary>Per-frame: move the loop's emitter and set its 0..1 volume (0 = inaudible but
        /// still running, so it resumes seamlessly).</summary>
        public static void UpdateLoop(Loop loop, Vector3 position, float volume)
        {
            if (loop == null || loop.Go == null) return;
            loop.Go.transform.position = position;
            AkSoundEngine.SetObjectPosition(loop.Go, position, Vector3.forward, Vector3.up);
            SetLoopVolume(loop, volume);
        }

        /// <summary>Volume only (e.g. mute while a menu's up) — the emitter stays where it was.</summary>
        public static void SetLoopVolume(Loop loop, float volume)
        {
            if (loop == null || loop.Go == null) return;
            var listener = ObjectRegistry<DefaultListener>.Instance?.MaybeSingle;
            if (listener != null)
                AkSoundEngine.SetGameObjectOutputBusVolume(loop.Go, listener.gameObject, Mathf.Clamp01(volume));
        }

        public static void StopLoop(Loop loop)
        {
            if (loop == null) return;
            if (loop.PlayingId != 0)
                AkSoundEngine.StopPlayingID(loop.PlayingId, 60, AkCurveInterpolation.AkCurveInterpolation_Linear);
            // Delay the destroy past the fade — AkGameObj unregisters on destroy, which would cut the voice dead.
            if (loop.Go != null) UnityEngine.Object.Destroy(loop.Go, 0.2f);
            loop.PlayingId = 0;
            loop.Go = null;
        }
    }
}
