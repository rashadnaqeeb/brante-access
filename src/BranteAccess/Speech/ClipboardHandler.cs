namespace BranteAccess.Speech
{
    /// <summary>Last-resort fallback: every announcement is copied to the clipboard (readable with
    /// any clipboard tool). Unity's copy buffer. Ported from wotr-access.</summary>
    internal sealed class ClipboardHandler : ISpeechHandler
    {
        public string Key => "clipboard";

        public bool Detect() => true;

        public bool Load()
        {
            HostLog.Info("[speech] Clipboard handler loaded (fallback).");
            return true;
        }

        public void Unload() { }

        public bool Speak(string text, bool interrupt) => Output(text, interrupt);

        public bool Output(string text, bool interrupt)
        {
            UnityEngine.GUIUtility.systemCopyBuffer = text;
            return true;
        }

        public bool Silence() => true;
    }
}
