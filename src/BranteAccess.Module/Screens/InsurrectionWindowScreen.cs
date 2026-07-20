using System.Collections.Generic;
using System.Reflection;
using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using UnityEngine;
using InsurrectionWindow = _Scripts.AMVCC.Views.Windows.InsurrectionWindowController;
using ParameterComponent = _Scripts.AMVCC.Views.Windows.ParameterComponent;
using ParameterGetSet = _Scripts.AMVCC.Views.Windows.ParameterGetSet;
using ParametersList = _Scripts.AMVCC.Models.Static.ParametersList;
using GameLoc = I2.Loc.LocalizationManager;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The Insurrection window (chapter V): title from the window's own I2 key, the side row
    /// under the game's own "Your side" header (the chosen side's name, or the game's no-side
    /// text), then the insurrection parameter rows. That is the whole sighted surface: the
    /// prefab's win-condition panels are inactive unfilled placeholders and the side-icon
    /// tooltip is dead code (its event has no broadcaster anywhere in the assembly), so the
    /// victory conditions read only where the game really shows them - the one-time side
    /// popup and the Destiny objectives.
    /// </summary>
    public sealed class InsurrectionWindowScreen : Screen
    {
        public override string Key => "window:insurrection";
        public override int Layer => 10;

        private static InsurrectionWindow Window()
        {
            var w = GameUi.OpenedWindow;
            return w == null ? null : w.GetComponent<InsurrectionWindow>();
        }

        public override bool IsActive() => Window() != null;

        // The controllers keep their content serialized private - same reflection route as the
        // Character window's era fields. Missing fields throw loudly at first use.
        private static FieldInfo WindowField(string name)
            => typeof(InsurrectionWindow).GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);

        public override Message ScreenName
        {
            get
            {
                var w = Window();
                return w == null ? null
                    : Message.MaybeRaw(GameLoc.GetTranslation(
                        (string)WindowField("_windowTitleKey").GetValue(w)));
            }
        }

        public override void Build(GraphBuilder b)
        {
            var iw = Window();
            if (iw == null) return;
            // Populate gate (Work-window pattern): the game fills the prefab in Start(), a
            // beat after ShowWindow - no graph until the first parameter row reads localized.
            var first = iw.GetComponentInChildren<ParameterComponent>();
            if (first == null) return;
            var firstName = System.Enum.GetName(typeof(ParametersList),
                first.GetComponent<ParameterGetSet>().Parameter.ParameterName);
            if (first.Name.text != GameLoc.GetTranslation(firstName)) return;

            b.PushContext("", role: null, positions: false);

            // The side panel the game shows: side name when chosen, its no-side text otherwise.
            var have = (GameObject)WindowField("_haveSidePanel").GetValue(iw);
            var none = (GameObject)WindowField("_noSidePanel").GetValue(iw);
            var panel = have != null && have.activeSelf ? have
                : none != null && none.activeSelf ? none : null;
            if (panel != null)
            {
                var p = panel;
                // The game's own "Your side" header text sits above the panel; pairing it on
                // gives the bare side value ("Not chosen") its on-screen meaning.
                var title = SideTitle(iw);
                b.AddItem(ControlId.Referenced(p.transform, "insurrection:side"), new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => Loc.T("hud.pair", new
                        {
                            label = title.text,
                            value = UiWidgets.LabelText(p),
                        }), kind: AnnouncementKinds.Label),
                    },
                });
            }

            ParameterRows.Add(b, iw, "insurrection:parameter:");

            b.PopContext();
            HudBar.Build(b);
        }

        private static TMPro.TMP_Text SideTitle(InsurrectionWindow iw)
        {
            foreach (var t in iw.GetComponentsInChildren<TMPro.TMP_Text>(true))
                if (t.name == "SideTitle") return t;
            return null;
        }

        public override string HelpText() => GameUi.WindowHelp();

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, null, _ => HudBar.ClickBack());
        }
    }
}
