"""scripts/dashboard.py against a fake bridge: log parsing, cached run/hub, the HTTP surface, and that nothing but GET /state is ever sent."""
import http.client
import importlib.util
import json
import pathlib
import queue
import tempfile
import threading
import time
import unittest
import urllib.error
import urllib.request

from tests.support import BATTLE_STATE, FakeBridge

SCRIPT = pathlib.Path(__file__).resolve().parent.parent / "scripts" / "dashboard.py"
spec = importlib.util.spec_from_file_location("dashboard", SCRIPT)
dashboard = importlib.util.module_from_spec(spec)
spec.loader.exec_module(dashboard)

HUB_STATE = {"phase": "hub", "frame": 5, "scene": "hub", "windows": [], "actions": [],
             "hub": {"level": 35, "exp": 16, "truth": 557, "time": 270, "runsPlayed": 36, "page": None}}
REST_STATE = {"phase": "rest", "frame": 6, "scene": "game", "windows": [], "actions": [],
              "run": {"gold": 54, "inDeck": 21, "minimum": 12, "maximum": 24, "backpack": 0, "backpackMax": 5, "relics": 4, "relicSlots": 6,
                      "attributes": {"Strength": 2, "Wisdom": 6}}}

LOG = """00:11:18 modal: 提示 / 该模式已有存档
00:11:30 play 炽焰新星 -> enemy 1: played: Card played.
00:11:31 play 无锋重刃 -> enemy 1: played: Card played.
00:11:34 end turn (hp 149, draw 8)
00:11:35 act reward_claim_0: ok: Claimed
00:11:36 skill: skill_use_0
00:12:01 fight won (1)
00:12:52 fight won (2)
00:19:00 bridge unreachable
"""


def get(url):
    with urllib.request.urlopen(url, timeout=5) as r:
        return r.status, r.headers.get("Content-Type"), r.read()


class LogTailTests(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.TemporaryDirectory()
        self.path = pathlib.Path(self.dir.name) / "bot.log"
        self.addCleanup(self.dir.cleanup)

    def test_missing_file_is_empty_not_an_error(self):
        out = dashboard.LogTail(self.path).poll()
        self.assertEqual(out["lines"], [])
        self.assertIsNone(out["ageS"])

    def test_counts_and_kinds(self):
        self.path.write_text(LOG, encoding="utf-8")
        out = dashboard.LogTail(self.path).poll()
        self.assertEqual(out["counts"], {"plays": 2, "turns": 1, "acts": 1, "wins": 2, "runs": 0})
        kinds = [l["kind"] for l in out["lines"]]
        self.assertEqual(kinds, ["info", "play", "play", "turn", "act", "skill", "win", "win", "warn"])
        self.assertEqual(out["lines"][1]["t"], "00:11:30")
        self.assertLess(out["ageS"], 5)

    def test_reads_only_new_whole_lines(self):
        tail = dashboard.LogTail(self.path)
        self.path.write_text("00:00:01 play A -> enemy 0: ok\n00:00:02 play B -> en", encoding="utf-8")   # second line half written
        self.assertEqual(tail.poll()["counts"]["plays"], 1)
        with open(self.path, "a", encoding="utf-8") as f:
            f.write("emy 0: ok\n")
        out = tail.poll()
        self.assertEqual(out["counts"]["plays"], 2)
        self.assertEqual([l["text"] for l in out["lines"]][-1], "play B -> enemy 0: ok")

    def test_truncated_log_starts_over(self):
        tail = dashboard.LogTail(self.path)
        self.path.write_text(LOG, encoding="utf-8")
        tail.poll()
        self.path.write_text("00:00:01 play A -> enemy 0: ok\n", encoding="utf-8")
        out = tail.poll()
        self.assertEqual(out["counts"]["plays"], 1)
        self.assertEqual(len(out["lines"]), 1)

    def test_keeps_only_the_last_lines(self):
        self.path.write_text("".join(f"00:00:00 act a{i}: ok\n" for i in range(200)), encoding="utf-8")
        out = dashboard.LogTail(self.path, keep=10).poll()
        self.assertEqual(len(out["lines"]), 10)
        self.assertEqual(out["lines"][-1]["text"], "act a199: ok")
        self.assertEqual(out["counts"]["acts"], 200)


class GameTests(unittest.TestCase):
    def test_unreachable_bridge(self):
        bridge = FakeBridge({})
        port = bridge.port
        bridge.close()
        self.assertEqual(dashboard.Game(port, timeout=0.5).snapshot(), {"reachable": False})

    def test_battle_is_compacted_and_dead_enemies_flagged(self):
        bridge = FakeBridge({("GET", "/state"): BATTLE_STATE})
        self.addCleanup(bridge.close)
        snap = dashboard.Game(bridge.port).snapshot()
        b = snap["battle"]
        self.assertTrue(snap["reachable"])
        self.assertEqual((b["player"]["hp"], b["player"]["maxHp"], b["player"]["power"]), (70, 80, 3))
        self.assertEqual(b["enemies"][0]["intents"][0]["name"], "黏黏冲撞")
        self.assertFalse(b["enemies"][1]["alive"])
        self.assertFalse(b["hand"][1]["playable"])
        self.assertNotIn("uid", b["hand"][0])   # only what the page shows

    def test_run_and_hub_survive_a_fight(self):
        states = iter([HUB_STATE, REST_STATE, BATTLE_STATE])
        bridge = FakeBridge({("GET", "/state"): lambda _b: next(states)})
        self.addCleanup(bridge.close)
        game = dashboard.Game(bridge.port)
        game.snapshot()
        game.snapshot()
        snap = game.snapshot()                         # battle: the server omits run/hub, the cache fills them
        self.assertEqual(snap["phase"], "battle")
        self.assertEqual(snap["hub"]["truth"], 557)
        self.assertEqual(snap["run"]["gold"], 54)
        self.assertIsNotNone(snap["runAt"])
        self.assertNotIn("runAgeS", snap)   # an age changes on every look; the page computes it from the timestamp
        self.assertEqual([p for _t, p in snap["phases"]], ["hub", "rest", "battle"])

    def test_phase_timeline_records_changes_only(self):
        bridge = FakeBridge({("GET", "/state"): BATTLE_STATE})
        self.addCleanup(bridge.close)
        game = dashboard.Game(bridge.port)
        for _ in range(3):
            game.snapshot()
        self.assertEqual(len(game.phases), 1)


class ServerTests(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.TemporaryDirectory()
        self.addCleanup(self.dir.cleanup)
        self.log = pathlib.Path(self.dir.name) / "bot.log"
        self.log.write_text(LOG, encoding="utf-8")
        self.tasks = pathlib.Path(self.dir.name) / "tasks.json"
        self.bridge = FakeBridge({("GET", "/state"): BATTLE_STATE})
        self.addCleanup(self.bridge.close)
        self.srv = dashboard.serve("127.0.0.1", 0, self.bridge.port, self.log, self.tasks)
        threading.Thread(target=self.srv.serve_forever, daemon=True).start()
        self.addCleanup(lambda: (self.srv.shutdown(), self.srv.server_close()))
        self.base = f"http://127.0.0.1:{self.srv.server_address[1]}"

    def test_binds_to_loopback(self):
        self.assertEqual(self.srv.server_address[0], "127.0.0.1")

    def test_status_joins_game_bot_and_tasks(self):
        self.tasks.write_text(json.dumps({"goal": "g", "steps": [{"text": "a", "state": "done"}]}), encoding="utf-8")
        code, ctype, body = get(self.base + "/api/status")
        d = json.loads(body)
        self.assertEqual(code, 200)
        self.assertIn("application/json", ctype)
        self.assertEqual(d["game"]["phase"], "battle")
        self.assertEqual(d["bot"]["counts"]["wins"], 2)
        self.assertEqual(d["tasks"]["steps"][0]["state"], "done")

    def test_missing_tasks_file_is_null_and_broken_one_is_reported(self):
        self.assertIsNone(json.loads(get(self.base + "/api/status")[2])["tasks"])
        self.tasks.write_text("{not json", encoding="utf-8")
        self.assertIn("error", json.loads(get(self.base + "/api/status")[2])["tasks"])

    def test_page_is_served_and_unknown_paths_404(self):
        code, ctype, body = get(self.base + "/")
        self.assertEqual(code, 200)
        self.assertIn("text/html", ctype)
        self.assertIn("/api/status", body.decode())
        with self.assertRaises(urllib.error.HTTPError) as cm:
            get(self.base + "/nope")
        self.assertEqual(cm.exception.code, 404)
        cm.exception.close()

    def test_favicon_is_an_empty_204_not_a_404(self):
        with urllib.request.urlopen(self.base + "/favicon.ico", timeout=5) as r:
            self.assertEqual(r.status, 204)

    def test_only_reads_the_state_endpoint(self):
        get(self.base + "/api/status")
        get(self.base + "/api/status")
        self.assertTrue(self.bridge.requests)
        self.assertEqual({(m, p) for m, p, _b in self.bridge.requests}, {("GET", "/state")})

    def test_page_never_puts_game_text_into_innerhtml_unescaped(self):
        # The log and the cards carry game text. Feed lines are DOM nodes built with textContent; everything else goes through esc().
        html = (SCRIPT.parent / "dashboard.html").read_text(encoding="utf-8")
        self.assertIn("const esc", html)
        self.assertIn("textContent", html)
        self.assertNotIn("${l.text}", html)
        self.assertNotIn("innerHTML = l.", html)


def read_event(fp):
    """The next (event, data) frame of an SSE body, comments and retry lines skipped. EOFError when the stream closes."""
    event, data = None, []
    while True:
        line = fp.readline().decode()
        if line == "":
            raise EOFError("stream closed")
        line = line.rstrip("\n")
        if line == "":
            if event:
                return event, json.loads("\n".join(data))
            continue
        if line.startswith((":", "retry:")):
            continue
        if line.startswith("event: "):
            event = line[7:]
        elif line.startswith("data: "):
            data.append(line[6:])


class Stream:
    """A page's EventSource: a thread reads the connection (blocking, like a browser) and queues the events for the test."""

    def __init__(self, base):
        host, port = base.replace("http://", "").split(":")
        self.conn = http.client.HTTPConnection(host, int(port), timeout=None)   # a browser waits for events as long as it takes
        self.conn.request("GET", "/api/stream")
        self.resp = self.conn.getresponse()
        self.events = queue.Queue()
        threading.Thread(target=self._read, daemon=True).start()

    def _read(self):
        try:
            while True:
                self.events.put((time.time(), *read_event(self.resp.fp)))
        except Exception:
            self.events.put(None)

    def first(self):
        """(name, data) of the next event, whatever it is."""
        item = self.events.get(timeout=5)
        return item[1], item[2]

    def next_of(self, wanted, limit=5.0):
        """The data of the next event called `wanted` (others are dropped); AssertionError if none within `limit` seconds."""
        deadline = time.time() + limit
        while time.time() < deadline:
            try:
                item = self.events.get(timeout=max(0.01, deadline - time.time()))
            except queue.Empty:
                break
            if item is None:
                raise AssertionError("stream closed")
            if item[1] == wanted:
                return item[2]
        raise AssertionError(f"no {wanted!r} event within {limit}s")

    def quiet_for(self, name, seconds):
        """True when no event called `name` arrives for `seconds`."""
        deadline = time.time() + seconds
        while time.time() < deadline:
            try:
                item = self.events.get(timeout=max(0.01, deadline - time.time()))
            except queue.Empty:
                break
            if item is not None and item[1] == name:
                return False
        return True

    def close(self):
        self.conn.close()


class StreamTests(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.TemporaryDirectory()
        self.addCleanup(self.dir.cleanup)
        self.log = pathlib.Path(self.dir.name) / "bot.log"
        self.log.write_text(LOG, encoding="utf-8")
        self.tasks = pathlib.Path(self.dir.name) / "tasks.json"
        self.frame = 0

        def state(_body):
            self.frame += 1                       # the frame number moves on every look: it must not count as a change
            return {**BATTLE_STATE, "frame": self.frame}
        self.bridge = FakeBridge({("GET", "/state"): state})
        self.addCleanup(self.bridge.close)
        self.srv = dashboard.serve("127.0.0.1", 0, self.bridge.port, self.log, self.tasks, fast=0.05, idle=0.5)
        threading.Thread(target=self.srv.serve_forever, daemon=True).start()
        self.addCleanup(lambda: (self.srv.shutdown(), self.srv.server_close()))
        self.base = f"http://127.0.0.1:{self.srv.server_address[1]}"

    def stream(self):
        s = Stream(self.base)
        self.addCleanup(s.close)
        return s

    def test_first_frame_is_a_full_snapshot(self):
        name, snap = self.stream().first()
        self.assertEqual(name, "snapshot")
        self.assertEqual(snap["game"]["phase"], "battle")
        self.assertEqual(snap["bot"]["counts"]["wins"], 2)
        self.assertEqual(snap["bot"]["seq"], len(LOG.splitlines()))
        self.assertEqual(snap["bot"]["lines"][-1]["n"], snap["bot"]["seq"])

    def test_new_log_lines_are_pushed_once_with_their_numbers(self):
        s = self.stream()
        _, snap = s.first()
        with open(self.log, "a", encoding="utf-8") as f:
            f.write("00:20:00 play 火球 -> enemy 0: played\n00:20:01 fight won (3)\n")
        pushed = s.next_of("log")
        self.assertEqual([l["n"] for l in pushed["lines"]], [snap["bot"]["seq"] + 1, snap["bot"]["seq"] + 2])
        self.assertEqual([l["kind"] for l in pushed["lines"]], ["play", "win"])
        self.assertEqual(pushed["counts"]["wins"], 3)
        self.assertTrue(s.quiet_for("log", 0.4))   # not repeated on the next poll

    def test_game_state_is_pushed_on_change_only(self):
        s = self.stream()
        s.first()
        self.assertTrue(s.quiet_for("state", 0.8))   # frames keep counting, nothing else moves
        self.bridge.routes[("GET", "/state")] = HUB_STATE
        t0 = time.time()
        state = s.next_of("state")
        self.assertEqual(state["phase"], "hub")
        self.assertLess(time.time() - t0, 0.6)       # a poll interval, not a second

    def test_task_file_changes_are_pushed(self):
        s = self.stream()
        _, snap = s.first()
        self.assertIsNone(snap["tasks"])
        self.tasks.write_text(json.dumps({"goal": "新目标", "steps": []}), encoding="utf-8")
        self.assertEqual(s.next_of("tasks")["goal"], "新目标")

    def test_beats_carry_the_clock_and_the_log_age(self):
        beat = self.stream().next_of("beat", limit=4.0)
        self.assertIn("uptimeS", beat)
        self.assertIn("ageS", beat)

    def test_polls_fast_only_while_a_page_is_connected(self):
        def hits(seconds):
            before = len(self.bridge.requests)
            time.sleep(seconds)
            return len(self.bridge.requests) - before
        idle = hits(1.0)
        s = self.stream()
        s.first()
        fast = hits(1.0)
        self.assertLessEqual(idle, 5)       # idle interval 0.5 s
        self.assertGreaterEqual(fast, 10)   # fast interval 0.05 s

    def test_the_stream_only_reads_state(self):
        s = self.stream()
        s.first()
        time.sleep(0.3)
        self.assertEqual({(m, p) for m, p, _b in self.bridge.requests}, {("GET", "/state")})

    def test_two_pages_get_the_same_events(self):
        a, b = self.stream(), self.stream()
        a.first()
        b.first()
        self.bridge.routes[("GET", "/state")] = HUB_STATE
        self.assertEqual(a.next_of("state")["phase"], "hub")
        self.assertEqual(b.next_of("state")["phase"], "hub")


class BroadcasterTests(unittest.TestCase):
    def test_a_page_that_cannot_keep_up_is_dropped(self):
        bus = dashboard.Broadcaster()
        slow, fine = bus.subscribe(), bus.subscribe()
        for _ in range(1000):
            slow.q.put_nowait(b"x")
        bus.publish("state", {"a": 1})
        self.assertTrue(slow.dead)
        self.assertEqual(bus.count(), 1)
        self.assertFalse(fine.dead)
        self.assertIn(b"event: state", fine.q.get_nowait())

    def test_frames_are_single_line_json(self):
        frame = dashboard.sse("log", {"text": "a\nb 中文"}).decode()
        self.assertEqual(frame, 'event: log\ndata: {"text": "a\\nb 中文"}\n\n')

if __name__ == "__main__":
    unittest.main()
