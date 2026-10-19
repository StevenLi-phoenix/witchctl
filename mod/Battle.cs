using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using Witch.UI;
using Witch.UI.Automation;
using Witch.UI.Window;

namespace WitchCtl
{
    /// <summary>
    /// Side-effect-free battle logic.
    ///
    /// The game's own RuntimeGameplayAutomationService cannot be used while a battle is on: its legality probe
    /// (RuntimeBattleAutomationService.CanPlayCardLegally) reflects into CommonCardItem.TryUse, which is not a
    /// check but the real thing: it spends energy, moves the card out of the hand and fires events. Every state
    /// read would play the hand. So during a battle we apply the same conditions TryUse/UseChecker use, read-only.
    /// </summary>
    internal static class Battle
    {
        internal sealed class CardView
        {
            public int Index;
            public CardItem Card;
            public bool Playable;
            public string Reason;
            public int Cost;
        }

        // Private helper of the game's service that wires an attack card to its target; reused as-is.
        private static readonly MethodInfo PrepareDirectedUse =
            typeof(RuntimeBattleAutomationService).GetMethod("PrepareDirectedCardUse", BindingFlags.NonPublic | BindingFlags.Static);

        private static FightUI FightWindow => UIManager.Instance?.GetUI<FightUI>("FightUI");

        /// <summary>True when GetLegalActions would take its battle branch (same order of checks, so same precedence).</summary>
        internal static bool IsActive()
        {
            var ui = UIManager.Instance;
            if (ui == null) return false;
            if (Active(ui.GetUI<MainMenuUI>("MainMenuUI"))) return false;
            var house = UnityEngine.Object.FindFirstObjectByType<HouseUI>(FindObjectsInactive.Include);
            if (house != null && house.gameObject.activeInHierarchy && ui.GetUI<GameEntryUI>("GameEntryUI") == null) return false;
            if (Active(ui.GetUI<GameEntryUI>("GameEntryUI"))) return false;
            if (Active(ui.GetUI<MapSelectUI>("MapSelectUI"))) return false;
            if (Active(ui.GetUI<DialogueUI>("DialogueUI"))) return false;
            return Active(FightWindow);
        }

        private static bool Active(UnityEngine.Component c) => c != null && c.gameObject.activeInHierarchy;

        private static readonly FieldInfo ShowRewardField = typeof(FightUI).GetField("ShowReward", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>
        /// The fight is won and the chest is waiting. The game never leaves `fightType == Win` standing: `Fight_Win.Init` swaps it to
        /// `None` in the same call, so the only durable sign is the chest object that `ShowChest()` switches on (and `ShowReward`,
        /// which `ShowBattleReward()` clears). Without this every fight waited ~10 s for the game's own auto-open.
        /// </summary>
        internal static bool ChestReady()
        {
            var fight = FightWindow;
            if (!Active(fight) || fight.chest == null || !fight.chest.activeSelf) return false;
            var type = FightManager.Instance?.fightType;
            if (type == FightType.Loss) return false;
            return ShowRewardField == null || (bool)ShowRewardField.GetValue(fight);
        }

        // ------------------------------------------------------------------ enemies

        /// <summary>Enemies in game order. This index is what `--target` and the state's enemy `index` mean.
        /// EnemyManager.Instance dereferences FightManager unguarded, so it throws outside a fight.</summary>
        internal static List<Enemy> Enemies()
        {
            try { return EnemyManager.Instance?.enemyList?.Where(e => e != null).ToList() ?? new List<Enemy>(); }
            catch (Exception) { return new List<Enemy>(); }
        }

        /// <summary>The game's own rule for a legal attack target (AttackCardItem.IsSelectableTarget): enabled and alive.
        /// A dead enemy lingers in the list until the game's second death pass, with HP at or below zero.</summary>
        internal static bool IsSelectable(StatusManager target) => target != null && target.enabled && target.CurHp > 0;

        private static StatusManager StatusOf(Enemy e) => e?.Status as StatusManager;

        internal static bool IsLiving(Enemy e) => IsSelectable(StatusOf(e));

        internal static string NameOf(Enemy e) => (e?.Status as UnityEngine.Component)?.name ?? "?";

        internal static bool AnyTarget() => Enemies().Any(e => IsSelectable(StatusOf(e)));

        /// <summary>The enemy a play should hit: the requested index, or the first living enemy. Null + message when none is valid.</summary>
        private static StatusManager PickTarget(int? index, out string error)
        {
            var enemies = Enemies();
            error = null;
            if (index.HasValue)
            {
                var chosen = StatusOf(enemies.ElementAtOrDefault(index.Value));
                if (IsSelectable(chosen)) return chosen;
                error = "enemy " + index.Value + " is not a valid target (missing or already dead)";
                return null;
            }
            var first = enemies.Select(StatusOf).FirstOrDefault(IsSelectable);
            if (first == null) error = "no living enemy to target";
            return first;
        }

        // ------------------------------------------------------------------ hand

        internal static List<CardItem> Hand() =>
            (FightUI.cardItemList ?? new List<CardItem>()).Where(c => c != null && c.gameObject != null && c.gameObject.activeInHierarchy).ToList();

        internal static List<CardView> EvaluateHand() =>
            Hand().Select((card, index) =>
            {
                // One odd card must not take the whole snapshot down.
                try
                {
                    bool ok = CanPlay(card, out var reason);
                    return new CardView { Index = index, Card = card, Playable = ok, Reason = reason, Cost = Cost(card) };
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogWarning("[WitchCtl] could not evaluate hand card " + index + ": " + e.Message);
                    return new CardView { Index = index, Card = card, Playable = false, Reason = "evaluation failed: " + e.Message };
                }
            }).ToList();

        /// <summary>Is the player allowed to act at all right now (turn-level conditions, independent of any card)?</summary>
        internal static bool PlayerWindow(out string reason)
        {
            var fight = FightManager.Instance;
            if (fight == null || fight.fightType != FightType.Player)
            {
                reason = "not the player's turn (" + (fight == null ? "no fight" : fight.fightType.ToString()) + ")";
                return false;
            }
            var ui = FightWindow;
            if (!Active(ui)) { reason = "FightUI is not available"; return false; }
            if (DeckSelect.IsOpen())
            {
                reason = "the card picker (deck_select) is open: choose with deck_pick_N first";
                return false;
            }
            if (Selecting())
            {
                reason = "a card selection is open (" + FightUI.SelectType + "): choose " + FightUI.SpecialCount + " more with battle_select_card_N, then battle_select_confirm";
                return false;
            }
            var button = ui.turnButton;
            if (button == null || !button.isInteractable) { reason = "turn button is not interactable (animation or enemy phase)"; return false; }
            // The game hides the button with SetActive(false) and leaves isInteractable true while a card asks for a
            // choice and after a win; its own keyboard shortcut checks activeInHierarchy for the same reason.
            if (!button.gameObject.activeInHierarchy) { reason = "end-turn button is hidden (a card is asking for a choice, or the fight is over)"; return false; }
            if (FightUI.InIEn) { reason = "the game is busy (a card is asking for a choice)"; return false; }
            // Cards are dealt one by one (0.15-0.55 s apiece). Acting on the first of them means choosing from a partial hand
            // and, with an empty-looking hand, ending the turn before the rest has arrived.
            int dealing = Dealing();
            if (dealing > 0) { reason = "cards are still being dealt (" + dealing + " waiting)"; return false; }
            reason = null;
            return true;
        }

        /// <summary>Cards drawn but not yet on the table.</summary>
        internal static int Dealing() => FightWindow?.createCardQueue?.Count ?? 0;

        internal static bool CanPlay(CardItem card, out string reason)
        {
            if (!PlayerWindow(out reason)) return false;
            if (card.hasUse || !card.enabled || !CardItem.canUse)
            {
                reason = "card not usable right now (already used, dealing animation or input locked)";
                return false;
            }
            if (card is CommonCardItem common)
            {
                var vars = common.dataConfig?.Vars;
                if ((vars != null && vars.TryGetValue("Usable", out var usable) && usable == "0") || common.Tags.Contains("Unusable"))
                {
                    reason = "card cannot be played";
                    return false;
                }
                var status = common.status;
                if (status == null || status.state == IStatusManager.State.Dead || status.state == IStatusManager.State.NoAction)
                {
                    reason = "player cannot act";
                    return false;
                }
                int power = Power();
                int cost = Cost(card);
                if (cost > power)
                {
                    reason = "not enough power (" + cost + " > " + power + ")";
                    return false;
                }
            }
            if (card is AttackCardItem && !AnyTarget())
            {
                reason = "no living enemy to target";
                return false;
            }
            reason = "ok";
            return true;
        }

        private static string CardVar(CardItem card, string key) =>
            card.dataConfig?.Vars != null && card.dataConfig.Vars.TryGetValue(key, out var v) ? v : null;

        /// <summary>The card may be aimed at the player (Vars CanSelf == "True").</summary>
        internal static bool CanSelf(CardItem card) => TargetRules.CanSelf(CardVar(card, "CanSelf"));

        /// <summary>The card may be aimed at an enemy (not Vars CanEnemy == "False").</summary>
        internal static bool CanEnemy(CardItem card) => TargetRules.CanEnemy(CardVar(card, "CanEnemy"));

        internal static int Power() => FightPlayer.Instance?.CurPowerCount ?? 0;

        /// <summary>Energy cost, same formula as the game's UseChecker.</summary>
        internal static int Cost(CardItem card)
        {
            var data = card.data ?? card.dataConfig?.data;
            if (data == null || !data.TryGetValue("Expend", out var raw) || !int.TryParse(raw, out var expend)) return 0;
            float multiplier = 1f;
            var dynamic = FightPlayer.Instance?.Status?.dynamicVariables;
            if (dynamic != null && dynamic.TryGetValue("CardCost", out var m)) multiplier = m;
            int extra = Var(card, "TotalExCost") + Var(card, "ExCost") + Var(card, "OnceExCost");
            return Math.Max(0, (int)(expend * multiplier) + extra);
        }

        private static int Var(CardItem card, string key) =>
            card.dataConfig?.Vars != null && card.dataConfig.Vars.TryGetValue(key, out var s) && int.TryParse(s, out var n) ? n : 0;

        internal static string IdOf(CardItem card) =>
            card.dataConfig?.data != null && card.dataConfig.data.TryGetValue("Id", out var id) ? id : null;

        // ------------------------------------------------------------------ card selection prompts

        // Private entry the game's own card click ends in while a prompt is open (CardItem.OnRightClick -> HandleSelectModeClick).
        private static readonly MethodInfo SelectModeClick =
            typeof(CardItem).GetMethod("HandleSelectModeClick", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>A card asked the player to pick hand cards (discard, burn, "choose N cards"): FightUI.InIEn with a SelectType. The
        /// end-turn button is hidden and every card locked until the choice is confirmed, so an agent that only knows play/end-turn
        /// waits for ever (seen: "选择1卡牌丢弃" after 冥想针剂). SpecialCount = picks still missing, SelectedCard = picks so far.</summary>
        internal static bool Selecting() => FightUI.InIEn && FightUI.SelectType != null;

        /// <summary>The game's own guard in CardItem.HandleSelectModeClick: a frozen card cannot be burnt or thrown.</summary>
        private static bool CanSelect(CardItem c) =>
            c != null && c.enabled && !c.hasUse && !(c.Tags.Contains("Froze") && (FightUI.SelectType == "Burn" || FightUI.SelectType == "Throw"));

        /// <summary>FightUI.Yes() accepts only then (the confirm button is shown under the same condition).</summary>
        internal static bool CanConfirm()
        {
            var button = FightWindow?.ConfirmButton;
            return Selecting() && button != null && button.gameObject.activeSelf
                   && (FightUI.SpecialCount <= 0 || FightUI.CanBeforeEnd || FightUI.SelectedCard.Count == FightUI.cardItemList.Count);
        }

        internal static string SelectTitle()
        {
            try { return Rich.Strip(FightWindow?.instance?.transform.Find("Title")?.GetComponent<TMPro.TMP_Text>()?.text ?? ""); }
            catch (Exception) { return ""; }
        }

        internal static object ReadSelect() => !Selecting() ? null : new
        {
            type = FightUI.SelectType,
            title = SelectTitle(),
            need = Math.Max(0, FightUI.SpecialCount),
            chosen = FightUI.SelectedCard.Count,
            canConfirm = CanConfirm(),
            optional = FightUI.CanBeforeEnd,
        };

        internal static bool IsChosen(CardItem card) => FightUI.SelectedCard.Contains(card);

        private static List<RuntimeLegalAction> SelectActions(List<CardView> hand)
        {
            var actions = new List<RuntimeLegalAction>();
            foreach (var v in hand.Where(h => CanSelect(h.Card)))
            {
                bool chosen = IsChosen(v.Card);
                actions.Add(new RuntimeLegalAction
                {
                    Id = "battle_select_card_" + v.Index,
                    Kind = "battle_select_card",
                    Label = Rich.NameOf(v.Card.dataConfig) ?? v.Card.name,
                    Description = chosen ? "chosen; act again to take it back" : "choose this card",
                    Parameters = new Dictionary<string, object> { ["cardIndex"] = v.Index, ["cardId"] = IdOf(v.Card), ["chosen"] = chosen },
                });
            }
            if (CanConfirm())
                actions.Add(new RuntimeLegalAction { Id = "battle_select_confirm", Kind = "battle_select_confirm", Label = "确定", Description = "Confirm the chosen cards." });
            return actions;
        }

        private static RuntimePerformActionResult SelectRefused(string actionId, string why) =>
            new RuntimePerformActionResult { Success = false, ActionId = actionId, Message = "refused: " + why };

        /// <summary>Choose / take back one hand card in the open prompt. A click is not proof (CLAUDE.md trap 15/16): the outcome is read back from SelectedCard.</summary>
        internal static RuntimePerformActionResult SelectToggle(string actionId, int index)
        {
            if (!Selecting()) return SelectRefused(actionId, "no card selection is open");
            var card = Hand().ElementAtOrDefault(index);
            if (card == null) return SelectRefused(actionId, "no card at index " + index);
            if (!CanSelect(card)) return SelectRefused(actionId, "that card cannot be chosen now (used, locked or frozen)");
            if (SelectModeClick == null) return SelectRefused(actionId, "game internals changed (HandleSelectModeClick not found)");
            bool before = IsChosen(card);
            var click = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, clickCount = 1 };
            SelectModeClick.Invoke(card, new object[] { click });
            bool after = IsChosen(card);
            if (after == before) return SelectRefused(actionId, "the game ignored the click (card still " + (after ? "chosen" : "not chosen") + ")");
            string name = Rich.NameOf(card.dataConfig) ?? card.name;
            UnityEngine.Debug.Log("[WitchCtl] " + actionId + ": " + (after ? "chose " : "took back ") + name + ", " + FightUI.SpecialCount + " more needed");
            return new RuntimePerformActionResult
            {
                Success = true,
                ActionId = actionId,
                Message = (after ? "Chose " : "Took back ") + name + "; " + Math.Max(0, FightUI.SpecialCount) + " more needed.",
            };
        }

        internal static RuntimePerformActionResult SelectConfirm(string actionId)
        {
            if (!Selecting()) return SelectRefused(actionId, "no card selection is open");
            if (!CanConfirm()) return SelectRefused(actionId, "cannot confirm yet: " + Math.Max(0, FightUI.SpecialCount) + " more card(s) to choose");
            FightWindow.Yes();
            UnityEngine.Debug.Log("[WitchCtl] " + actionId + ": confirmed " + FightUI.SelectedCard.Count + " card(s)");
            return new RuntimePerformActionResult { Success = true, ActionId = actionId, Message = "Confirmed the choice; the card effect resolves next." };
        }

        internal static List<RuntimeLegalAction> Actions(List<CardView> hand)
        {
            if (Selecting()) return SelectActions(hand);
            var actions = new List<RuntimeLegalAction>();
            if (PlayerWindow(out _))
                actions.Add(new RuntimeLegalAction { Id = "battle_end_turn", Kind = "battle_end_turn", Label = "结束回合", Description = "End the player's current turn." });
            foreach (var v in hand.Where(h => h.Playable))
            {
                actions.Add(new RuntimeLegalAction
                {
                    Id = "battle_play_card_" + v.Index,
                    Kind = "battle_play_card",
                    Label = Rich.NameOf(v.Card.dataConfig) ?? v.Card.name,
                    Description = "cost " + v.Cost,
                    Parameters = new Dictionary<string, object>
                    {
                        ["cardIndex"] = v.Index,
                        ["cardId"] = IdOf(v.Card),
                        ["requiresTarget"] = v.Card is AttackCardItem,
                        ["canSelf"] = v.Card is AttackCardItem && CanSelf(v.Card),
                        ["canEnemy"] = v.Card is AttackCardItem && CanEnemy(v.Card),
                    },
                });
            }
            actions.AddRange(Skills.Actions());
            return actions;
        }

        // ------------------------------------------------------------------ doing things

        internal sealed class PlayRequest
        {
            public int? CardIndex { get; set; }
            /// <summary>Stable handle (hand entries carry it as `uid`); indices shift as cards leave the hand.</summary>
            public string CardUid { get; set; }
            public string CardId { get; set; }
            public int? TargetIndex { get; set; }
            /// <summary>Aim at the player (a card with <c>CanSelf</c>: a heal, a buff on "a unit"). Not combinable with <see cref="TargetIndex"/>.</summary>
            public bool TargetSelf { get; set; }
            /// <summary>false (default): validate, queue the play and answer at once. true: answer after the card has resolved.</summary>
            public bool Wait { get; set; }
        }

        internal sealed class PlayResult
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public string CardId { get; set; }
            public string CardType { get; set; }
            public string TargetName { get; set; }
            public int? TargetHpBefore { get; set; }
            public int? TargetHpAfter { get; set; }
            public int HandCountBefore { get; set; }
            public int HandCountAfter { get; set; }
            public int? PowerBefore { get; set; }
            public int? PowerAfter { get; set; }
        }

        /// <summary>What the last resolved play did. Queued plays answer "queued" before they run, so their outcome lives here (and in state.flow).</summary>
        internal sealed class PlayOutcome
        {
            public int Seq { get; set; }
            public bool Ok { get; set; }
            public string CardId { get; set; }
            public string Error { get; set; }
        }

        internal static volatile PlayOutcome LastPlay;
        internal static volatile string LastError;
        private static int _seq;
        private static int _pending;

        internal static async UniTask<PlayResult> PlayAsync(PlayRequest request)
        {
            if (!IsActive()) return new PlayResult { Message = "no battle is on" };
            // Two plays validated against the same energy would both be queued and the second one fail inside the game
            // (with its "not enough energy" toast and sound), after the client was told "queued".
            if (Volatile.Read(ref _pending) > 0) return new PlayResult { Message = "refused: the previous play is still resolving" };

            var hand = EvaluateHand();
            CardView view = null;
            if (!string.IsNullOrWhiteSpace(request.CardUid))
                view = hand.FirstOrDefault(h => h.Card.dataConfig?.InstanceID == request.CardUid);
            else if (!string.IsNullOrWhiteSpace(request.CardId))
                view = hand.FirstOrDefault(h => IdOf(h.Card) == request.CardId);
            else if (request.CardIndex.HasValue)
                view = hand.FirstOrDefault(h => h.Index == request.CardIndex.Value);
            if (view == null) return new PlayResult { Message = "no matching card in hand" };

            var card = view.Card;
            var result = new PlayResult
            {
                CardId = IdOf(card),
                CardType = card.GetType().Name,
                HandCountBefore = hand.Count,
                PowerBefore = FightPlayer.Instance?.CurPowerCount,
            };
            if (!view.Playable)
            {
                result.Message = "refused: " + view.Reason;
                return result;
            }

            StatusManager target = null;
            if (card is AttackCardItem)
            {
                var why = TargetRules.Choose(CanSelf(card), CanEnemy(card), request.TargetSelf, request.TargetIndex.HasValue, out var kind);
                if (why != null)
                {
                    result.Message = "refused: " + why;
                    return result;
                }
                string targetError = null;
                target = kind == "self" ? FightPlayer.Instance?.Status as StatusManager : PickTarget(request.TargetIndex, out targetError);
                if (target == null)
                {
                    result.Message = "refused: " + (targetError ?? "the player has no status to aim at");
                    return result;
                }
                if (PrepareDirectedUse == null)
                {
                    result.Message = "refused: game internals changed (PrepareDirectedCardUse not found)";
                    return result;
                }
            }
            else if (request.TargetIndex.HasValue && card is CommonCardItem)
            {
                target = PickTarget(request.TargetIndex, out var targetError);
                if (target == null)
                {
                    result.Message = "refused: " + targetError;
                    return result;
                }
            }

            result.TargetName = target != null ? target.gameObject.name : null;
            result.TargetHpBefore = target?.CurHp;

            if (!request.Wait)
            {
                // Resolving a card runs the game's own scripts on the main thread (hundreds of ms). Validation is done and the
                // work is queued ahead of any later request, so a client that waits for this answer stays correctly ordered.
                Interlocked.Increment(ref _pending);
                Pump.Post(() =>
                {
                    try { Resolve(card, target, result.CardId); }
                    finally { Interlocked.Decrement(ref _pending); }
                });
                result.Success = true;
                result.Message = "queued";
                return result;
            }

            string error = Resolve(card, target, result.CardId);
            if (error != null)
            {
                result.Message = error;
                return result;
            }
            await UniTask.DelayFrame(2);

            result.Success = true;
            result.Message = "Card played.";
            result.TargetHpAfter = target?.CurHp;
            result.HandCountAfter = Hand().Count;
            result.PowerAfter = FightPlayer.Instance?.CurPowerCount;
            return result;
        }

        /// <summary>The state-changing call. Re-checks everything (the world may have moved since the request was validated)
        /// and verifies the game accepted the card. Returns an error message, or null on success.</summary>
        private static string Resolve(CardItem card, StatusManager target, string id)
        {
            try
            {
                if (card == null) return Record(id, "refused at resolve time: the card is gone");
                if (!CanPlay(card, out var why)) return Record(id, "refused at resolve time: " + why);
                if (card is AttackCardItem && !IsSelectable(target)) return Record(id, "refused at resolve time: the target is gone");

                int before = Power();
                if (card is AttackCardItem attack) PrepareDirectedUse.Invoke(null, new object[] { attack, target });
                else if (target != null && card is CommonCardItem common) common.dataConfig.scriptExecutor.Target = target;
                ((CommonCardItem)card).TrueUse();

                // TrueUse returns nothing; when the game's own check refuses it only resets hasUse.
                bool played = card == null || card.hasUse || Power() != before;
                if (!played) return Record(id, "the game did not accept the card");

                Record(id, null);
                UnityEngine.Debug.Log("[WitchCtl] played " + id + " power " + before + "->" + Power());
                return null;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[WitchCtl] play failed: " + e);
                return Record(id, "card play threw: " + e.Message);
            }
        }

        private static string Record(string cardId, string error)
        {
            LastPlay = new PlayOutcome { Seq = Interlocked.Increment(ref _seq), Ok = error == null, CardId = cardId, Error = error };
            if (error != null) LastError = error;
            return error;
        }

        internal static RuntimePerformActionResult EndTurn()
        {
            if (!PlayerWindow(out var reason))
                return new RuntimePerformActionResult { Success = false, ActionId = "battle_end_turn", Message = "refused: " + reason };
            FightWindow.turnButton.onClick.Invoke();
            UnityEngine.Debug.Log("[WitchCtl] end turn");
            return new RuntimePerformActionResult { Success = true, ActionId = "battle_end_turn", Message = "Ended the current turn." };
        }

        /// <summary>Routes a `battle_*` action id from `act` to the battle implementation.</summary>
        internal static async UniTask<RuntimePerformActionResult> PerformAsync(string actionId)
        {
            if (actionId == "battle_end_turn") return EndTurn();
            if (actionId == "battle_select_confirm") return SelectConfirm(actionId);
            if (Skills.Owns(actionId)) return Skills.Perform(actionId);
            const string selectPrefix = "battle_select_card_";
            if (actionId != null && actionId.StartsWith(selectPrefix, StringComparison.Ordinal) && int.TryParse(actionId.Substring(selectPrefix.Length), out var pick))
                return SelectToggle(actionId, pick);
            const string prefix = "battle_play_card_";
            if (actionId != null && actionId.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(actionId.Substring(prefix.Length), out var index))
            {
                var result = await PlayAsync(new PlayRequest { CardIndex = index, Wait = false });
                return new RuntimePerformActionResult { Success = result.Success, ActionId = actionId, Message = result.Message, Data = result };
            }
            return new RuntimePerformActionResult { Success = false, ActionId = actionId, Message = "refused: not a battle action id" };
        }
    }
}
