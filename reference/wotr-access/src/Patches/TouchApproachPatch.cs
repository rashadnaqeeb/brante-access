using System.Collections.Generic;
using HarmonyLib;
using Kingmaker.UnitLogic.Commands;

namespace WrathAccess.Patches
{
    /// <summary>
    /// Lets a turn-based TOUCH cast issued by OUR targeting flow walk into touch range like any other
    /// ability. Vanilla forces <c>ApproachRadius = float.MaxValue</c> for touch abilities on EVERY
    /// approach tick (<c>UnitUseAbility.OnTickApproaching</c>), which turns "heal my distant ally"
    /// into "cast in place, hold the charge, deliver later" — the only feedback is a cursor-mode
    /// change and a buff icon, both invisible to a blind player. Commands registered via
    /// <see cref="Enable"/> get the real approach distance re-applied AFTER the game's override runs;
    /// unregistered commands (mouse play, AI, held-charge deliveries) keep vanilla behavior. The
    /// caller pre-checks that the walk fits the MOVE action, so the approach never eats the standard
    /// action the cast itself needs.
    /// </summary>
    [HarmonyPatch(typeof(UnitUseAbility), "OnTickApproaching")]
    internal static class TouchApproachPatch
    {
        private static readonly HashSet<UnitUseAbility> _approach = new HashSet<UnitUseAbility>();

        /// <summary>Opt a just-issued touch cast into approaching its target.</summary>
        public static void Enable(UnitUseAbility cmd)
        {
            if (cmd != null) _approach.Add(cmd);
        }

        private static void Postfix(UnitUseAbility __instance)
        {
            if (_approach.Count == 0) return;
            _approach.RemoveWhere(c => c == null || c.IsFinished); // commands are transient — never leak
            if (!_approach.Contains(__instance)) return;

            var target = __instance.Target != null ? __instance.Target.Unit : null;
            if (target == null || __instance.Ability == null) return;
            float d = __instance.Ability.GetApproachDistance(target);
            __instance.ApproachRadius = d;
            if (__instance.MountCommand != null) __instance.MountCommand.ApproachRadius = d;
        }
    }
}
