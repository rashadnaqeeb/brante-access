using Kingmaker;
using Kingmaker.UI;

namespace WrathAccess
{
    /// <summary>
    /// Plays the game's UI sounds. Most controls' click sounds live in the view's click handler,
    /// which we bypass by driving the VM directly — so we replay them here for consistent feedback.
    /// </summary>
    public static class UiSound
    {
        public static void Click() => Play(UISoundType.ButtonClick);

        /// <summary>The game's control-hover sound — played when our focus moves to a new element
        /// (our equivalent of a mouseover).</summary>
        public static void Hover()
        {
            try
            {
                var g = Game.Instance;
                if (g != null && g.UI != null && g.UI.UISound != null)
                    g.UI.UISound.PlayHoverSound();
            }
            catch { }
        }

        public static void Play(UISoundType type)
        {
            try
            {
                var g = Game.Instance;
                if (g != null && g.UI != null && g.UI.UISound != null)
                    g.UI.UISound.Play(type);
            }
            catch
            {
                // Sound is non-essential; never let it break navigation.
            }
        }
    }
}
