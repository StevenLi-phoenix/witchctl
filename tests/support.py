"""Test helpers: run the CLI as a subprocess, and a fake bridge to run it against."""
import json
import subprocess
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

CLI = Path(__file__).resolve().parent.parent / "cli" / "witchctl"


def run_cli(*args, port):
    """Returns (exit_code, stdout, stderr)."""
    p = subprocess.run([sys.executable, "-I", str(CLI), "--port", str(port), *args], capture_output=True, text=True, timeout=30)
    return p.returncode, p.stdout, p.stderr


class FakeBridge:
    """Serves canned JSON. `routes` maps (METHOD, path) to a dict or a callable(body)->dict; requests are recorded."""

    def __init__(self, routes, port=0):
        self.routes, self.requests = routes, []
        outer = self

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *a):
                pass

            def _serve(self):
                n = int(self.headers.get("Content-Length") or 0)
                body = json.loads(self.rfile.read(n)) if n else None
                outer.requests.append((self.command, self.path, body))
                route = outer.routes.get((self.command, self.path)) or outer.routes.get((self.command, self.path.split("?")[0]))
                if route is None:
                    status, data = 404, {"error": "not_found", "message": self.path}
                else:
                    status, data = 200, route(body) if callable(route) else route
                raw = json.dumps(data, ensure_ascii=False).encode()
                self.send_response(status)
                self.send_header("Content-Length", str(len(raw)))
                self.send_header("X-Hop-Us", "3000")
                self.send_header("X-Exec-Us", "500")
                self.end_headers()
                self.wfile.write(raw)

            do_GET = do_POST = _serve

        self.server = ThreadingHTTPServer(("127.0.0.1", port), Handler)
        self.port = self.server.server_address[1]
        threading.Thread(target=self.server.serve_forever, daemon=True).start()

    def close(self):
        self.server.shutdown()
        self.server.server_close()


# Shape of a real battle snapshot (see `witchctl state --json`): text already resolved by the game, rich text stripped.
BATTLE_STATE = {
    "phase": "battle", "frame": 99, "scene": "game",
    "actions": [
        {"Id": "battle_end_turn", "Kind": "battle_end_turn", "Label": "结束回合", "Parameters": {}},
        {"Id": "battle_play_card_0", "Kind": "battle_play_card", "Label": "打击", "Parameters": {"cardIndex": 0, "requiresTarget": True}},
    ],
    "battle": {
        "player": {"unit": {"name": "魔女", "hp": 70, "maxHp": 80, "defend": 5, "dead": False, "state": "Default",
                            "buffs": [{"id": "buff_strength", "name": "力量", "level": 2}]},
                   "powerCount": 3, "maxPowerCount": 3, "totalCost": 0, "piles": {"draw": 4, "discard": 2}},
        "enemies": [
            {"index": 0, "id": "e1", "alive": True, "unit": {"name": "史莱姆", "hp": 12, "maxHp": 20, "defend": 0, "dead": False, "state": "Default", "buffs": []},
             "intents": [{"id": "slime_hit", "name": "黏黏冲撞", "text": "造成8点伤害。", "cooldown": 0}]},
            {"index": 1, "id": "e2", "alive": False, "unit": {"name": "蝙蝠", "hp": 0, "maxHp": 9, "defend": 0, "dead": True, "state": "Dead", "buffs": []},
             "intents": []},
        ],
        "hand": [
            {"index": 0, "uid": "uid-strike", "id": "strike", "name": "打击", "kind": "AttackCardItem", "type": "攻击牌", "rarity": "1", "cost": 1,
             "tags": [], "playable": True, "text": "造成5点伤害。"},
            {"index": 1, "uid": "uid-block", "id": "block", "name": "防御", "kind": "CommonCardItem", "type": "消耗技能牌", "rarity": "1", "cost": 3,
             "tags": ["Burnout"], "playable": False, "reason": "not enough power (3 > 2)", "text": "获得7点护盾。\n抽1张牌"},
        ],
        "flow": {"animating": False, "fightType": "Player", "canAct": True, "canUseCards": True, "busy": False, "fake": False},
    },
}

REWARDS_STATE = {
    "phase": "rewards", "frame": 120, "scene": "game",
    "actions": [
        {"Id": "reward_claim_1", "Kind": "reward_claim", "Label": "卡牌 — 选择一张卡牌"},
        {"Id": "reward_close", "Kind": "reward_close", "Label": "关闭"},
    ],
    "rewards": [
        {"index": 0, "kind": "Money", "title": "金钱", "text": "64", "claimable": False},
        {"index": 1, "kind": "Bless", "title": "卡牌", "text": "选择一张卡牌", "claimable": True},
    ],
}

CARD_CHOICE_STATE = {
    "phase": "card_choice", "frame": 130, "scene": "game",
    "actions": [{"Id": "card_choice_pick_0", "Kind": "card_choice_pick", "Label": "火球", "Parameters": {"cardId": "fireball"}},
                {"Id": "card_choice_skip", "Kind": "card_choice_skip", "Label": "跳过"}],
    "cardChoice": [
        {"index": 0, "id": "fireball", "name": "火球", "cost": "2", "rarity": "2", "text": "造成12点伤害。", "pickable": True},
        {"index": 1, "id": "ward", "name": "结界", "cost": "1", "rarity": "1", "text": "获得6点护盾。", "pickable": False},
    ],
}
