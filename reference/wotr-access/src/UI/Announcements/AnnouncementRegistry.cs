using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using WrathAccess.Settings;

namespace WrathAccess.UI.Announcements
{
    /// <summary>
    /// Builds the announcement settings (ported from SayTheSpire2). For EVERY concrete
    /// <see cref="Announcement"/> subclass: a global category <c>announcements.{key}</c> with an
    /// <c>enabled</c> toggle + <c>include_suffix</c> toggle (+ anything a static
    /// <c>RegisterSettings(CategorySetting)</c> on the announcement declares), hidden from the UI unless
    /// the announcement carries <see cref="ShowInGlobalSettingsAttribute"/>. For EVERY concrete
    /// <see cref="UIElement"/> with an <c>[AnnouncementOrder]</c>: per-element-type overrides at
    /// <c>ui.{element}.announcements.{ann}.{setting}</c> — a <see cref="NullableBoolSetting"/> mirroring
    /// each global setting (inherits the global until the user overrides it). <see cref="AnnouncementContext"/>
    /// resolves per-element override → global → default. (WotR has no buffer/hotkey contexts; the
    /// announcement-reorder feature is not ported yet.)
    /// </summary>
    public static class AnnouncementRegistry
    {
        public static void RegisterDefaults()
        {
            var asm = Assembly.GetExecutingAssembly();
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }

            // Globals first — per-element overrides reference them as fallbacks.
            foreach (var t in types.Where(t => !t.IsAbstract && typeof(Announcement).IsAssignableFrom(t)))
            {
                try { RegisterGlobal(t); }
                catch (Exception e) { Main.Log?.Error("[ann] global register failed for " + t.Name + ": " + e.Message); }
            }


            // Graph control types (the registry): the same per-type override schema, keyed on the registry
            // entry instead of a proxy class. Keys shared with legacy collapsed element keys ("toggle",
            // "slider") land in the SAME categories — one settings identity across both systems.
            foreach (var ct in WrathAccess.UI.ControlTypes.All)
            {
                try { RegisterControlTypeOverrides(ct); }
                catch (Exception e) { Main.Log?.Error("[ann] control-type register failed for " + ct.Key + ": " + e.Message); }
            }
        }

        private static void RegisterControlTypeOverrides(WrathAccess.UI.Graph.ControlType ct)
        {
            if (ct?.Key == null || ct.Order == null) return;
            var typeDisplay = SnakeToTitle(ct.Key);
            foreach (var kind in ct.Order)
            {
                var kindDisplay = SnakeToTitle(kind);
                var global = ModSettingsRegistry.EnsureCategory("announcements." + kind, "Announcements/" + kindDisplay);
                var perType = ModSettingsRegistry.EnsureCategory(
                    "ui." + ct.Key + ".announcements." + kind,
                    "UI/" + typeDisplay + "/Announcements/" + kindDisplay,
                    "/element." + ct.Key + "/announcements_group/announcement." + kind);
                foreach (var child in global.Children)
                {
                    if (perType.GetByKey(child.Key) != null) continue;
                    var ov = CreateOverride(child);
                    if (ov != null) perType.Add(ov);
                }
            }
        }

        /// <summary>Is an announcement part enabled for a control type — the graph announcer's
        /// PartFilter resolver: per-type override → global per-kind toggle → true. Kindless (custom)
        /// parts always speak.</summary>
        public static bool PartEnabled(string typeKey, string kind)
        {
            if (kind == null) return true;
            if (typeKey != null)
            {
                var ov = ModSettings.GetSetting<NullableBoolSetting>(
                    "ui." + typeKey + ".announcements." + kind + ".enabled");
                if (ov != null && ov.IsOverridden) return ov.LocalValue.Value;
            }
            var global = ModSettings.GetSetting<BoolSetting>("announcements." + kind + ".enabled");
            return global == null || global.Get();
        }

        private static void RegisterGlobal(Type annType)
        {
            var key = DeriveAnnouncementKey(annType);
            var display = DeriveDisplayName(StripSuffix(annType.Name, "Announcement"));
            var category = ModSettingsRegistry.EnsureCategory("announcements." + key, "Announcements/" + display,
                "/announcement." + key); // "announcements" root segment skipped (empty), leaf gets the loc key

            // Created either way (per-element overrides need it as a fallback); shown only if opted in.
            category.Hidden = annType.GetCustomAttribute<ShowInGlobalSettingsAttribute>() == null;

            if (category.GetByKey("enabled") == null)
                category.Add(new BoolSetting("enabled", "Announce", true, "ann.enabled"));
            if (category.GetByKey("include_suffix") == null)
                category.Add(new BoolSetting("include_suffix", "Include suffix punctuation", true, "ann.suffix"));

            annType.GetMethod("RegisterSettings", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(CategorySetting) }, null)?.Invoke(null, new object[] { category });
        }
        // Mirror a global setting as a Nullable* override that inherits from it. (Only Bool globals exist
        // today — enabled / include_suffix; extend for Int/Choice if an announcement declares them.)
        private static Setting CreateOverride(Setting global)
        {
            switch (global)
            {
                case BoolSetting b: return new NullableBoolSetting(b.Key, b.Label, b, b.LocalizationKey);
                default: return null;
            }
        }

        // ---- key / label derivation ----

        public static string DeriveAnnouncementKey(Type annType) => ToSnake(StripSuffix(annType.Name, "Announcement"));

        public static string DeriveElementKey(Type elType)
        {
            var attr = elType.GetCustomAttribute<ElementSettingsKeyAttribute>();
            if (attr != null) return attr.Key;
            return ToSnake(StripPrefix(StripSuffix(elType.Name, "Element"), "Proxy"));
        }

        private static string DeriveElementDisplayName(Type elType)
        {
            var attr = elType.GetCustomAttribute<ElementSettingsKeyAttribute>();
            if (attr != null) return SnakeToTitle(attr.Key);
            return DeriveDisplayName(StripPrefix(StripSuffix(elType.Name, "Element"), "Proxy"));
        }

        private static string DeriveDisplayName(string pascal)
        {
            var sb = new StringBuilder(pascal.Length + 4);
            for (int i = 0; i < pascal.Length; i++)
            {
                if (i > 0 && char.IsUpper(pascal[i])) sb.Append(' ');
                sb.Append(pascal[i]);
            }
            return sb.ToString();
        }

        private static string SnakeToTitle(string snake)
        {
            var parts = snake.Split('_');
            var sb = new StringBuilder();
            foreach (var p in parts)
            {
                if (p.Length == 0) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(char.ToUpperInvariant(p[0]));
                if (p.Length > 1) sb.Append(p.Substring(1));
            }
            return sb.ToString();
        }

        private static string ToSnake(string pascal)
        {
            var sb = new StringBuilder(pascal.Length + 4);
            for (int i = 0; i < pascal.Length; i++)
            {
                if (i > 0 && char.IsUpper(pascal[i])) sb.Append('_');
                sb.Append(char.ToLowerInvariant(pascal[i]));
            }
            return sb.ToString();
        }

        private static string StripSuffix(string name, string suffix)
            => name.EndsWith(suffix) ? name.Substring(0, name.Length - suffix.Length) : name;
        private static string StripPrefix(string name, string prefix)
            => name.StartsWith(prefix) ? name.Substring(prefix.Length) : name;
    }
}
