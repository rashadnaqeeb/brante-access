using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.Settings.Entities;
using Kingmaker.UI.MVVM._VM.Settings.Entities.Decorative;
using Kingmaker.UI.MVVM._VM.Settings.Entities.Difficulty;
using Owlcat.Runtime.UI.MVVM;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Emits graph nodes from a settings-entity collection (the SettingsEntity* VMs): runs of entities
    /// under a header become an expandable GROUP (a tree section — collapse to skip it), each entity a
    /// typed control node, and a key-binding row its own sub-group holding the two binding slots.
    /// Shared by the Settings screen and (later) the New Game difficulty phase. Node identity: the
    /// entity VM (tier 1) + a structural key of prefix:index (tier 2), so focus follows a control
    /// across renders and a rebuilt VM list still reconciles by position.
    /// </summary>
    internal static class SettingsEntityGraph
    {
        /// <summary>Emit the entities into the builder under <paramref name="keyPrefix"/>-scoped ids.
        /// <paramref name="flat"/>: headers are SKIPPED and the options read as a plain vertical list —
        /// for short single-section pages (New Game difficulty) whose screen/phase already carries the
        /// label; the full Settings screen keeps groups so whole sections can be skipped.</summary>
        public static void Emit(GraphBuilder b, IEnumerable<VirtualListElementVMBase> entities, string keyPrefix,
            bool flat = false)
        {
            if (entities == null) return;
            bool open = false;
            int i = 0;
            foreach (var e in entities)
            {
                if (e is SettingsEntityHeaderVM header)
                {
                    if (open) b.EndGroup();
                    string title = header.Tittle; // (sic — the VM's field)
                    if (!flat) // flat: the page already carries the label — no header node at all
                    {
                        b.BeginGroup(ControlId.Structural(keyPrefix + "sec:" + title), GraphNodes.Group(() => title));
                        open = true;
                    }
                    i++;
                    continue;
                }

                if (e is SettingEntityKeyBindingVM kb)
                {
                    // A key-binding row = an expandable sub-group (labeled with the control) holding its
                    // two binding slots: expand to reach them, Enter rebinds, Backspace clears.
                    string kbKey = keyPrefix + "kb:" + i;
                    b.BeginGroup(ControlId.Referenced(kb, kbKey), GraphNodes.Group(() => kb.Title));
                    b.AddItem(ControlId.Structural(kbKey + ":0"),
                        GraphNodes.KeyBindingSlot(kb, 0, Loc.T("bind.slot", new { index = 1 })));
                    b.AddItem(ControlId.Structural(kbKey + ":1"),
                        GraphNodes.KeyBindingSlot(kb, 1, Loc.T("bind.slot", new { index = 2 })));
                    b.EndGroup();
                    i++;
                    continue;
                }

                var vt = MakeVtable(e);
                if (vt != null)
                    b.AddItem(ControlId.Referenced(e, keyPrefix + "e:" + i), vt);
                i++;
            }
            if (open) b.EndGroup();
        }

        // The entity→control mapping (the old SettingsEntityBuilder.MakeProxy, factory-shaped).
        private static NodeVtable MakeVtable(VirtualListElementVMBase e)
        {
            if (e is SettingsEntityBoolVM b) return GraphNodes.GameToggle(b);
            if (e is SettingsEntitySliderVM s) return GraphNodes.Slider(s);
            if (e is SettingsEntityDropdownGameDifficultyVM diff) return GraphNodes.GameDifficulty(diff); // subclass — check before generic dropdown
            if (e is SettingsEntityDropdownVM d) return GraphNodes.GameDropdown(d);
            // Privacy/data opt-out: a button that opens the privacy page in a browser.
            if (e is SettingsEntityStatisticsOptOutVM opt)
                return GraphNodes.Button(() => opt.Title, () => opt.OpenSettingsInBrowser());
            if (e is SettingsEntityVM sv) return GraphNodes.UnsupportedSetting(() => sv.Title);
            return null;
        }
    }
}
