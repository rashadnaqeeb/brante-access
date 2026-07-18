using System.Collections.Generic;
using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using MapArea = _Scripts.AMVCC.Views.Windows.Map.MapAreaItemBehaviour;
using GameLoc = I2.Loc.LocalizationManager;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The Map of the Empire window: a picture of the Empire whose only readable content is
    /// the province and city name labels (the game's area items are hover-highlight only -
    /// no click action exists). One text row per visible area, in the prefab's layout order.
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
            foreach (var area in w.GetComponentsInChildren<MapArea>())
            {
                var item = area;
                if (!UiWidgets.Visible(item.gameObject)) continue;
                b.AddItem(ControlId.Referenced(item, "map:area:" + item.gameObject.name),
                    new NodeVtable
                    {
                        ControlType = ControlTypes.Text,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(() => item.Text.text,
                                kind: AnnouncementKinds.Label),
                        },
                        SearchText = () => item.Text.text,
                    });
            }
            b.PopContext();

            HudBar.Build(b);
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, null, _ => HudBar.ClickBack());
        }
    }
}
