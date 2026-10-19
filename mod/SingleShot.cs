using System.Collections.Generic;
using UnityEngine;
using Witch.UI.Automation;

namespace WitchCtl
{
    /// <summary>
    /// Scene-changing actions must fire once per phase entry. The game keeps offering them (and runs them again) for the few
    /// seconds the scene takes to change, and two `map_continue` in a row start two fights on top of each other: the second one
    /// never gets a player or a hand ("FightInit cleaned stale untracked fight objects"). A client that polls and acts in a loop
    /// hits this at once, so the bridge refuses the repeat instead of trusting every client to wait.
    /// </summary>
    internal static class SingleShot
    {
        private static readonly HashSet<string> Ids = new HashSet<string>
        {
            "main_menu_start_game", "house_open_game_menu", "game_entry_start", "map_continue", "hub_start_game",
        };

        private const float WindowSeconds = 10f;

        private static string _id;
        private static string _phase;
        private static float _at;

        /// <summary>A refusal message when <paramref name="actionId"/> was just issued and the phase has not moved on, else null.</summary>
        internal static string Check(string actionId)
        {
            if (!Ids.Contains(actionId) || actionId != _id) return null;
            float age = Time.realtimeSinceStartup - _at;
            if (age >= WindowSeconds || RuntimeGameplayAutomationService.GetLegalActions().Phase != _phase) return null;
            return actionId + " was issued " + age.ToString("0.0") + " s ago and the game is still changing scene; "
                   + "wait for the phase to change (`witchctl wait`) instead of repeating it";
        }

        internal static void Record(string actionId)
        {
            if (!Ids.Contains(actionId)) return;
            _id = actionId;
            _at = Time.realtimeSinceStartup;
            _phase = RuntimeGameplayAutomationService.GetLegalActions().Phase;
        }
    }
}
