using System;
using System.Collections.Generic;
using WrathAccess.Settings;

namespace WrathAccess.Speech
{
    /// <summary>
    /// Speech handler routing through Prism (https://github.com/ethindp/prism) — a unified native
    /// abstraction over screen-reader and TTS backends (NVDA, JAWS, SAPI, OneCore, …). The primary
    /// handler. Param-driven: a config's "backend" choice is applied on change (rebinding only when it
    /// actually differs, since the feature query / rebind cost a native round-trip). Screen-reader
    /// passthrough, so it cannot render to PCM (no positional speech) — that's the SAPI handler's job.
    /// </summary>
    public class PrismHandler : ISpeechHandler
    {
        private const string AutoBackend = "auto";

        private IntPtr _ctx = IntPtr.Zero;
        private IntPtr _backend = IntPtr.Zero;
        private PrismNative.BackendFeatures _backendFeatures;
        private string _currentBackend = AutoBackend; // last backend applied from a config (apply-on-change)
        private static List<Choice> _backendChoices;   // enumerated once (the registry probe is expensive)

        public string Key => "prism";
        public string Label => "Prism";
        public string LocalizationKey => "speech.prism";

        public void BuildSettings(CategorySetting into)
        {
            into.Add(new ChoiceSetting("backend", "Backend", BackendChoices(), AutoBackend, "speech.prism.backend"));
        }

        // Enumerate prism's registry once and keep only backends whose engine is actually available on
        // this machine (SupportedAtRuntime filters out the obviously-irrelevant, e.g. JAWS with no JAWS).
        private static List<Choice> BackendChoices()
        {
            if (_backendChoices != null) return _backendChoices;
            var choices = new List<Choice> { new Choice(AutoBackend, "Auto (Best Available)", "speech.backend_auto") };
            try
            {
                var probeCtx = PrismNative.Init(IntPtr.Zero);
                if (probeCtx != IntPtr.Zero)
                {
                    try
                    {
                        var count = (int)PrismNative.RegistryCount(probeCtx).ToUInt64();
                        for (int i = 0; i < count; i++)
                        {
                            var id = PrismNative.RegistryIdAt(probeCtx, (UIntPtr)(uint)i);
                            var name = PrismNative.RegistryName(probeCtx, id);
                            if (string.IsNullOrEmpty(name)) continue;
                            var backend = PrismNative.RegistryCreate(probeCtx, id);
                            if (backend == IntPtr.Zero) continue;
                            try
                            {
                                var features = (PrismNative.BackendFeatures)PrismNative.BackendGetFeatures(backend);
                                if ((features & PrismNative.BackendFeatures.SupportedAtRuntime) != 0)
                                    choices.Add(new Choice(name, name)); // backend names are product names — not translated
                            }
                            finally { PrismNative.BackendFree(backend); }
                        }
                    }
                    finally { PrismNative.Shutdown(probeCtx); }
                }
            }
            catch (DllNotFoundException) { /* prism.dll missing — the bare Auto choice remains */ }
            catch (Exception ex) { Main.Log?.Warning("[speech] Prism backend enumeration failed: " + ex.Message); }
            _backendChoices = choices;
            return _backendChoices;
        }

        public bool Detect()
        {
            try
            {
                var ctx = PrismNative.Init(IntPtr.Zero);
                if (ctx == IntPtr.Zero) { Main.Log?.Log("[speech] Prism: prism_init returned null (dll loaded but init failed)."); return false; }
                try
                {
                    var backend = PrismNative.RegistryCreateBest(ctx);
                    if (backend == IntPtr.Zero) { Main.Log?.Log("[speech] Prism: no usable backend on this machine."); return false; }
                    PrismNative.BackendFree(backend);
                    return true;
                }
                finally { PrismNative.Shutdown(ctx); }
            }
            catch (DllNotFoundException)
            {
                Main.Log?.Log("[speech] Prism: prism.dll not found next to Wrath.exe (or a dependency is missing — e.g. the VC++ runtime).");
                return false;
            }
            catch (Exception ex)
            {
                Main.Log?.Log("[speech] PrismHandler.Detect failed: " + ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }

        public bool Load()
        {
            try
            {
                _ctx = PrismNative.Init(IntPtr.Zero);
                if (_ctx == IntPtr.Zero)
                {
                    Main.Log?.Error("[speech] PrismHandler: prism_init returned NULL.");
                    return false;
                }
                // Start on the best available backend; the config's chosen backend is applied on first speak.
                var backend = ResolveBackend(AutoBackend);
                if (backend == IntPtr.Zero)
                {
                    Main.Log?.Error("[speech] PrismHandler: no backend could be acquired.");
                    Unload();
                    return false;
                }
                SetActiveBackend(backend, AutoBackend);
                return true;
            }
            catch (Exception ex)
            {
                Main.Log?.Error("[speech] PrismHandler failed to load: " + ex);
                Unload();
                return false;
            }
        }

        public void Unload()
        {
            if (_backend != IntPtr.Zero)
            {
                try { PrismNative.BackendStop(_backend); } catch { }
                try { PrismNative.BackendFree(_backend); } catch { }
                _backend = IntPtr.Zero;
            }
            if (_ctx != IntPtr.Zero)
            {
                try { PrismNative.Shutdown(_ctx); } catch { }
                _ctx = IntPtr.Zero;
            }
            _backendFeatures = 0;
            _currentBackend = AutoBackend;
        }

        // Apply a config's backend choice, rebinding only when it differs from what's bound (rebinding is
        // a native teardown/acquire — never per utterance). CRITICAL: acquire the replacement BEFORE tearing
        // down the current backend, so a backend choice that can't be acquired never leaves us silent — we
        // keep whatever's already working. Never strand a blind user with no voice.
        private void ApplyConfig(CategorySetting config)
        {
            var pref = config?.Get<ChoiceSetting>("backend")?.Current?.Id ?? AutoBackend;
            if (pref == _currentBackend && _backend != IntPtr.Zero) return;

            var replacement = ResolveBackend(pref); // named if acquirable, else best; zero only if nothing works
            if (replacement == IntPtr.Zero)
            {
                Main.Log?.Error("[speech] PrismHandler: backend '" + pref + "' could not be acquired; keeping current backend.");
                _currentBackend = pref; // don't re-attempt the failed acquire on every utterance
                return;
            }
            if (_backend != IntPtr.Zero && _backend != replacement)
            {
                try { PrismNative.BackendStop(_backend); } catch { }
                PrismNative.BackendFree(_backend);
            }
            SetActiveBackend(replacement, pref);
        }

        public bool Speak(string text, bool interrupt, CategorySetting config)
        {
            ApplyConfig(config);
            if (_backend == IntPtr.Zero) return false;
            try
            {
                return PrismNative.BackendSpeak(_backend, text, interrupt) == PrismNative.PrismError.Ok;
            }
            catch (Exception ex)
            {
                Main.Log?.Error("[speech] PrismHandler.Speak failed: " + ex.Message);
                return false;
            }
        }

        public bool Output(string text, bool interrupt, CategorySetting config)
        {
            ApplyConfig(config);
            if (_backend == IntPtr.Zero) return false;
            try
            {
                // prism_backend_output drives both speech and braille when supported; otherwise fall
                // through to plain speak so we still produce audio.
                if ((_backendFeatures & PrismNative.BackendFeatures.SupportsOutput) != 0)
                {
                    var err = PrismNative.BackendOutput(_backend, text, interrupt);
                    if (err == PrismNative.PrismError.Ok) return true;
                    if (err != PrismNative.PrismError.NotImplemented)
                        Main.Log?.Log("[speech] PrismHandler.Output -> " + err + ", falling back to Speak.");
                }
                return PrismNative.BackendSpeak(_backend, text, interrupt) == PrismNative.PrismError.Ok;
            }
            catch (Exception ex)
            {
                Main.Log?.Error("[speech] PrismHandler.Output failed: " + ex.Message);
                return false;
            }
        }

        public bool Silence()
        {
            if (_backend == IntPtr.Zero) return false;
            try
            {
                return PrismNative.BackendStop(_backend) == PrismNative.PrismError.Ok;
            }
            catch (Exception ex)
            {
                Main.Log?.Error("[speech] PrismHandler.Silence failed: " + ex.Message);
                return false;
            }
        }

        // Prism's API has a SupportsSpeakToMemory feature flag, so a render path is possible later;
        // the binding isn't ported yet.
        public bool SupportsAudioRender => false;
        public SpeechAudio RenderToAudio(string text, CategorySetting config) => null;

        /// <summary>Build a ready-to-use backend for the preference — the named backend if it can be acquired,
        /// otherwise the best available (auto). Returns zero ONLY when nothing at all can be acquired. Does not
        /// touch the active <see cref="_backend"/>, so callers can acquire-then-swap safely.</summary>
        private IntPtr ResolveBackend(string preferred)
        {
            if (_ctx == IntPtr.Zero) return IntPtr.Zero;
            preferred = preferred ?? AutoBackend;

            IntPtr backend = IntPtr.Zero;
            if (preferred != AutoBackend)
            {
                backend = AcquireNamed(preferred);
                if (backend == IntPtr.Zero)
                    Main.Log?.Log("[speech] Prism backend '" + preferred + "' unavailable; falling back to auto (best available).");
            }
            if (backend == IntPtr.Zero) backend = PrismNative.RegistryCreateBest(_ctx); // first working
            return backend;
        }

        // Create + initialize the registry backend whose name matches. Zero on ANY failure (not in registry,
        // create returned null, or init failed) — the caller then falls back to the best available.
        private IntPtr AcquireNamed(string preferred)
        {
            var count = (int)PrismNative.RegistryCount(_ctx).ToUInt64();
            ulong id = 0;
            for (int i = 0; i < count; i++)
            {
                var candidate = PrismNative.RegistryIdAt(_ctx, (UIntPtr)(uint)i);
                if (PrismNative.RegistryName(_ctx, candidate) == preferred) { id = candidate; break; }
            }
            if (id == 0) { Main.Log?.Log("[speech] Prism backend '" + preferred + "' not in registry."); return IntPtr.Zero; }

            var backend = PrismNative.RegistryCreate(_ctx, id);
            if (backend == IntPtr.Zero) { Main.Log?.Log("[speech] Prism backend '" + preferred + "' create returned null."); return IntPtr.Zero; }

            var initErr = PrismNative.BackendInitialize(backend);
            if (initErr != PrismNative.PrismError.Ok && initErr != PrismNative.PrismError.AlreadyInitialized)
            {
                Main.Log?.Log("[speech] Prism backend '" + preferred + "' init failed (" + initErr + ").");
                PrismNative.BackendFree(backend);
                return IntPtr.Zero;
            }
            return backend;
        }

        // Adopt a freshly-acquired backend as the active one and cache its features (the query does real work
        // per call on some backends, and features don't change after init).
        private void SetActiveBackend(IntPtr backend, string requested)
        {
            _backend = backend;
            _currentBackend = requested;
            _backendFeatures = (PrismNative.BackendFeatures)PrismNative.BackendGetFeatures(_backend);
            Main.Log?.Log("[speech] PrismHandler backend acquired: " + (PrismNative.BackendName(_backend) ?? "<unknown>")
                + " (requested=" + requested + ", features=0x" + ((ulong)_backendFeatures).ToString("X") + ")");
        }
    }
}
