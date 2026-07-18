using System.Collections.Generic;
using System.IO; // Path (cue wav)
using Kingmaker; // Game (party members for Ctrl+1..6)
using Kingmaker.EntitySystem.Entities; // UnitEntityData (party + pet ring)
using Kingmaker.UI.MVVM._VM.Formation; // FormationCharacterVM
using UnityEngine; // Vector2, Mathf, Time
using WrathAccess.Audio; // AudioEngines (enter/exit cue)
using WrathAccess.Exploration; // Geo (feet/metres)
using WrathAccess.Exploration.Overlays; // OverlayAudio (cue dir/volume)
using WrathAccess.Input; // InputManager (glide-key Held polling)
using WrathAccess.Settings; // ModSettings / IntSetting (cue volume)
using WrathAccess.UI;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The formation editor — one Tab stop holding a 2-D cursor over the formation's layout space. WASD step
    /// the cursor one grid cell (wired in Main via the Formation input category, live only while this is
    /// focused); each step announces the party member there + its position, or the empty cell's position.
    /// Enter picks up the member at the cursor and drops the held one where you press it again. Editing only
    /// applies to a Custom formation (the Auto one arranges itself).
    ///
    /// The cursor lives in the formation OFFSET space (metres) — the same value GetOffset/MoveCharacter use
    /// and the game adds to the destination on a party move — so placement is 1:1 with the layout. (Shift+WASD
    /// continuous gliding with enter/exit cues + release-to-read is the next increment.)
    /// </summary>
    public sealed class FormationField
    {
        private const float GridStep = 23f / 40f;        // one cell: 23 UI px at 40 px-per-metre ≈ 0.58 m
        private const float FieldHalf = 388f / 2f / 40f; // the draggable field's half-extent ≈ 4.85 m
        private const float GrabRadius = GridStep;        // "on" a member when within ~one cell
        private const float GlideSpeedFeet = 5f;          // Shift+WASD continuous speed (small field → slow)

        private Vector2 _cursor;            // offset metres; +x = east (right), +y = north (forward)
        private FormationCharacterVM _held; // the picked-up member, or null
        private FormationCharacterVM _cueInside; // member the glide cursor is currently over (for enter/exit cue)
        private bool _wasGliding;           // last frame's glide state (to fire read-on-release)
        private FormationCharacterVM _reviewed; // member last reached by the Comma cycle (Slash jumps here)

        /// <summary>Step the cursor one grid cell (called by the Formation input actions while focused).</summary>
        public void MoveStep(int dx, int dy)
        {
            _cursor = new Vector2(
                Mathf.Clamp(_cursor.x + dx * GridStep, -FieldHalf, FieldHalf),
                Mathf.Clamp(_cursor.y + dy * GridStep, -FieldHalf, FieldHalf));
            Tts.Speak(CellReadout(), interrupt: true);
        }

        // Continuous mode (Shift+WASD), ticked by the screen while the field node is focused: glide the
        // cursor freely (no grid snap), play an
        // enter/exit cue as it crosses a member, and on key release read where it landed. Gliding is too fast
        // to narrate per-frame, so it stays silent on move (the cue carries it) and speaks once on release —
        // the same feel as the exploration cursor's continuous mode (duplicated, not shared). The discrete
        // WASD path is unaffected (it isn't gliding, so this no-ops then).
        public void Tick()
        {
            int ix = (InputManager.Held("formation.glideRight") ? 1 : 0) - (InputManager.Held("formation.glideLeft") ? 1 : 0);
            int iz = (InputManager.Held("formation.glideUp") ? 1 : 0) - (InputManager.Held("formation.glideDown") ? 1 : 0);
            bool gliding = ix != 0 || iz != 0;

            if (gliding)
            {
                if (!_wasGliding) _cueInside = MemberAt(_cursor); // baseline on glide start (no cue this frame)
                var dir = new Vector2(ix, iz).normalized;
                float step = GlideSpeedFeet * Geo.MetresPerFoot * Time.unscaledDeltaTime;
                _cursor = new Vector2(
                    Mathf.Clamp(_cursor.x + dir.x * step, -FieldHalf, FieldHalf),
                    Mathf.Clamp(_cursor.y + dir.y * step, -FieldHalf, FieldHalf));
                var inside = MemberAt(_cursor);
                if (inside != _cueInside) { PlayCue(inside != null); _cueInside = inside; }
            }
            else if (_wasGliding)
            {
                Tts.Speak(CellReadout(), interrupt: true); // released → read where the cursor landed
            }
            _wasGliding = gliding;
        }

        // The cursor-crossed-a-member cue, duplicating the object overlay's enter/exit sound + volume.
        private static void PlayCue(bool enter)
        {
            float vol = (ModSettings.GetSetting<IntSetting>("audio.volumes.object")?.Get() ?? 100) / 100f * OverlayAudio.Master;
            AudioEngines.NAudio.Play2D(Path.Combine(OverlayAudio.Dir, enter ? "object_enter.wav" : "object_exit.wav"), vol);
        }

        /// <summary>Enter: pick up the member at the cursor / drop the held one here (Custom only).</summary>
        public void PickOrDrop()
        {
            var vm = FormationScreen.Vm();
            if (vm == null) return;
            if (!vm.IsCustomFormation) { Tts.Speak(Loc.T("formation.not_editable"), interrupt: true); return; }
            if (_held == null)
            {
                var who = MemberAt(_cursor, requireInteractable: true);
                if (who == null) { Tts.Speak(Loc.T("formation.nothing_here"), interrupt: true); return; }
                _held = who;
                Tts.Speak(Loc.T("formation.picked_up", new { name = who.Unit.CharacterName }), interrupt: true);
            }
            else
            {
                _held.MoveCharacter(_cursor);
                Tts.Speak(Loc.T("formation.placed", new { name = _held.Unit.CharacterName }) + ", " + PositionStr(_cursor),
                    interrupt: true);
                _held = null;
            }
        }

        /// <summary>Comma / Shift+Comma: REVIEW the next/previous member — reads its name + position (relative
        /// to the formation's origin) and plays a positional cue at its location, WITHOUT moving the editing
        /// cursor (Slash jumps the cursor there). Works on Auto too (to hear the layout) and Custom.</summary>
        public void CycleMember(int dir)
        {
            var vm = FormationScreen.Vm();
            if (vm == null) return;
            var members = new List<FormationCharacterVM>();
            foreach (var c in vm.Characters)
                if (c != null && c.Unit != null && c.IsVisible.Value) members.Add(c);
            if (members.Count == 0) { Tts.Speak(Loc.T("formation.no_members"), interrupt: true); _reviewed = null; return; }

            int idx = (_reviewed != null && members.Contains(_reviewed))
                ? Wrap(members.IndexOf(_reviewed) + dir, members.Count)
                : (dir > 0 ? 0 : members.Count - 1); // first / restart → first (next) or last (prev)
            _reviewed = members[idx];
            var off = _reviewed.GetOffset();
            PlayAt(off, "review.wav"); // positional cue at the member's location (relative to origin)
            Tts.Speak(_reviewed.Unit.CharacterName + ", " + PositionStr(off), interrupt: true);
        }

        /// <summary>Slash: move the editing cursor onto the member last reviewed with Comma (mirrors the
        /// exploration "plant the cursor on the review target").</summary>
        public void JumpToReviewed()
        {
            if (_reviewed == null) { Tts.Speak(Loc.T("formation.no_review"), interrupt: true); return; }
            _cursor = _reviewed.GetOffset();
            Tts.Speak(CellReadout(), interrupt: true);
        }

        // A positional cue at a layout offset (relative to origin): stereo-panned by east/west, quieter with
        // distance — the formation field's own placement, so it always goes through NAudio (no world emitter).
        private static void PlayAt(Vector2 off, string file)
        {
            float dist = off.magnitude;
            float refDist = 10f * Geo.MetresPerFoot, panWidth = 10f * Geo.MetresPerFoot;
            float vol = Mathf.Clamp(refDist / (refDist + dist), 0.1f, 1f)
                * ((ModSettings.GetSetting<IntSetting>("audio.volumes.object")?.Get() ?? 100) / 100f) * OverlayAudio.Master;
            // off.x = east, off.y = north — the spatializer pans/ITDs by east and front/back-filters by north.
            AudioEngines.NAudio.PlaySpatial(Path.Combine(OverlayAudio.Dir, file), vol, off.x, off.y, panWidth);
        }

        /// <summary>Ctrl+1..6: grab the Nth party member straight away (start dragging) — so Ctrl+1, then move
        /// the cursor, then Enter places member 1. Pressing the same number again cycles through that member's
        /// owned units (pets / mount), like exploration's select-character ring. Does NOT move the cursor
        /// (only WASD/Slash do). Custom only.</summary>
        public void PickMember(int index)
        {
            var vm = FormationScreen.Vm();
            if (vm == null) return;
            if (!vm.IsCustomFormation) { Tts.Speak(Loc.T("formation.not_editable"), interrupt: true); return; }

            // The member's ring (member, then its draggable pets/mount), mapped to their formation slots.
            var ring = new List<FormationCharacterVM>();
            foreach (var unit in PartyRingUnits(index))
            {
                var c = vm.Characters.Find(x => x != null && x.Unit == unit);
                if (c != null && c.IsInteractable.Value) ring.Add(c);
            }
            if (ring.Count == 0) { Tts.Speak(Loc.T("party.no_member", new { index = index + 1 }), interrupt: true); return; }

            // Re-pressing the same number advances within the ring; a different one (or nothing held) starts
            // at the member.
            int pos = _held != null ? ring.IndexOf(_held) : -1;
            _held = ring[pos >= 0 ? (pos + 1) % ring.Count : 0];
            Tts.Speak(Loc.T("formation.picked_up", new { name = _held.Unit.CharacterName }), interrupt: true);
        }

        // The party member at this slot, then each owned, in-game, controllable unit it owns (pets/mount) —
        // mirrors PartySelection's select-character ring so Ctrl+N cycles the same way here.
        private static List<UnitEntityData> PartyRingUnits(int index)
        {
            var ring = new List<UnitEntityData>();
            var party = Game.Instance?.Player?.PartyCharacters;
            if (party == null || index < 0 || index >= party.Count) return ring;
            var member = party[index].Value;
            if (member == null || member.View == null) return ring;
            ring.Add(member);
            var pets = member.Pets;
            if (pets != null)
                foreach (var pet in pets)
                {
                    var e = pet.Entity;
                    if (e != null && e != member && e.IsInGame && e.IsDirectlyControllable && e.View != null && !ring.Contains(e))
                        ring.Add(e);
                }
            return ring;
        }

        /// <summary>C: jump the cursor to the formation's origin (0, 0).</summary>
        public void CenterCursor()
        {
            _cursor = Vector2.zero;
            Tts.Speak(CellReadout(), interrupt: true);
        }

        private static int Wrap(int i, int n) => ((i % n) + n) % n;

        // The party member at/near the cursor (nearest within the grab radius), or null.
        private static FormationCharacterVM MemberAt(Vector2 at, bool requireInteractable = false)
        {
            var vm = FormationScreen.Vm();
            if (vm == null) return null;
            FormationCharacterVM best = null;
            float bestSq = GrabRadius * GrabRadius;
            foreach (var c in vm.Characters)
            {
                if (c == null || c.Unit == null) continue;
                if (requireInteractable ? !c.IsInteractable.Value : !c.IsVisible.Value) continue;
                float sq = (c.GetOffset() - at).sqrMagnitude;
                if (sq <= bestSq) { bestSq = sq; best = c; }
            }
            return best;
        }

        /// <summary>"&lt;member or empty&gt;, &lt;position&gt;" — what the cursor is over (the field node's value).</summary>
        public string CellReadout()
        {
            var who = MemberAt(_cursor);
            string name = who != null ? who.Unit.CharacterName : Loc.T("formation.empty");
            return name + ", " + PositionStr(_cursor);
        }

        // The offset as "X feet east/west, Y feet north/south" (or "center" near the origin).
        private static string PositionStr(Vector2 off)
        {
            const float eps = 0.001f; // collapse only a truly-zero axis (and the exact centre) — never a real value
            if (Mathf.Abs(off.x) < eps && Mathf.Abs(off.y) < eps) return Loc.T("formation.center");
            var parts = new List<string>(2);
            if (Mathf.Abs(off.x) >= eps)
                parts.Add(Feet(Mathf.Abs(off.x)) + " " + Loc.T(off.x > 0 ? "formation.east" : "formation.west"));
            if (Mathf.Abs(off.y) >= eps)
                parts.Add(Feet(Mathf.Abs(off.y)) + " " + Loc.T(off.y > 0 ? "formation.north" : "formation.south"));
            return string.Join(", ", parts);
        }

        // Feet to 2 decimals — the grid step is ~1.9 ft, so whole-foot rounding (the scanner's FeetStr) would
        // read adjacent cells as 2/4/6 ft and feel jumpy; the editor needs the finer precision.
        private static string Feet(float metres)
            => Loc.T("geo.feet", new
            {
                feet = Geo.Feet(metres).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
            });
    }
}
