#!/usr/bin/env python3
"""Craft the hand-made card 厄运盛宴 from the hub (the lab game): 10 negative buffs x999 on yourself, cost 0, rarity 1 (50 truth). Verifies the lab profile
afterwards (storage is `CardData` in SaveData.sav). `--copies N` (default 1) crafts until N cards are in storage (50 truth each); a no-op when that many are there.
Run with the game in the hub, no page open.

Why this card: every negative buff refunds card points (capped at -100 in total) and the layer count is free, so it costs nothing in the editor; 奈奈's
skill 可食用厄运 eats them all for doom power (10 per kind, permanent max HP), see CLAUDE.md trap 38. A run uses the card up: craft a new one per run."""
import json
import os
import subprocess
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import make_card as mc  # noqa: E402

NEGATIVE = ["毒素", "流血", "易伤", "灼烧", "失能", "遗忘", "腐朽", "混乱", "无力", "失格"]
NAME = "厄运盛宴"


def act(action):
    code, out, err = mc.cli("act", action)
    return code == 0


def phase():
    code, out, err = mc.cli("state", "--json")
    d = json.loads(out) if code == 0 else {}
    return d.get("phase"), d.get("hub")


def card_data_count():
    # WITCHCTL_PROFILE = the SaveData.sav to check (the real game's, when run.sh drives the Steam copy); default: the lab's
    base = Path(os.environ.get("WITCHCTL_PROFILE") or Path.home() / "Library/Application Support/com.MeowAlive.Witch-s-Apocalyptic-Journey.lab/SaveData/SaveData.sav")
    key = subprocess.run("printf 'yuanshenqidongyuanshenqidong1111' | xxd -p -c 64", shell=True, capture_output=True, text=True).stdout.strip()
    iv = subprocess.run("printf 'yuanshenqidongle' | xxd -p -c 32", shell=True, capture_output=True, text=True).stdout.strip()
    out = subprocess.run(["openssl", "enc", "-d", "-aes-256-cbc", "-K", key, "-iv", iv, "-in", str(base)], capture_output=True).stdout
    d = json.loads(out)
    return len(d.get("CardData") or []), d.get("Truth")


def craft_one():
    p, hub = phase()
    if p != "hub":
        print("not in the hub:", p)
        return 2
    act("hub_visit_store"); time.sleep(1.5)
    act("hub_page_item_0"); time.sleep(2.5)
    if not mc.find("添加词条", window="CardEditorUI"):
        print("the card editor did not open")
        return 3
    for buff in NEGATIVE:
        icon, level, name = mc.add_buff_row()
        ok_level = mc.settext(level, mc.LEVEL)
        time.sleep(0.4)
        ok_pick = mc.pick(icon, buff)
        time.sleep(0.4)
        now = {c["id"]: c["label"] for _, c in mc.items("CardEditorUI") if c["path"].endswith("buffInput")}
        print(f"  {buff}: level={ok_level} picked={ok_pick} -> '{now.get(name)}'")
        if not (ok_level and ok_pick and now.get(name) == buff):
            print("effect row failed; leaving the editor without creating anything")
            return 4
    cost0 = mc.find("0", window="CardEditorUI")
    zero = [c for c in cost0 if "powerContent" in c["path"]]
    mc.click(zero[0]["id"]); time.sleep(0.6)
    field = [c for c in mc.find(window="CardEditorUI") if c["path"].endswith("nameContent/InputField")]
    mc.settext(field[0]["id"], NAME); time.sleep(0.8)
    preview = [c["label"] for _, c in mc.items("CardEditorUI") if c["path"].endswith("ModalCard")]
    print("preview:", (preview[0] if preview else "")[:120].replace("\n", " "))
    ok = mc.find("确认编辑", window="CardEditorUI")
    mc.click(ok[0]["id"]); time.sleep(2)
    act("hub_page_close"); time.sleep(1)
    return 0


def main():
    copies = int(sys.argv[sys.argv.index("--copies") + 1]) if "--copies" in sys.argv else 1
    before, truth = card_data_count()
    print(f"cards in the profile before: {before}, truth {truth}")
    if before >= copies:
        print(f"{before} hand-made card(s) already in storage; nothing to do")
        return 0
    while True:
        have, truth = card_data_count()
        if have >= copies:
            break
        if truth is not None and truth < 50:
            print("not enough truth for another card")
            return 6
        rc = craft_one()
        if rc:
            return rc
        now, truth2 = card_data_count()
        print(f"cards in the profile: {now}, truth {truth2}")
        if now <= have:
            print("the card was not created")
            return 5
    return 0


if __name__ == "__main__":
    sys.exit(main())
