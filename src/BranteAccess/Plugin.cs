using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BranteAccess.Host;
using BranteAccess.Speech;
using UnityEngine;

namespace BranteAccess
{
    /// <summary>
    /// The permanent BepInEx entry point. Owns what can never reload: the speech pipeline (native
    /// handles), the module loader, the per-frame pump, and (Debug) the dev server. All feature
    /// code lives in BranteAccess.Module and hot-reloads through <see cref="ReloadModule"/>.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.rashadnaqeeb.branteaccess";
        public const string Name = "Brante Access";
        public const string Version = "0.1.0";

        // Public (not internal) so /eval-compiled code, which lives in its own assembly, can reach
        // the speech pipeline: `Plugin.Instance.Speech.Speak("...")` is the standard smoke test.
        public static Plugin Instance { get; private set; }

        public SpeechPipeline Speech { get; private set; }
        internal ModuleLoader Loader { get; private set; }

        private ConfigEntry<bool> _enabled;

        public bool Enabled => _enabled.Value;

        private void Awake()
        {
            Instance = this;
            HostLog.Source = Logger;
            _enabled = Config.Bind("General", "Enabled", true,
                "Master enable. Off = the mod loads but does nothing (no input, no speech).");

            Speech = new SpeechPipeline(Config);

            var modDir = Path.GetDirectoryName(Info.Location);
            var host = new ModHost(this, modDir, Version);
            Loader = new ModuleLoader(Path.Combine(modDir, "module", "BranteAccess.Module.dll"), host);

            var go = new GameObject("BranteAccess.Host");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            go.AddComponent<HostPump>();

            bool loaded = Loader.Load();
            HostLog.Info(Name + " " + Version + " host up; module " + (loaded ? "generation " + Loader.Generation : "FAILED TO LOAD"));

#if DEBUG
            StartDevServer(modDir);
#endif
        }

        private void OnDestroy()
        {
#if DEBUG
            StopDevServer();
#endif
            Loader?.Unload();
            Speech?.Shutdown();
        }

        /// <summary>Rebuild the feature module from its freshly built dll, no restart. Wired to
        /// F6 and POST /reload (Debug).</summary>
        internal void ReloadModule()
        {
            bool ok = Loader.Reload();
            // Debug-only status line, exempt from the no-inline-strings rule (dev tooling).
            Speech.Output(ok ? "module reloaded, generation " + Loader.Generation : "module reload failed, see log", interrupt: true);
        }

#if DEBUG
        private Dev.DevServer _devServer;

        private void StartDevServer(string modDir)
        {
            if (System.Environment.GetEnvironmentVariable("BRANTE_NO_DEV") == "1")
            {
                HostLog.Info("[dev] BRANTE_NO_DEV=1: dev server disabled.");
                return;
            }
            _devServer = new Dev.DevServer(this, modDir);
            _devServer.Start();
        }

        private void StopDevServer()
        {
            _devServer?.Stop();
            _devServer = null;
        }

        internal void DevTick() => _devServer?.MainThreadTick();
#endif
    }
}
