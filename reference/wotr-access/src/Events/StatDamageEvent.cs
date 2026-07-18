using Kingmaker.Blueprints.Root;         // LocalizedTexts
using Kingmaker.EntitySystem.Entities;    // UnitEntityData
using Kingmaker.EntitySystem.Stats;       // StatType

namespace WrathAccess.Events
{
    /// <summary>Ability score damage or drain (game's RuleDealStatDamage). Sourced — party / enemy /
    /// neutral. Drain vs damage picks the wording; the stat name uses the game's own localized table.</summary>
    [EventSettings("Ability damage", "combat", EventSources.All)]
    internal sealed class StatDamageEvent : ModEvent
    {
        private readonly UnitEntityData _unit;
        private readonly StatType _stat;
        private readonly int _amount;
        private readonly bool _drain;

        public StatDamageEvent(UnitEntityData unit, StatType stat, int amount, bool drain)
        {
            _unit = unit; _stat = stat; _amount = amount; _drain = drain;
        }

        public override UnitEntityData Unit => _unit;

        public override Message GetMessage()
        {
            // Game-localized stat name (passed through, not re-translated).
            string stat = LocalizedTexts.Instance.Stats.GetText(_stat);
            return Message.Localized("ui", _drain ? "event.ability_drain" : "event.ability_damage",
                new { name = _unit?.CharacterName, amount = _amount, stat });
        }
    }
}
