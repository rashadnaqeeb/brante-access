using System;
using System.Collections.Generic;

namespace WrathAccess.UI.Graph
{
    /// <summary>The outcome of a navigation operation, for the caller (navigator) to announce. The core
    /// never speaks — it returns what happened.</summary>
    public struct MoveResult
    {
        public bool Moved;              // focus actually changed nodes
        public GraphNode From;          // node before the operation (null on first landing)
        public GraphNode To;            // node after (== From when at an edge; null when the graph is empty)
        public string TransitionLabel;  // the crossed edge's spoken line, when it had one
    }

    /// <summary>
    /// The navigation engine: a directed graph of controls rebuilt from a render callback on each
    /// operation, with focus persisting in an external <see cref="GraphState"/>. Ported from Tanglebeep
    /// (with permission), itself from Factorio Access's key-graph.lua. Two invariants carry over:
    ///
    /// <para><b>Down-right total order</b> (<see cref="ComputeOrder"/>): from the start node, go right
    /// until stuck, queueing each down — visits a planar UI in reading order. Nodes down-right can't reach
    /// (later Tab-stops) are appended in declaration order, keeping the order total.</para>
    ///
    /// <para><b>Focus recovery on rebuild</b> (<see cref="Reconcile"/>): if the focused control vanished,
    /// land on the nearest survivor rather than jumping to the start — following the backing object that
    /// moved (tier 1) or the logical control whose backing object was rebuilt (tier 2) first.</para>
    ///
    /// Extensions over the original: Tab-stop cycling and region jumps as operations over node metadata
    /// (with per-stop remembered positions), and per-node secondary/tooltip/adjust behaviors.
    /// </summary>
    public sealed class KeyGraph
    {
        private readonly Func<GraphRender> _renderCallback;
        private readonly GraphState _state;
        private GraphRender _current;

        public KeyGraph(Func<GraphRender> renderCallback, GraphState state)
        {
            _renderCallback = renderCallback;
            _state = state;
        }

        public GraphState State => _state;

        /// <summary>The most recently built render, or null if not yet rendered / empty.</summary>
        public GraphRender Current => _current;

        /// <summary>The focused node in the current render, or null.</summary>
        public GraphNode CurrentNode => _current?.NodeAt(_state.CurKey);

        /// <summary>Rebuild the render and reconcile focus into it. False when the callback produced
        /// nothing (the caller should treat the graph as closed/empty).</summary>
        public bool Rerender()
        {
            _current = _renderCallback();
            if (_current == null || _current.Nodes.Count == 0)
            {
                _current = null;
                return false;
            }
            Reconcile(_current, _state);
            return true;
        }

        /// <summary>
        /// Move focus from the cached <see cref="GraphState.CurKey"/> to a valid control in
        /// <paramref name="render"/>, then recompute the traversal order.
        /// </summary>
        public static void Reconcile(GraphRender render, GraphState state)
        {
            // Honor a pending suggested move first, if its target still exists (consumed either way).
            if (state.NextSuggestedMove != null)
            {
                if (render.Nodes.ContainsKey(state.NextSuggestedMove))
                    state.CurKey = render.Nodes[state.NextSuggestedMove].Id;
                state.NextSuggestedMove = null;
            }

            ControlId old = state.CurKey;
            ControlId resolved = null;

            if (old != null)
            {
                // Tier 1: the same backing object, even if its structural key changed (it moved).
                if (old.Reference != null)
                {
                    foreach (var kv in render.Nodes)
                        if (kv.Value.Id.ReferenceMatches(old.Reference)) { resolved = kv.Value.Id; break; }
                }

                // Tier 2: the same structural key, even if the backing object was rebuilt.
                if (resolved == null)
                {
                    GraphNode structural;
                    if (render.Nodes.TryGetValue(old, out structural)) resolved = structural.Id;
                }

                // Fallback: nearest survivor walking the previous order backward.
                if (resolved == null && state.KeyOrder != null)
                {
                    int oldIndex = IndexOf(state.KeyOrder, old);
                    if (oldIndex >= 0)
                        for (int i = oldIndex; i >= 0; i--)
                        {
                            GraphNode survivor;
                            if (render.Nodes.TryGetValue(state.KeyOrder[i], out survivor))
                            {
                                resolved = survivor.Id;
                                break;
                            }
                        }
                }
            }

            // Nothing matched (or first render): the start node — but prefer the SELECTED member of its
            // stop (initial focus lands on the checked radio/tab, not the top of a long list).
            if (resolved == null)
            {
                var startNode = render.Nodes.ContainsKey(render.StartKey) ? render.Nodes[render.StartKey] : null;
                var sel = startNode != null ? SelectedNodeInStop(render, startNode.StopKey) : null;
                resolved = sel?.Id ?? startNode?.Id ?? render.StartKey;
            }

            state.CurKey = resolved;
            RememberStop(render, state, resolved);
            state.KeyOrder = ComputeOrder(render);
        }

        /// <summary>
        /// The down-right total order: go right until stuck (recording each node), queue every down for a
        /// later pass, repeat — then append any node the walk never reached (e.g. later Tab-stops, which
        /// have no cross-stop edges) in declaration order, so the order is total.
        /// </summary>
        public static List<ControlId> ComputeOrder(GraphRender render)
        {
            var order = new List<ControlId>();
            var seen = new HashSet<ControlId>();
            var downFringe = new List<ControlId> { render.StartKey };

            int i = 0;
            while (i < downFringe.Count)
            {
                ControlId k = downFringe[i];
                while (!seen.Contains(k))
                {
                    seen.Add(k);
                    order.Add(k);

                    GraphNode n;
                    if (!render.Nodes.TryGetValue(k, out n)) break;

                    Transition d, t;
                    if (n.Transitions.TryGetValue(GraphDir.Down, out d) && d != null)
                        downFringe.Add(d.Destination);
                    if (!n.Transitions.TryGetValue(GraphDir.Right, out t) || t == null) break;
                    k = t.Destination;
                }
                i++;
            }

            foreach (var node in render.Order)
                if (seen.Add(node.Id)) order.Add(node.Id);

            return order;
        }

        private static int IndexOf(List<ControlId> order, ControlId key)
        {
            for (int i = 0; i < order.Count; i++)
                if (order[i].Equals(key)) return i;
            return -1;
        }

        private static void RememberStop(GraphRender render, GraphState state, ControlId key)
        {
            var node = render.NodeAt(key);
            if (node?.StopKey != null) state.StopMemory[node.StopKey] = key;
        }

        private void SetCurrent(GraphNode node)
        {
            _state.CurKey = node.Id;
            if (node.StopKey != null) _state.StopMemory[node.StopKey] = node.Id;
        }

        // ---- navigation operations ----

        /// <summary>One step in <paramref name="dir"/>. Not moved (at an edge / empty) → To == From.</summary>
        public MoveResult Move(GraphDir dir)
        {
            var result = default(MoveResult);
            if (!Rerender()) return result;

            var node = CurrentNode;
            result.From = node;
            result.To = node;
            if (node == null) return result;

            Transition t;
            node.Transitions.TryGetValue(dir, out t);
            var dest = t != null ? _current.NodeAt(t.Destination) : null;
            if (dest == null || dest == node) return result;

            SetCurrent(dest);
            result.To = dest;
            result.Moved = true;
            result.TransitionLabel = t.Label;
            return result;
        }

        /// <summary>As far as possible in <paramref name="dir"/> (Home/End within a row or column).</summary>
        public MoveResult MoveToEdge(GraphDir dir)
        {
            var result = default(MoveResult);
            if (!Rerender()) return result;

            var node = CurrentNode;
            result.From = node;
            result.To = node;
            if (node == null) return result;

            var cur = node;
            while (true)
            {
                Transition t;
                if (!cur.Transitions.TryGetValue(dir, out t) || t == null) break;
                var next = _current.NodeAt(t.Destination);
                if (next == null || next == cur) break;
                cur = next;
            }

            if (cur != node)
            {
                SetCurrent(cur);
                result.To = cur;
                result.Moved = true;
            }
            return result;
        }

        /// <summary>Cycle to the next/previous Tab-stop (declaration order), landing on the stop's
        /// remembered position (else its first node). <paramref name="wrap"/> continues past the ends;
        /// without it, at the last/first stop the result is not-moved (the caller may blur instead).</summary>
        public MoveResult MoveStop(int dir, bool wrap)
        {
            var result = default(MoveResult);
            if (!Rerender()) return result;

            var node = CurrentNode;
            result.From = node;
            result.To = node;
            if (node == null) return result;

            var stops = StopOrder();
            if (stops.Count <= 1) return result;

            int idx = stops.IndexOf(node.StopKey);
            if (idx < 0) return result;
            int ni = idx + dir;
            if (wrap) ni = ((ni % stops.Count) + stops.Count) % stops.Count;
            if (ni < 0 || ni >= stops.Count || ni == idx) return result;

            var dest = StopLanding(stops[ni]);
            if (dest == null) return result;

            SetCurrent(dest);
            result.To = dest;
            result.Moved = true;
            return result;
        }

        /// <summary>Jump to the next/previous region within the current stop (declaration order), landing
        /// on the region's first node.</summary>
        public MoveResult MoveRegion(int dir)
        {
            var result = default(MoveResult);
            if (!Rerender()) return result;

            var node = CurrentNode;
            result.From = node;
            result.To = node;
            if (node == null || node.RegionKey == null) return result;

            var regions = new List<object>();
            foreach (var n in _current.Order)
                if (Equals(n.StopKey, node.StopKey) && n.RegionKey != null && !regions.Contains(n.RegionKey))
                    regions.Add(n.RegionKey);

            int idx = regions.IndexOf(node.RegionKey);
            int ni = idx + dir;
            if (idx < 0 || ni < 0 || ni >= regions.Count) return result;

            foreach (var n in _current.Order)
                if (Equals(n.StopKey, node.StopKey) && Equals(n.RegionKey, regions[ni]))
                {
                    SetCurrent(n);
                    result.To = n;
                    result.Moved = true;
                    return result;
                }
            return result;
        }

        /// <summary>Move focus to a specific control (a node just revealed, a screen's chosen landing).
        /// False when it isn't in the render.</summary>
        public bool Focus(ControlId id)
        {
            if (id == null || !Rerender()) return false;
            var node = _current.NodeAt(id);
            if (node == null) return false;
            SetCurrent(node);
            return true;
        }

        /// <summary>Tier-1 focus sync from the game: if a node's backing object is
        /// <paramref name="reference"/>, move focus there. True if focus changed nodes.</summary>
        public bool FocusByReference(object reference)
        {
            if (reference == null || _current == null) return false;
            foreach (var kv in _current.Nodes)
                if (kv.Value.Id.ReferenceMatches(reference))
                {
                    bool changed = _state.CurKey == null || !_state.CurKey.Equals(kv.Value.Id);
                    SetCurrent(kv.Value);
                    return changed;
                }
            return false;
        }

        private List<object> StopOrder()
        {
            var stops = new List<object>();
            foreach (var n in _current.Order)
                if (n.StopKey != null && !stops.Contains(n.StopKey)) stops.Add(n.StopKey);
            return stops;
        }

        /// <summary>Where focus lands when entering a stop with no active cursor: the remembered
        /// position, else the SELECTED member (a radio/tab/list item currently checked — the old
        /// RepresentativeChild behavior; a boon on long lists), else the stop's first node.</summary>
        public GraphNode StopLanding(object stopKey) => StopLanding(_current, _state, stopKey);

        internal static GraphNode StopLanding(GraphRender render, GraphState state, object stopKey)
        {
            ControlId remembered;
            if (state.StopMemory.TryGetValue(stopKey, out remembered))
            {
                var node = render.NodeAt(remembered);
                if (node != null && Equals(node.StopKey, stopKey)) return node;
            }
            var selected = SelectedNodeInStop(render, stopKey);
            if (selected != null) return selected;
            foreach (var n in render.Order)
                if (Equals(n.StopKey, stopKey)) return n;
            return null;
        }

        /// <summary>The first node in a stop that reads as SELECTED — carries a non-empty selected-kind
        /// announcement part (SelectionItem / ChoiceOption / Tab / radio all declare one), or null.</summary>
        public static GraphNode SelectedNodeInStop(GraphRender render, object stopKey)
        {
            foreach (var n in render.Order)
            {
                if (!Equals(n.StopKey, stopKey)) continue;
                var anns = n.Vtable?.Announcements;
                if (anns == null) continue;
                foreach (var a in anns)
                    if (a != null && a.Kind == AnnouncementKinds.Selected)
                    {
                        string t = null;
                        try { t = a.Text?.Invoke(); } catch { }
                        if (!string.IsNullOrEmpty(t)) return n;
                    }
            }
            return null;
        }

        // ---- tree operations (Right/Left semantics for expandable groups) ----

        /// <summary>What a tree side-step did (the caller composes the speech).</summary>
        public enum TreeMove
        {
            None,       // not applicable here (not in a tree / nothing to do) — caller decides consume/bubble
            Expanded,   // the focused group expanded (focus unchanged; speak its new state)
            Collapsed,  // the focused group collapsed (focus unchanged; speak its new state)
            EmptyGroup, // expanding found no children — auto-recollapsed (speak "no details")
            Descended,  // moved to the group's first child (announce as a move)
            Ascended,   // moved to the nearest focusable ancestor (announce as a move)
            Leaf,       // Right on a non-group inside a tree — consumed, nothing to descend into
        }

        public struct TreeResult
        {
            public TreeMove Kind;
            public MoveResult Move; // valid for Descended/Ascended
        }

        /// <summary>Is this node part of an expandable structure (itself a group, or under one)? The
        /// navigator uses this to decide whether Left/Right get tree semantics.</summary>
        public static bool InTree(GraphNode node)
        {
            for (var n = node; n != null; n = n.Parent)
                if (n.Expandable) return true;
            return false;
        }

        /// <summary>Right on a group: expand (auto-recollapse when it turns out empty), or descend into an
        /// expanded one. Right elsewhere in a tree: Leaf (consume). Assumes a current render.</summary>
        public TreeResult TreeRight()
        {
            var result = new TreeResult { Kind = TreeMove.None };
            if (!Rerender()) return result;
            var node = CurrentNode;
            if (node == null) return result;

            if (node.Expandable && !node.Expanded)
            {
                SetExpanded(node, true);
                if (!Rerender()) return result;
                var header = _current.NodeAt(node.Id);
                if (header == null) return result;
                if (FirstChildOf(header) == null)
                {
                    // A lazy drill-in that resolved to nothing: don't leave a silent empty-expanded node.
                    SetExpanded(header, false);
                    Rerender();
                    result.Kind = TreeMove.EmptyGroup;
                    return result;
                }
                result.Kind = TreeMove.Expanded;
                return result;
            }

            if (node.Expandable && node.Expanded)
            {
                var child = FirstChildOf(node);
                if (child == null) { result.Kind = TreeMove.Leaf; return result; }
                result.Move.From = node;
                SetCurrent(child);
                result.Move.To = child;
                result.Move.Moved = true;
                result.Kind = TreeMove.Descended;
                return result;
            }

            result.Kind = InTree(node) ? TreeMove.Leaf : TreeMove.None;
            return result;
        }

        /// <summary>Left on an expanded group: collapse. Left elsewhere in a tree: ascend to the nearest
        /// focusable ancestor. Assumes a current render.</summary>
        public TreeResult TreeLeft()
        {
            var result = new TreeResult { Kind = TreeMove.None };
            if (!Rerender()) return result;
            var node = CurrentNode;
            if (node == null) return result;

            if (node.Expandable && node.Expanded)
            {
                SetExpanded(node, false);
                Rerender(); // focus stays on the header by identity
                result.Kind = TreeMove.Collapsed;
                return result;
            }

            for (var p = node.Parent; p != null; p = p.Parent)
            {
                if (!p.Focusable || !_current.Nodes.ContainsKey(p.Id)) continue;
                result.Move.From = node;
                var target = _current.NodeAt(p.Id);
                SetCurrent(target);
                result.Move.To = target;
                result.Move.Moved = true;
                result.Kind = TreeMove.Ascended;
                return result;
            }

            result.Kind = InTree(node) ? TreeMove.Leaf : TreeMove.None;
            return result;
        }

        /// <summary>Home/End inside a tree: the first/last node sharing the focused node's parent (its
        /// siblings at the current depth). Assumes a current render.</summary>
        public MoveResult MoveToSiblingEdge(bool first)
        {
            var result = default(MoveResult);
            if (!Rerender()) return result;
            var node = CurrentNode;
            result.From = node;
            result.To = node;
            if (node == null) return result;

            GraphNode target = null;
            foreach (var n in _current.Order)
            {
                if (!ReferenceEquals(n.Parent, node.Parent)) continue;
                if (first) { target = n; break; }
                target = n; // last match wins
            }
            if (target == null || target == node) return result;
            SetCurrent(target);
            result.To = target;
            result.Moved = true;
            return result;
        }

        // Change a group's expansion: through its vtable override when declared (the adapter driving a
        // retained Container), else the persistent set.
        private void SetExpanded(GraphNode group, bool expanded)
        {
            if (expanded && group.Vtable.OnExpand != null) { group.Vtable.OnExpand(); return; }
            if (!expanded && group.Vtable.OnCollapse != null) { group.Vtable.OnCollapse(); return; }
            if (expanded) _state.Expanded.Add(group.Id);
            else _state.Expanded.Remove(group.Id);
        }

        private GraphNode FirstChildOf(GraphNode group)
        {
            foreach (var n in _current.Order)
                if (ReferenceEquals(n.Parent, group)) return n;
            return null;
        }

        // ---- behavior invokers (the caller announces fallbacks / state) ----

        /// <summary>Run the focused control's primary activation. False = it has none.</summary>
        public bool Activate()
        {
            if (!Rerender()) return false;
            var node = CurrentNode;
            if (node?.Vtable.OnActivate == null) return false;
            node.Vtable.OnActivate();
            return true;
        }

        /// <summary>Run the focused control's secondary activation. False = it has none.</summary>
        public bool Secondary()
        {
            if (!Rerender()) return false;
            var node = CurrentNode;
            if (node?.Vtable.OnSecondary == null) return false;
            node.Vtable.OnSecondary();
            return true;
        }

        /// <summary>Run the focused control's tooltip behavior. False = it has none.</summary>
        public bool Tooltip()
        {
            if (!Rerender()) return false;
            var node = CurrentNode;
            if (node?.Vtable.OnTooltip == null) return false;
            node.Vtable.OnTooltip();
            return true;
        }

        /// <summary>If the focused control adjusts horizontally (a slider), adjust and return true;
        /// false = the caller should navigate instead.</summary>
        public bool TryAdjust(int sign, bool large)
        {
            if (!Rerender()) return false;
            var node = CurrentNode;
            if (node?.Vtable.OnAdjust == null) return false;
            node.Vtable.OnAdjust(sign, large);
            return true;
        }
    }
}
