using System;

namespace WrathAccess.UI.Announcements
{
    /// <summary>
    /// Opts an <see cref="Announcement"/> subclass into the global Announcements settings UI. Without it
    /// the global category is still created (per-element overrides need it as a fallback) but hidden, so
    /// the user only configures that announcement through per-element overrides. Apply to announcements
    /// whose global toggle reads naturally (label, role, value, tooltip, position); skip context-specific ones.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ShowInGlobalSettingsAttribute : Attribute { }

    /// <summary>
    /// Overrides the per-element settings key that would otherwise be derived from the class name — for
    /// awkward names, or to share one override tree across several proxies.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ElementSettingsKeyAttribute : Attribute
    {
        public string Key { get; }
        public ElementSettingsKeyAttribute(string key) { Key = key; }
    }
}
