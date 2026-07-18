using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine; // Application.OpenURL
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The Help submenu, opened from the mod menu (Ctrl+M → Help). For now it holds a single entry —
    /// Read documentation — and more help options will live here later. Mirrors the ModSettings pattern: a
    /// static open flag, layered above the launcher so it stacks on top and returns to it on Escape.
    ///
    /// Read documentation opens the book in your default browser, preferring the copy bundled with this
    /// install (offline, matched to your version) and falling back to the hosted site when no local copy
    /// shipped with the build. Graph-native.
    /// </summary>
    public sealed class HelpScreen : Screen
    {
        // The hosted docs (GitHub Pages) — the fallback when this install has no bundled copy.
        private const string DocsUrl = "https://bradjrenshaw.github.io/wotr-access/";

        private static bool s_open;
        public static void Open() { s_open = true; }
        public static void CloseMenu() { s_open = false; }

        public override string Key => "overlay.help";
        public override string ScreenName => Loc.T("screen.help");
        public override int Layer => 38; // above the mod-menu launcher (35), so it stacks on top and returns to it
        public override bool IsActive() => s_open;

        // Escape closes the submenu (back to the mod menu beneath it).
        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => CloseMenu());
        }


        public override void Build(GraphBuilder b)
        {
            b.AddItem(ControlId.Structural("help:docs"),
                GraphNodes.Button(() => Loc.T("menu.read_docs"), OpenDocs));
        }

        // Prefer the docs bundled inside the mod folder (Main.ModDir/docs); fall back to the hosted site.
        private static void OpenDocs()
        {
            Tts.Speak(Loc.T("menu.opening_docs"));
            try
            {
                var local = string.IsNullOrEmpty(Main.ModDir) ? null : Path.Combine(Main.ModDir, "docs", "index.html");
                if (local != null && File.Exists(local))
                {
                    Application.OpenURL(new Uri(local).AbsoluteUri); // file:/// → default browser
                    return;
                }
            }
            catch (Exception e) { Main.Log?.Error("Open local docs failed: " + e.Message); }
            Application.OpenURL(DocsUrl);
        }
    }
}
