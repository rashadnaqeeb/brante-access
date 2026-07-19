using System.Collections.Generic;
using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using WorkWindow = _Scripts.AMVCC.Views.Windows.Work.WorkWindowController;
using ParameterComponent = _Scripts.AMVCC.Views.Windows.ParameterComponent;
using ParameterGetSet = _Scripts.AMVCC.Views.Windows.ParameterGetSet;
using ParametersList = _Scripts.AMVCC.Models.Static.ParametersList;
using GameLoc = I2.Loc.LocalizationManager;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The Occupation window (chapter 4 post): the game shows the current post as an image and
    /// activates that post's panel, so the content is the panel's parameter rows with name,
    /// value and segment folded on and the scale readout on Space. The post's name itself is
    /// text only in the Character window's occupation row - the parameter names here are
    /// already post-specific.
    /// </summary>
    public sealed class WorkWindowScreen : Screen
    {
        public override string Key => "window:work";
        public override int Layer => 10;

        public override bool IsActive()
        {
            var w = GameUi.OpenedWindow;
            return w != null && w.name == "Window_Work";
        }

        public override Message ScreenName
            => Message.MaybeRaw(GameLoc.GetTranslation("HUD.Inquisition"));

        private static WorkWindow Window()
        {
            var w = GameUi.OpenedWindow;
            return w == null ? null : w.GetComponent<WorkWindow>();
        }

        public override void Build(GraphBuilder b)
        {
            var ww = Window();
            if (ww == null) return;
            // The game fills the freshly-instantiated prefab in its Start(), a beat after
            // ShowWindow - until then every text is a serialized placeholder (Character-window
            // precedent). Gate on the first parameter row matching its own I2 translation.
            var first = ww.GetComponentInChildren<ParameterComponent>();
            if (first == null) return;
            var firstName = System.Enum.GetName(typeof(ParametersList),
                first.GetComponent<ParameterGetSet>().Parameter.ParameterName);
            if (first.Name.text != GameLoc.GetTranslation(firstName)) return;

            b.PushContext("", role: null, positions: false);
            ParameterRows.Add(b, ww, "work:parameter:");
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
