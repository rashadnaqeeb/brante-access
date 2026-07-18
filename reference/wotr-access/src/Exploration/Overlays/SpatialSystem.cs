using System.Collections.Generic;
using WrathAccess.Settings;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// The continuous-space lens: describes the exact point under the cursor — direction, distance, and
    /// vertical offset from the player (<see cref="Geo.Relative"/>). Point context, so it pairs with a
    /// free-gliding cursor; the tiled readout is <see cref="GridSystem"/>'s job. (A future "vertical"
    /// toggle would need <c>Geo.Relative</c> decomposed; raw coordinates are toggleable now.)
    /// </summary>
    internal sealed class SpatialSystem : OverlaySystem
    {
        public override string Name => "Spatial";
        public override string Key => "spatial";

        // A readout (announces the cursor's point on demand) — no continuous playback, so "when moving"
        // doesn't apply; Off/Continuous only.
        public override System.Collections.Generic.IReadOnlyList<OverlayMode> SupportedModes => OverlayModes.OffContinuous;

        public override void RegisterSettings(CategorySetting cat)
        {
            cat.Add(new BoolSetting("raw", "Announce raw coordinates", false, "overlay.spatial.raw"));
        }

        public override IEnumerable<OverlayAnnouncement> Announce(OverlayContext ctx)
        {
            if (!Enabled || ctx.Want != AnnouncementContext.Point) yield break;
            var text = Geo.Relative(ctx.Reference, ctx.Cursor);
            if (Bool("raw", false)) text += "; " + Geo.Raw(ctx.Cursor);
            yield return new OverlayAnnouncement(AnnouncementContext.Point, Message.Raw(text));
        }
    }
}
