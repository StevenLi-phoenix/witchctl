> 2026-10-13 静态反编译分析，由只读调研 agent 产出，**未经实机验证**；「候选」= 只有名字或 public 方法证据，没见到 prefab 绑定。
> 合并后的结论、已核实项和路线图见 [../scene-interfaces.md](../scene-interfaces.md)。文中的 scratchpad 路径只在当时的会话里存在。

# A2 老虎机 (SlotMachUI / SlotMachineManager) 操作清单 — 纯静态分析，未运行游戏

路径缩写：`UW`=`.toolchain/decomp/Witch.UI.Window/`，`W`=`.toolchain/decomp/Witch/`，行号指反编译文件。

## 结论先行
1. **老虎机不是 Classic 局的必经阶段。** `MapScene2 = SlotMachScene` 只是背景图标签；`SlotMachUI` 只会由 `SlotMachineManager` 创建，而它只在 `saveInfo.modeType == "Slot"`（游戏内叫 "Random Mode" / "Chaos Paradise"，玩家等级 >= 25 解锁）时才是 `ModeMapManager`。Classic 局永远不会出现它。
2. 在 Slot 模式里它**取代了整个地图选择界面**：不是拉杆转一下，而是"每抽一张、选一张、自动抽下一张"的筹码赌局（见 Q2）。`SlotRod`（拉杆）的点击是**空函数**，`SlotMachHand` 只是动画演员，二者都没有输入。
3. 当前 mod 对它**完全无感**（不会误操作，也看不见）：`GetUI<MapSelectUI>("MapSelectUI")` 按名字查，SlotMachUI 注册名是 `"SlotMachUI"` → 恒为 null → 阶段只会是 `unknown` + 通用 `ui` 转储。
4. 要做成可玩：M 档（约 170–230 行，≈ Rest.cs + Events.cs），不需要拖拽；"整个跳过"也可行（`EndSlot` + `modal_confirm` → 现有 `run_end`），但 Classic 根本用不到。建议：不做，只把 `mode_choice_survival` 改名并加防误点说明。

## Q1：MapScene2 与 Slot 模式的区别
| 项 | Classic（Normal） | Slot / Random Mode |
|---|---|---|
| 写入 `GameVars["MapScene1/2/3"]` | 是（`ModeChoiceUI.cs:475-488`，`CreateNewSave` 对**所有** modeType 都写：Courtyard\|Forest / **SlotMachScene** / Castle\|Chessboard） | 同左 |
| 谁读这些 GameVars | **无人**：`rg MapScene` 在反编译源码里只命中 ModeChoiceUI；Witch.dll 里该字符串只有 3 个字面量。Lua/配置表未知（资源在压缩包内，搜不到） | 同左 |
| 背景实际由谁定 | `GameApp.cs:766-771` 与 `BattleRewardsUI.cs:378-381`：按 `MapManager.Level` 硬编码：0-5 Forest，6-11 Courtyard，**12-17 SlotMachScene**，18-23 DollFairyTale，24+ Castle/BalancedHolySee，30+ Chessboard/Curia/DemonKing → MapScene* 变量是遗留物 | 同左（Slot 里 Level 每次选牌也 +1，见下） |
| 触发时机/"什么都不做"会怎样 | 无触发。Level 12-17 只是换背景，MapSelectUI 照旧（NormalMapManager）。无任何交互 | 进入模式后 `SlotMachUI.Start` 自动开第一抽；之后无计时器（SlotMachUI 无 Update），停在当前一抽直到玩家操作；存档里有 `SlotAccumulatedSelectionCards` + 骰子快照，重进可恢复 |
| 管理器 | `MapManager.SetMap("Normal")`→NormalMapManager（`MapManager.cs:1857-1866`） | `SetMap("Slot")`→`SlotMachineManager`；入口 `ModeChoiceUI.SlotMode()`（`ModeChoiceUI.cs:608`，Level<25 只弹 caption "25级解锁此模式"）；按钮 `ModeList/SlotMode`，**就是 mod 里叫 `mode_choice_survival` 的那个** |
| 通关 | 有最终 Boss | **没有**：`WinTheGame()` 恒 false（`SlotMachineManager.cs:5026`）；只能破产/主动结算/死亡结束 |

## Q2：Slot 模式的输入回路（"拉杆/旋转"实际是什么）
流程（均出自 `SlotMachineManager` / `SlotMachUI` 的 Rougamo 真身）：
- 开局 `InitRoleTable`：随机初始卡组/遗物，筹码 `ChipReserve=3000`，最大 San(血) = 60 + 持有筹码/100。
- `ReadyToChangeMap`(4160)：白天且当天战斗 `DayBattleCount < 2` → `ShowMapSelect`（RPC → `CurtainTurnUI` → `ShowUI<SlotMachUI>`）；白天打满 2 场 → `BeginNightPhase` 再进 SlotMachUI（夜晚）。夜晚队列打完 → 次日，先弹 `SlotSettlementUI` 过场（`__SlotSettlementUiPending`）再进 SlotMachUI。**日/夜不是玩家开关**，`RefreshDayNightUI` 只切背景与"第N天"(= Level/12+1)。
- **抽牌回路**：`SlotMachUI.Start`(4817) 自动 `TryChoiceCard`；每抽先 `TryAutoInvestBeforeDraw` 扣 150 筹码（付不起 → 结算，队列非空则弹 modal "Fight now / Settle now"）。白天前 4 抽 = "self"（只有奖励通道，单选 3 张，奖励来自 SlotReward 表：牌/遗物/祝福…）；之后及整个夜晚 = "enemy"：**奖励通道可点，灾厄通道不可点**（`SetClickEnabled(false)`），玩家点奖励后 `TryCommitRewardSelectionWithAutoHazard` **随机替你选一个灾厄**（怪物入队 `AddEnemy` / 词条 `AddEnemyAffix` / 倍率 `MapCal+=1` / 诅咒牌）。选中后手臂动画搬运（数秒）→ `ApplySelectionResultEmbedded` → `CloseEmbeddedDraftAfterDelay` **自动再抽**。每次落子 `Level++`、`DraftTurnCount++`。
- **站住(Stand) = `TryChangeMap`(2791)**：`DraftTurnCount >= 8` 才允许（<8 弹 caption），`>= 12` 强制站；白天若当天战斗不足且队列无怪也拒绝；付押金 `CurrentRoundRequiredStake = 100 + round*150`（付不起 → 结算）；夜晚自动加一只 Boss；`CmdReadyToNextMap` 开始依次打队列里的怪（普通 FightUI/BattleRewardsUI，`BattleRewardsUI.Entry` 在 Slot 下改调 `TryResolveQueuedBattleCompletion`，直接进下一场，中间**不经过**老虎机界面）。
- 粗算（我用常量推的，非实测）：3000 初始筹码约够"一白天 + 一夜晚"（每天 >=16 抽 × 150 + 两次押金），分数换筹码极少（score/50 再向下取整到 50），所以一局很短，点在于靠分数换 Truth 水晶。

## 1. SlotMachUI（抽牌界面，派生自 MapSelectUI，4948 行）
进入方式：见上；打开后 TopBar 左上被隐藏、`Content/Buttons/EndSlot` 被显示（`Awake`→`TopBarUI.ShowSlotButton`）。每场战斗开始时 `Commands.load`→`MapManager.CloseMapUI`→`CloseUI("SlotMachUI")` 销毁，下次重建（故 Start 的自动抽牌每次都会跑）。
| 操作 | 入口 | 参数 | 前置 / 副作用 | 可逆? | 调用 |
|---|---|---|---|---|---|
| 选奖励卡（抽牌） | `SlotMachSlotItem.OnClicked`(public) ← `OnPointerClick`/`button` 监听 → `SlotMachUI.OnEmbeddedSlotClicked`(2985)；路径 `Content/GetCard/RewardList/SlotCardPre(Clone)` | 通道里第 i 张（默认 3 张，`embeddedPickSlotCount` 可序列化，预制体可能不同） | 私有 `canClick` 在入场补间后才为真（<=0.6s），之前点击被**静默忽略**；牌/遗物入 `RoleTable.cardList` / `WithoutArmedRelicList`（遗物栏>=6 会弹 modal 确认入仓库）；选"祝福"开 BlessChoice；同时随机落一个灾厄；`SaveMap` 存档 | 否 | U（点 SlotMachSlotItem 的 GameObject；`UiLite.Clickables` 因它实现 `IPointerClickHandler` 已会列出，但 `interactable` 恒为 true）或 M `OnClicked()` |
| 选灾厄卡 | 同上，`Content/GetCard/EnemyList/…` | — | 点击被 `flag && flag2` 分支丢弃（不可点） | — | 不可用 |
| 站住 Stand | `SlotMachUI.TryChangeMap`(public)；按钮靠 `UpdateStandStatusUI` 里按 onClick 持久监听方法名 `"TryChangeMap"` 识别（**绑定已证实**，GameObject 名未知） | 无 | 见 Q2；**会换场景/付押金/`ChipRoundIndex++`，重复触发会重复付费** → 必须 SingleShot | 否 | M 或 U |
| 手动抽牌 Draw | `SlotMachUI.TryChoiceCard`(public)，按钮同样靠 `"TryChoiceCard"` 识别 | 无 | 草稿进行中直接 return；基本只在草稿关闭的 0.35-0.45s 间隙或 modal 之后有意义；会再扣 150 | 否 | M（实际不需要提供） |
| 用代币 | `CoinItem.OnPointerClick`→`UseSc`→`SlotMachUI.TryUseCoinToken(type, factor)`(public)；托盘 `MapSelect/CoinPrefab_i`（每个持有代币一个，4 类：bonus 重刷奖励通道 / misfortune 重刷灾厄通道 / multiplier `MapCal*=factor` / extra `RoleTable.Reward++`） | token 类型；multiplier 的 factor 来自 Coin 表行 `MultiplierFactor`（默认 2） | bonus/misfortune 仅草稿进行中；misfortune 不能用于前 4 抽；计数 `MisfortuneCoin/MultiplierCoin/BonusRewardCoin/ExtraRewardCoin`(public)；每个 (Level, 日夜) 发 5 个 | 否 | U（点 CoinPrefab_i）或 M `TryUseCoinToken` |
| 重抽快捷方法 | `EmbeddedRandomAgain` / `EmbeddedUseMultiplierToken{,2,5,10,20}` / `EmbeddedUseBonusRewardToken`(public) | — | 都转 `TryUseCoinToken`；预制体里是否有按钮绑它们未知（`Content/GetCard/Button(s)` 是候选） | 否 | M |
| 外援押注 Outsider Stake | `SlotMachUI.TryOutsiderStake`(2843)；候选按钮 `Content/ClockBoard/加注`（文本 "Stake"，未证实） | 玩家在**输入框**键入 Truth 水晶数，换 50 筹码/个 | 打开 `UIManager.ShowInputWindow`（`InputObj`/`InputWIndow`，**既不在 `GetAllUI()` 也不是 ModalWindow**，mod 的 `Popups.OpenModal`/`UiLite` 都看不到）；**花永久货币 Truth**，`dangerous` | 否 | 直接调 `slotMachineManager.TryApplyOutsiderStakeFromTruthOnce(n)`（M），别点按钮 |
| 祝福选择 | `BlessingChoiceGenerator.CreateBlessUI(SlotMachUI.transform, …)` → 预制体 `UI/BlessChoice`，3 选 1 | 选项 1-3 | 与奖励后的祝福同一个预制体，但**父节点是 SlotMachUI**；`Screens.BlessPicker` 只扫 `BattleRewardsUI` 的子节点 → 目前认不出 | 否 | U（复用 `blessing_pick_N`，需把父节点泛化） |
| 拉杆 | `SlotRod.OnPointerClick` | — | **空函数**；但它实现 `IPointerClickHandler`，会出现在通用 `ui` 转储里，点了没用 | — | 不要点 |
| 换日夜 | — | — | 不是玩家操作（见 Q2） | — | — |
| 结束本局 | `TopBarUI.CheckSlot`(public，按钮 `Content/Buttons/EndSlot`)→ modal "是否要结束此局" →`EndSlot`→`SlotMachineManager.EndGameWithAdvanceTimeFlow` | — | `dangerous`：结束并结算（记 `achievement_clear_slot`、发 Truth/经验/Time、清空卡组），房主走 `RpcGameOver`→`GameExitUI`（即已有 `run_end`）；随时可用（TopBar 常驻，战斗中是否隐藏未知） | 否 | U（`click EndSlot` + `modal_confirm`，**现有 mod 已能做到**） |

## 2. SlotSettlementUI（`UW/SlotSettlementUI.cs`，419 行）
- 出现：(a) 每个新一天开始前的**过场结算**（`OpenSlotSettlementUiBeforeDayMap`，`PendingOnConfirm = ShowMapSelect`，所有人都会见到）；(b) 非房主客户端的最终结算（`OpenChipSettlement` 的 else 分支）。**房主的最终结算走 `RpcGameOver`→`GameApp.GameOver`→`PlayerManager.UserCode_GameOver`→`GameExitUI`**，`GameExitUI` 无任何 Slot 代码，所以单机多半是现有 `run_end`（静态读码结论，待实测）。
| 操作 | 入口 | 备注 | 可逆? | 调用 |
|---|---|---|---|---|
| 确认并继续 | `SlotSettlementUI.ConfirmAndExit`(public，按钮路径未知) | 过场：回到老虎机；终局(b)：`EndRunAndReturnToMenu` → 非房主路径 `GameSaveManager.Delete()` + 回主菜单，`dangerous`(删存档) | 否 | U 或 M |
| 经验条动画 | `AnimateExpGainAsync` | 纯展示 | — | — |
价值 Med（过场必须点过才能继续）；工作量 S（`Popups` 表一行：窗口 `SlotSettlementUI` → 阶段 `slot_settlement`，按钮路径要实测）。

## 3. 价值与工作量
| 范围 | 价值 | 工作量 | 主要风险 |
|---|---|---|---|
| Classic 局里的老虎机 | 不存在 | — | — |
| Slot 模式可玩（`slot` 阶段：`slot_pick_N` / `slot_stand` / `slot_token_<type>` / `slot_end_run` + 祝福泛化 + 过场结算 + 输入框取消） | **Low**：需玩家等级>=25、单独的筹码赌局、没有通关，只产出 Truth 水晶；对"跑完 Classic"零帮助 | **M**（~170–230 行：Detect=`SlotMachUI` 激活；状态用 public 属性读：筹码/抽数/日夜/队列/词条/代币；通道卡用 `SlotMachSlotItem.GetDataConfig()`+文本；`canClick` 与 `embeddedDraftInProgress` 需反射）。不是 L：无拖拽、无多步选目标 | ① 选完后异步（手臂动画数秒）→ 要 `busy` 备注；② Stand 必须 SingleShot；③ 输入框不可见会卡死；④ 池化的 `SlotCardPre(Clone)` 复用，索引要按当次抽牌重算 |
| 只做"离开"（`slot_end_run`） | Med（防误入 Slot 后能脱身） | **S**：现有 `click EndSlot` + `modal_confirm` 已够；若要 id 化就 ~30 行 | `EndSlot` 会记通关成就并发奖励，是一次性结算 |
| 策略层（读灾厄/词条/倍率、算期望） | Low | L | 规则散在 SlotCal/SlotReward 表和骰子快照里 |

## 4. 与现有 mod（MapGate / MapNodes / Battle）的交互
- `MapGate.NotReady`、`MapNodes.Map()`、`Battle.IsActive`(48) 都用 `GetUI<MapSelectUI>("MapSelectUI")`；`UIManager.GetUI` 是按注册名 `uiList` 查（`UIManager.cs:1264`），SlotMachUI 是 `"SlotMachUI"` → null → 这三处在 Slot 里都**直接放行/不介入，不会误动作**。即便拿到了，SlotMachUI 预制体没有 `Map/NodeContent`、托盘里放的是 `CoinItem` 不是 `MapItem`，`Slots()/Choices()` 也只会返回空。
- 后果：`RuntimeGameplayAutomationService.GetLegalActions` 不进 `map_select` 分支 → 阶段 `unknown`（`State.cs:71` 会附 `ui` 转储，含 EndSlot、SlotMachSlotItem、CoinItem、SlotRod）。`Hub.Detect`/`Rest.Detect` 要求 `OnlyAmbientWindows`，SlotMachUI 打开时不满足，不会误判。
- 潜在坑：`Battle.IsActive` 里"MapSelectUI 活动则不是战斗"是为了盖住可能残留的 FightUI；Slot 里没有这层保护。若 FightUI 在 SlotMachUI 显示时仍 `activeInHierarchy`，会被报成空手牌的 `battle`。（待实测，见下）
- `modal` 已覆盖：EndSlot 确认、遗物栏已满确认、"筹码不足 Fight now/Settle now"（confirm=继续战斗，cancel=立刻结算；以按钮文字为准）。**不覆盖**输入框（Outsider Stake）。

## 5. 待实测的未知项
1. 实验档玩家等级是否 >= 25（否则 `mode_choice_survival` 只弹 caption，什么都不发生）。
2. Slot 屏幕上阶段/`ui` 转储到底长什么样（我从代码推的是 `unknown`）；Stand/Draw/加注/EndSlot/SlotSettlement 确认按钮的真实路径与绑定（`加注`→`TryOutsiderStake` 只是候选）。
3. FightUI 是否在 SlotMachUI 出现时已关闭（见 4）。
4. 房主终局到底是 `GameExitUI` 还是 `SlotSettlementUI`；`GrantChipSettlementRewardIfNeeded` 与 `GameExitUI` 自己的奖励是否重复发放。
5. `Content/GetCard/Button(s)` 里有哪些按钮；`embeddedPickSlotCount` 预制体取值；Slot 模式首次进入有没有教学浮层（Tutorial 链）。
6. `__SlotTransitionShopPending` 在 C# 里**从未被设为 true**（只有 false），"跨天商店/命运树/附魔"看起来是死代码，除非脚本置位。
7. TopBar 的 `EndSlot` 在战斗/奖励期间是否仍可点（若可点，Slot 局里任何时候都能结算）。
8. `MapScene*` 是否有 Lua 读者；Classic 的 Level 12-17 背景场景里有无可点击世界对象（预期无）。

## 6. 与 CLAUDE.md / 现有代码的出入
- `mode_choice_survival`（`Popups.cs:38`）命名误导：游戏里该按钮是 Slot/"Random Mode"（Chaos Paradise），代码里没有任何 "survival"（中文本地化未核对）。另有 `ModeList/SublimationMode`（"The Path of Sublimation"）未映射。建议改名 `mode_choice_slot` 并在 CLAUDE.md 注明"需玩家等级>=25，与 Classic 完全不同的循环，agent 不要选"。
- 反例没有发现：Trap 16/18（`map_continue` 过早、每 6 层自建路线）只适用于 Normal 的 `MapSelectUI`，不涉及 SlotMachUI。
- 调试控制台 `Commands.cs:2482` 的 `slot` 命令可直接加筹码（S 路线，等于作弊，不要用）。
