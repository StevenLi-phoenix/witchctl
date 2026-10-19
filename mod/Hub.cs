using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using Witch.UI.Automation;
using Witch.UI.Window;

namespace WitchCtl
{
    /// <summary>
    /// The hub (<c>NewHouse</c>: the witch's house, library, tower, ...) is a world scene whose "开始游戏" sign is a clickable
    /// <c>SceneItem</c>. The game's phase model only knows the hub through <c>HouseUI</c>, which is not up when a save already exists
    /// (Start on the main menu then lands here), so the phase stayed `unknown` with no window and no action: an agent sat there for
    /// good. Reported as phase <c>hub</c> with <c>hub_start_game</c>, a click on the sign (which opens the mode choice, entry screen or
    /// resumes the saved run, as the game decides), and <c>hub_open_achievements</c>, a click on the HUD's "Task" button
    /// (<c>HouseManager.ShowTask</c>; see <see cref="Achievements"/>). The state carries <c>hub</c>: the profile's level, experience, truth
    /// crystals and time (what the achievements pay in). The other two HUD buttons (back to the main menu, settings) are left alone.
    /// </summary>
    internal static class Hub
    {
        internal const string StartId = "hub_start_game", AchievementsId = "hub_open_achievements", VisitPrefix = "hub_visit_", ClosePageId = "hub_page_close", ItemPrefix = "hub_page_item_";
        private const string TaskPath = "NewHouse/UIRoot/MainHudCanvas/RightBottomFrame/Task";
        private const string PagesPath = "NewHouse/UIRoot/WindowCanvas", PageCloseButton = PagesPath + "/ExitButton", PageUnused = "";

        /// <summary>The seven buildings that open a "book page" (a window of the hub's own, not in `GetAllUI()`): sign type → id suffix.</summary>
        private static readonly (Witch.UI.Window.SceneItem.SceneItemType Type, string Name)[] Pages =
        {
            (Witch.UI.Window.SceneItem.SceneItemType.OpenLibrary, "library"), (Witch.UI.Window.SceneItem.SceneItemType.OpenTower, "tower"),
            (Witch.UI.Window.SceneItem.SceneItemType.OpenExhibition, "exhibition"), (Witch.UI.Window.SceneItem.SceneItemType.OpenStore, "store"),
            (Witch.UI.Window.SceneItem.SceneItemType.OpenAdministration, "administration"), (Witch.UI.Window.SceneItem.SceneItemType.OpenSpring, "spring"),
            (Witch.UI.Window.SceneItem.SceneItemType.OpenWitchHouse, "witch_house"),
        };

        /// <summary>The book-page canvas is switched off while closed (`HouseManager.CloseBook`). `GameObject.Find("A/B/C")` only needs the root `A` to be
        /// active and also returns inactive children, hence the explicit check (a closed hub once reported an open page).</summary>
        private static GameObject PageCanvas()
        {
            var canvas = GameObject.Find(PagesPath);
            return canvas != null && canvas.activeInHierarchy ? canvas : null;
        }

        /// <summary>The page showing, or null. MUIP's <c>WindowManager</c> keeps every page's object active and tracks the shown one in
        /// <c>currentWindowIndex</c> (the first active child was always the library: seen live).</summary>
        private static GameObject CurrentPage(out string name)
        {
            name = null;
            if (PageCanvas() == null) return null;
            var manager = GameObject.Find(PagesPath + "/Window Manager")?.GetComponent<Michsky.MUIP.WindowManager>();
            if (manager?.windows == null || manager.currentWindowIndex < 0 || manager.currentWindowIndex >= manager.windows.Count) return null;
            var shown = manager.windows[manager.currentWindowIndex];
            name = string.IsNullOrEmpty(shown.windowName) ? shown.windowObject?.name : shown.windowName;
            return shown.windowObject;
        }

        private static string OpenPage()
        {
            CurrentPage(out var name);
            return name ?? (PageCanvas() != null ? "?" : null);
        }

        /// <summary>The buttons (`HouseItem`) of the open page: shops, the task list, the announcement, ... (index = position in this list).</summary>
        private static List<HouseItem> PageItems()
        {
            var root = CurrentPage(out _);
            return root == null ? new List<HouseItem>() : root.GetComponentsInChildren<HouseItem>(false).ToList();
        }

        private static string LabelOf(UnityEngine.Component c) =>
            string.Join(" ", c.GetComponentsInChildren<TMP_Text>(false).Select(t => Rich.Strip(t.text)).Where(t => t.Length > 0).Distinct());

        private static Witch.UI.Window.SceneItem Sign(Witch.UI.Window.SceneItem.SceneItemType type) =>
            UnityEngine.Object.FindObjectsOfType<Witch.UI.Window.SceneItem>().FirstOrDefault(i => i.houseItemType == type && i.gameObject.activeInHierarchy);

        private static Witch.UI.Window.SceneItem StartSign() =>
            UnityEngine.Object.FindObjectsOfType<Witch.UI.Window.SceneItem>().FirstOrDefault(i => i.houseItemType == Witch.UI.Window.SceneItem.SceneItemType.StartGame && i.gameObject.activeInHierarchy);

        private static GameObject TaskButton()
        {
            var button = GameObject.Find(TaskPath);
            return button != null && button.activeInHierarchy ? button : null;
        }

        /// <summary>True when the hub is on screen with nothing open over it and nothing of the game's own phase model: only call when the game says `unknown`.</summary>
        internal static bool Detect() => StartSign() != null && UiLite.OnlyAmbientWindows();   // a popup over the hub is the phase, not the sign

        internal static List<RuntimeLegalAction> Actions()
        {
            if (OpenPage() != null)   // a page covers the hub: nothing behind it can be clicked
            {
                var onPage = new List<RuntimeLegalAction> { new RuntimeLegalAction { Id = ClosePageId, Kind = ClosePageId, Label = "关闭书页", Description = "Close the open book page." } };
                var items = PageItems();
                for (var i = 0; i < items.Count; i++)
                    if (HouseRules.Refusal(items[i].houseItemType.ToString()) == null)
                        onPage.Add(new RuntimeLegalAction
                        {
                            Id = ItemPrefix + i, Kind = "hub_page_item", Label = LabelOf(items[i]),
                            Description = "Press the page's " + items[i].houseItemType + " button; the window it opens is the next phase.",
                            Parameters = new Dictionary<string, object> { ["type"] = items[i].houseItemType.ToString() },
                        });
                return onPage;
            }
            var actions = new List<RuntimeLegalAction>
            {
                new RuntimeLegalAction { Id = StartId, Kind = "hub_start_game", Label = "开始游戏", Description = "Click the start-game sign in the hub." },
            };
            if (TaskButton() != null)
                actions.Add(new RuntimeLegalAction { Id = AchievementsId, Kind = AchievementsId, Label = "成就", Description = "Open the achievements window (rewards are claimed there)." });
            foreach (var (type, name) in Pages)
                if (Sign(type) != null)
                    actions.Add(new RuntimeLegalAction { Id = VisitPrefix + name, Kind = "hub_visit", Label = name, Description = "Open the " + name + " page of the hub (look only; hub_page_close closes it)." });
            return actions;
        }

        /// <summary>`hub` in the state: the profile numbers the HUD shows.</summary>
        internal static void Attach(Dictionary<string, object> state)
        {
            try
            {
                var profile = Singleton<GameRuntimeData>.Instance;
                if (profile == null) return;
                state["hub"] = new { level = (int)profile.Level, exp = (int)profile.Exp, truth = (int)profile.Truth, time = (int)profile.Time, runsPlayed = profile.playCount, page = OpenPage(),
                    pageItems = PageItems().Select((h, i) => new { index = i, type = h.houseItemType.ToString(), label = LabelOf(h), allowed = HouseRules.Refusal(h.houseItemType.ToString()) == null, note = HouseRules.Refusal(h.houseItemType.ToString()) }).ToList() };
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning("[WitchCtl] hub profile failed: " + e.Message); }
        }

        internal static bool Owns(string actionId) =>
            actionId == StartId || actionId == AchievementsId || actionId == ClosePageId
            || (actionId != null && (actionId.StartsWith(VisitPrefix, StringComparison.Ordinal) || actionId.StartsWith(ItemPrefix, StringComparison.Ordinal)));

        private static RuntimePerformActionResult Refuse(string actionId, string why) =>
            new RuntimePerformActionResult { Success = false, ActionId = actionId, Message = "refused: " + why };

        internal static RuntimePerformActionResult Perform(string actionId)
        {
            if (actionId == ClosePageId)
            {
                var close = OpenPage() == null ? null : GameObject.Find(PageCloseButton);
                if (close == null) return Refuse(actionId, "no book page is open");
                UiLite.ClickObject(close);
                return new RuntimePerformActionResult { Success = true, ActionId = actionId, Message = "Closed the book page." };
            }
            if (actionId.StartsWith(ItemPrefix, StringComparison.Ordinal))
            {
                var items = PageItems();
                if (items.Count == 0) return Refuse(actionId, "no book page is open");
                if (!int.TryParse(actionId.Substring(ItemPrefix.Length), out var index) || index < 0 || index >= items.Count) return Refuse(actionId, "no such button on this page");
                var why = HouseRules.Refusal(items[index].houseItemType.ToString());
                if (why != null) return Refuse(actionId, items[index].houseItemType + " is not offered: " + why);
                UiLite.ClickObject(items[index].gameObject);
                return new RuntimePerformActionResult { Success = true, ActionId = actionId, Message = "Pressed " + items[index].houseItemType + " (" + LabelOf(items[index]) + "); read the next phase." };
            }
            if (actionId.StartsWith(VisitPrefix, StringComparison.Ordinal))
            {
                var name = actionId.Substring(VisitPrefix.Length);
                var page = Pages.FirstOrDefault(p => p.Name == name);
                if (page.Name == null) return Refuse(actionId, "unknown page " + name);
                if (OpenPage() != null) return Refuse(actionId, "a book page is already open: close it first");
                var building = Sign(page.Type);
                if (building == null) return Refuse(actionId, "the " + name + " sign is not on screen");
                UiLite.ClickObject(building.gameObject);
                return new RuntimePerformActionResult { Success = true, ActionId = actionId, Message = "Opened the " + name + " page." };
            }
            if (actionId == AchievementsId)
            {
                var task = TaskButton();
                if (task == null) return new RuntimePerformActionResult { Success = false, ActionId = actionId, Message = "refused: the hub's task button is not on screen" };
                if (Achievements.IsOpen()) return new RuntimePerformActionResult { Success = false, ActionId = actionId, Message = "refused: the achievements window is already open" };
                UiLite.ClickObject(task);
                return new RuntimePerformActionResult { Success = true, ActionId = actionId, Message = "Clicked the achievements button in the hub." };
            }
            var sign = StartSign();
            if (sign == null) return new RuntimePerformActionResult { Success = false, ActionId = actionId, Message = "the hub is not on screen" };
            UiLite.ClickObject(sign.gameObject);
            return new RuntimePerformActionResult { Success = true, ActionId = actionId, Message = "Clicked the start-game sign in the hub." };
        }
    }
}
