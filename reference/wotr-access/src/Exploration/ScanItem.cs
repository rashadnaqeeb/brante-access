using System.Collections.Generic;
using Kingmaker.EntitySystem.Entities; // UnitEntityData
using Owlcat.Runtime.Visual.RenderPipeline.RendererFeatures.FogOfWar; // LineOfSightGeometry
using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// One thing the scanner can list: a name, a world position, the categories it belongs to (many-to-
    /// many), and whether the player can currently perceive it. <see cref="Describe"/> composes the
    /// spoken line relative to a reference point. Entity-backed items derive from <see cref="ProxyEntity"/>;
    /// local-map points of interest are <see cref="ProxyMarker"/>.
    /// </summary>
    internal abstract class ScanItem
    {
        public abstract string Name { get; }
        public abstract Vector3 Position { get; }

        /// <summary>The <see cref="ScanTaxonomy"/> leaf node keys this thing belongs to (many-to-many — a
        /// lootable corpse is both "units.enemies" and "containers.corpse"). The scanner buckets it into
        /// each leaf's subcategory list and its parent category's "All" list. Distinct from
        /// <see cref="Primary"/> (the single state-aware node that SOUNDS).</summary>
        public abstract IEnumerable<string> Nodes { get; }

        /// <summary>Only listed when the player could actually know about it (fog/vision). Default: yes.</summary>
        public virtual bool IsVisible => true;

        /// <summary>Can the player see this RIGHT NOW — vs <see cref="IsVisible"/>, which for static
        /// things is reveal-LATCHED like the local map ("we know about it"). Generic fallback: a
        /// fog-texture sample at the position. Entity-backed items override with the game's
        /// per-entity fog state (XZ distance + line of sight, refreshed per frame). Used by the
        /// review cycles ("what's around me now"); the scanner stays area-wide knowledge.</summary>
        public virtual bool CurrentlySeen
        {
            get
            {
                try { return !Kingmaker.Controllers.FogOfWarController.IsInFogOfWar(Position); }
                catch { return true; } // areas without a fog system → no extra filter
            }
        }

        // Metres of slack on the fog line-of-sight ray, so a thing isn't blocked by its own wall geometry
        // (e.g. a door set into its frame). Tunable; shared by the sonar and the review cycles.
        internal const float LosFudge = 0.6f;

        /// <summary>
        /// Whether this item should register on the sonar / review cycles from the given cursor. It must be
        /// KNOWN (<see cref="IsVisible"/> — revealed objects persist, fogged units drop), and either
        /// currently in a party member's sight (<see cref="CurrentlySeen"/>) OR — for a remembered thing
        /// under fog — have a clear line of sight from the cursor. The fog case uses the game's own LoS
        /// geometry, so we don't surface something straight through a wall you'd have to walk around to reach.
        /// (Room exits bypass this entirely — that geometry is known and we keep it always-reachable.)
        /// </summary>
        // The game's fog-reveal radius (FogOfWarController.VisionRadius — private, campaign-
        // adjustable: 11.7m * multiplier + addition), read by reflection and cached (refreshed every
        // few seconds; per-frame reads must not box). Fallback = the game's base 11.7m.
        private static readonly System.Reflection.FieldInfo VisionRadiusField =
            HarmonyLib.AccessTools.Field(typeof(Kingmaker.Controllers.FogOfWarController), "VisionRadius");
        private static float _visionRadius = 11.7f;
        private static float _visionRadiusNext;

        private static float FogVisionRadius
        {
            get
            {
                if (Time.unscaledTime >= _visionRadiusNext)
                {
                    _visionRadiusNext = Time.unscaledTime + 5f;
                    try { _visionRadius = (float)VisionRadiusField.GetValue(null); } catch { }
                }
                return _visionRadius;
            }
        }

        public bool DetectableFrom(Vector3 cursor)
        {
            if (!IsVisible) return false;
            if (CurrentlySeen) return true;
            // The game's fog lifts only within a revealer's VISION RADIUS *and* clear LOS — obstacle
            // testing alone can never say no down a long unobstructed corridor (verified live: a
            // remembered door 33m up a straight hall reads obstacle-free). Mirror both halves: a
            // remembered (fogged) thing is offerable iff standing at the cursor would lift its fog.
            if (DistanceTo(cursor) > FogVisionRadius) return false;
            var los = LineOfSightGeometry.Instance;
            if (los == null) return true;
            // Call the LOS oracle EXACTLY the way every game caller does (UnitSightCache,
            // FogOfWarController, AI TargetInfo): the entity POSITION as the ray target and EyeShift
            // (~1m up) added to BOTH endpoints. The grid's only vertical rule is "a wall blocks iff its
            // top is at or above from.y", so foot-level rays and bounds-shrunk endpoints (our old call)
            // read "clear" across the Shield Maze's stacked sections where the game's own convention
            // reads blocked. We deliberately DON'T add stricter tests of our own (symmetric/navmesh):
            // never hide something the game itself would show.
            return !los.HasObstacle(cursor + LineOfSightGeometry.EyeShift,
                Position + LineOfSightGeometry.EyeShift, LosFudge);
        }

        /// <summary>The unit this item represents, for ability targeting (null = target its point instead).</summary>
        public virtual UnitEntityData TargetUnit => null;

        /// <summary>
        /// XZ radius of the thing's footprint, in world units (metres). Large creatures/objects span
        /// several tiles, so the tile view tests footprint-vs-tile overlap, not just the centre point.
        /// Default 0 = a point (markers). Units use their corpulence; map objects their collider bounds.
        /// </summary>
        public virtual float Footprint => 0f;

        /// <summary>The thing's spatial extent. Default: a circle of <see cref="Footprint"/> (a point when
        /// 0). Things with a non-circular shape override — e.g. an exit is the doorway's opening span — so
        /// distance/bearing report the nearest PART of the thing while the cursor still targets its centre.</summary>
        public virtual ScanBounds Bounds
            => Footprint > 0f ? ScanBounds.Circle(Position, Footprint) : ScanBounds.Point(Position);

        /// <summary>Distance from <paramref name="from"/> to the nearest part of the thing (not its centre).</summary>
        public float DistanceTo(Vector3 from) => Geo.Distance(from, Bounds.NearestPoint(from));

        /// <summary>The closest point of the thing to <paramref name="from"/> (XZ), NON-ALLOCATING — for the
        /// per-frame lenses (sonar, object cue, tile grid, cursor target), which run over every item each
        /// frame and so must not build a <see cref="Bounds"/> object. Default: a circle of
        /// <see cref="Footprint"/> about the centre (the centre when 0), the same geometry the spoken
        /// <see cref="Bounds"/> uses. Shaped things (area effects) override this with their real footprint.
        /// Inside the footprint → the reference point itself (distance 0), exactly like a circle.</summary>
        public virtual Vector3 NearestPoint(Vector3 from) => ScanBounds.NearestOnCircleXZ(Position, Footprint, from);

        /// <summary>Is <paramref name="point"/> inside this thing's footprint (XZ)? — "the cursor is on it",
        /// using the real shape (a wall's rectangle, not a circle).</summary>
        public bool Contains(Vector3 point)
        {
            var np = NearestPoint(point);
            float dx = np.x - point.x, dz = np.z - point.z;
            return dx * dx + dz * dz < 1e-4f;
        }

        /// <summary>
        /// The PRIMARY <see cref="ScanTaxonomy"/> node — the single, state-aware role this thing sounds as
        /// right now (a dead lootable enemy is primary containers.corpse; an exit door flips doors→exits
        /// when opened). Null = not part of the taxonomy at all (sound is then the user's per-node pick via
        /// <see cref="ScanSounds.Resolve"/>; membership for the scanner stays the full <see cref="Nodes"/> set).
        /// </summary>
        public virtual string Primary => null;

        /// <summary>True for a creature/unit (vs a map object or marker). Lets the sonar treat units like
        /// interactables for the object enter/exit cue while leaving plain scenery out.</summary>
        public virtual bool IsUnit => false;

        /// <summary>Normalized source-asset key (the map object's prefab/GameObject name) for looking up a
        /// mod-authored visual description, deduped across every instance of that asset. Null for units and
        /// anything without a stable source object — see <see cref="EnvDescriptions"/>.</summary>
        public virtual string AssetKey => null;

        /// <summary>The stable <see cref="ScanTaxonomy"/> node this thing ANNOUNCES as — the key for its
        /// per-entity-type announcement overrides (which inherit node → category → global). Distinct from
        /// the state-aware sound <see cref="Primary"/>: default is Primary, but units override to their
        /// FACTION so a lootable corpse still announces as its faction (unit parts), not as a container.
        /// Null ⇒ globals only.</summary>
        protected virtual string AnnounceNode => Primary;

        /// <summary>The identity/state announcement parts, in canonical order, WITHOUT the spatial part
        /// (added by <see cref="Describe"/>). Default: just the name (with the unnamed fallback). Concrete
        /// proxies add type / hp / condition / object-state via <see cref="NameAndType"/>.</summary>
        protected virtual IEnumerable<Announce.ScanAnnouncement> StateParts()
        {
            yield return new Announce.NamePart(string.IsNullOrEmpty(Name) ? Loc.T("scan.unnamed") : Name);
        }

        /// <summary>Yield Name (+ Type) honouring the rule: when there's a real name, Name carries it and
        /// Type is a separate part; when there's NO real name the type word becomes the name (so nothing
        /// goes nameless, and we never say the same word twice); with neither, the unnamed fallback.</summary>
        protected IEnumerable<Announce.ScanAnnouncement> NameAndType(string realName, string typeWord)
        {
            if (!string.IsNullOrEmpty(realName))
            {
                yield return new Announce.NamePart(realName);
                if (!string.IsNullOrEmpty(typeWord)) yield return new Announce.TypePart(typeWord);
            }
            else if (!string.IsNullOrEmpty(typeWord)) yield return new Announce.NamePart(typeWord);
            else yield return new Announce.NamePart(Loc.T("scan.unnamed"));
        }

        public string Describe(Vector3 reference)
        {
            var parts = new List<Announce.ScanAnnouncement>(StateParts());
            // Bearing/distance/height to the nearest PART of the thing; coordinates (debug) report the
            // centre (where the cursor would snap).
            parts.Add(new Announce.SpatialPart(reference, Bounds.NearestPoint(reference), Position));
            return Announce.ScanAnnounceComposer.Compose(AnnounceNode, parts);
        }

        /// <summary>The spoken line for the thing itself, no position — for at-cursor announcements
        /// (the cursor is on it, so distance/bearing would be noise).</summary>
        public string DescribeInPlace()
            => Announce.ScanAnnounceComposer.Compose(AnnounceNode, new List<Announce.ScanAnnouncement>(StateParts()));

        /// <summary>
        /// Interact with this item — mirroring the game's click (auto-path + act), driven through the
        /// game's own click handlers/commands (see the interaction-pipeline memory).
        /// Base: not interactable (e.g. a raw map marker).
        /// </summary>
        public virtual InteractOutcome Interact() => InteractOutcome.NotSupported;
    }

    /// <summary>What an <see cref="ScanItem.Interact"/> attempt did — drives the caller's announcement.</summary>
    internal enum InteractOutcome
    {
        /// <summary>Not interactable / nothing triggered — the caller announces "can't interact".</summary>
        NotSupported,
        /// <summary>An action was issued — the caller announces "interacting with …".</summary>
        Started,
        /// <summary>The item REFUSED and already spoke its own reason (e.g. "too far to attack this
        /// turn") — the caller stays silent so the refusal isn't clobbered.</summary>
        RefusedSpoken,
    }
}
