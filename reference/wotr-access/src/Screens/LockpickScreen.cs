using System.Collections.Generic;
using Kingmaker; // Game
using Kingmaker.Blueprints.Root.Strings; // UIStrings.Lockpick
using Kingmaker.UI.MVVM._VM.Lockpick; // LockpickVM, LockpickType
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The lock-picking / disable-device window (<see cref="LockpickVM"/> on
    /// <c>InGameDynamicPartVM.LockpickVM</c>), opened when you interact with a locked container / door /
    /// trap (our <c>ProxyMapObject.Interact</c> now routes locked objects through the game's
    /// <c>LockpickVM.NeedLockpick</c> check). The game offers the Thievery attempt with each option's
    /// success chance: skill only, or spending basic Thieves' Tools (+5) / masterwork (+10) when carried,
    /// plus Destroy/force when the lock is destructible. (Lockpicks are passive items consumed HERE — not
    /// used from the inventory or an action bar, which is why clicking them did nothing.)
    ///
    /// Labels + visibility mirror LockpickBaseView exactly; activate via <c>LockpickVM.OnInteraction</c>
    /// (which closes the window). Back cancels via <c>Close()</c> (the window has no cancel button). Layer
    /// 30, Exclusive.
    ///
    /// Graph-native: declared fresh from the live VM every render; node keys carry the VM's identity, so
    /// a new lock re-homes focus with a fresh readout.
    /// </summary>
    public sealed class LockpickScreen : Screen
    {
        public LockpickScreen() { Wrap = true; }

        public override string Key => "overlay.lockpick";
        public override string ScreenName => Loc.T("screen.lockpick");
        public override int Layer => 30;
        public override bool Exclusive => true;

        public override bool IsActive() => Vm() != null;

        private static LockpickVM Vm()
            => Game.Instance?.RootUiContext?.InGameVM?.DynamicPartVM?.LockpickVM?.Value;

        public override IEnumerable<ElementAction> GetActions()
        {
            // Back = cancel (close without attempting); the game's window has no explicit cancel button.
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => Vm()?.Close());
        }


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            var t = UIStrings.Instance.Lockpick;
            string k = "lockpick:" + vm.GetHashCode() + ":";

            // Skill-only attempt — always available (mirrors m_CommonText).
            b.BeginStop("skill").AddItem(ControlId.Structural(k + "skill"), GraphNodes.Button(
                () => (string)t.Interaction + " - " + vm.BaseChance.Value + "%",
                () => vm.OnInteraction(LockpickType.None)));
            // Basic / masterwork Thieves' Tools, shown only when carried (the +5 / +10 option + its chance).
            if (vm.CanLockpick05.Value != null)
                b.BeginStop("tools5").AddItem(ControlId.Structural(k + "tools5"), GraphNodes.Button(
                    () => vm.CanLockpick05.Value.Blueprint.Name + " (" + vm.CanLockpick05.Value.Count + ") - " + vm.FirstChance.Value + "%",
                    () => vm.OnInteraction(LockpickType.Lockpick5)));
            if (vm.CanLockpick10.Value != null)
                b.BeginStop("tools10").AddItem(ControlId.Structural(k + "tools10"), GraphNodes.Button(
                    () => vm.CanLockpick10.Value.Blueprint.Name + " (" + vm.CanLockpick10.Value.Count + ") - " + vm.SecondChance.Value + "%",
                    () => vm.OnInteraction(LockpickType.Lockpick10)));
            // Force the lock open, when destructible.
            if (vm.CanDestroy.Value)
                b.BeginStop("destroy").AddItem(ControlId.Structural(k + "destroy"), GraphNodes.Button(
                    () => (string)t.Destroy, () => vm.OnInteraction(LockpickType.Destroy)));
        }
    }
}
