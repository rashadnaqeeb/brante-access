using System;
using Kingmaker.EntitySystem.Entities; // UnitEntityData
using UnityEngine;
using WrathAccess.Exploration; // Geo

namespace WrathAccess.Events
{
    /// <summary>Which unit a "sourced" event concerns, relative to the player — each gets its own
    /// settings (enable + speech config) so e.g. enemy damage can read in a different voice from party
    /// damage. A sourceless event (room changed) is <see cref="None"/>.</summary>
    [Flags]
    internal enum EventSources
    {
        None = 0,
        Party = 1,
        Enemy = 2,
        Neutral = 4,
        All = Party | Enemy | Neutral,
    }

    /// <summary>Declares an event's settings: a display label, an optional group, and which source types
    /// it applies to (<see cref="EventSources.None"/> = sourceless). Discovered by <see cref="EventRegistry"/>.</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    internal sealed class EventSettingsAttribute : Attribute
    {
        public string Label { get; }
        public string Group { get; }
        public EventSources Sources { get; }
        public EventSettingsAttribute(string label, string group = null, EventSources sources = EventSources.None)
        { Label = label; Group = group; Sources = sources; }
    }

    /// <summary>
    /// One thing that happened (a game event we hook, or one we raise ourselves), queued through
    /// <see cref="EventDispatcher"/> and read out per its (per-source) settings. PROTOTYPE: each instance
    /// is dispatched individually — no consolidation of, say, a fireball's per-target damage yet.
    /// </summary>
    internal abstract class ModEvent
    {
        /// <summary>The unit this event concerns (source classification + world position); null = sourceless.</summary>
        public virtual UnitEntityData Unit => null;

        /// <summary>The spoken line, or null/empty to skip.</summary>
        public abstract Message GetMessage();

        /// <summary>World position, for (future) positional playback.</summary>
        public virtual Vector3 Position => Unit != null ? Geo.Live(Unit) : Vector3.zero;

        /// <summary>This event's source bucket (Party/Enemy/Neutral), or None for a sourceless event.</summary>
        public EventSources Source => Classify(Unit);

        /// <summary>Whether this event should be READ right now, given perceptibility — the dispatcher
        /// drops it when false. Default: sourceless events and PARTY units always read (you can see party
        /// portraits/conditions at any time); enemy/neutral units read only while you can actually
        /// perceive them (in game + visible to the player). Override for event-specific rules.</summary>
        public virtual bool Visible
        {
            get
            {
                var u = Unit;
                if (u == null || Source == EventSources.Party) return true;
                try { return u.IsInGame && u.IsVisibleForPlayer; }
                catch { return true; } // no fog/visibility system here → don't suppress
            }
        }

        public static EventSources Classify(UnitEntityData u)
            => u == null ? EventSources.None
             : u.IsPlayerFaction ? EventSources.Party
             : u.IsPlayersEnemy ? EventSources.Enemy
             : EventSources.Neutral;
    }
}
