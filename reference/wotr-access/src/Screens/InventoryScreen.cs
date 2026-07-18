using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints.Root; // LocalizedTexts (filter labels)
using Kingmaker.UI.Common; // ItemsFilter (filter/sorter enums)
using Kingmaker.UI.MVVM._VM.ServiceWindows;
using Kingmaker.UI.MVVM._VM.ServiceWindows.Inventory; // InventoryVM, InventoryDollVM, EquipSlotVM
using Kingmaker.UI.MVVM._VM.Slots; // ItemSlotVM, ItemsFilterVM
using WrathAccess.UI;
using WrathAccess.UI.CharSheet;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The inventory service window (<see cref="InventoryVM"/>), graph-native. Mirrors the game window's
    /// content: the character switcher, the same character-summary blocks the char sheet shows (via the
    /// shared <see cref="CharSheetBlocks"/> on a <see cref="GraphCharSheetSink"/>), the equipment doll
    /// (grip + weapon-set rows over the "Slot: item" list), the party-wide load/gold lines, and the
    /// shared stash — search, sort, filter row, then the Name/Type/Qty/Weight/Value table whose item
    /// rows carry the tooltip and inventory actions (Enter = the double-click quick action, Backspace =
    /// the full context menu). Everything renders live; stash rows key by SLOT IDENTITY, so equipping /
    /// using / dropping lands focus on a genuinely different row and the differ reads it — the old
    /// signature/capture/restore machinery is deleted. Doll/stats keys carry the viewed unit, so
    /// switching characters re-keys them (switcher focus survives). Escape closes.
    /// </summary>
    public sealed class InventoryScreen : Screen
    {
        public override string Key => "service.Inventory";
        public override string ScreenName => Loc.T("screen.inventory");
        public override int Layer => 10;
        public override bool IsActive()
        {
            var rc = Game.Instance?.RootUiContext;
            if (rc == null) return false;
            var cur = rc.CurrentServiceWindow; // Inventory/Equipment/SmartItem all open the InventoryVM window
            return cur == ServiceWindowsType.Inventory || cur == ServiceWindowsType.Equipment
                   || cur == ServiceWindowsType.SmartItem;
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => ServiceWindows()?.HandleCloseAll());
        }

        private static InventoryVM Vm()
            => Game.Instance?.RootUiContext?.InGameVM?.StaticPartVM?.ServiceWindowsVM?.InventoryVM?.Value;

        private static ServiceWindowsVM ServiceWindows()
            => Game.Instance?.RootUiContext?.InGameVM?.StaticPartVM?.ServiceWindowsVM;


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            var unit = Game.Instance?.SelectionCharacter?.SelectedUnit?.Value.Value;
            string k = "inv:" + vm.GetHashCode() + ":";
            // Doll/stats keys carry the viewed unit: a character switch re-keys them (and re-homes into
            // the new character's content only if focus was inside it — switcher focus is untouched).
            string uk = k + (unit != null ? unit.CharacterName : "?") + ":";

            // Character switcher — drives the game's real selection; the stash below is party-shared.
            var party = Game.Instance?.Player?.Party;
            if (party != null && party.Count > 0)
            {
                b.BeginStop("chars").PushContext(Loc.T("label.characters"), "list");
                int ci = 0;
                foreach (var u in party)
                {
                    var un = u;
                    b.AddItem(ControlId.Referenced(un, k + "char:" + ci),
                        GraphNodes.Button(() => un.CharacterName,
                            () => Game.Instance.SelectionCharacter.SetSelected(un)));
                    ci++;
                }
                b.PopContext();
            }

            // Character summary — the same blocks as the char-sheet Summary page (shared renderer).
            b.BeginStop("stats");
            var sink = new GraphCharSheetSink(b, uk + "stats:");
            CharSheetBlocks.NamePortrait(vm.NameAndPortraitVM, sink);
            CharSheetBlocks.LevelClassScores(vm.LevelClassScoresVM, sink);
            CharSheetBlocks.Attacks(vm.AttacksBlockVM, sink);
            CharSheetBlocks.Defence(vm.DefenceBlockVM, sink);
            sink.Finish();

            BuildEquipment(b, vm.DollVM, uk);
            BuildLoad(b, vm.StashVM, k);
            BuildStash(b, vm, k);
        }

        // The equipment doll: the grip button (only when the active set can re-grip), the weapon-set
        // row (radios), then the flat "Slot: item" list of the worn gear.
        private static void BuildEquipment(GraphBuilder b, InventoryDollVM doll, string uk)
        {
            if (doll == null) return;
            b.BeginStop("equipment").PushContext(Loc.T("inv.equipment"), "list");

            if (doll.WeaponSets != null && doll.WeaponSets.Count > 0)
            {
                // Grip toggle for the active set, above the sets (only emitted when it can re-grip —
                // mirroring the game's grip button, which is hidden otherwise).
                if (doll.CurrentSet?.Value != null && doll.CurrentSet.Value.CanToggleGrip.Value)
                    b.AddItem(ControlId.Structural(uk + "grip"), ItemNodes.GripToggle(() => doll.CurrentSet?.Value));
                b.StartRow();
                int wi = 0;
                foreach (var ws in doll.WeaponSets)
                {
                    if (ws != null)
                        b.AddItem(ControlId.Referenced(ws, uk + "set:" + wi), ItemNodes.WeaponSet(ws));
                    wi++;
                }
                b.EndRow();
            }

            var set = doll.CurrentSet?.Value;
            EmitSlot(b, uk, Loc.T("slot.primary_hand"), set?.Primary);
            EmitSlot(b, uk, Loc.T("slot.secondary_hand"), set?.Secondary);
            EmitSlot(b, uk, Loc.T("slot.armor"), doll.Armor);
            EmitSlot(b, uk, Loc.T("slot.head"), doll.Head);
            EmitSlot(b, uk, Loc.T("slot.neck"), doll.Neck);
            EmitSlot(b, uk, Loc.T("slot.shoulders"), doll.Shoulders);
            EmitSlot(b, uk, Loc.T("slot.wrist"), doll.Wrist);
            EmitSlot(b, uk, Loc.T("slot.gloves"), doll.Gloves);
            EmitSlot(b, uk, Loc.T("slot.belt"), doll.Belt);
            EmitSlot(b, uk, Loc.T("slot.ring1"), doll.Ring1);
            EmitSlot(b, uk, Loc.T("slot.ring2"), doll.Ring2);
            EmitSlot(b, uk, Loc.T("slot.feet"), doll.Feet);
            EmitSlot(b, uk, Loc.T("slot.glasses"), doll.Glasses);
            EmitSlot(b, uk, Loc.T("slot.shirt"), doll.Shirt);
            if (doll.QuickSlots != null)
                for (int i = 0; i < doll.QuickSlots.Length; i++)
                    EmitSlot(b, uk, Loc.T("slot.quick", new { index = i + 1 }), doll.QuickSlots[i]);
            b.PopContext();
        }

        private static void EmitSlot(GraphBuilder b, string uk, string name, EquipSlotVM slot)
        {
            if (slot != null)
                b.AddItem(ControlId.Structural(uk + "slot:" + name), ItemNodes.EquipSlot(name, slot));
        }

        // Party-wide readout: carry weight + load status (with the encumbrance breakdown tooltip) and
        // gold. These live on the shared stash, between the per-character equipment and the stash list.
        private static void BuildLoad(GraphBuilder b, InventoryStashVM stash, string k)
        {
            if (stash == null) return;
            b.BeginStop("load").PushContext(Loc.T("inv.inventory"), "list");
            var enc = stash.EncumbranceVM;
            if (enc != null)
                b.AddItem(ControlId.Structural(k + "encumbrance"), GraphNodes.Text(
                    () => Loc.T("inv.encumbrance", new { value = enc.LoadWeight.Value
                        + (string.IsNullOrEmpty(enc.LoadStatus.Value) ? "" : ", " + enc.LoadStatus.Value) }),
                    () => stash.EncumbranceTooltip));
            b.AddItem(ControlId.Structural(k + "gold"),
                GraphNodes.Text(() => Loc.T("inv.gold", new { value = stash.Money.Value })));
            b.PopContext();
        }

        // The stash panel — ONE stop so Up/Down walks the controls and the table: search box on top,
        // then the sort combo, then the filter row, then the Name/Type/Qty/Weight/Value item table.
        private static void BuildStash(GraphBuilder b, InventoryVM vm, string k)
        {
            var stash = vm.StashVM;
            var group = stash?.ItemSlotsGroup;
            var filter = stash?.ItemsFilter;
            b.BeginStop("stash");

            if (filter?.ItemsFilterSearchVM != null)
                b.AddItem(ControlId.Structural(k + "search"),
                    ItemNodes.ItemSearch(filter.ItemsFilterSearchVM, () => group?.SearchString?.Value));

            if (filter != null)
                b.AddItem(ControlId.Structural(k + "sort"), ItemNodes.ItemSorter(filter));

            if (filter?.SelectionFilterGroup?.EntitiesCollection != null)
            {
                b.PushContext(Loc.T("inv.filters"), "list");
                b.StartRow();
                foreach (var type in FilterOrder)
                {
                    var e = FindFilter(filter, type);
                    if (e != null)
                        b.AddItem(ControlId.Referenced(e, k + "filter:" + type),
                            GraphNodes.SelectionItem(e, () => LocalizedTexts.Instance.ItemsFilter.GetText(e.CurrentFilter)));
                }
                b.EndRow();
                b.PopContext();
            }

            // The item table: rows key by SLOT identity (equip/use/drop lands on a different identity →
            // announced); each row carries Type/Qty/Weight/Value as parts and as header-labelled cells.
            var sheet = new GraphSheet(b, k + "stash:");
            sheet.Region(Loc.T("inv.stash"),
                new[] { Loc.T("col.type"), Loc.T("col.qty"), Loc.T("col.weight"), Loc.T("col.value") });
            bool any = false;
            if (group?.VisibleCollection != null)
                foreach (var slot in group.VisibleCollection)
                {
                    if (slot == null || !slot.HasItem) continue;
                    any = true;
                    var s = slot;
                    Func<string> type = () => s.TypeName.Value;
                    Func<string> qty = () => s.Count.Value > 1 ? s.Count.Value.ToString() : "1";
                    Func<string> weight = () => Weight(s.Weight.Value);
                    Func<string> cost = () => s.Cost.Value.ToString();
                    sheet.Row(
                        ItemNodes.InventoryItem(s, new[]
                        {
                            new NodeAnnouncement(type),
                            new NodeAnnouncement(qty),
                        }),
                        s, type, qty, weight, cost);
                }
            if (!any) sheet.Line(GraphNodes.Text(() => Loc.T("inv.no_items")));
            sheet.Finish();
        }

        // The game's stash filter bar: 8 category toggles ("Other" maps to NonUsable), in display order.
        private static readonly ItemsFilter.FilterType[] FilterOrder =
        {
            ItemsFilter.FilterType.NoFilter, ItemsFilter.FilterType.Weapon, ItemsFilter.FilterType.Armor,
            ItemsFilter.FilterType.Accessories, ItemsFilter.FilterType.Ingredients, ItemsFilter.FilterType.Usable,
            ItemsFilter.FilterType.Notable, ItemsFilter.FilterType.NonUsable,
        };

        private static ItemsFilterEntityVM FindFilter(ItemsFilterVM filter, ItemsFilter.FilterType type)
        {
            foreach (var e in filter.SelectionFilterGroup.EntitiesCollection)
                if (e != null && e.CurrentFilter == type) return e;
            return null;
        }

        private static string Weight(float w) => w <= 0f ? "0" : w.ToString("0.#");
    }
}
