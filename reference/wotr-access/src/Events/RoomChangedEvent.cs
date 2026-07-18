using WrathAccess.Exploration; // RoomMap

namespace WrathAccess.Events
{
    /// <summary>The review/movement cursor (or the player) entered a different room — raised by
    /// <see cref="RoomMap"/>'s dwell-gated room tracker. Sourceless. Reads the room's description
    /// (number + class), the same text the tracker spoke directly before.</summary>
    [EventSettings("Room changed", "exploration")]
    internal sealed class RoomChangedEvent : ModEvent
    {
        private readonly RoomMap.Room _room;
        public RoomChangedEvent(RoomMap.Room room) { _room = room; }
        public override Message GetMessage()
            => _room != null ? Message.Raw(RoomMap.Describe(_room)) : null;
    }
}
