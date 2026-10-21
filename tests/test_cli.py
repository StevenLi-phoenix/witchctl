import json
import os
import subprocess
import sys
import tempfile
import time
import unittest

from tests.support import BATTLE_STATE, CARD_CHOICE_STATE, CLI, REWARDS_STATE, FakeBridge, run_cli


class CliAgainstFakeBridge(unittest.TestCase):
    def tearDown(self):
        getattr(self, "bridge", None) and self.bridge.close()

    def serve(self, routes):
        self.bridge = FakeBridge(routes)
        return self.bridge

    def test_ping_text(self):
        b = self.serve({("GET", "/ping"): {"ok": True, "version": "0.1.0", "served": 3, "failed": 0, "audioMuted": True}})
        code, out, _ = run_cli("ping", port=b.port)
        self.assertEqual(code, 0)
        self.assertIn("v0.1.0", out)
        self.assertIn("muted=True", out)

    def test_battle_state_rendering(self):
        b = self.serve({("GET", "/state"): BATTLE_STATE})
        code, out, _ = run_cli("state", port=b.port)
        self.assertEqual(code, 0)
        self.assertIn("phase: battle", out)
        self.assertIn("魔女 HP 70/80 DEF 5 buffs: 力量×2", out)
        self.assertNotIn("[Default]", out)   # the normal unit state is not worth a tag
        self.assertIn("draw 4 discard 2", out)
        self.assertIn("[0] 史莱姆 HP 12/20", out)
        self.assertIn("[1] 蝙蝠 HP 0/9 DEF 0 [dead]", out)
        self.assertIn("intends: 黏黏冲撞 — 造成8点伤害。", out)
        self.assertIn("[0] ✓ 打击 (strike) cost 1 攻击牌 — 造成5点伤害。", out)
        self.assertIn("[1] ✗ 防御 (block) cost 3 消耗技能牌 [Burnout] — 获得7点护盾。 抽1张牌  (not enough power (3 > 2))", out)
        self.assertIn("canAct=True", out)
        self.assertIn("battle_end_turn  结束回合", out)
        self.assertNotIn("?", out.replace("?", "", 0) if False else out.split("intends")[0].replace("↗", ""))

    def test_card_selection_prompt_is_shown(self):
        # a card asked the player to pick hand cards: nothing is playable, the prompt and the picks so far must be visible
        battle = BATTLE_STATE["battle"]
        hand = [{**battle["hand"][0], "playable": False, "reason": "a card selection is open", "chosen": True},
                {**battle["hand"][1], "playable": False, "reason": "a card selection is open", "chosen": False}]
        select = {"type": "Burn", "title": "选择1卡牌丢弃", "need": 0, "chosen": 1, "canConfirm": True, "optional": False}
        state = {**BATTLE_STATE, "actions": [{"Id": "battle_select_card_1", "Kind": "battle_select_card", "Label": "防御", "Parameters": {"cardIndex": 1}},
                                             {"Id": "battle_select_confirm", "Kind": "battle_select_confirm", "Label": "确定"}],
                 "battle": {**battle, "hand": hand, "select": select}}
        b = self.serve({("GET", "/state"): state})
        _, out, _ = run_cli("state", port=b.port)
        self.assertIn("select:  Burn — 选择1卡牌丢弃 (choose 0 more, 1 chosen, can confirm)", out)
        self.assertIn("(strike) cost 1 攻击牌 — 造成5点伤害。  (a card selection is open)  ← chosen", out)
        self.assertEqual(out.count("← chosen"), 1)
        self.assertIn("battle_select_confirm  确定", out)

    def test_failed_last_play_is_called_out(self):
        state = {**BATTLE_STATE, "battle": {**BATTLE_STATE["battle"], "flow": {**BATTLE_STATE["battle"]["flow"],
                 "lastPlay": {"Seq": 3, "Ok": False, "CardId": "strike", "Error": "the game did not accept the card"}, "fake": True}}}
        b = self.serve({("GET", "/state"): state})
        _, out, _ = run_cli("state", port=b.port)
        self.assertIn("warning: last play (strike) failed: the game did not accept the card", out)
        self.assertIn("scripted-fight", out)

    def test_rewards_and_card_choice_screens_render(self):
        b = self.serve({("GET", "/state"): REWARDS_STATE})
        _, out, _ = run_cli("state", port=b.port)
        self.assertIn("phase: rewards", out)
        self.assertIn("reward [0] 金钱 — 64  (claimed)", out)
        self.assertIn("reward [1] 卡牌 — 选择一张卡牌", out)
        self.assertIn("reward_close  关闭", out)
        b.close()
        b = self.serve({("GET", "/state"): CARD_CHOICE_STATE})
        _, out, _ = run_cli("state", port=b.port)
        self.assertIn("card [0] 火球 cost 2 (rarity 2) — 造成12点伤害。", out)
        self.assertIn("card [1] 结界 cost 1 (rarity 1) — 获得6点护盾。  (not pickable)", out)
        self.assertIn("card_choice_skip  跳过", out)

    def test_full_card_collection_shows_why_nothing_is_pickable(self):
        """The game refuses a pick (modal "card limit") when the backpack is full; the state must say so and offer only skip."""
        state = {**CARD_CHOICE_STATE, "actions": [CARD_CHOICE_STATE["actions"][1]],
                 "cardChoice": [{**c, "pickable": False} for c in CARD_CHOICE_STATE["cardChoice"]],
                 "note": "card collection full (5/5): picking is refused by the game, only skip is possible"}
        b = self.serve({("GET", "/state"): state})
        _, out, _ = run_cli("state", port=b.port)
        self.assertIn("note: card collection full (5/5)", out)
        self.assertNotIn("card_choice_pick_0", out)
        self.assertIn("card_choice_skip  跳过", out)

    def test_tutorial_overlays_are_shown_with_their_advance_action(self):
        state = {**BATTLE_STATE,
                 "tutorial": [{"speaker": "克莉斯娜", "text": "左下角是魔女的魔能", "slide": 1, "slides": 2},
                              {"speaker": "克莉斯娜", "text": "点击右上角的按钮", "slide": 1, "slides": 1}],
                 "actions": [{"Id": "tutorial_advance", "Kind": "tutorial_advance", "Label": "继续"}] + BATTLE_STATE["actions"]}
        b = self.serve({("GET", "/state"): state})
        _, out, _ = run_cli("state", port=b.port)
        self.assertIn("tutorial: 克莉斯娜 (1/2): 左下角是魔女的魔能", out)
        self.assertIn("tutorial: 克莉斯娜 (1/1): 点击右上角的按钮", out)
        self.assertIn("tutorial_advance  继续", out)

    def test_map_route_and_tray_are_shown_with_place_actions(self):
        state = {"phase": "map_select", "frame": 9, "scene": "game", "windows": ["MapSelectUI"],
                 "note": "2 route node(s) are still empty: fill them with map_place_N first",
                 "mapNodes": [{"index": 0, "empty": False, "holds": "腐化蛛人 9 120"}, {"index": 1, "empty": True, "holds": None}],
                 "mapChoices": [{"index": 0, "kind": "Event", "id": "shop", "label": "旅行商人"}],
                 "actions": [{"Id": "map_place_0", "Kind": "map_place", "Label": "旅行商人"}]}
        b = self.serve({("GET", "/state"): state})
        _, out, _ = run_cli("state", port=b.port)
        self.assertIn("route:   [0] 腐化蛛人 9 120  [1] (empty)", out)
        self.assertIn("tray [0] 旅行商人 (Event)", out)
        self.assertIn("map_place_0  旅行商人", out)
        self.assertNotIn("map_continue", out)

    def test_map_not_ready_shows_the_reason_and_no_continue(self):
        state = {"phase": "map_select", "frame": 9, "scene": "game", "windows": ["MapSelectUI"], "actions": [],
                 "note": "the map nodes are still being filled in by the game; wait a few seconds"}
        b = self.serve({("GET", "/state"): state})
        _, out, _ = run_cli("state", port=b.port)
        self.assertIn("phase: map_select", out)
        self.assertIn("note: the map nodes are still being filled in", out)
        self.assertNotIn("map_continue", out)

    def test_rest_room_shows_hp_food_and_leave(self):
        state = {"phase": "rest", "frame": 9, "scene": "game", "windows": [],
                 "rest": {"hp": 27, "maxHp": 115, "food": [
                     {"index": 0, "id": "food_1", "name": "黑面包", "healPercentOfHp": 6, "maxHpUp": 8},
                     {"index": 1, "id": "food_2", "name": None, "healPercentOfHp": 3, "maxHpUp": 4}]},
                 "actions": [{"Id": "rest_eat_0", "Kind": "rest_eat", "Label": "黑面包"},
                             {"Id": "rest_leave", "Kind": "rest_leave", "Label": "离开"}]}
        b = self.serve({("GET", "/state"): state})
        _, out, _ = run_cli("state", port=b.port)
        self.assertIn("phase: rest", out)
        self.assertIn("rest:    HP 27/115", out)
        self.assertIn("food [0] 黑面包 (heals 6% of HP + 5, max HP +8)", out)
        self.assertIn("food [1] food_2 (heals 3%", out)   # no localized name: fall back to the id
        self.assertIn("rest_leave  离开", out)

    def test_click_can_be_a_right_click(self):
        sent = []
        b = self.serve({("POST", "/click"): lambda body: (sent.append(body), {"ok": True, "clicked": {"id": 5, "label": "x", "path": "p"}})[1]})
        code, out, _ = run_cli("click", "5", "--right", port=b.port)
        self.assertEqual(code, 0)
        self.assertEqual(sent, [{"Id": 5, "Right": True}])
        run_cli("click", "5", port=b.port)
        self.assertEqual(sent[1], {"Id": 5})   # a plain click does not mention the button

    def test_speed_sets_the_game_clock_and_reads_it_back(self):
        sent = []
        b = self.serve({("POST", "/speed"): lambda body: (sent.append(body), {"timeScale": body["Scale"]})[1], ("GET", "/speed"): {"timeScale": 3.0}})
        code, out, _ = run_cli("speed", "3", port=b.port)
        self.assertEqual(code, 0)
        self.assertEqual(sent, [{"Scale": 3.0}])
        self.assertIn("game speed x3", out)
        _, out, _ = run_cli("speed", port=b.port)
        self.assertIn("game speed x3", out)

    def test_slot_mode_shows_chips_and_what_leaving_pays(self):
        state = {"phase": "slot", "frame": 9, "scene": "game", "windows": ["SlotMachUI"],
                 "slot": {"chips": 5000, "chipsEarned": 5300, "nextStake": 100, "draws": 0, "night": False, "dayBattles": 0, "baseScore": 2107, "payoutTruth": 133},
                 "actions": [{"Id": "slot_end_run", "Kind": "slot_end_run", "Label": "结束此局"}]}
        b = self.serve({("GET", "/state"): state})
        _, out, _ = run_cli("state", port=b.port)
        self.assertIn("phase: slot", out)
        self.assertIn("slot:    chips 5000 (earned 5300, next stake 100) | draws 0 | day | base score 2107 | leaving now pays 133 truth", out)
        self.assertIn("slot_end_run  结束此局", out)

    def test_event_shows_text_options_and_which_are_disabled(self):
        state = {"phase": "event", "frame": 9, "scene": "game", "windows": ["EventUI"],
                 "event": {"id": "Event_1", "title": "新手考核", "text": "新手考核，即将模拟对战永夜化身\n第二段", "resolved": False,
                           "options": [{"index": 0, "label": "开战", "interactable": True, "effect": "战斗，胜利后获得遗物"},
                                       {"index": 1, "label": "离开", "interactable": False, "effect": ""}]},
                 "actions": [{"Id": "event_option_0", "Kind": "event_option", "Label": "开战"}]}
        b = self.serve({("GET", "/state"): state})
        _, out, _ = run_cli("state", port=b.port)
        self.assertIn("phase: event", out)
        self.assertIn("event:   新手考核", out)
        self.assertIn("新手考核，即将模拟对战永夜化身 第二段", out)   # paragraphs are folded into one line
        self.assertIn("option [0] 开战", out)
        self.assertIn("→ 战斗，胜利后获得遗物", out)   # the effect text the game shows only on hover
        self.assertIn("option [1] 离开  (disabled)", out)   # no effect text: no arrow
        self.assertIn("event_option_0  开战", out)

    def test_deck_window_lists_both_zones_with_limits_and_gold(self):
        state = {"phase": "deck", "frame": 9, "scene": "game", "windows": ["OutDeckUI"],
                 "deck": {"inDeck": 13, "minimum": 13, "maximum": 20, "backpack": 2, "backpackMax": 5, "gold": 140, "shopOpen": False,
                          "cards": [{"index": 0, "zone": "deck", "id": "c1", "name": "火雨术", "cost": "2", "rarity": "2", "type": "攻击", "tags": "", "text": "造成8点伤害"},
                                    {"index": 1, "zone": "backpack", "id": "c2", "name": "绽放", "cost": "1", "rarity": "1", "type": "技能", "tags": "Eternal", "text": "获得5点护盾"}]},
                 "actions": [{"Id": "deck_unequip_0", "Kind": "deck_unequip", "Label": "火雨术"}, {"Id": "deck_close", "Kind": "deck_close", "Label": "关闭"}]}
        b = self.serve({("GET", "/state"): state})
        _, out, _ = run_cli("state", port=b.port)
        self.assertIn("deck:    13 in deck (13–20), 2/5 in backpack, 140 gold", out)
        self.assertIn("[0] deck     火雨术 (2) — 造成8点伤害", out)
        self.assertIn("[1] backpack 绽放 (1) [Eternal] — 获得5点护盾", out)
        self.assertIn("deck_close  关闭", out)

    def test_career_skills_are_listed_with_cooldown_and_aim(self):
        import copy
        state = copy.deepcopy(BATTLE_STATE)
        state["battle"]["skills"] = [{"index": 0, "id": "skill_1", "name": "神罚", "text": "对目标造成20点伤害", "cooldown": 0, "aimed": True, "usable": True},
                                     {"index": 1, "id": "skill_2", "name": "祈祷", "text": "回复5点生命", "cooldown": 2, "aimed": False, "usable": False}]
        b = self.serve({("GET", "/state"): state})
        _, out, _ = run_cli("state", port=b.port)
        self.assertIn("skills:", out)
        self.assertIn("[0] 神罚 — ready, aimed — 对目标造成20点伤害", out)
        self.assertIn("[1] 祈祷 — cooldown 2 — 回复5点生命", out)

    def test_map_shows_level_who_builds_the_route_and_the_fixed_stops(self):
        state = {"phase": "map_select", "frame": 9, "scene": "game", "windows": ["MapSelectUI"],
                 "map": {"level": 12, "playerBuilds": True, "positionInCycle": 0,
                         "start": {"kind": "Event", "holds": "庭院"}, "end": {"kind": "Fight", "holds": "巨蜥洞穴 13 231 流血"}},
                 "mapNodes": [{"index": 0, "empty": False, "kind": "Fight", "holds": "魔女教教徒 7 96"}, {"index": 1, "empty": True, "kind": None, "holds": None}],
                 "actions": [{"Id": "map_continue", "Kind": "map_continue", "Label": "开启冒险"}]}
        b = self.serve({("GET", "/state"): state})
        _, out, _ = run_cli("state", port=b.port)
        self.assertIn("map:     level 12 (you build the route, 0 of 6 done) | start Event 庭院 | end Fight 巨蜥洞穴 13 231 流血", out)
        self.assertIn("route:   [0] Fight: 魔女教教徒 7 96  [1] (empty)", out)

    def test_before_the_run_screen_shows_choices_packs_and_leftover_difficulty(self):
        state = {"phase": "game_entry", "frame": 9, "scene": "game", "windows": ["GameEntryUI"],
                 "entry": {"windowOpen": True, "career": None, "careerAtStart": "神选者", "partner": {"id": "partner_2", "name": "炼金师"}, "partnerAtStart": None,
                           "careers": [{"index": 0, "id": "career_1", "name": "神选者", "selected": False, "sanMax": "60", "skills": ["active: 神选 — 检索一张牌 [1]"]}],
                           "partners": [{"index": 0, "id": "partner_1", "name": "骑士", "selected": False, "skills": []},
                                        {"index": 1, "id": "partner_2", "name": "炼金师", "selected": True, "skills": ["passive: 炼成 — 开局获得药剂"]}],
                           "main": "Strength", "second": "Wisdom", "attributes": ["Strength", "Lucky", "Perceive", "Wisdom"],
                           "packs": {"enabled": 7, "minimum": 6, "cards": 80, "relics": 40, "blessings": 30, "enchantments": 9,
                                     "list": [{"id": "cardpack_1", "name": "基础", "type": "Basic", "enabled": True}, {"id": "cardpack_9", "name": "火焰", "type": "Expansion", "enabled": False}]},
                           "hardTags": {"active": [{"id": "hard_3", "name": "强敌", "level": 2, "max": "3"}], "extraReward": 40}},
                 "actions": [{"Id": "game_entry_start", "Kind": "game_entry_start", "Label": "开始冒险"},
                             {"Id": "entry_career_0", "Kind": "entry_career", "Label": "神选者"}]}
        b = self.serve({("GET", "/state"): state})
        _, out, _ = run_cli("state", port=b.port)
        self.assertIn("before the run  [get-ready window open]", out)
        self.assertIn("career:  (none yet; the game takes 神选者 at the start)", out)   # nothing chosen: say what the game will pick
        self.assertIn("[0] ○ 神选者 (san 60) — active: 神选 — 检索一张牌 [1]", out)
        self.assertIn("partner: 炼金师 (partner_2)", out)
        self.assertIn("[1] ● 炼金师 — passive: 炼成 — 开局获得药剂", out)
        self.assertIn("attributes: main Strength, second Wisdom  (of Strength, Lucky, Perceive, Wisdom)", out)
        self.assertIn("packs:   7 enabled (at least 6) — 80 cards, 40 relics, 30 blessings, 9 enchantments", out)
        self.assertIn("[x] 基础 (cardpack_1)  [ ] 火焰 (cardpack_9)", out)
        self.assertIn("difficulty tags: 强敌 2/3 (extra reward 40)", out)   # a leftover tag makes every run harder: it must be visible
        self.assertIn("entry_career_0  神选者", out)

    def test_shop_lists_items_prices_and_why_something_cannot_be_bought(self):
        state = {"phase": "shop", "frame": 9, "scene": "game", "windows": ["ShopUI"],
                 "shop": {"window": "ShopUI", "gold": 100, "backpack": 5, "backpackMax": 5,
                          "refresh": {"cost": 20, "used": 0, "max": 3, "available": True, "note": None},
                          "items": [{"index": 0, "kind": "Relic", "name": "影劫草", "price": 90, "rarity": "2", "text": "洗牌时造成20点伤害", "buyable": True, "note": None},
                                    {"index": 1, "kind": "Card", "name": "烈阳", "price": 50, "rarity": "1", "text": "给予灼烧4层", "buyable": False,
                                     "note": "the backpack is full (5/5): put a card into the deck first"},
                                    {"index": 2, "kind": "Relic", "name": "咒怨石像", "price": 286, "rarity": "3", "text": "x", "buyable": True, "note": None,
                                     "warn": "all 6 relic slots are taken: it would go to the warehouse and do nothing"}]},
                 "actions": [{"Id": "shop_buy_0", "Kind": "shop_buy", "Label": "影劫草"}, {"Id": "shop_leave", "Kind": "shop_leave", "Label": "离开"}]}
        b = self.serve({("GET", "/state"): state})
        _, out, _ = run_cli("state", port=b.port)
        self.assertIn("shop:    ShopUI | 100 gold | backpack 5/5 | refresh 20 (0/3)", out)
        self.assertIn("[0] Relic  影劫草 — 90 gold — 洗牌时造成20点伤害", out)
        self.assertIn("[1] Card   烈阳 — 50 gold — 给予灼烧4层  (can't buy: the backpack is full (5/5)", out)
        self.assertIn("⚠ all 6 relic slots are taken", out)
        self.assertIn("shop_buy_0  影劫草", out)

    def test_hub_shows_profile_numbers_and_the_open_book_page(self):
        state = {"phase": "hub", "frame": 9, "scene": "NewHouse", "windows": [],
                 "hub": {"level": 7, "exp": 120, "truth": 672, "time": 3, "runsPlayed": 4, "page": "高塔",
                         "pageItems": [{"index": 0, "type": "rollShop", "label": "唤醒本源", "allowed": True, "note": None},
                                       {"index": 1, "type": "CardEditor", "label": "创造魔法", "allowed": False, "note": "spends truth crystals on a hand-made card"}]},
                 "actions": [{"Id": "hub_page_close", "Kind": "hub_page_close", "Label": "关闭书页"}, {"Id": "hub_page_item_0", "Kind": "hub_page_item", "Label": "唤醒本源"}]}
        b = self.serve({("GET", "/state"): state})
        _, out, _ = run_cli("state", port=b.port)
        self.assertIn("hub:     level 7 (exp 120) | 672 truth, 3 time | 4 runs played", out)
        self.assertIn("page 高塔 is open", out)
        self.assertIn("[0] rollShop 唤醒本源", out)
        self.assertIn("[1] CardEditor 创造魔法  (not offered: spends truth crystals on a hand-made card)", out)   # a button the CLI will not press says why
        closed = dict(state, hub=dict(state["hub"], page=None, pageItems=[]))
        b2 = self.serve({("GET", "/state"): closed})
        _, out2, _ = run_cli("state", port=b2.port)
        self.assertNotIn("is open", out2)

    def test_achievements_list_claimable_rewards_and_progress(self):
        state = {"phase": "achievements", "frame": 9, "scene": "NewHouse", "windows": ["AchievementUI"],
                 "achievements": {"truth": 672, "time": 3, "claimable": 1, "total": 3,
                                  "items": [{"index": 0, "id": "a1", "name": "初出茅庐", "text": "赢得一场战斗", "progress": 1, "target": 1, "reward": "20", "rewardType": "Truth", "claimable": True, "claimed": False},
                                            {"index": 1, "id": "a2", "name": "百战", "text": "赢得100场战斗", "progress": 12, "target": 100, "reward": "50", "rewardType": "Time", "claimable": False, "claimed": False},
                                            {"index": 2, "id": "a3", "name": "起步", "text": "开始游戏", "progress": 1, "target": 1, "reward": "5", "rewardType": "Truth", "claimable": False, "claimed": True}]},
                 "actions": [{"Id": "achievement_claim_0", "Kind": "achievement_claim", "Label": "初出茅庐"}, {"Id": "achievement_close", "Kind": "achievement_close", "Label": "关闭"}]}
        b = self.serve({("GET", "/state"): state})
        _, out, _ = run_cli("state", port=b.port)
        self.assertIn("achievements: 1 of 3 can be claimed | 672 truth, 3 time", out)
        self.assertIn("[0] ★ 初出茅庐 — 20 Truth — 赢得一场战斗 (1/1)", out)
        self.assertIn("[1]   百战 — 50 Time — 赢得100场战斗 (12/100)", out)
        self.assertIn("[2] ✓ 起步 — 5 Truth — 开始游戏 (1/1)  claimed", out)

    def test_meta_shop_lists_prices_levels_and_what_the_purse_cannot_pay(self):
        state = {"phase": "outsider_shop", "frame": 9, "scene": "NewHouse", "windows": ["OutsiderShopUI"],
                 "outsiderShop": {"tab": "origins", "truth": 552, "time": 0,
                                  "items": [{"index": 0, "id": "b1", "name": "强健体魄", "text": "最大生命+", "type": "", "currency": "Truth", "price": 40, "bought": 1, "levels": 5, "mode": "buy", "used": False, "affordable": True, "note": None},
                                            {"index": 1, "id": "b2", "name": "重生保险", "text": "死亡后复活", "type": "", "currency": "Truth", "price": 999, "bought": 0, "levels": 1, "mode": "buy", "used": False, "affordable": False, "note": "costs 999 truth crystals (have 552)"},
                                            {"index": 2, "id": "b3", "name": "传奇之路", "text": "x", "type": "", "currency": "Truth", "price": 100, "bought": 1, "levels": 1, "mode": "owned", "used": True, "affordable": True, "note": None},
                                            {"index": 3, "id": "b4", "name": "静音", "text": "y", "type": "", "currency": "Truth", "price": 10, "bought": 1, "levels": 1, "mode": "toggle", "used": False, "affordable": True, "note": None}]},
                 "actions": [{"Id": "outsider_buy_0", "Kind": "outsider_buy", "Label": "强健体魄"}, {"Id": "outsider_leave", "Kind": "outsider_leave", "Label": "离开"}]}
        b = self.serve({("GET", "/state"): state})
        _, out, _ = run_cli("state", port=b.port)
        self.assertIn("meta shop: origins | 552 truth, 0 time", out)
        self.assertIn("[0] 强健体魄 — 40 Truth — level 1/5 — 最大生命+", out)
        self.assertIn("[1] 重生保险 — 999 Truth — level 0/1 — 死亡后复活  (can't buy: costs 999 truth crystals (have 552))", out)
        self.assertIn("[2] 传奇之路 — owned (on)", out)
        self.assertIn("[3] 静音 — owned, switch with outsider_toggle_3 (off)", out)
        self.assertIn("outsider_buy_0  强健体魄", out)

    def test_card_picker_shows_caption_choices_and_what_is_chosen(self):
        state = {"phase": "deck_select", "frame": 9, "scene": "game", "windows": ["DeckUI"],
                 "select": {"remaining": 1, "chosen": 1, "caption": "请选择1张牌",
                            "cards": [{"index": 0, "id": "c1", "name": "火雨术", "cost": "2", "tags": "", "text": "造成8点伤害", "chosen": False},
                                      {"index": 1, "id": "c2", "name": "绽放", "cost": "1", "tags": "", "text": "获得5点护盾", "chosen": True}]},
                 "actions": [{"Id": "deck_pick_0", "Kind": "deck_pick", "Label": "火雨术"}]}
        b = self.serve({("GET", "/state"): state})
        _, out, _ = run_cli("state", port=b.port)
        self.assertIn("picker:  请选择1张牌 (1 more, 1 chosen)", out)
        self.assertIn("[0] 火雨术 (2) — 造成8点伤害\n", out)
        self.assertIn("[1] 绽放 (1) — 获得5点护盾  ← chosen", out)

    def test_run_summary_line_flags_a_full_backpack(self):
        state = {"phase": "card_choice", "frame": 9, "scene": "game", "windows": [], "actions": [],
                 "run": {"gold": 85, "inDeck": 14, "minimum": 13, "maximum": 20, "backpack": 5, "backpackMax": 5, "relics": 6, "relicSlots": 6}}
        b = self.serve({("GET", "/state"): state})
        _, out, _ = run_cli("state", port=b.port)
        self.assertIn("run:     85 gold | deck 14 (13–20) | backpack 5/5 (full) | relics 6/6", out)
        state["run"]["backpack"] = 1
        _, out, _ = run_cli("state", port=b.port)
        self.assertIn("backpack 1/5 | relics 6/6", out)
        self.assertNotIn("(full)", out)

    def test_a_backpack_without_room_says_why_and_shows_the_attributes(self):
        # MaxAlCardCount = Wisdom / 2 + 2: Wisdom -6 gives -1, every card pick is refused (seen live: the bot skipped every reward)
        state = {"phase": "map_select", "frame": 9, "scene": "game", "windows": [], "actions": [],
                 "run": {"gold": 1497, "inDeck": 20, "minimum": 14, "maximum": 14, "backpack": 1, "backpackMax": -1, "relics": 6, "relicSlots": 6,
                         "attributes": {"Strength": 3, "Wisdom": -6, "Lucky": 2}}}
        b = self.serve({("GET", "/state"): state})
        _, out, _ = run_cli("state", port=b.port)
        self.assertIn("backpack 1/-1 (no room: every card pick is refused)", out)
        self.assertIn("attributes: Strength 3, Wisdom -6, Lucky 2", out)

    def test_run_end_shows_result_score_and_continue(self):
        state = {"phase": "run_end", "frame": 9, "scene": "game", "windows": ["GameExitUI"],
                 "runEnd": {"won": True, "score": {"遗物": 526, "卡牌": 721}, "total": 1247, "detailsShown": True, "closing": False},
                 "actions": [{"Id": "run_end_continue", "Kind": "run_end_continue", "Label": "返回主菜单"}]}
        b = self.serve({("GET", "/state"): state})
        _, out, _ = run_cli("state", port=b.port)
        self.assertIn("phase: run_end", out)
        self.assertIn("run end: cleared  score 1247", out)
        self.assertIn("遗物 526  卡牌 721", out)
        self.assertIn("run_end_continue  返回主菜单", out)

    def test_state_full_asks_for_the_raw_rows(self):
        b = self.serve({("GET", "/state"): BATTLE_STATE})
        run_cli("state", "--json", "--full", port=b.port)
        self.assertEqual(b.requests[0][1], "/state?full=1")

    def test_json_flag_works_after_subcommand(self):
        b = self.serve({("GET", "/state"): BATTLE_STATE})
        code, out, _ = run_cli("state", "--json", port=b.port)
        self.assertEqual(code, 0)
        self.assertTrue(out.startswith("{"))
        self.assertIn('"phase": "battle"', out)

    def test_unreachable_bridge_exits_2_with_hint(self):
        code, _, err = run_cli("ping", port=1)
        self.assertEqual(code, 2)
        self.assertIn("not reachable", err)

    def test_no_answer_in_time_exits_1_not_2(self):
        """A busy game is not a missing game: the command may still run, so the exit code must not say 'unreachable'."""
        b = self.serve({("GET", "/state"): lambda _: time.sleep(1.5) or BATTLE_STATE})
        code, _, err = run_cli("state", "--timeout", "0.3", port=b.port)
        self.assertEqual(code, 1)
        self.assertIn("timeout", err)
        self.assertNotIn("not reachable", err)

    def test_raw_with_invalid_json_is_a_usage_error(self):
        b = self.serve({("POST", "/act"): {}})
        code, _, err = run_cli("raw", "POST", "/act", "{nope", port=b.port)
        self.assertEqual(code, 2)
        self.assertIn("not valid JSON", err)
        self.assertEqual(b.requests, [])

    def test_refused_action_exits_1_and_prints_reason(self):
        b = self.serve({("POST", "/act"): {"Success": False, "ActionId": "x", "Message": "Action is not currently legal in the present game state."}})
        code, out, _ = run_cli("act", "x", port=b.port)
        self.assertEqual(code, 1)
        self.assertIn("refused: Action is not currently legal", out)
        self.assertEqual(b.requests[0][2], {"ActionId": "x"})

    def test_play_by_uid_survives_shifting_indices(self):
        b = self.serve({("POST", "/play"): {"Success": True, "Message": "queued", "CardId": "strike"}})
        code, _, _ = run_cli("play", "--uid", "uid-strike", "--target", "0", port=b.port)
        self.assertEqual(code, 0)
        self.assertEqual(b.requests[0][2], {"CardUid": "uid-strike", "TargetIndex": 0})
        code, _, err = run_cli("play", port=b.port)
        self.assertEqual(code, 2)
        self.assertIn("needs a hand index or --uid", err)

    def test_play_self_aims_at_the_player(self):
        b = self.serve({("POST", "/play"): {"Success": True, "Message": "queued", "CardId": "heal"}})
        code, _, _ = run_cli("play", "1", "--self", port=b.port)
        self.assertEqual(code, 0)
        self.assertEqual(b.requests[0][2], {"CardIndex": 1, "TargetSelf": True})

    def test_play_self_and_target_together_is_a_usage_error(self):
        code, _, err = run_cli("play", "1", "--self", "--target", "0", port=1)
        self.assertEqual(code, 2)
        self.assertIn("either --target or --self", err)

    def test_hand_card_shows_when_it_cannot_be_aimed_at_an_enemy(self):
        import copy
        state = copy.deepcopy(BATTLE_STATE)
        hand = state["battle"]["hand"]
        self.assertGreaterEqual(len(hand), 2)
        hand[0]["targets"] = "self"
        hand[1]["targets"] = "enemy"
        hand.append(dict(hand[0], index=2, name="圣光", targets="enemy|self"))
        b = self.serve({("GET", "/state"): state})
        _, out, _ = run_cli("state", port=b.port)
        lines = [ln for ln in out.splitlines() if ln.startswith("  [") and "cost" in ln and "enemies" not in ln]
        cards = [ln for ln in lines if "cost" in ln and "✓" in ln or "✗" in ln]
        self.assertIn("[aim: self]", cards[0])
        self.assertNotIn("[aim:", cards[1])   # a plain attack stays quiet
        self.assertIn("[aim: enemy|self]", cards[2])

    def test_play_is_queued_by_default_and_says_so(self):
        b = self.serve({("POST", "/play"): {"Success": True, "Message": "queued", "CardId": "strike", "PowerBefore": 3, "HandCountBefore": 5}})
        code, out, _ = run_cli("play", "2", "--target", "1", port=b.port)
        self.assertEqual(code, 0)
        self.assertEqual(b.requests[0][2], {"CardIndex": 2, "TargetIndex": 1})
        self.assertIn("queued: strike", out)

    def test_play_wait_reports_the_resolved_card(self):
        b = self.serve({("POST", "/play"): {"Success": True, "Message": "Card played.", "HandCountBefore": 5, "HandCountAfter": 4,
                                            "PowerBefore": 3, "PowerAfter": 2, "TargetName": "slime", "TargetHpBefore": 20, "TargetHpAfter": 14}})
        code, out, _ = run_cli("play", "2", "--wait", port=b.port)
        self.assertEqual(code, 0)
        self.assertEqual(b.requests[0][2], {"CardIndex": 2, "Wait": True})
        self.assertIn("hand 5→4  power 3→2", out)
        self.assertIn("slime HP 20→14", out)

    def test_refused_play_exits_1(self):
        b = self.serve({("POST", "/play"): {"Success": False, "Message": "refused: not enough power (3 > 2)"}})
        code, out, _ = run_cli("play", "0", port=b.port)
        self.assertEqual(code, 1)
        self.assertIn("not enough power", out)

    def test_end_turn_posts_the_end_turn_action(self):
        b = self.serve({("POST", "/act"): {"Success": True, "Message": "Ended the current turn."}})
        code, _, _ = run_cli("end-turn", port=b.port)
        self.assertEqual(code, 0)
        self.assertEqual(b.requests[0][2], {"ActionId": "battle_end_turn"})

    def test_click_by_label_id_and_path(self):
        reply = {"ok": True, "clicked": {"id": 7, "label": "制作名单", "path": "a/b"}}
        b = self.serve({("POST", "/click"): reply})
        run_cli("click", "制作名单", port=b.port)
        run_cli("click", "-4890", port=b.port)
        run_cli("click", "--path", "TextList/Credits", port=b.port)
        self.assertEqual([r[2] for r in b.requests], [{"Text": "制作名单"}, {"Id": -4890}, {"Path": "TextList/Credits"}])

    def test_ambiguous_click_lists_candidates_and_exits_1(self):
        b = self.serve({("POST", "/click"): {"ok": False, "error": "ambiguous", "message": "use id",
                                             "candidates": [{"id": 1, "label": "确定", "path": "A/ok"}, {"id": 2, "label": "确定", "path": "B/ok"}]}})
        code, _, err = run_cli("click", "确定", port=b.port)
        self.assertEqual(code, 1)
        self.assertIn("ambiguous", err)

    def test_click_without_target_is_usage_error(self):
        code, _, err = run_cli("click", port=1)
        self.assertEqual(code, 2)
        self.assertIn("needs an id", err)

    def test_server_error_is_reported_on_stderr(self):
        b = self.serve({})
        code, _, err = run_cli("actions", port=b.port)
        self.assertEqual(code, 1)
        self.assertIn("not_found", err)

    def test_wait_times_out_with_exit_1(self):
        b = self.serve({("GET", "/state"): {"phase": "main_menu", "frame": 1, "actions": []}})
        code, _, err = run_cli("wait", "--phase", "battle", "--wait-timeout", "0.4", "--interval", "50", port=b.port)
        self.assertEqual(code, 1)
        self.assertIn("timeout", err)

    def test_wait_returns_once_phase_appears(self):
        t0 = time.monotonic()
        b = self.serve({("GET", "/state"): lambda _: {"phase": "battle" if time.monotonic() - t0 > 0.3 else "map_select", "frame": 1, "actions": []}})
        code, out, _ = run_cli("wait", "--phase", "battle", "--wait-timeout", "5", "--interval", "30", port=b.port)
        self.assertEqual(code, 0)
        self.assertIn("phase: battle", out)

    def test_wait_for_action_and_text(self):
        ui = {"windows": [{"name": "RewardUI", "clickables": [{"id": 1, "label": "领取奖励", "interactable": True}], "texts": ["战斗胜利"]}]}
        b = self.serve({("GET", "/state"): {"phase": "unknown", "frame": 1, "actions": [{"Id": "map_continue", "Kind": "map_continue"}]},
                        ("GET", "/ui"): ui})
        self.assertEqual(run_cli("wait", "--action", "map_continue", "--wait-timeout", "2", port=b.port)[0], 0)
        self.assertEqual(run_cli("wait", "--text", "战斗胜利", "--wait-timeout", "2", port=b.port)[0], 0)
        self.assertEqual(run_cli("wait", "--text", "不存在", "--wait-timeout", "0.3", port=b.port)[0], 1)

    def test_wait_survives_a_bridge_that_comes_up_late(self):
        import socket
        import threading
        s = socket.socket(); s.bind(("127.0.0.1", 0)); port = s.getsockname()[1]; s.close()  # free port, nobody listening yet
        late = {}
        threading.Timer(0.4, lambda: late.update(b=FakeBridge({("GET", "/state"): {"phase": "main_menu", "frame": 1, "actions": []}}, port=port))).start()
        code, out, _ = run_cli("wait", "--phase", "main_menu", "--wait-timeout", "6", "--interval", "50", port=port)
        if "b" in late:
            late["b"].close()
        self.assertEqual(code, 0)
        self.assertIn("phase: main_menu", out)

    def test_ui_rendering(self):
        ui = {"windows": [{"name": "MainMenuUI", "clickables": [{"id": -1, "label": "开始游戏", "interactable": True}, {"id": -2, "label": "模组管理", "interactable": False}], "texts": ["v1.1.0"]}]}
        b = self.serve({("GET", "/ui"): ui})
        code, out, _ = run_cli("ui", port=b.port)
        self.assertEqual(code, 0)
        self.assertIn("[-1] 开始游戏", out)
        self.assertIn("[-2] 模组管理  (disabled)", out)
        self.assertIn("· v1.1.0", out)

    def test_ui_lists_blocked_elements_apart_with_the_reason(self):
        ui = {"windows": [{"name": "MainMenuUI", "clickables": [{"id": -1, "label": "开始游戏", "interactable": True}],
                           "blocked": [{"path": "background/TextList/ExitGame", "label": "退出游戏", "reason": "quits the game"}], "texts": []}]}
        b = self.serve({("GET", "/ui"): ui})
        _, out, _ = run_cli("ui", port=b.port)
        self.assertIn("[-1] 开始游戏", out)
        self.assertIn("blocked: 退出游戏 (background/TextList/ExitGame) — quits the game", out)

    def test_blocked_click_is_refused_with_the_reason(self):
        b = self.serve({("POST", "/click"): {"ok": False, "error": "blocked", "message": "quits the game", "target": {"id": 5, "label": "退出游戏", "path": "x"}}})
        code, _, err = run_cli("click", "退出游戏", port=b.port)
        self.assertEqual(code, 1)
        self.assertIn("blocked: quits the game", err)


class Recon(unittest.TestCase):
    """`witchctl recon`: one JSON file per distinct screen (phase + open windows), written once."""

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        state = {"phase": "unknown", "frame": 5, "scene": "game", "windows": ["ChatUI", "TopBarUI", "ShopUI"], "actions": []}
        self.bridge = FakeBridge({("GET", "/state"): state, ("GET", "/ui"): {"windows": [{"name": "ShopUI", "clickables": [], "texts": []}]},
                                  ("GET", "/world"): {"window": "ShopUI", "count": 1, "objects": [{"path": "ShopUI/Content/Refresh", "onClick": ["ShopUI.Flushed"]}]}})
        self.addCleanup(self.bridge.close)

    def recon(self, *extra):
        return run_cli("recon", "--dir", self.tmp.name, *extra, port=self.bridge.port)

    def test_writes_state_ui_and_the_world_of_each_open_window(self):
        code, out, _ = self.recon()
        self.assertEqual(code, 0)
        files = os.listdir(self.tmp.name)
        self.assertEqual(files, ["unknown__ShopUI.json"])   # ambient windows (chat, top bar) are not part of the name
        with open(os.path.join(self.tmp.name, files[0]), encoding="utf-8") as f:
            data = json.load(f)
        self.assertEqual(set(data), {"time", "state", "ui", "worlds"})
        self.assertEqual(list(data["worlds"]), ["ShopUI"])
        self.assertEqual(data["worlds"]["ShopUI"]["objects"][0]["onClick"], ["ShopUI.Flushed"])
        self.assertIn("window=ShopUI", [r[1] for r in self.bridge.requests if r[1].startswith("/world")][0])
        self.assertIn("recorded", out)

    def test_world_scenes_dump_their_root_objects(self):
        hub = {"phase": "hub", "frame": 5, "scene": "game", "windows": ["ChatUI"], "actions": []}
        self.bridge.close()
        self.bridge = FakeBridge({("GET", "/state"): hub, ("GET", "/ui"): {"windows": []},
                                  ("GET", "/world"): {"root": "NewHouse", "count": 2, "objects": [{"path": "NewHouse/WorldRoot/StartGame", "handlers": ["SceneItem"]}]}})
        code, _, _ = self.recon()
        self.assertEqual(code, 0)
        with open(os.path.join(self.tmp.name, "hub.json"), encoding="utf-8") as f:
            data = json.load(f)
        self.assertEqual(list(data["worlds"]), ["root:NewHouse"])
        self.assertIn("root=NewHouse", [r[1] for r in self.bridge.requests if r[1].startswith("/world")][0])

    def test_same_screen_is_not_dumped_twice_unless_forced(self):
        self.recon()
        before = len(self.bridge.requests)
        code, out, _ = self.recon()
        self.assertEqual(code, 0)
        self.assertIn("already recorded", out)
        self.assertEqual(len(self.bridge.requests), before + 1)   # only the /state read that names the screen
        self.recon("--force")
        self.assertGreater(len(self.bridge.requests), before + 1)


class McpServer(unittest.TestCase):
    """`witchctl mcp`: newline-delimited JSON-RPC on stdio, the CLI's commands as tools."""

    def setUp(self):
        self.bridge = FakeBridge({("GET", "/state"): BATTLE_STATE,
                                  ("POST", "/act"): lambda body: {"Success": body.get("ActionId") == "battle_end_turn", "Message": "ok"}})
        self.addCleanup(self.bridge.close)
        self.p = subprocess.Popen([sys.executable, "-I", str(CLI), "mcp", "--port", str(self.bridge.port)],
                                  stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)

    def tearDown(self):
        self.p.kill()
        self.p.communicate()
        self.p.stderr.close()

    def send(self, msg):
        self.p.stdin.write(json.dumps(msg) + "\n")
        self.p.stdin.flush()
        return json.loads(self.p.stdout.readline())

    def test_initialize_lists_the_tools(self):
        r = self.send({"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {"protocolVersion": "2024-11-05"}})
        self.assertEqual(r["result"]["serverInfo"]["name"], "witchctl")
        r = self.send({"jsonrpc": "2.0", "id": 2, "method": "tools/list"})
        self.assertEqual({t["name"] for t in r["result"]["tools"]}, {"state", "act", "play", "ui", "click", "wait", "raw"})

    def test_state_returns_the_compact_text(self):
        r = self.send({"jsonrpc": "2.0", "id": 1, "method": "tools/call", "params": {"name": "state", "arguments": {}}})
        self.assertFalse(r["result"]["isError"])
        self.assertIn("phase: battle", r["result"]["content"][0]["text"])

    def test_refused_action_is_an_error_result_not_a_dead_server(self):
        r = self.send({"jsonrpc": "2.0", "id": 1, "method": "tools/call", "params": {"name": "act", "arguments": {"action_id": "nope"}}})
        self.assertTrue(r["result"]["isError"])
        self.assertIn("refused", r["result"]["content"][0]["text"])

    def test_unknown_tool_is_an_error_and_notifications_stay_silent(self):
        self.assertIn("error", self.send({"jsonrpc": "2.0", "id": 1, "method": "tools/call", "params": {"name": "nope"}}))
        self.p.stdin.write(json.dumps({"jsonrpc": "2.0", "method": "notifications/initialized"}) + "\n")
        self.p.stdin.flush()
        self.assertEqual(self.send({"jsonrpc": "2.0", "id": 2, "method": "ping"})["result"], {})


if __name__ == "__main__":
    unittest.main()
