namespace WitchCtl
{
    /// <summary>
    /// When buying in a shop / destiny tree / enchant window cannot work, as plain logic (tested in tests/modlogic). The game's own checks
    /// are silent when the money is short and answer a full backpack with a modal; the mod says why instead. Mirrors
    /// <c>ShopItem.TryBuy / EnchTryBuy</c> and <c>ShopUI.Flushed</c> (decompile, 2026-10-14). A reason, or null when it will work.
    /// </summary>
    internal static class ShopRules
    {
        internal static string Buy(string kind, int price, int gold, int backpack, int backpackMax, int enchantable)
        {
            switch (kind)
            {
                case "Card": case "Relic": case "Bless": case "RandomBlessing": case "EnchTag": break;
                default: return "unknown item kind '" + kind + "'";
            }
            if (gold < price) return "costs " + price + " gold (have " + gold + ")";
            if (kind == "Card" && backpack >= backpackMax) return "the backpack is full (" + backpack + "/" + backpackMax + "): put a card into the deck first (deck_equip) or sell one";
            if (kind == "EnchTag" && enchantable <= 0) return "every card already has an enchantment";
            return null;
        }

        /// <summary>The enchant item has its own entry point: <c>TryBuy</c> would charge once on opening and again when the card is picked.</summary>
        internal static bool UsesEnchantPath(string kind) => kind == "EnchTag";

        internal static int RefreshCost(int count, int step) => count * step + 20;

        internal static string Refresh(int count, int max, int step, int gold)
        {
            if (count >= max) return "no refreshes left (" + count + "/" + max + ")";
            var cost = RefreshCost(count, step);
            return gold < cost ? "a refresh costs " + cost + " gold (have " + gold + ")" : null;
        }
    }
}
