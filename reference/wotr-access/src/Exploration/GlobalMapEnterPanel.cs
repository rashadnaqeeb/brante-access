using System;
using Kingmaker;
using Kingmaker.Blueprints.Root; // BlueprintRoot (Calendar)
using Kingmaker.Blueprints.Root.Strings; // UIStrings
using Kingmaker.Globalmap.State; // GlobalMapArmyState
using Kingmaker.Globalmap.View; // GlobalMapView (+ LocationEntranceType)
using Kingmaker.UI.MVVM._VM.GlobalMap.Message; // GlobalMapEnterMessageVM
using WrathAccess.UI; // TextUtil

namespace WrathAccess.Exploration
{
    /// <summary>
    /// The accessible read-out of the game's location panel (<see cref="GlobalMapEnterMessageVM"/>). The VM
    /// is deliberately thin — ALL the panel's text and the accept-button enabled/label state are computed in
    /// the View (<c>GlobalMapEnterMessageView.FillDialogInfoLocation</c>) into View-private fields, so we
    /// faithfully REPLICATE that logic here (the same pattern as our other "mirror the view, not just the VM"
    /// spots) rather than reading the VM alone. Buttons are still invoked via the VM contract
    /// (<c>Accept</c>/<c>AlternativeAction</c>/<c>Close</c>) by <see cref="WrathAccess.Screens.GlobalMapEnterScreen"/>.
    /// </summary>
    internal static class GlobalMapEnterPanel
    {
        /// <summary>The panel title — the location name (a generic fallback for unnamed junctions).</summary>
        public static string Title(GlobalMapEnterMessageVM vm)
        {
            var name = vm != null && vm.Location != null ? TextUtil.StripRichText(vm.Location.State.Name) : null;
            return string.IsNullOrEmpty(name) ? Loc.T("worldmap.enter_title") : name;
        }

        public static bool HasSettlement(GlobalMapEnterMessageVM vm) => vm != null && vm.Settlement != null;

        /// <summary>The location's own description (its lore) — what the place IS. The game shows this to
        /// sighted players on the map hover, not in the enter panel itself, so the panel would otherwise omit
        /// the most useful context for a blind player. Empty for unnamed junctions / undescribed points.</summary>
        public static string LocationDescription(GlobalMapEnterMessageVM vm)
        {
            try { return vm != null && vm.Location != null ? TextUtil.StripRichText(vm.Location.State.Description) : null; }
            catch { return null; }
        }

        /// <summary>Accept-button label: "Enter" when standing on the location, else "Travel" (UIRoot strings).</summary>
        public static string AcceptLabel(GlobalMapEnterMessageVM vm)
        {
            var ui = Game.Instance.BlueprintRoot.UIRoot;
            return (string)(vm.IsCurrentLocation ? ui.GlobalMapEnterDialogAccept : ui.GlobalMapJounayDialogAccept);
        }

        public static string ManageLabel() => (string)UIStrings.Instance.Rest.ManageButton;
        public static string CloseLabel() => (string)UIStrings.Instance.CommonTexts.Close;

        /// <summary>Replicate <c>FillDialogInfoLocation</c>: the description text (travel time / enter
        /// confirmation / closed or restricted reason / no-path) and whether Accept is enabled. Wrapped so a
        /// surprise from an Act-2 crusade path can't kill the panel — it degrades to "name, can accept".</summary>
        public static void Compute(GlobalMapEnterMessageVM vm, out string description, out bool acceptEnabled)
        {
            description = string.Empty;
            acceptEnabled = false;
            try { ComputeCore(vm, out description, out acceptEnabled); }
            catch (System.Exception e)
            {
                // Don't mask a failure as the location name — leave the travel/reason line blank (the lore
                // description still shows) and keep Accept available, but log it so the cause is visible.
                WrathAccess.Main.Log?.Error("[worldmap] enter-panel description failed: " + e);
                description = string.Empty;
                acceptEnabled = true;
            }
        }

        private static void ComputeCore(GlobalMapEnterMessageVM vm, out string description, out bool acceptEnabled)
        {
            var location = vm.Location;
            bool isCurrent = vm.IsCurrentLocation;
            var controller = Game.Instance.GlobalMapController;
            var selectedArmy = controller != null ? controller.SelectedArmy : null;
            var view = GlobalMapView.Instance;
            var texts = Game.Instance.BlueprintRoot.LocalizedTexts.UserInterfacesText.GlobalMap;

            var travel = selectedArmy == null
                ? view.CalculatePlayerPathToLocation(location.Blueprint)
                : view.State.PathManager.CalculateArmyPathToLocation(selectedArmy, location.Blueprint);
            var entrance = view.PreviewLocationEntryType(location.Blueprint);

            // "Already-visited book event" / a settlement-only empty location / any selected-army case: no
            // confirmation text, accept off (matches the view's first early-out).
            bool flag = selectedArmy != null || (location.State.IsEmpty && vm.Settlement != null);
            if (isCurrent && (entrance == GlobalMapView.LocationEntranceType.BookEventFailed || flag))
            {
                acceptEnabled = false;
                description = flag ? string.Empty : (string)texts.BookEventAllreadyVisited;
                return;
            }

            // Closed — blocks entering always, travelling only when the map restricts it (else fall through).
            if (location.State.IsClosed && (isCurrent || view.Blueprint.RestrictTravelingToClosedLocations))
            {
                acceptEnabled = false;
                description = (location.Blueprint.UseCustomClosedText && !location.Blueprint.CustomClosedText.IsEmpty())
                    ? (string)location.Blueprint.CustomClosedText
                    : (string)texts.LocationIsClosed;
                return;
            }

            var restriction = location.State.GetInfo().Restriction;
            bool restricted = restriction != null && restriction.IsRestricted();
            acceptEnabled = location.State.EdgesOpened && !restricted;

            if (travel == null) { description = (string)texts.LocationPathIsHidden; return; }

            if (restricted)
            {
                description = restriction.GetDescription();
            }
            else if (isCurrent)
            {
                description = location.Blueprint.OverrideEnterConfirmationText
                    ? (string)location.Blueprint.CustomEnterConfirmationText
                    : (string)texts.GlobalMapEnterDialogDesc;
            }
            else if (selectedArmy == null)
            {
                var t = view.State.PathManager.GetPlayerPathTime(travel);
                if (t > TimeSpan.Zero)
                {
                    var period = Game.Instance.BlueprintRoot.Calendar.GetPeriodString(t);
                    description = string.Format((string)texts.GlobalMapJouneyDialogDesc,
                        string.IsNullOrEmpty(period) ? (string)texts.GlobalMapShortJourney : period);
                }
                else
                {
                    description = string.Format((string)texts.GlobalMapJouneyDialogDesc, (string)texts.GlobalMapUnknownJouneyDialogDesc);
                }
            }
            else // a selected army travelling to a non-current location (Act 2+): show the movement-point cost.
            {
                int cost = view.State.PathManager.GetArmyPathCost(travel, selectedArmy.Position);
                if (cost > 0) description = string.Format((string)texts.GlobalMapArmyJouneyDialogDesc, cost);
                else { description = string.Format((string)texts.GlobalMapJouneyDialogDesc, (string)texts.GlobalMapUnknownJouneyDialogDesc); acceptEnabled = false; }
            }
        }
    }
}
