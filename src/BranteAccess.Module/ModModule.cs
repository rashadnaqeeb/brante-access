using BranteAccess.Core.Modularity;

namespace BranteAccess.Module
{
    /// <summary>
    /// The reloadable module's entry point - the host finds this by scanning for IModModule.
    /// Feature code (screens, navigator, input, announcements) grows from here and hot-reloads
    /// without a game restart. IDevDriver strings are dev tooling, exempt from the
    /// no-inline-strings rule.
    /// </summary>
    public sealed class ModModule : IModModule, IDevDriver
    {
        private IModHost _host;

        public void Load(IModHost host)
        {
            _host = host;
            // Generation is not logged here: the loader bumps it only after Load succeeds (so a
            // failed reload leaves the old module live), and it logs the real number itself.
            _host.LogInfo("module code up, waiting for first tick");
        }

        public void Tick()
        {
        }

        public void Dispose()
        {
            _host.LogInfo("module disposed (generation " + _host.ModuleGeneration + ")");
        }

        // IDevDriver - the navigator does not exist yet; every probe says so rather than pretending.

        public string DispatchUi(string action) => null;

        public string DescribeNav() => "(navigator not built yet; hot reload proven)\n";

        public string TypeText(string text) => null;
    }
}
