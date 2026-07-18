using System.Collections.Generic;
using System.Linq;
using WrathAccess.Settings;

namespace WrathAccess.Speech
{
    /// <summary>
    /// The user's ADDITIONAL speech configurations — beyond the default (the root Speech settings) — for
    /// the events system to speak specific things through (e.g. enemy damage in a different voice). Mirrors
    /// the overlay roster: a hidden id "list" under <c>speech.additional</c>, created PRE-load so the saved
    /// set is restored, then each config's subtree (a hidden name + the shared handler/params schema) built
    /// POST-load with <see cref="ModSettings.ReapplyUnknown"/> filling values saved before the subtree
    /// existed. Add/Remove/Rename at runtime. Each config resolves to a <see cref="SpeechConfig"/>.
    /// </summary>
    public static class SpeechConfigRegistry
    {
        /// <summary>Pre-load (in BuildSettings, after the Speech category exists): create the
        /// speech.additional category + its id list, so Load can restore the roster.</summary>
        public static void Register()
        {
            var additional = ModSettingsRegistry.EnsureCategory(
                "speech.additional", "Speech/Additional speech configurations", "speech.additional");
            if (additional.Get<StringSetting>("list") == null)
                additional.Add(new StringSetting("list", "Config list", "", "speech.config.list") { Hidden = true });
        }

        /// <summary>Post-load (after ModSettings.Initialize): build each saved config's subtree, then
        /// re-apply the values saved before those subtrees existed.</summary>
        public static void BuildConfigs()
        {
            foreach (var id in Ids()) BuildConfig(id);
            ModSettings.Reindex();
            ModSettings.ReapplyUnknown();
        }

        public static IReadOnlyList<string> Ids() => IdList();

        public static string Name(string id) => NameSetting(id)?.Get() ?? id;

        public static void SetName(string id, string name)
        {
            if (!string.IsNullOrWhiteSpace(name)) NameSetting(id)?.Set(name);
        }

        /// <summary>The config for an id — an additional config, or the default for "default"/unknown.
        /// This is what the events system calls to "speak through config X".</summary>
        public static SpeechConfig Get(string id)
        {
            if (string.IsNullOrEmpty(id) || id == "default") return SpeechManager.Default;
            var cat = ConfigCat(id);
            return cat != null ? new SpeechConfig(cat, SpeechManager.Default) : SpeechManager.Default;
        }

        public static string Add()
        {
            var ids = IdList();
            var id = NewId(ids);
            BuildConfig(id, "Configuration " + (ids.Count + 1));
            ids.Add(id);
            ListSetting().Set(string.Join(",", ids));
            ModSettings.Reindex();
            ModSettings.MarkDirty();
            return id;
        }

        public static bool Remove(string id)
        {
            var ids = IdList();
            if (!ids.Contains(id)) return false;
            ids.Remove(id);
            ListSetting().Set(string.Join(",", ids));

            var additional = ModSettings.Root.Get<CategorySetting>("speech")?.Get<CategorySetting>("additional");
            var sub = additional?.GetByKey(id);
            if (sub != null) additional.Remove(sub);

            ModSettings.Reindex();
            ModSettings.MarkDirty();
            return true;
        }

        // ---- building ----

        private static void BuildConfig(string id, string defaultName = "Configuration")
        {
            var cat = ModSettingsRegistry.EnsureCategory("speech.additional." + id, "Speech/Additional/Config");
            var name = cat.Get<StringSetting>("name");
            if (name == null)
            {
                name = new StringSetting("name", "Name", defaultName, "speech.config.name") { Hidden = true };
                cat.Add(name);
            }
            cat.LabelProvider = () => name.Get(); // the menu node reads the live name
            // The shared config schema (handler choice + each handler's params), inherit-aware: every
            // setting follows the default config (SpeechManager.Default) until the user overrides it.
            if (cat.Get<ChoiceSetting>("handler") == null && cat.Get<NullableChoiceSetting>("handler") == null)
                SpeechManager.BuildConfigSchema(cat, SpeechManager.Default?.Tree);
        }

        // ---- list helpers ----

        private static CategorySetting ConfigCat(string id)
            => ModSettings.Root.Get<CategorySetting>("speech")?.Get<CategorySetting>("additional")?.Get<CategorySetting>(id);

        private static StringSetting NameSetting(string id) => ConfigCat(id)?.Get<StringSetting>("name");

        private static StringSetting ListSetting()
            => ModSettings.Root.Get<CategorySetting>("speech")?.Get<CategorySetting>("additional")?.Get<StringSetting>("list");

        private static List<string> IdList()
        {
            var raw = ListSetting()?.Get() ?? "";
            return raw.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToList();
        }

        private static string NewId(List<string> ids)
        {
            int n = 1;
            while (ids.Contains("config_" + n) || ConfigCat("config_" + n) != null) n++;
            return "config_" + n;
        }
    }
}
