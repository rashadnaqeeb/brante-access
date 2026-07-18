using System;
using System.Collections.Generic;
using Kingmaker.Blueprints.Root;                       // LocalizedTexts (sort labels)
using Kingmaker.Blueprints.Root.Strings;               // UIStrings.ContextMenu
using Kingmaker.PubSubSystem;                          // EventBus
using Kingmaker.UI.Common;                             // ItemsFilter, UIUtility
using Kingmaker.UI.MVVM._VM.ServiceWindows.Inventory;  // EquipSlotVM, WeaponSetVM, IInventoryHandler
using Kingmaker.UI.MVVM._VM.Slots;                     // ItemSlotVM, ItemsFilterVM, INewSlotsHandler
using WrathAccess.UI.Graph;

namespace WrathAccess.UI
{
    /// <summary>
    /// Node factories for the inventory family (the old ProxyInventoryItem / ProxyEquipSlot /
    /// ProxyWeaponSet / ProxyGripToggle / ProxyItemSorter / ProxyItemSearch, factory-shaped). Actions go
    /// through the same EventBus contracts the game's views use (IInventoryHandler / INewSlotsHandler →
    /// InventoryVM), so equip/drop/split logic is never reimplemented. Badges are localized (the proxies
    /// carried raw English — fixed in the port).
    /// </summary>
    public static class ItemNodes
    {
        private static UIContextMenu Menu => UIStrings.Instance.ContextMenu;

        // The slot's item template: comparisons (the EQUIPPED items) come first, the item's own is LAST —
        // the same end the game's ShowInfo reads. Resolved live per press.
        private static void OpenItemTooltip(ItemSlotVM slot)
        {
            var t = slot.Tooltip.Value;
            var tpl = t != null && t.Count > 0 ? t[t.Count - 1] : null;
            if (tpl != null) Screens.TooltipScreen.Open(tpl);
        }

        /// <summary>One stash item: name with its visible badges folded in (magic / notable / unusable /
        /// new), the item's tooltip on Space, Enter = the sighted double-click quick action (Equip
        /// equipment / Use usables, else the menu), Backspace = the full context menu (each entry gated
        /// by the same VM predicate the game's view checks, evaluated at open).</summary>
        public static NodeVtable InventoryItem(ItemSlotVM slot, IEnumerable<NodeAnnouncement> extraParts = null)
        {
            Func<string> label = () =>
            {
                var name = slot.DisplayName.Value;
                if (string.IsNullOrEmpty(name)) name = slot.Item.Value?.Name ?? "item";
                var flags = new List<string>();
                if (slot.IsMagic.Value) flags.Add(Loc.T("item.magic"));
                if (slot.IsNotable.Value) flags.Add(Loc.T("item.notable"));
                if (!slot.CanUse.Value) flags.Add(Loc.T("item.unusable"));
                if (slot.NeedCheck.Value) flags.Add(Loc.T("item.new"));
                return flags.Count > 0 ? name + " (" + string.Join(", ", flags.ToArray()) + ")" : name;
            };
            var anns = new List<NodeAnnouncement> { GraphNodes.LabelPart(label) };
            if (extraParts != null) anns.AddRange(extraParts);
            return new NodeVtable
            {
                ControlType = ControlTypes.Item,
                Announcements = anns,
                SearchText = label,
                OnActivate = () =>
                {
                    if (slot.IsEquipment) Equip(slot);
                    else if (slot.IsUsable) Use(slot);
                    else OpenStashMenu(slot, label());
                },
                OnSecondary = () => OpenStashMenu(slot, label()),
                OnTooltip = () => OpenItemTooltip(slot),
            };
        }

        // The live context-menu set — mirrors InventorySlotPCView.CreateContextMenu, each entry gated by
        // the same VM predicate, evaluated now (a non-applicable action just isn't listed).
        private static void OpenStashMenu(ItemSlotVM slot, string title)
        {
            var labels = new List<string>();
            var runs = new List<Action>();
            void Add(bool when, string label, Action run) { if (when) { labels.Add(label); runs.Add(run); } }

            Add(slot.IsEquipment, Menu.Equip, () => Equip(slot));
            Add(slot.IsUsable, Menu.Use, () => Use(slot));
            Add(slot.IsUsableWhileCan, Menu.UseWhileCan, () => UseWhileCan(slot));
            Add(slot.IsScroll, slot.CopyItemLabel, () => CopyScroll(slot));
            Add(slot.IsPossibleSplit, Menu.Split, () => Split(slot));
            Add(slot.CanTalk, Menu.Use, slot.StartDialog);
            Add(slot.HasItem, Menu.Drop, () => Drop(slot));
            Add(slot.HasItem, Menu.Information, slot.ShowInfo);

            if (labels.Count == 0) { Tts.Speak(Loc.T("menu.no_actions"), interrupt: true); return; }
            var actions = runs;
            Screens.ChoiceSubmenuScreen.Open(title, labels, -1,
                idx => { if (idx >= 0 && idx < actions.Count) actions[idx]?.Invoke(); });
        }

        /// <summary>One equipment-doll slot as a line — "Slot: item (badges)" / "Slot: empty". Enter
        /// unequips (the double-click action); Backspace = the full slot menu; Space = the item's tooltip.
        /// Empty slots read but carry no actions (equipping happens from the stash item's Equip).</summary>
        public static NodeVtable EquipSlot(string slotName, EquipSlotVM slot)
        {
            Func<bool> hasItem = () => slot != null && slot.HasItem;
            Func<string> label = () =>
            {
                if (!hasItem()) return slotName + ": " + Loc.T("slot.empty");
                var name = slot.DisplayName.Value;
                if (string.IsNullOrEmpty(name)) name = slot.Item.Value?.Name ?? "item";
                var flags = new List<string>();
                if (slot.IsMagic.Value) flags.Add(Loc.T("item.magic"));
                if (slot.IsNotable.Value) flags.Add(Loc.T("item.notable"));
                if (slot.CantRemove.Value) flags.Add(Loc.T("item.cant_remove"));
                if (flags.Count > 0) name += " (" + string.Join(", ", flags.ToArray()) + ")";
                return slotName + ": " + name;
            };
            return new NodeVtable
            {
                ControlType = ControlTypes.Item,
                Announcements = new[] { GraphNodes.LabelPart(label) },
                SearchText = label,
                OnActivate = () => { if (hasItem() && slot.TryUnequip()) Refresh(); },
                OnSecondary = () => { if (hasItem()) OpenEquipMenu(slot, label()); },
                OnTooltip = () => { if (hasItem()) OpenItemTooltip(slot); },
            };
        }

        // Mirrors InventoryEquipSlotPCView.CreateContextMenu.
        private static void OpenEquipMenu(EquipSlotVM slot, string title)
        {
            var labels = new List<string>();
            var runs = new List<Action>();
            void Add(bool when, string label, Action run) { if (when) { labels.Add(label); runs.Add(run); } }

            Add(slot.IsEquipment, Menu.TakeOff, () => { if (slot.TryUnequip()) Refresh(); });
            Add(slot.IsUsable, Menu.Use, () => Use(slot));
            Add(slot.IsUsableWhileCan, Menu.UseWhileCan, () => UseWhileCan(slot));
            Add(slot.IsScroll, slot.CopyItemLabel, () => CopyScroll(slot));
            Add(slot.CanTalk, Menu.Use, slot.StartDialog);
            Add(slot.HasItem, Menu.Information, slot.ShowInfo);

            if (labels.Count == 0) { Tts.Speak(Loc.T("menu.no_actions"), interrupt: true); return; }
            var actions = runs;
            Screens.ChoiceSubmenuScreen.Open(title, labels, -1,
                idx => { if (idx >= 0 && idx < actions.Count) actions[idx]?.Invoke(); });
        }

        /// <summary>One weapon set as a radio — "Weapon set II: Longsword, Shield". Enter activates it
        /// (the game's selection contract re-equips its weapons).</summary>
        public static NodeVtable WeaponSet(WeaponSetVM vm)
        {
            Func<string> label = () =>
            {
                string Weapon(EquipSlotVM s) => s != null && s.HasItem ? (s.DisplayName.Value ?? s.Item.Value?.Name) : null;
                var weapons = new List<string>();
                var p = Weapon(vm.Primary); if (!string.IsNullOrEmpty(p)) weapons.Add(p);
                var sec = Weapon(vm.Secondary); if (!string.IsNullOrEmpty(sec)) weapons.Add(sec);
                var set = Loc.T("inv.weapon_set", new { index = UIUtility.ArabicToRoman(vm.Index + 1) });
                return set + ": " + (weapons.Count == 0 ? Loc.T("slot.empty") : string.Join(", ", weapons.ToArray()));
            };
            return new NodeVtable
            {
                ControlType = ControlTypes.RadioButton,
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(label),
                    GraphNodes.SelectedPart(() => vm.IsSelected.Value),
                },
                SearchText = label,
                StateText = () => vm.IsSelected.Value ? Loc.T("state.selected") : null,
                OnActivate = () => vm.SetSelectedFromView(true),
            };
        }

        /// <summary>The grip toggle for the active weapon set — "Grip, one-handed/two-handed"; Enter flips
        /// it via TryToggleGrip. Only emitted when the set's weapon can re-grip (the caller checks
        /// CanToggleGrip, mirroring the game's grip button which is hidden otherwise). The value part is
        /// LIVE, so the flip announces itself.</summary>
        public static NodeVtable GripToggle(Func<WeaponSetVM> current)
        {
            Func<string> value = () =>
            {
                switch (current()?.Grip.Value)
                {
                    case Kingmaker.Items.GripType.OneHanded: return Loc.T("grip.one_handed");
                    case Kingmaker.Items.GripType.TwoHanded: return Loc.T("grip.two_handed");
                    default: return "";
                }
            };
            return new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new[]
                {
                    GraphNodes.LabelPart(() => Loc.T("inventory.grip")),
                    new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = () => Loc.T("inventory.grip"),
                OnActivate = () => current()?.TryToggleGrip(),
            };
        }

        /// <summary>The stash sort combo over <see cref="ItemsFilter.SorterType"/> — the current sort as a
        /// LIVE value; Enter opens the localized options and applies via SetCurrentSorter.</summary>
        public static NodeVtable ItemSorter(ItemsFilterVM filter)
        {
            string SortName(ItemsFilter.SorterType t) => LocalizedTexts.Instance.ItemsFilter.GetText(t);
            return new NodeVtable
            {
                ControlType = ControlTypes.ComboBox,
                Announcements = new[]
                {
                    GraphNodes.LabelPart(() => Loc.T("inventory.sort")),
                    new NodeAnnouncement(() => SortName(filter.CurrentSorter.Value), live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = () => Loc.T("inventory.sort"),
                OnActivate = () =>
                {
                    var values = (ItemsFilter.SorterType[])Enum.GetValues(typeof(ItemsFilter.SorterType));
                    var labels = new List<string>(values.Length);
                    int current = 0;
                    for (int i = 0; i < values.Length; i++)
                    {
                        labels.Add(SortName(values[i]));
                        if (values[i] == filter.CurrentSorter.Value) current = i;
                    }
                    Screens.ChoiceSubmenuScreen.Open(Loc.T("inventory.sort"), labels, current,
                        idx => { if (idx >= 0 && idx < values.Length) filter.SetCurrentSorter(values[idx]); });
                },
            };
        }

        /// <summary>The stash search box: the current text (or "blank") as a LIVE value; Enter opens the
        /// mod text entry and writes through SetSearchString (we drive the model, not the on-screen
        /// input field of the view we bypass).</summary>
        public static NodeVtable ItemSearch(ItemsFilterSearchVM search, Func<string> current)
        {
            return new NodeVtable
            {
                Announcements = new[]
                {
                    GraphNodes.LabelPart(() => Loc.T("inventory.search")),
                    new NodeAnnouncement(() => Loc.T("role.edit"), kind: AnnouncementKinds.Role),
                    new NodeAnnouncement(() =>
                    {
                        var v = current?.Invoke();
                        return string.IsNullOrEmpty(v) ? Loc.T("value.blank") : v;
                    }, live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = () => Loc.T("inventory.search"),
                OnActivate = () => Screens.ModTextEntryScreen.Open(Loc.T("inventory.search"),
                    current?.Invoke() ?? "", s => search.SetSearchString(s ?? "")),
            };
        }

        // Action verbs, routed exactly like the game's slot views (EventBus → InventoryVM / VM + Refresh).
        private static void Equip(ItemSlotVM slot) => EventBus.RaiseEvent<IInventoryHandler>(h => h.TryEquip(slot));
        private static void Drop(ItemSlotVM slot) => EventBus.RaiseEvent<IInventoryHandler>(h => h.TryDrop(slot));
        private static void Split(ItemSlotVM slot) => EventBus.RaiseEvent<INewSlotsHandler>(h => h.HandleTrySplitSlot(slot));
        private static void Use(ItemSlotVM slot) { slot.UseItem(); Refresh(); }
        private static void UseWhileCan(ItemSlotVM slot) { slot.UseItemWhileCan(); Refresh(); }
        private static void CopyScroll(ItemSlotVM slot) { slot.CopyItem(); Refresh(); }
        private static void Refresh() => EventBus.RaiseEvent<IInventoryHandler>(h => h.Refresh());
    }
}
