using Kingmaker.GameModes;
using Kingmaker.PubSubSystem;

namespace WrathAccess
{
    /// <summary>
    /// Tracks whether the in-game dialogue WINDOW is actually shown (and so clickable) right now —
    /// mirroring the game's <c>DialogView.m_VisibleState</c>, which is driven by BOTH the game mode
    /// (the window shows exactly while in <see cref="GameModeType.Dialog"/>) AND
    /// <c>CutsceneUIHideController</c>'s <see cref="ICutsceneDialogHandler.HandleDialogVisible"/> (a
    /// cutscene can hide the window without changing game mode). We receive the same EventBus events in
    /// the same order as the game's view, so this stays in lockstep with it.
    ///
    /// Our <see cref="WrathAccess.Screens.DialogueScreen"/> stays up through a cutscene transition (the
    /// conversation VM persists), so without this it would let the player keep "pressing" Continue while
    /// the game has hidden the button — spam-advancing the dialogue. We gate activation on <see cref="Shown"/>.
    /// </summary>
    internal sealed class DialogVisibility : IGameModeHandler, ICutsceneDialogHandler
    {
        private static DialogVisibility _instance;

        /// <summary>Is the dialogue window currently shown/interactable (matches the game's view)?</summary>
        public static bool Shown { get; private set; }

        public static void Initialize()
        {
            if (_instance != null) return;
            _instance = new DialogVisibility();
            EventBus.Subscribe(_instance);
        }

        // The window is shown exactly while Dialog mode is active; any other mode hides it.
        public void OnGameModeStart(GameModeType gameMode) => Shown = gameMode == GameModeType.Dialog;
        public void OnGameModeStop(GameModeType gameMode) { }

        // A cutscene UI-hide/show overrides — the window can hide while still nominally in Dialog mode.
        public void HandleDialogVisible(bool state) => Shown = state;
    }
}
