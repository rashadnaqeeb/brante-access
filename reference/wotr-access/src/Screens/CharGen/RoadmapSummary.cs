using System.Text;
using Kingmaker.UI.Common; // UIUtility.GetAlignmentName
using Kingmaker.UI.MVVM._VM.CharGen.Phases;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Class;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Mythic;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Race;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.AbilityScores;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Skills;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.FeatureSelector;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Alignment;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Name;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Spells;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The live summary each roadmap entry shows, mirroring the per-phase roadmap views
    /// (CharGenXPhaseRoadmapPCView). Read live, so it always reflects current choices. Visual-only
    /// phases (Pregen/Portrait show a portrait; Voice/Appearance/Total show nothing extra) return null.
    /// </summary>
    internal static class RoadmapSummary
    {
        public static string For(CharGenPhaseBaseVM phase)
        {
            switch (phase)
            {
                case CharGenClassPhaseVM c:
                {
                    var sel = c.SelectedClassVM.Value;
                    if (sel?.Class == null) return null;
                    return sel.Archetype != null ? sel.Class.Name + ", " + sel.Archetype.Name : sel.Class.Name;
                }

                case CharGenMythicPhaseVM m:
                    return m.SelectedMythicVM.Value?.Class?.Name;

                case CharGenRacePhaseVM r:
                {
                    string race = r.SelectedRaceVM.Value?.DisplayName;
                    string gender = r.SelectedGenderVM.Value?.DisplayName;
                    if (string.IsNullOrEmpty(race)) return gender;
                    return string.IsNullOrEmpty(gender) ? race : race + ", " + gender;
                }

                case CharGenAbilityScoresVM a:
                {
                    var sb = new StringBuilder();
                    foreach (var al in a.AbilityScoreAllocators)
                    {
                        if (al == null) continue;
                        if (sb.Length > 0) sb.Append(", ");
                        sb.Append(al.Name.Value).Append(' ').Append(al.StatValue.Value);
                    }
                    if (!a.IsPointsAllocated && a.Points != null && a.Points.Value > 0)
                        sb.Append(", ").Append(a.Points.Value).Append(" points unspent");
                    return sb.Length > 0 ? sb.ToString() : null;
                }

                case CharGenSkillsPhaseVM s:
                    return (!s.IsPointsAllocated && s.Points != null && s.Points.Value > 0)
                        ? s.Points.Value + " skill points unspent" : null;

                case CharGenFeatureSelectorPhaseVM f:
                    return f.LastSelectedItemVM.Value?.FeatureName;

                case CharGenAlignmentPhaseVM al:
                {
                    var sel = al.SelectedAlignmentVM.Value;
                    return sel != null ? UIUtility.GetAlignmentName(sel.Alignment) : null;
                }

                case CharGenNamePhaseVM n:
                    return string.IsNullOrEmpty(n.ChosenName.Value) ? null : n.ChosenName.Value;

                case CharGenSpellsPhaseVM sp:
                {
                    int chosen = sp.SelectedSpellVMs?.Count ?? 0;
                    string head = sp.SelectorLevel >= 0 ? "level " + sp.SelectorLevel : null;
                    if (chosen > 0) head = string.IsNullOrEmpty(head) ? chosen + " chosen" : head + ", " + chosen + " chosen";
                    return head;
                }

                default:
                    return null; // Pregen, Portrait, Voice, Appearance, Total — visual-only / no summary
            }
        }
    }
}
