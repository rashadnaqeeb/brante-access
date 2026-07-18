using System;
using System.Collections.Generic;
using Kingmaker;

namespace WrathAccess.Input
{
    /// <summary>
    /// Registry + per-frame poll, ticked from Main.OnUpdate. Actions live in CATEGORIES
    /// (<see cref="InputCategory"/>): each frame the live categories are the union of EVERY active screen's
    /// declared list, walked focus-first (the focused screen's deepest child down to the base context) so a
    /// deeper screen's categories take priority — until an <see cref="WrathAccess.Screens.Screen.Exclusive"/>
    /// screen, which blocks everything below it — plus Global. So a screen claims only its own categories and
    /// lets lower screens' categories pass through (a dialogue doesn't kill the in-game screen's exploration
    /// keys); an identical chord in two live categories resolves to the higher-priority (deeper) one (its
    /// lower twin is SHADOWED — that's how the same arrows mean HUD nav focused and cursor movement unfocused).
    /// UI-category presses dispatch into the active navigator; every other category fires its handler
    /// directly. With focus mode off only Global is live, so the game keeps its own keys.
    /// </summary>
    public static class InputManager
    {
        private static readonly List<InputAction> _actions = new List<InputAction>();
        public static IReadOnlyList<InputAction> Actions => _actions;

        public static InputAction Register(string key, string label, InputCategory category, Action onPerformed = null)
        {
            var action = new InputAction(key, label) { Category = category };
            if (onPerformed != null) action.Performed += onPerformed;
            _actions.Add(action);
            return action;
        }

        // The frame's live state, rebuilt at the top of Tick (cheap: ~50 actions x ~1 binding).
        private static readonly List<InputCategory> _activeCats = new List<InputCategory>();
        private static readonly HashSet<InputBinding> _live = new HashSet<InputBinding>();
        private static readonly Dictionary<string, int> _chordRank = new Dictionary<string, int>();

        /// <summary>Whether the action with this key is currently held via a LIVE (unshadowed, active-
        /// category) binding — for per-frame polling (e.g. the cursor's held-arrow vector). A held
        /// arrow stops counting the instant a higher claim takes the chord (a menu opening).</summary>
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

        // Live categories = the union of every active screen's declaration, walked focus-first (so a deeper
        // screen's categories rank higher), stopping at the first Exclusive screen (it blocks the rest of
        // the stack), + Global; focus mode off = Global only. Then walk categories in priority order marking
        // bindings live, shadowing any identical chord already claimed by an earlier (higher-priority)
        // category. Same-category duplicates are both live (the rebind capture prevents them; first wins).
        private static void RebuildLive()
        {
            _activeCats.Clear();
            if (FocusMode.Active)
            {
                foreach (var screen in WrathAccess.Screens.ScreenManager.FocusedFirst())
                {
                    foreach (var c in screen.InputCategories)
                        if (!_activeCats.Contains(c)) _activeCats.Add(c);
                    if (screen.Exclusive) break; // a modal owns the keyboard — block lower screens' categories
                }
            }
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
                        var chord = b.Chord; // cached per binding — was rebuilt every frame (the input GC churn)
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

        public static void Tick()
        {
            // Don't steal keystrokes while the player is typing in a game text field — either the
            // game's own console field (IsInInputField) or one we're driving via TextEntry.
            if (IsTypingInTextField() || WrathAccess.UI.TextEntry.SuppressInput) return;

            // A screen capturing raw input (e.g. key-binding capture) wants the keys to reach
            // the game's own handler — stand down entirely while it's focused.
            var current = WrathAccess.Screens.ScreenManager.Current;
            if (current != null && current.CapturesRawInput) return;

            RebuildLive(); // this frame's category claims + chord shadowing

            // Typematic repeat: fire once, pause, then repeat while held — at the user's
            // own OS keyboard delay/rate (falls back to defaults off Windows).
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
                    // Held past the delay → auto-repeat. Catch up at most one step per frame. The
                    // NextRepeatTime > 0 guard means we only repeat an action that was actually JustPressed
                    // this hold — NOT one that just became held because a shared key's modifier was released
                    // (e.g. releasing Shift while holding Tab must not fire a stray forward Tab).
                    fire = true;
                    action.NextRepeatTime = now + repeatInterval;
                }
                if (!held) action.NextRepeatTime = 0f; // reset on release (disarms repeat until next press)

                if (!fire) continue;
                // UI presses go to the navigator (UI is only ever live in focus mode); everything else
                // fires its handler directly — the category already decided who owns the key, so the
                // old navigator-first-then-bubble fallback chain is gone.
                bool consumed = action.Category == InputCategory.UI
                    && WrathAccess.UI.Navigation.DispatchJustPressed(action);
                if (!consumed) action.InvokePerformed();
            }
        }

        private static bool IsTypingInTextField()
        {
            var game = Game.Instance;
            return game != null && game.RootUiContext != null && game.RootUiContext.IsInInputField;
        }
    }
}
