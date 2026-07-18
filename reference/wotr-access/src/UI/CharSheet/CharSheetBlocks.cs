using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints.Root.Strings; // UIStrings (game-localized Level Up label)
using Kingmaker.UI.Common; // UIUtility.AddSign, UIUtilityItem.AttackData
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.LevelClassScores;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Martial.Attack;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Martial.Defence;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.NameAndPortrait;
using WrathAccess.UI.Graph;

namespace WrathAccess.UI.CharSheet
{
    /// <summary>
    /// Renders the reusable character-summary blocks — name/HP, level/race/ability-scores/classes, attacks,
    /// defence — into an <see cref="ICharSheetSink"/>. These CharInfo* section VMs back both the character
    /// sheet's Summary page and the inventory window's left panel (same VM types, wired by both
    /// CharacterInfoPCView and InventoryPCView), so both screens render them identically here. Everything is
    /// read live through closures; tooltips ride along per row.
    /// </summary>
    public static class CharSheetBlocks
    {
        public static void NamePortrait(CharInfoNameAndPortraitVM np, ICharSheetSink sink)
        {
            if (np == null) return;
            var items = new List<NodeVtable> { GraphNodes.Text(() => Loc.T("char.name", new { value = np.UnitName })) };
            var mythic = np.MythicName?.Value;
            if (!string.IsNullOrEmpty(mythic)) items.Add(GraphNodes.Text(() => Loc.T("char.mythic", new { value = np.MythicName.Value })));
            if (np.HitPoints != null)
                items.Add(GraphNodes.Text(() => Loc.T("char.hit_points", new { value = np.HitPoints.HpText.Value }), () => np.HitPoints.Tooltip.Value));
            sink.ListSection(Loc.T("section.character"), items);
        }

        // withLevelUp adds the game's Level Up button next to the XP readout — only the character
        // sheet's prefab wires that button (CharInfoExperiencePCView null-checks it), so the
        // inventory's shared summary leaves it off, mirroring the game.
        public static void LevelClassScores(CharInfoLevelClassScoresVM lcs, ICharSheetSink sink, bool withLevelUp = false)
        {
            if (lcs == null) return;
            // Visual order: header info (level, race/gender/alignment) reads first, then the block's
            // ability scores, then the class list (prefab puts AbilityScores above ClassesList).
            var xp = lcs.Experience;
            if (xp != null)
            {
                var items = new List<NodeVtable> { GraphNodes.Text(() => Loc.T("char.level", new { value = xp.Level })) };
                items.Add(GraphNodes.Text(() => Loc.T("char.experience", new { current = xp.CurrentExp, next = xp.NextLevelExp })));
                if (xp.NegativeLevels > 0) items.Add(GraphNodes.Text(() => Loc.T("char.negative_levels", new { value = xp.NegativeLevels })));
                if (withLevelUp && xp.CanLevelup)
                    // Same gate (CanLevelup, re-read every render — the button appears/disappears live,
                    // mirroring the game hiding it when no level-up is pending) and same VM call.
                    items.Add(GraphNodes.Button(
                        () => TextUtil.StripRichText(UIStrings.Instance.InGameMenuTexts.LevelUpText),
                        () => xp.LevelUp()));
                sink.ListSection(Loc.T("section.level"), items);
            }

            var rga = lcs.RaceGenderAlignment;
            if (rga != null)
                sink.ListSection(Loc.T("section.race"), new List<NodeVtable>
                {
                    GraphNodes.Text(() => Loc.T("char.race", new { value = rga.RaceValue }), () => rga.RaceTooltip),
                    GraphNodes.Text(() => Loc.T("char.gender", new { value = rga.GenderValue })),
                    GraphNodes.Text(() => Loc.T("char.alignment", new { value = rga.AlignmentDisplayValue }), () => rga.AlignmentTooltip),
                });

            if (lcs.AbilityScores?.AbilityScores != null)
            {
                var g = new StatGroup(Loc.T("section.ability_scores"), Loc.T("col.score"), Loc.T("col.modifier"));
                foreach (var a in lcs.AbilityScores.AbilityScores) g.Row(CharInfoStatRows.Ability(a)); // carries the stat tooltip
                sink.StatGroup(g);
            }

            var classes = lcs.Classes?.ClassVMs;
            if (classes != null && classes.Count > 0)
            {
                var items = new List<NodeVtable>();
                foreach (var c in classes) { var cc = c; items.Add(GraphNodes.Text(() => Loc.T("char.class_level", new { name = cc.ClassName, level = cc.Level }), () => cc.Tooltip)); }
                sink.ListSection(Loc.T("section.classes"), items);
            }
        }

        public static void Attacks(CharInfoAttacksBlockVM atk, ICharSheetSink sink)
        {
            if (atk == null) return;
            var g = new StatGroup(Loc.T("section.attacks"), Loc.T("col.attack"), Loc.T("col.damage"), Loc.T("col.crit")); // prefab columns: weapon, attack, damage, crit
            AddAttackRow(g, atk.MainHandAttack);
            AddAttackRow(g, atk.OffHandAttack);
            if (atk.AdditionalAttackEntities != null)
                foreach (var a in atk.AdditionalAttackEntities) AddAttackRow(g, a);
            if (g.Rows.Count > 0) sink.StatGroup(g);
            else sink.ListSection(Loc.T("section.attacks"), new[] { GraphNodes.Text(() => Loc.T("char.no_attacks")) });
        }

        private static void AddAttackRow(StatGroup g, CharInfoAttackEntityVM a)
        {
            if (a == null || string.IsNullOrEmpty(a.AttackName)) return;
            // The *Label properties are the column labels; the actual values live in AttackData (the view
            // does SetData(AttackData) for the value + SetLabel(*Label) for the heading).
            g.Row(new StatRow(() => a.AttackName,
                new Func<string>[] { () => AttackStr(a.AttackData), () => a.AttackData?.Damage ?? "", () => Crit(a.AttackData) },
                () => a.AttackTooltip));
        }

        private static string AttackStr(UIUtilityItem.AttackData d) // e.g. "+6/+1"
            => d?.Attacks == null ? "" : string.Join("/", d.Attacks.Select(n => UIUtility.AddSign(n)));

        private static string Crit(UIUtilityItem.AttackData d) // threat range + multiplier, e.g. "19-20 x2"
        {
            if (d == null) return "";
            var s = d.CritChance ?? "";
            if (!string.IsNullOrEmpty(d.CritDamage)) s = (s.Length > 0 ? s + " " : "") + d.CritDamage;
            return s;
        }

        public static void Defence(CharInfoDefenceBlockVM def, ICharSheetSink sink)
        {
            if (def == null) return;
            var g = new StatGroup(Loc.T("section.defense"));
            var ac = def.ArmorClass?.Value;
            if (ac != null)
            {
                // Prefab order: AC, Flat-footed, Touch (verified via the layout dump).
                g.Row(CharInfoStatRows.Value(ac.AC, signed: false));
                g.Row(CharInfoStatRows.Value(ac.FlatFooted, signed: false));
                g.Row(CharInfoStatRows.Value(ac.Touch, signed: false));
            }
            var st = def.SavingThrow?.Value;
            if (st != null)
            {
                g.Row(CharInfoStatRows.Value(st.Fortitude, signed: true));
                g.Row(CharInfoStatRows.Value(st.Reflex, signed: true));
                g.Row(CharInfoStatRows.Value(st.Will, signed: true));
            }
            g.Row(CharInfoStatRows.Value(def.Initiative?.Value, signed: true));
            g.Row(CharInfoStatRows.Value(def.Speed?.Value, signed: false));
            g.Row(CharInfoStatRows.Value(def.Size?.Value, signed: false));
            sink.StatGroup(g);
        }
    }
}
