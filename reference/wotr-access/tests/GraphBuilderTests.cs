using System;
using WrathAccess.UI.Graph;
using Xunit;

namespace WrathAccess.Tests
{
    public class GraphBuilderTests
    {
        private static NodeVtable Vt(string label) => new NodeVtable { Announcements = new[] { NodeAnnouncement.Static(label) } };
        private static ControlId Id(string key) => ControlId.Structural(key);

        [Fact]
        public void SingleItemsFormVerticalMenu()
        {
            var render = new GraphBuilder()
                .AddItem(Id("a"), Vt("A"))
                .AddItem(Id("b"), Vt("B"))
                .AddItem(Id("c"), Vt("C"))
                .Build();

            Assert.Equal(Id("a"), render.StartKey);
            Assert.Equal(Id("b"), render.Nodes[Id("a")].Transitions[GraphDir.Down].Destination);
            Assert.Equal(Id("a"), render.Nodes[Id("b")].Transitions[GraphDir.Up].Destination);
            Assert.Equal(Id("c"), render.Nodes[Id("b")].Transitions[GraphDir.Down].Destination);
            Assert.False(render.Nodes[Id("a")].Transitions.ContainsKey(GraphDir.Up));
            Assert.False(render.Nodes[Id("a")].Transitions.ContainsKey(GraphDir.Left));
            Assert.False(render.Nodes[Id("a")].Transitions.ContainsKey(GraphDir.Right));
        }

        [Fact]
        public void RowsWireHorizontally()
        {
            var render = new GraphBuilder()
                .StartRow().AddItem(Id("a"), Vt("A")).AddItem(Id("b"), Vt("B")).EndRow()
                .Build();

            Assert.Equal(Id("b"), render.Nodes[Id("a")].Transitions[GraphDir.Right].Destination);
            Assert.Equal(Id("a"), render.Nodes[Id("b")].Transitions[GraphDir.Left].Destination);
        }

        [Fact]
        public void SharedRowKeysPreserveColumn()
        {
            var render = new GraphBuilder()
                .StartRow("grid").AddItem(Id("a1"), Vt("A1")).AddItem(Id("a2"), Vt("A2")).EndRow()
                .StartRow("grid").AddItem(Id("b1"), Vt("B1")).AddItem(Id("b2"), Vt("B2")).EndRow()
                .Build();

            Assert.Equal(Id("b2"), render.Nodes[Id("a2")].Transitions[GraphDir.Down].Destination);
            Assert.Equal(Id("a2"), render.Nodes[Id("b2")].Transitions[GraphDir.Up].Destination);
        }

        [Fact]
        public void UnkeyedRowsLandOnFirstItem()
        {
            var render = new GraphBuilder()
                .StartRow().AddItem(Id("a1"), Vt("A1")).AddItem(Id("a2"), Vt("A2")).EndRow()
                .StartRow().AddItem(Id("b1"), Vt("B1")).AddItem(Id("b2"), Vt("B2")).EndRow()
                .Build();

            Assert.Equal(Id("b1"), render.Nodes[Id("a2")].Transitions[GraphDir.Down].Destination);
        }

        [Fact]
        public void RaggedKeyedRowFallsToFirstItem()
        {
            var render = new GraphBuilder()
                .StartRow("grid").AddItem(Id("a1"), Vt("A1")).AddItem(Id("a2"), Vt("A2")).AddItem(Id("a3"), Vt("A3")).EndRow()
                .StartRow("grid").AddItem(Id("b1"), Vt("B1")).EndRow()
                .Build();

            // Column 2 doesn't exist below → first item.
            Assert.Equal(Id("b1"), render.Nodes[Id("a3")].Transitions[GraphDir.Down].Destination);
        }

        [Fact]
        public void ArrowsNeverCrossStops()
        {
            var render = new GraphBuilder()
                .AddItem(Id("a"), Vt("A"))
                .BeginStop()
                .AddItem(Id("b"), Vt("B"))
                .Build();

            Assert.False(render.Nodes[Id("a")].Transitions.ContainsKey(GraphDir.Down));
            Assert.False(render.Nodes[Id("b")].Transitions.ContainsKey(GraphDir.Up));
            Assert.NotEqual(render.Nodes[Id("a")].StopKey, render.Nodes[Id("b")].StopKey);
        }

        [Fact]
        public void ContextBuildsNonFocusableParentChain()
        {
            var render = new GraphBuilder()
                .PushContext("Settings", "list")
                .AddItem(Id("a"), Vt("A"))
                .PushContext("Advanced")
                .AddItem(Id("b"), Vt("B"))
                .PopContext()
                .AddItem(Id("c"), Vt("C"))
                .Build();

            var a = render.Nodes[Id("a")];
            var b2 = render.Nodes[Id("b")];
            var c2 = render.Nodes[Id("c")];
            Assert.NotNull(a.Parent);
            Assert.False(a.Parent.Focusable);
            Assert.Null(a.Parent.Parent);
            Assert.Same(a.Parent, b2.Parent.Parent);   // Advanced nests under Settings
            Assert.Same(a.Parent, c2.Parent);          // c popped back out to Settings
            Assert.False(render.Nodes.ContainsKey(a.Parent.Id)); // context nodes are never navigable
        }

        [Fact]
        public void GroupsEmitHeadersAndSuppressCollapsedSubtrees()
        {
            var expansion = new System.Collections.Generic.HashSet<ControlId>();
            GraphRender Build() => new GraphBuilder(expansion)
                .BeginGroup(Id("combat"), Vt("Combat"))
                    .AddItem(Id("pause"), Vt("Auto pause"))
                    .BeginGroup(Id("nested"), Vt("Nested"))
                        .AddItem(Id("deep"), Vt("Deep"))
                    .EndGroup()
                .EndGroup()
                .AddItem(Id("after"), Vt("After"))
                .Build();

            var collapsed = Build();
            Assert.True(collapsed.Nodes.ContainsKey(Id("combat")));
            Assert.True(collapsed.Nodes[Id("combat")].Expandable);
            Assert.False(collapsed.Nodes[Id("combat")].Expanded);
            Assert.False(collapsed.Nodes.ContainsKey(Id("pause")));  // collapsed → children swallowed
            Assert.False(collapsed.Nodes.ContainsKey(Id("nested")));
            Assert.True(collapsed.Nodes.ContainsKey(Id("after")));

            expansion.Add(Id("combat"));
            var expanded = Build();
            Assert.True(expanded.Nodes.ContainsKey(Id("pause")));
            Assert.Same(expanded.Nodes[Id("combat")], expanded.Nodes[Id("pause")].Parent);
            Assert.True(expanded.Nodes.ContainsKey(Id("nested"))); // nested header visible…
            Assert.False(expanded.Nodes.ContainsKey(Id("deep")));  // …but its own subtree still collapsed

            expansion.Add(Id("nested"));
            var deep = Build();
            Assert.True(deep.Nodes.ContainsKey(Id("deep")));
            Assert.Same(deep.Nodes[Id("nested")], deep.Nodes[Id("deep")].Parent);
        }

        [Fact]
        public void PositionsAutoStampBySiblingGroup()
        {
            var expansion = new System.Collections.Generic.HashSet<ControlId>();
            expansion.Add(Id("g"));
            var render = new GraphBuilder(expansion)
                .AddItem(Id("a"), Vt("A"))          // top level: a, g = 2 siblings
                .BeginGroup(Id("g"), Vt("G"))
                    .AddItem(Id("c1"), Vt("C1"))    // group level: 3 siblings
                    .AddItem(Id("c2"), Vt("C2"))
                    .AddItem(Id("c3"), Vt("C3"))
                .EndGroup()
                .BeginStop()
                .AddItem(Id("lone"), Vt("Lone"))    // single sibling → no position
                .BeginStop()
                .StartRow().AddItem(Id("r1"), Vt("R1")).AddItem(Id("r2"), Vt("R2")).EndRow() // row members
                .Build();

            Assert.Equal(1, render.Nodes[Id("a")].PositionIndex);
            Assert.Equal(2, render.Nodes[Id("a")].PositionCount);
            Assert.Equal(2, render.Nodes[Id("g")].PositionIndex);
            Assert.Equal(2, render.Nodes[Id("c2")].PositionIndex);
            Assert.Equal(3, render.Nodes[Id("c2")].PositionCount);
            Assert.Equal(0, render.Nodes[Id("lone")].PositionCount);
            Assert.Equal(1, render.Nodes[Id("r1")].PositionIndex);
            Assert.Equal(2, render.Nodes[Id("r2")].PositionIndex);
            Assert.Equal(2, render.Nodes[Id("r2")].PositionCount);
        }

        [Fact]
        public void RegionsAreStamped()
        {
            var render = new GraphBuilder()
                .SetRegion("filters").AddItem(Id("a"), Vt("A"))
                .SetRegion("items").AddItem(Id("b"), Vt("B"))
                .Build();

            Assert.Equal("filters", render.Nodes[Id("a")].RegionKey);
            Assert.Equal("items", render.Nodes[Id("b")].RegionKey);
        }

        [Fact]
        public void MixedModesKeepDeclarationOrder()
        {
            // A screen declaring list → raw grid → button must keep that Tab-stop order (raw nodes must
            // NOT be appended after all menu rows — that displaced FlowSheet stops behind later buttons).
            var render = new GraphBuilder()
                .AddItem(Id("list1"), Vt("L1"))
                .BeginStop()
                .AddNode(Id("cell1"), Vt("C1"))
                .BeginStop()
                .AddItem(Id("button"), Vt("B"))
                .Build();

            Assert.Equal(Id("list1"), render.Order[0].Id);
            Assert.Equal(Id("cell1"), render.Order[1].Id);
            Assert.Equal(Id("button"), render.Order[2].Id);
        }

        [Fact]
        public void MixedStopStitchesMenuToRawVertically()
        {
            // A stop with menu controls above raw sheet content (the inventory stash: search/sort/
            // filters over the item table) must be arrow-traversable across the mode boundary.
            var render = new GraphBuilder()
                .AddItem(Id("search"), Vt("Search"))
                .StartRow().AddItem(Id("f1"), Vt("F1")).AddItem(Id("f2"), Vt("F2")).EndRow()
                .AddNode(Id("r0"), Vt("Row0"))
                .AddNode(Id("r1"), Vt("Row1"))
                .Connect(Id("r0"), GraphDir.Down, Id("r1"))
                .Connect(Id("r1"), GraphDir.Up, Id("r0"))
                .Build();

            // Filter cells drop into the sheet's first row; the sheet's top links back up.
            Assert.Equal(Id("r0"), render.Nodes[Id("f1")].Transitions[GraphDir.Down].Destination);
            Assert.Equal(Id("r0"), render.Nodes[Id("f2")].Transitions[GraphDir.Down].Destination);
            Assert.Equal(Id("f1"), render.Nodes[Id("r0")].Transitions[GraphDir.Up].Destination);
            // The sheet's own wiring is untouched.
            Assert.Equal(Id("r1"), render.Nodes[Id("r0")].Transitions[GraphDir.Down].Destination);
        }

        [Fact]
        public void RawModeWiresExplicitEdges()
        {
            var render = new GraphBuilder()
                .AddNode(Id("a"), Vt("A"))
                .AddNode(Id("b"), Vt("B"))
                .Connect(Id("a"), GraphDir.Right, Id("b"), "crossing the aisle")
                .Connect(Id("a"), GraphDir.Down, Id("ghost")) // undeclared → dropped
                .SetStart(Id("b"))
                .Build();

            Assert.Equal(Id("b"), render.StartKey);
            Assert.Equal("crossing the aisle", render.Nodes[Id("a")].Transitions[GraphDir.Right].Label);
            Assert.False(render.Nodes[Id("a")].Transitions.ContainsKey(GraphDir.Down));
        }

        [Fact]
        public void GuardsRejectMisuse()
        {
            Assert.Null(new GraphBuilder().Build()); // empty = closed

            var dup = new GraphBuilder().AddItem(Id("a"), Vt("A"));
            Assert.Throws<InvalidOperationException>(() => dup.AddItem(Id("a"), Vt("A2")));

            Assert.Throws<ArgumentException>(() => new GraphBuilder().AddItem(Id("x"), new NodeVtable()));
        }

        [Fact]
        public void MenuRowsAndRawNodesMix()
        {
            // A screen mixing an auto-wired list with a computed-topology grid: raw edges may
            // reference menu nodes.
            var render = new GraphBuilder()
                .AddItem(Id("list1"), Vt("List1"))
                .AddNode(Id("cell1"), Vt("Cell1"))
                .AddNode(Id("cell2"), Vt("Cell2"))
                .Connect(Id("cell1"), GraphDir.Right, Id("cell2"))
                .Connect(Id("cell1"), GraphDir.Up, Id("list1"))
                .Build();

            Assert.Equal(3, render.Order.Count);
            Assert.Equal(Id("cell2"), render.Nodes[Id("cell1")].Transitions[GraphDir.Right].Destination);
            Assert.Equal(Id("list1"), render.Nodes[Id("cell1")].Transitions[GraphDir.Up].Destination);
        }
    }
}
