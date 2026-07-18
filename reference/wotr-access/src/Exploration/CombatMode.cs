using System.Collections.Generic; // List
using Kingmaker; // Game
using Kingmaker.Blueprints.Root.Strings; // UIStrings (game-localized TB texts)
using Kingmaker.EntitySystem.Entities; // UnitEntityData
using Kingmaker.PubSubSystem; // EventBus, IMessageModalUIHandler
using Kingmaker.TurnBasedMode; // PathVisualizer, ActionsState
using Kingmaker.TurnBasedMode.Controllers; // CombatAction (prediction state machine)
using Kingmaker.UI; // MessageModalBase
using Kingmaker.UnitLogic.Commands; // UnitUseAbility / UnitAttack / UnitMoveTo (live action)
using Kingmaker.UnitLogic.Commands.Base; // UnitCommand
using TurnBased.Controllers; // CombatController, TurnController
using UnityEngine; // Vector3

namespace WrathAccess.Exploration
{
    /// <summary>
    /// Turn-based combat helpers. Commands already execute correctly while it's a unit's turn (the game ticks
    /// the current turn in Default mode); the player just needs to know <i>whose</i> turn it is, since they
    /// can't see the initiative tracker.
    /// </summary>
    internal static class CombatMode
    {
        public static bool InTurnBased => CombatController.IsInTurnBasedCombat();

        /// <summary>The unit whose turn it is (null between turns).</summary>
        public static UnitEntityData CurrentUnit => CombatController.SelectedUnit;

        // TurnController.GetActionsStates(unit) is private; we need it to drive the game's command pathfinding.
        private static readonly System.Reflection.MethodInfo GetActionsStatesMethod =
            HarmonyLib.AccessTools.Method(typeof(TurnController), "GetActionsStates");

        /// <summary>
        /// Compute, with the GAME's own pathfinding, where the current unit would end up walking toward
        /// <paramref name="point"/> this turn, and return that endpoint (null if no path). We can't rely on
        /// the game's turn-based path preview because it's driven by the OS mouse / hover predictions, which
        /// our keyboard cursor doesn't feed. Instead we point the unit's <c>ActionsState</c> at our cursor and
        /// call <c>PathVisualizer.CalculatePathForCommand</c> — which runs synchronously through the unit's own
        /// agent (<c>AgentASP.FindPath</c> + <c>BlockUntilCalculated</c>) and populates the current path. The
        /// move/attack we then issue walks that path (the turn-based execution clamps it to the budget).
        /// </summary>
        public static Vector3? PathEndpointToward(Vector3 point, float approachRadius = 0.3f)
        {
            var pts = ComputePath(point, approachRadius, updateActionsState: true); // commit path: a real move follows
            if (pts == null || pts.Count == 0) return null;
            return pts[pts.Count - 1];
        }

        /// <summary>Drop any movement RESERVATIONS our path computations wrote into the acting unit's
        /// turn state (SetPrediction WillBeUsed/WillBeLost). The game's completion accounting
        /// (UpdateCurrentStates) converts lingering predictions into REAL activity losses when the
        /// next command finishes — a stale reservation from a refused order would silently forfeit
        /// the standard action's attacks and abilities. Call after every refusal that follows a
        /// TryApproach/PathEndpointToward whose command was never issued.</summary>
        public static void CancelPathReservation()
        {
            var turn = Game.Instance?.TurnBasedCombatController?.CurrentTurn;
            var cu = CurrentUnit;
            if (turn == null || cu == null || GetActionsStatesMethod == null) return;
            (GetActionsStatesMethod.Invoke(turn, new object[] { cu }) as ActionsState)?.Clear();
        }

        /// <summary>
        /// Path facts for an announcement: the walk length to <paramref name="point"/>, how far short of it
        /// the path ends (a partial path to the closest reachable node = the spot itself is unreachable),
        /// and the current unit's remaining movement this turn — all in metres. False if no path at all.
        /// </summary>
        public static bool TryPathInfo(Vector3 point, out float lengthMeters, out float endGapMeters,
            out float moveActionMeters, out float totalMeters)
        {
            lengthMeters = endGapMeters = moveActionMeters = totalMeters = 0f;
            var cu = CurrentUnit;
            // PREVIEW ONLY — updateActionsState: false is load-bearing. With true, every cursor stop
            // would write movement RESERVATIONS (SetPrediction WillBeUsed) into the live turn state,
            // and the game's completion accounting converts stale ones into REAL losses: sweep the
            // cursor, take one step, and the standard action's attacks/abilities read Lost — every
            // spell greyed for the turn (user repro, 2026-07-08).
            var pts = ComputePath(point, 0.3f, updateActionsState: false);
            if (cu == null || pts == null || pts.Count == 0) return false;
            for (int i = 1; i < pts.Count; i++) lengthMeters += Vector3.Distance(pts[i - 1], pts[i]);
            var end = pts[pts.Count - 1];
            float dx = end.x - point.x, dz = end.z - point.z;
            endGapMeters = Mathf.Sqrt(dx * dx + dz * dz);
            // Two budgets, matching the game's path break markers: the move action alone, and the maximum
            // with the standard action converted to a second move ("can I get there at all this turn").
            moveActionMeters = cu.CombatState.TBM.GetRemainingMovementRange(total: false, singleActionMove: false);
            totalMeters = cu.CombatState.TBM.GetRemainingMovementRange(total: true, singleActionMove: false);
            return true;
        }

        /// <summary>
        /// Turn-based: compute (and populate, so the issued command walks it) the approach to within
        /// <paramref name="reach"/> of <paramref name="targetPos"/>, reporting the path's walk length and the
        /// current unit's remaining MOVE-action range — both metres. Returns false when there's no movement
        /// path to walk (already within reach, or unreachable); callers treat that as "no move needed / can't
        /// move", and only act on the returned lengths when it's true.
        /// </summary>
        public static bool TryApproach(Vector3 targetPos, float reach, out float walkMeters, out float moveActionMeters)
        {
            walkMeters = moveActionMeters = 0f;
            var cu = CurrentUnit;
            if (cu == null) return false;
            var pts = ComputePath(targetPos, reach, updateActionsState: true); // commit path: the approach command walks it
            moveActionMeters = cu.CombatState.TBM.GetRemainingMovementRange(total: false, singleActionMove: false);
            if (pts == null || pts.Count == 0) return false; // already in reach, or no route at all
            // ENDPOINT test — the same check the game's click validation applies (PathVisualizer.
            // IsCurrentPathForPoint): an A* approach path completes at the CLOSEST ATTAINABLE node when
            // no in-reach node is available (allies crowding a melee target, clearance-tight doorways,
            // reach beyond the 6x-speed path cap) — its LENGTH can fit the budget while its END still
            // stands short of acting range. Walking it strands the unit doing nothing, silently, and
            // parks a pending order for next turn. Short-of-reach endpoints report as unreachable.
            var end = pts[pts.Count - 1];
            if (Owlcat.Runtime.Core.Utils.GeometryUtils.MechanicsDistance(end, targetPos) > reach) return false;
            for (int i = 1; i < pts.Count; i++) walkMeters += Vector3.Distance(pts[i - 1], pts[i]);
            return true;
        }

        private static System.Collections.Generic.List<Vector3> ComputePath(Vector3 point, float approachRadius, bool updateActionsState)
        {
            if (!InTurnBased) return null;
            var turn = Game.Instance?.TurnBasedCombatController?.CurrentTurn;
            var cu = CurrentUnit;
            var pv = PathVisualizer.Instance;
            if (turn == null || cu == null || cu.View == null || pv == null || GetActionsStatesMethod == null) return null;

            var actionsState = GetActionsStatesMethod.Invoke(turn, new object[] { cu }) as ActionsState;
            if (actionsState == null) return null;
            // Compute from a CLEAN slate, the way the game's own prediction loop does (it calls
            // ActionsState.Clear() before every recompute). Without this, a lingering WillBeUsed on the
            // Move action — which completed partial moves NEVER clear — makes UpdateVisualPath's leg
            // attribution charge the whole walk to the STANDARD action, whose WillBeUsed prediction
            // cascades "ability will be lost" and greys every spell at the next command conversion.
            if (updateActionsState) actionsState.Clear();
            actionsState.ApproachPoint = point;
            actionsState.ApproachRadius = approachRadius; // 0.3 ~= exact (move); the attack reach for an approach
            actionsState.NeedLOS = false;
            actionsState.IgnoreBlockerId = 0; // don't inherit a stale hover value (vanilla commands pass 0 too)
            pv.CalculatePathForCommand(cu, actionsState, updateActionsState);

            var path = pv.CurrentPathForUnit(cu.View);
            if (path == null || path.vectorPath == null || path.vectorPath.Count == 0) return null;
            return path.vectorPath;
        }

        /// <summary>
        /// Write the turn-state predictions for a command OUR flow just issued — the exact writes the
        /// game's mouse-prediction loop performs for a hovered command (TurnController.
        /// UpdateActionPredictions, "Handle Selected Command"). That loop is suppressed while the mod
        /// runs (TurnPredictionPatch: it derives phantom commands from the parked OS mouse and poisons
        /// the turn state), so without this call nothing would mark the standard action Used when a
        /// cast or attack completes and the action bar would stay optimistically enabled. Call after
        /// every successful command issue in turn-based; movement predictions for the approach walk are
        /// already written by ComputePath (commit mode) — plain moves need nothing more.
        /// </summary>
        public static void NoteIssuedCommand(UnitEntityData unit)
        {
            if (!InTurnBased || unit == null || unit != CurrentUnit) return;
            var turn = Game.Instance?.TurnBasedCombatController?.CurrentTurn;
            if (turn == null || GetActionsStatesMethod == null) return;
            var s = GetActionsStatesMethod.Invoke(turn, new object[] { unit }) as ActionsState;
            if (s == null) return;
            var cmd = unit.Commands.GetCommand(UnitCommand.CommandType.Standard)
                ?? unit.Commands.GetCommand(UnitCommand.CommandType.Move)
                ?? unit.Commands.GetCommand(UnitCommand.CommandType.Swift)
                ?? unit.Commands.GetCommand(UnitCommand.CommandType.Free);
            if (cmd == null || cmd is UnitMoveTo) return;

            var usage = CombatAction.UsageType.None;
            var activity = CombatAction.ActivityType.Move;
            bool fullRound = false;
            Kingmaker.UI.IUIDataProvider ability = null;
            if (cmd is UnitUseAbility ua)
            {
                fullRound = cmd.IsFullRoundAbility();
                ability = ua.Ability;
                usage = ua.Ability.SourceItem == null
                    ? CombatAction.UsageType.UseAbility : CombatAction.UsageType.UseItem;
                activity = CombatAction.ActivityType.Ability;
                // A sticky-touch cast leaves a held charge whose delivery is a free action.
                var touch = unit.Get<Kingmaker.UnitLogic.Parts.UnitPartTouch>();
                if (ua.Ability.Blueprint.StickyTouch != null
                    && (touch == null || touch.Ability.Blueprint != ua.Ability.Blueprint))
                    s.Free.SetPrediction(CombatAction.UsageType.TouchDeliver,
                        CombatAction.ActivityType.Ability, CombatAction.ActivityState.WillBeUsed, ability);
            }
            else if (cmd is UnitAttack)
            {
                fullRound = cmd.IsFullAttack();
                usage = fullRound ? CombatAction.UsageType.FullAttack : CombatAction.UsageType.SingleAttack;
                activity = CombatAction.ActivityType.Attack;
                if (cmd.IsSpellCombatAttack() && s.Move.CanUse)
                    s.Move.SetPrediction(CombatAction.UsageType.SpellCombatAttack,
                        CombatAction.ActivityType.Attack, CombatAction.ActivityState.WillBeUsed);
            }
            else if (cmd is UnitInteractWithObject || cmd is UnitInteractWithUnit || cmd is UnitLootUnit)
            {
                usage = CombatAction.UsageType.InteractObject;
                activity = CombatAction.ActivityType.Ability;
            }

            if (fullRound)
            {
                bool surprising = Game.Instance.TurnBasedCombatController.IsSurprising(unit);
                if ((s.Move.CanUse || surprising) && s.Standard.CanUse)
                {
                    s.Move.SetPrediction(usage, activity, CombatAction.ActivityState.WillBeUsed, ability);
                    s.Standard.SetPrediction(usage, activity, CombatAction.ActivityState.WillBeUsed, ability);
                }
                else s.Overused = true;
            }
            else
            {
                var type = cmd.Type;
                if (cmd.IsIgnoreCooldown || cmd.IsFreeTouch()) type = UnitCommand.CommandType.Free;
                else if (type == UnitCommand.CommandType.Move && !s.Move.CanUse && s.Standard.CanUse)
                    type = UnitCommand.CommandType.Standard;
                s.Use(type, usage, activity, ability, null);
            }
        }

        /// <summary>
        /// The unit cursor-relative things (recenter, distance/bearing readouts, scan origin) should anchor
        /// to. In turn-based that's the unit whose turn it is — so "c" recenters on the acting unit and you
        /// navigate/attack/move relative to it, not the main character. Null outside turn-based (callers fall
        /// back to the main character).
        /// </summary>
        public static UnitEntityData ReferenceUnit => InTurnBased ? CurrentUnit : null;

        // Announce whose turn it is when the active unit changes — the cue to act (your unit) or wait (an
        // enemy) — and when a player unit's turn ENDS. The end cue watches the game's own end-turn signal:
        // every end path (Space/ForceToEnd, auto-end on exhausted actions, AI) goes through
        // TurnController.End() → Status = Ended, so we track the live turn object and fire on that
        // transition (the ITurnBasedUnitTurnEndedHandler EventBus interface is never raised by the game).
        // Enemy turn-ends stay silent — the next "X's turn" cue already covers them. Ticked from Main.OnUpdate.
        private static UnitEntityData _lastTurn;
        private static TurnController _trackedTurn;
        private static UnitEntityData _trackedUnit;
        public static void TickTurn()
        {
            if (!InTurnBased) { _lastTurn = null; _trackedTurn = null; _trackedUnit = null; return; }

            var turn = Game.Instance?.TurnBasedCombatController?.CurrentTurn;
            // The tracked turn finished (reached Ended, or was disposed/replaced between frames).
            if (_trackedTurn != null && (turn != _trackedTurn || _trackedTurn.Status == TurnController.TurnStatus.Ended))
            {
                if (_trackedUnit != null && _trackedUnit.IsDirectlyControllable)
                    Tts.Speak(Message.Localized("ui", "combat.turn_ended", new { name = _trackedUnit.CharacterName }).Resolve());
                _trackedTurn = null;
                _trackedUnit = null;
            }
            if (turn != null && _trackedTurn == null && turn.Status != TurnController.TurnStatus.Ended)
            {
                _trackedTurn = turn;
                _trackedUnit = turn.Rider; // the turn's owner (stable), not the selection-dependent SelectedUnit
            }

            var cur = CurrentUnit;
            if (cur == _lastTurn) return;
            _lastTurn = cur;
            if (cur != null)
            {
                // Two invisible-by-ear states get surfaced with the turn cue, because both make the
                // unit "act on its own" the moment its turn starts: an AI-controlled party member
                // (Ctrl+D) has its whole turn played by the game brain, and a PENDING ORDER from an
                // earlier turn (multi-turn orders are vanilla TB design) resumes automatically.
                string key = cur.IsPlayersEnemy ? "combat.turn_enemy"
                    : !cur.IsDirectlyControllable ? "combat.turn_ai"
                    : "combat.turn";
                Tts.Speak(Message.Localized("ui", key, new { name = cur.CharacterName }).Resolve());
                if (!cur.IsPlayersEnemy && cur.IsDirectlyControllable && !cur.Commands.Empty)
                {
                    var action = DescribeAction(cur);
                    Tts.Speak(action != null
                        ? Message.Localized("ui", "combat.pending_order", new { action }).Resolve()
                        : Message.Localized("ui", "combat.pending_order_generic").Resolve());
                }
            }
        }

        /// <summary>
        /// R: the acting unit's action economy + remaining movement. Availability comes from the game's own
        /// checks (the same Has*Action calls its turn logic uses); movement is the remaining range with both
        /// actions spent on moving — the same budget the path verdict compares against. Gated to focus-mode
        /// exploration like the scanner keys.
        /// </summary>
        public static void AnnounceStatus()
        {
            if (!FocusMode.Active) return;
            var screen = WrathAccess.Screens.ScreenManager.Current;
            if (screen == null || screen.Key != "ctx.ingame") return;
            // Real-time-with-pause: report what the selected character(s) are doing (turn-based instead
            // gives the acting unit's action economy below).
            if (!InTurnBased) Tts.Speak(SelectedActionsLine(), interrupt: true);
            else
            {
                var line = StatusLine();
                Tts.Speak(line ?? Message.Localized("ui", "combat.no_active_turn").Resolve(), interrupt: true);
            }
            // A held touch charge shapes what the unit can do (its spell won't recast; the interact
            // keys deliver) — part of the status, queued after the main line.
            var touch = TouchCharge.Held(out _);
            if (touch != null)
                Tts.Speak(Loc.T("touch.holding_status", new { spell = touch.Ability.Data.Name }));
        }

        // "{name}, {action}" for each selected, controllable character (idle when they've nothing queued),
        // joined for the whole selection — so one press reads what everyone you've selected is up to.
        private static string SelectedActionsLine()
        {
            var units = Game.Instance?.SelectionCharacter?.SelectedUnits;
            var parts = new List<string>();
            if (units != null)
                foreach (var u in units)
                    if (u != null && u.IsDirectlyControllable)
                        parts.Add(Loc.T("combat.unit_action",
                            new { name = u.CharacterName, action = DescribeAction(u) ?? Loc.T("combat.idle") }));
            return parts.Count > 0 ? string.Join(". ", parts) : Loc.T("party.none_selected");
        }

        /// <summary>What the unit is doing right now, from its live commands — "casting X" / "using X" /
        /// "attacking Y" / "moving" — or null when idle. The same model the game's cast overtip reads;
        /// shared by the scanner's action announcement and the RTWP status key.</summary>
        public static string DescribeAction(UnitEntityData unit)
        {
            var cmds = unit?.Commands;
            if (cmds == null) return null;
            var ability = cmds.UnitUseAbility;
            if (ability != null && ability.Ability != null && ability.Ability.Blueprint != null)
            {
                var bp = ability.Ability.Blueprint;
                return Loc.T(bp.IsSpell ? "unit.action_casting" : "unit.action_using", new { name = bp.Name });
            }
            if (cmds.Standard is UnitAttack atk)
                return atk.TargetUnit != null
                    ? Loc.T("unit.action_attacking_target", new { name = atk.TargetUnit.CharacterName })
                    : Loc.T("unit.action_attacking");
            foreach (var c in cmds.Raw)
                if (c is UnitMoveTo || c is UnitMoveAlongPath || c is UnitMoveContiniously)
                    return Loc.T("unit.action_moving");
            return null;
        }

        /// <summary>The acting unit's action economy + movement budgets as one spoken line (null when no
        /// active turn). Shared by the R status key and the HUD Turn panel's status element.</summary>
        public static string StatusLine()
        {
            if (!InTurnBased) return null;
            var turn = Game.Instance?.TurnBasedCombatController?.CurrentTurn;
            var cu = CurrentUnit;
            if (turn == null || cu == null) return null;

            string State(bool available) => Message.Localized("ui", available ? "combat.available" : "combat.used").Resolve();
            // Two layers must agree for the standard action to actually be usable: the time/cooldown
            // layer (HasStandardAction) AND the activity state machine the action bar gates on
            // (TurnState.Standard.CanUseAbility — e.g. Lost after converting the standard into a second
            // move). Reading only the cooldown said "standard available" while every spell was greyed.
            var states = GetActionsStatesMethod?.Invoke(turn, new object[] { cu }) as ActionsState;
            bool standardOk = cu.HasStandardAction() && (states == null || states.Standard.CanUseAbility);
            var sb = new System.Text.StringBuilder(Message.Localized("ui", "combat.status_actions", new
            {
                name = cu.CharacterName,
                standard = State(standardOk),
                move = State(cu.HasMoveAction()),
                swift = State(cu.HasSwiftAction()),
            }).Resolve());
            // Two numbers, like the game's path break markers: the move action alone, and (when different)
            // the maximum with the standard action converted to a second move.
            int moveFt = Mathf.RoundToInt(
                cu.CombatState.TBM.GetRemainingMovementRange(total: false, singleActionMove: false) / Geo.MetresPerFoot);
            int totalFt = Mathf.RoundToInt(
                cu.CombatState.TBM.GetRemainingMovementRange(total: true, singleActionMove: false) / Geo.MetresPerFoot);
            sb.Append(", ").Append(Message.Localized("ui", "combat.movement_remaining", new { feet = moveFt }).Resolve());
            if (totalFt > moveFt) sb.Append(", ").Append(Message.Localized("ui", "combat.with_standard", new { feet = totalFt }).Resolve());
            if (turn.HasFiveFootStep(cu)) sb.Append(", ").Append(Message.Localized("ui", "combat.five_foot_step").Resolve());
            return sb.ToString();
        }

        // ---- HUD Turn panel controls ----

        private static TurnController Turn() => Game.Instance?.TurnBasedCombatController?.CurrentTurn;

        /// <summary>End turn — the game's own End Turn button is literally PauseBind() (which in turn-based
        /// means end-turn / speed-up / dismiss the start window, with all the game's guards).</summary>
        public static void EndTurn()
        {
            if (!InTurnBased) return;
            Game.Instance?.PauseBind();
        }

        /// <summary>The five-foot-step movement mode is currently engaged for the acting unit.</summary>
        public static bool FiveFootEngaged
        {
            get { var t = Turn(); var cu = CurrentUnit; return t != null && cu != null && t.GetEnabledFiveFootStep(cu); }
        }

        /// <summary>A five-foot step is still available to the acting unit this turn.</summary>
        public static bool FiveFootAvailable
        {
            get { var t = Turn(); var cu = CurrentUnit; return t != null && cu != null && t.HasFiveFootStep(cu); }
        }

        /// <summary>Toggle five-foot-step mode — exactly the game's panel button
        /// (PredictionPanelBaseView.EnableFiveFoot: flip ModifyMovementLimit).</summary>
        public static void ToggleFiveFoot()
        {
            var t = Turn();
            if (t == null) return;
            t.ModifyMovementLimit(!t.ModifyMovementLimitValue);
            Tts.Speak(Message.Localized("ui", FiveFootEngaged ? "turn.five_foot_on" : "turn.five_foot_off").Resolve(),
                interrupt: true);
        }

        /// <summary>
        /// Delay the acting unit's turn until after <paramref name="target"/> — mirroring the game's
        /// initiative-tracker flow (InitiativeTrackerBaseView.DelayInitiative): gated by CanDelay; a
        /// first-use confirmation and a "this skips into the next round" confirmation are raised through
        /// the game's OWN message modal (game-localized texts; our MessageModalScreen reads it).
        /// </summary>
        public static void DelayAfter(UnitEntityData target)
        {
            var turn = Turn();
            if (turn == null || !turn.CanDelay())
            {
                Tts.Speak(Message.Localized("ui", "turn.cant_delay").Resolve(), interrupt: true);
                return;
            }
            if (target == null || target == turn.Rider || target == CurrentUnit)
            {
                Tts.Speak(Message.Localized("ui", "turn.delay_self").Resolve(), interrupt: true);
                return;
            }

            var ui = Game.Instance.Player.UISettings;
            if (!ui.DelayNotificatioinSeen) // (the game's own property name, typo included)
            {
                Confirm(UIStrings.Instance.TurnBasedTexts.ConfirmDelay, () =>
                {
                    ui.DelayNotificatioinSeen = true;
                    DoDelay(turn, target);
                });
            }
            else if (IsSkipRound(target))
            {
                Confirm(UIStrings.Instance.TurnBasedTexts.ConfirmRoundDelay, () => DoDelay(turn, target));
            }
            else
            {
                DoDelay(turn, target);
            }
        }

        private static void DoDelay(TurnController turn, UnitEntityData target)
        {
            turn.DelayInitiaive(target); // the game's method (typo included)
            UiSound.Play(UISoundType.DelayTurn);
            Tts.Speak(Message.Localized("ui", "turn.delayed", new { name = target.CharacterName }).Resolve());
        }

        // The game's IsSkipRound: the target sits EARLIER in the sorted order than us, so acting after
        // them means waiting into the next round.
        private static bool IsSkipRound(UnitEntityData target)
        {
            var turn = Turn();
            var tb = Game.Instance?.TurnBasedCombatController;
            if (turn == null || tb == null) return false;
            int me = -1, tgt = -1, i = 0;
            foreach (var u in tb.SortedUnits)
            {
                if (u == turn.Rider) me = i;
                if (u == target) tgt = i;
                i++;
            }
            return me > tgt;
        }

        private static void Confirm(string text, System.Action onYes)
        {
            EventBus.RaiseEvent(delegate(IMessageModalUIHandler w)
            {
                w.HandleOpen(text, MessageModalBase.ModalType.Dialog,
                    delegate(MessageModalBase.ButtonType button)
                    {
                        if (button == MessageModalBase.ButtonType.Yes) onYes();
                    });
            });
        }
    }
}
