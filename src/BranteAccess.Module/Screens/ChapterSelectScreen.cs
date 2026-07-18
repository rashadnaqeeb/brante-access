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
        public override bool IsActive() => GameUi.IsSceneLoaded("GameLoadingScreen");

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
                        new NodeAnnouncement(() => UiWidgets.LabelText(cont) + ", " + age.text,
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
                            new NodeAnnouncement(() => UiWidgets.LabelText(go),
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
                                Mod.Speech.Speak(UiWidgets.LabelText(help), interrupt: true);
                        },
                    });
            }

            var back = screen.transform.Find("Container/BotPanel/BackToMainMenu").gameObject;
            b.AddItem(ControlId.Referenced(back.GetComponent<UnityEngine.UI.Button>(),
                "chapterselect:back"), new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => UiWidgets.LabelText(back),
                        kind: AnnouncementKinds.Label),
                },
                OnActivate = () => UiWidgets.Click(back),
            });
        }

        private static string LockReason(LoadChapterItemBehavior item)
        {
            if (_Scripts.Managers.GameManager.Instance.IronManMode)
                return Loc.T("chapterselect.ironman");
            return item.Interactable ? null : Loc.T("chapterselect.locked");
        }
    }

    /// <summary>
    /// The restart-from-chapter confirmation (LoadPreviousChapterConfirmPopup, a popup slot
    /// occupant). All four texts are the popup's own serialized TMP fields, read by reflection so
    /// no hierarchy names are assumed; the buttons are the TMPs' parent Buttons. Escape cancels
    /// through the game's cancel handler.
    /// </summary>
    public sealed class ChapterRestartConfirmScreen : Screen
    {
        public override string Key => "chapterrestart";
        public override int Layer => 22;
        public override bool IsActive() => Popup() != null;

        private static readonly FieldInfo TitleField = typeof(LoadPreviousChapterConfirmPopup)
            .GetField("_title", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo DescriptionField = typeof(LoadPreviousChapterConfirmPopup)
            .GetField("_description", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo ConfirmTextField = typeof(LoadPreviousChapterConfirmPopup)
            .GetField("_confirmButtonText", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo CancelTextField = typeof(LoadPreviousChapterConfirmPopup)
            .GetField("_cancelButtonText", BindingFlags.NonPublic | BindingFlags.Instance);

        private static LoadPreviousChapterConfirmPopup Popup()
            => Object.FindObjectOfType<LoadPreviousChapterConfirmPopup>();

        public override Message ScreenName
        {
            get
            {
                var p = Popup();
                return p == null ? null
                    : Message.MaybeRaw(((TMPro.TextMeshProUGUI)TitleField.GetValue(p)).text);
            }
        }

        public override System.Collections.Generic.IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, null, _ =>
            {
                var p = Popup();
                if (p != null) UiWidgets.Click(ButtonOf(p, CancelTextField));
            });
        }

        private static GameObject ButtonOf(LoadPreviousChapterConfirmPopup p, FieldInfo textField)
            => ((TMPro.TextMeshProUGUI)textField.GetValue(p))
                .GetComponentInParent<UnityEngine.UI.Button>().gameObject;

        public override void Build(GraphBuilder b)
        {
            var p = Popup();
            if (p == null) return;

            var description = (TMPro.TextMeshProUGUI)DescriptionField.GetValue(p);
            var descriptionId = ControlId.Referenced(description, "chapterrestart:description");
            b.AddItem(descriptionId, new NodeVtable
            {
                ControlType = ControlTypes.Text,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => description.text, kind: AnnouncementKinds.Label),
                },
            });
            b.SetStart(descriptionId);

            foreach (var textField in new[] { ConfirmTextField, CancelTextField })
            {
                var button = ButtonOf(p, textField);
                b.AddItem(ControlId.Referenced(button.GetComponent<UnityEngine.UI.Button>(),
                    "chapterrestart:" + button.name), new NodeVtable
                {
                    ControlType = ControlTypes.Button,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => UiWidgets.LabelText(button),
                            kind: AnnouncementKinds.Label),
                    },
                    OnActivate = () => UiWidgets.Click(button),
                });
            }
        }
    }
}
