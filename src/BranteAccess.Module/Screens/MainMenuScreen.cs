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
        // The PREGAME gate drops this screen the instant a game starts (SetCharacterName and save
        // load flip RUNNING before the menu scene unloads) - otherwise the popup closing would
        // refocus the menu and speak stale lines over the scene transition. The handoff gate
        // covers the load flow: LoadGameButton_Click disables Continue and ONLY the load window's
        // back button re-enables it (UpdateButtonsState), so "saves exist but Continue is dead"
        // is the game's own "menu handed off" flag - without it the menu re-announces itself in
        // the frames between LoadWindow, GameLoadingScreen, and the loaded scene.
        public override bool IsActive()
            => GameUi.SceneName == "MainMenu" && GameUi.State == GameState.PREGAME
               && !LoadFlowHandoff();

        private static bool LoadFlowHandoff()
        {
            if (!SavesExist()) return false;
            foreach (var custom in UnityEngine.Object.FindObjectsOfType<CustomMainMenuButton>())
                if (custom.MainMenuButton == MainMenuButtonsEnum.Continue)
                    return !custom.GetComponent<Button>().interactable;
            return false;
        }

        public override void Build(GraphBuilder b)
        {
            foreach (var custom in MenuButtons())
            {
                var go = custom.gameObject;
                // Availability keys off the MODEL (saves on disk - only Continue has a real
                // unavailable state), never Button.interactable: the game also disables these
                // buttons as post-click guards (Continue while its window opens, New Game for 1s
                // after click), and the live watch read that guard as a bogus "unavailable" the
                // moment a window opened over the focused button. Non-live: saves can't change
                // while the menu itself is focused. A click during a guard is the game's own
                // silent no-op, same as the dimmed button a sighted player sees.
                bool isContinue = custom.MainMenuButton == MainMenuButtonsEnum.Continue;
                b.AddItem(
                    ControlId.Referenced(custom, "mainmenu:" + custom.MainMenuButton),
                    new NodeVtable
                    {
                        ControlType = ControlTypes.Button,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(LabelFor(custom), kind: AnnouncementKinds.Label),
                            new NodeAnnouncement(
                                () => !isContinue || SavesExist() ? null : Loc.T("state.unavailable"),
                                kind: AnnouncementKinds.Enabled),
                        },
                        OnActivate = () =>
                        {
                            if (isContinue && !SavesExist())
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

        private static bool SavesExist() => _Scripts.Managers.SaveLoadManager.Instance.SavesExist();

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
