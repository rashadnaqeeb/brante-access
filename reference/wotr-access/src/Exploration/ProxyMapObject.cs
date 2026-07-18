using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Kingmaker;                              // Game
using Kingmaker.Blueprints.Root;              // BlueprintRoot
using Kingmaker.Controllers.Clicks.Handlers;  // ClickMapObjectHandler, ClickGroundHandler
using Kingmaker.EntitySystem.Entities; // MapObjectEntityData
using Kingmaker.GameModes;             // GameModeType
using Kingmaker.PubSubSystem;          // EventBus, ILockpickUIHandler
using Kingmaker.UI.MapObjectOvertip;   // AreaTransitionController
using Kingmaker.UI.MVVM._VM.Lockpick;  // LockpickVM (lock tool-choice window)
using Kingmaker.UI.Selection;          // SelectionManagerPC
using Kingmaker.UnitLogic.Commands;    // AreaTransitionGroupCommand
using Kingmaker.View;                  // EntityViewBase, ObstacleAnalyzer
using Kingmaker.View.MapObjects;       // InteractionPart family, LocalMapMarkerPart, AreaTransitionPart
using UnityEngine;                     // Bounds, Collider, Mathf

namespace WrathAccess.Exploration
{
    /// <summary>
    /// An interactable map object. Its categories come from the interaction parts it carries (many-to-
    /// many — a thing that's both lootable and searchable is in Containers and Search Points); lock/trap
    /// parts are state, not categories. Map objects rarely have a real name, so we label by the local-
    /// map marker description if it has one, else by interaction type ("Door", "Container", …). An object
    /// with no relevant interactions reports no categories and is excluded by the scanner.
    /// </summary>
    internal sealed class ProxyMapObject : ProxyEntity
    {
        private readonly MapObjectEntityData _obj;

        public ProxyMapObject(MapObjectEntityData obj) : base(obj) { _obj = obj; }

        // Mirror the local map's own marker filter (LocalMapMarkerPart.IsVisible): a static object stays
        // listed once it's been revealed, even if it's currently back in fog — so we keep showing things
        // the player has seen, like the map does. Perception gates the hidden ones: IsPerceptionCheckPassed
        // is true by default but false for secret/trapped objects until their perception check passes, so
        // undiscovered ones don't leak. (This is why we don't use the base current-visibility filter.)
        public override bool IsVisible => _obj.IsInGame && _obj.IsRevealed && _obj.IsPerceptionCheckPassed;

        // The source prefab/GameObject name, normalized to a stable dedupe key for mod-authored descriptions.
        public override string AssetKey => EnvDescriptions.NormalizeAssetKey(_obj.View?.name);

        // Footprint = half the object's larger horizontal extent (an enclosing radius for our planar
        // cursor). Sourced from the COLLIDERS, not renderers: MapObjectView sets AutoUpdateRenderers=false,
        // so the view never caches its renderers and GetMaxBounds() returns a zero-size box — but the
        // colliders ARE cached (the clickable physical extent). Looked up live (objects can resize).
        public override float Footprint
        {
            get
            {
                var view = _obj.View;
                if (view == null) return 0f;
                var b = new Bounds(view.transform.position, Vector3.zero);
                bool any = false;
                foreach (var col in view.Colliders)
                {
                    if (col == null) continue;
                    b.Encapsulate(col.bounds);
                    any = true;
                }
                if (!any) b = view.GetMaxBounds(); // fallback (e.g. an object with only hide-renderers)
                return Mathf.Max(b.extents.x, b.extents.z);
            }
        }

        public override IEnumerable<string> Nodes
        {
            get
            {
                var nodes = new HashSet<string>();
                var interactions = _obj.Interactions; // InteractionPart parts only
                for (int i = 0; i < interactions.Count; i++)
                {
                    var part = interactions[i];
                    // A one-way door (DisableOnOpen) disables its part once opened, but the doorway is
                    // still a navigation landmark — keep it a DOOR (open → the "open doors" subcategory).
                    // A disabled CLOSED door stays out (HiddenPart gating / scripts) so secret doors don't leak.
                    if (part is InteractionDoorPart dr)
                    {
                        if (part.Enabled || dr.IsOpen) nodes.Add(dr.IsOpen ? "doors.open" : "doors");
                        continue;
                    }
                    // A HiddenPart gates the object's OTHER interactions: while unrevealed it disables them
                    // (Enabled = false) and they only turn on once a skill check is passed. Skip disabled
                    // parts so a hidden chest reads as a search point, not a container, until it's opened.
                    if (!part.Enabled) continue;
                    switch (part)
                    {
                        // Unrevealed hidden object → a search point (a skill check to reveal). Once Opened
                        // the real parts are enabled and categorize themselves, so we add nothing here.
                        case HiddenPart h: if (!h.Opened) nodes.Add("searchpoints"); break;
                        case InteractionLootPart l: nodes.Add(LootNode(l)); break; // "containers.<subtype>"
                        case InteractionSkillCheckPart _: nodes.Add("searchpoints"); break;
                        // A discovered trap — only while ARMED (TrapActive flips off on disarm/trigger;
                        // the game's highlight and interaction gate on it the same way).
                        case DisableTrapInteractionPart t: if (t.Owner != null && t.Owner.TrapActive) nodes.Add("traps"); break;
                        default: nodes.Add("mechanisms"); break; // dialog, combine, button, device, bark
                    }
                }
                // Area transitions and restrictions are separate entity parts, not InteractionParts.
                if (_obj.Get<AreaTransitionPart>() != null) nodes.Add("exits");
                // No interactable role → plain scenery (a prop). Surfaced so it can be browsed; visibility
                // still gates whether it's listed.
                if (nodes.Count == 0) nodes.Add("scenery");
                return nodes;
            }
        }

        // The primary taxonomy node, by interactable role and STATE. Priority: exits > loot > doors >
        // traps > search points > mechanisms — with one state flip: an exit DOOR still closed is
        // primarily a door (interacting opens it); once open it's primarily the exit. An unfound
        // HiddenPart reads as a search point (there's something here, but you haven't searched it out);
        // plain scenery is the silent-by-default scenery node. Skips disabled parts, EXCEPT an opened
        // one-way door — that stays a door landmark, on the doors.open node (its own sound).
        public override string Primary
        {
            get
            {
                InteractionLootPart loot = null;
                bool door = false, doorOpen = false, trap = false, hidden = false, skill = false, mechanism = false;
                var interactions = _obj.Interactions;
                for (int i = 0; i < interactions.Count; i++)
                {
                    var part = interactions[i];
                    if (part is InteractionDoorPart d)
                    {
                        // disabled-but-open = an opened one-way door, still a door landmark (see Categories)
                        if (part.Enabled || d.IsOpen) { door = true; doorOpen = d.IsOpen; }
                        continue;
                    }
                    if (!part.Enabled) continue;
                    switch (part)
                    {
                        case InteractionLootPart l: loot = l; break;
                        case DisableTrapInteractionPart t: trap = t.Owner != null && t.Owner.TrapActive; break;
                        case HiddenPart h: if (!h.Opened) hidden = true; break;
                        case InteractionSkillCheckPart _: skill = true; break;
                        default: mechanism = true; break;
                    }
                }

                bool exit = _obj.Get<AreaTransitionPart>() != null;
                if (exit && !(door && !doorOpen)) return ScanTaxonomy.Exits;
                if (loot != null) return LootNode(loot);
                if (door) return doorOpen ? ScanTaxonomy.DoorsOpen : ScanTaxonomy.Doors;
                if (trap) return ScanTaxonomy.Traps;
                if (hidden || skill) return ScanTaxonomy.SearchPoints;
                if (mechanism) return ScanTaxonomy.Mechanisms;
                return ScanTaxonomy.Scenery;
            }
        }

        private static string LootNode(InteractionLootPart loot)
        {
            switch (loot.Settings.LootContainerType) // Settings is public on InteractionPart<T>
            {
                case LootContainerType.Chest: return ScanTaxonomy.ContainersChest;
                case LootContainerType.Unit: return ScanTaxonomy.ContainersCorpse;
                case LootContainerType.Environment: return ScanTaxonomy.ContainersEnvironment;
                case LootContainerType.PlayerChest: return ScanTaxonomy.ContainersStash;
                case LootContainerType.OneSlot: return ScanTaxonomy.ContainersSingle;
                // DefaultLoot + anything new → the catch-all container NODE. (This used to return the
                // raw sound stem "loot-generic", which is not a taxonomy key — Resolve() found no such
                // node and every DefaultLoot container went SILENT in the sonar/cues.)
                default: return ScanTaxonomy.ContainersOther;
            }
        }

        // The object's real name (curated marker label, else the designer's prefab name), or null when it
        // has neither — the announcement then falls back to the TYPE word (NameAndType), and the Name
        // property below keeps its old desc→prefab→category→"object" behaviour for other consumers.
        private string RealName()
        {
            var desc = _obj.Get<LocalMapMarkerPart>()?.GetDescription();
            if (!string.IsNullOrEmpty(desc)) return desc; // curated/localized marker label wins
            var prefab = CleanName(_obj.View?.name);
            return string.IsNullOrEmpty(prefab) ? null : prefab; // the designer's prefab name ("Bag", "Jug")
        }

        // The object's type word: the singular of its first node's category ("Door", "Container", …).
        private string TypeWord()
        {
            foreach (var key in Nodes)
            {
                var node = ScanTaxonomy.Get(key);
                var catKey = node == null ? null : node.IsCategory ? node.Key : node.Parent?.Key;
                switch (catKey)
                {
                    case "doors": return Loc.T("scan.singular.door");
                    case "containers": return Loc.T("scan.singular.container");
                    case "exits": return Loc.T("scan.singular.exit");
                    case "searchpoints": return Loc.T("scan.singular.search_point");
                    case "traps": return Loc.T("scan.singular.trap");
                    default: return Loc.T("scan.singular.object"); // mechanisms / scenery / unknown
                }
            }
            return Loc.T("scan.singular.object");
        }

        public override string Name => RealName() ?? TypeWord();

        // Turn the designer's GameObject/prefab name into a readable object name: "Loot_Bag_Big 2" → "Bag
        // Big", "Horgus_Dialogue" → "Horgus", "Go_To_Sull" → "Go To Sull". Owlcat names map-object prefabs
        // after the real thing, so this beats the bare category ("Container"). Strips Unity clone/instance
        // suffixes, a few known noise prefixes/suffixes, and splits underscores/camelCase. NOTE: this is
        // "what it is", not "what it looks like" — puzzles needing visual detail want real model
        // descriptions later (deferred, e.g. render + vision). Tuned to observed names; extend as needed.
        private static readonly string[] NoisePrefixes = { "Loot_" };
        private static readonly string[] NoiseSuffixes = { "_Dialogue" };

        private static string CleanName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var s = raw.Replace("(Clone)", "").Trim();
            s = Regex.Replace(s, @"[ _]\d+$", "").Trim(); // Unity's duplicate suffix (" 2", "_3")
            foreach (var p in NoisePrefixes)
                if (s.StartsWith(p, System.StringComparison.OrdinalIgnoreCase)) { s = s.Substring(p.Length); break; }
            foreach (var sfx in NoiseSuffixes)
                if (s.EndsWith(sfx, System.StringComparison.OrdinalIgnoreCase)) { s = s.Substring(0, s.Length - sfx.Length); break; }
            s = s.Replace('_', ' ');
            s = Regex.Replace(s, @"(?<=[a-z0-9])(?=[A-Z])", " "); // split camelCase
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s.Length > 0 ? s : null;
        }

        // Announce-node = Primary (the leaf: doors / containers.corpse / …); object part-set.
        protected override IEnumerable<Announce.ScanAnnouncement> StateParts()
        {
            foreach (var p in NameAndType(RealName(), TypeWord())) yield return p;
            var states = StateWords();
            if (states.Count > 0) yield return new Announce.ObjectStatePart(states);
        }

        // open / restricted / trapped — the same flags the old Extra joined.
        private List<string> StateWords()
        {
            var bits = new List<string>();
            var doorPart = _obj.Get<InteractionDoorPart>();
            if (doorPart != null && doorPart.IsOpen) bits.Add(Loc.T("object.open"));
            if (_obj.Get<InteractionRestrictionPart>() != null) bits.Add(Loc.T("object.restricted"));
            var trapPart = _obj.Get<DisableTrapInteractionPart>();
            if (trapPart?.Owner != null && trapPart.Owner.TrapActive) bits.Add(Loc.T("object.trapped"));
            return bits;
        }

        // Same as clicking the object: paths to it and runs its interaction. We pass the current
        // selection (the unit source the click uses); the interaction's SelectUnit picks the actor.
        // forceOvertipInteractions: true — overtip interactions are hover-triggered in mouse mode and
        // we have no hover.
        public override InteractOutcome Interact()
        {
            var view = _obj.View;
            if (view == null) return InteractOutcome.NotSupported;

            // Area transitions (area exits) aren't InteractionParts, so ClickMapObjectHandler can't act on
            // them — that's why they read "can't interact". Trigger them the way the overtip's click does.
            var transition = _obj.Get<AreaTransitionPart>();
            if (transition != null)
                return TriggerTransition(view, transition) ? InteractOutcome.Started : InteractOutcome.NotSupported;

            var units = Game.Instance?.SelectionCharacter?.SelectedUnits;
            if (units == null || units.Count == 0) return InteractOutcome.NotSupported;

            // Locked objects: route through the game's lock check so its tool-choice window (skill / +5 /
            // +10 / destroy) opens — the branch ClickMapObjectHandler.OnClick runs ABOVE Interact, which our
            // direct call (kept for the forced-overtip behaviour) skipped. LockpickScreen makes it accessible.
            if (LockpickVM.NeedLockpick(view))
            {
                EventBus.RaiseEvent<ILockpickUIHandler>(h => h.HandleLockpickRequest(view, false));
                return InteractOutcome.Started;
            }
            if (CombatMode.InTurnBased) CombatMode.CancelPathReservation(); // clean slate before issuing
            bool started = ClickMapObjectHandler.Interact(view.gameObject, units, forceOvertipInteractions: true);
            if (started) CombatMode.NoteIssuedCommand(CombatMode.CurrentUnit);
            return started ? InteractOutcome.Started : InteractOutcome.NotSupported;
        }

        // Mirrors AreaTransitionController.StartAreaTransition: move the party to the exit in formation,
        // then run each unit's UnitAreaTransition (the group command loads the next area once they arrive).
        private static bool TriggerTransition(EntityViewBase view, AreaTransitionPart transition)
        {
            var game = Game.Instance;
            if (game == null || game.Player.IsInCombat || game.CurrentMode == GameModeType.Dialog) return false;
            if (AreaTransitionController.CanNotMove(transition)) return false; // shows its own warning bark

            var position = view.transform.position;
            var party = game.Player.GetPartyCharactersForGroupCommand(position);
            if (party.Count == 0) return false;

            var groupCommand = new AreaTransitionGroupCommand(party, transition);
            (game.UI.SelectionManager as SelectionManagerPC)?.MultiSelect(party.Select(u => u.View), canAddToSelection: false);
            ClickGroundHandler.MoveSelectedUnitsToPoint(
                ObstacleAnalyzer.GetDeepNavmeshPoint(position, 1.5f),
                ClickGroundHandler.GetDefaultDirection(position),
                preview: false, showTargetMarker: true,
                BlueprintRoot.Instance.Formations.MinSpaceFactor, ignoreHold: true,
                (unit, s) => AreaTransitionController.RunUnitTransitionCommand(groupCommand, unit, s.Destination, s.SpeedLimit));
            return true;
        }
    }
}
