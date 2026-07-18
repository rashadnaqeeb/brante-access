using System.Collections.Generic;
using System.Text;

namespace WrathAccess.Exploration.Announce
{
    /// <summary>
    /// Turns a scan item's ordered parts into one spoken line: skips parts disabled for this element type
    /// (per <see cref="ScanAnnounceContext"/>) and empty renders, comma-joins the rest. The proxy yields
    /// its parts already in canonical order (name, type, state…, spatial), so no separate order table.
    /// </summary>
    internal static class ScanAnnounceComposer
    {
        public static string Compose(string elementKey, IEnumerable<ScanAnnouncement> parts)
        {
            var ctx = new ScanAnnounceContext(elementKey);
            var sb = new StringBuilder();
            foreach (var p in parts)
            {
                if (p == null || !ctx.ResolveBool(p.Key, "enabled", true)) continue;
                var text = p.Render(ctx)?.Resolve();
                if (string.IsNullOrEmpty(text)) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(text);
            }
            return sb.ToString();
        }
    }
}
