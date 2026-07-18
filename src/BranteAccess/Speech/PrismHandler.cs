using System;

namespace BranteAccess.Speech
{
    /// <summary>
    /// Primary speech handler, routed through Prism - screen-reader passthrough (NVDA, JAWS) with
    /// SAPI/OneCore fallback inside Prism itself. Runs on the best available backend; ported from
    /// wotr-access with the settings-tree backend picker dropped (auto only - reinstate if a user
    /// need appears).
    /// </summary>
    internal sealed class PrismHandler : ISpeechHandler
    {
        private IntPtr _ctx = IntPtr.Zero;
        private IntPtr _backend = IntPtr.Zero;
        private PrismNative.BackendFeatures _features;

        public string Key => "prism";

        public bool Detect()
        {
            try
            {
                var ctx = PrismNative.Init(IntPtr.Zero);
                if (ctx == IntPtr.Zero) { HostLog.Info("[speech] Prism: prism_init returned null (dll loaded but init failed)."); return false; }
                try
                {
                    var backend = PrismNative.RegistryCreateBest(ctx);
                    if (backend == IntPtr.Zero) { HostLog.Info("[speech] Prism: no usable backend on this machine."); return false; }
                    PrismNative.BackendFree(backend);
                    return true;
                }
                finally { PrismNative.Shutdown(ctx); }
            }
            catch (DllNotFoundException)
            {
                HostLog.Info("[speech] Prism: prism.dll not found next to the game exe (or a dependency like the VC++ runtime is missing).");
                return false;
            }
            catch (Exception ex)
            {
                HostLog.Info("[speech] PrismHandler.Detect failed: " + ex.GetType().Name + " " + ex.Message);
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
                    HostLog.Error("[speech] PrismHandler: prism_init returned NULL.");
                    return false;
                }
                _backend = PrismNative.RegistryCreateBest(_ctx);
                if (_backend == IntPtr.Zero)
                {
                    HostLog.Error("[speech] PrismHandler: no backend could be acquired.");
                    Unload();
                    return false;
                }
                _features = (PrismNative.BackendFeatures)PrismNative.BackendGetFeatures(_backend);
                HostLog.Info("[speech] PrismHandler backend acquired: " + (PrismNative.BackendName(_backend) ?? "<unknown>")
                    + " (features=0x" + ((ulong)_features).ToString("X") + ")");
                return true;
            }
            catch (Exception ex)
            {
                HostLog.Error("[speech] PrismHandler failed to load: " + ex);
                Unload();
                return false;
            }
        }

        public void Unload()
        {
            if (_backend != IntPtr.Zero)
            {
                try { PrismNative.BackendStop(_backend); } catch (Exception ex) { HostLog.Warning("[speech] Prism stop on unload failed: " + ex.Message); }
                try { PrismNative.BackendFree(_backend); } catch (Exception ex) { HostLog.Warning("[speech] Prism backend free failed: " + ex.Message); }
                _backend = IntPtr.Zero;
            }
            if (_ctx != IntPtr.Zero)
            {
                try { PrismNative.Shutdown(_ctx); } catch (Exception ex) { HostLog.Warning("[speech] Prism shutdown failed: " + ex.Message); }
                _ctx = IntPtr.Zero;
            }
            _features = 0;
        }

        public bool Speak(string text, bool interrupt)
        {
            if (_backend == IntPtr.Zero) return false;
            try
            {
                return PrismNative.BackendSpeak(_backend, text, interrupt) == PrismNative.PrismError.Ok;
            }
            catch (Exception ex)
            {
                HostLog.Error("[speech] PrismHandler.Speak failed: " + ex.Message);
                return false;
            }
        }

        public bool Output(string text, bool interrupt)
        {
            if (_backend == IntPtr.Zero) return false;
            try
            {
                // prism_backend_output drives both speech and braille where supported; otherwise
                // fall through to plain speak so we still produce audio.
                if ((_features & PrismNative.BackendFeatures.SupportsOutput) != 0)
                {
                    var err = PrismNative.BackendOutput(_backend, text, interrupt);
                    if (err == PrismNative.PrismError.Ok) return true;
                    if (err != PrismNative.PrismError.NotImplemented)
                        HostLog.Info("[speech] PrismHandler.Output -> " + err + ", falling back to Speak.");
                }
                return PrismNative.BackendSpeak(_backend, text, interrupt) == PrismNative.PrismError.Ok;
            }
            catch (Exception ex)
            {
                HostLog.Error("[speech] PrismHandler.Output failed: " + ex.Message);
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
                HostLog.Error("[speech] PrismHandler.Silence failed: " + ex.Message);
                return false;
            }
        }
    }
}
