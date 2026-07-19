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

        public override bool IsActive()
        {
            var p = GameUi.OpenedPopup;
            // The trigger popup belongs to its dedicated screen for its whole lifecycle:
            // the generic sweep announcing it on its very first frame reads the prefab's
            // serialized editor text before the game's ConfigurePopup runs (heard live as
            // Russian/Portuguese titles on chapter entry).
            return p != null && p.activeInHierarchy
                && p.GetComponent<_Scripts.AMVCC.Views.Windows.TriggerScenePopupController>() == null;
        }

        public override void Build(GraphBuilder b)
        {
            var p = GameUi.OpenedPopup;
            if (p == null) return;
            PanelSweep.Build(b, p, "popup");
        }
    }
}
