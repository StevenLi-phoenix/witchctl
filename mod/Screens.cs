using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Michsky.MUIP;
using TMPro;
using UnityEngine;
using Witch;
using Witch.UI;
using Witch.UI.Automation;
using Witch.UI.Window;
using UIManager = Witch.UI.UIManager;

namespace WitchCtl
{
    /// <summary>
    /// The screens that follow a fight. The game's phase model does not know them (they surface as `unknown`, or as a
    /// `battle` with nothing to do), so without this an agent is stuck right after its first win:
    ///   FightUI (fight won, chest) → BattleRewardsUI → CardChoiceUI / blessing picker → BattleRewardsUI → Close → map.
    /// Each is reported as its own phase with ordinary action ids, driven through the same pointer clicks a player makes.
    /// </summary>
    internal static class Screens
    {
        internal sealed class Screen
        {
            public string Phase;
            public List<RuntimeLegalAction> Actions = new List<RuntimeLegalAction>();
            public Dictionary<string, object> Detail = new Dictionary<string, object>();
            /// <summary>No modelled actions: attach the UI dump so the agent can still click around.</summary>
            public bool NeedsUi;

            public void WriteTo(Dictionary<string, object> state)
            {
                state["phase"] = Phase;
                state["actions"] = Actions;
                foreach (var kv in Detail) state[kv.Key] = kv.Value;
            }
        }

        private const string OpenRewards = "battle_open_rewards";
        private const string ClaimPrefix = "reward_claim_";
        private const string ClosePrefix = "reward_close";
        private const string PickPrefix = "card_choice_pick_";
        private const string SkipId = "card_choice_skip";
        private const string BlessPrefix = "blessing_pick_";

        // Private state of the card-choice item: only true after the ~1 s fade-in, and again false once a card is chosen.
        private static readonly FieldInfo CanClickField = typeof(CardChoiceItem).GetField("canClick", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo ChoiceConfigField = typeof(CardChoiceItem).GetField("dataConfig", BindingFlags.NonPublic | BindingFlags.Instance);

        internal static bool Owns(string actionId) =>
            actionId != null && (actionId == OpenRewards || actionId.StartsWith("reward_", StringComparison.Ordinal)
                                    || actionId.StartsWith("card_choice_", StringComparison.Ordinal) || actionId.StartsWith(BlessPrefix, StringComparison.Ordinal)
                                    || Events.Owns(actionId) || RunEnd.Owns(actionId) || Popups.Owns(actionId) || Deck.Owns(actionId) || DeckSelect.Owns(actionId) || Shops.Owns(actionId) || Achievements.Owns(actionId) || OutsiderShop.Owns(actionId));

        private static bool Active(UnityEngine.Component c) => c != null && c.gameObject.activeInHierarchy;

        /// <summary>The post-fight screen showing right now, or null (then the normal battle/menu logic applies).</summary>
        internal static Screen Detect()
        {
            var ui = UIManager.Instance;
            if (ui == null) return null;

            var modal = Popups.DetectModal();   // a yes/no window over anything else: the screens behind it cannot be clicked
            if (modal != null) return modal;

            var deck = Deck.Detect();   // the deck window opened by the agent (or by a "backpack full" modal): it sits on top of whatever it was opened over
            if (deck != null) return deck;

            var pick = DeckSelect.Detect();   // "choose N cards" (enchantment, event): the fight behind it is NOT paused, so it must come before `battle`
            if (pick != null) return pick;

            var achievements = Achievements.Detect();   // opened from the hub
            if (achievements != null) return achievements;

            var outsider = OutsiderShop.Detect();   // the hub's meta shops (card packs / careers), opened from a book page
            if (outsider != null) return outsider;

            var choice = ui.GetUI<CardChoiceUI>("CardChoiceUI");
            if (Active(choice)) return CardChoice(choice);

            var rewards = ui.GetUI<BattleRewardsUI>("BattleRewardsUI");
            if (Active(rewards))
            {
                // While the blessing picker is open the reward list ("Window Manager") is switched off and the picker hangs off the same window.
                if (rewards.transform.Find("Window Manager")?.gameObject.activeInHierarchy == true) return Rewards(rewards);
                var picker = BlessPicker(rewards);
                if (picker != null) return Blessings(picker);
            }

            var tree = ui.GetUI<DestinyTreeUI>("DestinyTreeUI");
            if (Active(tree))
            {
                var treePicker = BlessPicker(tree);   // a divination's three blessings, over the tree
                if (treePicker != null) return Blessings(treePicker);
            }

            var shop = Shops.Detect();   // shop / destiny tree / enchant window: opened over the map or an event
            if (shop != null) return shop;

            var runEnd = RunEnd.Detect();   // the last screen of a run: above the fight that ended it
            if (runEnd != null) return runEnd;

            if (Battle.IsActive())
            {
                var type = FightManager.Instance?.fightType;
                if (type == FightType.Win || Battle.ChestReady()) return FightWon();
                if (type == FightType.Loss) return new Screen { Phase = "battle_lost", NeedsUi = true };
            }
            return Events.Detect() ?? Popups.Detect();
        }

        // ------------------------------------------------------------------ fight won

        private static Screen FightWon()
        {
            var screen = new Screen { Phase = "battle_won" };
            screen.Actions.Add(new RuntimeLegalAction { Id = OpenRewards, Kind = OpenRewards, Label = "打开宝箱", Description = "Open the chest and show the battle rewards." });
            return screen;
        }

        // ------------------------------------------------------------------ rewards

        private sealed class RewardItem
        {
            public int Index;
            public string Kind;
            public string Title;
            public string Text;
            public bool Interactable;
            public GameObject Go;
        }

        private static List<RewardItem> RewardItems(BattleRewardsUI ui)
        {
            var items = new List<RewardItem>();
            if (ui.itemList == null) return items;
            foreach (var point in ui.itemList.GetComponentsInChildren<PointUse>(false))
            {
                var button = point.GetComponent<ButtonManager>();
                items.Add(new RewardItem
                {
                    Index = items.Count,
                    Kind = point.RewardType,
                    Title = TextAt(point.transform, "Normal/texts/title"),
                    Text = TextAt(point.transform, "Normal/texts/description"),
                    Interactable = button != null && button.isInteractable,
                    Go = point.gameObject,
                });
            }
            return items;
        }

        private static ButtonManager CloseButton(BattleRewardsUI ui) => ui.transform.Find("Window Manager/Close")?.GetComponent<ButtonManager>();

        private static Screen Rewards(BattleRewardsUI ui)
        {
            var screen = new Screen { Phase = "rewards" };
            var items = RewardItems(ui);
            foreach (var item in items.Where(i => i.Interactable))
                screen.Actions.Add(new RuntimeLegalAction
                {
                    Id = ClaimPrefix + item.Index,
                    Kind = "reward_claim",
                    Label = item.Title + (string.IsNullOrEmpty(item.Text) ? "" : " — " + item.Text),
                    Description = "Claim this reward (a card reward opens the card choice).",
                });
            var close = CloseButton(ui);
            if (close != null && close.gameObject.activeInHierarchy && close.isInteractable)
                screen.Actions.Add(new RuntimeLegalAction { Id = ClosePrefix, Kind = "reward_close", Label = "关闭", Description = "Leave the reward screen; unclaimed rewards turn into gold." });
            screen.Detail["rewards"] = items.Select(i => new { index = i.Index, kind = i.Kind, title = i.Title, text = i.Text, claimable = i.Interactable }).ToList();
            return screen;
        }

        // ------------------------------------------------------------------ card choice

        private sealed class ChoiceItem
        {
            public int Index;
            public CardChoiceItem Item;
            public string Id;
            public string Rarity;
            public string Name;
            public string Cost;
            public string Text;
            public bool CanClick;
        }

        /// <summary>
        /// The game keeps at most <c>MaxAlCardCount</c> picked-but-unequipped cards (backpack). Past that a click on a card only shows a
        /// "卡牌数量已达上限" modal and adds nothing, while the screen stays open — so offering the pick would be a lie.
        /// </summary>
        private static bool CollectionFull(out int count, out int max)
        {
            var role = RoleTable.Instance;
            count = role?.UnCardList?.Count ?? 0;
            max = role?.MaxAlCardCount ?? int.MaxValue;
            return role != null && count >= max;
        }

        internal static bool CanClick(CardChoiceItem item) => CanClickField == null || item == null || (bool)CanClickField.GetValue(item);

        private static List<ChoiceItem> ChoiceItems(CardChoiceUI ui)
        {
            var items = new List<ChoiceItem>();
            for (int n = 1; n <= 3; n++)
            {
                var item = ui.transform.Find("Content/Item" + n)?.GetComponent<CardChoiceItem>();
                if (item == null || !item.gameObject.activeInHierarchy) continue;
                var data = (ChoiceConfigField?.GetValue(item) as IDataConfig)?.data;
                items.Add(new ChoiceItem
                {
                    Index = items.Count,
                    Item = item,
                    Id = data != null && data.TryGetValue("Id", out var id) ? id : null,
                    Rarity = data != null && data.TryGetValue("Rarity", out var rarity) ? rarity : null,
                    Name = TextAt(item.transform, "Front/字体/nameTxt"),
                    Cost = TextAt(item.transform, "Front/cost/cost"),
                    Text = TextAt(item.transform, "Front/字体/msgTxt"),
                    CanClick = CanClick(item),
                });
            }
            return items;
        }

        private static GameObject SkipButton(CardChoiceUI ui)
        {
            var skip = ui.transform.Find("Button");
            return skip != null && skip.gameObject.activeInHierarchy ? skip.gameObject : null;
        }

        private static Screen CardChoice(CardChoiceUI ui)
        {
            var screen = new Screen { Phase = "card_choice" };
            var items = ChoiceItems(ui);
            var full = CollectionFull(out var held, out var capacity);
            foreach (var item in items.Where(i => i.CanClick && !full))
                screen.Actions.Add(new RuntimeLegalAction
                {
                    Id = PickPrefix + item.Index,
                    Kind = "card_choice_pick",
                    Label = item.Name,
                    Description = "cost " + item.Cost + " — " + item.Text,
                    Parameters = new Dictionary<string, object> { ["cardId"] = item.Id },
                });
            if (SkipButton(ui) != null && items.Any(i => i.CanClick))
                screen.Actions.Add(new RuntimeLegalAction { Id = SkipId, Kind = "card_choice_skip", Label = "跳过", Description = "Take none of the cards." });
            screen.Detail["cardChoice"] = items.Select(i => new { index = i.Index, id = i.Id, name = i.Name, cost = i.Cost, rarity = i.Rarity, text = i.Text, pickable = i.CanClick && !full }).ToList();
            if (full) screen.Detail["note"] = "card collection full (" + held + "/" + capacity + "): picking is refused by the game, only skip is possible";
            return screen;
        }

        // ------------------------------------------------------------------ blessing picker

        private sealed class BlessItem
        {
            public int Index;
            public string Text;
            public GameObject Button;
            public bool Interactable;
        }

        /// <summary>The blessing picker hung off a window (<c>BattleRewardsUI</c> after a fight, <c>DestinyTreeUI</c> after a divination).</summary>
        private static Transform BlessPicker(UnityEngine.Component host)
        {
            foreach (Transform child in host.transform)
                if (child.name.StartsWith("BlessChoice", StringComparison.Ordinal) && child.gameObject.activeInHierarchy) return child;
            return null;
        }

        private static List<BlessItem> BlessItems(Transform picker)
        {
            var items = new List<BlessItem>();
            var list = picker.Find("Window Manager/Windows/牌堆/Content/List View Custom/List");
            for (int n = 1; n <= 3 && list != null; n++)
            {
                var option = list.Find("Blessing" + n);
                if (option == null || !option.gameObject.activeInHierarchy) continue;
                var button = option.Find("button")?.GetComponent<ButtonManager>();
                var parts = option.GetComponentsInChildren<TMP_Text>(false)
                    .Where(t => button == null || !t.transform.IsChildOf(button.transform))
                    .Select(t => Rich.Strip(t.text)).Where(t => t.Length > 0).Distinct();
                items.Add(new BlessItem
                {
                    Index = items.Count,
                    Text = string.Join(" | ", parts),
                    Button = button?.gameObject,
                    Interactable = button != null && button.isInteractable,
                });
            }
            return items;
        }

        private static Screen Blessings(Transform picker)
        {
            var screen = new Screen { Phase = "blessing_choice" };
            var items = BlessItems(picker);
            foreach (var item in items.Where(i => i.Interactable))
                screen.Actions.Add(new RuntimeLegalAction { Id = BlessPrefix + item.Index, Kind = "blessing_pick", Label = item.Text, Description = "Take this blessing (all of its parts)." });
            screen.Detail["blessings"] = items.Select(i => new { index = i.Index, text = i.Text, pickable = i.Interactable }).ToList();
            return screen;
        }

        // ------------------------------------------------------------------ doing things

        /// <summary>Performs a screen action. Only call for ids where <see cref="Owns"/> is true.</summary>
        internal static RuntimePerformActionResult Perform(string actionId)
        {
            string refusal = null;
            string message = null;
            var ui = UIManager.Instance;

            if (Events.Owns(actionId)) return Events.Perform(actionId);
            if (RunEnd.Owns(actionId)) return RunEnd.Perform(actionId);
            if (Popups.Owns(actionId)) return Popups.Perform(actionId);
            if (Deck.Owns(actionId)) return Deck.Perform(actionId);
            if (DeckSelect.Owns(actionId)) return DeckSelect.Perform(actionId);
            if (Shops.Owns(actionId)) return Shops.Perform(actionId);
            if (Achievements.Owns(actionId)) return Achievements.Perform(actionId);
            if (OutsiderShop.Owns(actionId)) return OutsiderShop.Perform(actionId);
            if (actionId == OpenRewards)
            {
                var fight = ui?.GetUI<FightUI>("FightUI");
                if (!Active(fight) || !(FightManager.Instance?.fightType == FightType.Win || Battle.ChestReady())) refusal = "the fight is not won";
                else { fight.ShowBattleReward(); message = "Opened the battle rewards."; UnityEngine.Debug.Log("[WitchCtl] battle_open_rewards: chest opened by the agent"); }
            }
            else if (actionId.StartsWith(ClaimPrefix, StringComparison.Ordinal))
            {
                var rewards = ui?.GetUI<BattleRewardsUI>("BattleRewardsUI");
                if (!Active(rewards)) refusal = "the reward screen is not open";
                else if (!int.TryParse(actionId.Substring(ClaimPrefix.Length), out var index)) refusal = "bad reward index";
                else
                {
                    var item = RewardItems(rewards).FirstOrDefault(i => i.Index == index);
                    if (item == null) refusal = "no reward " + index + " on screen";
                    else if (!item.Interactable) refusal = "reward " + index + " was already claimed";
                    else { UiLite.ClickObject(item.Go); message = "Claimed: " + item.Title; }
                }
            }
            else if (actionId == ClosePrefix)
            {
                var rewards = ui?.GetUI<BattleRewardsUI>("BattleRewardsUI");
                var close = Active(rewards) ? CloseButton(rewards) : null;
                if (close == null || !close.gameObject.activeInHierarchy || !close.isInteractable) refusal = "the reward screen cannot be closed right now";
                else { UiLite.ClickObject(close.gameObject); message = "Closed the reward screen."; }
            }
            else if (actionId.StartsWith(PickPrefix, StringComparison.Ordinal))
            {
                var choice = ui?.GetUI<CardChoiceUI>("CardChoiceUI");
                if (!Active(choice)) refusal = "the card choice is not open";
                else if (!int.TryParse(actionId.Substring(PickPrefix.Length), out var index)) refusal = "bad card index";
                else
                {
                    var item = ChoiceItems(choice).FirstOrDefault(i => i.Index == index);
                    if (item == null) refusal = "no card " + index + " on screen";
                    else if (!item.CanClick) refusal = "card " + index + " cannot be picked yet (fade-in) or a card was already chosen";
                    else if (CollectionFull(out var held, out var capacity)) refusal = "card collection is full (" + held + "/" + capacity + "): the game would refuse the pick; skip instead";
                    else { UiLite.ClickObject(item.Item.gameObject); message = "Picked: " + item.Name; }
                }
            }
            else if (actionId.StartsWith(BlessPrefix, StringComparison.Ordinal))
            {
                var rewards = ui?.GetUI<BattleRewardsUI>("BattleRewardsUI");
                var tree = ui?.GetUI<DestinyTreeUI>("DestinyTreeUI");
                var picker = (Active(rewards) ? BlessPicker(rewards) : null) ?? (Active(tree) ? BlessPicker(tree) : null);
                if (picker == null) refusal = "the blessing picker is not open";
                else if (!int.TryParse(actionId.Substring(BlessPrefix.Length), out var index)) refusal = "bad blessing index";
                else
                {
                    var item = BlessItems(picker).FirstOrDefault(i => i.Index == index);
                    if (item == null || item.Button == null) refusal = "no blessing " + index + " on screen";
                    else if (!item.Interactable) refusal = "blessing " + index + " cannot be picked";
                    else { UiLite.ClickObject(item.Button); message = "Took blessing: " + item.Text; }
                }
            }
            else if (actionId == SkipId)
            {
                var choice = ui?.GetUI<CardChoiceUI>("CardChoiceUI");
                var skip = Active(choice) ? SkipButton(choice) : null;
                if (skip == null) refusal = "there is nothing to skip";
                else { UiLite.ClickObject(skip); message = "Skipped the card choice."; }
            }
            else refusal = "unknown screen action";

            if (refusal != null) return new RuntimePerformActionResult { Success = false, ActionId = actionId, Message = "refused: " + refusal };
            UnityEngine.Debug.Log("[WitchCtl] " + actionId + ": " + message);
            return new RuntimePerformActionResult { Success = true, ActionId = actionId, Message = message };
        }

        private static string TextAt(Transform root, string path) => Rich.Strip(root.Find(path)?.GetComponent<TMP_Text>()?.text);
    }
}
