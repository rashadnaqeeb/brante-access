using Kingmaker.EntitySystem.Entities; // UnitEntityData
using Kingmaker.Inspect; // InspectUnitsHelper
using Kingmaker.UI.MVVM._VM.Tooltip.Utils; // TooltipHelper

namespace WrathAccess.Exploration
{
    /// <summary>
    /// Opens the game's unit Inspect window (caught by <see cref="WrathAccess.Screens.InspectScreen"/>) for a
    /// unit — but ONLY when that unit actually has an inspect, gated by the game's own
    /// <see cref="InspectUnitsHelper.IsInspectAllow"/> (the same check right-click inspect uses). Two entry
    /// points: the scanner's review-cursor unit, and the unit the movement cursor is over. When there's no
    /// inspectable unit, it just says so rather than opening an empty window.
    /// </summary>
    internal static class Inspect
    {
        /// <summary>Inspect the scanner's review-cursor unit.</summary>
        public static void Review() => Open(ReviewUnit());

        /// <summary>Inspect the unit the movement cursor is over.</summary>
        public static void AtCursor() => Open(CursorUnit());

        private static void Open(UnitEntityData unit)
        {
            if (unit == null || !InspectUnitsHelper.IsInspectAllow(unit))
            {
                Tts.Speak(Loc.T("inspect.none"), interrupt: true);
                return;
            }
            TooltipHelper.ShowInspectTooltip(unit); // → InspectWindowVM, caught by InspectScreen
        }

        // The scanner's review target, when it's on a unit (same source the review-unit buffer uses).
        private static UnitEntityData ReviewUnit()
        {
            var item = Scanner.Reviewed;
            return item != null && item.IsUnit ? item.TargetUnit : null;
        }

        // The visible unit whose footprint contains the movement cursor (nearest centre if several overlap).
        // The movement cursor falls back to the player when it hasn't been placed, mirroring the overlay.
        private static UnitEntityData CursorUnit()
        {
            var p = Cursor.Has ? Cursor.Position.Value : Overlays.Cursor.PlayerPosition;
            UnitEntityData best = null;
            float bestCentre = float.MaxValue;
            foreach (var it in WorldModel.Items)
            {
                if (!it.IsUnit || !it.IsVisible) continue;
                var u = it.TargetUnit;
                if (u == null || !it.Contains(p)) continue;
                float c = Geo.Distance(p, it.Position);
                if (c < bestCentre) { bestCentre = c; best = u; }
            }
            return best;
        }
    }
}
