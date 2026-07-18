using System.Collections.Generic;
using WrathAccess.Input;
using WrathAccess.UI.Graph;

namespace WrathAccess.UI
{
    /// <summary>
    /// The graph-based navigator: runs every screen on the key-graph core (<see cref="KeyGraph"/> over
    /// <see cref="TreeGraphAdapter"/>), replacing the push-based announce discipline with pull-based
    /// diffing — the graph is rebuilt per operation and per frame, focus is reconciled by identity, and
    /// a focus change is announced exactly once no matter what caused it (input, a screen moving focus,
    /// a content rebuild, or the game yanking a VM). Screens keep their retained Container trees and
    /// their existing Navigation API; behavior policies (tree expand/collapse, sheet/table readouts,
    /// tab semantics, tooltip fall-throughs, type-ahead) carry over from the retired push-based
    /// navigator unchanged.
    /// </summary>
    public sealed class GraphNavigator : Navigator
    {
        // One GraphState per LIVE screen (focus cursor, per-stop memory, tree expansion): a screen
        // covered by another (a tooltip reader, a dropdown submenu, any higher layer) keeps its state
        // and restores exactly where you were when focus returns; a POPPED screen's state is dropped
        // (ScreenClosed), so reopening starts fresh — matching the old retained-tree lifecycle.
        private readonly Dictionary<Screens.Screen, GraphState> _states =
            new Dictionary<Screens.Screen, GraphState>();
        private GraphState _state = new GraphState();
        private KeyGraph _graph;

        // The differ's memory: the node identity (and its render node, for context diffing) last spoken.
        private ControlId _lastSpokenKey;
        private GraphNode _lastSpokenNode;

        // A focus request whose target isn't in the render yet (lazy content): applied by EnsureFocus.
        private ControlId _pendingFocus;
        private bool _pendingAnnounce;

        public GraphNavigator()
        {
            _search.OnNoMatch = text => Speak(Loc.T("search.no_match", new { text }), interrupt: true);
        }


        /// <summary>Focus = a focused NODE.</summary>
        public override bool HasFocus => _graph?.CurrentNode != null;

        public override void Attach(Screens.Screen screen)
        {
            bool same = ReferenceEquals(screen, Screen);
            Screen = screen;
            ClearSearch(announce: false);
            if (!same)
            {
                // Swap to this screen's own state (creating it on first attach). The differ memory
                // resets so the (possibly restored) landing announces itself on return.
                if (screen != null)
                {
                    if (!_states.TryGetValue(screen, out _state))
                    {
                        _state = new GraphState();
                        _states[screen] = _state;
                    }
                }
                else
                {
                    _state = new GraphState();
                }
                _lastSpokenKey = null;
                _lastSpokenNode = null;
                _pendingFocus = null;
                _pendingStop = null;
                _liveKey = null;
            }
            _graph = screen != null ? new KeyGraph(() => BuildRender(screen), _state) : null;
        }

        public override void ScreenClosed(Screens.Screen screen)
        {
            if (screen != null) _states.Remove(screen);
        }

        public override void FocusNode(ControlId id, bool announce = true)
        {
            if (id == null) return;
            _pendingFocus = id;
            _pendingAnnounce = announce;
        }

        // A pending land-on-stop request (applied by EnsureFocus once the stop has nodes, like
        // _pendingFocus): resolves to the stop's FIRST node at apply time, so it works when the
        // caller can't know the node keys (a wizard page whose content varies per step).
        private object _pendingStop;

        public override void FocusStop(object stopKey)
        {
            _pendingStop = stopKey;
        }

        public override object FocusedStopKey => _graph?.CurrentNode?.StopKey;

        /// <summary>The live render + focused node id (DEBUG inspection — the /gui dump).</summary>
        internal GraphRender CurrentRender => _graph?.Current;
        internal ControlId FocusedNodeId => _graph?.CurrentNode?.Id;

        // Screens declare fresh from live game state on every render (immediate mode).
        private GraphRender BuildRender(Screens.Screen screen)
        {
            var b = new GraphBuilder(_state.Expanded); // groups consult the persistent expansion set
            screen.Build(b);
            return b.Build();
        }

        public override void Blur()
        {
            _state.CurKey = null;
            _lastSpokenKey = null;
            _lastSpokenNode = null;
            _pendingFocus = null;
            _liveKey = null;
        }

        /// <summary>The per-frame pull: rebuild + reconcile, establish initial focus when content appears,
        /// apply pending focus requests, and announce any focus-identity change exactly once. This single
        /// path replaces the initial-focus debt, the per-callsite announce decisions, and the stranded-
        /// panel special case of the old navigator.</summary>
        public override void EnsureFocus()
        {
            if (Screen == null || _graph == null) return;

            if (_state.CurKey == null && _pendingFocus == null)
            {
                // Unfocused screens (exploration) stay unfocused until Tab seats a cursor.
                if (Screen.StartUnfocused) return;
                if (!_graph.Rerender()) return; // no content yet — Reconcile will seat the start node once there is
                // Declared initial landing (a wizard's page content): seat the stop's landing node
                // (remembered → selected member → first), BEFORE the differ announces below.
                var stop = Screen.InitialFocusStop;
                if (stop != null)
                {
                    var land = KeyGraph.StopLanding(_graph.Current, _graph.State, stop);
                    if (land != null) _graph.Focus(land.Id);
                }
            }
            else
            {
                if (!_graph.Rerender()) return; // nothing focusable this frame — retry
                if (_pendingFocus != null)
                {
                    // One retry frame for a target focused mid-build; a target that still isn't in the
                    // render was removed — drop the request rather than re-seating every frame.
                    if (_graph.Current.Nodes.ContainsKey(_pendingFocus))
                    {
                        _graph.Focus(_pendingFocus);
                        if (!_pendingAnnounce) { _lastSpokenKey = _pendingFocus; _lastSpokenNode = _graph.CurrentNode; }
                    }
                    _pendingFocus = null;
                }
                if (_pendingStop != null)
                {
                    var land = KeyGraph.StopLanding(_graph.Current, _graph.State, _pendingStop);
                    if (land != null) _graph.Focus(land.Id);
                    _pendingStop = null; // announce rides the normal differ below
                }
            }

            var node = _graph.CurrentNode;
            if (node == null) return;

            if (_lastSpokenKey == null || !_lastSpokenKey.Equals(node.Id))
            {
                // Queued (not interrupting): landings follow the screen name / preceding feedback.
                if (FocusMode.Active) Speak(ComposeMove(_lastSpokenNode, node, entry: _lastSpokenNode == null));
                _lastSpokenKey = node.Id;
                _lastSpokenNode = node;
            }

            WatchLive(node);
        }

        // ---- live announcements: watch the FOCUSED node's Live parts and speak a part when its value
        // changes (an async toggle settling, the game flipping a state) — state feedback in the
        // architecture instead of per-element watcher machinery. Baselines silently whenever focus lands
        // on a new identity (the focus announcement already spoke the initial state).
        private ControlId _liveKey;
        private readonly List<string> _liveValues = new List<string>();

        private void WatchLive(GraphNode node)
        {
            var anns = GraphAnnouncer.EffectiveAnnouncements(node); // type-merged + settings-filtered
            if (anns.Count == 0) return;
            bool baseline = _liveKey == null || !_liveKey.Equals(node.Id) || _liveValues.Count != anns.Count;
            if (baseline) { _liveKey = node.Id; _liveValues.Clear(); }

            for (int i = 0; i < anns.Count; i++)
            {
                if (anns[i] == null || !anns[i].Live)
                {
                    if (baseline) _liveValues.Add(null);
                    continue;
                }
                string v = null;
                try { v = anns[i].Text?.Invoke(); } catch { }
                if (baseline) { _liveValues.Add(v); continue; }
                if (!string.Equals(_liveValues[i], v))
                {
                    _liveValues[i] = v;
                    if (!string.IsNullOrEmpty(v) && FocusMode.Active) Speak(v, interrupt: false);
                }
            }
        }

        public override void AnnounceCurrent()
        {
            if (_graph == null) return;
            // An unfocused screen with nothing focused has nothing to announce — and Rerender would
            // auto-seat a phantom focus at the start node.
            if (_state.CurKey == null && Screen != null && Screen.StartUnfocused) return;
            if (!_graph.Rerender()) return;
            var node = _graph.CurrentNode;
            if (node == null) return;
            Speak(ComposeMove(null, node, entry: true));
            _lastSpokenKey = node.Id;
            _lastSpokenNode = node;
        }


        // ---- input ----

        public override bool OnInputJustPressed(InputAction action)
        {
            if (_search.IsSearchActive)
            {
                if (_searchFocusId != null && !_searchFocusId.Equals(_graph?.CurrentNode?.Id))
                    ClearSearch(announce: false); // focus moved under us → results are stale
                else if (action.Key == "ui.home" && _search.ResultCount > 0) { _search.JumpToFirstResult(); return true; }
                else if (action.Key == "ui.end" && _search.ResultCount > 0) { _search.JumpToLastResult(); return true; }
                else if (FiredFromSearchKey(action)) return true; // reserved key — TickTypeahead owns it
                else ClearSearch(announce: false);
            }

            switch (action.Key)
            {
                case "ui.up": return Arrow(NavDirection.Up);
                case "ui.down": return Arrow(NavDirection.Down);
                case "ui.left": return Arrow(NavDirection.Left);
                case "ui.right": return Arrow(NavDirection.Right);
                case "ui.next": return Tab(1);
                case "ui.prev": return Tab(-1);
                case "ui.home": return JumpEdge(first: true);
                case "ui.end": return JumpEdge(first: false);
                // Region jumps consume only when the focused node is IN a region (adapter FlowSheets
                // stamp regions too) — elsewhere Ctrl+arrows bubble (the scanner's category cycle).
                case "ui.regionPrev": return _graph?.CurrentNode?.RegionKey != null && RegionJump(-1);
                case "ui.regionNext": return _graph?.CurrentNode?.RegionKey != null && RegionJump(1);
                case "ui.activate":
                {
                    if (_graph?.CurrentNode == null) return false;
                    VtableActivate();
                    return true;
                }
                case "ui.secondary":
                {
                    var node = _graph?.CurrentNode;
                    if (node == null) return false;
                    if (node.Vtable.OnSecondary != null) _graph.Secondary();
                    return true;
                }
                case "ui.back":
                    return Screen != null && Screen.InvokeAction(ActionIds.Back);
                case "ui.tooltip":
                {
                    var node = _graph?.CurrentNode;
                    if (node == null) return false;
                    if (node.Vtable.OnTooltip != null) { _graph.Tooltip(); return true; }
                    Speak(Loc.T("nav.no_tooltip"));
                    return true;
                }
                default:
                    return false;
            }
        }

        private static GraphDir ToDir(NavDirection dir)
        {
            switch (dir)
            {
                case NavDirection.Up: return GraphDir.Up;
                case NavDirection.Down: return GraphDir.Down;
                case NavDirection.Left: return GraphDir.Left;
                default: return GraphDir.Right;
            }
        }

        private bool Arrow(NavDirection dir)
        {
            var focusNode = _graph?.CurrentNode;
            if (focusNode == null) return false;

            // A focused slider/dropdown adjusts on Left/Right (priority over any navigation).
            if (dir == NavDirection.Left || dir == NavDirection.Right)
            {
                if (VtableAdjust(dir == NavDirection.Right ? 1 : -1)) return true;
            }

            // Edge-wired movement first (rows/grids/flattened tree rows all ride edges).
            var move = _graph.Move(ToDir(dir));
            if (move.Moved) { AnnounceMove(move); return true; }

            // At an edge. Left/Right get tree semantics: expand/collapse a group, descend into an
            // expanded one, ascend from a child — generic engine operations over Parent/Expandable.
            if (dir == NavDirection.Left || dir == NavDirection.Right)
            {
                var tr = dir == NavDirection.Right ? _graph.TreeRight() : _graph.TreeLeft();
                switch (tr.Kind)
                {
                    case KeyGraph.TreeMove.Expanded:
                    case KeyGraph.TreeMove.Collapsed:
                        SpeakFocusedState();
                        return true;
                    case KeyGraph.TreeMove.EmptyGroup:
                        Speak(Loc.T("nav.no_details"), interrupt: true);
                        return true;
                    case KeyGraph.TreeMove.Descended:
                    case KeyGraph.TreeMove.Ascended:
                        AnnounceMove(tr.Move);
                        return true;
                    case KeyGraph.TreeMove.Leaf:
                        return true; // inside a tree; nothing that way — consume
                }
            }

            // Nothing moved: consume edges inside trees (old behavior); bubble from plain lists so
            // an unfocused screen's arrows fall through to the overlay.
            return KeyGraph.InTree(focusNode);
        }

        // Speak the focused group's post-toggle state (its full readout includes expanded/collapsed) and
        // rebaseline the differ+live watch so the toggle isn't re-announced.
        private void SpeakFocusedState()
        {
            var node = _graph.CurrentNode;
            if (node == null) return;
            Speak(GraphAnnouncer.LeafText(node), interrupt: true);
            _lastSpokenKey = node.Id;
            _lastSpokenNode = node;
            _liveKey = null;
        }

        private bool Tab(int step)
        {
            // Snapshot BEFORE rerendering: Reconcile auto-seats a null cursor at the start node, and an
            // unfocused screen's Tab must enter at the first stop, not step from that phantom seat.
            bool wasUnfocused = _state.CurKey == null;
            if (_graph == null || !_graph.Rerender())
            {
                // No focusable content: on an unfocused-capable screen Tab has nothing to enter.
                return false;
            }

            var stops = new List<object>();
            foreach (var n in _graph.Current.Order)
                if (n.StopKey != null && !stops.Contains(n.StopKey)) stops.Add(n.StopKey);
            if (stops.Count == 0) return false;

            var curNode = wasUnfocused ? null : _graph.CurrentNode;
            int idx = curNode != null ? stops.IndexOf(curNode.StopKey) : -1;

            if (idx < 0)
            {
                // Unfocused (exploration): Tab enters at the first/last stop.
                return LandOnStop(stops[step >= 0 ? 0 : stops.Count - 1]);
            }

            int ni = idx + step;
            if (ni < 0 || ni >= stops.Count)
            {
                if (Screen != null && Screen.StartUnfocused)
                {
                    Blur(); // truly unfocused → a later re-entry stays in exploration
                    if (!string.IsNullOrEmpty(Screen.ScreenName)) Speak(Screen.ScreenName, interrupt: true);
                    return true;
                }
                if (Screen != null && Screen.Wrap)
                    ni = ((ni % stops.Count) + stops.Count) % stops.Count;
                else
                    return true; // at the end; consume, no wrap
            }
            return LandOnStop(stops[ni]);
        }

        // Land on a stop (Tab cycling).
        private bool LandOnStop(object stopKey)
        {
            // Remembered position → SELECTED member → first node (the shared StopLanding — so Tab
            // into a radio/tab group lands on the current pick, not the top).
            var land = KeyGraph.StopLanding(_graph.Current, _graph.State, stopKey);
            if (land == null || !_graph.Focus(land.Id)) return true;

            var node = _graph.CurrentNode;
            WrathAccess.UiSound.Hover();
            Speak(ComposeMove(_lastSpokenNode, node, entry: false), interrupt: true);
            _lastSpokenKey = node.Id;
            _lastSpokenNode = node;
            return true;
        }

        private bool JumpEdge(bool first)
        {
            var focusNode = _graph?.CurrentNode;
            if (focusNode == null) return false;

            // In a tree (adapter or native): first/last sibling at the current depth.
            if (KeyGraph.InTree(focusNode))
            {
                var sib = _graph.MoveToSiblingEdge(first);
                if (sib.Moved) AnnounceMove(sib);
                return true;
            }

            // First/last along the vertical axis of the current structure.
            var move = _graph.MoveToEdge(first ? GraphDir.Up : GraphDir.Down);
            if (move.Moved) AnnounceMove(move);
            return true;
        }

        private bool RegionJump(int dir)
        {
            var result = _graph.MoveRegion(dir);
            if (!result.Moved) return true; // no region that way → consume
            AnnounceMove(result, regionEntry: true);
            return true;
        }


        private void AnnounceMove(MoveResult result, bool regionEntry = false)
        {
            var node = result.To;
            if (node == null) return;
            WrathAccess.UiSound.Hover();
            Speak(ComposeMove(result.From, node, entry: false, transitionLabel: result.TransitionLabel, regionEntry: regionEntry), interrupt: true);
            _lastSpokenKey = node.Id;
            _lastSpokenNode = node;
        }

        // Run the focused node's vtable activation; speak its StateText as immediate feedback when it
        // declares one, and rebaseline the live watch so the same change isn't spoken twice.
        private bool VtableActivate()
        {
            var node = _graph.CurrentNode;
            if (node?.Vtable.OnActivate == null) return false;
            _graph.Activate();
            node = _graph.CurrentNode;
            var st = node?.Vtable.StateText;
            if (st != null)
            {
                Speak(st(), interrupt: true);
                _liveKey = null; // rebaseline: the change was just spoken synchronously
            }
            return true;
        }

        private bool VtableAdjust(int sign)
        {
            var node = _graph.CurrentNode;
            if (node?.Vtable.OnAdjust == null) return false;
            _graph.TryAdjust(sign, large: false);
            node = _graph.CurrentNode;
            var st = node?.Vtable.StateText;
            if (st != null)
            {
                Speak(st(), interrupt: true);
                _liveKey = null; // rebaseline: the change was just spoken synchronously
            }
            return true;
        }

        // ---- composition (GraphAnnouncer for lists/trees; ported sheet/table framing for grids) ----

        private string ComposeMove(GraphNode from, GraphNode to, bool entry, string transitionLabel = null, bool regionEntry = false)
        {
            return GraphAnnouncer.Compose(entry ? null : from, to, transitionLabel);
        }


        // ---- type-ahead search (glue ported from TraditionalNavigator; landing goes via the graph) ----

        private readonly TypeAheadSearch _search = new TypeAheadSearch();
        private readonly List<GraphNode> _searchNodes = new List<GraphNode>(); // the focused stop's nodes
        private ControlId _searchFocusId; // where the last result landed (staleness check)
        private WrathAccess.Screens.Screen _lastTypeaheadScreen;
        private int _searchHeldDir;
        private float _searchRepeatIn;

        public override void TickTypeahead()
        {
            if (Screen == null || Screen.CapturesRawInput || !Screen.AllowsTypeahead
                || !FocusMode.Active || _graph?.CurrentNode == null) // node-based: Current (the element) is null on graph-native screens
            {
                if (_search.IsSearchActive || _search.HasBuffer) ClearSearch(announce: false);
                _lastTypeaheadScreen = Screen;
                return;
            }

            if (!ReferenceEquals(Screen, _lastTypeaheadScreen))
            {
                _lastTypeaheadScreen = Screen;
                if (_search.IsSearchActive || _search.HasBuffer) ClearSearch(announce: false);
                return;
            }

            if (UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftControl) || UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightControl)
                || UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftAlt) || UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightAlt))
                return;

            bool shift = UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftShift) || UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightShift);
            if (_search.IsSearchActive && !shift)
            {
                if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Escape)) { ClearSearch(announce: true); return; }
                if (_search.ResultCount > 0 && TickResultArrows()) return;
            }
            else
            {
                _searchHeldDir = 0;
            }

            var typed = UnityEngine.Input.inputString;
            if (string.IsNullOrEmpty(typed)) return;

            foreach (var ch in typed)
            {
                if (char.IsLetter(ch)) TypeChar(ch);
                else if (ch == ' ' && _search.HasBuffer) TypeChar(ch);
            }
        }

        private bool TickResultArrows()
        {
            int dir = UnityEngine.Input.GetKey(UnityEngine.KeyCode.UpArrow) ? -1
                : UnityEngine.Input.GetKey(UnityEngine.KeyCode.DownArrow) ? 1 : 0;
            if (dir == 0) { _searchHeldDir = 0; return false; }

            if (dir != _searchHeldDir)
            {
                _searchHeldDir = dir;
                _searchRepeatIn = WrathAccess.Input.OsKeyboard.InitialDelay;
                _search.NavigateResults(dir);
                return true;
            }

            _searchRepeatIn -= UnityEngine.Time.unscaledDeltaTime;
            if (_searchRepeatIn <= 0f)
            {
                _searchRepeatIn = WrathAccess.Input.OsKeyboard.RepeatInterval;
                _search.NavigateResults(dir);
            }
            return true;
        }

        private static bool FiredFromSearchKey(InputAction action)
        {
            foreach (var b in action.Bindings)
            {
                if (!(b is KeyboardBinding kb) || kb.Ctrl || kb.Alt || !kb.Held()) continue;
                if (kb.Key >= UnityEngine.KeyCode.A && kb.Key <= UnityEngine.KeyCode.Z) return true;
                if (!kb.Shift && (kb.Key == UnityEngine.KeyCode.Space
                    || kb.Key == UnityEngine.KeyCode.UpArrow || kb.Key == UnityEngine.KeyCode.DownArrow
                    || kb.Key == UnityEngine.KeyCode.Escape)) return true;
            }
            return false;
        }

        private void TypeChar(char c)
        {
            RebuildSearchScope();
            if (_searchNodes.Count > 0)
            {
                _search.AddChar(c);
                _search.Search(_searchNodes.Count, i => SearchTextOf(_searchNodes[i]), SearchFocusNodeResult);
            }
        }

        // The node's type-ahead text, STRIPPED of rich-text markup: node labels are raw game strings
        // (speech strips downstream in Tts, so markup is invisible there), and matching against raw
        // markup demoted marked-up titles out of the starts-with tier — "Load Game" in color tags
        // starts with '<', losing to a plain "DLC" substring match.
        private static string SearchTextOf(GraphNode n)
        {
            string t;
            if (n.Vtable.SearchText != null) t = n.Vtable.SearchText();
            else
            {
                var first = n.Vtable.Announcements != null && n.Vtable.Announcements.Count > 0
                    ? n.Vtable.Announcements[0] : null;
                t = first?.Text?.Invoke();
            }
            return t == null ? null : TextUtil.StripRichText(t);
        }

        private void RebuildSearchScope()
        {
            _searchNodes.Clear();
            // The searchable scope is the focused node's Tab-stop.
            var node = _graph?.CurrentNode;
            if (node == null || _graph.Current == null) return;
            foreach (var n in _graph.Current.Order)
                if (Equals(n.StopKey, node.StopKey) && !n.Vtable.ExcludeFromSearch)
                    _searchNodes.Add(n);
        }

        private void SearchFocusNodeResult(int index)
        {
            if (index < 0 || index >= _searchNodes.Count) return;
            if (!_graph.Focus(_searchNodes[index].Id)) return;
            var node = _graph.CurrentNode;
            WrathAccess.UiSound.Hover();
            Speak(ComposeMove(_lastSpokenNode, node, entry: false), interrupt: true);
            _lastSpokenKey = node.Id;
            _lastSpokenNode = node;
            _searchFocusId = node.Id; // the staleness check clears results if focus moves off this
        }

        private void ClearSearch(bool announce)
        {
            bool had = _search.IsSearchActive || _search.HasBuffer;
            _search.Clear();
            _searchNodes.Clear();
            _searchFocusId = null;
            _searchHeldDir = 0;
            if (announce && had) Speak(Loc.T("search.cleared"), interrupt: true);
        }
    }
}
