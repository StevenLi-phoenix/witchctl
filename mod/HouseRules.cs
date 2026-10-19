namespace WitchCtl
{
    /// <summary>Which of the hub's page buttons (<c>HouseItem.HouseItemType</c>) an agent may press, as plain logic (tested in tests/modlogic).</summary>
    internal static class HouseRules
    {
        /// <summary>Null = allowed; otherwise why not. The names are the game's enum names.</summary>
        internal static string Refusal(string type)
        {
            switch (type)
            {
                case "OutsiderShop": case "cardShop": case "rollShop": case "task": case "announcement": return null;
                case "CardEditor": return null;   // opens the editor window; truth (50 x rarity) is only spent by its Confirm button
                case "ModManager": return "can switch mods off, this one included";
                case "TeachMode": return "deletes the tutorial save and starts the tutorial run";
                case "store": case "Dictionary": return "its window is not modelled (read-only, nothing to do there)";
                default: return "the game's own handler for it is empty";   // GameMenu, crystalBall, Achievement, shadow
            }
        }
    }
}
