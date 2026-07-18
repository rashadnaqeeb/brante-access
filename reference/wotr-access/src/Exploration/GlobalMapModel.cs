using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.Armies; // ArmyFaction
using Kingmaker.Globalmap.Blueprints;
using Kingmaker.Globalmap.State; // GlobalMapArmyState
using Kingmaker.Globalmap.View;
using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// Read-only facade over the world map (<see cref="GlobalMapView"/>) — the world-map analogue of
    /// <see cref="WorldModel"/>. Exposes the party (traveler) position and the map's points categorised by
    /// type. The map lays its points out on the same XZ plane areas use (Y is flat), so <see cref="Geo"/>'s
    /// bearing/distance carry over directly. Armies, edges, and live diffing arrive in later increments.
    /// </summary>
    internal static class GlobalMapModel
    {
        public static bool Active
        {
            get
            {
                var g = Game.Instance;
                return g != null && g.RootUiContext != null && g.RootUiContext.IsGlobalMap
                    && GlobalMapView.Instance != null;
            }
        }

        /// <summary>The world map is in its PURE interactive mode — the global map is the CURRENT game mode,
        /// not sitting under a rest / dialog / book-event / battle overlay. (Those keep GlobalMap in the mode
        /// stack — <c>IsGlobalMap</c> stays true — but aren't the current mode.) The movement cursor and the
        /// travel-Continue must only act here: under an overlay the overlay owns input, and "resuming travel"
        /// just restarts the journey that re-fires the event → the infinite loop.</summary>
        public static bool Interactive
            => Game.Instance != null && Game.Instance.CurrentMode == Kingmaker.GameModes.GameModeType.GlobalMap;

        /// <summary>Travel is paused MID-JOURNEY awaiting a Continue — the game stopped the party (a discovery
        /// / event) and the next move resumes it. Enter on the cursor resumes via
        /// <see cref="GlobalMapActions.ResumeTravel"/>. Gated on <see cref="Interactive"/>: travel is also
        /// "paused" during a rest / book event, but the answer there is the overlay, not Continue.</summary>
        public static bool TravelPaused
        {
            get
            {
                if (!Interactive) return false;
                var c = Game.Instance.GlobalMapController;
                return c != null && c.TravelsPaused && c.SelectedTraveler != null && c.SelectedTraveler.TravelData != null;
            }
        }

        /// <summary>The party's pawn position on the map (the selected traveler's view).</summary>
        public static Vector3 TravelerPos
        {
            get
            {
                var t = Game.Instance?.GlobalMapController?.SelectedTraveler;
                return (t != null && t.View != null) ? t.View.Position : Vector3.zero;
            }
        }

        /// <summary>The point the party is currently standing on (null mid-travel).</summary>
        public static BlueprintGlobalMapPoint CurrentLocation
            => Game.Instance?.GlobalMapController?.SelectedTraveler?.Location;

        /// <summary>The view of the current location (for its edges / connected points), or null.</summary>
        public static GlobalMapPointView CurrentLocationView
        {
            get
            {
                var bp = CurrentLocation;
                if (bp == null || GlobalMapView.Instance == null) return null;
                var st = GlobalMapView.Instance.State?.GetPointState(bp);
                return st != null ? st.View : null;
            }
        }

        public static IEnumerable<GlobalMapPointView> Points
            => GlobalMapView.Instance != null ? GlobalMapView.Instance.Points : Enumerable.Empty<GlobalMapPointView>();

        /// <summary>Revealed, enterable places (named locations) — what the player browses and travels to.</summary>
        public static IEnumerable<GlobalMapPointView> Locations
            => Points.Where(p => p != null && p.State != null && p.State.IsRevealed
                && p.Blueprint != null && p.Blueprint.Type == GlobalMapPointType.Location);

        /// <summary>Revealed road junctions (unnamed waypoints) — the road skeleton, not browse targets.</summary>
        public static IEnumerable<GlobalMapPointView> Junctions
            => Points.Where(p => p != null && p.State != null && p.State.IsRevealed
                && p.Blueprint != null && p.Blueprint.Type.IsWaypoint());

        /// <summary>Revealed armies on the current map. Empty until the crusade is active (Act 2+) — on every
        /// other map the game clears the army list, so this is naturally inert in Act 1.</summary>
        public static IEnumerable<GlobalMapArmyState> Armies
        {
            get
            {
                var st = GlobalMapView.Instance != null ? GlobalMapView.Instance.State : null;
                if (st == null || st.Armies == null) return Enumerable.Empty<GlobalMapArmyState>();
                return st.Armies.Where(a => a != null && a.IsRevealed && a.Data != null);
            }
        }

        /// <summary>Revealed enemy (demon) armies — the <c>.</c> cycle.</summary>
        public static IEnumerable<GlobalMapArmyState> EnemyArmies
            => Armies.Where(a => a.Data.Faction == ArmyFaction.Demons);

        /// <summary>Revealed allied (crusader/player) armies — the <c>,</c> cycle.</summary>
        public static IEnumerable<GlobalMapArmyState> AllyArmies
            => Armies.Where(a => a.Data.Faction == ArmyFaction.Crusaders);
    }
}
