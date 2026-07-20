using System;
using HarmonyLib;

namespace BranteAccess.Module.Patches
{
    /// <summary>
    /// Suppresses the game's own keyboard while one of our screens owns the surface. Every game
    /// key read is a bare Input.GetKeyDown inside a per-class Update body (complete survey of
    /// Assembly-CSharp, 2026-07-18), so one shared prefix skips those bodies. The mod's own
    /// actions re-invoke the same game handlers (NextPage, ShowPauseMenu, Button_Click) so game
    /// logic and sounds still run.
    /// </summary>
    internal static class GameInputPatches
    {
        // Every listed Update body is INPUT-ONLY - skipping it removes nothing but key reads.
        // Deliberately not listed: NameRequestWindow.Update (also drives button interactable per
        // frame; handled with text entry in Phase 3), GameManager.Update and the Console toggle
        // (dev-gated developer keys the mod leaves alone), and ChapterCutscene: its Enter/Space/
        // Escape read IS both the accessible skip and the only code path that ever ends the
        // cutscene (LoadScene is called from that branch alone) - suppressing it traps the player
        // permanently. CutsceneIntro IS listed (user directive 2026-07-20, see DECISIONS.md): the
        // game's intro "skip" only silences the voiceover - nothing stops the visual timeline
        // (zero PlayableDirector references in the assembly; the advance is an animation event at
        // the transition's natural end) - and its mark-shown writes OpenedSceneName, which the
        // Intro-scene preload has already clobbered, so a skip eats the At The End of Time event.
        // The cutscene self-advances at its natural end; the voiceover is the content.
        private static readonly Type[] InputOnlyUpdates =
        {
            typeof(CutsceneIntro),
            typeof(_Scripts.AMVCC.Controllers.TextController),
            typeof(_Scripts.Managers.UIManager),
            typeof(_Scripts.AMVCC.Views.Windows.ChapterFinal.ChapterFinalWindowController),
            typeof(_Scripts.AMVCC.Views.Windows.ChapterStart.ChapterStartWindowController),
            typeof(_Scripts.AMVCC.Views.Windows.Credits.WindowCreditsController),
            typeof(_Scripts.AMVCC.Views.Windows.Death.SimplePageTurner),
            typeof(_Scripts.AMVCC.Views.Windows.DeletePopupConfirmation.DeletePopupConfirmation),
            typeof(_Scripts.AMVCC.Views.Windows.Popup.ExitConfirmationPopup.ExitConfirmationPopupController),
            typeof(_Scripts.AMVCC.Views.Windows.Popup.InterludePopupController),
            typeof(_Scripts.AMVCC.Views.Windows.Popup.ParametersConvertationPanelComponent),
            typeof(_Scripts.AMVCC.Views.Windows.SaveLoad.LoadWindow),
            typeof(_Scripts.AMVCC.Views.Windows.Settings.SettingsWindow),
        };

        private static Harmony _harmony;

        /// <summary>Unique id per module generation so a reload's UnpatchSelf removes exactly this
        /// generation's patches while the incoming generation's stay.</summary>
        public static void Apply()
        {
            _harmony = new Harmony("BranteAccess.Module." + Guid.NewGuid().ToString("N"));
            var prefix = new HarmonyMethod(typeof(GameInputPatches), nameof(SkipWhileScreenActive));
            foreach (var type in InputOnlyUpdates)
            {
                var update = AccessTools.Method(type, "Update");
                if (update == null)
                    throw new InvalidOperationException(
                        "no Update method on " + type.FullName + " - game update changed the input surface");
                _harmony.Patch(update, prefix: prefix);
            }
            Mod.Log("game-input patches on " + InputOnlyUpdates.Length
                + " game Update methods (harmony id " + _harmony.Id + ")");
        }

        public static void Remove()
        {
            if (_harmony == null) return;
            _harmony.UnpatchSelf();
            Mod.Log("game-input patches removed (harmony id " + _harmony.Id + ")");
            _harmony = null;
        }

        // Suppress only while one of OUR screens is active: with an empty stack (a surface the
        // mod has no screen for yet) the game's own keys - Escape to pause, A/D paging - are the
        // only working keyboard, and dead keys are worse than stock keys. Once a screen claims
        // the surface, its actions own the keyboard and the game's reads stay skipped.
        private static bool SkipWhileScreenActive()
            => Screens.ScreenManager.Current == null;
    }
}
