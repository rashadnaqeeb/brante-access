using _Scripts.Managers;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BranteAccess.Module.Game
{
    /// <summary>
    /// The one adapter for the game's UI surface (CLAUDE.md: centralize shared reads; never cache
    /// game state - every property re-reads the live singleton at call time). UIManager.Initiate
    /// is null between scene loads; that is the only legitimate null here.
    /// </summary>
    public static class GameUi
    {
        public static UIManager Manager => UIManager.Initiate;

        public static string SceneName => SceneManager.GetActiveScene().name;

        /// <summary>Whether a named scene is loaded, active or ADDITIVE - the main menu opens
        /// Settings/LoadWindow as additive scene loads, not UIManager window slots.</summary>
        public static bool IsSceneLoaded(string name) => SceneManager.GetSceneByName(name).isLoaded;

        public static GameObject OpenedWindow => Manager == null ? null : Manager.OpenedWindow;
        public static GameObject OpenedPopup => Manager == null ? null : Manager.OpenedPopup;
        public static GameObject OpenedTooltip => Manager == null ? null : Manager.OpenedTooltip;

        public static bool PauseOpen =>
            Manager != null && Manager.PauseWindow != null && Manager.PauseWindow.activeInHierarchy;
    }
}
