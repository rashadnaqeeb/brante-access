using System.Collections.Generic;
using BranteAccess.Module.Input;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;

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

        // ---- graph declaration (consumed by the navigator) ----

        /// <summary>Declare this screen's navigable content into the builder, fresh from live game
        /// state (immediate mode - called per operation and per frame; never cache). The default is
        /// empty: the screen has no navigable content yet and arrows/Tab fall through.</summary>
        public virtual void Build(GraphBuilder b) { }

        /// <summary>Where focus lands when the screen first gains content: the stop with this key
        /// (its remembered/selected/first node). Null = the graph's start node.</summary>
        public virtual object InitialFocusStop => null;

        /// <summary>Tab past the last stop wraps to the first (and vice versa). Default false: Tab
        /// stops at the ends (or blurs, on a StartUnfocused screen).</summary>
        public virtual bool Wrap => false;

        /// <summary>When true the screen starts with NOTHING focused (exploration state - the game's
        /// own keys work); Tab enters the graph, Tab off the end leaves it again. Default false:
        /// focus seats itself as soon as the screen has content.</summary>
        public virtual bool StartUnfocused => false;

        /// <summary>Type-ahead search is available on this screen. Default true; screens whose stops
        /// are log-like text may keep it, screens where letters mean something else opt out.</summary>
        public virtual bool AllowsTypeahead => true;

        /// <summary>Screen-level detail: what Space reads when the focused node has no detail
        /// of its own - a window's what-this-is help where the game provides one (the sighted
        /// user's TitleRow help icon). Null keeps the plain no-tooltip line.</summary>
        public virtual string HelpText() => null;

        /// <summary>Screen-level actions dispatched by id - the navigator maps Escape to
        /// <see cref="ActionIds.Back"/>. Default: none.</summary>
        public virtual IEnumerable<ElementAction> GetActions() { yield break; }

        /// <summary>Run the screen action with this id. False when the screen doesn't declare it
        /// (the navigator lets the key fall through).</summary>
        public bool InvokeAction(string id)
        {
            foreach (var a in GetActions())
                if (a.Id == id) { a.Execute(); return true; }
            return false;
        }

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
