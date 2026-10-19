#!/usr/bin/env python3
"""Fill the open card editor with 'add N layers of <negative buff> to yourself' effects (refunds points, capped at -100 in total).
Run with the editor open and the first buff (毒素) already added. Never presses 确认编辑."""
import json
import subprocess
import sys
import time
from pathlib import Path

CLI = Path(__file__).resolve().parents[2] / "cli" / "witchctl"
LEVEL = "999"


def cli(*args):
    p = subprocess.run([sys.executable, "-I", str(CLI), *args], capture_output=True, text=True, timeout=60)
    return p.returncode, p.stdout, p.stderr


def windows():
    code, out, err = cli("ui", "--json")
    return json.loads(out)["windows"] if code == 0 else []


def items(window=None):
    for w in windows():
        if window and w["name"] != window:
            continue
        for c in w["clickables"]:
            yield w["name"], c


def click(cid):
    code, out, err = cli("click", "--", str(cid))
    return code == 0


def settext(cid, text):
    body = {"Action": "set_text", "Selector": {"InstanceId": int(cid)}, "Text": text, "Submit": True}
    code, out, err = cli("raw", "POST", "/ui/do", json.dumps(body))
    return '"Success": true' in out or '"Success":true' in out


def find(label=None, path_part=None, window=None, startswith=None):
    hits = []
    for w, c in items(window):
        if label is not None and c["label"] != label:
            continue
        if startswith is not None and not c["label"].startswith(startswith):
            continue
        if path_part is not None and path_part not in c["path"]:
            continue
        hits.append(c)
    return hits


def rows():
    """The effect rows' (icon id, level-input id, buff-name field id) in list order."""
    icons = [c["id"] for _, c in items("CardEditorUI") if c["path"].endswith("Item1(Clone)/Content/Icon")]
    levels = [c for _, c in items("CardEditorUI") if c["path"].endswith("InputField(Clone)")]
    names = [c for _, c in items("CardEditorUI") if c["path"].endswith("buffInput")]
    return icons, levels, names


def add_buff_row():
    before = set(rows()[0])
    assert find("添加词条", window="CardEditorUI"), "editor not open"
    click(find("添加词条", window="CardEditorUI")[0]["id"]); time.sleep(0.6)
    timing = find(startswith="使用时", window="FloatingWindow") or find("使用时", window="FloatingWindow")
    assert timing, "no 使用时 entry in the floating menu"
    click(timing[0]["id"]); time.sleep(0.6)
    entry = find("添加一个buff", window="FloatingWindow")
    assert entry, "no 添加一个buff entry"
    click(entry[0]["id"]); time.sleep(0.9)
    icons, levels, names = rows()
    new = [i for i in icons if i not in before]
    assert len(new) == 1, f"expected one new row, got {new}"
    idx = icons.index(new[0])
    return icons[idx], levels[idx]["id"], names[idx]["id"]


def pick(icon, buff):
    click(icon); time.sleep(0.7)
    picker = [c for _, c in items("CardEditorUI") if c["label"] == buff and "buffInput" not in c["path"] and "InputField" not in c["path"]]
    if not picker:   # the picker is paged: look at page 2
        page2 = find("2", window="CardEditorUI")
        if page2:
            click(page2[-1]["id"]); time.sleep(0.7)
        picker = [c for _, c in items("CardEditorUI") if c["label"] == buff and "buffInput" not in c["path"] and "InputField" not in c["path"]]
    if not picker:
        return False
    click(picker[0]["id"]); time.sleep(0.7)
    return True


def main():
    buffs = sys.argv[1:] or ["流血", "易伤", "灼烧", "失能", "遗忘", "腐朽", "混乱", "无力", "失格"]
    for buff in buffs:
        icon, level, name = add_buff_row()
        ok_level = settext(level, LEVEL)
        time.sleep(0.4)
        ok_pick = pick(icon, buff)
        time.sleep(0.4)
        now = {c["id"]: c["label"] for _, c in items("CardEditorUI") if c["path"].endswith("buffInput")}
        print(f"{buff}: level_set={ok_level} picked={ok_pick} -> row buff '{now.get(name)}'")
    texts = [c["label"] for _, c in items("CardEditorUI") if c["path"].endswith("Cost/cost")]
    code, out, err = cli("ui")
    for line in out.splitlines():
        if "点数占用" in line or line.strip().startswith(("· -", "· 0", "· 110", "· 50/")):
            print(line.strip())


if __name__ == "__main__":
    main()
