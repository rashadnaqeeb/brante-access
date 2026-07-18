using System.Collections.Generic;
using WrathAccess.UI;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Base for a navigable screen. Lifecycle (dispatched by ScreenManager from the stack diff): OnPush (entered
    /// the stack) → OnFocus (became active); OnUnfocus → OnPop on the way out. The
    /// push/focus split enables focus restoration: a covered screen gets OnUnfocus
    /// then, when re-exposed, OnFocus without another OnPush, so its built tree and
    /// remembered focus survive.
    ///
    /// Navigation/input is owned by the active Navigator (Navigation.Active), which
    /// ScreenManager attaches to the focused screen. Screens declare their graph
    /// (Build, immediate mode) and expose ScreenName.
    /// </summary>
    public abstract class Screen
    {
        /// <summary>Whether Tab wraps from the last stop back to the first (set by screens that are
        /// closed loops, e.g. the formation window).</summary>
        public bool Wrap { get; set; }

        /// <summary>Screen-level actions (Back/Escape handlers and the like), dispatched by id.</summary>
        public virtual IEnumerable<ElementAction> GetActions() { yield break; }

        /// <summary>Find an advertised action by id and execute it. Returns true if found.</summary>
        public bool InvokeAction(string id, object args = null)
        {
            foreach (var a in GetActions())
                if (a.Id == id) { a.Execute(args); return true; }
            return false;
        }

        /// <summary>Stable identity used for stack diffing.</summary>
        public abstract string Key { get; }

        /// <summary>The screen's graph declaration — IMMEDIATE MODE: the navigator calls this on every
        /// render; declare controls fresh from live game state each call (no retained tree, no rebuild
        /// bookkeeping); focus persists by <see cref="WrathAccess.UI.Graph.ControlId"/> identity.
        /// Declare nothing while the screen's content doesn't exist yet (a raw-capture screen declares
        /// nothing at all).</summary>
        public virtual void Build(WrathAccess.UI.Graph.GraphBuilder b) { }

        /// <summary>Keep this screen's per-screen nav state (focus, stop memory, tree expansion) when it
        /// POPS off the stack. Default false: closing a window resets it — reopening starts fresh. Override
        /// true where popping isn't really closing (dialogue hides during cutscene gaps / under the pause
        /// menu) or where resuming your place is the point (the log).</summary>
        public virtual bool KeepStateOnPop => false;

        /// <summary>Graph-native screens: the Tab-stop initial focus lands on when the screen first
        /// gains a cursor (a wizard opening on its page content rather than the roadmap, which stays
        /// first in Tab order). Null = the graph's start node. Declarative — unlike a FocusStop request,
        /// it can't be lost to the attach that follows OnPush.</summary>
        public virtual object InitialFocusStop => null;

        /// <summary>Spoken when the screen gains focus. Null/empty = silent.</summary>
        public virtual string ScreenName => null;

        /// <summary>Stack layer: higher sits on top. 0 = base context, then service windows, then overlays.</summary>
        public virtual int Layer => 0;

        /// <summary>Is this screen currently showing? Evaluated every frame.</summary>
        public abstract bool IsActive();

        /// <summary>When true, the screen starts with NO focused element — input (arrows) bubbles to the
        /// global handlers (e.g. the exploration overlay), and Tab ENTERS the screen's element tree (the
        /// HUD). Tabbing past the ends returns to this unfocused state. Used by the in-game screen so
        /// exploration keeps the arrows and Tab brings up the HUD regions.</summary>
        public virtual bool StartUnfocused => false;

        /// <summary>
        /// When true (and this is the focused screen), InputManager stops dispatching so raw
        /// keys reach the game (e.g. the key-binding capture dialog, which reads input itself).
        /// </summary>
        public virtual bool CapturesRawInput => false;

        /// <summary>Whether typing letters runs the type-ahead search over the focused region. Off for
        /// the in-game screen, where letters are exploration hotkeys (scanner, status, …).</summary>
        public virtual bool AllowsTypeahead => true;

        private static readonly WrathAccess.Input.InputCategory[] UiOnly = { WrathAccess.Input.InputCategory.UI };

        /// <summary>The input categories this screen uses while it's the TOP screen, in priority order
        /// (an identical chord in two categories resolves to the earlier one). Default: plain UI
        /// navigation. The in-game screen adds Exploration and flips the order with HUD focus.</summary>
        public virtual System.Collections.Generic.IReadOnlyList<WrathAccess.Input.InputCategory> InputCategories => UiOnly;

        /// <summary>When true, this screen blocks the input categories of screens BELOW it in the stack:
        /// only its own <see cref="InputCategories"/> (plus Global) stay live — a true modal that owns the
        /// keyboard. Default false: a screen claims just its declared categories and lets lower screens'
        /// categories pass through (so e.g. a dialogue doesn't kill exploration keys the in-game screen owns).</summary>
        public virtual bool Exclusive => false;

        public virtual void OnPush() { }

        public virtual void OnFocus()
        {
            // Screen-change announcement (never interrupt — carried SayTheSpire preference).
            // The Navigator separately announces the focused element within the screen.
            if (!string.IsNullOrEmpty(ScreenName))
                Tts.Speak(ScreenName);
        }

        public virtual void OnUnfocus() { }
        public virtual void OnPop() { }
        /// <summary>Per-frame update for the ACTIVE (top) screen, dispatched by ScreenManager.</summary>
        public virtual void OnUpdate() { }

        // ---- child screen tree (mod-driven sub-screens within a screen) ----
        // A screen can host a single ActiveChild (which can host its own child, forming a chain) — e.g. a
        // dropdown's choice list, a confirm modal, a dialogue sub-choice. The focused screen is the chain's
        // deepest. The poll-driven OUTER stack lives in ScreenManager; these children are pushed/removed
        // imperatively by their parent. ScreenManager re-syncs focus each frame, so a push/remove here is
        // picked up automatically; removing an outer screen disposes its whole child subtree.

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

        /// <summary>Push a sub-screen as this screen's active child (replacing any existing child). The
        /// child becomes the focused screen; ScreenManager re-syncs focus on its next tick.</summary>
        public void PushChild(Screen child)
        {
            if (child == null || child == ActiveChild) return;
            if (ActiveChild != null) RemoveChild(ActiveChild);
            child.ParentScreen = this;
            ActiveChild = child;
            child.OnPush();
        }

        /// <summary>Remove this screen's active child, disposing its whole subtree first (deepest-first
        /// OnPop). Focus falls back to this screen on the next ScreenManager tick.</summary>
        public void RemoveChild(Screen child)
        {
            if (child == null || ActiveChild != child) return;
            if (child.ActiveChild != null) child.RemoveChild(child.ActiveChild); // recurse: grandchildren first
            child.OnPop();
            WrathAccess.UI.Navigation.ScreenClosed(child);
            child.ParentScreen = null;
            ActiveChild = null;
        }

        public virtual List<string> GetHelpMessages() => new List<string>();
    }
}
