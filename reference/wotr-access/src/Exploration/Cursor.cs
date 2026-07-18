using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// The one virtual cursor — a single world point that the scanner and every overlay share. The
    /// scanner plants it on a listed thing (Home); the tile-view overlay walks it across the grid; and
    /// move-to-cursor (Backspace) sends the party wherever it currently sits, no matter which tool put it
    /// there. Keeping it shared is what lets you browse tiles and then walk to the one you're on.
    /// </summary>
    internal static class Cursor
    {
        public static Vector3? Position { get; private set; }
        public static bool Has => Position.HasValue;
        public static void Set(Vector3 p) => Position = p;
        public static void Clear() => Position = null;
    }
}
