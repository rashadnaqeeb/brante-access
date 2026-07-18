using System.Collections.Generic;
using System.Linq;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Pregen;
using WrathAccess.UI;
using WrathAccess.UI.Graph;
using WrathAccess.UI.Tooltips;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Pregen phase: the premade-character list (ending with Custom Character) + a live Details panel —
    /// the selected option's build, rendered as a document from its tooltip, keyed per selection (so
    /// picking re-keys the detail while list focus stays put).
    /// </summary>
    public sealed class PregenPhaseContent : CharGenPhaseContent<CharGenPregenPhaseVM>
    {
        public PregenPhaseContent(CharGenPregenPhaseVM phase) : base(phase) { }

        public override void Build(GraphBuilder b, string k)
        {
            int i = 0;
            foreach (var item in Phase.PregenSelectionGroup.EntitiesCollection)
            {
                if (item == null) { i++; continue; }
                var it = item; // capture for the live closures
                // Label = name, race, class, role (skipping blanks). Details (class description +
                // features) are the selected character's, shown by the phase's InfoVM as a drill-in.
                b.AddItem(ControlId.Referenced(it, k + "pregen:" + i),
                    GraphNodes.SelectionItem(it,
                        () => string.Join(", ", new[] { it.CharacterName.Value, it.Race.Value, it.Class.Value, it.Role.Value }
                            .Where(p => !string.IsNullOrWhiteSpace(p))),
                        tooltip: () => it.IsSelected.Value && Phase.InfoVM != null ? Phase.InfoVM.CurrentTooltip : null));
                i++;
            }
            // "Custom Character" — the final option; selecting it branches into the full custom flow.
            b.AddItem(ControlId.Structural(k + "custom"), new NodeVtable
            {
                ControlType = ControlTypes.RadioButton,
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(() => Loc.T("label.custom_character")),
                    GraphNodes.SelectedPart(() => Phase.IsCustomCharacter.Value),
                },
                SearchText = () => Loc.T("label.custom_character"),
                StateText = () => Phase.IsCustomCharacter.Value ? Loc.T("state.selected") : null,
                OnActivate = () =>
                {
                    UiSound.Play(Kingmaker.UI.UISoundType.ButtonClick);
                    Phase.SelectCreateCustomCharacter();
                },
            });

            // The selected pregen's build, as a document keyed per selection.
            var tpl = Phase.InfoVM != null ? Phase.InfoVM.CurrentTooltip : null;
            if (tpl != null)
            {
                string dk = k + "detail:" + (Phase.SelectedPregenEntity.Value?.GetHashCode() ?? 0) + ":";
                b.BeginStop("details").PushContext(Loc.T("chargen.details"), role: null, positions: false);
                TooltipFlowBuilder.Emit(b, dk, tpl, includeEmptyNotice: false);
                b.PopContext();
            }
        }
    }
}
