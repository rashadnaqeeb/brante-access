using System;
using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.Tooltip.Bricks;
using Owlcat.Runtime.UI.Tooltips;

namespace WrathAccess.UI.Tooltips
{
    /// <summary>
    /// Renders a game <see cref="TooltipBaseTemplate"/> into graph nodes — the DOCUMENT model. A
    /// template is a linear brick stream (effectively an HTML page: headings, paragraphs, label/value
    /// rows, tables, inline links), so we lay it out flat:
    ///  • a <see cref="TooltipBrickTitleVM"/> becomes an inline HEADING row — read as
    ///    "&lt;title&gt;, heading level N" (N from its H1–H6 <see cref="TooltipTitleType"/>). It does NOT
    ///    start a group: heading-driven sectioning grouped bricks illogically (e.g. a spell's description
    ///    landing inside its "Descriptors" section), so headings are just markers in the flat stream;
    ///  • every other brick contributes its renderer's <see cref="BrickLine"/>s as rows;
    ///  • plain (drill-in-free) multi-line text — a class's skill list packed into one brick — splits
    ///    into one row per line so each reads on its own.
    /// Space FOLLOWS each row's drill-ins to a new page (the <see cref="WrathAccess.Screens.TooltipScreen"/>
    /// stack): the row's own nested template and/or its inline glossary links, resolved LIVE. Read at
    /// <see cref="TooltipTemplateType.Info"/> (the larger panel form).
    /// </summary>
    public static class TooltipFlowBuilder
    {
        /// <summary>Emit the document's rows as text nodes under <paramref name="keyPrefix"/>-indexed
        /// keys. Returns the row count.</summary>
        /// <param name="includeEmptyNotice">When the template yields no rows, add a single
        /// "No tooltip information" row (true, for standalone pages where Space must show something)
        /// or emit nothing (false, for embedded detail panels).</param>
        public static int Emit(WrathAccess.UI.Graph.GraphBuilder b, string keyPrefix,
            TooltipBaseTemplate template, TooltipTemplateType type = TooltipTemplateType.Info,
            bool includeEmptyNotice = true)
        {
            int i = 0;
            foreach (var row in Rows(template, type))
            {
                var line = row;
                b.AddItem(WrathAccess.UI.Graph.ControlId.Structural(keyPrefix + "r" + i), new WrathAccess.UI.Graph.NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new[] { new WrathAccess.UI.Graph.NodeAnnouncement(() => Speech(line)) },
                    SearchText = () => TextUtil.StripRichText(line.Text),
                    // Every row offers Space: the handler resolves the drill targets LIVE (the line's own
                    // template + inline glossary links from the RAW text) and speaks "no tooltip" when
                    // there are none — the reader's UX.
                    OnTooltip = () => WrathAccess.Screens.TooltipScreen.FollowLinks(
                        line.Text, null, line.DrillIn != null ? line.DrillIn() : null),
                });
                i++;
            }
            if (i == 0 && includeEmptyNotice)
            {
                b.AddItem(WrathAccess.UI.Graph.ControlId.Structural(keyPrefix + "empty"),
                    GraphNodes.Text(() => Loc.T("tooltip.empty")));
                i++;
            }
            return i;
        }

        // What a row speaks: the markup-stripped text, plus the already-localized suffix (heading rank).
        private static string Speech(BrickLine line)
        {
            var text = TextUtil.StripRichText(line.Text);
            return string.IsNullOrEmpty(line.Suffix) ? text : text + ", " + line.Suffix;
        }

        // The document's rows, flat: headings inline, each brick's renderer lines, plain drill-in-free
        // multi-line text split one row per line (each reads on its own).
        private static IEnumerable<BrickLine> Rows(TooltipBaseTemplate template, TooltipTemplateType type)
        {
            if (template == null) yield break;
            Prepare(template, type);
            foreach (var brick in Bricks(template, type))
            {
                var vm = SafeGetVM(brick);
                if (vm == null) continue;

                if (vm is TooltipBrickTitleVM title)
                {
                    // Inline heading row (NOT a new section): "<title>, heading level N", N from H1–H6.
                    if (!string.IsNullOrWhiteSpace(title.Title))
                        yield return new BrickLine(title.Title,
                            suffix: Loc.T("role.heading_level", new { level = (int)title.Type + 1 }));
                    continue;
                }

                foreach (var line in TooltipBrickRegistry.Lines(vm, expanded: true))
                {
                    if (line == null || line.IsEmpty) continue;
                    if (line.DrillIn == null && line.Text.IndexOf('\n') >= 0)
                    {
                        foreach (var part in line.Text.Split('\n'))
                        {
                            var one = part.Trim();
                            if (one.Length > 0) yield return new BrickLine(one);
                        }
                        continue;
                    }
                    yield return line;
                }
            }
        }

        private static IEnumerable<ITooltipBrick> Bricks(TooltipBaseTemplate t, TooltipTemplateType type)
        {
            foreach (var b in Section(t, x => x.GetHeader(type))) yield return b;
            foreach (var b in Section(t, x => x.GetBody(type))) yield return b;
            foreach (var b in Section(t, x => x.GetFooter(type))) yield return b;
        }

        private static IEnumerable<ITooltipBrick> Section(TooltipBaseTemplate t,
            Func<TooltipBaseTemplate, IEnumerable<ITooltipBrick>> get)
        {
            try { return get(t) ?? Array.Empty<ITooltipBrick>(); }
            catch (Exception e) { Main.Log?.Error("TooltipFlowBuilder section: " + e.Message); return Array.Empty<ITooltipBrick>(); }
        }

        private static TooltipBaseBrickVM SafeGetVM(ITooltipBrick brick)
        {
            if (brick == null) return null;
            try { return brick.GetVM(); }
            catch (Exception e) { Main.Log?.Error("TooltipFlowBuilder GetVM: " + e.Message); return null; }
        }

        private static void Prepare(TooltipBaseTemplate template, TooltipTemplateType type)
        {
            try { template.Prepare(type); }
            catch (Exception e) { Main.Log?.Error("TooltipFlowBuilder.Prepare: " + e.Message); }
        }
    }
}
