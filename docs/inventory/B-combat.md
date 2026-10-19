> 2026-10-13 静态反编译分析，由只读调研 agent 产出，**未经实机验证**；「候选」= 只有名字或 public 方法证据，没见到 prefab 绑定。
> 合并后的结论、已核实项和路线图见 [../scene-interfaces.md](../scene-interfaces.md)。文中的 scratchpad 路径只在当时的会话里存在。

# inv-B-combat: everything the player can do inside a fight (static analysis, group B)

**Baseline.** `mod/Battle.cs` at HEAD (413 lines) = read-only legality, `battle_play_card_N` (+target), `battle_end_turn`. The working tree
(520 lines, uncommitted, someone else's edit while I read) already adds hand-card prompts (`battle_select_card_N`, `battle_select_confirm`,
`state.battle.select`, via private `CardItem.HandleSelectModeClick` + `FightUI.Yes`). I count that as "in progress" for S2; nothing else below
exists in either version. Line refs are decomp files under `.toolchain/decomp/` (real bodies are the `_0024Rougamo_*` ones).
**`FightType` is the turn state machine, not the fight kind:** None, Init, Start, Player, Enemy, Partner, OtherTurn, Win, Loss, Escape (enum
itself is in another assembly; values from usages, `FightManager.ChangeUnit` :3200). Fight *kind* = level row `Note` (`EnemyManager.cs:182`):
normal x1, 精英 x2, boss x3, 无名 x4 (settlement multiplier only). If >3 enemies and the mode allows it (`:195`), enemies spawn one at a time
(`TrySpawnNextWheelEnemy`), so `enemyList` can be briefly empty between waves.

## S1. Fight main screen (FightUI)
Reached: any fight node/event -> `FightInit` -> FightUI. Shows hand, energy (`Left/Time/total/val`), piles (`Left/Card`, `ClockBoard/弃牌堆`),
career skills (`Left/Skill1` or `Left/Skill2/Skill1|Skill2`), buttons `ClockBoard/{结束回合,确定,结束战斗,重开战斗}`.

| operation | entry point | parameters | preconditions / side effects | rev? | invoke |
|---|---|---|---|---|---|
| play non-targeted card | `CommonCardItem.TrueUse` :322 (human: drag up past y>50 `OnEndDrag` :280, double-click <=0.25 s `OnPointerDown` :411, digit key `UseCardDirectly` :391) | card only. Targets come from the card script: Self / Target (= `enemyList[0]` by default) / All... / AllRandomN / ExSelf (`ScriptExecutor.SetStatus` :6229) | `UseChecker` :18 (Usable/Unusable, cost>energy) + `TryUse` :441 (not Dead/NoAction, fightType==Player). Pays `min(int(expend*CardCost),4)+extra` (:478; the 4-cap is absent from UseChecker and `Battle.Cost`: harmless mismatch) | no | M (done) |
| play targeted card (AttackCardItem) | `AttackCardItem.TrueUse` :536 after `scriptExecutor.Target` + private `hitEnemy` (mod reuses `PrepareDirectedCardUse`) | target StatusManager. Legal = `IsSelectableTarget` :711: FightPlayer/OtherPlayer always, `OtherObj` (**Enemy and Partner**) with CurHp>0. Ray check :609-615 adds: self only if card Vars `CanSelf=="True"`; enemies excluded if `CanEnemy=="False"`. No valid hit -> game falls back to `enemyList[0]` | same gates as above | no | M. **GAP**: `Battle.Enemies/AnyTarget/PickTarget` only know `enemyList`, so ally-target / self-target attack cards (CanEnemy=False, CanSelf=True) are mis-gated ("no living enemy") or aimed at an enemy. Partners live in `PatternManager.Instance.PatternList` (:17), self = `FightPlayer.Instance.Status` |
| end turn | `turnButton` -> `FightManager.TurnEnd` :3191 -> `FightUI.onChangeTurnBtn` :2953 -> `TurnBtn` :1034 | none | `canUse=false`, waits `InIEn`/deal queue, `EndRound`, `RemoveAllCards` :1228, which may raise S2 prompts (BurnCount>0 -> optional Burn; RetainCard>0 -> Retain). Space key | no | M (done) |
| use career skill | `SkillItem.TrueUse` :576 (public) after `TryUse` :489; human = PointerDown on the skill (an `EventTrigger`, **not** a Button) :506 | skill slot 1 or 2 (Career row `ActionImage1/2`, `Skill1/2`, `FightUI.InitSkill` :2798). `Vars["BaseScript"]=="CommonCardItem"` -> fires at once; otherwise aim line (`CheckRayToEnemy` :526) at a target: player/OtherPlayer/enemy/partner with HP>0 (no CanSelf rule) | fightType==Player, not Dead/NoAction, `CardItem.canUse`, cooldown `RoleTable.SkillTime[id]<=0` (set/decremented by scripts via `ChangeSkill` :3784, reset to 0 after each fight `Fight_Win:263`); idol careers also `CanAffordIdolPopularity` (spends "popularity"). **No energy check** | no | M: set `hitEnemy` by reflection (as for cards) then `TrueUse()`. U is doubtful (not a ButtonManager). **NOT in mod** |
| read draw / discard pile | `FightCardManager.Instance.cardList` / `usedCardList`; UI viewers `FightUI.CreateDeckMenu` :2843 / `CreateUsedCardList` :2848 (candidate bindings of `Left/Card`, `ClockBoard/弃牌堆`) | - | **`DrawCard` takes the LAST element** (:527); Inherent-tag cards are sorted last = drawn first (`RandomIndex` :481). Listing the list shows the exact draw order. The UI viewer is a `DeckUI` instance and S3 pickers wait for `DeckUI==null` (`ScriptExecutor:6154`): never open it from the bot | yes | S: add card ids to `piles` (state has counts only, `State.Piles`) |
| restart fight | `ClockBoard/重开战斗`, key R -> `FightAgainCheck` :2858 -> modal "是否要重启战斗" -> `FightAgainBtn` :2866 -> `FightManager.ReSetFight` -> `ClearFightui` -> `FightUI.Reset` :2896 | - | Re-inits the fight; player RoleTable restored from the start-of-fight snapshot (`TempRoleList`). Gate only `!IsReset && !quickReset`; hidden after a win; multiplayer needs all votes. Free and unlimited = save-scum | n/a | U + `modal_confirm` (modal already a phase). **dangerous (run integrity)** |
| speed-up 1.5x | setting "加速模式"="开启" (`SettingTable.cs:17,31`) -> `GameSpeed.Multiplier` (22 call sites) | - | Persistent setting: `Singleton<GameRuntimeData>.Instance.settingTable.SetValue(...)` (lab has its own prefs). Shortens animation waits (`NowAnimation`), not the 240-460 ms main-thread card resolve | yes | S |
| cosmetic settings | "显示伤害数字"="关闭", "低配模式" | - | fewer popups | yes | S |

Value: skills **High** (career-defining; nothing else lets the bot use them); ally/self targeting **Med-High** (size unknown, see unknowns);
pile contents **Med** (planning info, free); speed-up **Med** (throughput); restart **Avoid by default** (opt-in flag only).
Effort: skills **M** (list `{slot,id,name,cd,usable,needsTarget,text}` + `skill_use_N` with optional target; risk = aimed skills need the private `hitEnemy`, and `TryUse` can be false while `canUse` toggles);
targets **S-M** (replace `Enemies()` by a target list with kind enemy/partner/self + CanSelf/CanEnemy; risk = card Vars keys unseen offline); piles **S**; speed **S**.

## S2. Hand-card prompts (blocks the bot; working tree already models them)
Reached: a card/relic/enemy script calls one of the rows below. State while open: `FightUI.InIEn==true`, `SelectType`, `SpecialCount` (picks still
missing), `SelectedCard`, `CanBeforeEnd` (optional), title text in `instance/Title` (clone `SelectCard` of prefab `UI/FightCardInstance`), end-turn
button hidden, `CardItem.canUse=false`. Cards wait for the deal queue and for any earlier prompt (`InIEn`) before opening.

| kind (`SelectType`) | raised by | notes |
|---|---|---|
| discard N ("Throw") | `ScriptExecutor.ThrowCard` :5117 -> `FightUI.ThrowCardScript` :1330 -> `Throw` :1767 | if N >= hand and not optional: discards everything, **no prompt**. Frozen cards cannot be chosen |
| exhaust N ("Burn") | `ScriptExecutor.BurnCard` :5148 -> `Burning` :1351 -> `Burn` :1482; also end of turn via `BurnCount` -> `Burn(count,"2")` :1232 (optional) | same auto-resolve and Frozen rule |
| choose N hand cards, then a callback ("CardToAction") | `ScriptExecutor.ChooseCardToAction` :5526 -> `SelectCardToAction` :1372 -> `CardToAction` :1414 | the callback gets the chosen `CardItem`s (upgrade/copy/transform/...); which cards use it is in the card table, not in code |
| retain at turn end ("Retain") | dynamic var `RetainCard`>0 -> `Wait` :1261 | optional (type "2"); unchosen cards are thrown |

Resolve (all four): pick = `CardItem.OnRightClick(PointerEventData left)` (public, :828) -> private `HandleSelectModeClick` :1626 (refused unless
fightType==Player; with `SpecialCount<=0` it swaps out the last pick; picking again takes a card back); confirm = `FightUI.Yes()` :3361 (public;
= button `ClockBoard/确定`, key E "结束选牌"), accepted only if `SpecialCount<=0 || CanBeforeEnd || all cards chosen`. Digit keys also toggle in select
mode (:2517). Reversible until confirm. A win (`CanWin` -> `EndInstance` :3347) kills any open prompt, so it cannot deadlock a win. Invoke **M** (done).
Value High, effort S (done). Unknown: a prompt raised during the *enemy* turn (on-hit scripts) cannot be answered by the game itself
(`HandleSelectModeClick` needs fightType==Player); `Battle.Actions` would offer picks that are silently ignored (the new code reads the result back, good).

## S3. Deck picker (DeckUI, window "新牌堆"): NOT modelled
Reached: `ScriptExecutor.SelectDeckCards` :6150 <- `CopyCard/CopyCardWare` :5540 (copy a card into hand), `GetDeckUIToAction/PackToDeckAction` :6096
(pick from a list -> callback), `AddCardByDeck/WarpAddCardByDeck` :6109 (move picked cards into hand), `OutFightSelectCardToAction` :6145. The list is
whatever the card script passes (draw pile, discard, whole deck or a generated "discover" list): unknowable offline. I found no other scry/discover UI.
(`CreateDeckMenuForSelect` is also used out of fight by `Commands.cs:2435/2805` and `CardEnchUI:200`: other groups.)

| operation | entry point | parameters | preconditions / side effects | rev? | invoke |
|---|---|---|---|---|---|
| pick / un-pick a card | `ButtonManager` per source card under `Window Manager/Windows/新牌堆/Content/List View Custom/Scroll Area/List/card(Clone)`, listener `DeckUI.cs:232` toggles `DisplayCard.isSelect` | index in list (child order = `SourceList` order; each child `DisplayCard` carries `dataConfig` for the listing) | **No confirm button**: reaching `count` calls `Close()` by itself. Text "Please Choose {n} X Cards" (`Description`) | yes until count reached | U (ButtonManager click) |
| close early = skip | `UIBase.Close()` -> `DeckUI.OnDisable` :253 fires event `SelectCardEnd` -> callback runs with the partial list (possibly empty) | - | close button path unknown (candidate `Window Manager/Buttons/*` or a generic Close) | no | U / M `GetUI<DeckUI>().Close()` |
| tell picker from viewer | `Window Manager/Buttons/新牌堆` active (CreateDeckMenuForSelect hides the 抽牌堆/弃牌堆 tabs) | - | - | - | - |

Danger found: the picker does **not** touch `canUse` or the turn button, so `Battle.PlayerWindow()` stays true and `battle_play_card_N` /
`battle_end_turn` are still offered while it is open; ending the turn then fires the callback in the enemy turn, and a second picker queues behind
`deckUiSelectBusy`. The new phase must pre-empt the battle actions (like S2 does). Value **High** (it blocks the card script; the pick decides what
enters the hand). Effort **M**: Detect (DeckUI + 新牌堆 tab active) + list from `DisplayCard.dataConfig` + `deck_pick_N` + `deck_close`; risks: item
labels live in the `DisplayCard` child, early close commits a partial pick, "count reached -> auto close" races with a second click.

## S4. How a fight ends
| ending | trigger | what happens / what the player must click |
|---|---|---|
| Win | last enemy dies (`StatusManager:4184`, no wheel enemy left) or `DOAllAction` finds none -> `FightUI.CanWin` :3313 (hides end-turn, `EndInstance`, kills leftovers, **shows `ClockBoard/结束战斗`**, `CmdChangeType(Win)`) -> `Fight_Win.Init` :139 -> `WinLogic`/`ResetStates` (after `NowAnimation` ends) -> `ShowChest` :2364 | chest object `FightUI.chest` (Button.onClick = `ShowBattleReward`, `FightUI:2678`); **auto `ShowBattleReward` after 10 s** (:2377). Levels `level_*0, 10046, 10048, 10051` (high-tide / boss-night rows, `Fight_Win:198`): FightUI just closes, `InHighTide=true`, **no chest, no rewards screen** |
| Escape | scripts only (`ScriptExecutor.EscapeFight` :5812, `StatusManager:4707`, console). There is **no flee button** | `Fight_Escape` = win without chest: "Win" events + 150 x mult feat, FightUI closes at once, `MapManager.TryChange()` (slot mode: `TryResolveQueuedBattleCompletion`). Nothing to click |
| Loss | all players dead (`FightManager.CmdCheckDead` :2486) -> `RpcGameOver` -> `Fight_Loss` | FightUI is closed immediately, `GameExitUI` (loss) opens = our `run_end`. Nothing to click in the fight |
| scripted fight (`IsFake`) defeat | `StatusManager:4681` | full heal, role restored from snapshot, 0.3 s later `CmdChangeType(Escape)` -> ends like Escape (only if `TempRoleList` has the id, else it just returns) |

**Contradictions with CLAUDE.md / README (static, need one live log to confirm):**
1. `Screens.Detect` and `OpenRewards` key on `FightManager.fightType == Win`, but `Fight_Win.Init` :157 and `Fight_Escape.Init` :97 call
   `ChangeUnit(None)` synchronously and `ChangeUnit` allows Win->None (`FightManager:3202`). So `fightType` never reads Win at a frame boundary:
   `battle_won` is unreachable, `battle_open_rewards` would answer "the fight is not won", and the run only moves on because of the game's 10 s
   auto-open (README: "the game usually opens the chest by itself" = ~10 s dead time per fight). Robust signal: `FightUI.chest.activeSelf`
   (set in `ShowChest`, after `WinLogic`) or `endfight.gameObject.activeSelf` (set in `CanWin`, public Transform). Same story for `battle_lost`
   (`Fight_Loss` closes FightUI and sets None). Calling `ShowBattleReward()` as soon as `chest` is active saves the 10 s.
2. Trap 14 says a defeat in a scripted fight never ends it; `StatusManager:4681-4707` ends it via Escape after 0.3 s (stalemate only when `TempRoleList` lacks the id).
3. Win in the four special levels has no chest/rewards phase: the mod would see `unknown` while FightUI closes. Needs a look at what follows.
Unknown: binding of `ClockBoard/结束战斗` (prefab onClick; candidate `ShowBattleReward`/`Yes`).

## S5. Checked, not player inputs (do not model)
| item | finding |
|---|---|
| dice | `Dice.Roll` is an RNG, `DiceIcon` an animation; `DiceCheck` :6400 rolls instantly. No input |
| partner commands | `Partner` is AI-driven (`Fight_Partner` has no input); partners only matter as targets (S1) |
| relic / blessing / consumable in a fight | none: relics are passive (`RelicItemConfig` only sell / take-off via right-click float window; take-off refused in fight), `BlessingRelic` has no click, no consumable code path |
| ping | `PingSelection` = Supabase latency telemetry (`LatencyRecorder:353`, auto-upload in Steam multiplayer): **dangerous (upload)**, not UI. Chat/emoji = multiplayer only |
| auto-play | `FightUI.autoCard` is only set by console `auto on` (`Commands.cs:3280`), which also grants 999 energy and 10 cards: cheat, **Avoid**. Console `win` etc. likewise |
| SelectGroup / SelectMessage / SelectOutline | hover outline, tooltip, toggle group: cosmetic |
| OptionsUI | dialogue choice list (`DialogueManager:500`). Scripts can raise dialogue lines/options mid-fight (`ShowDialogue/ShowOptions`, `ScriptExecutor:3448/3477`); existing `dialogue` phase already wins precedence (`Battle.IsActive` checks DialogueUI first) |
| modal over fight | only "是否要重启战斗" (FightUI:2860) in the fight code; `Popups.DetectModal` covers it |
| start-of-fight | Preemptive ("nascent") cards auto-play in `FightInit.TriggerNascentCards`; they can open S2/S3 prompts before the first hand exists |

## Unknowns a live look must settle
1. Log `FightManager.fightType`, `FightUI.chest.activeSelf`, `endfight.activeSelf` every 100 ms from the last enemy's death to `BattleRewardsUI` (contradiction 1); and what phase the four special levels land in.
2. Card table scan (`GameConfigManager.GetTable(DataType.Card)`, scripts are text; bundle is compressed so offline needs UnityPy): count cards whose scripts use `ChooseCardToAction / CopyCard / AddCardByDeck / GetDeckUIToAction`, and Vars `CanSelf=True` / `CanEnemy=False`. Also the `Career` table: skills per career, `BaseScript` of each, cooldown model. This sizes S1 targets, S1 skills and S3.
3. Does `UiLite.ClickObject` reach `EventTrigger` PointerDown (skills)? Else reflect `hitEnemy` + `TrueUse`.
4. DeckUI picker: is `Battle.PlayerWindow()` really true while it is open; is there a visible close button and its path; does a human-clicked early close pass an empty list to the callback?
5. `/world`/`ui` dump of FightUI after a win: what `结束战斗` and the chest `Button` call.
6. Enemy-turn prompts (fightType Enemy): do they stall until the next player turn?
