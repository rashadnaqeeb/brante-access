using BepInEx.Logging;

namespace BranteAccess
{
    /// <summary>The host's log funnel. Everything host-side logs through here (never Unity
    /// Debug.Log directly); the module logs through IModHost. Set once in Plugin.Awake.</summary>
    internal static class HostLog
    {
        public static ManualLogSource Source;

        public static void Info(string message) => Source?.LogInfo(message);
        public static void Warning(string message) => Source?.LogWarning(message);
        public static void Error(string message) => Source?.LogError(message);
    }
}
