using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using WrathAccess.Settings;
using WrathAccess.Speech;

namespace WrathAccess.Events
{
    /// <summary>
    /// Builds the event settings (the events analogue of <see cref="WrathAccess.UI.Announcements.AnnouncementRegistry"/>),
    /// in two root nodes mirroring the UI tab's global-defaults + per-item-overrides shape:
    /// <list type="bullet">
    /// <item><b>Event settings</b> (<c>events.settings</c>) — the SIMPLIFIED global view: one node per
    /// source bucket (party/enemy/neutral) plus <c>unitless</c> (sourceless events), each with Announce +
    /// Speech configuration + Positional audio. These are the base defaults.</item>
    /// <item><b>Individual event customization</b> (<c>events.custom</c>) — every concrete
    /// <see cref="ModEvent"/> with an <see cref="EventSettingsAttribute"/>, per applicable source, with the
    /// same three settings but INHERIT-aware: Announce/Positional are <see cref="NullableBoolSetting"/>s
    /// that follow the source's global default, and Speech configuration gets an "Inherit" option.</item>
    /// </list>
    /// Built POST-load (config list known, saved values re-apply). The dispatcher resolves each event's
    /// effective setting: its per-event override if set, else the source's global default.
    /// </summary>
    internal static class EventRegistry
    {
        private static readonly Dictionary<Type, string> _keys = new Dictionary<Type, string>();
        private static readonly EventSources[] SourceOrder = { EventSources.Party, EventSources.Enemy, EventSources.Neutral };
        // The global buckets, in display order. "unitless" is the default for sourceless events.
        private static readonly string[] Buckets = { "party", "enemy", "neutral", "unitless" };

        public static void RegisterDefaults()
        {
            ModSettingsRegistry.EnsureCategory("events", "Events", "category.events");

            // Global defaults FIRST (the custom nodes reference these buckets as their inherit source).
            BuildGlobalSettings();

            Assembly asm = Assembly.GetExecutingAssembly();
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }

            foreach (var t in types.Where(t => !t.IsAbstract && typeof(ModEvent).IsAssignableFrom(t)
                                               && t.GetCustomAttribute<EventSettingsAttribute>() != null))
            {
                try { RegisterCustom(t); }
                catch (Exception e) { Main.Log?.Error("[events] register failed for " + t.Name + ": " + e.Message); }
            }

            ModSettings.Reindex();
            ModSettings.ReapplyUnknown(); // apply values saved before these categories existed
        }

        // ---- the simplified global view: per-source defaults ----

        private static void BuildGlobalSettings()
        {
            ModSettingsRegistry.EnsureCategory("events.settings", "Events/Event settings", "event.settings");
            foreach (var bucket in Buckets)
            {
                var sc = ModSettingsRegistry.EnsureCategory("events.settings." + bucket,
                    "Events/Event settings/" + bucket, "source." + bucket);
                BuildGlobalOutput(sc);
            }
        }

        // One source's global default: plain settings (the base the per-event overrides inherit).
        private static void BuildGlobalOutput(CategorySetting into)
        {
            if (into.GetByKey("enabled") == null)
                into.Add(new BoolSetting("enabled", "Announce", true, "event.enabled"));
            if (into.GetByKey("speech_config") == null)
                into.Add(new ChoiceSetting("speech_config", "Speech configuration", () => ConfigChoices(), "default", "event.speech_config"));
            if (into.GetByKey("positional") == null)
                into.Add(new BoolSetting("positional", "Positional audio", true, "event.positional"));
        }

        private static CategorySetting GlobalBucket(string bucket)
            => ModSettingsRegistry.EnsureCategory("events.settings." + bucket, "Events/Event settings/" + bucket, "source." + bucket);

        // ---- individual event customization: per-event, per-source, inheriting the global ----

        private static void RegisterCustom(Type t)
        {
            var attr = t.GetCustomAttribute<EventSettingsAttribute>();
            var key = ToSnake(StripSuffix(t.Name, "Event"));
            _keys[t] = key;

            ModSettingsRegistry.EnsureCategory("events.custom", "Events/Individual event customization", "event.custom");
            var cat = ModSettingsRegistry.EnsureCategory("events.custom." + key, "Events/custom/" + attr.Label, "event." + key);

            if (attr.Sources == EventSources.None)
                BuildCustomOutput(cat, GlobalBucket("unitless"));
            else
                foreach (var src in SourceOrder)
                {
                    if ((attr.Sources & src) == 0) continue;
                    var name = src.ToString().ToLowerInvariant();
                    var sc = ModSettingsRegistry.EnsureCategory("events.custom." + key + "." + name,
                        "Events/custom/" + attr.Label + "/" + src, "source." + name);
                    BuildCustomOutput(sc, GlobalBucket(name));
                }

            // Optional per-event extra settings (custom verbosity etc.), same hook as announcements.
            t.GetMethod("RegisterSettings", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(CategorySetting) }, null)?.Invoke(null, new object[] { cat });
        }

        // One event-source override: Announce/Positional follow the global (NullableBool, null = inherit);
        // the speech-config dropdown is a NullableChoiceSetting resolving through the source's bucket.
        private static void BuildCustomOutput(CategorySetting into, CategorySetting global)
        {
            if (into.GetByKey("enabled") == null)
                into.Add(new NullableBoolSetting("enabled", "Announce", global?.Get<BoolSetting>("enabled"), "event.enabled"));
            if (into.GetByKey("speech_config") == null)
            {
                var sc = new NullableChoiceSetting("speech_config", "Speech configuration",
                    () => ConfigChoices(), localizationKey: "event.speech_config");
                var globalSc = global?.Get<ChoiceSetting>("speech_config");
                sc.ResolveInherited = () => globalSc?.Current?.Id ?? "default";
                into.Add(sc);
            }
            if (into.GetByKey("positional") == null)
                into.Add(new NullableBoolSetting("positional", "Positional audio", global?.Get<BoolSetting>("positional"), "event.positional"));
        }

        // Default + the user's additional speech configs. Read LIVE (the dropdowns hold a provider, not
        // this list), so configs added/removed at runtime show up the next time a dropdown reads it.
        // (Inheriting is the setting's own nullable state now, not a sentinel option in this list.)
        private static List<Choice> ConfigChoices()
        {
            var choices = new List<Choice> { new Choice("default", "Default", "event.config_default") };
            foreach (var id in SpeechConfigRegistry.Ids())
                choices.Add(new Choice(id, SpeechConfigRegistry.Name(id)));
            return choices;
        }

        // ---- dispatcher-side resolution (per-event override, else the source's global default) ----

        public static bool Enabled(ModEvent e)
        {
            var nb = ModSettings.GetSetting<NullableBoolSetting>(CustomPath(e) + ".enabled");
            return nb != null ? nb.Resolved : GlobalEnabled(e.Source);
        }

        public static bool Positional(ModEvent e)
        {
            var nb = ModSettings.GetSetting<NullableBoolSetting>(CustomPath(e) + ".positional");
            return nb != null ? nb.Resolved : GlobalPositional(e.Source);
        }

        public static string ConfigId(ModEvent e)
        {
            var id = ModSettings.GetSetting<NullableChoiceSetting>(CustomPath(e) + ".speech_config")?.EffectiveId;
            return string.IsNullOrEmpty(id) ? GlobalConfigId(e.Source) : id;
        }

        private static bool GlobalEnabled(EventSources s)
            => ModSettings.GetSetting<BoolSetting>("events.settings." + Bucket(s) + ".enabled")?.Get() ?? true;
        private static bool GlobalPositional(EventSources s)
            => ModSettings.GetSetting<BoolSetting>("events.settings." + Bucket(s) + ".positional")?.Get() ?? true;
        private static string GlobalConfigId(EventSources s)
            => ModSettings.GetSetting<ChoiceSetting>("events.settings." + Bucket(s) + ".speech_config")?.Current?.Id ?? "default";

        private static string Bucket(EventSources s)
            => s == EventSources.Party ? "party"
             : s == EventSources.Enemy ? "enemy"
             : s == EventSources.Neutral ? "neutral"
             : "unitless";

        private static string CustomPath(ModEvent e)
        {
            var key = _keys.TryGetValue(e.GetType(), out var k) ? k : ToSnake(StripSuffix(e.GetType().Name, "Event"));
            return e.Source == EventSources.None
                ? "events.custom." + key
                : "events.custom." + key + "." + e.Source.ToString().ToLowerInvariant();
        }

        // ---- key derivation (matches AnnouncementRegistry's snake_case) ----

        private static string StripSuffix(string name, string suffix)
            => name.EndsWith(suffix) ? name.Substring(0, name.Length - suffix.Length) : name;

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
    }
}
