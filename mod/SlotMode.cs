using System;
using System.Collections.Generic;
using Witch;
using Witch.UI;
using Witch.UI.Automation;
using Witch.UI.Window;
using UIManager = Witch.UI.UIManager;

namespace WitchCtl
{
    /// <summary>
    /// Chaos Paradise ("Slot" mode; the hub button is `mode_choice_slot`, player level 25+). The map screen is replaced by <c>SlotMachUI</c>: a
    /// chip game of picked rewards, queued enemies and day/night fights, that ends when the chips run out or the player leaves. Leaving early is
    /// a feature the game's own tutorial teaches ("click the top right to leave early", <c>TopBarUI.CheckSlot</c> → the yes/no window "是否要结束此局"
    /// → <c>SlotMachineManager.EndGameWithAdvanceTimeFlow</c>): the run is settled on the spot, and the settlement score is worth Truth
    /// (<c>GetSettlementTruthReward</c>, capped at a score of 50000). With the difficulty tags up the score is multiplied ~40x, so
    /// leaving at once pays the cap, in a fraction of the time a classic run needs.
    /// The game's phase model knows nothing of it (`unknown`); reported here as phase <c>slot</c> with the numbers that matter and the one
    /// action <c>slot_end_run</c>, which only opens the game's own confirmation (answer it with `modal_confirm_end_run`). Picking rewards, standing,
    /// tokens and the stake window (which spends permanent Truth) are not modelled; <c>EndSlot</c> stays on the `ui` deny list.
    /// </summary>
    internal static class SlotMode
    {
        internal const string EndId = "slot_end_run";

        private static SlotMachineManager Manager() => MapManager.Instance?.ModeMapManager as SlotMachineManager;

        private static SlotMachUI Screen()
        {
            var ui = UIManager.Instance?.GetUI<SlotMachUI>("SlotMachUI");
            return ui != null && ui.gameObject.activeInHierarchy ? ui : null;
        }

        /// <summary>True when the Slot screen is what is on screen. Only call when the game's phase is `unknown` (a window above it, such as the
        /// yes/no window, is found first by <see cref="Screens.Detect"/>).</summary>
        internal static bool Detect() => Screen() != null && Manager() != null;

        internal static bool Owns(string actionId) => actionId == EndId;

        /// <summary>`slot`: chips, draws, the day/night, and what leaving now would pay (<c>payoutTruth</c>, from the live score).</summary>
        internal static void Attach(Dictionary<string, object> state)
        {
            var manager = Manager();
            if (manager == null) return;
            try
            {
                state["slot"] = new
                {
                    chips = manager.ChipReserve,
                    chipsEarned = manager.ChipTotalEarned,
                    nextStake = manager.CurrentRoundRequiredStake,
                    draws = manager.DraftTurnCount,
                    night = manager.IsNightPhase,
                    dayBattles = manager.DayBattleCount,
                    baseScore = manager.GetCurrentSettlementBaseScore(),
                    payoutTruth = manager.GetSettlementTruthReward(),
                };
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("[WitchCtl] slot summary failed: " + e);
            }
        }

        internal static List<RuntimeLegalAction> Actions() => new List<RuntimeLegalAction>
        {
            new RuntimeLegalAction
            {
                Id = EndId, Kind = "slot_end_run", Label = "结束此局",
                Description = "Leave Chaos Paradise now: opens the game's confirmation (answer with modal_confirm_end_run), then the run is settled and `slot.payoutTruth` is paid.",
            },
        };

        internal static RuntimePerformActionResult Perform(string actionId)
        {
            string refusal = null, message = null;
            var top = UIManager.Instance?.GetUI<TopBarUI>("TopBarUI");
            if (Screen() == null || Manager() == null) refusal = "the Chaos Paradise screen is not on screen";
            else if (top == null) refusal = "no top bar to leave through";
            else
            {
                top.CheckSlot();   // the button's own handler: shows "是否要结束此局"; the window's confirm button ends the run
                message = "Opened the end-of-run confirmation; confirm with modal_confirm_end_run.";
            }
            if (refusal != null) return new RuntimePerformActionResult { Success = false, ActionId = actionId, Message = "refused: " + refusal };
            UnityEngine.Debug.Log("[WitchCtl] " + actionId + ": " + message);
            return new RuntimePerformActionResult { Success = true, ActionId = actionId, Message = message };
        }
    }
}
