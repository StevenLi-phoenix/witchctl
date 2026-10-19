using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Witch.UI;
using Witch.UI.Automation;
using Witch.UI.Window;

namespace WitchCtl
{
    /// <summary>
    /// One-call snapshot for the agent: phase and legal actions plus, during a battle, the numbers needed to pick a
    /// move. All text is the game's own rendering with placeholders resolved and rich text stripped (see <see cref="Text"/>).
    /// <c>?full=1</c> adds the raw config rows the game keeps behind each card.
    /// </summary>
    internal static class StateReader
    {
        internal static object Read(bool full = false)
        {
            var state = ReadPhase(full);
            Tutorial.Attach(state);
            AttachRun(state);
            return state;
        }

        /// <summary>`run`: gold and the card counts of the run in progress (outside a fight, where an agent decides what to do with them).
        /// A full backpack is why card rewards get refused; a deck below its maximum has room for the spare cards.</summary>
        private static void AttachRun(Dictionary<string, object> state)
        {
            var role = RoleTable.Instance;
            if (role == null || state.TryGetValue("phase", out var phase) && (phase as string) == "battle" || role.cardList == null) return;
            try
            {
                state["run"] = new
                {
                    gold = (int)role.Money,
                    inDeck = role.cardList.Count, minimum = role.CardBottomCount, maximum = role.CardTopCount,
                    backpack = role.UnCardList?.Count ?? 0, backpackMax = role.MaxAlCardCount,
                    relics = role.relicList?.Count ?? 0, relicSlots = Shops.RelicSlots,
                    attributes = role.VarsMap,   // MaxAlCardCount = Wisdom / 2 + 2: a negative Wisdom leaves no room and the game refuses every card pick
                };
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning("[WitchCtl] run summary failed: " + e.Message); }
        }

        private static Dictionary<string, object> ReadPhase(bool full)
        {
            var state = new Dictionary<string, object>
            {
                ["frame"] = Time.frameCount,
                ["scene"] = SceneManager.GetActiveScene().name,
                ["windows"] = UiLite.WindowNames(),
            };

            var screen = Screens.Detect();
            if (screen != null)
            {
                if (screen.Phase != "deck" && screen.Phase != "modal") Deck.AddOpenAction(screen.Actions);
                screen.WriteTo(state);
                if (screen.NeedsUi) state["ui"] = UiLite.Dump();
                return state;
            }

            if (Battle.IsActive())
            {
                var hand = Battle.EvaluateHand();
                state["phase"] = "battle";
                state["actions"] = Battle.Actions(hand);
                state["battle"] = ReadBattle(hand, full);
                return state;
            }

            var legal = RuntimeGameplayAutomationService.GetLegalActions();
            state["phase"] = legal.Phase;
            var legalActions = new List<RuntimeLegalAction>(WithheldUntilReady(legal, out var note));
            Deck.AddOpenAction(legalActions);
            state["actions"] = legalActions;
            if (note != null) state["note"] = note;
            if (legal.Phase == "map_select") MapNodes.Attach(state);
            if (legal.Phase == "game_entry")
            {
                GameEntry.Attach(state);
                legalActions.AddRange(GameEntry.Actions());
            }
            if (legal.Phase == "unknown" && Hub.Detect())
            {
                state["phase"] = "hub";
                state["actions"] = Hub.Actions();
                Hub.Attach(state);
                return state;
            }
            if (legal.Phase == "unknown" && Rest.Detect())
            {
                state["phase"] = "rest";
                state["actions"] = Rest.Actions();
                Rest.Attach(state);
                return state;
            }
            if (legal.Phase == "unknown" && SlotMode.Detect())
            {
                state["phase"] = "slot";
                state["actions"] = SlotMode.Actions();
                SlotMode.Attach(state);
                return state;
            }
            // The game only models a few phases; for the rest (shops, events, ...) the UI is the truth.
            if (legal.Phase == "unknown") state["ui"] = UiLite.Dump();
            return state;
        }

        /// <summary>Phase + actions only (cheap), same precedence as <see cref="Read"/>.</summary>
        internal static object ReadActions()
        {
            string phase;
            List<RuntimeLegalAction> actions;
            var screen = Screens.Detect();
            if (screen != null)
            {
                phase = screen.Phase; actions = screen.Actions;
                if (phase != "deck" && phase != "modal") Deck.AddOpenAction(actions);
            }
            else if (Battle.IsActive()) { phase = "battle"; actions = Battle.Actions(Battle.EvaluateHand()); }
            else
            {
                var legal = RuntimeGameplayAutomationService.GetLegalActions();
                phase = legal.Phase;
                actions = new List<RuntimeLegalAction>(WithheldUntilReady(legal, out _));
                if (legal.Phase == "map_select") actions = actions.Concat(MapNodes.Actions()).ToList();
                if (legal.Phase == "game_entry") actions = actions.Concat(GameEntry.Actions()).ToList();
                Deck.AddOpenAction(actions);
                if (legal.Phase == "unknown" && Hub.Detect()) { phase = "hub"; actions = Hub.Actions(); }
                else if (legal.Phase == "unknown" && Rest.Detect()) { phase = "rest"; actions = Rest.Actions(); }
                else if (legal.Phase == "unknown" && SlotMode.Detect()) { phase = "slot"; actions = SlotMode.Actions(); }
            }
            var shown = Tutorial.Visible();
            if (shown.Count > 0) actions = new List<RuntimeLegalAction> { Tutorial.Action(shown[0]) }.Concat(actions).ToList();
            return new { Phase = phase, Actions = actions };
        }

        /// <summary>The game's legal actions minus the ones that would silently do nothing yet (see <see cref="MapGate"/>).</summary>
        private static List<RuntimeLegalAction> WithheldUntilReady(RuntimeLegalActionsSnapshot legal, out string note)
        {
            note = null;
            var actions = legal.Actions ?? new List<RuntimeLegalAction>();
            if (legal.Phase != "map_select" || !actions.Any(a => a.Id == "map_continue")) return actions;
            note = MapGate.NotReady();
            return note == null ? actions : actions.Where(a => a.Id != "map_continue").ToList();
        }

        private static object ReadBattle(List<Battle.CardView> hand, bool full)
        {
            var battle = new Dictionary<string, object>();
            Section(battle, "player", ReadPlayer);
            Section(battle, "enemies", () => ReadEnemies(full));
            Section(battle, "hand", () => hand.Select(v => Card(v, full)).ToList());
            Section(battle, "skills", Skills.Read);       // career skills: cooldown, usable, aimed
            Section(battle, "select", Battle.ReadSelect);   // null unless a card is asking the player to pick hand cards
            Section(battle, "flow", ReadFlow);
            return battle;
        }

        // A broken section must not take the whole snapshot down.
        private static void Section(Dictionary<string, object> into, string name, Func<object> read)
        {
            try { into[name] = read(); }
            catch (Exception e)
            {
                into[name + "Error"] = e.GetType().Name + ": " + e.Message;
                UnityEngine.Debug.LogWarning("[WitchCtl] state section '" + name + "' failed: " + e);
            }
        }

        private static object ReadPlayer()
        {
            var player = FightPlayer.Instance;
            if (player == null) return null;
            return new
            {
                unit = Unit(player.Status),
                powerCount = player.CurPowerCount,
                maxPowerCount = player.MaxPowerCount,
                totalCost = FightPlayer.TotalCost,
                piles = Piles(),
            };
        }

        /// <summary>Draw and discard pile sizes: the hand shrinks to nothing when the draw pile runs dry, which looks like a bug without them.</summary>
        private static object Piles()
        {
            try
            {
                var cards = FightCardManager.Instance;
                return new { draw = cards.cardList.Count, discard = cards.usedCardList.Count };
            }
            catch (Exception) { return null; }
        }

        private static object ReadEnemies(bool full)
        {
            return Battle.Enemies().Select((enemy, index) =>
            {
                var intent = enemy.FightAction?.CaptureIntent();
                var status = enemy.Status as StatusManager;
                var shape = new Dictionary<string, object>
                {
                    ["index"] = index,
                    ["id"] = enemy.Id,
                    ["alive"] = Battle.IsSelectable(status),
                    ["unit"] = Unit(enemy.Status),
                    ["intents"] = intent?.CardIds?.Select(id => Intent(enemy, id)).ToList(),
                };
                if (full) shape["intentRaw"] = intent;
                return shape;
            }).ToList();
        }

        /// <summary>One planned enemy action. The enemy's live action card carries the real numbers (what the intent bubble shows);
        /// the config row only has {0}-style placeholders.</summary>
        private static object Intent(Enemy enemy, string id)
        {
            var live = enemy.ActionCards?.FirstOrDefault(c => c.dataConfig?.data != null && c.dataConfig.data.TryGetValue("Id", out var cid) && cid == id);
            if (live != null)
                return new { id, name = Rich.NameOf(live.dataConfig), text = Rich.Describe(live.dataConfig), cooldown = live.nowCD };
            var row = Globals.GetDataBydId?.Invoke(id);
            return new { id, name = row == null ? null : Rich.Strip(row.Localize("Name")), text = row == null ? null : Rich.Strip(row.Localize("Description")), cooldown = (int?)null };
        }

        private static object Card(Battle.CardView v, bool full)
        {
            var config = v.Card.dataConfig;
            var data = config?.data;
            string Field(string key) => data != null && data.TryGetValue(key, out var value) ? value : null;
            var card = new Dictionary<string, object>
            {
                ["index"] = v.Index,
                ["uid"] = config?.InstanceID,
                ["id"] = Field("Id"),
                ["name"] = Rich.NameOf(config),
                ["kind"] = v.Card.GetType().Name,
                ["type"] = Field("Type"),
                ["rarity"] = Field("Rarity"),
                ["cost"] = v.Cost,
                ["tags"] = v.Card.Tags?.Where(t => !string.IsNullOrEmpty(t)).ToList(),
                ["targets"] = v.Card is AttackCardItem ? (Battle.CanEnemy(v.Card) ? (Battle.CanSelf(v.Card) ? "enemy|self" : "enemy") : (Battle.CanSelf(v.Card) ? "self" : "none")) : null,
                ["playable"] = v.Playable,
                ["reason"] = v.Playable ? null : v.Reason,
                ["text"] = Rich.Describe(config),
            };
            if (Battle.Selecting()) card["chosen"] = Battle.IsChosen(v.Card);
            if (full)
            {
                card["data"] = data;
                card["vars"] = v.Card.Vars;
            }
            return card;
        }

        private static object ReadFlow()
        {
            var fightUi = UIManager.Instance?.GetUI<FightUI>("FightUI");
            return new
            {
                animating = fightUi != null && fightUi.NowAnimation,
                fightType = FightManager.Instance?.fightType.ToString(),
                canAct = Battle.PlayerWindow(out _),
                canUseCards = CardItem.canUse,
                busy = FightUI.InIEn,
                dealing = Battle.Dealing(),
                // A scripted fight: the player is revived at full HP instead of losing, so a defeat never ends it.
                fake = FightManager.Instance != null && FightManager.Instance.IsFake,
                lastPlay = Battle.LastPlay,
            };
        }

        internal static object Unit(IStatusManager status)
        {
            if (status == null) return null;
            return new
            {
                name = status.Name,
                // The game lets HP go below zero until its second death pass; the number an agent wants is never negative.
                hp = Math.Max(0, status.CurHp),
                maxHp = status.MaxHp,
                defend = status.Defend,
                dead = status.state == IStatusManager.State.Dead || status.CurHp <= 0,
                state = status.state.ToString(),
                buffs = Buffs(status),
            };
        }

        private static object Buffs(IStatusManager status)
        {
            try
            {
                return status.GetBuffs()?
                    .Where(b => b?.buffConfig != null)
                    .Select(b => new { id = b.buffConfig.BuffId, name = Rich.Strip(b.buffConfig.BuffName), level = b.buffConfig.Level })
                    .ToList();
            }
            catch (Exception) { return new object[0]; }
        }
    }
}
