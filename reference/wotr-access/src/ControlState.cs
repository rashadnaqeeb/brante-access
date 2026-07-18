using Kingmaker;

namespace WrathAccess
{
    /// <summary>
    /// Single source of truth for whether the player can act in the world. We use the GAME's own input
    /// gate: <c>Game.ClickEventsController</c> (the world-click <c>PointerController</c>) is assigned per
    /// game mode — set in Default/Pause, GlobalMap/Kingdom/Settlement and TacticalCombat, and left null in
    /// everything else (Cutscene, Dialog incl. storybook/interchapter, Rest, FullScreenUi menus, loading /
    /// None). So "the click controller exists" == "the player has control". One field read the game
    /// maintains on every mode change — no scene lookup, no flag conjunction to flicker or get stuck.
    /// </summary>
    internal static class ControlState
    {
        public static bool HasControl => Game.Instance?.ClickEventsController != null;
    }
}
