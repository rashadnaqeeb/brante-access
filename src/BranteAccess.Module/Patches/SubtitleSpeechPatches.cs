using System;
using HarmonyLib;
using _Scripts.AMVCC.Views.Windows.Cutscenes;

namespace BranteAccess.Module.Patches
{
    /// <summary>
    /// Speaks cutscene subtitle lines as the game delivers them. SubtitlesTextChanger.
    /// OnSubtitleTextChange is fired only by scene-side animation/Bolt events (no assembly
    /// callers), so it IS the "this line is on screen now" signal; the method advances its key
    /// index only when it actually set a new line, and that advance already encodes both the
    /// ShowSubtitlesInCutscenes setting and key exhaustion. Subtitles stay player-controlled:
    /// setting off means no subtitle speech (user directive 2026-07-21, DECISIONS.md) - for the
    /// 12 languages without voiceover audio, players enable subtitles to get the lines.
    /// </summary>
    internal static class SubtitleSpeechPatches
    {
        private static Harmony _harmony;

        /// <summary>Unique id per module generation so a reload's UnpatchSelf removes exactly this
        /// generation's patches while the incoming generation's stay.</summary>
        public static void Apply()
        {
            _harmony = new Harmony("BranteAccess.Module.Subtitles." + Guid.NewGuid().ToString("N"));
            var target = AccessTools.Method(typeof(SubtitlesTextChanger),
                nameof(SubtitlesTextChanger.OnSubtitleTextChange));
            if (target == null)
                throw new InvalidOperationException(
                    "no OnSubtitleTextChange on SubtitlesTextChanger - game update changed the subtitle surface");
            _harmony.Patch(target,
                prefix: new HarmonyMethod(typeof(SubtitleSpeechPatches), nameof(CaptureIndex)),
                postfix: new HarmonyMethod(typeof(SubtitleSpeechPatches), nameof(SpeakDeliveredLine)));
            Mod.Log("subtitle speech patch on SubtitlesTextChanger.OnSubtitleTextChange (harmony id "
                + _harmony.Id + ")");
        }

        public static void Remove()
        {
            if (_harmony == null) return;
            _harmony.UnpatchSelf();
            Mod.Log("subtitle speech patch removed (harmony id " + _harmony.Id + ")");
            _harmony = null;
        }

        private static void CaptureIndex(int ____currentKeyIndex, out int __state)
            => __state = ____currentKeyIndex;

        private static void SpeakDeliveredLine(SubtitlesTextChanger __instance,
            int ____currentKeyIndex, int __state)
        {
            if (____currentKeyIndex == __state) return;
            try
            {
                // The component's text was set from I2 this frame; reading it back is the live
                // localized line. The host pipeline strips TMP rich text. Queued, never interrupts:
                // lines arrive paced by the scene's own timing.
                Mod.Speech.Speak(__instance.TextComponent.text);
            }
            catch (Exception e)
            {
                Mod.Error("subtitle line speech failed: " + e);
            }
        }
    }
}
