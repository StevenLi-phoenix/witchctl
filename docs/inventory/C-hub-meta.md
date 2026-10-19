> 2026-10-13 静态反编译分析，由只读调研 agent 产出，**未经实机验证**；「候选」= 只有名字或 public 方法证据，没见到 prefab 绑定。
> 合并后的结论、已核实项和路线图见 [../scene-interfaces.md](../scene-interfaces.md)。文中的 scratchpad 路径只在当时的会话里存在。

# inv-C：hub（house）与局外界面（静态分析，只读）
范围：HouseManager/HouseUI/HouseItem/SceneItem、MainMenuUI、ModeChoiceUI（顺带，因为它是 hub→局的必经点）、GameEntryUI/SelectHardUI/CardPackUI、Storehouse/Warehouse/OutDeck/OutsiderShop，以及其余 meta 界面。
来源：decomp + windows.json；另对 `aa/catalog.bin` 做了 `strings`，只用来确认资源 key（`UI/House`、`Tutorial/House`、`House_*` 本地化表），**prefab 内部的对象名读不到**（bundle 已压缩，无 UnityPy）。
记号：U/M/D/W/S 同 brief；**[候选]** = 只有名字/public 方法证据，没看到 prefab 绑定。

## 0. 先看这里
1. **`game_entry_start` 绕过了整个"出发前准备"**。`GameEntryUI.StartGame()` 直接开局，所以每局都在用默认值：第一个职业、第一个已解锁战备、核心=Strength/次要=Wisdom，以及 profile 里**持久化**的 `HardTags`、`UseCardPack`（见 §2）。职业/战备/两个本源/难度词条/卡包都是 public 方法或左键点击就能选，影响起手牌、起手祝福、属性 +2、结算「深渊」加成。这是 hub 区域里最值得做的一块（M）。
2. **ModeChoice 与 mod 的假设不符**：Story 按钮在代码里没有对应处理函数（只有一个空的 `ShowUnDone()`，疑似挂在它上面；`MapManager.SetMap` 也没有 Story 管理器）；`mode_choice_survival` 实为 SlotMode（"随机模式"），**Level < 25 时只弹 caption 不进**；第 4 个模式 **Sublimation（升华之路，`SublimationManager`，1092 行）完全没被 mod 暴露**。另外有存档时的弹窗里 **cancel = "New Game"（立刻删除该模式的旧存档）**，agent 若把 `modal_cancel` 当"安全取消"会毁掉进行中的局。
3. **hub 的 7 个"书页"（图书馆/塔/展览/商店/管理处/温泉/魔女之家）是对话记录页**（`HouseDialogueManager`，结算后排队的剧情聊天，玩家台词要点击继续），对打局无用。真正有用的 hub 功能只有 StartGame、OutsiderShop（永久解锁，花 Truth/Time）、成就领奖（给 Truth/Time）。**危险项集中在 MainMenu/Settings/ModManager/Announcement/TopBar**（§4）。

## 1. Hub（`GameApp.HouseItem` = NewHouse，脚本 `HouseManager`）
到达：`main_menu_start_game` → `MainMenuUI.StartGame` → `GameApp.StartHouse()`（**`IsTutorialCompleted.Count==0` 的新 profile 直接进教程局，hub 根本不出现**）；`GameApp.ReturnToMenu` 之后也回这里。`OnEnable`：刷新 HUD（`levelText/coinText/TruthText/TimeText` ← `GameRuntimeData.Level/Exp/Money/Truth/Time`）、`AnnCheck()`（`playCount>=1` 且有未读公告 → AnnouncementUI，已被 `Popups` 覆盖）、`ChangeNight()`（强制白天）。

**1a. 世界物体 `SceneItem`（W，collider + `IPointerClickHandler`；`Hub.cs` 已证明 `UiLite.ClickObject` 可点）。枚举只有 8 个：**

| SceneItemType | → `HouseManager` | 效果 | 价值 |
|---|---|---|---|
| StartGame | `StartGame()`（public） | 守卫：`GameServer.EndCommit` 为 false → caption"请等待结算完成"并返回；`ModeChoiceUI` 已开 → 静默返回；否则 `ModeChoiceUI.Init()`。已建模 `hub_start_game` | High |
| OpenLibrary / OpenTower / OpenExhibition / OpenStore / OpenAdministration / OpenSpring / OpenWitchHouse | `OpenWindowByIndex(0..6)` → `Openwindow()` 打开书页 `WindowManagerItem`（MUIP `WindowManager`，页签 `WindowButtonParent` 的子物体名 = 本地化键，页内容 `WindowItemParent/<同名>`） | 书页内容：`HouseDialogueManager`（`Type` 默认"温泉"）历史对话 + 结算后新对话（`success_/fail_` 行，初始排 2 条，玩家台词需点击）；页内是否还有 HouseItem 按钮 **未知**（见 §6） | Low |
物体的真实 GameObject 名（= `name.Localize("House")` 的键）**代码里没有**，需要 `/world?root=NewHouse`。

**1b. UI 按钮 `HouseItem`（U；`ButtonManager` + `IPointerClickHandler` → `HouseManager.ClickItem`）。枚举 14 个：**

| HouseItemType | 效果 | 价值/风险 |
|---|---|---|
| OutsiderShop / cardShop / rollShop | `OutsiderShopUI`（全部 / `InitType("Card")` "借阅图书" / `InitType("Roll")` "唤醒本源"），§3 | Med |
| task | `AchievementUI`（成就+领奖，§3） | Med |
| store | `StorehouseUI.Show()`，只读 `GameRuntimeData.CardData/RelicData` | Low |
| Dictionary | `DictionaryUI` | Low |
| announcement | `AnnouncementUI.ShowAnnouncement()` | Low（已建模关闭） |
| CardEditor | `CardEditorUI`，确认后扣 Truth（50×稀有度）把自制卡加进 `CardData` | Avoid（L，表单+浮窗） |
| ModManager | `ModManagerUI` | **dangerous**（§4） |
| shadow | `FirstTalk` 事件（仅教程未完成时） | Low |
| TeachMode | modal → `ClearCompletedSpotlightTutorials()` + `ModeChoiceUI.TeachMode(needDelete:true)`：**删除 Teach 存档**并开教程局 | Avoid |
| GameMenu / crystalBall / Achievement | `HouseManager` 里是空 case | — |
HouseManager 其余 public（可能是 prefab 按钮的绑定目标，[候选]）：`ReturnMain()`（隐藏 hub → `MainMenuUI`）、`ShowTask()`、`OpenSetting()`、`CloseBook()`。`HouseUI`（带镜头滚动的旧 house，`GameMenu→ReturnMenu`、`task→TaskUI`）是游戏自带 phase 模型依赖的那个，与 NewHouse 并存；CLAUDE.md trap 19 的说法成立。

## 2. 出发前：ModeChoiceUI → GameEntryUI（局内之前所有会改变结果的选择）
**2a. ModeChoiceUI**（`mode_choice_*` 已建模 4 个；`Init`：`playCount==0` → 自动 `TeachMode()`）

| 按钮 `ModeList/…` | 处理 | 备注 |
|---|---|---|
| NormalMode（经典） | `NormalMode()`：有缓存存档 → modal(confirm="Continue Game"→`ReturnGame`；**cancel="New Game"→`CreateNewSave()`**，`mustChoose:false`)；否则 `CreateNewSave()` | `CreateNewSave` 立刻 `DeleteExistingSavesForMode`；Steam 已初始化时 `StartLobby` 建 **public Steam 大厅(4人)**，lab 里 Steam 未初始化走 `StartLocalHost` |
| SublimationMode | `SublimationMode()`，同上模式 | **mod 未暴露**；自己的 `SublimationManager`（`GeneratrMap/MapItemInit` 预填 6 格节点、`WinTheGame`、`RandomSetReward`），是否需要玩家铺路未读透，需单独评估 |
| SlotMode（随机） | `Level<25` → caption"25级解锁"并返回；否则同上 | mod 的 `mode_choice_survival`；归 inv-slot |
| StoryMode | **无处理函数**（`ShowUnDone` 为空）[候选：prefab 里指向它] | mod 的 `mode_choice_story` 大概率是空操作 |
| 教程 | `TeachMode(needDelete)`，仅教程/HouseItem 触发 | — |
后续都走 `Close()` → `ShowUI<GameEntryUI>().Init()`。

**2b. GameEntryUI**（phase `game_entry`；`Init()` 里 `career=partner=null`、`selectHardUI.Init()`、`cardPackUI.Init()`、默认点 Strength/Wisdom）

| 操作 | 入口 | 参数 | 前置/副作用 | 可逆 | 调用 |
|---|---|---|---|---|---|
| 打开准备窗 | `OpenChoice()`→`OpenWindow()`；`Content/1P` 的 onClick（Init 里注册，仅自己的槽） | — | 要 `RoleTable.Instance` 且 `!IsStarted`；隐藏 `ForeBack/Button`+`ExitGame`，置 `HasPrepared`；`CloseWindow()` 还原（按钮路径未知） | 是 | U `Content/1P` / M |
| 选职业 | `ShowCareer.OnPointerClick`→`ShowDetail(item,"职业选择")`（public，设静态 `career` 与 `RoleTable.Career`）；候选列表 `showCareers`（public，已解锁；`playCount==0` 只剩第一个） | 职业序号 | 决定技能/`SanMax`（`CheckCareer`：MaxSan=SanMax+`Gain.exMaxHp`） | 是 | U `Window Manager/Windows/职业选择/Content/Middle/View/Content/<克隆>` / M |
| 选战备(partner) | 同上 `"战备"`，列表 `showPartners` | 序号 | `CheckCareer` 把 `partner.Bless` 加进 `blessingConfigs`（起手祝福）；窗口一开 `ShowDetail(showPartners[0])` 已默认选第一个 | 是 | U / M |
| 核心/次要本源 | `FightVarItem`(+`SwitchButton`)→`SelectVar(var,"Main"/"Second")`（public）；var∈Strength/Lucky/Perceive/Wisdom；两者不能相同（相撞时自动点邻居） | var | 局加载时 `NormalGame()`：两项各 `VarsMap+=2`，并各加 2 张起手牌（Strength=burningcard_2+elementscard_9；Lucky=card_5+card_9；Perceive=universalcard_16+card_6；Wisdom=card_3+elementscard_1），总数<12 再补 card_1+card_2 | 是 | U（路径在 `MainParent/SecondParent`，序列化字段，名字未知）/ M |
| 难度词条 | `OpenSelectHard()` ← `ForeBack/Statue` [候选] | — | `playCount<1` → modal"通过更多冒险解锁"；仅房主 | `SelectHardUI.Hide()` | U |
| 卡包 | `CardPackUI`（GameEntryUI 子物体）`CardPackItem`(SwitchButton)→`SetPackEnabled(id,bool)`；`ShowInfo(item)` 看包内容 | 包 id | 至少保留 6 个（否则 modal）；不含锁定包与 cardpack_13；`OnDestroy` 时写回 `GameRuntimeData.UseCardPack` 并 Save | 是 | U / M |
| 出发 | `ForeBack/Button`(SwitchButton→`ChangeReady`) **或** `StartGame()`（mod 现用） | — | `ChangeReady` 首次按只弹"出发前请做好准备"modal(confirm=`OpenChoice`)，除非教程；`StartGame`：`!isHost`/`waitCount<连接数`/`selectedSave==null` 时**静默 return**；把 `SelectHardUI.UseSc` 展开成 `selectedSave.HardTags/HardLevel`；`Career==null` 则 `CheckCareer()` 用表第 0 行；`ShowWaitingUI` 最多等 10 s 再 `PlayerManager.StartGame()` | 否 | U / M |
| 离开 | `ExitGame`→`ReturnHouse()`→`GameApp.ReturnToMenu`+`QuitLobby` | — | 旧存档**已在 `CreateNewSave` 被删** | 否 | U |
| 邀请 `Content/{2,3,4}P` | 非自己槽的 onClick → `SteamFriends.ActivateGameOverlayInviteDialog` | — | 多人，抢焦点 | — | **Avoid** |
| 表情 `Content/EmojiPanelUI` | `CreateEmoji/ShowEmoji` | — | 联机广播，会累计成就计数 | — | Avoid |

**2c. SelectHardUI**（难度词条；`UseSc` 是 static，进 `Init()` 时从 `GameRuntimeData.HardTags` 读，`OnDisable` 写回并 Save → **跨局持久**）

| 操作 | 入口 | 说明 | 调用 |
|---|---|---|---|
| 增/减一层 | `HardItem` 左键 `AddSc`（计数 0..MaxCount 循环回 0）/ 右键 `RemoveSc`；M：`SelectHardUI.AddSc/DeleteSc(HardItem)` | 锁定词条 → modal"需要在唤醒本源购买"。`UiLite` 只发左键，用 N 次左键即可到任意层数 | U / M |
| 重置 | `Content/ResetButton`→`ReSetHard()` | 清空 `UseSc` | U |
| 保存/关闭 | `Content/SaveButton`（"确定保存"）[候选 `Hide()`] | 真正的持久化在 `OnDisable` | U |
| 切页 | `Content/WindowManager/Buttons/难度｜测试` | `Type=="Test"` 的词条在第二页 | U |
数值：每条 `Level×5×层数` 累加成 `AddReward`（显示 `100+AddReward%`，上限 100）；结算「深渊」加成 = `((1+Difficulty/5)×(1+min(EXHard,100)/100)−1)×总分`（`GameExitUI`），`EXHard=Σ Level×5`。有 `FightScript` 的词条会作为遗物进战斗（`BlessingRelic.Init`），`UseScript` 开局执行（`ApplySaveSc`）→ **更难**。
**状态必须暴露**：profile 里残留的词条会让每一局默默变难；至少要在 `game_entry` 的 state 里报 `UseSc`（id/name/level/max/count）与 `AddReward`。

## 3. 局外进度与局内关联界面
| 界面 | 到达 | 操作（入口） | 价值/风险 | 工作量 |
|---|---|---|---|---|
| OutsiderShopUI（"唤醒本源"/"借阅图书"） | HouseItem OutsiderShop/cardShop/rollShop | 选物品 `SetCurrentItem(OutsideShopItem)`（点击 [候选]）；买 `Content/描述/Content/Buy`→`TryBuy()`→`BuyCheck()`；已买的再点=`UseCheck()` 开关；货币 **Truth/Time（游戏内，非真钱）**，价格 `Price×(已买次数+1)` 或 `TimePrice`；`BuyScript` 解锁职业/战备/词条/卡包/`Gain` 加成 | Med（长期：更多职业/战备/词条，`Gain.Strength…`）；花费永久、不可逆 | M（~150 行；风险=物品多、状态列表大） |
| AchievementUI + AchItem | HouseItem task / `HouseManager.ShowTask` | 每条 `Button`(ButtonManager，仅 `当前≥目标且未领` 时可点)→`AchItem.GetReward()`：`Truth` 或 `Time` += Reward，写 `Gain[<id>Reward]=1` | Med（给 OutsiderShop 攒货币）；安全 | S–M（~60 行） |
| StorehouseUI/StorehouseItem | HouseItem store | 只读列表（点击空实现），悬停看描述；数据 = 休息室 `SafeBoxUI` 的局外仓库（`CardData/RelicData`） | Low | S（只读 state） |
| OutDeckUI(+ShowCard) | **局内** `TopBarUI.OnhasCardDeckBtn`（`Content/Buttons/CardBack` [候选]）、`CardChoiceItem`/`TopStatusItem` 也会打开 | 装备↔备选 `ShowCard.MoveItem()`（**public**，M）；右键浮窗(`FloatingWindow`)才有 move/Destroy；拖拽 `AddToList`(D)；`DecomposeItem()` 花 20+金币删牌，`SellItem()` 在商店/命运树/附魔界面卖牌；上限 `CardTopCount/CardBottomCount/MaxAlCardCount`（陷阱 15 同源） | **High（局内牌组管理，目前完全没建模）**；归 economy 组 | M（列表 + move/destroy，~140 行） |
| WarehouseUI/WarehouseItem | 休息室 `LeftBox`(`BreaksUI.ShowWarehouse`) | `WarehouseItem.TryMove()`(public) 经 `RpcSendItem/RpcGetItem` 与共享仓库互传，有 `MoveCheck` 限制 | Low（联机共享仓） | — |

## 4. 其余 meta 界面（紧凑表）与危险项
| 界面 | 到达 | 内容 | 危险操作 | 价值 |
|---|---|---|---|---|
| **MainMenuUI** | 启动/`HouseManager.ReturnMain` | `background/TextList/`{StartGame,SettingTable,ModManager,Credits,ExitGame}、`RightTop/Steam`、`Version`。mod 只建模 Start | `ExitGame`→modal→`Application.Quit()`（**`modal_confirm` 会退出游戏**）；`RightTop/Steam`→`OpenWebsite`=`Application.OpenURL`（开浏览器抢焦点） | Low |
| **SettingUI** | MainMenu/`HouseManager.OpenSetting`/ESC/`TopBarUI.ShowSetting` | 音量/语言/分辨率/窗口/按键(`ResetButton`)；`Save/Apply` | `CloseTheGame`→`Application.Quit()`；**`ReturnMain()` 在局内=`TopBarUI.ReturnToMenu()` 无确认放弃本局**；`SendFeedBack/SendFeelBack`→Supabase 上传文本+错误日志（需 Steam+联网，空文本被拒）；改分辨率/窗口会动隐藏窗口 | Avoid |
| **ModManagerUI** | MainMenu/HouseItem | 本地 mod 开关(`ToggleLocalMod` 直接改 `Mods/<名>/ModConfig.json` 的 `Enabled`)、Steam 创意工坊查询/同步下载 | 关掉 WitchCtl 自己（下次启动失效）；`SteamWorkshopDownloadService` 下载 | Avoid |
| **AnnouncementUI** | hub `AnnCheck`/HouseItem | 列表项 `SwitchButton`→`ShowWin`（已读标记写 `Gain["Ann<id>"]`） | 顶部 `Button`("跳转文档")→`OpenDoct/OpenChoice`=`Application.OpenURL`（开浏览器） | Low |
| LanguageUseUI | 新 profile 首次启动（`Gain.SetLanguage==0`） | `ChangeChinese/Tra/Ko/Ja/En` + `ReturnMain`(关闭+Save) | — | 已被 `greedy.py`/`live.py` 用 `click` 处理 |
| DictionaryUI(+Dict*Item) | HouseItem Dictionary/`TopBarUI.ShowDict`(局内也能开) | 卡牌/遗物/祝福三页检索（`Retrieve`、`SortingBy*`、`ShowInfo`），纯展示 | — | Low（要数据不如直接读 config 表） |
| IllustratedBookUI / ResultUI / TutorialUI / LoadingUI | **全库无调用者**（prefab 绑定/遗留；LoadingUI 是启动场景对象） | — | — | Low |
| AcknowledgmentsUI | MainMenu Credits | 滚动字幕，`Back`/ESC 关 | — | Low |
| ChatUI / EmojiPanelUI | 联机时 `PlayerManager` 打开 | `SendChatMessage`、表情广播 | 联网 | Avoid |
| CardEditorUI | HouseItem CardEditor | 名称/描述输入框、稀有度、费用、效果浮窗、图标选择；确认扣 Truth | 永久花费 | Avoid |
| TopBarUI.TryReturn | 局内 | modal"Return to the main menu?"确认→放弃本局（`SafeBoxUI.ClearList`+`GameApp.ReturnToMenu`） | 放弃局 | Avoid |
**被动联网**（非操作触发）：`GameSaveAnalyser.UpdateToSupabase`（`GameServer.EndGame`）、`LatencyRecorder`、`BugReporter` 都以 `STEAMBUILD && !OFFLINE && SteamManager.Initialized` 为前提；lab 里 Steam 未初始化（CLAUDE.md trap 8）所以不触发，装进真 Steam 副本就会触发。

## 5. 汇总：价值 / 工作量
| 项 | 价值 | 工作量 | 主要风险 |
|---|---|---|---|
| `game_entry` 暴露 state（当前职业/战备/本源/`UseSc`/卡包/`AddReward`）+ 职业·战备·本源动作 | **High** | M（~120 行，调 `showCareers/showPartners/ShowDetail/SelectVar`） | `Start()` 与 `Init()` 的执行顺序决定 partner 默认值；`MainParent` 视觉与 `SelectVar` 的同步 |
| 难度词条（列表/加减/重置） | Med（+≤100% 深渊奖励，但更难） | M（~100 行） | 锁定词条、`playCount<1`、`Test` 页 |
| 卡包开关 | Med | S–M | 最少 6 个 |
| ModeChoice 补 Sublimation + 修 story/survival 标注 + 新增 modal 标注 | High（避免毁档/空点） | S（改 `Popups.cs`） | Slot 的 Level 门槛 |
| OutDeckUI 牌组管理（局内） | High | M | 归 economy 组 |
| OutsiderShop 买/用 | Med（长期） | M | 状态大、不可逆花费 |
| 成就领奖 | Med | S–M | — |
| hub state 加 HUD 数值(Level/Exp/Money/Truth/Time) | Low–Med | S | — |
| 书页/字典/仓库/设置/聊天等 | Low / Avoid | — | 见 §4 危险 |

## 6. 需要实机确认（`/world`、`ui` dump）
1. `GET /world?root=NewHouse&depth=4`：8 个 `SceneItem` 的真实名字；14 个 `HouseItem` 各挂在哪一页；打开一页后 `UiLite.OnlyAmbientWindows()` 是否仍为真（书页 `WindowManager` 不是 `UIBase`，hub phase 可能在书页打开时仍报 `hub`）。
2. GameEntryUI：`MainParent/SecondParent`、`CardPackUI`、`CloseWindow` 的实际路径；`ForeBack/Statue` 点击是否真调 `OpenSelectHard`；`SaveButton` 绑定；partner 默认是 `showPartners[0]` 还是 `CheckCareer` 的表第 1 行（`Start` 与 `Init` 谁后执行）。
3. lab profile 的 `playCount/Level`、已解锁职业与战备数、`SelectHardUI.UseSc`、`UseCardPack` 现值（是否已有残留词条）。
4. `ModalWindowManager.TryClose`（MUIP，未反编译）：对 `mustChoose:false` 的"继续/新游戏"弹窗按 ESC 是否触发 cancel 回调（= 新游戏）。
5. StoryMode 按钮是否可点/可见；`HouseManager.StartGame` 在 run_end 后 `EndCommit` 为 false 的窗口多长（`SingleShot` 会把重试挡 10 s）。
6. 书页里除对话外还有什么按钮。

## 7. 与 CLAUDE.md 的出入 / 顺带发现
- trap 25"story / classic / survival"：实为 Normal/Sublimation/Slot(Level≥25)/Story(无处理)；见 §2a。
- 通用 `modal_confirm/cancel`：有存档时 cancel=新游戏(删档)，MainMenu/Settings 的退出弹窗 confirm=退出游戏。建议 `Popups.DetectModal` 按弹窗文字/按钮文字给出带后果的 id（如 `modal_continue_game`/`modal_new_game_destroys_save`），或对这两类拒绝。
- 游戏内置**开发控制台**：`ConsoleLogic.Input("cmd args")` 是 public static，反射调 `Commands.<名>`，代码里无任何门槛（含 `give`、`unlock`、`unlockall`、`debugmoney`、`variable`、`load`、`dialogue`、`showann`、`connect <addr>`=起网络客户端）。只适合做测试夹具，不是 agent 动作（等于作弊/联网）。
- "Not done" 补充：真 Steam 副本上 `CreateNewSave` 会建公开大厅，装进去前需要用户决定。
