using System.Collections.Generic;
using System.Text;
using System.Linq;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings; // UIStrings / UITextCharSheet (localized labels)
using Kingmaker.UI.Common; // UIUtility.AddSign, UIUtilityItem.AttackData
using Kingmaker.UI.MVVM._PCView.ServiceWindows.CharacterInfo; // CharInfoComponentType, CharInfoPageType (enums)
using Kingmaker.UI.MVVM._VM.ServiceWindows;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Abilities;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Alignment;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.BuffsAndConditions;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.LevelClassScores;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Martial;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Martial.Attack;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Martial.BAB;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Martial.Defence;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.NameAndPortrait;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Progression.Main; // UnitProgressionVM
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Skills;
using Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Stories;
using Kingmaker.UI.MVVM._VM.Tooltip.Templates; // TooltipTemplateGlossary (section-header glossary tooltips)
using WrathAccess.UI;
using WrathAccess.UI.CharSheet;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The in-game character sheet (CharacterInfo service window), graph-native. Page-based: a tab stop
    /// of the PC pages (Summary / Abilities / Martial / Progression / Biography) from
    /// <see cref="CharInfoMenuVM"/>, plus the current page's content built live from the component blocks
    /// the game populated (<c>CharacterInfoVM.ComponentVMs</c>) — stat rendering on the shared
    /// <see cref="GraphCharSheetSink"/>, the Progression page via <see cref="ProgressionGrid.Emit"/>.
    /// Content keys carry the page (and viewed unit), so a page switch re-keys content only (tab focus
    /// survives). Blocks render in the game's real per-page order/set via
    /// <see cref="CharInfoWindowUtility.GetComponentsList"/> (unit-type-aware); the two identical attack
    /// blocks the Martial page carries are de-duplicated. Escape closes the window.
    /// </summary>
    public sealed class CharacterInfoScreen : Screen
    {
        public override string Key => "service.Character";
        public override string ScreenName => Loc.T("screen.character");
        public override int Layer => 10;
        public override bool IsActive()
            => Game.Instance?.RootUiContext?.CurrentServiceWindow == ServiceWindowsType.CharacterInfo;

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => ServiceWindows()?.HandleCloseAll());
        }

        private static CharacterInfoVM Vm()
            => Game.Instance?.RootUiContext?.InGameVM?.StaticPartVM?.ServiceWindowsVM?.CharacterInfoVM?.Value;

        private static ServiceWindowsVM ServiceWindows()
            => Game.Instance?.RootUiContext?.InGameVM?.StaticPartVM?.ServiceWindowsVM;


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "chinfo:" + vm.GetHashCode() + ":";

            // Page tabs — the game's page radio group; activating selects the page (the content below
            // re-keys). Keyed by entity, so tab focus survives page switches.
            var entities = vm.CharInfoMenuVM?.SelectionGroup?.EntitiesCollection;
            if (entities != null)
            {
                b.BeginStop("tabs").PushContext(Loc.T("char.pages"), "list");
                int ti = 0;
                foreach (var e in entities)
                {
                    if (e == null) continue;
                    var ent = e;
                    b.AddItem(ControlId.Referenced(ent, k + "tab:" + ti),
                        GraphNodes.Tab(() => PageName(ent.PageType), () => ent.IsSelected.Value,
                            () => ent.SetSelectedFromView(true)));
                    ti++;
                }
                b.PopContext();
            }

            // The current page's blocks, in the game's real order. Keys carry page + viewed unit, so a
            // page switch (or inspecting another unit) re-keys the content.
            var page = vm.CurrentPage?.Value ?? CharInfoPageType.None;
            var unit = vm.UnitDescriptor?.Value;
            string pk = k + page + ":" + (unit != null ? unit.GetHashCode().ToString() : "?") + ":";
            b.BeginStop("content");
            var sink = new GraphCharSheetSink(b, pk);
            bool attacksShown = false; // Martial has AttackMain + AttackMartial, same VM/data — show once
            UnitProgressionVM progression = null; // its own stop below, not a sink section
            foreach (var type in PageComponents(vm))
            {
                if (!vm.ComponentVMs.TryGetValue(type, out var rp) || rp?.Value == null) continue;
                var block = rp.Value;
                if (block is CharInfoAttacksBlockVM)
                {
                    if (attacksShown) continue;
                    attacksShown = true;
                }
                if (block is UnitProgressionVM prog) progression = prog;
                else RenderBlock(type, block, sink);
            }
            sink.Finish();

            if (progression != null)
            {
                b.BeginStop("progression");
                ProgressionGrid.Emit(b, pk + "prog:", progression, null,
                    new ProgressionGrid.Options { AllClassBands = true });
            }
        }

        // The blocks shown on the current page, in the game's real order. CharInfoWindowUtility.PagesContent
        // is the source of truth (decoded from its static cctor): pages Summary/Abilities/Martial/Progression
        // all lead with the persistent summary panel [NameAndPortrait, LevelClassScores, AttackMain,
        // DefenceMain] then add page-specific blocks; the set is also unit-type-aware (Summary appends
        // AlignmentWheel for the PC, Stories for a companion, Martial for a pet). We call the game's own
        // GetComponentsList rather than a hand-built order so it stays faithful (e.g. Biography is
        // NameFullPortrait → AlignmentWheel → history, not the enum order).
        private static List<CharInfoComponentType> PageComponents(CharacterInfoVM vm)
        {
            var unit = vm.UnitDescriptor?.Value;
            if (unit == null) return new List<CharInfoComponentType>();
            var page = vm.CurrentPage?.Value ?? CharInfoPageType.None;
            return CharInfoWindowUtility.GetComponentsList(page, unit) ?? new List<CharInfoComponentType>();
        }

        private static void RenderBlock(CharInfoComponentType type, CharInfoComponentVM block, ICharSheetSink sink)
        {
            switch (block)
            {
                case CharInfoNameAndPortraitVM np: CharSheetBlocks.NamePortrait(np, sink); break;
                case CharInfoLevelClassScoresVM lcs: CharSheetBlocks.LevelClassScores(lcs, sink, withLevelUp: true); break;
                case CharInfoAttacksBlockVM atk: CharSheetBlocks.Attacks(atk, sink); break;
                case CharInfoDefenceBlockVM def: CharSheetBlocks.Defence(def, sink); break;
                case CharInfoSkillsBlockVM sk: RenderSkills(sk, sink); break;
                case CharInfoAbilitiesVM ab: RenderFeatureGroups(ab.ShowGroupList, "Abilities", sink); break;
                case CharInfoBuffsAndConditionsVM bc: RenderFeatureGroups(bc.ShowGroupList, "Buffs and conditions", sink); break;
                case CharInfoMartialVM mt: RenderMartial(mt, sink); break;
                case CharInfoAlignmentVM al: RenderAlignment(type, al, sink); break;
                case CharInfoStoriesVM st: RenderStories(st, sink); break;
                default: sink.ListSection(type.ToString(), new[] { GraphNodes.Text(() => Loc.T("char.not_shown")) }); break;
            }
        }

        // CharInfoAlignmentVM backs three blocks: the wheel (current alignment + mythic level) and the
        // history list (AlignmentHistory + the Biography page's copy). We mirror each view's bound text —
        // the wheel shows GetAlignmentName/AlignmentUndetectable + MythicLevel (Deity/BirthDay are computed
        // but read by no view, so omitted); the history shows "Alignment shifted <direction>: <description>".
        private static void RenderAlignment(CharInfoComponentType type, CharInfoAlignmentVM al, ICharSheetSink sink)
        {
            if (type == CharInfoComponentType.AlignmentWheel)
            {
                var items = new List<NodeVtable>();
                items.Add(GraphNodes.Text(() => Loc.T("char.alignment", new { value = AlignmentText(al) })));
                if (!string.IsNullOrEmpty(al.MythicLevel)) items.Add(GraphNodes.Text(() => Loc.T("char.mythic", new { value = al.MythicLevel })));
                sink.ListSection("Alignment", items);
                return;
            }
            // History (AlignmentHistory / BiographyAlignmentHistory).
            var hist = al.AlignmentHistory;
            if (hist == null || hist.Count == 0) return;
            var lines = new List<NodeVtable>();
            string shifted = (string)S.AlignmentShifted;
            foreach (var rec in hist)
            {
                var r = rec;
                lines.Add(GraphNodes.Text(() => shifted + " " + (string)UIUtility.GetAlignmentShiftDirectionText(r.Direction)
                    + (string.IsNullOrEmpty(r.Description) ? "" : ": " + r.Description)));
            }
            sink.ListSection((string)S.History, lines);
        }

        private static string AlignmentText(CharInfoAlignmentVM al)
            => al.IsUndetectable ? (string)S.AlignmentUndetectable : (string)UIUtility.GetAlignmentName(al.CurrentAlignment);

        // Companion stories — each a title (heading) + its text. Matches CharInfoStoriesVM.Stories.
        private static void RenderStories(CharInfoStoriesVM st, ICharSheetSink sink)
        {
            if (st.Stories == null || st.Stories.Count == 0) return;
            var items = new List<NodeVtable>();
            foreach (var story in st.Stories)
            {
                var s = story;
                if (!string.IsNullOrEmpty(s.Title)) items.Add(GraphNodes.Heading(() => s.Title));
                if (!string.IsNullOrEmpty(s.StoryText)) items.Add(GraphNodes.Text(() => s.StoryText));
            }
            if (items.Count > 0) sink.ListSection("Stories", items);
        }

        private static void RenderSkills(CharInfoSkillsBlockVM sk, ICharSheetSink sink)
        {
            if (sk.Skills == null) return;
            var g = new StatGroup(Loc.T("section.skills"), Loc.T("col.rank"), Loc.T("col.modifier"));
            foreach (var s in sk.Skills) g.Row(CharInfoStatRows.Skill(s));
            sink.StatGroup(g);
        }

        private static UITextCharSheet S => UIStrings.Instance.CharacterSheet;

        // The Martial page's composite block. We mirror CharInfoMartialPCView.RefreshView's bind order
        // exactly (it does NOT bind the VM's DefenceBlock — defence shows as its own DefenceMain block):
        // BAB (main/melee/ranged), Initiative, Spell Resistance, Combat Maneuver, Weapon Proficiency,
        // Damage Reduction, Energy Resistance. BAB/proficiency/DR/resistance reuse the chargen patterns.
        private static void RenderMartial(CharInfoMartialVM m, ICharSheetSink sink)
        {
            var bab = new List<NodeVtable>();
            AddBab(bab, m.MainBab, (string)S.BAB);
            AddBab(bab, m.MeleeBab, (string)S.BABMelee);
            AddBab(bab, m.RangedBab, (string)S.BABRanged);
            if (bab.Count > 0) sink.ListSection((string)S.Attack, bab);

            var g = new StatGroup((string)S.MartialQualities);
            g.Row(CharInfoStatRows.Value(m.Initiative, signed: true));
            g.Row(CharInfoStatRows.Value(m.SpellResistance, signed: false));
            var cm = m.CombatManeuver;
            if (cm != null)
            {
                // The VM names CMB/CMD just "Bonus"/"Defense"; prefix so they're unambiguous out of context.
                g.Row(CharInfoStatRows.Value(cm.CMB, signed: true, nameOverride: (string)S.CombatManeuver + ", " + (string)S.Bonus));
                g.Row(CharInfoStatRows.Value(cm.CMD, signed: false, nameOverride: (string)S.CombatManeuver + ", " + (string)S.Defense));
            }
            sink.StatGroup(g);

            // Each of these sections' only tooltip in-game is a glossary on its header label
            // (m_Label.SetGlossaryTooltip — "WeaponProficiency"/"DR"/"ER"); the entries themselves carry no
            // per-row tooltip and their per-feature description is deliberately not rendered (m_Description
            // unwired). The FlowSheet has no focusable header, so we surface the category glossary as each
            // entry's drill-in (the real glossary key, not invented text).
            var wp = m.WeaponProficiency?.Data;
            if (wp != null && wp.Count > 0)
            {
                var items = new List<NodeVtable>();
                foreach (var e in wp) { var entry = e; items.Add(GraphNodes.Text(() => entry.DisplayName, () => new TooltipTemplateGlossary("WeaponProficiency"))); }
                sink.ListSection((string)S.WeaponProficiency, items);
            }

            var dr = m.DamageReduction?.Data;
            if (dr != null && dr.Count > 0)
            {
                var items = new List<NodeVtable>();
                foreach (var e in dr) { var entry = e; items.Add(GraphNodes.Text(() => entry.Value + "/" + string.Join(", ", entry.Exceptions.ToArray()), () => new TooltipTemplateGlossary("DR"))); }
                sink.ListSection((string)S.DamageReduction, items);
            }

            var er = m.EnergyResistance?.Data;
            if (er != null && er.Count > 0)
            {
                var items = new List<NodeVtable>();
                foreach (var e in er) { var entry = e; items.Add(GraphNodes.Text(() => entry.Immunity ? entry.Type + ", immunity" : entry.Type + " " + entry.Value, () => new TooltipTemplateGlossary("ER"))); }
                sink.ListSection((string)S.EnergyRsistance, items);
            }
        }

        private static void AddBab(List<NodeVtable> items, CharInfoBABVM bab, string label)
        {
            if (bab == null) return;
            items.Add(GraphNodes.Text(() => label + ", " + BabString(bab), () => bab.Tooltip));
        }

        // Mirrors CharInfoBABView.FillData: first attack always signed; later ones show "-" when <= 0.
        internal static string BabString(CharInfoBABVM bab)
        {
            var vals = bab.BabValue;
            if (vals == null || vals.Count == 0) return "+0";
            var parts = new List<string>(vals.Count);
            for (int i = 0; i < vals.Count; i++)
                parts.Add((vals[i] <= 0 && i != 0) ? "-" : (vals[i] >= 0 ? "+" + vals[i] : vals[i].ToString()));
            return string.Join("/", parts);
        }

        // Features / feats / special abilities / buffs — grouped under headings; each drills into its tooltip.
        // Same shape as chargen's BuildFeatures (CharInfoFeatureGroupVM list). Abilities and Buffs &
        // Conditions both render this way (both expose ShowGroupList of CharInfoFeatureGroupVM).
        private static void RenderFeatureGroups(List<CharInfoFeatureGroupVM> groups, string label, ICharSheetSink sink)
        {
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
            if (items.Count > 0) sink.ListSection(label, items);
        }

        private static string FeatureName(CharInfoFeatureVM f) // rank shows only when stacked (>1), like the badge
            => f.Rank.HasValue && f.Rank.Value > 1 ? f.DisplayName + " " + f.Rank.Value : f.DisplayName;

        private static string PageName(CharInfoPageType p)
        {
            switch (p)
            {
                case CharInfoPageType.SummaryPC: return "Summary";
                case CharInfoPageType.AbilitiesPC: return "Abilities";
                case CharInfoPageType.MartialPC: return "Martial";
                case CharInfoPageType.ProgressionPC: return "Progression";
                case CharInfoPageType.BiographyPC: return "Biography";
                default: return p.ToString();
            }
        }
    }
}
