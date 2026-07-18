using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints.Classes.Spells; // CantripsType
using Kingmaker.Blueprints.Root.Strings; // UIStrings (spellbook labels)
using Kingmaker.UI.MVVM._VM.ServiceWindows;
using Kingmaker.UI.MVVM._VM.ServiceWindows.Spellbook; // SpellbookVM
using Kingmaker.UI.MVVM._VM.ServiceWindows.Spellbook.KnownSpells; // AbilityDataVM
using Kingmaker.UI.MVVM._VM.ServiceWindows.Spellbook.MemorizingPanel; // SpellbookMemorizingPanelVM, SpellbookMemorizeSlotVM
using WrathAccess.UI;
using WrathAccess.UI.CharSheet;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The spellbook service window (<see cref="SpellbookVM"/>), graph-native: a character switcher, the
    /// spellbook switcher (multiclass casters), the caster characteristics, the spell-level switcher, the
    /// known spells for the current level (a table with the School column; Enter memorizes, Backspace =
    /// Cast / Add to action bar / Apply metamagic), and the memorizing panel (prepared casters: forget on
    /// Enter, "needs rest" state, the spontaneous/cantrip substitute line). The METAMAGIC BUILDER is a
    /// wholly different content set on the same screen: entering it (via a spell's menu) swaps the content
    /// stops and lands focus on the builder (<c>FocusStop("mixer")</c>); Back leaves the builder first,
    /// then closes the window. Everything renders live; level-dependent sections key by unit + book +
    /// level, so switching re-keys them while the switchers keep focus. Escape closes.
    /// </summary>
    public sealed class SpellbookScreen : Screen
    {
        public override string Key => "service.Spellbook";
        public override string ScreenName => Loc.T("screen.spellbook");
        public override int Layer => 10;
        public override bool IsActive()
            => Game.Instance?.RootUiContext?.CurrentServiceWindow == ServiceWindowsType.Spellbook;

        public override IEnumerable<ElementAction> GetActions()
        {
            // In the metamagic builder, Back leaves the builder (not the whole window).
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ =>
            {
                var vm = Vm();
                if (vm != null && vm.MetamagicBuilderMode.Value)
                {
                    vm.MetamagicBuilderMode.Value = false;
                    Navigation.FocusStop("known"); // back to the spell list you built from
                }
                else ServiceWindows()?.HandleCloseAll();
            });
        }

        private static bool MixerActive(SpellbookVM vm)
            => vm.MetamagicBuilderMode.Value && vm.SpellbookMetamagicMixerVM != null;

        private static SpellbookVM Vm()
            => Game.Instance?.RootUiContext?.InGameVM?.StaticPartVM?.ServiceWindowsVM?.SpellbookVM?.Value;

        private static ServiceWindowsVM ServiceWindows()
            => Game.Instance?.RootUiContext?.InGameVM?.StaticPartVM?.ServiceWindowsVM;


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            var unit = Game.Instance?.SelectionCharacter?.SelectedUnit?.Value.Value;
            string k = "spellbook:" + vm.GetHashCode() + ":";
            string uk = k + (unit != null ? unit.CharacterName : "?") + ":";

            // Character switcher — switching the selected unit re-keys everything below it.
            var party = Game.Instance?.Player?.Party;
            if (party != null && party.Count > 0)
            {
                b.BeginStop("chars").PushContext(Loc.T("label.characters"), "list");
                int ci = 0;
                foreach (var u in party)
                {
                    var un = u;
                    b.AddItem(ControlId.Referenced(un, k + "char:" + ci),
                        GraphNodes.Button(() => un.CharacterName,
                            () => Game.Instance.SelectionCharacter.SetSelected(un)));
                    ci++;
                }
                b.PopContext();
            }

            if (!vm.HasSpellbooks.Value)
            {
                b.BeginStop("none").AddItem(ControlId.Structural(uk + "none"),
                    GraphNodes.Text(() => Loc.T("spell.none")));
                return;
            }

            if (MixerActive(vm)) { BuildMixer(b, vm, uk); return; }

            // Spellbook (caster) switcher — only when there's more than one.
            var books = vm.SpellbookSwitcherVM;
            if (books != null && books.HasMoreThanOneSpellbooks.Value && books.SelectionGroup?.EntitiesCollection != null)
            {
                b.BeginStop("books").PushContext(Loc.T("spell.spellbooks"), "list");
                b.StartRow();
                int bi = 0;
                foreach (var e in books.SelectionGroup.EntitiesCollection)
                {
                    if (e == null) continue;
                    var ent = e;
                    b.AddItem(ControlId.Referenced(ent, uk + "book:" + bi),
                        GraphNodes.SelectionItem(ent, () => ent.BookName));
                    bi++;
                }
                b.EndRow();
                b.PopContext();
            }

            string bk = uk + (vm.CurrentSpellbook?.Value?.Blueprint?.name ?? "?") + ":";

            // Caster characteristics.
            var ch = vm.SpellbookCharacteristicsVM;
            if (ch != null)
            {
                b.BeginStop("caster");
                var sink = new GraphCharSheetSink(b, bk + "caster:");
                var g = new StatGroup(Loc.T("spell.caster"));
                g.Row(CharInfoStatRows.Value(ch.CasterLevel, signed: false));
                g.Row(CharInfoStatRows.Value(ch.Concentration, signed: true));
                g.Row(CharInfoStatRows.Value(ch.SpellPenetration, signed: true));
                g.Row(CharInfoStatRows.Value(ch.SpellFailureChance, signed: false));
                sink.StatGroup(g);
                sink.Finish();
            }

            // Spell-level switcher (only levels that have spells).
            var levels = vm.SpellbookLevelSwitcherVM?.SelectionGroup?.EntitiesCollection;
            if (levels != null)
            {
                // A vertical list (user preference): Down walks the levels like any other list.
                b.BeginStop("levels").PushContext(Loc.T("metamagic.spell_level"), "list");
                int li = 0;
                foreach (var e in levels)
                {
                    if (e == null || !e.IsAvailable.Value) { li++; continue; }
                    var ent = e;
                    b.AddItem(ControlId.Referenced(ent, bk + "lvl:" + li),
                        GraphNodes.SelectionItem(ent, () => LevelName(ent.SpellbookLevel.Level)));
                    li++;
                }
                b.PopContext();
            }

            string lk = bk + (vm.CurrentSpellbookLevel?.Value?.Level ?? -1) + ":";
            BuildKnownSpells(b, vm, lk);
            BuildMemorize(b, vm, lk);
        }

        // The known spells at the current level: a table whose rows key by spell VM (memorizing/forgetting
        // re-deals cleanly); School is a value column carried as a part too.
        private static void BuildKnownSpells(GraphBuilder b, SpellbookVM vm, string lk)
        {
            var known = vm.SpellbookKnownSpellsVM?.KnownSpells;
            var unit = vm.UnitDescriptor?.Value;
            b.BeginStop("known");
            var sheet = new GraphSheet(b, lk + "known:");
            sheet.Region(WithLevel(Loc.T("spell.spells"), vm), new[] { Loc.T("col.school") });
            bool any = false;
            if (known != null)
                foreach (var spell in known)
                {
                    if (spell == null) continue;
                    any = true;
                    var s = spell;
                    var vt = SpellNodes.KnownSpell(s, unit);
                    var anns = new List<NodeAnnouncement>(vt.Announcements)
                    {
                        new NodeAnnouncement(() => s.SchoolName), // the school reads with the row
                    };
                    vt.Announcements = anns;
                    sheet.Row(vt, s, () => s.SchoolName);
                }
            if (!any) sheet.Line(GraphNodes.Text(() => Loc.T("spell.none_at_level")));
            sheet.Finish();
        }

        // The memorizing panel (prepared casters): the special (domain/favorite) and common memorized
        // slots for the current level, plus a spells-per-day / status readout.
        private static void BuildMemorize(GraphBuilder b, SpellbookVM vm, string lk)
        {
            var panel = vm.SpellbookMemorizingPanelVM;
            if (panel == null) return;
            b.BeginStop("memorize").PushContext(WithLevel(Loc.T("spell.memorize"), vm), "list", positions: false);

            // Like the game: show the memorize slots when there are any at this level, else a substitute
            // line (spontaneous spells-per-day, cantrips "cast at will", not-enough-stat/level, …).
            bool hasSlots = panel.IsCorrectLevelValue && panel.HasAnySlot;
            if (hasSlots)
            {
                if (panel.HasSpecialSlots && panel.SpecialMemorizedSpells != null)
                    EmitSlots(b, panel.SpecialMemorizedSpells, SpecialLabel(panel), lk + "special:");
                if (panel.HasCommonSlots && panel.CommonMemorizedSpells != null)
                    EmitSlots(b, panel.CommonMemorizedSpells,
                        (string)UIStrings.Instance.SpellBookTexts.MemorizedSpells, lk + "common:");
            }
            else
            {
                // Live: the spontaneous count changes on cast.
                b.AddItem(ControlId.Structural(lk + "substitute"), GraphNodes.Text(() => SubstituteText(panel)));
            }
            if (panel.NeedToSleep)
                b.AddItem(ControlId.Structural(lk + "needsleep"),
                    GraphNodes.Text(() => (string)UIStrings.Instance.SpellBookTexts.NeedToSleep));
            b.PopContext();
        }

        private static void EmitSlots(GraphBuilder b, List<SpellbookMemorizeSlotVM> slots, string label, string key)
        {
            b.PushContext(label);
            int i = 0;
            foreach (var s in slots)
            {
                if (s != null)
                    b.AddItem(ControlId.Referenced(s, key + i), SpellNodes.MemorizeSlot(s));
                i++;
            }
            b.PopContext();
        }

        // Mirrors SpellbookMemorizingPanelView.SetupSubstituteText: the line shown in place of slots — the
        // spontaneous daily count, the cantrip/orison "cast at will" note, or a can't-cast/no-slots reason.
        private static string SubstituteText(SpellbookMemorizingPanelVM p)
        {
            var t = UIStrings.Instance.SpellBookTexts;
            if (p.IsCorrectLevelValue && !p.IsCantripLevel)
            {
                if (p.IsSpontaneous)
                {
                    if (p.NotEnoughStat) return string.Format((string)t.NotEnoughAbilityScore, p.CasterStat, p.CasterStatMin);
                    if (p.HasAnyKnownSpells) return string.Format((string)t.SpontaneuseSpellsPreDay, p.RemainingSpontaneousSpells, p.SpellsPerDay);
                    return (string)t.CanNotCastSpellsOfLevel;
                }
                if (!p.HasCommonSlots && !p.HasSpecialSlots)
                {
                    if (p.NotEnoughStat) return string.Format((string)t.NotEnoughAbilityScore, p.CasterStat, p.CasterStatMin);
                    if (p.NotEnoughLevel) return (string)t.CanNotCastSpellsOfLevel;
                    return (string)t.CharacterHasNotSlots;
                }
                return (string)t.CanNotCastSpellsOfLevel;
            }
            if (p.IsCorrectLevelValue && p.IsCantripLevel)
            {
                if (!p.HasCantrips) return (string)t.CanNotCastSpellsOfLevel;
                return p.CantripsType == CantripsType.Orisions ? (string)t.MemorizePanelOrisons : (string)t.MemorizePanelCantrips;
            }
            return (string)t.DontHaveSpellsInBook;
        }

        // The metamagic builder (entered via a known spell's "Apply metamagic"): the base spell, the known
        // metamagic feats as toggles, the Heighten level stepper (when applicable), the resulting spell
        // (name + level + whether it's castable), and "Write spell" — which creates the metamagic'd custom
        // spell and leaves the builder. Back leaves the builder. Keys carry the base spell, so building a
        // different spell re-keys the builder.
        private static void BuildMixer(GraphBuilder b, SpellbookVM vm, string uk)
        {
            var mixer = vm.SpellbookMetamagicMixerVM;
            var sel = mixer.SpellbookMetamagicSelector;
            var baseSpell = vm.CurrentSelectedSpell?.Value;
            string mk = uk + "mix:" + (baseSpell != null ? baseSpell.GetHashCode().ToString() : "?") + ":";

            b.BeginStop("mixer").PushContext(Loc.T("spell.metamagic"), role: null, positions: false);

            if (baseSpell != null)
            {
                var bs = baseSpell;
                b.AddItem(ControlId.Structural(mk + "base"), GraphNodes.Text(
                    () => Loc.T("spell.spell") + ", " + bs.DisplayName, () => bs.Tooltip));
            }

            var feats = sel?.MetamagicItems;
            if (feats != null && feats.Count > 0)
            {
                int fi = 0;
                foreach (var item in feats)
                {
                    if (item != null)
                        b.AddItem(ControlId.Referenced(item, mk + "feat:" + fi), SpellNodes.MetamagicToggle(item));
                    fi++;
                }
            }
            else
            {
                b.AddItem(ControlId.Structural(mk + "nofeats"), GraphNodes.Text(() => Loc.T("spell.no_metamagic")));
            }

            var lvl = sel?.SpellbookSpellLevelSelector;
            if (lvl != null && lvl.CanChangeLevel.Value)
                b.AddItem(ControlId.Structural(mk + "heighten"), SpellNodes.MetamagicLevel(lvl));

            // Live: the result level/castability change as metamagics toggle.
            b.AddItem(ControlId.Structural(mk + "result"), GraphNodes.Text(
                () => ResultLine(baseSpell, lvl?.ResultSpellLevel.Value ?? 0, lvl?.CanUseSpell ?? true)));
            // Always show Write (greyed when you can't write yet — no metamagic applied, or the result
            // level exceeds your castable slots), mirroring the game's Interactable = CanWriteSpell.
            b.AddItem(ControlId.Structural(mk + "write"), GraphNodes.Button(
                () => Loc.T("metamagic.write"),
                () => mixer.TryWriteNewSpell(),
                () => mixer.CanWriteSpell.Value));
            b.PopContext();
        }

        private static string ResultLine(AbilityDataVM baseSpell, int level, bool castable)
        {
            var s = Loc.T("metamagic.result_line", new { name = baseSpell?.DisplayName ?? "", level = LevelName(level) });
            if (!castable) s += " (" + Loc.T("metamagic.too_high") + ")";
            return s;
        }

        // The special-slots heading: an explicit name if the book sets one, else Domain / Favorite school.
        private static string SpecialLabel(SpellbookMemorizingPanelVM panel)
        {
            if (!string.IsNullOrEmpty(panel.SpecialSlotsName)) return panel.SpecialSlotsName;
            return panel.HasDomainSlots
                ? (string)UIStrings.Instance.SpellBookTexts.DomainSlots
                : (string)UIStrings.Instance.SpellBookTexts.FavoriteSchoolSlots;
        }

        private static string LevelName(int level)
            => level == 0 ? Loc.T("spell.cantrips") : Loc.T("spell.level", new { level });

        // The memorize/known sections are filtered to the current spell level (the level switcher drives
        // both), so put the level in their headers — otherwise it's only known from the switcher.
        private static string WithLevel(string label, SpellbookVM vm)
        {
            int lvl = vm.CurrentSpellbookLevel?.Value?.Level ?? -1;
            return lvl < 0 ? label : label + ", " + LevelName(lvl);
        }
    }
}
