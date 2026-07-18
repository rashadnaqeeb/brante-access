using System.Collections.Generic;
using WrathAccess.Input;   // InputManager.Held (mode hold keys)
using WrathAccess.Screens;
using WrathAccess.Settings; // ChoiceSetting (mode cycle)
using WrathAccess.UI; // NavDirection

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// Holds the registered overlays and which one is active, and forwards input to it. Each overlay is a
    /// composition of movement modes + systems (see the builders below). Cycling steps off → 0 → … → off,
    /// only while exploring (focus mode owns the keyboard AND the plain in-game context is on top), so the
    /// overlay keys never fight the game during normal play. Movement/announce verbs are gated on
    /// <see cref="Active"/>; the selected overlay still ticks when inactive so its audio can mute itself.
    /// </summary>
    internal static class OverlayManager
    {
        private static List<Overlay> _overlays = new List<Overlay>();
        private static int _active = -1; // -1 = overlays off

        /// <summary>Install the overlays built from settings (see <see cref="OverlaySettingsRegistry"/>).
        /// Selects the Default overlay (index 0, always first) so spatial audio is engaged by default — users
        /// shouldn't have to press Ctrl+O first; cycling can still reach "off". OnEnter is balanced by Cycle's
        /// OnExit (a no-op for the cursor modes), and is a light no-op at boot when no area is loaded; the
        /// overlay does nothing until <see cref="Active"/> (in exploration) anyway, so engaging it early is safe.</summary>
        public static void SetOverlays(List<Overlay> overlays)
        {
            _overlays = overlays ?? new List<Overlay>();
            _active = _overlays.Count > 0 ? 0 : -1; // 0 = the Default overlay; -1 only if there are none
        }

        // We're "exploring" (overlay live, audio + cursor active) only when focus mode owns the keyboard, the
        // in-game context OR the world map is on top, AND we actually have control — so a cutscene/scripted
        // event or a modal (the encounter/enter panel pops a different top screen) silences the overlay like
        // it does our keys. The world map shares the framework: its systems run under whichever overlay is
        // engaged, scoped to the world-map context (see Overlay's scope filter).
        private static bool InExploration
        {
            get
            {
                if (!FocusMode.Active || ScreenManager.Current == null)
                    return false;
                // Context-only, NOT control: the overlay is engaged whenever the in-area / world-map context
                // is on top with focus mode, so the sensing systems keep running through in-area scripted
                // sequences (e.g. the new game's opening, where players reported the overlay failing to
                // auto-enable). Dialogue, menus, rest and the pause screen each push their OWN top screen, so
                // the key check below already excludes them. Control is a SEPARATE, finer signal each
                // component consults: cursor movement is gated on it in Overlay.Tick (so it can't drift during
                // a cutscene), while the audio systems decide for themselves (today: they keep playing).
                var key = ScreenManager.Current.Key;
                return key == "ctx.ingame" || key == "ctx.globalmap";
            }
        }

        /// <summary>Which context the engaged overlay runs in right now — drives Overlay's per-system scope
        /// filter (in-area systems on "ctx.ingame", world-map systems on "ctx.globalmap").</summary>
        internal static OverlayScope CurrentScope =>
            ScreenManager.Current != null && ScreenManager.Current.Key == "ctx.globalmap"
                ? OverlayScope.WorldMap : OverlayScope.InArea;

        public static bool Active => InExploration && _active >= 0;

        private static Overlay Current => _active >= 0 ? _overlays[_active] : null;

        /// <summary>The overlay currently driving audio/cursor, or null when overlays are off — for
        /// callers (the scanner's review ping) that want the ACTIVE overlay's settings when there is
        /// one and a fallback of their own when not.</summary>
        internal static Overlay ActiveOverlay => Active ? Current : null;

        public static void Cycle()
        {
            if (!InExploration) return;
            Current?.OnExit();
            _active++;
            if (_active >= _overlays.Count) _active = -1;
            if (Current == null) { Tts.Speak(Loc.T("overlay.off"), interrupt: true); return; }
            Tts.Speak(Current.Name, interrupt: true);
            Current.OnEnter();
            Current.AnnounceCurrent();
        }

        // Ticks the selected overlay every frame (whether or not we're exploring); its systems/modes
        // self-gate on Active so they mute/idle when a menu's up or focus is off. First mirror the
        // hold-to-force-on keys (Shift+F1/F2) onto the engaged overlay's systems — InputManager.Held is
        // category-aware, so it's only true while exploring owns those keys, and false (released) otherwise.
        public static void Tick(float dt)
        {
            SetForceHeld("walltones", InputManager.Held("overlay.holdWalltones"));
            SetForceHeld("sonar", InputManager.Held("overlay.holdSonar"));
            Current?.Tick(dt);
        }

        // Hold-to-force-on (Shift+F1/F2): the system plays as if Continuous while its key is held,
        // whatever its saved mode. Set every frame on the engaged overlay's system.
        private static void SetForceHeld(string key, bool held)
        {
            var sys = Current?.GetSystem(key);
            if (sys != null) sys.ForceHeld = held;
        }

        /// <summary>Cycle a system's play mode on the engaged overlay (Ctrl+F1/F2): Off → When moving →
        /// Continuous → Off (through its supported subset), persisted, announcing the new mode.</summary>
        public static void CycleMode(string key)
        {
            if (!InExploration) return;
            var sys = Current?.GetSystem(key);
            var setting = sys?.OverlayCat?.Get<ChoiceSetting>("mode");
            if (sys == null || setting == null) return;

            var modes = sys.SupportedModes;
            var cur = OverlayModes.Parse(setting.Current?.Id);
            int i = 0;
            for (; i < modes.Count; i++) if (modes[i] == cur) break;
            setting.Set(OverlayModes.Id(modes[(i + 1) % modes.Count]));

            var sysName = WrathAccess.Localization.LocalizationManager.GetOrDefault("settings", "system." + key, sys.Name);
            Tts.Speak(Loc.T("overlay.mode_set", new { system = sysName, mode = setting.Current?.Label ?? "" }), interrupt: true);
        }

        public static void Recenter() { if (Active) Current.Recenter(); }
        public static void AnnounceCurrent() { if (Active) Current.AnnounceCurrent(); }
        public static void VerticalFollow(int dir) { if (Active) Current.VerticalFollow(dir); }
    }
}
