using System;

namespace WitchCtl
{
    /// <summary>Action ids of career skills, as plain logic (tested in tests/modlogic). `/act` carries nothing but an id, so the target
    /// of an aimed skill is part of it: <c>skill_use_1</c> (no target needed), <c>skill_use_1_self</c>, <c>skill_use_1_enemy_0</c>.</summary>
    internal static class SkillRules
    {
        internal const string Prefix = "skill_use_";

        internal static string Id(int slot, string kind, int enemy) =>
            Prefix + slot + (kind == "self" ? "_self" : kind == "enemy" ? "_enemy_" + enemy : "");

        /// <summary>
        /// What can be seen right after <c>SkillItem.TrueUse</c>: the cooldown started, a picker opened, cards are being dealt or the hand grew.
        /// Null = none of them. A call that returned is not proof the skill did anything (seen live: the first use of 神选 changed nothing, the
        /// second started its cooldown and opened the card picker), so the caller says so instead of reporting a plain "used".
        /// </summary>
        internal static string Observed(int cooldownBefore, int cooldownAfter, int handBefore, int handAfter, int dealing, bool pickerOpen)
        {
            var seen = new System.Collections.Generic.List<string>();
            if (cooldownAfter > cooldownBefore) seen.Add("cooldown " + cooldownBefore + "→" + cooldownAfter);
            if (pickerOpen) seen.Add("a card picker opened");
            if (dealing > 0) seen.Add(dealing + " card(s) being dealt");
            if (handAfter != handBefore) seen.Add("hand " + handBefore + "→" + handAfter);
            return seen.Count == 0 ? null : string.Join(", ", seen);
        }

        /// <summary>slot (0-based index in the fight's skill list); kind "" | "self" | "enemy"; enemy index or -1.</summary>
        internal static bool TryParse(string id, out int slot, out string kind, out int enemy)
        {
            slot = -1; kind = ""; enemy = -1;
            if (id == null || !id.StartsWith(Prefix, StringComparison.Ordinal)) return false;
            var parts = id.Substring(Prefix.Length).Split('_');
            if (!int.TryParse(parts[0], out slot) || slot < 0) return false;
            if (parts.Length == 1) return true;
            if (parts.Length == 2 && parts[1] == "self") { kind = "self"; return true; }
            if (parts.Length == 3 && parts[1] == "enemy" && int.TryParse(parts[2], out enemy) && enemy >= 0) { kind = "enemy"; return true; }
            slot = -1; enemy = -1;
            return false;
        }
    }
}
