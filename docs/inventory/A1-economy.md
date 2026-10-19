> 2026-10-13 静态反编译分析，由只读调研 agent 产出，**未经实机验证**；「候选」= 只有名字或 public 方法证据，没见到 prefab 绑定。
> 合并后的结论、已核实项和路线图见 [../scene-interfaces.md](../scene-interfaces.md)。文中的 scratchpad 路径只在当时的会话里存在。

# inv-A1 经济/构筑/局内菜单（静态分析，只读）

反编译根目录 `.toolchain/decomp/`，下文 `W/` = `Witch.UI.Window/`。「候选」= 只见到 public 方法或名字对应，没见到 AddListener，需要实机 `/world` 确认。
证据另有 `.lab/player.log`（bot 的真实点击记录）：bot 已经进过 ShopUI / DestinyTreeUI / CardEnchUI，每次都只点 `ExitButton`。

## 0. 先看这几条（影响整个决策）
1. **买来/奖励拿到的卡不进战斗牌组。** `ShopItem.TryBuy`、`CardChoiceUI.Select`(W/CardChoiceUI.cs:164) 都是 `RoleTable.UnCardList.Add`（备选区）；开战只读 `RoleTable.cardList`（`FightCardManager.Init` 行 371，`FightInit` 只重置 UnCardList 的变量）。进牌组要走 OutDeckUI 的「move」（`ShowCard.MoveItem`，见 §6）。现有 bot 的 `card_choice_pick` 因此对战斗**没有影响**。全仓库没有任何自动装备。
2. 三个建筑节点的映射已坐实：地图节点 `Type=Build` + `NodeId` → `MapManager`(行 2149) `RpcLoadMap` → `Commands.load("build", id)`(Commands.cs:3018-3050)：`shop`=旅行商人→ShopUI，`tree`=天界赐福→DestinyTreeUI，`ench`=血脉铭刻→CardEnchUI，`Breaks`=一息安隅→休息室。事件脚本也能开：`ScriptExecutor.StartLevel("build","shop")`(行 3405)，log 里事件选项「驻足购买」→ShopUI。**魔女手记**：反编译与 resources.assets 都没有这个节点名（Map 表不是明文），log 里它之后紧跟一个 EventUI（run2：「自己动手，丰衣足食」；run1：「金币盲盒/前去疗伤/驻足购买」），所以大概率是 `Type=event` 的随机事件卡，不是新界面。
3. 三个建筑界面 = 同一个类族：`DestinyTreeUI`、`CardEnchUI` 继承 `ShopUI`，共用 ExitButton、卖牌区(`SellItem`)、卖遗物行(`RelicItemConfig`)、金钱显示；**一个 `Shops.cs`（3 个 phase、同一套 sell/leave）就够，不是三份活。**
4. 点击可能被静默吞掉（同 trap 15）：钱不够 `ShopItem.TryBuy` 直接 return（无提示，价格文本变红）；`Flushed` 钱不够/次数满也静默；卡牌备选区满、遗物满是**弹 modal**（会挂住，需要 modal_confirm/cancel）。mod 必须在调用前自己预检并给出 `note`，不能依赖「调用了」。
5. 离开不回滚：买/卖/占卜都是点击即写 `RoleTable`，无购物车。`ExitButton` 的监听是 `MapManager.TryChange(); Close()`（ShopUI.cs 的 Awake），**一次性**：出去就进下一个节点，不能回来；未花的钱带走。

## 1. ShopUI（旅行商人）  Find: `Content/Refresh`、`ExitButton`、`Content/Backpack/Money/text`
到达：路线卡 `shop` 或事件「驻足购买」。展示：3 个遗物 + 3 张卡（`SetShopItems`，非诅咒、非 Rarity4 遗物、未持有）；下方是自己的牌/遗物（卖）。
| 操作 | 入口 | 参数 | 前置/副作用 | 可逆 | 调用 |
|---|---|---|---|---|---|
| 买遗物 | `ShopItem.TryBuy()`（public，无参，对象=ShopTran 下带 ShopItem 的子物体，跳过模板 `Item`） | 第 N 个商品 | `Money>=itemPrice` 否则**静默**；已有 6 件且静态 `firstBuy` 为真→**弹 modal**（确认后再调 TryBuy）；买到的进 `WithoutArmedRelicList`，`RoleTable.Listen` 在 <6 件时自动装备，超出留在备选/仓库；成功后 `Destroy(item)` | 否 | **M**（实际按钮是 prefab 接线，候选：商品根或子按钮；根上的 `OnPointerClick` 是空函数，点根没用） |
| 买卡 | 同上 | 同上 | `UnCardList.Count>=MaxAlCardCount`(5 或 Wisdom/2+2)→弹 modal「卡牌数量已达上限」，**确认后会打开 OutDeckUI**（叠一个界面）；成功进 UnCardList（不入牌组，见 §0.1） | 否 | M，预检备选区 |
| 刷新 | `ShopUI.Flushed()`（public） | 无 | 价格 `20 + flushedCount×flushmoneychange`(默认 40)，次数上限 `GameVar.RefreshCount`(默认 999)；不够/超限**静默**；重抽 3+3 | 否（花钱） | M；按钮 `Content/Refresh`（ButtonManager，prefab 接线，候选）|
| 卖卡 | `SellItem.TrySell()`（public） | 牌的 `dataConfig`（列表 = SellCardPrefab 同级 `SellItem`，先 cardList 后 UnCardList） | +`20×Rarity`(Money setter 再乘 MoneyMultiplier)；`Tag` 含 `Eternal`→modal「不可移除」；装备中且 `cardList.Count<=CardBottomCount`(13−Wisdom/5)→modal「已达下限」 | 否 | **M**。UI 路径是右键→`FloatingWindow` 的「sell」按钮，`UiLite.ClickObject` 只发左键，所以不用 U |
| 卖遗物 | `RelicItemConfig.ShowFloatingWindow` 里的**匿名 lambda**（无 public 方法） | 遗物 | +`70×Rarity`；`relicList/WithoutArmedRelicList.Remove`；`GameSaveAnalyser.TryPushNative(Delete)` | 否 | **S** 复刻 lambda，或 D：发 `button=Right` 的 pointerClick 再点浮窗按钮（右键分支只看 `eventData.button`，可行；浮窗按钮路径需实机看） |
| 离开 | `ExitButton`（ButtonManager，代码接线） | – | `MapManager.TryChange()`+`Close()`；→ OnDestroy 里如有 EventUI 还会 `EventUI.TryChangeMap()` | 否 | U（已在用，需 SingleShot） |
价格公式（`ShopItem.PriceCalculate`）：基价(遗物150/卡50/附魔80/祝福 `20×Weight−15`，3 阶=100)×`[1,1.3,1.6,3.5,4.5,6,8,10][Rarity]`×`PriceMul`(默认100)/100 ± `Rarity×5` 的骰子；高潮期(`InHighTide`)×1.2。
价值：**High**——遗物是主要成长，钱现在无处可花。工作量：**M–L**（Shops.cs ~200 行：列商品、预检、modal 兜底）。主要风险：买键的接线类型（用 M 绕开）、`Destroy` 延迟一帧（下一次 /act 才安全）、modal 叠 OutDeckUI。
未知：商品 prefab 的买按钮路径；浮窗按钮路径；`Content/Refresh` 在 DestinyTree/CardEnch 的 prefab 里是否存在（`ChangeFlushShow` 有 null 判断，说明可能没有）。

## 2. DestinyTreeUI（天界赐福，`tree`）  继承 ShopUI（没有自己的 Awake，沿用 ShopUI 的；`Start` 只补价格增量/金钱文本）
展示：`BuyBlessCount+3`(+1 个 3 阶) 个祝福商品（`ItemType=="Bless"`，Rarity<3，排除 blessing_21）+ 1 个「RandomBlessing」(占卜)。
| 操作 | 入口 | 参数 | 前置/副作用 | 可逆 | 调用 |
|---|---|---|---|---|---|
| 买祝福 | `ShopItem.TryBuy()` | 第 N 个 | `Money>=price` 否则静默；`blessingConfigs.Add`（立即生效，`blessAddListener` 跑 OwnScript）；无数量上限 | 否 | M |
| 占卜 | `ShopItem.TryBuy()`(RandomBlessing) → `DestinyTreeUI.Divination()` → `GenerateBless()` | – | 价 `70 + BuyBlessCount×40`(>6 次 50，>9 次 70)；`BuyBlessCount++`；`new BlessingChoiceGenerator().CreateBlessUI(DestinyTreeUI.transform)` 在**本窗口下**实例化 `UI/BlessChoice`（3 选 1，每项含若干条祝福）；钱在 TryBuy 末尾才扣 | 否 | M；选择界面 = 现有 `blessing_choice`，但 `Screens.BlessPicker` 只在 `BattleRewardsUI` 下找 `BlessChoice*` 子物体，**要改成也找 DestinyTreeUI 下的** |
| 选占卜结果 | `Blessing{1..3}/button`(ButtonManager，lambda 里 `blessingConfigs.Add` 全部条目，`canGet` 单次) | 1..3 | 没有取消按钮（代码里只有 3 个选项）；选完 `Destroy(obj,3.1s)` | 否 | U（复用 `blessing_pick_N`） |
| 卖牌/卖遗物/离开 | 同 §1 | | 同 §1 | | 同 §1 |
价值：**High**（祝福=最大的构筑杠杆，且是 3 个里唯一不受背包上限约束的）。工作量 **S–M**（在 Shops.cs 里多一个 phase + 扩展 BlessPicker）。风险：占卜弹出的 BlessChoice 无取消，开了就必须选一个；`TryBuy` 里 `itemPrice` 是旧价（扣款用旧价、之后更新为新价），预检用 `Cost` 即可。

## 3. CardEnchUI（血脉铭刻，`ench`）+ DeckUI 选牌模式
展示：6 个 `EnchTag` 商品（80 基价，同上公式）。买=选一张**未附魔**的牌（cardList+UnCardList）。
| 操作 | 入口 | 参数 | 前置/副作用 | 可逆 | 调用 |
|---|---|---|---|---|---|
| 选附魔 | **`ShopItem.EnchTryBuy()`**（public）→ `CardEnchUI.ShowCardToEnch(item)` | 第 N 个附魔 | `Money>=price` 否则静默；无未附魔牌→静默 return；否则 `ShowUI<DeckUI>` + `CreateDeckMenuForSelect(1,…)` | 否 | M。注意：**别调 `TryBuy()`**：EnchTag 分支调用 ShowCardToEnch 后立刻 `Money-=price`，回调里又 `Money-=price`，会扣两次；`EnchTryBuy` 不扣，prefab 多半接的就是它（候选，需实机确认） |
| 选牌 | DeckUI `Window Manager/Windows/新牌堆/Content/List View Custom/Scroll Area/List/card(Clone)`（ButtonManager，每张牌一个，同名） | 第 K 张 | 点中即 `OnSelect`；数量到 count(1) 自动 `Close()` → `OnDisable` 触发 `SelectCardEnd` → 回调写 `enchasedDict`、`Tag+=…`、跑 `LoadScript`、`Money-=itemPrice`、`Destroy(item)` | 再点一次可取消选中，关闭后不可逆 | U（用 index/uid 区分）|
| 取消选牌 | DeckUI 没有代码接线的关闭键 | – | 关闭后 `cardList.Count==0`→不扣钱、附魔留着 | – | **未知**：prefab 里有没有返回键/ESC |
| 卸下附魔 | `EnchCardItem.Unload()`（右键「unmount」） | 一张牌 | 恢复描述+`UnloadScript`；全仓库没有别处引用 EnchCardItem（可能是旧界面） | 是 | Avoid |
价值：**Med–High**（附魔直接改单张牌；但依赖牌已在牌组里，§0.1）。工作量 **M**：除 Shops.cs 外要给 **DeckUI 选牌模式**一个 phase（`deck_select`，动作 `deck_pick_K`）——它也被事件复用，见 §5。风险：DeckUI 的取消路径、牌同名需要 index。

## 4. 卖出入口还有一个（OutDeckUI）
商店/命运树/附魔任一打开时，`ShowCard.ShowFloatingWindow` 的第二个按钮是「sell」(`ShowCard.SellItem()`，public，+`20×Rarity`)，否则是「Destroy」(`DecomposeItem()`，**花 20 金**销毁，诅咒牌还要 Wisdom≥20)。

## 5. DeckUI（W/DeckUI.cs）
到达：战斗中 FightUI 的抽牌堆/弃牌堆按钮（`CreateDeckMenu/CreateUsedDeckMenu`，**只读**）；选牌模式由 `CreateDeckMenuForSelect(count, selected, source)` 打开，调用方：CardEnchUI、`Commands`(行 2430、2801：事件/脚本发牌)、`ScriptExecutor.SelectDeckCards`(行 6150，**事件选项后的「选 N 张牌」**，如移除/升级/复制，`deckUiSelectBusy` 串行)。
| 操作 | 入口 | 参数 | 备注 | 调用 |
|---|---|---|---|---|
| 看抽/弃牌堆 | `Window Manager/Buttons/抽牌堆|弃牌堆` | – | 战斗内只读，对打牌没有价值（`/state` 已有 hand） | Avoid |
| 选牌 | `…/新牌堆/…/List/card(Clone)` | 第 K 张 | 见 §3；`Description` 文本显示「请选择 N 张牌」 | U |
价值：**High（对事件）**——只要有一个事件选项走 SelectDeckCards，现在就卡在 `unknown` 里。工作量 M。**与 CLAUDE.md 的关系**：事件(trap 23)只写了「Entry/EndEvent」，没提事件内会弹 DeckUI 选牌，这是漏洞。

## 6. OutDeckUI（TopBar `CardBack`，代码接线 `OnhasCardDeckBtn`）——不在我名单但决定 §0.1
装备/备选两栏（`Window Manager/Windows/牌堆/Content/equipCard|unequipCard`），每个 `ShowCard` 右键浮窗：`MoveItem()`（装备↔备选，上限 `CardTopCount=max(Wisdom+18,Bottom)`，下限 `CardBottomCount`，备选上限 `MaxAlCardCount`；越界弹 modal）、`DecomposeItem()`/`SellItem()`。拖拽走 `AddToList`（D），但 `MoveItem()` 是 public，**M 即可，不必拖**。价值 **High**（没有它新卡永远不上场）。工作量 M，和 `Shops.cs` 同一批做最划算（phase `deck_manage`：`deck_equip_K` / `deck_unequip_K`）。

## 7. SafeBoxUI（保险箱）
到达：休息室世界物体 `Box` → `BreaksUI.RelicBackPoint()` → `ShowUI<SafeBoxUI>`（`GameEntryUI` 无入口）。休息室里打开它时 `Rest.Detect` 因窗口非 Ambient 让位给 `unknown`。
| 操作 | 入口 | 参数 | 前置/副作用 | 调用 |
|---|---|---|---|---|
| 存钱 | `SafeBoxUI.RetainMoney()` | – | `SafeBoxSaveMoneyCount>0` 且 `Money>=150`；存 `min(100, Money/10)` 进**元存档** `GameRuntimeData.Money`；失败弹 modal | M |
| 取钱 | `ChangeMoney()` | – | 每次 `min(vault/10,200)`，到手只有一半，每个休息点 5 次 | M |
| 存/取牌、遗物 | `PutIntoStore(go)` / `PutItBack(go)`（public，参数是 `SafeBoxItem` 物体） | 一件 | 各最多 3 件；Level<2 有额外限制；`Eternal` 不可存；结算时 `SafeboxSave()` 写 `GameRuntimeData.CardData/RelicData`（**跨局永久**） | M |
| 丢弃 / 卖遗物 | 浮窗 lambda | | 同 RelicItemConfig | S |
价值：**Low**（只影响下一局的起手，且写元存档）。工作量 L。风险：写 meta 存档。建议先不做；至少让它被识别为 phase `safebox` 并给一个关闭动作（关闭键路径未知，prefab 才有）。休息室的 `BreaksUI.ReturnHome()`（弹 modal「回归」→`GameApp.GameOver()`，**主动结束本局**）顺带发现，归休息室范围，标 dangerous。

## 8. BackpackUI / StatusUI / BuffBarUI / StatusBarUI
| 界面 | 到达 | 内容 | 操作 | 结论 |
|---|---|---|---|---|
| BackpackUI | TopBar `Status`(候选→`TopBarUI.OpenBackpack`)、快捷键 `GameApp.OpenBackpack`、`TopStatusItem` 点击；`BackpackUI.Exit` 是 InputAction→`Hide()`，**关闭键路径未知** | 容器，里面只有一个 `StatusUI` 窗口 | – | 只读；价值 Low，但打开后会遮住别的界面，要保证 phase 能识别并关闭 |
| StatusUI | 同上 | 属性/祝福/遗物/金钱/真理 | 遗物行是 `RelicItemConfig`：右键「sell」「Take off」（战斗中拒绝）。装备↔卸下靠**拖拽改 parent**（`OnTransformParentChanged`），`ifEquipped` setter 是 private | 状态已有 `/state`；卸下/装备遗物 = D 或 S，Low（>6 件遗物时才需要） |
| BuffBarUI / StatusBarUI | 战斗场景的单位头顶条（非窗口） | 血条/Buff | `StatusBarUI.OnSelected/OnUnSelected` 是目标高亮 | 只读，**不需要**；`/state` 已含 |

## 9. TopBarUI `Content/Buttons/*`（局内顶栏）
`Awake` 只给 `CardBack` 接了线，其余是 prefab 接线，下表「→」按 TopBarUI 同名 public 方法推断（候选）。
| 按钮 | → | 作用 | 危险? |
|---|---|---|---|
| CardBack | `OnhasCardDeckBtn`（代码确认） | 开 OutDeckUI（§6） | 否 |
| Status | `OpenBackpack` | 开 BackpackUI/StatusUI | 否 |
| Illustration | `ShowDict` | 开 `DictionaryUI`（图鉴，只读） | 否 |
| Setting | `ShowSetting` | 开 `SettingUI`：含 `CloseTheGame()`（modal 确认后 `Application.Quit()`）和 **`ReturnMain()`（无确认，直接 `TopBarUI.ReturnToMenu`）**，还有按键重绑 | **dangerous**：别让 agent 点；generic `click` 的 `ui` 清单里应过滤 |
| ExitGame | `TryReturn` | modal「返回主菜单?」→`ReturnToMenu()`→`GameApp.ReturnToMenu()`：中途放弃（不调 GameOver，代码里没见清存档；CLAUDE.md trap 19 说存档会续，推断而非实测） | **dangerous**（会让 bot 丢掉局内上下文） |
| EndSlot | `CheckSlot`（仅 Slot/生存模式 `ShowSlotButton` 后可见） | modal→`SlotMachineManager.EndGameWithAdvanceTimeFlow()` 结束本局 | dangerous（仅 slot 模式） |

## 10. 只在主城/开局前（局内不可达）
| 界面 | 到达 | 内容 | 价值/备注 |
|---|---|---|---|
| OutsiderShopUI(+OutsideShopItem/OutSideItem) | 主城物体 `HouseItemType.OutsiderShop/cardShop/rollShop`(`HouseManager.StartShop`/`InitType`) | 元商店：用 `Truth`(真理之晶)/`Time` 买永久解锁（卡、遗物、角色、伙伴、属性物品）。`SetCurrentItem(item)` 选中 + `TryBuy()`/`BuyCheck()` 买或启停（`UsedBuyedItems`），`Content/描述/Content/Buy` 是 ButtonManager | **Med**：影响下一局可抽到的池子，通关不需要。写元存档（`GameRuntimeData.Save`）。工作量 M–L（列表+选中+买）。真理之晶不足静默 |
| CardPackUI(+CardPackItem/PackShowItem) | `GameEntryUI`（phase `game_entry`）里，`cardPackUI.Init()` | 开关卡包（至少 6 个启用），`SwitchButton` 点击→`PackCheck`→`SetPackEnabled`；右键 `ShowInfo`；`OnDestroy` 写 `GameRuntimeData.UseCardPack` 并存档 | **Med**：决定商店/奖励/祝福的抽取池（`CardPackCheck` 过滤），可当「开局配置」。工作量 S–M；写元存档 |
| AffectionUI(+AffectionItem) | 仅 `GameApp.PreLoadUI` 预载后隐藏，**没有任何代码打开它**（可能死功能/prefab 接线） | 好感度任务，点击 `AddReward` 给 `Truth` | Low；未知如何到达，先不做 |

## 11. 未知项（实机 `/world?root=…` / `ui` 一次能全部查掉）
- ShopItem prefab 买按钮的实际路径与组件（`TryBuy` vs `EnchTryBuy` 谁被接线；是否有 `val` 下的 Button）。
- `Content/Refresh` 在三种 UI 里是否存在；右键浮窗按钮的路径（`FloatingWindow`）。
- DeckUI 选牌模式的取消/返回键；SafeBoxUI、BackpackUI 的关闭键；BlessChoice 是否盖住 ExitButton。
- 魔女手记的节点类型（Map 表读不到；看 `MapGate` 里 `node.data["Type"]`/`NodeId` 即可）。
- 事件里 DeckUI 选牌的触发频率（决定 §5 的优先级）。
- 与 CLAUDE.md 的冲突/补充：①trap 15 只说「选牌被拒」，没说选中的牌**不入牌组**；②trap 23 缺「事件内选牌 DeckUI」；③「Shop … not modelled」属实，但 log 证明 bot 已多次进入并只点 ExitButton，没有被卡。

## 建议顺序（按价值/成本）
1. OutDeckUI 装备动作 + DeckUI 选牌 phase（让已拿到的卡真正上场，并解开事件选牌）——M。
2. `Shops.cs`：shop / destiny_tree / card_ench 三个 phase，动作 `shop_buy_N`、`shop_flush`、`shop_sell_card_N`、`shop_sell_relic_N`、`shop_divine`、`ench_pick_N`、`shop_leave`，全部 M 调用 + 预检 + `note`——M–L。
3. CardPackUI 作为开局配置（S–M）；OutsiderShopUI、SafeBoxUI、Affection 暂缓。
4. 在 generic `ui` 清单里隐藏 TopBar 的 `Setting`/`ExitGame`/`EndSlot` 与 SettingUI 的退出类按钮。
