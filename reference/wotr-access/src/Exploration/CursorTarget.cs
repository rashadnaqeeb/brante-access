using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// What the world cursor is "inside": the nearest visible unit or interactable whose footprint
    /// contains the cursor. Scenery and markers don't qualify (same set as the object enter/exit cue —
    /// <c>SonarSound != null || IsUnit</c>). This is how Enter (our left click) picks its target — we
    /// resolve from our own cursor + <see cref="WorldModel"/> rather than the game's screen-ray mouse.
    /// </summary>
    internal static class CursorTarget
    {
        private const float LevelGap = 3f; // metres; ignore things on another level

        public static ScanItem Inside()
        {
            if (!Cursor.Has) return null;
            var c = Cursor.Position.Value;
            ScanItem best = null;
            float bestSqr = float.MaxValue;
            foreach (var it in WorldModel.Items)
            {
                if (!it.IsVisible) continue;
                if (!ScanTaxonomy.IsInteractive(it.Primary) && !it.IsUnit) continue; // units + interactables only
                var p = it.Position;
                if (Mathf.Abs(p.y - c.y) > LevelGap) continue;
                if (!it.Contains(c)) continue;                        // cursor inside the actual footprint shape
                float dx = p.x - c.x, dz = p.z - c.z, sqr = dx * dx + dz * dz; // nearest CENTRE wins ties
                if (sqr < bestSqr) { bestSqr = sqr; best = it; }
            }
            return best;
        }
    }
}
