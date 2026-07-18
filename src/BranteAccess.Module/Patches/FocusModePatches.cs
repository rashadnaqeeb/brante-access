using System;
using HarmonyLib;

namespace BranteAccess.Module.Patches
{
    /// <summary>
    /// Suppresses the game's own keyboard while focus mode is on. Every game key read is a bare
    /// Input.GetKeyDown inside a per-class Update body (complete survey of Assembly-CSharp,
    /// 2026-07-18), so one shared prefix skips those bodies while <see cref="FocusMode.Active"/>;
    /// off restores stock keys untouched. The mod's own actions re-invoke the same game handlers
    /// (NextPage, ShowPauseMenu, Button_Click) so game logic and sounds still run.
    /// </summary>
    internal static class FocusModePatches
    {
        // Every listed Update body is INPUT-ONLY - skipping it removes nothing but key reads.
        // Deliberately not listed: NameRequestWindow.Update (also drives button interactable per
        // frame; handled with text entry in Phase 3), GameManager.Update and the Console toggle
        // (dev-gated developer keys the mod leaves alone), and the two cutscene classes
        // (ChapterCutscene, CutsceneIntro): their key reads ARE the accessible path - a voiceover
        // plays and any key / Enter skips; suppressing them left the player trapped in the
        // cutscene with no keyboard at all (found live in the new-game flow). CutsceneScreen
        // announces them; the game handles the keys.
        private static readonly Type[] InputOnlyUpdates =
        {
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
            var prefix = new HarmonyMethod(typeof(FocusModePatches), nameof(SkipWhileFocused));
            foreach (var type in InputOnlyUpdates)
            {
                var update = AccessTools.Method(type, "Update");
                if (update == null)
                    throw new InvalidOperationException(
                        "no Update method on " + type.FullName + " - game update changed the input surface");
                _harmony.Patch(update, prefix: prefix);
            }
            Mod.Log("focus-mode patches on " + InputOnlyUpdates.Length
                + " game Update methods (harmony id " + _harmony.Id + ")");
        }

        public static void Remove()
        {
            if (_harmony == null) return;
            _harmony.UnpatchSelf();
            Mod.Log("focus-mode patches removed (harmony id " + _harmony.Id + ")");
            _harmony = null;
        }

        private static bool SkipWhileFocused() => !FocusMode.Active;
    }
}
