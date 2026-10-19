# Changelog

All notable changes to this project are documented here. Format: [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

### Added
- **A whole classic run won hands-free** (2026-10-19, 奈奈 with the doom-power engine, `scripts/nana/run.sh`; see CLAUDE.md traps 38–40): `GET /config?table=…&match=…` (`mod/ConfigDump.cs`, the game's config rows with their
  scripts), `POST /speed` + `witchctl speed N`, `witchctl click --right`, the floating menu (card editor, safe box) in `ui` / `click`, the rest room's `rest_safebox` action, the card editor and the safe box
  (`CardEditor` allowed in the hub; the card is crafted with `scripts/nana/craft_feast.py` and taken into the run with `scripts/nana/safebox.py`).
- **A whole classic run won in the real Steam game** (2026-10-19, user's go: "安装 mod 并打"): the mod installed into the Steam copy's `Contents/Mods/WitchCtl` (`Mods/` existed, empty; the bundle's seal was already invalid, no re-sign needed),
  game started through Steam (`open -g steam://rungameid/3709430`, `witchctl audio off` right after the bridge came up), `scripts/nana/run.sh` unchanged. 奈奈 never died (rebirth stayed at 70), 14 fights, 11 eats, 92725 points,
  profile Lv 151 → 154, truth 968 → 1220, one stored 厄运盛宴 used up (9 left). Log: `docs/runs/2026-10-19-nana-real-steam-win.log`. `scripts/nana/craft_feast.py` reads the profile named by `WITCHCTL_PROFILE` (default: the lab's).
  Files: scripts/nana/craft_feast.py, README.md, CLAUDE.md, docs/real-game-howto.md, docs/runs/2026-10-19-nana-real-steam-win.log
- Two whole runs won hands-free in a row through `scripts/nana/run.sh` (2026-10-19, ~14 min each, logs in `docs/runs/`); the bot holds the transformation until the eat is done (`hold_transform`) and can cap the deck (`--max-deck`).
- `docs/real-game-howto.md`: the doom-power build by hand in the real game (no tools); `scripts/nana/craft_feast.py --copies N` stocks the profile's storage with hand-made cards (10 were stored in the real profile).
- `examples/greedy.py`: `--career`, `--partner`, `--packs`, `--prefer` (card names to take over rarer ones), `--speed`, `--pause PHASE|WINDOW` (hand over to a person: exit code 10), `--route rest-first`;
  `eat_plan` (the card and the skill, only ever together), the transformation skill, health-scaling attack cards valued by the player's max HP, rebirth/revival logging, fight-start and run-end log lines.
- Chaos Paradise (Slot mode) as phase `slot` (`mod/SlotMode.cs`): `slot` section (chips, draws, day/night, base score, `payoutTruth` = what leaving now pays) and the action
  `slot_end_run`, which opens the game's own "end this run?" window (confirm: `modal_confirm_end_run`); the settlement is the existing `run_end`. `EndSlot` stays on the deny list.
  The CLI renders the `slot:` line. `examples/greedy.py --mode slot` runs the whole cycle hands-free in ~23 s and pays ~+222 truth per run with the difficulty tags up
  (a classic run: ~+130 in 8-25 minutes). `mode_pick`, `modal_choice(allow_end_run)`, `slot_action`; tests in `tests/test_greedy_play.py` and `tests/test_cli.py`.
- The bot logs the settlement screen (`run end: WON|lost, total N (遗物 .., 金币 .., 深渊 ..)`).

### Changed
- `examples/greedy.py` plays to win instead of attacking blindly (the first full run with a maxed profile won nine fights in a row and was
  then killed in three turns by the middle boss, having never blocked): free draws and kills first, block when the announced damage (or what the
  last enemy turn cost, `TurnMemory`) would leave it under half health, heal when low, buffs before attacks, attacks by damage per energy at the
  enemy it kills else the weakest. Event options are scored on their effect text (relic > blessing > gold, curses and losses subtract, a gamble
  with a prize gets a bonus); card rewards are picked by rarity then effect size; `--route bold` (now the default) builds the route from the two
  hardest fights, the shop and the rest room last. One life per run, no save/load tricks. `tests/test_greedy_play.py` (42 tests).

### Added
- `scripts/dashboard.py` + `dashboard.html`: a read-only progress page on loopback (default `:47801`), pushed live over Server-Sent Events
  (`/api/stream`; `/api/status` is the one-shot form). One poller thread reads the bridge's `/state` 5 times a second while a page is connected (every
  second otherwise), the bot's log (new lines pushed with line numbers, appended as DOM nodes) and an optional hand-kept task checklist
  (`--tasks FILE`), and publishes only what changed; the frame number and ages are left out of the change check, a page that cannot keep up is
  dropped and the browser reconnects with a fresh snapshot. Shows the fight (HP bars, intents, hand) or the run (gold, deck, relics), the
  profile's currency, the bot feed with win/play counters and a stall banner. Never sends anything but `GET /state`. `tests/test_dashboard.py`
  (26 tests, incl. push latency and a headless-Chrome render check done by hand).
- `scripts/steam-whoami.py`: which Steam account the local client is on (live session from the connection log, else the autologin account),
  read from Steam's own files with no Steamworks call. `--expect NAME` exits 1 on any other account; `--hook` prints SessionStart hook JSON.
  Used as a gate: the game is only launched on the alt account `eqckd`.
- The game's generic yes/no window (`UIManager.ShowModalWindow`, `ModalWindow(Clone)` under `Upper Canvas`) is phase `modal`: `modalTitle`,
  `modalText` and the actions `modal_confirm` / `modal_cancel`; it outranks every other screen. Classic mode with a saved run opens it
  ("continue game / new game"), and before, `mode_choice_classic` just reopened it every 3 s while the phase stayed `mode_choice`.
- `examples/greedy.py` answers it (`modal_choice`): resume a saved run, decline any other question. `tests/test_greedy.py`.
- Card selection prompts in a fight (discard / burn / "choose N cards": `FightUI.InIEn` + `SelectType`): `battle.select` (type, title, need,
  chosen, canConfirm, optional), hand cards carry `chosen`, and the actions `battle_select_card_N` (choose / take back, the result is read back
  from `SelectedCard`) and `battle_select_confirm` (`FightUI.Yes()`). Before, the turn button was hidden, every card unplayable and no action
  offered: the bot sat there until its 90 s stall alarm ("选择1卡牌丢弃" after 冥想针剂). `greedy.py` answers it (`select_choice`).
- `scripts/bootstrap-dotnet.sh` also installs the decompiler into `.toolchain/ilspycmd` (output goes to `.toolchain/decomp`, both ignored).
- Career skills (`mod/Skills.cs`): `battle.skills` (name, text, cooldown, `aimed`, `usable`) and the actions `skill_use_N`, `skill_use_N_self`,
  `skill_use_N_enemy_M` (an aimed skill needs its target in the id). The result is read back and says so when nothing observable changed
  ("no effect seen yet …, draw pile N"): a returned `TrueUse` is not proof. Live: 阿米莉娅's 神选 (opens the card picker) and 奈奈's two skills,
  aimed at an enemy and at herself, refused without a target / with a bad one.
- Card picker (`mod/DeckSelect.cs`, phase `deck_select`: "choose N cards" from the draw pile, an enchantment, an event): `select` and the actions
  `deck_pick_N` / `deck_select_cancel`. Live through 神选; the enchantment and event entries are not yet exercised.
- Deck window (`mod/Deck.cs`, phase `deck`): `deck_open` next to every phase's actions, `deck_equip_N`, `deck_unequip_N`, `deck_sell_N` (a shop is open),
  `deck_destroy_N` (costs gold), `deck_close`; limits are checked first (`mod/DeckRules.cs`) so the game's "limit reached" modal never opens, and the move
  is read back from the card lists. Rewards and purchases go to the backpack and a fight reads only the deck: before this, nothing an agent
  picked ever reached a fight, and a full backpack made the game refuse every further card reward (`run`: deck / backpack / relic counts).
  Live: open, equip, close.
- Shops (`mod/Shops.cs`, phase `shop`: the shop, the destiny tree, the blood-line engraving): `shop` (items with price, why not buyable, relic-slot
  warning, refresh cost), `shop_buy_N`, `shop_refresh`, `shop_leave` (`mod/ShopRules.cs`). Before, a shop was `unknown` and all the gold earned was
  never spent. Live: buying a relic and a card, leaving the shop and the engraving, opening the destiny tree.
- The screen before a run (`mod/GameEntry.cs`, `mod/EntryRules.cs`): `entry` (career and partner with their skills, attributes, card packs, leftover
  difficulty tags) and `entry_career_N`, `entry_partner_N`, `entry_main_VAR`, `entry_second_VAR`, `entry_pack_on_ID` / `entry_pack_off_ID`,
  `entry_prepare_open` / `entry_prepare_close`. `game_entry_start` calls `StartGame()` and skipped all of it, so every run used the defaults. Live:
  career, partner, both attributes (including the game moving the other one on a clash), the pack floor of 6, closing the window; enabling a pack and
  editing difficulty tags (read-only here) are not exercised (the profile has exactly 6 packs, all enabled).
- `map`: the level counter, whether the player builds the route (`level % 6 == 0`), the fixed first and last stops, and the node type in `mapNodes`.
- Achievements (`mod/Achievements.cs`, phase `achievements`, opened by `hub_open_achievements`): every achievement with progress / target / reward, `achievement_claim_N`
  (the game's own `AchItem.GetReward`, the truth / time payout is read back) and `achievement_close`. Live: open, claim, refusals, close.
- The hub: `hub` (level, exp, truth crystals, time, runs played, the open book page and its buttons), `hub_visit_<building>` for the seven book pages, `hub_page_close`,
  `hub_page_item_N` (`mod/HouseRules.cs` decides which buttons: shops, the task list and the announcement yes; the card editor, mod manager, tutorial replay no, with the reason).
- The hub's meta shops (`mod/OutsiderShop.cs`, `mod/OutsiderRules.cs`, phase `outsider_shop`, from the library's 借阅图书 and the tower's 唤醒本源): `outsiderShop` (tab, truth,
  time, every item with price, levels bought, whether the purse covers it), `outsider_buy_N` (for good: the currency is read back), `outsider_toggle_N`, `outsider_leave`. A buy is
  only offered when affordable (the game's `TryBuy` returns silently). Live: both tabs, a stat level (672→652 truth, next level 20→40), a one-off item (the item-reveal popup takes
  over the phase) and the refusals; `outsider_toggle_N` is unit-tested only (no switchable item seen).
- `witchctl state` renders `hub`, `achievements` and the meta shop. `run.attributes` (the run's Strength / Lucky / Perceive / Wisdom) is in the state: a negative Wisdom leaves the
  backpack no room (`Wisdom / 2 + 2`), which is why the game refuses every card pick; the CLI says "no room" instead of "full" then.
- Attack cards carry `targets` (who they may hit: `enemy`, `self`, `enemy|self` or `none`; other cards have none) and `play --self` aims at the
  player; without it the first living enemy is still used (`mod/TargetRules.cs`). Unit-tested; live only `enemy` and untargeted cards have come up so far.
- Yes/no windows whose button has a lasting effect say so in its id (`modal_cancel_new_game_deletes_save`, `modal_confirm_quit_game`,
  `modal_confirm_return_to_menu`, `modal_confirm_end_run`; the harmless ones stay `modal_confirm` / `modal_cancel`) and the bot answers only the safe
  ones (`mod/Danger.cs`). Event options show the game's hover text (`event.options[].effect`).
- Buttons that quit the game, give the run up, open external links or wreck the map (`mod/Danger.cs`) are listed under `blocked` in `ui` and refused by
  `/click`. The game's own `/ui/do` and `/scene/do` pass-throughs are not filtered.
- `witchctl recon` and `greedy.py --recon`: record every new screen (state, UI and world dumps, with the wiring of each button) under `.recon/`.
  `GET /world?window=<name>` dumps one window.
- `examples/greedy.py --route rest` and `--runs N`: when the player builds the route (a tray of 8 cards for 4 frames) the default takes the first four, which can never be
  the rest room (the game always appends it as the tray's last card); `rest` keeps it for the last frame, right before the fixed end boss. `--runs N` stops after N finished runs,
  so two policies can be compared run by run. Default behaviour is unchanged.

### Changed
- `battle_won` is reported as soon as the chest is ready (about 3 s after the last enemy dies; `Battle.ChestReady`). The game never leaves
  `fightType == Win` standing (`Fight_Win.Init` swaps it to `None` in the same call), so every fight used to wait about 10 s for the game's own auto-open.
- `examples/greedy.py` also manages the deck (equips spare cards, opens the deck window from a full-backpack card choice), spends gold in shops
  (relics, then blessings, then cards; enchantments and divination are left alone), answers card pickers, and uses career skills.

### Fixed
- The card picker's ids (`deck_pick_N`, `deck_select_cancel`) were claimed by the deck window's `deck_` prefix and refused with "the deck window is not
  open": the bot looped on 神选's picker. Found live; ownership is now decided in one place (`DeckRules.IsPickerAction`).
- `examples/greedy.py` skipped reward cards while `CardChoiceUI` was still fading in (neither pick nor skip was offered yet).
- `examples/greedy.py`'s 90 s stall alarm ran on the wall clock: a laptop lid closed mid-fight (the Mac sleeps, the game freezes) read as "stuck for 90 s"
  on the first look after each wake (two false exits in a row, 2026-10-14). The timer is now `StuckWatch` on `time.monotonic()`, which stands still in sleep on macOS.
- A career skill whose use changed nothing (seen twice with 神选: no cooldown, no picker, no new card) was reported as a plain success; the result now
  says "no effect seen yet" with the draw pile size, so the cause can be pinned down. Skills are also neither offered nor accepted with no living enemy.

## [0.2.0] - 2026-10-13

### Added
- Post-fight screens as phases with ordinary action ids (`mod/Screens.cs`): `battle_won` (`battle_open_rewards`), `rewards`
  (`reward_claim_N`, `reward_close`), `card_choice` (`card_choice_pick_N`, `card_choice_skip`), `blessing_choice` (`blessing_pick_N`),
  `battle_lost`. Before, an agent was stuck right after its first win.
- Scene-changing actions fire once (`mod/SingleShot.cs`): a repeated `map_continue` / `game_entry_start` / `main_menu_start_game` is refused
  while the scene is still changing. This was the real cause of the "second fight never initialises" bug (two fights started on top of each other).
- `state`: `note` (why an action is missing), `windows` (open windows), `battle.player.piles` (draw / discard sizes), `flow.dealing / busy / fake / lastPlay`, per-card `uid`
  and enemy `alive`; `play --uid`; `state --full` for the raw config rows.
- `examples/greedy.py`: an agent that plays through menus, fights, rewards and the map using only the CLI.
- `tests/`: live test of a whole first fight (menu → fight → rewards → map) and regression tests for every fix below.
- Tutorial overlays (`TutorialSpotlightUI`, shown in the first-run "Teach" game over any phase and blocking a human's pointer) are
  reported as `tutorial` (speaker, text, slide n/m) with the action `tutorial_advance`, which clicks the topmost visible one. Before,
  no agent ever saw or dismissed them.
- Route building (every sixth map): `mapNodes` (the frames, empty or what they hold), `mapChoices` (the card tray) and `map_place_N`,
  which puts tray card N into the first empty frame the way releasing a dragged card does. Before, the agent stalled on that map.
- The hub (`NewHouse`, reached by Start on the main menu when a save exists) is phase `hub` with `hub_start_game` (a click on the world sign).
  Before it was `unknown` with no window and no action, and an agent sat there for good.
- The rest room (prefab `Breaks`, a world scene whose window list is empty) is phase `rest`: `rest` in the state (HP, food) and the actions
  `rest_eat_N` and `rest_leave`. Before it was `unknown` with no action (user: "商店卡住"); it comes right after the route is built.
- Story events (`mod/Events.cs`) are phase `event`: `event` (title, full text, options with `interactable`, `resolved`) and `event_option_N`.
  Before they were `unknown` and an agent sat in front of the single option "开战" for good.
- The end-of-run settlement screen (`mod/RunEnd.cs`, `GameExitUI`) is phase `run_end` with `runEnd` (cleared/lost, score breakdown) and
  `run_end_continue` (two clicks: breakdown, then back to the hub). Before it was `unknown` with no action after the last fight.
- Hub popups (`mod/Popups.cs`): `announcement` (`announcement_close`) and `mode_choice` (`mode_choice_story|classic|survival|back`), `item_reveal` (`texts` = the new item, `item_reveal_confirm`). The hub and the
  rest room only claim the phase when no other window is open over them (before, the bot clicked the dead hub sign every 3 s).
- `GET /world?root=<name>&depth=N` (`witchctl raw GET ...`): components, pointer handlers, prefab click targets and screen position of the
  objects below a world root. The game's own scene snapshot returns nothing for scenes like this one.

### Changed
- Card and enemy-intent text is the game's own rendering (placeholders resolved, `<color>`/`<sprite>` stripped), so intents show real
  numbers (`造成9伤害`, was `造成{0}伤害`). Buffs are the real buffs with stack counts.
- The hand carries `{uid, id, name, kind, type, rarity, cost, tags, text, playable}` instead of the complete multi-language table row.
- `canAct` stays false until all cards are dealt, and while a card asks for a choice or the end-turn button is hidden.
- Targets: `--target` is the enemy's index in the state; without it the first living enemy is used.
- CLI: a bridge that accepts but does not answer in time exits 1 (was 2, "unreachable"); "refused" is said once; `raw` with invalid JSON exits 2.
- `scripts/lab.sh` finds the game by its executable path, not its command line, refuses `install` while the game runs, and `stop` waits until the
  process is gone (an immediate `start` failed with LaunchServices error -600).

### Fixed
- `map_continue` was offered (and reported as done) while the game was still filling in the map nodes; the click then only made the game
  show "请填满所有节点" and nothing happened. The action is now withheld, with `note` saying why, until a click takes effect, and a click
  before that is refused.
- A card pick was reported as done while the game silently refused it (full card collection: only a "card limit" modal appears and the screen
  stays open). `card_choice` then offers only `card_choice_skip` and explains why in `note`.
- Attack cards could be played at a dead enemy that had not been removed yet; HP no longer shows below zero (`dead` flag instead).
- A request that timed out on the main thread could still run later, so a retried `play` / `act` / `click` ran twice.
- `play` reported success when the game refused the card; the outcome is re-checked when the play resolves and kept in `flow.lastPlay`.
  A second `play` while one is still resolving is refused.
- `buffs` listed queued effects instead of the unit's buffs.
- Card-choice labels were `<sprite name="1">`; hand cards are no longer offered as clickable UI items (a click skipped every check).
- A malformed JSON body gives 400, not 500; `EnemyManager` access outside a fight no longer throws.
- Mute is applied from the first frame and re-asserted in `LateUpdate` too.

## [0.1.0] - 2026-10-12

### Added
- `mod/`: C# mod for 魔女：终末旅途 (Witch's Apocalyptic Journey) that serves a loopback HTTP API from inside the game
  (`/ping /state /actions /act /play /ui /click /audio /debug`, plus the game's own slow `/ui/full /ui/do /scene /scene/do`).
  Loaded by the game's official mod system (`Mods/WitchCtl/Scripts/Entry.dll`), no injection.
- `cli/witchctl`: stdlib-only Python CLI (`ping state actions act play end-turn ui click wait audio raw`),
  compact text output for agents, `--json` for the raw response, exit codes 0/1/2.
- Side-effect-free battle logic (`Battle.cs`): legality of cards, `play`, `end-turn`.
- Fast UI path (`UiLite.cs`): active windows only, direct pointer click (~5 ms instead of 0.8 s / 2.4 s).
- `scripts/lab.sh`: APFS-clone of the game with its own bundle id (isolated saves/prefs), build+install, hidden
  background launch that never takes focus, stop, reset.
- Tests: 18 CLI unit tests against a fake bridge; live end-to-end tests including a battle from a fresh profile
  and a regression test for the state-read side effect (`WITCHCTL_E2E_PLAY=1`).

### Fixed
- Reading state during a battle no longer spends energy or plays cards. The game's own
  `RuntimeGameplayAutomationService.GetLegalActions` does that (see CLAUDE.md), so it is bypassed while a fight is on.
