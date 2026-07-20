using System.Collections.Generic;
using System.Reflection;
using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using UnityEngine;
using ConvPanel = _Scripts.AMVCC.Views.Windows.Popup.ParametersConvertationPanelComponent;
using InterludePopup = _Scripts.AMVCC.Views.Windows.Popup.InterludePopupController;
using ParameterComponent = _Scripts.AMVCC.Views.Windows.ParameterComponent;
using ParameterGetSet = _Scripts.AMVCC.Views.Windows.ParameterGetSet;
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
    /// whose TMP text IS the game's own localized rendering. The chapter-transition
    /// CONVERSION book (ParametersConvertationPanelComponent) instead builds as ONE flat
    /// list on the chapter books' pattern: title and description, then every page's folded
    /// parameter rows behind per-page regions, the game's visible page following focus
    /// through its own pager clicks, the pager arrows dropped.
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
        private static readonly FieldInfo ConvPageField = typeof(ConvPanel)
            .GetField("_currentPage", BindingFlags.NonPublic | BindingFlags.Instance);

        // Live component reference for delivery bookkeeping only; text re-reads at speech time.
        private InterludePopup _watched;
        private int _spokenPage;
        // The conversion book instance whose arrival was already delivered - its focus-driven
        // page flips change the visible join, and only the arrival may speak.
        private int _deliveredConv;

        // Post-close panels build over several frames: the conversion panel activates with
        // prefab placeholder text one frame before its Start localizes it, and stat rows
        // populate one by one while values animate. The panel is delivered once its content
        // has held still this long, so the placeholder frame and half-filled states are
        // never spoken.
        private readonly SettledDelivery _stats = new SettledDelivery(0.45f);

        private static InterludePopup Popup() => Object.FindObjectOfType<InterludePopup>();

        private static int PageIndex(InterludePopup p) => (int)PageIndexField.GetValue(p);

        // Structural, not Referenced: all rows would share the popup component, and
        // reference-tier reconciliation snaps focus to the first node sharing a reference.
        // Qualified by popup instance: when one interlude scene follows another with no
        // popup-free frame between them, the screen stays focused and the navigator's differ
        // never resets - a bare page index would re-seat on an identical key and swallow the
        // new interlude entirely (heard live as a silent GameOver entry).
        private static ControlId PageId(InterludePopup p, int page) =>
            ControlId.Structural("interlude:" + p.GetInstanceID() + ":page:" + page);

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
            _deliveredConv = 0;
            _stats.Reset();
        }

        public override void OnUpdate()
        {
            var p = Popup();
            if (p == null) return;
            if (!p.MainInterludePanel.activeSelf)
            {
                // Post-close phase. The conversion book's visible page follows focus through
                // the game's own pager clicks (the row regions carry the page).
                var conv = p.GetComponentInChildren<ConvPanel>();
                if (conv != null)
                {
                    var region = Navigation.FocusedRegionKey as string;
                    if (region != null && region.StartsWith(ConvRegionPrefix))
                        SyncConvPage(conv, int.Parse(region.Substring(ConvRegionPrefix.Length)));
                }
                else
                    _deliveredConv = 0;

                // A new or swapped panel is delivered whole, once, off the rendered content;
                // focus re-seats silently on its first row (the page row it sat on is gone
                // from the graph - without this the re-seat would double-speak the first row
                // on top of the delivery). When the panel merely GREW, only the new tail is
                // spoken and focus stays put.
                var sig = PanelSweep.JoinVisible(p.gameObject);
                bool grew;
                var delivery = _stats.Poll(sig, out grew);
                if (delivery == null || delivery.Length == 0) return;
                if (conv != null)
                {
                    // The conversion book's rows speak themselves under navigation - only
                    // its ARRIVAL is delivered, re-seated like any swapped panel.
                    if (_deliveredConv == conv.GetInstanceID()) return;
                    _deliveredConv = conv.GetInstanceID();
                    Navigation.FocusNode(FirstConvId(conv), announce: false);
                    Mod.Speech.Speak(delivery);
                    return;
                }
                if (!grew)
                    Navigation.FocusNode(PanelSweep.FirstTextId(p.gameObject, "interlude"),
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

            if (p.MainInterludePanel.activeSelf)
            {
                int pages = PageIndex(p);
                b.PushContext("", role: null, positions: false);
                for (int i = 0; i <= pages; i++)
                {
                    int page = i;
                    bool newest = page == pages;
                    b.AddItem(PageId(p, page), new NodeVtable
                    {
                        ControlType = ControlTypes.Text,
                        SilentRecovery = true,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(() => PageText(Popup(), page),
                                kind: AnnouncementKinds.Label),
                        },
                        // NextPage's own end-of-pages guard makes Enter safe on the last page.
                        OnActivate = !newest ? (System.Action)null : () => Popup().NextPage(),
                    });
                }
                b.SetStart(PageId(p, pages));
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

            // Post-close phase: the conversion book builds flat; any other content is
            // game-generated stat panels (name, change, new value rows plus a per-panel Next
            // button) - swept live, the rendered text is the game's output.
            var conv = p.GetComponentInChildren<ConvPanel>();
            if (conv != null)
            {
                BuildConversion(b, conv);
                return;
            }
            PanelSweep.Build(b, p.gameObject, "interlude");
        }

        private const string ConvRegionPrefix = "interlude:conv:page:";

        private static int ConvPage(ConvPanel conv) => (int)ConvPageField.GetValue(conv);

        // Turn the game's own pager until the shown page is the focused row's page - the
        // click handlers run so panels, dots and sounds stay coherent, and their own edge
        // guards stop at the ends.
        private static void SyncConvPage(ConvPanel conv, int target)
        {
            int guard = conv.Panels.Count;
            while (ConvPage(conv) < target && guard-- > 0) conv.NextButton_Click();
            while (ConvPage(conv) > target && guard-- > 0) conv.PreviousButton_Click();
        }

        // The arrival re-seat target: the same first row BuildConversion creates.
        private static ControlId FirstConvId(ConvPanel conv)
        {
            if (!string.IsNullOrEmpty(conv.Title.text))
                return ControlId.Structural("interlude:conv:title");
            if (!string.IsNullOrEmpty(conv.Description.text))
                return ControlId.Structural("interlude:conv:desc");
            return ControlId.Structural("interlude:conv:pos:0");
        }

        // The conversion book as one flat list (the chapter books' pattern): title and
        // description rows, then every page's rows behind per-page regions - pages carry no
        // game titles here, so each page's section row is its position alone - with buttons
        // outside the pager (the panel's continue/close family) as rows at the bottom and
        // the pager arrows dropped (paging follows focus). Rows on not-yet-shown pages read
        // components the panel's own Start configured for all eras.
        private void BuildConversion(GraphBuilder b, ConvPanel conv)
        {
            b.PushContext("", role: null, positions: false);
            if (!string.IsNullOrEmpty(conv.Title.text))
                b.AddItem(ControlId.Structural("interlude:conv:title"),
                    TextRow(() => conv.Title.text));
            if (!string.IsNullOrEmpty(conv.Description.text))
                b.AddItem(ControlId.Structural("interlude:conv:desc"),
                    TextRow(() => conv.Description.text));

            var panels = conv.Panels;
            for (int i = 0; i < panels.Count; i++)
            {
                int page = i;
                b.SetRegion(ConvRegionPrefix + i);
                b.AddItem(ControlId.Structural("interlude:conv:pos:" + i),
                    TextRow(() => Loc.T("nav.position",
                        new { index = page + 1, count = conv.Panels.Count })));
                BuildConvPanelRows(b, conv, panels[i], page);
            }
            b.SetRegion(null);
            b.PopContext();

            foreach (var btn in conv.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            {
                if (btn == conv.NextButton || btn == conv.PreviousButton) continue;
                if (UnderAnyPanel(panels, btn.transform)) continue;
                if (!UiWidgets.Visible(btn.gameObject)) continue;
                // The popup prefabs' full-screen click-away backdrop would only add a bare
                // unlabeled stop (same rule as the generic sweep).
                if (btn.gameObject.name == "back"
                    && string.IsNullOrEmpty(UiWidgets.LabelText(btn.gameObject))) continue;
                PanelSweep.AddButton(b, btn, "interlude");
            }
        }

        // One page's rows: parameters fold name, value and segment with the scale breakdown
        // on Space; texts that are not part of a stat trio (and not a button label) sweep as
        // plain rows so no page content is dropped; a button on a page activates through the
        // game's own click path after landing its page.
        private void BuildConvPanelRows(GraphBuilder b, ConvPanel conv, GameObject panel, int page)
        {
            var folded = new HashSet<Component>();
            foreach (var par in panel.GetComponentsInChildren<ParameterComponent>(true))
            {
                if (!WouldShow(panel, par.transform)) continue;
                if (par.Name != null) folded.Add(par.Name);
                if (par.TextValue != null) folded.Add(par.TextValue);
                if (par.Descr != null) folded.Add(par.Descr);
                var pc = par;
                var scales = pc.GetComponent<ParameterGetSet>();
                b.AddItem(ControlId.Referenced(pc, "interlude:conv:param:" + pc.GetInstanceID()),
                    new NodeVtable
                    {
                        ControlType = ControlTypes.Text,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(() => PanelSweep.ParameterLabel(pc),
                                kind: AnnouncementKinds.Label),
                        },
                        OnTooltip = scales == null ? (System.Action)null
                            : () => Mod.Speech.Speak(Readouts.ParameterScales(scales.Parameter)),
                    });
            }
            foreach (var tmp in panel.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                if (folded.Contains(tmp)) continue;
                if (!WouldShow(panel, tmp.transform)) continue;
                if (string.IsNullOrEmpty(tmp.text) || tmp.text.Trim().Length == 0) continue;
                if (UnderButton(panel, tmp.transform)) continue;
                var t = tmp;
                b.AddItem(ControlId.Referenced(t, "interlude:conv:text:" + t.GetInstanceID()),
                    TextRow(() => Readouts.DashAsNone(t.text)));
            }
            foreach (var btn in panel.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            {
                if (!WouldShow(panel, btn.transform)) continue;
                var bt = btn;
                b.AddItem(ControlId.Referenced(bt, "interlude:conv:btn:" + bt.GetInstanceID()),
                    new NodeVtable
                    {
                        ControlType = ControlTypes.Button,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(() => ConvButtonLabel(bt),
                                kind: AnnouncementKinds.Label),
                            new NodeAnnouncement(
                                () => ConvPage(conv) == page
                                    && !UiWidgets.Interactable(bt.gameObject)
                                    ? Loc.T("state.unavailable") : null,
                                kind: AnnouncementKinds.Enabled),
                        },
                        OnActivate = () =>
                        {
                            SyncConvPage(conv, page);
                            if (!UiWidgets.Interactable(bt.gameObject))
                            {
                                Mod.Speech.Speak(Loc.T("state.unavailable"), interrupt: true);
                                return;
                            }
                            UiWidgets.Click(bt.gameObject);
                        },
                    });
            }
        }

        // Label read that works on inactive pages (UiWidgets' child queries skip inactive).
        private static string ConvButtonLabel(UnityEngine.UI.Button btn)
        {
            var tmp = btn.GetComponentInChildren<TMPro.TMP_Text>(true);
            if (tmp != null && tmp.text.Trim().Length > 0) return tmp.text;
            var legacy = btn.GetComponentInChildren<UnityEngine.UI.Text>(true);
            if (legacy != null && legacy.text.Trim().Length > 0) return legacy.text;
            Mod.Log("[interlude] unlabeled conversion-page button: " + btn.gameObject.name);
            return null;
        }

        private static bool UnderAnyPanel(List<GameObject> panels, Transform t)
        {
            foreach (var panel in panels)
                if (WouldShow(panel, t)) return true;
            return false;
        }

        // GetComponentInParent skips inactive objects on this Unity, so the button-ancestor
        // check walks transforms explicitly (pages are inactive until shown).
        private static bool UnderButton(GameObject panel, Transform t)
        {
            for (; t != null; t = t.parent)
            {
                if (t.GetComponent<UnityEngine.UI.Button>() != null) return true;
                if (t.gameObject == panel) return false;
            }
            return false;
        }

        // Shown-when-its-page-shows: every ancestor below the panel root is individually
        // active (the panel root itself toggles with the game's page flips, so it is exempt).
        private static bool WouldShow(GameObject panel, Transform t)
        {
            for (; t != null && t.gameObject != panel; t = t.parent)
                if (!t.gameObject.activeSelf) return false;
            return t != null;
        }

        private static NodeVtable TextRow(System.Func<string> text) => new NodeVtable
        {
            ControlType = ControlTypes.Text,
            Announcements = new[]
            {
                new NodeAnnouncement(text, kind: AnnouncementKinds.Label),
            },
        };
    }
}
