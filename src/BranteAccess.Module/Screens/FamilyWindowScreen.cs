using System.Collections.Generic;
using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using FamilyWindow = _Scripts.AMVCC.Views.Windows.Family.FamilyWindowController;
using FamilyTile = _Scripts.AMVCC.Views.Windows.Family.StatusRelationGetSet;
using RelationTile = _Scripts.AMVCC.Views.Windows.Components.RelationCharacterComponent;
using CharacterList = _Scripts.AMVCC.Models.Static.CharacterList;
using GameLoc = I2.Loc.LocalizationManager;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The Family window laid out as the visual tree: the tile row containers under FamilyTree
    /// ARE the generations (grandfather row, parents row, hero's row), navigated as a grid -
    /// Left/Right walks a generation, Up/Down moves between generations holding the column. A
    /// row speaks what its tile shows: name and role, plus the game's selected marker. Enter
    /// runs the tile's own button (the game marks the member selected and fills its info panel)
    /// and opens that panel as a browsable sub-screen (<see cref="CharacterInfoScreen"/>),
    /// closed with Escape; the hero tile instead opens the Character window (the game's own
    /// redirect - it tracks no model data for the hero).
    /// </summary>
    public sealed class FamilyWindowScreen : Screen
    {
        public override string Key => "window:family";
        public override int Layer => 10;

        // All generation rows share one row key: vertical moves hold the column, so Down/Up
        // round-trips instead of snapping to a generation's first tile.
        private const string GenerationRowKey = "family:gen";

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

        public override void Build(GraphBuilder b)
        {
            var wm = Window();
            if (wm == null) return;

            // The generations, straight from the game's layout: tiles grouped by their row
            // container (FamilyTree's FirstRow/SecondRow/ThirdRow), rows ordered top-down.
            // A tile is readable once StatusRelationGetSet.Start has populated it: the gate
            // is the WhoIs text matching the exact translation Start writes (serialized
            // prefab placeholders carry Russian or empty text there). Gating on the tile's
            // own texts survives a save whose hero name is itself empty - the old hero-name
            // gate left this window permanently silent on one (seen live 2026-07-19).
            var rowParents = new List<UnityEngine.Transform>();
            var rowTiles = new List<List<FamilyTile>>();
            foreach (var member in wm.Characters)
            {
                if (!UiWidgets.Visible(member.gameObject)) continue;
                var whoIs = Tile(member).WhoIs;
                if (whoIs != null && whoIs.text != (ExpectedWhoIs(member) ?? "")) continue;
                var parent = member.transform.parent;
                int i = rowParents.IndexOf(parent);
                if (i < 0)
                {
                    rowParents.Add(parent);
                    rowTiles.Add(new List<FamilyTile>());
                    i = rowParents.Count - 1;
                }
                rowTiles[i].Add(member);
            }
            if (rowParents.Count == 0) return;

            var rowOrder = new List<int>();
            for (int i = 0; i < rowParents.Count; i++) rowOrder.Add(i);
            rowOrder.Sort((x, y) => rowParents[y].position.y.CompareTo(rowParents[x].position.y));

            foreach (var r in rowOrder)
            {
                var tiles = rowTiles[r];
                tiles.Sort((a, b) =>
                    a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));

                // One structural level per generation: silent (empty label), but it keeps each
                // lone tile's position group to its own generation.
                b.PushContext("", role: null, positions: true);
                b.StartRow(GenerationRowKey);
                foreach (var member in tiles)
                {
                    var tile = member;
                    var isHero = tile.CharacterObject.Name == CharacterList.Hero;
                    b.AddItem(ControlId.Referenced(tile,
                        "family:member:" + tile.CharacterObject.Name),
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
                                : () => Mod.Speech.Speak(
                                    Readouts.CharacterDetail(tile.CharacterObject)),
                            OnActivate = () =>
                            {
                                UiWidgets.Click(tile.gameObject);
                                if (!isHero) PushChild(new CharacterInfoScreen(tile,
                                    "window:family:member", () => MemberLabel(tile)));
                            },
                        });
                }
                b.EndRow();
                b.PopContext();
            }

            HudBar.Build(b);
        }

        private static RelationTile Tile(FamilyTile member)
            => member.GetComponent<RelationTile>();

        // The WhoIs text StatusRelationGetSet.Start writes: the member's WhoIs term, or the
        // Hero term for the hero tile (its serialized WhoIs enum is meaningless).
        private static string ExpectedWhoIs(FamilyTile member)
            => member.CharacterObject.Name == CharacterList.Hero
                ? GameLoc.GetTranslation(CharacterList.Hero.ToString())
                : GameLoc.GetTranslation(member.CharacterObject.WhoIs.ToString());

        // The game marks the selected member by disabling its tile button (SelectCharacter).
        private static bool IsSelected(FamilyTile member)
        {
            var btn = member.GetComponent<UnityEngine.UI.Button>();
            return btn != null && !btn.interactable;
        }

        // What the tile itself shows: name and role. Everything else lives in the member info
        // sub-screen, like the sighted panel.
        internal static string MemberLabel(FamilyTile member)
        {
            var tile = Tile(member);
            var parts = new List<string> { Readouts.Collapse(tile.Name.text) };
            if (tile.WhoIs != null) parts.Add(tile.WhoIs.text);
            return string.Join(", ", parts.ToArray());
        }

        public override string HelpText() => GameUi.WindowHelp();

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, null, _ => HudBar.ClickBack());
        }
    }
}
