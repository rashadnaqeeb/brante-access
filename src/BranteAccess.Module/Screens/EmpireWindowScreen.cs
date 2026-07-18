using System.Collections.Generic;
using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using EmpireWindow = _Scripts.AMVCC.Views.Windows.Empire.EmpireWindowController;
using ParameterComponent = _Scripts.AMVCC.Views.Windows.ParameterComponent;
using ParameterGetSet = _Scripts.AMVCC.Views.Windows.ParameterGetSet;
using ParametersList = _Scripts.AMVCC.Models.Static.ParametersList;
using GameLoc = I2.Loc.LocalizationManager;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The Province window (the game's Empire surface): the Overseer and Patriarch rows as
    /// label-value pairs using the window's own header texts, with the character's description
    /// on Space where the game tracks a character behind the office, then the province
    /// parameter rows through the shared ParameterRows sweep (name, value, segment folded on;
    /// scale breakdown on Space).
    /// </summary>
    public sealed class EmpireWindowScreen : Screen
    {
        public override string Key => "window:empire";
        public override int Layer => 10;

        public override bool IsActive()
        {
            var w = GameUi.OpenedWindow;
            return w != null && w.name == "Window_Empire";
        }

        public override Message ScreenName
            => Message.MaybeRaw(GameLoc.GetTranslation("HUD.Empire"));

        private static EmpireWindow Window()
        {
            var w = GameUi.OpenedWindow;
            return w == null ? null : w.GetComponent<EmpireWindow>();
        }

        public override void Build(GraphBuilder b)
        {
            var wm = Window();
            if (wm == null) return;
            // The game fills the freshly-instantiated prefab in its Start(), a beat after
            // ShowWindow (Home-window precedent). Gate on the first active parameter row
            // matching its own I2 translation.
            var first = FirstActiveParameter(wm);
            if (first == null) return;
            var firstName = System.Enum.GetName(typeof(ParametersList),
                first.GetComponent<ParameterGetSet>().Parameter.ParameterName);
            if (first.Name.text != GameLoc.GetTranslation(firstName)) return;

            b.PushContext("", role: null, positions: false);
            OfficeRow(b, "empire:overseer", wm.ImperatorName,
                () => Window().Imperator.Character);
            OfficeRow(b, "empire:patriarch", wm.PatriarkhName,
                () => Window().Patriarkh.Character);
            ParameterRows.Add(b, wm, "empire:parameter:");
            b.PopContext();

            HudBar.Build(b);
        }

        private static ParameterComponent FirstActiveParameter(EmpireWindow wm)
        {
            foreach (var go in wm.Parameters)
                if (go.activeSelf) return go.GetComponent<ParameterComponent>();
            return null;
        }

        // An office row: the window's own header label ("Overseer", "Patriarch") paired with
        // the office holder's name; Space reads the tracked character's description where the
        // game sets one (elective outcomes like "None" and the hero leave it unset).
        private static void OfficeRow(GraphBuilder b, string id,
            TMPro.TextMeshProUGUI nameText,
            System.Func<_Scripts.AMVCC.Models.Static.Character> character)
        {
            var header = nameText.transform.parent.Find("Name")
                .GetComponent<TMPro.TMP_Text>();
            b.AddItem(ControlId.Referenced(nameText, id), new NodeVtable
            {
                ControlType = ControlTypes.Text,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => Loc.T("hud.pair", new
                    {
                        label = header.text,
                        value = nameText.text,
                    }), kind: AnnouncementKinds.Label),
                },
                OnTooltip = () =>
                {
                    var co = character();
                    if (co != null)
                        Mod.Speech.Speak(Readouts.CharacterDetail(co));
                },
            });
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, null, _ => HudBar.ClickBack());
        }
    }
}
