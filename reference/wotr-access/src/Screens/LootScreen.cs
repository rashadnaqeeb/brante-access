using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings; // UIStrings (game-localized Leave/remove-loot labels)
using Kingmaker.UI.MVVM._VM.Loot;
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The loot window (<see cref="LootVM"/>) shown when you loot a container or corpse — driven, like the
    /// dialogue screen, off a reactive on the in-game static HUD (<c>LootContextVM.LootVM</c>), not a
    /// RootUIContext service window. Each loot source (a corpse/chest = <see cref="LootObjectVM"/>) is its
    /// own Tab-stop list of items; arrows move within a source, Tab moves between sources. Enter takes an
    /// item — immediate mode drops the emptied slot from the next render, so focus slides to the nearest
    /// remaining item and announces it. "Take all" / Skin / the zone-exit controls are their own stops;
    /// Escape closes. The party-stash side (moving items the other way, chest deposits) is deferred.
    /// </summary>
    public sealed class LootScreen : Screen
    {
        public override string Key => "ctx.loot";
        public override string ScreenName => Loc.T("screen.loot");
        public override int Layer => 15; // over the in-game context + service windows, alongside dialogue

        private static LootVM Vm()
        {
            var rc = Game.Instance != null ? Game.Instance.RootUiContext : null;
            return rc?.InGameVM?.StaticPartVM?.LootContextVM?.LootVM?.Value;
        }

        public override bool IsActive() => Vm() != null;


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "loot:" + vm.GetHashCode() + ":"; // a new LootVM = a fresh window = fresh keys

            // One Tab-stop list per loot source; only slots that still hold an item emit, so taking an
            // item slides focus to the nearest survivor (and an emptied source drops out entirely).
            if (vm.ContextLoot != null)
            {
                int s = 0;
                foreach (var obj in vm.ContextLoot)
                {
                    if (obj?.SlotsGroup == null) { s++; continue; }
                    bool any = false;
                    foreach (var slot in obj.SlotsGroup.VisibleCollection)
                        if (slot != null && slot.HasItem) { any = true; break; }
                    if (!any) { s++; continue; }

                    b.BeginStop(k + "src:" + s);
                    b.PushContext(string.IsNullOrEmpty(obj.DisplayName) ? Loc.T("loot.container") : obj.DisplayName, "list");
                    int i = 0;
                    foreach (var slot in obj.SlotsGroup.VisibleCollection)
                    {
                        if (slot != null && slot.HasItem)
                            b.AddItem(ControlId.Referenced(slot, k + "src:" + s + ":item:" + i), GraphNodes.LootItem(vm, slot));
                        i++;
                    }
                    b.PopContext();
                    s++;
                }
            }

            // Take all — gated like the game's Collect All button (CanCollectItems): trophy items needing
            // a skin roll don't count, so a corpse holding only those reads it as unavailable instead of
            // silently doing nothing.
            b.BeginStop(k + "takeall").AddItem(ControlId.Structural(k + "takeall"),
                GraphNodes.Button(() => Loc.T("loot.take_all"), vm.CollectAll,
                    () => vm.LootCollector != null ? vm.LootCollector.CanCollectItems : vm.HasItemsToLoot,
                    sound: null)); // CollectAll plays the game's own loot sounds

            // Skin — present while the window holds trophy items (HasItemsToSkinning), mirroring the
            // game's skin button: one roll over every trophy in the window (the VM plays the LootSkinning
            // sound), then announce the game's own success/half/failure line (the real view raises it as
            // a warning notification). Successfully skinned items become takeable.
            if (vm.LootCollector != null && vm.LootCollector.HasItemsToSkinning)
            {
                var collector = vm.LootCollector;
                b.BeginStop(k + "skin").AddItem(ControlId.Structural(k + "skin"),
                    GraphNodes.Button(
                        () => TextUtil.StripRichText(UIStrings.Instance.LootWindow.Skinning),
                        () =>
                        {
                            collector.UseSkinning(out int max, out int current);
                            var line = current == max ? UIStrings.Instance.CommonTexts.SkinningSuccess
                                : current != 0 ? UIStrings.Instance.CommonTexts.SkinningHalfSuccess
                                : UIStrings.Instance.CommonTexts.SkinningFailed;
                            Tts.Speak(TextUtil.StripRichText(line), interrupt: true);
                        },
                        () => collector.HasItemsToSkinning,
                        sound: null)); // the VM plays LootSkinning itself
            }

            // Zone-exit variant (leaving the area with uncollected loot): mirror the game's extra
            // controls — a Leave button (proceed WITHOUT collecting; in this mode Take all also leaves,
            // both run the deferred transition) and the remove-uncollected-loot toggle. Escape stays the
            // cancel (vm.Close keeps you in the area), exactly like the game's X / EscManager.
            if (vm.Mode == LootContextVM.LootWindowMode.ZoneExit)
            {
                b.BeginStop(k + "leave").AddItem(ControlId.Structural(k + "leave"),
                    GraphNodes.Button(() => TextUtil.StripRichText(UIStrings.Instance.LootWindow.LeaveZone), vm.LeaveZone));
                b.BeginStop(k + "removeloot").AddItem(ControlId.Structural(k + "removeloot"),
                    GraphNodes.Toggle(
                        () => TextUtil.StripRichText(UIStrings.Instance.LootWindow.RemoveUncollectedLootHint),
                        () => vm.RemoveUncollectedLoot.Value,
                        vm.SwitchRemoveUncollectedLoot));
            }
        }

        // Escape closes the window (the game's own EscManager is muted while focus mode owns the keyboard).
        public override IEnumerable<ElementAction> GetActions()
        {
            var vm = Vm();
            if (vm != null)
                yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => vm.Close());
        }
    }
}
