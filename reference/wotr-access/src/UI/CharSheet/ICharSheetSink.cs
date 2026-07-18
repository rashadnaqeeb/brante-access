using System.Collections.Generic;
using WrathAccess.UI.Graph;

namespace WrathAccess.UI.CharSheet
{
    /// <summary>
    /// Where the character sheet's sections are written. The sheet's data assembly (which CharInfo* VMs,
    /// which groups) is the same regardless of presentation; the sink decides the shape
    /// (today: <see cref="GraphCharSheetSink"/>'s unified grid). Two section kinds: column-aligned stat groups, and
    /// free-form lists (Summary, Attack, Features, …). Empty groups/sections are skipped. Shared by the chargen Total
    /// phase and the in-game character window.
    /// </summary>
    public interface ICharSheetSink
    {
        void StatGroup(StatGroup group);
        void ListSection(string label, IEnumerable<NodeVtable> items);
        void Finish();
    }
}
