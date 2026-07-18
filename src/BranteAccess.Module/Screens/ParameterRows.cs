using BranteAccess.Module.Game;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;
using UnityEngine;
using ParameterComponent = _Scripts.AMVCC.Views.Windows.ParameterComponent;
using ParameterGetSet = _Scripts.AMVCC.Views.Windows.ParameterGetSet;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The stat windows' shared parameter rows (Character, Home, ...): every live
    /// ParameterComponent under the root becomes one row with name, value and segment folded
    /// on and the full scale breakdown on Space. The component search excludes inactive
    /// panels, so only the era/state the game shows is read.
    /// </summary>
    internal static class ParameterRows
    {
        public static void Add(GraphBuilder b, Component root, string idPrefix)
        {
            foreach (var par in root.GetComponentsInChildren<ParameterComponent>())
            {
                var pc = par;
                var pgs = pc.GetComponent<ParameterGetSet>();
                b.AddItem(ControlId.Referenced(pc, idPrefix + pc.GetInstanceID()),
                    new NodeVtable
                    {
                        ControlType = ControlTypes.Text,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(() => pc.Name.text + " " + pc.TextValue.text
                                + (string.IsNullOrEmpty(pc.Descr.text) ? "" : ", " + pc.Descr.text),
                                kind: AnnouncementKinds.Label),
                        },
                        OnTooltip = pgs == null ? (System.Action)null
                            : () => Mod.Speech.Speak(Readouts.ParameterScales(pgs.Parameter)),
                    });
            }
        }
    }
}
