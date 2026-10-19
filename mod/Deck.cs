using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Witch.UI.Automation;
using Witch.UI.Window;
using UIManager = Witch.UI.UIManager;

namespace WitchCtl
{
    /// <summary>
    /// The deck window (<c>OutDeckUI</c>, TopBar button <c>CardBack</c>): the cards in the fighting deck and the backpack of spare ones.
    /// Why it matters: rewards and purchases go to the backpack (<c>RoleTable.UnCardList</c>) and a fight reads only the deck
    /// (<c>cardList</c>), so without moving cards here nothing an agent picks ever reaches a fight, and a full backpack makes the game
    /// refuse further card rewards. Phase <c>deck</c> while the window is open; the action <c>deck_open</c> is offered next to the other
    /// phases' actions whenever the TopBar button is there.
    /// Actions (N = the card's index in the `deck.cards` list): <c>deck_equip_N</c> (backpack → deck), <c>deck_unequip_N</c> (deck → backpack),
    /// <c>deck_sell_N</c> (a shop window is open: +gold), <c>deck_destroy_N</c> (no shop: costs gold), <c>deck_close</c>.
    /// The game does the moving itself (<c>ShowCard.MoveItem / SellItem / DecomposeItem</c>, the right-click menu's own calls); the
    /// limits are checked first (<see cref="DeckRules"/>) so no "limit reached" modal pops up, and the result is read back.
    /// </summary>
    internal static class Deck
    {
        internal const string Open = "deck_open", Close = "deck_close";
        private const string EquipPrefix = "deck_equip_", UnequipPrefix = "deck_unequip_", SellPrefix = "deck_sell_", DestroyPrefix = "deck_destroy_";

        internal static bool Owns(string actionId) =>
            actionId != null && actionId.StartsWith("deck_", StringComparison.Ordinal) && !DeckRules.IsPickerAction(actionId);   // the picker is DeckSelect's

        private static bool Active(UnityEngine.Component c) => c != null && c.gameObject.activeInHierarchy;

        private static OutDeckUI Window()
        {
            var ui = UIManager.Instance?.GetUI<OutDeckUI>("OutDeckUI");
            return Active(ui) ? ui : null;
        }

        private static bool ShopOpen()
        {
            var ui = UIManager.Instance;
            return ui != null && (Active(ui.GetUI<ShopUI>("ShopUI")) || Active(ui.GetUI<DestinyTreeUI>("DestinyTreeUI")) || Active(ui.GetUI<CardEnchUI>("CardEnchUI")));
        }

        // ------------------------------------------------------------------ reading

        private sealed class Entry
        {
            public int Index;
            public ShowCard Card;
            public bool Equipped;
            public string Id, Name, Cost, Rarity, Type, Tags, Text;
            public bool Eternal => Tags.IndexOf("Eternal", StringComparison.Ordinal) >= 0;
            public bool Curse => Type == "诅咒";
        }

        private static string Get(IDictionary<string, string> d, string key) => d != null && d.TryGetValue(key, out var v) ? v : "";

        private static List<Entry> Cards(OutDeckUI ui)
        {
            var found = new List<Entry>();
            foreach (var (zone, equipped) in new[] { (ui.equipCardTransform, true), (ui.unequipCardTransform, false) })
            {
                if (zone == null) continue;
                foreach (var card in zone.GetComponentsInChildren<ShowCard>(false))
                {
                    var config = card.dataConfig;
                    if (config?.data == null) continue;
                    found.Add(new Entry
                    {
                        Index = found.Count,
                        Card = card,
                        Equipped = equipped,
                        Id = Get(config.data, "Id"),
                        Name = Rich.NameOf(config) ?? Get(config.data, "Id"),
                        Cost = Get(config.data, "Expend"),
                        Rarity = Get(config.data, "Rarity"),
                        Type = Get(config.data, "Type"),
                        Tags = Get(config.Vars, "Tag"),
                        Text = Rich.Describe(config),
                    });
                }
            }
            return found;
        }

        private static int Wisdom(RoleTable role) => role.VarsMap != null && role.VarsMap.TryGetValue("Wisdom", out var w) ? w : 0;

        /// <summary>Why this move / removal is not possible right now (null = fine). Mirrors the game's own checks.</summary>
        private static string Refusal(string kind, Entry e, RoleTable role, bool shop)
        {
            int deck = role.cardList.Count;
            switch (kind)
            {
                case "equip": return e.Equipped ? "that card is already in the deck" : DeckRules.Equip(deck, role.CardTopCount);
                case "unequip": return !e.Equipped ? "that card is already in the backpack" : DeckRules.Unequip(deck, role.CardBottomCount, role.UnCardList.Count, role.MaxAlCardCount);
                case "sell": return shop ? DeckRules.Sell(e.Eternal, e.Curse, Wisdom(role), e.Equipped, deck, role.CardBottomCount) : "selling needs a shop window open (use deck_destroy)";
                default: return shop ? "a shop window is open: sell the card instead" : DeckRules.Destroy(e.Eternal, e.Curse, Wisdom(role), e.Equipped, deck, role.CardBottomCount, (int)role.Money, e.Card.DestroyCost);
            }
        }

        /// <summary>The deck window as a screen, or null.</summary>
        internal static Screens.Screen Detect()
        {
            var ui = Window();
            var role = RoleTable.Instance;
            if (ui == null || role == null) return null;
            var screen = new Screens.Screen { Phase = "deck" };
            try
            {
                var shop = ShopOpen();
                var cards = Cards(ui);
                foreach (var e in cards)
                {
                    foreach (var kind in new[] { e.Equipped ? "unequip" : "equip", shop ? "sell" : "destroy" })
                    {
                        var why = Refusal(kind, e, role, shop);
                        if (why != null) continue;
                        var id = (kind == "equip" ? EquipPrefix : kind == "unequip" ? UnequipPrefix : kind == "sell" ? SellPrefix : DestroyPrefix) + e.Index;
                        screen.Actions.Add(new RuntimeLegalAction
                        {
                            Id = id, Kind = "deck_" + kind, Label = e.Name,
                            Description = kind == "equip" ? "Put into the fighting deck." : kind == "unequip" ? "Take out of the deck into the backpack."
                                : kind == "sell" ? "Sell for " + 20 * Math.Max(1, int.TryParse(e.Rarity, out var r) ? r : 1) + " gold (before multipliers)."
                                : "Destroy for " + e.Card.DestroyCost + " gold. Permanent.",
                            Parameters = new Dictionary<string, object> { ["cardId"] = e.Id, ["index"] = e.Index },
                        });
                    }
                }
                screen.Actions.Add(new RuntimeLegalAction { Id = Close, Kind = Close, Label = "关闭", Description = "Close the deck window." });
                screen.Detail["deck"] = new
                {
                    inDeck = role.cardList.Count, minimum = role.CardBottomCount, maximum = role.CardTopCount,
                    backpack = role.UnCardList.Count, backpackMax = role.MaxAlCardCount,
                    gold = (int)role.Money, shopOpen = shop,
                    cards = cards.Select(e => new { index = e.Index, zone = e.Equipped ? "deck" : "backpack", id = e.Id, name = e.Name, cost = e.Cost, rarity = e.Rarity, type = e.Type, tags = e.Tags, text = e.Text }).ToList(),
                };
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[WitchCtl] deck scan failed: " + ex);
                screen.Actions.Clear();
                screen.Actions.Add(new RuntimeLegalAction { Id = Close, Kind = Close, Label = "关闭", Description = "Close the deck window." });
                screen.Detail["deckError"] = ex.GetType().Name + ": " + ex.Message;
            }
            return screen;
        }

        // ------------------------------------------------------------------ opening

        private static Transform OpenButton() => UIManager.Instance?.GetUI<TopBarUI>("TopBarUI")?.transform.Find("Content/Buttons/CardBack");

        /// <summary>`deck_open` for the phases whose window is not itself the deck, when the TopBar button is on screen (never in a fight).</summary>
        internal static void AddOpenAction(List<RuntimeLegalAction> actions)
        {
            if (actions == null || actions.Any(a => a.Id == Open) || Window() != null || Battle.IsActive()) return;
            var button = OpenButton();
            if (button == null || !button.gameObject.activeInHierarchy || Popups.DetectModal() != null) return;
            actions.Add(new RuntimeLegalAction { Id = Open, Kind = Open, Label = "牌组", Description = "Open the deck window (fighting deck and backpack)." });
        }

        // ------------------------------------------------------------------ doing things

        internal static RuntimePerformActionResult Perform(string actionId)
        {
            string refusal = null, message = null;
            var ui = Window();
            var role = RoleTable.Instance;
            if (actionId == Open)
            {
                var button = OpenButton();
                if (ui != null) refusal = "the deck window is already open";
                else if (button == null || !button.gameObject.activeInHierarchy) refusal = "the deck button is not on screen";
                else if (Battle.IsActive()) refusal = "not during a fight";
                else if (Popups.DetectModal() != null) refusal = "answer the open window first";
                else { UiLite.ClickObject(button.gameObject); message = "Opened the deck window."; }
            }
            else if (ui == null || role == null) refusal = "the deck window is not open";
            else if (actionId == Close)
            {
                UIManager.Instance.CloseUI("OutDeckUI");
                message = "Closed the deck window.";
            }
            else
            {
                var (kind, text) = actionId.StartsWith(EquipPrefix, StringComparison.Ordinal) ? ("equip", actionId.Substring(EquipPrefix.Length))
                    : actionId.StartsWith(UnequipPrefix, StringComparison.Ordinal) ? ("unequip", actionId.Substring(UnequipPrefix.Length))
                    : actionId.StartsWith(SellPrefix, StringComparison.Ordinal) ? ("sell", actionId.Substring(SellPrefix.Length))
                    : actionId.StartsWith(DestroyPrefix, StringComparison.Ordinal) ? ("destroy", actionId.Substring(DestroyPrefix.Length)) : (null, null);
                var entry = kind == null || !int.TryParse(text, out var index) ? null : Cards(ui).FirstOrDefault(c => c.Index == index);
                if (entry == null) refusal = "no such card in the deck window";
                else if ((refusal = Refusal(kind, entry, role, ShopOpen())) == null)
                    message = Apply(kind, entry, role, ref refusal);
            }
            if (refusal != null) return new RuntimePerformActionResult { Success = false, ActionId = actionId, Message = "refused: " + refusal };
            UnityEngine.Debug.Log("[WitchCtl] " + actionId + ": " + message);
            return new RuntimePerformActionResult { Success = true, ActionId = actionId, Message = message };
        }

        /// <summary>Calls the game's own method and checks the card lists afterwards: a call that returned is not proof it did anything.</summary>
        private static string Apply(string kind, Entry e, RoleTable role, ref string refusal)
        {
            int deck = role.cardList.Count, backpack = role.UnCardList.Count, gold = (int)role.Money;
            switch (kind)
            {
                case "equip": case "unequip": e.Card.MoveItem(); break;
                case "sell": e.Card.SellItem(); break;
                default: e.Card.DecomposeItem(); break;
            }
            int deckAfter = role.cardList.Count, backpackAfter = role.UnCardList.Count, goldAfter = (int)role.Money;
            bool changed = kind == "equip" ? deckAfter == deck + 1 && backpackAfter == backpack - 1
                         : kind == "unequip" ? deckAfter == deck - 1 && backpackAfter == backpack + 1
                         : deckAfter + backpackAfter == deck + backpack - 1;
            if (!changed)
            {
                refusal = "the game did not " + kind + " it (deck " + deck + "→" + deckAfter + ", backpack " + backpack + "→" + backpackAfter + ")";
                return null;
            }
            return kind + ": " + e.Name + " (deck " + deck + "→" + deckAfter + ", backpack " + backpack + "→" + backpackAfter
                   + (goldAfter != gold ? ", gold " + gold + "→" + goldAfter : "") + ")";
        }
    }
}
