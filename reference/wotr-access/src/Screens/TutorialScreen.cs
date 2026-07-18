using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings; // UIStrings
using Kingmaker.Tutorial; // TutorialData
using Kingmaker.UI.MVVM._VM.Tutorial;
using UnityEngine;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// A tutorial popup: reads the current page's text, offers page controls (multi-page modals), a
    /// "don't show" checkbox and a Dismiss button. Handles BOTH window kinds off the shared
    /// <see cref="TutorialWindowVM"/> base — the modal "big" window (<see cref="TutorialModalWindowVM"/>,
    /// multi-page) and the "small"/hint window (<see cref="TutorialHintWindowVM"/>). The basic-controls
    /// tutorials (Movement/camera) are actually the *small* kind despite rendering full-size (their
    /// blueprint's Windowed flag is false).
    ///
    /// It announces the text on appearance even when Focus Mode is off (a blocking popup shouldn't be
    /// silent); navigating still needs Focus Mode. Dismiss mirrors the game's close/Esc
    /// (<c>ShowWindow.Value = false</c>); the checkbox applies <c>BanTutor()</c> on dismiss.
    ///
    /// Graph-native: node keys carry the VM identity AND the page, so a new tutorial or a page flip drops
    /// the old keys — focus re-homes onto the text and the differ reads it (the old rebuild+announce dance).
    /// </summary>
    public sealed class TutorialScreen : Screen
    {
        public override string Key => "ctx.tutorial";
        public override int Layer => 28; // modal popup, above gameplay/windows/settings

        private bool _banOnClose;
        private TutorialWindowVM _spokenVm;    // focus-mode-OFF fallback delivery markers
        private TutorialData.Page _spokenPage;

        private static TutorialWindowVM Vm()
        {
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            var tv = rc?.CommonVM?.TutorialVM?.Value;
            if (tv == null) return null;
            var big = tv.BigWindowVM;
            if (big != null && big.ShowWindow.Value) return big;
            var small = tv.SmallWindowVM;
            if (small != null && small.ShowWindow.Value) return small;
            return null;
        }

        public override bool IsActive() => Vm() != null;

        public override void OnPush() { _banOnClose = false; }
        public override void OnPop() { _banOnClose = false; _spokenVm = null; _spokenPage = null; }

        public override void OnFocus()
        {
            // The differ reads the landing under focus mode; cover the off case so a blocking tutorial is
            // never silent.
            if (!FocusMode.Active) SpeakText();
        }

        public override void OnUpdate()
        {
            // A new tutorial replacing the current one resets the checkbox; the focus-mode-off fallback
            // speaks new content (under focus mode the key change re-homes + announces via the differ).
            var vm = Vm();
            if (vm == null) return;
            if (vm != _spokenVm) _banOnClose = false;
            if (!FocusMode.Active && (vm != _spokenVm || CurrentPageOf(vm) != _spokenPage)) SpeakText();
            _spokenVm = vm;
            _spokenPage = CurrentPageOf(vm);
        }


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            var modal = vm as TutorialModalWindowVM;
            int page = modal != null ? modal.CurrentPageIndex.Value : 0;
            string k = "tutorial:" + vm.GetHashCode() + ":" + page + ":";

            b.PushContext(Loc.T("tutorial.title"), "list");
            b.AddItem(ControlId.Structural(k + "text"), GraphNodes.Text(() => PageText(Vm())));
            if (modal != null && modal.MultiplePages)
            {
                b.AddItem(ControlId.Structural(k + "prev"),
                    GraphNodes.Button(() => Loc.T("tutorial.prev_page"), () => StepPage(-1), CanPrev));
                b.AddItem(ControlId.Structural(k + "next"),
                    GraphNodes.Button(() => Loc.T("tutorial.next_page"), () => StepPage(1), CanNext));
            }
            if (vm.CanBeBanned)
                b.AddItem(ControlId.Structural(k + "ban"), GraphNodes.Toggle(
                    () => (string)UIStrings.Instance.Tutorial.DontShowThisTutorial,
                    () => _banOnClose, () => _banOnClose = !_banOnClose));
            b.AddItem(ControlId.Structural(k + "dismiss"),
                GraphNodes.Button(() => Loc.T("tutorial.dismiss"), Dismiss));
            b.PopContext();
        }

        private void SpeakText()
        {
            var vm = Vm();
            if (vm != null) Tts.Speak(Loc.T("tutorial.prefix", new { text = PageText(vm) }), interrupt: true);
        }

        private static TutorialData.Page CurrentPageOf(TutorialWindowVM vm)
        {
            if (vm is TutorialModalWindowVM m) return m.CurrentPage.Value;
            var pages = vm != null ? vm.Pages : null;
            return (pages != null && pages.Count > 0) ? pages[0] : null;
        }

        private static bool CanPrev() => Vm() is TutorialModalWindowVM m && m.CurrentPageIndex.Value > 0;
        private static bool CanNext() => Vm() is TutorialModalWindowVM m && m.CurrentPageIndex.Value < m.PageCount - 1;

        private static void StepPage(int dir)
        {
            if (Vm() is TutorialModalWindowVM m)
                m.CurrentPageIndex.Value = Mathf.Clamp(m.CurrentPageIndex.Value + dir, 0, m.PageCount - 1);
        }

        private void Dismiss()
        {
            var vm = Vm();
            if (vm == null) return;
            if (_banOnClose) vm.BanTutor();
            vm.ShowWindow.Value = false;
        }

        private static string PageText(TutorialWindowVM vm)
        {
            if (vm == null) return "";
            if (vm is TutorialModalWindowVM m)
            {
                var prefix = m.MultiplePages ? "Page " + (m.CurrentPageIndex.Value + 1) + " of " + m.PageCount + ". " : "";
                return prefix + FormatPage(m.CurrentPage.Value);
            }
            var pages = vm.Pages;
            if (pages == null || pages.Count == 0) return "";
            var parts = new List<string>();
            foreach (var p in pages) { var t = FormatPage(p); if (!string.IsNullOrEmpty(t)) parts.Add(t); }
            return string.Join(". ", parts.ToArray());
        }

        private static string FormatPage(TutorialData.Page page)
        {
            if (page == null) return "";
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(page.Title)) parts.Add(page.Title);
            if (!string.IsNullOrEmpty(page.TriggerText)) parts.Add(page.TriggerText);
            if (!string.IsNullOrEmpty(page.Description)) parts.Add(page.Description);
            if (!string.IsNullOrEmpty(page.SolutionText)) parts.Add(page.SolutionText);
            return string.Join(". ", parts.ToArray());
        }
    }
}
