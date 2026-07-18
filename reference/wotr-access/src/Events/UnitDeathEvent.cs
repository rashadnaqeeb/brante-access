using Kingmaker.EntitySystem.Entities; // UnitEntityData

namespace WrathAccess.Events
{
    /// <summary>A unit died (game's IUnitHandler.HandleUnitDeath). Sourced — party / enemy / neutral.</summary>
    [EventSettings("Unit death", "combat", EventSources.All)]
    internal sealed class UnitDeathEvent : ModEvent
    {
        private readonly UnitEntityData _unit;
        public UnitDeathEvent(UnitEntityData unit) { _unit = unit; }
        public override UnitEntityData Unit => _unit;
        public override Message GetMessage()
            => Message.Localized("ui", "event.death", new { name = _unit?.CharacterName });
    }
}
