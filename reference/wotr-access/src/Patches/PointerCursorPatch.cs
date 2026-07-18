using System.Reflection;
using HarmonyLib;
using Kingmaker;
using Kingmaker.Controllers.Clicks;
using WrathAccess.Exploration;
using WrathAccess.Exploration.Overlays;

namespace WrathAccess.Patches
{
    /// <summary>
    /// Makes our virtual cursor the world-pointer authority. The game's entire pointer-driven pipeline reads
    /// <c>PointerController.WorldPosition</c> — most importantly the turn-based path preview/clamp
    /// (<c>PathVisualizer</c>, which the turn controller then forces onto move/attack commands), plus ground
    /// moves, attack-approach, and ability AoE/targeting. That world position is normally raycast from the OS
    /// mouse every frame in <c>PointerController.Tick</c>. A blind player drives our keyboard cursor instead,
    /// so the mouse-based point is meaningless — which is why turn-based moves instant-completed (empty path)
    /// and distant attacks never approached.
    ///
    /// This postfix overwrites the default pointer's world position with our cursor's whenever our nav cursor
    /// owns input (<see cref="OverlayManager.Active"/> and a cursor is placed). With it, the game's own
    /// commands path to where our cursor actually is, so move-to-cursor and attack-approach work in
    /// turn-based without us reimplementing the pathing/budget logic.
    /// </summary>
    [HarmonyPatch(typeof(PointerController), nameof(PointerController.Tick))]
    internal static class PointerCursorPatch
    {
        private static readonly PropertyInfo WorldPos =
            AccessTools.Property(typeof(PointerController), nameof(PointerController.WorldPosition));

        private static void Postfix(PointerController __instance)
        {
            if (WorldPos == null) return;
            if (!OverlayManager.Active || !WrathAccess.Exploration.Cursor.Has) return;
            // Only the default pointer feeds PathVisualizer / the click handlers; leave any others alone.
            if (__instance != Game.Instance?.DefaultPointerController) return;
            WorldPos.SetValue(__instance, WrathAccess.Exploration.Cursor.Position.Value);
        }
    }

    /// <summary>
    /// <c>PointerController.InGui</c> is true whenever the OS mouse is over a UI element
    /// (<c>EventSystem.IsPointerOverGameObject()</c>). With a blind player the mouse sits parked over the
    /// HUD, so it's permanently true — and <c>PathVisualizer</c> clears the turn-based path whenever it's
    /// true, which is why no move path was ever produced. Since our cursor (not the mouse) is the pointer
    /// authority, report "not in GUI" while our nav cursor owns input so the world-pointer pipeline runs.
    /// </summary>
    [HarmonyPatch(typeof(PointerController), nameof(PointerController.InGui), MethodType.Getter)]
    internal static class PointerInGuiPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (OverlayManager.Active && WrathAccess.Exploration.Cursor.Has) __result = false;
        }
    }
}
