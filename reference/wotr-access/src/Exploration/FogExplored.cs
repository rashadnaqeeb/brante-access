using Unity.Collections;
using UnityEngine;
using FogArea = Owlcat.Runtime.Visual.RenderPipeline.RendererFeatures.FogOfWar.FogOfWarArea;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// Answers "has the player ever seen this ground?" by reading the GAME'S OWN explored layer.
    ///
    /// <c>FogOfWarArea.FogOfWarMapRT</c> encodes exploration in its channels (verified live, 2026-07-04,
    /// against the decompiled FogOfWarShadowmapPass): the per-frame clear writes with ColorMask 10
    /// (Red|Blue) and the final blend accumulates into GREEN, which is never cleared — so
    /// <b>R = currently visible</b> (re-fogs when the party leaves) and <b>G = explored, ever</b>. The
    /// game persists the whole texture into the save as per-scene .fog entries (JPEG q50) and restores
    /// it on load, so G arrives pre-populated for any save — including ones made before this mod
    /// existed. (An earlier probe misread G as empty and we accumulated R ourselves with our own save
    /// persistence; that whole layer is retired — the game already does it.)
    ///
    /// Cheap and alloc-free per tick: every 0.5s one GPU blit of the fog RT to a small scratch RT and a
    /// synchronous CPU readback — the LATEST snapshot answers <see cref="IsExplored"/> (no accumulation:
    /// G is already the accumulator). Coordinates map like the fog camera — a plain ortho projection
    /// over <c>GetWorldBounds()</c>, +x east / +z north, validated against the live texture. The
    /// threshold is generous (0.35) because the save round-trips through JPEG.
    /// </summary>
    internal static class FogExplored
    {
        private const int N = 256;             // snapshot resolution per axis (cell ≈ boundsSize/256)
        private const byte Threshold = 90;     // G >= this (~0.35) = explored (JPEG-soft edges)
        private const float IntervalSec = 0.5f;

        private static byte[] _g;              // latest snapshot's G channel (N*N, row-major, +z north)
        private static string _key;            // current fog area key (scene name)
        private static Bounds _bounds;         // current fog area world bounds
        private static bool _ready;            // at least one snapshot for the current area
        private static float _timer;

        private static RenderTexture _small;   // NxN scratch for the downsample blit
        private static Texture2D _read;        // NxN CPU-side readback target

        /// <summary>Snapshot the game's fog texture (latest wins). Call once per frame.</summary>
        public static void Tick(float dt)
        {
            var fog = FogArea.Active;
            if (fog == null) return;
            var rt = fog.FogOfWarMapRT;
            if (rt == null) return;

            string key = fog.gameObject.scene.name;
            if (string.IsNullOrEmpty(key)) key = fog.name;
            if (key != _key)
            {
                _key = key;
                _bounds = fog.GetWorldBounds();
                _ready = false;
                _timer = 0f; // snapshot promptly on entering the area
            }

            _timer -= dt;
            if (_timer > 0f) return;
            _timer = IntervalSec;
            Snapshot(rt);
        }

        private static void Snapshot(RenderTexture rt)
        {
            if (_small == null) { _small = new RenderTexture(N, N, 0, RenderTextureFormat.ARGB32); _small.Create(); }
            if (_read == null) _read = new Texture2D(N, N, TextureFormat.RGBA32, false);
            if (_g == null) _g = new byte[N * N];

            var prev = RenderTexture.active;
            Graphics.Blit(rt, _small);                     // GPU downsample the fog RT to NxN
            RenderTexture.active = _small;
            _read.ReadPixels(new Rect(0, 0, N, N), 0, 0);  // no Apply(): we only read the CPU copy back
            RenderTexture.active = prev;

            NativeArray<Color32> raw = _read.GetRawTextureData<Color32>(); // view, no managed allocation
            var g = _g;
            int lim = Mathf.Min(raw.Length, g.Length);
            for (int i = 0; i < lim; i++)
            {
                var p = raw[i];
                g[i] = p.g >= p.r ? p.g : p.r; // explored = G; R folded in for the freshest reveal edge
            }
            _ready = true;
        }

        /// <summary>Has this world point ever been revealed? Defaults to <c>true</c> when we have no data
        /// or the point is outside the fog area, so we never falsely report explored ground as unexplored.</summary>
        public static bool IsExplored(Vector3 world)
        {
            var g = _g;
            if (!_ready || g == null) return true;
            var b = _bounds;
            if (b.size.x < 1e-3f || b.size.z < 1e-3f) return true;
            float u = (world.x - b.min.x) / b.size.x;
            float v = (world.z - b.min.z) / b.size.z;
            if (u < 0f || u > 1f || v < 0f || v > 1f) return true; // outside the fog area — don't claim unexplored
            int x = Mathf.Clamp((int)(u * N), 0, N - 1);
            int y = Mathf.Clamp((int)(v * N), 0, N - 1);
            return g[y * N + x] >= Threshold;
        }
    }
}
