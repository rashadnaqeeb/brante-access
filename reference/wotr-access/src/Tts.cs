namespace WrathAccess
{
    /// <summary>
    /// The call-site facade over the speech-handler system (<see cref="Speech.SpeechManager"/> -
    /// Prism / SAPI / Clipboard, selectable in the Speech settings tab). Keeps the two house rules at
    /// the boundary: rich-text is stripped (game labels are TMP markup), and speech NEVER interrupts by
    /// default - queued speech is the user's preference (carried over from SayTheSpire). Routed through
    /// the handler's Output (speech + braille where supported).
    /// </summary>
    public static class Tts
    {
        public static void Speak(string text, bool interrupt = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            text = TextUtil.StripRichText(text); // game labels are TMP rich text
            if (string.IsNullOrEmpty(text)) return;
            Speech.SpeechManager.Output(text, interrupt);
        }

        public static void Stop() => Speech.SpeechManager.Silence();
    }
}
