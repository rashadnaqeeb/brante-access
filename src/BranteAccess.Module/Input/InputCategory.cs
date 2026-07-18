namespace BranteAccess.Module.Input
{
    /// <summary>
    /// The input layer an action belongs to. Screens declare which categories they use (in priority
    /// order); each frame the live set is the focused screen's declaration plus <see cref="Global"/>.
    /// An identical chord bound in two live categories resolves to the higher-priority one
    /// (shadowing); conflict prevention only applies WITHIN a category. Trimmed from wotr's
    /// seven-category set: Brante is a menu-driven narrative game with no world cursor, so UI + Global
    /// cover it. Add a category here only when a screen genuinely needs a different key layer
    /// (candidate: text entry for the name-request window).
    /// </summary>
    public enum InputCategory
    {
        /// <summary>Always live, even when focus mode is off (focus toggle itself, dev reload).</summary>
        Global,
        /// <summary>Screen/menu navigation - live while focus mode is on (later: while the focused
        /// screen declares it).</summary>
        UI,
    }
}
