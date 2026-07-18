namespace WrathAccess
{
    /// <summary>
    /// Shorthand for the mod's spoken/user-facing strings. RULE: every string the mod speaks or displays
    /// MUST come from the locale tables — speech/UI text from <c>assets/locale/&lt;lang&gt;/ui.json</c>
    /// (via this helper or <see cref="Message.Localized"/>), settings labels via
    /// <c>Setting.LocalizationKey</c> + <c>settings.json</c>. Never hardcode English in a Speak call or a
    /// label. The one exception is debug-only tooling (Player.log dumps, dev hotkeys).
    /// </summary>
    internal static class Loc
    {
        public static string T(string key) => Message.Localized("ui", key).Resolve();
        public static string T(string key, object args) => Message.Localized("ui", key, args).Resolve();
    }
}
