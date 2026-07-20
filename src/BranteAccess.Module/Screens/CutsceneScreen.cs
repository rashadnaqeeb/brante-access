using System.Collections.Generic;
using BranteAccess.Module.Input;
using BranteAccess.Module.Speech;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The two voiceover cutscenes; their content is the AUDIO narration, and this screen claims
    /// NO input category so every key reaches the game untouched. CutsceneIntro (logo intro) is
    /// unskippable under the mod: the game's own "skip" is audio-only and eats the intro event
    /// (see GameInputPatches), so its Update is suppressed and the announcement promises nothing -
    /// the cutscene self-advances at its natural end. ChapterCutscene (chapter narration) keeps
    /// the game's Space/Enter/Escape skip: that read is also the only code path that ends the
    /// cutscene, and its skip is the real end-of-cutscene handler (advances immediately).
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
