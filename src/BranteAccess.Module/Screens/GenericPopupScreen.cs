using BranteAccess.Module.Game;
using BranteAccess.Module.UI;
using BranteAccess.Module.UI.Graph;

namespace BranteAccess.Module.Screens
{
    /// <summary>
    /// The UIManager popup slot's generic reader (PopupsEnum family: InfoPopup, TriggerPopup,
    /// ObjectivePopup, EventPopup, ...): these popups are game-composed panels - title,
    /// body text, one or two buttons - with no shared model to adapt, so the swept live
    /// components are the readout. Dedicated screens (interlude) sit at a higher layer and
    /// win the surfaces they know better.
    /// </summary>
    public sealed class GenericPopupScreen : Screen
    {
        public override string Key => "popup";
        public override int Layer => 20;

        // Live reference for first-frame bookkeeping only, never content.
        private UnityEngine.GameObject _seen;
        private int _seenFrame;

        public override bool IsActive()
        {
            var p = GameUi.OpenedPopup;
            // Popups with dedicated screens belong to them for their whole lifecycle: the
            // generic sweep announcing one on its very first frame reads the prefab's
            // serialized editor text before the game's own configure pass runs (heard live
            // as Russian/Portuguese titles on chapter entry and the side popup).
            if (p == null || !p.activeInHierarchy
                || p.GetComponent<_Scripts.AMVCC.Views.Windows.TriggerScenePopupController>() != null
                || p.GetComponent<InsurrectionSidePopupController>() != null)
            {
                _seen = null;
                return false;
            }
            // Popups that localize in Start (finals reminder, heard live as a Russian title)
            // still show serialized editor text on the frame UIManager assigns the slot; every
            // popup therefore activates one frame after first observation - Start has run by
            // then, and synchronously-configured popups lose nothing audible.
            if (!ReferenceEquals(p, _seen))
            {
                _seen = p;
                _seenFrame = UnityEngine.Time.frameCount;
            }
            return UnityEngine.Time.frameCount > _seenFrame;
        }

        public override void Build(GraphBuilder b)
        {
            var p = GameUi.OpenedPopup;
            if (p == null) return;
            PanelSweep.Build(b, p, "popup");
        }
    }
}
