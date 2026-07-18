using System;
using System.Text;
using UnityEngine;

namespace WrathAccess.Screens
{
    /// <summary>
    /// A mod-owned single-line text editor for arbitrary mod strings — e.g. an overlay's name. Pushed as a
    /// CHILD SCREEN of whatever screen opened it (so it owns the keyboard while up and focus returns to the
    /// opener on close). Unlike the game-field text entry it needs no TMP_InputField: it sets
    /// <see cref="Screen.CapturesRawInput"/> so our nav stands down and reads
    /// <see cref="UnityEngine.Input.inputString"/> each frame (printable chars, backspace, Enter), echoing
    /// each keystroke for the screen reader. Enter confirms (fires the callback), Escape cancels. Focus mode
    /// stays on (the opener engaged it), so the game's letter hotkeys stay muted while typing.
    /// </summary>
    public sealed class ModTextEntryScreen : Screen
    {
        private readonly string _label;
        private readonly StringBuilder _buf;
        private readonly Action<string> _onConfirm;
        private bool _armed; // the opening key (the Enter that got us here) has released

        private ModTextEntryScreen(string label, string initial, Action<string> onConfirm)
        {
            _label = label;
            _buf = new StringBuilder(initial ?? "");
            _onConfirm = onConfirm;
        }

        /// <summary>Open the editor as a child of the current screen.</summary>
        public static void Open(string label, string initial, Action<string> onConfirm)
            => ScreenManager.Current?.PushChild(new ModTextEntryScreen(label, initial, onConfirm));

        public override string Key => "overlay.modtextentry";
        public override bool CapturesRawInput => true;
        public override bool IsActive() => false; // only ever a child screen

        public override void OnFocus()
        {
            var cur = _buf != null && _buf.Length > 0 ? _buf.ToString() : Loc("text.blank", "blank");
            Tts.Speak((_label ?? Loc("text.prompt", "Edit text")) + ". " + cur + ". "
                + Loc("text.help", "Type to edit, Backspace to delete, Enter to confirm, Escape to cancel."));
        }

        public override void OnUpdate()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                Tts.Speak(Loc("text.cancelled", "Cancelled"));
                Close();
                return;
            }

            // Wait for the opening key to release, else its '\n' confirms immediately.
            if (!_armed)
            {
                if (!UnityEngine.Input.anyKey) _armed = true;
                return;
            }

            var typed = UnityEngine.Input.inputString;
            if (string.IsNullOrEmpty(typed)) return;

            foreach (char c in typed)
            {
                if (c == '\b')
                {
                    if (_buf.Length > 0)
                    {
                        char removed = _buf[_buf.Length - 1];
                        _buf.Remove(_buf.Length - 1, 1);
                        Echo(removed);
                    }
                    else Tts.Speak(Loc("text.blank", "blank"), interrupt: true);
                }
                else if (c == '\n' || c == '\r')
                {
                    var value = _buf.ToString().Trim();
                    var cb = _onConfirm;
                    Close();
                    cb?.Invoke(value);
                    return;
                }
                else if (!char.IsControl(c))
                {
                    _buf.Append(c);
                    Echo(c);
                }
            }
        }

        private void Close() => ParentScreen?.RemoveChild(this);

        // Echo a typed/deleted character (space spoken as the word, since TTS skips a bare space).
        private static void Echo(char c)
            => Tts.Speak(c == ' ' ? Loc("text.space", "space") : c.ToString(), interrupt: true);

        private static string Loc(string key, string fallback)
            => WrathAccess.Localization.LocalizationManager.GetOrDefault("settings", key, fallback);
    }
}
