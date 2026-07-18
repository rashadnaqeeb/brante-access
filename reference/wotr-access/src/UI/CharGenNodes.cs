using System;
using System.Collections.Generic;
using Kingmaker.UI; // UISoundType
using Kingmaker.UI.MVVM._VM.CharGen.Phases; // CharGenPhaseBaseVM
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Class; // CharGenClassSelectorItemVM
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Common; // StringSequentialSelectorVM
using TMPro;
using WrathAccess.UI.Graph;

namespace WrathAccess.UI
{
    /// <summary>
    /// Node factories for the character-generation family (the old ProxyTextField /
    /// ProxySequentialSelector / ProxyStepper / ProxyRoadmapEntry / ProxyClassItem, factory-shaped).
    /// </summary>
    public static class CharGenNodes
    {
        /// <summary>A text-entry control wrapping one of the game's <see cref="TMP_InputField"/>s. The
        /// field is fetched live via <paramref name="acquire"/> (it lives on the active view, not the VM
        /// tree); activating hands the keyboard to <see cref="TextEntry"/>, which drives the real field
        /// so Unity handles caret/backspace/Unicode/IME. The value part is LIVE (edits read back).</summary>
        public static NodeVtable TextField(string label, Func<TMP_InputField> acquire, Func<string> value)
        {
            return new NodeVtable
            {
                Announcements = new[]
                {
                    GraphNodes.LabelPart(() => label),
                    new NodeAnnouncement(() => Loc.T("role.edit"), kind: AnnouncementKinds.Role),
                    new NodeAnnouncement(() =>
                    {
                        var v = value?.Invoke();
                        return string.IsNullOrEmpty(v) ? Loc.T("value.blank") : v;
                    }, live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = () => label,
                OnActivate = () =>
                {
                    var field = acquire?.Invoke();
                    if (field != null) TextEntry.Begin(field, label);
                    else Tts.Speak(Loc.T("text.unavailable"), interrupt: true);
                },
            };
        }

        /// <summary>A sequential (cycle) selector — Left/Right step through the options, reading the
        /// label + current value (the race ability-bonus chooser, birth month/day). The VM is fetched
        /// live (created on demand); the step re-announces the new value synchronously.</summary>
        public static NodeVtable SequentialSelector(string label, Func<StringSequentialSelectorVM> vm)
        {
            Func<string> value = () => vm?.Invoke()?.Value?.Value ?? "";
            return new NodeVtable
            {
                ControlType = ControlTypes.Slider, // Left/Right cycles, so reads like the settings sliders
                Announcements = new[]
                {
                    GraphNodes.LabelPart(() => label),
                    new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = () => label,
                StateText = value,
                OnAdjust = (sign, large) =>
                {
                    var v = vm?.Invoke();
                    if (v == null) return;
                    if (sign < 0) v.OnLeft(); else v.OnRight();
                },
            };
        }

        /// <summary>A point-buy +/- stepper (raise or lower one step), driven by delegates. Reads its
        /// cost/state label; Enter performs the step and re-announces the caller's summary (the new
        /// value + remaining pool) synchronously — live reads keep it fresh the instant you step.</summary>
        public static NodeVtable Stepper(Func<string> label, Func<bool> enabled, Action act, Func<string> summary)
        {
            return new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new[]
                {
                    GraphNodes.LabelPart(label),
                    summary != null ? new NodeAnnouncement(summary, kind: AnnouncementKinds.Value) : null,
                    GraphNodes.DisabledPart(enabled),
                },
                SearchText = label,
                StateText = summary,
                OnActivate = () =>
                {
                    if (enabled != null && !enabled()) return;
                    UiSound.Play(UISoundType.ButtonClick);
                    act?.Invoke();
                },
            };
        }

        /// <summary>One step in the chargen roadmap: phase name, its state (current / completed) plus the
        /// live per-phase summary (class name, the six scores…), "disabled" while locked. Activating
        /// jumps to that phase — gated on availability, exactly like the game's roadmap.</summary>
        public static NodeVtable RoadmapEntry(CharGenPhaseBaseVM vm, Func<string> summary)
        {
            Func<bool> available = () => vm != null && vm.IsAvailable.Value;
            Func<string> state = () =>
            {
                string s = vm.IsSelected.Value ? Loc.T("value.current")
                    : vm.IsCompletedAndAvailible.Value ? Loc.T("value.completed") : null;
                var extra = summary?.Invoke();
                if (string.IsNullOrEmpty(extra)) return s;
                return string.IsNullOrEmpty(s) ? extra : s + ", " + extra;
            };
            return new NodeVtable
            {
                ControlType = ControlTypes.Tab,
                Announcements = new[]
                {
                    GraphNodes.LabelPart(() => vm?.PhaseName.Value ?? ""),
                    new NodeAnnouncement(state, live: true, kind: AnnouncementKinds.Value),
                    GraphNodes.DisabledPart(available),
                },
                SearchText = () => vm?.PhaseName.Value ?? "",
                OnActivate = () =>
                {
                    // Can't "go to" the phase you're already on; locked phases can't be jumped to.
                    if (!available() || vm.IsSelected.Value) return;
                    UiSound.Play(UISoundType.ButtonClick);
                    vm.SetSelectedFromView(true);
                },
            };
        }

        /// <summary>A class (or archetype) choice in the Class phase. Mirrors the game's item view:
        /// every shown class is CLICKABLE whenever <c>IsAvailible</c> (a locked prestige class stays
        /// selectable to read its requirements — announced "prerequisites not met", not "disabled");
        /// activation routes through WarnLevelupPlansWillDropBeforeAction (the discard-plan confirm in
        /// level-up mode) then the unselect-archetypes / toggle-selection flow, exactly like OnClick.</summary>
        public static NodeVtable ClassItem(CharGenClassSelectorItemVM vm)
        {
            Func<bool> interactable = () => vm != null && vm.IsAvailible;
            return new NodeVtable
            {
                ControlType = ControlTypes.RadioButton,
                Announcements = new[]
                {
                    GraphNodes.LabelPart(() => vm?.DisplayName ?? ""),
                    GraphNodes.SelectedPart(() => vm.IsSelected.Value),
                    new NodeAnnouncement(() => interactable() && !vm.PrerequisitesDone
                        ? Loc.T("state.prerequisites_not_met") : null, live: true, kind: AnnouncementKinds.Value),
                    GraphNodes.DisabledPart(interactable),
                },
                SearchText = () => vm?.DisplayName ?? "",
                StateText = () => vm.IsSelected.Value ? Loc.T("state.selected") : null,
                OnActivate = () =>
                {
                    if (!interactable()) return;
                    UiSound.Play(UISoundType.ButtonClick);
                    vm.WarnLevelupPlansWillDropBeforeAction(() =>
                    {
                        if (!vm.TryUnselectArchetypes()) vm.SetSelectedFromView(!vm.IsSelected.Value);
                    });
                },
            };
        }
    }
}
