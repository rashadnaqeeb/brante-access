namespace BranteAccess.Module
{
    /// <summary>
    /// While active, the mod owns the keyboard: FocusModePatches skips the game's own input-only
    /// Update bodies (their non-input work, where any exists, is untouched) and InputManager's UI
    /// category goes live. Toggling off restores stock behavior for a sighted co-pilot. Defaults ON -
    /// the mod's whole audience runs focused. Brante has no game-side suppression lever (input reads
    /// are bare Input.GetKeyDown calls scattered across Updates), so unlike wotr's KeyboardAccess
    /// guard this is a plain flag the Harmony prefixes consult.
    /// </summary>
    internal static class FocusMode
    {
        public static bool Active { get; private set; } = true;

        public static void Toggle() => Set(!Active);

        public static void Set(bool on)
        {
            if (on == Active) return;
            Active = on;
            // Interrupt: the toggle changes what every subsequent key does; stale speech misleads.
            Mod.Speech.Speak(Loc.T(on ? "focusmode.on" : "focusmode.off"), interrupt: true);
            Mod.Log("FocusMode: " + (on ? "on" : "off"));
        }
    }
}
