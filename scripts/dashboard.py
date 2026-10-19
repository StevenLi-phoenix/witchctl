#!/usr/bin/env python3
"""Read-only progress page for a witchctl session.

    scripts/dashboard.py --log run1.log [--tasks tasks.json] [--port 47801]

Serves one page (scripts/dashboard.html), a Server-Sent Events stream (`GET /api/stream`: snapshot, then state / log / tasks / beat events as
they change) and a one-shot `GET /api/status`. Everything is read: the bridge's `/state`
(side-effect free, also in a fight), the bot's log file and an optional task list the operator keeps by hand.
Nothing is clicked or played. Binds to loopback unless --host says otherwise. stdlib only.
"""
import argparse
import collections
import json
import logging
import queue
import re
import threading
import time
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

HERE = Path(__file__).resolve().parent
LOG = logging.getLogger("dashboard")

LINE = re.compile(r"^(\d\d:\d\d:\d\d) (.*)$")
WON = re.compile(r"^fight won \((\d+)\)")
KINDS = (("play ", "play"), ("end turn", "turn"), ("act ", "act"), ("skill:", "skill"), ("picker:", "skill"),
         ("select:", "skill"), ("fight won", "win"), ("run over", "win"), ("deck:", "deck"), ("shop", "shop"),
         ("modal:", "info"), ("item:", "info"))


def classify(text):
    for prefix, kind in KINDS:
        if text.startswith(prefix):
            return kind
    if "unreachable" in text or "stuck" in text or "out of time" in text:
        return "warn"
    return "info"


class LogTail:
    """Reads a growing log incrementally: counters over the whole file, the last `keep` lines for display."""

    def __init__(self, path, keep=80):
        self.path = Path(path) if path else None
        self.keep = keep
        self.seq = 0                 # line numbers never restart, so a page can drop lines it has already seen
        self._reset()

    def _reset(self):
        self.offset = 0
        self.lines = collections.deque(maxlen=self.keep)
        self.counts = {"plays": 0, "turns": 0, "acts": 0, "wins": 0, "runs": 0}

    def poll(self):
        """Absorb new bytes; returns {lines, new, counts, ageS, size, seq}: the last lines kept, those that arrived in this call, the counters,
        the seconds since the last write (None if there is no file) and the number of the latest line."""
        self._new = []
        if not self.path or not self.path.exists():
            return {"lines": [], "new": [], "counts": self.counts, "ageS": None, "size": 0, "seq": self.seq}
        st = self.path.stat()
        if st.st_size < self.offset:      # truncated / replaced: start over
            self._reset()
        with open(self.path, "rb") as f:
            f.seek(self.offset)
            data = f.read()
        end = data.rfind(b"\n") + 1        # only whole lines; a half-written one is picked up next time
        self.offset += end
        for raw in data[:end].decode("utf-8", "replace").splitlines():
            self._absorb(raw)
        return {"lines": list(self.lines), "new": self._new, "counts": dict(self.counts), "ageS": round(time.time() - st.st_mtime, 1),
                "size": st.st_size, "seq": self.seq}

    def _absorb(self, raw):
        m = LINE.match(raw)
        t, text = (m.group(1), m.group(2)) if m else ("", raw)
        kind = classify(text)
        if kind == "play":
            self.counts["plays"] += 1
        elif kind == "turn":
            self.counts["turns"] += 1
        elif kind == "act":
            self.counts["acts"] += 1
        won = WON.match(text)
        if won:
            self.counts["wins"] = max(self.counts["wins"], int(won.group(1)))
        if text.startswith("run over"):
            self.counts["runs"] += 1
        self.seq += 1
        entry = {"n": self.seq, "t": t, "kind": kind, "text": text}
        self.lines.append(entry)
        self._new.append(entry)


def unit(u):
    return {"name": u.get("name"), "hp": u.get("hp"), "maxHp": u.get("maxHp"), "defend": u.get("defend"),
            "dead": u.get("dead"), "buffs": [{"name": b.get("name"), "level": b.get("level")} for b in u.get("buffs") or []]}


def compact_battle(b):
    if not b:
        return None
    p = b.get("player") or {}
    flow = b.get("flow") or {}
    return {
        "player": {**unit(p.get("unit") or {}), "power": p.get("powerCount"), "maxPower": p.get("maxPowerCount"), "piles": p.get("piles")},
        "enemies": [{**unit(e.get("unit") or {}), "index": e.get("index"), "alive": e.get("alive"),
                     "intents": [{"name": i.get("name"), "text": i.get("text")} for i in e.get("intents") or []]}
                    for e in b.get("enemies") or []],
        "hand": [{"name": c.get("name"), "cost": c.get("cost"), "type": c.get("type"), "playable": c.get("playable"), "text": c.get("text")}
                 for c in b.get("hand") or []],
        "select": b.get("select"),
        "flow": {"canAct": flow.get("canAct"), "fightType": flow.get("fightType"), "fake": flow.get("fake")},
    }


class Game:
    """Polls the bridge. `run` and `hub` only exist outside a fight, so the last seen values are kept (with their age)."""

    def __init__(self, port, timeout=1.5):
        self.url = f"http://127.0.0.1:{port}/state"
        self.timeout = timeout
        self.lock = threading.Lock()
        self.run = self.hub = None
        self.run_ts = self.hub_ts = None
        self.phases = collections.deque(maxlen=12)   # (time, phase) on every change
        self.started = time.time()

    def snapshot(self):
        try:
            with urllib.request.urlopen(self.url, timeout=self.timeout) as r:
                st = json.load(r)
        except Exception as e:                        # bridge down, game stopped, timeout
            LOG.debug("bridge: %s", e)
            return {"reachable": False}
        now = time.time()
        with self.lock:
            if st.get("run"):
                self.run, self.run_ts = st["run"], now
            if st.get("hub"):
                self.hub, self.hub_ts = st["hub"], now
            phase = st.get("phase")
            if not self.phases or self.phases[-1][1] != phase:
                self.phases.append((time.strftime("%H:%M:%S"), phase))
            return {
                "reachable": True, "phase": phase, "frame": st.get("frame"), "windows": st.get("windows"),
                "battle": compact_battle(st.get("battle")),
                "run": self.run, "runAt": self.run_ts,
                "hub": self.hub, "hubAt": self.hub_ts,
                "phases": list(self.phases),
            }


def read_tasks(path):
    """The operator's checklist: {"goal": str, "steps": [{"text", "state": done|doing|todo, "note"?}], "meta": {...}}."""
    if not path:
        return None
    try:
        return json.loads(Path(path).read_text(encoding="utf-8"))
    except FileNotFoundError:
        return None
    except (OSError, ValueError) as e:
        return {"error": str(e)}


def sse(event, data):
    """One Server-Sent Event frame (a single JSON line, so no data: continuation lines are needed)."""
    return f"event: {event}\ndata: {json.dumps(data, ensure_ascii=False)}\n\n".encode()


class Subscriber:
    def __init__(self):
        self.q = queue.Queue(maxsize=1000)
        self.dead = False


class Broadcaster:
    """Fan-out of events to the connected pages. A page that cannot keep up is dropped (its connection closes and the browser reconnects
    with a fresh snapshot) instead of slowing the poller down."""

    def __init__(self):
        self.lock = threading.Lock()
        self.subs = set()

    def subscribe(self):
        sub = Subscriber()
        with self.lock:
            self.subs.add(sub)
        return sub

    def unsubscribe(self, sub):
        with self.lock:
            self.subs.discard(sub)

    def count(self):
        with self.lock:
            return len(self.subs)

    def publish(self, event, data):
        frame = sse(event, data)
        with self.lock:
            subs = list(self.subs)
        for sub in subs:
            try:
                sub.q.put_nowait(frame)
            except queue.Full:
                sub.dead = True
                self.unsubscribe(sub)


class Dashboard:
    """Owns the sources (bridge, log, task file) and turns changes into events. `step()` is one poll cycle; a background thread runs it
    every `fast` seconds while a page is connected (5 a second by default; a state read costs the game's main thread ~10 ms) and every `idle` seconds otherwise.
    Fields that change on every look (frame number, ages) are left out of the change check, so an idle game pushes nothing."""

    VOLATILE = ("frame",)

    def __init__(self, game, tail, tasks_path, fast=0.2, idle=1.0, beat=2.0):
        self.game, self.tail, self.tasks_path = game, tail, tasks_path
        self.fast, self.idle, self.beat = fast, idle, beat
        self.bus = Broadcaster()
        self.lock = threading.RLock()
        self.stop = threading.Event()
        self._game_sig = self._tasks_sig = None
        self._last_beat = 0.0
        self.cache = {"game": {"reachable": False}, "bot": self.tail.poll(), "tasks": None}
        self.step()

    def status(self):
        with self.lock:
            return {"now": time.time(), "uptimeS": round(time.time() - self.game.started), **self.cache}

    def step(self):
        with self.lock:
            bot = self.tail.poll()
            if bot["new"]:
                self.bus.publish("log", {"lines": bot["new"], "counts": bot["counts"], "ageS": bot["ageS"], "seq": bot["seq"]})
            game = self.game.snapshot()
            sig = json.dumps({k: v for k, v in game.items() if k not in self.VOLATILE}, sort_keys=True, ensure_ascii=False)
            if sig != self._game_sig:
                self._game_sig = sig
                self.bus.publish("state", game)
            tasks = read_tasks(self.tasks_path)
            tsig = json.dumps(tasks, sort_keys=True, ensure_ascii=False)
            if tsig != self._tasks_sig:
                self._tasks_sig = tsig
                self.bus.publish("tasks", tasks)
            now = time.time()
            if now - self._last_beat >= self.beat:     # lets a quiet page see "the log has not moved for N s" and the clock tick
                self._last_beat = now
                self.bus.publish("beat", {"now": now, "uptimeS": round(now - self.game.started), "counts": bot["counts"], "ageS": bot["ageS"]})
            self.cache = {"game": game, "bot": {k: bot[k] for k in ("lines", "counts", "ageS", "size", "seq")}, "tasks": tasks}

    def run(self):
        while not self.stop.is_set():
            try:
                self.step()
            except Exception:                           # one bad poll must not end the stream
                LOG.exception("poll failed")
            self.stop.wait(self.fast if self.bus.count() else self.idle)

    def start(self):
        threading.Thread(target=self.run, name="dashboard-poll", daemon=True).start()


def make_handler(dash, html_path):
    class Handler(BaseHTTPRequestHandler):
        def log_message(self, fmt, *args):
            LOG.debug("%s " + fmt, self.address_string(), *args)

        def _send(self, code, body, ctype):
            self.send_response(code)
            self.send_header("Content-Type", ctype)
            self.send_header("Content-Length", str(len(body)))
            self.send_header("Cache-Control", "no-store")
            self.end_headers()
            self.wfile.write(body)

        def _stream(self):
            self.send_response(200)
            self.send_header("Content-Type", "text/event-stream; charset=utf-8")
            self.send_header("Cache-Control", "no-store")
            self.send_header("X-Accel-Buffering", "no")
            self.end_headers()
            sub = dash.bus.subscribe()          # subscribe first: nothing published during the snapshot is lost (the page drops repeats by line number)
            try:
                self.wfile.write(b"retry: 1000\n\n" + sse("snapshot", dash.status()))
                self.wfile.flush()
                while not sub.dead and not dash.stop.is_set():
                    try:
                        frame = sub.q.get(timeout=10)
                    except queue.Empty:
                        frame = b": keepalive\n\n"
                    self.wfile.write(frame)
                    self.wfile.flush()
            except (BrokenPipeError, ConnectionResetError, OSError):
                pass
            finally:
                dash.bus.unsubscribe(sub)

        def do_GET(self):
            path = self.path.split("?")[0]
            if path == "/api/stream":
                self._stream()
            elif path == "/api/status":
                dash.step()                      # the one-shot endpoint is always current
                self._send(200, json.dumps(dash.status(), ensure_ascii=False).encode(), "application/json; charset=utf-8")
            elif path in ("/", "/index.html"):
                self._send(200, Path(html_path).read_bytes(), "text/html; charset=utf-8")
            elif path == "/favicon.ico":         # browsers ask for it on every first visit
                self._send(204, b"", "image/x-icon")
            else:
                self._send(404, b"not found", "text/plain")

    return Handler


class DashboardServer(ThreadingHTTPServer):
    daemon_threads = True

    def server_close(self):
        self.dashboard.stop.set()
        super().server_close()


def serve(host, port, bridge_port, log_path, tasks_path, html_path=HERE / "dashboard.html", fast=0.2, idle=1.0):
    dash = Dashboard(Game(bridge_port), LogTail(log_path), tasks_path, fast=fast, idle=idle)
    srv = DashboardServer((host, port), make_handler(dash, html_path))
    srv.dashboard = dash
    dash.start()
    return srv


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--log", help="the bot's log file (greedy.py output)")
    ap.add_argument("--tasks", help="JSON checklist maintained by hand")
    ap.add_argument("--port", type=int, default=47801)
    ap.add_argument("--bridge-port", type=int, default=47800)
    ap.add_argument("--host", default="127.0.0.1", help="bind address (loopback by default)")
    ap.add_argument("--fast-ms", type=int, default=200, help="game poll interval while a page is connected (default 200: 5 updates a second)")
    ap.add_argument("-v", "--verbose", action="store_true")
    a = ap.parse_args()
    logging.basicConfig(level=logging.DEBUG if a.verbose else logging.INFO, format="%(asctime)s %(message)s")
    srv = serve(a.host, a.port, a.bridge_port, a.log, a.tasks, fast=a.fast_ms / 1000)
    LOG.info("dashboard on http://%s:%d  (bridge :%d, log %s, tasks %s)", a.host, a.port, a.bridge_port, a.log, a.tasks)
    try:
        srv.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
