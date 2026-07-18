using System;
using BranteAccess.Module.Speech;

namespace BranteAccess.Module.UI
{
    /// <summary>A screen-level action advertised by <see cref="Screens.Screen.GetActions"/> and
    /// dispatched by id (the navigator maps Escape to <see cref="ActionIds.Back"/>). Ported from
    /// wotr-access.</summary>
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
        public const string Activate = "activate";  // primary action (Enter)
        public const string Context = "context";    // secondary action (Backspace)
        public const string Back = "back";          // screen-level back/close (Escape)
        public const string Increase = "increase";
        public const string Decrease = "decrease";
        public const string SetValue = "setValue";
        public const string Reset = "reset";
    }
}
