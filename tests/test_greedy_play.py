"""examples/greedy.py, the play policy: blocking when the next hit would hurt, risk-taking at events and on the route, card quality."""
import importlib.util
import pathlib
import unittest

GREEDY = pathlib.Path(__file__).resolve().parent.parent / "examples" / "greedy.py"
spec = importlib.util.spec_from_file_location("greedy", GREEDY)
greedy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(greedy)


def card(name, cost, text, kind="技能牌", playable=True):
    return {"uid": f"u-{name}", "name": name, "cost": cost, "text": text, "type": kind, "playable": playable}


def foe(index, hp, intents=(), alive=True, defend=0):
    return {"index": index, "alive": alive, "unit": {"name": f"e{index}", "hp": hp, "maxHp": hp, "defend": defend},
            "intents": [{"name": n, "text": t, "cooldown": c} for n, t, c in intents]}


def fight(hand, enemies, hp=100, max_hp=100, defend=0, power=3):
    return {"player": {"unit": {"hp": hp, "maxHp": max_hp, "defend": defend}, "powerCount": power}, "hand": hand, "enemies": enemies}


STRIKE = card("打击", 1, "造成6点伤害。", "攻击牌")
BIG = card("重击", 2, "造成20点伤害。", "攻击牌")
GUARD = card("防御", 1, "获得8点护盾。")
WALL = card("铁壁", 2, "获得20点护盾。")
DRAW = card("冥想", 0, "抽2张卡牌,\n焚毁", "消耗技能牌")
BUFF = card("蓄力", 1, "获得2层力量。")
HEAL = card("包扎", 1, "恢复15点生命。")
HIT8 = ("重拳", "造成8点伤害。", 0)


class CardEffects(unittest.TestCase):
    def test_numbers_come_from_the_text(self):
        self.assertEqual(greedy.card_effects(STRIKE), {"damage": 6, "block": 0, "heal": 0, "draw": 0})
        self.assertEqual(greedy.card_effects(WALL)["block"], 20)
        self.assertEqual(greedy.card_effects(HEAL)["heal"], 15)
        self.assertEqual(greedy.card_effects(DRAW)["draw"], 2)

    def test_a_buff_card_has_no_numbers(self):
        self.assertEqual(greedy.card_effects(BUFF), {"damage": 0, "block": 0, "heal": 0, "draw": 0})

    def test_missing_text(self):
        self.assertEqual(greedy.card_effects({"name": "?"}), {"damage": 0, "block": 0, "heal": 0, "draw": 0})


class IncomingDamage(unittest.TestCase):
    def test_flat_and_multi_hit_intents(self):
        self.assertEqual(greedy.incoming_damage([foe(0, 50, [HIT8])]), 8)
        self.assertEqual(greedy.incoming_damage([foe(0, 50, [("破灭", "造成2*4伤害。", 1)])]), 8)

    def test_an_enemy_uses_one_move_the_worst_one(self):
        self.assertEqual(greedy.incoming_damage([foe(0, 50, [HIT8, ("重击", "造成15点伤害。", 1)])]), 15)

    def test_moves_still_cooling_down_and_the_dead_do_not_count(self):
        self.assertEqual(greedy.incoming_damage([foe(0, 50, [("大招", "造成99点伤害。", 3)])]), 0)
        self.assertEqual(greedy.incoming_damage([foe(0, 0, [HIT8], alive=False)]), 0)

    def test_enemies_add_up_and_non_damage_intents_are_zero(self):
        enemies = [foe(0, 50, [HIT8]), foe(1, 50, [HIT8]), foe(2, 50, [("窃取", "偷窃对方10点金币。", 0)])]
        self.assertEqual(greedy.incoming_damage(enemies), 16)


class PickCard(unittest.TestCase):
    def test_nothing_playable_ends_the_turn(self):
        self.assertIsNone(greedy.pick_card(fight([card("x", 5, "造成1点伤害。", "攻击牌", playable=False)], [foe(0, 20)])))
        self.assertIsNone(greedy.pick_card(fight([STRIKE], [])))

    def test_safe_turn_attacks_with_the_best_damage_per_energy(self):
        pick = greedy.pick_card(fight([GUARD, STRIKE, BIG], [foe(0, 80, [HIT8])]))
        self.assertEqual(pick, ("u-重击", 0, "重击"))   # 20 for 2 beats 6 for 1

    def test_attacks_the_weakest_enemy(self):
        pick = greedy.pick_card(fight([STRIKE], [foe(0, 80), foe(1, 30)]))
        self.assertEqual(pick[1], 1)

    def test_blocks_first_when_the_hit_would_leave_us_low(self):
        enemies = [foe(0, 200, [("碾压", "造成45点伤害。", 0)])]
        pick = greedy.pick_card(fight([STRIKE, GUARD, WALL], enemies, hp=60, max_hp=100))
        self.assertEqual(pick[2], "铁壁")   # the biggest block per energy

    def test_keeps_blocking_until_the_hit_is_covered_then_attacks(self):
        enemies = [foe(0, 200, [("碾压", "造成45点伤害。", 0)])]
        hand = [STRIKE, WALL]
        self.assertEqual(greedy.pick_card(fight(hand, enemies, hp=60, defend=0))[2], "铁壁")
        self.assertEqual(greedy.pick_card(fight(hand, enemies, hp=60, defend=45))[2], "打击")

    def test_a_hit_we_can_afford_is_not_blocked(self):
        enemies = [foe(0, 200, [("轻拍", "造成10点伤害。", 0)])]
        self.assertEqual(greedy.pick_card(fight([GUARD, STRIKE], enemies, hp=100, max_hp=100))[2], "打击")

    def test_a_kill_beats_blocking(self):
        enemies = [foe(0, 5, [("碾压", "造成90点伤害。", 0)])]
        self.assertEqual(greedy.pick_card(fight([GUARD, STRIKE], enemies, hp=50, max_hp=100))[2], "打击")

    def test_the_last_turn_cost_sizes_the_block_when_intents_say_nothing(self):
        enemies = [foe(0, 200, [("蓄势", "正在进行蓄力。", 0)])]
        hand = [STRIKE, WALL]
        self.assertEqual(greedy.pick_card(fight(hand, enemies, hp=60), expected_loss=50)[2], "铁壁")
        self.assertEqual(greedy.pick_card(fight(hand, enemies, hp=60), expected_loss=0)[2], "打击")

    def test_cheap_draw_comes_first_on_a_safe_turn(self):
        self.assertEqual(greedy.pick_card(fight([STRIKE, DRAW], [foe(0, 80)]))[2], "冥想")

    def test_buffs_before_attacks_when_safe(self):
        self.assertEqual(greedy.pick_card(fight([STRIKE, BUFF], [foe(0, 80)]))[2], "蓄力")

    def test_heals_when_low(self):
        self.assertEqual(greedy.pick_card(fight([STRIKE, HEAL], [foe(0, 80)], hp=30, max_hp=100))[2], "包扎")
        self.assertEqual(greedy.pick_card(fight([STRIKE, HEAL], [foe(0, 80)], hp=90, max_hp=100))[2], "打击")

    def test_spare_energy_still_banks_block(self):
        self.assertEqual(greedy.pick_card(fight([GUARD], [foe(0, 80)]))[2], "防御")   # nothing better to do with the energy


class TurnMemory(unittest.TestCase):
    def test_hp_lost_plus_the_block_that_was_up_is_the_estimate(self):
        mem = greedy.TurnMemory()
        mem.ended({"hp": 100, "defend": 10})
        self.assertEqual(mem.expected_loss({"hp": 70, "defend": 0}), 40)   # 30 hp went through a block of 10

    def test_a_fully_absorbed_turn_keeps_a_decayed_memory(self):
        mem = greedy.TurnMemory()
        mem.ended({"hp": 100, "defend": 0})
        mem.expected_loss({"hp": 60, "defend": 0})   # 40
        mem.ended({"hp": 100, "defend": 40})
        self.assertEqual(mem.expected_loss({"hp": 100, "defend": 0}), 28)   # 0.7 * 40: not forgotten at once

    def test_no_history_before_the_first_turn_ends(self):
        self.assertEqual(greedy.TurnMemory().expected_loss({"hp": 50, "defend": 0}), 0)

    def test_estimate_is_read_once_per_turn(self):
        mem = greedy.TurnMemory()
        mem.ended({"hp": 100, "defend": 0})
        self.assertEqual(mem.expected_loss({"hp": 80, "defend": 0}), 20)
        self.assertEqual(mem.expected_loss({"hp": 60, "defend": 0}), 20)   # same turn, more plays: the estimate does not move


def option(index, label, effect, interactable=True):
    return {"index": index, "label": label, "effect": effect, "interactable": interactable}


def event_state(options, resolved=False):
    acts = [{"Id": f"event_option_{o['index']}", "Kind": "event_option", "Label": o["label"]} for o in options]
    return {"phase": "event", "event": {"options": options, "resolved": resolved}, "actions": acts + [{"Id": "deck_open", "Kind": "deck_open"}]}


class EventChoice(unittest.TestCase):
    def test_takes_the_relic_over_the_gold(self):
        st = event_state([option(0, "拿钱", "获得50金币"), option(1, "拿宝", "获得遗物\"迷途天使之环\"")])
        self.assertEqual(greedy.event_choice(st), "event_option_1")

    def test_a_gamble_with_a_prize_beats_a_sure_small_one(self):
        st = event_state([option(0, "稳妥", "获得30金币"), option(1, "赌一把", "有50%概率获得遗物，否则失去20点生命")])
        self.assertEqual(greedy.event_choice(st), "event_option_1")

    def test_a_curse_is_not_worth_a_bit_of_gold(self):
        st = event_state([option(0, "签约", "获得20金币，获得诅咒"), option(1, "无视", "获得10金币")])
        self.assertEqual(greedy.event_choice(st), "event_option_1")

    def test_leaving_empty_handed_is_the_last_resort(self):
        st = event_state([option(0, "离开", "无事发生"), option(1, "尝试", "获得一张卡牌")])
        self.assertEqual(greedy.event_choice(st), "event_option_1")

    def test_locked_options_are_skipped(self):
        st = event_state([option(0, "好东西", "获得遗物", interactable=False), option(1, "凑合", "获得10金币")])
        self.assertEqual(greedy.event_choice(st), "event_option_1")

    def test_options_without_an_effect_text_fall_back_to_the_first(self):
        st = event_state([option(0, "A", ""), option(1, "B", "")])
        self.assertEqual(greedy.event_choice(st), "event_option_0")

    def test_a_resolved_event_has_one_button_take_it(self):
        st = {"phase": "event", "event": {"options": [], "resolved": True}, "actions": [{"Id": "event_option_0", "Kind": "event_option", "Label": "结束事件"}]}
        self.assertEqual(greedy.event_choice(st), "event_option_0")

    def test_no_actions_no_choice(self):
        self.assertIsNone(greedy.event_choice({"phase": "event", "actions": []}))


def offer(index, name, rarity, text, cost="1", pickable=True):
    return {"index": index, "id": name, "name": name, "cost": cost, "rarity": rarity, "text": text, "pickable": pickable}


class CardQuality(unittest.TestCase):
    IDS = ["card_choice_pick_0", "card_choice_pick_1", "card_choice_pick_2", "card_choice_skip"]

    def test_without_card_details_it_is_the_first_one_as_before(self):
        self.assertEqual(greedy.card_choice(self.IDS), "card_choice_pick_0")

    def test_higher_rarity_wins(self):
        cards = [offer(0, "a", "1", "造成6点伤害。"), offer(1, "b", "3", "造成6点伤害。"), offer(2, "c", "2", "造成6点伤害。")]
        self.assertEqual(greedy.card_choice(self.IDS, cards), "card_choice_pick_1")

    def test_same_rarity_prefers_more_effect(self):
        cards = [offer(0, "a", "2", "造成6点伤害。"), offer(1, "b", "2", "造成14点伤害。"), offer(2, "c", "2", "获得5点护盾。")]
        self.assertEqual(greedy.card_choice(self.IDS, cards), "card_choice_pick_1")

    def test_cards_the_game_will_refuse_are_not_picked(self):
        cards = [offer(0, "a", "3", "x", pickable=False), offer(1, "b", "1", "y"), offer(2, "c", "1", "z")]
        self.assertEqual(greedy.card_choice(self.IDS, cards), "card_choice_pick_1")

    def test_skip_when_only_skip_is_offered(self):
        self.assertEqual(greedy.card_choice(["card_choice_skip"], [offer(0, "a", "3", "x")]), "card_choice_skip")


class PreferredCards(unittest.TestCase):
    IDS = ["card_choice_pick_0", "card_choice_pick_1", "card_choice_pick_2", "card_choice_skip"]

    def test_a_preferred_card_beats_a_rarer_one(self):
        cards = [offer(0, "稀有牌", "3", "造成30点伤害。"), offer(1, "复仇短匕", "2", "对敌方造成自身最大生命值10%的伤害。"), offer(2, "普通牌", "1", "x")]
        self.assertEqual(greedy.card_choice(self.IDS, cards, prefer=("复仇短匕",)), "card_choice_pick_1")

    def test_the_earlier_name_in_the_list_wins(self):
        cards = [offer(0, "绯咏弦奏", "2", "a"), offer(1, "复仇短匕", "2", "b")]
        self.assertEqual(greedy.card_choice(self.IDS, cards, prefer=("复仇短匕", "绯咏弦奏")), "card_choice_pick_1")

    def test_nothing_preferred_falls_back_to_rarity(self):
        cards = [offer(0, "a", "1", "x"), offer(1, "b", "3", "y")]
        self.assertEqual(greedy.card_choice(self.IDS, cards, prefer=("复仇短匕",)), "card_choice_pick_1")

    def test_an_unpickable_preferred_card_is_not_chosen(self):
        cards = [offer(0, "复仇短匕", "2", "x", pickable=False), offer(1, "b", "1", "y")]
        self.assertEqual(greedy.card_choice(self.IDS, cards, prefer=("复仇短匕",)), "card_choice_pick_1")


def tray(*entries):
    return [{"index": i, "kind": k, "id": cid, "label": label} for i, (k, cid, label) in enumerate(entries)]


def route_state(entries, empty):
    nodes = [{"index": i, "empty": i < empty} for i in range(4)]
    return {"mapChoices": tray(*entries), "mapNodes": nodes}


class BoldRoute(unittest.TestCase):
    FULL = (("Fight", "level_1", "史莱姆 5 40"), ("Fight", "level_2", "石像鬼 12 150 精英"), ("Build", "shop", "商店"),
            ("Event", "event_3", "魔女手记"), ("Fight", "level_4", "蝙蝠 4 30"), ("Build", "Breaks", "一息安隅"))

    def test_first_frame_takes_the_hardest_fight(self):
        self.assertEqual(greedy.route_pick(route_state(self.FULL, 4), "bold"), "map_place_1")

    def test_second_frame_the_next_hardest(self):
        entries = tuple(e for e in self.FULL if e[1] != "level_2")
        self.assertEqual(greedy.route_pick(route_state(entries, 3), "bold"), "map_place_0")

    def test_third_frame_is_the_shop(self):
        entries = tuple(e for e in self.FULL if e[1] not in ("level_2", "level_1"))
        self.assertEqual(greedy.route_pick(route_state(entries, 2), "bold"), "map_place_0")

    def test_last_frame_is_the_rest_room(self):
        entries = (("Event", "event_3", "魔女手记"), ("Build", "Breaks", "一息安隅"))
        self.assertEqual(greedy.route_pick(route_state(entries, 1), "bold"), "map_place_1")

    def test_falls_back_to_what_is_there(self):
        entries = (("Event", "event_3", "魔女手记"), ("Build", "Breaks", "一息安隅"))
        self.assertEqual(greedy.route_pick(route_state(entries, 4), "bold"), "map_place_0")   # no fight, no shop: an event, never the rest room early

    def test_no_map_nodes_no_opinion(self):
        self.assertIsNone(greedy.route_pick({"mapChoices": tray(*self.FULL)}, "bold"))


class SlotFarm(unittest.TestCase):
    """--mode slot: Chaos Paradise, left at once (the game's own "leave early" button pays the score of the run, which the difficulty tags
    multiply), instead of playing a classic run."""

    def test_the_mode_screen_gets_the_requested_mode(self):
        ids = ["mode_choice_story", "mode_choice_classic", "mode_choice_slot", "mode_choice_back"]
        self.assertEqual(greedy.mode_pick(ids, "slot"), "mode_choice_slot")
        self.assertEqual(greedy.mode_pick(ids, "classic"), "mode_choice_classic")

    def test_slot_is_never_picked_when_classic_is_asked_for_and_missing_falls_back(self):
        self.assertEqual(greedy.mode_pick(["mode_choice_slot", "mode_choice_back"], "classic"), "mode_choice_slot")   # first offered, as before
        self.assertEqual(greedy.mode_pick(["mode_choice_classic"], "slot"), "mode_choice_classic")                    # level < 25: no slot button
        self.assertEqual(greedy.mode_pick([], "slot"), "mode_choice_back")

    def test_leaving_confirmation_is_answered_only_in_slot_mode(self):
        acts = [action("modal_confirm_end_run", "是"), action("modal_cancel", "否")]
        self.assertEqual(greedy.modal_choice(acts, allow_end_run=True), "modal_confirm_end_run")
        self.assertEqual(greedy.modal_choice(acts), "modal_cancel")   # a classic run never gives itself up

    def test_other_questions_are_still_declined_in_slot_mode(self):
        acts = [action("modal_confirm", "退出游戏"), action("modal_cancel", "取消")]
        self.assertEqual(greedy.modal_choice(acts, allow_end_run=True), "modal_cancel")

    def test_the_slot_screen_leaves_through_its_one_action(self):
        st = {"phase": "slot", "actions": [{"Id": "slot_end_run", "Kind": "slot_end_run"}]}
        self.assertEqual(greedy.slot_action(st), "slot_end_run")
        self.assertIsNone(greedy.slot_action({"phase": "slot", "actions": []}))


def action(action_id, label):
    return {"Id": action_id, "Kind": action_id, "Label": label}


class EntryPrefs(unittest.TestCase):
    """Before a run: the partner and the card packs that make it safer (a partner that revives at every battle start, the pack that stacks rebirth)."""

    ACTS = [action("game_entry_start", "开始冒险"), action("entry_prepare_open", "出发前准备"), action("entry_career_1", "奈奈"),
            action("entry_partner_1", "匣上黑猫"), action("entry_partner_2", "噩梦原型"), action("entry_main_Lucky", "Lucky"),
            action("entry_pack_on_cardpack_7", "蛇自阴影中袭来")]

    def test_partner_first_then_the_pack(self):
        self.assertEqual(greedy.entry_pref({"actions": self.ACTS}, "匣上黑猫", ("蛇自阴影中袭来",)), "entry_partner_1")
        rest = [a for a in self.ACTS if a["Id"] != "entry_partner_1"]   # the chosen partner is no longer offered
        self.assertEqual(greedy.entry_pref({"actions": rest}, "匣上黑猫", ("蛇自阴影中袭来",)), "entry_pack_on_cardpack_7")

    def test_nothing_left_to_do(self):
        rest = [a for a in self.ACTS if a["Id"] not in ("entry_partner_1", "entry_pack_on_cardpack_7")]
        self.assertIsNone(greedy.entry_pref({"actions": rest}, "匣上黑猫", ("蛇自阴影中袭来",)))

    def test_an_unlocked_partner_that_is_missing_is_skipped(self):
        rest = [a for a in self.ACTS if a["Id"] != "entry_partner_1"]
        self.assertIsNone(greedy.entry_pref({"actions": rest}, "不存在的搭档", ()))

    def test_only_partner_and_pack_buttons_are_ever_pressed(self):
        acts = [action("entry_career_1", "匣上黑猫"), action("game_entry_start", "匣上黑猫")]   # a label match on a career or the start button is not a partner
        self.assertIsNone(greedy.entry_pref({"actions": acts}, "匣上黑猫", ("匣上黑猫",)))

    def test_no_preferences_no_action(self):
        self.assertIsNone(greedy.entry_pref({"actions": self.ACTS}, None, ()))


class FightStartLine(unittest.TestCase):
    def test_lists_the_enemies_with_their_moves_and_our_health(self):
        battle = fight([STRIKE], [foe(0, 4452, [("斩灭", "造成30*4伤害。", 0), ("蓄势", "正在进行蓄力。", 1)])], hp=412, max_hp=460)
        line = greedy.fight_start_line(battle)
        self.assertIn("hp 412/460", line)
        self.assertIn("e0 4452", line)
        self.assertIn("斩灭 造成30*4伤害。(0)", line)
        self.assertIn("incoming 120", line)


def skill(index, sid, name, usable=True, cooldown=0, aimed=True):
    return {"index": index, "id": sid, "name": name, "usable": usable, "cooldown": cooldown, "aimed": aimed}


class EatPlan(unittest.TestCase):
    """奈奈: the hand-made card 厄运盛宴 hangs every negative buff on her; 可食用厄运 eats them for doom power (max HP). The card is only ever played while the
    skill is ready, and the skill is used on herself the moment the buffs are on."""

    SKILLS = [skill(0, "careercard_2", "可食用厄运"), skill(1, "careercard_3", "灾祸具现化", aimed=False)]
    ACTS = [action("skill_use_0_self", "可食用厄运"), action("skill_use_0_enemy_0", "可食用厄运"), action("skill_use_1", "灾祸具现化")]
    FEAST = card("厄运盛宴", 0, "使用后:对自己，添加999层的毒素。")

    def battle(self, hand, buffs=(), skills=None):
        b = fight(hand, [foe(0, 500)], hp=200, max_hp=200)
        b["skills"] = self.SKILLS if skills is None else skills
        b["player"]["unit"]["buffs"] = list(buffs)
        return b

    def test_plays_the_card_when_the_skill_is_ready(self):
        self.assertEqual(greedy.eat_plan(self.battle([STRIKE, self.FEAST]), self.ACTS), ("card", "u-厄运盛宴", "厄运盛宴"))

    def test_eats_at_once_when_the_buffs_are_on(self):
        loaded = [{"name": "毒素", "level": 999}, {"name": "流血", "level": 999}]
        self.assertEqual(greedy.eat_plan(self.battle([STRIKE, self.FEAST], loaded), self.ACTS), ("skill", "skill_use_0_self"))

    def test_never_plays_the_card_while_the_skill_is_cooling_down(self):
        cooling = [skill(0, "careercard_2", "可食用厄运", usable=False, cooldown=3), self.SKILLS[1]]
        self.assertIsNone(greedy.eat_plan(self.battle([self.FEAST], skills=cooling), self.ACTS))

    def test_never_plays_the_card_without_the_self_action(self):
        enemy_only = [a for a in self.ACTS if not a["Id"].endswith("_self")]
        self.assertIsNone(greedy.eat_plan(self.battle([self.FEAST]), enemy_only))

    def test_no_card_in_hand_no_plan(self):
        self.assertIsNone(greedy.eat_plan(self.battle([STRIKE]), self.ACTS))

    def test_another_career_has_no_such_skill(self):
        self.assertIsNone(greedy.eat_plan(self.battle([self.FEAST], skills=[skill(0, "careercard_1", "神选", aimed=False)]), self.ACTS))

    def test_the_eater_is_never_chosen_by_the_generic_skill_rule(self):
        acts = [dict(action("skill_use_0_enemy_0", "可食用厄运"), Kind="skill_use", Parameters={"skillId": "careercard_2", "aimed": True})]
        self.assertIsNone(greedy.skill_choice(acts, [foe(0, 50)], set()))

    def test_the_transformation_is_used_like_any_ready_skill(self):
        acts = [dict(action("skill_use_1", "灾祸具现化"), Kind="skill_use", Parameters={"skillId": "careercard_3", "aimed": False})]
        self.assertEqual(greedy.skill_choice(acts, [foe(0, 50)], set()), "skill_use_1")


class HpScalingCards(unittest.TestCase):
    AVENGER = card("复仇短匕", 1, "对敌方造成自身最大生命值10%的伤害。", "攻击牌")
    LANCE = card("复仇链枪", 2, "对随机目标造成自身已损失生命50%的伤害。", "攻击牌")

    def test_damage_from_max_hp_and_lost_hp(self):
        unit = {"hp": 4000, "maxHp": 14000}
        self.assertEqual(greedy.card_effects(self.AVENGER, unit)["damage"], 1400)
        self.assertEqual(greedy.card_effects(self.LANCE, unit)["damage"], 5000)

    def test_without_the_player_they_have_no_number(self):
        self.assertEqual(greedy.card_effects(self.AVENGER)["damage"], 0)

    def test_a_big_health_pool_makes_the_dagger_the_best_attack(self):
        b = fight([STRIKE, BIG, self.AVENGER], [foe(0, 5000)], hp=14000, max_hp=14000)
        self.assertEqual(greedy.pick_card(b)[2], "复仇短匕")

    def test_the_lance_waits_for_the_health_to_be_gone(self):
        full = fight([STRIKE, self.LANCE], [foe(0, 5000)], hp=14000, max_hp=14000)
        self.assertEqual(greedy.pick_card(full)[2], "打击")     # nothing lost: 0 damage for 2 energy
        hurt = fight([STRIKE, self.LANCE], [foe(0, 5000)], hp=3000, max_hp=14000)
        self.assertEqual(greedy.pick_card(hurt)[2], "复仇链枪")   # 5500 damage


class FeastIsNeverPlayedAsAnOrdinaryCard(unittest.TestCase):
    """The run that died: 厄运盛宴 has no numbers in its text, so it scored as a free buff and was played while 可食用厄运 cooled down (999 layers of poison,
    bleeding, ... with nothing to eat them). Only `eat_plan` may play it."""

    FEAST = card("厄运盛宴", 0, "使用后:对自己，添加999层的毒素。")

    def test_it_is_never_the_pick(self):
        self.assertEqual(greedy.pick_card(fight([STRIKE, self.FEAST], [foe(0, 80)])), ("u-打击", 0, "打击"))

    def test_alone_in_hand_the_turn_just_ends(self):
        self.assertIsNone(greedy.pick_card(fight([self.FEAST], [foe(0, 80)])))

    def test_not_even_when_the_hit_is_coming(self):
        enemies = [foe(0, 200, [("碾压", "造成45点伤害。", 0)])]
        self.assertEqual(greedy.pick_card(fight([self.FEAST, GUARD], enemies, hp=60, max_hp=100))[2], "防御")


class PauseAt(unittest.TestCase):
    def test_pauses_on_a_listed_phase(self):
        self.assertTrue(greedy.should_pause("rest", ("rest", "shop")))
        self.assertFalse(greedy.should_pause("battle", ("rest", "shop")))
        self.assertFalse(greedy.should_pause("rest", ()))

    def test_a_window_name_pauses_too(self):
        self.assertTrue(greedy.should_pause("unknown", ("SafeBoxUI",), windows=["ChatUI", "SafeBoxUI"]))
        self.assertFalse(greedy.should_pause("unknown", ("SafeBoxUI",), windows=["ChatUI"]))


class LeanDeck(unittest.TestCase):
    """奈奈's engine needs one particular card in hand: a deck stuffed to 43 cards draws it a third as often (doom power stalled at 335 instead of 1200)."""

    def st(self, in_deck, backpack=3, maximum=23):
        return {"actions": [action("deck_open", "牌组"), action("deck_equip_0", "x")], "run": {"inDeck": in_deck, "backpack": backpack, "maximum": maximum}}

    def test_no_cap_fills_the_deck_as_before(self):
        self.assertTrue(greedy.wants_deck(self.st(18)))
        self.assertFalse(greedy.wants_deck(self.st(23)))

    def test_a_cap_stops_equipping_below_the_game_maximum(self):
        self.assertTrue(greedy.wants_deck(self.st(15), cap=18))
        self.assertFalse(greedy.wants_deck(self.st(18), cap=18))

    def test_in_the_window_it_closes_once_the_cap_is_reached(self):
        self.assertEqual(greedy.deck_choice(self.st(17), cap=18), "deck_equip_0")
        self.assertEqual(greedy.deck_choice(self.st(18), cap=18), "deck_close")
        self.assertEqual(greedy.deck_choice(self.st(40)), "deck_equip_0")   # no cap: as before


class HoldTheTransformation(unittest.TestCase):
    """The transformation ends a fight in a turn, so a fight is over before the feast card is drawn and the doom power (max HP) stops growing (7 eats in 11 fights,
    4k HP at the boss instead of 16k). Hold it until the eat is done, the second turn is over, or health is in danger."""

    def battle(self, hp=1000, max_hp=1000, eat_usable=True, eat_cooldown=0):
        b = fight([STRIKE], [foe(0, 500)], hp=hp, max_hp=max_hp)
        b["skills"] = [skill(0, "careercard_2", "可食用厄运", usable=eat_usable, cooldown=eat_cooldown), skill(1, "careercard_3", "灾祸具现化", aimed=False)]
        return b

    def test_waits_while_the_eat_is_still_to_come(self):
        self.assertTrue(greedy.hold_transform(self.battle(), turn=0))
        self.assertTrue(greedy.hold_transform(self.battle(), turn=1))

    def test_goes_ahead_on_the_third_turn(self):
        self.assertFalse(greedy.hold_transform(self.battle(), turn=2))

    def test_goes_ahead_once_the_eat_is_done(self):
        self.assertFalse(greedy.hold_transform(self.battle(eat_usable=False, eat_cooldown=5), turn=0))

    def test_goes_ahead_when_health_is_in_danger(self):
        self.assertFalse(greedy.hold_transform(self.battle(hp=300), turn=0))

    def test_another_career_has_nothing_to_wait_for(self):
        b = fight([STRIKE], [foe(0, 500)])
        b["skills"] = [skill(0, "careercard_1", "神选", aimed=False)]
        self.assertFalse(greedy.hold_transform(b, turn=0))

    def test_the_skill_rule_skips_a_held_skill(self):
        acts = [dict(action("skill_use_1", "灾祸具现化"), Kind="skill_use", Parameters={"skillId": "careercard_3", "aimed": False})]
        self.assertIsNone(greedy.skill_choice(acts, [foe(0, 50)], set(), hold=("careercard_3",)))
        self.assertEqual(greedy.skill_choice(acts, [foe(0, 50)], set()), "skill_use_1")

    def test_turns_are_counted_per_fight(self):
        mem = greedy.TurnMemory()
        self.assertEqual(mem.turn, 0)
        mem.ended({"hp": 10, "defend": 0})
        mem.ended({"hp": 10, "defend": 0})
        self.assertEqual(mem.turn, 2)


class EntryCareer(unittest.TestCase):
    def test_career_comes_before_partner_and_packs(self):
        acts = [action("entry_career_2", "奈奈"), action("entry_partner_1", "匣上黑猫"), action("entry_pack_on_cardpack_7", "蛇自阴影中袭来")]
        self.assertEqual(greedy.entry_pref({"actions": acts}, "匣上黑猫", ("蛇自阴影中袭来",), career="奈奈"), "entry_career_2")
        rest = acts[1:]   # the chosen career is no longer offered
        self.assertEqual(greedy.entry_pref({"actions": rest}, "匣上黑猫", ("蛇自阴影中袭来",), career="奈奈"), "entry_partner_1")


class RestFirstRoute(unittest.TestCase):
    FULL = (("Fight", "level_1", "史莱姆 5 40"), ("Fight", "level_2", "石像鬼 12 150"), ("Build", "shop", "商店"), ("Build", "Breaks", "一息安隅"))

    def test_the_rest_room_goes_into_the_first_frame(self):
        self.assertEqual(greedy.route_pick(route_state(self.FULL, 4), "rest-first"), "map_place_3")

    def test_after_that_the_hardest_fights_then_the_shop(self):
        entries = tuple(e for e in self.FULL if e[1] != "Breaks")
        self.assertEqual(greedy.route_pick(route_state(entries, 3), "rest-first"), "map_place_1")   # hardest fight first

    def test_without_a_rest_room_it_falls_back_to_the_bold_pick(self):
        entries = tuple(e for e in self.FULL if e[1] != "Breaks")
        self.assertEqual(greedy.route_pick(route_state(entries, 4), "rest-first"), greedy.route_pick(route_state(entries, 4), "bold"))


class BuffLine(unittest.TestCase):
    def test_lists_buffs_with_levels(self):
        unit = {"buffs": [{"name": "重生", "level": 70}, {"name": "元素", "level": 3}]}
        self.assertEqual(greedy.buff_text(unit), "重生×70 元素×3")
        self.assertEqual(greedy.buff_text({}), "-")

    def test_fight_start_shows_our_buffs(self):
        battle = fight([STRIKE], [foe(0, 100)], hp=300, max_hp=300)
        battle["player"]["unit"]["buffs"] = [{"name": "重生", "level": 70}]
        self.assertIn("buffs 重生×70", greedy.fight_start_line(battle))

    def test_a_revival_is_a_death_and_a_rebirth_buff_that_was_spent(self):
        mem = greedy.TurnMemory()
        mem.ended({"hp": 40, "defend": 0, "buffs": [{"name": "重生", "level": 70}]})
        self.assertTrue(mem.revived({"hp": 217, "maxHp": 300, "buffs": []}))                              # 70 layers in, none left, hp back up
        mem.ended({"hp": 40, "defend": 0, "buffs": [{"name": "重生", "level": 170}]})
        self.assertTrue(mem.revived({"hp": 300, "maxHp": 300, "buffs": [{"name": "重生", "level": 70}]}))  # 100 layers spent

    def test_more_max_hp_is_not_a_revival(self):
        mem = greedy.TurnMemory()
        mem.ended({"hp": 40, "defend": 0, "buffs": [{"name": "重生", "level": 70}]})
        self.assertFalse(mem.revived({"hp": 629, "maxHp": 822, "buffs": [{"name": "重生", "level": 70}]}))   # healed by a max HP bonus, the buff is untouched

    def test_nothing_recorded_nothing_revived(self):
        self.assertFalse(greedy.TurnMemory().revived({"hp": 217, "maxHp": 300, "buffs": []}))


class RunEndLine(unittest.TestCase):
    def test_nothing_until_the_breakdown_is_shown(self):
        self.assertIsNone(greedy.run_end_line({"runEnd": {"won": False, "score": {}, "total": 0, "detailsShown": False}}))
        self.assertIsNone(greedy.run_end_line({}))

    def test_a_lost_run_with_its_breakdown(self):
        st = {"runEnd": {"won": False, "score": {"遗物": 9800, "金币": 280, "属性": 1040}, "total": 11120, "detailsShown": True}}
        self.assertEqual(greedy.run_end_line(st), "run end: lost, total 11120 (遗物 9800, 金币 280, 属性 1040)")

    def test_a_won_run(self):
        st = {"runEnd": {"won": True, "score": {"关卡": 500}, "total": 500, "detailsShown": True}}
        self.assertTrue(greedy.run_end_line(st).startswith("run end: WON, total 500"))


if __name__ == "__main__":
    unittest.main()
