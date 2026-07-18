namespace WrathAccess.Buffers
{
    /// <summary>
    /// The four buffer review actions, bound to Alt+arrows (see Main): Alt+Left/Right cycle between buffers
    /// (announcing the buffer's name + its current line), Alt+Up/Down move through the current buffer's lines
    /// (announcing just the line). Speech interrupts so rapid scrolling stays responsive. Live wherever the
    /// Exploration input category is — i.e. in a game, exploring or with the HUD focused.
    /// </summary>
    internal static class BufferControls
    {
        public static void NextBuffer() { BufferManager.Instance.MoveToNext(); ReportBuffer(); }
        public static void PrevBuffer() { BufferManager.Instance.MoveToPrevious(); ReportBuffer(); }

        public static void NextItem()
        {
            var b = BufferManager.Instance.CurrentBuffer;
            b?.MoveToNext();
            ReportItem(b);
        }

        public static void PrevItem()
        {
            var b = BufferManager.Instance.CurrentBuffer;
            b?.MoveToPrevious();
            ReportItem(b);
        }

        // Switching buffers reads "<buffer>: <current line>" (or empty / none).
        private static void ReportBuffer()
        {
            var b = BufferManager.Instance.CurrentBuffer;
            if (b == null) { Tts.Speak(Loc.T("buffers.none"), interrupt: true); return; }
            if (b.IsEmpty) { Tts.Speak(Loc.T("buffers.empty", new { buffer = b.Label }), interrupt: true); return; }
            Tts.Speak(Loc.T("buffers.current", new { buffer = b.Label, item = b.CurrentItem ?? "" }), interrupt: true);
        }

        // Moving within a buffer reads just the landed line.
        private static void ReportItem(Buffer b)
        {
            if (b == null) { Tts.Speak(Loc.T("buffers.no_selected"), interrupt: true); return; }
            if (b.IsEmpty) { Tts.Speak(Loc.T("buffers.empty", new { buffer = b.Label }), interrupt: true); return; }
            var item = b.CurrentItem;
            if (item != null) Tts.Speak(item, interrupt: true);
        }
    }
}
