using System;
using System.Collections.Generic;
using HarmonyLib;
using Kingmaker.UI.MVVM._VM.CharGen.Phases;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.AbilityScores;
using Kingmaker.UnitLogic.Class.LevelUp;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Ability Scores (point-buy) phase. A live "points remaining" line, then a grid of the six
    /// abilities (rows) by Score / Modifier / Race bonus / Raise / Lower (columns). Score/Modifier/Race
    /// bonus are read-only (Space anywhere in the row opens the stat detail); Raise/Lower are the game's
    /// stepper buttons — they read the point cost and Enter to spend/refund, re-announcing the new score
    /// and remaining points. All numbers read the live point-buy state (synchronous with the step), not
    /// the LateUpdate-lagged reactives. A race-bonus chooser (which stat gets the racial +2) appears
    /// below when the race offers one (immediate mode — it renders whenever available).
    /// </summary>
    public sealed class AbilityScoresPhaseContent : CharGenPhaseContent<CharGenAbilityScoresVM>
    {
        // The controller (and thus the live point-buy model) isn't exposed publicly on the phase.
        private static readonly System.Reflection.FieldInfo ControllerField =
            AccessTools.Field(typeof(CharGenPhaseBaseVM), "m_LevelUpController");

        private readonly LevelUpController _controller;

        public AbilityScoresPhaseContent(CharGenAbilityScoresVM phase) : base(phase)
            => _controller = ControllerField?.GetValue(phase) as LevelUpController;

        public override void Build(GraphBuilder b, string k)
        {
            var sheet = new GraphSheet(b, k + "abl:");

            sheet.Region(null); // unlabelled status line (self-describing, single cell)
            sheet.Line(GraphNodes.Text(() => Loc.T("chargen.points_remaining", new { value = Points() })));

            sheet.Region(Loc.T("section.ability_scores"), new[]
            {
                Loc.T("col.score"), Loc.T("col.modifier"), Loc.T("col.race_bonus"),
                Loc.T("col.raise"), Loc.T("col.lower"),
            });
            foreach (var a in Phase.AbilityScoreAllocators)
            {
                if (a == null) continue;
                var alloc = a; // capture for the live closures
                Action tooltip = () =>
                {
                    var tpl = alloc.TooltipTemplate();
                    if (tpl != null) TooltipScreen.Open(tpl);
                };
                sheet.RowAt(
                    new NodeVtable
                    {
                        ControlType = ControlTypes.Text,
                        Announcements = new[] { GraphNodes.LabelPart(() => alloc.Name.Value) },
                        SearchText = () => alloc.Name.Value,
                        OnTooltip = tooltip,
                    },
                    alloc,
                    new[]
                    {
                        Cell(1, () => alloc.StatValue.Value.ToString(), alloc, tooltip),
                        Cell(2, () => Signed(alloc.Bonus.Value), alloc, tooltip),
                        Cell(3, () => alloc.RaceBonus.Value.HasValue ? Signed(alloc.RaceBonus.Value.Value) : "", alloc, tooltip),
                        Stepper(4, alloc, raise: true, tooltip),
                        Stepper(5, alloc, raise: false, tooltip),
                    });
            }
            sheet.Finish();

            // The race ability-bonus chooser only exists for races that let you pick where the +2 goes.
            if (Phase.RaceBonusAvailable != null && Phase.RaceBonusAvailable.Value)
                b.AddItem(ControlId.Structural(k + "racebonus"),
                    CharGenNodes.SequentialSelector("Racial ability bonus",
                        () => Phase.RaceBonusSelector != null ? Phase.RaceBonusSelector.Value : null));
        }

        private KeyValuePair<int, NodeVtable> Cell(int col, Func<string> value,
            CharGenAbilityScoreAllocatorVM alloc, Action tooltip)
            => new KeyValuePair<int, NodeVtable>(col, new NodeVtable
            {
                ControlType = ControlTypes.Text,
                Announcements = new[] { new NodeAnnouncement(value) },
                SearchText = () => alloc.Name.Value,
                OnTooltip = tooltip, // Space on any cell in the row → stat detail
            });

        private KeyValuePair<int, NodeVtable> Stepper(int col, CharGenAbilityScoreAllocatorVM alloc,
            bool raise, Action tooltip)
        {
            var vt = CharGenNodes.Stepper(
                () => CostLabel(alloc, raise),
                () => CanAct(alloc, raise),
                raise ? (Action)alloc.TryIncreaseValue : alloc.TryDecreaseValue,
                () => Summary(alloc));
            vt.SearchText = () => alloc.Name.Value;
            vt.OnTooltip = tooltip;
            return new KeyValuePair<int, NodeVtable>(col, vt);
        }

        // Live reads from the point-buy model (falling back to the allocator's reactive only when not in
        // point-buy mode), so the grid never shows a stale value after a step.
        private StatsDistribution Dist => _controller?.State?.StatsDistribution;
        private bool PointBuy => Dist != null && Dist.Available;

        private int Points() => PointBuy ? Dist.Points : (_controller != null ? _controller.State.AttributePoints : 0);

        // The live point-buy score for one ability (base, pre-racial) — from StatsDistribution, which
        // updates synchronously with AddStatPoint (the allocator's reactive only catches up on LateUpdate).
        private int Score(CharGenAbilityScoreAllocatorVM a)
            => (PointBuy && Dist.StatValues.TryGetValue(a.StatType, out var v)) ? v : a.StatValue.Value;

        private bool CanAct(CharGenAbilityScoreAllocatorVM a, bool raise) => raise
            ? (PointBuy ? Dist.CanAdd(a.StatType) : (_controller != null && _controller.State.AttributePoints > 0))
            : (PointBuy ? Dist.CanRemove(a.StatType) : a.CanRemove.Value);

        // The point cost shown on the stepper — or, when you can't raise, why ("maximum" at 18, or
        // "N points, need M more"); lowering shows the refund ("minimum" at 7, or "N points back").
        private string CostLabel(CharGenAbilityScoreAllocatorVM a, bool raise)
        {
            if (raise)
            {
                if (Score(a) >= 18) return L("value.maximum");
                int cost = PointBuy ? Dist.GetAddCost(a.StatType) : 1;
                if (CanAct(a, raise: true)) return L("value.points", new { count = cost });
                int need = cost - Points();                 // tell them how short they are
                return L("value.points_need_more", new { count = cost, need = need > 0 ? need : 1 });
            }
            if (Score(a) <= 7) return L("value.minimum");
            int refund = PointBuy ? -Dist.GetRemoveCost(a.StatType) : 1; // GetRemoveCost is negative (points returned)
            return L("value.points_back", new { count = refund });
        }

        // Spoken after a step: the now-current total score (incl. racial), its modifier, and the
        // remaining pool — computed from the live base + racial so it's fresh the instant you step.
        private string Summary(CharGenAbilityScoreAllocatorVM a)
        {
            int total = Score(a) + (a.RaceBonus.Value ?? 0);
            int mod = (int)Math.Floor((total - 10) / 2.0);
            return L("value.ability_summary", new { name = a.Name.Value, total, mod = Signed(mod), points = Points() });
        }

        private static string Signed(int v) => v >= 0 ? "+" + v : v.ToString();
        private static string L(string key) => Message.Localized("ui", key).Resolve();
        private static string L(string key, object vars) => Message.Localized("ui", key, vars).Resolve();
    }
}
