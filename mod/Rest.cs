using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Witch.UI;
using Witch.UI.Automation;
using Witch.UI.Window;
using UIManager = Witch.UI.UIManager;

namespace WitchCtl
{
    /// <summary>
    /// The rest room (<c>GameApp.StartBreaks</c> instantiates the prefab "Breaks", node "Break-Night") is a world scene, not a UI window:
    /// food sprites on a table (<c>FoodItem</c>, a Button over a PolygonCollider2D), a safe box, and an arrow button (<c>CloseRole</c>) that
    /// leaves. The game's phase model reports `unknown` there and its window list is empty (only Chat/Caption/TopBar), so an agent
    /// had no action at all (user screenshot: "商店卡住"). Reported as phase <c>rest</c> with <c>rest_eat_N</c> (what a click on a food
    /// does: <c>FoodItem.EatFood</c>) and <c>rest_leave</c> (<c>BreaksUI.Close</c>, the arrow's click handler). The safe box and the
    /// warehouse (multiplayer) are not modelled.
    /// </summary>
    internal static class Rest
    {
        private const string EatPrefix = "rest_eat_";
        internal const string LeaveId = "rest_leave";
        internal const string SafeBoxId = "rest_safebox";

        private static readonly FieldInfo HasClosed = typeof(BreaksUI).GetField("hasClosed", BindingFlags.NonPublic | BindingFlags.Instance);

        internal sealed class Food
        {
            public int Index;
            public Witch.FoodItem Item;
            public string Id;
            public string Name;
            public int HealPercent;
            public int MaxHpUp;
        }

        private static BreaksUI Room()
        {
            var room = UnityEngine.Object.FindObjectOfType<BreaksUI>();
            return room != null && room.gameObject.activeInHierarchy ? room : null;
        }

        /// <summary>
        /// True when the rest room is what is on screen: the room exists and no window of the game's own is open above it (an event or a
        /// shop over the room stays `unknown` with the `ui` dump). Only call when the game's phase is `unknown`.
        /// </summary>
        internal static bool Detect() => Room() != null && UiLite.OnlyAmbientWindows();

        internal static bool Owns(string actionId) =>
            actionId != null && (actionId == LeaveId || actionId == SafeBoxId || actionId.StartsWith(EatPrefix, StringComparison.Ordinal));

        private static int Number(IDataConfig config, string key) =>
            config?.data != null && config.data.TryGetValue(key, out var text) && int.TryParse(text, out var n) ? n : 0;

        /// <summary>The food table has no text of its own (the localized name is "丢失的文本"); the sprite's file name is the dish (大汉堡, 蛋糕, ...).</summary>
        private static string NameOf(IDataConfig config)
        {
            var named = Rich.NameOf(config);
            if (!string.IsNullOrEmpty(named) && !named.Contains("丢失")) return named;
            return config?.data != null && config.data.TryGetValue("Icon", out var icon) && !string.IsNullOrEmpty(icon) ? icon.Substring(icon.LastIndexOf('/') + 1) : null;
        }

        internal static List<Food> Foods(BreaksUI room)
        {
            var foods = new List<Food>();
            foreach (var item in room.GetComponentsInChildren<Witch.FoodItem>(false))
            {
                if (item == null || item.dataConfig == null) continue;   // not initialised yet (Start has not run)
                foods.Add(new Food
                {
                    Index = foods.Count,
                    Item = item,
                    Id = item.dataConfig.data != null && item.dataConfig.data.TryGetValue("Id", out var id) ? id : null,
                    Name = NameOf(item.dataConfig),
                    HealPercent = Number(item.dataConfig, "Hp"),
                    MaxHpUp = Number(item.dataConfig, "HPPercent") * 2,
                });
            }
            return foods;
        }

        /// <summary>Why leaving would do nothing right now (the room is already closing), else null.</summary>
        private static string Closing(BreaksUI room)
        {
            if (HasClosed != null && (bool)HasClosed.GetValue(room)) return "the room is closing: the next scene is loading";
            if (UIManager.Instance?.GetUI<CurtainTurnUI>("CurtainTurnUI") != null) return "a scene transition is running; wait a few seconds";
            return null;
        }

        /// <summary>Adds the room (<c>rest</c>) to a snapshot; the actions are <see cref="Actions"/>.</summary>
        internal static void Attach(Dictionary<string, object> state)
        {
            var room = Room();
            if (room == null) return;
            try
            {
                var closing = Closing(room);
                var role = RoleTable.Instance;
                state["rest"] = new
                {
                    hp = role?.San,
                    maxHp = role?.MaxSan,
                    food = Foods(room).Select(f => new { index = f.Index, id = f.Id, name = f.Name, healPercentOfHp = f.HealPercent, maxHpUp = f.MaxHpUp }).ToList(),
                };
                if (closing != null) state["note"] = closing;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("[WitchCtl] rest room scan failed: " + e);
            }
        }

        internal static List<RuntimeLegalAction> Actions()
        {
            var found = new List<RuntimeLegalAction>();
            var room = Room();
            if (room == null || Closing(room) != null) return found;
            foreach (var food in Foods(room))
                found.Add(new RuntimeLegalAction
                {
                    Id = EatPrefix + food.Index,
                    Kind = "rest_eat",
                    Label = food.Name,
                    Description = "Eat it: heals " + food.HealPercent + "% of current HP + 5 and raises max HP by " + food.MaxHpUp + ". It is gone afterwards.",
                });
            found.Add(new RuntimeLegalAction { Id = SafeBoxId, Kind = "rest_safebox", Label = "保险箱", Description = "Open the safe box: it hands cards and relics kept in the profile (hand-made cards, stored relics) to this run's backpack. The window is then `SafeBoxUI`." });
            found.Add(new RuntimeLegalAction { Id = LeaveId, Kind = "rest_leave", Label = "离开", Description = "Leave the rest room for the next node of the route." });
            return found;
        }

        internal static RuntimePerformActionResult Perform(string actionId)
        {
            string refusal = null, message = null;
            var room = Room();
            if (room == null) refusal = "the rest room is not on screen";
            else if (Closing(room) is string closing) refusal = closing;
            else if (actionId == LeaveId)
            {
                room.Close();
                message = "Leaving the rest room.";
            }
            else if (actionId == SafeBoxId)
            {
                room.RelicBackPoint();
                message = "Opened the safe box.";
            }
            else if (!int.TryParse(actionId.Substring(EatPrefix.Length), out var index)) refusal = "bad food index";
            else
            {
                var food = Foods(room).FirstOrDefault(f => f.Index == index);
                var role = RoleTable.Instance;
                if (food == null) refusal = "no food " + index + " on the table";
                else if (role == null) refusal = "no character data";
                else
                {
                    int hp = role.San, maxHp = role.MaxSan;
                    food.Item.EatFood();
                    // The handler works on the numbers at once: no change means the click was not a meal.
                    if (role.San == hp && role.MaxSan == maxHp) refusal = "eating " + food.Name + " changed nothing";
                    else message = "Ate " + food.Name + ": HP " + hp + " -> " + role.San + ", max HP " + maxHp + " -> " + role.MaxSan + ".";
                }
            }
            if (refusal != null) return new RuntimePerformActionResult { Success = false, ActionId = actionId, Message = "refused: " + refusal };
            UnityEngine.Debug.Log("[WitchCtl] " + actionId + ": " + message);
            return new RuntimePerformActionResult { Success = true, ActionId = actionId, Message = message };
        }
    }
}
