using System.Collections.Generic;
using BranteAccess.Module.Input;
using BranteAccess.Module.Speech;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// Base for a navigable screen. Lifecycle (dispatched by ScreenManager from the stack diff):
    /// OnPush (entered the stack), OnFocus (became the focused screen); OnUnfocus then OnPop on
    /// the way out. The push/focus split enables focus restoration: a covered screen gets
    /// OnUnfocus then, when re-exposed, OnFocus without another OnPush. Ported from wotr-access;
    /// graph declaration (Build) arrives with the navigator.
    /// </summary>
    public abstract class Screen
    {
        /// <summary>Stable identity used for stack diffing.</summary>
        public abstract string Key { get; }

        /// <summary>Spoken when the screen gains focus. Null = silent. A Message so it re-resolves
        /// per announcement (live language).</summary>
        public virtual Message ScreenName => null;

        /// <summary>Stack layer: higher sits on top. 0 = base context (a scene), 10 = game window,
        /// 20 = popup, 24 = pause, 30+ = modal overlays.</summary>
        public virtual int Layer => 0;

        /// <summary>Is this screen currently showing? Evaluated every frame against live game state.</summary>
        public abstract bool IsActive();

        /// <summary>Keep this screen's per-screen nav state when it POPS. Default false: closing a
        /// window resets it - reopening starts fresh. Override where popping isn't really closing.</summary>
        public virtual bool KeepStateOnPop => false;

        /// <summary>When true (and focused), InputManager stops dispatching so raw keys reach the
        /// game (a key-binding capture reads input itself).</summary>
        public virtual bool CapturesRawInput => false;

        private static readonly InputCategory[] UiOnly = { InputCategory.UI };

        /// <summary>The input categories this screen uses, in priority order (an identical chord in
        /// two live categories resolves to the earlier one). Default: plain UI navigation.</summary>
        public virtual IReadOnlyList<InputCategory> InputCategories => UiOnly;

        /// <summary>When true, this screen blocks the categories of screens BELOW it in the stack -
        /// a true modal that owns the keyboard. Default false: lower screens' categories pass through.</summary>
        public virtual bool Exclusive => false;

        public virtual void OnPush() { }

        public virtual void OnFocus()
        {
            // Screen-change announcement - never interrupts (house style).
            var name = ScreenName;
            if (name != null)
            {
                var text = name.Resolve();
                if (text.Length > 0) Mod.Speech.Speak(text);
            }
        }

        public virtual void OnUnfocus() { }
        public virtual void OnPop() { }

        /// <summary>Per-frame update for the FOCUSED screen, dispatched by ScreenManager.</summary>
        public virtual void OnUpdate() { }

        // ---- child screen tree (mod-driven sub-screens within a screen) ----
        // A screen can host a single ActiveChild (which can host its own child, forming a chain) -
        // a drill-in detail page, a confirm layer. The focused screen is the chain's deepest. The
        // poll-driven OUTER stack lives in ScreenManager; children are pushed/removed imperatively
        // by their parent; ScreenManager re-syncs focus each frame.

        /// <summary>The screen hosting this one as a child, or null for an outer (poll-driven) screen.</summary>
        public Screen ParentScreen { get; private set; }

        /// <summary>This screen's single active child sub-screen, or null.</summary>
        public Screen ActiveChild { get; private set; }

        /// <summary>The deepest screen in this chain (this screen if it has no active child).</summary>
        public Screen DeepestActiveScreen()
        {
            var s = this;
            while (s.ActiveChild != null) s = s.ActiveChild;
            return s;
        }

        /// <summary>Push a sub-screen as this screen's active child (replacing any existing child).
        /// The child becomes the focused screen on ScreenManager's next tick.</summary>
        public void PushChild(Screen child)
        {
            if (child == null || child == ActiveChild) return;
            if (ActiveChild != null) RemoveChild(ActiveChild);
            child.ParentScreen = this;
            ActiveChild = child;
            child.OnPush();
        }

        /// <summary>Remove this screen's active child, disposing its whole subtree first
        /// (deepest-first OnPop). Focus falls back to this screen on the next tick.</summary>
        public void RemoveChild(Screen child)
        {
            if (child == null || ActiveChild != child) return;
            if (child.ActiveChild != null) child.RemoveChild(child.ActiveChild); // grandchildren first
            child.OnPop();
            ScreenManager.NavigatorScreenClosed?.Invoke(child);
            child.ParentScreen = null;
            ActiveChild = null;
        }
    }
}
