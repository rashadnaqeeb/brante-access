using System.IO;
using System.Reflection;
using WrathAccess.Settings;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>Shared audio helpers for the sound systems: the bundled-audio path and the global master
    /// volume (the settings-wide Audio tab), which every system's volume is scaled by.</summary>
    internal static class OverlayAudio
    {
        // Assets live at the MOD ROOT (a sibling of Assemblies/ — anything under Assemblies/ would be
        // Assembly.LoadFrom'd by the game's mod loader). Falls back to the DLL's folder for a loose copy.
        public static string Dir =>
            Path.Combine(Main.ModDir ?? Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "assets", "audio");

        /// <summary>Global master volume as a 0..1 fraction (default full).</summary>
        public static float Master =>
            (ModSettings.GetSetting<IntSetting>("audio.master_volume")?.Get() ?? 100) / 100f;
    }
}
