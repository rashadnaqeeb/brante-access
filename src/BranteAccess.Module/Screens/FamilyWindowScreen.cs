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
    /// The Family window as a browsable tree, shaped by the game's own layout: the tile row
    /// containers under FamilyTree ARE the generations (grandfather row, parents row, hero's
    /// row), read top row first, left to right within a row. Up/Down walks members in that
    /// order with positions restarting per generation; each member row folds on everything the
    /// game's per-character info panel shows (name and role from the live tile, then estate,
    /// relation value with the game's relation word, any set status), re-read from the model at
    /// speech time. A non-hero member is an expandable group: Right expands and steps into the
    /// info panel's description paragraph and the status detail behind the game's help icon;
    /// Left collapses. Space still reads the same detail in one go. Enter runs the tile's own
    /// button: the game marks the member selected and fills its visual panel; the hero tile
    /// opens the Character window (the game's own redirect - it tracks no model data for the
    /// hero, so the hero row is a plain item).
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

                b.PushContext("", role: null, positions: true);
                foreach (var member in tiles)
                {
                    var tile = member;
                    var isHero = tile.CharacterObject.Name == CharacterList.Hero;
                    var id = ControlId.Referenced(tile,
                        "family:member:" + tile.CharacterObject.Name);
                    var vtable = new NodeVtable
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
                    };

                    if (isHero)
                    {
                        b.AddItem(id, vtable);
                        continue;
                    }

                    b.BeginGroup(id, vtable);
                    // Children built only while expanded (the model calls stay off the
                    // per-frame path for collapsed members).
                    if (b.IsExpanded(id))
                    {
                        var co = tile.CharacterObject;
                        // Structural ids only: a child sharing the tile reference would win the
                        // header's tier-1 focus match and yank focus off the child every rebuild.
                        if (!string.IsNullOrEmpty(Readouts.CharacterDescriptionText(co)))
                            b.AddItem(ControlId.Structural(id.StructuralKey + ":description"),
                                new NodeVtable
                                {
                                    Announcements = new[]
                                    {
                                        new NodeAnnouncement(
                                            () => Readouts.CharacterDescriptionText(co)),
                                    },
                                    ExcludeFromSearch = true,
                                });
                        if (Readouts.CharacterStatusDetail(co) != null)
                            b.AddItem(ControlId.Structural(id.StructuralKey + ":status"),
                                new NodeVtable
                                {
                                    Announcements = new[]
                                    {
                                        new NodeAnnouncement(
                                            () => Readouts.CharacterStatusDetail(co)),
                                    },
                                    ExcludeFromSearch = true,
                                });
                    }
                    b.EndGroup();
                }
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

        public override string HelpText() => GameUi.WindowHelp();

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, null, _ => HudBar.ClickBack());
        }
    }
}
