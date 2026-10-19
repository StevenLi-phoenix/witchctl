using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using Cysharp.Threading.Tasks;
using Witch.UI.Automation;
using Witch.UI.Window;
using UIManager = Witch.UI.UIManager;

namespace WitchCtl
{
    /// <summary>
    /// Every sixth map the player builds the route: cards (fights, shops, events, ...) sit in a tray and are dragged into the empty node
    /// frames; only a full route can be started. The game's phase model knows none of that (it offers `map_continue` regardless, and
    /// the click then only shows "请填满所有节点"). A human drags a card with a held pointer; an agent has no pointer, so
    /// <c>map_place_N</c> does what the game does when the pointer is released over a frame: <c>MapItem.AddToList(frame)</c>, then
    /// <c>SetNodes()</c> (the route data) and the tray re-layout (see <c>MapItem.RayCheck</c>). Cards go into the first empty frame,
    /// in route order; there is no un-place (the game has one, by dragging the card back, which we do not model).
    /// </summary>
    internal static class MapNodes
    {
        private const string PlacePrefix = "map_place_";

        internal sealed class Slot
        {
            public int Index;
            public SwapContentIdentity Frame;
            public bool Empty;
            public string Holds;
            public string Kind;   // Fight / Event / Shop / ... from the prefab's name
        }

        internal sealed class Choice
        {
            public int Index;
            public MapItem Item;
            public string Kind;
            public string Id;
            public string Label;
        }

        private static MapSelectUI Map()
        {
            var map = UIManager.Instance?.GetUI<MapSelectUI>("MapSelectUI");
            return map != null && map.gameObject.activeInHierarchy ? map : null;
        }

        internal static bool Owns(string actionId) => actionId != null && actionId.StartsWith(PlacePrefix, StringComparison.Ordinal);

        private static string LabelOf(MapItem item) =>
            string.Join(" ", item.GetComponentsInChildren<TMP_Text>(false).Select(t => Rich.Strip(t.text)).Where(t => t.Length > 0).Distinct());

        /// <summary>The route's frames in play order (the fixed start and end frames carry no <c>SwapContentIdentity</c> and are not listed).</summary>
        internal static List<Slot> Slots(MapSelectUI map)
        {
            var slots = new List<Slot>();
            var nodes = map.transform.Find("Map/NodeContent");
            if (nodes == null) return slots;
            foreach (Transform child in nodes)
            {
                var frame = child.GetComponent<SwapContentIdentity>();
                if (frame == null || frame.Content == null) continue;
                var held = frame.Content.GetComponentInChildren<MapItem>(false);
                var marker = frame.Content.Find("Null");
                slots.Add(new Slot
                {
                    Index = slots.Count,
                    Frame = frame,
                    Empty = held == null && marker != null && marker.gameObject.activeSelf,
                    Holds = held == null ? null : LabelOf(held),
                    Kind = held == null ? null : MapRules.KindOf(held.name),
                });
            }
            return slots;
        }

        /// <summary>The cards still in the tray.</summary>
        internal static List<Choice> Choices(MapSelectUI map)
        {
            var tray = map.transform.Find("MapSelect");
            var choices = new List<Choice>();
            if (tray == null) return choices;
            foreach (Transform child in tray)
            {
                var item = child.GetComponent<MapItem>();
                if (item == null || !child.gameObject.activeSelf || item.hasSelected || item.name == "Null") continue;
                var data = item.node?.data;
                choices.Add(new Choice
                {
                    Index = choices.Count,
                    Item = item,
                    Kind = data != null && data.TryGetValue("Type", out var kind) ? kind : null,
                    Id = data != null && data.TryGetValue("NodeId", out var id) ? id : null,
                    Label = LabelOf(item),
                });
            }
            return choices;
        }

        internal static int EmptyCount(MapSelectUI map) => Slots(map).Count(s => s.Empty);

        private static bool Busy(MapSelectUI map) => map.IsAnimating || MapGate.Closing(map);

        /// <summary>The fixed first / last frame of the route (they carry no <c>SwapContentIdentity</c>, so <see cref="Slots"/> skips them).</summary>
        private static object FixedFrame(MapSelectUI map, string name)
        {
            var content = map.transform.Find("Map/NodeContent/" + name + "/Content");
            var held = content == null ? null : content.GetComponentInChildren<MapItem>(false);
            return held == null ? null : new { kind = MapRules.KindOf(held.name), holds = LabelOf(held) };
        }

        /// <summary>`map`: which map this is (the game's level counter), who builds its route, and the fixed first and last stops.</summary>
        private static void AttachRoute(Dictionary<string, object> state, MapSelectUI map)
        {
            var level = MapManager.Instance?.Level ?? -1;
            state["map"] = new
            {
                level, playerBuilds = MapRules.PlayerBuilds(level), positionInCycle = level >= 0 ? level % MapRules.Cycle : -1,
                start = FixedFrame(map, "Start"), end = FixedFrame(map, "End"),
            };
        }

        /// <summary>Adds the route (<c>mapNodes</c>, <c>mapChoices</c>) and the placement actions to a map_select snapshot.</summary>
        internal static void Attach(Dictionary<string, object> state)
        {
            var map = Map();
            if (map == null) return;
            try
            {
                var slots = Slots(map);
                var choices = Choices(map);
                AttachRoute(state, map);
                if (slots.Count == 0) return;
                state["mapNodes"] = slots.Select(s => new { index = s.Index, empty = s.Empty, kind = s.Kind, holds = s.Holds }).ToList();
                if (choices.Count > 0)
                    state["mapChoices"] = choices.Select(c => new { index = c.Index, kind = c.Kind, id = c.Id, label = c.Label }).ToList();
                if (state.TryGetValue("actions", out var actions) && actions is List<RuntimeLegalAction> list)
                    list.AddRange(Actions(map, slots, choices));
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("[WitchCtl] map route scan failed: " + e);
            }
        }

        internal static List<RuntimeLegalAction> Actions(MapSelectUI map, List<Slot> slots, List<Choice> choices)
        {
            var found = new List<RuntimeLegalAction>();
            var next = slots.FirstOrDefault(s => s.Empty);
            if (next == null || Busy(map)) return found;
            foreach (var choice in choices)
                found.Add(new RuntimeLegalAction
                {
                    Id = PlacePrefix + choice.Index,
                    Kind = "map_place",
                    Label = choice.Label,
                    Description = "Put this card into route node " + next.Index + " (the first empty one).",
                });
            return found;
        }

        internal static List<RuntimeLegalAction> Actions()
        {
            var map = Map();
            return map == null ? new List<RuntimeLegalAction>() : Actions(map, Slots(map), Choices(map));
        }

        internal static RuntimePerformActionResult Perform(string actionId)
        {
            string refusal = null, message = null;
            var map = Map();
            if (map == null) refusal = "the map is not open";
            else if (!int.TryParse(actionId.Substring(PlacePrefix.Length), out var index)) refusal = "bad card index";
            else if (Busy(map)) refusal = "the map is busy (animating or closing)";
            else
            {
                var slots = Slots(map);
                var slot = slots.FirstOrDefault(s => s.Empty);
                var choice = Choices(map).FirstOrDefault(c => c.Index == index);
                if (slot == null) refusal = "every node is already filled";
                else if (choice == null) refusal = "no card " + index + " in the tray";
                else if (!Place(map, choice.Item, slot)) refusal = "the game refused the placement";
                else message = "Placed " + choice.Label + " into node " + slot.Index + "; " + EmptyCount(map) + " empty left.";
            }
            if (refusal != null) return new RuntimePerformActionResult { Success = false, ActionId = actionId, Message = "refused: " + refusal };
            UnityEngine.Debug.Log("[WitchCtl] " + actionId + ": " + message);
            return new RuntimePerformActionResult { Success = true, ActionId = actionId, Message = message };
        }

        /// <summary>What <c>MapItem.RayCheck</c> does after the pointer is released over a frame, minus the tweens.</summary>
        private static bool Place(MapSelectUI map, MapItem item, Slot slot)
        {
            var before = item.transform.parent;
            item.lastParent = before;
            item.lastPos = item.initPosition;
            item.AddToList(slot.Frame);
            if (item.transform.parent == before) return false;

            map.SetNodes();
            item.hasSelected = true;
            var blocks = item.GetComponent<ObjectGroup>();
            if (blocks != null) blocks.blocksRaycasts = true;
            var sorting = item.GetComponent<SortingGroup>();
            if (sorting != null) sorting.sortingOrder = -20;
            UniTask.WaitForEndOfFrame().ContinueWith(delegate { map.UpdateCardItemPos(); }).Forget();
            return true;
        }
    }
}
