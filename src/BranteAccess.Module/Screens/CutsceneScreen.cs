using System.Collections.Generic;
using BranteAccess.Module.Input;
using BranteAccess.Module.Speech;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The two voiceover cutscenes (CutsceneIntro: logo intro, any key skips; ChapterCutscene:
    /// chapter narration, Space/Enter/Escape skip). Their content is the AUDIO narration and
    /// their Updates stay UNPATCHED (see FocusModePatches) so the game's own skip keys work -
    /// this screen only announces what is playing and how to skip, and claims NO input category
    /// so every key reaches the game untouched.
    /// </summary>
    public sealed class CutsceneScreen : Screen
    {
        public override string Key => "cutscene";
        public override int Layer => 0;

        private static readonly InputCategory[] None = new InputCategory[0];
        public override IReadOnlyList<InputCategory> InputCategories => None;

        public override bool IsActive()
            => UnityEngine.Object.FindObjectOfType<CutsceneIntro>() != null
            || UnityEngine.Object.FindObjectOfType<ChapterCutscene>() != null;

        public override Message ScreenName
            => Message.Localized("ui",
                UnityEngine.Object.FindObjectOfType<CutsceneIntro>() != null
                    ? "cutscene.intro" : "cutscene.chapter");
    }
}
