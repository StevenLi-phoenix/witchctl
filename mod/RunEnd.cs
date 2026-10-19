using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Witch.UI.Automation;
using Witch.UI.Window;
using UIManager = Witch.UI.UIManager;

namespace WitchCtl
{
    /// <summary>
    /// The settlement screen at the end of a run (<c>GameExitUI</c>, after the last fight or a defeat). The game's phase model reports
    /// `unknown` with no action. Its one button (<c>主内容/ButtonUse</c>) works in two steps: the first click shows the score multipliers,
    /// the next one claims the reward and returns to the main menu (<c>ReturnAsync</c>, which closes the window). Phase <c>run_end</c>,
    /// action <c>run_end_continue</c> (click it until the phase changes).
    /// </summary>
    internal static class RunEnd
    {
        internal const string ContinueId = "run_end_continue";

        private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly FieldInfo Breakdown = typeof(GameExitUI).GetField("rewardDict", Private);
        private static readonly FieldInfo Total = typeof(GameExitUI).GetField("totalScoreVal", Private);
        private static readonly FieldInfo FirstClick = typeof(GameExitUI).GetField("firstClicek", Private);
        private static readonly FieldInfo Closed = typeof(GameExitUI).GetField("hasClosed", Private);

        internal static bool Owns(string actionId) => actionId == ContinueId;

        private static GameExitUI Ui()
        {
            var ui = UIManager.Instance?.GetUI<GameExitUI>("GameExitUI");
            return ui != null && ui.gameObject.activeInHierarchy ? ui : null;
        }

        private static GameObject Button(GameExitUI ui)
        {
            var button = ui.transform.Find("主内容/ButtonUse");
            return button != null && button.gameObject.activeInHierarchy ? button.gameObject : null;
        }

        internal static Screens.Screen Detect()
        {
            var ui = Ui();
            if (ui == null) return null;
            try
            {
                var screen = new Screens.Screen { Phase = "run_end" };
                var closed = Closed != null && (bool)Closed.GetValue(ui);
                var detailsShown = FirstClick != null && !(bool)FirstClick.GetValue(ui);
                screen.Detail["runEnd"] = new
                {
                    won = !GameExitUI.loss,
                    score = Breakdown?.GetValue(ui) as Dictionary<string, int>,
                    total = Total?.GetValue(ui),
                    detailsShown,   // the first click was made: the next one returns to the main menu
                    closing = closed,
                };
                if (!closed && Button(ui) != null)
                    screen.Actions.Add(new RuntimeLegalAction
                    {
                        Id = ContinueId,
                        Kind = ContinueId,
                        Label = detailsShown ? "返回主菜单" : "结算",
                        Description = detailsShown ? "Claim the reward and return to the main menu." : "Show the score breakdown; click again to return to the main menu.",
                    });
                return screen;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("[WitchCtl] run-end scan failed: " + e);
                return null;
            }
        }

        internal static RuntimePerformActionResult Perform(string actionId)
        {
            string refusal = null, message = null;
            var ui = Ui();
            var button = ui == null ? null : Button(ui);
            if (ui == null) refusal = "the settlement screen is not open";
            else if (Closed != null && (bool)Closed.GetValue(ui)) refusal = "the settlement screen is already closing";
            else if (button == null) refusal = "the settlement button is not up yet";
            else
            {
                bool second = FirstClick != null && !(bool)FirstClick.GetValue(ui);
                UiLite.ClickObject(button);
                message = second ? "Returning to the main menu." : "Showing the score breakdown.";
            }
            if (refusal != null) return new RuntimePerformActionResult { Success = false, ActionId = actionId, Message = "refused: " + refusal };
            UnityEngine.Debug.Log("[WitchCtl] " + actionId + ": " + message);
            return new RuntimePerformActionResult { Success = true, ActionId = actionId, Message = message };
        }
    }
}
