using WrathAccess.Input;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>The held arrow keys of a cursor slot as one combined vector (primary = plain arrows,
    /// secondary = Shift+arrows). Both movement styles poll this, so held diagonals (e.g. Up+Right)
    /// move the cursor along the combined direction instead of zigzagging per-key.</summary>
    internal static class CursorKeys
    {
        public static void HeldVector(MovementSlot slot, out int dx, out int dz)
        {
            bool primary = slot == MovementSlot.Primary;
            dx = 0; dz = 0;
            if (InputManager.Held(primary ? "explore.cursorUp" : "explore.secondaryUp")) dz += 1;       // +Z = north
            if (InputManager.Held(primary ? "explore.cursorDown" : "explore.secondaryDown")) dz -= 1;
            if (InputManager.Held(primary ? "explore.cursorRight" : "explore.secondaryRight")) dx += 1; // +X = east
            if (InputManager.Held(primary ? "explore.cursorLeft" : "explore.secondaryLeft")) dx -= 1;
        }
    }
}
