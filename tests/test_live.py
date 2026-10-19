"""End-to-end tests against a running lab game (scripts/lab.sh start).

Skipped when no bridge answers. The test that plays a whole first fight changes the lab profile (isolated from any
real save by its own bundle id) and needs a fresh one, so it only runs with WITCHCTL_E2E_PLAY=1 after `lab.sh reset`.
"""
import json
import os
import re
import time
import unittest

from tests.live import PORT, action_ids, bridge_up, cli, percentile, reach_battle, reach_rest, state, wait_state

BUDGET_MS = 150
KNOWN_PHASES = {"main_menu", "house", "hub", "game_entry", "map_select", "dialogue", "battle", "battle_won", "battle_lost",
                "rewards", "card_choice", "blessing_choice", "rest", "event", "run_end", "mode_choice", "announcement", "item_reveal", "modal",
                "deck", "deck_select", "shop", "achievements", "outsider_shop", "unknown"}
RICH_TEXT = re.compile(r"[<>{}]")   # unresolved placeholders ({0}) and Unity rich text (<color>, <sprite>)


@unittest.skipUnless(bridge_up(), "no WitchCtl bridge on the configured port (scripts/lab.sh start)")
class LiveReadOnly(unittest.TestCase):
    def test_ping_reports_version_and_mute_flag(self):
        data = json.loads(cli("ping", "--json", check=True)[1])
        self.assertTrue(data["ok"])
        self.assertIsInstance(data["audioMuted"], bool)  # lab.sh start launches with WITCHCTL_MUTE=1

    def test_state_has_known_phase_actions_and_open_windows(self):
        st = state()
        self.assertIn(st["phase"], KNOWN_PHASES)
        self.assertIsInstance(st["actions"], list)
        self.assertTrue(all(isinstance(w, str) for w in st["windows"]))

    def test_cli_round_trip_within_budget(self):
        """Whole process: interpreter start + HTTP + main-thread hop + game call."""
        for cmd in (["state"], ["actions"], ["ui"], ["ping"]):
            times = [cli(*cmd, check=True)[3] for _ in range(60)]
            p99 = percentile(times, 0.99)
            self.assertLess(p99, BUDGET_MS, f"`witchctl {cmd[0]}` p99={p99:.0f}ms p50={percentile(times, .5):.0f}ms")

    def test_ui_dump_lists_windows_without_rich_text(self):
        ui = json.loads(cli("ui", "--json", check=True)[1])
        self.assertTrue(ui["windows"])
        for w in ui["windows"]:
            self.assertIn("clickables", w)
            for c in w["clickables"]:
                self.assertNotIn("<sprite", c["label"], f"{w['name']}: {c}")

    def test_click_on_missing_target_is_refused_without_side_effects(self):
        code, _, err, _ = cli("click", "这个按钮绝对不存在")
        self.assertEqual(code, 1)
        self.assertIn("no_match", err)

    def test_unknown_action_id_is_refused(self):
        code, out, _, _ = cli("act", "no_such_action")
        self.assertEqual(code, 1)
        self.assertIn("refused", out)

    def test_screen_actions_are_refused_when_their_screen_is_not_open(self):
        phase = state()["phase"]
        if phase in ("rewards", "card_choice", "blessing_choice", "battle_won", "event", "run_end", "rest", "mode_choice", "announcement",
                     "modal", "deck", "deck_select", "shop", "game_entry", "achievements", "outsider_shop"):
            self.skipTest("one of the modelled screens is open")
        actions = ["reward_close", "reward_claim_0", "card_choice_pick_0", "card_choice_skip", "blessing_pick_0", "battle_open_rewards",
                   "event_option_0", "run_end_continue", "rest_eat_0", "rest_leave", "mode_choice_classic", "announcement_close",
                   "deck_equip_0", "deck_close", "deck_pick_0", "deck_select_cancel", "shop_buy_0", "shop_refresh", "shop_leave",
                   "entry_career_0", "entry_main_Lucky", "entry_pack_off_cardpack_1", "entry_prepare_open",
                   "achievement_claim_0", "achievement_close", "outsider_buy_0", "outsider_toggle_0", "outsider_leave"]
        if phase != "hub":   # in the hub a page can be open; closing / pressing a button on a page is then real
            actions += ["hub_page_close", "hub_page_item_0"]
        if phase != "battle":   # in a fight a skill is a real action
            actions += ["skill_use_0", "skill_use_0_self", "skill_use_0_enemy_0"]
        for action in actions:
            code, out, _, _ = cli("act", action)
            self.assertEqual(code, 1, action)
            self.assertIn("refused", out)

    def test_hub_pages_each_report_their_own_name_and_close_again(self):
        """Seven buildings open seven book pages. The page showing is the MUIP WindowManager's current one (every page object stays active,
        which once made all of them read as the library). Read-only: nothing is bought."""
        if state()["phase"] != "hub":
            self.skipTest("not in the hub")
        expected = {"library": "图书馆", "tower": "高塔", "exhibition": "展览馆", "store": "储藏室", "administration": "行政厅", "spring": "温泉", "witch_house": "魔女屋"}
        seen = {}
        for building, name in expected.items():
            if "hub_visit_" + building not in action_ids(state()):
                continue
            self.assertEqual(cli("act", "hub_visit_" + building)[0], 0, building)
            st = wait_state(lambda s: s["phase"] == "hub" and (s.get("hub") or {}).get("page"), 5, "the page to open")
            for _ in range(3):   # a first-visit tip may sit over the page
                if "tutorial_advance" not in action_ids(st):
                    break
                cli("act", "tutorial_advance")
                st = state()
            seen[building] = st["hub"]["page"]
            self.assertEqual(seen[building], name)
            self.assertIn("hub_page_close", action_ids(st))
            self.assertEqual(cli("act", "hub_visit_" + building)[0], 1, "a second page cannot be opened over the first")
            self.assertEqual(cli("act", "hub_page_close")[0], 0)
            wait_state(lambda s: s["phase"] == "hub" and not (s.get("hub") or {}).get("page"), 5, "the page to close")
        self.assertEqual(len(set(seen.values())), len(seen), "every page must report a different name")

    def test_meta_shop_opens_from_the_tower_lists_items_and_refuses_what_it_cannot_do(self):
        """Read-only tour of the tower's "唤醒本源" shop: items with their prices, refusals for unknown / unaffordable / not-owned, leave."""
        if state()["phase"] != "hub":
            self.skipTest("not in the hub")
        if "hub_visit_tower" not in action_ids(state()):
            self.skipTest("no tower sign on screen")
        cli("act", "hub_visit_tower")
        st = wait_state(lambda s: (s.get("hub") or {}).get("page") == "高塔", 5, "the tower page")
        for _ in range(3):
            if "tutorial_advance" not in action_ids(st):
                break
            cli("act", "tutorial_advance")
            st = state()
        item = [i for i in st["hub"]["pageItems"] if i["type"] == "rollShop"]
        self.assertTrue(item, "the tower page must offer rollShop")
        self.assertEqual(cli("act", "hub_page_item_%d" % item[0]["index"])[0], 0)
        st = wait_state(lambda s: s["phase"] in ("outsider_shop", "unknown"), 5, "the shop window")
        for _ in range(3):
            if "tutorial_advance" not in action_ids(st):
                break
            cli("act", "tutorial_advance")
            st = wait_state(lambda s: s["phase"] == "outsider_shop", 5, "the shop after its tip")
        self.assertEqual(st["phase"], "outsider_shop")
        shop = st["outsiderShop"]
        self.assertIn(shop["tab"], ("cards", "origins"))
        self.assertTrue(shop["items"])
        ids = action_ids(st)
        self.assertIn("outsider_leave", ids)
        for it in shop["items"]:
            self.assertIn(it["mode"], ("buy", "toggle", "owned"))
            if it["mode"] == "buy" and not it["affordable"]:
                self.assertNotIn("outsider_buy_%d" % it["index"], ids, "an item the purse cannot pay must not be offered")
                code, out, _, _ = cli("act", "outsider_buy_%d" % it["index"])
                self.assertEqual(code, 1)
                self.assertIn("refused", out)
        self.assertEqual(cli("act", "outsider_buy_999")[0], 1)
        owned = [it for it in shop["items"] if it["mode"] == "owned"]
        if owned:
            self.assertEqual(cli("act", "outsider_buy_%d" % owned[0]["index"])[0], 1, "an owned item cannot be bought again")
        self.assertEqual(cli("act", "outsider_leave")[0], 0)
        wait_state(lambda s: s["phase"] == "hub", 5, "the shop to close")
        self.assertEqual(cli("act", "hub_page_close")[0], 0)

    def test_malformed_json_body_is_a_400_not_a_500(self):
        import socket
        body = b"{nope"
        head = b"POST /act HTTP/1.1\r\nHost: x\r\nConnection: close\r\nContent-Length: " + str(len(body)).encode() + b"\r\n\r\n"
        with socket.create_connection(("127.0.0.1", PORT), timeout=5) as sock:
            sock.sendall(head + body)
            reply = b""
            while chunk := sock.recv(4096):
                reply += chunk
        self.assertIn(b" 400 ", reply.split(b"\r\n", 1)[0])


@unittest.skipUnless(bridge_up() and os.environ.get("WITCHCTL_E2E_PLAY") == "1",
                     "set WITCHCTL_E2E_PLAY=1 on a fresh profile (lab.sh reset && lab.sh start): plays a whole first fight")
class LivePlay(unittest.TestCase):
    def test_first_fight_from_menu_to_map(self):
        st = state()
        if st["phase"] == "main_menu":
            # A repeated scene change must be refused: two `start_game` / `map_continue` in a row start two scenes on top of each other.
            self.assertEqual(cli("act", "main_menu_start_game")[0], 0)
            code, out, _, _ = cli("act", "main_menu_start_game")
            self.assertEqual(code, 1)
            self.assertIn("still changing scene", out)

        st = reach_battle()
        self.check_snapshot(st)
        self.play_first_playable_card(st)
        self.assertEqual(self.fight_to_the_end(), "won")
        self.take_every_reward()

    def test_rest_room_eat_and_leave(self):
        """The rest room is a world scene (no window): phase `rest`, food to eat, an arrow to leave. Needs a run that starts
        with route building, i.e. not the tutorial run of a fresh profile."""
        try:
            st = reach_rest()
        except AssertionError as e:
            self.skipTest("no rest room on this profile's run: " + str(e).splitlines()[0])
        ids = action_ids(st)
        rest = st["rest"]
        self.assertGreater(rest["maxHp"], 0)
        self.assertIn("rest_leave", ids)
        self.assertEqual(len([i for i in ids if i.startswith("rest_eat_")]), len(rest["food"]))

        self.assertEqual(cli("act", "rest_eat_99")[0], 1, "a food that is not on the table must be refused")
        if rest["food"]:
            code, out, _, ms = cli("act", "rest_eat_0")
            self.assertEqual(code, 0, out)
            self.assertLess(ms, BUDGET_MS)
            after = wait_state(lambda s: s["phase"] == "rest" and len(s["rest"]["food"]) == len(rest["food"]) - 1, 5, "the food to be gone")
            self.assertGreater(after["rest"]["maxHp"], rest["maxHp"])

        self.assertEqual(cli("act", "rest_leave")[0], 0)
        self.assertEqual(cli("act", "rest_leave")[0], 1, "leaving twice must be refused")
        wait_state(lambda s: s["phase"] != "rest", 15, "the rest room to close")

    # ------------------------------------------------------------------ steps

    def check_snapshot(self, st):
        raw = cli("state", "--json", check=True)[1]
        self.assertLess(len(raw), 12000, "a snapshot should stay small (the raw card tables are behind --full)")
        b = st["battle"]
        self.assertTrue(b["enemies"], "battle without enemies")
        self.assertGreater(b["player"]["unit"]["maxHp"], 0)
        self.assertGreaterEqual(b["player"]["piles"]["draw"], 0)
        self.assertEqual(b["flow"]["dealing"], 0, "canAct must wait until the hand has been dealt")
        for unit in [b["player"]["unit"]] + [e["unit"] for e in b["enemies"]]:
            self.assertGreaterEqual(unit["hp"], 0)
            self.assertIsInstance(unit["buffs"], list)
        for e in b["enemies"]:
            for intent in e["intents"] or []:
                self.assertNotRegex(intent["text"], RICH_TEXT, f"enemy intent text still raw: {intent}")
        for card in b["hand"]:
            for key in ("uid", "id", "name", "type", "cost", "text", "playable"):
                self.assertIn(key, card)
            self.assertNotIn("data", card)
            self.assertNotRegex(card["text"], RICH_TEXT, f"card text still raw: {card}")

    def play_first_playable_card(self, st):
        b = st["battle"]
        playable = [c for c in b["hand"] if c["playable"]]
        self.assertTrue(playable, "no playable card in the opening hand: " + json.dumps([c.get("reason") for c in b["hand"]], ensure_ascii=False))
        card = playable[0]
        power_before = b["player"]["powerCount"]
        target = ["--target", str(next(e["index"] for e in b["enemies"] if e["alive"]))] if card["kind"] == "AttackCardItem" else []

        # Default `play` validates, queues and answers at once; the game's own card resolution (hundreds of ms) happens after.
        code, out, err, ms = cli("play", "--uid", card["uid"], *target)
        self.assertEqual(code, 0, out + err)
        self.assertIn("queued", out)
        self.assertLess(ms, BUDGET_MS, f"play ack took {ms:.0f}ms")

        after = wait_state(lambda s: s["battle"]["player"]["powerCount"] == power_before - card["cost"], 5, "the energy to drop by the card's cost")
        self.assertTrue(after["battle"]["flow"]["lastPlay"]["Ok"], after["battle"]["flow"]["lastPlay"])

        # Reading state must be side-effect free. The game's own legality probe spends energy on every call, so thirty
        # reads (state + actions) that leave the energy untouched is the regression test for that.
        power_now = state()["battle"]["player"]["powerCount"]
        for _ in range(15):
            state()
            cli("actions", check=True)
        self.assertEqual(state()["battle"]["player"]["powerCount"], power_now, "reading state changed the player's energy")

    def fight_to_the_end(self):
        """Greedy turns until the fight is decided. Returns 'won' or 'lost'."""
        for turn in range(40):
            st = wait_state(lambda s: s["phase"] != "battle" or s["battle"]["flow"].get("canAct"), 90, "the player's turn or the end of the fight")
            if st["phase"] in ("battle_won", "rewards"):
                return "won"
            if st["phase"] != "battle":
                return "lost" if st["phase"] == "battle_lost" else st["phase"]
            while True:
                b = state()["battle"]
                playable = [c for c in b["hand"] if c["playable"]]
                alive = [e for e in b["enemies"] if e["alive"]]
                if not playable or not alive:
                    break
                best = max(playable, key=lambda c: (10 if "攻击" in (c["type"] or "") else 5, c["cost"]))
                code, out, err, _ = cli("play", "--uid", best["uid"], "--target", str(min(alive, key=lambda e: e["unit"]["hp"])["index"]), "--wait")
                if code != 0:
                    break
            cli("end-turn")
            time.sleep(0.5)
        self.fail("the first fight did not end in 40 turns")

    def take_every_reward(self):
        """Fight won → (chest) → rewards → card choice / blessing → close → map."""
        for _ in range(16):
            # A screen is reported as soon as it is up, but its actions only appear once it can be used (cards fade in for a second,
            # the map fills in its nodes): wait for an offer, not just for the phase.
            st = wait_state(lambda s: s["phase"] in ("battle_won", "rewards", "card_choice", "blessing_choice", "map_select") and action_ids(s),
                            60, "an action on a reward screen or the map")
            phase, ids = st["phase"], action_ids(st)
            if phase == "map_select":
                self.assertIn("map_continue", ids)
                return
            if phase == "battle_won":
                action = "battle_open_rewards"
            elif phase == "rewards":
                claim = [i for i in ids if i.startswith("reward_claim_")]
                action = claim[0] if claim else "reward_close"
            elif phase == "card_choice":
                action = "card_choice_pick_0"
                self.assertTrue(all(c["name"] and "<" not in c["text"] for c in st["cardChoice"]), st["cardChoice"])
            else:
                action = "blessing_pick_0"
            self.assertIn(action, ids)
            code, out, err, _ = cli("act", action)
            self.assertEqual(code, 0, out + err)
            time.sleep(1.0)
        self.fail("never got back to the map")


if __name__ == "__main__":
    unittest.main()
