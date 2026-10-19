namespace WitchCtl
{
    /// <summary>
    /// Who a targeted card may hit, as plain logic (tested in tests/modlogic). The card's Vars say it: <c>CanSelf == "True"</c> lets it be aimed at
    /// the player, <c>CanEnemy == "False"</c> forbids enemies (<c>AttackCardItem</c> ray check, decompile 2026-10-14). The mod used to aim every
    /// targeted card at an enemy, so a heal or a buff that targets "a unit" could only ever be wasted on one.
    /// </summary>
    internal static class TargetRules
    {
        internal static bool CanSelf(string var) => var == "True";

        internal static bool CanEnemy(string var) => var != "False";

        /// <summary>A refusal, or null with <paramref name="kind"/> = "enemy" | "self".</summary>
        internal static string Choose(bool canSelf, bool canEnemy, bool wantSelf, bool wantEnemy, out string kind)
        {
            kind = null;
            if (wantSelf && wantEnemy) return "pick one target: an enemy index or yourself, not both";
            if (wantSelf)
            {
                if (!canSelf) return "this card cannot be aimed at yourself";
                kind = "self"; return null;
            }
            if (wantEnemy)
            {
                if (!canEnemy) return "this card cannot target enemies (use --self)";
                kind = "enemy"; return null;
            }
            if (canEnemy) { kind = "enemy"; return null; }
            if (canSelf) { kind = "self"; return null; }
            return "this card has no legal target kind";
        }
    }
}
