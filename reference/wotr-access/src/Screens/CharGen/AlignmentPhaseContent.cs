using Kingmaker.UI.Common; // UIUtility.GetAlignmentName
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Alignment;
using Kingmaker.UI.MVVM._VM.Tooltip.Templates; // TooltipTemplateAlignment
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Alignment phase: the nine alignments as a radio list (Lawful/Neutral/Chaotic × Good/Neutral/Evil,
    /// in the game's row-major order). The names are self-describing, so a flat list reads cleanly
    /// without the visual wheel. Class-restricted alignments read "disabled" and can't be picked (via
    /// IsAvailable); Space on any one opens its description (TooltipTemplateAlignment). The sector list
    /// is built lazily by the game — immediate mode simply renders it once it materializes.
    /// </summary>
    public sealed class AlignmentPhaseContent : CharGenPhaseContent<CharGenAlignmentPhaseVM>
    {
        public AlignmentPhaseContent(CharGenAlignmentPhaseVM phase) : base(phase) { }

        public override void Build(GraphBuilder b, string k)
        {
            var sectors = Phase.AlignmentSectorViewModels;
            if (sectors == null || sectors.Count == 0) return; // lazy — renders once it materializes

            b.PushContext(Loc.T("chargen.alignments"), "list");
            int i = 0;
            foreach (var sector in sectors)
            {
                if (sector == null) { i++; continue; }
                var s = sector; // capture for the live closures
                b.AddItem(ControlId.Referenced(s, k + "align:" + i),
                    GraphNodes.SelectionItem(s,
                        () => UIUtility.GetAlignmentName(s.Alignment),
                        tooltip: () => new TooltipTemplateAlignment(s.Alignment, isUndetectable: false)));
                i++;
            }
            b.PopContext();
        }
    }
}
