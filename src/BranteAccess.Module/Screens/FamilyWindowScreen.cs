using System.Collections.Generic;
using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using FamilyWindow = _Scripts.AMVCC.Views.Windows.Family.FamilyWindowController;
using FamilyTile = _Scripts.AMVCC.Views.Windows.Family.StatusRelationGetSet;
using RelationTile = _Scripts.AMVCC.Views.Windows.Components.RelationCharacterComponent;
using CharacterList = _Scripts.AMVCC.Models.Static.CharacterList;
using ParametersManager = _Scripts.Managers.ParametersManager;
using GameLoc = I2.Loc.LocalizationManager;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The Family window: one row per family member with everything the game's per-character
    /// info panel shows folded on - name and role from the live tile, then estate, relation
    /// value with the game's relation word, and any set status, all re-read from the model
    /// at speech time (the same ParametersManager calls the game's own select handler makes).
    /// Space reads the character's description paragraph plus the status detail the game puts
    /// behind its help-icon tooltip. Enter runs the tile's own button: the game marks the
    /// member selected and fills its visual panel; the hero tile opens the Character window.
    /// </summary>
    public sealed class FamilyWindowScreen : Screen
    {
        public override string Key => "window:family";
        public override int Layer => 10;

        public override bool IsActive()
        {
            var w = GameUi.OpenedWindow;
            return w != null && w.name == "Window_Family";
        }

        public override Message ScreenName
            => Message.MaybeRaw(GameLoc.GetTranslation("HUD.Family"));

        private static FamilyWindow Window()
        {
            var w = GameUi.OpenedWindow;
            return w == null ? null : w.GetComponent<FamilyWindow>();
        }

        // Selecting a tile changes no focus, so the state change is the delivery: watch the
        // game's own selected-member marker and speak it once per change. Seeded on focus so
        // entering the window stays quiet about a pre-existing selection.
        private FamilyTile _spokenSelected;

        private static FamilyTile SelectedMember()
        {
            var wm = Window();
            if (wm == null) return null;
            foreach (var t in wm.Characters)
                if (IsSelected(t)) return t;
            return null;
        }

        public override void OnFocus()
        {
            base.OnFocus();
            _spokenSelected = SelectedMember();
        }

        public override void OnUpdate()
        {
            var selected = SelectedMember();
            if (selected == _spokenSelected) return;
            _spokenSelected = selected;
            if (selected != null)
                Mod.Speech.Speak(Loc.T("member.selected",
                    new { name = Readouts.Collapse(Tile(selected).Name.text) }));
        }

        public override void Build(GraphBuilder b)
        {
            var wm = Window();
            if (wm == null) return;
            // Tiles fill in StatusRelationGetSet.Start a beat after ShowWindow; until then
            // every tile text is a serialized prefab placeholder. The hero tile's populate
            // result (the game writes HeroName + surname into it) gates the graph.
            var hero = wm.Characters.Find(t => t.CharacterObject.Name == CharacterList.Hero);
            if (hero == null || !Tile(hero).Name.text.StartsWith(
                    ParametersManager.Instance.HeroName))
                return;

            b.PushContext("", role: null, positions: true);
            foreach (var member in wm.Characters)
            {
                var tile = member;
                if (!UiWidgets.Visible(tile.gameObject)) continue;
                var isHero = tile.CharacterObject.Name == CharacterList.Hero;
                b.AddItem(ControlId.Referenced(tile, "family:member:" + tile.CharacterObject.Name),
                    new NodeVtable
                    {
                        ControlType = ControlTypes.Button,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(() => MemberLabel(tile),
                                kind: AnnouncementKinds.Label),
                            new NodeAnnouncement(() => IsSelected(tile)
                                    ? Loc.T("state.selected") : null,
                                kind: AnnouncementKinds.Selected),
                        },
                        SearchText = () => Tile(tile).Name.text,
                        OnTooltip = isHero ? (System.Action)null
                            : () => Mod.Speech.Speak(Readouts.CharacterDetail(tile.CharacterObject)),
                        OnActivate = () => UiWidgets.Click(tile.gameObject),
                    });
            }
            b.PopContext();

            HudBar.Build(b);
        }

        private static RelationTile Tile(FamilyTile member)
            => member.GetComponent<RelationTile>();

        // The game marks the selected member by disabling its tile button (SelectCharacter).
        private static bool IsSelected(FamilyTile member)
        {
            var btn = member.GetComponent<UnityEngine.UI.Button>();
            return btn != null && !btn.interactable;
        }

        // Everything the game's info panel shows for the member, folded onto the row: tile
        // name and role, then estate, relation (the panel's own signed value + word format,
        // labeled with the game's Relations term), and the status word when one is set (the
        // game shows a bare dash and hides its help icon for CharacterStatus.Good).
        private static string MemberLabel(FamilyTile member)
        {
            var tile = Tile(member);
            var parts = new List<string> { Readouts.Collapse(tile.Name.text) };
            if (tile.WhoIs != null) parts.Add(tile.WhoIs.text);
            if (member.CharacterObject.Name != CharacterList.Hero)
            {
                var co = member.CharacterObject;
                parts.Add(Readouts.CharacterEstate(co));
                parts.Add(Readouts.CharacterRelationPair(co));
                var status = Readouts.CharacterStatusWord(co);
                if (status != null) parts.Add(status);
            }
            return string.Join(", ", parts.ToArray());
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, null, _ => HudBar.ClickBack());
        }
    }
}
