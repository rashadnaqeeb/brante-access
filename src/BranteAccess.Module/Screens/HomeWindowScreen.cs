using System.Collections.Generic;
using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using HomeWindow = _Scripts.AMVCC.Views.Windows.HomeWindow.HomeWindowController;
using ObjectiveInitializer = _Scripts.AMVCC.Views.Windows.Destiny.ObjectiveInitializer;
using ParameterComponent = _Scripts.AMVCC.Views.Windows.ParameterComponent;
using ParameterGetSet = _Scripts.AMVCC.Views.Windows.ParameterGetSet;
using ParametersList = _Scripts.AMVCC.Models.Static.ParametersList;
using GameLoc = I2.Loc.LocalizationManager;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The House of Brante window: the household parameter rows with name, value and segment
    /// folded on and the scale readout on Space, then the heir row the game draws at the
    /// bottom of the page. When the family has broken
    /// (chapter 3+ outcome), the game swaps in a panel with its own parameters plus the
    /// family-collapse objective - the same row sweep covers those parameters and the
    /// objective reads through the Destiny readout on Space.
    /// </summary>
    public sealed class HomeWindowScreen : Screen
    {
        public override string Key => "window:home";
        public override int Layer => 10;

        public override bool IsActive()
        {
            var w = GameUi.OpenedWindow;
            return w != null && w.name == "Window_Home";
        }

        public override Message ScreenName
            => Message.MaybeRaw(GameLoc.GetTranslation("HUD.Home"));

        private static HomeWindow Window()
        {
            var w = GameUi.OpenedWindow;
            return w == null ? null : w.GetComponent<HomeWindow>();
        }

        public override void Build(GraphBuilder b)
        {
            var hw = Window();
            if (hw == null) return;
            // The game fills the freshly-instantiated prefab in its Start(), a beat after
            // ShowWindow - until then every text is a serialized placeholder (Character-window
            // precedent). Gate on the first parameter row matching its own I2 translation.
            var first = hw.GetComponentInChildren<ParameterComponent>();
            if (first == null) return;
            var firstName = System.Enum.GetName(typeof(ParametersList),
                first.GetComponent<ParameterGetSet>().Parameter.ParameterName);
            if (first.Name.text != GameLoc.GetTranslation(firstName)) return;

            b.PushContext("", role: null, positions: false);

            ParameterRows.Add(b, hw, "home:parameter:");

            // The broken-family panel carries the collapse objective under its parameters.
            if (hw.FamilyBrokenPanel.activeInHierarchy)
            {
                var oi = hw.FamilyBrokenPanel.GetComponentInChildren<ObjectiveInitializer>();
                if (oi != null)
                    b.AddItem(ControlId.Referenced(oi, "home:objective"), new NodeVtable
                    {
                        ControlType = ControlTypes.Text,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(() => oi.ObjectiveName.text,
                                kind: AnnouncementKinds.Label),
                        },
                        SearchText = () => oi.ObjectiveName.text,
                        OnTooltip = () => Mod.Speech.Speak(Readouts.ObjectiveDetails(oi)),
                    });
            }

            // The heir line sits at the bottom of the page, below the household stats.
            if (UiWidgets.Visible(hw.Heir))
            {
                var heir = hw.Heir.GetComponent<HeirParameterComponent>();
                b.AddItem(ControlId.Referenced(heir, "home:heir"), new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => Loc.T("hud.pair", new
                        {
                            label = heir.Name.text,
                            value = heir.TextValue.text,
                        }), kind: AnnouncementKinds.Label),
                    },
                });
            }

            b.PopContext();

            HudBar.Build(b);
        }

        public override string HelpText() => GameUi.WindowHelp();

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, null, _ => HudBar.ClickBack());
        }
    }
}
