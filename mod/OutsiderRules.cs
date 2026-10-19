using System;

namespace WitchCtl
{
    /// <summary>
    /// The hub's two meta shops (<c>OutsiderShopUI</c>: "借阅图书" = card packs, "唤醒本源" = careers, partners, items) as plain logic (tested in tests/modlogic).
    /// An item has <c>BuyCount</c> levels; each costs truth crystals or time (<c>PriceType</c>), taken for good. A one-off item (BuyCount 1) that
    /// <c>CanClose</c> can be switched on and off afterwards. Mirrors <c>OutsideShopItem.BuyCheck / TryBuy / UseCheck</c> (decompile, 2026-10-14);
    /// the game's own <c>TryBuy</c> returns silently when the currency is short, so the mod says why instead.
    /// </summary>
    internal static class OutsiderRules
    {
        internal const string LeaveId = "outsider_leave", BuyPrefix = "outsider_buy_", TogglePrefix = "outsider_toggle_";

        internal static string Buy(int index) => BuyPrefix + index;
        internal static string Toggle(int index) => TogglePrefix + index;

        /// <summary>What the Buy button does for an item: "buy" while levels are left, "toggle" for a bought one-off that can be switched, else "owned" (nothing to do).</summary>
        internal static string Mode(int bought, int buyCount, bool canClose) =>
            bought < buyCount ? "buy" : buyCount == 1 && canClose ? "toggle" : "owned";

        /// <summary>Why a purchase cannot work, or null. Truth and time are separate purses: one never pays the other's price.</summary>
        internal static string BuyRefusal(string priceType, int price, int truth, int time)
        {
            switch (priceType)
            {
                case "Truth": return truth < price ? "costs " + price + " truth crystals (have " + truth + ")" : null;
                case "Time": return time < price ? "costs " + price + " time (have " + time + ")" : null;
                default: return "unknown currency '" + priceType + "'";
            }
        }

        /// <summary>kind = buy | toggle (arg = index in `outsiderShop.items`) | leave.</summary>
        internal static bool TryParse(string id, out string kind, out int index)
        {
            kind = null; index = -1;
            if (id == LeaveId) { kind = "leave"; return true; }
            if (id == null) return false;
            string rest;
            if (id.StartsWith(BuyPrefix, StringComparison.Ordinal)) { kind = "buy"; rest = id.Substring(BuyPrefix.Length); }
            else if (id.StartsWith(TogglePrefix, StringComparison.Ordinal)) { kind = "toggle"; rest = id.Substring(TogglePrefix.Length); }
            else return false;
            if (int.TryParse(rest, out index) && index >= 0) return true;
            kind = null; index = -1;
            return false;
        }
    }
}
