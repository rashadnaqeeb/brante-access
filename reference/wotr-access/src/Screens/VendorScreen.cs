using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings;          // UIStrings (vendor button labels)
using Kingmaker.UI.MVVM._VM.Slots;                 // ItemSlotVM, SlotsGroupVM
using Kingmaker.UI.MVVM._VM.Vendor;                // VendorVM
using WrathAccess.UI;
using WrathAccess.UI.Graph;

namespace WrathAccess.Screens
{
    /// <summary>
    /// The vendor / trade window (<see cref="VendorVM"/>, an in-game static-part reactive). Graph-native
    /// on <see cref="GraphSheet"/>: four browsable table stops — Your inventory (led by your gold), Store,
    /// Buy cart, Sell cart (each cart ending in its "return all" line) — then Bulk sell, Deal, and Close
    /// stops. The trade engine is <c>VendorLogic</c>: an item moves with one call,
    /// <see cref="ItemSlotVM.VendorTryMove"/>, which routes by the slot's own collection (stock→buy,
    /// inventory→sell, cart→return), so one <see cref="GraphNodes.VendorItem"/> serves every region
    /// (Enter moves one, Backspace moves all / shows info, Space = the item's tooltip). Rows key by
    /// position, so after a move focus stays at the same row — the next item — and the differ reads it
    /// (the old signature/capture/restore machinery is deleted). Each item row carries Type/Qty/Price as
    /// parts; Right steps the columns with their headers. Escape closes. Selling equipped gear (the
    /// per-character doll) and the redundant item-info toggle are a later slice.
    /// </summary>
    public sealed class VendorScreen : Screen
    {
        public VendorScreen() { Wrap = true; }

        public override string Key => "ctx.vendor";
        public override string ScreenName => Vm()?.VendorName?.Value ?? Loc.T("screen.vendor");
        public override int Layer => 15; // above contexts + service windows, same family as loot/dialogue
        public override bool IsActive() => Vm() != null;

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"),
                _ => Vm()?.Close());
        }

        private static VendorVM Vm()
            => Game.Instance?.RootUiContext?.InGameVM?.StaticPartVM?.VendorVM?.Value;

        private static Kingmaker.Items.VendorLogic Logic => Game.Instance?.Vendor;


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "vendor:" + vm.GetHashCode() + ":";

            // Your inventory — your gold reads first (a lead line), then the sellable items.
            EmitTable(b, k + "inv", vm.StashVM?.ItemSlotsGroup, Loc.T("vendor.your_inventory"),
                GraphNodes.VendorSide.Inventory, buy: false,
                lead: GraphNodes.Text(() => Loc.T("vendor.gold", new { value = vm.StashVM?.Money?.Value ?? 0 })),
                trailer: null);
            EmitTable(b, k + "store", vm.VendorSlotsGroup, Loc.T("vendor.store"),
                GraphNodes.VendorSide.Stock, buy: true, lead: null, trailer: null);
            EmitTable(b, k + "buycart", vm.VendorExchangePart, Loc.T("vendor.buy_cart"),
                GraphNodes.VendorSide.BuyCart, buy: true, lead: null,
                trailer: GraphNodes.Button(() => Strip(UIStrings.Instance.Vendor.ReturnBuy),
                    () => vm.ReturnBuy(), () => vm.CanVendorExchangeReturn.Value));
            EmitTable(b, k + "sellcart", vm.PlayerExchangePart, Loc.T("vendor.sell_cart"),
                GraphNodes.VendorSide.SellCart, buy: false, lead: null,
                trailer: GraphNodes.Button(() => Strip(UIStrings.Instance.Vendor.ReturnSell),
                    () => vm.ReturnSale(), () => vm.CanPlayerExchangeReturn.Value));

            // Bulk sell: the three category toggles that decide what Mass sale dumps (Masterwork /
            // Non-magical / Gems & animal parts, read live off the always-present OptionsVM), then the
            // Mass sale button.
            b.BeginStop("bulk").PushContext(Loc.T("vendor.bulk_sell"), "list");
            if (vm.OptionsVM?.ItemVms != null)
            {
                int oi = 0;
                foreach (var opt in vm.OptionsVM.ItemVms)
                {
                    if (opt == null) continue;
                    var o = opt;
                    b.AddItem(ControlId.Referenced(o, k + "bulk:" + oi),
                        GraphNodes.Toggle(() => o.Title.Value, () => o.State.Value, () => o.SwitchOption()));
                    oi++;
                }
            }
            b.AddItem(ControlId.Structural(k + "masssale"),
                GraphNodes.Button(() => Strip(UIStrings.Instance.Vendor.MassSell), () => vm.MassSale()));
            b.PopContext();

            // Deal: the running deal total (you receive / you pay) then the Deal button (enabled only
            // when the deal is affordable).
            b.BeginStop("deal").PushContext(Loc.T("vendor.deal_section"), "list");
            b.AddItem(ControlId.Structural(k + "dealtotal"), GraphNodes.Text(() => DealSummary(vm)));
            b.AddItem(ControlId.Structural(k + "deal"),
                GraphNodes.Button(() => Strip(UIStrings.Instance.Vendor.Deal),
                    () => { vm.Deal(); AnnounceDealt(vm); },
                    () => vm.IsPossibleDeal.Value));
            b.PopContext();

            b.BeginStop("close").AddItem(ControlId.Structural(k + "close"),
                GraphNodes.Button(() => Loc.T("vendor.close"), () => vm.Close()));
        }

        // One trade region as its own labelled Tab-stop: an optional lead line (your gold), a
        // Type/Qty/Price table (Price = buy or sell per side), and an optional trailing "return all".
        // Rows carry their metadata as parts on the item cell; empty regions read one "empty" line.
        private static void EmitTable(GraphBuilder b, string key, SlotsGroupVM<ItemSlotVM> group,
            string label, GraphNodes.VendorSide side, bool buy, NodeVtable lead, NodeVtable trailer)
        {
            b.BeginStop(key);
            var sheet = new GraphSheet(b, key + ":");
            var cols = new[] { Loc.T("col.type"), Loc.T("col.qty"), Loc.T(buy ? "vendor.col_buy" : "vendor.col_sell") };
            sheet.Region(label, cols);
            if (lead != null) sheet.Line(lead);

            bool any = false;
            if (group?.VisibleCollection != null)
                foreach (var slot in group.VisibleCollection)
                {
                    if (slot == null || !slot.HasItem) continue;
                    any = true;
                    var s = slot;
                    Func<string> type = () => s.TypeName.Value;
                    Func<string> qty = () => s.Count.Value > 1 ? s.Count.Value.ToString() : "1";
                    Func<string> price = () => Price(s, buy);
                    sheet.Row(
                        GraphNodes.VendorItem(s, side, new[]
                        {
                            new NodeAnnouncement(type),
                            new NodeAnnouncement(qty),
                            new NodeAnnouncement(price),
                        }),
                        s, // identity keys: selling out a stack lands on a NEW identity → announced
                        type, qty, price);
                }
            if (!any) sheet.Line(GraphNodes.Text(() => Loc.T("vendor.empty")));
            if (trailer != null) sheet.Line(trailer);
            sheet.Finish();
        }

        private static string Price(ItemSlotVM s, bool buy)
        {
            var item = s.Item.Value;
            if (item == null) return "";
            var v = Logic;
            long p = v == null ? s.Cost.Value : (buy ? v.GetItemBuyPrice(item) : v.GetItemSellPrice(item));
            return p.ToString();
        }

        // DealPrice is the net: positive = you receive, negative = you pay.
        private static string DealSummary(VendorVM vm)
        {
            long d = vm.DealPrice.Value;
            if (d > 0) return Loc.T("vendor.deal_receive", new { value = d });
            if (d < 0) return Loc.T("vendor.deal_pay", new { value = -d });
            return Loc.T("vendor.deal_none");
        }

        private static void AnnounceDealt(VendorVM vm)
            => Tts.Speak(Loc.T("vendor.dealt", new { value = vm.StashVM?.Money?.Value ?? 0 }), interrupt: false);

        private static string Strip(Kingmaker.Localization.LocalizedString s)
            => TextUtil.StripRichText((string)s);
    }
}
