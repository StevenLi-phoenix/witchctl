namespace WitchCtl
{
    /// <summary>
    /// When the game refuses to move, sell or destroy a card, as plain logic (no Unity types, tested in tests/modlogic). The game answers
    /// these with a modal ("limit reached") or silently, and a modal in the way blocks the next action: the mod checks first and says why.
    /// Mirrors <c>ShowCard.MoveItem</c>, <c>SellItem</c> and <c>DecomposeItem</c> (decompile, 2026-10-14). Each returns a reason or null.
    /// </summary>
    internal static class DeckRules
    {
        internal const string PickPrefix = "deck_pick_", PickCancelId = "deck_select_cancel";

        /// <summary>
        /// Ids of the card picker (<c>DeckSelect</c>), which share the <c>deck_</c> prefix with the deck window's (<c>Deck</c>). The deck window
        /// must not claim them: it did once (seen live, a career skill opened a picker in a fight and every <c>deck_pick_N</c> was answered
        /// "the deck window is not open").
        /// </summary>
        internal static bool IsPickerAction(string id) =>
            id != null && (id.StartsWith(PickPrefix, System.StringComparison.Ordinal) || id == PickCancelId);

        /// <summary>Backpack → deck: <paramref name="top"/> is <c>CardTopCount</c>.</summary>
        internal static string Equip(int deck, int top) =>
            deck < top ? null : "the deck is full (" + deck + "/" + top + ")";

        /// <summary>Deck → backpack: the deck keeps at least <c>CardBottomCount</c> cards, the backpack holds <c>MaxAlCardCount</c>.</summary>
        internal static string Unequip(int deck, int bottom, int spare, int spareMax)
        {
            if (spare >= spareMax) return "the backpack is full (" + spare + "/" + spareMax + ")";
            if (deck <= bottom) return "the deck is at its minimum size (" + deck + ", minimum " + bottom + ")";
            return null;
        }

        private static string Removable(bool eternal, bool curse, int wisdomNeeded, int wisdom, bool equipped, int deck, int bottom)
        {
            if (eternal) return "this card cannot be removed (Eternal)";
            if (equipped && deck <= bottom) return "the deck is at its minimum size (" + deck + ", minimum " + bottom + ")";
            if (curse && wisdom < wisdomNeeded) return "removing a curse needs wisdom " + wisdomNeeded + " (have " + wisdom + ")";
            return null;
        }

        /// <summary>Sell (a shop window is open): +20 × rarity gold. A curse needs wisdom 20.</summary>
        internal static string Sell(bool eternal, bool curse, int wisdom, bool equipped, int deck, int bottom) =>
            Removable(eternal, curse, 20, wisdom, equipped, deck, bottom);

        /// <summary>Destroy (no shop open): costs <paramref name="cost"/> gold; a curse needs wisdom of at least that much.</summary>
        internal static string Destroy(bool eternal, bool curse, int wisdom, bool equipped, int deck, int bottom, int money, int cost)
        {
            if (eternal) return "this card cannot be removed (Eternal)";
            if (money < cost) return "destroying costs " + cost + " gold (have " + money + ")";
            return Removable(false, curse, cost, wisdom, equipped, deck, bottom);
        }
    }
}
