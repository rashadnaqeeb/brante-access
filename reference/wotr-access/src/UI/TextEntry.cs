using System;
using TMPro;
using UnityEngine.Events;

namespace WrathAccess.UI
{
    /// <summary>
    /// Owns the keyboard while the user edits one of the game's own <see cref="TMP_InputField"/>s
    /// directly, so Unity/TMP handle caret, selection, backspace, Unicode and IME composition
    /// natively — we only focus the field, echo each change, and exit on commit.
    ///
    /// While <see cref="Active"/>, <c>InputManager.Tick()</c> early-returns, so our nav and global
    /// hotkeys don't also fire and the field receives raw keystrokes. The field's <c>onEndEdit</c>
    /// (Enter or focus loss — already wired by the game to the phase VM, which commits the value) is
    /// our commit-and-exit signal, so we never have to parse Enter/Escape ourselves.
    /// </summary>
    public static class TextEntry
    {
        public static bool Active { get; private set; }

        private static int _endedFrame = -1;

        /// <summary>True while we own the keyboard, plus the single frame in which editing ended.
        /// The same Enter/Escape press that commits the field stays <c>GetKeyDown</c>-true for the
        /// whole frame; if the EventSystem runs TMP's key handling before our input poll, editing is
        /// already over by the time we poll, so without this guard that keypress would also dispatch
        /// as a nav action (re-activating the field, or navigating back on Escape).</summary>
        public static bool SuppressInput => Active || _endedFrame == UnityEngine.Time.frameCount;

        private static TMP_InputField _field;
        private static string _label;
        private static string _last = "";
        private static UnityAction<string> _onChanged;
        private static UnityAction<string> _onEnd;

        public static void Begin(TMP_InputField field, string label)
        {
            if (Active || field == null) return;
            _field = field;
            _label = string.IsNullOrEmpty(label) ? "text" : label;
            _last = field.text ?? "";
            Active = true;

            _onChanged = OnChanged;
            _onEnd = OnEnd;
            field.onValueChanged.AddListener(_onChanged);
            field.onEndEdit.AddListener(_onEnd);

            // Focus the field and drop the caret at the end (clearing any select-all on focus) so
            // typing appends rather than replacing the whole value.
            field.Select();
            field.ActivateInputField();
            field.MoveTextEnd(false);

            string current = string.IsNullOrEmpty(_last) ? "blank" : _last;
            Tts.Speak(Loc.T("edit.begin", new { label = _label, value = current }), interrupt: true);
        }

        private static void OnChanged(string text)
        {
            text = text ?? "";
            Echo(_last, text);
            _last = text;
        }

        // Enter or focus loss — TMP has committed to the VM via the game's own onEndEdit listener.
        private static void OnEnd(string finalText) => End(finalText);

        public static void End(string finalText)
        {
            if (!Active) return;
            var field = _field;
            if (field != null)
            {
                if (_onChanged != null) field.onValueChanged.RemoveListener(_onChanged);
                if (_onEnd != null) field.onEndEdit.RemoveListener(_onEnd);
                field.DeactivateInputField();
            }
            Active = false;
            _endedFrame = UnityEngine.Time.frameCount;
            _field = null; _onChanged = null; _onEnd = null;

            string val = string.IsNullOrEmpty(finalText) ? "blank" : finalText;
            Tts.Speak(_label + ": " + val, interrupt: true);
            // Hand focus back to the nav element the user was on.
            Navigation.AnnounceCurrent();
        }

        /// <summary>Per-keystroke echo: announce the inserted run, or "deleted" + the removed run.
        /// Diffs by common prefix so a mid-string edit (caret moved) still reads the right char.</summary>
        private static void Echo(string oldText, string newText)
        {
            int p = 0;
            int max = Math.Min(oldText.Length, newText.Length);
            while (p < max && oldText[p] == newText[p]) p++;

            if (newText.Length > oldText.Length)
                Tts.Speak(Speakable(newText.Substring(p, newText.Length - oldText.Length)), interrupt: true);
            else if (newText.Length < oldText.Length)
                Tts.Speak(Loc.T("edit.deleted", new { text = Speakable(oldText.Substring(p, oldText.Length - newText.Length)) }), interrupt: true);
        }

        // A lone space says nothing through TTS; name it so the user hears the keypress landed.
        private static string Speakable(string s) => s == " " ? "space" : s;
    }
}
