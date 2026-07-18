using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules.Abilities;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Parts;
using Kingmaker.Utility;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// The held touch-spell charge (<see cref="UnitPartTouch"/> — a cast sticky-touch spell whose
    /// delivery hasn't happened yet). Sighted players get a glowing hand and, in turn-based, the
    /// deliver-cursor (the deliver ability is deliberately hidden from the action bar); by ear the
    /// state was invisible — it blocks recasting the spell ("can't cast that now" with uses left)
    /// and, OUT of combat, lingers forever when the auto-delivery was interrupted (combat end clears
    /// it with a slot refund; nothing out of combat ever does). We mirror the sighted deliver-click
    /// on the interact keys (ProxyUnit) and announce the state (PartyStateWatch, the R readout).
    /// </summary>
    internal static class TouchCharge
    {
        /// <summary>The charge held by the unit who'd act on I/Enter right now — the turn-based acting
        /// unit, else the single selected unit. Null when none.</summary>
        public static UnitPartTouch Held(out UnitEntityData holder)
        {
            holder = CombatMode.InTurnBased ? CombatMode.CurrentUnit
                : Game.Instance?.SelectionCharacter?.SingleSelectedUnit;
            var part = holder?.Get<UnitPartTouch>();
            return part != null && part.Ability != null ? part : null;
        }

        /// <summary>Whether the charge may be delivered to this unit — the deliver ability's own
        /// target rules (cures target allies; harm touches target enemies too). An invalid target
        /// falls through to the normal unit interaction.</summary>
        public static bool CanDeliverTo(UnitPartTouch part, UnitEntityData target)
            => target != null && part.Ability.Data.CanTarget(new TargetWrapper(target));

        /// <summary>Deliver the charge — exactly what the game's auto-cast does
        /// (AbilityEffectStickyTouch.Apply): an instant rulebook cast on self, else the held touch
        /// ability's cast command with the charge's cooldown exemption (free-ish when delivered the
        /// round it was cast).</summary>
        public static void Deliver(UnitEntityData holder, UnitPartTouch part, UnitEntityData target)
        {
            if (holder == target)
            {
                Rulebook.Trigger(new RuleCastSpell(part.Ability.Data, new TargetWrapper(target)));
                return;
            }
            var cmd = UnitUseAbility.CreateCastCommand(part.Ability.Data, new TargetWrapper(target));
            cmd.IgnoreCooldown(part.IgnoreCooldownBeforeTime);
            holder.Commands.Run(cmd);
        }
    }
}
