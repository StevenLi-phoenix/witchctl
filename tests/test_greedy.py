import importlib.util
import pathlib
import time
import unittest

GREEDY = pathlib.Path(__file__).resolve().parent.parent / "examples" / "greedy.py"
spec = importlib.util.spec_from_file_location("greedy", GREEDY)
greedy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(greedy)


def action(action_id, label):
    return {"Id": action_id, "Kind": action_id, "Label": label}


class ModalChoice(unittest.TestCase):
    def test_saved_run_is_resumed_not_deleted(self):
        # the real window: confirm = "Continue Game", cancel = "New Game" (which deletes the save)
        acts = [action("modal_confirm", "继续游戏"), action("modal_cancel", "新游戏")]
        self.assertEqual(greedy.modal_choice(acts), "modal_confirm")

    def test_english_labels(self):
        acts = [action("modal_confirm", "Continue Game"), action("modal_cancel", "New Game")]
        self.assertEqual(greedy.modal_choice(acts), "modal_confirm")

    def test_unknown_question_is_declined(self):
        acts = [action("modal_confirm", "退出游戏"), action("modal_cancel", "取消")]
        self.assertEqual(greedy.modal_choice(acts), "modal_cancel")

    def test_confirm_only_unknown_question_is_left_alone(self):
        self.assertIsNone(greedy.modal_choice([action("modal_confirm", "删除存档")]))

    def test_no_buttons_yet(self):
        self.assertIsNone(greedy.modal_choice([]))   # the open animation is still running

    def test_save_window_with_the_irreversible_new_game_id(self):
        # the mod now names the effect on the dangerous button: the bot has never to ask for it
        acts = [action("modal_confirm", "继续游戏"), action("modal_cancel_new_game_deletes_save", "新游戏")]
        self.assertEqual(greedy.modal_choice(acts), "modal_confirm")

    def test_save_window_in_a_language_the_word_list_does_not_know(self):
        acts = [action("modal_confirm", "Fortsetzen"), action("modal_cancel_new_game_deletes_save", "Neues Spiel")]
        self.assertEqual(greedy.modal_choice(acts), "modal_confirm")   # the new-game id alone says which button is which

    def test_quit_window_is_declined_by_the_plain_cancel(self):
        acts = [action("modal_confirm_quit_game", "退出"), action("modal_cancel", "取消")]
        self.assertEqual(greedy.modal_choice(acts), "modal_cancel")

    def test_never_returns_an_irreversible_id(self):
        for acts in ([action("modal_confirm_return_to_menu", "确定")], [action("modal_confirm_end_run", "确定"), action("modal_cancel_new_game_deletes_save", "x")]):
            self.assertNotIn(greedy.modal_choice(acts), ("modal_confirm_return_to_menu", "modal_confirm_end_run", "modal_cancel_new_game_deletes_save"))


class DeckManagement(unittest.TestCase):
    def st(self, ids, backpack=2, in_deck=14, maximum=20):
        return {"phase": "map_select", "actions": [{"Id": i} for i in ids], "run": {"backpack": backpack, "inDeck": in_deck, "maximum": maximum}}

    def test_opens_the_deck_when_spare_cards_wait_and_there_is_room(self):
        self.assertTrue(greedy.wants_deck(self.st(["deck_open"])))

    def test_does_not_open_it_for_nothing(self):
        self.assertFalse(greedy.wants_deck(self.st(["deck_open"], backpack=0)))     # nothing spare
        self.assertFalse(greedy.wants_deck(self.st(["deck_open"], in_deck=20)))     # deck full: equip would be refused
        self.assertFalse(greedy.wants_deck(self.st([])))                            # the button is not there (menu, fight)
        self.assertFalse(greedy.wants_deck({"actions": [{"Id": "deck_open"}]}))     # a state without the run summary

    def test_equips_until_nothing_can_be_equipped_then_closes(self):
        self.assertEqual(greedy.deck_choice(self.st(["deck_unequip_0", "deck_equip_3", "deck_equip_4", "deck_close"])), "deck_equip_3")
        self.assertEqual(greedy.deck_choice(self.st(["deck_unequip_0", "deck_destroy_0", "deck_close"])), "deck_close")

    def test_never_sells_or_destroys(self):
        for ids in (["deck_sell_1", "deck_destroy_1"], ["deck_close", "deck_sell_1"]):
            self.assertNotIn(greedy.deck_choice(self.st(ids)), ("deck_sell_1", "deck_destroy_1"))


class SkillChoice(unittest.TestCase):
    def act(self, id_, skill, aimed=False):
        return {"Id": id_, "Kind": "skill_use", "Parameters": {"skillId": skill, "aimed": aimed}}

    def enemies(self, *hps):
        return [{"index": i, "alive": True, "unit": {"hp": hp}} for i, hp in enumerate(hps)]

    def test_uses_a_skill_that_needs_no_target(self):
        self.assertEqual(greedy.skill_choice([self.act("skill_use_0", "s1")], self.enemies(30), tried=set()), "skill_use_0")

    def test_aims_at_the_weakest_living_enemy(self):
        acts = [self.act("skill_use_0_enemy_0", "s1", True), self.act("skill_use_0_enemy_1", "s1", True), self.act("skill_use_0_self", "s1", True)]
        self.assertEqual(greedy.skill_choice(acts, self.enemies(50, 12), tried=set()), "skill_use_0_enemy_1")

    def test_never_aims_a_skill_at_itself(self):
        self.assertIsNone(greedy.skill_choice([self.act("skill_use_0_self", "s1", True)], self.enemies(), tried=set()))

    def test_tries_each_skill_once_until_the_set_is_cleared(self):
        acts = [self.act("skill_use_0", "s1"), self.act("skill_use_1", "s2")]
        self.assertEqual(greedy.skill_choice(acts, self.enemies(30), tried={"s1"}), "skill_use_1")
        self.assertIsNone(greedy.skill_choice(acts, self.enemies(30), tried={"s1", "s2"}))

    def test_no_skill_offered(self):
        self.assertIsNone(greedy.skill_choice([{"Id": "battle_end_turn"}], self.enemies(30), tried=set()))


class CardChoice(unittest.TestCase):
    def test_picks_when_a_card_is_pickable(self):
        self.assertEqual(greedy.card_choice(["card_choice_pick_0", "card_choice_pick_1", "card_choice_skip", "deck_open"]), "card_choice_pick_0")

    def test_skips_when_the_collection_is_full(self):
        # the mod withholds the picks when the backpack is full and leaves only skip
        self.assertEqual(greedy.card_choice(["card_choice_skip", "deck_open"]), "card_choice_skip")

    def test_waits_while_the_cards_fade_in(self):
        # right after the screen opens nothing is clickable yet: no picks and no skip. Skipping then threw every reward card away.
        self.assertIsNone(greedy.card_choice(["deck_open"]))
        self.assertIsNone(greedy.card_choice([]))


class ShopChoice(unittest.TestCase):
    def buy(self, index, kind, price, warn=None):
        return {"Id": f"shop_buy_{index}", "Parameters": {"kind": kind, "price": price, "warn": warn}}

    def test_relics_first_then_blessings_then_cards(self):
        acts = [self.buy(0, "Card", 50), self.buy(1, "Bless", 40), self.buy(2, "Relic", 150), {"Id": "shop_leave"}]
        self.assertEqual(greedy.shop_choice(acts), "shop_buy_2")
        self.assertEqual(greedy.shop_choice([a for a in acts if a["Id"] != "shop_buy_2"]), "shop_buy_1")
        self.assertEqual(greedy.shop_choice([self.buy(0, "Card", 50), {"Id": "shop_leave"}]), "shop_buy_0")

    def test_cheaper_first_within_a_kind(self):
        acts = [self.buy(0, "Relic", 200), self.buy(1, "Relic", 120), {"Id": "shop_leave"}]
        self.assertEqual(greedy.shop_choice(acts), "shop_buy_1")

    def test_skips_a_relic_that_would_go_to_the_warehouse(self):
        # seen live: the 3rd relic opened "slots full, goes to the warehouse: buy?" and did nothing for the run
        acts = [self.buy(0, "Relic", 286, warn="all 6 relic slots are taken"), self.buy(1, "Card", 50), {"Id": "shop_leave"}]
        self.assertEqual(greedy.shop_choice(acts), "shop_buy_1")
        self.assertEqual(greedy.shop_choice([acts[0], acts[2]]), "shop_leave")

    def test_leaves_when_nothing_is_affordable(self):
        self.assertEqual(greedy.shop_choice([{"Id": "shop_refresh"}, {"Id": "shop_leave"}]), "shop_leave")

    def test_does_not_start_an_enchantment_or_a_divination_or_refresh(self):
        # enchanting needs the card picker, a divination the blessing picker: both are left to a smarter agent
        acts = [self.buy(0, "EnchTag", 80), self.buy(1, "RandomBlessing", 70), {"Id": "shop_refresh"}, {"Id": "shop_leave"}]
        self.assertEqual(greedy.shop_choice(acts), "shop_leave")

    def test_nothing_offered_yet(self):
        self.assertIsNone(greedy.shop_choice([]))


class PickerChoice(unittest.TestCase):
    def test_picks_the_first_card_not_yet_chosen(self):
        acts = [{"Id": "deck_pick_0", "Parameters": {"chosen": True}}, {"Id": "deck_pick_1", "Parameters": {"chosen": False}}, {"Id": "deck_pick_2", "Parameters": {"chosen": False}}]
        self.assertEqual(greedy.picker_choice(acts), "deck_pick_1")

    def test_cancels_when_there_is_nothing_to_choose(self):
        self.assertEqual(greedy.picker_choice([{"Id": "deck_select_cancel"}]), "deck_select_cancel")

    def test_nothing_offered(self):
        self.assertIsNone(greedy.picker_choice([]))


class ReconKey(unittest.TestCase):
    def test_ambient_windows_are_not_part_of_the_screen(self):
        st = {"phase": "unknown", "windows": ["TopBarUI", "ShopUI", "ChatUI"]}
        self.assertEqual(greedy.recon_key(st), ("unknown", ("ShopUI",)))

    def test_window_order_does_not_matter(self):
        a = {"phase": "unknown", "windows": ["B", "A"]}
        b = {"phase": "unknown", "windows": ["A", "B"]}
        self.assertEqual(greedy.recon_key(a), greedy.recon_key(b))

    def test_no_windows_key(self):
        self.assertEqual(greedy.recon_key({"phase": "battle"}), ("battle", ()))


class SelectChoice(unittest.TestCase):
    def pick(self, index, chosen=False):
        return {"Id": f"battle_select_card_{index}", "Parameters": {"cardIndex": index, "chosen": chosen}}

    def test_chooses_the_first_card_not_yet_chosen(self):
        acts = [self.pick(0, chosen=True), self.pick(1), self.pick(2)]
        self.assertEqual(greedy.select_choice(acts), "battle_select_card_1")

    def test_confirms_as_soon_as_the_game_offers_it(self):
        acts = [self.pick(0, chosen=True), self.pick(1), {"Id": "battle_select_confirm"}]
        self.assertEqual(greedy.select_choice(acts), "battle_select_confirm")

    def test_nothing_to_do_yet(self):
        self.assertIsNone(greedy.select_choice([self.pick(0, chosen=True)]))   # e.g. the picked card is still moving
        self.assertIsNone(greedy.select_choice([]))


class RoutePick(unittest.TestCase):
    """The route is built from a tray of 8 cards for 4 frames; the rest room ("Breaks") is always the last card of the tray (NormalMapManager)."""

    @staticmethod
    def tray(*cards):
        return [{"index": i, "kind": kind, "id": cid, "label": cid} for i, (kind, cid) in enumerate(cards)]

    @staticmethod
    def state(tray, empty):
        nodes = [{"index": i, "empty": i >= 4 - empty} for i in range(4)]
        return {"mapChoices": tray, "mapNodes": nodes}

    FULL = (("Fight", "level_1"), ("Fight", "level_2"), ("Build", "shop"), ("Event", "event_3"), ("Fight", "level_4"), ("Build", "Breaks"))

    def test_first_policy_takes_the_first_card(self):
        self.assertEqual(greedy.route_pick(self.state(self.tray(*self.FULL), 4), "first"), "map_place_0")

    def test_rest_policy_takes_cards_in_order_while_there_is_room(self):
        for empty in (4, 3, 2):
            self.assertEqual(greedy.route_pick(self.state(self.tray(*self.FULL), empty), "rest"), "map_place_0")

    def test_rest_policy_puts_the_rest_room_in_the_last_frame(self):
        # the last empty frame is the one right before the fixed end boss
        self.assertEqual(greedy.route_pick(self.state(self.tray(*self.FULL), 1), "rest"), "map_place_5")

    def test_rest_policy_uses_the_index_the_tray_reports(self):
        tray = [{"index": 0, "kind": "Fight", "id": "level_1", "label": "a"}, {"index": 1, "kind": "Build", "id": "Breaks", "label": "b"}]
        self.assertEqual(greedy.route_pick(self.state(tray, 1), "rest"), "map_place_1")

    def test_rest_policy_without_a_rest_room_falls_back_to_the_first_card(self):
        tray = self.tray(("Fight", "level_1"), ("Build", "shop"))
        self.assertEqual(greedy.route_pick(self.state(tray, 1), "rest"), "map_place_0")

    def test_rest_policy_never_leaves_a_frame_empty_for_the_rest_room(self):
        # only the rest room left and two frames to fill: it goes in now rather than the bot stalling
        self.assertEqual(greedy.route_pick(self.state(self.tray(("Build", "Breaks")), 2), "rest"), "map_place_0")

    def test_no_choices_or_no_route_data_means_no_opinion(self):
        self.assertIsNone(greedy.route_pick({}, "rest"))
        self.assertIsNone(greedy.route_pick(self.state([], 2), "rest"))
        self.assertIsNone(greedy.route_pick({"mapChoices": self.tray(*self.FULL)}, "rest"))   # no mapNodes: cannot tell the last frame


class RunOver(unittest.TestCase):
    def test_leaving_the_settlement_screen_ends_a_run(self):
        self.assertTrue(greedy.run_over("run_end", "hub"))
        self.assertTrue(greedy.run_over("run_end", "main_menu"))

    def test_staying_on_it_or_arriving_does_not(self):
        self.assertFalse(greedy.run_over("run_end", "run_end"))   # the first click only shows the score breakdown
        self.assertFalse(greedy.run_over("battle", "run_end"))
        self.assertFalse(greedy.run_over(None, "hub"))


class StuckWatch(unittest.TestCase):
    def setUp(self):
        self.now = 1000.0
        self.watch = greedy.StuckWatch(limit=90, clock=lambda: self.now)

    def test_an_unchanged_state_is_stuck_only_after_the_limit(self):
        self.assertFalse(self.watch.stuck("a"))
        self.now += 89
        self.assertFalse(self.watch.stuck("a"))
        self.now += 2
        self.assertTrue(self.watch.stuck("a"))

    def test_a_changed_state_starts_the_count_again(self):
        self.watch.stuck("a")
        self.now += 80
        self.assertFalse(self.watch.stuck("b"))
        self.now += 80
        self.assertFalse(self.watch.stuck("b"))

    def test_the_default_clock_does_not_run_while_the_mac_sleeps(self):
        # a lid closed in the middle of a fight froze the game for minutes; the wall clock then read "stuck for 90 s" on the first look after the
        # wake (seen 2026-10-14: two false stalls). On macOS time.monotonic() is mach_absolute_time(), which stands still in sleep.
        self.assertIs(greedy.StuckWatch().clock, time.monotonic)


if __name__ == "__main__":
    unittest.main()
