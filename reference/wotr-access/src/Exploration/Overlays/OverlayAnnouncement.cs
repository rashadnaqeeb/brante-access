namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// One spoken fragment a <see cref="OverlaySystem"/> contributes about the cursor's surroundings,
    /// tagged with the <see cref="AnnouncementContext"/> it describes (tile vs point). The overlay's
    /// announce pipeline keeps the fragments whose context matches what's being looked at and composes them
    /// into one utterance. (Audio — sonar, wall tones, fog/object cues — is system <c>Tick</c> work, not
    /// an announcement.) Mirrors the typed-announcement pattern from the UI layer.
    /// </summary>
    internal sealed class OverlayAnnouncement
    {
        public readonly AnnouncementContext Context;
        public readonly Message Text;

        public OverlayAnnouncement(AnnouncementContext context, Message text)
        {
            Context = context;
            Text = text;
        }
    }
}
