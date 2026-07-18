using Kingmaker.UI.MVVM._VM.ActionBar; // ActionBarSlotVM

namespace WrathAccess.Exploration
{
    /// <summary>
    /// Coordinator for accessible targeting. It holds the registered <see cref="ITargetingMode"/>s and owns
    /// the shared input plumbing: while any mode is aiming (<see cref="Aiming"/>), the scanner's
    /// act-on-target keys commit/cancel through whichever one is live (Enter at the cursor, I on the scanner
    /// item, Backspace/Escape to cancel). Activation is type-specific (an action-bar slot vs the HUD Rest
    /// button), so each kind gets its own entry point; everything after that is shared — which is exactly what
    /// lets the rest camp reuse the same aim/commit/cancel flow as a spell.
    ///
    /// To add a new targeting kind: implement <see cref="ITargetingMode"/>, add an instance to
    /// <see cref="Modes"/>, and (if it needs a bespoke entry) add an activation method here.
    /// </summary>
    internal static class Targeting
    {
        private static readonly AbilityTargeting Ability = new AbilityTargeting();
        private static readonly RestTargeting Rest = new RestTargeting();

        // Registry. Order is the resolution priority on the (rare, transient) chance two report active at once;
        // ability first, since arming it replaces any other pointer mode anyway.
        private static readonly ITargetingMode[] Modes = { Ability, Rest };

        /// <summary>True while some mode is aiming, so the act-on-target keys should commit/cancel instead of
        /// doing their normal job.</summary>
        public static bool Aiming => Current != null;

        private static ITargetingMode Current
        {
            get
            {
                foreach (var m in Modes)
                    if (m.Active) return m;
                return null;
            }
        }

        // ---- activation (type-specific entry points) ----

        /// <summary>Activate an action-bar slot: self-cast / aim / toggle (see <see cref="AbilityTargeting"/>).</summary>
        public static void Activate(ActionBarSlotVM vm) => Ability.Activate(vm);

        /// <summary>Activate the HUD Rest button: enter rest aim if the game allows resting here.
        /// <paramref name="name"/> is the game's localized Rest label, announced as the prompt.</summary>
        public static void BeginRest(string name) => Rest.Begin(name);

        // ---- commit / cancel (while aiming) ----

        /// <summary>Enter: commit at the world cursor — the unit under it if any, else the cursor point.</summary>
        public static void CommitAtCursor()
        {
            var m = Current;
            if (m == null) return;
            if (!Cursor.Has) { Tts.Speak(Loc.T("scan.no_cursor"), interrupt: true); return; }
            m.CommitAt(CursorTarget.Inside()?.TargetUnit, Cursor.Position.Value);
        }

        /// <summary>I: commit at the selected scanner item — its unit if entity-backed, else its point.</summary>
        public static void CommitOn(ScanItem item)
        {
            var m = Current;
            if (m == null) return;
            if (item == null) { Tts.Speak(Loc.T("scan.no_item"), interrupt: true); return; }
            m.CommitAt(item.TargetUnit, item.Position);
        }

        /// <summary>Backspace/Escape: cancel the active aim.</summary>
        public static void Cancel() => Current?.Cancel();

        // ---- per-frame ----

        private static bool _wasAiming;

        /// <summary>Per-frame upkeep for every mode (e.g. the rest camp's deferred party interaction), plus:
        /// the moment aiming begins, hand the keyboard from the HUD back to exploration so the player can aim
        /// immediately — the scanner/cursor own the keys only while the HUD is unfocused. Saves manually
        /// Tabbing out of the action bar after using an ability or Rest. Only the in-game HUD (a StartUnfocused
        /// screen) is blurred; if focus is elsewhere it's left alone.</summary>
        public static void Tick()
        {
            foreach (var m in Modes)
                m.Tick();

            bool aiming = Aiming;
            if (aiming && !_wasAiming
                && WrathAccess.UI.Navigation.HasFocus
                && (WrathAccess.Screens.ScreenManager.Current?.StartUnfocused ?? false))
                WrathAccess.UI.Navigation.Blur();
            _wasAiming = aiming;
        }
    }
}
