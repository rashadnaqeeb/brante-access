using Kingmaker;
using Kingmaker.Blueprints.Root.Strings;
using Kingmaker.UI.MVVM._VM.NewGame;
using Kingmaker.UI.MVVM._VM.NewGame.Difficulty;
using Kingmaker.UI.MVVM._VM.NewGame.Story;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The New Game wizard (MainMenuVM.NewGameVM): Story → Difficulty → Save injector, on the shared
    /// graph-native <see cref="WizardScreen"/> shell. Next/Back delegate to the VM's OnButton*; the
    /// Next label/availability come from the current phase. Advancing past the last step enters
    /// character generation; backing past the first exits to the menu. Story + Difficulty are
    /// implemented (Difficulty rides the same <see cref="SettingsEntityGraph"/> emitter as the game
    /// settings window — collapsible header sections); the save injector is a placeholder.
    /// </summary>
    public sealed class NewGameScreen : WizardScreen
    {
        public override string Key => "ctx.newgame";
        public override string ScreenName => Loc.T("screen.new_game");
        public override int Layer => 0; // mutually exclusive with the main-menu sidebar

        private static NewGameVM Vm()
        {
            var g = Game.Instance;
            var mm = g != null && g.RootUiContext != null ? g.RootUiContext.MainMenuVM : null;
            if (mm == null || mm.NewGameVM == null) return null;
            // Once character generation opens, the wizard is done — hand off.
            if (mm.CharGenContextVM != null && mm.CharGenContextVM.CharGenVM != null
                && mm.CharGenContextVM.CharGenVM.Value != null) return null;
            return mm.NewGameVM;
        }

        protected override object WizardVm() => Vm();
        protected override object CurrentPhase() => Vm()?.MenuSelectionGroup.SelectedEntity.Value;

        protected override string PhaseLabel() => Vm()?.MenuSelectionGroup.SelectedEntity.Value?.Title;

        protected override void BuildContent(GraphBuilder b, string k)
        {
            var phase = Vm()?.MenuSelectionGroup.SelectedEntity.Value?.NewGamePhaseVM;
            if (phase is NewGamePhaseStoryVM story)
                BuildStory(b, k, story);
            else if (phase is NewGamePhaseDifficultyVM difficulty)
                // Same VMs as the Settings screen, but FLAT: one short section, so headers read as
                // labels and the options are a plain vertical list (no collapsed tree to open first).
                SettingsEntityGraph.Emit(b, difficulty.SettingEntities, k, flat: true);
            else
                b.AddItem(ControlId.Structural(k + "unavailable"),
                    GraphNodes.Text(() => Loc.T("wizard.step_unavailable")));
        }

        protected override void OnBack() => Vm()?.OnButtonBack();
        protected override void OnNext() => Vm()?.OnButtonNext();

        protected override string NextLabel()
        {
            var p = Vm()?.MenuSelectionGroup.SelectedEntity.Value?.NewGamePhaseVM;
            return p != null && p.NextStepTitle != null ? p.NextStepTitle.Value : Loc.T("wizard.next");
        }

        protected override bool NextEnabled()
        {
            var p = Vm()?.MenuSelectionGroup.SelectedEntity.Value?.NewGamePhaseVM;
            return p == null || p.IsButtonNextAvailable.Value;
        }

        private static void BuildStory(GraphBuilder b, string k, NewGamePhaseStoryVM story)
        {
            // Campaign choices (reads as "<name>, radio button, selected, N of M").
            int i = 0;
            foreach (var e in story.SelectionGroup.EntitiesCollection)
            {
                if (e == null) continue;
                var ent = e; // capture for the live closure
                b.AddItem(ControlId.Referenced(ent, k + "camp:" + i),
                    GraphNodes.SelectionItem(ent, () => ent.Title));
                i++;
            }

            // Live description of the currently-selected campaign (its own Tab-stop after the list,
            // matching the old layout; updates as you pick).
            b.BeginStop("ng_desc").AddItem(ControlId.Structural(k + "desc"), GraphNodes.Text(
                () => story.Description != null ? story.Description.Value : ""));

            // Hardcore/permadeath mode toggle (code name "Last Azlanti"; the localized label reads
            // "Sink or Swim Mode"). Only enabled for dungeon campaigns (Midnight Isles) and hidden by
            // the game otherwise — immediate mode: emitted only while enabled.
            if (story.LastAzlantiEnabled.Value)
                b.BeginStop("ng_azlanti").AddItem(ControlId.Structural(k + "azlanti"), GraphNodes.Toggle(
                    () => (string)UIStrings.Instance.NewGameWin.LastAzlantiMode,
                    () => story.LastAzlantiIsOn.Value,
                    () => story.SwitchLastAzlanti()));
        }
    }
}
