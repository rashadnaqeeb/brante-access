using System.Collections.Generic;
using Kingmaker;
using Kingmaker.UI.MVVM;
using Kingmaker.UI.MVVM._VM.ContextMenu;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The main menu, our first navigable screen: the sidebar entries (Continue / New Game / Load / …)
    /// from MainMenuSideBarVM as one arrow-navigable list, each confirming to run its entry's command —
    /// letting a blind player start/load a game with our own nav and unlock the downstream screens.
    ///
    /// Graph-native: declared fresh from the live sidebar VM every render; entry identity rides the entry
    /// VMs. The "Main Menu" list context reproduces the old labeled-container announcement on entry.
    /// </summary>
    public sealed class MainMenuScreen : Screen
    {
        public override string Key => "ctx.mainmenu";
        public override int Layer => 0;
        // No ScreenName: the sidebar list context announces "Main Menu" via the context diff instead of
        // the screen self-announcing.

        public override bool IsActive()
        {
            var g = Game.Instance;
            if (g == null || g.RootUiContext == null || !g.RootUiContext.IsMainMenu) return false;

            // The sidebar is hidden whenever a main-menu sub-window covers it
            // (New Game setup, character generation, DLC manager, marketing popup).
            // Until those get their own screens, just stop being navigable here.
            var mm = g.RootUiContext.MainMenuVM;
            if (mm == null) return false;
            if (mm.NewGameVM != null) return false;
            if (mm.DLCManagerVM != null) return false;
            if (mm.CharGenContextVM != null && mm.CharGenContextVM.CharGenVM != null
                && mm.CharGenContextVM.CharGenVM.Value != null) return false;
            if (mm.MarketingMessageVM != null && mm.MarketingMessageVM.Value != null) return false;
            return true;
        }


        public override void Build(GraphBuilder b)
        {
            var sidebar = RootUIContext.Instance?.MainMenuVM?.MainMenuSideBarVM;
            if (sidebar == null) return;

            var entries = new List<ContextMenuEntityVM>();
            foreach (var vm in new[]
            {
                sidebar.ContinueVm, sidebar.NewGameVm, sidebar.LoadVm, sidebar.DLCManagerVm,
                sidebar.OptionsVm, sidebar.CreditVm, sidebar.LicenseVm, sidebar.ExitVm,
            })
                if (vm != null && !vm.IsSeparator) entries.Add(vm);

            b.PushContext(Loc.T("screen.main_menu"), "list");
            for (int i = 0; i < entries.Count; i++)
                b.AddItem(ControlId.Referenced(entries[i], "mainmenu:" + i),
                    GraphNodes.MenuEntry(entries[i], GraphNodes.Position(i + 1, entries.Count)));
            b.PopContext();
        }
    }
}
