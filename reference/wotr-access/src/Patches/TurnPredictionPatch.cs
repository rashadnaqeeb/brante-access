using HarmonyLib;
using TurnBased.Controllers;

namespace WrathAccess.Patches
{
    /// <summary>
    /// Silences the game's mouse-hover prediction loop (<c>TurnController.UpdateActionPredictions</c>)
    /// during player turns while the mod is enabled. The loop re-runs after EVERY command ends
    /// (<c>HandleUnitCommandDidEnd</c> sets <c>m_NeedNewPredictions</c>) and derives a phantom command
    /// from the PHYSICAL mouse: a simulated click on the hovered unit/object, or — over bare terrain —
    /// a path to the point under the OS cursor, written into the live turn state with
    /// <c>updateActionsState: true</c>. For a mouse player those predictions are continuously
    /// overwritten and the eventual click matches the last one; for a keyboard player the mouse is
    /// parked somewhere arbitrary, so the junk reservation sits until the next command's start/end
    /// conversion (<c>CombatAction.UpdateCurrentStates</c>) turns it into REAL activity losses — the
    /// long-standing symptom was every spell greying out after any movement. With the loop dead, the
    /// mod is the sole author of predictions: movement via <c>CombatMode.ComputePath</c> (commit mode),
    /// actions via <c>CombatMode.NoteIssuedCommand</c> after each command we issue.
    /// </summary>
    [HarmonyPatch(typeof(TurnController), "UpdateActionPredictions")]
    internal static class TurnPredictionPatch
    {
        private static readonly System.Reflection.FieldInfo NeedNewPredictions =
            AccessTools.Field(typeof(TurnController), "m_NeedNewPredictions");

        private static bool Prefix(TurnController __instance)
        {
            if (!Main.Enabled) return true;
            var unit = __instance.SelectedUnit;
            if (unit == null || !unit.IsDirectlyControllable) return true; // AI turns keep vanilla flow
            NeedNewPredictions.SetValue(__instance, false); // consume the request so the loop never spins
            return false;
        }
    }
}
