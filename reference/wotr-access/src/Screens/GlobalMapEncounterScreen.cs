using System.Collections.Generic;
using Kingmaker;
using Kingmaker.PubSubSystem; // IEscMenuHandler
using Kingmaker.UI.MVVM._VM.GlobalMap.Message; // GlobalMapRandomEncounterVM
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The world-map travel popup (<see cref="GlobalMapRandomEncounterVM"/>) the game raises when travel
    /// stops for an encounter / discovery — the event line (Title + Description) as the focused cue, then
    /// the real choices (Enter = the game's <c>EnterLabel</c> action, Avoid when offered). Focus starts on
    /// the cue, and when Continue is the only way forward (Avoid disabled) Enter on the cue advances it —
    /// exactly like dialogue. Buttons fire the game's button-click sound + the VM method (Accept/Avoid),
    /// the same as pressing the real control. A modal: <see cref="Exclusive"/> blocks the world-map keys
    /// beneath, and being the top screen (not ctx.globalmap) drops OverlayManager.Active so the world-map
    /// cursor + sonar freeze.
    ///
    /// Graph-native: node keys carry the popup VM's identity, so a NEW popup re-homes focus onto its cue
    /// and the differ delivers the new line — the old "land silently, speak from OnUpdate" dance is now
    /// just the landing announcement. A focus-mode-off fallback still speaks the line via Tts (a blocking
    /// popup must never be silent).
    /// </summary>
    public sealed class GlobalMapEncounterScreen : Screen
    {
        public override string Key => "ctx.globalmapencounter";
        public override string ScreenName => Loc.T("screen.world_map_encounter");
        public override int Layer => 15; // a modal over the world-map base context (like dialogue)
        public override bool Exclusive => true;

        private GlobalMapRandomEncounterVM _spokenVm; // focus-mode-OFF fallback delivery marker

        private static GlobalMapRandomEncounterVM Vm()
        {
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            return rc?.GlobalMapVM?.GlobalMapRandomEncounterVM?.Value;
        }

        public override bool IsActive() => Vm() != null;

        public override void OnPop() { _spokenVm = null; }

        public override void OnUpdate()
        {
            // Focus-mode-off fallback: the differ can't speak (it announces only under focus mode), but a
            // blocking popup must never be silent.
            if (FocusMode.Active) return;
            var vm = Vm();
            if (vm == null || vm == _spokenVm) return;
            _spokenVm = vm;
            Tts.Speak(CueText(vm), interrupt: false);
        }


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "gmenc:" + vm.GetHashCode() + ":";

            // The event line as the cue (the start node — the landing announcement reads it). When Avoid
            // is disabled, Continue is the only way forward, so Enter on the cue advances it.
            var cue = GraphNodes.Text(() => CueText(Vm()));
            cue.OnActivate = () => { if (Vm()?.AvoidIsDisable ?? false) AcceptLive(); };
            b.AddItem(ControlId.Structural(k + "cue"), cue);

            // The real choices: Enter (Continue/Fight) + Avoid (only when offered).
            string enterLabel = TextUtil.StripRichText(vm.EnterLabel);
            b.AddItem(ControlId.Structural(k + "enter"),
                GraphNodes.Button(() => enterLabel, AcceptLive, sound: null));
            if (!vm.AvoidIsDisable)
            {
                string avoidLabel = TextUtil.StripRichText(vm.AvoidLabel);
                b.AddItem(ControlId.Structural(k + "avoid"),
                    GraphNodes.Button(() => avoidLabel, AvoidLive, sound: null));
            }
        }

        // Accept / Avoid on the LIVE VM (what the OwlcatButtons are wired to), each with the game's
        // button-click sound — identical to pressing the real button. (The factories' generic click is
        // suppressed; these play the game's own controller path.)
        private static void AcceptLive() { PlayClick(); Vm()?.Accept(); }
        private static void AvoidLive() { PlayClick(); Vm()?.Avoid(); }

        private static void PlayClick() => Kingmaker.UI.UISoundController.Instance?.PlayButtonClickSound();

        // Title + Description as one spoken line ("Title. Description"), colours stripped.
        private static string CueText(GlobalMapRandomEncounterVM vm)
        {
            if (vm == null) return string.Empty;
            var title = TextUtil.StripRichText(vm.Title);
            var desc = TextUtil.StripRichText(vm.Description);
            if (string.IsNullOrWhiteSpace(title)) return desc ?? string.Empty;
            if (string.IsNullOrWhiteSpace(desc)) return title;
            return title + ". " + desc;
        }

        // Escape opens the game menu, like the rest of the world map (the game's EscManager is muted while
        // focus mode owns the keyboard).
        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "hud.game_menu"),
                _ => EventBus.RaiseEvent(delegate (IEscMenuHandler h) { h.HandleOpen(); }));
        }
    }
}
