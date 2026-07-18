using System.Collections.Generic;
using System.Linq;
using Kingmaker; // GameStarter
using Kingmaker.UI.MVVM._PCView.Settings.GammaCorrection; // GammaCorrectionPCView
using Kingmaker.UI.MVVM._VM.Settings.Entities; // SettingsEntitySliderVM
using Kingmaker.UI.MVVM._VM.Settings.GammaCorrection;     // GammaCorrectionVM
using Owlcat.Runtime.UI.MVVM; // IHasViewModel
using UnityEngine; // Resources
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The first-launch brightness/gamma + contrast calibration (<see cref="GammaCorrectionVM"/>), which
    /// GameStarter shows BEFORE the main menu and which BLOCKS boot (its coroutine spins on
    /// <c>gammaCorrectionIsChosen</c>) until the player confirms. It's a visual setup, so most players just
    /// confirm — but low-vision players can adjust it: we expose the same gamma + contrast sliders the game's
    /// own view builds (<c>new SettingsEntitySliderVM(vm.GammaCorrection/Contrast)</c>), which write the
    /// setting's temp value (applied live), then "Continue" confirms+saves it (VM.Close — which also marks
    /// gamma touched so this never auto-shows again, and fires the close callback that releases GameStarter's
    /// wait loop). "Reset to default" restores the default gamma/contrast.
    ///
    /// Detected via <c>GameStarter.IsGammaCorrectionActive</c> (a public static the boot block toggles); the
    /// live VM comes from the bound <see cref="GammaCorrectionPCView"/> (mouse mode) via IHasViewModel.
    ///
    /// Graph-native: declared fresh from the live VM every render. The slider VMs we build over the game's
    /// UISettings entities are the one piece of retained state (they're disposables observing the underlying
    /// settings), created per gamma VM and disposed on pop.
    ///
    /// To re-trigger first-time setup for testing: in <c>general_settings.json</c> (LocalLow, game closed)
    /// set <c>"settings.graphics.settings.graphics.gamma-correction-new-was-touched"</c> to <c>false</c>.
    /// </summary>
    public sealed class GammaScreen : Screen
    {
        public GammaScreen() { Wrap = true; } // Tab cycles help ↔ sliders ↔ Continue ↔ Reset

        public override string Key => "ctx.gamma";
        public override string ScreenName => Loc.T("screen.gamma");
        public override int Layer => 40; // boot-time modal, above everything else
        public override bool Exclusive => true;

        public override bool IsActive() => GameStarter.IsGammaCorrectionActive && Vm() != null;

        private static GammaCorrectionVM Vm()
        {
            var view = Resources.FindObjectsOfTypeAll<GammaCorrectionPCView>().FirstOrDefault();
            return view is IHasViewModel h ? h.GetViewModel() as GammaCorrectionVM : null;
        }

        // The slider VMs we build over the game's UISettings entities — ours to dispose (the game disposes
        // its own copies; these are independent observers of the same underlying gamma/contrast settings).
        private GammaCorrectionVM _slidersFor;
        private SettingsEntitySliderVM _gamma, _contrast;

        public override void OnPop() { DisposeSliders(); }

        private void EnsureSliders(GammaCorrectionVM vm)
        {
            if (ReferenceEquals(vm, _slidersFor)) return;
            DisposeSliders();
            _slidersFor = vm;
            _gamma = new SettingsEntitySliderVM(vm.GammaCorrection);
            _contrast = new SettingsEntitySliderVM(vm.Contrast);
        }

        private void DisposeSliders()
        {
            _gamma?.Dispose();
            _contrast?.Dispose();
            _gamma = null;
            _contrast = null;
            _slidersFor = null;
        }


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            EnsureSliders(vm);
            string k = "gamma:" + vm.GetHashCode() + ":";

            b.BeginStop("help").AddItem(ControlId.Structural(k + "help"), GraphNodes.Text(() => Loc.T("gamma.help")));

            // The two sliders are one Tab-stop (arrows move between them), matching Settings.
            b.BeginStop("sliders").PushContext(Loc.T("gamma.sliders"), "list");
            b.AddItem(ControlId.Referenced(_gamma, k + "g"), GraphNodes.Slider(_gamma, GraphNodes.Position(1, 2)));
            b.AddItem(ControlId.Referenced(_contrast, k + "c"), GraphNodes.Slider(_contrast, GraphNodes.Position(2, 2)));
            b.PopContext();

            b.BeginStop("continue").AddItem(ControlId.Structural(k + "continue"),
                GraphNodes.Button(() => Loc.T("gamma.continue"), () => vm.Close()));
            b.BeginStop("reset").AddItem(ControlId.Structural(k + "reset"),
                GraphNodes.Button(() => Loc.T("gamma.reset"), () => vm.Reset()));
        }

        // No cancel on this screen (the game only offers Apply / Default) — map Back/Escape to proceed.
        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "gamma.continue"),
                _ => Vm()?.Close());
        }
    }
}
