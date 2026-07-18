using Kingmaker.View; // ObstacleAnalyzer
using Pathfinding;    // NNInfo / NavmeshBase / GraphNode
using UnityEngine;
using WrathAccess.Audio; // Audio.Engine / IWallTones

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// Directional <b>wall tones</b>: four looping sounds whose volumes rise as a wall nears in that
    /// cardinal direction. Each frame it traces the navmesh in each direction and turns the distance-to-wall
    /// into a 0..1 volume, then hands the four (hit, volume) pairs to the active audio engine's wall-tone
    /// voices — NAudio spatializes them with a fixed compass pan (E/W hard L/R, N/S centred), Wwise places
    /// 3D emitters at the hit points. Self-gates on <see cref="OverlayManager.Active"/> (mutes when a menu's
    /// up); releases its voices on exit.
    /// </summary>
    internal sealed class WallToneSystem : AudioSystem
    {
        public override string Name => "Wall tones";
        public override string Key => "walltones";

        private float Range => Int("range", 10) * Geo.MetresPerFoot;

        private static readonly Vector3[] DirVecs = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left }; // N,S,E,W
        private string ToneSet => ChoiceId("tone_set", "1");

        private IWallTones _tones;
        private IAudioEngine _engineUsed; // the engine _tones was built from — rebuild on a live engine swap
        private string _setUsed;
        private readonly Vector3[] _hits = new Vector3[4];
        private readonly float[] _vols = new float[4];

        protected override void RegisterAudioSettings(WrathAccess.Settings.CategorySetting cat)
        {
            cat.Add(new WrathAccess.Settings.IntSetting("range", "Range (feet)", 10, 1, 40, 1, "overlay.walltones.range"));
            cat.Add(new WrathAccess.Settings.ChoiceSetting("tone_set", "Tone set",
                new[]
                {
                    new WrathAccess.Settings.Choice("1", "Set 1", "overlay.walltones.tone_set.1"),
                    new WrathAccess.Settings.Choice("2", "Set 2", "overlay.walltones.tone_set.2"),
                }, "1", "overlay.walltones.tone_set"));
        }

        public override void OnEnter(Overlay overlay) { }

        public override void OnExit(Overlay overlay)
        {
            _tones?.Dispose();
            _tones = null; _engineUsed = null; _setUsed = null;
        }

        public override void Tick(float dt, Overlay overlay)
        {
            // Silent without control (cutscene): the overlay stays engaged, but wall tones shouldn't play
            // over a scripted scene. Mute (don't dispose) so they resume seamlessly when control returns.
            if (!OverlayManager.Active || !ShouldPlay(overlay) || !WrathAccess.ControlState.HasControl) { Mute(); return; }

            // (Re)build the voices on first use, when the user picks a different tone set, or when the audio
            // engine is swapped live (Audio.Engine returns a different cached instance).
            var engine = AudioEngines.Active;
            if (_tones == null || !ReferenceEquals(_engineUsed, engine) || _setUsed != ToneSet)
            {
                _tones?.Dispose();
                _engineUsed = engine; _setUsed = ToneSet;
                _tones = engine.CreateWallTones(ToneSet);
            }

            var c = overlay.Cursor.Position;
            float v = EffectiveVolume;

            // Nearest node once per frame, reused for the 4 direction Linecasts. A FRESH query (no cross-frame
            // hint): the hint short-circuited the game's y/area node selection, which drifted the trace near
            // walls (triangle edges) and shifted the L/R balance. Fresh-once is bit-identical to the original
            // per-direction TraceAlongNavmesh — same node, same Linecasts — just GetNearest run 1x not 4x.
            NNInfo node = ObstacleAnalyzer.GetNearestNode(c);
            for (int i = 0; i < 4; i++)
            {
                _hits[i] = TraceFrom(c, node, c + DirVecs[i] * Range);
                _vols[i] = Curve(c, _hits[i]) * v;
            }
            _tones.Update(_hits, _vols);
        }

        // Inactive (menu up / disabled): keep the voices alive but silent, so they resume seamlessly.
        private void Mute()
        {
            if (_tones == null) return;
            for (int i = 0; i < _vols.Length; i++) _vols[i] = 0f;
            _tones.Update(_hits, _vols);
        }

        // ObstacleAnalyzer.TraceAlongNavmesh, but with a precomputed nearest node so the GetNearest query
        // isn't repeated per direction. The Linecast (null trace list) is the cheap stack-walking part.
        private static Vector3 TraceFrom(Vector3 start, NNInfo node, Vector3 end)
        {
            float dx = start.x - node.position.x, dz = start.z - node.position.z;
            if (dx * dx + dz * dz > 0.0001f) return node.position; // cursor off-navmesh → clamp (matches the game's trace)
            NavmeshBase.Linecast(node.node.Graph as NavmeshBase, start, end, node.node, out var hit, null);
            return hit.point;
        }

        // 0 (no wall within range) → 1 (right at the wall), curved so it bites close in.
        private float Curve(Vector3 c, Vector3 hit)
        {
            float dx = hit.x - c.x, dz = hit.z - c.z;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            if (dist >= Range) return 0f;
            float t = 1f - dist / Range;
            return t * t;
        }
    }
}
