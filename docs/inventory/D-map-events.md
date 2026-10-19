> 2026-10-13 静态反编译分析，由只读调研 agent 产出，**未经实机验证**；「候选」= 只有名字或 public 方法证据，没见到 prefab 绑定。
> 合并后的结论、已核实项和路线图见 [../scene-interfaces.md](../scene-interfaces.md)。文中的 scratchpad 路径只在当时的会话里存在。

# Inventory D — 地图 / 节点 / 事件 / 对话 / 世界场景（静态分析，未运行游戏）

**方法备注**：游戏配置表（Map/Event/Level/Dialogue/Hard…）在 Addressables 的 UnityFS(LZ4HC) 包里，`UnityPy` 没装。我写了 60 行纯 stdlib 解码器放在
`<scratchpad>/ub/{unityfs,tbl,multi}.py`（`python3 -I tbl.py <bundle> [列号] [宽度]`），只读 `.lab/.../dataconfig_assets_dataconfigs/{data,text}/*.bundle`，
所以下面的节点池、事件脚本、对话分支是**读到的真数据**，不是猜的。其他 inv-* 同事可直接复用来读 card/relic/blessing/shop 表。
`*` 开头的 Id（`*26`、`*999`…）= 锁定 id（`RandomPool` 过滤 `IsLocked`；解析在未反编译的 Witch.Core 里，故“候选”）。

## 1. 一局的结构（NormalMapManager，Normal 模式）
`Level` = 已进入的节点数（`CmdNextMap` 里 `Level+1`）。32 个节点，6 个一段（chunk），每段 `Level%6==0` 时生成并让玩家铺路线（trap 18）。
| 位置 | 内容 | 必选? | 进入的屏 |
|---|---|---|---|
| 每段 slot0（Start） | 固定剧情事件：chunk0 `event_2001`；chunk1 `2002`(选魔王线/教廷线/永夜线，写 `EndBoss`/`HasSix`，不可逆)；chunk2-5 随 `EndBoss`/`Branch` 取 2003-2020 | 必 | EventUI |
| slot1-4（自由位） | 从 8 张候选“命运牌”里**选 4 张并排序**（`7-ExDeleteDes` 次加权抽样 + 必有 1 张 `Breaks`） | 全可选 | 见下表 |
| slot5（End） | chunk0/2 = 精英；chunk1/3 = 首领(按幕池抽)；chunk4 = 首领 `map_31`/`1004`/`1005` 随机；`ExLockDes`(难度词条 hard#2) 每级把 1 个自由位变精英 | 必 | FightUI |
| 末段 Level30-31 | `SetSpeciaMap`：只有 2 个真节点 = 终章事件(2016-2020) + 终局 Boss(`EndBoss`: map_0 永夜化身 / 999 魔王 / 1001 圣械审判机关 / 1002 历战勇者)。画面上 4 张 BreakPrefab 仅是摆设(候选，需实机确认) | 必 | EventUI → FightUI |
| 自动插入(非节点) | `Level%12==11` 或 `==29`（即 11、23、29，各是第 1/3/4 个 Boss 之前）**不显示地图**，`RpcLoadMap("event","event_999")`「黑潮-序幕」：①回血30%/不回 ②再选「驻足购买」(开 ShopUI)/「继续前进」；同时 `CmdChangeHide()` 把 `InHighTide`(黑潮) 置 true（敌人×1.3、Boss 奖励 rarity-4 遗物+高阶祝福） | 必 | EventUI(+ShopUI) |
| 终局之后 | 4 场终局战(`level_*0/10046/10048/10051`)胜利：不开宝箱，`InHighTide=true`；`*0`、`10048` 有对话 `Winlevel_*` → `StartLevel("build","break")` 休息室 → `BreaksUI.Close` 里 `WinTheGame()` → `GameExitUI`。10046/10051 之后谁推进代码里没找到（见未知项） | 必 | Dialogue→Breaks→GameExitUI |
`Level>=32` → `ReadyToChangeMap` 直接 `GameExitUI`。固定节点 12 个（6 开场事件 + 6 固定战），自由节点 5×4=20 个。其他模式：Sublimation 30 层(终章 `event_1000`)、Teach 12 层(脚本化路线+`event_1001`+boss map_1000)、Slot=另一个 manager（inv-slot 负责；`SlotMachScene` 在 Normal 里只是第 12-17 层的背景）。

**自由位候选的构成（我用 RandomPool 逻辑做了蒙特卡洛，单段 8 张）**：≈2 精英 + 2 普通 + 1-2 魔女手记(随机事件) + 2 建筑(商店/天界赐福/血脉铭刻) + 1 一息安隅(Breaks)；
精英/普通/首领按“幕”(= `Level/12`) 过滤（act0: 精英 3/5/6/8/11、普通 7/9/10/17/45/46；act1: 精英 4/16/19/23/33/34/35/44、普通 15/18/20/37；act2 池很小：普通 21、精英 1003；−1 = 各幕通用：普通 38-41、精英 42/43、建筑 shop/tree/Breaks/ench）。
每张卡 = Map 表一行 → `Type`（Fight/Event/Build）+ `NodeId`；卡面带敌人攻击/血量（`MapItem.Init` 按 `SumOfEnemyPositive`、`EXEnemyHp/Atk`、`Level/12−幕` 算出，已在 `mapChoices.label` 的文本里）。

## 2. 节点类型 → 打开什么（`Commands.load`，MapManager.CmdNextMap 用 `Type.ToLower()`+`NodeId`）
| 节点 | NodeId | 打开 | 备注 |
|---|---|---|---|
| 普通/精英/首领/无名 战斗 | `level_100xx` | `GameApp.StartFight` → FightUI（背景 = `UI/Scene/<SceneType>`） | `Level` 表 `Note`=普通/精英/boss/无名；无名=终局多阶段战 |
| 魔女手记 | 抽到的 `event_N`（已见过的本局不再抽） | EventUI | 17 个可抽事件：1,2,3,4,8,9,10,11,12,13,14,16,17,18,19,20,21 |
| 模拟战(IsFake) | 仅 `fakefight` | `GameApp.StartFakeFight` | 代码里唯一来源是事件 13（模拟考核）；败北=`Escape`，不扣命。CLAUDE.md trap 14 说“第二场剧情战是 fake”——我没能在代码/数据里找到别的来源，可能就是事件 13（待实机核对） |
| 旅行商人 | `shop` | ShopUI | inv-economy |
| 天界赐福 | `tree` | DestinyTreeUI | 同上 |
| 血脉铭刻 | `ench` | CardEnchUI | 同上 |
| 一息安隅 | `Breaks` | `GameApp.StartBreaks()` 世界场景（Rest.cs 已建模） | |
| 奖励 | `reward` | BattleRewardsUI | 仅脚本调用 |
`MapManager.TryChange()` 在 Shop/CardEnch/DestinyTree/EventUI 任一打开时**不**推进（0.3 s 后检查）。

## 3. MapSelectUI（map_select）所有操作
Reached：每个节点结束后 `ReadyToChangeMap` → `ShowMapSelect`（CurtainTurnUI 拉幕）。`Level%6!=0` 时路线已定，只有“下一步”。
| operation | entry point | params | preconditions & side effects | reversible? | invoke by |
|---|---|---|---|---|---|
| 继续/出发 | `Next` ButtonManager → `MapSelectUI.TryContinue()` → `SelectMap` → `CmdReadyToNextMap` | – | 所有 `GetNodes()[i].data` 必须非空，否则只弹“请填满所有节点”；置 `isCloseing`，Animator Exit，**一次性** | 否 | U（已有 map_continue+MapGate+SingleShot） |
| 放置命运牌 | `MapItem.AddToList(SwapContentIdentity)` → `MapSelectUI.SetNodes()` → `UpdateCardItemPos()` | 哪张牌、哪个**空**框（框 = `Map/NodeContent/NodeN`，Null 子物体 active=空） | 目标框非空则退回原位；游戏允许放任意空框，mod 只放“第一个空框” | 是（见下） | M（已有 map_place_N） |
| 取回命运牌（un-place） | `MapItem.RayCheck` else 分支：父物体改回 `MapSelect`、`hasSelected=false`、`SetNodes()`、`UpdateCardItemPos()` | 已放置的牌 | `Item.AddToParent` 把 Null 排到最后，所以 `OnPointerDown` 的“最后一个子物体名为 Null”条件对已放置的牌成立 → 游戏本身支持。**无交换**：占用框不接受拖放，要换序得先取回/移到空框 | 是 | M/D（mod 未建模，设想 `map_unplace_N`） |
| 悬停看牌/框 | `MapItem.OnPointerEnter`、`MapFrame.OnPointerEnter` → `KeywordDisplay` tooltip | – | 仅信息（敌人词条、事件说明） | – | 信息：直接读 `node.data` + Level 表即可 |
| 画笔/橡皮/清空 | `QuillBtn`/`EreaseBtn`/`GarbageBtn` → `ToggleDrawTool`/`ClearAllDrawingsAndSync` | – | 纯多人标注（`CmdMapDraw*` 同步）。**隐患**：开笔会让全图 `_drawInputImage.raycastTarget=true`，遮住地图卡的指针事件；通用 `click` 误点这些按钮会卡住拖放 | 是 | Avoid（不要暴露） |
| 重抽/跳过/回退/逃跑/改路线 | **不存在** | – | 路线一旦在 `Level%6==0` 铺好就是单链（`currentNode.GetChild(0)`）；唯一的“选择”就是 4/8 + 顺序 | – | – |
价值：铺路(map_place/continue) **High**（已有）；路线可见性 **Med-High**：mod 的 `mapNodes` 故意不列固定的 Start/End 框，agent 看不到本段末尾是哪个 Boss/精英、也看不到已走到第几格 → 建议 `route`（`GetNodes()` 每格 Type/NodeId/Note/敌人，加 `Level%6` 指针）effort **S**；un-place **Low-Med**（S，约 25 行，风险：`OnTransformParentChanged`/Null 重排要照 RayCheck 做全）。

## 4. EventUI 事件（phase `event`，`event_option_N` 已有）
Reached：自由位“魔女手记”卡 / 每段 Start 剧情 / `event_999` / 其他事件的 `ContinueEvent`。结构：`Init(id)` 跑 `InitScript`（设 `Vars["ChoiceN"]`：`"1"`可选、`"2"`显示但禁用、`"0"`/缺省=不显示），≤4 个选项，点击=`Chicked` → 该选项的 C# 脚本（Roslyn，`PlayerInfo.*`）→ 单次选项以 `EndEvent()` 收尾（其余选项隐藏，选中项被复制成“结束事件”按钮，点击=`Entry()`：`EntryScript` 里有 `StartLevel` 就开战/商店/休息室，否则 `TryChangeMap`→`CmdEventWait`→下一步）或 `ContinueEvent("k")`（换成 `Sub_<id>_k`，多页）。
| 选项类型（数据里数的，64 行事件表） | 例子 | 现有 event_option_N 能表达吗 |
|---|---|---|
| 立即改数值/发物（随机卡/遗物/祝福、属性、金币、最大生命） | 2,4,14,16,17,19,21,*2001 | 能，但**不知道效果**：效果文字在 `<subtip>`（悬停才显示），按钮上只有标题如“喂它点卡牌”。建议 `Events.cs` 读 `EventUI.dataConfig.data.Localize(N+"Describe")` 的 `<subtip>`，effort S |
| 花钱/花命（`Money-=`、`Hp=1`、`MaxHp-=`），钱不够时选项显示但禁用 | 8(50/200金)、10(死亡轮盘 HP→1)、12、18、20、*2007、*2013 | 能（禁用项已不出 action） |
| 赌（`DefaultRoll`/Dice+Lucky） | 3,10,11,18,20,*2010,*2006/2015 | 能；结果由 `ChangeEventSubtip` 弹字，agent 看不到 → 同上 |
| **可重复**（不 `EndEvent`，用 `ThisCount`/`LockChoice` 自行关闭，最多 4-8 次） | 10,11,12,18,20 | 能，但每次点完必须重读 state（选项会变禁用/消失）；贪心 bot 会把钱花光 |
| 多页（`ContinueEvent`） | 9,*999,*2003/2004/2006/2008/2009/2011/2015/2018 | 能（每页是新选项集） |
| **弹出选牌 DeckUI**（`OutFightSelectCardToAction`） | 3 选项1：选≤3 张牌随机加词条；*2012 选项2：选 1 张复制 | **不能**：点完后 EventUI 仍在、phase 仍报 `event`，但前面盖着 `DeckUI.CreateDeckMenuForSelect`（与商店删牌/附魔同类，L，归 deck 屏建模） |
| 后接战斗 | 9→精英战、13→模拟精英战(fake)、*7(魔王冢,锁定)、*2006/*2015→level_10049/10050 | 能点；之后 phase 变 battle |
| 后接商店/休息室 | *999→ShopUI、*2001/2011-2014→Breaks | 能点；ShopUI 目前 `unknown` |
| 改路线/结局变量（不可逆） | *2002(魔王线/教廷线/永夜线&勇者线)、*2004、*2005、*2006/*2015、*2014(难度+2) | 能点，但 agent 必须先看到 subtip 才能有意义地选 |
| 提前结束整局 | `*15`「提前结算」选项1 `EndTheGame()`+400 金 | 能点，**dangerous**（锁定事件，未解锁不会出现） |
价值 **High**（通关必经，每局 ≥6 个固定事件+多个随机事件）。额外风险：事件奖励走 `Commands.give("randomcard…")`，牌堆和背包都满时**静默丢弃**（trap 15 同族）；背包满的 caption 只有 `give card` 分支才弹。

## 5. DialogueUI / DialogueManager / VN / Narration
| 项 | 结论 |
|---|---|
| 触发 | `DialogueManager.RegisterDialogueEvents` 按 `EventName` 挂 EventCenter：`StartTutorial`(新档开场)、`FirstFight/2Fight/3Fight/4Fight/7Node/FirstShop/FirstBless/Mapselect/SecondAD`(Teach 教学)、`Winlevel_*0`/`Winlevel_10048`(终局战后，结尾 `StartLevel("build","break")`)。`PlayerInfo.ShowDialogue(id)` 也能在脚本里调 |
| 能问什么 | 只有：点击推进（`trigger` → `EndDialogue` → `NextDialogue`/`EndScript`）和 **1-2 个固定选项**（`ChoiceCount`、`ChoiceTextN`、`ChoiceScriptN`）。**没有**条件隐藏/禁用、没有输入框、没有小游戏。`ChoiceCount==1` 时点击文字框自动选 1 |
| 危险分支 | `StartTutorial_48`（拒绝合同）→ `QuitAndDeleteSave()`：反编译体只关 DialogueUI、`CloseHouse`、显示主菜单（名字像删档，代码里没见删除，候选）；`FirstFight/2Fight/3Fight` 的“是/否”里有 `WinTheFight()`（秒胜教学战） |
| 其他 UI | `buttons/Play`(自动播放)、`Skip`、`HistoryButton`：纯便利，不需要建模。`OptionsUI`/`ShowOptions` 数据里**没有任何调用**（只有 Lua mod 用）。`VNPlayerManager` = 创作者预览工具（`IsPreviewMode`），不在玩家流程；`NarrationManager` = 战斗语音/字幕，非交互 |
| 价值/effort | Low（仅教学/开场/终局；现有 `dialogue_advance/choose` 够）。 |

## 6. 世界场景 / 转场
| 对象 | 结论 |
|---|---|
| `SceneType`（Road,Castle,PuppetTheater,Forest,Chessboard,Courtyard,SlotMachScene,Curia,DollFairyTale,BalancedHolySee,DemonKing） | 只是**背景预制体** `UI/Scene/<name>`，`GameApp.UpdateBack` 按层数换：<6 Forest/Courtyard(随机)，6-11 Courtyard，12-17 SlotMachScene，18-23 DollFairyTale，24-29 Castle/BalancedHolySee(教廷线)，≥30 Chessboard/Curia/DemonKing。代码只读它的 `SceneInfo`（`ground_y`、`bgmList`、相机边界、视差）。全局 `SceneItem` 只有 `parallaxFactor`，**无点击处理**。Road/PuppetTheater 代码里无引用。→ 无可点对象，不需建模（若想确认预制体里没有碰撞体：实机 `GET /world?root=<SceneType>`） |
| `Witch.UI.Window.SceneItem` | 这是**大厅**的 8 个可点位置（OpenLibrary/Tower/Exhibition/Store/Administration/Spring/WitchHouse/StartGame），归 inv-hub |
| `CurtainTurnUI`/`InkTurnUI`/`SceneTurnUI` | 纯转场。`CurtainTurnUI.Play` 期间 `canvasTf CanvasGroup.blocksRaycasts=false`（真鼠标点不到，UiLite 合成事件不受影响）；`SceneTurnUI.Close` 至少 5 s 才淡出（加载遮罩）。phase 应报“过渡中”，别当 `unknown` 乱点 |

## 7. 未知项（需要实机一眼）
1. 已放置牌的真实拖回是否可行（我读代码判断可行）；`mapChoices.label` 是否含敌人攻/血。
2. `*` 前缀 = 锁定 id 的确认（GameConfigData 在 Witch.Core，未反编译），以及哪些解锁条件会让 `*7/*15/*26/*36` 进池。
3. `InHighTide`(黑潮) 由 `CmdChangeHide` **翻转**而非置位：`ReadyToChangeMap` 在 Level 11/23/29/31 不是幂等的，重复触发会翻回去（风险，未见实例）。
4. 终局战 `level_10046`/`10051` 胜利后谁推进到 GameExitUI（没有 `Winlevel_` 对话）。
5. 末段 4 张 BreakPrefab 的含义；`ExLockDes/ExDeleteDes` 来自难度词条 hard#1/#2（`命运牌数量−1`/`额外一张命运牌已锁定`），默认 0。
6. 事件 3/*2012 点选项后 EventUI 与 DeckUI 的叠放顺序，以及 `Events.Detect` 当时报什么。

## 8. 与 CLAUDE.md 的出入 / 补充
- trap 18 成立，补充：不是“铺路”而是**从 8 张里选 4 张+排序**；游戏支持取回，mod 没做。
- trap 14：fake 战代码来源仅事件 13（模拟考核，随机池事件，选“参与模拟”→`StartLevel("fakefight","elite")`，同时 `Reward+=1`），见上。
- trap 24 “教学局事件 新手考核→永夜化身”：事件 `*1001` 本身 `EntryScript` 为空，终局战来自 Teach 的 map_1000 节点，不是事件直接开战。
- 建议优先级：①事件 subtip（S，高性价比）②`route` 状态（S）③`map_unplace_N`（S）④event 内 DeckUI 选牌（L，与商店/附魔共享）；对话/场景/画笔不做。
