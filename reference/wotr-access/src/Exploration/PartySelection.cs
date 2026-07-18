using System.Collections.Generic;
using Kingmaker;
using Kingmaker.EntitySystem.Entities; // UnitEntityData
using Kingmaker.UI.Common; // UIUtilityUnit.SetCharacterSelected (the game's own select path)
using TurnBased.Controllers; // CombatController (turn-based state)
using WrathAccess.Screens;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// Keyboard party selection, driving the game's REAL selection (`UI.SelectionManager` →
    /// `SelectionCharacter.SelectedUnits`), so move-to-cursor's single-vs-formation behaviour falls out of
    /// the game's own logic (see <see cref="Scanner"/> / ClickGroundHandler): one member selected → only
    /// they move; the whole party selected → everyone moves into the set formation at the target.
    ///
    /// Ctrl+1..6 each own a member and that member's owned units (pets/mounts — see how the game models
    /// ownership: a pet is its own unit linked by <c>Master</c>, listed in <c>member.Pets</c>): pressing
    /// the key selects the member, pressing it again cycles to their first owned unit, and so on, wrapping
    /// back to the member. The cycle position is derived from the current single selection — no state to
    /// keep — so pressing a different key (or after a select-all) starts that member's ring at the member.
    ///
    /// The manager writes <c>SelectedUnits</c> directly and synchronously, so a selection set here is
    /// visible to the very next move. Gated to focus-mode exploration so it doesn't collide with the
    /// game's own selection keys (which work when focus mode is off and the game owns the keyboard).
    /// Arbitrary multi-select subsets are deferred — Ctrl+A (all) and Ctrl+1..6 (one + owned) cover it.
    /// </summary>
    internal static class PartySelection
    {
        private static bool Active =>
            FocusMode.Active && ScreenManager.Current != null && ScreenManager.Current.Key == "ctx.ingame";

        public static void SelectWholeParty() { if (Active && !TurnBasedBlocks()) DoSelectAll(); }
        public static void SelectMember(int index) { if (Active && !TurnBasedBlocks()) DoSelectMember(index); }

        // Hold position (H) / Stop (G) — the party commands the game exposes as the in-game menu's Hold/Stop
        // buttons (SelectionManager.SwitchHold / SwitchStop, toggling the state across the current selection).
        // These hotkeys only ISSUE the command; the announcement is owned by the HUD Hold/Stop toggle elements,
        // which poll IsHold()/IsStop() each frame and speak the ACTUAL settled result (see InGameScreen). That
        // keeps one announcement path whether the change came from a hotkey, the HUD button, or the game, and
        // it reports what really happened — never a predicted state that the command might not have reached.
        // Gated like selection (focus-mode exploration), which keeps them out of turn-based combat where they
        // aren't the controls you use.
        public static void ToggleHold()
        {
            if (!Active) return;
            var sm = Game.Instance?.UI?.SelectionManager;
            if (sm == null) return;
            if (!HasControllableSelection()) { Tts.Speak(Loc.T("party.none_selected"), interrupt: true); return; }
            sm.SwitchHold();
        }

        public static void Stop()
        {
            if (!Active) return;
            var sm = Game.Instance?.UI?.SelectionManager;
            if (sm == null) return;
            if (!HasControllableSelection()) { Tts.Speak(Loc.T("party.none_selected"), interrupt: true); return; }
            sm.SwitchStop();
        }

        // Stealth (Ctrl+S) / AI control (Ctrl+D) — the two ControlCharactersVM toggles the game draws in the
        // action-bar cluster (the sneak + AI buttons). Same selection-command shape as Hold/Stop: the hotkey
        // just invokes the game's own click handler (OnStealthClick / OnAiClick, which carry the stealth tick
        // / AI-flag logic), and the HUD toggle elements own the announcement by polling IsStealthOn/IsAiOn.
        public static void ToggleStealth()
        {
            if (!Active) return;
            if (!HasControllableSelection()) { Tts.Speak(Loc.T("party.none_selected"), interrupt: true); return; }
            ControlChars()?.OnStealthClick();
        }

        public static void ToggleAi()
        {
            if (!Active) return;
            if (!HasControllableSelection()) { Tts.Speak(Loc.T("party.none_selected"), interrupt: true); return; }
            ControlChars()?.OnAiClick();
        }

        // Live state for the HUD toggles (read the units directly, not the VM's once-per-frame reactive).
        // Stealth mirrors the game's "any selected unit is sneaking"; AI mirrors "every selected unit is
        // AI-controlled" — exactly what ControlCharactersVM.IsInStealthState / IsInAiState report.
        public static bool IsStealthOn()
        {
            var units = Game.Instance?.SelectionCharacter?.SelectedUnits;
            if (units == null) return false;
            foreach (var u in units)
                if (u != null && !u.IsDisposed && u.Descriptor.State.IsInStealth) return true;
            return false;
        }

        public static bool IsAiOn()
        {
            var units = Game.Instance?.SelectionCharacter?.SelectedUnits;
            if (units == null) return false;
            bool any = false;
            foreach (var u in units)
                if (u != null && !u.IsDisposed) { any = true; if (!u.IsAIEnabled) return false; }
            return any;
        }

        private static Kingmaker.UI.MVVM._VM.ActionBar.ControlCharactersVM ControlChars()
            => Game.Instance?.RootUiContext?.InGameVM?.StaticPartVM?.ActionBarVM?.ControlCharactersVM;

        // A cheap, allocation-free fingerprint of the current selection: its size and the single-selected
        // unit's identity, which together capture every real selection change (whole-party ↔ single, and
        // single ↔ different single). The HUD Hold/Stop toggles use it to tell a genuine toggle apart from
        // IsHold()/IsStop() flipping merely because the selection changed (they're "all selected …"), so a
        // character swap rebaselines them silently instead of chattering "Hold released".
        public static int SelectionFingerprint()
        {
            var sc = Game.Instance?.SelectionCharacter;
            if (sc == null) return 0;
            int count = sc.SelectedUnits?.Count ?? 0;
            var single = sc.SingleSelectedUnit;
            return count * 397 ^ (single != null ? single.GetHashCode() : 0);
        }

        // SwitchHold/SwitchStop quietly no-op on an empty selection. Guard so we report "No one selected".
        private static bool HasControllableSelection()
        {
            var units = Game.Instance?.SelectionCharacter?.SelectedUnits;
            if (units == null) return false;
            foreach (var u in units)
                if (u != null && u.IsDirectlyControllable) return true;
            return false;
        }

        // In turn-based combat the acting unit is fixed by initiative and the game keeps it selected, so
        // switching party members (or selecting all) doesn't apply — you act with one unit per turn. Stand
        // down and announce whose turn it is instead of fighting the game's selection.
        private static bool TurnBasedBlocks()
        {
            if (!CombatController.IsInTurnBasedCombat()) return false;
            var cur = CombatController.SelectedUnit;
            Tts.Speak(cur != null
                ? Message.Localized("ui", "combat.turn", new { name = cur.CharacterName }).Resolve()
                : Message.Localized("ui", "combat.mode_turn_based").Resolve(), interrupt: true);
            return true;
        }

        private static void DoSelectAll()
        {
            var sm = Game.Instance?.UI?.SelectionManager;
            if (sm == null) return;
            sm.SelectAll();
            // Count only units that survive the game's controllability purge (capital party mode
            // leaves just the main character + their pets commandable).
            int n = 0;
            foreach (var u in Game.Instance.SelectionCharacter.SelectedUnits)
                if (u != null && u.IsDirectlyControllable) n++;
            Tts.Speak(n == 1 ? Loc.T("party.selected_all_one") : Loc.T("party.selected_all", new { count = n }), interrupt: true);
        }

        private static void DoSelectMember(int index)
        {
            var ring = BuildRing(index);
            if (ring.Count == 0)
            {
                Tts.Speak(Loc.T("party.no_member", new { index = index + 1 }), interrupt: true);
                return;
            }
            // Capital party mode (e.g. the Defender's Heart): companions roam as NPCs — the game
            // purges non-controllable units from the selection every frame (SelectionManagerBase
            // .LateUpdate gates on IsDirectlyControllable), so "selecting" them is a lie that
            // silently reverts to the main character. Say so instead.
            if (!ring[0].IsDirectlyControllable)
            {
                Tts.Speak(Loc.T("party.not_commandable", new { name = ring[0].CharacterName }), interrupt: true);
                return;
            }

            // Cycle within this member's ring (member → owned units → back). Position is derived from the
            // current SINGLE selection: if it's one of this ring's units, advance to the next (wrapping);
            // otherwise — a different member, or a multi-select like Ctrl+A — start at the member (ring[0]).
            var current = Game.Instance.SelectionCharacter.SingleSelectedUnit;
            int pos = (current != null) ? ring.IndexOf(current) : -1;
            var unit = ring[(pos >= 0) ? (pos + 1) % ring.Count : 0];

            // Route through the game's OWN single-character select — exactly what the "select character N"
            // keybinding and a portrait click do (PartyCharacterVM.SetCharacterSelected → this). It both
            // updates the SelectedUnit reactive (so the character sheet/inventory/etc. follow) AND the
            // SelectedUnits set (via SwitchSelectionUnitInGroup), then scrolls the camera to the unit. Our
            // old sm.SelectUnit only did the SelectedUnits half, so SelectedUnit stayed stale and the sheet
            // never updated. follow:false matches the keybinding (recenter, not continuous follow). The
            // unit's "selected" voice bark still fires through SelectUnit's ask path.
            UIUtilityUnit.SetCharacterSelected(unit, follow: false);
            Tts.Speak(Loc.T("party.member_selected", new { name = unit.CharacterName }), interrupt: true);
        }

        // A member's selection ring: the member, then each owned, in-game, controllable unit they own
        // (pets/mounts via member.Pets — a pet is its own unit linked back by Master). Empty if there's
        // no such party slot.
        private static List<UnitEntityData> BuildRing(int index)
        {
            var ring = new List<UnitEntityData>();
            var party = Game.Instance?.Player?.PartyCharacters;
            if (party == null || index < 0 || index >= party.Count) return ring;
            var member = party[index].Value;
            if (member == null || member.View == null) return ring;

            ring.Add(member);
            var pets = member.Pets;
            if (pets != null)
            {
                foreach (var pet in pets)
                {
                    var e = pet.Entity;
                    if (e != null && e != member && e.IsInGame && e.IsDirectlyControllable
                        && e.View != null && !ring.Contains(e))
                        ring.Add(e);
                }
            }
            return ring;
        }
    }
}
