using BranteAccess.Module.Speech;

namespace BranteAccess.Module
{
    /// <summary>
    /// Shorthand for the mod's spoken/user-facing strings. RULE (CLAUDE.md): every string the mod
    /// speaks or displays comes from the locale tables - speech/UI text from lang/&lt;code&gt;/ui.txt
    /// via this helper or Message.Localized; binding labels via the settings table. Word order lives
    /// in {var} templates, never in code-side concatenation. The exception is debug-only tooling.
    /// </summary>
    internal static class Loc
    {
        public static string T(string key) => Message.Localized("ui", key).Resolve();
        public static string T(string key, object vars) => Message.Localized("ui", key, vars).Resolve();
    }
}
