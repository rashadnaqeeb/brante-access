using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Controllers.Clicks.Handlers; // ClickGroundHandler
using Kingmaker.EntitySystem.Entities; // UnitEntityData
using Kingmaker.View; // ObstacleAnalyzer (navmesh Area reachability)
using UnityEngine;
using WrathAccess.Screens;

namespace WrathAccess.Exploration
{
    /// <summary>The review cursor's cycle groups (Comma/Period/N/M/B): living party / living enemies /
    /// living neutrals / everything else in the taxonomy (loot incl. corpses, doors, exits, search
    /// points, traps, mechanisms — scenery excluded) / points of interest (their own cycle, and
    /// excluded from Others since markers often duplicate entities — same logic as the All bucket).</summary>
    internal enum ReviewGroup { Party, Enemies, Neutrals, Others, Poi, Unexplored }

    /// <summary>
    /// The scanner: a categorized, distance-sorted list of things in the current area, browsed with
    /// PageUp/Down (items) and Ctrl+PageUp/Down (categories). Its selection IS the <b>review cursor</b>
    /// — the look-without-moving counterpart of the movement cursor (NVDA object-navigator style):
    /// Comma/Period/N/M cycle it through nearby party / enemies / neutrals / everything else, sorted
    /// closest-to-farthest FROM the movement cursor (Shift+key cycles backward), announcing name +
    /// state + bearing/distance — a tactical overview that never moves your position. I interacts with
    /// the review target; Home plants the movement cursor ON it (the explicit opt-in); K reads the
    /// movement cursor; Shift+K reads the whole party.
    ///
    /// Active only while Focus Mode owns the keyboard AND the plain in-game context is on top (no
    /// dialogue/menu/window), so our keys never collide with the game's. Distances/bearings are relative
    /// to the main character. Lists rebuild on every action (cheap; always fresh), sorted by distance.
    /// </summary>
    internal static class Scanner
    {
        // Two-level buckets, rebuilt each action: one list per taxonomy node (a category's own list IS
        // its "All" — the union of its subcategories), plus the synthetic top-level "Everything".
        private static readonly Dictionary<string, List<ScanItem>> _byNode =
            new Dictionary<string, List<ScanItem>>();
        private static readonly List<ScanItem> _everything = new List<ScanItem>();

        // Category cursor: 0 = the synthetic "Everything", 1.. = ScanTaxonomy.Categories. Subcategory
        // cursor: index into NavSubcategories(currentCategory) (0 = the category's "All" entry).
        private static int _catIndex;
        private static int _subIndex;
        private static int _itemIndex;   // index into the current (sub)category's list
        private static bool _entered;    // first scanner key announces the current spot without moving
        private static bool _debugAll;   // debug (F11): list everything, ignoring the visibility filter

        private static int CatCount => ScanTaxonomy.Categories.Count + 1; // +1 for Everything
        private static bool OnEverything => _catIndex <= 0;
        private static ScanTaxonomy.Node CurrentCategory
            => OnEverything ? null : ScanTaxonomy.Categories[_catIndex - 1];
        private static IReadOnlyList<ScanTaxonomy.Node> CurrentSubs
            => OnEverything ? null : ScanTaxonomy.NavSubcategories(CurrentCategory);
        // The (sub)category node whose item list we're browsing (null on Everything).
        private static ScanTaxonomy.Node CurrentNode
        {
            get
            {
                if (OnEverything) return null;
                var subs = CurrentSubs;
                return subs[Mathf.Clamp(_subIndex, 0, subs.Count - 1)];
            }
        }
        private static List<ScanItem> CurrentList
        {
            get
            {
                if (OnEverything) return _everything;
                var node = CurrentNode;
                return node != null && _byNode.TryGetValue(node.Key, out var l) ? l : null;
            }
        }

        private static string SpokenLabel(ScanTaxonomy.Node n) => Loc.T(n.LocKey);
        private static string CategoryLabel
            => OnEverything ? Loc.T("scan.everything") : SpokenLabel(CurrentCategory);
        private static string SubcategoryLabel
            => OnEverything ? Loc.T("scan.everything")
             : _subIndex <= 0 ? Loc.T("scan.all_of", new { label = SpokenLabel(CurrentCategory) })
             : SpokenLabel(CurrentNode);

        private static bool Active =>
            FocusMode.Active && ScreenManager.Current != null && ScreenManager.Current.Key == "ctx.ingame";

        // The reference unit for distances/bearings: in turn-based the acting unit (so readouts are relative
        // to whoever's turn it is), otherwise the main character.
        private static UnitEntityData Leader
        {
            get { var p = Game.Instance?.Player; return CombatMode.ReferenceUnit ?? (p != null ? p.MainCharacter.Value : null); }
        }

        private static Vector3 Reference => Geo.Live(Leader);

        // The scan list reads (and sorts) relative to the shared cursor when one is placed — so you can
        // "look around" from the cursor or a thing you planted it on (Home), or from wherever the tile
        // overlay last moved it. Falls back to the player when no cursor is set.
        private static Vector3 ScanFrom => Cursor.Has ? Cursor.Position.Value : Reference;

        // ---- input entry points (gated) ----
        public static void NextItem() { if (Active) StepItem(1); }
        public static void PrevItem() { if (Active) StepItem(-1); }
        public static void NextCategory() { if (Active) StepCategory(1); }
        public static void PrevCategory() { if (Active) StepCategory(-1); }
        public static void NextSubcategory() { if (Active) StepSubcategory(1); }
        public static void PrevSubcategory() { if (Active) StepSubcategory(-1); }
        public static void CursorToSelected() { if (Active) CommitCursor(); }
        public static void AnnounceCursor() { if (Active) SpeakCursor(); }
        public static void AnnounceWhereAmI() { if (Active) SpeakWhereAmI(); }

        /// <summary>Describe target (X) / describe room (Shift+X): speak the mod-authored description
        /// of the focused scanner object (its asset description) or of the room the cursor is standing
        /// in. Distinct from the game's own examine text — always "extra description from the mod".</summary>
        public static void DescribeTarget() { if (Active) Speak(EnvDescriptions.DescribeItem(Reviewed)); }
        public static void DescribeRoom() { if (Active) Speak(EnvDescriptions.DescribeRoom(ScanFrom)); }
        public static void AnnounceParty() { if (Active) SpeakParty(); }
        // While aiming an ability, the act-on-target inputs commit the cast instead of their normal job:
        // I → cast on the selected scanner item, Enter → cast at the cursor, Backspace → cancel aim.
        public static void InteractSelected()
        {
            if (!Active) return;
            if (Targeting.Aiming) { Targeting.CommitOn(Selected); return; }
            DoInteract(Selected, Loc.T("scan.no_item"));
        }
        // Enter = left click: interact with the object/unit the cursor is INSIDE (not the scanner's list
        // selection). Same context-sensitive action a left click would do (select/attack/talk/open/loot).
        public static void InteractAtCursor()
        {
            if (!Active) return;
            if (Targeting.Aiming) { Targeting.CommitAtCursor(); return; }
            DoInteract(CursorTarget.Inside(), Loc.T("scan.nothing_here"));
        }
        public static void MoveToCursor()
        {
            if (!Active) return;
            if (Targeting.Aiming) { Targeting.Cancel(); return; }
            DoMoveToCursor();
        }
        /// <summary>Cycle the review cursor through a group, nearest-first from the movement cursor.
        /// The landing becomes the scanner selection too, so PageUp/Down, I, and Home all follow it.</summary>
        public static void CycleReview(ReviewGroup group, int dir)
        {
            if (!Active) return;
            // The frontier is derived data recomputed on demand (full-grid work) — refresh it on ITS
            // key, before the rebuild below folds it in, never per frame.
            if (group == ReviewGroup.Unexplored) { FrontierModel.Refresh(); WorldModel.FoldFrontier(); }
            DoCycleReview(group, dir);
        }

        /// <summary>Cycle the review cursor through the CURRENT ROOM's exits (V / Shift+V): the room
        /// map's geometric openings to neighbouring rooms, plus door and area-transition items in this
        /// room (a closed door cuts the navmesh, so the door IS the exit there; transitions sit where
        /// the mesh just ends). An item within a couple of metres of an opening replaces it (richer:
        /// name, state, interactable).</summary>
        public static void CycleRoomExits(int dir) { if (Active) DoCycleRoomExits(dir); }

        public static void ToggleDebugShowAll()
        {
            if (!Active) return;
            _debugAll = !_debugAll;
            Speak("Scanner debug: " + (_debugAll ? "showing all, including hidden" : "showing visible only"));
        }

        // Debug (F9): speak + log every AREA PART of the current area — name, indoor flag, which one
        // is current, and (log only) the bounds extents. Dev tooling, English by design.
        public static void DebugDumpAreaParts()
        {
            if (!Active) return;
            var area = Game.Instance?.CurrentlyLoadedArea;
            if (area == null) { Speak("No area"); return; }
            var current = Kingmaker.Blueprints.Area.AreaService.Instance?.CurrentAreaPart;

            var all = new List<Kingmaker.Blueprints.Area.BlueprintAreaPart> { area }; // the area IS part 0
            if (area.Parts != null)
                foreach (var pr in area.Parts)
                {
                    var part = pr?.Get();
                    if (part != null) all.Add(part);
                }

            var spoken = new List<string>();
            Main.Log?.Log("[areaparts] === " + all.Count + " parts in " + area.name + " ===");
            foreach (var part in all)
            {
                // AreaLocalName is the SUB-part field; the area itself (part 0) is named by AreaName —
                // the same fallback AreaDisplayName uses.
                string name = part.AreaLocalName != null && !part.AreaLocalName.IsEmpty()
                    ? TextUtil.StripRichText(part.AreaLocalName)
                    : part is Kingmaker.Blueprints.Area.BlueprintArea asArea && asArea.AreaName != null
                        ? TextUtil.StripRichText(asArea.AreaName) : "(unnamed)";
                var line = name
                    + (part == current ? " (current)" : "")
                    + (part.IsIndoor ? ", indoors" : "");
                spoken.Add(line);
                var b = part.Bounds != null ? part.Bounds.LocalMapBounds : default(Bounds);
                Main.Log?.Log("[areaparts] bp='" + part.name + "' name='" + name + "'"
                    + (part == current ? " CURRENT" : "") + " indoor=" + part.IsIndoor
                    + " mapBounds=" + b.min + ".." + b.max);
            }
            Speak(all.Count + " area parts: " + string.Join("; ", spoken));
        }

        // Debug (F10): dump every map object's identity AND its sonar pipeline view to Player.log —
        // GameObject/prefab name, blueprint, marker label, interaction parts (+Enabled), our Primary
        // taxonomy node, the resolved sound, and the visibility gates. The one-stop "why doesn't this
        // thing ping" probe. Read via the Unity Player.log.
        public static void DumpObjectNames()
        {
            if (!Active) return;
            var state = Game.Instance?.State;
            if (state == null) { Speak("No area"); return; }
            int n = 0;
            Main.Log?.Log("[objdump] === map objects in current area ===");
            foreach (var mo in state.MapObjects)
            {
                if (mo == null) continue;
                var view = mo.View;
                string go = view != null ? view.name : "(no view)";
                var fh = view != null ? view.FactHolder : null;
                string bp = (fh != null && fh.Blueprint != null) ? fh.Blueprint.name : "";
                string marker = mo.Get<Kingmaker.View.MapObjects.LocalMapMarkerPart>()?.GetDescription() ?? "";

                var parts = new System.Text.StringBuilder();
                var interactions = mo.Interactions;
                if (interactions != null)
                    for (int i = 0; i < interactions.Count; i++)
                    {
                        var part = interactions[i];
                        if (part == null) continue;
                        if (parts.Length > 0) parts.Append(",");
                        parts.Append(part.GetType().Name).Append(part.Enabled ? "" : "(disabled)");
                        if (part is Kingmaker.View.MapObjects.InteractionLootPart lp)
                            parts.Append("[").Append(lp.Settings != null ? lp.Settings.LootContainerType.ToString() : "?").Append("]");
                    }

                var proxy = new ProxyMapObject(mo);
                string primary = proxy.Primary ?? "null";
                string snd = ScanSounds.Resolve(proxy.Primary) ?? "silent";
                Main.Log?.Log("[objdump] go='" + go + "' bp='" + bp + "' marker='" + marker + "'"
                    + " parts=[" + parts + "]"
                    + " primary=" + primary + " sound=" + snd
                    + " vis=" + proxy.IsVisible + " seen=" + proxy.CurrentlySeen
                    + " inGame=" + mo.IsInGame + " revealed=" + mo.IsRevealed + " percept=" + mo.IsPerceptionCheckPassed);
                n++;
            }
            Main.Log?.Log("[objdump] === " + n + " map objects ===");
            Speak("Dumped " + n + " objects to log");
        }

        private static void Rebuild()
        {
            _everything.Clear();
            foreach (var n in ScanTaxonomy.AllNodes())
            {
                if (_byNode.TryGetValue(n.Key, out var existing)) existing.Clear();
                else _byNode[n.Key] = new List<ScanItem>();
            }

            foreach (var item in WorldModel.Items) Add(item); // Add filters to visible + buckets by node

            var refPos = ScanFrom;
            _everything.Sort((a, b) => a.DistanceTo(refPos).CompareTo(b.DistanceTo(refPos)));
            foreach (var list in _byNode.Values)
                list.Sort((a, b) => a.DistanceTo(refPos).CompareTo(b.DistanceTo(refPos)));
        }

        private static void Add(ScanItem item)
        {
            if (item == null) return;
            if (!_debugAll && !item.IsVisible) return; // debug (F11) lists hidden things too
            bool inEverything = false;
            foreach (var key in item.Nodes)
            {
                var node = ScanTaxonomy.Get(key);
                if (node == null) continue;
                // The leaf's own list, plus its parent category's "All" (so "All units" sees every faction).
                if (_byNode.TryGetValue(node.Key, out var nl) && !nl.Contains(item)) nl.Add(item);
                var cat = node.IsCategory ? node : node.Parent;
                if (cat != null && cat.Key != node.Key
                    && _byNode.TryGetValue(cat.Key, out var cl) && !cl.Contains(item)) cl.Add(item);
                // "Everything" aggregates the real things — not scenery-only props, not the curated POI
                // markers (which duplicate entities). Added once however many nodes the item has.
                if (cat != null && cat.Key != "scenery" && cat.Key != "poi") inEverything = true;
            }
            if (inEverything && !_everything.Contains(item)) _everything.Add(item);
        }

        private static ScanItem Selected
        {
            get
            {
                if (_selectionOverride != null) return _selectionOverride;
                var l = CurrentList;
                return (l != null && _itemIndex >= 0 && _itemIndex < l.Count) ? l[_itemIndex] : null;
            }
        }

        /// <summary>The review cursor's current target (the reviewed scan item), for consumers outside the
        /// scanner — e.g. the review-unit <see cref="WrathAccess.Buffers.Buffer"/>. Null when nothing's selected.</summary>
        internal static ScanItem Reviewed => Selected;

        private static void StepCategory(int dir)
        {
            _selectionOverride = null;
            Rebuild();
            if (_entered) _catIndex = NextCategoryIndex(_catIndex, dir);
            _entered = true;
            _subIndex = 0;   // land on the category's "All"
            _itemIndex = 0;
            AnnounceList(CategoryLabel);
        }

        private static void StepSubcategory(int dir)
        {
            _selectionOverride = null;
            Rebuild();
            if (OnEverything) { _entered = true; Speak(Loc.T("scan.no_subcategories")); return; }
            if (_entered) _subIndex = NextSubcategoryIndex(_subIndex, dir);
            _entered = true;
            _itemIndex = 0;
            AnnounceList(SubcategoryLabel);
        }

        // Ctrl+PageUp/Down lands only on the synthetic "Everything" (index 0 — always included, even when
        // empty) or a category that currently has items; empty categories are skipped. Index 0 always
        // qualifies, so the scan terminates (worst case: back to Everything).
        private static int NextCategoryIndex(int from, int dir)
        {
            int n = CatCount, i = from;
            for (int step = 0; step < n; step++)
            {
                i = ((i + dir) % n + n) % n;
                if (i == 0 || ListCount(ScanTaxonomy.Categories[i - 1].Key) > 0) return i;
            }
            return from;
        }

        // Shift+PageUp/Down lands only on the category's "All" (subIndex 0 — always included) or a
        // subcategory that currently has items; empty subcategories are skipped.
        private static int NextSubcategoryIndex(int from, int dir)
        {
            var subs = CurrentSubs;
            int n = subs.Count, i = from;
            for (int step = 0; step < n; step++)
            {
                i = ((i + dir) % n + n) % n;
                if (i == 0 || ListCount(subs[i].Key) > 0) return i;
            }
            return from;
        }

        private static int ListCount(string nodeKey)
            => _byNode.TryGetValue(nodeKey, out var l) ? l.Count : 0;

        private static void StepItem(int dir)
        {
            _selectionOverride = null;
            Rebuild();
            var list = CurrentList;
            if (list == null || list.Count == 0)
            {
                _entered = true;
                Speak(Loc.T("scan.category_empty", new { label = OnEverything ? CategoryLabel : SubcategoryLabel }));
                return;
            }
            _itemIndex = Mathf.Clamp(_itemIndex, 0, list.Count - 1);
            if (_entered) _itemIndex = ((_itemIndex + dir) % list.Count + list.Count) % list.Count;
            _entered = true;
            PlayReviewPing(Selected);
            AnnounceItem(list);
        }

        // Group membership rides the taxonomy's PRIMARY node, so it's state-aware for free: a living
        // enemy cycles under Period, its lootable corpse under M (containers.corpse), an emptied corpse
        // under nothing; scenery never cycles.
        private static bool InGroup(ScanItem it, ReviewGroup g)
        {
            var p = it.Primary;
            if (p == null) return false;
            switch (g)
            {
                case ReviewGroup.Party: return p == ScanTaxonomy.UnitsParty;
                case ReviewGroup.Enemies: return p == ScanTaxonomy.UnitsEnemies;
                case ReviewGroup.Neutrals: return p == ScanTaxonomy.UnitsNeutrals;
                case ReviewGroup.Poi: return p == ScanTaxonomy.Poi;
                case ReviewGroup.Unexplored: return p == ScanTaxonomy.Unexplored;
                default: return p != ScanTaxonomy.UnitsParty && p != ScanTaxonomy.UnitsEnemies
                    && p != ScanTaxonomy.UnitsNeutrals && p != ScanTaxonomy.Scenery
                    && p != ScanTaxonomy.Poi && p != ScanTaxonomy.Unexplored;
            }
        }

        private static string ReviewGroupLabel(ReviewGroup g)
        {
            switch (g)
            {
                case ReviewGroup.Party: return Loc.T("taxonomy.units.party");
                case ReviewGroup.Enemies: return Loc.T("taxonomy.units.enemies");
                case ReviewGroup.Neutrals: return Loc.T("taxonomy.units.neutrals");
                case ReviewGroup.Poi: return Loc.T("taxonomy.poi");
                case ReviewGroup.Unexplored: return Loc.T("taxonomy.unexplored");
                default: return Loc.T("review.others");
            }
        }

        // A geometric room exit as a reviewable item: look at it, ping it, plant the cursor on it
        // (Home / Slash); not interactable (there's nothing there — it's an opening).
        private sealed class RoomExitItem : ScanItem
        {
            private readonly RoomMap.Exit _exit;
            public RoomExitItem(RoomMap.Exit exit) { _exit = exit; }
            public override string Name => Loc.T("exit.to_room", new { room = RoomMap.Describe(_exit.To) });
            public override Vector3 Position => _exit.Position; // centre of the opening (cursor target)
            // Distance/bearing report the nearest part of the opening: the watershed boundary cells (the
            // complete threshold) when we have them, else the navmesh portal edges (e.g. a ramp the grid
            // missed). The cursor still targets the centre.
            public override ScanBounds Bounds
                => _exit.Boundary != null && _exit.Boundary.Length > 0
                    ? ScanBounds.Cloud(_exit.Position, _exit.Boundary)
                    : ScanBounds.Segments(_exit.Position, _exit.Edges);
            public override IEnumerable<string> Nodes { get { yield return "exits"; } }
        }

        // The V-cycle's landing when it's a geometric opening: openings aren't WorldModel items, so
        // they can't become the list selection — Selected resolves to this instead until any other
        // selection-moving action clears it (I "can't interact", Home/Slash plant the cursor on it).
        private static ScanItem _selectionOverride;

        private static void DoCycleRoomExits(int dir)
        {
            var prev = Selected;
            Rebuild();
            var refPos = ScanFrom;
            var room = RoomMap.RoomAt(refPos);
            var emptyMsg = Loc.T("scan.category_empty", new { label = Loc.T("taxonomy.exits") });
            if (room == null) { Speak(emptyMsg); return; }

            // Door/transition items in (or adjacent to) this room — probe around the thing, since a
            // closed door's own cells are cut out of the navmesh and resolve to either side or nothing.
            var things = new List<ScanItem>();
            foreach (var it in WorldModel.Items)
            {
                if (!_debugAll && !it.IsVisible) continue;
                bool isExit = false;
                foreach (var key in it.Nodes)
                    if (key == "exits" || key == "doors" || key == "doors.open") { isExit = true; break; }
                if (!isExit) continue;
                var p = it.Position;
                bool inRoom = false;
                for (int k = 0; k < 5 && !inRoom; k++)
                {
                    var probe = p;
                    if (k == 1) probe.x += 1.5f; else if (k == 2) probe.x -= 1.5f;
                    else if (k == 3) probe.z += 1.5f; else if (k == 4) probe.z -= 1.5f;
                    inRoom = RoomMap.RoomAt(probe) == room;
                }
                if (inRoom) things.Add(it);
            }

            var candidates = new List<ScanItem>(things);
            foreach (var exit in room.Exits)
            {
                bool covered = false;
                foreach (var t in things)
                {
                    float dx = t.Position.x - exit.Position.x, dz = t.Position.z - exit.Position.z;
                    if (dx * dx + dz * dz < 2.5f * 2.5f) { covered = true; break; }
                }
                if (!covered) candidates.Add(new RoomExitItem(exit));
            }
            if (candidates.Count == 0) { Speak(emptyMsg); return; }
            candidates.Sort((a, b) => a.DistanceTo(refPos).CompareTo(b.DistanceTo(refPos)));

            // Continue from the current target when it's one of these exits; openings match by
            // position, since RoomExitItems are recreated on every press.
            int idx = -1;
            if (prev != null)
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (candidates[i] == prev
                        || (prev is RoomExitItem && candidates[i] is RoomExitItem
                            && (candidates[i].Position - prev.Position).sqrMagnitude < 0.05f)) { idx = i; break; }
                }
            idx = idx < 0
                ? (dir >= 0 ? 0 : candidates.Count - 1)
                : ((idx + dir) % candidates.Count + candidates.Count) % candidates.Count;
            var target = candidates[idx];

            if (target is RoomExitItem) _selectionOverride = target;
            else { _selectionOverride = null; SyncSelectionTo(target); }
            PlayReviewPing(target);
            // Bare openings announce their destination themselves ("Exit to Room N…"); a door or
            // transition item speaks the THING, so append where it leads (and its unexplored state).
            string leadsTo = "";
            if (!(target is RoomExitItem))
            {
                var dest = ExitDestination(target.Position, room);
                if (dest != null) leadsTo = ", " + Loc.T("exit.leads_to", new { room = RoomMap.Describe(dest) });
            }
            Speak(target.Describe(refPos) + leadsTo + ", "
                + Loc.T("nav.position", new { index = idx + 1, count = candidates.Count }));
        }

        /// <summary>The room on the far side of a door/transition item in the V cycle: the covered
        /// geometric opening's destination when one is nearby, else probe around the thing — a CLOSED
        /// door cuts the navmesh, so no portal (and no Exit record) exists there. Null when nothing
        /// resolves (e.g. an area transition — the mesh just ends).</summary>
        private static RoomMap.Room ExitDestination(Vector3 p, RoomMap.Room from)
        {
            foreach (var exit in from.Exits)
            {
                float dx = exit.Position.x - p.x, dz = exit.Position.z - p.z;
                if (dx * dx + dz * dz < 2.5f * 2.5f) return exit.To;
            }
            for (int radius = 0; radius < 2; radius++)
                for (int k = 0; k < 4; k++)
                {
                    var probe = p;
                    float d = radius == 0 ? 1.5f : 2.5f;
                    if (k == 0) probe.x += d; else if (k == 1) probe.x -= d;
                    else if (k == 2) probe.z += d; else probe.z -= d;
                    var r = RoomMap.RoomAt(probe);
                    if (r != null && r != from) return r;
                }
            return null;
        }

        private static void DoCycleReview(ReviewGroup group, int dir)
        {
            var prev = Selected; // the cached selection — ScanItem identities are stable in WorldModel
            _selectionOverride = null;
            Rebuild();
            var refPos = ScanFrom;
            // Same rule as the sonar (ScanItem.DetectableFrom): cycle anything a party member can see right
            // now, plus remembered things under fog that have a clear line of sight from the cursor — so a
            // chest behind a wall isn't offered until you'd actually have a straight path to it. (Room exits,
            // V, are a separate cycle and stay always-reachable — that geometry is known.)
            var candidates = new List<ScanItem>();
            foreach (var it in WorldModel.Items)
                if ((_debugAll || it.DetectableFrom(refPos)) && InGroup(it, group)) candidates.Add(it);
            if (candidates.Count == 0)
            {
                Speak(Loc.T("scan.category_empty", new { label = ReviewGroupLabel(group) }));
                return;
            }
            candidates.Sort((a, b) => a.DistanceTo(refPos).CompareTo(b.DistanceTo(refPos)));

            // Continue from the current target when it's in this group; otherwise enter at the nearest
            // (or, cycling backward into a fresh group, the farthest). Distances are live, so the order
            // self-heals as things move.
            int idx = prev != null ? candidates.IndexOf(prev) : -1;
            idx = idx < 0
                ? (dir >= 0 ? 0 : candidates.Count - 1)
                : ((idx + dir) % candidates.Count + candidates.Count) % candidates.Count;
            var target = candidates[idx];

            SyncSelectionTo(target);
            PlayReviewPing(target);
            Speak(target.Describe(refPos) + ", "
                + Loc.T("nav.position", new { index = idx + 1, count = candidates.Count }));
        }

        // The review-cursor landing ping: the active overlay's sonar settings when one is on (so a
        // customized overlay's choice/volume applies), else the Default overlay's — positioned at the
        // reviewed thing relative to the movement cursor.
        private static void PlayReviewPing(ScanItem item)
        {
            if (item == null) return;
            var overlay = Overlays.OverlayManager.ActiveOverlay ?? Overlays.OverlaySettingsRegistry.DefaultOverlay;
            overlay?.Get<Overlays.SonarSystem>()?.PlayReview(item, ScanFrom);
        }

        // Point the scanner's browse position at this item so PageUp/Down continues from the review target
        // in context — landing on the most specific subcategory that contains it, else the category's All.
        private static void SyncSelectionTo(ScanItem item)
        {
            var cats = ScanTaxonomy.Categories;
            for (int c = 0; c < cats.Count; c++)
            {
                var subs = ScanTaxonomy.NavSubcategories(cats[c]);
                for (int s = 1; s < subs.Count; s++) // specific subcategories first
                    if (_byNode.TryGetValue(subs[s].Key, out var sl))
                    {
                        int i = sl.IndexOf(item);
                        if (i >= 0) { _catIndex = c + 1; _subIndex = s; _itemIndex = i; _entered = true; return; }
                    }
                if (_byNode.TryGetValue(cats[c].Key, out var al)) // then the category's All
                {
                    int i = al.IndexOf(item);
                    if (i >= 0) { _catIndex = c + 1; _subIndex = 0; _itemIndex = i; _entered = true; return; }
                }
            }
            int ie = _everything.IndexOf(item); // fallback: the Everything list
            if (ie >= 0) { _catIndex = 0; _subIndex = 0; _itemIndex = ie; _entered = true; }
        }

        // Announce the current (sub)category: its label + count, and the first item if any.
        private static void AnnounceList(string label)
        {
            var list = CurrentList;
            int count = list != null ? list.Count : 0;
            var msg = count == 1
                ? Loc.T("scan.category_one", new { label })
                : Loc.T("scan.category_many", new { label, count });
            if (count > 0) { _itemIndex = Mathf.Clamp(_itemIndex, 0, count - 1); msg += ". " + ItemLine(list); PlayReviewPing(Selected); }
            Speak(msg);
        }

        private static void AnnounceItem(List<ScanItem> list) => Speak(ItemLine(list));

        private static string ItemLine(List<ScanItem> list)
        {
            var item = list[_itemIndex];
            var tag = (_debugAll && !item.IsVisible) ? " (hidden)" : ""; // flag fogged items in debug mode
            return item.Describe(ScanFrom) + tag + ", " + Loc.T("nav.position", new { index = _itemIndex + 1, count = list.Count });
        }

        // Cursor/interact act on the item you last navigated to (the cached selection) — no rebuild,
        // so they don't re-sort the list out from under you between hearing an item and acting on it.
        private static void CommitCursor()
        {
            var sel = Selected;
            if (sel == null) { Speak(Loc.T("scan.no_item")); return; }
            Cursor.Set(sel.Position); // the shared cursor — overlays move this same point
            Speak(Loc.T("scan.cursor_on", new { name = string.IsNullOrEmpty(sel.Name) ? Loc.T("scan.item_fallback") : sel.Name, pos = Geo.Raw(sel.Position) }));
        }

        private static void DoInteract(ScanItem target, string noneMsg)
        {
            if (target == null) { Speak(noneMsg); return; }
            var name = string.IsNullOrEmpty(target.Name) ? Loc.T("scan.that_fallback") : target.Name;
            var mc = Game.Instance?.Player?.MainCharacter.Value;
            if (mc != null)
            {
                // Interact approaches the thing adjacently (it doesn't path onto the thing's exact point),
                // so we don't require its centre to be on-mesh. A closed door sits in its own navmesh cut,
                // so its centre can snap to the far side — re-test a point nudged toward the actor, which
                // lands on our side, so we don't wrongly block walking up to open it.
                var from = Geo.Live(mc);
                bool reachable = SameArea(from, target.Position)
                                 || SameArea(from, Vector3.MoveTowards(target.Position, from, 2f));
                if (!reachable) { Speak(Loc.T("scan.cant_reach", new { name })); return; }
            }
            EnsureSelection();
            switch (target.Interact())
            {
                case InteractOutcome.Started:
                    Speak(Loc.T("scan.interacting", new { name = string.IsNullOrEmpty(target.Name) ? Loc.T("scan.item_fallback") : target.Name }));
                    break;
                case InteractOutcome.RefusedSpoken:
                    break; // the item already spoke its refusal — don't talk over it
                default:
                    Speak(Loc.T("scan.cant_interact", new { name }));
                    break;
            }
        }

        // Walk to the shared cursor — the point planted by Home or moved by the tile-view overlay. Routes
        // through the game's own MoveSelectedUnitsToPoint, so the selection decides the behaviour exactly
        // like a mouse click: one member selected → only they go; the whole party → everyone into the set
        // formation at the target ([[wotr-access-party-selection]]). Selection is set by the Ctrl+A /
        // Ctrl+1..6 actions and read here synchronously. A move issued while paused queues and walks on
        // unpause (Space). Reachability is checked from the lead selected unit.
        private static void DoMoveToCursor()
        {
            if (!Cursor.Has) { Speak(Loc.T("scan.no_cursor")); return; }
            var dest = Cursor.Position.Value;
            if (!OnNavmesh(dest)) { Speak(Loc.T("scan.not_walkable")); return; }

            EnsureSelection(); // default to the whole party if nothing's selected yet
            var refUnit = Game.Instance?.SelectionCharacter?.FirstSelectedUnit
                          ?? Game.Instance?.Player?.MainCharacter.Value;
            if (refUnit == null) { Speak(Loc.T("scan.no_character")); return; }
            if (!SameArea(Geo.Live(refUnit), dest)) { Speak(Loc.T("scan.cursor_unreachable")); return; }

            // Turn-based: don't move to the raw cursor point — drive the game's own pathfinding to our cursor
            // and move to that path's endpoint. The prediction system that normally produces this path is
            // mouse-driven and won't fire for us, so issuing a raw UnitMoveTo just instant-completes.
            if (CombatMode.InTurnBased)
            {
                var ep = CombatMode.PathEndpointToward(dest);
                if (!ep.HasValue) { CombatMode.CancelPathReservation(); Speak(Loc.T("scan.no_path_cursor")); return; }
                dest = ep.Value;
            }

            ClickGroundHandler.MoveSelectedUnitsToPoint(dest);
            // Engage the game's camera follower on the lead unit so the camera tracks it as it walks (and,
            // because IsOn is persistent and TryFollow re-targets to the selected unit every frame, through
            // later moves/selections too). The move flow itself never touches the camera — in the base game
            // follow-mode is engaged separately (double-click portrait / FollowUnit key), which our keyboard
            // selection doesn't do. Follow() internally honours the CameraFollowsUnit setting, so this is a
            // no-op when the player has follow turned off.
            Game.Instance?.CameraController?.Follower?.Follow(refUnit);
            Speak(Loc.T("scan.moving"));
        }

        private const uint NoArea = 999999u; // ObstacleAnalyzer.GetArea's sentinel when no node is near

        // Two world points are mutually walkable iff their nearest navmesh nodes share an Area (connected
        // component) — the game's own cross-Area move block ([[wotr-exploration-world-model]]): a closed
        // door that's the only route splits the Areas, so this is "no path" until it's opened. Because
        // reachable ⇒ same Area, a DIFFERENT id means genuinely unreachable, so we never block a target you
        // could actually reach. Unknown (off-mesh → NoArea) is treated as same so a snap we can't classify
        // doesn't block; callers that must not path onto off-mesh points gate on OnNavmesh first.
        private static bool SameArea(Vector3 a, Vector3 b)
        {
            uint ar = ObstacleAnalyzer.GetArea(a), br = ObstacleAnalyzer.GetArea(b);
            return ar == NoArea || br == NoArea || ar == br;
        }

        // Is this point actually on walkable ground? We refuse to path to off-navmesh points: the game
        // would silently clamp to the nearest walkable spot, and a blind player can't see the marker
        // showing where it landed. (Same 0.35 m tolerance the tile view uses to call a tile walkable.)
        private static bool OnNavmesh(Vector3 p) => NavmeshProbe.Sample(p.x, p.z, p.y).OnNavmesh;

        // The move/interaction logic acts on the player's current selection; keyboard nav may have none
        // (the player hasn't pressed a selection key yet), so default to the whole party — the game's own
        // default. SelectAll goes through MultiSelect (silent, no selection voice) and writes SelectedUnits
        // synchronously, so the move that follows sees it immediately.
        private static void EnsureSelection()
        {
            var sc = Game.Instance?.SelectionCharacter;
            if (sc == null) return;
            if (sc.SelectedUnits != null && sc.SelectedUnits.Count > 0) return;
            if (CombatMode.InTurnBased) return; // TB: the game keeps the acting unit selected; never SelectAll
            Game.Instance.UI?.SelectionManager?.SelectAll();
        }

        private static void SpeakCursor()
        {
            if (!Cursor.Has) { Speak(Loc.T("scan.no_cursor")); return; }
            Speak(Loc.T("scan.cursor_at", new { rel = Geo.Relative(Reference, Cursor.Position.Value) }));
        }

        // "Where am I": the game's own display name for the current location (AreaDisplayName prefers
        // the current AREA PART's local name — the section, e.g. an upper floor — then settlement/
        // renamed/area name), indoors when the part says so, and the MOVEMENT CURSOR's position within
        // the section's map bounds as a 3x3 compass region (ScanFrom: the cursor when one is placed,
        // the leader otherwise — same reference the scanner reads from). World-aligned compass,
        // matching every other bearing we speak (the visual map may be rotated via LocalMapRotation;
        // we stay consistent with our own directions instead).
        private static void SpeakWhereAmI()
        {
            var pos = ScanFrom;

            var parts = new List<string>();
            var area = Game.Instance?.CurrentlyLoadedArea;
            string name = area != null ? TextUtil.StripRichText(area.AreaDisplayName) : null;
            if (!string.IsNullOrWhiteSpace(name)) parts.Add(name);

            var part = Kingmaker.Blueprints.Area.AreaService.Instance?.CurrentAreaPart;
            if (part != null && part.IsIndoor) parts.Add(Loc.T("where.indoors"));
            var room = RoomMap.RoomAt(pos);
            if (room != null) parts.Add(RoomMap.Describe(room));
            // The spot-level flag ("you're standing in the dark part") — but Describe already ends
            // with "unexplored" for a fully-unexplored room, so don't say it twice.
            if (!FogExplored.IsExplored(pos) && (room == null || RoomMap.UnexploredPercent(room) < 100))
                parts.Add(Loc.T("where.unexplored"));
            var bounds = part?.Bounds;
            if (bounds != null)
            {
                var b = bounds.LocalMapBounds;
                if (b.size.x > 1f && b.size.z > 1f)
                {
                    float fx = Mathf.Clamp01((pos.x - b.min.x) / b.size.x);
                    float fz = Mathf.Clamp01((pos.z - b.min.z) / b.size.z);
                    parts.Add(Loc.T("where.region", new { region = RegionWord(fx, fz) }));
                }
            }
            Speak(parts.Count > 0 ? string.Join(", ", parts) : Loc.T("where.unknown"));
        }

        // 3x3 grid over the map bounds -> center or a compass word (+Z = north, like Geo's compass).
        private static string RegionWord(float fx, float fz)
        {
            int col = fx < 1f / 3f ? -1 : fx > 2f / 3f ? 1 : 0;
            int row = fz < 1f / 3f ? -1 : fz > 2f / 3f ? 1 : 0;
            if (col == 0 && row == 0) return Loc.T("where.center");
            string key = row > 0 ? (col < 0 ? "geo.northwest" : col > 0 ? "geo.northeast" : "geo.north")
                : row < 0 ? (col < 0 ? "geo.southwest" : col > 0 ? "geo.southeast" : "geo.south")
                : col < 0 ? "geo.west" : "geo.east";
            return Loc.T(key);
        }

        private static void SpeakParty()
        {
            var player = Game.Instance?.Player;
            var members = player != null ? player.PartyAndPets : null;
            if (members == null || members.Count == 0) { Speak(Loc.T("scan.no_party")); return; }
            var refPos = Reference;
            var parts = new List<string>();
            foreach (var m in members)
                if (m != null) parts.Add(m.CharacterName + ", " + Geo.Relative(refPos, Geo.Live(m)));
            Speak(Loc.T("scan.party", new { list = string.Join("; ", parts.ToArray()) }));
        }

        private static void Speak(string msg) { if (!string.IsNullOrEmpty(msg)) Tts.Speak(msg, interrupt: true); }
    }
}
