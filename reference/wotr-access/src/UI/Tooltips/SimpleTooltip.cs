using Kingmaker.UI.MVVM._VM.Tooltip.Templates;
using Owlcat.Runtime.UI.Tooltips;

namespace WrathAccess.UI.Tooltips
{
    /// <summary>Builds a minimal header+body tooltip template (the "simple" case) — used by
    /// settings controls so their description is readable on the tooltip screen too.</summary>
    public static class SimpleTooltip
    {
        public static TooltipBaseTemplate Make(string title, string description)
            => string.IsNullOrEmpty(description) ? null : new TooltipTemplateSimple(title, description);
    }
}
