using BranteAccess.Module.UI.Graph;

namespace BranteAccess.Module.UI
{
    /// <summary>
    /// The control-type registry: each entry is a <see cref="ControlType"/> VALUE - its key, the speak
    /// order of its announcement kinds, and the parts common to every control of the type (the localized
    /// role word). A node factory just sets the type and gets the role word and the ordering for free.
    /// Ported from wotr-access, trimmed to the types Brante's surfaces need; new types are added here.
    /// </summary>
    public static class ControlTypes
    {
        private static readonly string[] StandardOrder =
        {
            AnnouncementKinds.Label,
            AnnouncementKinds.Role,
            AnnouncementKinds.Value,
            AnnouncementKinds.Selected,
            AnnouncementKinds.Enabled,
            AnnouncementKinds.Tooltip,
            AnnouncementKinds.Position,
        };

        private static NodeAnnouncement[] RoleWord(string word)
            => new[] { new NodeAnnouncement(() => Loc.T("role." + word), kind: AnnouncementKinds.Role) };

        public static readonly ControlType Button = new ControlType
        {
            Key = "button",
            Order = StandardOrder,
            Common = () => RoleWord("button"),
        };

        public static readonly ControlType Toggle = new ControlType
        {
            Key = "toggle",
            Order = StandardOrder,
            Common = () => RoleWord("toggle"),
        };

        public static readonly ControlType Slider = new ControlType
        {
            Key = "slider",
            Order = StandardOrder,
            Common = () => RoleWord("slider"),
        };

        /// <summary>One option of a single-select group (a language list, a save-slot row acting as a
        /// pick list).</summary>
        public static readonly ControlType RadioButton = new ControlType
        {
            Key = "radio_button",
            Order = StandardOrder,
            Common = () => RoleWord("radiobutton"),
        };

        /// <summary>A dropdown: value = the current option; activation opens the option list.</summary>
        public static readonly ControlType ComboBox = new ControlType
        {
            Key = "combo_box",
            Order = StandardOrder,
            Common = () => RoleWord("combobox"),
        };

        /// <summary>A tab in a tab strip (save/load chapter pages, HUD window bar).</summary>
        public static readonly ControlType Tab = new ControlType
        {
            Key = "tab",
            Order = StandardOrder,
            Common = () => RoleWord("tab"),
        };

        /// <summary>An expandable group header (a tree section). No role word of its own - the announcer
        /// appends the expanded/collapsed state word.</summary>
        public static readonly ControlType Group = new ControlType
        {
            Key = "group",
            Order = StandardOrder,
        };

        /// <summary>A read-only text line (a transcript passage, a stat readout) - no role word.</summary>
        public static readonly ControlType Text = new ControlType
        {
            Key = "text",
            Order = StandardOrder,
        };

        /// <summary>Every registered type. New types are added here.</summary>
        public static readonly ControlType[] All = { Button, Toggle, Slider, RadioButton, ComboBox, Tab, Group, Text };
    }
}
