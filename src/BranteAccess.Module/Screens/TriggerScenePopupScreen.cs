using BranteAccess.Module.Game;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using UnityEngine;
using TriggerScenePopup = _Scripts.AMVCC.Views.Windows.TriggerScenePopupController;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The scene-trigger popup ("this event is a consequence of your previous actions"): title,
    /// the game's explanation line, the conditions that fired, and Continue. Rows come from the
    /// same SceneCondition model the popup's generators render, not the generated views - the
    /// popup marks a negated condition only by strikethrough styling, which a text sweep would
    /// silently drop; the model rows speak the not-word.
    /// </summary>
    public sealed class TriggerScenePopupScreen : Screen
    {
        public override string Key => "popup:trigger";
        public override int Layer => 21;

        private static TriggerScenePopup Popup()
        {
            var p = GameUi.OpenedPopup;
            return p == null || !p.activeInHierarchy ? null : p.GetComponent<TriggerScenePopup>();
        }

        public override bool IsActive() => Popup() != null;

        public override void Build(GraphBuilder b)
        {
            var popup = Popup();
            if (popup == null) return;

            b.PushContext("", role: null, positions: false);

            b.AddItem(ControlId.Referenced(popup.Title, "trigger:title"), new NodeVtable
            {
                ControlType = ControlTypes.Text,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => popup.Title.text, kind: AnnouncementKinds.Label),
                },
            });

            foreach (var tmp in popup.GetComponentsInChildren<TMPro.TMP_Text>())
                if (tmp.name == "Description" || tmp.name == "Condition")
                {
                    var t = tmp;
                    b.AddItem(ControlId.Referenced(t, "trigger:text:" + t.name), new NodeVtable
                    {
                        ControlType = ControlTypes.Text,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(() => t.text, kind: AnnouncementKinds.Label),
                        },
                    });
                }

            // The popup renders the condition for the scene it opened on; the same lookup is
            // the model at speech time.
            var cond = _Scripts.Helpers.SceneConditionsCollector.Instance.FindCondition(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            if (cond != null)
            {
                int n = 0;
                foreach (var c in cond.Condition)
                {
                    var row = c;
                    AddConditionRow(b, n++, () => Readouts.TriggerParamRow(row));
                }
                foreach (var c in cond.ConditionsByRelation)
                {
                    var row = c;
                    AddConditionRow(b, n++, () => Readouts.TriggerRelationRow(row));
                }
                foreach (var c in cond.ConditionsByStatus)
                {
                    var row = c;
                    AddConditionRow(b, n++, () => Readouts.TriggerStatusRow(row));
                }
                foreach (var c in cond.ConditionsByObjective)
                {
                    var row = c;
                    AddConditionRow(b, n++, () => Readouts.TriggerObjectiveRow(row));
                }
            }

            b.AddItem(ControlId.Referenced(popup.NextButton, "trigger:continue"), new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => UiWidgets.LabelText(popup.NextButton),
                        kind: AnnouncementKinds.Label),
                },
                OnActivate = () => UiWidgets.Click(popup.NextButton),
            });

            b.PopContext();
        }

        private static void AddConditionRow(GraphBuilder b, int index, System.Func<string> text)
        {
            b.AddItem(ControlId.Structural("trigger:cond:" + index), new NodeVtable
            {
                ControlType = ControlTypes.Text,
                Announcements = new[]
                {
                    new NodeAnnouncement(text, kind: AnnouncementKinds.Label),
                },
            });
        }
    }
}
