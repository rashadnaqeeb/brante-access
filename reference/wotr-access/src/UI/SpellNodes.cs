using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.GameModes; // GameModeType
using Kingmaker.PubSubSystem; // EventBus, ISlotWasAddedHandler
using Kingmaker.UI.MVVM._VM.ServiceWindows.Spellbook; // SpellbookVM, ISpellbookHandler
using Kingmaker.UI.MVVM._VM.ServiceWindows.Spellbook.KnownSpells; // AbilityDataVM
using Kingmaker.UI.MVVM._VM.ServiceWindows.Spellbook.MemorizingPanel; // SpellbookMemorizeSlotVM
using Kingmaker.UI.MVVM._VM.ServiceWindows.Spellbook.Metamagic; // SpellbookMetamagicSlotVM, SpellbookSpellLevelSelectorVM
using Kingmaker.UI.UnitSettings; // MechanicActionBarSlot*
using Kingmaker.UnitLogic; // UnitDescriptor
using Owlcat.Runtime.UniRx; // DelayedInvoker
using WrathAccess.UI.Graph;

namespace WrathAccess.UI
{
    /// <summary>
    /// Node factories for the spellbook family (the old ProxyKnownSpell / ProxyMemorizeSlot /
    /// ProxyMetamagicToggle / ProxyMetamagicLevel, factory-shaped). Actions ride the game's own
    /// contracts: ISpellbookHandler memorize/forget, the MechanicActionBarSlot builders SpellbookVM's
    /// own TryContext uses for cast/add-to-bar, and the metamagic builder's VM methods. Spell badges
    /// (domain/mythic/opposition) are localized (the proxy carried raw English — fixed in the port).
    /// </summary>
    public static class SpellNodes
    {
        /// <summary>One known spell: name with badges, its tooltip on Space, Enter = memorize (the game's
        /// double-click; no-op for spontaneous casters), Backspace = the game's right-click set (Cast when
        /// castable now / Add to action bar / Apply metamagic when the book supports it).</summary>
        public static NodeVtable KnownSpell(AbilityDataVM vm, UnitDescriptor unit)
        {
            Func<string> label = () =>
            {
                var name = vm.DisplayName;
                var flags = new List<string>();
                if (vm.IsDomain) flags.Add(Loc.T("spell.domain"));
                if (vm.IsMythic) flags.Add(Loc.T("spell.mythic"));
                if (vm.IsOpposite) flags.Add(Loc.T("spell.opposition"));
                return flags.Count > 0 ? name + " (" + string.Join(", ", flags.ToArray()) + ")" : name;
            };
            return new NodeVtable
            {
                ControlType = ControlTypes.Spell,
                Announcements = new[] { GraphNodes.LabelPart(label) },
                SearchText = () => vm.DisplayName,
                OnActivate = () => EventBus.RaiseEvent<ISpellbookHandler>(h => h.TryMemorize(vm)),
                OnSecondary = () => OpenSpellMenu(vm, unit),
                OnTooltip = () => { if (vm.Tooltip != null) Screens.TooltipScreen.Open(vm.Tooltip); },
            };
        }

        // The spell's action menu — the game's right-click set, each entry gated live.
        private static void OpenSpellMenu(AbilityDataVM vm, UnitDescriptor unit)
        {
            var labels = new List<string>();
            var runs = new List<Action>();
            void Add(bool when, string label, Action run) { if (when) { labels.Add(label); runs.Add(run); } }

            Add(CanCast(vm, unit), Loc.T("spellbook.cast"), () => Cast(vm, unit));
            Add(NotKingdomOrMap, Loc.T("spellbook.add_to_bar"), () => AddToActionBar(vm, unit));
            Add(Book()?.MetamagicAvailable.Value ?? false, Loc.T("metamagic.apply"), () => ApplyMetamagic(vm));

            if (labels.Count == 0) { Tts.Speak(Loc.T("menu.no_actions"), interrupt: true); return; }
            var actions = runs;
            Screens.ChoiceSubmenuScreen.Open(vm.DisplayName, labels, -1,
                idx => { if (idx >= 0 && idx < actions.Count) actions[idx]?.Invoke(); });
        }

        private static SpellbookVM Book()
            => Game.Instance?.RootUiContext?.InGameVM?.StaticPartVM?.ServiceWindowsVM?.SpellbookVM?.Value;

        // Enter the metamagic builder for this spell: select it, flip the builder mode (the SpellbookVM
        // creates the mixer from the selection), and land focus on the builder content.
        private static void ApplyMetamagic(AbilityDataVM vm)
        {
            var book = Book();
            if (book == null) return;
            book.CurrentSelectedSpell.Value = vm;
            book.MetamagicBuilderMode.Value = true;
            Navigation.FocusStop("mixer");
        }

        // The spell's mechanic action-bar slot — memorized / spontaneous / converted by the same rules the
        // game's SpellbookVM.TryContext uses. Shared by Cast and Add to action bar.
        private static MechanicActionBarSlot BuildSlot(AbilityDataVM vm, UnitDescriptor unit)
        {
            var spell = vm.SpellData;
            if (spell == null || unit == null) return null;
            return (!spell.Blueprint.IsCantrip && !spell.IsSpontaneous)
                ? ((!spell.IsVariable && !spell.GetConversions().Any())
                    ? (MechanicActionBarSlot)new MechanicActionBarSlotMemorizedSpell(spell.SpellSlot) { Unit = unit }
                    : new MechanicActionBarSlotSpontaneusConvertedSpell { Spell = spell, Unit = unit })
                : new MechanicActionBarSlotSpontaneousSpell(spell) { Unit = unit };
        }

        private static bool NotKingdomOrMap =>
            !Game.Instance.IsModeActive(GameModeType.Kingdom) && !Game.Instance.IsModeActive(GameModeType.GlobalMap);

        // Castable right now: a free/available slot for it, the spell available, and not Kingdom/global-map.
        private static bool CanCast(AbilityDataVM vm, UnitDescriptor unit)
        {
            if (!NotKingdomOrMap || vm.SpellData == null || !vm.SpellData.IsAvailable) return false;
            var slot = BuildSlot(vm, unit);
            return slot != null && slot.IsPossibleActive();
        }

        // Mirror SpellbookVM.ContextCastAbility: close the window, then (next frame) click the slot — which
        // sets up the cast / world-cursor targeting, exactly like clicking the spell on the action bar.
        private static void Cast(AbilityDataVM vm, UnitDescriptor unit)
        {
            var slot = BuildSlot(vm, unit);
            if (slot == null) return;
            EventBus.RaiseEvent<INewServiceWindowUIHandler>(h => h.HandleCloseAll());
            DelayedInvoker.InvokeInFrames(slot.OnClick, 1); // after the window closes, like the game's DelayedCast
        }

        private static void AddToActionBar(AbilityDataVM vm, UnitDescriptor unit)
        {
            var slot = BuildSlot(vm, unit);
            if (slot == null) return;
            unit.UISettings.SetSlotInternal(slot);
            EventBus.RaiseEvent<ISlotWasAddedHandler>(h => h.SlotWasAdded(unit));
            Tts.Speak(Loc.T("spellbook.added_to_bar"), interrupt: false);
        }

        /// <summary>One memorize slot: the prepared spell (or "empty slot"), "needs rest" while spent /
        /// awaiting rest (LIVE), its tooltip when filled, and Enter = forget (the game's double-click).
        /// Empty slots are filled from the known list, so they carry no action.</summary>
        public static NodeVtable MemorizeSlot(SpellbookMemorizeSlotVM vm)
        {
            Func<bool> filled = () => vm?.SpellData != null;
            Func<string> label = () => filled() ? vm.DisplayName : Loc.T("spellbook.empty_slot");
            return new NodeVtable
            {
                ControlType = ControlTypes.Spell,
                Announcements = new[]
                {
                    GraphNodes.LabelPart(label),
                    new NodeAnnouncement(() => filled() && vm.NeedRestToRestore ? Loc.T("spellbook.needs_rest") : null,
                        live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = label,
                OnActivate = () => { if (filled()) EventBus.RaiseEvent<ISpellbookHandler>(h => h.TryForget(vm)); },
                OnTooltip = () => { if (filled() && vm.Tooltip != null) Screens.TooltipScreen.Open(vm.Tooltip); },
            };
        }

        /// <summary>One metamagic feat in the builder — a toggle ("Feat (+N), toggle, on/off") applying/
        /// removing it on the spell being built; carries the feat's tooltip.</summary>
        public static NodeVtable MetamagicToggle(SpellbookMetamagicSlotVM vm)
        {
            Func<string> label = () => (vm.Feature?.Name ?? "") + " (+" + vm.Cost + ")"; // +N spell levels
            Func<string> value = () => Loc.T(vm.IsSelected ? "value.on" : "value.off");
            return new NodeVtable
            {
                ControlType = ControlTypes.Toggle,
                Announcements = new[]
                {
                    GraphNodes.LabelPart(label),
                    new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = () => vm.Feature?.Name ?? "",
                StateText = value,
                OnActivate = () =>
                {
                    UiSound.Play(Kingmaker.UI.UISoundType.SettingsSwitchToggle);
                    vm.OnSelect();
                },
                OnTooltip = () => { if (vm.Tooltip != null) Screens.TooltipScreen.Open(vm.Tooltip); },
            };
        }

        /// <summary>The Heighten level stepper — a slider over the result spell level, stepping within the
        /// caster's range (only present when Heighten Spell is applied).</summary>
        public static NodeVtable MetamagicLevel(SpellbookSpellLevelSelectorVM vm)
        {
            Func<string> value = () => vm.ResultSpellLevel.Value.ToString();
            return new NodeVtable
            {
                ControlType = ControlTypes.Slider,
                Announcements = new[]
                {
                    GraphNodes.LabelPart(() => Loc.T("metamagic.spell_level")),
                    new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = () => Loc.T("metamagic.spell_level"),
                StateText = value,
                OnAdjust = (sign, large) =>
                {
                    if (sign > 0 && vm.CanIncrease) vm.IncreaseSpellLevel();
                    else if (sign < 0 && vm.CanDecrease) vm.DecreaseSpellLevel();
                },
            };
        }
    }
}
