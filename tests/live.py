"""Helpers for tests that talk to a real game: drive the CLI and walk a fresh lab profile around."""
import json
import os
import subprocess
import sys
import time

from tests.support import CLI

PORT = int(os.environ.get("WITCHCTL_PORT", "47800"))

# Scene-changing actions: the bridge refuses a repeat while the scene is still changing, so a walker fires each once.
SCENE_CHANGES = ("main_menu_start_game", "hub_start_game", "game_entry_start", "map_continue")
POPUP_CLOSERS = ("announcement_close", "item_reveal_confirm", "mode_choice_classic", "modal_confirm")   # what sits in the way after a finished run ("modal_confirm": classic mode asks to continue the saved run)


def cli(*args, check=False):
    """Run the CLI. Returns (exit_code, stdout, stderr, wall_ms)."""
    t0 = time.perf_counter()
    p = subprocess.run([sys.executable, "-I", str(CLI), "--port", str(PORT), *args], capture_output=True, text=True, timeout=60)
    ms = (time.perf_counter() - t0) * 1000
    if check and p.returncode != 0:
        raise AssertionError(f"witchctl {' '.join(args)} -> {p.returncode}: {p.stderr or p.stdout}")
    return p.returncode, p.stdout, p.stderr, ms


def state():
    return json.loads(cli("state", "--json", check=True)[1])


def action_ids(st):
    return [a["Id"] for a in st.get("actions", [])]


def bridge_up():
    try:
        return cli("ping")[0] == 0
    except Exception:
        return False


def percentile(xs, p):
    xs = sorted(xs)
    return xs[min(len(xs) - 1, int(len(xs) * p))]


def wait_state(predicate, timeout, what, step=0.2):
    """Poll `state` until predicate(state) holds. Raises with the last phase when it never does."""
    deadline = time.monotonic() + timeout
    st = None
    while time.monotonic() < deadline:
        st = state()
        if predicate(st):
            return st
        time.sleep(step)
    raise AssertionError(f"timed out after {timeout}s waiting for {what}; last phase: {st and st['phase']}")


def walk_until(done, what, timeout=150, log=print):
    """Walk menus, dialogue, the route map and rest rooms of a lab profile until `done(state)`. Returns that state."""
    deadline = time.monotonic() + timeout
    fired, previous = set(), None
    while time.monotonic() < deadline:
        st = state()
        phase, ids = st["phase"], action_ids(st)
        if phase != previous:
            fired.clear()
            previous = phase
            log(f"  phase={phase} actions={ids}")
        if done(st):
            return st
        todo = [a for a in SCENE_CHANGES + POPUP_CLOSERS + ("rest_leave",) if a in ids and a not in fired]
        place = [a for a in ids if a.startswith("map_place_")]
        if todo:
            cli("act", todo[0], check=True)
            fired.add(todo[0])
        elif "run_end_continue" in ids:  # the settlement screen needs two clicks
            cli("act", "run_end_continue")
            time.sleep(1.5)
        elif phase == "event" and ids:  # the first option, then the "leave the event" button that replaces the options
            cli("act", ids[0])
            time.sleep(1.0)
        elif place:  # every sixth map the route is built by hand: the first card into the first empty node
            cli("act", place[0])
        elif "tutorial_advance" in ids:  # first-run tips sit over any phase
            cli("act", "tutorial_advance")
        elif "dialogue_advance" in ids:
            cli("act", "dialogue_advance", check=True)
        elif phase == "game_entry":  # first launch: the language picker sits on top of the entry screen
            ui = json.loads(cli("ui", "--json", check=True)[1])
            lang = [c for w in ui["windows"] if w["name"] == "LanguageUseUI" for c in w["clickables"]]
            if lang and "language" not in fired:
                cli("click", "--", str(lang[0]["id"]), check=True)
                fired.add("language")
        time.sleep(0.4)
    raise AssertionError(f"no {what} within {timeout}s; last state:\n" + cli("state")[1])


def reach_battle(timeout=150, log=print):
    """Walk until a fight has dealt its hand. Returns the battle state."""
    return walk_until(lambda st: st["phase"] == "battle" and st["battle"].get("flow", {}).get("canAct") and st["battle"]["hand"],
                      "battle", timeout, log)


def reach_rest(timeout=60, log=print):
    """Walk until the rest room is up. Returns its state."""
    return walk_until(lambda st: st["phase"] == "rest", "rest room", timeout, log)
