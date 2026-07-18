#if DEBUG
using System;
using System.Text;
using WrathAccess.Screens;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Dev
{
    /// <summary>
    /// Interpreted dump of the focused screen's GRAPH — the mod's OWN view, one line per node rendered
    /// the way the announcer reads it (all announcement parts joined), with stop/region boundaries and
    /// the currently-focused node marked. The dev driver's /gui: lets me see what nav state the mod is
    /// in without the user's ears. DEBUG-only.
    /// </summary>
    internal static class GuiInspector
    {
        public static string Dump()
        {
            var sb = new StringBuilder();
            var screen = ScreenManager.Current;
            if (screen == null) return "(no active screen)\n";

            sb.Append("screen: ").Append(screen.Key).Append(" | ").Append(screen.ScreenName ?? "");
            var nav = Navigation.Active as GraphNavigator;
            var render = nav?.CurrentRender;
            if (render == null) { sb.Append("  (no render)\n"); return sb.ToString(); }
            if (!nav.HasFocus) sb.Append("  (nothing focused)");
            sb.Append('\n');

            object stop = new object(), region = new object(); // sentinels ≠ any real key
            foreach (var node in render.Order)
            {
                if (!Equals(node.StopKey, stop)) { stop = node.StopKey; sb.Append("-- stop: ").Append(stop).Append('\n'); }
                if (!Equals(node.RegionKey, region)) { region = node.RegionKey; if (region != null) sb.Append("   -- region: ").Append(region).Append('\n'); }
                sb.Append(nav.HasFocus && Equals(nav.FocusedNodeId, node.Id) ? "> " : "  ");
                sb.Append(SafeText(node));
                sb.Append("  [").Append(node.Id).Append("]\n");
            }
            return sb.ToString();
        }

        // Nodes resolve live game data when rendering announcements — never let one throw kill the dump.
        private static string SafeText(GraphNode node)
        {
            try { return GraphAnnouncer.LeafText(node); }
            catch (Exception e) { return "<err: " + e.Message + ">"; }
        }
    }
}
#endif
