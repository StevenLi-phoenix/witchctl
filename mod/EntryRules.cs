using System;

namespace WitchCtl
{
    /// <summary>
    /// Action ids and limits of the "before the run" screen (<c>GameEntryUI</c>), as plain logic (no Unity types, tested in tests/modlogic).
    /// `/act` carries only an id, so the choice is part of it: <c>entry_career_1</c>, <c>entry_partner_0</c>, <c>entry_main_Lucky</c>,
    /// <c>entry_second_Wisdom</c>, <c>entry_pack_off_cardpack_9</c>, plus <c>entry_prepare_open</c> / <c>entry_prepare_close</c> for the window
    /// the choices live in. The start itself stays the game's own <c>game_entry_start</c>.
    /// </summary>
    internal static class EntryRules
    {
        /// <summary>The four attributes (`FightVarItem` names under `MainParent` / `SecondParent`).</summary>
        internal static readonly string[] Vars = { "Strength", "Lucky", "Perceive", "Wisdom" };

        /// <summary>`CardPackUI.SetPackEnabled` refuses to go below this many enabled packs (it opens a modal instead).</summary>
        internal const int MinPacks = 6;

        internal const string PrepareOpen = "entry_prepare_open", PrepareClose = "entry_prepare_close";

        internal static bool Owns(string id) => id != null && id.StartsWith("entry_", StringComparison.Ordinal);

        internal static string Career(int index) => "entry_career_" + index;
        internal static string Partner(int index) => "entry_partner_" + index;
        internal static string Var(string slot, string name) => "entry_" + slot + "_" + name;   // slot: main | second
        internal static string Pack(bool enable, string packId) => "entry_pack_" + (enable ? "on_" : "off_") + packId;

        /// <summary>kind: prepare_open | prepare_close | career | partner | main | second | pack_on | pack_off; arg: the index, attribute or pack id.</summary>
        internal static bool TryParse(string id, out string kind, out string arg)
        {
            kind = null; arg = null;
            if (!Owns(id)) return false;
            if (id == PrepareOpen) { kind = "prepare_open"; return true; }
            if (id == PrepareClose) { kind = "prepare_close"; return true; }
            foreach (var prefix in new[] { "career", "partner", "main", "second", "pack_on", "pack_off" })
            {
                var head = "entry_" + prefix + "_";
                if (!id.StartsWith(head, StringComparison.Ordinal)) continue;
                arg = id.Substring(head.Length);
                if (arg.Length == 0) return false;
                if ((prefix == "career" || prefix == "partner") && !(int.TryParse(arg, out var n) && n >= 0)) return false;
                if ((prefix == "main" || prefix == "second") && Array.IndexOf(Vars, arg) < 0) return false;
                kind = prefix;
                return true;
            }
            return false;
        }

        /// <summary>Why a card pack cannot be switched (null = fine). <paramref name="enabled"/> counts the packs the screen lists.</summary>
        internal static string PackRefusal(bool enable, bool isOn, int enabled)
        {
            if (enable) return isOn ? "that pack is already enabled" : null;
            if (!isOn) return "that pack is already disabled";
            return enabled <= MinPacks ? "at least " + MinPacks + " card packs must stay enabled (now " + enabled + ")" : null;
        }
    }
}
