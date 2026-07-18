using System;
using System.Collections.Generic;
using Owlcat.Runtime.UI.Tooltips;

namespace WrathAccess.UI.CharSheet
{
    /// <summary>
    /// One stat's read-only content: a name, its visible value cells (parallel to the owning
    /// <see cref="StatGroup"/>'s columns), and the tooltip that holds the full modifier breakdown
    /// (drilled into with Space). Everything is a live <see cref="Func{T}"/>, so a cell always reflects
    /// current data. Carries only what the game's CharInfoStat view actually shows — values, not the
    /// per-source modifiers, which live in the tooltip and never appear inline.
    /// </summary>
    public sealed class StatRow
    {
        public Func<string> Name { get; }
        public Func<string>[] Values { get; }
        public Func<TooltipBaseTemplate> Tooltip { get; }

        public StatRow(Func<string> name, Func<string>[] values, Func<TooltipBaseTemplate> tooltip = null)
        {
            Name = name;
            Values = values ?? new Func<string>[0];
            Tooltip = tooltip;
        }
    }

    /// <summary>
    /// A column-aligned set of stats that share the same value columns — e.g. the six ability scores
    /// (Score / Modifier) or the skills (Rank / Modifier). The <see cref="ICharSheetLayout"/> decides
    /// how to present it, so the character sheet's accessible shape is swappable without touching how
    /// its data is assembled. <see cref="Label"/> names the group (a localized string where the game
    /// has one); <see cref="Columns"/> are the value-column headers (the name column is implicit).
    /// </summary>
    public sealed class StatGroup
    {
        public string Label { get; }
        public string[] Columns { get; }
        public List<StatRow> Rows { get; } = new List<StatRow>();

        public StatGroup(string label, params string[] columns)
        {
            Label = label;
            Columns = columns ?? new string[0];
        }

        /// <summary>Append a row (ignores null, so callers can pass a mapper result directly).</summary>
        public StatGroup Row(StatRow row)
        {
            if (row != null) Rows.Add(row);
            return this;
        }
    }
}
