using System;
using System.Collections.Generic;
using WrathAccess.Input;
using WrathAccess.Settings;
using WrathAccess.UI.Graph;

namespace WrathAccess.UI
{
    /// <summary>
    /// Node factories for the MOD's own setting model (<see cref="Setting"/> family) + the recursive
    /// tree emitter — the graph analog of the retired ModSettingsScreen.BuildSettingNode. Categories
    /// become expandable groups keyed by their settings PATH (stable across renders, so expansion and
    /// focus persist); leaves become typed controls whose values are read live. Inherit-aware settings
    /// (nullable bool/int, dropdowns with an "inherit" choice) all speak ONE consistent form — the
    /// resolved value first, then "overridden" or "inherited" — and reset to inherit on Backspace, with
    /// LIVE value parts so a reset or capture announces the new state under focus.
    /// </summary>
    internal static class ModSettingNodes
    {
        /// <summary>Emit one setting (recursively for categories) under path-based keys. Hidden settings
        /// and empty categories are skipped, matching the old builder.</summary>
        public static void Emit(GraphBuilder b, Setting s, string prefix)
        {
            if (s.Hidden) return;
            switch (s)
            {
                case CategorySetting cat:
                    if (!HasVisibleLeaf(cat)) return; // skip empty groups
                    b.BeginGroup(ControlId.Structural(prefix + cat.Key), GraphNodes.Group(() => cat.Label));
                    foreach (var c in cat.Children) Emit(b, c, prefix + cat.Key + ".");
                    b.EndGroup();
                    break;
                case BindingSetting bs:
                    b.AddItem(ControlId.Structural(prefix + bs.Key), ModBinding(bs.Action));
                    break;
                case BoolSetting bo:
                    b.AddItem(ControlId.Structural(prefix + bo.Key),
                        GraphNodes.Toggle(() => bo.Label, bo.Get, () => bo.Set(!bo.Get())));
                    break;
                case NullableBoolSetting nb:
                    b.AddItem(ControlId.Structural(prefix + nb.Key), OverrideToggle(nb));
                    break;
                case NullableIntSetting ni:
                    b.AddItem(ControlId.Structural(prefix + ni.Key), NullableIntSlider(ni));
                    break;
                case IntSetting i:
                    b.AddItem(ControlId.Structural(prefix + i.Key), IntSlider(i));
                    break;
                case ChoiceSetting c:
                    b.AddItem(ControlId.Structural(prefix + c.Key), ChoiceSettingDropdown(c));
                    break;
                case NullableChoiceSetting nc:
                    b.AddItem(ControlId.Structural(prefix + nc.Key), NullableChoiceDropdown(nc));
                    break;
            }
        }

        // Does this category render anything (a visible non-category leaf anywhere below)?
        public static bool HasVisibleLeaf(CategorySetting cat)
        {
            foreach (var c in cat.Children)
            {
                if (c.Hidden) continue;
                if (c is CategorySetting sub) { if (HasVisibleLeaf(sub)) return true; }
                else return true;
            }
            return false;
        }

        /// <summary>A mod <see cref="IntSetting"/> as a slider: Left/Right step by the setting's Step,
        /// clamped by the setting itself; the new value speaks synchronously.</summary>
        public static NodeVtable IntSlider(IntSetting setting)
        {
            Func<string> value = () => setting.Get().ToString();
            return new NodeVtable
            {
                ControlType = ControlTypes.Slider,
                Announcements = new[]
                {
                    GraphNodes.LabelPart(() => setting.Label),
                    new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = () => setting.Label,
                StateText = value,
                OnAdjust = (sign, large) => setting.Set(setting.Get() + sign * setting.Step),
            };
        }

        /// <summary>A <see cref="NullableIntSetting"/> — a slider that follows the default config until
        /// overridden: speaks the RESOLVED value plus the state word ("5, inherited"); Left/Right write an
        /// explicit override from the resolved value; Backspace resets to inherit (the live part re-reads it).</summary>
        public static NodeVtable NullableIntSlider(NullableIntSetting setting)
        {
            // Consistent inherit wording: the RESOLVED value first, then the state word.
            Func<string> value = () => setting.Resolved + ", "
                + Loc.T(setting.IsOverridden ? "value.overridden" : "value.inherited");
            return new NodeVtable
            {
                ControlType = ControlTypes.Slider,
                Announcements = new[]
                {
                    GraphNodes.LabelPart(() => setting.Label),
                    new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = () => setting.Label,
                StateText = value,
                OnAdjust = (sign, large) => setting.SetExplicit(setting.Resolved + sign * setting.Step),
                OnSecondary = () => setting.Reset(),
            };
        }

        /// <summary>A <see cref="NullableBoolSetting"/> — the per-type announcement override: a checkbox of
        /// the RESOLVED value ("on, overridden" / "on, inherited"); Enter writes an explicit on/off,
        /// Backspace resets to inherit.</summary>
        public static NodeVtable OverrideToggle(NullableBoolSetting setting)
        {
            // Consistent inherit wording: the RESOLVED value first, then the state word.
            Func<string> value = () => Loc.T(setting.Resolved ? "value.on" : "value.off") + ", "
                + Loc.T(setting.IsOverridden ? "value.overridden" : "value.inherited");
            return new NodeVtable
            {
                ControlType = ControlTypes.Toggle,
                Announcements = new[]
                {
                    GraphNodes.LabelPart(() => setting.Label),
                    new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = () => setting.Label,
                StateText = value,
                OnActivate = () =>
                {
                    UiSound.Play(Kingmaker.UI.UISoundType.SettingsSwitchToggle);
                    setting.ToggleExplicit();
                },
                OnSecondary = () => setting.Reset(), // live part re-reads the now-inherited value
            };
        }

        /// <summary>A combo box over a fixed list of strings, opening the shared choice submenu — generic,
        /// delegate-driven (the old ProxyChoiceDropdown). Options beyond <paramref name="selectableCount"/>
        /// are VIRTUAL: displayable as the current value (a derived "Custom" state) but not offered in the
        /// chooser. Inherit-capable ChoiceSettings go through <see cref="ChoiceSettingDropdown"/>, which
        /// owns the overridden/inherited wording and the Backspace reset.</summary>
        public static NodeVtable ChoiceDropdown(string label, List<string> options, Func<int> current,
            Action<int> onSelect, int selectableCount = -1)
        {
            int selectable = (selectableCount < 0 || options == null) ? (options?.Count ?? 0) : selectableCount;
            Func<string> value = () =>
            {
                int i = current != null ? current() : -1;
                return options != null && i >= 0 && i < options.Count ? options[i] : "";
            };
            return new NodeVtable
            {
                ControlType = ControlTypes.ComboBox,
                Announcements = new[]
                {
                    GraphNodes.LabelPart(() => label),
                    new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = () => label,
                OnActivate = () =>
                {
                    int cur = current != null ? current() : -1;
                    Screens.ChoiceSubmenuScreen.Open(label, options.GetRange(0, selectable),
                        cur < selectable ? cur : -1, // a virtual current value preselects nothing
                        onSelect);
                },
            };
        }

        /// <summary>A plain <see cref="ChoiceSetting"/> as a dropdown (no inheritance — that's
        /// <see cref="NullableChoiceDropdown"/>). Choices are re-read live per open (runtime rosters).</summary>
        public static NodeVtable ChoiceSettingDropdown(ChoiceSetting c, string labelOverride = null)
        {
            Func<string> label = () => labelOverride ?? c.Label;
            return new NodeVtable
            {
                ControlType = ControlTypes.ComboBox,
                Announcements = new[]
                {
                    GraphNodes.LabelPart(label),
                    new NodeAnnouncement(() => c.Current?.Label ?? "", live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = label,
                OnActivate = () =>
                {
                    var choices = c.Choices;
                    var labels = new List<string>(choices.Count);
                    foreach (var ch in choices) labels.Add(ch.Label);
                    Screens.ChoiceSubmenuScreen.Open(label(), labels, IndexOfChoice(c),
                        idx => { if (idx >= 0 && idx < c.Choices.Count) c.Set(c.Choices[idx].Id); });
                },
            };
        }

        /// <summary>A <see cref="NullableChoiceSetting"/> as a dropdown — the choice counterpart of the
        /// nullable sliders/toggles, same consistent wording: "{picked}, overridden" for an explicit pick,
        /// "{resolved}, inherited" while inheriting (the inherit option's label when it can't resolve).
        /// The chooser leads with the inherit option (picking it resets); Backspace resets too.</summary>
        public static NodeVtable NullableChoiceDropdown(NullableChoiceSetting c, string labelOverride = null)
        {
            Func<string> label = () => labelOverride ?? c.Label;
            Func<string> value = () =>
            {
                if (c.IsOverridden)
                    return (c.Current?.Label ?? c.LocalValue) + ", " + Loc.T("value.overridden");
                var resolved = c.ResolvedLabel ?? c.InheritOption.Label;
                return resolved + ", " + Loc.T("value.inherited");
            };
            return new NodeVtable
            {
                ControlType = ControlTypes.ComboBox,
                Announcements = new[]
                {
                    GraphNodes.LabelPart(label),
                    new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = label,
                OnActivate = () =>
                {
                    var choices = c.Choices;
                    var labels = new List<string>(choices.Count + 1) { c.InheritOption.Label };
                    foreach (var ch in choices) labels.Add(ch.Label);
                    int current = 0; // inheriting preselects the inherit option
                    if (c.IsOverridden)
                        for (int i = 0; i < choices.Count; i++)
                            if (choices[i].Id == c.LocalValue) { current = i + 1; break; }
                    Screens.ChoiceSubmenuScreen.Open(label(), labels, current, idx =>
                    {
                        if (idx == 0) c.Reset();
                        else if (idx - 1 < c.Choices.Count) c.SetExplicit(c.Choices[idx - 1].Id);
                    });
                },
                // Backspace = back to inheriting (the live value part re-reads the resolved state).
                OnSecondary = () => c.Reset(),
            };
        }

        public static int IndexOfChoice(ChoiceSetting c)
        {
            for (int i = 0; i < c.Choices.Count; i++)
                if (c.Choices[i].Id == c.ValueId) return i;
            return -1;
        }

        /// <summary>A mod key-binding row (one <see cref="InputAction"/>): label + every bound combo
        /// (LIVE — a capture or clear announces the new state). Enter opens the capture dialog to rebind
        /// (REPLACES the set); Backspace opens a menu: Add binding (append an alternative combo) or
        /// Clear bindings. Rebinding is announced by the capture screen itself.</summary>
        public static NodeVtable ModBinding(InputAction action)
        {
            Func<string> value = () => action.Bindings.Count == 0
                ? Loc.T("value.not_bound") : action.BindingsDisplay;
            return new NodeVtable
            {
                ControlType = ControlTypes.KeyBinding,
                Announcements = new[]
                {
                    GraphNodes.LabelPart(() => action.DisplayLabel),
                    new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = () => action.DisplayLabel,
                OnActivate = () => Screens.ModKeyCaptureScreen.Open(action),
                OnSecondary = () =>
                {
                    var options = new List<string>
                    {
                        Loc.T("bind.option_add"),
                        Loc.T("bind.option_clear"),
                    };
                    Screens.ChoiceSubmenuScreen.Open(action.DisplayLabel, options, -1, idx =>
                    {
                        if (idx == 0) Screens.ModKeyCaptureScreen.Open(action, append: true);
                        else if (idx == 1)
                        {
                            action.ClearBindings();
                            Tts.Speak(Loc.T("value.not_bound")); // the live part covers focused re-read
                        }
                    });
                },
            };
        }
    }
}
