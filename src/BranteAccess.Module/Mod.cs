using BranteAccess.Core.Modularity;

namespace BranteAccess.Module
{
    /// <summary>
    /// The module's host handle - set once in ModModule.Load, fresh per reload generation.
    /// Statics are safe here: each generation is a distinct assembly with its own statics, and
    /// only the live generation is ticked.
    /// </summary>
    internal static class Mod
    {
        public static IModHost Host;

        public static ISpeech Speech => Host.Speech;

        public static void Log(string message) => Host.LogInfo(message);
        public static void Warn(string message) => Host.LogWarning(message);
        public static void Error(string message) => Host.LogError(message);
    }
}
