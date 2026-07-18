using System.Collections.Generic;
using Kingmaker.Blueprints.Root.Strings; // UIStrings / UITextCharSheet (localized labels)
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Total;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Martial.BAB; // CharInfoBABVM (decompiler-skipped; members reconstructed from its view)
using WrathAccess.UI;
using WrathAccess.UI.CharSheet;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The final chargen phase — "results" (<see cref="CharGenTotalPhaseVM"/>): the full character
    /// sheet. It's built from the game's reusable CharInfo* section VMs, so the stat readers (via
    /// <see cref="ICharSheetLayout"/> + <see cref="CharInfoStatRows"/>) are reused when we build the
    /// in-game character sheet. The Complete button isn't part of this content — it's the wizard-level
    /// Next/Complete (always enabled here), handled by <see cref="CharGenScreen"/>.
    ///
    /// The whole sheet is ONE FlowSheet (one Tab-stop): each section is a stacked region — arrow
    /// cell-to-cell across all of it, Ctrl+Up/Down jump between sections, Space on a stat/feature/class
    /// drills into its tooltip. We mirror what the detailed view actually binds — so e.g. Size, which the
    /// VM builds but the view doesn't show, is omitted. Sections that are empty for this character (no DR,
    /// no spellbook, …) add no region. The presentation is the swappable <see cref="ICharSheetSink"/>.
    /// </summary>
    public sealed class TotalPhaseContent : CharGenPhaseContent<CharGenTotalPhaseVM>
    {
        // The accessible presentation — the graph char-sheet sink (one sheet stop, a region per
        // section), created per render in Build.
        private ICharSheetSink _sink;

        public TotalPhaseContent(CharGenTotalPhaseVM phase) : base(phase) { }

        private static UITextCharSheet S => UIStrings.Instance.CharacterSheet;

        public override void Build(WrathAccess.UI.Graph.GraphBuilder b, string k)
        {
            _sink = new GraphCharSheetSink(b, k + "total:");
            BuildSummary();

            // Ability Scores — Score + Modifier as a grid (no localized group title; the game shows an
            // unlabeled block).
            if (Phase.AbilityScores?.AbilityScores != null)
            {
                var g = new StatGroup(Loc.T("section.ability_scores"), Loc.T("col.score"), Loc.T("col.modifier"));
                foreach (var a in Phase.AbilityScores.AbilityScores) g.Row(CharInfoStatRows.Ability(a));
                _sink.StatGroup(g);
            }

            // Skills — Rank + Modifier as a grid.
            if (Phase.SkillBlock?.Skills != null)
            {
                var g = new StatGroup((string)S.Skills, "Rank", "Modifier");
                foreach (var sk in Phase.SkillBlock.Skills) g.Row(CharInfoStatRows.Skill(sk));
                _sink.StatGroup(g);
            }

            BuildDefense();
            BuildAttack();
            BuildCombat();
            BuildClasses();
            BuildFeatures();
            BuildSpells();
            BuildWeaponProficiency();
            BuildDamageReduction();
            BuildEnergyResistance();

            _sink.Finish();
        }

        // Race / gender / alignment and HP — free-form lines, each with the game's tooltip.
        private void BuildSummary()
        {
            var items = new List<NodeVtable>();
            var rga = Phase.RaceGenderAlignment;
            if (rga != null)
            {
                items.Add(GraphNodes.Text(() => rga.RaceValue, () => rga.RaceTooltip));
                items.Add(GraphNodes.Text(() => rga.GenderValue, () => rga.GenderTooltip));
                items.Add(GraphNodes.Text(() => rga.AlignmentDisplayValue, () => rga.AlignmentTooltip));
            }
            if (Phase.HitPoints != null)
                items.Add(GraphNodes.Text(() => Labeled((string)S.HP, Phase.HitPoints.HpText.Value), () => Phase.HitPoints.Tooltip.Value));
            _sink.ListSection((string)S.Summary, items);
        }

        // AC / Touch / Flat-footed, the three saves, and Speed — single value each (a flat list).
        private void BuildDefense()
        {
            var g = new StatGroup((string)S.Defense);
            var ac = Phase.ArmorClass;
            if (ac != null)
            {
                g.Row(CharInfoStatRows.Value(ac.AC, signed: false));
                g.Row(CharInfoStatRows.Value(ac.Touch, signed: false));
                g.Row(CharInfoStatRows.Value(ac.FlatFooted, signed: false));
            }
            var st = Phase.SavingThrow;
            if (st != null)
            {
                g.Row(CharInfoStatRows.Value(st.Fortitude, signed: true));
                g.Row(CharInfoStatRows.Value(st.Reflex, signed: true));
                g.Row(CharInfoStatRows.Value(st.Will, signed: true));
            }
            if (Phase.Speed != null) g.Row(CharInfoStatRows.Value(Phase.Speed, signed: false));
            _sink.StatGroup(g);
        }

        // Base attack bonus — Main / Melee / Ranged, each an attack string like "+6/+1". CharInfoBABVM
        // was skipped by the decompiler; its public members (Type, BabValue, Tooltip) come from its view.
        private void BuildAttack()
        {
            var items = new List<NodeVtable>();
            AddBab(items, Phase.MainBAB, (string)S.BAB);
            AddBab(items, Phase.MeleeBAB, (string)S.BABMelee);
            AddBab(items, Phase.RangedBAB, (string)S.BABRanged);
            _sink.ListSection((string)S.Attack, items);
        }

        private void AddBab(List<NodeVtable> items, CharInfoBABVM bab, string label)
        {
            if (bab == null) return;
            items.Add(GraphNodes.Text(() => label + ", " + BabString(bab), () => bab.Tooltip));
        }

        // Mirrors CharInfoBABView.FillData: first attack always signed; later ones show "-" when <= 0.
        private static string BabString(CharInfoBABVM bab)
        {
            var vals = bab.BabValue;
            if (vals == null || vals.Count == 0) return "+0";
            var parts = new List<string>(vals.Count);
            for (int i = 0; i < vals.Count; i++)
                parts.Add((vals[i] <= 0 && i != 0) ? "-" : Signed(vals[i]));
            return string.Join("/", parts);
        }

        // Combat Maneuver (CMB / CMD), Initiative, Spell Resistance — single value each.
        private void BuildCombat()
        {
            var g = new StatGroup((string)S.MartialQualities);
            var cm = Phase.CombatManeuver;
            if (cm != null)
            {
                // The VM names CMB/CMD just "Bonus"/"Defense"; prefix so they're unambiguous out of context.
                g.Row(CharInfoStatRows.Value(cm.CMB, signed: true, nameOverride: (string)S.CombatManeuver + ", " + (string)S.Bonus));
                g.Row(CharInfoStatRows.Value(cm.CMD, signed: false, nameOverride: (string)S.CombatManeuver + ", " + (string)S.Defense));
            }
            if (Phase.Initiative != null) g.Row(CharInfoStatRows.Value(Phase.Initiative, signed: true, nameOverride: (string)S.Initiative));
            if (Phase.SpellResistance != null) g.Row(CharInfoStatRows.Value(Phase.SpellResistance, signed: false));
            _sink.StatGroup(g);
        }

        // Classes (and mythic) — "Name level", each drilling into the class tooltip.
        private void BuildClasses()
        {
            var vms = Phase.Classes?.ClassVMs;
            if (vms == null || vms.Count == 0) return;
            var items = new List<NodeVtable>();
            foreach (var c in vms)
            {
                var entry = c;
                items.Add(GraphNodes.Text(() => entry.ClassName + " " + entry.Level, () => entry.Tooltip));
            }
            _sink.ListSection((string)S.Class, items);
        }

        // Features, feats, traits (and, in chargen, newly-known spells) — grouped under headings in one
        // list; each feature drills into its full description tooltip.
        private void BuildFeatures()
        {
            var groups = Phase.Abilities?.ShowGroupList;
            if (groups == null) return;
            var items = new List<NodeVtable>();
            foreach (var group in groups)
            {
                if (group == null || group.IsEmpty) continue;
                if (!string.IsNullOrEmpty(group.Label)) items.Add(GraphNodes.Heading(() => group.Label));
                foreach (var f in group.FeatureList)
                {
                    var feat = f;
                    items.Add(GraphNodes.Text(() => FeatureName(feat), () => feat.Tooltip));
                }
            }
            _sink.ListSection((string)S.FeaturesAndAbilitites, items);
        }

        // Spells per day, by spellbook and level (cantrips are at-will). The known/prepared spells
        // themselves show under Features (the chargen "new spells" group) and the Spells phase.
        private void BuildSpells()
        {
            var books = Phase.SpellTables?.SpellbookTables;
            if (books == null || books.Count == 0) return;
            var items = new List<NodeVtable>();
            foreach (var book in books)
            {
                var b = book;
                items.Add(GraphNodes.Text(() => SpellbookLine(b)));
            }
            _sink.ListSection((string)S.Spells, items);
        }

        private void BuildWeaponProficiency()
        {
            var data = Phase.WeaponProficiency?.Data;
            if (data == null || data.Count == 0) return;
            var items = new List<NodeVtable>();
            foreach (var e in data)
            {
                var entry = e;
                items.Add(GraphNodes.Text(() => entry.DisplayName));
            }
            _sink.ListSection((string)S.WeaponProficiency, items);
        }

        private void BuildDamageReduction()
        {
            var data = Phase.DamageReduction?.Data;
            if (data == null || data.Count == 0) return;
            var items = new List<NodeVtable>();
            foreach (var e in data)
            {
                var entry = e;
                items.Add(GraphNodes.Text(() => entry.Value + "/" + string.Join(", ", entry.Exceptions.ToArray())));
            }
            _sink.ListSection((string)S.DamageReduction, items);
        }

        private void BuildEnergyResistance()
        {
            var data = Phase.EnergyResistance?.Data;
            if (data == null || data.Count == 0) return;
            var items = new List<NodeVtable>();
            foreach (var e in data)
            {
                var entry = e;
                items.Add(GraphNodes.Text(() => entry.Immunity ? entry.Type + ", immunity" : entry.Type + " " + entry.Value));
            }
            _sink.ListSection((string)S.EnergyRsistance, items);
        }

        private static string FeatureName(Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Abilities.CharInfoFeatureVM f)
        {
            // Match the visible badge: rank shows only when stacked (> 1).
            if (f.Rank.HasValue && f.Rank.Value > 1) return f.DisplayName + " " + f.Rank.Value;
            return f.DisplayName;
        }

        private static string SpellbookLine(Kingmaker.UI.MVVM._VM.ServiceWindows.MythicInfo.CharInfoSpellTableVM book)
        {
            var t = book.SpellTable;
            if (t == null || t.Count == 0) return book.SpellbookName;
            var parts = new List<string> { "cantrips " + Cantrips(t[0]) };
            for (int i = 1; i < t.Count; i++) parts.Add("level " + i + " " + t[i]);
            return book.SpellbookName + ": " + string.Join(", ", parts.ToArray());
        }

        // SpellTable[0] is the cantrip cell: an infinity sprite (at-will) or "-" (none). The sprite tag
        // would strip to nothing for speech, so translate it; leave "-" as-is.
        private static string Cantrips(string cell)
        {
            if (!string.IsNullOrEmpty(cell) && (cell.Contains("sprite") || cell.Contains("Infinity"))) return "at will";
            return string.IsNullOrEmpty(cell) ? "-" : cell;
        }

        private static string Labeled(string label, string value)
            => string.IsNullOrEmpty(value) ? label : label + ", " + value;

        private static string Signed(int v) => v >= 0 ? "+" + v : v.ToString();
    }
}
