using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using UnityEngine;
using WindowCredits = _Scripts.AMVCC.Views.Windows.Credits.WindowCreditsController;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The credits roll (Window_Credits - the menu's Credits scene and the game-over ending both
    /// carry this controller). The visual scroll keeps playing; the credit blocks are exposed as
    /// text rows in top-to-bottom order so they can be read at the player's own pace, labels read
    /// live from the scene's TMP blocks. Escape skips through the game's own paths: LoadMainMenu()
    /// for the menu variant, the controller's ItsGameOver keyboard branch (unblock Esc, load
    /// MainMenu) for the ending variant - both are what the suppressed stock keys would have done.
    /// </summary>
    public sealed class CreditsScreen : Screen
    {
        public override string Key => "credits";
        public override Message ScreenName => Message.Localized("ui", "screen.credits");
        public override int Layer => 10;
        public override bool IsActive() => Controller() != null;

        private static readonly FieldInfo GameOverField = typeof(WindowCredits)
            .GetField("ItsGameOver", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo BlockField = typeof(WindowCredits)
            .GetField("_block", BindingFlags.NonPublic | BindingFlags.Instance);

        private static WindowCredits Controller() => Object.FindObjectOfType<WindowCredits>();

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, null, _ => Skip());
        }

        // Mirrors the controller's Update key branch, which focus mode suppresses.
        private static void Skip()
        {
            var ctrl = Controller();
            if ((bool)BlockField.GetValue(ctrl)) return;
            if ((bool)GameOverField.GetValue(ctrl))
            {
                _Scripts.Managers.UIManager.Initiate.IsEscButtonBlocked = false;
                UnityEngine.SceneManagement.SceneManager.LoadSceneAsync("MainMenu");
            }
            else
            {
                ctrl.LoadMainMenu();
            }
        }

        public override void Build(GraphBuilder b)
        {
            var ctrl = Controller();
            if (ctrl == null) return;

            // One row per credit block, ordered top-to-bottom. Sibling order under the Credits
            // container IS the reading order; world y is not usable here because the graph builds
            // on the scene's first frame, before the layout group has positioned the blocks.
            var blocks = ctrl.GetComponentsInChildren<TMPro.TMP_Text>()
                .OrderBy(t => t.transform.parent.GetSiblingIndex());
            foreach (var tmp in blocks)
            {
                var block = tmp;
                b.AddItem(ControlId.Referenced(block, "credits:" + block.transform.parent.name),
                    new NodeVtable
                    {
                        ControlType = ControlTypes.Text,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(() => block.text, kind: AnnouncementKinds.Label),
                        },
                    });
            }
        }
    }
}
