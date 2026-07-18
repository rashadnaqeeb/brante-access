using System;
using UnityEngine;

namespace BranteAccess.Host
{
    /// <summary>
    /// The permanent per-frame pump. Drives the live module's Tick on the Unity main thread and
    /// (Debug) the dev server's main-thread queue plus the F6 reload key. A Tick exception is
    /// logged with a per-message throttle - every frame would flood the log, silence would hide
    /// the failure from a player who cannot see the screen.
    /// </summary>
    internal sealed class HostPump : MonoBehaviour
    {
        private string _lastError;
        private float _lastErrorTime;
        private int _suppressed;

        private void Update()
        {
            var plugin = Plugin.Instance;
            if (plugin == null) return;

#if DEBUG
            plugin.DevTick();
            if (Input.GetKeyDown(KeyCode.F6)) plugin.ReloadModule();
#endif

            if (!plugin.Enabled) return;
            var module = plugin.Loader.Module;
            if (module == null) return;
            try
            {
                module.Tick();
                if (_suppressed > 0 && Time.unscaledTime - _lastErrorTime > 5f)
                {
                    HostLog.Error("Module.Tick: previous error repeated " + _suppressed + " more times: " + _lastError);
                    _suppressed = 0;
                    _lastError = null;
                }
            }
            catch (Exception ex)
            {
                var msg = ex.GetType().Name + ": " + ex.Message;
                if (msg == _lastError && Time.unscaledTime - _lastErrorTime < 5f)
                {
                    _suppressed++;
                }
                else
                {
                    if (_suppressed > 0)
                        HostLog.Error("Module.Tick: previous error repeated " + _suppressed + " more times.");
                    HostLog.Error("Module.Tick threw: " + ex);
                    _lastError = msg;
                    _lastErrorTime = Time.unscaledTime;
                    _suppressed = 0;
                }
            }
        }
    }
}
