using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings; // UIStrings (game-localized Accept label)
using Kingmaker.UI.MVVM._VM.GroupChanger;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The Group manager (<see cref="GroupChangerVM"/>) — party selection, most commonly when leaving an
    /// area for the world map, but also on recruit, capital management, scripted ShowPartySelection, a party
    /// split (the Detach subclass), and from the world map itself. Two arrow-navigated lists (Current Party /
    /// Companions); Enter on a character moves it between them (<c>MoveCharacter</c>, mirroring the portrait
    /// click), respecting the locked main character and the slot cap. Character nodes key by LIST + SLOT
    /// (not by unit), so after a move focus stays in the list you were working in, on the row the character
    /// vacated — the old hand-written FocusAfterMove, emergent. An Accept stop commits the party and leaves
    /// (<c>Go</c>); Escape cancels and stays (<c>Close</c>, only when the party is already valid — like the
    /// game's X, which hides otherwise; the Detach variant never allows cancel). Driven, like the loot
    /// window, off a <c>GroupChangerContextVM</c> — the in-game static HUD's, or the global map's (see
    /// <see cref="Vm"/>) — not a RootUIContext service window.
    /// </summary>
    public sealed class GroupChangerScreen : Screen
    {
        public override string Key => "ctx.groupchanger";
        public override string ScreenName => Loc.T("screen.group_manager");
        public override int Layer => 16; // a hard modal over the in-game context (and the global map)

        // A true modal. Unlike loot/dialogue/rest (which lose control, so the in-game screen drops its
        // Exploration/InGame categories), the group changer keeps control — so block the categories below
        // it explicitly, or exploration hotkeys would still fire under the modal.
        public override bool Exclusive => true;

        private static GroupChangerVM Vm()
        {
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            if (rc == null) return null;
            // The group changer has two hosts, each alive only in its context: the in-game static HUD (area
            // exit / recruit / capital / scripted ShowPartySelection / party split), and the world map's own
            // context ("change party" on the global map, the islands map). Both surface the SAME base
            // GroupChangerVM (Common or Detach), so one screen covers all of it.
            return rc.InGameVM?.StaticPartVM?.GroupChangerContextVM?.GroupChangerVM?.Value
                ?? rc.GlobalMapVM?.GroupChangerContextVM?.GroupChangerVM?.Value;
        }

        public override bool IsActive() => Vm() != null;


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "group:" + vm.GetHashCode() + ":"; // a new VM = a fresh window = fresh keys

            EmitList(b, vm, vm.PartyCharacter, vm.PartyHeader, k + "party");
            EmitList(b, vm, vm.RemoteCharacter, vm.RemoteHeader, k + "remote");

            // Accept = commit the chosen party and leave (Go); greys out until the selection is valid.
            b.BeginStop(k + "accept").AddItem(ControlId.Structural(k + "accept"),
                GraphNodes.Button(
                    () => TextUtil.StripRichText((string)UIStrings.Instance.CommonTexts.Accept),
                    () => vm.Go(),
                    () => vm.AcceptEnabled.Value));
        }

        // One list as a Tab-stop. Keys are LIST + SLOT INDEX (structural, not the unit): a move re-deals
        // the lists and focus stays on the same slot — now the next character — which the differ reads.
        private static void EmitList(GraphBuilder b, GroupChangerVM vm,
            IEnumerable<GroupChangerCharacterVM> chars, string header, string key)
        {
            b.BeginStop(key).PushContext(header, "list");
            int i = 0;
            foreach (var ch in chars)
            {
                var c = ch;
                b.AddItem(ControlId.Structural(key + ":" + i),
                    GraphNodes.Button(() => CharLabel(c), () => Activate(vm, c)));
                i++;
            }
            b.PopContext();
        }

        // Name plus the badges the portrait shows (lock / level-up / mythic level-up / overload).
        private static string CharLabel(GroupChangerCharacterVM ch)
        {
            var parts = new List<string> { ch.UnitRef.Value.CharacterName };
            if (ch.IsLock) parts.Add(Loc.T("group.locked_tag"));
            if (ch.IsLevelUp) parts.Add(Loc.T("group.levelup_tag"));
            if (ch.IsMythicLevelUp) parts.Add(Loc.T("group.mythic_tag"));
            if (ch.IsCharacterOverload) parts.Add(Loc.T("group.overload_tag"));
            return string.Join(", ", parts);
        }

        private static void Activate(GroupChangerVM vm, GroupChangerCharacterVM ch)
        {
            if (ch.IsLock) { Tts.Speak(Loc.T("group.cant_move")); return; } // main / required: pinned in party
            bool inParty = false;
            foreach (var p in vm.PartyCharacter)
                if (ReferenceEquals(p, ch)) { inParty = true; break; }
            if (!inParty && vm.PartyCharacter.Count >= 6) { Tts.Speak(Loc.T("group.party_full")); return; }

            vm.MoveCharacter(ch.UnitRef); // Party <-> Companions (mirrors the portrait click)
            Tts.Speak(Loc.T("group.moved", new { name = ch.UnitRef.Value.CharacterName,
                dest = inParty ? vm.RemoteHeader : vm.PartyHeader }));
            // The next render re-deals the slot keys; focus stays on the vacated slot (the next
            // character), or slides to the nearest survivor if the list shrank at the end.
        }

        // Escape = cancel and stay in the area (Close), but only when the current party is valid — mirroring
        // the game's X, which is shown only then; otherwise tell the player to pick a valid party.
        public override IEnumerable<ElementAction> GetActions()
        {
            var vm = Vm();
            if (vm == null) yield break;
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ =>
            {
                if (vm.CloseCondition()) vm.Close();
                else Tts.Speak(Loc.T("group.cant_close"));
            });
        }
    }
}
