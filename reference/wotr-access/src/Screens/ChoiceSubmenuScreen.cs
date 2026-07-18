using System;
using System.Collections.Generic;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// A list of options to pick from (e.g. a dropdown's values), pushed as a CHILD SCREEN of whatever
    /// screen opened it (<see cref="Screen.PushChild"/>). As a child it's the focused screen while open and
    /// owns the keyboard; selecting an option or backing out removes it, and ScreenManager re-focuses the
    /// parent on its remembered control (the dropdown) automatically. Reusable for any "open a list and
    /// pick one" interaction.
    ///
    /// Graph-native: the option list is immutable per instance, and <c>SetStart</c> lands focus on the
    /// CURRENT option (the graph's start node), so opening reads the selected value first.
    /// </summary>
    public sealed class ChoiceSubmenuScreen : Screen
    {
        private readonly string _title;
        private readonly List<string> _options;
        private readonly int _current;
        private readonly Action<int> _onSelect;

        public ChoiceSubmenuScreen(string title, List<string> options, int current, Action<int> onSelect)
        {
            _title = title;
            _options = options;
            _current = current;
            _onSelect = onSelect;
            Wrap = true;
        }

        /// <summary>Open the submenu as a child of the current screen.</summary>
        public static void Open(string title, List<string> options, int current, Action<int> onSelect)
        {
            // Opening IS the activation — the click every caller (dropdowns, sorters, link pickers)
            // used to get from the old proxy base's default activation sound. One chokepoint here.
            UiSound.Play(Kingmaker.UI.UISoundType.ButtonClick);
            ScreenManager.Current?.PushChild(new ChoiceSubmenuScreen(title, options, current, onSelect));
        }

        public override string Key => "overlay.choicesubmenu";
        public override string ScreenName => _title;
        public override bool IsActive() => false; // never poll-pushed — only ever a child screen

        public override IEnumerable<ElementAction> GetActions()
        {
            // Back closes the submenu without changing the value.
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => Close());
        }

        private void Close() => ParentScreen?.RemoveChild(this);


        public override void Build(GraphBuilder b)
        {
            for (int i = 0; i < _options.Count; i++)
            {
                int idx = i;
                string label = _options[i];
                // Snapshot is safe: the submenu is ephemeral (a fresh instance per open, closed by the
                // selection itself), so the selected state can't change while it lives.
                b.AddItem(ControlId.Structural("choice:" + i), GraphNodes.ChoiceOption(
                    () => label, () => idx == _current,
                    () => { _onSelect?.Invoke(idx); Close(); },
                    GraphNodes.Position(i + 1, _options.Count)));
            }
            if (_current >= 0 && _current < _options.Count)
                b.SetStart(ControlId.Structural("choice:" + _current)); // land on the current option
        }
    }
}
