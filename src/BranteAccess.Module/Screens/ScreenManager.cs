using System;
using System.Collections.Generic;
using System.Linq;
using BranteAccess.Module.Input;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// Resolves the active screen stack each frame by polling every registered screen's IsActive
    /// against live game state (poll-and-diff - robust to the game's own open/close flows, which
    /// have no single event), then dispatches lifecycle events. The stack is ordered bottom to top
    /// by Layer; the focused screen is the top screen's deepest active child. Ported from
    /// wotr-access; the navigator hooks are seams until the navigator lands.
    /// </summary>
    public static class ScreenManager
    {
        private static readonly List<Screen> _registered = new List<Screen>();
        private static List<Screen> _stack = new List<Screen>();
        private static Screen _focused;

        /// <summary>Navigator seams, filled when the graph navigator lands: attach to a newly
        /// focused screen; drop per-screen nav state on close; ensure something is focused after
        /// the focused screen built its content.</summary>
        public static Action<Screen> NavigatorAttach;
        public static Action<Screen> NavigatorScreenClosed;
        public static Action NavigatorEnsureFocus;

        public static Screen Current => _stack.Count > 0 ? _stack[_stack.Count - 1].DeepestActiveScreen() : null;
        public static IReadOnlyList<Screen> Stack => _stack;

        /// <summary>Active screens in focus-priority order - the focused screen first (top outer
        /// screen's deepest child), then outward/down to the base context. The order the input
        /// claim-chain walks: a deeper screen's categories shadow a shallower one's.</summary>
        public static IEnumerable<Screen> FocusedFirst()
        {
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                var chain = new List<Screen>();
                for (var s = _stack[i]; s != null; s = s.ActiveChild) chain.Add(s); // outer -> deepest
                for (int j = chain.Count - 1; j >= 0; j--) yield return chain[j];  // deepest -> outer
            }
        }

        /// <summary>InputManager's ActiveCategoriesProvider: the union of every active screen's
        /// declared categories, focus-first, stopping at the first Exclusive screen (a modal owns
        /// the keyboard). Global is appended by InputManager itself.</summary>
        public static void ProvideCategories(List<InputCategory> cats)
        {
            foreach (var screen in FocusedFirst())
            {
                foreach (var c in screen.InputCategories)
                    if (!cats.Contains(c)) cats.Add(c);
                if (screen.Exclusive) break;
            }
        }

        public static void Register(Screen screen) => _registered.Add(screen);

        /// <summary>Reset all stack state - a fresh module generation re-registers from scratch.</summary>
        public static void Clear()
        {
            _registered.Clear();
            _stack = new List<Screen>();
            _focused = null;
        }

        public static void Tick()
        {
            ApplyDiff(Resolve()); // poll the outer (game-driven) screens -> push/pop
            SyncFocus();          // focus the deepest screen
            var cur = Current;
            if (cur != null) Safe(cur.OnUpdate, cur, "OnUpdate"); // may push/remove child sub-screens
            SyncFocus();          // re-sync if OnUpdate changed the child tree
            NavigatorEnsureFocus?.Invoke();
        }

        /// <summary>Active screens, ordered bottom (low layer) to top (high layer).</summary>
        private static List<Screen> Resolve()
        {
            var active = new List<Screen>();
            for (int i = 0; i < _registered.Count; i++)
                if (SafeIsActive(_registered[i])) active.Add(_registered[i]);
            return active.OrderBy(s => s.Layer).ToList();
        }

        private static bool SafeIsActive(Screen s)
        {
            try { return s.IsActive(); }
            catch (Exception e)
            {
                Mod.Error("Screen.IsActive threw for '" + s.Key + "': " + e.Message);
                return false;
            }
        }

        // Diff the polled active set against the persistent stack: pop outer screens that went
        // inactive (each with its whole child subtree, top first) and push newly-active ones.
        private static void ApplyDiff(List<Screen> desired)
        {
            for (int i = _stack.Count - 1; i >= 0; i--)
                if (!desired.Contains(_stack[i])) PopTree(_stack[i]);
            for (int i = 0; i < desired.Count; i++)
                if (!_stack.Contains(desired[i])) { var s = desired[i]; Safe(s.OnPush, s, "OnPush"); }
            _stack = desired;
        }

        // An outer screen leaving the stack disposes its child subtree, then OnPops itself.
        private static void PopTree(Screen s)
        {
            if (s.ActiveChild != null) s.RemoveChild(s.ActiveChild);
            Safe(s.OnPop, s, "OnPop");
            // Closing a screen clears its nav state (reopening starts fresh) unless the screen opts
            // out because popping isn't really closing.
            if (!s.KeepStateOnPop) NavigatorScreenClosed?.Invoke(s);
        }

        // Re-attach the navigator whenever the focused screen changes - from an outer push/pop OR a
        // child-tree change. Idempotent (no-op when unchanged).
        private static void SyncFocus()
        {
            var cur = Current;
            if (ReferenceEquals(cur, _focused)) return;
            _focused?.OnUnfocus();
            _focused = cur;
            if (cur != null) Safe(cur.OnFocus, cur, "OnFocus"); // speaks the screen name
            NavigatorAttach?.Invoke(cur);
        }

        private static void Safe(Action a, Screen s, string hook)
        {
            try { a(); }
            catch (Exception e) { Mod.Error("Screen." + hook + " threw for '" + (s?.Key ?? "?") + "': " + e); }
        }
    }
}
