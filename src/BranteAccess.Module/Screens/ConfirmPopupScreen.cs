using System.Reflection;
using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using UnityEngine;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// Shared shape for the game's confirm popups (LoadPreviousChapterConfirmPopup,
    /// ExitConfirmationPopupController): four serialized TMP fields - _title, _description,
    /// _confirmButtonText, _cancelButtonText - read by reflection so no hierarchy names are
    /// assumed, buttons found as the TMPs' parent Buttons. Title is the screen name, description
    /// the start node, Escape cancels through the game's cancel handler.
    /// </summary>
    public abstract class ConfirmPopupScreen<T> : Screen where T : Component
    {
        public override int Layer => 22;
        public override bool IsActive() => Popup() != null;

        private static readonly FieldInfo TitleField = typeof(T)
            .GetField("_title", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo DescriptionField = typeof(T)
            .GetField("_description", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo ConfirmTextField = typeof(T)
            .GetField("_confirmButtonText", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo CancelTextField = typeof(T)
            .GetField("_cancelButtonText", BindingFlags.NonPublic | BindingFlags.Instance);

        private static T Popup() => Object.FindObjectOfType<T>();

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

        private static GameObject ButtonOf(T p, FieldInfo textField)
            => ((TMPro.TextMeshProUGUI)textField.GetValue(p))
                .GetComponentInParent<UnityEngine.UI.Button>().gameObject;

        public override void Build(GraphBuilder b)
        {
            var p = Popup();
            if (p == null) return;

            var description = (TMPro.TextMeshProUGUI)DescriptionField.GetValue(p);
            var descriptionId = ControlId.Referenced(description, Key + ":description");
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
                    Key + ":" + button.name), new NodeVtable
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

    /// <summary>The restart-from-chapter confirmation opened by a chapter-select icon.</summary>
    public sealed class ChapterRestartConfirmScreen : ConfirmPopupScreen<LoadPreviousChapterConfirmPopup>
    {
        public override string Key => "chapterrestart";
    }

    /// <summary>The quit-to-main-menu confirmation opened by the pause window's exit button
    /// (never occupies the UIManager popup slot - it is its own top-level popup).</summary>
    public sealed class ExitConfirmScreen
        : ConfirmPopupScreen<_Scripts.AMVCC.Views.Windows.Popup.ExitConfirmationPopup.ExitConfirmationPopupController>
    {
        public override string Key => "exitconfirm";
    }
}
