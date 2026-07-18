using System;

namespace BranteAccess.Core.Modularity
{
    /// <summary>
    /// The contract every reloadable feature module implements. The host loads the module assembly
    /// from bytes (the on-disk dll is never locked, so dotnet build can overwrite it while the game
    /// runs), instantiates the one implementor, calls <see cref="Load"/> once, drives
    /// <see cref="Tick"/> from its permanent per-frame pump, and calls
    /// <see cref="IDisposable.Dispose"/> on the old instance after a successful reload. The module
    /// owns no native handles; its Harmony patches use a per-load unique id and unpatch in Dispose,
    /// so a reload tears down cleanly. Old generations leak until process exit (Mono cannot unload
    /// assemblies) - acceptable, dev-only.
    /// </summary>
    public interface IModModule : IDisposable
    {
        /// <summary>Capture the host services and wire up patches/subscriptions. Called once.</summary>
        void Load(IModHost host);

        /// <summary>Per-frame work, called by the host pump on the Unity main thread.</summary>
        void Tick();
    }
}
