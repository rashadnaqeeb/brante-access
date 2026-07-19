using System.Collections.Generic;
using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using MapArea = _Scripts.AMVCC.Views.Windows.Map.MapAreaItemBehaviour;
using Tooltip = _Scripts.AMVCC.Views.TooltipWithTitleBehavior;
using GameLoc = I2.Loc.LocalizationManager;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The Map of the Empire window: a picture of the Empire whose readable content is the
    /// hover surfaces - city areas (visible name labels) and province regions (invisible
    /// hover zones, named only in their tooltip keys). One row per surface in prefab layout
    /// order, cities then provinces; a province row carries the province word (the game
    /// shows the distinction only as map layout). Space reads the same Map.X.Description
    /// the game's hover tooltip shows. No click action exists anywhere on the map.
    /// </summary>
    public sealed class MapWindowScreen : Screen
    {
        public override string Key => "window:map";
        public override int Layer => 10;

        public override bool IsActive()
        {
            var w = GameUi.OpenedWindow;
            return w != null && w.name == "Window_Map";
        }

        public override Message ScreenName
            => Message.MaybeRaw(GameLoc.GetTranslation("HUD.Map"));

        public override void Build(GraphBuilder b)
        {
            var w = GameUi.OpenedWindow;
            if (w == null) return;

            b.PushContext("", role: null, positions: true);
            foreach (var tip in w.GetComponentsInChildren<Tooltip>())
            {
                var t = tip;
                if (!UiWidgets.Visible(t.gameObject)) continue;
                var area = t.GetComponent<MapArea>();
                // City labels resolve through the item's own I2 term, not the rendered TMP - the
                // label component localizes on its own Start() timing, so the first-open frame
                // still shows the prefab's serialized text (the pairing label IS
                // GetTranslation(TitleKey), see the map detail pass in ROADMAP).
                System.Func<string> name = () => GameLoc.GetTranslation(t.TitleKey);
                var label = area != null
                    ? name
                    : () => Loc.T("map.province", new { name = name() });
                b.AddItem(ControlId.Referenced(t, "map:area:" + t.gameObject.name),
                    new NodeVtable
                    {
                        ControlType = ControlTypes.Text,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(label, kind: AnnouncementKinds.Label),
                        },
                        SearchText = name,
                        OnTooltip = string.IsNullOrEmpty(t.TitleMainText)
                            ? (System.Action)null
                            : () => Mod.Speech.Speak(
                                GameLoc.GetTranslation(t.TitleMainText)),
                    });
            }
            b.PopContext();

            HudBar.Build(b);
        }

        public override string HelpText() => GameUi.WindowHelp();

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, null, _ => HudBar.ClickBack());
        }
    }
}
