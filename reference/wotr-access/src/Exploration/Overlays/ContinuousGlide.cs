using Kingmaker.View; // ObstacleAnalyzer.TraceAlongNavmesh
using UnityEngine;
using WrathAccess.Input;
using WrathAccess.Settings;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// A precise, free-moving cursor: hold the arrows to glide the world point continuously at a
    /// configurable ft/sec. It traces from the current point toward the intended one along the navmesh
    /// each frame (<see cref="ObstacleAnalyzer.TraceAlongNavmesh"/>) and stops at the first wall/ledge, so
    /// it can't leave walkable ground. Feedback is audio (wall tones / sonar), so it doesn't speak on
    /// move — describing the exact point is the Point-context job of <c>SpatialSystem</c>. Speed reads live
    /// from the cursor slot's settings.
    /// </summary>
    internal sealed class ContinuousGlide : MovementMode
    {
        private readonly MovementSlot _slot;
        private readonly CategorySetting _settings; // cursor.<slot> — holds "speed"

        public ContinuousGlide(MovementSlot slot, CategorySetting settings)
        {
            _slot = slot;
            _settings = settings;
        }

        public override string Name => "Continuous glide";
        public override MovementSlot Slot => _slot;
        public override AnnouncementContext Context => AnnouncementContext.Point;
        public override bool AnnouncesOnMove => false; // audio-driven, not per-frame speech

        private float Speed => (_settings?.Get<IntSetting>("speed")?.Get() ?? 15) * Geo.MetresPerFoot;

        public override void OnEnter(Overlay overlay)
        {
            // Make sure the shared cursor is planted (so move-to-cursor has a point); the getter already
            // falls back to the player, so reading-then-writing pins it there on a cold start.
            overlay.Cursor.Position = overlay.Cursor.Position;
        }

        public override void Tick(float dt, Overlay overlay)
        {
            if (!OverlayManager.Active) return;            // menu up / focus off → don't move

            CursorKeys.HeldVector(_slot, out int ix, out int iz);
            float dx = ix, dz = iz;
            if (dx == 0f && dz == 0f) return;

            var cur = overlay.Cursor.Position;
            var dir = new Vector3(dx, 0f, dz).normalized;
            var intended = cur + dir * (Speed * dt);
            overlay.Cursor.Position = ObstacleAnalyzer.TraceAlongNavmesh(cur, intended); // stops at walls/ledges
        }
    }
}
