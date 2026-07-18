using System.Collections.Generic;
using System.Linq;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Portrait;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Portrait phase: a Default/Custom tab selector + the portraits in the current tab. Portraits
    /// have no names (they're images), so we label them by the blueprint's asset name (codey but
    /// distinguishing) falling back to position; selecting one applies it. Portrait keys carry the
    /// tab, so switching re-keys the list (tab focus survives). The custom-portrait creator (file
    /// import) and the "create new" item are skipped for now.
    /// </summary>
    public sealed class PortraitPhaseContent : CharGenPhaseContent<CharGenPortraitPhaseVM>
    {
        public PortraitPhaseContent(CharGenPortraitPhaseVM phase) : base(phase) { }

        public override void Build(GraphBuilder b, string k)
        {
            b.PushContext(Loc.T("label.tabs"), "list");
            int ti = 0;
            foreach (var tab in Phase.TabSelector.EntitiesCollection)
            {
                if (tab == null) { ti++; continue; }
                var t = tab; // capture for the live closure
                b.AddItem(ControlId.Referenced(t, k + "tab:" + ti),
                    GraphNodes.Tab(() => t.Tab.ToString(), () => t.IsSelected.Value,
                        () => t.SetSelectedFromView(true)));
                ti++;
            }
            b.PopContext();

            string pk = k + "p:" + (Phase.CurrentTab.Value?.GetHashCode() ?? 0) + ":";
            b.BeginStop("portraits").PushContext(Loc.T("chargen.portraits"), "list");
            int index = 1;
            bool any = false;
            foreach (var portrait in CurrentPortraits())
            {
                if (portrait == null || portrait.CustomPortraitCreatorItem) continue; // skip "create new"
                var p = portrait; var positional = "Portrait " + index; // capture for the live closure
                // Portraits play their own selection sound off the VM path — no click of ours.
                b.AddItem(ControlId.Referenced(p, pk + index),
                    GraphNodes.SelectionItem(p, () =>
                    {
                        var bp = p.GetBlueprintPortrait();
                        var n = bp != null ? bp.name : null;
                        return !string.IsNullOrEmpty(n) ? n : positional;
                    }, sound: null));
                any = true;
                index++;
            }
            if (!any)
                b.AddItem(ControlId.Structural(pk + "none"), GraphNodes.Text(() => Loc.T("chargen.no_portraits")));
            b.PopContext();
        }

        // Default tab → all built-in portraits (grouped by category); Custom tab → imported ones.
        private IEnumerable<CharGenPortraitSelectorItemVM> CurrentPortraits()
        {
            bool custom = Phase.CurrentTab.Value != null && Phase.CurrentTab.Value.Tab == CharGenPortraitTab.Custom;
            if (custom) return Phase.CustomPortraitGroup.PortraitCollection;
            return Phase.PortraitGroupVms.Values.SelectMany(g => g.PortraitCollection);
        }
    }
}
