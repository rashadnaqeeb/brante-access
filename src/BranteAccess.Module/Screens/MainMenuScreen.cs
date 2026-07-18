using System.Collections.Generic;
using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using UnityEngine.UI;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The main menu: one Tab-stop of vertical buttons, read live from the scene's
    /// CustomMainMenuButton components each render (their labels are localized SPRITES, so the
    /// spoken labels are mod-authored ui.txt keys - see DECISIONS.md). Activation invokes the
    /// game Button's own onClick so game logic and sounds run.
    /// </summary>
    public sealed class MainMenuScreen : Screen
    {
        public override string Key => "mainmenu";
        public override Message ScreenName => Message.Localized("ui", "screen.mainmenu");
        public override int Layer => 0;
        public override bool IsActive() => GameUi.SceneName == "MainMenu";

        public override void Build(GraphBuilder b)
        {
            foreach (var custom in MenuButtons())
            {
                var button = custom.GetComponent<Button>();
                b.AddItem(
                    ControlId.Referenced(custom, "mainmenu:" + custom.MainMenuButton),
                    new NodeVtable
                    {
                        ControlType = ControlTypes.Button,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(LabelFor(custom), kind: AnnouncementKinds.Label),
                            new NodeAnnouncement(
                                () => button.interactable ? null : Loc.T("state.unavailable"),
                                live: true, kind: AnnouncementKinds.Enabled),
                        },
                        OnActivate = () =>
                        {
                            if (!button.interactable)
                            {
                                Mod.Speech.Speak(Loc.T("state.unavailable"), interrupt: true);
                                return;
                            }
                            button.onClick.Invoke();
                        },
                    });
            }
        }

        // The scene's menu buttons in visual order (top to bottom). Re-queried per render - never
        // cached (CLAUDE.md).
        private static List<CustomMainMenuButton> MenuButtons()
        {
            var buttons = new List<CustomMainMenuButton>(
                UnityEngine.Object.FindObjectsOfType<CustomMainMenuButton>());
            buttons.Sort((a, c) =>
            {
                int byY = c.transform.position.y.CompareTo(a.transform.position.y);
                return byY != 0 ? byY : a.transform.position.x.CompareTo(c.transform.position.x);
            });
            return buttons;
        }

        private static System.Func<string> LabelFor(CustomMainMenuButton custom)
        {
            switch (custom.MainMenuButton)
            {
                case MainMenuButtonsEnum.Continue: return () => Loc.T("mainmenu.continue");
                case MainMenuButtonsEnum.NewGame: return () => Loc.T("mainmenu.newgame");
                case MainMenuButtonsEnum.Credits: return () => Loc.T("mainmenu.credits");
                case MainMenuButtonsEnum.Settings: return () => Loc.T("mainmenu.settings");
                case MainMenuButtonsEnum.Quit: return () => Loc.T("mainmenu.quit");
                default:
                    // A button the enum doesn't cover: its object name is audible (so the gap is
                    // discoverable), never silent.
                    return () => custom.gameObject.name;
            }
        }
    }
}
