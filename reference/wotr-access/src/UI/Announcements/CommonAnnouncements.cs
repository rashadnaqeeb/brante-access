namespace WrathAccess.UI.Announcements
{
    [ShowInGlobalSettings]
    public sealed class LabelAnnouncement : Announcement
    {
        public override string Key => "label";
    }

    [ShowInGlobalSettings]
    public sealed class RoleAnnouncement : Announcement
    {
        public override string Key => "role";
    }

    public sealed class EnabledAnnouncement : Announcement
    {
        public override string Key => "enabled";
    }

    public sealed class SelectedAnnouncement : Announcement
    {
        public override string Key => "selected";
    }

    [ShowInGlobalSettings]
    public sealed class ValueAnnouncement : Announcement
    {
        public override string Key => "value";
    }

    [ShowInGlobalSettings]
    public sealed class TooltipAnnouncement : Announcement
    {
        public override string Key => "tooltip";
    }

    [ShowInGlobalSettings]
    public sealed class PositionAnnouncement : Announcement
    {
        public override string Key => "position";
    }

    public sealed class CountAnnouncement : Announcement
    {
        public override string Key => "count";
    }

}