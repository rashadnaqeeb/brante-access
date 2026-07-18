using System.Collections.Generic;
using Kingmaker;
using Kingmaker.DialogSystem.Blueprints; // BlueprintBookPage
using Kingmaker.UI.MVVM._VM.Dialog.BookEvent; // BookEventVM
using Kingmaker.UI.MVVM._VM.Dialog.Interchapter; // InterchapterVM (a BookEventVM subclass)
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// A book event (<see cref="BookEventVM"/>) — the illustrated storybook page with a passage of
    /// narrative and numbered choices (e.g. the Areelu vision). It's a <c>DialogType.Book</c> conversation,
    /// so it rides the SAME in-game HUD VM as ordinary dialogue (<c>DialogContextVM.BookEventVM</c>, beside
    /// <c>DialogVM</c>) and reuses the dialogue <c>AnswerVM</c> → <see cref="WrathAccess.UI.Proxies.DialogAnswerButton"/>.
    ///
    /// A page carries several cues (the paragraphs, shown together) plus the answers; we read the whole
    /// passage when a new page appears (keyed on <c>BlueprintBookPage</c>, like the dialogue cue), and the
    /// passage is the first focusable element so you can re-read it. Choosing an answer advances to the next
    /// page in place (new passage + choices) until the book closes.
    ///
    /// Interchapter/epilogue narration (e.g. "Trapped in the Darkness") is the same thing — <see
    /// cref="InterchapterVM"/> derives from BookEventVM, just stored in a separate context slot and carrying
    /// a page <c>Title</c> — so we pick that VM up too and read its title ahead of the passage. The
    /// skill-check "choose a character" sub-step is still deferred.
    /// </summary>
    public sealed class BookEventScreen : Screen
    {
        public override string Key => "ctx.bookevent";
        public override string ScreenName => Loc.T("screen.book_event");
        public override int Layer => 15; // over the in-game context + service windows, like dialogue

        // Same hide-not-close pop semantics as dialogue.
        public override bool KeepStateOnPop => true;

        private BlueprintBookPage _focusedPage; // page whose first line focus was pointed at
        private BlueprintBookPage _spokenPage;  // page we've read aloud

        private static BookEventVM Vm()
        {
            // In-area OR world-map context (the global map carries its own DialogContextVM) — see
            // DialogTranscript.Context. Interchapter/epilogue is a BookEventVM subclass in its own slot.
            var ctx = DialogTranscript.Context();
            return ctx?.BookEventVM?.Value ?? ctx?.InterchapterVM?.Value;
        }

        public override bool IsActive() => Vm() != null;

        public override void OnPush() { Reset(); }
        // Keep _spokenPage across a hide/re-push (don't re-read the passage); clear only the focus
        // marker so the re-push lands back on the passage top (the pop dropped the graph state).
        public override void OnPop() { _focusedPage = null; if (Vm() == null) Reset(); }
        private void Reset() { _focusedPage = null; _spokenPage = null; }

        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm == null) return;
            var page = vm.BlueprintBookPage.Value;
            if (page == null) return; // VM exists a frame before the first page is pushed

            // A new page: land focus on the top of the passage SILENTLY (the queued passage read below
            // is the speech); Down reaches the choices, Up re-reads earlier paragraphs.
            if (page != _focusedPage)
            {
                _focusedPage = page;
                Navigation.FocusNode(ControlId.Structural(PageKey(vm) + "row:0"), announce: false);
            }
            if (page != _spokenPage) { _spokenPage = page; Speak(vm); }
        }

        // Speak the whole passage once per page, QUEUED (never interrupting — the dialogue rule). Re-reading
        // individual paragraphs is done by arrowing the rows.
        private static void Speak(BookEventVM vm)
        {
            var lines = PassageLines(vm);
            if (lines.Count > 0)
                Tts.Speak(TextUtil.StripRichText(string.Join("\n", lines.ToArray())), interrupt: false);
        }

        private static string PageKey(BookEventVM vm)
            => "book:" + vm.GetHashCode() + ":" + (vm.BlueprintBookPage.Value?.GetHashCode() ?? 0) + ":";


        // Same shape as ordinary dialogue: the passage rows, then the choices — one stop, no positions.
        // Keys carry the page, so choosing an answer re-keys everything (OnUpdate re-homes silently).
        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null || vm.BlueprintBookPage.Value == null) return;
            string k = PageKey(vm);

            b.PushContext("", role: null, positions: false); // silent positions-off scope (a transcript)
            var lines = PassageLines(vm);
            for (int i = 0; i < lines.Count; i++)
            {
                var raw = lines[i];
                b.AddItem(ControlId.Structural(k + "row:" + i), new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new[] { new NodeAnnouncement(() => TextUtil.StripRichText(raw)) },
                    // Raw text kept un-stripped so glossary links survive for Space.
                    OnTooltip = () => TooltipScreen.FollowLinks(raw, null),
                });
            }

            var answers = vm.Answers.Value;
            if (answers != null)
            {
                int ai = 0;
                foreach (var a in answers)
                {
                    if (a != null)
                        b.AddItem(ControlId.Referenced(a, k + "ans:" + ai), DialogTranscript.AnswerNode(a));
                    ai++;
                }
            }
            b.PopContext();
        }

        // The page as transcript lines: the interchapter title first (e.g. "Trapped in the Darkness"), then
        // one line per cue paragraph (raw text — kept un-stripped so glossary links survive for Space).
        private static List<string> PassageLines(BookEventVM vm)
        {
            var lines = new List<string>();
            if (vm is InterchapterVM ic && !string.IsNullOrWhiteSpace(ic.Title.Value)) lines.Add(ic.Title.Value);
            foreach (var cue in vm.Cues)
            {
                var t = cue?.BaseText;
                if (string.IsNullOrWhiteSpace(t)) continue;
                foreach (var part in t.Split('\n'))
                    if (!string.IsNullOrWhiteSpace(part)) lines.Add(part.Trim());
            }
            return lines;
        }
    }
}
