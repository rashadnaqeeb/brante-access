using System;
using System.Collections.Generic;
using HarmonyLib;
using Kingmaker.UI.MVVM._VM.CharGen.Phases;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Skills;
using Kingmaker.UnitLogic.Class.LevelUp;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Skills phase: a "skill points remaining / max rank" line, then a grid of every skill (rows) by
    /// Rank / Modifier / Raise / Lower. Pathfinder skill points are flat — 1 point per rank, capped at
    /// the character's level (max rank) — so Raise/Lower are stepper buttons (the same window logic is
    /// reused at level-up, where ranks climb past 1). The skill name is annotated "(class skill)" where
    /// it applies; Space anywhere in the row opens the skill detail. Values read live from the preview
    /// stat + level-up state (synchronous with Spend/UnspendSkillPoint), so steps aren't stale.
    /// </summary>
    public sealed class SkillsPhaseContent : CharGenPhaseContent<CharGenSkillsPhaseVM>
    {
        private static readonly System.Reflection.FieldInfo ControllerField =
            AccessTools.Field(typeof(CharGenPhaseBaseVM), "m_LevelUpController");

        private readonly LevelUpController _controller;

        public SkillsPhaseContent(CharGenSkillsPhaseVM phase) : base(phase)
            => _controller = ControllerField?.GetValue(phase) as LevelUpController;

        public override void Build(GraphBuilder b, string k)
        {
            var sheet = new GraphSheet(b, k + "sk:");

            sheet.Region(null); // unlabelled status line
            sheet.Line(GraphNodes.Text(() => Loc.T("chargen.skill_points", new { points = Points(), rank = MaxRank() })));

            sheet.Region(Loc.T("section.skills"), new[]
            {
                Loc.T("col.rank"), Loc.T("col.modifier"), Loc.T("col.raise"), Loc.T("col.lower"),
            });
            foreach (var s in Phase.SkillAllocators)
            {
                if (s == null) continue;
                var sk = s; // capture for the live closures
                Action tooltip = () =>
                {
                    var tpl = sk.TooltipTemplate();
                    if (tpl != null) TooltipScreen.Open(tpl);
                };
                sheet.RowAt(
                    new NodeVtable
                    {
                        ControlType = ControlTypes.Text,
                        Announcements = new[] { GraphNodes.LabelPart(() => RowName(sk)) },
                        SearchText = () => sk.Name.Value,
                        OnTooltip = tooltip,
                    },
                    sk,
                    new[]
                    {
                        Cell(1, () => Rank(sk).ToString(), sk, tooltip),
                        Cell(2, () => Signed(Total(sk)), sk, tooltip),
                        Stepper(3, sk, raise: true, tooltip),
                        Stepper(4, sk, raise: false, tooltip),
                    });
            }
            sheet.Finish();
        }

        private KeyValuePair<int, NodeVtable> Cell(int col, Func<string> value,
            CharGenSkillAllocatorVM sk, Action tooltip)
            => new KeyValuePair<int, NodeVtable>(col, new NodeVtable
            {
                ControlType = ControlTypes.Text,
                Announcements = new[] { new NodeAnnouncement(value) },
                SearchText = () => sk.Name.Value,
                OnTooltip = tooltip, // Space on any cell in the row → skill detail
            });

        private KeyValuePair<int, NodeVtable> Stepper(int col, CharGenSkillAllocatorVM sk,
            bool raise, Action tooltip)
        {
            var vt = CharGenNodes.Stepper(
                raise ? (Func<string>)(() => RaiseLabel(sk)) : () => LowerLabel(sk),
                raise ? (Func<bool>)(() => CanAdd(sk)) : () => CanRemove(sk),
                raise ? (Action)sk.TryIncreaseValue : sk.TryDecreaseValue,
                () => Summary(sk));
            vt.SearchText = () => sk.Name.Value;
            vt.OnTooltip = tooltip;
            return new KeyValuePair<int, NodeVtable>(col, vt);
        }

        // Live reads (synchronous with Spend/UnspendSkillPoint), mirroring the allocator's own sources.
        private int Rank(CharGenSkillAllocatorVM s) => _controller?.Preview?.Stats?.GetStat(s.StatType)?.BaseValue ?? 0;
        private int Total(CharGenSkillAllocatorVM s) => _controller?.Preview?.Stats?.GetStat(s.StatType)?.ModifiedValue ?? 0;
        private int UnitRank(CharGenSkillAllocatorVM s) => _controller?.Unit?.Stats?.GetStat(s.StatType)?.BaseValue ?? 0;
        private int Points() => _controller != null ? _controller.State.SkillPointsRemaining : 0;
        private int MaxRank() => Phase.MaxRank != null ? Phase.MaxRank.Value : 1;

        private bool CanAdd(CharGenSkillAllocatorVM s) => Rank(s) < MaxRank() && Points() > 0;
        private bool CanRemove(CharGenSkillAllocatorVM s) => Rank(s) > UnitRank(s); // can't drop below committed ranks

        private string RowName(CharGenSkillAllocatorVM s)
            => s.Name.Value + (s.IsClassSkill.Value ? " (class skill)" : "");

        private string RaiseLabel(CharGenSkillAllocatorVM s)
            => Rank(s) >= MaxRank() ? "maximum ranks" : (Points() > 0 ? "1 point" : "no points");

        private string LowerLabel(CharGenSkillAllocatorVM s) => CanRemove(s) ? "1 point back" : "no ranks";

        private string Summary(CharGenSkillAllocatorVM s)
            => s.Name.Value + ", rank " + Rank(s) + ", " + Signed(Total(s)) + ", " + Points() + " points left";

        private static string Signed(int v) => v >= 0 ? "+" + v : v.ToString();
    }
}
