using Kingmaker;
using Kingmaker.UI.MVVM._VM.MessageBox;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The game's generic message/confirm modal (CommonVM.MessageModalVM) — used for the
    /// settings save-changes prompt and confirmations across the whole game. Reads the
    /// message text and exposes the Accept / Decline buttons, activating them via the VM
    /// (OnAcceptPressed / OnDeclinePressed). Layer 30 (above everything else).
    ///
    /// Text-field modals (e.g. the save overwrite-rename prompt) add an editable name field that opens
    /// our text-entry overlay; Accept reads the field (the VM's OnAcceptPressed passes InputText on).
    /// The item-list modal variant isn't handled yet.
    ///
    /// Graph-native: declared fresh from the live VM every render. Node keys carry the VM's identity,
    /// so a modal SWAP (one closed, another opened) drops the old keys and focus re-homes to the new
    /// message with a fresh readout.
    /// </summary>
    public sealed class MessageModalScreen : Screen
    {
        public MessageModalScreen() { Wrap = true; } // Tab cycles message ↔ buttons

        public override string Key => "overlay.modal";
        public override string ScreenName => Loc.T("screen.dialog");
        public override int Layer => 30;

        public override bool IsActive() => Vm() != null;

        private static MessageModalVM Vm()
        {
            var g = Game.Instance;
            return g != null && g.RootUiContext != null && g.RootUiContext.CommonVM != null
                ? g.RootUiContext.CommonVM.MessageModalVM.Value
                : null;
        }


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "modal:" + vm.GetHashCode() + ":";

            // Message body first (focusable so it can be re-read), then the buttons — each its own Tab-stop.
            if (!string.IsNullOrEmpty(vm.MessageText))
                b.BeginStop("msg").AddItem(ControlId.Structural(k + "msg"), GraphNodes.Text(() => vm.MessageText));

            // Text-field modal (e.g. save overwrite-rename): an editable field. Enter opens our text-entry
            // overlay prefilled with the current value; Accept below submits it (OnAcceptPressed reads InputText).
            if (vm.ModalType == Kingmaker.UI.MessageModalBase.ModalType.TextField)
                b.BeginStop("edit").AddItem(ControlId.Structural(k + "edit"), GraphNodes.Button(
                    () => Loc.T("modal.edit_name", new { value = vm.InputText.Value }),
                    () => ModTextEntryScreen.Open(Loc.T("modal.name"), vm.InputText.Value, t => vm.InputText.Value = t)));

            b.BeginStop("accept").AddItem(ControlId.Structural(k + "accept"),
                GraphNodes.Button(() => vm.AcceptText, () => vm.OnAcceptPressed()));
            if (vm.ShowDecline)
                b.BeginStop("decline").AddItem(ControlId.Structural(k + "decline"),
                    GraphNodes.Button(() => vm.DeclineText, () => vm.OnDeclinePressed()));
        }
    }
}
