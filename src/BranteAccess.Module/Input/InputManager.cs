using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BranteAccess.Module.Input
{
    /// <summary>
    /// Registry + per-frame poll, ticked from the module's Tick. Actions live in CATEGORIES
    /// (<see cref="InputCategory"/>): each frame the live categories come from
    /// <see cref="ActiveCategoriesProvider"/> (the screen stack, once built; until then the default
    /// is UI while focus mode is on) plus Global, which is always live. Within the live set an
    /// identical chord in two categories resolves to the higher-priority (earlier) one - its lower
    /// twin is SHADOWED. UI-category presses go to <see cref="UiDispatcher"/> (the navigator, once
    /// built) first; every other category, or an unconsumed UI press, fires the action's own
    /// handler. With focus mode off only Global is live, so the game keeps its own keys.
    /// Ported from wotr-access.
    /// </summary>
    public static class InputManager
    {
        private static readonly List<InputAction> _actions = new List<InputAction>();
        public static IReadOnlyList<InputAction> Actions => _actions;

        /// <summary>Fills the frame's live categories in priority order (Global is appended by the
        /// manager itself). Replaced by the screen stack when it lands; the default is the
        /// pre-screen-stack world: UI live whenever focus mode is on.</summary>
        public static Action<List<InputCategory>> ActiveCategoriesProvider = cats =>
        {
            if (FocusMode.Active) cats.Add(InputCategory.UI);
        };

        /// <summary>Routes a UI-category press into the active navigator; returns true if consumed.
        /// Null until the navigator lands - then every UI press falls through to Performed.</summary>
        public static Func<InputAction, bool> UiDispatcher;

        /// <summary>When true, the whole poll stands down so raw keys reach the game (a screen that
        /// captures raw input, e.g. key-binding capture). Wired to the screen stack.</summary>
        public static Func<bool> SuppressDispatch;

        public static InputAction Register(string key, string label, InputCategory category, Action onPerformed = null)
        {
            var action = new InputAction(key, label) { Category = category };
            if (onPerformed != null) action.Performed += onPerformed;
            _actions.Add(action);
            return action;
        }

        /// <summary>Drop all registered actions - the module re-registers from scratch each load,
        /// but statics within one generation must be resettable for tests.</summary>
        public static void Clear() => _actions.Clear();

        // The frame's live state, rebuilt at the top of Tick (cheap: few dozen actions x ~1 binding).
        private static readonly List<InputCategory> _activeCats = new List<InputCategory>();
        private static readonly HashSet<InputBinding> _live = new HashSet<InputBinding>();
        private static readonly Dictionary<string, int> _chordRank = new Dictionary<string, int>();

        /// <summary>Whether the action with this key is currently held via a LIVE (unshadowed,
        /// active-category) binding - for per-frame polling. A held arrow stops counting the
        /// instant a higher claim takes the chord (a popup opening).</summary>
        public static bool Held(string key)
        {
            for (int i = 0; i < _actions.Count; i++)
                if (_actions[i].Key == key) return HeldLive(_actions[i]);
            return false;
        }

        private static bool JustPressedLive(InputAction a)
        {
            for (int i = 0; i < a.Bindings.Count; i++)
                if (_live.Contains(a.Bindings[i]) && a.Bindings[i].JustPressed()) return true;
            return false;
        }

        private static bool HeldLive(InputAction a)
        {
            for (int i = 0; i < a.Bindings.Count; i++)
                if (_live.Contains(a.Bindings[i]) && a.Bindings[i].Held()) return true;
            return false;
        }

        // Live categories from the provider + Global, then walk categories in priority order marking
        // bindings live, shadowing any identical chord already claimed by an earlier (higher-priority)
        // category. Same-category duplicates are both live (first wins).
        private static void RebuildLive()
        {
            _activeCats.Clear();
            ActiveCategoriesProvider(_activeCats);
            if (!_activeCats.Contains(InputCategory.Global)) _activeCats.Add(InputCategory.Global);

            _live.Clear();
            _chordRank.Clear();
            for (int rank = 0; rank < _activeCats.Count; rank++)
            {
                var cat = _activeCats[rank];
                for (int i = 0; i < _actions.Count; i++)
                {
                    var a = _actions[i];
                    if (a.Category != cat) continue;
                    for (int j = 0; j < a.Bindings.Count; j++)
                    {
                        var b = a.Bindings[j];
                        var chord = b.Chord; // cached per binding - see InputBinding
                        if (_chordRank.TryGetValue(chord, out int owner))
                        {
                            if (owner < rank) continue; // shadowed by a higher category
                        }
                        else _chordRank[chord] = rank;
                        _live.Add(b);
                    }
                }
            }
        }

        /// <summary>Dispatching stands down this frame: the player is typing in a game text field
        /// (hero-name entry, dev console) or the focused screen captures raw input. Guards Tick AND
        /// <see cref="Dispatch"/> - the dev route must mirror what a physical press would do.</summary>
        public static bool DispatchSuppressed
            => IsTypingInTextField() || (SuppressDispatch != null && SuppressDispatch());

        // The keystroke that ENDS a suppression source must not dispatch: TMP's Enter commit /
        // Escape cancel deactivate the field during the EventSystem's update, earlier in the same
        // frame than this tick, so by poll time suppression is already off while the key still
        // reads JustPressed - it would re-activate the edit control it just left. Stand down for
        // one tick after suppression ends. Dispatch() is exempt: a synthetic press can never be
        // the physical keystroke that ended the edit.
        private static bool _suppressedLastTick;

        public static void Tick()
        {
            bool suppressed = DispatchSuppressed;
            bool standDown = suppressed || _suppressedLastTick;
            _suppressedLastTick = suppressed;
            if (standDown) return;

            RebuildLive(); // this frame's category claims + chord shadowing

            // Typematic repeat: fire once, pause, then repeat while held - at the user's
            // own OS keyboard delay/rate (falls back to defaults if the query fails).
            float now = UnityEngine.Time.unscaledTime;
            float initialDelay = OsKeyboard.InitialDelay;
            float repeatInterval = OsKeyboard.RepeatInterval;
            for (int i = 0; i < _actions.Count; i++)
            {
                var action = _actions[i];
                bool held = HeldLive(action);

                bool fire = false;
                if (JustPressedLive(action))
                {
                    fire = true;
                    action.NextRepeatTime = now + initialDelay;
                }
                else if (action.Repeats && held && action.NextRepeatTime > 0f && now >= action.NextRepeatTime)
                {
                    // Held past the delay -> auto-repeat. Catch up at most one step per frame. The
                    // NextRepeatTime > 0 guard means we only repeat an action that was actually
                    // JustPressed this hold - NOT one that just became held because a shared key's
                    // modifier was released (releasing Shift while holding Tab must not fire a
                    // stray forward Tab).
                    fire = true;
                    action.NextRepeatTime = now + repeatInterval;
                }
                if (!held) action.NextRepeatTime = 0f; // reset on release (disarms repeat until next press)

                if (!fire) continue;
                Fire(action);
            }
        }

        /// <summary>Fire the action with this key through the same routing a real press takes
        /// (UI to the navigator first, else Performed) - including the category gate: an action
        /// whose category no screen currently claims does not fire, exactly like the physical key
        /// it mirrors. The dev driver's entry point - features are verified through it. False when
        /// no action has the key or its category is inactive.</summary>
        public static bool Dispatch(string key)
        {
            if (DispatchSuppressed) return false;
            RebuildLive();
            for (int i = 0; i < _actions.Count; i++)
                if (_actions[i].Key == key)
                {
                    if (!_activeCats.Contains(_actions[i].Category)) return false;
                    Fire(_actions[i]);
                    return true;
                }
            return false;
        }

        /// <summary>Whether this category is live right now (screen claims + Global). For the dev
        /// driver's error reporting - <see cref="Dispatch"/> enforces the gate itself.</summary>
        public static bool CategoryActive(InputCategory cat)
        {
            RebuildLive();
            return _activeCats.Contains(cat);
        }

        private static void Fire(InputAction action)
        {
            bool consumed = action.Category == InputCategory.UI
                && UiDispatcher != null && UiDispatcher(action);
            if (!consumed) action.InvokePerformed();
        }

        private static bool IsTypingInTextField()
        {
            var es = EventSystem.current;
            if (es == null) return false;
            var selected = es.currentSelectedGameObject;
            if (selected == null) return false;
            var tmpField = selected.GetComponent<TMP_InputField>();
            if (tmpField != null && tmpField.isFocused) return true;
            var legacyField = selected.GetComponent<InputField>();
            return legacyField != null && legacyField.isFocused;
        }
    }
}
