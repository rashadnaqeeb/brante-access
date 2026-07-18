using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using GameLoc = Kingmaker.Localization.LocalizationManager;

namespace WrathAccess.Localization
{
    /// <summary>
    /// Loads the mod's own speech strings from JSON locale files and resolves them for <see cref="Message"/>
    /// (sets <see cref="Message.LocalizationResolver"/>). One file per table under
    /// <c>assets/locale/&lt;lang&gt;/&lt;table&gt;.json</c> — a flat <c>{ "KEY": "template with {vars}" }</c>
    /// (the SayTheSpire2 layout). The language follows the GAME's current locale (the <c>Locale</c> enum
    /// name, e.g. "enGB"/"deDE"); English (enGB) is always loaded as a fallback, so a missing/untranslated
    /// key reads in English, not as a raw key.
    ///
    /// The game's locale is a live getter off its language setting, so we <b>poll it every frame</b>
    /// (<see cref="Tick"/>) and reload when it changes — a mid-session language swap MUST be picked up
    /// (deferring this detection caused a hard-to-trace bug in SayTheSpire2). Game CONTENT (skill/feat/UI
    /// names) is already localized by the game via UIStrings/blueprints; this covers only the mod's own
    /// glue/role/structural text.
    /// </summary>
    public static class LocalizationManager
    {
        private const string Fallback = "enGB"; // the game's English locale code

        // table -> (key -> string). _tables = current language; _fallbackTables = English (always loaded).
        private static readonly Dictionary<string, Dictionary<string, string>> _tables =
            new Dictionary<string, Dictionary<string, string>>();
        private static readonly Dictionary<string, Dictionary<string, string>> _fallbackTables =
            new Dictionary<string, Dictionary<string, string>>();

        private static string _language = Fallback;

        public static string Language => _language;

        // Assets live at the MOD ROOT (a sibling of Assemblies/ — anything under Assemblies/ would be
        // Assembly.LoadFrom'd by the game's mod loader). Falls back to the DLL's folder for a loose copy.
        private static string Root =>
            Path.Combine(Main.ModDir ?? Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "assets", "locale");

        public static void Initialize()
        {
            LoadLanguage(Fallback, _fallbackTables); // always available as the fallback
            _language = CurrentGameLanguage();
            if (_language != Fallback) LoadLanguage(_language, _tables);
            Message.LocalizationResolver = Get; // wire Message → us
            Main.Log?.Log("[loc] initialized, language " + _language);
        }

        /// <summary>Poll the game's live locale; reload when it changes (a mid-session language swap must
        /// apply immediately). Cheap — a settings read + an enum compare. Ticked from Main.OnUpdate.</summary>
        public static void Tick()
        {
            var lang = CurrentGameLanguage();
            if (lang == _language) return;
            _language = lang;
            _tables.Clear();
            if (lang != Fallback) LoadLanguage(lang, _tables);
            Main.Log?.Log("[loc] language changed to " + lang);
        }

        /// <summary>Current language ?? English fallback. Null only if the key is missing from BOTH (a
        /// dev error — logged; <see cref="Message"/> then falls back to the raw key).</summary>
        public static string Get(string table, string key)
        {
            if (_language != Fallback
                && _tables.TryGetValue(table, out var t) && t.TryGetValue(key, out var v))
                return v;
            if (_fallbackTables.TryGetValue(table, out var ft) && ft.TryGetValue(key, out var fv))
                return fv;
            Main.Log?.Warning("[loc] missing string: " + table + "." + key);
            return null;
        }

        /// <summary>Like <see cref="Get"/> but quiet (no missing-string warning) and returns the supplied
        /// fallback instead of null — for callers that intentionally carry a default (e.g. setting labels).</summary>
        public static string GetOrDefault(string table, string key, string fallback)
        {
            if (_language != Fallback
                && _tables.TryGetValue(table, out var t) && t.TryGetValue(key, out var v))
                return v;
            if (_fallbackTables.TryGetValue(table, out var ft) && ft.TryGetValue(key, out var fv))
                return fv;
            return fallback;
        }

        private static string CurrentGameLanguage()
        {
            // The game's localization may not be ready at mod load — fall back to English; the per-frame
            // poll corrects it once the game initializes (and on any later language swap).
            try { return GameLoc.CurrentLocale.ToString(); }
            catch { return Fallback; }
        }

        private static void LoadLanguage(string lang, Dictionary<string, Dictionary<string, string>> target)
        {
            target.Clear();
            var dir = Path.Combine(Root, lang);
            if (!Directory.Exists(dir)) { Main.Log?.Log("[loc] no locale dir: " + dir); return; }
            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    var table = Path.GetFileNameWithoutExtension(file);
                    var entries = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(file));
                    if (entries != null) target[table] = entries;
                }
                catch (Exception e) { Main.Log?.Error("[loc] failed to load " + file + ": " + e.Message); }
            }
        }
    }
}
