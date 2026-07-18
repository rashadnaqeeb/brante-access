using Kingmaker.UI; // UISoundType
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// Shared graph-native shell for the game's phase-based wizards (New Game setup, character
    /// generation): an optional header (the chargen roadmap), the current phase's content under the
    /// phase name as context — keys carry the phase, so advancing re-keys the page — and Back/Next
    /// stops. A phase change plays the page-turn and lands focus on the new page's content
    /// (<c>FocusStop("content")</c>); opening lands there too (<see cref="InitialFocusStop"/>).
    /// Subclasses supply the VM, the current phase, the phase content, and the footer behaviour.
    /// </summary>
    public abstract class WizardScreen : Screen
    {
        protected WizardScreen() { Wrap = true; }

        public override bool IsActive() => WizardVm() != null;

        /// <summary>The wizard root VM, or null when inactive. Used for activity + change detection.</summary>
        protected abstract object WizardVm();

        /// <summary>The current phase object — compared by reference to detect phase changes.</summary>
        protected abstract object CurrentPhase();

        /// <summary>Label for the content (the current phase's name) — its context level.</summary>
        protected abstract string PhaseLabel();

        /// <summary>Emit the current phase's content. <paramref name="k"/> is the phase-scoped key
        /// prefix (carries the VM + phase, so a phase change re-keys the page).</summary>
        protected abstract void BuildContent(GraphBuilder b, string k);

        protected abstract void OnBack();
        protected abstract void OnNext();
        protected abstract string NextLabel();
        protected virtual bool NextEnabled() => true;
        protected virtual bool BackEnabled() => true;

        /// <summary>Optional content above the phase content — chargen uses it for the roadmap strip.</summary>
        protected virtual void BuildHeader(GraphBuilder b) { }

        /// <summary>Called each update while the phase is unchanged.</summary>
        protected virtual void OnPhaseTick() { }

        // Open landing goes to the phase content, not the header (the roadmap stays first in Tab order).
        public override object InitialFocusStop => "content";

        private object _lastVm;
        private object _lastPhase;

        public override void OnPop() { _lastVm = null; _lastPhase = null; }

        public override void OnUpdate()
        {
            var vm = WizardVm();
            if (vm == null) return;
            var phase = CurrentPhase();
            if (!ReferenceEquals(vm, _lastVm) || !ReferenceEquals(phase, _lastPhase))
            {
                // A phase change WITHIN this wizard (Next/Back/roadmap jump) — not the initial build or
                // a VM swap. The game plays a page-turn on phase advance; our VM-level SelectNext/Prev
                // bypasses it, so play it here, and land focus on the new page (its keys changed).
                bool phaseChange = ReferenceEquals(vm, _lastVm) && _lastPhase != null;
                _lastVm = vm;
                _lastPhase = phase;
                if (phaseChange)
                {
                    UiSound.Play(UISoundType.BookPageTurn);
                    Navigation.FocusStop("content");
                }
                return;
            }
            OnPhaseTick();
        }


        public override void Build(GraphBuilder b)
        {
            var vm = WizardVm();
            if (vm == null) return;

            BuildHeader(b); // e.g. the chargen roadmap (its own stop, first in Tab order)

            var phase = CurrentPhase();
            string k = "wiz:" + vm.GetHashCode() + ":" + (phase != null ? phase.GetHashCode() : 0) + ":";
            b.BeginStop("content").PushContext(PhaseLabel());
            BuildContent(b, k);
            b.PopContext();

            // Footer: Back then Next (label + availability track the current phase live).
            b.BeginStop("back").AddItem(ControlId.Structural("wiz:back"),
                GraphNodes.Button(() => Loc.T("wizard.back"), OnBack, BackEnabled));
            b.BeginStop("next").AddItem(ControlId.Structural("wiz:next"),
                GraphNodes.Button(NextLabel, OnNext, NextEnabled));
        }
    }
}
