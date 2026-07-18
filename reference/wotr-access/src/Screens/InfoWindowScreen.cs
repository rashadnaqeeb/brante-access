using System.Collections.Generic;
using Kingmaker;
using Kingmaker.UI.MVVM._VM.InfoWindow; // InfoWindowVM
using Owlcat.Runtime.UI.Tooltips; // TooltipBaseTemplate

namespace WrathAccess.Screens
{
    /// <summary>
    /// The game's persistent Info window (<see cref="InfoWindowVM"/> on <c>CommonVM.TooltipContextVM</c>) —
    /// opened by an item's "Information"/"Details" context action (<c>ItemSlotVM.ShowInfo</c> →
    /// <c>TooltipHelper.ShowInfo</c> → <c>HandleInfoRequest</c>) and by glossary-link info. It's a real,
    /// modal window (the game itself gates input on it), unlike the transient hover tooltip — so without a
    /// screen for it a blind player who opened "Details" was trapped with no way to read or close it.
    /// Rendering + close lifecycle live in <see cref="TemplateWindowScreen"/>; here we just point at the
    /// reactive (item Details and glossary share the <see cref="InfoWindowVM"/> type) and its OnClose.
    /// </summary>
    public sealed class InfoWindowScreen : TemplateWindowScreen
    {
        public override string Key => "overlay.infowindow";
        public override string ScreenName => Loc.T("screen.info");

        private static InfoWindowVM Vm()
        {
            var t = Game.Instance?.RootUiContext?.CommonVM?.TooltipContextVM;
            if (t == null) return null;
            return t.InfoWindowVM.Value ?? t.GlossaryInfoWindowVM.Value;
        }

        protected override object Window => Vm();
        protected override IEnumerable<TooltipBaseTemplate> Templates() => Vm()?.GetTooltipTemplates();
        protected override void CloseWindow() => Vm()?.OnClose();
    }
}
