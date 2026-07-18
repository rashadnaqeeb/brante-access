using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BranteAccess.Module.Input
{
    /// <summary>
    /// A named mod command with one or more bindings. InputManager polls the phase state
    /// (JustPressed/Held) each frame; UI-category presses route to the navigator dispatcher first,
    /// everything else fires <see cref="Performed"/>. Ported from wotr-access.
    /// </summary>
    public class InputAction
    {
        public string Key { get; }
        public string Label { get; }

        /// <summary>The display label resolved through the settings locale table ("bind.&lt;key&gt;"),
        /// falling back to the registration label. Use for anything spoken/displayed.</summary>
        public string DisplayLabel
            => Localization.LocalizationManager.GetOrDefault("settings", "bind." + Key, Label);

        /// <summary>The input layer this action lives in (decides when it's polled and how identical
        /// chords across categories resolve - see <see cref="InputCategory"/>).</summary>
        public InputCategory Category { get; internal set; } = InputCategory.Global;

        private readonly List<InputBinding> _bindings = new List<InputBinding>();
        public IReadOnlyList<InputBinding> Bindings => _bindings;

        /// <summary>Fired on JustPressed when not consumed by the navigator.</summary>
        public event Action Performed;

        /// <summary>Fired whenever the binding set changes - a future binding-settings layer saves on this.</summary>
        public event Action BindingsChanged;

        public string BindingsDisplay =>
            _bindings.Count == 0 ? "(none)" : string.Join(", ", _bindings.Select(b => b.DisplayName));

        public InputAction(string key, string label)
        {
            Key = key;
            Label = label;
        }

        public InputAction AddBinding(InputBinding binding)
        {
            _bindings.Add(binding);
            BindingsChanged?.Invoke();
            return this;
        }

        public InputAction AddBinding(KeyCode key, bool ctrl = false, bool shift = false, bool alt = false)
            => AddBinding(new KeyboardBinding(key, ctrl, shift, alt));

        /// <summary>Drop all bindings (a rebind replaces them, or a saved config reloads them).</summary>
        public void ClearBindings()
        {
            _bindings.Clear();
            BindingsChanged?.Invoke();
        }

        public bool JustPressed { get { for (int i = 0; i < _bindings.Count; i++) if (_bindings[i].JustPressed()) return true; return false; } }
        public bool Held { get { for (int i = 0; i < _bindings.Count; i++) if (_bindings[i].Held()) return true; return false; } }
        public bool Released { get { for (int i = 0; i < _bindings.Count; i++) if (_bindings[i].Released()) return true; return false; } }

        /// <summary>Whether this action auto-repeats while held (nav directions, Tab). Set via Repeating().</summary>
        public bool Repeats { get; private set; }
        internal float NextRepeatTime;

        public InputAction Repeating() { Repeats = true; return this; }

        internal void InvokePerformed() => Performed?.Invoke();
    }
}
