using Kingmaker;
using Kingmaker.UI.MVVM._VM.Settings.KeyBindSetupDialog;
using WrathAccess.UI;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The key-binding capture dialog (SettingsVM.CurrentKeyBindDialog). Key capture lives
    /// in the game's view (it reads raw Input each frame), so this screen sets
    /// <see cref="CapturesRawInput"/> — InputManager stops dispatching while it's on top, so
    /// the keys reach the game's capture. We just announce: instructions on open, and the
    /// "already in use" message when a conflict keeps the dialog open. Success/cancel closes
    /// the dialog → we pop and the slot re-announces the new binding.
    /// </summary>
    public sealed class KeyBindCaptureScreen : Screen
    {
        /// <summary>Set by the slot before opening, so we can name what's being bound.</summary>
        public static string PendingLabel;

        public static KeyBindingSetupDialogVM Dialog()
        {
            var g = Game.Instance;
            var sv = g != null && g.RootUiContext != null && g.RootUiContext.CommonVM != null
                ? g.RootUiContext.CommonVM.SettingsVM.Value
                : null;
            return sv != null ? sv.CurrentKeyBindDialog.Value : null;
        }

        public override string Key => "overlay.keybindcapture";
        public override int Layer => 27; // above Settings (25), below the message modal (30)
        public override bool CapturesRawInput => true;
        public override bool IsActive() => Dialog() != null;

        private bool _lastOccupied;

        public override void OnPush() { _lastOccupied = false; }

        public override void OnFocus()
        {
            string what = string.IsNullOrEmpty(PendingLabel) ? "" : PendingLabel + ". ";
            Tts.Speak(Loc.T("bind.prompt", new { what }));
        }

        public override void OnUpdate()
        {
            var dlg = Dialog();
            if (dlg == null) return;
            if (dlg.CurrentBindingIsOccupied && !_lastOccupied)
                Tts.Speak(Loc.T("bind.in_use"), interrupt: true);
            _lastOccupied = dlg.CurrentBindingIsOccupied;
        }
    }
}
