using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using Witch.UI.Automation;
using Witch.UI.Window;
using UIManager = Witch.UI.UIManager;

namespace WitchCtl
{
    /// <summary>
    /// The card picker (<c>DeckUI</c> in select mode, <c>CreateDeckMenuForSelect</c>): "choose N cards" for an enchantment, a story event that
    /// copies or removes a card, and so on. The same window also shows the draw / discard pile during a fight; only its third page,
    /// <c>Windows/新牌堆</c>, is the picker. It has no confirm button: the window closes by itself when N cards are chosen. Clicking a card toggles
    /// it. The game does NOT lock the fight while it is open (the end-turn button stays live), so this outranks the battle phase.
    /// Phase <c>deck_select</c>, actions <c>deck_pick_N</c> (toggle) and, while nothing is chosen, <c>deck_select_cancel</c>.
    /// The choice is made by clicking the card's button the way a player does and read back from <c>DisplayCard.isSelect</c>.
    /// </summary>
    internal static class DeckSelect
    {
        private const string PickPrefix = DeckRules.PickPrefix, CancelId = DeckRules.PickCancelId;
        private const string PagePath = "Window Manager/Windows/新牌堆";

        internal static bool Owns(string actionId) => DeckRules.IsPickerAction(actionId);

        private static DeckUI Ui()
        {
            var ui = UIManager.Instance?.GetUI<DeckUI>("DeckUI");
            if (ui == null || !ui.gameObject.activeInHierarchy) return null;
            var page = ui.transform.Find(PagePath);
            return page != null && page.gameObject.activeInHierarchy ? ui : null;
        }

        internal static bool IsOpen() => Ui() != null;

        private sealed class Entry
        {
            public int Index;
            public Transform Slot;
            public DisplayCard Card;
            public bool Chosen;
            public string Id, Name, Cost, Text, Tags;
        }

        private static List<Entry> Cards(DeckUI ui)
        {
            var found = new List<Entry>();
            var list = ui.transform.Find(PagePath + "/Content/List View Custom/Scroll Area/List");
            if (list == null) return found;
            foreach (Transform slot in list)
            {
                if (!slot.gameObject.activeInHierarchy) continue;   // the template "card" is switched off
                var card = slot.GetComponentInChildren<DisplayCard>(false);
                var config = card?.dataConfig;
                if (config?.data == null) continue;
                config.data.TryGetValue("Id", out var id);
                config.data.TryGetValue("Expend", out var cost);
                string tags = null;
                config.Vars?.TryGetValue("Tag", out tags);
                found.Add(new Entry { Index = found.Count, Slot = slot, Card = card, Chosen = card.isSelect, Id = id, Name = Rich.NameOf(config) ?? id, Cost = cost, Text = Rich.Describe(config), Tags = tags ?? "" });
            }
            return found;
        }

        /// <summary>How many cards are still to be chosen: the first number in the page's caption ("choose 2 cards", later "N more").</summary>
        private static int Remaining(DeckUI ui)
        {
            var text = ui.transform.Find(PagePath + "/Content/Description")?.GetComponent<TMP_Text>()?.text ?? "";
            var m = Regex.Match(text, @"\d+");
            return m.Success && int.TryParse(m.Value, out var n) ? n : -1;
        }

        internal static Screens.Screen Detect()
        {
            var ui = Ui();
            if (ui == null) return null;
            var screen = new Screens.Screen { Phase = "deck_select" };
            try
            {
                var cards = Cards(ui);
                var chosen = cards.Count(c => c.Chosen);
                foreach (var c in cards)
                    screen.Actions.Add(new RuntimeLegalAction
                    {
                        Id = PickPrefix + c.Index, Kind = "deck_pick", Label = c.Name,
                        Description = (c.Chosen ? "Take back the choice of this card. " : "Choose this card. ") + "cost " + c.Cost + " — " + c.Text,
                        Parameters = new Dictionary<string, object> { ["cardId"] = c.Id, ["chosen"] = c.Chosen },
                    });
                if (chosen == 0)
                    screen.Actions.Add(new RuntimeLegalAction { Id = CancelId, Kind = CancelId, Label = "取消", Description = "Close the picker without choosing (an enchantment is then not bought)." });
                screen.Detail["select"] = new
                {
                    remaining = Remaining(ui), chosen,
                    caption = Rich.Strip(ui.transform.Find(PagePath + "/Content/Description")?.GetComponent<TMP_Text>()?.text),
                    cards = cards.Select(c => new { index = c.Index, id = c.Id, name = c.Name, cost = c.Cost, tags = c.Tags, text = c.Text, chosen = c.Chosen }).ToList(),
                };
                screen.Detail["note"] = "the picker closes by itself when the last card is chosen; the fight (if any) is not paused behind it";
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[WitchCtl] card picker scan failed: " + ex);
                screen.Detail["selectError"] = ex.GetType().Name + ": " + ex.Message;
                screen.NeedsUi = true;
            }
            return screen;
        }

        internal static RuntimePerformActionResult Perform(string actionId)
        {
            string refusal = null, message = null;
            var ui = Ui();
            if (ui == null) refusal = "the card picker is not open";
            else if (actionId == CancelId)
            {
                var cards = Cards(ui);
                if (cards.Any(c => c.Chosen)) refusal = "cards are already chosen: take them back first (deck_pick_N toggles)";
                else { UIManager.Instance.CloseUI("DeckUI"); message = "Closed the card picker without choosing."; }
            }
            else if (!int.TryParse(actionId.Substring(PickPrefix.Length), out var index)) refusal = "bad card index";
            else
            {
                var entry = Cards(ui).FirstOrDefault(c => c.Index == index);
                if (entry == null) refusal = "no card " + index + " in the picker";
                else
                {
                    bool before = entry.Chosen;
                    UiLite.ClickObject(entry.Slot.gameObject);
                    // The last pick closes the window within the same click: gone = chosen.
                    bool closed = Ui() == null;
                    if (!closed && entry.Card.isSelect == before) refusal = "the click did not " + (before ? "unselect" : "select") + " the card";
                    else message = closed ? "Chose " + entry.Name + "; that was the last one, the picker closed." : (before ? "Took back " : "Chose ") + entry.Name + ".";
                }
            }
            if (refusal != null) return new RuntimePerformActionResult { Success = false, ActionId = actionId, Message = "refused: " + refusal };
            UnityEngine.Debug.Log("[WitchCtl] " + actionId + ": " + message);
            return new RuntimePerformActionResult { Success = true, ActionId = actionId, Message = message };
        }
    }
}
