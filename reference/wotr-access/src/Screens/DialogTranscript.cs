using System;
using Kingmaker;                            // Game
using Kingmaker.UI.MVVM._VM.Dialog;         // DialogContextVM
using Kingmaker.UI.MVVM._VM.Dialog.Dialog;  // AnswerVM
using Kingmaker.Utility;                    // UIConsts.GetAnswerString
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Shared pieces of the conversation surfaces (ordinary dialogue + book events/interchapters):
    /// the live dialog context lookup and the player-answer node, so both screens read and behave
    /// identically.
    /// </summary>
    internal static class DialogTranscript
    {
        /// <summary>The active dialog context — the SAME <see cref="DialogContextVM"/> (holding DialogVM /
        /// BookEventVM / InterchapterVM) whether the conversation runs in an area (on <c>InGameVM</c>) or on
        /// the world map (on <c>GlobalMapVM</c>, which carries its own context). The dialogue + book-event
        /// screens read from whichever is live, so they work in both places with no other change.</summary>
        public static DialogContextVM Context()
        {
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            if (rc == null) return null;
            return rc.InGameVM?.StaticPartVM?.DialogContextVM ?? rc.GlobalMapVM?.DialogContextVM;
        }

        /// <summary>One player answer: the game's own answer formatter (numbered prefix plus skill-check /
        /// alignment / mythic tags, gated by the same dialogue settings, so we surface only what's drawn);
        /// Enter chooses via <see cref="AnswerVM.OnChooseAnswer"/> (the game plays NextDialogLine — no
        /// click of ours); Space resolves the per-stat DC-preview links (glossary falls through). Disabled
        /// while the dialogue window is hidden (a cutscene transition must not let Enter choose through a
        /// hidden window).</summary>
        public static NodeVtable AnswerNode(AnswerVM vm)
        {
            Func<bool> enabled = () => vm != null && vm.Enable.Value && WrathAccess.DialogVisibility.Shown;
            return new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new[]
                {
                    GraphNodes.LabelPart(() => AnswerText(vm)),
                    GraphNodes.DisabledPart(enabled),
                },
                SearchText = () => AnswerText(vm),
                OnActivate = () => { if (enabled()) vm.OnChooseAnswer(); },
                OnTooltip = () => TooltipScreen.FollowLinks(AnswerText(vm),
                    (id, keys) => WrathAccess.UI.Proxies.DialogLinks.ResolveSkillCheck(
                        keys, null, vm?.Answer?.Value?.SkillChecksDC)),
            };
        }

        // The bind name matches the view's own (DialogAnswerView builds "DialogChoice{Index}"). Returns
        // TMP rich text (<link>-wrapped checks); Tts strips that at speak time. Plain fallback for
        // system answers — the game's button reads an UNNUMBERED "Continue".
        public static string AnswerText(AnswerVM vm)
        {
            if (vm == null) return "";
            var bp = vm.Answer?.Value;
            var text = bp != null ? bp.DisplayText : null;
            if (string.IsNullOrEmpty(text)) return Loc.T("label.continue");
            try { return UIConsts.GetAnswerString(bp, "DialogChoice" + vm.Index, vm.Index); }
            catch { return vm.Index + ". " + text; }
        }
    }
}
