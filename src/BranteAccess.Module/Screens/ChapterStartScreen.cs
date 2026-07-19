using System.Collections.Generic;
using System.Reflection;
using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using UnityEngine;
using ChapterStartWindow = _Scripts.AMVCC.Views.Windows.ChapterStart.ChapterStartWindowController;
using ObjectiveInitializer = _Scripts.AMVCC.Views.Windows.Destiny.ObjectiveInitializer;
using ParameterComponent = _Scripts.AMVCC.Views.Windows.ParameterComponent;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The chapter start book (ChapterStartWindowController): pages of chapter goals,
    /// parameters and new features, ending in the begin-chapter page. Rows are the window's
    /// header, the current page's title + description, then the page's content - objectives
    /// fold their tooltip description onto the row (the description only exists on hover for
    /// sighted players), parameters fold name, value and segment. Page turns ride the game's
    /// own Prev/Next buttons as nodes; a turned page announces its title, position and
    /// description as the delivery, with focus staying wherever the player is (the buttons
    /// survive rebuilds, so paging repeatedly is one keypress per page).
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
                return w == null ? null : Message.MaybeRaw(w.Title.text);
            }
        }

        private static readonly FieldInfo CurrentPageField = typeof(ChapterStartWindow)
            .GetField("_currentPage", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo PanelsField = typeof(ChapterStartWindow)
            .GetField("_panels", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo BlockPagesField = typeof(ChapterStartWindow)
            .GetField("_blockPages", BindingFlags.NonPublic | BindingFlags.Instance);

        // Live component reference for delivery bookkeeping only.
        private ChapterStartWindow _watched;
        private int _spokenPage;

        private static ChapterStartWindow Window() => Object.FindObjectOfType<ChapterStartWindow>();

        private static int CurrentPage(ChapterStartWindow w) => (int)CurrentPageField.GetValue(w);
        private static List<GameObject> Panels(ChapterStartWindow w)
            => (List<GameObject>)PanelsField.GetValue(w);
        private static bool Blocked(ChapterStartWindow w) => (bool)BlockPagesField.GetValue(w);

        // The page delivery: title first, position, then the page description where the game
        // shows one (it hides it on the begin page).
        private static string PageAnnouncement(ChapterStartWindow w)
        {
            var text = w.PageTitle.text;
            text += ", " + Loc.T("nav.position", new
            {
                index = CurrentPage(w) + 1,
                count = Panels(w).Count,
            });
            if (UiWidgets.Visible(w.PageDescription.gameObject)
                && !string.IsNullOrEmpty(w.PageDescription.text))
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

            if (!string.IsNullOrEmpty(w.ShortDescription.text))
                b.AddItem(ControlId.Referenced(w.ShortDescription, "chapterstart:desc"),
                    TextRow(() => Window().ShortDescription.text));

            var titleId = ControlId.Structural("chapterstart:pagetitle");
            b.AddItem(titleId, TextRow(() => PageAnnouncement(Window())));
            b.SetStart(titleId);

            // Current page content. Objectives, parameters and section unlocks are folded rows
            // read from their components; any other panel (the begin page) is swept generically.
            if (page < panels.Count)
            {
                var panel = panels[page];
                var objectives = panel.GetComponentsInChildren<ObjectiveInitializer>();
                var parameters = panel.GetComponentsInChildren<ParameterComponent>();
                var unlocks = panel.GetComponentsInChildren<UnlockedItemBehaviour>();
                if (objectives.Length > 0)
                    foreach (var obj in objectives)
                    {
                        var o = obj;
                        b.AddItem(ControlId.Referenced(o, "chapterstart:objective:" + o.GetInstanceID()),
                            TextRow(() => o.ObjectiveName.text + ". " + o.ObjectiveDescription));
                    }
                else if (parameters.Length > 0)
                    foreach (var par in parameters)
                    {
                        var pc = par;
                        b.AddItem(ControlId.Referenced(pc, "chapterstart:parameter:" + pc.GetInstanceID()),
                            TextRow(() => pc.Name.text + " " + pc.TextValue.text
                                + (string.IsNullOrEmpty(pc.Descr.text) ? "" : ", " + pc.Descr.text)));
                    }
                else if (unlocks.Length > 0)
                    // A New Sections row is the unlock text plus an image-only icon that opens
                    // the unlocked window (HudController wiring on the icon's Button) - one
                    // button row per unlock, labeled by the game's own unlock text, instead of
                    // a text row and a bare unlabeled icon stop.
                    foreach (var unlock in unlocks)
                    {
                        var u = unlock;
                        var icon = u.GetComponentInChildren<UnityEngine.UI.Button>();
                        b.AddItem(ControlId.Referenced(u, "chapterstart:unlock:" + u.GetInstanceID()),
                            new NodeVtable
                            {
                                ControlType = ControlTypes.Button,
                                Announcements = new[]
                                {
                                    new NodeAnnouncement(
                                        () => u.GetComponentInChildren<TMPro.TMP_Text>().text,
                                        kind: AnnouncementKinds.Label),
                                    new NodeAnnouncement(
                                        () => UiWidgets.Interactable(icon.gameObject)
                                            ? null : Loc.T("state.unavailable"),
                                        kind: AnnouncementKinds.Enabled),
                                },
                                OnActivate = () =>
                                {
                                    if (!UiWidgets.Interactable(icon.gameObject))
                                    {
                                        Mod.Speech.Speak(Loc.T("state.unavailable"), interrupt: true);
                                        return;
                                    }
                                    UiWidgets.Click(icon.gameObject);
                                },
                            });
                    }
                else
                {
                    b.PopContext();
                    PanelSweep.Build(b, panel, "chapterstart");
                    b.PushContext("", role: null, positions: false);
                }
            }
            b.PopContext();

            PagerButton(b, w, w.PreviousButton, "chapterstart:prev", "pager.prev",
                () => Window().PreviousButton_Click());
            PagerButton(b, w, w.NextButton, "chapterstart:next", "pager.next",
                () => Window().NextButton_Click());
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
        private void PagerButton(GraphBuilder b, ChapterStartWindow w, UnityEngine.UI.Button btn,
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
