using System;
using System.Runtime.InteropServices;

namespace WrathAccess.Input
{
    /// <summary>
    /// Reads the OS keyboard typematic settings (initial delay + repeat rate) so our nav
    /// key-repeat feels like the rest of the user's system. On Windows this queries
    /// user32 SystemParametersInfo; on any platform/API where that's unavailable it falls
    /// back to sane defaults. Values are read once and cached (call Refresh to re-read).
    /// </summary>
    public static class OsKeyboard
    {
        public const float DefaultInitialDelay = 0.4f;   // seconds before repeat starts
        public const float DefaultRepeatInterval = 0.06f; // seconds between repeats

        private static bool _loaded;
        private static float _initialDelay = DefaultInitialDelay;
        private static float _repeatInterval = DefaultRepeatInterval;

        public static float InitialDelay { get { EnsureLoaded(); return _initialDelay; } }
        public static float RepeatInterval { get { EnsureLoaded(); return _repeatInterval; } }

        /// <summary>Force a re-read of the OS settings (e.g. if the user changed them mid-session).</summary>
        public static void Refresh() { _loaded = false; EnsureLoaded(); }

        private const uint SPI_GETKEYBOARDDELAY = 0x0016; // pvParam ← 0..3
        private const uint SPI_GETKEYBOARDSPEED = 0x000A; // pvParam ← 0..31

        [DllImport("user32.dll", SetLastError = false)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref int pvParam, uint fWinIni);

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                int delay = 0, speed = 0;
                if (SystemParametersInfo(SPI_GETKEYBOARDDELAY, 0, ref delay, 0) &&
                    SystemParametersInfo(SPI_GETKEYBOARDSPEED, 0, ref speed, 0))
                {
                    // Delay: 0..3 maps to ~250..1000 ms.
                    if (delay < 0) delay = 0; else if (delay > 3) delay = 3;
                    _initialDelay = (delay + 1) * 0.25f;

                    // Speed: 0..31 maps to ~2.5..30 repeats/sec; interval = 1 / rate.
                    if (speed < 0) speed = 0; else if (speed > 31) speed = 31;
                    float cps = 2.5f + (speed / 31f) * (30f - 2.5f);
                    _repeatInterval = 1f / cps;

                    Main.Log?.Log(string.Format(
                        "OS key repeat: delay={0:0.###}s, interval={1:0.###}s (raw delay={2}, speed={3})",
                        _initialDelay, _repeatInterval, delay, speed));
                    return;
                }
                Main.Log?.Log("OS key repeat query returned false; using defaults.");
            }
            catch (Exception e)
            {
                // Non-Windows, or user32 unavailable → keep defaults.
                Main.Log?.Log("OS key repeat unavailable, using defaults: " + e.Message);
            }
            _initialDelay = DefaultInitialDelay;
            _repeatInterval = DefaultRepeatInterval;
        }
    }
}
