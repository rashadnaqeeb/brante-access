using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BranteAccess.Core.Modularity;

namespace BranteAccess.Host
{
    /// <summary>
    /// Loads, unloads, and reloads the feature module without restarting the game. The module dll
    /// is read into memory and loaded from its BYTES (never from its path) so the on-disk file
    /// stays unlocked and dotnet build can overwrite it while the game runs. Mono cannot unload
    /// assemblies, so each reload loads a fresh copy and old generations leak until process exit -
    /// fine for a dev loop. Ported from Non-Visual Calculus, AssemblyLoadContext replaced with
    /// plain Assembly.Load.
    /// </summary>
    internal sealed class ModuleLoader
    {
        private readonly string _modulePath;
        private readonly IModHost _host;

        public ModuleLoader(string modulePath, IModHost host)
        {
            _modulePath = modulePath;
            _host = host;
        }

        /// <summary>The live module instance, or null if no load has succeeded. Read fresh each frame.</summary>
        public IModModule Module { get; private set; }

        /// <summary>How many loads have succeeded this process (1 = the boot load). Lets a dev
        /// driver tell that a reload actually swapped.</summary>
        public int Generation { get; private set; }

        public string ModulePath => _modulePath;

        /// <summary>Load the module from its current bytes. Returns false (and logs) on any
        /// failure; a failed reload leaves the running module untouched.</summary>
        public bool Load()
        {
            try
            {
                if (!File.Exists(_modulePath))
                {
                    HostLog.Error("ModuleLoader: module dll not found at " + _modulePath);
                    return false;
                }

                byte[] bytes = File.ReadAllBytes(_modulePath);
                var pdbPath = Path.ChangeExtension(_modulePath, ".pdb");
                byte[] pdb = File.Exists(pdbPath) ? File.ReadAllBytes(pdbPath) : null;
                Assembly asm = pdb != null ? Assembly.Load(bytes, pdb) : Assembly.Load(bytes);

                Type type = asm.GetTypes().FirstOrDefault(
                    t => typeof(IModModule).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);
                if (type == null)
                {
                    HostLog.Error("ModuleLoader: no IModModule implementor in " + asm.GetName().Name);
                    return false;
                }

                var module = (IModModule)Activator.CreateInstance(type);
                try
                {
                    module.Load(_host);
                }
                catch
                {
                    // A Load that dies partway has already applied some of its patches and
                    // subscriptions; Dispose is the only owner that can take them back out.
                    // Without this, they leak for the rest of the process while the old module
                    // keeps running.
                    try { module.Dispose(); }
                    catch (Exception dex)
                    {
                        HostLog.Error("ModuleLoader: dispose after failed Load also failed: " + dex);
                    }
                    throw;
                }

                // Swap in only once the new module is fully live, so a failed reload (locked /
                // corrupt / half-written dll) leaves the running module untouched.
                DisposeCurrent();
                Module = module;
                Generation++;
                HostLog.Info("ModuleLoader: loaded " + type.FullName + " (generation " + Generation
                    + ", dll written " + File.GetLastWriteTime(_modulePath).ToString("HH:mm:ss") + ")");
                return true;
            }
            catch (Exception ex)
            {
                HostLog.Error("ModuleLoader.Load failed: " + ex);
                return false;
            }
        }

        public bool Reload() => Load();

        /// <summary>Tear down the current module (for shutdown).</summary>
        public void Unload() => DisposeCurrent();

        private void DisposeCurrent()
        {
            if (Module == null) return;
            try
            {
                Module.Dispose();
            }
            catch (Exception ex)
            {
                HostLog.Error("ModuleLoader: module Dispose threw: " + ex);
            }
            Module = null;
        }
    }
}
