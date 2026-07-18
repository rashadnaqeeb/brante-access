using System.Collections.Generic;
using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using UnityEngine.UI;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The main menu. Stop 1: the five sprite buttons, read live from the scene's
    /// CustomMainMenuButton components each render (their labels are localized SPRITES, so the
    /// spoken labels are mod-authored ui.txt keys - see DECISIONS.md; the Continue button runs
    /// the game's LoadGameButton_Click, opening the load window). Stop 2: the scene's remaining
    /// visible buttons (Discord links), labels from their own game text. Activation goes through
    /// the game's pointer-click path so game logic and sounds run.
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
                var go = custom.gameObject;
                b.AddItem(
                    ControlId.Referenced(custom, "mainmenu:" + custom.MainMenuButton),
                    new NodeVtable
                    {
                        ControlType = ControlTypes.Button,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(LabelFor(custom), kind: AnnouncementKinds.Label),
                            new NodeAnnouncement(
                                () => UiWidgets.Interactable(go) ? null : Loc.T("state.unavailable"),
                                live: true, kind: AnnouncementKinds.Enabled),
                        },
                        OnActivate = () =>
                        {
                            if (!UiWidgets.Interactable(go))
                            {
                                Mod.Speech.Speak(Loc.T("state.unavailable"), interrupt: true);
                                return;
                            }
                            UiWidgets.Click(go);
                        },
                    });
            }

            // The scene's other visible buttons (the Discord links) - their labels are the
            // game's own localized text, read live.
            var extras = ExtraButtons();
            if (extras.Count == 0) return;
            b.BeginStop("extras");
            foreach (var extra in extras)
            {
                var go = extra.gameObject;
                b.AddItem(
                    ControlId.Referenced(extra, "mainmenu:extra:" + go.name),
                    new NodeVtable
                    {
                        ControlType = ControlTypes.Button,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(() => UiWidgets.LabelText(go),
                                kind: AnnouncementKinds.Label),
                        },
                        OnActivate = () => UiWidgets.Click(go),
                    });
            }
        }

        // Visible MainMenu-scene buttons that are not sprite menu buttons, in visual order.
        // Scene-scoped: FindObjectsOfType sees ADDITIVELY loaded scenes too (Settings/LoadWindow),
        // and their same-named buttons would collide as duplicate control ids (found by sweep).
        private static List<Button> ExtraButtons()
        {
            var list = new List<Button>();
            foreach (var btn in UnityEngine.Object.FindObjectsOfType<Button>())
                if (btn.gameObject.scene.name == "MainMenu"
                    && btn.GetComponent<CustomMainMenuButton>() == null
                    && UiWidgets.Visible(btn.gameObject))
                    list.Add(btn);
            list.Sort((a, c) =>
            {
                int byY = c.transform.position.y.CompareTo(a.transform.position.y);
                return byY != 0 ? byY : a.transform.position.x.CompareTo(c.transform.position.x);
            });
            return list;
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
