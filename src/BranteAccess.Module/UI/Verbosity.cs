using System;
using System.IO;

namespace BranteAccess.Module.UI
{
    /// <summary>
    /// Navigation verbosity. Verbose (the default) speaks every announcement part; concise drops
    /// the control-type role word and the "n of m" position from navigator readouts - the same
    /// part set wotr-access's Concise preset turns off, minus tooltips (Space/F1 readouts are
    /// explicit requests here, not parts). Toggled by Ctrl+V. Persisted to prefs.txt in the
    /// plugin folder: module statics reset every hot reload, so an unpersisted toggle would
    /// silently revert mid-session.
    /// </summary>
    internal static class Verbosity
    {
        public static bool Verbose { get; private set; } = true;

        private static string PrefsPath => Path.Combine(Mod.Host.ModDir, "prefs.txt");

        /// <summary>Read the saved choice (same flat key = value format as the lang files).</summary>
        public static void Load()
        {
            try
            {
                if (!File.Exists(PrefsPath)) return;
                foreach (var rawLine in File.ReadAllLines(PrefsPath))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    if (line.Substring(0, eq).Trim() == "verbose")
                        Verbose = line.Substring(eq + 1).Trim() != "0";
                }
            }
            catch (Exception e) { Mod.Warn("[verbosity] failed to read prefs: " + e.Message); }
        }

        /// <summary>Flip and save; returns the new state. A failed save still flips - the session
        /// keeps working, only persistence is lost, and the warning says so.</summary>
        public static bool Toggle()
        {
            Verbose = !Verbose;
            try
            {
                File.WriteAllText(PrefsPath,
                    "# Brante Access preferences\nverbose = " + (Verbose ? "1" : "0") + "\n");
            }
            catch (Exception e) { Mod.Warn("[verbosity] failed to save prefs: " + e.Message); }
            return Verbose;
        }
    }
}
