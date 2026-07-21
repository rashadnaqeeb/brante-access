using System.Reflection;
using BranteAccess.Module.Game;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using TMPro;
using UnityEngine.UI;
using GameManager = _Scripts.Managers.GameManager;
using YearPopup = _Scripts.AMVCC.Views.Windows.Popup.YearIncrementPopup.YearIncrementPopupController;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The year-increment flash (YearIncrementPopup) shown between events when the year
    /// advances: animated year digits, chapter picture and name, and the age scale. The year
    /// is rendered as digit sprites and the age slider is animation, so a text sweep cannot
    /// read them - the row folds the serializer's current year with the popup's own localized
    /// chapter and age texts. Enter clicks the popup's next button (the game's skip; the
    /// button gates itself until the animation enables it); the popup hides itself after its
    /// animation either way, so no hint.
    /// </summary>
    public sealed class YearPopupScreen : Screen
    {
        public override string Key => "popup:year";
        public override int Layer => 22;

        private static readonly FieldInfo ChapterNameField = typeof(YearPopup)
            .GetField("_chapterName", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo AgeField = typeof(YearPopup)
            .GetField("_ageTmp", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo NextButtonField = typeof(YearPopup)
            .GetField("_nextButton", BindingFlags.NonPublic | BindingFlags.Instance);

        // Live reference for first-frame bookkeeping only, never content.
        private YearPopup _seen;
        private int _seenFrame;

        private static YearPopup Popup()
        {
            var p = GameUi.OpenedPopup;
            return p == null || !p.activeInHierarchy ? null : p.GetComponent<YearPopup>();
        }

        public override bool IsActive()
        {
            var p = Popup();
            if (p == null)
            {
                _seen = null;
                return false;
            }
            // The popup localizes its texts in Start: activate one frame after first
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
            b.AddItem(ControlId.Referenced(popup, "yearpopup:summary"), new NodeVtable
            {
                ControlType = ControlTypes.Text,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => Loc.T("yearpopup.summary", new
                    {
                        year = GameManager.Instance.GameManagerSerializer.CurrentYear,
                        chapter = ((TextMeshProUGUI)ChapterNameField.GetValue(Popup())).text,
                        age = ((TextMeshProUGUI)AgeField.GetValue(Popup())).text,
                    }), kind: AnnouncementKinds.Label),
                },
                OnActivate = () => UiWidgets.Click(
                    ((Button)NextButtonField.GetValue(Popup())).gameObject),
            });
        }
    }
}
