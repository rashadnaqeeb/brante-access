using System.Collections.Generic;
using Kingmaker;
using Kingmaker.UI.MVVM._VM.SaveLoad;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The save/load window (CommonVM.SaveLoadVM) — one screen with a Save or Load mode, opened single-
    /// (just Save / just Load, from the Esc menu) or dual-mode (with a Save/Load selector, from the main
    /// menu). Graph-native, and the first <see cref="GraphSheet"/> consumer: the mode selector (dual-mode
    /// only), then the slots as one sheet stop — one REGION per playthrough (Ctrl+arrows jump between
    /// them), each slot a row whose primary cell is the selection radio carrying the row's metadata as
    /// parts (arrowing down the list reads the whole row; Right steps through the metadata columns with
    /// their headers) — then the action buttons (New save in Save mode, Save/Load, Delete), which act on
    /// the selected slot. Node keys carry the VM + mode, so flipping Save↔Load re-keys and re-homes;
    /// slots key by save name+region (stable as the list refreshes). Overwrite-rename and delete go
    /// through the game's own message modals (MessageModalScreen); New save takes its name from our
    /// text-entry overlay. Layer 20.
    /// </summary>
    public sealed class SaveLoadScreen : Screen
    {
        public SaveLoadScreen() { Wrap = true; } // Tab cycles mode ↔ slots ↔ buttons

        public override string Key => "overlay.saveload";
        public override string ScreenName => Loc.T("screen.saveload");
        public override int Layer => 20;
        public override bool IsActive() => Vm() != null;

        private static SaveLoadVM Vm()
        {
            var g = Game.Instance;
            return g != null && g.RootUiContext != null && g.RootUiContext.CommonVM != null
                ? g.RootUiContext.CommonVM.SaveLoadVM.Value
                : null;
        }


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            bool saveMode = vm.Mode.Value == SaveLoadMode.Save;
            string k = "saveload:" + vm.GetHashCode() + ":" + vm.Mode.Value + ":";

            // 1) Mode selector — only when both modes are offered (dual-mode). Stable across mode flips
            // (keyed by entity), so selector focus survives switching.
            var modes = vm.SaveLoadMenuVM?.SelectionGroup?.EntitiesCollection;
            if (modes != null && modes.Count > 1)
            {
                b.BeginStop("modes").PushContext(Loc.T("save.mode"), "list");
                int mi = 0;
                foreach (var e in modes)
                {
                    if (e == null) continue;
                    var me = e;
                    b.AddItem(ControlId.Referenced(me, "saveload:mode:" + mi),
                        GraphNodes.Tab(() => ModeLabel(me.Mode), () => me.IsSelected.Value,
                            () => me.SetSelectedFromView(true)));
                    mi++;
                }
                b.PopContext();
            }

            // 2) The slots: one sheet stop, a region per playthrough.
            var groups = vm.SaveSlotCollectionVm?.SaveSlotGroups;
            if (groups != null)
            {
                b.BeginStop("slots");
                var cols = new[]
                {
                    Loc.T("save.col.character"), Loc.T("save.col.location"), Loc.T("save.col.saved"),
                    Loc.T("save.col.playtime"), Loc.T("save.col.type"), Loc.T("save.col.description"),
                };
                var sheet = new GraphSheet(b, k + "slots:");
                foreach (var g in groups)
                {
                    if (g == null || g.SaveLoadSlots == null || g.SaveLoadSlots.Count == 0) continue;
                    g.IsExpanded.Value = true; // ensure the group's slots are available/selectable
                    sheet.Region(GroupLabel(g), cols);
                    foreach (var slot in g.SaveLoadSlots)
                    {
                        if (slot == null) continue;
                        var s = slot;
                        // The primary carries the row's metadata as parts (the associated readout:
                        // arrowing down the list reads the whole row); the cells repeat them
                        // individually under their column headers for Left/Right inspection.
                        sheet.Row(
                            GraphNodes.SelectionItem(s, () => SlotName(s), extraParts: new[]
                            {
                                new NodeAnnouncement(() => s.CharacterName.Value),
                                new NodeAnnouncement(() => s.LocationName.Value),
                                new NodeAnnouncement(() => s.SaveTime.Value),
                                new NodeAnnouncement(() => SlotType(s)),
                            }),
                            s, // identity keys: deleting a save announces the row focus lands on
                            () => s.CharacterName.Value,
                            () => s.LocationName.Value,
                            () => s.SaveTime.Value,
                            () => s.TimeInGame.Value,
                            () => SlotType(s),
                            () => s.Description.Value);
                    }
                }
                sheet.Finish();
            }

            // 3) Action buttons — each its own Tab-stop (act on the selected slot).
            if (saveMode)
                b.BeginStop("new").AddItem(ControlId.Structural(k + "new"),
                    GraphNodes.Button(() => Loc.T("save.new"), NewSave));
            b.BeginStop("commit").AddItem(ControlId.Structural(k + "commit"),
                GraphNodes.Button(
                    () => Loc.T(saveMode ? "save.action.save" : "save.action.load"),
                    () => vm.SelectedSaveSlot.Value?.SaveOrLoad(),
                    () => { var s = vm.SelectedSaveSlot.Value; return s != null && s.ShowSaveLoadButton; }));
            b.BeginStop("delete").AddItem(ControlId.Structural(k + "delete"),
                GraphNodes.Button(() => Loc.T("save.action.delete"),
                    () => vm.SelectedSaveSlot.Value?.Delete(),
                    () => { var s = vm.SelectedSaveSlot.Value; return s != null && !s.ShowReadOnlyMark.Value; }));
        }

        private void NewSave()
        {
            var vm = Vm();
            if (vm?.NewSaveSlotVm == null) return;
            ModTextEntryScreen.Open(Loc.T("save.new"), NewSaveSlotVM.DefaultSaveName, name =>
            {
                if (!string.IsNullOrEmpty(name)) Vm()?.NewSaveSlotVm.Save(name);
            });
        }

        private static string ModeLabel(SaveLoadMode mode)
            => Loc.T(mode == SaveLoadMode.Save ? "save.mode.save" : "save.mode.load");

        private static string SlotName(SaveSlotVM s)
        {
            var n = s.SaveName.Value;
            return string.IsNullOrEmpty(n) ? Loc.T("save.group.default") : n;
        }

        private static string GroupLabel(SaveSlotGroupVM g)
        {
            if (!string.IsNullOrEmpty(g.CharacterName)) return g.CharacterName;
            if (!string.IsNullOrEmpty(g.GameName)) return g.GameName;
            return Loc.T("save.group.default");
        }

        // The slot's kind, composed from the game's marks (auto/quick saves are their own thing; a manual
        // save may also be read-only and/or DLC-gated).
        private static string SlotType(SaveSlotVM s)
        {
            if (s.ShowAutoSaveMark.Value) return Loc.T("save.mark.auto");
            if (s.ShowQuickSaveMark.Value) return Loc.T("save.mark.quick");
            var parts = new List<string> { Loc.T("save.mark.manual") };
            if (s.ShowReadOnlyMark.Value) parts.Add(Loc.T("save.mark.readonly"));
            if (s.ShowDlcRequiredLabel.Value) parts.Add(Loc.T("save.mark.dlc"));
            return string.Join(", ", parts.ToArray());
        }
    }
}
