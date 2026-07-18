using Kingmaker;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// Announces party command-state changes — hold position, stop, stealth, AI — by polling the game
    /// state and speaking when it changes, no matter what caused it (our hotkeys, the HUD toggles, or the
    /// game itself) and regardless of UI focus. Spoken directly through the speech handler — deliberately
    /// NOT an event (events are a curated playability set) and deliberately NOT a UI element concern (the
    /// HUD toggles are plain checkboxes; smuggling this watcher into them was the old hack).
    ///
    /// Rebaselines silently when the SELECTION changes: Hold/Stop/Stealth/AI are all-or-nothing aggregates
    /// over the selected characters, so a character swap flips them without anything being toggled.
    /// Ticked from InGameScreen.OnUpdate — per frame while in play, matching where the announcements used
    /// to originate (covered screens pause it, so a dialogue doesn't narrate hold flips underneath).
    /// </summary>
    internal static class PartyStateWatch
    {
        private static bool _armed;
        private static int _ctx;
        private static bool _hold, _stop, _stealth, _ai;

        // Held touch charges announced so far: unit id -> spell name. A lingering charge blocks
        // recasting its spell and — out of combat — never expires; sighted players see a glowing
        // hand. Announce once per hold, and only when the holder is command-idle (a running command
        // means the auto-delivery is still in flight and will clear it silently).
        private static readonly System.Collections.Generic.Dictionary<string, string> _held
            = new System.Collections.Generic.Dictionary<string, string>();

        public static void Tick()
        {
            TickTouchCharges();
            var sel = Game.Instance?.UI?.SelectionManager;
            if (sel == null) return;
            bool hold = sel.IsHold();
            bool stop = sel.IsStop();
            bool stealth = PartySelection.IsStealthOn();
            bool ai = PartySelection.IsAiOn();
            int ctx = PartySelection.SelectionFingerprint();

            if (!_armed || ctx != _ctx)
            {
                _armed = true;
                _ctx = ctx;
                _hold = hold; _stop = stop; _stealth = stealth; _ai = ai;
                return; // baseline / selection swap — silent
            }

            if (hold != _hold) { _hold = hold; Tts.Speak(Loc.T(hold ? "party.hold_on" : "party.hold_off")); }
            // StopState auto-clears the instant a unit takes a manual order, so "un-stopped" is just noise.
            if (stop != _stop) { _stop = stop; if (stop) Tts.Speak(Loc.T("party.stopped")); }
            if (stealth != _stealth) { _stealth = stealth; Tts.Speak(Loc.T(stealth ? "party.stealth_on" : "party.stealth_off")); }
            if (ai != _ai) { _ai = ai; Tts.Speak(Loc.T(ai ? "party.ai_on" : "party.ai_off")); }
        }

        private static void TickTouchCharges()
        {
            var party = Game.Instance?.Player?.Party;
            if (party == null) return;
            foreach (var u in party)
            {
                if (u == null) continue;
                var part = u.Get<Kingmaker.UnitLogic.Parts.UnitPartTouch>();
                string spell = part != null && part.Ability != null ? part.Ability.Data.Name : null;
                if (spell == null) { _held.Remove(u.UniqueId); continue; }
                if (!u.Commands.Empty) continue; // delivery in flight — let it finish quietly
                if (_held.TryGetValue(u.UniqueId, out var prev) && prev == spell) continue;
                _held[u.UniqueId] = spell;
                Tts.Speak(Loc.T("touch.holding", new { name = u.CharacterName, spell }));
            }
        }
    }
}
