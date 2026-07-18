using System.Collections.Generic;
using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The game's one opened-window slot while the window has no dedicated screen yet: the HUD
    /// bar is the whole graph (Tab lands on the pressed button - the game's own selection
    /// state; arrows + Enter switch windows; the back button returns to the scene) and Escape
    /// runs the game's back button. Announces the window by the game's own term for the HUD
    /// button that opened it. Dedicated window screens replace this coverage window by window.
    /// </summary>
    public sealed class WindowShellScreen : Screen
    {
        // Windows with a dedicated screen - the shell stands down for these.
        private static readonly HashSet<string> Covered = new HashSet<string>
        {
            "Window_Character",
            "Window_Family",
            "Window_Destiny",
            "Window_Home",
            "Window_Relations",
            "Window_Empire",
            "Window_Map",
        };

        public override string Key => "window";
        public override int Layer => 10;

        public override bool IsActive()
        {
            var w = GameUi.OpenedWindow;
            return w != null && !Covered.Contains(w.name);
        }
        public override Message ScreenName => Message.MaybeRaw(HudBar.OpenWindowTitle());

        // Delivery bookkeeping only - the name re-reads from the game at speech time.
        private string _spokenWindow;

        public override void OnFocus()
        {
            base.OnFocus(); // announces the opening window's name
            _spokenWindow = GameUi.OpenedWindow == null ? null : GameUi.OpenedWindow.name;
        }

        public override void OnPop()
        {
            _spokenWindow = null;
        }

        // Switching windows from the bar keeps this screen focused (same key), so the swap in
        // the game's opened-window slot is the delivery signal for the new window's name.
        public override void OnUpdate()
        {
            var w = GameUi.OpenedWindow;
            if (w == null) return;
            if (w.name == _spokenWindow) return;
            _spokenWindow = w.name;
            Mod.Speech.Speak(HudBar.OpenWindowTitle());
        }

        // The window-null guard closes a one-frame race: the frame the window dies, a rebuild
        // still runs before the stack diff pops this screen, and focus would reconcile off the
        // vanished back button onto a neighbor and announce it over the scene's own return
        // announcements (seen live). An empty graph is "closed" - nothing speaks.
        public override void Build(GraphBuilder b)
        {
            if (GameUi.OpenedWindow == null) return;
            HudBar.Build(b);
        }

        public override string HelpText() => GameUi.WindowHelp();

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, null, _ => HudBar.ClickBack());
        }
    }
}
