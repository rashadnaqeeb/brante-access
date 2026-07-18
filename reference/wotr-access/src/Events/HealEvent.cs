using Kingmaker.EntitySystem.Entities; // UnitEntityData

namespace WrathAccess.Events
{
    /// <summary>A unit was healed (game's IHealingHandler over RuleHealDamage). Sourced — party / enemy /
    /// neutral each have their own enable + speech config. PROTOTYPE: one event per heal instance, mirroring
    /// <see cref="DamageEvent"/> (the inverse). <c>amount</c> is the actual HP restored (RuleHealDamage.Value,
    /// already clamped to missing health), so an over-heal reads only what it actually recovered.</summary>
    [EventSettings("Heal", "combat", EventSources.All)]
    internal sealed class HealEvent : ModEvent
    {
        private readonly UnitEntityData _unit;
        private readonly int _amount;

        public HealEvent(UnitEntityData unit, int amount) { _unit = unit; _amount = amount; }

        public override UnitEntityData Unit => _unit;

        public override Message GetMessage()
            => Message.Localized("ui", "event.heal", new { name = _unit?.CharacterName, amount = _amount });
    }
}
