using System.Collections.Generic;
using System.Linq;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Progression.ChupaChupses; // ClassArchetypeDifType
using Kingmaker.UI.MVVM._VM.Tooltip.Bricks;
using Owlcat.Runtime.UI.Tooltips; // TooltipBaseBrickVM

namespace WrathAccess.UI.Tooltips
{
    // One renderer per game brick VM type. Register new ones in TooltipBrickRegistry.RegisterDefaults.
    // Single-line bricks implement only GetExpandedLines (flat falls back to it). Multi-line bricks
    // override GetFlatLines to condense.

    public sealed class TextBrickRenderer : TooltipBrickRenderer<TooltipBrickTextVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickTextVM vm) => One(vm?.Text);

    }

    public sealed class ColorizedTextBrickRenderer : TooltipBrickRenderer<TooltipBrickColorizedTextVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickColorizedTextVM vm) => One(vm?.Text);

    }

    public sealed class TitleBrickRenderer : TooltipBrickRenderer<TooltipBrickTitleVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickTitleVM vm) => One(vm?.Title);
    }

    public sealed class DoubleTextBrickRenderer : TooltipBrickRenderer<TooltipBrickDoubleTextVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickDoubleTextVM vm)
            => One(vm == null ? null : Join(vm.LeftLine, vm.RightLine));

    }

    // Item tooltip footer (weight | cost). The VM is a plain DoubleText subclass — same rendering,
    // but the registry dispatches on exact type, so the subclass needs its own registration.
    public sealed class ItemFooterBrickRenderer : TooltipBrickRenderer<TooltipBrickItemFooterVM>
    {
        private static readonly DoubleTextBrickRenderer Inner = new DoubleTextBrickRenderer();

        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickItemFooterVM vm)
            => Inner.GetExpandedLines(vm);

    }

    public sealed class TripleTextBrickRenderer : TooltipBrickRenderer<TooltipBrickTripleTextVM>
    {
        // Flat path: all three columns on one line. (Left/Right come from the DoubleTextVM base.)
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickTripleTextVM vm)
            => One(vm == null ? null : Join(vm.LeftLine, vm.MiddleLine, vm.RightLine));

    }

    public sealed class IconAndNameBrickRenderer : TooltipBrickRenderer<TooltipBrickIconAndNameVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickIconAndNameVM vm)
            => One(vm?.Line, () => vm?.Tooltip); // icon may carry a nested tooltip → drill-in
    }

    public sealed class PortraitAndNameBrickRenderer : TooltipBrickRenderer<TooltipBrickPortraitAndNameVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickPortraitAndNameVM vm) => One(vm?.Line);
    }

    public sealed class IconNameDescBrickRenderer : TooltipBrickRenderer<TooltipBrickIconNameDescVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickIconNameDescVM vm)
            => One(vm == null ? null : Join(vm.Name, vm.Desc));
    }

    public sealed class FeatureBrickRenderer : TooltipBrickRenderer<TooltipBrickFeatureVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickFeatureVM vm)
            => One(vm?.Name, () => vm?.Tooltip); // full feature/ability writeup on drill-in
    }

    public sealed class MultipleFeatureBrickRenderer : TooltipBrickRenderer<TooltipBrickMultipleFeatureVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickMultipleFeatureVM vm)
        {
            if (vm?.TooltipBrickFeatures == null) yield break;
            foreach (var f in vm.TooltipBrickFeatures)
                if (f != null) yield return new BrickLine(f.Name, () => f.Tooltip);
        }

        public override IEnumerable<BrickLine> GetFlatLines(TooltipBrickMultipleFeatureVM vm)
            => One(vm?.TooltipBrickFeatures == null ? null
                : Join(vm.TooltipBrickFeatures.Where(f => f != null).Select(f => f.Name).ToArray()));
    }

    // A per-level feature in the archetype/first-level progression: like a feature (name + drill-in
    // write-up) plus the archetype's Added/Removed marker. It subclasses TooltipBrickFeatureVM, but
    // the registry keys on exact type, so it needs its own renderer (it won't reuse FeatureBrick's).
    public sealed class ArchetypeFeatureBrickRenderer : TooltipBrickRenderer<TooltipBrickArchetypeFeatureVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickArchetypeFeatureVM vm)
        {
            if (vm == null || string.IsNullOrWhiteSpace(vm.Name)) return None;
            // The archetype's Added/Removed marker — e.g. "Bombs (removed)" — so archetype changes
            // read in the tooltip.
            string text = vm.Name;
            if (vm.DifType == ClassArchetypeDifType.Added) text += " (" + Loc.T("tooltip.archetype_added") + ")";
            else if (vm.DifType == ClassArchetypeDifType.Removed) text += " (" + Loc.T("tooltip.archetype_removed") + ")";
            return One(text, () => vm.Tooltip);
        }

    }

    public sealed class FeatureShortDescriptionBrickRenderer : TooltipBrickRenderer<TooltipBrickFeatureShortDescriptionVM>
    {
        // Name + short desc, with the feature's full write-up as a drill-in (e.g. signature features).
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickFeatureShortDescriptionVM vm)
            => One(vm == null ? null : Join(vm.Name, vm.Description), () => vm?.Tooltip);
    }

    public sealed class EntityHeaderBrickRenderer : TooltipBrickRenderer<TooltipBrickEntityHeaderVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickEntityHeaderVM vm)
            => One(vm == null ? null : Join(vm.MainTitle, vm.Title, vm.LeftLabel, vm.RightLabel));
    }

    public sealed class IconValueStatBrickRenderer : TooltipBrickRenderer<TooltipBrickIconValueStatVM>
    {
        // The Inverted flag swaps which component shows Name vs Value on screen
        // (TooltipBrickIconValueStatView.SetupValues) — mirror it so the pair reads the right way.
        internal static string Line(TooltipBrickIconValueStatVM vm)
            => vm.Type.HasFlag(TooltipIconValueStatType.Inverted)
                ? Stat(vm.Value, vm.Name, vm.Icon)
                : Stat(vm.Name, vm.Value, vm.Icon);

        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickIconValueStatVM vm)
            => One(vm == null ? null : Line(vm), () => vm?.Tooltip);
    }

    public sealed class MultipleIconValueStatBrickRenderer : TooltipBrickRenderer<TooltipBrickMultipleIconValueStatVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickMultipleIconValueStatVM vm)
        {
            if (vm?.TooltipBrickIconValueStats == null) yield break;
            foreach (var s in vm.TooltipBrickIconValueStats)
                if (s != null) yield return new BrickLine(IconValueStatBrickRenderer.Line(s), () => s.Tooltip);
        }

        public override IEnumerable<BrickLine> GetFlatLines(TooltipBrickMultipleIconValueStatVM vm)
            => One(vm?.TooltipBrickIconValueStats == null ? null
                : Join(vm.TooltipBrickIconValueStats.Where(s => s != null).Select(IconValueStatBrickRenderer.Line).ToArray()));
    }

    public sealed class ValueStatFormulaBrickRenderer : TooltipBrickRenderer<TooltipBrickValueStatFormulaVM>
    {
        // The view lays out [Value][Symbol][Name] left-to-right (e.g. "+11 = Attack bonus") — read in
        // that screen order, Symbol included (it was being dropped).
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickValueStatFormulaVM vm)
            => One(vm == null ? null : string.Join(" ",
                new[] { vm.Value, vm.Symbol, vm.Name }.Where(x => !string.IsNullOrWhiteSpace(x))));
    }

    public sealed class TwoColumnsStatBrickRenderer : TooltipBrickRenderer<TooltipBrickTwoColumnsStatVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickTwoColumnsStatVM vm)
            => One(vm == null ? null : Join(Stat(vm.NameLeft, vm.ValueLeft, vm.IconLeft), Stat(vm.NameRight, vm.ValueRight, vm.IconRight)));

    }

    public sealed class ThreeColumnsStatBrickRenderer : TooltipBrickRenderer<TooltipBrickThreeColumnsStatVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickThreeColumnsStatVM vm)
            => One(vm == null ? null : Join(Stat(vm.NameLeft, vm.ValueLeft, vm.IconLeft),
                Stat(vm.NameCenter, vm.ValueCenter, vm.IconCenter), Stat(vm.NameRight, vm.ValueRight, vm.IconRight)));

    }

    public sealed class AbilityTargetBrickRenderer : TooltipBrickRenderer<TooltipBrickAbilityTargetVM>
    {
        // Label + text, with its drill-in tooltip.
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickAbilityTargetVM vm)
            => One(vm == null ? null : Join(vm.Label, vm.Text), () => vm?.Tooltip);
    }

    public sealed class PictureBrickRenderer : TooltipBrickRenderer<TooltipBrickPictureVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickPictureVM vm)
            => One(vm?.Picture != null ? Loc.T("tooltip.image", new { name = vm.Picture.name }) : null);
    }

    // Composite class build: difficulty, build-balance axes, then the description.
    public sealed class ShortClassDescriptionBrickRenderer : TooltipBrickRenderer<TooltipBrickShortClassDescriptionVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickShortClassDescriptionVM vm)
        {
            if (vm == null) yield break;

            var rate = vm.DifficultyRateVM;
            if (rate != null && !string.IsNullOrEmpty(rate.RateName))
                yield return new BrickLine(Loc.T("tooltip.rate",
                    new { name = rate.RateName, rate = rate.Rate, max = rate.MaxRate }));

            var bal = vm.ClassBalanceVM != null ? vm.ClassBalanceVM.ClassBalanceVM : null;
            if (bal != null)
                yield return new BrickLine(ClassBalanceBrickRenderer.Line(bal,
                    !string.IsNullOrEmpty(vm.BuildTitle) ? vm.BuildTitle : Loc.T("tooltip.build_balance")));

            string desc = vm.ClassDescriptionVM != null ? vm.ClassDescriptionVM.Text : null;
            if (!string.IsNullOrWhiteSpace(desc)) yield return new BrickLine(desc);
        }
    }

    public sealed class AbilityScoresBlockBrickRenderer : TooltipBrickRenderer<TooltipBrickAbilityScoresBlockVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickAbilityScoresBlockVM vm)
        {
            var stats = vm?.AbilityScoresBlock?.AbilityScores;
            if (stats == null) yield break;
            foreach (var s in stats) { var line = StatLine(s); if (!string.IsNullOrWhiteSpace(line)) yield return new BrickLine(line, () => s.Tooltip); }
        }

        public override IEnumerable<BrickLine> GetFlatLines(TooltipBrickAbilityScoresBlockVM vm)
            => One(vm?.AbilityScoresBlock?.AbilityScores == null ? null
                : Join(vm.AbilityScoresBlock.AbilityScores.Select(StatLine).ToArray()));
    }

    public sealed class SkillsBrickRenderer : TooltipBrickRenderer<TooltipBrickSkillsVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickSkillsVM vm)
        {
            var stats = vm?.Skills?.Skills;
            if (stats == null) yield break;
            foreach (var s in stats) { var line = StatLine(s); if (!string.IsNullOrWhiteSpace(line)) yield return new BrickLine(line, () => s.Tooltip); }
        }

        public override IEnumerable<BrickLine> GetFlatLines(TooltipBrickSkillsVM vm)
            => One(vm?.Skills?.Skills == null ? null : Join(vm.Skills.Skills.Select(StatLine).ToArray()));
    }

    // Spells-per-day for one character level: Values is indexed by spell level (0 = cantrips, an
    // "infinity" sprite = at will; 1..max = the per-day count). One node per spell level, labeled
    // with word ordinals ("First", "Second", …) — no "level", to avoid clashing with character level.
    public sealed class SpellTableBrickRenderer : TooltipBrickRenderer<TooltipBrickSpellTableVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickSpellTableVM vm)
        {
            if (vm?.Values == null) yield break;
            for (int i = 0; i < vm.Values.Count; i++)
            {
                if (string.IsNullOrEmpty(vm.Values[i])) continue; // no spells of this level
                string label = i <= 10 ? Loc.T("spell_ordinal." + i) : i.ToString();
                string value = i == 0 ? Loc.T("tooltip.at_will") : vm.Values[i]; // cantrips: infinity sprite
                yield return new BrickLine(label + ": " + value);
            }
        }
    }

    // Racial (and similar) ability-score modifiers: one entry per ability, but only the ones the
    // source actually changes carry a value. Read those as "Strength: +2" / "Constitution: -2", each
    // with the stat's glossary drill-in; skip the unmodified abilities.
    public sealed class AbilityScoreBonusesBrickRenderer : TooltipBrickRenderer<TooltipBrickAbilityScoreBonusesVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickAbilityScoreBonusesVM vm)
        {
            if (vm?.UIStatBonuses == null) yield break;
            foreach (var s in vm.UIStatBonuses)
            {
                if (s == null || !s.IsValueEnabled) continue;
                int v = s.BonusValue;
                yield return new BrickLine(s.DisplayName + ": " + (v >= 0 ? "+" + v : v.ToString()), () => s.Tooltip);
            }
        }
    }

    // A feat/feature's prerequisites: one line per requirement with whether it's met, e.g.
    // "Strength 13 (met)", "Power Attack (not met)".
    public sealed class PrerequisiteBrickRenderer : TooltipBrickRenderer<TooltipBrickPrerequisiteVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickPrerequisiteVM vm)
        {
            if (vm?.PrerequisiteEntries == null) yield break;
            foreach (var e in vm.PrerequisiteEntries)
            {
                if (e == null || string.IsNullOrWhiteSpace(e.Text)) continue;
                yield return new BrickLine(e.Text + " (" + Loc.T(e.Done ? "tooltip.met" : "tooltip.not_met") + ")");
            }
        }
    }

    // Pure layout — nothing to read.
    public sealed class SeparatorBrickRenderer : TooltipBrickRenderer<TooltipBrickSeparatorVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickSeparatorVM vm) => None;
    }

    public sealed class SpaceBrickRenderer : TooltipBrickRenderer<TooltipBrickSpaceVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickSpaceVM vm) => None;
    }
    // ---- The unit-inspect stat blocks (creature pages / Inspect): each wraps the same CharInfo
    // VMs the character sheet renders — one "Name: Value" line per stat, with the stat's modifier-
    // breakdown / glossary tooltip (CharInfoStatVM.Tooltip) drilled in on Space, same as the char sheet. ----

    public sealed class AbilityScoresBrickRenderer : TooltipBrickRenderer<TooltipBrickAbilityScoresVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickAbilityScoresVM vm)
        {
            var stats = vm?.AbilityScoresBlock?.AbilityScores;
            if (stats == null) yield break;
            foreach (var s in stats) { var line = StatLine(s); if (!string.IsNullOrWhiteSpace(line)) yield return new BrickLine(line, () => s.Tooltip); }
        }

        public override IEnumerable<BrickLine> GetFlatLines(TooltipBrickAbilityScoresVM vm)
            => One(vm?.AbilityScoresBlock?.AbilityScores == null ? null
                : Join(vm.AbilityScoresBlock.AbilityScores.Select(StatLine).ToArray()));
    }

    public sealed class ArmorClassBrickRenderer : TooltipBrickRenderer<TooltipBrickArmorClassVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickArmorClassVM vm)
        {
            var ac = vm?.ArmorClass;
            if (ac == null) yield break;
            foreach (var s in new[] { ac.AC, ac.FlatFooted, ac.Touch })
            { var line = StatLine(s); if (!string.IsNullOrWhiteSpace(line)) yield return new BrickLine(line, () => s.Tooltip); }
        }
    }

    public sealed class SavingThrowBrickRenderer : TooltipBrickRenderer<TooltipBrickSavingThrowVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickSavingThrowVM vm)
        {
            var st = vm?.SavingThrowVM;
            if (st == null) yield break;
            foreach (var s in new[] { st.Fortitude, st.Reflex, st.Will })
            { var line = StatLine(s); if (!string.IsNullOrWhiteSpace(line)) yield return new BrickLine(line, () => s.Tooltip); }
        }
    }

    public sealed class SizeSpeedInitiativeBrickRenderer : TooltipBrickRenderer<TooltipBrickSizeSpeedInitiativeVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickSizeSpeedInitiativeVM vm)
        {
            if (vm == null) yield break;
            foreach (var s in new[] { vm.Size, vm.Speed, vm.Initiative })
            { var line = StatLine(s); if (!string.IsNullOrWhiteSpace(line)) yield return new BrickLine(line, () => s.Tooltip); }
        }
    }

    // A list of value-stat-formula bricks (e.g. the inspect view's attack lines) — delegate each
    // child to its own registered renderer.
    public sealed class MultipleValueStatFormulaBrickRenderer : TooltipBrickRenderer<TooltipBrickMultipleValueStatFormulaVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickMultipleValueStatFormulaVM vm)
        {
            if (vm?.TooltipBrickValueStatFormulas == null) yield break;
            foreach (var f in vm.TooltipBrickValueStatFormulas)
                foreach (var el in TooltipBrickRegistry.Lines(f, expanded: true)) yield return el;
        }

    }

}
