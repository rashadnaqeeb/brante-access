using System.Collections.Generic;
using System.Reflection;
using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using I2.Loc;
using UnityEngine;
using CaseOfYearPanelType = _Scripts.AMVCC.Views.Windows.Components.CaseOfYearPanelType;
using ChapterFinalWindow = _Scripts.AMVCC.Views.Windows.ChapterFinal.ChapterFinalWindowController;
using ObjectiveInitializer = _Scripts.AMVCC.Views.Windows.Destiny.ObjectiveInitializer;
using PanelType = _Scripts.AMVCC.Views.Windows.Components.PanelType;
using ParameterComponent = _Scripts.AMVCC.Views.Windows.ParameterComponent;
using ParameterGetSet = _Scripts.AMVCC.Views.Windows.ParameterGetSet;
using TimelineItem = _Scripts.AMVCC.Views.Windows.Destiny.TimelineComponent;
using TimelineYear = _Scripts.AMVCC.Views.Windows.Destiny.TimelineYearComponent;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The end-of-chapter book (ChapterFinalWindowController, a scene prefab) as ONE flat
    /// list: the chapter epilogue text, then every page's rows in reading order - the page's
    /// title row (title, position, description), then its content - ending in the continue
    /// button that starts the next chapter. The game's visible page FOLLOWS FOCUS: each
    /// page's rows carry a per-page region, and OnUpdate turns the game's own pager
    /// (Prev/Next clicks) until the shown page matches the focused row's page; Ctrl+Up/Down
    /// jumps a page at a time (regions). Timeline rows fold the event's branch and year on
    /// (the year headers between rows carry the year for every event under them); objective
    /// rows fold the description, with the requirement rows on Space; parameter rows fold
    /// name, value and segment, with the scale breakdown on Space. Rows on not-yet-shown
    /// pages read model fields, which exist while their panel is inactive.
    /// </summary>
    public sealed class ChapterFinalScreen : Screen
    {
        public override string Key => "chapterfinal";
        public override int Layer => 10;

        // The controller GameObject outlives its readable content on both ends: pages stay
        // inactive until the pregame gate lifts, and the hide animation fades them to zero
        // while the next chapter loads behind. The left page's own visibility is the gate.
        public override bool IsActive()
        {
            var w = Window();
            if (w == null || !w.gameObject.activeInHierarchy) return false;
            var left = (GameObject)LeftPageField.GetValue(w);
            if (!left.activeInHierarchy) return false;
            var fade = left.GetComponent<CanvasGroup>();
            return fade == null || fade.alpha > 0;
        }

        public override Message ScreenName
        {
            get
            {
                var w = Window();
                return w == null ? null : Message.MaybeRaw(w.Title.text);
            }
        }

        private static readonly FieldInfo CurrentPageField = typeof(ChapterFinalWindow)
            .GetField("_currentPage", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo PanelsField = typeof(ChapterFinalWindow)
            .GetField("_panels", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo BlockPagesField = typeof(ChapterFinalWindow)
            .GetField("_blockPages", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo LeftPageField = typeof(ChapterFinalWindow)
            .GetField("_leftPage", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo PageCountField = typeof(ChapterFinalWindow)
            .GetField("_pageCount", BindingFlags.NonPublic | BindingFlags.Instance);
        // The serialized per-window localization keys SetPageText reads for the three legacy
        // panel types (the newer types derive their keys from the PanelType name).
        private static readonly FieldInfo FirstTitleField = typeof(ChapterFinalWindow)
            .GetField("_firstPageTitle", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo SecondTitleField = typeof(ChapterFinalWindow)
            .GetField("_secondPageTitle", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo ThirdTitleField = typeof(ChapterFinalWindow)
            .GetField("_thirdPageTitle", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo FirstDescField = typeof(ChapterFinalWindow)
            .GetField("_firstPageDescription", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo SecondDescField = typeof(ChapterFinalWindow)
            .GetField("_secondPageDescription", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo ThirdDescField = typeof(ChapterFinalWindow)
            .GetField("_thirdPageDescription", BindingFlags.NonPublic | BindingFlags.Instance);

        private static ChapterFinalWindow Window() => Object.FindObjectOfType<ChapterFinalWindow>();

        private static int CurrentPage(ChapterFinalWindow w) => (int)CurrentPageField.GetValue(w);
        private static List<GameObject> Panels(ChapterFinalWindow w)
            => (List<GameObject>)PanelsField.GetValue(w);
        private static bool Blocked(ChapterFinalWindow w) => (bool)BlockPagesField.GetValue(w);

        private const string RegionPrefix = "chapterfinal:page:";

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
        // NextButton_Click caps at the controller's hardcoded _pageCount of 4 while the panel
        // list can grow past it (per-category objective pages); UpdateButtons already treats
        // the panel list as the truth, so the cap is lifted to match before paging.
        private static void SyncPage(ChapterFinalWindow w, int target)
        {
            if (Blocked(w)) return;
            var panels = Panels(w);
            if ((int)PageCountField.GetValue(w) < panels.Count)
                PageCountField.SetValue(w, panels.Count);
            int guard = panels.Count;
            while (CurrentPage(w) < target && guard-- > 0) w.NextButton_Click();
            while (CurrentPage(w) > target && guard-- > 0) w.PreviousButton_Click();
        }

        public override void Build(GraphBuilder b)
        {
            var w = Window();
            if (w == null) return;
            var panels = Panels(w);

            b.PushContext("", role: null, positions: false);

            if (!string.IsNullOrEmpty(w.Subtitle.text))
                b.AddItem(ControlId.Referenced(w.Subtitle, "chapterfinal:subtitle"),
                    TextRow(() => Window().Subtitle.text));
            if (!string.IsNullOrEmpty(w.ShortDescription.text))
                b.AddItem(ControlId.Referenced(w.ShortDescription, "chapterfinal:desc"),
                    TextRow(() => Window().ShortDescription.text));
            // The prefab carries the epilogue in two texts; they duplicate in the shipped
            // chapters, so the second is a row only when it actually says something new.
            if (!string.IsNullOrEmpty(w.Text.text) && w.Text.text != w.ShortDescription.text)
                b.AddItem(ControlId.Referenced(w.Text, "chapterfinal:text"),
                    TextRow(() => Window().Text.text));

            for (int i = 0; i < panels.Count; i++)
            {
                int page = i;
                b.SetRegion(RegionPrefix + i);
                if (!string.IsNullOrEmpty(TitleKey(w, panels[i])))
                {
                    var titleId = ControlId.Structural("chapterfinal:pagetitle:" + i);
                    b.AddItem(titleId, TextRow(() => PageAnnouncement(Window(), page)));
                    if (i == 0) b.SetStart(titleId);
                }
                BuildPanelRows(b, panels[i], page);
            }
            b.SetRegion(null);
            b.PopContext();
        }

        // The page's section row: title, position in the book, and the game's page description
        // (which the game blanks on its last page via the container's alpha - mirrored).
        private static string PageAnnouncement(ChapterFinalWindow w, int page)
        {
            var panels = Panels(w);
            var text = LocalizationManager.GetTranslation(TitleKey(w, panels[page]));
            text += ", " + Loc.T("nav.position", new { index = page + 1, count = panels.Count });
            if (page < panels.Count - 1)
            {
                var key = DescriptionKey(w, panels[page]);
                var desc = key == null ? null : LocalizationManager.GetTranslation(key);
                if (!string.IsNullOrEmpty(desc)) text += ", " + desc;
            }
            return text;
        }

        // SetPageText's key sources per panel type. The game swaps the title and description
        // keys for the per-category objective types; the localization data itself is straight
        // ("ObjectivePanelPerson.Title" holds the title), so the keys are read unswapped here.
        private static string TitleKey(ChapterFinalWindow w, GameObject panel)
        {
            var type = panel.GetComponent<CaseOfYearPanelType>().PanelType;
            switch (type)
            {
                case PanelType.ObjectivePanel: return (string)FirstTitleField.GetValue(w);
                case PanelType.ParameterPanel: return (string)SecondTitleField.GetValue(w);
                case PanelType.NewFeaturesPanel: return (string)ThirdTitleField.GetValue(w);
                case PanelType.NextButtonPanel: return null; // the continue page has no header
                default: return type + ".Title";
            }
        }

        private static string DescriptionKey(ChapterFinalWindow w, GameObject panel)
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
        // The timeline check runs first: its rows carry blank ObjectiveInitializers, so the
        // objective sweep must never see them. A panel whose only content is a button is the
        // continue page; anything else sweeps its would-be-visible texts.
        private void BuildPanelRows(GraphBuilder b, GameObject panel, int page)
        {
            var timeline = panel.GetComponentsInChildren<TimelineItem>(true);
            if (timeline.Length > 0)
            {
                TimelineRows(b, timeline);
                return;
            }
            bool any = false;
            foreach (var obj in panel.GetComponentsInChildren<ObjectiveInitializer>(true))
            {
                if (!WouldShow(panel, obj.transform)) continue;
                any = true;
                var o = obj;
                b.AddItem(ControlId.Referenced(o, "chapterfinal:objective:" + o.GetInstanceID()),
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
                b.AddItem(ControlId.Referenced(pc, "chapterfinal:parameter:" + pc.GetInstanceID()),
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
            var next = panel.GetComponentInChildren<UnityEngine.UI.Button>(true);
            if (next != null)
            {
                ContinueButton(b, page, next);
                return;
            }
            foreach (var tmp in panel.GetComponentsInChildren<TMPro.TMP_Text>(true))
                SweptText(b, panel, tmp, tmp.text);
            foreach (var legacy in panel.GetComponentsInChildren<UnityEngine.UI.Text>(true))
                SweptText(b, panel, legacy, legacy.text);
        }

        private static void SweptText(GraphBuilder b, GameObject panel, Component text, string value)
        {
            if (!WouldShow(panel, text.transform)) return;
            if (string.IsNullOrEmpty(value) || value.Trim().Length == 0) return;
            var t = text;
            b.AddItem(ControlId.Referenced(t, "chapterfinal:swept:" + t.GetInstanceID()),
                TextRow(() => t is TMPro.TMP_Text tmp ? tmp.text : ((UnityEngine.UI.Text)t).text));
        }

        // Shown-when-its-page-shows: every ancestor below the panel root is individually active
        // (the panel root itself toggles with the game's page flips, so it is exempt).
        private static bool WouldShow(GameObject panel, Transform t)
        {
            for (; t != null && t.gameObject != panel; t = t.parent)
                if (!t.gameObject.activeSelf) return false;
            return t != null;
        }

        // The chapter's event log in on-screen order: year headers apply to every event row
        // under them, so each row folds its event name, branch and year (header label + value,
        // both the game's own texts).
        private static void TimelineRows(GraphBuilder b, TimelineItem[] timeline)
        {
            var content = timeline[0].transform.parent;
            string year = null;
            foreach (Transform child in content)
            {
                var yearItem = child.GetComponent<TimelineYear>();
                if (yearItem != null)
                {
                    var label = yearItem.GetComponentInChildren<TMPro.TMP_Text>(true);
                    year = label.text + " " + yearItem.Value.text;
                    continue;
                }
                var item = child.GetComponent<TimelineItem>();
                if (item == null) continue;
                var row = item;
                var rowYear = year;
                b.AddItem(ControlId.Referenced(row, "chapterfinal:timeline:" + row.GetInstanceID()),
                    new NodeVtable
                    {
                        ControlType = ControlTypes.Text,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(() => TimelineLabel(row, rowYear),
                                kind: AnnouncementKinds.Label),
                        },
                        SearchText = () => row.EventName.text,
                    });
            }
        }

        private static string TimelineLabel(TimelineItem row, string year)
        {
            var parts = new List<string> { row.EventName.text };
            if (!string.IsNullOrEmpty(row.BranchName.text)) parts.Add(row.BranchName.text);
            if (year != null) parts.Add(year);
            return string.Join(", ", parts.ToArray());
        }

        // The continue button that ends the chapter: labeled with its own localized text, it
        // runs the game's handler (marks the scene shown, disables itself, plays the hide
        // animation whose end triggers the Bolt flow into the next chapter). Activation first
        // lands the game on its page (an inactive panel cannot animate the hide coherently);
        // the unavailable state only speaks once its page is the shown one - the window's
        // pre-game gate would misreport it as unavailable from every other page.
        private void ContinueButton(GraphBuilder b, int page, UnityEngine.UI.Button btn)
        {
            b.AddItem(ControlId.Referenced(btn, "chapterfinal:continue"), new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new[]
                {
                    new NodeAnnouncement(
                        () => btn.GetComponentInChildren<TMPro.TMP_Text>(true).text,
                        kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(
                        () => OnShownPage(page) && !Available(btn)
                            ? Loc.T("state.unavailable") : null,
                        kind: AnnouncementKinds.Enabled),
                },
                OnActivate = () =>
                {
                    var w = Window();
                    SyncPage(w, page);
                    if (!Available(btn))
                    {
                        Mod.Speech.Speak(Loc.T("state.unavailable"), interrupt: true);
                        return;
                    }
                    w.NextSceneButton_Click(btn);
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

        private static bool Available(UnityEngine.UI.Button btn)
            => btn.interactable && !Blocked(Window());
    }
}
