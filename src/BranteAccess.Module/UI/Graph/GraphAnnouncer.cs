using System;
using System.Collections.Generic;
using System.Text;

namespace BranteAccess.Module.UI.Graph
{
    /// <summary>
    /// Composes the spoken line for a focus change by diffing the old and new focus PATHS - each node's
    /// ancestor chain (<see cref="GraphNode.Parent"/>) plus the node itself, compared by identity. Newly-
    /// entered levels read outermost-first, then the landing control, recursing as deep as the hierarchy
    /// goes. Sibling moves share the whole prefix and read just the control; ascends likewise; and
    /// descending from a group onto its own child re-announces nothing but the child - the group is on
    /// the child's chain AND is the from-node, so the prefix swallows it. Ported from wotr-access.
    /// </summary>
    public static class GraphAnnouncer
    {
        /// <summary>The line for landing on <paramref name="to"/> having come from <paramref name="from"/>
        /// (null = from nothing: the full path reads). <paramref name="transitionLabel"/> is the crossed
        /// edge's spoken line, when it had one. Null when there is nothing to say.</summary>
        public static string Compose(GraphNode from, GraphNode to, string transitionLabel = null)
        {
            if (to == null) return null;

            var toPath = PathOf(to);
            var fromPath = from != null ? PathOf(from) : EmptyPath;

            // Common prefix by identity - levels we were already inside (or ON: descending from a group
            // onto its child keeps the group in the prefix) stay silent.
            int i = 0;
            while (i < fromPath.Count && i < toPath.Count && fromPath[i].Id.Equals(toPath[i].Id)) i++;

            var parts = new List<string>();
            if (!string.IsNullOrEmpty(transitionLabel)) parts.Add(transitionLabel);

            if (i >= toPath.Count)
            {
                // Ascended (or same node): announce just the now-innermost focus.
                var text = LeafText(to);
                if (!string.IsNullOrEmpty(text)) parts.Add(text);
            }
            else
            {
                for (int j = i; j < toPath.Count; j++)
                {
                    var text = LeafText(toPath[j]);
                    if (string.IsNullOrEmpty(text)) continue;
                    // Dedupe: a level whose label just duplicates the next level down (or the control
                    // itself - a section wrapping a control of the same name).
                    if (j + 1 < toPath.Count)
                    {
                        var label = FirstPartText(toPath[j]);
                        var next = FirstPartText(toPath[j + 1]);
                        if (!string.IsNullOrEmpty(label) && !string.IsNullOrEmpty(next)
                            && DuplicatesNext(label, next)) continue;
                    }
                    parts.Add(text);
                }
            }

            if (parts.Count == 0) return null;
            var sb = new StringBuilder();
            for (int p = 0; p < parts.Count; p++)
            {
                if (p > 0) sb.Append(", ");
                sb.Append(parts[p]);
            }
            return sb.ToString();
        }

        /// <summary>The full readout for a landing with no prior focus (screen entry, focus restore).</summary>
        public static string ComposeFull(GraphNode to) => Compose(null, to);

        private static readonly List<GraphNode> EmptyPath = new List<GraphNode>();

        // The node's path: ancestors outermost-first, then the node itself.
        private static List<GraphNode> PathOf(GraphNode node)
        {
            var path = new List<GraphNode>();
            for (var n = node; n != null; n = n.Parent) path.Add(n);
            path.Reverse();
            return path;
        }

        /// <summary>Pluggable per-part filter - installed when a per-type announcement settings layer
        /// lands; null (now, and in tests) = everything speaks. Returning false drops the part from
        /// readouts AND from the live watch.</summary>
        public static Func<ControlType, NodeAnnouncement, bool> PartFilter;

        /// <summary>
        /// A node's EFFECTIVE announcement parts: the control type's common parts (the role word) merged
        /// with the node's own - a node part overrides a common part of the same kind - sorted by the
        /// type's kind order (unknown/kindless parts append in declaration order), then filtered. This is
        /// the single list readouts and the live watch operate on.
        /// </summary>
        public static List<NodeAnnouncement> EffectiveAnnouncements(GraphNode node)
        {
            var result = new List<NodeAnnouncement>();
            var vt = node?.Vtable;
            if (vt == null) return result;
            var type = vt.ControlType;

            var common = type?.Common?.Invoke();
            if (common != null)
                foreach (var c in common)
                    if (c != null && !HasKind(vt.Announcements, c.Kind)) result.Add(c);
            if (vt.Announcements != null)
                foreach (var a in vt.Announcements)
                    if (a != null) result.Add(a);

            if (type?.Order != null && type.Order.Length > 0 && result.Count > 1)
            {
                // Stable: composite key = (kind's order index, declaration index) - List.Sort alone is
                // unstable and would scramble same-bucket (kindless) parts.
                var keyed = new List<KeyValuePair<long, NodeAnnouncement>>(result.Count);
                for (int i = 0; i < result.Count; i++)
                    keyed.Add(new KeyValuePair<long, NodeAnnouncement>(
                        (long)OrderIndex(type.Order, result[i].Kind) << 32 | (uint)i, result[i]));
                keyed.Sort((x, y) => x.Key.CompareTo(y.Key));
                result.Clear();
                foreach (var kv in keyed) result.Add(kv.Value);
            }

            if (PartFilter != null)
                result.RemoveAll(a => !PartFilter(type, a));
            return result;
        }

        private static bool HasKind(IReadOnlyList<NodeAnnouncement> anns, string kind)
        {
            if (anns == null || kind == null) return false;
            foreach (var a in anns)
                if (a != null && a.Kind == kind) return true;
            return false;
        }

        // Sort key: declared kinds by their order index; everything else after (one shared bucket, with
        // the declaration-index tie-break above keeping their relative order).
        private static int OrderIndex(string[] order, string kind)
        {
            if (kind != null)
                for (int i = 0; i < order.Length; i++)
                    if (order[i] == kind) return i;
            return order.Length;
        }

        /// <summary>A node's own readout: its effective announcement parts, resolved live, non-empty ones
        /// joined - plus, for an expandable group, its expanded/collapsed state word. The first part is
        /// the control's label, so path dedupe's prefix check applies.</summary>
        public static string LeafText(GraphNode node)
        {
            var anns = EffectiveAnnouncements(node);
            var sb = new StringBuilder();
            for (int i = 0; i < anns.Count; i++)
            {
                string t = null;
                // Guarded like the live-watch and selected-probe invocations: these delegates read
                // live game state and a transient throw must cost one part, not the input loop -
                // an unwind here repeats every frame (same node, same compose) and locks the keyboard.
                if (anns[i]?.Text != null)
                    try { t = anns[i].Text(); }
                    catch (Exception e) { Mod.Warn("announcement part threw on " + node?.Id + ": " + e.Message); }
                if (string.IsNullOrEmpty(t)) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(t);
            }
            if (node != null && node.Expandable && !node.Vtable.SpeaksOwnExpansion && ExpandedStateText != null)
            {
                var state = ExpandedStateText(node.Expanded);
                if (!string.IsNullOrEmpty(state))
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(state);
                }
            }

            // The auto-stamped sibling position, unless the node carries its own (an explicit
            // position-kind part, or a composed message).
            if (node != null && node.PositionCount > 1 && PositionText != null
                && !node.Vtable.SpeaksOwnPosition && !HasKind(node.Vtable.Announcements, AnnouncementKinds.Position)
                && (PartFilter == null || PartFilter(node.Vtable.ControlType, AutoPositionProbe)))
            {
                var pos = PositionText(node.PositionIndex, node.PositionCount);
                if (!string.IsNullOrEmpty(pos))
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(pos);
                }
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }

        /// <summary>Pluggable "n of m" wording (localized - wired by the module at load); null = no auto
        /// positions.</summary>
        public static Func<int, int, string> PositionText;

        // A stand-in part handed to the PartFilter so a position-kind toggle governs the auto-stamped
        // position too.
        private static readonly NodeAnnouncement AutoPositionProbe =
            new NodeAnnouncement(() => null, kind: AnnouncementKinds.Position);

        /// <summary>Pluggable expanded/collapsed wording for group headers (localized - wired by the
        /// module at load); null = groups don't speak their state.</summary>
        public static Func<bool, string> ExpandedStateText;

        /// <summary>The first announcement part's text (the label) - for dedupe and search fallbacks.</summary>
        public static string FirstPartText(GraphNode node)
        {
            var anns = node?.Vtable?.Announcements;
            if (anns == null || anns.Count == 0) return null;
            try { return anns[0]?.Text?.Invoke(); }
            catch (Exception e)
            {
                Mod.Warn("label part threw on " + node.Id + ": " + e.Message);
                return null;
            }
        }

        // The next part "starts as" this label: equal, or its first comma-separated segment is the label
        // (a control's readout leads with its label).
        private static bool DuplicatesNext(string label, string next)
        {
            if (!next.StartsWith(label)) return false;
            return next.Length == label.Length || next[label.Length] == ',';
        }
    }
}
