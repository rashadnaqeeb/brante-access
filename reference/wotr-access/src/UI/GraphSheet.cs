using System;
using System.Collections.Generic;
using WrathAccess.UI.Graph;

namespace WrathAccess.UI
{
    /// <summary>
    /// The graph-native table/document emitter — the FlowSheet idiom rebuilt on graph primitives, with
    /// no special composer: one Tab-stop of vertically-stacked REGIONS (each a Ctrl+arrow jump target
    /// and a context level, so entering one announces its title once via the path diff), rows navigated
    /// Up/Down with the column preserved, cells Left/Right. The old framing rules ride the graph's own
    /// mechanisms:
    ///  - column header on column change  = left/right EDGE LABELS (the destination column's header);
    ///  - row name when off in a metadata column = vertical edge labels into non-primary cells;
    ///  - the associated-element readout = the PRIMARY (column 0) cell's announcement list carrying the
    ///    row's metadata as extra parts — vertical navigation rides column 0, so moving down the table
    ///    reads the whole row, per-part filterable like any control;
    ///  - empty cells read the localized "blank".
    /// Emit rows in one region, then start the next; Finish() closes the last region. Raw mode
    /// underneath (explicit edges), so no auto positions — matching the old sheets.
    /// </summary>
    public sealed class GraphSheet
    {
        private readonly GraphBuilder _b;
        private readonly string _key;
        private int _regionIndex = -1;
        private bool _contextOpen;

        // Current region state. Row cells carry their LOGICAL column (sparse rows skip empty cells --
        // they aren't landable, like the old FlowSheet's visitability -- but vertical navigation still
        // matches columns by logical number).
        private struct CellRef { public int Col; public ControlId Id; }

        private string[] _columns; // headers for cells 1..N (null = a plain list region)
        private int _row = -1;
        private List<CellRef> _prevRowIds;
        private List<CellRef> _rowIds;
        private Func<string> _rowName; // the current row's primary label (for vertical edge labels)
        private Func<string> _prevRowName;
        private object _rowRef;        // the current row's domain object (identity keys), or null

        public GraphSheet(GraphBuilder b, string keyPrefix)
        {
            _b = b;
            _key = keyPrefix;
        }

        /// <summary>Start a region: a Ctrl+arrow jump target and a context level ("Buy cart, table").
        /// <paramref name="columns"/> are the headers for the metadata cells (column 0 — the primary —
        /// has none); null/empty = a plain one-column list region.</summary>
        public GraphSheet Region(string label, string[] columns = null, string role = null)
        {
            CloseRegion();
            _regionIndex++;
            _b.SetRegion(_key + "reg:" + _regionIndex);
            if (!string.IsNullOrEmpty(label))
            {
                _b.PushContext(label, role ?? (columns != null && columns.Length > 0 ? Loc.T("role.table") : null),
                    positions: false);
                _contextOpen = true;
            }
            _columns = columns;
            return this;
        }

        /// <summary>One row: the interactive/primary cell's vtable plus the metadata cell values (their
        /// count should match the region's columns). Metadata cells are read-only text.
        /// <paramref name="rowRef"/> is the row's DOMAIN OBJECT (the item slot, the save VM) and should
        /// be passed whenever rows can appear/vanish/reorder: keys derive from it, so a removed row's
        /// focus slides to a genuinely different identity and the differ announces the landing — index
        /// keys would silently rebadge the next row as "the same control". The primary additionally
        /// carries it as its reference (tier-1 follow when the row moves).</summary>
        public GraphSheet Row(NodeVtable primary, object rowRef, params Func<string>[] cells)
        {
            BeginRow(primary, rowRef);
            if (cells != null)
                for (int i = 0; i < cells.Length; i++)
                {
                    var v = cells[i];
                    if (v == null) continue; // sparse: an empty logical column isn't landable
                    EmitCell(new NodeVtable
                    {
                        ControlType = ControlTypes.Text,
                        Announcements = new[] { new NodeAnnouncement(() => Blank(v?.Invoke())) },
                        SearchText = _rowName, // type-ahead matches the row's name from any cell
                    }, i + 1);
                }
            WireVertical();
            return this;
        }

        /// <summary>A row whose cells are pre-built vtables at explicit LOGICAL columns (sparse grids --
        /// the progression level ruler). Column numbers are 1-based (0 is the primary).</summary>
        public GraphSheet RowAt(NodeVtable primary, object rowRef,
            IEnumerable<KeyValuePair<int, NodeVtable>> cells)
        {
            BeginRow(primary, rowRef);
            if (cells != null)
                foreach (var kv in cells)
                    if (kv.Value != null) EmitCell(kv.Value, kv.Key);
            WireVertical();
            return this;
        }

        private void BeginRow(NodeVtable primary, object rowRef)
        {
            _rowRef = rowRef;
            _row++;
            _prevRowIds = _rowIds;
            _prevRowName = _rowName;
            _rowIds = new List<CellRef>();

            // The row's name for vertical edge labels = the primary's label (first announcement part).
            _rowName = primary.Announcements != null && primary.Announcements.Count > 0
                ? primary.Announcements[0].Text : null;

            EmitCell(primary, 0);
        }

        /// <summary>A single full-width line (a lead row like "Your gold", a section note).</summary>
        public GraphSheet Line(NodeVtable vt)
        {
            BeginRow(vt, null);
            WireVertical();
            return this;
        }

        /// <summary>Close the final region. Call once after the last row.</summary>
        public void Finish() => CloseRegion();

        private void CloseRegion()
        {
            if (_contextOpen) { _b.PopContext(); _contextOpen = false; }
            // Rows don't chain across region boundaries here — they do: the last row of a region wires
            // to the first of the next as rows are emitted (prev-row linkage carries across Region()).
            _columns = null;
        }

        private void EmitCell(NodeVtable vt, int col)
        {
            // Identity keys when the row has a domain object: stable across reorders/removals (the
            // primary also carries the reference for tier-1 follow); positional only for static lines.
            string skey = _rowRef != null
                ? _key + "row" + _rowRef.GetHashCode() + "c" + col
                : _key + "r" + _row + "c" + col;
            var id = _rowRef != null && col == 0 ? ControlId.Referenced(_rowRef, skey) : ControlId.Structural(skey);
            _b.AddNode(id, vt);

            // Left/right to the nearest EMITTED cell (sparse rows skip empty columns), labeled with the
            // destination column's header (none onto the primary, whose full readout identifies it).
            if (_rowIds.Count > 0)
            {
                var left = _rowIds[_rowIds.Count - 1];
                _b.Connect(id, GraphDir.Left, left.Id, left.Col == 0 ? null : Header(left.Col));
                _b.Connect(left.Id, GraphDir.Right, id, Header(col));
            }
            _rowIds.Add(new CellRef { Col = col, Id = id });
        }

        // Vertical edges between the completed row and the previous one: the same LOGICAL column where
        // both rows have it, else the other row's primary (sparse/ragged rows never dead-end). Labels
        // name the destination ROW when landing off-primary (so you know which row you're in without
        // the full readout); landings on column 0 stay unlabeled -- the primary's parts read the row.
        private void WireVertical()
        {
            if (_prevRowIds == null || _prevRowIds.Count == 0) return;

            foreach (var cell in _rowIds)
            {
                bool matched = HasCol(_prevRowIds, cell.Col);
                _b.Connect(cell.Id, GraphDir.Up, FindAt(_prevRowIds, cell.Col),
                    matched && cell.Col > 0 ? Text(_prevRowName) : null);
            }
            foreach (var cell in _prevRowIds)
            {
                bool matched = HasCol(_rowIds, cell.Col);
                _b.Connect(cell.Id, GraphDir.Down, FindAt(_rowIds, cell.Col),
                    matched && cell.Col > 0 ? Text(_rowName) : null);
            }
        }

        private static ControlId FindAt(List<CellRef> row, int col)
        {
            foreach (var c in row) if (c.Col == col) return c.Id;
            return row[0].Id; // fall to the row's primary
        }

        private static bool HasCol(List<CellRef> row, int col)
        {
            foreach (var c in row) if (c.Col == col) return true;
            return false;
        }

        private string Header(int col)
            => _columns != null && col - 1 >= 0 && col - 1 < _columns.Length ? _columns[col - 1] : null;

        private static string Text(Func<string> f) => f?.Invoke();

        private static string Blank(string v) => string.IsNullOrWhiteSpace(v) ? Loc.T("value.blank") : v;
    }
}
