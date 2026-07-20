using BranteAccess.Module.Input;

namespace BranteAccess.Module.UI
{
    /// <summary>
    /// Holds the active Navigator and is the entry point input dispatches into. ScreenManager
    /// re-attaches it on screen change (via the seams wired in ModModule.Load). Ported from wotr-access.
    /// </summary>
    public static class Navigation
    {
        public static Navigator Active = new GraphNavigator();

        public static void Attach(Screens.Screen screen) => Active?.Attach(screen);

        /// <summary>True when something is focused (the navigator owns the keys).</summary>
        public static bool HasFocus => Active != null && Active.HasFocus;

        public static bool DispatchJustPressed(InputAction action) =>
            Active != null && Active.OnInputJustPressed(action);

        /// <summary>Feed typed characters to the active navigator's type-ahead search (per frame).</summary>
        public static void TickTypeahead() => Active?.TickTypeahead();

        public static void AnnounceCurrent() => Active?.AnnounceCurrent();

        /// <summary>Re-establish initial focus if the focused screen has focusable content but nothing is
        /// focused yet (e.g. a screen that built its content lazily after attach). Ticked each frame.</summary>
        public static void EnsureFocus() => Active?.EnsureFocus();

        /// <summary>Return to the unfocused state - see <see cref="Navigator.Blur"/>.</summary>
        public static void Blur() => Active?.Blur();

        /// <summary>Notify that a screen closed (its per-screen nav state is dropped).</summary>
        public static void ScreenClosed(Screens.Screen screen) => Active?.ScreenClosed(screen);

        /// <summary>Move focus to a graph node by id.</summary>
        public static void FocusNode(Graph.ControlId id, bool announce = true) => Active?.FocusNode(id, announce);

        /// <summary>Move focus to the first node of a Tab-stop.</summary>
        public static void FocusStop(object stopKey) => Active?.FocusStop(stopKey);

        /// <summary>The Tab-stop the focused node belongs to, or null.</summary>
        public static object FocusedStopKey => Active?.FocusedStopKey;

        /// <summary>The region the focused node belongs to, or null.</summary>
        public static object FocusedRegionKey => Active?.FocusedRegionKey;
    }
}
