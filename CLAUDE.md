# witchctl — project notes

Goal: control 魔女：终末旅途 (Steam app 3709430, MeowAlive, Unity Mono) from a CLI with < 150 ms end-to-end. User context: do not
steal focus, do not make noise (they study on the same Mac); the game runs hidden (`open -g -j`) and muted in a lab copy.
The project lives in `~/Codes/witchctl` (rewritten there from the volatile `/tmp/temp/witchctl` prototype on 2026-10-13).

## STOP rule: Steam account must be `eqckd`
Before running the game in any way (`lab.sh start`, `open` on the app, the live tests), run `scripts/steam-whoami.py --expect eqckd`.
Exit ≠ 0 (another account, logged out, or Steam's autologin pointing elsewhere) → **do not launch; stop and tell the user**. Do not switch
accounts yourself. `eqckd` = persona name of the user's alt Steam account (see trap 7). The SessionStart hook
(`.claude/settings.json`, local) prints the same check at the start of every session; re-check right before launching, the account can change mid-session.

## Layout
- `mod/` C# (net472, builds against the game's own `Managed/*.dll`, read-only):
  `Bridge` (HttpListener thread → main-thread `Pump`), `Routes`, `State` (snapshot), `Battle` (side-effect-free fight logic),
  `Screens` (post-fight screens as phases), `SingleShot` (scene-change guard), `MapGate` / `MapNodes` (map readiness, route building), `Hub` (world hub scene),
  `Tutorial` (tutorial overlays), `Rest` (rest room), `Events` (story events), `RunEnd` (settlement screen), `Popups` (hub popups + the yes/no `modal` window), `World` (`/world` dump of world objects), `Rich` (text cleanup), `UiLite` (fast UI), `Audio`,
  `Diagnostics` (`/debug`), `Deck` (deck window: equip / unequip / sell / destroy), `DeckSelect` (the "choose N cards" picker), `Shops` (shop, destiny tree,
  blood-line engraving), `Skills` (career skills), `GameEntry` (the screen before a run), `Danger` (what must never be clicked / what a yes-no button really does),
  `Achievements` (the achievements window), `OutsiderShop` (the hub's meta shops; `Hub` also opens the seven book pages), `SlotMode` (Chaos Paradise: phase `slot`, action `slot_end_run`), `ConfigDump` (`GET /config?table=Enemy&match=…`: whole rows of the game's config tables, scripts included).
  **`Entry` is the mod's entry point** (`[ModInitialize]` + `Audio`), not the before-a-run screen: check `ls mod/` before creating a file (it was overwritten once).
  Unity-free logic lives in `*Rules.cs` (`DeckRules ShopRules SkillRules TargetRules EntryRules MapRules HouseRules OutsiderRules`, plus `Danger`) and is tested by `tests/modlogic`.
- `cli/witchctl` stdlib-only Python, raw-socket HTTP (http.client import costs 11 ms), shebang `python3 -I -S`.
- `examples/greedy.py` agent that plays menus → fights → rewards → map using only the CLI (smoke test + template).
- `scripts/steam-whoami.py` which Steam account the client is on (live session from `logs/connection_log.txt`, else `registry.vdf`
  AutoLoginUser; names from `config/loginusers.vdf`). File-based on purpose: a Steamworks `SteamAPI_Init` would make the caller count as a running game.
  macOS has no `ActiveProcess/ActiveUser` key (that is Windows/Linux only). `STEAM_DIR` overrides the Steam folder.
- `scripts/lab.sh` clone/install/start/stop/reset. `.lab/` = APFS clone of the game with bundle id `…Journey.lab` (own save folder
  `~/Library/Application Support/com.MeowAlive.Witch-s-Apocalyptic-Journey.lab` + own prefs). `.toolchain/` local .NET 8 SDK.
  Changing the mod needs `lab.sh stop` → `install` → `start` (install refuses while the game runs).
- `scripts/nana/` the doom-power pipeline (see trap 39): `run.sh` (all of it), `craft_feast.py`, `safebox.py`, `make_card.py` (editor helpers). Lab game only.
- `scripts/dashboard.py` (+ `dashboard.html`) read-only progress page: `python3 -I scripts/dashboard.py --log RUN.log [--tasks tasks.json] [--fast-ms 200]`, loopback `:47801`;
  live over SSE (`/api/stream`: `snapshot`, then `state` / `log` / `tasks` / `beat` events), `/api/status` one-shot. One poller thread, fast only while a page is open;
  it only issues `GET /state` (tested). `-p 'test_dashboard.py'` (26 tests). Checking the page: headless Chrome never finishes loading an SSE page (`--dump-dom` hangs),
  drive it over DevTools instead (a small node script with the global `WebSocket`: navigate, wait, `Runtime.evaluate`, `Page.captureScreenshot`).
- **Moving the lab profile to the real Steam copy** (done 2026-10-19 with the user's go): the profile is `SaveData/SaveData.sav` (AES-256-CBC with the static key/IV in
  `GameRuntimeData`; decrypt with `openssl enc -d -aes-256-cbc -K … -iv …` to read Level/Truth/Time/`BuyedItems`). The game's real folder is
  `~/Library/Application Support/com.MeowAlive.Witch-s-Apocalyptic-Journey/SaveData` (`Application.persistentDataPath`), while Steam's Auto-Cloud rules point at
  `…/MeowAlive/Witch's Apocalyptic Journey/SaveData` (`Watching 0 files`; Steam also *moves* the previous user's cloud files out of that folder on an account switch).
  Procedure the user prescribed and that held: launch through Steam (`open -g steam://rungameid/3709430`, pre-launch sync runs) → copy the file over while the game runs →
  `kill` the game (SIGTERM; the game rewrites `SaveData.sav` at startup, a quit would not matter after the kill) → compare hashes. Back up the old file beside it first.
- Tests: `python3 -I -m unittest discover -s tests -t . -p 'test_cli.py'` (fake bridge, 51 tests); `-p 'test_steam_whoami.py'` (fake Steam folder, 10 tests); `-p 'test_greedy.py'` (bot policy, 48 tests);
  `-p 'test_modlogic.py'` (the Unity-free C# logic compiled and run by `tests/modlogic` with the local .NET SDK); `-p 'test_live.py'` (real game; the
  play test needs `WITCHCTL_E2E_PLAY=1` and a fresh profile: `lab.sh reset`).
- `witchctl recon` / `greedy.py --recon`: dump every new screen (state, UI, world with button wiring) into `.recon/` (ignored). Read it before guessing a path.

## Phases and actions
`main_menu house hub game_entry map_select dialogue battle battle_won battle_lost rewards card_choice blessing_choice rest event run_end
mode_choice announcement item_reveal modal deck deck_select shop slot unknown`.
The game's own phase model (`RuntimeGameplayAutomationService`) stops at `battle`; everything after a win is `Screens.cs`
(precedence in `/state` and `/act`: modal → deck → deck_select → card_choice → rewards → shop → run_end → battle → game service). Post-fight flow:
FightUI (Win, chest, `ShowBattleReward()`) → `BattleRewardsUI` → `CardChoiceUI` or the blessing picker (a `BlessChoice(Clone)` child of
BattleRewardsUI) → back to rewards → close → `map_select`. The shop windows (`ShopUI`, `DestinyTreeUI`, `CardEnchUI`) are phase `shop`;
`deck_open` is offered in every phase whose TopBar button is up. Card growth path: reward / purchase → backpack → `deck_equip_N`.
`game_entry` carries an `entry` section and `entry_*` actions next to the game's `game_entry_start`.

## How the game is hooked (verified)
- Official mod system: `Application.dataPath + "/Mods"` (mac: `<App>/Contents/Mods`), folder name = `ModName`, `ModConfig.json`
  (`Enabled:true`), `Scripts/Entry.lua` and/or `Scripts/Entry.dll`. The DLL is loaded with `Assembly.LoadFrom` and every static
  `[ModInitialize]` method is invoked with the `ModConfig`. Assembly name must be `ModName.ModAuthor`. Works on macOS.
- No SIP/signing problem: nothing is injected. After adding files to the clone, re-seal with `codesign --force --deep --sign -`.
- Official hooks (`HookBefore/After`) use Rougamo IL weaving: only methods the developer wove can be hooked. Not needed here.
- Decompile: `.toolchain/ilspycmd/ilspycmd -p -o .toolchain/decomp -r <Managed> <Managed>/Witch.dll` (ilspycmd 8.2.0.7535, installed by
  `scripts/bootstrap-dotnet.sh`; run it with `DOTNET_ROOT=$PWD/.toolchain/dotnet DOTNET_ROLL_FORWARD=Major`, it targets .NET 6 and the local
  runtime is 8; MUIP types such as `ModalWindowManager` are in `Plugins.dll`, use `-t <type>`). Game code is in
  `Witch.dll`, `Witch.Core.dll`, `AllScripts.dll`; no obfuscation. Official tutorial: github.com/meowalive/apocalyptic-journey-mod-tutorial.
  Rougamo-woven methods appear as `_0024Rougamo_X`; the real body is there.

## Traps found the hard way
1. **`RuntimeGameplayAutomationService.GetLegalActions()` is not read-only in a battle.** It calls `CanPlayCardLegally`, which reflects
   into `CommonCardItem.TryUse()` — the real play: spends energy, removes the card, fires events (and shows "费用不足" toasts, sounds).
   `PlayCardAsync` then plays it a second time. `Battle.cs` re-implements the checks read-only and `/state /actions /act /play` use it
   whenever `Battle.IsActive()`. Regression test: `LivePlay.test_first_fight_from_menu_to_map` (30 reads leave the energy untouched).
2. The game's own UI automation is slow (`/ui/full` ≈ 0.8 s, `/ui/do` ≈ 2.4 s: scores every node, scans all GameObjects). `UiLite` is the fast path.
3. Resolving a card (`TrueUse`) blocks the main thread 240–460 ms. `/play` therefore validates, queues, answers; `Wait:true` answers after.
   A second `play` while one is queued is refused; the outcome (re-checked at resolve time) lands in `flow.lastPlay`.
4. Unity pauses when unfocused → `Application.runInBackground = true` in `Entry.Init`.
5. The game has a multi-second loading hitch (≈6 s) some 10–20 s after launch; `lab.sh start` waits for 30 consecutive fast answers.
6. Launching the binary directly takes focus; `open -g -j` does not. `open` from Claude Code leaks `NO_COLOR/PAGER` (script uses `env -u`).
7. A lab run with the Steam bundle id rewrites `SaveData.sav` on start/exit. Hence the separate bundle id. The Steam account on this Mac is the
   user's ALT account (main-account save lives elsewhere), so save rewrites/achievements here are harmless; the lab stays isolated mostly
   to keep the Steam install untouched.
8. Single player is a Mirror host over KCP when Steam is not initialized (`steamInitialized:false`, `serverActive:true`): Steam is not needed. The lab game
   is **not started by Steam** (`open -g -j` on the clone): it does call `SteamAPI_Init()` (`.lab/player.log`: "Loaded …steamclient.dylib OK") and then prints
   "STEAM连接失败，将以本地模式运行！" (local mode; `FizzySteamworks: Steamworks is not initialized`). Likely cause (untested): there is no `steam_appid.txt` in the clone and no
   `SteamAppId` in the environment, so Steam cannot tell which game this is. Making Init succeed would count the process as a running game on `eqckd` (see
   `scripts/steam-whoami.py`) and could touch achievements / cloud saves of that account: only with the user's say-so.
9. `unknown` is also what scene transitions look like: retry/`wait`, do not fail. The game's phase model does not know the house scene without `HouseUI`.
10. **Scene-changing actions must fire once** (the old "second fight never initialises" bug). `MapSelectUI.TryContinue()` stays offered while the
    scene takes seconds to change; every repeat starts another fight on top (no `FightPlayer`, empty hand, "FightInit cleaned stale untracked fight
    objects"). `SingleShot` refuses `main_menu_start_game / house_open_game_menu / game_entry_start / map_continue` for 10 s while the legal-actions
    phase is unchanged. Agents poll with `witchctl wait`, not by repeating.
11. A dead enemy lingers in `enemyList` with CurHp ≤ 0 until the second death pass: `IsSelectable` = `enabled && CurHp > 0`; HP is clamped to 0, `dead` is a flag.
12. Cards are dealt one by one (`FightUI.createCardQueue`, 0.15–0.55 s apiece). `canAct` stays false while it is non-empty, otherwise an agent ends the
    turn with a partial hand. The end-turn button is hidden (`SetActive(false)`) during card selection and after a win while it stays "interactable".
13. `GetBuffs()` is the unit's buffs; `effectList` is something else (queued effects). Intent numbers come from the live `enemy.ActionCards`
    (`ObjectCard`); the config rows only have `{0}` placeholders. Card text = `LocalizeEx.Description(IDataConfig)` through `Rich.Strip`.
14. `FightManager.IsFake` = scripted fight: `StatusManager` resets the player to full HP on death, so a defeat never ends it (`flow.fake`).
    The second story fight is one; a greedy bot can stalemate there.
15. **A card pick can be refused silently**: `CardChoiceItem`'s click does nothing but show the "卡牌数量已达上限" modal when
    `RoleTable.UnCardList.Count >= MaxAlCardCount` (5, or `Wisdom/2+2`), and the screen stays open. `card_choice` then offers only `card_choice_skip`
    and `cardChoiceNote` says why. Same family: a click is not proof, so screens re-read the state afterwards (the agent sees the next phase).
    `MaxAlCardCount = Wisdom / 2 + 2` (`NormalMapManager`), so a negative Wisdom leaves no room at all: seen live with Wisdom -6 → max -1, every reward card refused and
    the bot (rightly) skipped them all. `run.attributes` shows the values; the CLI says "no room" when the maximum is ≤ 0.
16. **`map_continue` is offered too early.** The game's `CanContinueMap` (host) only checks that the `MapItem`s exist; `TryContinue` itself
    needs every `GetNodes()[i].data` filled (a few seconds after the map shows), else it just shows the caption "请填满所有节点" and returns —
    while the game's action still answers `Success`. `MapGate` withholds the action (state `note` says why) and refuses an early click; it also
    covers `isCloseing` / an existing `CurtainTurnUI`, where `SelectMap` silently returns. This, not a flaky click, is why the first
    `map_continue` of every run used to do nothing. General rule: the game's `Success` means "called", never "took effect".
17. **Tutorial overlays are invisible to the game's own window list.** `TutorialSpotlightUI`, `TutorialSpotlightUI2`, … (Canvas children, only in
    "Teach" saves, `onlyTriggerInTutorial`) are multi-slide click-through tips; `UIManager.GetAllUI()` does not list them, so `/ui` and the
    phase model never showed them (the user caught it: tips from the first fight were still on screen in the map). `Tutorial.cs` finds them with
    `FindObjectsOfType`, skips the hidden ones (own `CanvasGroup` alpha 0 / no raycasts = waiting for a trigger such as a story line) and clicks
    the topmost with the same pointer events a click makes (`OnPointerDown` sets `pointerPressStartedOnSpotlight`; a click on the frame the
    overlay opened is ignored, hence "refused … try again"). Hidden overlays must not be counted or the agent spins on a click that cannot work.
18. **Every sixth map the route is built by the player** (`MapManager.Level % 6 == 0` → `MapItemInit`; other levels are filled by the host
    in the background). `MapItem`s in the tray (`MapSelect/…`) are pressed, a line follows the pointer (`StartLine`, Input System based), and on
    release `RayCheck` raycasts for a `SwapContentIdentity` frame (`NodeContent/NodeN`, empty = its `Content/Null` child active) and calls
    `AddToList` → `SetNodes()` → `UpdateCardItemPos()`. `MapNodes.Place` calls those directly (public API, no pointer, no tweens). The game's own
    `map_continue` is offered anyway and `TryContinue` then only shows "请填满所有节点".
    **The tray holds 8 cards for 4 free frames** (`ReadyToSelect`: `SelectNode.GetRange(8 × (Level / 6), 8)`; start and end frames are fixed: an event
    and the elite / boss), so the player chooses. Each batch is random cards (the `Map` table without `Breaks`) with one `Breaks` (the rest room, "一息安隅")
    **appended last, always** (`NormalMapManager`, `SelectNode.Add(Breaks)`): a bot that takes `map_place_0` four times never gets the rest room
    (seen live: two full trays, and 0 rest rooms in the 148 cards `greedy.py` placed over 37 routes). Tray labels read `name attack health tags`. `state.mapChoices` lists the whole tray (kind, id, label).
19. **The hub is a world scene.** `NewHouse/WorldRoot/*` are `SceneItem`s (pointer-click handlers over `PolygonCollider2D`); the game's `house` phase needs
    an active `HouseUI`, which is not up when a save exists, so Start on the menu left the phase `unknown`, windows empty, no action (user screenshot:
    "卡主界面了？"). `Hub.cs`: phase `hub`, action `hub_start_game` = pointer click on the `StartGame` item (single-shot). A saved run resumes from there
    (the save is at the last map: restarting the lab game keeps the run). Also: `SceneItem` is ambiguous with another type, qualify `Witch.UI.Window.SceneItem`.
20. `UIManager` is ambiguous (`Witch.UI` vs `Michsky.MUIP`): alias it. A class named `Text` clashes with UnityEngine.UI.Text (hence `Rich`).
21. Shell: macOS `sed` is BSD (no `\b`), default `cp` here is GNU (use `/bin/cp -Rc` for an APFS clone), the Bash tool is zsh (no word splitting).
    `pkill -f` on a script name misses `python3 -I script.py` — confirm the PID with `ps -o command -p` first.
22. **The rest room is a world scene.** `GameApp.StartBreaks` instantiates the prefab `UI/Breaks` as a root object `Breaks` (`BreaksUI` +
    `Break-Night/com/…`); it is not in `UIManager.GetAllUI()`, `/scene` returns 0 objects for it, so phase `unknown`, `windows` empty,
    no action (user: "商店卡住"). Found with `GET /world?root=Breaks&depth=5` (components, handlers, prefab `onClick` targets, screen position):
    `FoodItem` ×N (Button added in code → `EatFood`: HP += Hp% of current HP + 5, max HP += HPPercent×2, then the object is destroyed),
    The food table has no text (name = "丢失的文本"): the dish is the icon's file name. `Box` → `BreaksUI.RelicBackPoint` (SafeBoxUI), `LeftBox` → `ShowWarehouse` (multiplayer), `Content/CloseRole` → `BreaksUI.Close` (the arrow:
    curtain, destroy the room, `MapManager.TryChange()`). `Rest.cs` offers `rest_eat_N` / `rest_leave`; it claims the phase only when no window besides
    Chat/Caption/TopBar is open (an event or shop above the room stays `unknown` + `ui`). `GameObject.Find("Breaks")` is how the whole game asks "are we resting".
    Method for any future unknown screen: `/world` first, then read the component classes in the decompile.

23. **Events (`EventUI`) are a window but not a phase of the game.** Title `Windows/Map0/Content/Title`; the description fades in paragraph by
    paragraph (private `descriptionParagraphs`, whole text readable at once); options `Selector/option1..4` are `ButtonManager`s that
    appear one after the other (scale tween) and whose click runs `Chicked` → the option's script; `LockChoice` disables one; `EndEvent` hides the others and
    clones the chosen one as the "结束事件" button, whose click is `Entry` (EntryScript: a fight via `StartLevel`, or `TryChangeMap` → leave).
    `Events.Detect` returns null while `Battle.IsActive()` (the window stays behind a fight it started). Option N counts every visible button, disabled included.

24. **The run ends in `GameExitUI`** (after the last fight, or a defeat: static `GameExitUI.loss`). One button `主内容/ButtonUse`: the first click
    (`NextShow`) shows the multipliers, the second (`ReturnAsync`, guarded by `hasClosed`) credits the reward, `ReturnToMenu()` and closes. It is above the
    last `FightUI`, so `Screens.Detect` checks it before `battle_won`. The saved run is NOT cleared by a restart: the lab game came back one fight before the end.
    The tutorial run ("新手考核" event → 永夜化身) ends here too; afterwards Start lands in the hub (`hub_start_game`), the next run starts at route building.

25. **A popup over the hub must be the phase, not the hub.** After the tutorial run Start in the hub opens `AnnouncementUI` (the patch list) and `ModeChoiceUI`
    (story / classic / survival); the phase stayed `hub` with a sign that no longer reacts (user would have seen the bot clicking it every 3 s). `Hub.Detect` and
    `Rest.Detect` now need `UiLite.OnlyAmbientWindows()` (Chat/Caption/TopBar only); `Popups.cs` is a table (window → phase, button path → action id) for such windows,
    the one on top names the phase. Labels are collapsed to one line.

26. **A click that "works" can open a yes/no window instead.** `ModeChoiceUI.NormalMode()` with a saved Normal run does not start a game: it calls
    `UIManager.ShowModalWindow("Tips", "该模式已有存档…", continue / new game)` (`ReturnGame` / `CreateNewSave`, the latter deletes the save).
    The window is a `ModalWindow(Clone)` under `Upper Canvas`, held in the public field `UIManager.WindowObj`; it is not in `GetAllUI()`, and it
    sets `blocksRaycasts = false` on every other root canvas. So the phase stayed `mode_choice`, the bot clicked classic every 3 s (a second
    `ShowModalWindow` returns the open one), and the log said `Clicked` each time. `Popups.DetectModal` makes it phase `modal`
    (`modal_confirm` / `modal_cancel`, `modalTitle`, `modalText`), checked first in `Screens.Detect`; buttons only once `isOn`. Trigger: restarting the
    game with a run in progress (the save is at the last map). Verified live 2026-10-13: menu → hub → classic → modal → "继续游戏" → map → battle.
    It also happens for the other modes. Other `ShowModalWindow` callers (quit game, delete save, …) now surface as `modal` too; `greedy.py` only
    ever confirms "continue" and declines the rest.

27. **A card can open a selection prompt in the middle of a fight.** `ScriptExecutor.ChooseCardToAction` / `FightUI.Burning` / throw start a coroutine
    (`CardToAction`, `Burn`, …) that sets the statics `FightUI.InIEn`, `SelectType`, `SpecialCount` (picks still missing), `SelectedCard`,
    hides `ClockBoard/结束回合` and locks the hand (`CardItem.canUse = false`), then waits for `selectConfirmed`. Picking = a left click on a hand
    card, which ends in the private `CardItem.HandleSelectModeClick` (a second click takes it back; with `SpecialCount <= 0` a new pick replaces the
    last one); confirming = `FightUI.Yes()`, valid when `SpecialCount <= 0 || CanBeforeEnd || all cards chosen`. The game's phase model and the old
    `Battle.cs` knew nothing of it: `canAct=false`, no action, the bot stalled 90 s (2026-10-13 23:30, "选择1卡牌丢弃" after 冥想针剂).
    `Battle.Selecting()` + `battle_select_card_N` / `battle_select_confirm` + `battle.select`. Verified live 2026-10-13 23:36: the restart resumed the
    same fight (the save is at the last map), the same 冥想针剂 prompt came up again, the bot chose the card, confirmed, and the fight went on
    (next card played, turn ended, next turn played). Unit tests: CLI rendering, bot policy.

28. **Rewards and purchases go to the backpack, a fight reads only the deck.** `RoleTable.UnCardList` (backpack, `MaxAlCardCount`) vs `cardList` (deck,
    `CardBottomCount`–`CardTopCount`); the move is `OutDeckUI` → `ShowCard.MoveItem()` (sell `SellItem`, destroy `DecomposeItem`), which `Deck.cs` calls and then
    reads back. Before it existed no card an agent picked ever reached a fight and the full backpack refused further rewards (trap 15). Relics: six slots
    (`relicList` armed, `WithoutArmedRelicList` spare; the shop UI arms a bought relic when there is room, a seventh goes to the warehouse and does nothing,
    so `shop_buy_N` carries a `warn`). Verified live: bot decks grew 15 → 18 through `deck_equip_N`.
29. **After a win `fightType` never reads `Win` at a frame boundary** (`Fight_Win.Init` swaps it to `None` in the same call), so `battle_won` used to be
    unreachable and every fight waited ~10 s for the game's own chest auto-open (7 of 7 fights, 0 `battle_open_rewards`). `Battle.ChestReady()` (chest object on and
    the private `ShowReward` still set) is the durable sign: `battle_won` now comes ~3 s after the last enemy.
30. **A returned call is not an effect, and `deck_` ids clash.** A career skill (`SkillItem.TrueUse`; an aimed one needs the private `hitEnemy` set first)
    can open a card picker (`DeckUI`, phase `deck_select`, 神选 = "retrieve a card from the draw pile") and the fight behind it is NOT paused. Its ids
    (`deck_pick_N`) share the `deck_` prefix with the deck window's: `Deck.Owns` once swallowed them ("the deck window is not open"), found live, now
    `DeckRules.IsPickerAction`. Twice a 神选 use changed nothing at all (no cooldown, no picker, no card; cause not pinned down: the message now says "no
    effect seen yet" and gives the draw pile size). `PlayerWindow()` also refuses while the picker is open.
31. **The screen before a run (`GameEntryUI`) is where the run is decided, and `game_entry_start` skipped it.** Live state before any click: career 阿米莉娅
    (`RoleTable.Career`), partner 报丧偈羽 (`GameEntryUI.partner`, static), main Strength / second Wisdom (private `MainVar` / `SecondVar` Transforms under
    `MainParent` / `SecondParent`). A click is `ShowCareer.OnPointerClick` (→ `ShowDetail`, sets the statics) and `FightVarItem.OnPointerClick` (→ `SelectVar`; choosing
    the other slot's attribute makes the game move that slot to a neighbour); all synchronous, the get-ready window (`Window Manager`) is opened first because the
    choices animate inside it. Card packs: only `CardPackUI.UseCardPack` changes on a toggle; `GameRuntimeData.UseCardPack` (what `GameConfigManager.CardPackCheck`
    filters every card / relic / blessing pool with) is written in `CardPackUI.OnDestroy`, i.e. when the screen goes, and persists across runs; at least 6 must stay on
    (this profile lists exactly 6, so only the refusal was exercised). Difficulty tags (`SelectHardUI.UseSc` = `GameRuntimeData.HardTags`) persist too, are shown in `entry.hardTags`,
    and need `playCount ≥ 1` to open; no action edits them.

32. **The hub's book pages: every page object stays active, the shown one is `WindowManager.currentWindowIndex`.** `NewHouse/UIRoot/WindowCanvas/Window Manager`
    (MUIP) holds the seven pages (`Windows/图书馆`, `高塔`, `展览馆`, `储藏室`, `行政厅`, `温泉`, `魔女屋`); "the first active child" read as the library for all seven (seen live).
    `HouseManager.CloseBook` deactivates `WindowCanvas`, and **`GameObject.Find("A/B/C")` returns inactive descendants as long as the root `A` is active**, so a closed
    hub once reported an open page: always check `activeInHierarchy`. The pages' buttons are `HouseItem`s (`houseItemType`): library `Dictionary` + `cardShop`,
    tower `rollShop`, exhibition `TeachMode` ×2 (deletes the tutorial save), storage `CardEditor`, administration `announcement`; spring and witch house have none.
    `HouseRules` says which an agent may press. A first visit shows a tutorial tip over the page (`tutorial_advance`).
33. **Truth crystals and time are the profile's currency** (`GameRuntimeData.Truth/Time`, `ObfuscatedInt`, shown in `hub`). Achievements pay them once
    (`AchItem.GetReward()`, window `AchievementUI` from the HUD's Task button; the item's `Button` is interactable only while claimable); the hub's meta shops spend them.
    Some achievements fire on a purchase ("领受神恩", unlock a tower upgrade): buying one thing made another claimable.
34. **The meta shop (`OutsiderShopUI`) is one window with two tabs**: `cardShop` ("借阅图书") shows the `card` / `cardpack` rows of the `BuyItems` table, `rollShop` ("唤醒本源")
    the rest (stat levels ×5 for 20+ truth each time: price = `Price × (bought + 1)`, `TimePrice` overrides; careers, partners, relics, items). `OutsideShopItem.TryBuy` returns
    silently when the purse is short (`OutsiderRules.BuyRefusal` says why first) and runs `BuyScript` 400 ms later; the last level opens `ItemShowUI` (phase `item_reveal`)
    **over the shop**, so `OutsiderShop.Detect` yields to `Popups.Detect`. A bought one-off with `CanClose == TRUE` is a switch (`UsedBuyedItems`); no such item has been seen yet,
    so `outsider_toggle_N` is unit-tested only. Spending is permanent: the lab save is its own, a real save would lose the currency.

35. **"使徒" (unlock all outsider growth) = every priced row of the `BuyItems` table bought `BuyCount` times**, except `outsideshop_2` (the 999-level 破损表盘) and
    `outsideshop_29` (`AchievementRuntimeService.AreAllShopItemsPurchased`): careers, partners and the ten library card packs all count. Left to buy on the lab profile
    (2026-10-19): 14 careers 6000 + 10 card packs 3800 + 神话之路 100 ≈ 9900 truth; Time has no other use. A Steam guide that says careers/familiars are exempt is wrong.
    Truth per run is `RewardCal(score)` capped at 500 (150 score per point up to 7500, 300 up to 16500, then 500); score = relics (350·2^rarity) + gold/2 + 20 per attribute point
    + feats·3 + blessings + cards, times a level bonus (0 when the player died) and the difficulty / hard-tag multiplier; a lost run pays ~130, a mid-game death less.
36. **The boss 失心魔女 copies the player's starting 元素 and, if any card is played on turn 1, his 超凡** (Steam discussion, unverified here), then snowballs past block;
    advice from players: do not feed it turn-1 plays, prefer block that scales with 元素. The bot's ten-fight record against it is 0-1.

37. **Chaos Paradise (Slot mode, `mode_choice_slot`, player level 25+) pays far more truth per minute than a classic run, and leaving early is the game's own feature**
    (tutorial slide 12: "click the top right to leave early"). `SlotMode.cs`: phase `slot` (`slot.payoutTruth` = what leaving now pays, `slot.baseScore`, chips, draws) and the action
    `slot_end_run`, which only opens the game's yes/no window ("是否要结束此局" = `TopBarUI.CheckSlot`); its confirm is `modal_confirm_end_run` (named by effect, `Danger.cs`), then the usual
    `run_end` screen (two clicks). `EndSlot` itself stays on the `ui` deny list. Payout (measured 2026-10-19, difficulty tags maxed, no card picked): **+222 truth, +133 time, ~+2 levels per run,
    a full automatic cycle takes ~23 s** (`examples/greedy.py --mode slot`). Why: the Slot settlement (`SlotMachineManager.PrepareChipSettlement`: score capped at 50000, tier function
    `RewardCalByGameExitRule` -> 133 truth at the cap) is paid, and `GameExitUI` then pays its own classic formula on its own breakdown (~89) — both land. The score is boosted by the
    "深渊" term `(1 + min(EXHard,100)/5)(1 + EXHard/100) - 1` (~x40), where `EXHard` = sum of tag level x 5 x stacks. A classic run, for comparison: ~+130 truth in 8-25 minutes.
    **Difficulty tags** (statue on the `game_entry` screen: `click Statue`, then per tag `Delete` once = max stack when at 0 — `Add` wraps past the max back to 0 —, then `确定保存`; `一键重置` clears
    them) are saved in the profile (`GameRuntimeData.HardTags`) and make every later classic run brutal: **reset them (`一键重置` + `确定保存`) before the profile goes to the real game.**
    Never press `加注` in Slot mode: it spends permanent truth (50 chips per crystal). Unintended-behaviour caveat: a Steam guide (since removed) called this cheating; a patch can end it.

38. **Reading the game's rules instead of guessing: `witchctl raw GET '/config?table=Buff&match=%E9%87%8D%E7%94%9F'`** (`ConfigDump.cs`; keywords must be percent-encoded; no `table` lists them: Card, Enemy,
    EnemyCard, Buff, Relic, Career, Bless, Partner, Level, CardPack, BuyItems, Hard, Event, SlotCal…). Rows carry their Lua-ish scripts (`UseScript`, `ApplyScript`), so a card, buff or boss move can be read exactly.
    Facts found this way that decide runs: **重生 (rebirth)**: death with ≥30 layers revives at "layers"% max HP and then costs 100 layers (a partner's 重生70 is one life, per run, not per fight);
    **enemy damage and HP scale with the map level** (a boss at level ~17 has ~12x the base row: 2117 HP, ~400 per hit), and 失心魔女's 镜像复制 copies every buff of the player; 菲奥涅裁决术 is an *execute*
    (target under (10+神启)% of ITS OWN max HP), not a free kill; 复仇短匕 (card_10, base pool) deals 10% of the player's max HP, 复仇链枪 50% of the HP lost so far.
39. **奈奈's doom-power engine (the run was won with it, 2026-10-19: WON, 20k max HP, bosses die to one basic card).** Parts: (a) the hand-made card 厄运盛宴 (hub, 储藏室 → 创造魔法 = `CardEditorUI`, 50 x rarity truth):
    10 negative buffs (毒素 流血 易伤 灼烧 失能 遗忘 腐朽 混乱 无力 失格) x999 on 自己, cost 0, rarity 1. Negative buffs REFUND card points (capped at -100 in total) and the layer count costs nothing, so the card is free;
    positive buffs, damage and block are expensive (about 25 damage for the whole budget): no god cards. (b) 奈奈's skill `careercard_2` 可食用厄运 (CD 5, aimed: `skill_use_N_self`) eats all negative buffs: **per kind
    min(max(1, rarity*layers/5), 10) doom power, total 67**; doom power (`buff_DoomPower`, kept in `PlayerInfo.SpecialVars`, restored at every fight start) adds its layer count to MAX HP on every change, so max HP grows
    about 173 → 442 → 822 → 1292 → 2409 → … 20000 over a run. (c) her skill `careercard_3` 灾祸具现化 (CD 2, unaimed) = buff 灾祸化身: temporary strength and perception equal to the doom power and 2% of max HP to every
    enemy on each action: that is the damage (a 3500-HP boss died on the turn it was used; the Demon King (14707) to one 炽焰新星 after eating again). (d) the card reaches the run through the **safe box**
    (`SafeBoxUI`, only in a rest room: `rest_safebox`; right-click the card (`witchctl click --right ID`) → floating menu `移动` — **never `丢弃`, it destroys the card** —; `取出金钱` takes gold out, +500 per rest point):
    the card is MOVED out of the profile into the run and is gone when the run ends: craft one per run. Route policy `rest-first` puts the rest room into the first frame.
    **A bug that killed a run**: the generic card rule played 厄运盛宴 as a harmless 0-cost buff while 可食用厄运 cooled down: 999 layers of poison with nothing to eat them. `pick_card` never plays it;
    `eat_plan` plays it only when the skill is usable, and eats the moment the poison is on. Whole pipeline: `scripts/nana/run.sh` (craft → first rest room → safe box → the rest of the run; ~15–30 min at `--speed 2`).
    **Real Steam game (2026-10-19): WON, 14 fights, 11 eats, 92725 points, never died (`docs/runs/2026-10-19-nana-real-steam-win.log`).** **Lab record (2026-10-19)**: attempt 1 lost (the feast bug), 2 WON (assembled by hand + bot, 20k max HP), 3 unknown (the log was lost: editing `run.sh` while it ran shifts bash's read offset — never edit a script
    that is running), **4 WON hands-free through `run.sh`** (14 min, 93758 points, 12 fights, 8 eats, 27 transformations, never died; `docs/runs/2026-10-19-nana-win-attempt4.log`), **5 WON again** with the hold fix
    (14.4 min, 78608 points, 13 fights, 9 eats, 15 transformations, never died; `docs/runs/2026-10-19-nana-win-attempt5.log`).
    Tuning learned: the deck must stay lean (`--max-deck 20`; 43 cards drew the feast card a third as often) and the transformation ends fights in a turn, so it is held (`hold_transform`) until the eat is done,
    turn 3 or health under 40 % — otherwise the doom power (max HP, strength) stops at ~400 and the Demon King fight is a coin flip (attempt 4 had 5013 HP there, attempt 2 had 20k).
40. **The floating menu and right clicks.** `FloatingWindow` (the card editor's add-effect list, the safe box's right-click menu) is not a UIManager window: `UiLite` now walks it too (`ui` shows a `FloatingWindow`
    section, `click ID` works). `click --right` sends a right click. Card editor flow: `添加词条` → `使用时` → `添加一个buff` → set the level with `/ui/do` `set_text` (`scripts/nana/make_card.py`) → pick the buff
    with its icon (paged: page 2 for 无力 / 失格) → `确认编辑` (spends the truth). `witchctl speed 2` (`/speed`, `Time.timeScale`, clamp 0.25–4) halves nothing much: fights still wait on animations, ~25 % faster.

## Scene interface inventory (2026-10-13)
Every screen/scene and what a player can do there, with what is modelled, value, effort and a roadmap: `docs/scene-interfaces.md`; the five raw static
reports behind it: `docs/inventory/`. Facts still worth knowing before touching any screen: `mode_choice_survival` is really Slot mode (player Lv ≥ 25), Story has
no handler, Sublimation is unmapped (its evaluation, nothing built: `docs/scene-interfaces.md` §6); generic `modal_cancel` can mean "New Game" (deletes the saved run) and `modal_confirm` can mean "quit the game" (hence the
effect in the id, `Danger.cs`); `/ui/do` and `/scene/do` pass straight to the game's own services and bypass the denylist.

## Not done / ideas
- Windows build of the mod is untested (paths in `lab.sh` are macOS only). The mod is installed in the real Steam copy since 2026-10-19 (the user's go; `Contents/Mods/WitchCtl`, copy of `build/WitchCtl.local.dll` + `mod/ModConfig.json` + `mod/Icon.png`); remove that folder to go back to the vanilla game. A real run: launch through Steam, `witchctl audio off`, `main_menu_start_game`, then `LOG=… WITCHCTL_PROFILE=<real SaveData.sav> scripts/nana/run.sh`; move a stale `Saves/Normal*.sav` aside first (a saved run turns Start into the continue/new-game modal). Do not hide the game window (the user declined).
- Not exercised live yet: `play --self` / cards with `targets` other than `enemy` or none, the card picker for an enchantment or an event (`CardEnchUI` → `DeckSelect`),
  the divination blessing picker over `DestinyTreeUI`, `shop_refresh`, `deck_sell/destroy/unequip`, enabling a card pack. Not modelled: difficulty-tag editing, `outsider_toggle_N` (no switchable item seen),
  Sublimation, playing the Slot mode beyond leaving it (picking rewards, standing, tokens); `map_place_N` has no un-place (dragging a placed card back).
- No real combat AI; `examples/greedy.py` is a smoke test.
- Card text in the enemy's turn loses its numbers ("获得点护盾。" instead of "获得5点护盾。"); the player's turn is fine (the numbers come from live card values). Unfixed: an agent cannot act then anyway.
- `actions`/`state` exec time grew from ~6 to ~10 ms in the `unknown` phase (the `FindObjectsOfType` scans of `Tutorial`, `Hub`, `Rest`); the whole call is ~55 ms. Cache the finds (e.g. 0.5 s) if the budget gets tight.
- A defeat goes straight to `run_end` (lost), a won run too; after that the hub shows `announcement` / `item_reveal` popups once per patch / new item.
