using Kingmaker.EntitySystem.Entities; // UnitEntityData
using Kingmaker.UnitLogic.Buffs;        // Buff

namespace WrathAccess.Events
{
    /// <summary>A unit gained a buff/debuff (game's IUnitBuffHandler). Sourced (the buff's owner).</summary>
    [EventSettings("Buff gained", "combat", EventSources.All)]
    internal sealed class BuffGainedEvent : ModEvent
    {
        private readonly Buff _buff;
        public BuffGainedEvent(Buff buff) { _buff = buff; }
        public override UnitEntityData Unit => _buff?.Owner;
        public override Message GetMessage()
            => Message.Localized("ui", "event.buff_gained", new { name = _buff?.Owner?.CharacterName, buff = _buff?.Name });
    }

    /// <summary>A unit lost a buff/debuff (game's IUnitBuffHandler). Sourced (the buff's owner).</summary>
    [EventSettings("Buff lost", "combat", EventSources.All)]
    internal sealed class BuffLostEvent : ModEvent
    {
        private readonly Buff _buff;
        public BuffLostEvent(Buff buff) { _buff = buff; }
        public override UnitEntityData Unit => _buff?.Owner;
        public override Message GetMessage()
            => Message.Localized("ui", "event.buff_lost", new { name = _buff?.Owner?.CharacterName, buff = _buff?.Name });
    }
}
