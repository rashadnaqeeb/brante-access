using Kingmaker.EntitySystem; // EntityDataBase
using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// A <see cref="ScanItem"/> backed by a live game entity. Reads its position live and filters to
    /// what the player can actually perceive (so we don't leak fogged/hidden things — see the
    /// surface-only-visible memory). Default is current visibility, which matches the local map for the
    /// dynamic case (units); <see cref="ProxyMapObject"/> overrides it to the map's reveal-based rule so a
    /// discovered static object keeps showing through fog. Concrete kinds: <see cref="ProxyUnit"/>,
    /// <see cref="ProxyMapObject"/>.
    /// </summary>
    internal abstract class ProxyEntity : ScanItem
    {
        protected readonly EntityDataBase Entity;

        protected ProxyEntity(EntityDataBase entity) { Entity = entity; }

        public override Vector3 Position => Geo.Live(Entity); // live view transform, not the lagging data position

        public override bool IsVisible => Entity.IsInGame && Entity.IsVisibleForPlayer;

        // The game's own per-entity fog state (XZ-distance + line-of-sight from party revealers,
        // refreshed per frame by FogOfWarController) — truer than a fog-texture sample: a wall keeps
        // a nearby chest out of the review cycles. No camera-angle term for anything that matters
        // (only ambient "extra" units freeze their state while off-camera, and they aren't rendered
        // for sighted players then either).
        public override bool CurrentlySeen => !Entity.IsInFogOfWar;
    }
}
