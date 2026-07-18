namespace WrathAccess.Input
{
    /// <summary>
    /// The input layer an action belongs to. Screens declare which categories they use
    /// (<see cref="WrathAccess.Screens.Screen.InputCategories"/>, in priority order) and only the
    /// TOP screen's declaration is live — plus <see cref="Global"/>, which is always on. Within the
    /// live declaration, an identical chord bound in two categories resolves to the earlier-declared
    /// one (shadowing): the in-game screen flips [UI, Exploration] ↔ [Exploration, UI] with HUD focus,
    /// so the same arrows navigate the HUD when focused and move the world cursor when not — while
    /// chords only one category binds (Shift+arrows, scanner keys) work in both states. Conflict
    /// prevention therefore only applies WITHIN a category (the rebind capture steals).
    /// </summary>
    public enum InputCategory
    {
        /// <summary>Always live, even when focus mode is off (focus toggle, mod menu, quick save).</summary>
        Global,
        /// <summary>Screen/menu navigation — live when the focused screen declares it.</summary>
        UI,
        /// <summary>The in-game world: cursor movement, interaction, scanner, overlays, party, combat.
        /// CONTROL-GATED — the in-game screen drops this whenever we don't have control (a cutscene,
        /// dialogue, loading), so movement/scanner/etc. go dead then (see ControlState / InGameScreen).</summary>
        Exploration,
        /// <summary>Base in-game keys that must work EVEN without control — opening the pause menu /
        /// cancelling targeting. The in-game screen claims this whenever it's in a game, control or not, so
        /// Escape still opens the menu during a cutscene/dialogue while <see cref="Exploration"/> is gated off.</summary>
        InGame,
        /// <summary>The world (global) map: its own scanner / cursor / travel keys. A SEPARATE category from
        /// <see cref="Exploration"/> so the world-map systems are fully isolated from the in-area ones — the
        /// same physical keys (PageUp/Down, …) route to whichever screen is up. Declared by the world-map screen.</summary>
        WorldMap,
        /// <summary>The mod's service-window hotkeys (open character sheet / inventory / spellbook / journal).
        /// Declared by the in-game screen ONLY while it has control, and by the world-map screen — so the same
        /// chords open the windows both in an area and on the world map, yet stay dead during a cutscene /
        /// dialogue / loading. A category of its own so they ride neither <see cref="Exploration"/> (in-area
        /// only) nor <see cref="UI"/> (every menu and modal).</summary>
        Windows,
        /// <summary>The formation window's WASD cursor (move/place party members). Declared by the formation
        /// screen ONLY while its editing field is focused, so WASD drive the field cursor there and stay free
        /// for navigation/typeahead on the screen's other tab stops.</summary>
        Formation,
    }
}
