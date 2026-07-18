namespace BranteAccess.Core.Modularity
{
    /// <summary>
    /// The services the permanent host lends to a reloadable module. The host implements this; the
    /// module receives it in <see cref="IModModule.Load"/> and calls back through it. Loaded in the
    /// host's context so both sides agree on the interface's type identity across reloads.
    /// </summary>
    public interface IModHost
    {
        void LogInfo(string message);
        void LogWarning(string message);
        void LogError(string message);

        /// <summary>The running mod version, for the launch line.</summary>
        string ModVersion { get; }

        /// <summary>The single funnel for everything the mod says. Host-owned: the speech backends
        /// hold native handles (Prism context, SAPI COM), which must survive a module reload.</summary>
        ISpeech Speech { get; }

        /// <summary>The deployed plugin folder (lang files live under it).</summary>
        string ModDir { get; }

        /// <summary>Master enable flag from the host config. The pump already gates Tick on it;
        /// exposed so module code can gate event-driven work too.</summary>
        bool Enabled { get; }

        /// <summary>How many module loads have succeeded this process (1 = boot). Lets dev tooling
        /// verify a reload actually swapped generations.</summary>
        int ModuleGeneration { get; }
    }

    /// <summary>The speech surface the host owns. Rich-text stripping and the dev speech tap happen
    /// inside the host pipeline; callers pass raw text. Interrupt is opt-in - queued is the house
    /// default and the caller must justify interrupting.</summary>
    public interface ISpeech
    {
        /// <summary>Speech only.</summary>
        void Speak(string text, bool interrupt = false);

        /// <summary>Speech plus braille where the backend supports it. The default choice.</summary>
        void Output(string text, bool interrupt = false);

        void Silence();
    }
}
