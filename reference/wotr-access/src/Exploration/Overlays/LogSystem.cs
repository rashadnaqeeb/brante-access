using System.Collections.Generic;
using WrathAccess.Settings;

namespace WrathAccess.Exploration.Overlays
{
    /// <summary>
    /// Speaks the game's log as it happens — every line the on-screen log prints, with a per-message-type
    /// toggle (grouped into Combat / Magic / Checks / Dialogue / World / Crusade subcategories, all
    /// defaulting to on) so each overlay can shape what it narrates. Lines come from <see cref="LogFeed"/>
    /// (one Harmony tap on the game's log-thread system), tagged by the thread type that produced them; the
    /// game-localized text is spoken as-is (rich text stripped by Tts), queued so it never cuts off
    /// navigation. Unknown thread types fall under the top-level "Other messages" toggle.
    ///
    /// Replaces the old persistent GameLogReader (barks + narrative messages — now the Barks and Narrative
    /// messages toggles). Warnings are deliberately NOT read here: <see cref="WrathAccess.WarningReader"/>
    /// already speaks every warning the moment it's raised (including ones that never reach the log).
    /// </summary>
    internal sealed class LogSystem : OverlaySystem
    {
        public override string Name => "Log";
        public override string Key => "log";

        // The log is context-free (it just drains the game's log feed), so it reads in areas AND on the
        // world map — which is how travel/discovery lines get spoken once an overlay is engaged on the map.
        public override OverlayScope Scope => OverlayScope.Both;

        // Cursor-independent (it drains the game's log feed) — "when moving" has no meaning here.
        public override System.Collections.Generic.IReadOnlyList<OverlayMode> SupportedModes => OverlayModes.OffContinuous;

        // thread type name → (group, toggle key, label). The game's real per-type granularity: one entry
        // per log-thread class (see LogThreadService.Setup), grouped for browsing.
        private static readonly string[,] Map =
        {
            // group, key, label, thread type
            { "combat", "attack_rolls", "Attack rolls", "RulebookAttackWithWeaponLogThread" },
            { "combat", "attack_results", "Attack results", "AttackLogThread" },
            { "combat", "attacks_of_opportunity", "Attacks of opportunity", "AttackOfOpportunityLogThread" },
            { "combat", "damage", "Damage", "DamageDealtLogThread" },
            { "combat", "healing", "Healing", "HealingLogThread" },
            { "combat", "life_state", "Death and unconsciousness", "UnitLifeStateChangedLogThread" },
            { "combat", "projectiles", "Projectile hits", "ProjectileHitLogThread" },
            { "combat", "maneuvers", "Combat maneuvers", "RulebookCombatManeuverLogThread" },
            { "combat", "initiative", "Initiative rolls", "UnitInitiativeLogThread" },
            { "combat", "combat_state", "Combat started and ended", "PartyCombatLogThread" },
            { "combat", "abilities", "Ability uses", "UseAbilityLogThread" },
            { "combat", "evasion", "Evasion", "UnitEvasionLogThread" },
            { "combat", "deflect", "Arrow deflection", "UnitDeflectArrowLogThread" },
            { "combat", "fake_death", "Fake death", "UnitFakeDeathMessageLogThread" },
            { "combat", "stealth", "Stealth spotted", "UnitInStealthSpottedLogThread" },
            { "combat", "hit_dice", "Hit dice restrictions", "HitDiceRestrictionLogThread" },

            { "magic", "casts", "Spell casts", "RulebookCastSpellLogThread" },
            { "magic", "concentration", "Concentration checks", "RulebookCheckConcentration" },
            { "magic", "defensive_casting", "Casting defensively", "RulebookCastingDefensivelyLogThread" },
            { "magic", "spell_resistance", "Spell resistance checks", "RulebookSpellResistanceCheckLogThread" },
            { "magic", "dispel", "Dispel magic", "RulebookDispellMagicLogThread" },
            { "magic", "buffs", "Buffs and immunities", "RulebookCanApplyBuffLogThread" },
            { "magic", "spell_turning", "Spell turning", "SpellTurningLogThread" },
            { "magic", "kineticist", "Kineticist burn", "KineticistGlobalLogThread" },
            { "magic", "stat_damage", "Stat damage", "RulebookDealStatDamageLogThread" },
            { "magic", "stat_damage_healed", "Stat damage healed", "RulebookHealStatDamageLogThread" },
            { "magic", "energy_drain", "Energy drain", "RulebookDrainEnergyLogThread" },
            { "magic", "energy_drain_healed", "Energy drain healed", "RulebookHealEnergyDrainLogThread" },

            { "checks", "skill_checks", "Skill checks", "RollSkillCheckLogThread" },
            { "checks", "saves", "Saving throws", "RulebookSavingThrowLogThread" },
            { "checks", "perception", "Perception", "PerceptionLogThread" },
            { "checks", "pick_lock", "Pick lock", "PickLockLogThread" },
            { "checks", "disarm_trap", "Disarm trap", "DisarmTrapLogThread" },
            { "checks", "skinning", "Skinning", "SkinningRollSkillCheckLogThread" },
            { "checks", "roll_chance", "Chance rolls", "RulebookRollChance" },

            { "dialogue", "barks", "Barks", "BarkLogThread" },
            { "dialogue", "cues", "Dialogue lines", "DialogCueLogThread" },
            { "dialogue", "history", "Dialogue history", "DialogHistoryLogThread" },
            { "dialogue", "started", "Dialogue started", "DialogLogThread" },
            { "dialogue", "alignment", "Alignment shifts", "AlignmentShiftLogThread" },

            { "world", "messages", "Narrative messages", "MessageLogThread" },
            { "world", "loot", "Items collected", "ItemsCollectionLogThread" },
            { "world", "equipment", "Equipment changes", "UnitEquipmentLogThread" },
            { "world", "party_xp", "Party experience", "PartyGainExperienceLogThread" },
            { "world", "unit_xp", "Character experience", "UnitGainExperienceLogThread" },
            { "world", "location", "Location", "LocationLogThread" },
            { "world", "rest", "Rest", "RestFinishedLogThread" },
            { "world", "identify", "Item identification", "IdentifyLogThread" },
            { "world", "party_encumbrance", "Party encumbrance", "PartyEncumbranceLogThread" },
            { "world", "unit_encumbrance", "Character encumbrance", "UnitEncumbranceLogThread" },
            { "world", "time", "Game time", "GameTimeAdvancedLogThread" },
            { "world", "features", "Special features", "SpecialFeatureChangedLogThread" },
            { "world", "restrictions", "Interaction restrictions", "InteractionRestrictionLogThread" },

            { "crusade", "morale", "Crusade morale", "KingdomMoraleChangeLogThread" },
            { "crusade", "turns", "Battle turns", "TacticalCombatTurnLogThread" },
            { "crusade", "skip_turns", "Skipped turns", "TacticalCombatSkipTurnLogThread" },
            { "crusade", "postponed_turns", "Postponed turns", "TacticalCombatPostponeTurnLogThread" },
            { "crusade", "battle_morale", "Battle morale", "TacticalCombatMoraleLogThread" },
            { "crusade", "resurrect", "Resurrections", "TacticalCombatResurrectLogThread" },
            { "crusade", "deaths", "Unit deaths", "TacticalCombatUnitsDiedThread" },
        };

        private static readonly string[,] Groups =
        {
            { "combat", "Combat" }, { "magic", "Magic" }, { "checks", "Checks" },
            { "dialogue", "Dialogue" }, { "world", "World" }, { "crusade", "Crusade" },
        };

        // thread type → (group, key) for message-time lookup.
        private static readonly Dictionary<string, KeyValuePair<string, string>> Lookup = BuildLookup();
        private static Dictionary<string, KeyValuePair<string, string>> BuildLookup()
        {
            var d = new Dictionary<string, KeyValuePair<string, string>>();
            for (int i = 0; i < Map.GetLength(0); i++)
                d[Map[i, 3]] = new KeyValuePair<string, string>(Map[i, 0], Map[i, 1]);
            return d;
        }

        public override void RegisterSettings(CategorySetting cat)
        {
            var groups = new Dictionary<string, CategorySetting>();
            for (int i = 0; i < Groups.GetLength(0); i++)
            {
                var g = new CategorySetting(Groups[i, 0], Groups[i, 1], localizationKey: "overlay.log." + Groups[i, 0]);
                groups[Groups[i, 0]] = g;
                cat.Add(g);
            }
            for (int i = 0; i < Map.GetLength(0); i++)
                groups[Map[i, 0]].Add(new BoolSetting(Map[i, 1], Map[i, 2], true,
                    "overlay.log." + Map[i, 0] + "." + Map[i, 1]));
            cat.Add(new BoolSetting("other", "Other messages", true, "overlay.log.other"));
        }

        public override void Tick(float dt, Overlay overlay)
        {
            if (!ShouldPlay(overlay)) { LogFeed.Clear(); return; }
            // The context gate (one of our screens covering exploration — rest, dialogue, a window)
            // silences log NARRATION: partly lens design, partly because the covering screen often
            // reads the same content itself (dialogue cues, loot). But BARKS are a character SPEAKING,
            // and no screen re-delivers those — camp banter during rest arrives here as BarkLogThread
            // lines while the rest screen is on top, and was being dropped. Speech always gets through.
            bool covered = !OverlayManager.Active;
            while (LogFeed.TryDequeue(out string thread, out string text))
            {
                if (covered && thread != "BarkLogThread") continue;
                if (ShouldSpeak(thread))
                    Tts.Speak(text, interrupt: false); // passive content — queue behind, never cut off nav
            }
        }

        private bool ShouldSpeak(string threadType)
        {
            if (Lookup.TryGetValue(threadType, out var loc))
                return Settings?.Get<CategorySetting>(loc.Key)?.Get<BoolSetting>(loc.Value)?.Get() ?? true;
            if (threadType == "WarningNotificationLogThread") return false; // WarningReader speaks these live
            return Bool("other", true);
        }
    }
}
