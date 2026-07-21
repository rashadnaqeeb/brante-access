using System.Reflection;
using BranteAccess.Module.Game;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using TMPro;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The insurrection day-stage flash (DayTimePopup) - chapter 5's twin of the year
    /// popup, shown between events when the day stage advances (morning, day, evening,
    /// night). The stage name is the popup's own localized text; the sun-position slider
    /// carries no extra information. No buttons - the popup hides itself after its
    /// animation.
    /// </summary>
    public sealed class DayStagePopupScreen : Screen
    {
        public override string Key => "popup:daystage";
        public override int Layer => 22;

        private static readonly FieldInfo StageNameField = typeof(DayTimePopupController)
            .GetField("_chapterName", BindingFlags.NonPublic | BindingFlags.Instance);

        // Live reference for first-frame bookkeeping only, never content.
        private DayTimePopupController _seen;
        private int _seenFrame;

        private static DayTimePopupController Popup()
        {
            var p = GameUi.OpenedPopup;
            return p == null || !p.activeInHierarchy
                ? null : p.GetComponent<DayTimePopupController>();
        }

        public override bool IsActive()
        {
            var p = Popup();
            if (p == null)
            {
                _seen = null;
                return false;
            }
            // The popup localizes its text in Start: activate one frame after first
            // observation so the row never reads the prefab's serialized editor text.
            if (!ReferenceEquals(p, _seen))
            {
                _seen = p;
                _seenFrame = UnityEngine.Time.frameCount;
            }
            return UnityEngine.Time.frameCount > _seenFrame;
        }

        public override void Build(GraphBuilder b)
        {
            var popup = Popup();
            if (popup == null) return;
            b.AddItem(ControlId.Referenced(popup, "daystage:name"), new NodeVtable
            {
                ControlType = ControlTypes.Text,
                Announcements = new[]
                {
                    new NodeAnnouncement(
                        () => ((TextMeshProUGUI)StageNameField.GetValue(Popup())).text,
                        kind: AnnouncementKinds.Label),
                },
            });
        }
    }
}
