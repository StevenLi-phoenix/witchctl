using System;
using System.Collections.Generic;
using System.Linq;
using Michsky.MUIP;
using UnityEngine;
using Witch.UI.Automation;
using Witch.UI.Window;
using UIManager = Witch.UI.UIManager;

namespace WitchCtl
{
    /// <summary>
    /// The achievements window (<c>AchievementUI</c>, opened from the hub's "Task" button, see <see cref="Hub"/>), phase <c>achievements</c>.
    /// A finished achievement pays truth crystals or time (the profile currency the meta shops sell for), once: <c>achievement_claim_N</c>
    /// (N = index in `achievements.items`) calls the game's own <c>AchItem.GetReward()</c>, which the window only allows when the progress has
    /// reached the target and the reward was not taken (the item's button is interactable). The payout is read back from the profile.
    /// <c>achievement_close</c> closes the window. Claiming is harmless and cannot be undone.
    /// </summary>
    internal static class Achievements
    {
        private const string ClaimPrefix = "achievement_claim_", CloseId = "achievement_close";

        internal static bool Owns(string actionId) => actionId != null && actionId.StartsWith("achievement_", StringComparison.Ordinal);

        private static AchievementUI Window()
        {
            var ui = UIManager.Instance?.GetUI<AchievementUI>("AchievementUI");
            return ui != null && ui.gameObject.activeInHierarchy ? ui : null;
        }

        internal static bool IsOpen() => Window() != null;

        private sealed class Entry
        {
            public int Index;
            public AchItem Item;
            public string Id, Name, Text, RewardType, Reward;
            public float Progress, Target;
            public bool Claimable;
            public bool Done => Progress >= Target;
        }

        private static List<Entry> Items(AchievementUI ui)
        {
            var found = new List<Entry>();
            if (ui.ItemParent == null) return found;
            foreach (var item in ui.ItemParent.GetComponentsInChildren<AchItem>(false))
            {
                if (item.gameObject == ui.ItemPre || item.dataConfig?.data == null) continue;
                var data = item.dataConfig.data;
                found.Add(new Entry
                {
                    Index = found.Count, Item = item,
                    Id = data.TryGetValue("Id", out var id) ? id : "?",
                    Name = Rich.NameOf(item.dataConfig), Text = Rich.Describe(item.dataConfig),
                    RewardType = item.RewardType, Reward = data.TryGetValue("Reward", out var r) ? r : "",
                    Progress = item.CurrentCount, Target = item.TopCount,
                    Claimable = item.transform.Find("Button")?.GetComponent<ButtonManager>()?.isInteractable == true,
                });
            }
            return found;
        }

        private static int Currency(string type)
        {
            var profile = Singleton<GameRuntimeData>.Instance;
            return type == "Truth" ? (int)profile.Truth : (int)profile.Time;
        }

        internal static Screens.Screen Detect()
        {
            var ui = Window();
            if (ui == null) return null;
            var screen = new Screens.Screen { Phase = "achievements" };
            try
            {
                var items = Items(ui);
                foreach (var e in items.Where(e => e.Claimable))
                    screen.Actions.Add(new RuntimeLegalAction
                    {
                        Id = ClaimPrefix + e.Index, Kind = "achievement_claim", Label = e.Name,
                        Description = "Claim " + e.Reward + " " + e.RewardType + " (once). " + e.Text,
                        Parameters = new Dictionary<string, object> { ["reward"] = e.Reward, ["type"] = e.RewardType },
                    });
                screen.Actions.Add(new RuntimeLegalAction { Id = CloseId, Kind = CloseId, Label = "关闭", Description = "Close the achievements window." });
                screen.Detail["achievements"] = new
                {
                    truth = Currency("Truth"), time = Currency("Time"),
                    claimable = items.Count(e => e.Claimable), total = items.Count,
                    items = items.Select(e => new
                    {
                        index = e.Index, id = e.Id, name = e.Name, text = e.Text, progress = e.Progress, target = e.Target,
                        reward = e.Reward, rewardType = e.RewardType, claimable = e.Claimable, claimed = e.Done && !e.Claimable,
                    }).ToList(),
                };
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[WitchCtl] achievements scan failed: " + ex);
                screen.Actions.Clear();
                screen.Actions.Add(new RuntimeLegalAction { Id = CloseId, Kind = CloseId, Label = "关闭", Description = "Close the achievements window." });
                screen.Detail["achievementsError"] = ex.GetType().Name + ": " + ex.Message;
            }
            return screen;
        }

        internal static RuntimePerformActionResult Perform(string actionId)
        {
            string refusal = null, message = null;
            var ui = Window();
            if (ui == null) refusal = "the achievements window is not open";
            else if (actionId == CloseId)
            {
                UIManager.Instance.CloseUI("AchievementUI");
                message = "Closed the achievements window.";
            }
            else if (actionId.StartsWith(ClaimPrefix, StringComparison.Ordinal))
            {
                var entry = int.TryParse(actionId.Substring(ClaimPrefix.Length), out var index) ? Items(ui).FirstOrDefault(e => e.Index == index) : null;
                if (entry == null) refusal = "no such achievement";
                else if (!entry.Claimable) refusal = entry.Done ? entry.Name + " was already claimed" : entry.Name + " is not finished (" + entry.Progress + "/" + entry.Target + ")";
                else
                {
                    int before = Currency(entry.RewardType);
                    entry.Item.GetReward();
                    int after = Currency(entry.RewardType);
                    if (after == before) refusal = "the game did not pay out (" + entry.RewardType + " " + before + ")";
                    else message = "Claimed " + entry.Name + ": " + entry.RewardType + " " + before + "→" + after + ".";
                }
            }
            else refusal = "unknown achievement action";
            if (refusal != null) return new RuntimePerformActionResult { Success = false, ActionId = actionId, Message = "refused: " + refusal };
            UnityEngine.Debug.Log("[WitchCtl] " + actionId + ": " + message);
            return new RuntimePerformActionResult { Success = true, ActionId = actionId, Message = message };
        }
    }
}
