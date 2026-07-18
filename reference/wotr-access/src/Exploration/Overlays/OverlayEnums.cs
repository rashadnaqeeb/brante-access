namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// The subject an announcement is about — its "type". A <see cref="Tile"/> readout describes the
    /// discrete cell the cursor is in (contents, walkable, bearing to the cell centre); a <see cref="Point"/>
    /// readout describes the exact point under the cursor. A system produces announcements in its own
    /// worldview, and the overlay surfaces the context matching how you're currently looking (the active
    /// movement mode's context). Extensible (region, nearest thing, …).
    /// </summary>
    internal enum AnnouncementContext
    {
        Point,
        Tile,
    }

    /// <summary>
    /// Which input a movement mode listens on, so several modes can drive one cursor at once (e.g. arrows
    /// glide while Shift+arrows step). Only Primary is wired today; Secondary is the planned second slot.
    /// </summary>
    internal enum MovementSlot
    {
        Primary,
        Secondary,
    }

    /// <summary>Outcome of a vertical (level) follow, so the overlay can announce appropriately.</summary>
    internal enum VerticalResult
    {
        Unsupported, // this movement mode has no level concept (e.g. a free glider) — stay silent
        NoSurface,   // there was no floor to follow in that direction
        Moved,       // followed to a surface — announce the new spot
    }
}
