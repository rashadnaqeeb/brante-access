using System;
using System.Collections.Generic;
using Kingmaker.Blueprints.Root;                 // BlueprintRoot.Instance.Dialog.SkillCheckTooltipID
using Kingmaker.Controllers.Dialog;              // SkillCheckResult, SkillCheckDC
using Kingmaker.EntitySystem.Stats;              // StatType
using Kingmaker.UI.MVVM._VM.Tooltip.Templates;   // TooltipTemplateSkillCheck{Result,DC}
using Kingmaker.UI.Tooltip;                      // TooltipType
using Owlcat.Runtime.UI.Tooltips;

namespace WrathAccess.UI.Proxies
{
    /// <summary>
    /// Resolves the two dialogue-only inline link kinds the glossary can't — a skill-check RESULT
    /// (on a cue, after a roll: DC / d20 / total / pass-fail) and a skill-check DC PREVIEW (on an
    /// answer: the chance per character). Mirrors the game's own <c>TooltipTriggerGlossary</c> dispatch:
    /// the link only flags its kind via the parsed keys, and the actual check data comes from the
    /// cue/answer VM. Returns null for anything else (glossary links fall through to the standard path).
    /// </summary>
    public static class DialogLinks
    {
        public static TooltipBaseTemplate ResolveSkillCheck(string[] keys,
            List<SkillCheckResult> results, List<SkillCheckDC> dcs)
        {
            if (keys == null) return null;

            // Result link (cue): keyed by the dialog root's SkillCheckTooltipID.
            if (results != null && results.Count > 0
                && Array.IndexOf(keys, BlueprintRoot.Instance.Dialog.SkillCheckTooltipID) >= 0)
                return new TooltipTemplateSkillCheckResult(results, keys);

            // DC-preview link (answer): keyed by "SkillcheckDC" + a StatType name; show that stat's checks.
            if (dcs != null && dcs.Count > 0
                && Array.IndexOf(keys, TooltipType.SkillcheckDC.ToString()) >= 0)
            {
                foreach (var k in keys)
                    if (Enum.TryParse(k, out StatType st))
                    {
                        var forStat = dcs.FindAll(d => d.StatType == st);
                        if (forStat.Count > 0) return new TooltipTemplateSkillCheckDC(forStat);
                    }
            }
            return null;
        }
    }
}
