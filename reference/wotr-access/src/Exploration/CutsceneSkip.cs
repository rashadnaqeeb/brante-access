using Kingmaker;
using Kingmaker.Controllers;
using Kingmaker.GameModes;

namespace WrathAccess.Exploration
{
    /// <summary>
    /// Space: skip the current cutscene — the game's own skip functionality (its Enter binding),
    /// which self-gates: only in Cutscene/CutsceneGlobalMap modes, refused for NonSkippable scenes.
    /// We front it with spoken feedback the game's silent refusal lacks, and bind SPACE instead of
    /// the game's Enter so a habitual Enter right after confirming dialogue can't blow through a
    /// scene. Registered in the InGame category: live while a cutscene holds control (Exploration is
    /// control-gated dead there); during normal play the Exploration Space (pause) shadows it.
    /// </summary>
    internal static class CutsceneSkip
    {
        public static void Request()
        {
            var game = Game.Instance;
            if (game == null
                || (game.CurrentMode != GameModeType.Cutscene && game.CurrentMode != GameModeType.CutsceneGlobalMap))
                return; // not a cutscene: stay silent — this key only means "skip" inside one

            // Mirror the game's own refusal test (SkipCutscene just logs and returns for these).
            foreach (var p in game.State.Cutscenes)
                if (p != null && p.LockControls && p.Cutscene != null && p.Cutscene.NonSkippable)
                {
                    Tts.Speak(Loc.T("cutscene.cant_skip"), interrupt: true);
                    return;
                }

            Tts.Speak(Loc.T("cutscene.skipping"), interrupt: true);
            CutsceneController.SkipCutscene();
        }
    }
}
