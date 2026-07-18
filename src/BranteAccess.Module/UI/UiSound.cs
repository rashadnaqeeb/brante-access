using System;
using _Scripts.Managers;

namespace BranteAccess.Module.UI
{
    /// <summary>
    /// The game's own UI earcons, for our keyboard focus (our focus move = a sighted player's
    /// mouseover). SoundManager.instance is null between scene loads - the only legitimate null here;
    /// a missing sound is worth a log line but must never break navigation, so failures are caught
    /// and logged.
    /// </summary>
    public static class UiSound
    {
        /// <summary>The game's control-hover sound (the HUD button hover clip through the same
        /// pointer-enter source PointerSfxPlayer uses).</summary>
        public static void Hover()
        {
            try
            {
                var sm = SoundManager.instance;
                if (sm != null && sm.HudButtonsPointEnterSoundSFX != null)
                    sm.PlayPointerEnterClip(sm.HudButtonsPointEnterSoundSFX);
            }
            catch (Exception e) { Mod.Warn("UiSound.Hover failed: " + e.Message); }
        }

        /// <summary>The game's button-click sound, for activations that bypass a game Button.</summary>
        public static void Click()
        {
            try
            {
                var sm = SoundManager.instance;
                if (sm != null) sm.PlayClickSound();
            }
            catch (Exception e) { Mod.Warn("UiSound.Click failed: " + e.Message); }
        }
    }
}
