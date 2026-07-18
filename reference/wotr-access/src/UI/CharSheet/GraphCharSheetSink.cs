using System;
using System.Collections.Generic;
using WrathAccess.UI.Graph;

namespace WrathAccess.UI.CharSheet
{
    /// <summary>
    /// The graph-native char-sheet sink: presents the shared blocks (<see cref="CharSheetBlocks"/>) as
    /// ONE <see cref="GraphSheet"/> stop — each stat group / list section a region (Ctrl+arrow jump
    /// target), rows read whole with their values as parts, Right stepping the value columns under
    /// their headers, Space following the stat's drill-in tooltip.
    /// </summary>
    public sealed class GraphCharSheetSink : ICharSheetSink
    {
        private readonly GraphSheet _sheet;

        public GraphCharSheetSink(GraphBuilder b, string keyPrefix)
        {
            _sheet = new GraphSheet(b, keyPrefix);
        }

        // A multi-column group → a table region (row name + value cells, values as parts on the name);
        // a single-value group → a list region of "name, value" lines. Either way Space follows the
        // stat's drill-in tooltip.
        public void StatGroup(StatGroup g)
        {
            if (g == null || g.Rows.Count == 0) return;
            if (g.Columns.Length >= 2)
            {
                _sheet.Region(g.Label, g.Columns);
                foreach (var row in g.Rows)
                {
                    var r = row; // capture for the live closures
                    var parts = new List<NodeAnnouncement> { GraphNodes.LabelPart(r.Name) };
                    foreach (var v in r.Values) parts.Add(new NodeAnnouncement(v));
                    var cells = new Func<string>[r.Values.Length];
                    for (int i = 0; i < r.Values.Length; i++) cells[i] = r.Values[i];
                    _sheet.Row(StatVt(parts, r.Tooltip), null, cells);
                }
            }
            else
            {
                _sheet.Region(g.Label);
                foreach (var row in g.Rows)
                {
                    var r = row; // capture
                    _sheet.Line(StatVt(new List<NodeAnnouncement>
                    {
                        GraphNodes.LabelPart(r.Name),
                        new NodeAnnouncement(r.Values.Length > 0 ? r.Values[0] : null),
                    }, r.Tooltip));
                }
            }
        }

        public void ListSection(string label, IEnumerable<NodeVtable> items)
        {
            if (items == null) return;
            bool open = false;
            foreach (var it in items)
            {
                if (it == null) continue;
                if (!open) { _sheet.Region(label); open = true; } // opened lazily → empty sections add nothing
                _sheet.Line(it);
            }
        }

        public void Finish() => _sheet.Finish();

        private static NodeVtable StatVt(List<NodeAnnouncement> parts,
            Func<Owlcat.Runtime.UI.Tooltips.TooltipBaseTemplate> tooltip)
        {
            return new NodeVtable
            {
                ControlType = ControlTypes.Text,
                Announcements = parts,
                SearchText = parts.Count > 0 ? parts[0].Text : null,
                OnTooltip = tooltip == null ? (Action)null : () =>
                {
                    var tpl = tooltip();
                    if (tpl != null) Screens.TooltipScreen.Open(tpl);
                },
            };
        }
    }
}
