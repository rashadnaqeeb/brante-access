using System;

namespace WrathAccess.UI
{
    /// <summary>
    /// An action a UIElement advertises (distinct from <c>InputAction</c>, which is a
    /// keybinding). Navigators discover actions by <see cref="Id"/> and invoke them —
    /// they never switch on element type. Parameterless actions (activate/increase/…)
    /// ignore args; parameterized ones (setValue/…) read named values from the
    /// anonymous-object <c>args</c> via <see cref="ActionArgs"/>.
    /// </summary>
    public sealed class ElementAction
    {
        public string Id { get; }
        public Message Label { get; }
        private readonly Action<object> _execute;

        public ElementAction(string id, Message label, Action<object> execute)
        {
            Id = id;
            Label = label;
            _execute = execute;
        }

        public void Execute(object args = null) => _execute?.Invoke(args);
    }

    /// <summary>Standard action ids navigators map inputs to.</summary>
    public static class ActionIds
    {
        public const string Activate = "activate";  // primary action (Enter / left click)
        public const string Context = "context";    // secondary action (Backspace / right click)
        public const string Back = "back";           // screen-level back/close (Escape)
        public const string Increase = "increase";
        public const string Decrease = "decrease";
        public const string SetValue = "setValue";
        public const string Reset = "reset";
    }
}
