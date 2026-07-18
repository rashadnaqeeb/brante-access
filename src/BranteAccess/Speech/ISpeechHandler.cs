namespace BranteAccess.Speech
{
    /// <summary>A speech backend. Load-on-demand: Detect probes cheaply, Load acquires the native
    /// resources, Unload releases them. Speak/Output return false on failure so the pipeline can
    /// log it (a silent speech failure is invisible to a blind player).</summary>
    internal interface ISpeechHandler
    {
        string Key { get; }

        /// <summary>Cheap availability probe (no persistent resources).</summary>
        bool Detect();

        bool Load();
        void Unload();

        /// <summary>Speech only.</summary>
        bool Speak(string text, bool interrupt);

        /// <summary>Speech plus braille where supported; falls back to plain speech.</summary>
        bool Output(string text, bool interrupt);

        bool Silence();
    }
}
