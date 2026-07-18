#if DEBUG
using BepInEx.Logging;

namespace BranteAccess.Dev
{
    /// <summary>
    /// Mirrors every BepInEx log event into the dev server's /log ring buffer, in the disk log's
    /// "[Level:Source] text" shape, so a driver reads the log in-band with cursors and long-polling
    /// instead of grepping LogOutput.log. Ported from Non-Visual Calculus.
    /// </summary>
    internal sealed class DevLogListener : ILogListener
    {
        private readonly LineLog _lines;

        public DevLogListener(LineLog lines) => _lines = lines;

        public void LogEvent(object sender, LogEventArgs eventArgs)
            => _lines.Add("[" + eventArgs.Level + ":" + eventArgs.Source.SourceName + "] " + eventArgs.Data);

        public void Dispose() { }
    }
}
#endif
