# 场景操作接口清单，以及「全部做成可调用」的评估

2026-10-13。来源：五份只读静态分析（[`inventory/`](inventory/)，共约 170 个界面/操作/节点条目）+ 当天 bot 的实战日志。
**标记**：✔ = 我核实过（读了代码或对过日志）；其余是静态推断，prefab 里的按钮绑定看不到，凡「候选」都要实机确认。

## 0. 结论

- **不建议全做。** 值得做的是 P0 + P1 共 11 项，≈ 950 行 C#（现有 mod 2734 行）；再加 P2 的开局配置与长期进度 ≈ 1400 行。其余是纯元进度、联网、作弊，或会毁档/退出游戏。行数是按已有模块的体量（休息室 163、事件 125、结算 95）估的，真正的成本在每项至少要一次实机遇到来验证。
- **「可调用」有三层，现状差别很大：**
  - T0 能点：通用 `click`，覆盖 `GetAllUI()` 窗口里实现了点击接口的组件。点不到：世界物体、拖拽、`UIBase` 之外的窗口（模态框、输入框）。
  - T1 有类型化 phase + 动作：已建模约 20 个屏幕。
  - T2 带参数的选择（买哪件、装备哪张、给哪张牌附魔）：几乎没有。
- **最大的缺口不是「少几个按钮」，而是一个断点：奖励/购买的卡进的是备选区，不进战斗牌组** ✔（`CardChoiceUI.cs:164` 写 `UnCardList`；`FightCardManager.cs:371` 开战只读 `cardList`）。没有装备动作，所有选牌、买牌、附魔对战斗都没有影响，bot 从头到尾在用初始牌组。
- **一个已量化的缺陷**：胜利后 `battle_won` 走不到，每场白等约 10 秒游戏自己开宝箱 ✔（今天 7 场战斗：最后一张牌到奖励画面 11–12 s，`battle_open_rewards` 0 次）。
- 建议顺序：P0 小修（≈ 300 行，先防空转和误伤）→ P1 让成长生效（≈ 650 行）→ P2 开局配置与长期进度（≈ 450 行）→ 其余不做。

## 1. 总表：场景 × 操作 × 现状

invoke：**U** 点 UI 路径　**M** 直接调 public 方法　**D** 拖拽/射线　**W** 点世界物体　**S** 写数据。effort：S ≈ 40 行，M ≈ 100–170 行，L = 多步/拖拽/大类。

| 场景 | 玩家操作 | invoke | mod 现状 | 价值 | effort |
|---|---|---|---|---|---|
| 主菜单 | 开始游戏 | U | ✔ `main_menu_start_game` | — | — |
| | 设置 / Mod 管理 / 退出 / Steam 链接 | U | 通用 `click` 点得到 | dangerous | 黑名单 S |
| hub（8 个 `SceneItem`） | StartGame | W | ✔ `hub_start_game` | — | — |
| | 7 个「书页」（结算后剧情对话） | W | ✘ | Low | 不做 |
| hub（14 种 `HouseItem` 按钮） | 成就领奖 | U | ✘ | Med | S–M |
| | 元商店 OutsiderShop（花 Truth/Time，永久） | U/M | ✘ | Med | M |
| | 仓库只读 / 图鉴 / 公告 | U | 公告 ✔ | Low | — |
| | CardEditor / ModManager / TeachMode（删教程档） | U | ✘ | Avoid | 不做 |
| 弹窗 | 公告关闭 / 新物品确认 / 模式选择 | U | ✔ | — | — |
| | 模态框确认 / 取消 | U | ✔ `modal_*`，但**后果不同**（§3 第 4 条） | — | S 加后果标注 |
| 模式选择 | 经典 | U | ✔ | — | — |
| | 「survival」= 实际是 Slot（Lv≥25，Lv 不够只弹提示） | U | ✔ 但**名字错** | Low | S 改名 |
| | 升华之路 Sublimation | U | ✘ 未映射 | Med？ | M，需单独评估 |
| | 剧情 Story | U | 代码里**无处理函数**，疑似空操作 | — | 标注 |
| 出发前 `game_entry` | 开局 | M | ✔ `game_entry_start`，但**绕过准备**，永远用默认值 | — | — |
| | 选职业 / 战备 / 核心·次要本源 | M（`ShowDetail`、`SelectVar`） | ✘ | **High** | M（≈ 120） |
| | 难度词条（跨局持久，可能让每局默默变难） | M（`AddSc`） | ✘ 状态都没报 | Med | M（≈ 100） |
| | 卡包开关（≥ 6 个） | M（`SetPackEnabled`） | ✘ | Med | S–M |
| 地图 `map_select` | 继续 / 摆放命运牌（8 选 4 并排序） | M | ✔ | — | — |
| | 取回已放置的牌（游戏支持，无交换） | M/D | ✘ | Low–Med | S（≈ 25） |
| | 看本段末尾是哪个精英/Boss、走到第几格 | 读 | ✘（`mapNodes` 故意不列固定框） | Med–High | S |
| | 重抽 / 跳过 / 回退 / 逃跑 | — | **游戏里不存在** | — | — |
| | 画笔/橡皮/清空（多人标注） | U | 点了会遮住整张图的指针 | dangerous | 黑名单 S |
| 战斗 | 出牌 + 单体目标、结束回合 | M | ✔ | — | — |
| | 选牌提示（弃牌 / 焚毁 / 选 N 张 / 保留）| M | ✔ **今天新增并实战验证**（`battle_select_*`） | High | — |
| | **职业技能**（每职业 1–2 个，EventTrigger 不是 Button） | M（反射设 `hitEnemy` 再 `TrueUse`） | ✘ | **High** | M |
| | **牌堆选牌器 DeckUI**（复制牌、事件选牌、附魔共用；无确认键，选满自动关） | U | ✘，且它**不锁 `canUse`**：打牌/结束回合动作仍被提供 | **High** | M，phase 必须抢占 battle |
| | 友方 / 自身为目标（`CanSelf` / `CanEnemy`） | M | ✘（`Battle.Enemies` 只认 `enemyList`） | Med–High | S–M |
| | 抽牌堆内容（顺序可知：末尾先抽） | 读 | 只有数量 | Med | S |
| | 加速模式（设置项） | S | ✘ | Med | S |
| | 重开战斗 | U+modal | 点得到 | save-scum | 默认不给，显式开关 |
| | 骰子 / 伙伴指令 / 局内遗物 / 逃跑键 | — | **不是玩家输入** | — | — |
| 战后 | 胜利 → 开宝箱 | M | ✘ `battle_won` 走不到（✔ 日志），白等 10 s | **High** | S（看 `chest.activeSelf`） |
| | 奖励 / 选牌 / 祝福 | M | ✔ | — | — |
| | 4 个特殊关卡赢了没有宝箱 | — | 会显示 `unknown` | Med | S，先看现象 |
| 事件 | 选项 1–4 | U | ✔ `event_option_N` | — | — |
| | **选项的效果文字**（只在悬停 `<subtip>` 里） | 读 | ✘，现在是盲选；有的不可逆（魔王线/教廷线/永夜线） | **High** | S |
| | 点完弹 DeckUI 选牌（事件 3、`*2012`） | U | ✘，卡在 `unknown` | High | 并入 DeckUI |
| 休息室 | 吃 / 离开 | W | ✔ | — | — |
| | `BreaksUI.ReturnHome`（主动结束本局） | U | 点得到 | dangerous | 黑名单 |
| **商店 / 天界赐福 / 血脉铭刻** | 买遗物·卡·祝福、刷新、卖牌、卖遗物、占卜、附魔、离开 | M（`TryBuy`、`Flushed`、`TrySell`、`EnchTryBuy`） | ✘ 只会点 ExitButton | **High** | M–L（≈ 200，一个 `Shops.cs` 覆盖三者） |
| **牌组管理 OutDeckUI** | 装备 / 卸下 / 销毁 / 卖（`ShowCard.MoveItem()` public） | M | ✘ | **High**（§0 的断点） | M（≈ 140） |
| 其它局内菜单 | 背包 / 状态 / 图鉴（只读） | U | ✘ | Low | 只需能关 |
| | 保险箱 SafeBox（写元存档） | M | ✘ | Low | 暂缓 |
| 结算 | 两次继续 | U | ✔ | — | — |
| Slot 模式 | 选奖励卡、Stand、代币、结束 | U/M | ✘，phase 为 `unknown` | Low（等级 25、无通关、只产 Truth） | M；只做「离开」S |
| 联网 / 元 | 聊天 / 表情 / 邀请 / ping（上传） | — | — | Avoid | 不做 |
| 开发控制台 `ConsoleLogic.Input` | 无门槛，含 `give` / `unlockall` / `connect` | M | — | 作弊 | 只留作测试夹具 |

## 2. 路线图

**P0 — 先去掉空转和误伤（全部 S，≈ 300 行）**
1. 胜利判定改看 `FightUI.chest.activeSelf` / `endfight.activeSelf`；`battle_open_rewards` 调 `ShowBattleReward()`。每场省 ~10 s。
2. 危险按钮黑名单（`ui` 清单 + `click` 都拒）：TopBar 的 Setting / ExitGame / EndSlot；SettingUI 的 ReturnMain / CloseTheGame / 反馈上传；MainMenu 的 ExitGame / Steam 链接；ModManager；公告里的外链；地图画笔三键；`BreaksUI.ReturnHome`。
3. `modal` 动作带后果：区分「继续游戏」「新游戏（删档）」「退出游戏」「放弃本局」，不再只有 confirm/cancel。
4. 模式选择：`mode_choice_survival` 改名 `mode_choice_slot`（注明 Lv≥25），补 `mode_choice_sublimation`，story 标注「无处理函数」。
5. 事件选项带 `<subtip>` 效果文字；`game_entry` 与 `map` 的只读状态（当前职业/战备/本源/词条/卡包；每格类型、敌人、`Level%6`）。
6. **recon 模式**：bot 遇到未建模窗口时自动 dump `ui --json` 与 `/world?root=…` 到 `.recon/`。bot 每局本来就会走进商店、附魔、天赋树、休息室、事件，跑几局就能把 §4 的未知项一次查完，不用逐个手动开。

**P1 — 让局内成长真正生效（M，≈ 650 行）**
1. 牌组管理：`deck_equip_K` / `deck_unequip_K` / `deck_sell_K`。没有它，P1 其余都白做。
2. DeckUI 选牌 phase `deck_select`：`deck_pick_K` / `deck_close`，抢占 battle（它不锁 `canUse`）；事件、附魔、复制牌共用。
3. `Shops.cs`：`shop_buy_N`、`shop_flush`、`shop_sell_*`、`shop_divine`、`ench_pick_N`、`shop_leave`。点击要在 mod 里**预检**钱够不够、备选区满没满：不够时游戏静默返回，满了会弹 modal，modal 确认后还会再叠开 OutDeckUI。`BlessPicker` 要从只找 `BattleRewardsUI` 扩到也找 `DestinyTreeUI`。离开是一次性的，要加 SingleShot。
4. 职业技能 `skill_use_N`（可带目标）。
5. 友方/自身目标。

**P2 — 开局配置与长期进度（≈ 450 行）**：职业/战备/本源动作、难度词条、卡包、成就领奖、OutsiderShop、Sublimation 模式（单独评估）。
**不做**：Slot 玩法（只留「离开」）、7 个书页、图鉴、仓库/保险箱、CardEditor、设置、Mod 管理、聊天/表情/邀请/ping、开发控制台、自动出牌、重开战斗（需要时做成显式开关）。

**架构**：不要写 25 个手写模块。简单窗口（结算过场、背包关闭、成就领奖…）进 `Popups.cs` 那种表；加一个**安全的通用兜底 phase**：未建模窗口里的可点项自动列为动作，但过黑名单。手写模块只给有状态的屏幕：商店、牌组、`game_entry`、技能。

## 3. 与 CLAUDE.md 不符或需补充的

1. 奖励/购买的卡进备选区，不进牌组 ✔。trap 15 只写了「背包满会拒绝」，没写这一点。
2. `battle_won` 走不到、每场白等约 10 s ✔（静态推断：`Fight_Win.Init` 同步把 `fightType` 改成 None，所以帧边界上永远读不到 Win）。
3. 模式：`survival` = Slot（Lv≥25）；story 无处理函数；Sublimation 没映射（trap 25 的「story / classic / survival」不准）。
4. 通用 `modal_*` 的后果不同：有存档时 **cancel = 新游戏，立刻删旧档**；MainMenu/Settings 的退出弹窗 **confirm = `Application.Quit()`**。`greedy.py` 现在只在确认键含「继续/Continue」时才确认，否则取消，所以不会误删，但这靠的是策略，不是接口。
5. trap 18 补充：路线不是「铺」而是从 8 张里选 4 张并排序，游戏支持取回，mod 没做。
6. trap 14（脚本战败北永不结束）与代码不符：`StatusManager:4681` 会回满血并在 0.3 s 后 Escape 结束（静态）。
7. 老虎机不是经典局的必经阶段，`MapScene2` 只是背景标签，没人读。
8. `game_entry_start` 直接调 `StartGame()`，用默认职业；profile 里残留的难度词条会让每局默默变难。
9. 事件内会弹 DeckUI 选牌，trap 23 没提。
10. 「魔女手记」节点大概率就是随机事件卡，不是新界面（`Map` 表读不到节点名，静态推断）。

## 4. 需要实机确认的（用 recon 一次跑完）

- 8 个 `SceneItem` 与 14 个 `HouseItem` 的真实路径（`/world?root=NewHouse`）；书页打开时 hub 是否仍报 `hub`。
- `GameEntryUI` 里 `MainParent` / `SecondParent` / `CardPackUI` / `CloseWindow` 的路径；partner 默认值是谁。
- 商店：买按钮到底接了 `TryBuy` 还是 `EnchTryBuy`（接错会扣两次钱）；`Content/Refresh` 在三种界面里是否都有；右键浮窗按钮路径。
- DeckUI 选牌器：有没有取消键；`PlayerWindow()` 此时是否真的为 true；提前关闭是否传空列表给回调。
- 胜利后 `chest` / `endfight` 何时变 active；4 个特殊关卡赢了之后是什么 phase。
- 职业技能：`UiLite.ClickObject` 能否触发 EventTrigger；每职业技能数；哪些牌用选牌提示（`GameConfigManager` 的卡表，运行时扫）。
- Slot 的 `unknown` 实际长什么样；实验档玩家等级是否 ≥ 25。
- 敌人回合触发的选牌提示会不会一直等到下个玩家回合。

调研 agent 里有一个写了纯 stdlib 的 UnityFS 解码器（读卡表、事件表），在当时会话的 scratchpad 里，**我没审过它的代码**，用之前先读。

## 5. 实现状态（2026-10-14，「全修」之后）

**实机** = 在实验副本里亲眼看到动作生效并回读过；**单测** = 纯逻辑（`tests/modlogic`）或 CLI 渲染有测试，但游戏里没走到。

| 项 | 状态 | 说明 |
|---|---|---|
| P0 胜利判定、危险按钮黑名单、modal 带后果的 id、模式选择改名、事件效果文字、`game_entry`/`map` 只读状态、recon | 实机 | `map` 在 level 13 读到：`playerBuilds:false`、起点 Event、终点 Fight、各格 kind |
| 牌组：`deck_equip/unequip/sell/destroy` | 实机：打开、装备、关闭；其余单测 | 上限在 mod 里预检（`DeckRules`） |
| 选牌 phase `deck_select` | 实机：神选的抽牌堆选牌；附魔/事件入口未走到 | `deck_pick_` 前缀与牌组窗口冲突，已用 `DeckRules.IsPickerAction` 分开 |
| 商店/天赋树/血脉刻印 `shop_*` | 实机：买遗物、买牌、离开、打开天赋树；`shop_refresh`、占卜选祝福、附魔选牌未走到 | |
| 职业技能 `skill_use_*` | 实机：无目标、指向敌人、指向自己、各类拒绝 | 神选两次「看不到效果」，原因未查明，消息里会如实说 |
| 自身/友方目标 `play --self` | 单测 | 实机只遇到过 `enemy` 和无目标的牌 |
| 开局配置 `entry_*` | 实机：职业、搭档、两个属性（含冲突时被游戏挪动）、卡包下限 6、关窗；启用卡包未走到（档里正好 6 个包且全开） | 难度词条只读 |
| 成就 `achievements`（`hub_open_achievements`、`achievement_claim_N`、`achievement_close`） | 实机：打开、领取（真理之晶回读）、拒绝、关闭 | 奖励一次性、不可撤销 |
| hub 状态与七个书页（`hub_visit_*`、`hub_page_close`、`hub_page_item_N`） | 实机：七页各自报对名字（图书馆/高塔/展览馆/储藏室/行政厅/温泉/魔女屋），按钮列表正确 | 第一版把七页都认成图书馆（见 CLAUDE.md trap 32） |
| 局外商店 `outsider_shop`（`outsider_buy_N`、`outsider_toggle_N`、`outsider_leave`） | 实机：两个页签读取、买一级属性（真理 672→652，下一级 20→40）、买一次性道具（弹窗接管 phase）、各类拒绝；**`outsider_toggle_N` 只有单测**（没有可开关的道具） | 花的是实验副本存档里的货币 |
| Sublimation | 只评估（§6），没写代码 | |
| 不做的 | Slot 玩法、七页的剧情内容、图鉴、仓库/保险箱、CardEditor、设置、Mod 管理、聊天/表情/ping、开发控制台、自动出牌、重开战斗 | 在 hub 里 `HouseRules` 拒绝并说明原因 |

**留给真机的后续**：附魔选牌（`CardEnchUI`→`DeckSelect`）、占卜祝福选择、`shop_refresh`、`deck_sell/destroy/unequip`、启用卡包、`play --self`、`outsider_toggle_N`。

## 6. Sublimation（升华之路）评估——只读代码，没写、没跑

- **入口**：`ModeChoiceUI.SublimationMode()`（`ModeChoiceUI.cs:637`）与经典局同一套存档逻辑——有缓存存档就 `ReturnGame("Sublimation")`，否则 `CreateNewSave("Sublimation")`；存档槽独立于经典局。`mode_choice_sublimation` 已经列出，`Popups.cs` 里的说明是「未建模」。
- **运行时**：`SublimationManager`（`IModeManager`）。30 关封顶（`Level >= 30` → `GameExitUI`，即现有 `run_end`）；`Level % 6 == 0` 时生成固定节点（`GeneratrMap`：第 0/2 段「普通事件 + 精英」、第 1/3 段「普通事件 + 首领」、第 4 段「休息室 + 首领」；`GameVar.ExLockDes` 追加精英，`ExDeleteDes` 减少随机节点）；`Level % 12 == 11` 与 `Level == 29` 走专门事件 `event_999` / `event_1000`；事件池排除 id 含 `Sub` 的行。地图、奖励（`SetReward`/`RandomSetReward`）、结算复用 `MapSelectUI`、`MapTree`、`BattleRewardsUI`、`GameExitUI`。
- **推断**：大部分屏幕应该已被现有 phase 覆盖；可能新出现的是 `event_999/1000` 与开局（`InitRoleTable` / `CardCountSet` 改了角色表和卡数上限，`game_entry` 是否照常出现、职业/搭档是否由模式决定，没有读到）。`NowDice` 是一个骰子对象，没读到玩家要不要点它。
- **没有验证过的**：以上都是反编译的静态阅读，没有在游戏里进入过；实验档是否解锁、Lv 要求（没看到），未知。
- **建议**：不预先写代码。让 bot 带 `--recon` 从 `mode_choice_sublimation` 进一局（实验副本，独立存档槽），把出现的 `unknown` 界面落到 `.recon/`，有了实物再决定要不要建模；多半只需要给 `Popups` 加几行表、给 `event_999/1000` 一个事件说明，不到 100 行。风险：它占用 `Sublimation` 存档槽，经典局存档不动。
