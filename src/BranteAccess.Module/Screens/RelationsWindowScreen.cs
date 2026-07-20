using System.Collections.Generic;
using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using RelationsWindow = _Scripts.AMVCC.Views.Windows.Relations.RelationsWindowController;
using CharacterTile = _Scripts.AMVCC.Views.Windows.Family.StatusRelationGetSet;
using RelationTile = _Scripts.AMVCC.Views.Windows.Components.RelationCharacterComponent;
using WhoIsEnum = _Scripts.AMVCC.Models.Static.WhoIs;
using GameLoc = I2.Loc.LocalizationManager;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The Relations window: one row per unlocked non-family acquaintance with everything the
    /// game's per-character page shows folded on - name from the live tile, then the role,
    /// estate, relation value with the game's relation word, and any set status, all re-read
    /// from the model at speech time (the same ParametersManager calls the game's own select
    /// handler makes). Space reads the description paragraph plus the status detail behind
    /// the page's help icon. Enter runs the tile's own button (the game marks the character
    /// selected and fills its visual page) and opens that page as a browsable sub-screen
    /// (<see cref="CharacterInfoScreen"/>), closed with Escape - the same flow as the Family
    /// window. The game's empty-state placeholder text is the sole row when no one has been
    /// met yet.
    /// </summary>
    public sealed class RelationsWindowScreen : Screen
    {
        public override string Key => "window:relations";
        public override int Layer => 10;

        public override bool IsActive()
        {
            var w = GameUi.OpenedWindow;
            return w != null && w.name == "Window_Relations";
        }

        public override Message ScreenName
            => Message.MaybeRaw(GameLoc.GetTranslation("HUD.Relation"));

        private static RelationsWindow Window()
        {
            var w = GameUi.OpenedWindow;
            return w == null ? null : w.GetComponent<RelationsWindow>();
        }

        private static List<CharacterTile> Tiles(RelationsWindow wm)
            => new List<CharacterTile>(
                wm.CharacterContainer.GetComponentsInChildren<CharacterTile>());

        // The controller's own generate query: every unlocked non-family character gets a tile.
        private static int ExpectedTileCount()
        {
            var helper = _Scripts.Helpers.CharacterParametersSerializeHelper.Initiate;
            var count = 0;
            foreach (var cm in helper.Characters)
                if (cm.IsUnlocked
                    && !helper.CharacterObjects.Find(co => co.Name == cm.Character).IsFamily)
                    count++;
            return count;
        }

        public override void Build(GraphBuilder b)
        {
            var wm = Window();
            if (wm == null) return;
            // The tile list is generated in the controller's Start(), a beat after ShowWindow
            // instantiates the prefab; until then the prefab's own placeholder sits active with
            // unlocalized serialized text. The same model query Start runs (unlocked non-family
            // characters) says how many tiles to expect, so the graph waits for that count -
            // and trusts the placeholder only for a genuinely empty list, where the game
            // activating it has also run its localization.
            var tiles = Tiles(wm);
            if (tiles.Count < ExpectedTileCount()) return;
            if (tiles.Count == 0 && !wm.Placeholder.activeSelf) return;

            b.PushContext("", role: null, positions: true);

            if (tiles.Count == 0)
                b.AddItem(ControlId.Referenced(wm.Placeholder, "relations:empty"), new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => UiWidgets.LabelText(wm.Placeholder),
                            kind: AnnouncementKinds.Label),
                    },
                });

            foreach (var t in tiles)
            {
                var tile = t;
                if (!UiWidgets.Visible(tile.gameObject)) continue;
                b.AddItem(
                    ControlId.Referenced(tile, "relations:member:" + tile.CharacterObject.Name),
                    new NodeVtable
                    {
                        ControlType = ControlTypes.Button,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(() => CharacterLabel(tile),
                                kind: AnnouncementKinds.Label),
                            new NodeAnnouncement(() => IsSelected(tile)
                                    ? Loc.T("state.selected") : null,
                                kind: AnnouncementKinds.Selected),
                        },
                        SearchText = () => Tile(tile).Name.text,
                        OnTooltip = () =>
                            Mod.Speech.Speak(Readouts.CharacterDetail(tile.CharacterObject)),
                        OnActivate = () =>
                        {
                            UiWidgets.Click(tile.gameObject);
                            PushChild(new CharacterInfoScreen(tile,
                                "window:relations:member", () => MemberName(tile)));
                        },
                    });
            }
            b.PopContext();

            HudBar.Build(b);
        }

        private static RelationTile Tile(CharacterTile tile)
            => tile.GetComponent<RelationTile>();

        // The game marks the selected character by disabling its tile button (SelectCharacter).
        private static bool IsSelected(CharacterTile tile)
        {
            var btn = tile.GetComponent<UnityEngine.UI.Button>();
            return btn != null && !btn.interactable;
        }

        // The sub-screen's name, matching the game page's header: tile name, then the role -
        // the same first parts the row label leads with.
        private static string MemberName(CharacterTile tile)
        {
            var parts = new List<string> { Readouts.Collapse(Tile(tile).Name.text) };
            var role = GameLoc.GetTranslation(
                System.Enum.GetName(typeof(WhoIsEnum), tile.CharacterObject.WhoIs));
            if (!string.IsNullOrEmpty(role)) parts.Add(role);
            return string.Join(", ", parts.ToArray());
        }

        // Everything the game's character page shows, folded onto the row: tile name, then the
        // role, estate, relation (the page's own signed value + word format, labeled with the
        // game's Relations term), and the status word when one is set (the game shows a bare
        // dash and hides its help icon for CharacterStatus.Good).
        private static string CharacterLabel(CharacterTile tile)
        {
            var co = tile.CharacterObject;
            var parts = new List<string> { Readouts.Collapse(Tile(tile).Name.text) };
            var role = GameLoc.GetTranslation(
                System.Enum.GetName(typeof(WhoIsEnum), co.WhoIs));
            if (!string.IsNullOrEmpty(role)) parts.Add(role);
            parts.Add(Readouts.CharacterEstate(co));
            parts.Add(Readouts.CharacterRelationPair(co));
            var status = Readouts.CharacterStatusWord(co);
            if (status != null) parts.Add(status);
            return string.Join(", ", parts.ToArray());
        }

        public override string HelpText() => GameUi.WindowHelp();

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, null, _ => HudBar.ClickBack());
        }
    }
}
