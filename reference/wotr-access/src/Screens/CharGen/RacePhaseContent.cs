using Kingmaker.UI.MVVM._VM.CharGen.Phases.Race;
using Owlcat.Runtime.UI.Tooltips; // TooltipTemplateType
using WrathAccess.UI;
using WrathAccess.UI.Graph;
using WrathAccess.UI.Tooltips;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Race phase: the race list, a gender selector, and a live Details panel — the SELECTED race's
    /// own template (TooltipTemplateLevelUpRace: ability-score bonuses, racial features,
    /// description), rendered as a document at
    /// <see cref="TooltipTemplateType.Info"/>. Selecting goes through the game's own SetSelectedFromView;
    /// the detail keys carry race + gender, so it re-keys on change while list focus stays put.
    /// </summary>
    public sealed class RacePhaseContent : CharGenPhaseContent<CharGenRacePhaseVM>
    {
        public RacePhaseContent(CharGenRacePhaseVM phase) : base(phase) { }

        // The game's on-screen info panel is HOVER-fed (rows scrolling under the parked mouse write
        // their template into ReactiveTooltipTemplate), so it can show a race nobody selected. While
        // our UI drives, re-assert the SELECTION's template whenever the panel diverges — the same
        // UpdateTooltipTemplate() the game's selection path calls (see ClassPhaseContent.SyncGamePanel).
        private static readonly System.Reflection.FieldInfo TplRaceField =
            HarmonyLib.AccessTools.Field(
                typeof(Kingmaker.UI.MVVM._VM.Tooltip.Templates.TooltipTemplateLevelUpRace), "m_Race");

        private void SyncGamePanel()
        {
            var race = Phase.SelectedRaceVM.Value?.Race;
            if (race == null) return;
            var tpl = Phase.ReactiveTooltipTemplate.Value
                as Kingmaker.UI.MVVM._VM.Tooltip.Templates.TooltipTemplateLevelUpRace;
            if (tpl == null) return; // not a race page (glossary etc.) — leave it be
            if (!ReferenceEquals(TplRaceField?.GetValue(tpl), race))
                Phase.UpdateTooltipTemplate();
        }

        public override void Build(GraphBuilder b, string k)
        {
            SyncGamePanel();
            b.PushContext(Loc.T("chargen.races"), "list");
            int i = 0;
            if (Phase.RaceSelector?.EntitiesCollection != null)
                foreach (var item in Phase.RaceSelector.EntitiesCollection)
                {
                    if (item == null) { i++; continue; }
                    var it = item;
                    b.AddItem(ControlId.Referenced(it, k + "race:" + i),
                        GraphNodes.SelectionItem(it, () => it.DisplayName));
                    i++;
                }
            b.PopContext();

            b.BeginStop("gender").PushContext(Loc.T("chargen.gender"), "list");
            int gi = 0;
            if (Phase.GenderSelector?.EntitiesCollection != null)
                foreach (var g in Phase.GenderSelector.EntitiesCollection)
                {
                    if (g == null) { gi++; continue; }
                    var ge = g;
                    b.AddItem(ControlId.Referenced(ge, k + "gender:" + gi),
                        GraphNodes.SelectionItem(ge, () => ge.DisplayName));
                    gi++;
                }
            b.PopContext();

            // The detail as a document, computed LIVE from the SELECTION — never from the phase's
            // shared ReactiveTooltipTemplate: the game's race rows write their own template into that
            // reactive on MOUSE HOVER (CharGenRaceSelectorItemVM.TryShowTooltip), so it tracks whatever
            // the mouse happens to sit over, not the selected race. Keys carry race + gender, so a
            // change re-keys it (focus in the lists is untouched); glossary links follow on Space.
            var tpl = Phase.SelectedRaceVM.Value?.TooltipTemplate();
            if (tpl != null)
            {
                string dk = k + "detail:" + (Phase.SelectedRaceVM.Value?.GetHashCode() ?? 0)
                    + ":" + (Phase.SelectedGenderVM.Value?.GetHashCode() ?? 0) + ":";
                b.BeginStop("details").PushContext(Loc.T("chargen.details"), role: null, positions: false);
                TooltipFlowBuilder.Emit(b, dk, tpl, TooltipTemplateType.Info, includeEmptyNotice: false);
                b.PopContext();
            }
        }
    }
}
