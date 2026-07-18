using System.Collections.Generic;
using Kingmaker;
using Kingmaker.UI.MVVM._VM.Tooltip; // InspectWindowVM
using Owlcat.Runtime.UI.Tooltips; // TooltipBaseTemplate

namespace WrathAccess.Screens
{
    /// <summary>
    /// The game's unit Inspect window (<see cref="InspectWindowVM"/> on <c>CommonVM.TooltipContextVM</c>) —
    /// the bestiary-style readout opened by our inspect keys (review-cursor unit / unit under the movement
    /// cursor) via <c>TooltipHelper.ShowInspectTooltip</c>. Its main content is an <c>InfoWindowVM</c> built
    /// from a <c>TooltipTemplateUnitInspect</c> (<c>UnitSectionVM</c>), so it renders exactly like the Info
    /// window; close runs the window's own <c>Close()</c> (which also leaves the FullScreenUi mode it
    /// entered). The combat-units list it also carries is left for later. Rendering + lifecycle: see
    /// <see cref="TemplateWindowScreen"/>.
    /// </summary>
    public sealed class InspectScreen : TemplateWindowScreen
    {
        public override string Key => "overlay.inspect";
        public override string ScreenName => Loc.T("screen.inspect");

        private static InspectWindowVM Vm()
            => Game.Instance?.RootUiContext?.CommonVM?.TooltipContextVM?.InspectWindowVM?.Value;

        protected override object Window => Vm();
        protected override IEnumerable<TooltipBaseTemplate> Templates() => Vm()?.UnitSectionVM?.GetTooltipTemplates();
        protected override void CloseWindow() => Vm()?.Close();
    }
}
