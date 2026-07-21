using System.Text;
using BranteAccess.Core.Modularity;
using BranteAccess.Module.Game;
using BranteAccess.Module.Input;
using BranteAccess.Module.Patches;
using BranteAccess.Module.Screens;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using UnityEngine;

namespace BranteAccess.Module
{
    /// <summary>
    /// The reloadable module's entry point - the host finds this by scanning for IModModule.
    /// Feature code (screens, navigator, input, announcements) grows from here and hot-reloads
    /// without a game restart. IDevDriver strings are dev tooling, exempt from the
    /// no-inline-strings rule.
    /// </summary>
    public sealed class ModModule : IModModule, IDevDriver
    {
        public void Load(IModHost host)
        {
            Mod.Host = host;
            Localization.LocalizationManager.Initialize();
            RegisterActions();
            RegisterScreens();
            InputManager.ActiveCategoriesProvider = ScreenManager.ProvideCategories;
            InputManager.SuppressDispatch = () =>
            {
                var current = ScreenManager.Current;
                return current != null && current.CapturesRawInput;
            };
            InputManager.UiDispatcher = Navigation.DispatchJustPressed;
            ScreenManager.NavigatorAttach = Navigation.Attach;
            ScreenManager.NavigatorScreenClosed = Navigation.ScreenClosed;
            ScreenManager.NavigatorEnsureFocus = Navigation.EnsureFocus;
            GraphAnnouncer.PositionText = (index, count) =>
                Loc.T("nav.position", new { index, count });
            Verbosity.Load();
            GraphAnnouncer.PartFilter = (type, part) =>
                Verbosity.Verbose || (part.Kind != AnnouncementKinds.Role
                                      && part.Kind != AnnouncementKinds.Position);
            GraphAnnouncer.ExpandedStateText = expanded =>
                Loc.T(expanded ? "nav.expanded" : "nav.collapsed");
            GameInputPatches.Apply();
            SubtitleSpeechPatches.Apply();
            // Generation is not logged here: the loader bumps it only after Load succeeds (so a
            // failed reload leaves the old module live), and it logs the real number itself.
            Mod.Log("module code up, waiting for first tick");
        }

        public void Tick()
        {
            Localization.LocalizationManager.Tick();
            ScreenManager.Tick();
            InputManager.Tick();
            Navigation.TickTypeahead();
        }

        public void Dispose()
        {
            GameInputPatches.Remove();
            SubtitleSpeechPatches.Remove();
            Mod.Log("module disposed (generation " + Mod.Host.ModuleGeneration + ")");
        }

        // Global actions live here; screens register their own categories' actions as they land.
        // Registration labels are fallbacks - DisplayLabel resolves lang/<code>/settings.txt.
        private static void RegisterActions()
        {
            // The UI navigation set (routed to the navigator via UiDispatcher; live only while a
            // screen declares the UI category).
            InputManager.Register("ui.up", "Navigate up", InputCategory.UI)
                .AddBinding(KeyCode.UpArrow).Repeating();
            InputManager.Register("ui.down", "Navigate down", InputCategory.UI)
                .AddBinding(KeyCode.DownArrow).Repeating();
            InputManager.Register("ui.left", "Navigate left", InputCategory.UI)
                .AddBinding(KeyCode.LeftArrow).Repeating();
            InputManager.Register("ui.right", "Navigate right", InputCategory.UI)
                .AddBinding(KeyCode.RightArrow).Repeating();
            InputManager.Register("ui.next", "Next control group", InputCategory.UI)
                .AddBinding(KeyCode.Tab).Repeating();
            InputManager.Register("ui.prev", "Previous control group", InputCategory.UI)
                .AddBinding(KeyCode.Tab, shift: true).Repeating();
            InputManager.Register("ui.activate", "Activate", InputCategory.UI)
                .AddBinding(KeyCode.Return).AddBinding(KeyCode.KeypadEnter);
            InputManager.Register("ui.secondary", "Secondary action", InputCategory.UI)
                .AddBinding(KeyCode.Backspace);
            // Unconsumed by the focused screen (no Back action), Escape falls through to the
            // game's own global read, gated exactly as the game gates it.
            InputManager.Register("ui.back", "Back", InputCategory.UI, GameUi.GlobalEscape)
                .AddBinding(KeyCode.Escape);
            InputManager.Register("ui.tooltip", "Read tooltip", InputCategory.UI)
                .AddBinding(KeyCode.Space).AddBinding(KeyCode.F1);
            InputManager.Register("ui.home", "First item", InputCategory.UI)
                .AddBinding(KeyCode.Home);
            InputManager.Register("ui.end", "Last item", InputCategory.UI)
                .AddBinding(KeyCode.End);
            InputManager.Register("ui.regionPrev", "Previous region", InputCategory.UI)
                .AddBinding(KeyCode.UpArrow, ctrl: true).Repeating();
            InputManager.Register("ui.regionNext", "Next region", InputCategory.UI)
                .AddBinding(KeyCode.DownArrow, ctrl: true).Repeating();

            // Global (live on every screen): verbosity toggle - concise drops the role word and
            // the item position from navigator announcements.
            InputManager.Register("mod.verbosity", "Toggle verbosity", InputCategory.Global,
                () => Mod.Speech.Speak(
                    Loc.T(Verbosity.Toggle() ? "verbosity.verbose" : "verbosity.concise"),
                    interrupt: true))
                .AddBinding(KeyCode.V, ctrl: true);
        }

        // The outer (poll-driven) screens. The window/popup/pause entries are silent generic
        // scaffolding proving the stack against the game's real slots; Phase 3 replaces them with
        // per-surface screens that speak and build graphs.
        private static void RegisterScreens()
        {
            var screens = new Screens.Screen[]
            {
                new MainMenuScreen(),
                new NameRequestScreen(),
                new DisclaimerScreen(),
                new CutsceneScreen(),
                new SettingsScreen(),
                new LoadWindowScreen(),
                new DeleteConfirmScreen(),
                new ChapterSelectScreen(),
                new ChapterRestartConfirmScreen(),
                new PauseScreen(),
                new ExitConfirmScreen(),
                new CreditsScreen(),
                new GameOverScreen(),
                new ChapterPictureScreen(),
                new ChapterStartScreen(),
                new ChapterFinalScreen(),
                new DeathScreen(),
                new InterludeScreen(),
                new CaseOfYearPopupScreen(),
                new SceneScreen(),
                new LiveTimelineScreen(),
                new WindowShellScreen(),
                new CharacterWindowScreen(),
                new FamilyWindowScreen(),
                new DestinyWindowScreen(),
                new HomeWindowScreen(),
                new RelationsWindowScreen(),
                new EmpireWindowScreen(),
                new MapWindowScreen(),
                new WorkWindowScreen(),
                new InsurrectionWindowScreen(),
                new CaseForYearScreen(),
                new GenericPopupScreen(),
                new TriggerScenePopupScreen(),
                new InsurrectionSidePopupScreen(),
            };
            foreach (var s in screens) ScreenManager.Register(s);
            Mod.Log("ScreenManager: " + screens.Length + " outer screens registered");
        }

        // IDevDriver - probed by the host per request, so the newest generation always answers.

        public string DispatchUi(string action)
        {
            if (InputManager.DispatchSuppressed) return "suppressed (raw input capture)";
            foreach (var a in InputManager.Actions)
                if (a.Key == action && !InputManager.CategoryActive(a.Category))
                    return "not dispatched: category " + a.Category + " inactive";
            return InputManager.Dispatch(action) ? "dispatched " + action : null;
        }

        public string DescribeNav()
        {
            var sb = new StringBuilder();
            sb.Append("language ").Append(Localization.LocalizationManager.Language).Append('\n');
            var nav = Navigation.Active as GraphNavigator;
            var render = nav?.CurrentRender;
            var focusedNode = nav?.FocusedNode;
            sb.Append("focused node: ").Append(focusedNode != null
                ? focusedNode.Id + " stop=" + focusedNode.StopKey
                    + (focusedNode.RegionKey != null ? " region=" + focusedNode.RegionKey : "")
                : "(none)").Append('\n');
            if (render != null)
            {
                sb.Append("graph (").Append(render.Order.Count).Append(" nodes):\n");
                foreach (var n in render.Order)
                    sb.Append(ReferenceEquals(n, focusedNode) ? "* " : "  ")
                      .Append(n.Id).Append(" stop=").Append(n.StopKey)
                      .Append(" \"").Append(GraphAnnouncer.LeafText(n)).Append("\"\n");
            }
            var focused = ScreenManager.Current;
            sb.Append("stack:");
            foreach (var s in ScreenManager.Stack)
                for (var c = s; c != null; c = c.ActiveChild)
                    sb.Append(' ').Append(c.Key).Append('(').Append(c.Layer).Append(')')
                      .Append(ReferenceEquals(c, focused) ? "*" : "");
            if (ScreenManager.Stack.Count == 0) sb.Append(" (empty)");
            sb.Append('\n');
            var cats = new System.Collections.Generic.List<InputCategory>();
            ScreenManager.ProvideCategories(cats);
            if (!cats.Contains(InputCategory.Global)) cats.Add(InputCategory.Global);
            sb.Append("categories: ").Append(string.Join(", ", cats)).Append('\n');
            foreach (var a in InputManager.Actions)
                sb.Append(a.Category).Append('.').Append(a.Key)
                  .Append(" [").Append(a.BindingsDisplay).Append("] ")
                  .Append(a.DisplayLabel).Append('\n');
            return sb.ToString();
        }

        public string TypeText(string text) => null;
    }
}
