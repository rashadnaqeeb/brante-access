using System;
using Kingmaker;

namespace WrathAccess
{
    /// <summary>
    /// When active, holds the game's <c>KeyboardAccess.Disabled</c> guard so the
    /// game's own keyboard shortcuts are suppressed and our navigation owns the
    /// keyboard. <c>KeyboardAccess.Tick()</c> early-returns while Disabled is held,
    /// so this is the game's own, fully reversible "mute my shortcuts" lever.
    ///
    /// Note: this only mutes KeyboardAccess hotkeys — not Rewired movement/camera
    /// (those are inert in full-screen UI anyway; handled separately on the map).
    /// Our own keys are captured via InputManager's poll, independent of this guard.
    /// </summary>
    public static class FocusMode
    {
        private static IDisposable _guard;
        private static object _keyboard; // the KeyboardAccess instance the guard belongs to

        public static bool Active => _guard != null;

        public static void Toggle() => Set(!Active);

        public static void Set(bool on)
        {
            if (on == Active) return;
            if (on)
            {
                // Game/Keyboard may not exist extremely early; if so, this no-ops.
                var kb = Game.Instance?.Keyboard;
                _guard = kb?.Disabled.Scope();
                _keyboard = kb;
                if (_guard == null)
                    Main.Log?.Log("FocusMode: could not engage (game not ready).");
            }
            else
            {
                _guard?.Dispose();
                _guard = null;
                _keyboard = null;
            }
        }

        /// <summary>Per-frame: re-acquire the suppression scope when the game rebuilds its keyboard.
        /// Returning to the main menu / loading a save constructs a fresh KeyboardAccess, so a scope
        /// held on the old instance suppresses nothing — the game's own hotkeys come back alive (the
        /// game's Escape was toggling our freshly opened pause menu straight back shut).</summary>
        public static void Tick()
        {
            if (!Active) return;
            var kb = Game.Instance?.Keyboard;
            if (kb == null || ReferenceEquals(kb, _keyboard)) return;
            _guard.Dispose(); // release the old instance's counter (harmless if it's defunct)
            _guard = kb.Disabled.Scope();
            _keyboard = kb;
            Main.Log?.Log("FocusMode: re-engaged on a fresh KeyboardAccess (scene reload).");
        }
    }
}
