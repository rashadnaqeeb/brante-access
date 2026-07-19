using BranteAccess.Module.Game;
using BranteAccess.Module.Speech;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using UnityEngine;
using TimelineComponent = _Scripts.AMVCC.Views.Windows.Destiny.TimelineComponent;
using TimelineYearComponent = _Scripts.AMVCC.Views.Windows.Destiny.TimelineYearComponent;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The life-timeline scene (end of the game, before the finals): the hero's full name
    /// heads the screen, then the whole life as the game lays it out - a year marker row, then
    /// that year's unlocked events as "category, event" rows - and the End Life button that
    /// hands the flow to the finals. Rows follow the scroll content's child order, which is
    /// the game's own instantiation order.
    /// </summary>
    public sealed class LiveTimelineScreen : Screen
    {
        public override string Key => "livetimeline";
        public override int Layer => 0;

        public override bool IsActive()
            => Object.FindObjectOfType<WindowLiveTimelineController>() != null;

        public override Message ScreenName
        {
            get
            {
                var w = Object.FindObjectOfType<WindowLiveTimelineController>();
                return w == null ? null : Message.MaybeRaw(Readouts.Collapse(w.Name.text));
            }
        }

        public override void Build(GraphBuilder b)
        {
            var w = Object.FindObjectOfType<WindowLiveTimelineController>();
            if (w == null) return;
            var scroll = w.GetComponentInChildren<UnityEngine.UI.ScrollRect>();
            if (scroll == null) return;

            b.PushContext("", role: null, positions: false);

            var content = scroll.content;
            for (int i = 0; i < content.childCount; i++)
            {
                var child = content.GetChild(i);
                if (!child.gameObject.activeInHierarchy) continue;
                var year = child.GetComponent<TimelineYearComponent>();
                if (year != null)
                {
                    var y = year;
                    b.AddItem(ControlId.Referenced(y, "timeline:year:" + i), new NodeVtable
                    {
                        ControlType = ControlTypes.Text,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(() => y.Value.text,
                                kind: AnnouncementKinds.Label),
                        },
                    });
                    continue;
                }
                var ev = child.GetComponent<TimelineComponent>();
                if (ev == null) continue;
                var e = ev;
                b.AddItem(ControlId.Referenced(e, "timeline:event:" + i), new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => Loc.T("hud.pair", new
                        {
                            label = e.BranchName.text,
                            value = e.EventName.text,
                        }), kind: AnnouncementKinds.Label),
                    },
                    SearchText = () => e.EventName.text,
                });
            }

            var btn = w.GetComponentInChildren<UnityEngine.UI.Button>();
            if (btn != null)
                b.AddItem(ControlId.Referenced(btn, "timeline:continue"), new NodeVtable
                {
                    ControlType = ControlTypes.Button,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => UiWidgets.LabelText(btn.gameObject),
                            kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(
                            () => UiWidgets.Interactable(btn.gameObject)
                                ? null : Loc.T("state.unavailable"),
                            kind: AnnouncementKinds.Enabled),
                    },
                    OnActivate = () =>
                    {
                        if (!UiWidgets.Interactable(btn.gameObject)) return;
                        UiWidgets.Click(btn.gameObject);
                    },
                });

            b.PopContext();
        }
    }
}
