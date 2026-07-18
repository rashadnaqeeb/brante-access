using System.Collections.Generic;
using System.Reflection;
using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using UnityEngine;
using InterludePopup = _Scripts.AMVCC.Views.Windows.Popup.InterludePopupController;
using TextBlock = _Scripts.AMVCC.Controllers.TextBlock;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The interlude popup (chapter openings, story intermissions) - the popup twin of the
    /// event-scene pager, spoken with the same transcript pattern: delivered pages are text
    /// rows, Enter on the newest advances through the game's NextPage, a new page re-homes
    /// silently and the queued delivery announcement is the speech. The close button appears
    /// on the last page (the game reveals it there). After close, some interludes swap the
    /// main panel for generated stat-change panels; those are spoken by a generic sweep of
    /// the popup's live visible texts and buttons - the rows are game-generated composites
    /// whose TMP text IS the game's own localized rendering.
    /// </summary>
    public sealed class InterludeScreen : Screen
    {
        public override string Key => "interlude";
        public override int Layer => 21;

        public override bool IsActive()
        {
            var p = Popup();
            return p != null && p.gameObject.activeInHierarchy;
        }

        public override Message ScreenName
        {
            get
            {
                var p = Popup();
                return p == null ? null : Message.MaybeRaw(p.TitleTMP.text);
            }
        }

        private static readonly FieldInfo PageIndexField = typeof(InterludePopup)
            .GetField("_pageIndex", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo TextBlockField = typeof(InterludePopup)
            .GetField("_textBlock", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo InsertNameMethod = typeof(InterludePopup)
            .GetMethod("InsertCharacterName", BindingFlags.NonPublic | BindingFlags.Instance);

        // Live component reference for delivery bookkeeping only; text re-reads at speech time.
        private InterludePopup _watched;
        private int _spokenPage;
        private string _spokenStatsSig;

        private static InterludePopup Popup() => Object.FindObjectOfType<InterludePopup>();

        private static int PageIndex(InterludePopup p) => (int)PageIndexField.GetValue(p);

        // Structural, not Referenced: all rows would share the popup component, and
        // reference-tier reconciliation snaps focus to the first node sharing a reference.
        private static ControlId PageId(int page) => ControlId.Structural("interlude:page:" + page);

        private static string PageText(InterludePopup p, int page)
        {
            var block = ((List<TextBlock>)TextBlockField.GetValue(p))[page];
            var text = (string)InsertNameMethod.Invoke(p, new object[] { block.SpeechByKey });
            if (block.Character != null)
                text = _Scripts.AMVCC.Models.Static.KeyChapterParametersController.Initiate
                    .GetCharacterTrueName(block.Character.Name) + ": " + text;
            return text;
        }

        public override void OnPop()
        {
            _watched = null;
            _spokenStatsSig = null;
        }

        public override void OnUpdate()
        {
            var p = Popup();
            if (p == null) return;
            if (!p.MainInterludePanel.activeSelf)
            {
                // Post-close stat panels: a new or swapped panel is delivered whole, once, off
                // the rendered content; focus re-seats silently on its first row (the page row
                // it sat on is gone from the graph - without this the re-seat would double-speak
                // the first row on top of the delivery).
                var sig = PanelSweep.JoinVisible(p.gameObject);
                if (sig.Length == 0 || sig == _spokenStatsSig) return;
                _spokenStatsSig = sig;
                Navigation.FocusNode(PanelSweep.FirstTextId(p.gameObject, "interlude"),
                    announce: false);
                Mod.Speech.Speak(sig);
                return;
            }
            _spokenStatsSig = null;
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
            Navigation.FocusNode(PageId(page), announce: false);
            Mod.Speech.Speak(PageText(p, page));
        }

        public override void Build(GraphBuilder b)
        {
            var p = Popup();
            if (p == null) return;

            if (p.MainInterludePanel.activeSelf)
            {
                int pages = PageIndex(p);
                b.PushContext("", role: null, positions: false);
                for (int i = 0; i <= pages; i++)
                {
                    int page = i;
                    bool newest = page == pages;
                    b.AddItem(PageId(page), new NodeVtable
                    {
                        ControlType = ControlTypes.Text,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(() => PageText(Popup(), page),
                                kind: AnnouncementKinds.Label),
                        },
                        // NextPage's own end-of-pages guard makes Enter safe on the last page.
                        OnActivate = !newest ? (System.Action)null : () => Popup().NextPage(),
                    });
                }
                b.SetStart(PageId(pages));
                b.PopContext();

                // The game reveals the close button on the last page only.
                if (p.ClosePopupButton != null && p.ClosePopupButton.activeSelf)
                {
                    var close = p.ClosePopupButton;
                    b.AddItem(ControlId.Referenced(
                        close.GetComponentInChildren<UnityEngine.UI.Button>(), "interlude:close"),
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

            // Post-close phase: game-generated stat panels (name, change, new value rows plus
            // a per-panel Next button) - swept live, the rendered text is the game's output.
            PanelSweep.Build(b, p.gameObject, "interlude");
        }
    }
}
