using System.Collections.Generic;
using Kingmaker;
using Kingmaker.UI.MVVM._VM.ContextMenu;
using Kingmaker.UI.MVVM._VM.EscMenu;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The in-game pause/Escape menu (<c>CommonVM.EscMenuContextVM.EscMenu</c>): the game's own
    /// ContextMenuEntityVM buttons — quick save/load, save/load, options, photo mode, main menu,
    /// quit, bug report (whichever exist on this platform/build) — live-enabled (greyed quick load
    /// before any quicksave, etc.). Escape closes through the VM's own close action. Opened the same
    /// way the game's gear button opens it (IEscMenuHandler.HandleOpen — see the HUD Menu entry and
    /// the exploration Escape binding); the game's EscMode pause runs underneath.
    ///
    /// Graph-native: declared fresh from the live VM every render — entry identity rides the entry
    /// VMs (tier 1), so a reopened menu re-homes naturally.
    /// </summary>
    public sealed class EscMenuScreen : Screen
    {
        public override string Key => "ctx.escmenu";
        public override string ScreenName => Loc.T("screen.game_menu");
        public override int Layer => 24; // over contexts/windows/dialogs; below modals + tutorials

        private static EscMenuVM Vm()
        {
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            return rc?.CommonVM?.EscMenuContextVM?.EscMenu?.Value;
        }

        public override bool IsActive() => Vm() != null;


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            var entries = new List<ContextMenuEntityVM>();
            foreach (var entry in new[]
            {
                vm.QuickSaveVm, vm.QuickLoadVm, vm.SaveVm, vm.LoadVm, vm.OptionsVm,
                vm.PhotoModeVm, vm.MainMenuVm, vm.ExitVm, vm.BugReportVm,
            })
                if (entry != null && !entry.IsSeparator) entries.Add(entry);

            for (int i = 0; i < entries.Count; i++)
                b.AddItem(ControlId.Referenced(entries[i], "escmenu:" + i),
                    GraphNodes.MenuEntry(entries[i], GraphNodes.Position(i + 1, entries.Count)));
        }

        // Escape closes the menu via its own close action (the same path the game's X / re-press uses).
        public override IEnumerable<ElementAction> GetActions()
        {
            var vm = Vm();
            if (vm != null)
                yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                    _ => vm.OnClose());
        }
    }
}
