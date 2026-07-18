using BranteAccess.Core.Modularity;

namespace BranteAccess.Host
{
    /// <summary>The host services handed to the reloadable module (see Core.IModHost).</summary>
    internal sealed class ModHost : IModHost
    {
        private readonly Plugin _plugin;

        public ModHost(Plugin plugin, string modDir, string modVersion)
        {
            _plugin = plugin;
            ModDir = modDir;
            ModVersion = modVersion;
        }

        public void LogInfo(string message) => HostLog.Info(message);
        public void LogWarning(string message) => HostLog.Warning(message);
        public void LogError(string message) => HostLog.Error(message);

        public string ModVersion { get; }
        public ISpeech Speech => _plugin.Speech;
        public string ModDir { get; }
        public bool Enabled => _plugin.Enabled;
        public int ModuleGeneration => _plugin.Loader.Generation;
    }
}
