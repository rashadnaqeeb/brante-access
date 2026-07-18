using System.Collections.Generic;
using System.Reflection;
using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using UnityEngine;
using ChapterFinalWindow = _Scripts.AMVCC.Views.Windows.ChapterFinal.ChapterFinalWindowController;
using ObjectiveInitializer = _Scripts.AMVCC.Views.Windows.Destiny.ObjectiveInitializer;
using TimelineItem = _Scripts.AMVCC.Views.Windows.Destiny.TimelineComponent;
using TimelineYear = _Scripts.AMVCC.Views.Windows.Destiny.TimelineYearComponent;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The end-of-chapter book (ChapterFinalWindowController, a scene prefab): the chapter
    /// epilogue text, then pages - the timeline of the chapter's events, achieved objectives
    /// per category, parameter summaries, and last the continue button that starts the next
    /// chapter. Timeline rows fold the event's branch and year on (the year headers between
    /// rows carry the year for every event under them); objective rows fold the description,
    /// with the requirement rows on Space; parameter pages reuse the shared parameter rows.
    /// Page turns ride the game's own Prev/Next buttons; a turned page announces its title,
    /// position and description as the delivery. The game's A/D keys are suppressed by the
    /// focus-mode patch, so paging is only through the nodes.
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

        // Live component reference for delivery bookkeeping only.
        private ChapterFinalWindow _watched;
        private int _spokenPage;

        private static ChapterFinalWindow Window() => Object.FindObjectOfType<ChapterFinalWindow>();

        private static int CurrentPage(ChapterFinalWindow w) => (int)CurrentPageField.GetValue(w);
        private static List<GameObject> Panels(ChapterFinalWindow w)
            => (List<GameObject>)PanelsField.GetValue(w);
        private static bool Blocked(ChapterFinalWindow w) => (bool)BlockPagesField.GetValue(w);

        // The page delivery: title first, position, then the page description. The game blanks
        // the whole title container on the continue page (alpha), so that page is position only.
        private static string PageAnnouncement(ChapterFinalWindow w)
        {
            var shown = w.PageDescriptionContainer.alpha > 0;
            var text = "";
            if (shown && !string.IsNullOrEmpty(w.PageTitle.text))
                text = w.PageTitle.text + ", ";
            text += Loc.T("nav.position", new
            {
                index = CurrentPage(w) + 1,
                count = Panels(w).Count,
            });
            if (shown && !string.IsNullOrEmpty(w.PageDescription.text))
                text += ", " + w.PageDescription.text;
            return text;
        }

        public override void OnPop()
        {
            _watched = null;
        }

        public override void OnUpdate()
        {
            var w = Window();
            if (w == null) return;
            int page = CurrentPage(w);
            if (w != _watched)
            {
                // Screen entry: the seat announcement reads the page-title row.
                _watched = w;
                _spokenPage = page;
                return;
            }
            if (page == _spokenPage) return;
            // Page turned: the delivery is the speech; focus stays where the player is (the
            // pager buttons are Referenced, so repeated paging is one keypress per page).
            _spokenPage = page;
            Mod.Speech.Speak(PageAnnouncement(w));
        }

        public override void Build(GraphBuilder b)
        {
            var w = Window();
            if (w == null) return;
            var panels = Panels(w);
            int page = CurrentPage(w);

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

            var titleId = ControlId.Structural("chapterfinal:pagetitle");
            b.AddItem(titleId, TextRow(() => PageAnnouncement(Window())));
            b.SetStart(titleId);

            if (page < panels.Count)
                BuildPanel(b, w, panels[page]);

            b.PopContext();

            PagerButton(b, w.PreviousButton, "chapterfinal:prev", "pager.prev",
                () => Window().PreviousButton_Click());
            PagerButton(b, w.NextButton, "chapterfinal:next", "pager.next",
                () => Window().NextButton_Click());
        }

        // One page's content. The timeline check runs first: its rows carry blank
        // ObjectiveInitializers, so the objective sweep must never see them.
        private void BuildPanel(GraphBuilder b, ChapterFinalWindow w, GameObject panel)
        {
            var timeline = panel.GetComponentsInChildren<TimelineItem>();
            if (timeline.Length > 0)
            {
                TimelineRows(b, timeline);
                return;
            }
            var objectives = panel.GetComponentsInChildren<ObjectiveInitializer>();
            if (objectives.Length > 0)
            {
                foreach (var obj in objectives)
                {
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
                return;
            }
            if (panel.GetComponentsInChildren<_Scripts.AMVCC.Views.Windows.ParameterComponent>()
                    .Length > 0)
            {
                ParameterRows.Add(b, panel.transform, "chapterfinal:parameter:");
                return;
            }
            var next = panel.GetComponentInChildren<UnityEngine.UI.Button>(true);
            if (next != null)
            {
                ContinueButton(b, w, next);
                return;
            }
            b.PopContext();
            PanelSweep.Build(b, panel, "chapterfinal");
            b.PushContext("", role: null, positions: false);
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
                    var label = yearItem.GetComponentInChildren<TMPro.TMP_Text>();
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
        // animation whose end triggers the Bolt flow into the next chapter).
        private void ContinueButton(GraphBuilder b, ChapterFinalWindow w, UnityEngine.UI.Button btn)
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
                        () => Available(btn) ? null : Loc.T("state.unavailable"),
                        kind: AnnouncementKinds.Enabled),
                },
                OnActivate = () =>
                {
                    if (!Available(btn))
                    {
                        Mod.Speech.Speak(Loc.T("state.unavailable"), interrupt: true);
                        return;
                    }
                    Window().NextSceneButton_Click(btn);
                },
            });
        }

        private static NodeVtable TextRow(System.Func<string> text) => new NodeVtable
        {
            ControlType = ControlTypes.Text,
            Announcements = new[]
            {
                new NodeAnnouncement(text, kind: AnnouncementKinds.Label),
            },
        };

        // The pager arrows are image-only buttons - the label is mod-authored. Blocked pages
        // (the window's own pre-game gate) and the game's end-stop disable state both refuse.
        private void PagerButton(GraphBuilder b, UnityEngine.UI.Button btn,
            string id, string labelKey, System.Action click)
        {
            b.AddItem(ControlId.Referenced(btn, id), new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => Loc.T(labelKey), kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(
                        () => Available(btn) ? null : Loc.T("state.unavailable"),
                        kind: AnnouncementKinds.Enabled),
                },
                OnActivate = () =>
                {
                    if (!Available(btn))
                    {
                        Mod.Speech.Speak(Loc.T("state.unavailable"), interrupt: true);
                        return;
                    }
                    click();
                },
            });
        }

        private static bool Available(UnityEngine.UI.Button btn)
            => btn.interactable && !Blocked(Window());
    }
}
