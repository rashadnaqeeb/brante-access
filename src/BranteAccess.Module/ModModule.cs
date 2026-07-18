using System.Text;
using BranteAccess.Core.Modularity;
using BranteAccess.Module.Input;
using BranteAccess.Module.Patches;
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
            FocusModePatches.Apply();
            // Generation is not logged here: the loader bumps it only after Load succeeds (so a
            // failed reload leaves the old module live), and it logs the real number itself.
            Mod.Log("module code up, waiting for first tick");
        }

        public void Tick()
        {
            Localization.LocalizationManager.Tick();
            InputManager.Tick();
        }

        public void Dispose()
        {
            FocusModePatches.Remove();
            Mod.Log("module disposed (generation " + Mod.Host.ModuleGeneration + ")");
        }

        // Global actions live here; screens register their own categories' actions when the screen
        // stack lands. Registration labels are fallbacks - DisplayLabel resolves lang/<code>/settings.txt.
        private static void RegisterActions()
        {
            InputManager.Register("focusmode", "Toggle focus mode", InputCategory.Global, FocusMode.Toggle)
                .AddBinding(KeyCode.F10);
        }

        // IDevDriver - probed by the host per request, so the newest generation always answers.

        public string DispatchUi(string action)
            => InputManager.Dispatch(action) ? "dispatched " + action : null;

        public string DescribeNav()
        {
            var sb = new StringBuilder();
            sb.Append("focus mode ").Append(FocusMode.Active ? "on" : "off")
              .Append("; language ").Append(Localization.LocalizationManager.Language)
              .Append("; navigator not built yet\n");
            foreach (var a in InputManager.Actions)
                sb.Append(a.Category).Append('.').Append(a.Key)
                  .Append(" [").Append(a.BindingsDisplay).Append("] ")
                  .Append(a.DisplayLabel).Append('\n');
            return sb.ToString();
        }

        public string TypeText(string text) => null;
    }
}
