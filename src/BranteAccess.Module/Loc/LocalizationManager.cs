using System;
using System.Collections.Generic;
using System.IO;
using BranteAccess.Module.Speech;
using GameLoc = I2.Loc.LocalizationManager;

namespace BranteAccess.Module.Localization
{
    /// <summary>
    /// Loads the mod's own strings and resolves them for <see cref="Message"/> (sets
    /// <see cref="Message.LocalizationResolver"/>). One file per table under
    /// <c>lang/&lt;code&gt;/&lt;table&gt;.txt</c> - flat <c>key = value</c> lines ('#' comments,
    /// <c>\n</c> escapes; the game ships no JSON library, so no JSON). The language follows the
    /// game's live I2 locale code ("en"/"ru"/...); English is always loaded as the fallback, so a
    /// missing key reads in English, not as a raw key.
    ///
    /// The game locale is polled every frame (<see cref="Tick"/>) and reloaded on change - a
    /// mid-session language swap must apply immediately (hard-learned in the reference mods).
    /// Game CONTENT is localized by the game itself via I2 keys; this covers only the mod's own
    /// glue/structural text. The enGB-equivalent (lang/en) is the complete manifest.
    /// </summary>
    internal static class LocalizationManager
    {
        private const string Fallback = "en";

        // table -> (key -> template). _tables = current language; _fallbackTables = English.
        private static readonly Dictionary<string, Dictionary<string, string>> _tables =
            new Dictionary<string, Dictionary<string, string>>();
        private static readonly Dictionary<string, Dictionary<string, string>> _fallbackTables =
            new Dictionary<string, Dictionary<string, string>>();

        private static string _language = Fallback;

        public static string Language => _language;

        private static string Root => Path.Combine(Mod.Host.ModDir, "lang");

        public static void Initialize()
        {
            LoadLanguage(Fallback, _fallbackTables);
            _language = CurrentGameLanguage();
            if (_language != Fallback) LoadLanguage(_language, _tables);
            Message.LocalizationResolver = Get;
            Mod.Log("[loc] initialized, language " + _language);
        }

        /// <summary>Poll the game's live locale; reload on change. Cheap - a string compare.</summary>
        public static void Tick()
        {
            var lang = CurrentGameLanguage();
            if (lang == _language) return;
            _language = lang;
            _tables.Clear();
            if (lang != Fallback) LoadLanguage(lang, _tables);
            Mod.Log("[loc] language changed to " + lang);
        }

        /// <summary>Current language ?? English fallback. Null only if the key is missing from BOTH
        /// (a dev error - logged; Message then falls back to the raw key).</summary>
        public static string Get(string table, string key)
        {
            if (_language != Fallback
                && _tables.TryGetValue(table, out var t) && t.TryGetValue(key, out var v))
                return v;
            if (_fallbackTables.TryGetValue(table, out var ft) && ft.TryGetValue(key, out var fv))
                return fv;
            Mod.Warn("[loc] missing string: " + table + "." + key);
            return null;
        }

        /// <summary>Like <see cref="Get"/> but quiet and returns the supplied fallback - for callers
        /// that intentionally carry a default (e.g. binding labels).</summary>
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
            // I2 may not be initialized extremely early - fall back to English; the per-frame poll
            // corrects it once ready.
            try
            {
                var code = GameLoc.CurrentLanguageCode;
                return string.IsNullOrEmpty(code) ? Fallback : code;
            }
            catch (Exception e)
            {
                Mod.Warn("[loc] game locale unavailable, using " + Fallback + ": " + e.Message);
                return Fallback;
            }
        }

        private static void LoadLanguage(string lang, Dictionary<string, Dictionary<string, string>> target)
        {
            target.Clear();
            var dir = Path.Combine(Root, lang);
            if (!Directory.Exists(dir)) { Mod.Log("[loc] no locale dir: " + dir); return; }
            foreach (var file in Directory.GetFiles(dir, "*.txt"))
            {
                try
                {
                    target[Path.GetFileNameWithoutExtension(file)] = ParseFlatFile(file);
                }
                catch (Exception e) { Mod.Error("[loc] failed to load " + file + ": " + e.Message); }
            }
        }

        // key = value, one per line; '#' starts a comment line; '\n' in a value is a newline.
        private static Dictionary<string, string> ParseFlatFile(string file)
        {
            var entries = new Dictionary<string, string>();
            foreach (var rawLine in File.ReadAllLines(file))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    Mod.Warn("[loc] unparseable line in " + Path.GetFileName(file) + ": " + rawLine);
                    continue;
                }
                var key = line.Substring(0, eq).Trim();
                var value = line.Substring(eq + 1).Trim().Replace("\\n", "\n");
                entries[key] = value;
            }
            return entries;
        }
    }
}
