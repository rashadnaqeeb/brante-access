using WrathAccess.UI.Graph;
using Xunit;

namespace WrathAccess.Tests
{
    public class GraphAnnouncerTests
    {
        private static GraphNode Node(string label, GraphNode parent = null) => new GraphNode
        {
            Id = ControlId.Structural(label),
            Vtable = new NodeVtable { Announcements = new[] { NodeAnnouncement.Static(label) } },
            Parent = parent,
        };

        private static GraphNode Context(string label, string role = null, GraphNode parent = null) => new GraphNode
        {
            Id = ControlId.Structural("ctx:" + label),
            Vtable = new NodeVtable
            {
                Announcements = role == null
                    ? new[] { NodeAnnouncement.Static(label) }
                    : new[] { NodeAnnouncement.Static(label), NodeAnnouncement.Static(role) },
            },
            Parent = parent,
            Focusable = false,
        };

        [Fact]
        public void EntryFromNothingReadsFullChain()
        {
            var options = Context("Options");
            var list = Context("Difficulty settings", "list", options);
            var node = Node("Normal, radio button, selected", list);

            Assert.Equal("Options, Difficulty settings, list, Normal, radio button, selected",
                GraphAnnouncer.ComposeFull(node));
        }

        [Fact]
        public void SiblingMoveReadsLeafOnly()
        {
            var list = Context("Difficulty settings", "list");
            var from = Node("Easy", list);
            var to = Node("Hard", list);

            Assert.Equal("Hard", GraphAnnouncer.Compose(from, to));
        }

        [Fact]
        public void EnteringNestedContextReadsEnteredLevels()
        {
            var outer = Context("Options");
            var from = Node("Back", outer);
            var list = Context("Difficulty settings", "list", outer);
            var to = Node("Normal", list);

            Assert.Equal("Difficulty settings, list, Normal", GraphAnnouncer.Compose(from, to));
        }

        [Fact]
        public void AscendReadsLeafOnly()
        {
            var outer = Context("Options");
            var list = Context("Difficulty settings", "list", outer);
            var from = Node("Normal", list);
            var to = Node("Back", outer);

            Assert.Equal("Back", GraphAnnouncer.Compose(from, to));
        }

        [Fact]
        public void DescendingFromGroupOntoItsChildReadsChildOnly()
        {
            // The group is ON the child's chain AND is the from-node: the prefix swallows it — the old
            // retained-path behavior (stepping into your own group never re-announces the group).
            var group = Node("Combat");
            group.Expandable = true;
            group.Expanded = true;
            var child = Node("Auto pause on combat start, toggle, on", group);

            Assert.Equal("Auto pause on combat start, toggle, on", GraphAnnouncer.Compose(group, child));
        }

        [Fact]
        public void EnteringAGroupFromOutsideReadsTheGroup()
        {
            var group = Node("Combat");
            group.Expandable = true;
            group.Expanded = true;
            var child = Node("Auto pause on combat start, toggle, on", group);
            var elsewhere = Node("Tabs");

            Assert.Equal("Combat, Auto pause on combat start, toggle, on",
                GraphAnnouncer.Compose(elsewhere, child));
        }

        [Fact]
        public void ExpandedStateWordAppendsToGroups()
        {
            var group = Node("Combat");
            group.Expandable = true;
            group.Expanded = false;
            try
            {
                GraphAnnouncer.ExpandedStateText = e => e ? "expanded" : "collapsed";
                Assert.Equal("Combat, collapsed", GraphAnnouncer.ComposeFull(group));
                group.Expanded = true;
                Assert.Equal("Combat, expanded", GraphAnnouncer.ComposeFull(group));
                group.Vtable.SpeaksOwnExpansion = true; // adapter nodes carry their own state word
                Assert.Equal("Combat", GraphAnnouncer.ComposeFull(group));
            }
            finally { GraphAnnouncer.ExpandedStateText = null; }
        }

        [Fact]
        public void DuplicateContainerLabelIsSkipped()
        {
            // A "Game difficulty" section wrapping the "Game difficulty" control: the section stays silent.
            var section = Context("Game difficulty");
            var to = Node("Game difficulty, menu button", section);
            Assert.Equal("Game difficulty, menu button", GraphAnnouncer.ComposeFull(to));

            // But a control that merely STARTS with different text keeps its container.
            var other = Node("Game difficulty presets, menu button", Context("Game difficulty"));
            Assert.Equal("Game difficulty, Game difficulty presets, menu button",
                GraphAnnouncer.ComposeFull(other));
        }

        private static readonly ControlType TestButton = new ControlType
        {
            Key = "button",
            Order = new[] { AnnouncementKinds.Label, AnnouncementKinds.Role, AnnouncementKinds.Value, AnnouncementKinds.Enabled, AnnouncementKinds.Position },
            Common = () => new[] { new NodeAnnouncement(() => "button", kind: AnnouncementKinds.Role) },
        };

        private static GraphNode TypedNode(ControlType type, params NodeAnnouncement[] parts) => new GraphNode
        {
            Id = ControlId.Structural("typed"),
            Vtable = new NodeVtable { ControlType = type, Announcements = parts },
        };

        [Fact]
        public void ControlTypeSuppliesRoleAndOrdering()
        {
            // Parts declared out of order — the type's kind order sorts them; the common role merges in.
            var node = TypedNode(TestButton,
                new NodeAnnouncement(() => "on", kind: AnnouncementKinds.Value),
                new NodeAnnouncement(() => "Hold position", kind: AnnouncementKinds.Label));

            Assert.Equal("Hold position, button, on", GraphAnnouncer.ComposeFull(node));
        }

        [Fact]
        public void NodePartOverridesCommonOfSameKind()
        {
            var node = TypedNode(TestButton,
                new NodeAnnouncement(() => "Continue", kind: AnnouncementKinds.Label),
                new NodeAnnouncement(() => "menu button", kind: AnnouncementKinds.Role));

            Assert.Equal("Continue, menu button", GraphAnnouncer.ComposeFull(node));
        }

        [Fact]
        public void KindlessPartsKeepDeclarationOrderAfterKnownKinds()
        {
            var node = TypedNode(TestButton,
                new NodeAnnouncement(() => "custom one"),
                new NodeAnnouncement(() => "Continue", kind: AnnouncementKinds.Label),
                new NodeAnnouncement(() => "custom two"));

            Assert.Equal("Continue, button, custom one, custom two", GraphAnnouncer.ComposeFull(node));
        }

        [Fact]
        public void PartFilterDropsParts()
        {
            var node = TypedNode(TestButton,
                new NodeAnnouncement(() => "Continue", kind: AnnouncementKinds.Label));
            try
            {
                GraphAnnouncer.PartFilter = (type, part) => part.Kind != AnnouncementKinds.Role;
                Assert.Equal("Continue", GraphAnnouncer.ComposeFull(node));
            }
            finally { GraphAnnouncer.PartFilter = null; }
        }

        [Fact]
        public void LeafTextJoinsAnnouncementParts()
        {
            var node = new GraphNode
            {
                Id = ControlId.Structural("x"),
                Vtable = new NodeVtable
                {
                    Announcements = new[]
                    {
                        NodeAnnouncement.Static("Hold position"),
                        NodeAnnouncement.Static("toggle"),
                        new NodeAnnouncement(() => "on", live: true),
                        new NodeAnnouncement(() => null), // empty at speak time — silent
                    },
                },
            };
            Assert.Equal("Hold position, toggle, on", GraphAnnouncer.ComposeFull(node));
        }

        [Fact]
        public void TransitionLabelLeads()
        {
            var from = Node("A");
            var to = Node("B");
            Assert.Equal("next column, B", GraphAnnouncer.Compose(from, to, "next column"));
        }

        [Fact]
        public void ContextChangeAtSameDepthReadsNewLevel()
        {
            var from = Node("Fireball", Context("Level 1 spells", "table"));
            var to = Node("Haste", Context("Level 2 spells", "table"));

            Assert.Equal("Level 2 spells, table, Haste", GraphAnnouncer.Compose(from, to));
        }
    }
}
