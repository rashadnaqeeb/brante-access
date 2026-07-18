using System;
using System.Collections.Generic;
using Kingmaker.UI.MVVM._VM.CharGen.Phases;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Class;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Portrait;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Pregen;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Race;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.FeatureSelector;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.AbilityScores;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Skills;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Voice;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Alignment;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Name;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Spells;
using Kingmaker.UI.MVVM._VM.CharGen.Phases.Total;
using WrathAccess.UI;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Builds (and live-refreshes) the navigable content for one chargen phase. One subclass per
    /// phase VM type, created fresh on phase entry by <see cref="CharGenPhaseContentFactory"/> so it
    /// can hold per-entry state (panels it refreshes). Keeps each phase isolated; CharGenScreen just
    /// dispatches. Mirrors the "let the game drive, we wrap+activate its controls" model.
    /// </summary>
    public abstract class CharGenPhaseContent
    {
        /// <summary>Emit the phase's content (immediate mode — declared fresh from the live VM every
        /// render, so the old Tick()-driven refills are gone). <paramref name="k"/> is the phase-scoped
        /// key prefix from the wizard shell.</summary>
        public abstract void Build(WrathAccess.UI.Graph.GraphBuilder b, string k);
    }

    /// <summary>Typed base: reads the concrete phase VM without casting.</summary>
    public abstract class CharGenPhaseContent<TVM> : CharGenPhaseContent where TVM : CharGenPhaseBaseVM
    {
        protected readonly TVM Phase;
        protected CharGenPhaseContent(TVM phase) { Phase = phase; }
    }

    /// <summary>Maps a phase VM type to its content builder. Add a phase = write a class + Register.</summary>
    public static class CharGenPhaseContentFactory
    {
        private static readonly Dictionary<Type, Func<CharGenPhaseBaseVM, CharGenPhaseContent>> _map =
            new Dictionary<Type, Func<CharGenPhaseBaseVM, CharGenPhaseContent>>();

        static CharGenPhaseContentFactory() { RegisterDefaults(); }

        public static void Register<TVM>(Func<TVM, CharGenPhaseContent> create) where TVM : CharGenPhaseBaseVM
            => _map[typeof(TVM)] = vm => create((TVM)vm);

        /// <summary>The content builder for a phase, or null if we don't handle it yet (→ placeholder).</summary>
        public static CharGenPhaseContent Create(CharGenPhaseBaseVM phase)
        {
            if (phase != null && _map.TryGetValue(phase.GetType(), out var create)) return create(phase);
            return null;
        }

        private static void RegisterDefaults()
        {
            Register<CharGenPregenPhaseVM>(vm => new PregenPhaseContent(vm));
            Register<CharGenPortraitPhaseVM>(vm => new PortraitPhaseContent(vm));
            Register<CharGenClassPhaseVM>(vm => new ClassPhaseContent(vm));
            Register<CharGenRacePhaseVM>(vm => new RacePhaseContent(vm));
            Register<CharGenFeatureSelectorPhaseVM>(vm => new FeatureSelectorPhaseContent(vm));
            Register<CharGenAbilityScoresVM>(vm => new AbilityScoresPhaseContent(vm));
            Register<CharGenSkillsPhaseVM>(vm => new SkillsPhaseContent(vm));
            Register<CharGenVoicePhaseVM>(vm => new VoicePhaseContent(vm));
            Register<CharGenAlignmentPhaseVM>(vm => new AlignmentPhaseContent(vm));
            Register<CharGenNamePhaseVM>(vm => new NamePhaseContent(vm));
            Register<CharGenSpellsPhaseVM>(vm => new SpellsPhaseContent(vm));
            Register<CharGenTotalPhaseVM>(vm => new TotalPhaseContent(vm));
        }
    }
}
