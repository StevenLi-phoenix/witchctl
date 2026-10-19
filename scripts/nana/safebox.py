#!/usr/bin/env python3
"""In the rest room: open the safe box, move the hand-made card 厄运盛宴 into the run (right-click -> 移动; NEVER 丢弃, which destroys it), take out as much
gold as the rest point allows, close. Verifies the deck grew by one."""
import json
import re
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import make_card as mc  # noqa: E402

CARD = "厄运盛宴"


def state():
    code, out, err = mc.cli("state", "--json")
    return json.loads(out) if code == 0 else {}


def deck_counts():
    """(min, current, max) from the safe box's counter text, e.g. '12/15/23'."""
    for w in mc.windows():
        if w["name"] != "SafeBoxUI":
            continue
    code, out, err = mc.cli("ui")
    m = re.search(r"· (\d+)/(\d+)/(\d+)", out)
    return tuple(int(x) for x in m.groups()) if m else None


def money():
    code, out, err = mc.cli("ui")
    cur = re.search(r"当前金钱: (\d+)", out)
    return int(cur.group(1)) if cur else None


def main():
    st = state()
    if st.get("phase") != "rest":
        print("not in the rest room:", st.get("phase"))
        return 2
    mc.cli("act", "rest_safebox"); time.sleep(2)
    before = deck_counts()
    cards = [c for _, c in mc.items("SafeBoxUI") if CARD in c["label"]]
    if not cards:
        print("the card is not in the safe box")
        return 3
    mc.cli("click", "--right", "--", str(cards[0]["id"])); time.sleep(1)
    move = mc.find("移动", window="FloatingWindow")
    if not move:
        print("no 移动 entry in the card's menu")
        return 4
    mc.click(move[0]["id"]); time.sleep(1.5)
    after = deck_counts()
    print("deck (min/now/max):", before, "->", after)
    if not (before and after and after[1] == before[1] + 1):
        print("the card did not move into the deck")
        return 5
    start = money()
    for _ in range(12):
        code, out, err = mc.cli("click", "取出金钱")
        time.sleep(0.5)
        if state().get("phase") == "modal":
            mc.cli("act", "modal_confirm"); time.sleep(1.2)
            break
    print("gold:", start, "->", money())
    if any(w["name"] == "SafeBoxUI" for w in mc.windows()):
        exit_btn = [c for _, c in mc.items("SafeBoxUI") if c["label"] == "ExitButton"]
        if exit_btn:
            mc.click(exit_btn[0]["id"]); time.sleep(2)
    s = state()
    print("phase now:", s.get("phase"), "| run:", (s.get("run") or {}).get("inDeck"), "cards in the deck")
    return 0 if s.get("phase") == "rest" else 6


if __name__ == "__main__":
    sys.exit(main())
