using Kingmaker.EntitySystem.Entities; // UnitEntityData
using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// One kind of accessible "aim, then commit" action — a position/unit-targeted ability, the rest camp,
    /// and (in future) anything else that drops the player into one of the game's pointer modes. The
    /// <see cref="Targeting"/> coordinator owns the shared input plumbing: while ANY registered mode is
    /// <see cref="Active"/>, the act-on-target keys (Enter at the cursor, I on the scanner item,
    /// Backspace/Escape to cancel) route to it.
    ///
    /// A mode reports <see cref="Active"/> from LIVE game state (the pointer mode it armed), so it can never
    /// go stale, and it owns what committing/cancelling actually do plus its own spoken feedback. Adding a new
    /// targeting kind is: implement this interface and register an instance in <see cref="Targeting"/>.
    /// </summary>
    internal interface ITargetingMode
    {
        /// <summary>True while this mode is armed and waiting for a target — read from live game state.</summary>
        bool Active { get; }

        /// <summary>Commit at a chosen target: the unit under the cursor / scanner item (may be null) and the
        /// world point. The mode decides how to use them — a spell casts on the unit when present else the
        /// point; the rest camp ignores the unit and places at the point.</summary>
        void CommitAt(UnitEntityData unit, Vector3 point);

        /// <summary>Abandon aiming (drop the pointer mode).</summary>
        void Cancel();

        /// <summary>Per-frame upkeep tied to this mode (e.g. the rest camp's deferred party interaction once
        /// the marker is placed). Most modes need nothing here.</summary>
        void Tick();
    }
}
