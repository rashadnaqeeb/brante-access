namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// How a <see cref="Cursor"/> moves in response to one input slot. Owned by the cursor (NOT a system),
    /// so several can drive the same cursor on different keys — e.g. continuous glide on the primary slot
    /// and tile-stepping on the secondary. A mode may read sibling <see cref="OverlaySystem"/>s through the
    /// overlay (e.g. tile-step reads cell size from <c>GridSystem</c>).
    ///
    /// All modes poll their slot's held arrows in <see cref="Tick"/> as one combined vector (see
    /// <see cref="CursorKeys"/>), so held diagonals work: <b>discrete</b> modes step on a typematic
    /// cadence and announce each landing (<see cref="AnnouncesOnMove"/> true); <b>continuous</b> modes
    /// glide per frame (false — feedback is audio, not per-frame speech).
    /// </summary>
    internal abstract class MovementMode
    {
        public abstract string Name { get; }
        public abstract MovementSlot Slot { get; }

        /// <summary>The announcement context this mode reads when it moves / is asked to describe.</summary>
        public abstract AnnouncementContext Context { get; }

        /// <summary>Whether a move triggers the overlay to speak the new position. Discrete steppers → true;
        /// continuous gliders → false (audio-driven).</summary>
        public virtual bool AnnouncesOnMove => true;

        public virtual void OnEnter(Overlay overlay) { }
        public virtual void OnExit(Overlay overlay) { }

        /// <summary>Continuous movement: glide per frame by polling this slot's held keys.</summary>
        public virtual void Tick(float dt, Overlay overlay) { }

        /// <summary>Reset the cursor to the player (modes with a grid snap to the player's cell).</summary>
        public virtual void Recenter(Overlay overlay) => overlay.Cursor.Position = Cursor.PlayerPosition;

        /// <summary>Follow a surface to the level below (-1) or above (+1). Modes without a level concept
        /// return <see cref="VerticalResult.Unsupported"/>.</summary>
        public virtual VerticalResult VerticalFollow(int dir, Overlay overlay) => VerticalResult.Unsupported;
    }
}
