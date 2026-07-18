namespace BranteAccess.Core.Modularity
{
    /// <summary>
    /// Dev-only seam the reloadable module exposes so the host's dev server can drive and inspect
    /// the mod's own navigator (which owns the keyboard and reads its own input, so OS-level key
    /// injection is not how features get verified). The host probes the loaded module for this by
    /// cast each request, so it always talks to the live generation. Not used in normal play.
    /// </summary>
    public interface IDevDriver
    {
        /// <summary>Dispatch one semantic input verb (an action key like "nav.down", "nav.activate")
        /// through the module's input dispatch - the same code path a real key press runs. Returns a
        /// status line, or null when the verb is unknown so the caller reports it.</summary>
        string DispatchUi(string action);

        /// <summary>Describe the navigator's live state: active screen, focus path, focused node -
        /// independent of what was last spoken.</summary>
        string DescribeNav();

        /// <summary>Type text into a live mod-owned text entry (name request). Returns a status
        /// line, or null when no text entry is active.</summary>
        string TypeText(string text);
    }
}
