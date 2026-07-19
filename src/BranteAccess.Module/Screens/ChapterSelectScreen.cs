using System.Reflection;
using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using UnityEngine;
using LoadingScreen = _Scripts.AMVCC.Views.Windows.LoadingWindow.GameLoadingScreenBehaviour;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The chapter-select page shown after picking a save ("GameLoadingScreen" - the life-progress
    /// slider with chapter icons). Continue resumes the save (its label folds in the game's age
    /// text, which is what the slider position shows); each chapter icon restarts from that
    /// chapter through the game's own confirmation popup, with lock state read from the item's
    /// model (Interactable + IronManMode - Button.interactable here is hover animation, not
    /// availability). Space on a chapter item reads the game's own restart help line. Escape is
    /// deliberately dead: the game blocks its Esc on this page too, and Quit to Main Menu is an
    /// explicit button.
    /// </summary>
    public sealed class ChapterSelectScreen : Screen
    {
        public override string Key => "chapterselect";
        public override Message ScreenName => Message.Localized("ui", "screen.chapterselect");
        public override int Layer => 10;
        // Behaviour presence, not a scene name: the same page is "GameLoadingScreen" (additive,
        // from the load window) but also "LoadingScreen_Child" etc. between chapters (caught
        // live - the between-chapters variant left the stack empty and silent).
        public override bool IsActive() => Behaviour() != null;

        private static readonly FieldInfo AgeField = typeof(LoadingScreen)
            .GetField("_ageTmp", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo ContinueField = typeof(LoadingScreen)
            .GetField("_continueButton", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo HelpBoxField = typeof(LoadingScreen)
            .GetField("_restartHelpTextBox", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo[] ItemFields =
        {
            typeof(LoadingScreen).GetField("_childItem", BindingFlags.NonPublic | BindingFlags.Instance),
            typeof(LoadingScreen).GetField("_teenItem", BindingFlags.NonPublic | BindingFlags.Instance),
            typeof(LoadingScreen).GetField("_youthItem", BindingFlags.NonPublic | BindingFlags.Instance),
            typeof(LoadingScreen).GetField("_peaceItem", BindingFlags.NonPublic | BindingFlags.Instance),
            typeof(LoadingScreen).GetField("_warItem", BindingFlags.NonPublic | BindingFlags.Instance),
        };

        private static LoadingScreen Behaviour() => Object.FindObjectOfType<LoadingScreen>();

        public override void Build(GraphBuilder b)
        {
            var screen = Behaviour();
            if (screen == null) return;
            var age = (TMPro.TextMeshProUGUI)AgeField.GetValue(screen);
            var cont = (GameObject)ContinueField.GetValue(screen);

            // Continue resumes the save; the age text (the slider's meaning) rides on its label.
            // While the game withholds Continue (a pending restart choice), the age stays audible
            // as a plain text node so the information never disappears with the button.
            if (UiWidgets.Visible(cont))
            {
                var contId = ControlId.Referenced(cont.GetComponent<UnityEngine.UI.Button>(),
                    "chapterselect:continue");
                b.AddItem(contId, new NodeVtable
                {
                    ControlType = ControlTypes.Button,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => UiWidgets.LocalizedLabel(cont) + ", " + age.text,
                            kind: AnnouncementKinds.Label),
                    },
                    OnActivate = () => UiWidgets.Click(cont),
                });
                b.SetStart(contId);
            }
            else
            {
                b.AddItem(ControlId.Referenced(age, "chapterselect:age"), new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => age.text, kind: AnnouncementKinds.Label),
                    },
                });
            }

            foreach (var field in ItemFields)
            {
                var item = (LoadChapterItemBehavior)field.GetValue(screen);
                var go = item.gameObject;
                b.AddItem(ControlId.Referenced(item, "chapterselect:chapter:" + item.Chapter),
                    new NodeVtable
                    {
                        ControlType = ControlTypes.Button,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(() => UiWidgets.LocalizedLabel(go),
                                kind: AnnouncementKinds.Label),
                            new NodeAnnouncement(() => LockReason(item),
                                kind: AnnouncementKinds.Enabled),
                        },
                        OnActivate = () =>
                        {
                            var reason = LockReason(item);
                            if (reason != null)
                            {
                                Mod.Speech.Speak(reason, interrupt: true);
                                return;
                            }
                            UiWidgets.Click(go);
                        },
                        OnTooltip = () =>
                        {
                            var help = (GameObject)HelpBoxField.GetValue(Behaviour());
                            if (UiWidgets.Visible(help))
                                Mod.Speech.Speak(UiWidgets.LabelText(help));
                        },
                    });
            }

            // Only the load-window variant has a quit-to-menu panel; the between-chapters
            // loading-progress variant has no BotPanel at all.
            var backPanel = screen.transform.Find("Container/BotPanel/BackToMainMenu");
            if (backPanel != null)
            {
                var back = backPanel.gameObject;
                b.AddItem(ControlId.Referenced(back.GetComponent<UnityEngine.UI.Button>(),
                    "chapterselect:back"), new NodeVtable
                {
                    ControlType = ControlTypes.Button,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => UiWidgets.LocalizedLabel(back),
                            kind: AnnouncementKinds.Label),
                    },
                    OnActivate = () => UiWidgets.Click(back),
                });
            }
        }

        private static string LockReason(LoadChapterItemBehavior item)
        {
            if (_Scripts.Managers.GameManager.Instance.IronManMode)
                return Loc.T("chapterselect.ironman");
            return item.Interactable ? null : Loc.T("chapterselect.locked");
        }
    }
}
