using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.LevelClassScores.AbilityScores; // CharInfoStatVM
using Owlcat.Runtime.UI.Tooltips;
using UnityEngine;

namespace WrathAccess.UI.Tooltips
{
    /// <summary>
    /// One read-out line of a rendered tooltip brick — the plain carrier the renderers emit and the
    /// flow builder turns into graph nodes. <see cref="Text"/> is RAW game text (markup intact — the
    /// drill-in link extractor needs the &lt;link&gt; tags; speech strips it); <see cref="Suffix"/> is an
    /// already-localized read-out tail ("heading level 2") outside the searchable text;
    /// <see cref="DrillIn"/> is the line's lazy nested tooltip (a feature write-up, a stat breakdown),
    /// resolved live on Space — never a cached template.
    /// </summary>
    public sealed class BrickLine
    {
        public readonly string Text;
        public readonly string Suffix;
        public readonly Func<TooltipBaseTemplate> DrillIn;

        public BrickLine(string text, Func<TooltipBaseTemplate> drillIn = null, string suffix = null)
        {
            Text = text;
            DrillIn = drillIn;
            Suffix = suffix;
        }

        public bool IsEmpty => string.IsNullOrWhiteSpace(Text);
    }

    /// <summary>
    /// Converts one game tooltip-brick VM into <see cref="BrickLine"/>s. Each brick type has its own
    /// renderer (registered in <see cref="TooltipBrickRegistry"/>), keeping its logic in one place.
    /// Two forms: <c>Expanded</c> (granular — e.g. one line per skill) and <c>Flat</c> (condensed —
    /// e.g. all skills in a single line); the caller picks per context. Flat defaults to expanded, so
    /// single-line bricks only implement one method.
    /// </summary>
    public abstract class TooltipBrickRenderer
    {
        public abstract Type BrickType { get; }
        public abstract IEnumerable<BrickLine> GetExpandedLines(TooltipBaseBrickVM vm);
        public abstract IEnumerable<BrickLine> GetFlatLines(TooltipBaseBrickVM vm);

        // ---- shared formatting helpers ----

        /// <summary>"Label: Value"; falls back to the icon's sprite name when there's no text label.</summary>
        protected static string Stat(string name, string value, Sprite icon)
        {
            string label = !string.IsNullOrEmpty(name) ? name : (icon != null ? icon.name : null);
            if (string.IsNullOrEmpty(label)) return value;
            if (string.IsNullOrEmpty(value)) return label;
            return label + ": " + value;
        }

        protected static string Join(params string[] parts) =>
            string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

        protected static string StatLine(CharInfoStatVM s)
        {
            if (s == null) return null;
            string name = s.Name != null ? s.Name.Value : null;
            string val = (s.StringValue != null && !string.IsNullOrEmpty(s.StringValue.Value))
                ? s.StringValue.Value
                : s.StatValue.Value.ToString();
            if (string.IsNullOrEmpty(name)) return val;
            return name + ": " + val;
        }

        protected static IEnumerable<BrickLine> One(string text, Func<TooltipBaseTemplate> tooltip = null)
        {
            yield return new BrickLine(text, tooltip);
        }

        protected static readonly IEnumerable<BrickLine> None = Array.Empty<BrickLine>();
    }

    /// <summary>Typed base: subclasses read the concrete VM without casting.</summary>
    public abstract class TooltipBrickRenderer<TVM> : TooltipBrickRenderer where TVM : TooltipBaseBrickVM
    {
        public sealed override Type BrickType => typeof(TVM);
        public sealed override IEnumerable<BrickLine> GetExpandedLines(TooltipBaseBrickVM vm) => GetExpandedLines((TVM)vm);
        public sealed override IEnumerable<BrickLine> GetFlatLines(TooltipBaseBrickVM vm) => GetFlatLines((TVM)vm);

        public abstract IEnumerable<BrickLine> GetExpandedLines(TVM vm);
        public virtual IEnumerable<BrickLine> GetFlatLines(TVM vm) => GetExpandedLines(vm);
    }
}
