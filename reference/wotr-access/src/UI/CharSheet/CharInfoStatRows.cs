using System;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.LevelClassScores.AbilityScores;

namespace WrathAccess.UI.CharSheet
{
    /// <summary>
    /// Builds <see cref="StatRow"/>s from the game's reusable <see cref="CharInfoStatVM"/> — the single
    /// VM behind nearly every character-sheet tile (ability scores, skills, AC, saving throws,
    /// initiative, speed, …). Reads only what the matching CharInfoStat view shows on screen; the full
    /// modifier breakdown rides along as the tooltip (<see cref="CharInfoStatVM.Tooltip"/>, a
    /// TooltipTemplateStat) resolved live on each drill-in. These mappers are reused as-is when we build
    /// the in-game character sheet.
    /// </summary>
    public static class CharInfoStatRows
    {
        // Ability score: Score (the total, racial already folded in) + Modifier. The results-screen
        // prefab wires no race-bonus column, so we don't surface one — match what's shown.
        public static StatRow Ability(CharInfoStatVM vm) => vm == null ? null : new StatRow(
            () => vm.Name.Value,
            new Func<string>[] { () => Display(vm, signed: false), () => Signed(vm.Bonus.Value) },
            () => vm.Tooltip);

        // Skill: Rank + Modifier (the signed total bonus), mirroring CharInfoSkillView.
        public static StatRow Skill(CharInfoStatVM vm) => vm == null ? null : new StatRow(
            () => vm.Name.Value,
            new Func<string>[] { () => vm.Rank.Value.ToString(), () => Signed(vm.StatValue.Value) },
            () => vm.Tooltip);

        // A single-value tile (AC/Touch/Flat, saves, initiative, speed, CMB/CMD, …). Whether the value
        // reads with a sign depends on the stat (a save is "+5", an AC is "18"), so the caller decides.
        public static StatRow Value(CharInfoStatVM vm, bool signed, string nameOverride = null) =>
            vm == null ? null : new StatRow(
                () => nameOverride ?? vm.Name.Value,
                new Func<string>[] { () => Display(vm, signed) },
                () => vm.Tooltip);

        // Mirrors CharInfoStatView.SetValue: "-" when disabled, the string value if the stat carries one
        // (e.g. Size), else the number (signed when asked).
        private static string Display(CharInfoStatVM vm, bool signed)
        {
            if (!vm.IsValueEnabled.Value) return "-";
            if (!string.IsNullOrEmpty(vm.StringValue.Value)) return vm.StringValue.Value;
            return signed ? Signed(vm.StatValue.Value) : vm.StatValue.Value.ToString();
        }

        private static string Signed(int v) => v >= 0 ? "+" + v : v.ToString();
    }
}
