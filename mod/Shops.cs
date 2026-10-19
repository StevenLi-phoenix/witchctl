using System;
using System.Collections.Generic;
using System.Linq;
using Michsky.MUIP;
using TMPro;
using UnityEngine;
using Witch.UI.Automation;
using Witch.UI.Window;
using UIManager = Witch.UI.UIManager;

namespace WitchCtl
{
    /// <summary>
    /// The three buying windows, one code path: the shop (<c>ShopUI</c>: relics, cards, blessings, refresh), the destiny tree
    /// (<c>DestinyTreeUI</c>: blessings, divination) and the blood-line engraving (<c>CardEnchUI</c>: enchantments). The last two derive from
    /// <c>ShopUI</c> and fill the same <c>ShopTran</c> list with <c>ShopItem</c>s. Before this a shop was `unknown` and the only thing an agent
    /// could do was leave it, so all the gold it earned was never spent.
    /// Phase <c>shop</c> (`shop.window` says which one). Actions: <c>shop_buy_N</c> (N = index in `shop.items`), <c>shop_refresh</c>, <c>shop_leave</c>.
    /// Cards bought go to the backpack, not the deck: see <see cref="Deck"/> (`deck_open`, `deck_equip_N`). Selling is `deck_sell_N` there.
    /// Buying calls the game's own <c>ShopItem.TryBuy</c> (an enchantment: <c>EnchTryBuy</c>, which opens the card picker, see
    /// <see cref="DeckSelect"/>); the money is checked first (<see cref="ShopRules"/>) because the game returns silently when it is short.
    /// </summary>
    internal static class Shops
    {
        private const string BuyPrefix = "shop_buy_", RefreshId = "shop_refresh", LeaveId = "shop_leave";
        private static readonly string[] WindowNames = { "CardEnchUI", "DestinyTreeUI", "ShopUI" };   // derived windows first: they are ShopUIs too

        internal static bool Owns(string actionId) => actionId != null && actionId.StartsWith("shop_", StringComparison.Ordinal);

        private static bool Active(UnityEngine.Component c) => c != null && c.gameObject.activeInHierarchy;

        internal static ShopUI Window(out string name)
        {
            name = null;
            var ui = UIManager.Instance;
            if (ui == null) return null;
            foreach (var n in WindowNames)
            {
                var w = n == "CardEnchUI" ? ui.GetUI<CardEnchUI>(n) : n == "DestinyTreeUI" ? ui.GetUI<DestinyTreeUI>(n) : ui.GetUI<ShopUI>(n);
                if (Active(w)) { name = n; return w; }
            }
            return null;
        }

        internal static bool IsOpen() => Window(out _) != null;

        private sealed class Entry
        {
            public int Index;
            public ShopItem Item;
            public string Kind, Name, Text, Rarity;
            public int Price;
            /// <summary>Buyable, but not worth it: a relic bought with all six slots taken goes to the warehouse and does nothing (the game asks first).</summary>
            public string Warn;
        }

        internal const int RelicSlots = 6;   // ShopUI shows "6 - relicList.Count" empty slots; ShopItem.TryBuy asks once past 6

        private static int Gold => (int)RoleTable.Instance.Money;

        private static int Price(ShopItem item, ShopUI window) =>
            item.ItemType == "RandomBlessing" && window is DestinyTreeUI tree ? tree.Cost : item.itemPrice;

        private static List<Entry> Items(ShopUI window)
        {
            var found = new List<Entry>();
            if (window.ShopTran == null) return found;
            foreach (var item in window.ShopTran.GetComponentsInChildren<ShopItem>(false))
            {
                var config = item.dataConfig;
                if (item.ItemType != "RandomBlessing" && config?.data == null) continue;   // the pooled template
                found.Add(new Entry
                {
                    Index = found.Count,
                    Item = item,
                    Kind = item.ItemType,
                    Name = item.ItemType == "RandomBlessing" ? "RandomBlessing" : Rich.NameOf(config) ?? (config.data.TryGetValue("Id", out var id) ? id : "?"),
                    Text = item.ItemType == "RandomBlessing" ? "Buy random blessings (divination)." : Rich.Describe(config),
                    Rarity = config != null && config.data.TryGetValue("Rarity", out var r) ? r : "",
                    Price = Price(item, window),
                    Warn = item.ItemType == "Relic" && RoleTable.Instance.relicList.Count >= RelicSlots
                        ? "all " + RelicSlots + " relic slots are taken: it would go to the warehouse and do nothing" : null,
                });
            }
            return found;
        }

        private static int Enchantable(RoleTable role) =>
            role.cardList.Concat(role.UnCardList).Count(c => !role.enchasedDict.ContainsKey(c.InstanceID));

        private static string BuyRefusal(Entry e, RoleTable role) =>
            ShopRules.Buy(e.Kind, e.Price, Gold, role.UnCardList.Count, role.MaxAlCardCount, e.Kind == "EnchTag" ? Enchantable(role) : 0);

        private static Transform RefreshButton(ShopUI window)
        {
            var t = window.transform.Find("Content/Refresh");
            return Active(t) ? t : null;
        }

        private static Transform ExitButton(ShopUI window)
        {
            var t = window.transform.Find("ExitButton");
            return Active(t) ? t : null;
        }

        internal static Screens.Screen Detect()
        {
            var window = Window(out var name);
            var role = RoleTable.Instance;
            if (window == null || role == null) return null;
            var screen = new Screens.Screen { Phase = "shop" };
            try
            {
                var items = Items(window);
                foreach (var e in items)
                {
                    var why = BuyRefusal(e, role);
                    if (why != null) continue;
                    screen.Actions.Add(new RuntimeLegalAction
                    {
                        Id = BuyPrefix + e.Index, Kind = "shop_buy", Label = e.Name,
                        Description = "Buy for " + e.Price + " gold. " + (e.Kind == "Card" ? "Goes to the backpack (not the deck). " : "") + e.Text,
                        Parameters = new Dictionary<string, object> { ["kind"] = e.Kind, ["price"] = e.Price, ["warn"] = e.Warn },
                    });
                }
                object refresh = null;
                if (RefreshButton(window) != null)
                {
                    var cost = ShopRules.RefreshCost(window.flushedCount, window.flushmoneychange);
                    var why = ShopRules.Refresh(window.flushedCount, window.maxFlushedCount, window.flushmoneychange, Gold);
                    refresh = new { cost, used = window.flushedCount, max = window.maxFlushedCount, available = why == null, note = why };
                    if (why == null)
                        screen.Actions.Add(new RuntimeLegalAction { Id = RefreshId, Kind = RefreshId, Label = "刷新" + cost, Description = "Re-roll the shop's items for " + cost + " gold." });
                }
                if (ExitButton(window) != null)
                    screen.Actions.Add(new RuntimeLegalAction { Id = LeaveId, Kind = LeaveId, Label = "离开", Description = "Leave the " + name + "." });
                screen.Detail["shop"] = new
                {
                    window = name,
                    gold = Gold,
                    backpack = role.UnCardList.Count, backpackMax = role.MaxAlCardCount,
                    relics = role.relicList.Count, relicSlots = RelicSlots,
                    refresh,
                    items = items.Select(e => new { index = e.Index, kind = e.Kind, name = e.Name, price = e.Price, rarity = e.Rarity, text = e.Text, buyable = BuyRefusal(e, role) == null, note = BuyRefusal(e, role), warn = e.Warn }).ToList(),
                };
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[WitchCtl] shop scan failed: " + ex);
                screen.Detail["shopError"] = ex.GetType().Name + ": " + ex.Message;
                screen.NeedsUi = true;
            }
            return screen;
        }

        // ------------------------------------------------------------------ doing things

        private static float _leftAt = -100f;

        internal static RuntimePerformActionResult Perform(string actionId)
        {
            string refusal = null, message = null;
            var window = Window(out var name);
            var role = RoleTable.Instance;
            if (window == null || role == null) refusal = "no shop window is open";
            else if (actionId == LeaveId)
            {
                var exit = ExitButton(window);
                if (exit == null) refusal = "there is no exit button";
                else if (Time.realtimeSinceStartup - _leftAt < 5f) refusal = "already leaving; wait for the window to close (leaving advances the map once)";
                else { _leftAt = Time.realtimeSinceStartup; UiLite.ClickObject(exit.gameObject); message = "Left the " + name + "."; }
            }
            else if (actionId == RefreshId)
            {
                var why = RefreshButton(window) == null ? "this window has no refresh" : ShopRules.Refresh(window.flushedCount, window.maxFlushedCount, window.flushmoneychange, Gold);
                if (why != null) refusal = why;
                else
                {
                    int before = window.flushedCount, gold = Gold;
                    window.Flushed();
                    if (window.flushedCount != before + 1) refusal = "the game did not refresh (count " + before + "→" + window.flushedCount + ")";
                    else message = "Refreshed (" + before + "→" + window.flushedCount + "), gold " + gold + "→" + Gold + ".";
                }
            }
            else if (actionId.StartsWith(BuyPrefix, StringComparison.Ordinal))
            {
                var entry = int.TryParse(actionId.Substring(BuyPrefix.Length), out var index) ? Items(window).FirstOrDefault(e => e.Index == index) : null;
                if (entry == null) refusal = "no such item in the " + name;
                else if ((refusal = BuyRefusal(entry, role)) == null) message = Buy(entry, role, ref refusal);
            }
            else refusal = "unknown shop action";

            if (refusal != null) return new RuntimePerformActionResult { Success = false, ActionId = actionId, Message = "refused: " + refusal };
            UnityEngine.Debug.Log("[WitchCtl] " + actionId + ": " + message);
            return new RuntimePerformActionResult { Success = true, ActionId = actionId, Message = message };
        }

        private static string Buy(Entry e, RoleTable role, ref string refusal)
        {
            int gold = Gold, cards = role.UnCardList.Count, relics = role.WithoutArmedRelicList?.Count ?? 0, blessings = role.blessingConfigs?.Count ?? 0;
            if (ShopRules.UsesEnchantPath(e.Kind)) e.Item.EnchTryBuy(); else e.Item.TryBuy();
            int goldAfter = Gold;
            if (e.Kind == "EnchTag")   // charged later, when the card is picked in the DeckUI picker
                return DeckSelect.IsOpen() ? "Chose the enchantment " + e.Name + "; now pick the card to enchant (phase deck_select)." : Fail(ref refusal, "the game did not open the card picker");
            if (Popups.DetectModal() != null)   // e.g. "the relic slots are full, it goes to the warehouse: buy?"
                return "Asked to buy " + e.Name + " but the game opened a window; answer it (phase modal).";
            if (goldAfter != gold - e.Price && e.Kind != "RandomBlessing")
                return Fail(ref refusal, "the game did not complete the purchase of " + e.Name + " (gold " + gold + "→" + goldAfter + ")");
            return "Bought " + e.Name + " for " + (gold - goldAfter) + " gold (gold " + gold + "→" + goldAfter + ")"
                   + (e.Kind == "Card" ? "; backpack " + cards + "→" + role.UnCardList.Count : "")
                   + (e.Kind == "Relic" ? "; spare relics " + relics + "→" + (role.WithoutArmedRelicList?.Count ?? 0) : "")
                   + (e.Kind == "Bless" ? "; blessings " + blessings + "→" + (role.blessingConfigs?.Count ?? 0) : "")
                   + (e.Kind == "RandomBlessing" ? "; choose one in the blessing picker (phase blessing_choice)" : "") + ".";
        }

        private static string Fail(ref string refusal, string why) { refusal = why; return null; }
    }
}
