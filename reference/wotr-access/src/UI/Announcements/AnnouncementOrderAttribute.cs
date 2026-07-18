using System;

namespace WrathAccess.UI.Announcements
{
    /// <summary>
    /// Declares the canonical order of announcements in an element's focus message.
    /// The composer sorts yielded parts by this list; anything not declared is
    /// appended in yield order. A hint, not a contract.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public sealed class AnnouncementOrderAttribute : Attribute
    {
        public Type[] Types { get; }
        public AnnouncementOrderAttribute(params Type[] types) { Types = types; }
    }
}
