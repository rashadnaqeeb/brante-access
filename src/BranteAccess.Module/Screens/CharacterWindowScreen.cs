using System.Collections.Generic;
using System.Reflection;
using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using UnityEngine;
using CharacterWindow = _Scripts.AMVCC.Views.Windows.Character.CharacterWindowManager;
using GameLoc = I2.Loc.LocalizationManager;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The Character window ("Personality"): identity rows (name, age, estate, occupation),
    /// the deaths row (the game's skull strip - its label is the game's own "Deaths" text, the
    /// count is the Death parameter), then the chapter's parameter rows with name, value and
    /// segment folded on and the scale readout on Space. All rows re-read live components or
    /// the model at speech time; the HUD bar rides along as its own Tab-stop.
    /// </summary>
    public sealed class CharacterWindowScreen : Screen
    {
        public override string Key => "window:character";
        public override int Layer => 10;

        public override bool IsActive()
        {
            var w = GameUi.OpenedWindow;
            return w != null && w.name == "Window_Character";
        }

        public override Message ScreenName
            => Message.MaybeRaw(GameLoc.GetTranslation("HUD.Character"));

        private static readonly FieldInfo NameField = typeof(CharacterWindow)
            .GetField("_name", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo SurnameField = typeof(CharacterWindow)
            .GetField("_surname", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo PostField = typeof(CharacterWindow)
            .GetField("_post", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo EstateField = typeof(CharacterWindow)
            .GetField("_estate", BindingFlags.NonPublic | BindingFlags.Instance);

        private static CharacterWindow Window()
        {
            var w = GameUi.OpenedWindow;
            return w == null ? null : w.GetComponent<CharacterWindow>();
        }

        private static TMPro.TextMeshProUGUI Text(FieldInfo field, CharacterWindow wm)
            => (TMPro.TextMeshProUGUI)field.GetValue(wm);

        // A label-value pair as the game lays it out: the pair container's visible texts in
        // hierarchy order ("Age: 6 Years"). Null when the game hides the container.
        private static string PairRow(Component member)
        {
            if (member == null || !UiWidgets.Visible(member.gameObject)) return null;
            var parts = new List<string>();
            foreach (var t in member.transform.parent.GetComponentsInChildren<TMPro.TMP_Text>())
                if (UiWidgets.Visible(t.gameObject) && !string.IsNullOrEmpty(t.text))
                    parts.Add(t.text);
            return parts.Count == 0 ? null : string.Join(" ", parts.ToArray());
        }

        // The skull strip's own label text ("Deaths") - the game renders the count as sprites.
        private static TMPro.TMP_Text DeathsLabel(CharacterWindow wm)
        {
            foreach (var t in wm.GetComponentsInChildren<TMPro.TMP_Text>())
                if (t.name == "Lives") return t;
            return null;
        }

        public override void Build(GraphBuilder b)
        {
            var wm = Window();
            if (wm == null) return;
            // The game fills the freshly-instantiated prefab a beat after ShowWindow - until
            // its Start() has written the hero name, every text is a serialized placeholder
            // (caught live: the entry seat spoke Russian prefab text). No graph until the
            // game's own populate result is in place; the seat then reads real rows.
            if (Text(NameField, wm).text != _Scripts.Managers.ParametersManager.Instance.HeroName)
                return;

            b.PushContext("", role: null, positions: false);

            AddRow(b, ControlId.Structural("character:name"), () =>
            {
                var w = Window();
                return Loc.T("hud.pair", new
                {
                    label = Text(NameField, w).text,
                    value = Text(SurnameField, w).text,
                });
            });
            AddRow(b, ControlId.Structural("character:age"), () => PairRow(Window().Age));
            AddRow(b, ControlId.Structural("character:estate"),
                () => PairRow(Text(EstateField, Window())));
            AddRow(b, ControlId.Structural("character:post"),
                () => PairRow(Text(PostField, Window())));

            if (DeathsLabel(wm) != null)
                AddRow(b, ControlId.Structural("character:deaths"), () =>
                {
                    var w = Window();
                    return Loc.T("character.deaths", new
                    {
                        label = DeathsLabel(w).text,
                        value = _Scripts.Managers.ParametersManager.Instance
                            .GetParameterValue(w.LiveParameter),
                        max = w.LiveImages.Length,
                    });
                });

            ParameterRows.Add(b, wm, "character:parameter:");

            b.PopContext();

            HudBar.Build(b);
        }

        private static void AddRow(GraphBuilder b, ControlId id, System.Func<string> text)
        {
            if (text() == null) return;
            b.AddItem(id, new NodeVtable
            {
                ControlType = ControlTypes.Text,
                Announcements = new[]
                {
                    new NodeAnnouncement(text, kind: AnnouncementKinds.Label),
                },
            });
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, null, _ => HudBar.ClickBack());
        }
    }
}
