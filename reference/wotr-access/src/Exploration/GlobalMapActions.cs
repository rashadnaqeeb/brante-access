using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Armies; // ArmyFaction
using Kingmaker.Globalmap.State; // GlobalMapArmyState
using Kingmaker.Globalmap.View;
using UnityEngine;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// Shared world-map verbs + labels, used by both the location list (<see cref="WrathAccess.Screens
    /// .GlobalMapScreen"/>) and the world-map scanner (<see cref="GlobalMapScanner"/>). Travel/enter drives
    /// the game's own <c>GoToLocationRevealed</c>/<c>EnterLocation</c> (the two common branches of the
    /// location panel); labels read name + compass bearing + state. Miles distance / the full multi-option
    /// panel arrive in later increments.
    /// </summary>
    internal static class GlobalMapActions
    {
        /// <summary>A point's spoken name — its location name, or a generic "junction" for the unnamed
        /// road-junction waypoints.</summary>
        public static string Name(GlobalMapPointView p)
        {
            var n = (string)p.Blueprint.Name;
            return string.IsNullOrEmpty(n) ? Loc.T("worldmap.junction") : n;
        }

        /// <summary>Name + compass bearing from the party + a state tag (here / closed). For lists.</summary>
        public static string Label(GlobalMapPointView p)
        {
            var parts = new List<string> { Name(p) };
            if (p.Blueprint == GlobalMapModel.CurrentLocation)
            {
                parts.Add(Loc.T("worldmap.you_are_here"));
            }
            else
            {
                var bearing = Geo.Bearing(GlobalMapModel.TravelerPos, p.transform.position);
                if (!string.IsNullOrEmpty(bearing)) parts.Add(bearing);
                if (p.State.IsClosed) parts.Add(Loc.T("worldmap.closed"));
            }
            return string.Join(", ", parts);
        }

        /// <summary>Name + state (here / closed), WITHOUT bearing — for "what the cursor is on" readouts
        /// (the analogue of in-area DescribeInPlace), where direction would be noise.</summary>
        public static string InPlace(GlobalMapPointView p)
        {
            var parts = new List<string> { Name(p) };
            if (p.Blueprint == GlobalMapModel.CurrentLocation) parts.Add(Loc.T("worldmap.you_are_here"));
            else if (p.State.IsClosed) parts.Add(Loc.T("worldmap.closed"));
            return string.Join(", ", parts);
        }

        /// <summary>Compass bearing + miles distance from the party to an arbitrary point on the map — the
        /// tiled cursor's readout when a step lands on an EMPTY cell (no point), so each step still places the
        /// cursor for tracking (the tiled mode's purpose). Units == miles on the global map.</summary>
        public static string PositionAt(Vector3 c)
        {
            var party = GlobalMapModel.TravelerPos;
            if (Geo.IsHere(party, c)) return Loc.T("geo.here");
            var bearing = Geo.Bearing(party, c);
            var miles = Geo.MilesStr(Geo.Distance(party, c));
            return string.IsNullOrEmpty(bearing) ? miles : bearing + ", " + miles;
        }

        /// <summary>Select a point — <b>100% the game's own click</b> (<see cref="GlobalMapPointView.HandleClick"/>),
        /// which routes through the global-map input-state machine + the click debounce before selecting, so the
        /// game's state stays consistent. (A hand-rolled <c>InteractWithLocation</c> skipped the state machine and
        /// softlocked the map.) The game's location panel then surfaces accessibly as a tab stop on
        /// <see cref="WrathAccess.Screens.GlobalMapScreen"/>, which announces the selection when it appears. The
        /// view's own guards (revealed / travels-paused / not-in-cutscene) decide whether anything happens.</summary>
        public static void Go(GlobalMapPointView pv)
        {
            if (pv != null) pv.HandleClick();
        }

        /// <summary>Resume travel paused mid-journey — the same <c>StartTravels</c> the game's move-helper
        /// Continue button drives (verified live), with its button-click sound.</summary>
        public static void ResumeTravel()
        {
            Kingmaker.UI.UISoundController.Instance?.PlayButtonClickSound();
            Game.Instance.GlobalMapController.StartTravels(fromClick: true);
            Tts.Speak(Loc.T("worldmap.continuing"));
        }

        // ---- armies (the . enemy / , ally cycles) ----

        /// <summary>An army's world position — its spawned pawn, else the node it sits on; null while it has
        /// neither (shouldn't happen for a revealed army on the live map).</summary>
        public static Vector3? ArmyPosition(GlobalMapArmyState army)
        {
            if (army == null) return null;
            var view = army.View;
            if (view != null) return view.Position;
            var loc = army.Location;
            if (loc != null && GlobalMapView.Instance != null)
            {
                var pv = GlobalMapView.Instance.GetPointView(loc);
                if (pv != null) return pv.transform.position;
            }
            return null;
        }

        /// <summary>An army's spoken name (e.g. "Crusade Army III" / "Demon Army"), with a generic fallback.</summary>
        public static string ArmyName(GlobalMapArmyState army)
        {
            var n = army != null && army.Data != null ? (string)army.Data.ArmyName : null;
            return string.IsNullOrEmpty(n) ? Loc.T("worldmap.army_fallback") : n;
        }

        /// <summary>Name + side (ally/enemy) + bearing + miles from the party — for the army cycles.</summary>
        public static string ArmyLabel(GlobalMapArmyState army)
        {
            var parts = new List<string>
            {
                ArmyName(army),
                Loc.T(army.Data.Faction == ArmyFaction.Crusaders ? "worldmap.army_ally" : "worldmap.army_enemy"),
            };
            var pos = ArmyPosition(army);
            if (pos.HasValue)
            {
                var from = GlobalMapModel.TravelerPos;
                if (!Geo.IsHere(from, pos.Value))
                {
                    parts.Add(Geo.Bearing(from, pos.Value));
                    parts.Add(Geo.MilesStr(Geo.Distance(from, pos.Value)));
                }
            }
            return string.Join(", ", parts);
        }
    }
}
