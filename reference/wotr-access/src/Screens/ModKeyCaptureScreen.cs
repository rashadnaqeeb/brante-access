using System.Collections.Generic;
using UnityEngine;
using WrathAccess.Input;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Captures a key combo for one of OUR <see cref="InputAction"/>s (distinct from the game-binding
    /// <see cref="KeyBindCaptureScreen"/>). Pushed as a CHILD SCREEN of whatever opened it (the mod menu's
    /// binding row). It sets <see cref="Screen.CapturesRawInput"/> so the InputManager stands down (our nav
    /// won't eat the keypress), and reads the next non-modifier keydown directly in <see cref="OnUpdate"/>,
    /// capturing the current Ctrl/Shift/Alt state. Focus mode stays on, so the game's own keys remain muted;
    /// Escape cancels. Closing returns focus to the binding row.
    /// </summary>
    public sealed class ModKeyCaptureScreen : Screen
    {
        private readonly InputAction _action;
        private readonly bool _append;     // add to the existing bindings instead of replacing them
        private bool _armed;               // the key that opened the dialog has released; ready to capture
        private bool _awaitRelease;        // bound; staying up until the confirming key releases
        private KeyCode _releaseKey;

        private ModKeyCaptureScreen(InputAction action, bool append) { _action = action; _append = append; }

        /// <summary>Open the capture dialog as a child of the current screen.</summary>
        public static void Open(InputAction action, bool append = false)
        {
            if (action != null) ScreenManager.Current?.PushChild(new ModKeyCaptureScreen(action, append));
        }

        public override string Key => "overlay.modkeycapture";
        public override bool CapturesRawInput => true;
        public override bool IsActive() => false; // only ever a child screen

        public override void OnFocus()
            => Tts.Speak(Message.Localized("settings", "rebind.prompt",
                new { action = _action != null ? _action.DisplayLabel : "" }).Resolve());

        public override void OnUpdate()
        {
            var action = _action;
            if (action == null) return;

            // After a successful bind, keep the screen up (input still stood down) until the confirming key
            // is released — otherwise that same press propagates into the menu/new binding (a cascade).
            if (_awaitRelease)
            {
                if (!UnityEngine.Input.GetKey(_releaseKey)) Close();
                return;
            }

            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                Tts.Speak(Message.Localized("settings", "rebind.cancelled").Resolve());
                Close();
                return;
            }

            // Wait for the opening key (the Enter/activate press that got us here) to release before we
            // start reading — otherwise that very press is captured as the new binding.
            if (!_armed)
            {
                if (!UnityEngine.Input.anyKey) _armed = true;
                return;
            }

            if (!UnityEngine.Input.anyKeyDown) return;

            foreach (var key in CaptureKeys)
            {
                if (!UnityEngine.Input.GetKeyDown(key)) continue;
                bool ctrl = UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl);
                bool shift = UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
                bool alt = UnityEngine.Input.GetKey(KeyCode.LeftAlt) || UnityEngine.Input.GetKey(KeyCode.RightAlt);
                var binding = new KeyboardBinding(key, ctrl, shift, alt);

                // Reject a combo already bound to another action — otherwise one keypress fires two
                // actions. In append mode, also reject a combo this action already has.
                if (_append && HasBinding(action, binding))
                {
                    Tts.Speak(Message.Localized("settings", "rebind.conflict",
                        new { combo = binding.DisplayName, other = action.DisplayLabel }).Resolve(), interrupt: true);
                    return;
                }
                // A within-category conflict STEALS the combo (announced) - refusing would dead-end a
                // mid-reshuffle user. Across categories the same combo is legal by design: stack-order
                // shadowing decides which one a press means (see InputCategory).
                var conflict = FindConflict(binding, action);
                if (conflict != null)
                {
                    RemoveEqualBinding(conflict, binding);
                    Tts.Speak(Message.Localized("settings", "rebind.stolen",
                        new { combo = binding.DisplayName, other = conflict.DisplayLabel }).Resolve(), interrupt: true);
                }

                if (!_append) action.ClearBindings(); // append mode ADDS an alternative combo
                action.AddBinding(binding); // BindingsChanged → BindingSetting auto-saves
                Tts.Speak(Message.Localized("settings", "rebind.bound",
                    new { action = action.DisplayLabel, combo = binding.DisplayName }).Resolve());
                _releaseKey = key;
                _awaitRelease = true; // close once the key is released
                return;
            }
        }

        private void Close() => ParentScreen?.RemoveChild(this);

        private static bool HasBinding(InputAction action, InputBinding binding)
        {
            foreach (var b in action.Bindings)
                if (b.Type == binding.Type && b.Serialize() == binding.Serialize()) return true;
            return false;
        }

        // The SAME-CATEGORY action (other than the one being bound) that already uses this exact
        // combo, or null. Other categories never conflict - shadowing resolves them.
        private static InputAction FindConflict(InputBinding binding, InputAction self)
        {
            foreach (var a in InputManager.Actions)
            {
                if (a == self || a.Category != self.Category) continue;
                foreach (var b in a.Bindings)
                    if (b.Type == binding.Type && b.Serialize() == binding.Serialize()) return a;
            }
            return null;
        }

        private static void RemoveEqualBinding(InputAction owner, InputBinding binding)
        {
            foreach (var b in owner.Bindings)
                if (b.Type == binding.Type && b.Serialize() == binding.Serialize())
                {
                    owner.RemoveBinding(b); // BindingsChanged -> its BindingSetting saves
                    return;
                }
        }

        // Keyboard keys we can bind to: every KeyCode except None, the modifier keys, Escape, and the
        // mouse/joystick range (which start at Mouse0). Built once.
        private static KeyCode[] _captureKeys;
        private static KeyCode[] CaptureKeys
        {
            get
            {
                if (_captureKeys != null) return _captureKeys;
                var list = new List<KeyCode>();
                foreach (KeyCode k in System.Enum.GetValues(typeof(KeyCode)))
                {
                    if (k == KeyCode.None || k == KeyCode.Escape) continue;
                    if (k == KeyCode.LeftControl || k == KeyCode.RightControl ||
                        k == KeyCode.LeftShift || k == KeyCode.RightShift ||
                        k == KeyCode.LeftAlt || k == KeyCode.RightAlt) continue;
                    if ((int)k >= (int)KeyCode.Mouse0) continue; // mouse + joystick buttons
                    list.Add(k);
                }
                _captureKeys = list.ToArray();
                return _captureKeys;
            }
        }
    }
}
