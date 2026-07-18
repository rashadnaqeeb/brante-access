using System.Collections.Generic;
using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using UnityEngine;
using DestinyWindow = _Scripts.AMVCC.Views.Windows.Destiny.DestinyWindow;
using ObjectiveInitializer = _Scripts.AMVCC.Views.Windows.Destiny.ObjectiveInitializer;
using GameLoc = I2.Loc.LocalizationManager;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The Destiny window: the chapter tabs as a button row (locked tabs refuse with the
    /// unavailable word plus the game's own hover reason, HUD.WillOpenN), then the active
    /// chapter's objective lists grouped under the game's own category headers. An achieved
    /// objective carries the achieved word (the game shows it only as black-vs-gray). Space
    /// reads the objective's description and its requirement rows. Switching tabs is spoken
    /// as a delivery keyed off which chapter panel the game shows. The window's timeline pane
    /// is dead code in the shipped game (its load handler is empty) - nothing to read there.
    /// </summary>
    public sealed class DestinyWindowScreen : Screen
    {
        public override string Key => "window:destiny";
        public override int Layer => 10;

        public override bool IsActive()
        {
            var w = GameUi.OpenedWindow;
            return w != null && w.name == "Window_Destiny";
        }

        public override Message ScreenName
            => Message.MaybeRaw(GameLoc.GetTranslation("HUD.Destiny"));

        private static DestinyWindow Window()
        {
            var w = GameUi.OpenedWindow;
            return w == null ? null : w.GetComponent<DestinyWindow>();
        }

        private static GameObject[] Panels(DestinyWindow dw) => new[]
        {
            dw.FirstChapterWindow, dw.SecondChapterWindow, dw.ThirdChapterWindow,
            dw.FourthChapterWindow, dw.FifthChapterWindow,
        };

        // The five category containers of one chapter panel, in the game's on-screen order.
        private static GameObject[] Categories(DestinyWindow dw, int chapterIndex)
        {
            switch (chapterIndex)
            {
                case 0: return new[] { dw.FirstChapterPersonContainer, dw.FirstChapterFamilyContainer, dw.FirstChapterWorkContainer, dw.FirstChapterFinalContainer, dw.FirstChapterFamilyFinalContainer };
                case 1: return new[] { dw.SecondChapterPersonContainer, dw.SecondChapterFamilyContainer, dw.SecondChapterWorkContainer, dw.SecondChapterFinalContainer, dw.SecondChapterFamilyFinalContainer };
                case 2: return new[] { dw.ThirdChapterPersonContainer, dw.ThirdChapterFamilyContainer, dw.ThirdChapterWorkContainer, dw.ThirdChapterFinalContainer, dw.ThirdChapterFamilyFinalContainer };
                case 3: return new[] { dw.FourthChapterPersonContainer, dw.FourthChapterFamilyContainer, dw.FourthChapterWorkContainer, dw.FourthChapterFinalContainer, dw.FourthChapterFamilyFinalContainer };
                default: return new[] { dw.FifthChapterPersonContainer, dw.FifthChapterFamilyContainer, dw.FifthChapterWorkContainer, dw.FifthChapterFinalContainer, dw.FifthChapterFamilyFinalContainer };
            }
        }

        private static int ActivePanel(DestinyWindow dw)
        {
            var panels = Panels(dw);
            for (var i = 0; i < panels.Length; i++)
                if (panels[i].activeSelf) return i;
            return -1;
        }

        private static string TabLabel(GameObject tab)
            => UiWidgets.LocalizedLabel(tab);

        // A locked tab's reason is the game's own hover tooltip title (HUD.WillOpen2..5),
        // carried by the TooltipWithTitleBehavior on the tab itself. The current chapter's
        // tab has no tooltip component, so the bare unavailable word is the fallback.
        private static string LockedReason(GameObject tab)
        {
            var tip = tab.GetComponent<_Scripts.AMVCC.Views.TooltipWithTitleBehavior>();
            return tip == null || string.IsNullOrEmpty(tip.TitleKey)
                ? Loc.T("state.unavailable")
                : Loc.T("state.unavailable_reason",
                    new { reason = GameLoc.GetTranslation(tip.TitleKey) });
        }

        public override void Build(GraphBuilder b)
        {
            var dw = Window();
            if (dw == null) return;

            b.PushContext("", role: null, positions: false);
            b.StartRow();
            for (var i = 0; i < dw.Tabs.Count; i++)
            {
                var tab = dw.Tabs[i];
                var index = i;
                b.AddItem(ControlId.Referenced(tab.transform, "destiny:tab:" + tab.name),
                    new NodeVtable
                    {
                        ControlType = ControlTypes.Tab,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(() => TabLabel(tab),
                                kind: AnnouncementKinds.Label),
                            new NodeAnnouncement(() => ActivePanel(Window()) == index
                                    ? Loc.T("state.selected") : null,
                                kind: AnnouncementKinds.Selected),
                            new NodeAnnouncement(() => tab.GetComponent<UnityEngine.UI.Button>()
                                    .interactable ? null : LockedReason(tab),
                                kind: AnnouncementKinds.Enabled),
                        },
                        SearchText = () => TabLabel(tab),
                        OnActivate = () =>
                        {
                            if (!tab.GetComponent<UnityEngine.UI.Button>().interactable)
                            {
                                Mod.Speech.Speak(LockedReason(tab), interrupt: true);
                                return;
                            }
                            UiWidgets.Click(tab);
                        },
                    });
            }
            b.EndRow();
            b.PopContext();

            var active = ActivePanel(dw);
            if (active >= 0)
                foreach (var category in Categories(dw, active))
                {
                    if (!category.activeInHierarchy) continue;
                    var header = CategoryHeader(category);
                    b.PushContext(header, role: null, positions: true);
                    foreach (var oi in category
                        .GetComponentsInChildren<ObjectiveInitializer>(true))
                    {
                        var objective = oi;
                        b.AddItem(
                            ControlId.Referenced(objective,
                                "destiny:objective:" + objective.Objective.name),
                            new NodeVtable
                            {
                                ControlType = ControlTypes.Text,
                                Announcements = new[]
                                {
                                    new NodeAnnouncement(() => ObjectiveLabel(objective),
                                        kind: AnnouncementKinds.Label),
                                },
                                SearchText = () => objective.ObjectiveName.text,
                                OnTooltip = () =>
                                    Mod.Speech.Speak(Readouts.ObjectiveDetails(objective)),
                            });
                    }
                    b.PopContext();
                }

            HudBar.Build(b);
        }

        private static string CategoryHeader(GameObject category)
        {
            foreach (var t in category.GetComponentsInChildren<TMPro.TMP_Text>())
                if (t.name == "CategoryText")
                    return UiWidgets.LocalizedLabel(t.gameObject);
            return null;
        }

        private static string ObjectiveLabel(ObjectiveInitializer oi)
            => oi.ObjectiveName.text
                + (oi.Objective.Unlocked ? ", " + Loc.T("destiny.achieved") : "");

        // Switching chapter tabs keeps focus on the tab, so the panel change is the delivery:
        // speak the newly shown chapter's tab label once per change, seeded on focus.
        private int _spokenPanel = -1;

        public override void OnFocus()
        {
            base.OnFocus();
            var dw = Window();
            _spokenPanel = dw == null ? -1 : ActivePanel(dw);
        }

        public override void OnUpdate()
        {
            var dw = Window();
            if (dw == null) return;
            var active = ActivePanel(dw);
            if (active == _spokenPanel) return;
            _spokenPanel = active;
            if (active >= 0) Mod.Speech.Speak(TabLabel(dw.Tabs[active]));
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, null, _ => HudBar.ClickBack());
        }
    }
}
