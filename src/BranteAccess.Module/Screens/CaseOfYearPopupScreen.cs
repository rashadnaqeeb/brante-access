using System.Collections.Generic;
using System.Reflection;
using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using UnityEngine;
using CasePopup = _Scripts.AMVCC.Views.Windows.CaseOfYearPopupController;
using TextBlock = _Scripts.AMVCC.Controllers.TextBlock;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The case-of-year story popup (yearly case beats), the interlude popup's twin, spoken
    /// with the same transcript pattern: delivered pages are text rows, Enter on the newest
    /// advances through the game's NextPage, a new page re-homes silently and the queued
    /// delivery announcement is the speech. Unlike the interlude the game's NextPage has no
    /// end guard, so the newest row only carries the advance action while pages remain. Page
    /// text mirrors the controller's own set exactly: a plain translation of the block key
    /// (this controller does no name substitution). After close, the popup swaps the case
    /// panel for generated stat panels - swept live like the interlude's, delivered once
    /// settled. An unconfigured popup (no blocks yet) is left to the generic popup sweep.
    /// </summary>
    public sealed class CaseOfYearPopupScreen : Screen
    {
        public override string Key => "caseofyear:popup";
        // The popup slot holds one popup, so the slot's dedicated screens never coexist.
        public override int Layer => 21;

        private static readonly FieldInfo PageIndexField = typeof(CasePopup)
            .GetField("_pageIndex", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo TextBlockField = typeof(CasePopup)
            .GetField("_textBlock", BindingFlags.NonPublic | BindingFlags.Instance);

        // Live component reference for delivery bookkeeping only; text re-reads at speech time.
        private CasePopup _watched;
        private int _spokenPage;
        private readonly SettledDelivery _stats = new SettledDelivery(0.45f);

        private static CasePopup Popup() => Object.FindObjectOfType<CasePopup>();

        private static int PageIndex(CasePopup p) => (int)PageIndexField.GetValue(p);

        private static List<TextBlock> Blocks(CasePopup p)
            => (List<TextBlock>)TextBlockField.GetValue(p);

        public override bool IsActive()
        {
            var p = Popup();
            return p != null && p.gameObject.activeInHierarchy && Blocks(p).Count > 0;
        }

        public override Message ScreenName
        {
            get
            {
                var p = Popup();
                return p == null ? null : Message.MaybeRaw(p.TitleTMP.text);
            }
        }

        // Structural (rows share the popup component) and qualified by popup instance, like
        // the interlude: back-to-back popups with no popup-free frame between them must not
        // reconcile onto a stale identical page key.
        private static ControlId PageId(CasePopup p, int page) =>
            ControlId.Structural("caseofyear:" + p.GetInstanceID() + ":page:" + page);

        // The controller's own rendering of a page, re-derived at speech time.
        private static string PageText(CasePopup p, int page)
            => I2.Loc.LocalizationManager.GetTranslation(Blocks(p)[page].SpeechByKey ?? "");

        public override void OnPop()
        {
            _watched = null;
            _stats.Reset();
        }

        public override void OnUpdate()
        {
            var p = Popup();
            if (p == null) return;
            if (!p.MainCasePanel.activeSelf)
            {
                // Post-close stat panels: a new or swapped panel is delivered whole, once,
                // off the rendered content, with a silent re-seat on its first row; growth
                // delivers only the new tail with focus left alone.
                var sig = PanelSweep.JoinVisible(p.gameObject);
                bool grew;
                var delivery = _stats.Poll(sig, out grew);
                if (delivery == null || delivery.Length == 0) return;
                if (!grew)
                    Navigation.FocusNode(PanelSweep.FirstTextId(p.gameObject, "caseofyear"),
                        announce: false);
                Mod.Speech.Speak(delivery);
                return;
            }
            _stats.Reset();
            int page = PageIndex(p);
            if (p != _watched)
            {
                // Screen entry: the navigator's focus-seat announcement reads the current row.
                _watched = p;
                _spokenPage = page;
                return;
            }
            if (page == _spokenPage) return;
            _spokenPage = page;
            Navigation.FocusNode(PageId(p, page), announce: false);
            Mod.Speech.Speak(PageText(p, page));
        }

        public override void Build(GraphBuilder b)
        {
            var p = Popup();
            if (p == null) return;

            if (p.MainCasePanel.activeSelf)
            {
                int pages = PageIndex(p);
                int last = Blocks(p).Count - 1;
                b.PushContext("", role: null, positions: false);
                for (int i = 0; i <= pages; i++)
                {
                    int page = i;
                    // The game's NextPage is UNGUARDED past the last block (it gates by
                    // disabling its next button) - mirrored by withholding the action on
                    // the last block; the close button below is the way on.
                    bool advances = page == pages && page < last;
                    b.AddItem(PageId(p, page), new NodeVtable
                    {
                        ControlType = ControlTypes.Text,
                        SilentRecovery = true,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(() => PageText(Popup(), page),
                                kind: AnnouncementKinds.Label),
                        },
                        OnActivate = !advances ? (System.Action)null : () => Popup().NextPage(),
                    });
                }
                b.SetStart(PageId(p, pages));
                b.PopContext();

                // The game reveals the close button on the last page only.
                if (p.ClosePopupButton != null && p.ClosePopupButton.activeSelf)
                {
                    var close = p.ClosePopupButton;
                    b.AddItem(ControlId.Referenced(
                        close.GetComponentInChildren<UnityEngine.UI.Button>(), "caseofyear:close"),
                        new NodeVtable
                        {
                            ControlType = ControlTypes.Button,
                            Announcements = new[]
                            {
                                new NodeAnnouncement(() => UiWidgets.LabelText(close),
                                    kind: AnnouncementKinds.Label),
                            },
                            OnActivate = () => UiWidgets.Click(
                                close.GetComponentInChildren<UnityEngine.UI.Button>().gameObject),
                        });
                }
                return;
            }

            // Post-close phase: game-generated stat panels, swept live.
            PanelSweep.Build(b, p.gameObject, "caseofyear");
        }
    }
}
