using System;
using Kingmaker;                          // Game.Instance (BlueprintRoot for the "Permanent" string)
using Kingmaker.Armies.TacticalCombat;    // TacticalCombatHelper.IsActive (rounds vs time)
using Kingmaker.Blueprints.Root;          // BlueprintRoot (Calendar period string)
using Kingmaker.Blueprints.Root.Strings;  // UIStrings (the "Rounds" label)
using Kingmaker.EntitySystem.Entities;    // UnitEntityData
using Kingmaker.UnitLogic.Buffs;          // Buff

namespace WrathAccess.Buffers
{
    /// <summary>
    /// A <see cref="Buffer"/> over a live unit (the selected party member, or the review-cursor target). Its
    /// lines, in order: the unit's name, HP, AC (full / touch / flat-footed), then every visible buff and
    /// debuff in the game's own order, each as "name [×stacks][, duration]". Conditions like Prone / Fatigued
    /// are themselves buffs (each applied by an AddCondition component), so they appear in that same list — no
    /// separate condition channel. We mirror the game's buff display exactly: the same visibility filter
    /// (<c>!Blueprint.IsHiddenInUI</c>; suppressed buffs are NOT distinguished — the game's UI ignores
    /// <c>IsSuppressed</c>) and the same duration format as the buff tooltip. The unit is resolved live on
    /// every <see cref="Update"/> via the supplied factory, so the buffer always reflects the current
    /// selection / review target without an explicit re-bind.
    /// </summary>
    internal sealed class UnitBuffer : Buffer
    {
        private readonly Func<UnitEntityData> _resolve;

        public UnitBuffer(string key, Func<UnitEntityData> resolve) : base(key) { _resolve = resolve; }

        public override void Update() => Repopulate(() => Populate(_resolve?.Invoke()));

        private void Populate(UnitEntityData unit)
        {
            if (unit == null) return;
            Add(unit.CharacterName);
            Add(Loc.T("unit.hp", new { hp = unit.HPLeft, max = unit.MaxHP }));
            Add(AcLine(unit));
            // Visible buffs in game order — same filter as the game's own buff lists (blueprint flag only;
            // suppressed buffs still show, undistinguished, exactly as the game presents them).
            var buffs = unit.Buffs?.Enumerable;
            if (buffs != null)
                foreach (var buff in buffs)
                    if (buff != null && buff.Blueprint != null && !buff.Blueprint.IsHiddenInUI)
                        Add(BuffLine(buff));
        }

        // "AC 30, Touch 20, Flat-Footed 15" — the three armor-class values the game tracks.
        private static string AcLine(UnitEntityData unit)
        {
            var ac = unit.Descriptor?.Stats?.AC;
            if (ac == null) return null;
            return Loc.T("unit.ac", new { ac = ac.ModifiedValue, touch = ac.Touch, flat = ac.FlatFooted });
        }

        // "Bless, 9 rounds" / "Rage ×2, Rounds: 5" / "Barkskin, 2 minutes" / "Fatigued, Permanent".
        private static string BuffLine(Buff buff)
        {
            string line = buff.Name;
            if (buff.Rank > 1) line += " " + Loc.T("buff.stacks", new { count = buff.Rank });
            string dur = Duration(buff);
            if (!string.IsNullOrEmpty(dur)) line += ", " + dur;
            return line;
        }

        // The buff's remaining duration, formatted exactly as the game's buff tooltip
        // (TooltipBrickBuffVM.GetDuration): a localized "Permanent", nothing for instantaneous, a compact
        // period out of tactical combat ("2 minutes"), or "Rounds: N" within it. All game-localized.
        private static string Duration(Buff buff)
        {
            if (buff.IsPermanent)
                return (string)Game.Instance.BlueprintRoot.LocalizedTexts.UserInterfacesText.CommonTexts.PermanentBuffTimer;
            if (buff.TimeLeft <= TimeSpan.Zero)
                return null;
            if (!TacticalCombatHelper.IsActive)
                return BlueprintRoot.Instance.Calendar.GetCompactPeriodString(buff.TimeLeft);
            return (string)UIStrings.Instance.TurnBasedTexts.Rounds + ": " + ((float)buff.TimeLeft.Seconds / 6f);
        }
    }
}
