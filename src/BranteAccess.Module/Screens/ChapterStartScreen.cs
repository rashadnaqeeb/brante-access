using System.Reflection;
using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using I2.Loc;
using UnityEngine;
using CaseOfYearPanelType = _Scripts.AMVCC.Views.Windows.Components.CaseOfYearPanelType;
using ChapterStartWindow = _Scripts.AMVCC.Views.Windows.ChapterStart.ChapterStartWindowController;
using ObjectiveInitializer = _Scripts.AMVCC.Views.Windows.Destiny.ObjectiveInitializer;
using PanelType = _Scripts.AMVCC.Views.Windows.Components.PanelType;
using ParameterComponent = _Scripts.AMVCC.Views.Windows.ParameterComponent;
using ParameterGetSet = _Scripts.AMVCC.Views.Windows.ParameterGetSet;
using TextMeshProLocalization = _Scripts.Localization.TextMeshProLocalization;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The chapter start book (ChapterStartWindowController) as ONE flat list: every page's
    /// rows in reading order - the page's title row (title, position, description), then its
    /// objectives / parameters / section unlocks - ending in the begin-chapter button on the
    /// game's last page. The game's visible page FOLLOWS FOCUS: each page's rows carry a
    /// per-page region, and OnUpdate turns the game's own pager (Prev/Next clicks) until the
    /// shown page matches the focused row's page - arrowing straight down walks the whole
    /// book with no pager stops and no jumping back to the top; Ctrl+Up/Down jumps a page at
    /// a time (regions). Objectives fold their tooltip description onto the row (it only
    /// exists on hover for sighted players), with the requirement rows on Space; parameters
    /// fold name, value and segment, with the scale breakdown on Space. Rows on
    /// not-yet-shown pages read model fields, which exist while their panel is inactive.
    /// </summary>
    public sealed class ChapterStartScreen : Screen
    {
        public override string Key => "chapterstart";
        public override int Layer => 10;

        public override bool IsActive()
        {
            var w = Window();
            return w != null && w.gameObject.activeInHierarchy;
        }

        public override Message ScreenName
        {
            get
            {
                var w = Window();
                // Localize-aware: the freshly instantiated window still renders its
                // serialized Russian title the beat it appears (heard live at chapter V).
                return w == null ? null
                    : Message.MaybeRaw(UiWidgets.LocalizedLabel(w.Title.gameObject));
            }
        }

        private static readonly FieldInfo CurrentPageField = typeof(ChapterStartWindow)
            .GetField("_currentPage", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo PanelsField = typeof(ChapterStartWindow)
            .GetField("_panels", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo BlockPagesField = typeof(ChapterStartWindow)
            .GetField("_blockPages", BindingFlags.NonPublic | BindingFlags.Instance);
        // The serialized per-window localization keys SetPageText reads for the three legacy
        // panel types (the newer types derive their keys from the PanelType name).
        private static readonly FieldInfo FirstTitleField = typeof(ChapterStartWindow)
            .GetField("_firstPageTitle", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo SecondTitleField = typeof(ChapterStartWindow)
            .GetField("_secondPageTitle", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo ThirdTitleField = typeof(ChapterStartWindow)
            .GetField("_thirdPageTitle", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo FirstDescField = typeof(ChapterStartWindow)
            .GetField("_firstPageDescription", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo SecondDescField = typeof(ChapterStartWindow)
            .GetField("_secondPageDescription", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo ThirdDescField = typeof(ChapterStartWindow)
            .GetField("_thirdPageDescription", BindingFlags.NonPublic | BindingFlags.Instance);

        private static ChapterStartWindow Window() => Object.FindObjectOfType<ChapterStartWindow>();

        private static int CurrentPage(ChapterStartWindow w) => (int)CurrentPageField.GetValue(w);
        private static System.Collections.Generic.List<GameObject> Panels(ChapterStartWindow w)
            => (System.Collections.Generic.List<GameObject>)PanelsField.GetValue(w);
        private static bool Blocked(ChapterStartWindow w) => (bool)BlockPagesField.GetValue(w);

        private const string RegionPrefix = "chapterstart:page:";

        public override void OnUpdate()
        {
            var w = Window();
            if (w == null) return;
            var region = Navigation.FocusedRegionKey as string;
            if (region == null || !region.StartsWith(RegionPrefix)) return;
            SyncPage(w, int.Parse(region.Substring(RegionPrefix.Length)));
        }

        // Turn the game's own pager until the shown page is the focused row's page - the game's
        // click handlers run so panels, dots and tooltips stay coherent. The window's pre-game
        // gate wins: retried every frame, the sync lands the moment the intro releases the book.
        private static void SyncPage(ChapterStartWindow w, int target)
        {
            if (Blocked(w)) return;
            int guard = Panels(w).Count;
            while (CurrentPage(w) < target && guard-- > 0) w.NextButton_Click();
            while (CurrentPage(w) > target && guard-- > 0) w.PreviousButton_Click();
        }

        public override void Build(GraphBuilder b)
        {
            var w = Window();
            if (w == null) return;
            var panels = Panels(w);

            b.PushContext("", role: null, positions: false);

            if (!string.IsNullOrEmpty(w.ShortDescription.text))
                b.AddItem(ControlId.Referenced(w.ShortDescription, "chapterstart:desc"),
                    TextRow(() => Window().ShortDescription.text));

            for (int i = 0; i < panels.Count; i++)
            {
                int page = i;
                var panel = panels[i];
                b.SetRegion(RegionPrefix + i);
                if (TitleKey(w, panel) != null)
                {
                    var titleId = ControlId.Structural("chapterstart:pagetitle:" + i);
                    b.AddItem(titleId, TextRow(() => PageAnnouncement(Window(), page)));
                    if (i == 0) b.SetStart(titleId);
                }
                BuildPanelRows(b, panel, page);
            }
            b.SetRegion(null);
            b.PopContext();
        }

        // The page's section row: title, position in the book, and the game's page description
        // (which the game hides on its last page - mirrored).
        private static string PageAnnouncement(ChapterStartWindow w, int page)
        {
            var panels = Panels(w);
            var text = LocalizationManager.GetTranslation(TitleKey(w, panels[page]));
            text += ", " + Loc.T("nav.position", new { index = page + 1, count = panels.Count });
            if (page < panels.Count - 1)
            {
                var desc = LocalizationManager.GetTranslation(DescriptionKey(w, panels[page]));
                if (!string.IsNullOrEmpty(desc)) text += ", " + desc;
            }
            return text;
        }

        private static string TitleKey(ChapterStartWindow w, GameObject panel)
        {
            var type = panel.GetComponent<CaseOfYearPanelType>().PanelType;
            switch (type)
            {
                case PanelType.ObjectivePanel: return (string)FirstTitleField.GetValue(w);
                case PanelType.ParameterPanel: return (string)SecondTitleField.GetValue(w);
                case PanelType.NewFeaturesPanel: return (string)ThirdTitleField.GetValue(w);
                case PanelType.NextButtonPanel: return null; // the begin page has no header
                default: return type + ".Title";
            }
        }

        private static string DescriptionKey(ChapterStartWindow w, GameObject panel)
        {
            var type = panel.GetComponent<CaseOfYearPanelType>().PanelType;
            switch (type)
            {
                case PanelType.ObjectivePanel: return (string)FirstDescField.GetValue(w);
                case PanelType.ParameterPanel: return (string)SecondDescField.GetValue(w);
                case PanelType.NewFeaturesPanel: return (string)ThirdDescField.GetValue(w);
                case PanelType.NextButtonPanel: return null;
                default: return type + ".Description";
            }
        }

        // A page's content rows, read from components that exist while the panel is inactive.
        // Objectives, parameters and section unlocks are folded model rows; any other panel
        // (the begin page) sweeps its would-be-visible texts and buttons.
        private void BuildPanelRows(GraphBuilder b, GameObject panel, int page)
        {
            bool any = false;
            foreach (var obj in panel.GetComponentsInChildren<ObjectiveInitializer>(true))
            {
                if (!WouldShow(panel, obj.transform)) continue;
                any = true;
                var o = obj;
                b.AddItem(ControlId.Referenced(o, "chapterstart:objective:" + o.GetInstanceID()),
                    new NodeVtable
                    {
                        ControlType = ControlTypes.Text,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(
                                () => o.ObjectiveName.text + ". " + o.ObjectiveDescription,
                                kind: AnnouncementKinds.Label),
                        },
                        SearchText = () => o.ObjectiveName.text,
                        OnTooltip = () => Mod.Speech.Speak(Readouts.ObjectiveDetails(o)),
                    });
            }
            if (any) return;
            foreach (var par in panel.GetComponentsInChildren<ParameterComponent>(true))
            {
                if (!WouldShow(panel, par.transform)) continue;
                any = true;
                var pc = par;
                var pgs = pc.GetComponent<ParameterGetSet>();
                b.AddItem(ControlId.Referenced(pc, "chapterstart:parameter:" + pc.GetInstanceID()),
                    new NodeVtable
                    {
                        ControlType = ControlTypes.Text,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(() => PanelSweep.ParameterLabel(pc),
                                kind: AnnouncementKinds.Label),
                        },
                        OnTooltip = pgs == null ? (System.Action)null
                            : () => Mod.Speech.Speak(Readouts.ParameterScales(pgs.Parameter)),
                    });
            }
            if (any) return;
            // A New Sections row is the unlock text plus an image-only icon that opens the
            // unlocked window (HudController wiring on the icon's Button) - one button row per
            // unlock, labeled by the game's own unlock text.
            foreach (var unlock in panel.GetComponentsInChildren<UnlockedItemBehaviour>(true))
            {
                if (!WouldShow(panel, unlock.transform)) continue;
                any = true;
                var u = unlock;
                var icon = u.GetComponentInChildren<UnityEngine.UI.Button>(true);
                Button(b, page, "chapterstart:unlock:" + u.GetInstanceID(), u,
                    () => u.GetComponentInChildren<TMPro.TMP_Text>(true).text, icon.gameObject);
            }
            if (any) return;
            foreach (var tmp in panel.GetComponentsInChildren<TMPro.TMP_Text>(true))
                SweptText(b, panel, tmp, tmp.text);
            foreach (var legacy in panel.GetComponentsInChildren<UnityEngine.UI.Text>(true))
                SweptText(b, panel, legacy, legacy.text);
            foreach (var btn in panel.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            {
                if (!WouldShow(panel, btn.transform)) continue;
                var bt = btn;
                var pn = panel;
                Button(b, page, "chapterstart:btn:" + bt.GetInstanceID(), bt,
                    () => SweptButtonLabel(pn, bt), bt.gameObject);
            }
        }

        private static void SweptText(GraphBuilder b, GameObject panel, Component text, string value)
        {
            if (!WouldShow(panel, text.transform)) return;
            if (string.IsNullOrEmpty(value) || value.Trim().Length == 0) return;
            if (UnderButton(panel, text.transform)) return; // the button speaks it
            var t = text;
            b.AddItem(ControlId.Referenced(t, "chapterstart:text:" + t.GetInstanceID()),
                TextRow(() => Readouts.DashAsNone(LocalizedText(t))));
        }

        // GetComponentInParent skips inactive objects on this Unity, so the button-ancestor
        // check walks transforms explicitly (the begin panel is inactive until its page shows).
        private static bool UnderButton(GameObject panel, Transform t)
        {
            for (; t != null; t = t.parent)
            {
                if (t.GetComponent<UnityEngine.UI.Button>() != null) return true;
                if (t.gameObject == panel) return false;
            }
            return false;
        }

        // Shown-when-its-page-shows: every ancestor below the panel root is individually active
        // (the panel root itself toggles with the game's page flips, so it is exempt).
        private static bool WouldShow(GameObject panel, Transform t)
        {
            for (; t != null && t.gameObject != panel; t = t.parent)
                if (!t.gameObject.activeSelf) return false;
            return t != null;
        }

        // Localize-aware text read that works on inactive objects (UiWidgets.LocalizedLabel's
        // child queries skip inactive children): the component's own I2 binding wins, else the
        // rendered text.
        private static string LocalizedText(Component text)
        {
            var tl = text.GetComponent<TextMeshProLocalization>();
            if (tl != null)
            {
                var s = tl.ItsKeysCombination
                    ? LocalizationManager.GetTranslation(tl.Keys[0]) + " "
                        + LocalizationManager.GetTranslation(tl.Keys[1])
                    : LocalizationManager.GetTranslation(tl.Key);
                if (!string.IsNullOrEmpty(s) && s.Trim().Length > 0) return s;
            }
            var tmp = text as TMPro.TMP_Text;
            return tmp != null ? tmp.text : ((UnityEngine.UI.Text)text).text;
        }

        private static string SweptButtonLabel(GameObject panel, UnityEngine.UI.Button btn)
        {
            foreach (var t in btn.GetComponentsInChildren<TMPro.TMP_Text>(true))
                if (WouldShow(panel, t.transform) && t.text.Trim().Length > 0)
                    return LocalizedText(t);
            foreach (var t in btn.GetComponentsInChildren<UnityEngine.UI.Text>(true))
                if (WouldShow(panel, t.transform) && t.text.Trim().Length > 0)
                    return LocalizedText(t);
            Mod.Log("[chapterstart] unlabeled button swept: " + btn.gameObject.name);
            return null;
        }

        // A button row whose click target lives on a page: activation first lands the game on
        // that page (the click path needs the panel active and raycastable), then rides the
        // game's own pointer-click path. The unavailable state only speaks once the row's page
        // is the shown one - an inactive panel would misreport every row as unavailable.
        private static void Button(GraphBuilder b, int page, string id, Component keyRef,
            System.Func<string> label, GameObject target)
        {
            b.AddItem(ControlId.Referenced(keyRef, id), new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new[]
                {
                    new NodeAnnouncement(label, kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(
                        () => OnShownPage(page) && !UiWidgets.Interactable(target)
                            ? Loc.T("state.unavailable") : null,
                        kind: AnnouncementKinds.Enabled),
                },
                OnActivate = () =>
                {
                    var w = Window();
                    SyncPage(w, page);
                    if (!UiWidgets.Interactable(target))
                    {
                        Mod.Speech.Speak(Loc.T("state.unavailable"), interrupt: true);
                        return;
                    }
                    UiWidgets.Click(target);
                },
            });
        }

        private static bool OnShownPage(int page)
        {
            var w = Window();
            return w != null && CurrentPage(w) == page;
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
