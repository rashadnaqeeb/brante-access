using System.Collections.Generic;
using WrathAccess.Settings;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>Which context(s) a system runs in: the in-area scene, the world (global) map, or both. The
    /// overlay only ticks/announces systems matching the live context (see <see cref="OverlayManager.CurrentScope"/>),
    /// so navmesh-bound in-area systems stay off the map while world-map systems stay off in areas; the Log
    /// is Both (it just reads the game's log feed).</summary>
    internal enum OverlayScope { InArea, WorldMap, Both }

    /// <summary>How a system decides to play: never (Off), only while the cursor is moving (WhenMoving),
    /// or always (Continuous) — the shared vocabulary for every system's "mode" dropdown + cycle order.</summary>
    internal enum OverlayMode { Off, WhenMoving, Continuous }

    /// <summary>Mode &lt;-&gt; persisted id (the ChoiceSetting value) + choice helpers.</summary>
    internal static class OverlayModes
    {
        public static readonly System.Collections.Generic.IReadOnlyList<OverlayMode> All =
            new[] { OverlayMode.Off, OverlayMode.WhenMoving, OverlayMode.Continuous };
        public static readonly System.Collections.Generic.IReadOnlyList<OverlayMode> OffContinuous =
            new[] { OverlayMode.Off, OverlayMode.Continuous };

        public static string Id(OverlayMode m) =>
            m == OverlayMode.Off ? "off" : m == OverlayMode.WhenMoving ? "when_moving" : "continuous";
        public static OverlayMode Parse(string id) =>
            id == "when_moving" ? OverlayMode.WhenMoving : id == "continuous" ? OverlayMode.Continuous : OverlayMode.Off;
        public static Choice Choice(OverlayMode m) => new Choice(Id(m), Id(m), "overlay.mode." + Id(m));
    }

    /// <summary>
    /// A pure provider attached to an <see cref="Overlay"/> — it queries the world relative to the cursor
    /// and exposes <see cref="OverlayAnnouncement"/>s and/or makes sound in <see cref="Tick"/>; it NEVER
    /// moves the cursor or owns movement keys (that's <see cref="MovementMode"/>), which is what lets a
    /// tiled-space system and a continuous-space system coexist on one overlay. Exactly one of each
    /// concrete type per overlay, so siblings can be looked up by type.
    ///
    /// Each system is data-driven: it declares its tunables in <see cref="RegisterSettings"/> and reads them
    /// LIVE through the bound category (via <see cref="Bool"/>/<see cref="Int"/>/<see cref="ChoiceId"/>), so
    /// editing a setting takes effect immediately. The universal <c>enabled</c> toggle is added by the
    /// registry; a disabled system self-gates (no announcements, no Tick work).
    /// </summary>
    internal abstract class OverlaySystem
    {
        public abstract string Name { get; }
        public abstract string Key { get; } // settings-path segment, e.g. "grid"

        /// <summary>Which context this system runs in (default: the in-area scene). The overlay skips systems
        /// whose scope doesn't match the live context, so an in-area system never ticks on the world map.</summary>
        public virtual OverlayScope Scope => OverlayScope.InArea;

        /// <summary>The overlay's own category for this system: holds `enabled` (+ the hidden
        /// `customized` flag, + the `custom` override subtree when materialized).</summary>
        public CategorySetting OverlayCat { get; private set; }

        /// <summary>The shared defaults for this system type (defaults.&lt;key&gt;).</summary>
        public CategorySetting DefaultsCat { get; private set; }

        public void Bind(CategorySetting overlayCat, CategorySetting defaultsCat)
        {
            OverlayCat = overlayCat;
            DefaultsCat = defaultsCat;
        }

        /// <summary>The resolution root for tunables — WHOLE-SUBTREE inheritance: an overlay that has
        /// customized this system reads ONLY its own `custom` copy; otherwise ONLY the shared defaults.
        /// Resolved live on every read, so Customize/Reset take effect immediately.</summary>
        protected CategorySetting Settings => OverlayCat?.Get<CategorySetting>("custom") ?? DefaultsCat;

        /// <summary>Add this system's tunables to its settings category (the <c>enabled</c> toggle and the
        /// audio <c>volume</c> are added for you — see the registry / <see cref="AudioSystem"/>).</summary>
        public virtual void RegisterSettings(CategorySetting cat) { }

        /// <summary>The play modes this system offers — its "mode" dropdown + cycle order. Default: all
        /// three; systems whose behaviour can't distinguish "when moving" (event/readout systems) override
        /// to a subset.</summary>
        public virtual System.Collections.Generic.IReadOnlyList<OverlayMode> SupportedModes => OverlayModes.All;

        /// <summary>The current per-overlay play mode (replaces the old `enabled` bool). Per-overlay always
        /// (composition is the overlay's identity — never inherited).</summary>
        public OverlayMode Mode
        {
            get
            {
                var c = OverlayCat?.Get<ChoiceSetting>("mode");
                return c?.Current != null ? OverlayModes.Parse(c.Current.Id) : OverlayMode.Off;
            }
        }

        /// <summary>Transient "force on while a hotkey is held" (Shift+F1/F2) — set by the input layer each
        /// frame, never persisted. Plays as if Continuous.</summary>
        public bool ForceHeld { get; set; }

        /// <summary>Active at all — for readout/announce systems with no movement-timed playback.</summary>
        public bool Enabled => Mode != OverlayMode.Off || ForceHeld;

        /// <summary>The audio play gate (replaces the old <see cref="Enabled"/> in the sound systems'
        /// Tick): Continuous always; WhenMoving only while the relevant cursor moved recently; Off never —
        /// a held hotkey forces it on regardless.</summary>
        public bool ShouldPlay(Overlay overlay)
        {
            if (ForceHeld) return true;
            switch (Mode)
            {
                case OverlayMode.Continuous: return true;
                case OverlayMode.WhenMoving: return MovingNow(overlay);
                default: return false;
            }
        }

        /// <summary>Whether this system's relevant cursor is moving (for WhenMoving). Default: the overlay's
        /// in-area cursor; world-map systems override to their own cursor.</summary>
        protected virtual bool MovingNow(Overlay overlay) => overlay != null && overlay.CursorMovingRecently;

        protected bool Bool(string key, bool fallback) => Settings?.Get<BoolSetting>(key)?.Get() ?? fallback;
        protected int Int(string key, int fallback) => Settings?.Get<IntSetting>(key)?.Get() ?? fallback;
        protected string ChoiceId(string key, string fallback) => Settings?.Get<ChoiceSetting>(key)?.Current?.Id ?? fallback;

        public virtual void OnEnter(Overlay overlay) { }
        public virtual void OnExit(Overlay overlay) { }

        /// <summary>Per-frame work while an overlay is selected; self-gates on
        /// <see cref="OverlayManager.Active"/> and <see cref="Enabled"/>.</summary>
        public virtual void Tick(float dt, Overlay overlay) { }

        /// <summary>The announcements this system contributes (each tagged with its
        /// <see cref="AnnouncementContext"/>); empty if disabled or none.</summary>
        public virtual IEnumerable<OverlayAnnouncement> Announce(OverlayContext ctx)
        {
            yield break;
        }
    }
}
