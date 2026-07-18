namespace WrathAccess.UI.Announcements
{
    /// <summary>
    /// One addressable KIND of spoken announcement part (label, role, status, position…) — the
    /// settings identity the registry keys per-part enablement/order on. The graph's control types
    /// and the scanner/event registries share these keys; the classes are markers (the old element
    /// rendering is gone), carrying only the key + settings attributes.
    /// </summary>
    public abstract class Announcement
    {
        /// <summary>Stable identity (e.g. "label", "role", "position") — used for settings/ordering.</summary>
        public abstract string Key { get; }
    }
}
