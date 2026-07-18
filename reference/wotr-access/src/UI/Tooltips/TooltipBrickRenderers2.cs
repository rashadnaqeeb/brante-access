using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints.Root.Strings; // UIStrings (game-localized headers)
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Class.ShortDescription; // ClassBalanceVM
using Kingmaker.UI.MVVM._VM.Tooltip.Bricks;
using Owlcat.Runtime.UI.Tooltips;

namespace WrathAccess.UI.Tooltips
{
    // Renderers added by the 2026-07-04 brick audit (all 54 game brick VM types vs the registry).
    // Same contract as TooltipBrickRenderers: mirror what the game's brick PC VIEW shows, nothing more.

    /// <summary>A filled-pips rating ("Difficulty: 3 of 5") — the standalone form of the rate bar
    /// ShortClassDescription embeds.</summary>
    public sealed class RateBrickRenderer : TooltipBrickRenderer<TooltipBrickRateVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickRateVM vm)
            => vm == null ? None
             : string.IsNullOrEmpty(vm.RateName)
                ? One(Loc.T("tooltip.rate_bare", new { rate = vm.Rate, max = vm.MaxRate }))
                : One(Loc.T("tooltip.rate", new { name = vm.RateName, rate = vm.Rate, max = vm.MaxRate }));
    }

    /// <summary>The class build-balance radar as text ("Build balance: Melee 0, Ranged 2, …").</summary>
    public sealed class ClassBalanceBrickRenderer : TooltipBrickRenderer<TooltipBrickClassBalanceVM>
    {
        internal static string Line(ClassBalanceVM bal, string title)
            => title + ": Melee " + bal.Melee.Value + ", Ranged " + bal.Ranged.Value
             + ", Defense " + bal.Defense.Value + ", Support " + bal.Support.Value
             + ", Control " + bal.Control.Value + ", Magic " + bal.Magic.Value;

        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickClassBalanceVM vm)
            => vm?.ClassBalanceVM == null ? None
             : One(Line(vm.ClassBalanceVM, Loc.T("tooltip.build_balance")));
    }

    /// <summary>A button brick's label (read-only mention; the game buttons here are mouse-side).</summary>
    public sealed class ButtonBrickRenderer : TooltipBrickRenderer<TooltipBrickButtonVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickButtonVM vm) => One(vm?.Text);
    }

    /// <summary>A live-updating timer line (corruption clock etc.) — resolved at read time.</summary>
    public sealed class TimerBrickRenderer : TooltipBrickRenderer<TooltipBrickTimerVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickTimerVM vm)
            => One(vm?.TextFunc != null ? vm.TextFunc() : null);
    }

    /// <summary>A buff row: name + duration, with the buff's write-up on drill-in. The game HIDES the
    /// whole brick while the duration string is empty (TooltipBrickBuffView) — mirror that.</summary>
    public sealed class BuffBrickRenderer : TooltipBrickRenderer<TooltipBrickBuffVM>
    {
        internal static IEnumerable<BrickLine> Lines(TooltipBrickBuffVM vm)
        {
            if (vm == null) yield break;
            string dur = vm.Duration != null ? vm.Duration.Value : null;
            if (string.IsNullOrEmpty(dur)) yield break; // the view hides the brick entirely then
            yield return new BrickLine(Join(vm.Name, dur), () => vm.Tooltip);
        }

        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickBuffVM vm) => Lines(vm);
    }

    public sealed class MultipleBuffBrickRenderer : TooltipBrickRenderer<TooltipBrickMultipleBuffVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickMultipleBuffVM vm)
        {
            if (vm?.TooltipBrickBuff == null) yield break;
            foreach (var b in vm.TooltipBrickBuff)
                foreach (var line in BuffBrickRenderer.Lines(b)) yield return line;
        }
    }

    /// <summary>A crusade army effect — a buff-shaped row (name + duration + write-up). Registry keys
    /// on exact type, so the BuffVM subclass needs its own registration.</summary>
    public sealed class ArmyEffectBrickRenderer : TooltipBrickRenderer<TooltipBrickArmyEffectVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickArmyEffectVM vm)
        {
            if (vm == null) yield break;
            string dur = vm.Duration != null ? vm.Duration.Value : null;
            yield return new BrickLine(Join(vm.Name, dur), () => vm.Tooltip);
        }
    }

    /// <summary>The "does not stack with" list: the game's own header, then one line per entity.</summary>
    public sealed class NonStackBrickRenderer : TooltipBrickRenderer<TooltipBrickNonStackVm>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickNonStackVm vm)
        {
            if (vm?.Entities == null || vm.Entities.Count == 0) yield break;
            yield return new BrickLine((string)UIStrings.Instance.Tooltips.NonStackHeaderLabel);
            foreach (var e in vm.Entities)
                if (e != null && !string.IsNullOrEmpty(e.Name)) yield return new BrickLine(e.Name);
        }
    }

    /// <summary>The change-visual source item: the game's header + the item's name.</summary>
    public sealed class ChangeVisualBrickRenderer : TooltipBrickRenderer<TooltipBrickChangeVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickChangeVM vm)
        {
            if (vm?.VisualSourceItem == null || string.IsNullOrEmpty(vm.VisualSourceItem.Name)) yield break;
            yield return new BrickLine((string)UIStrings.Instance.ChangeVisual.ChangeVisualTargetItem);
            yield return new BrickLine(vm.VisualSourceItem.Name);
        }
    }

    /// <summary>Base attack bonus ("Base Attack Bonus: +6/+1") with the stat drill-in — the same
    /// composition the char sheet's BAB rows use (CharInfoBABView.FillData).</summary>
    public sealed class BabHorizontalBrickRenderer : TooltipBrickRenderer<TooltipBrickBabHorizontalVM>
    {
        internal static IEnumerable<BrickLine> Lines(TooltipBrickBabHorizontalVM vm)
        {
            var bab = vm?.BabVm;
            if (bab == null) yield break;
            yield return new BrickLine(
                (string)UIStrings.Instance.CharacterSheet.BAB + ": " + Screens.CharacterInfoScreen.BabString(bab),
                () => bab.Tooltip);
        }

        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickBabHorizontalVM vm) => Lines(vm);
    }

    public sealed class BabVerticalBrickRenderer : TooltipBrickRenderer<TooltipBrickBabVerticalVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickBabVerticalVM vm)
            => BabHorizontalBrickRenderer.Lines(vm);
    }

    /// <summary>A kingdom morale event flag: its name + description (what the flag view shows).</summary>
    public sealed class KingdomMoraleFlagBrickRenderer : TooltipBrickRenderer<TooltipBrickKingdomMoraleFlagVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickKingdomMoraleFlagVM vm)
        {
            var bp = vm?.FlagVM?.KingdomMoraleFlag?.Blueprint;
            if (bp == null) yield break;
            if (!string.IsNullOrEmpty((string)bp.Name)) yield return new BrickLine((string)bp.Name);
            if (!string.IsNullOrEmpty((string)bp.Description)) yield return new BrickLine((string)bp.Description);
        }
    }

    /// <summary>The kingdom morale bar: the current value (the pips/gradient are visual only).</summary>
    public sealed class KingdomMoraleBarBrickRenderer : TooltipBrickRenderer<TooltipBrickKingdomMoraleBarVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickKingdomMoraleBarVM vm)
            => vm == null ? None : One(Loc.T("tooltip.morale", new { value = vm.CurrentValue }));
    }

    /// <summary>Interactive-only bricks with no readable content — silent (not a fallback warning).</summary>
    public sealed class HistoryManagementBrickRenderer : TooltipBrickRenderer<TooltipBrickHistoryManagementVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickHistoryManagementVM vm)
            => string.IsNullOrEmpty(vm?.Title) ? None : One(vm.Title);
    }

    public sealed class AutoLevelupButtonBrickRenderer : TooltipBrickRenderer<TooltipBrickAutoLevelupButtonVM>
    {
        public override IEnumerable<BrickLine> GetExpandedLines(TooltipBrickAutoLevelupButtonVM vm) => None;
    }
}
