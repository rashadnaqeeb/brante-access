using Kingmaker;
using Kingmaker.Settings;
using Kingmaker.UI.MVVM._VM.Settings;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The settings window (shared CommonVM screen — also covers the in-game pause menu's options).
    /// Tab-stops: the tab strip (Game / Controls / Graphics / Sound…), then the current tab's settings as
    /// a tree of collapsible header sections (one stop you arrow through), then Apply and Close buttons.
    ///
    /// Graph-native: declared fresh from the live VM every render. The graph starts on the SELECTED tab;
    /// content node keys carry the selected tab's identity, so switching tabs re-keys only the content
    /// (focus on the tab strip is untouched) and section expansion is remembered per tab by key.
    /// </summary>
    public sealed class SettingsScreen : Screen
    {
        public SettingsScreen() { Wrap = true; } // Tab wraps around the whole dialog

        public override string Key => "overlay.settings";
        public override string ScreenName => Loc.T("screen.settings");
        public override int Layer => 25;

        public override bool IsActive() => Vm() != null;

        private static SettingsVM Vm()
        {
            var g = Game.Instance;
            return g != null && g.RootUiContext != null && g.RootUiContext.CommonVM != null
                ? g.RootUiContext.CommonVM.SettingsVM.Value
                : null;
        }

        // Back (Escape) closes the settings window. (Close prompts the save-changes modal when there are
        // unconfirmed changes — MessageModalScreen handles it.)
        public override System.Collections.Generic.IEnumerable<ElementAction> GetActions()
        {
            var vm = Vm();
            if (vm != null)
                yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => vm.Close());
        }


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "settings:" + vm.GetHashCode() + ":";

            // The tab strip: one stop, arrows between tabs; the graph STARTS on the selected tab.
            b.BeginStop("tabs").PushContext(Loc.T("label.tabs"), "list");
            var tabs = vm.SelectionGroup.EntitiesCollection;
            for (int i = 0; i < tabs.Count; i++)
            {
                var id = ControlId.Referenced(tabs[i], k + "tab:" + i);
                b.AddItem(id, GraphNodes.SettingsTab(tabs[i], vm, GraphNodes.Position(i + 1, tabs.Count)));
                if (ReferenceEquals(vm.SelectedMenuEntity.Value, tabs[i])) b.SetStart(id);
            }
            b.PopContext();

            // The current tab's settings: header sections as collapsible groups in one stop. Keys carry
            // the selected tab, so a tab switch re-keys the content and expansion is remembered per tab.
            var selected = vm.SelectedMenuEntity.Value;
            string contentKey = k + "tab" + (selected != null ? selected.GetHashCode().ToString() : "?") + ":";
            b.BeginStop("content");
            SettingsEntityGraph.Emit(b, vm.SettingEntities, contentKey);

            // Action buttons: individual stops, like a Windows dialog.
            b.BeginStop("apply").AddItem(ControlId.Structural(k + "apply"),
                GraphNodes.Button(() => Loc.T("settings.apply"), () => vm.ApplyAndClose(),
                    SettingsController.HasUnconfirmedSettings));
            b.BeginStop("close").AddItem(ControlId.Structural(k + "close"),
                GraphNodes.Button(() => Loc.T("action.close"), () => vm.Close()));
        }
    }
}
