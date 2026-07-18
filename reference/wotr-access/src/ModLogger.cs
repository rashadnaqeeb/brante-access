namespace WrathAccess
{
    /// <summary>
    /// The mod's logger (replaces UMM's ModEntry.ModLogger after the move to the game's native mod
    /// system). Writes through Unity's Debug log with a [WrathAccess] prefix, which lands in Player.log —
    /// the file we already read to diagnose ([[wotr-read-player-log]]). Same Log/Warning/Error surface as
    /// the UMM logger, so call sites (Main.Log?.Log(...)) are unchanged.
    /// </summary>
    public sealed class ModLogger
    {
        public void Log(string message) => UnityEngine.Debug.Log("[WrathAccess] " + message);
        public void Warning(string message) => UnityEngine.Debug.LogWarning("[WrathAccess] " + message);
        public void Error(string message) => UnityEngine.Debug.LogError("[WrathAccess] " + message);
    }
}
