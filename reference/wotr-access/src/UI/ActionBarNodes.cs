using System;
using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.ActionBar;
using Kingmaker.UI.UnitSettings; // MechanicActionBarSlot + subtypes
using WrathAccess.UI.Graph;

namespace WrathAccess.UI
{
    /// <summary>
    /// Graph factory for action-bar slots (the selected unit's abilities / spells / activatable items /
    /// special actions). Everything is read LIVE from the slot's underlying
    /// <see cref="MechanicActionBarSlot"/>, never the ActionBarSlotVM's cached reactive properties: the
    /// game rapidly repopulates these slots as it swaps the selected character, so a cached read would
    /// lag. Toggle abilities (Fight Defensively, Power Attack, stances…) carry a LIVE on/off/targeting
    /// value part — the async settle/revert (e.g. Saddle Up) announces itself while focused; the
    /// enabled gate (<c>IsPossibleActive</c>, the same value the game greys the slot by) is live too.
    /// Activate branches via Targeting (self-cast / aim / toggle); Space drills into the game tooltip.
    /// </summary>
    internal static class ActionBarNodes
    {
        public static NodeVtable Slot(ActionBarSlotVM vm)
        {
            Func<MechanicActionBarSlot> slot = () => vm?.MechanicActionBarSlot;
            Func<bool> enabled = () => slot()?.IsPossibleActive() ?? false;
            return new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(() => slot()?.GetTitle() ?? ""),
                    // Toggle state (on / off / targeting) — LIVE, so the game's async settle under
                    // focus announces itself. Null (silent) for non-toggle slots.
                    new NodeAnnouncement(() =>
                    {
                        var key = ToggleStateKey(slot());
                        return key != null ? Loc.T(key) : null;
                    }, live: true, kind: AnnouncementKinds.Value),
                    // Resource count for non-toggles (uses / casts / charges) — read at focus time.
                    new NodeAnnouncement(() => ResourceText(slot()), kind: AnnouncementKinds.Value),
                    GraphNodes.DisabledPart(enabled), // live (mounted Charge becoming usable announces)
                },
                SearchText = () => slot()?.GetTitle() ?? "",
                OnActivate = () => Activate(vm),
                OnTooltip = () =>
                {
                    var tpl = slot()?.GetTooltipTemplate();
                    if (tpl != null) Screens.TooltipScreen.Open(tpl);
                },
            };
        }

        // The live "ui" value key for a toggle ability's state, or null if this slot isn't a toggle:
        //   on + waiting for a target -> "value.targeting"  (e.g. Saddle Up: on, but needs a mount target)
        //   on, no targeting          -> "value.on"          (a plain toggle, e.g. Fight Defensively)
        //   off                       -> "value.off"
        private static string ToggleStateKey(MechanicActionBarSlot s)
        {
            if (!(s is MechanicActionBarSlotActivableAbility act) || act.ActivatableAbility == null) return null;
            var a = act.ActivatableAbility;
            if (!a.IsOn) return "value.off";
            return a.IsWaitingForTarget ? "value.targeting" : "value.on";
        }

        // The resource count labeled by the slot's kind ("3 casts", "1 charge"), a bare number for
        // unknown kinds, null for toggles (their state part reads instead) and slots with no resource.
        private static string ResourceText(MechanicActionBarSlot s)
        {
            if (s == null || ToggleStateKey(s) != null) return null;
            int res = s.GetResource();
            if (res <= 0) return null;
            var unitBase = ResourceUnitBase(s);
            return unitBase != null
                ? Loc.T("value.amount", new { count = res, unit = Loc.T("unit." + unitBase + (res == 1 ? "" : "s")) })
                : res.ToString();
        }

        // The resource-count's unit, by slot kind (singular base; caller adds "s" for plural). Null =
        // bare number (unknown kind). Spell family is the MechanicActionBarSlotSpell base + global magic.
        private static string ResourceUnitBase(MechanicActionBarSlot s)
        {
            if (s is MechanicActionBarSlotSpell || s is MechanicActionBarSlotGlobalMagicSpell) return "cast";
            if (s is MechanicActionBarSlotItem) return "charge";
            if (s is MechanicActionBarSlotAbility) return "use";
            return null;
        }

        private static void Activate(ActionBarSlotVM vm)
        {
            var s = vm?.MechanicActionBarSlot;
            // A click does nothing useful when the slot can neither be activated nor (for a toggle)
            // deactivated — mirrors the game's own gate.
            bool blocked = s != null && !s.IsPossibleActive()
                && !(s is MechanicActionBarSlotActivableAbility act && act.IsPossibleDeactivate());
            int warnings = WarningReader.Count;
            Exploration.Targeting.Activate(vm); // branches: self-cast / aim / toggle

            // The game raises a warning only for some refusals (e.g. turn-based "not enough actions");
            // for a plainly-disabled ability it just plays a sound and reports no text. Give a spoken
            // fallback when it stayed silent, so Enter on a greyed slot always says something.
            if (blocked && WarningReader.Count == warnings)
                Tts.Speak(Loc.T("action.cant_use"), interrupt: true);
        }
    }
}
