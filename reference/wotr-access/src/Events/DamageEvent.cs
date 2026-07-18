using Kingmaker.EntitySystem.Entities; // UnitEntityData

namespace WrathAccess.Events
{
    /// <summary>A unit took damage (game's IDamageHandler). Sourced — party / enemy / neutral each have
    /// their own enable + speech config. PROTOTYPE: one event per damage instance (a multi-hit / AoE
    /// reads each hit; consolidation comes later).</summary>
    [EventSettings("Damage", "combat", EventSources.All)]
    internal sealed class DamageEvent : ModEvent
    {
        private readonly UnitEntityData _unit;
        private readonly int _amount;

        public DamageEvent(UnitEntityData unit, int amount) { _unit = unit; _amount = amount; }

        public override UnitEntityData Unit => _unit;

        public override Message GetMessage()
            => Message.Localized("ui", "event.damage", new { name = _unit?.CharacterName, amount = _amount });
    }
}
