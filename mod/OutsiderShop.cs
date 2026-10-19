using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Witch.UI.Automation;
using Witch.UI.Window;
using UIManager = Witch.UI.UIManager;

namespace WitchCtl
{
    /// <summary>
    /// The hub's two meta shops, one window (<c>OutsiderShopUI</c>, opened by the library's "借阅图书" and the tower's "唤醒本源" buttons, see
    /// <see cref="Hub"/>), phase <c>outsider_shop</c>: card packs, careers, partners and run-modifying items, paid with truth crystals or time
    /// that the achievements and finished runs earn. Buying is for good (the currency is gone, the item is unlocked), hence the named ids:
    /// <c>outsider_buy_N</c> (N = index in `outsiderShop.items`; calls the game's <c>OutsideShopItem.BuyCheck</c>, read back from the profile),
    /// <c>outsider_toggle_N</c> (switch a bought one-off item on or off) and <c>outsider_leave</c>. A buy is only offered when the purse covers
    /// it: the game's own <c>TryBuy</c> returns without a word when it does not. Rules: <see cref="OutsiderRules"/>.
    /// </summary>
    internal static class OutsiderShop
    {
        private static float _leftAt = -100f;

        internal static bool Owns(string actionId) => actionId != null && actionId.StartsWith("outsider_", StringComparison.Ordinal);

        private static OutsiderShopUI Window()
        {
            var window = UIManager.Instance?.GetUI<OutsiderShopUI>("OutsiderShopUI");
            return window != null && window.gameObject.activeInHierarchy ? window : null;
        }

        internal static bool IsOpen() => Window() != null;

        private sealed class Entry
        {
            public int Index;
            public OutsideShopItem Item;
            public string Id, Name, Text, Type, PriceType, Mode;
            public int Price, Bought, BuyCount;
            public bool CanClose, Used;
        }

        private static int BoughtLevels(string id) => Singleton<GameRuntimeData>.Instance.BuyedItems.TryGetValue(id, out var levels) ? levels : 0;
        private static int Truth => (int)Singleton<GameRuntimeData>.Instance.Truth;
        private static int Time_ => (int)Singleton<GameRuntimeData>.Instance.Time;

        /// <summary>The items of the tab showing (the window keeps the other tab's items too, switched off).</summary>
        private static List<Entry> Items(OutsiderShopUI window)
        {
            var profile = Singleton<GameRuntimeData>.Instance;
            var found = new List<Entry>();
            if (window.ItemParent == null) return found;
            foreach (Transform child in window.ItemParent)
            {
                var item = child.GetComponent<OutsideShopItem>();
                if (item == null || !child.gameObject.activeSelf || item.dataConfig?.data == null || item.theData == null) continue;
                var data = item.dataConfig.data;
                var buyCount = int.TryParse(data.GetValueOrDefault("BuyCount", "1"), out var n) ? n : 1;
                var bought = BoughtLevels(item.itemId);
                var canClose = data.GetValueOrDefault("CanClose", "") == "TRUE";
                found.Add(new Entry
                {
                    Index = found.Count, Item = item, Id = item.itemId,
                    Name = Rich.Strip(item.itemName), Text = Rich.Strip(item.itemDescription),
                    Type = data.GetValueOrDefault("Type", "?"), PriceType = data.GetValueOrDefault("PriceType", "?"),
                    Price = item.itemPrice, Bought = bought, BuyCount = buyCount, CanClose = canClose,
                    Used = profile.UsedBuyedItems.Contains(item.itemId),
                    Mode = OutsiderRules.Mode(bought, buyCount, canClose),
                });
            }
            return found;
        }

        private static string Refusal(Entry e) => e.Mode == "buy" ? OutsiderRules.BuyRefusal(e.PriceType, e.Price, Truth, Time_) : null;

        internal static Screens.Screen Detect()
        {
            var window = Window();
            if (window == null || Popups.Detect() != null) return null;   // the item-reveal popup after a purchase sits on top of the shop
            var screen = new Screens.Screen { Phase = "outsider_shop" };
            try
            {
                var items = Items(window);
                foreach (var e in items)
                {
                    if (e.Mode == "buy" && Refusal(e) == null)
                        screen.Actions.Add(new RuntimeLegalAction
                        {
                            Id = OutsiderRules.Buy(e.Index), Kind = "outsider_buy", Label = e.Name,
                            Description = "Buy " + e.Name + " for " + e.Price + " " + e.PriceType + " (level " + (e.Bought + 1) + "/" + e.BuyCount + "), for good. " + e.Text,
                            Parameters = new Dictionary<string, object> { ["price"] = e.Price, ["currency"] = e.PriceType, ["type"] = e.Type },
                        });
                    else if (e.Mode == "toggle")
                        screen.Actions.Add(new RuntimeLegalAction
                        {
                            Id = OutsiderRules.Toggle(e.Index), Kind = "outsider_toggle", Label = e.Name,
                            Description = (e.Used ? "Switch off " : "Switch on ") + e.Name + " (already bought; reversible).",
                        });
                }
                if (window.transform.Find("ExitButton") != null)
                    screen.Actions.Add(new RuntimeLegalAction { Id = OutsiderRules.LeaveId, Kind = OutsiderRules.LeaveId, Label = "离开", Description = "Close the shop." });
                screen.Detail["outsiderShop"] = new
                {
                    tab = items.Count > 0 && items.All(i => i.Type == "card" || i.Type == "cardpack") ? "cards" : "origins",
                    truth = Truth, time = Time_,
                    items = items.Select(e => new
                    {
                        index = e.Index, id = e.Id, name = e.Name, text = e.Text, type = e.Type, currency = e.PriceType, price = e.Price,
                        bought = e.Bought, levels = e.BuyCount, mode = e.Mode, used = e.Used, affordable = e.Mode != "buy" || Refusal(e) == null,
                        note = Refusal(e),
                    }).ToList(),
                };
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[WitchCtl] outsider shop scan failed: " + ex);
                screen.Actions.Clear();
                if (window.transform.Find("ExitButton") != null)
                    screen.Actions.Add(new RuntimeLegalAction { Id = OutsiderRules.LeaveId, Kind = OutsiderRules.LeaveId, Label = "离开", Description = "Close the shop." });
                screen.Detail["outsiderShopError"] = ex.GetType().Name + ": " + ex.Message;
            }
            return screen;
        }

        internal static RuntimePerformActionResult Perform(string actionId)
        {
            string refusal = null, message = null;
            var window = Window();
            if (window == null) refusal = "the meta shop is not open";
            else if (!OutsiderRules.TryParse(actionId, out var kind, out var index)) refusal = "unknown shop action";
            else if (kind == "leave")
            {
                var exit = window.transform.Find("ExitButton");
                if (exit == null) refusal = "there is no exit button";
                else if (UnityEngine.Time.realtimeSinceStartup - _leftAt < 2f) refusal = "already leaving; wait for the window to close";
                else { _leftAt = UnityEngine.Time.realtimeSinceStartup; UiLite.ClickObject(exit.gameObject); message = "Closed the meta shop."; }
            }
            else
            {
                var entry = Items(window).FirstOrDefault(e => e.Index == index);
                if (entry == null) refusal = "no such item in this tab";
                else if (kind == "buy")
                {
                    if (entry.Mode != "buy") refusal = entry.Name + " cannot be bought any more (" + entry.Bought + "/" + entry.BuyCount + ")";
                    else if ((refusal = Refusal(entry)) == null)
                    {
                        int truth = Truth, time = Time_;
                        window.SetCurrentItem(entry.Item);
                        entry.Item.BuyCheck();
                        var bought = BoughtLevels(entry.Id);
                        if (bought != entry.Bought + 1) refusal = "the game did not take the purchase (level " + entry.Bought + "→" + bought + ", " + entry.PriceType + " unchanged?)";
                        else message = "Bought " + entry.Name + " (level " + bought + "/" + entry.BuyCount + "): truth " + truth + "→" + Truth + ", time " + time + "→" + Time_ + ".";
                    }
                }
                else   // toggle
                {
                    if (entry.Mode != "toggle") refusal = entry.Name + (entry.Mode == "buy" ? " is not bought yet" : " cannot be switched off");
                    else
                    {
                        window.SetCurrentItem(entry.Item);
                        entry.Item.BuyCheck();   // a bought one-off: BuyCheck takes the UseCheck path, which flips UsedBuyedItems
                        var used = Singleton<GameRuntimeData>.Instance.UsedBuyedItems.Contains(entry.Id);
                        if (used == entry.Used) refusal = "the game did not switch " + entry.Name + " (still " + (used ? "on" : "off") + ")";
                        else message = entry.Name + " is now " + (used ? "on" : "off") + ".";
                    }
                }
            }
            if (refusal != null) return new RuntimePerformActionResult { Success = false, ActionId = actionId, Message = "refused: " + refusal };
            UnityEngine.Debug.Log("[WitchCtl] " + actionId + ": " + message);
            return new RuntimePerformActionResult { Success = true, ActionId = actionId, Message = message };
        }
    }
}
