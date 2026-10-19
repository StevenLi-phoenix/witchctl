#!/usr/bin/env python3
"""A simple agent: plays whatever phase the game is in, using only the `witchctl` CLI.

It exists to show the loop an agent runs (read state, pick an action id, act) and to smoke-test the bridge
end to end: menu, dialogue, fights, rewards, card choice, map. The policies are plain heuristics, not search:
fights play free draws and kills first, block when the announced (or last turn's) damage would leave us under half health, and
attack with the best damage per energy otherwise; events and the route lean towards risk and reward (agents that always play it
safe score worst); cards are picked by rarity. One life per run, no save/load tricks.

    examples/greedy.py --fights 2          # stop after winning two fights
    examples/greedy.py --seconds 600
    examples/greedy.py --runs 1 --route rest   # one whole run, keeping the rest room for the last frame when the route is built
"""
import argparse
import json
import re
import subprocess
import sys
import time
from pathlib import Path

AFTER_WIN = ("battle_won", "rewards", "card_choice", "blessing_choice")
OUT_OF_FIGHT = ("map_select", "rewards", "card_choice", "blessing_choice", "rest", "event", "hub", "main_menu", "run_end", "shop", "game_entry")
# What a card or an enemy move says in words (the game's text has the live numbers in it on the player's turn).
DAMAGE, HITS = re.compile(r"造成(\d+)点伤害"), re.compile(r"造成(\d+)\*(\d+)伤害")
BLOCK, HEAL, DRAW = re.compile(r"获得(\d+)点护盾"), re.compile(r"(?:恢复|回复)(\d+)点生命"), re.compile(r"抽(\d+)张")
MAX_HP_PCT, LOST_HP_PCT = re.compile(r"最大生命值(\d+)%的伤害"), re.compile(r"已损失生命(\d+)%的伤害")
CLI = Path(__file__).resolve().parent.parent / "cli" / "witchctl"


def cli(*args, port):
    p = subprocess.run([sys.executable, "-I", str(CLI), "--port", str(port), *args], capture_output=True, text=True, timeout=60)
    return p.returncode, p.stdout, p.stderr


def log(*parts):
    print(time.strftime("%H:%M:%S"), *parts, flush=True)


def select_choice(actions):
    """Next step of a card-selection prompt (discard / burn / "choose N cards"): confirm as soon as the game offers it (a mandatory
    prompt offers it only once enough cards are chosen; an optional one at once, then choosing nothing costs nothing), else
    choose the first card not yet chosen. None when nothing can be done yet."""
    ids = [a["Id"] for a in actions]
    if "battle_select_confirm" in ids:
        return "battle_select_confirm"
    for a in actions:
        if a["Id"].startswith("battle_select_card_") and not (a.get("Parameters") or {}).get("chosen"):
            return a["Id"]
    return None


CONTINUE_WORDS = ("继续", "Continue")
AMBIENT_WINDOWS = ("ChatUI", "CaptionUI", "TopBarUI", "LanguageUseUI")


MANUAL_SKILLS = ("careercard_2",)   # 奈奈's 可食用厄运 is played together with its card (see eat_plan), never by the generic "use any ready skill" rule; her 灾祸具现化 (the
                                    # transformation: temporary strength and perception from doom power, 2% of max HP to every enemy on each action) is an ordinary ready skill


def hold_transform(battle, turn):
    """奈奈 should not transform at once: it ends the fight in a turn, before 厄运盛宴 is drawn and eaten (the doom power that becomes max HP and strength). Hold it
    while the eat is still to come (the skill is ready), for the first two turns, and only while health is not in danger (under 40 % it goes ahead)."""
    chosen = next((x for x in battle.get("skills") or [] if x.get("id") == EAT_SKILL), None)
    unit = battle["player"]["unit"]
    safe = unit["hp"] >= 0.4 * (unit.get("maxHp") or unit["hp"])
    return bool(chosen and chosen.get("usable") and turn < 2 and safe)


def skill_choice(actions, enemies, tried, hold=()):
    """A career skill to use now, or None. Skills whose id is in `tried` are left alone (the caller clears it now and then); an aimed
    skill goes at the weakest living enemy, never at the player."""
    weakest = min((e for e in enemies if e.get("alive")), key=lambda e: e["unit"]["hp"], default=None)
    for a in actions:
        if a.get("Kind") != "skill_use" or (a.get("Parameters") or {}).get("skillId") in tried or (a.get("Parameters") or {}).get("skillId") in MANUAL_SKILLS + tuple(hold):
            continue
        if not (a.get("Parameters") or {}).get("aimed"):
            return a["Id"]
        if weakest is not None and a["Id"].endswith(f"_enemy_{weakest['index']}"):
            return a["Id"]
    return None


def number(pattern, text):
    m = pattern.search(text or "")
    return int(m.group(1)) if m else 0


def card_effects(card, unit=None):
    """The numbers a card's text promises: damage, block, heal, cards drawn (0 when the text names none). With the player's unit (`hp`, `maxHp`) the damage
    that scales with health is worked out too: "x% of max HP" and "x% of the health lost so far" (复仇短匕, 复仇链枪: with ten thousand max HP they are the best attacks)."""
    text = card.get("text") or ""
    damage = number(DAMAGE, text)
    if unit and unit.get("maxHp"):
        damage += number(MAX_HP_PCT, text) * unit["maxHp"] // 100 + number(LOST_HP_PCT, text) * max(0, unit["maxHp"] - unit["hp"]) // 100
    return {"damage": damage, "block": number(BLOCK, text), "heal": number(HEAL, text), "draw": number(DRAW, text)}


FEAST_CARD, EAT_SKILL, LOADED_BUFF = "厄运盛宴", "careercard_2", "毒素"


def eat_plan(battle, actions):
    """奈奈's doom-power loop: the hand-made card 厄运盛宴 puts 999 layers of every negative buff on her (a refund of card points, no energy), and her skill
    可食用厄运 eats them all for doom power (permanent max HP, up to 10 per kind). The pair is dangerous apart: ("skill", action id) when the buffs are on
    (eat them now), ("card", uid, name) when the skill is ready and the card is in hand, else None. The card is never played while the skill cools down."""
    chosen = next((x for x in battle.get("skills") or [] if x.get("id") == EAT_SKILL), None)
    if not chosen:
        return None
    on_self = f"skill_use_{chosen['index']}_self"
    if not chosen.get("usable") or not any(a["Id"] == on_self for a in actions):
        return None
    if any(b.get("name") == LOADED_BUFF and (b.get("level") or 0) >= 100 for b in battle["player"]["unit"].get("buffs") or []):
        return ("skill", on_self)
    held = next((c for c in battle["hand"] if c["name"] == FEAST_CARD and c["playable"]), None)
    return ("card", held["uid"], held["name"]) if held else None


def incoming_damage(enemies):
    """What the living enemies are about to deal, from their announced moves: each uses one move (the worst that is ready or one turn away,
    `cooldown` <= 1), the enemies add up. Only the words of the move are read ("造成8点伤害" / "造成2*4伤害"), so strength and weakness are
    not in it: `TurnMemory` covers that."""
    total = 0
    for e in enemies:
        if not e.get("alive"):
            continue
        worst = 0
        for move in e.get("intents") or []:
            if (move.get("cooldown") or 0) > 1:
                continue
            text = move.get("text") or ""
            hits = HITS.search(text)
            worst = max(worst, int(hits.group(1)) * int(hits.group(2)) if hits else number(DAMAGE, text))
        total += worst
    return total


def rebirth_level(unit):
    """Layers of the rebirth buff (重生) on a unit: 100 of them are one life, at least 30 are needed to come back."""
    return next((b.get("level") or 0 for b in unit.get("buffs") or [] if b.get("name") == "重生"), 0)


class TurnMemory:
    """What the enemies' last turn cost, to size this turn's block when the announced moves do not show their real damage. `ended` is called
    when we end our turn, `expected_loss` on the next one: hp lost plus the block that was up (it was used up, or the loss would be 0);
    a turn that cost nothing decays the memory (x0.7) instead of erasing it."""

    def __init__(self):
        self.after = None
        self.loss = 0
        self.turn = 0          # our turns ended in this fight

    def ended(self, unit):
        self.after = (unit["hp"], unit.get("defend") or 0, rebirth_level(unit))
        self.turn += 1

    def revived(self, unit):
        """True when the rebirth buff we had at the end of our last turn has been (partly) spent and we are back on our feet: the enemies killed us and the buff
        brought us back (rebirth costs 100 layers per life). More maximum health heals too, but leaves the buff alone. Call before `expected_loss`."""
        return self.after is not None and rebirth_level(unit) < self.after[2] and unit["hp"] > self.after[0]

    def expected_loss(self, unit):
        if self.after is not None:
            hp, defend = self.after[0], self.after[1]
            lost = hp - unit["hp"]
            self.loss = lost + defend if lost > 0 else round(self.loss * 0.7)
            self.after = None
        return self.loss


def pick_card(battle, expected_loss=0):
    """(uid, target index, name) of the card to play next, or None to end the turn.

    Order: free draws first (see more cards), a card that kills, block when the coming hit would leave us under half health (the biggest block per
    energy; `expected_loss` is the last turn's cost, used when the announced moves show less), healing when low, buffs, attacks by damage per
    energy, then block with the energy that is left. Attacks go at the enemy they kill, else the weakest."""
    enemies = [e for e in battle["enemies"] if e["alive"]]
    playable = [c for c in battle["hand"] if c["playable"] and c["name"] != FEAST_CARD]   # that card is only ever played by eat_plan, next to its skill
    if not enemies or not playable:
        return None
    unit = battle["player"]["unit"]
    hp, max_hp, defend = unit["hp"], unit.get("maxHp") or unit["hp"], unit.get("defend") or 0
    need = max(0, max(incoming_damage(enemies), expected_loss) - defend)
    urgent = need > 0 and hp - need < max_hp * 0.5
    low = hp < max_hp * 0.4
    weakest_first = sorted(enemies, key=lambda e: e["unit"]["hp"])

    def victim(damage):
        return next((e for e in weakest_first if damage >= e["unit"]["hp"] + (e["unit"].get("defend") or 0)), None)

    def score(card):
        fx, cost = card_effects(card, unit), max(card["cost"], 0.5)
        attack = fx["damage"] > 0 or "攻击" in (card.get("type") or "")
        if fx["draw"] and card["cost"] <= 1:
            return 100
        if attack and victim(fx["damage"]):
            return 90
        if urgent and fx["block"]:
            return 80 + min(fx["block"], need) / cost
        if low and fx["heal"]:
            return 70 + fx["heal"] / cost
        if not (attack or fx["block"] or fx["heal"] or fx["draw"]):
            return 60
        if attack:
            return 50 + fx["damage"] / cost
        if fx["block"]:
            return 20 + fx["block"] / cost
        return 10

    best = max(playable, key=score)
    aim = victim(card_effects(best, unit)["damage"]) or weakest_first[0]
    return best["uid"], aim["index"], best["name"]


def card_choice(ids, cards=None, prefer=()):
    """Pick the best offered card: a name in `prefer` first (earlier = better), then higher rarity, then more damage / block / heal / draw in its text
    (`cards` is the state's `cardChoice`).
    Without details, the first one. Skip only when the mod offers skip but no pick (backpack full); otherwise None = still fading in."""
    pick = [i for i in ids if i.startswith("card_choice_pick_")]
    if pick and cards:
        offered = {c.get("index"): c for c in cards if c.get("pickable", True)}

        def worth(pick_id):
            c = offered[int(pick_id.rsplit("_", 1)[1])]
            rank = len(prefer) - prefer.index(c.get("name")) if c.get("name") in prefer else 0
            return (rank, int(c["rarity"]) if str(c.get("rarity")).isdigit() else 0, sum(card_effects(c).values()))
        known = [i for i in pick if int(i.rsplit("_", 1)[1]) in offered]
        if known:
            return max(known, key=worth)
    if pick:
        return pick[0]
    return "card_choice_skip" if "card_choice_skip" in ids else None


EVENT_GAINS = (("遗物", 4), ("祝福", 2), ("最大生命", 2), ("属性", 1.5), ("金币", 1.5), ("金钱", 1.5), ("卡牌", 1), ("升级", 1), ("战斗", 1))
EVENT_COSTS = (("死亡", -5), ("无事发生", -3), ("诅咒", -2), ("失去", -1.5), ("扣除", -1.5), ("受到", -1))
EVENT_GAMBLE = ("概率", "随机", "%")


def event_value(option):
    """How good an event option reads: what it gives minus what it costs, plus a bonus for a gamble that has a prize. The effect text is the
    game's own ("获得遗物…"); with none, the label. Agents that always play it safe are the weak ones, so a coin flip for a relic beats 30 gold."""
    text = option.get("effect") or option.get("label") or ""
    gain = sum(w for word, w in EVENT_GAINS if word in text)
    cost = sum(w for word, w in EVENT_COSTS if word in text)
    gamble = 1.5 if gain > 0 and any(word in text for word in EVENT_GAMBLE) else 0
    return gain + cost + gamble


def event_choice(st):
    """The `event_option_N` to press: the best interactable option, the first on a tie. A resolved event has a single button (the way out)."""
    ids = [a["Id"] for a in st.get("actions", []) if a["Id"].startswith("event_option_")]
    event = st.get("event") or {}
    options = [o for o in event.get("options") or [] if o.get("interactable", True)]
    if event.get("resolved") or not options:
        return ids[0] if ids else None
    best = max(options, key=event_value)
    wanted = f"event_option_{best['index']}"
    return wanted if wanted in ids else ids[0] if ids else None


def wants_deck(st, cap=None):
    """Rewards and purchases land in the backpack and a fight reads only the deck: open the deck window when spare cards wait and the
    deck has room (the game also refuses further card rewards once the backpack is full). `cap` keeps the deck smaller than the game allows."""
    run = st.get("run") or {}
    return (any(a["Id"] == "deck_open" for a in st.get("actions", [])) and run.get("backpack", 0) > 0
            and run.get("inDeck", 0) < min(run.get("maximum", 0), cap or run.get("maximum", 0)))


def deck_choice(st, cap=None):
    """In the deck window: move spare cards into the deck while it has room (and is under `cap`), then close. Selling and destroying are never done."""
    ids = [a["Id"] for a in st.get("actions", [])]
    equip = [i for i in ids if i.startswith("deck_equip_")]
    full = cap is not None and (st.get("run") or {}).get("inDeck", 0) >= cap
    return equip[0] if equip and not full else "deck_close"


SHOP_KINDS = ("Relic", "Bless", "Card")   # in this order; enchantments and divination need a picker, left alone


def shop_choice(actions):
    """What to do in a shop window: buy the best thing on offer that the mod says is affordable (relics, then blessings, then cards;
    cheaper first), leave when nothing is. The mod only offers purchases that can work."""
    buys = [a for a in actions if a["Id"].startswith("shop_buy_") and (a.get("Parameters") or {}).get("kind") in SHOP_KINDS
            and not (a.get("Parameters") or {}).get("warn")]   # warn: buyable but pointless (relic slots full)
    if buys:
        return min(buys, key=lambda a: (SHOP_KINDS.index(a["Parameters"]["kind"]), a["Parameters"].get("price", 0)))["Id"]
    return "shop_leave" if any(a["Id"] == "shop_leave" for a in actions) else None


def picker_choice(actions):
    """In the card picker: choose the first card not yet chosen (the window closes itself after the last one); cancel if none."""
    for a in actions:
        if a["Id"].startswith("deck_pick_") and not (a.get("Parameters") or {}).get("chosen"):
            return a["Id"]
    return "deck_select_cancel" if any(a["Id"] == "deck_select_cancel" for a in actions) else None


def recon_key(st):
    """What makes two screens 'the same' for --recon: the phase and the open windows (the always-present ones left out)."""
    return st.get("phase"), tuple(sorted(w for w in st.get("windows") or [] if w not in AMBIENT_WINDOWS))


def modal_choice(actions, allow_end_run=False):
    """Which button of the game's yes/no window to press, or None. A saved run is resumed ("Continue Game" is the confirm button
    there; "New Game" would delete it). Any other question is declined: it is more likely "quit?" or "delete?" than something
    a bot should agree to. `allow_end_run` (slot mode only) answers "end this run?" with yes: leaving is what that mode does."""
    labels = {a["Id"]: a.get("Label", "") for a in actions}
    if allow_end_run and "modal_confirm_end_run" in labels:
        return "modal_confirm_end_run"
    # The mod puts the effect into the id of an irreversible button (modal_cancel_new_game_deletes_save, modal_confirm_quit_game):
    # the plain ids are the harmless ones, and the bot only ever sends those.
    if "modal_confirm" in labels and (any(word in labels["modal_confirm"] for word in CONTINUE_WORDS)
                                      or "modal_cancel_new_game_deletes_save" in labels):
        return "modal_confirm"
    return "modal_cancel" if "modal_cancel" in labels else None


def entry_pref(st, partner, packs, tried=(), career=None):
    """The next pre-run setup click, or None: the wanted career (`entry_career_N`, matched by label), the wanted partner (`entry_partner_N`), then each
    wanted card pack that is off (`entry_pack_on_<id>`). A chosen partner or pack is no longer offered, so asking again after a click returns the next one. Ids in `tried`
    are skipped (the caller counts attempts: a click the game refuses must not be repeated for ever)."""
    acts = st.get("actions", [])

    def find(prefix, name):
        return next((a["Id"] for a in acts if a["Id"].startswith(prefix) and name in (a.get("Label") or "") and a["Id"] not in tried), None)
    if career:
        hit = find("entry_career_", career)
        if hit:
            return hit
    if partner:
        hit = find("entry_partner_", partner)
        if hit:
            return hit
    for name in packs:
        hit = find("entry_pack_on_", name)
        if hit:
            return hit
    return None


def buff_text(unit):
    """A unit's buffs as `name×level` (the rebirth buff shows whether a death is still covered)."""
    return " ".join(f"{b.get('name')}×{b.get('level')}" for b in unit.get("buffs") or []) or "-"


def fight_start_line(battle):
    """One log line for the start of a fight: our health and block, the damage the announced moves add up to, and every enemy with its moves
    `name text(cooldown)`: what a boss really does is read from these lines afterwards."""
    unit = battle["player"]["unit"]
    foes = "; ".join(f"{e['unit']['name']} {e['unit']['hp']} [" + ", ".join(f"{m.get('name')} {m.get('text')}({m.get('cooldown')})" for m in e.get("intents") or []) + "]"
                     for e in battle["enemies"] if e.get("alive"))
    return (f"fight start: hp {unit['hp']}/{unit.get('maxHp')} block {unit.get('defend')} buffs {buff_text(unit)}, "
            f"incoming {incoming_damage(battle['enemies'])}, enemies {foes}")


def should_pause(phase, pause, windows=()):
    """True when the bot should stop and hand over: `--pause` lists phases (`rest`, `shop`, `card_choice`, ...) and/or window names (`SafeBoxUI`)."""
    return phase in pause or any(w in pause for w in windows)


def mode_pick(ids, mode="classic"):
    """The mode-screen button: the asked-for mode (`classic` or `slot`), else the first playable one offered (slot needs player level 25),
    else the way back. Story mode has no handler in the game and is never taken."""
    want = f"mode_choice_{mode}"
    if want in ids:
        return want
    playable = [i for i in ids if i.startswith("mode_choice_") and i not in ("mode_choice_back", "mode_choice_story")]
    return playable[0] if playable else "mode_choice_back"


def slot_action(st):
    """The way out of Chaos Paradise (`slot_end_run`), or None when the screen offers nothing yet."""
    return "slot_end_run" if any(a["Id"] == "slot_end_run" for a in st.get("actions", [])) else None


def run_end_line(st):
    """One log line for the settlement screen: won or lost, the points total and what it is made of (relics, gold, attributes, ...).
    None until the breakdown is shown (the first click on the screen shows it)."""
    end = st.get("runEnd") or {}
    if not end.get("detailsShown"):
        return None
    parts = ", ".join(f"{k} {v}" for k, v in (end.get("score") or {}).items())
    return f"run end: {'WON' if end.get('won') else 'lost'}, total {end.get('total')} ({parts})"


def run_over(previous, phase):
    """True on the first look after the settlement screen (`run_end`) is gone: the run is counted once, however many clicks the screen took."""
    return previous == "run_end" and phase != "run_end"


def route_pick(st, policy):
    """The `map_place_N` id to press next while the player builds the route, or None for "no opinion" (the caller then takes the first offered card).

    The tray holds 8 cards for 4 frames, and the game always appends the rest room ("Breaks") as the last card (`NormalMapManager`), so taking the
    first card four times (policy "first", the original behaviour) can never reach it. Policy "rest" takes the cards in tray order but keeps the rest
    room for the last frame, right before the fixed end boss. Needs `mapNodes` to know which frame is the last one."""
    tray = st.get("mapChoices") or []
    if not tray:
        return None
    if policy not in ("rest", "bold", "rest-first"):
        return f"map_place_{tray[0]['index']}"
    nodes = st.get("mapNodes")
    if not nodes:
        return None
    empty = sum(1 for n in nodes if n.get("empty"))
    breaks = [c for c in tray if c.get("id") == "Breaks"]
    others = [c for c in tray if c.get("id") != "Breaks"]
    if policy == "rest-first" and breaks and empty >= len(nodes):   # the safe box (cards from the profile) is only in the rest room: get there first
        pick = breaks[0]
    elif policy in ("bold", "rest-first") and others:
        pick = bold_card(others, breaks, empty)
    else:
        pick = breaks[0] if breaks and (empty <= 1 or not others) else (others or breaks)[0]
    return f"map_place_{pick['index']}"


def fight_difficulty(card):
    """How hard a fight card is, from its label "name attack health tags": health plus ten times the attack."""
    nums = [int(n) for n in re.findall(r"\d+", card.get("label") or "")][:2] + [0, 0]
    return nums[1] + 10 * nums[0]


def bold_card(others, breaks, empty):
    """The risk-taking route: the two hardest fights first (elites pay in relics, gold and score), then the shop, the rest room last. Frames are
    filled in order (4, 3, 2, 1 empty); whatever is missing falls through to the next kind, the rest room only when nothing else is left."""
    fights = sorted((c for c in others if c.get("kind") == "Fight"), key=fight_difficulty, reverse=True)
    shops = [c for c in others if c.get("id") == "shop"]
    events = [c for c in others if c.get("kind") == "Event"]
    rest = [c for c in others if c not in fights and c not in shops and c not in events]
    order = {2: (shops, fights, events), 1: (breaks, shops, events, fights)}.get(empty, (fights, shops, events))
    for group in (*order, rest, breaks):
        if group:
            return group[0]
    return others[0]


class StuckWatch:
    """Says when the screen has not changed for `limit` seconds. The clock is `time.monotonic`, which stands still while the Mac sleeps: with the
    wall clock a closed lid (the game frozen for minutes) read as "stuck for 90 s" on the first look after the wake."""

    def __init__(self, limit=90.0, clock=time.monotonic):
        self.limit, self.clock = limit, clock
        self.sig, self.since = None, clock()

    def stuck(self, sig):
        if sig != self.sig:
            self.sig, self.since = sig, self.clock()
            return False
        return self.clock() - self.since > self.limit


class Player:
    def __init__(self, port, route="bold", mode="classic", partner=None, packs=(), career=None, prefer=(), max_deck=0):
        self.port = port
        self.route = route
        self.mode = mode
        self.partner, self.packs, self.career = partner, tuple(packs), career
        self.prefer = tuple(prefer)
        self.max_deck = max_deck or None
        self.entry_tried = {}
        self.fight_logged = False
        self.fights_won = 0
        self.seen_tips = set()
        self.skills_tried, self.skills_cleared = set(), 0.0
        self.turns = TurnMemory()
        self.last_end = None

    def run(self, *args):
        return cli(*args, port=self.port)

    def state(self):
        code, out, err = self.run("state", "--json")
        return json.loads(out) if code == 0 else None

    def act(self, action_id):
        code, out, err = self.run("act", action_id)
        log(f"act {action_id}: {(out or err).strip()}")
        return code == 0

    # ---------------------------------------------------------------- fight policy

    def fight_step(self, st):
        battle = st["battle"]
        flow = battle.get("flow") or {}
        if battle.get("select"):   # a card asks for a choice: canAct is false until it is answered
            choice = select_choice(st.get("actions", []))
            if choice:
                log(f"select: {battle['select'].get('title')} ({battle['select'].get('chosen')} chosen, {battle['select'].get('need')} to go)")
                self.act(choice)
            time.sleep(0.5)
            return
        if not battle.get("player") or flow.get("animating") or not flow.get("canAct"):
            time.sleep(0.15)
            return
        if not self.fight_logged:
            self.fight_logged = True
            log(fight_start_line(battle))
        plan = eat_plan(battle, st.get("actions", []))
        if plan and plan[0] == "skill":
            log(f"eat: {plan[1]}")
            self.act(plan[1])
            time.sleep(0.8)
            return
        if plan:
            log(f"feast: playing {plan[2]} (the skill is ready)")
            code, out, err = self.run("play", "--uid", plan[1], "--target", "0", "--wait")
            log(f"play {plan[2]}: {(out or err).strip()}")
            time.sleep(0.5)
            return
        if time.time() - self.skills_cleared > 8:   # each skill is tried once per 8 s: a "usable" one that does nothing must not spin
            self.skills_tried.clear()
            self.skills_cleared = time.time()
        hold = ("careercard_3",) if hold_transform(battle, self.turns.turn) else ()
        skill = skill_choice(st.get("actions", []), battle["enemies"], self.skills_tried, hold)
        if skill:
            sid = next(a["Parameters"]["skillId"] for a in st["actions"] if a["Id"] == skill)
            self.skills_tried.add(sid)
            log(f"skill: {skill}")
            self.act(skill)
            time.sleep(0.8)
            return
        unit = battle["player"]["unit"]
        if self.turns.revived(unit):
            log(f"revived: hp {unit['hp']}/{unit.get('maxHp')} buffs {buff_text(unit)}")
        pick = pick_card(battle, self.turns.expected_loss(unit))
        if pick:
            uid, target, name = pick
            code, out, err = self.run("play", "--uid", uid, "--target", str(target), "--wait")
            log(f"play {name} -> enemy {target}: {(out or err).strip()}")
            if code != 0:
                time.sleep(0.3)
        else:
            player = battle["player"]
            log(f"end turn (hp {unit['hp']}, block {unit.get('defend')}, rebirth {next((b.get('level') for b in unit.get('buffs') or [] if b.get('name') == '重生'), 0)}, draw {player.get('piles', {}).get('draw')}, "
                f"last turn cost {self.turns.loss}, incoming {incoming_damage(battle['enemies'])}, "
                f"enemies {[(e['unit']['name'], e['unit']['hp']) for e in battle['enemies']]})")
            self.turns.ended(unit)
            self.run("end-turn")
            time.sleep(1.0)

    # ---------------------------------------------------------------- everything else

    def transition(self, action_id):
        """Scene-changing action: fire once, then wait for the phase to move on (the bridge refuses repeats anyway)."""
        self.act(action_id)
        time.sleep(3.0)

    def step(self, st):
        phase = st["phase"]
        ids = [a["Id"] for a in st.get("actions", [])]
        if phase in OUT_OF_FIGHT:   # the next fight starts with a clean memory of what the enemies' turns cost
            self.turns = TurnMemory()
            self.fight_logged = False
        if phase != "game_entry":
            self.entry_tried = {}
        if "tutorial_advance" in ids:   # first-run tips sit over any phase and block a human's pointer: read them, click through
            for tip in st.get("tutorial") or []:
                line = f"tutorial: {tip['speaker']} ({tip['slide']}/{tip['slides']}): {tip['text']}"
                if line not in self.seen_tips:   # the same tip is polled many times
                    self.seen_tips.add(line)
                    log(line)
            self.act("tutorial_advance")
            time.sleep(0.4)
            return
        if phase == "deck":
            self.act(deck_choice(st, self.max_deck))
            time.sleep(0.4)
            return
        if phase == "deck_select":
            choice = picker_choice(st.get("actions", []))
            if choice:
                log(f"picker: {(st.get('select') or {}).get('caption')}: {choice}")
                self.act(choice)
            time.sleep(0.5)
            return
        if phase == "shop":
            choice = shop_choice(st.get("actions", []))
            if choice:
                shop = st.get("shop") or {}
                log(f"shop ({shop.get('gold')} gold): {choice}")
                if choice == "shop_leave":   # why nothing was bought: an empty list (still filling?) or items that cannot be bought
                    listing = "; ".join(f"{i.get('kind')} {i.get('name')} {i.get('price')}g {'ok' if i.get('buyable') else 'no: ' + str(i.get('note'))[:40]}"
                                        + (" ⚠" if i.get("warn") else "") for i in shop.get("items") or [])
                    log("  shop items: " + (listing or "(none listed)"))
                self.act(choice)
            time.sleep(1.0 if choice == "shop_leave" else 0.5)
            return
        if phase in ("map_select", "card_choice", "rewards") and wants_deck(st, self.max_deck):
            log(f"deck: {st['run']['backpack']} spare card(s), deck {st['run']['inDeck']}/{st['run']['maximum']}: opening the deck window")
            self.act("deck_open")
            time.sleep(0.8)
            return
        place = [i for i in ids if i.startswith("map_place_")]
        if phase == "map_select" and place:   # every sixth map the route is built by hand: fill the first empty node (see route_pick for the card)
            pick = route_pick(st, self.route)
            self.act(pick if pick in place else place[0])
            time.sleep(0.8)
            return
        if phase == "battle":
            return self.fight_step(st)
        if phase == "battle_won":
            self.act("battle_open_rewards")
            time.sleep(1.0)
        elif phase == "rewards":
            claim = [i for i in ids if i.startswith("reward_claim_")]
            self.act(claim[0] if claim else "reward_close")
            time.sleep(0.8)
        elif phase == "blessing_choice":
            pick = [i for i in ids if i.startswith("blessing_pick_")]
            if pick:
                self.act(pick[0])
            time.sleep(2.5)
        elif phase == "card_choice":
            choice = card_choice(ids, st.get("cardChoice"), self.prefer)   # a full card collection leaves only `skip`; while the cards fade in nothing is offered
            if choice:
                offered = {f"card_choice_pick_{c['index']}": c for c in st.get("cardChoice") or []}
                if choice in offered:
                    log(f"card: {offered[choice]['name']} (rarity {offered[choice].get('rarity')}, cost {offered[choice].get('cost')}): {offered[choice].get('text')}")
                self.act(choice)
                time.sleep(2.5)
            else:
                time.sleep(0.3)
        elif "main_menu_start_game" in ids:
            self.transition("main_menu_start_game")
        elif "hub_start_game" in ids:   # a save exists: Start on the menu lands in the hub, whose sign has to be clicked
            self.transition("hub_start_game")
        elif "game_entry_start" in ids:
            pref = entry_pref(st, self.partner, self.packs, tuple(i for i, n in self.entry_tried.items() if n >= 2), career=self.career) if self.mode == "classic" else None
            if pref:   # partner / card packs first, the run itself after
                self.entry_tried[pref] = self.entry_tried.get(pref, 0) + 1
                log(f"entry: {pref}")
                self.act(pref)
                time.sleep(0.8)
            else:
                self.transition("game_entry_start")
        elif "map_continue" in ids:
            self.transition("map_continue")
        elif "dialogue_advance" in ids:
            self.act("dialogue_advance")
            time.sleep(0.3)
        elif phase == "game_entry":   # first launch: the language picker covers the entry screen
            self.pick_language()
        elif phase == "announcement":   # the update list the hub shows once per patch: close it
            self.act("announcement_close")
            time.sleep(1.0)
        elif phase == "item_reveal":   # "new item" card after a run: read it, confirm it
            log("item: " + " / ".join(st.get("texts") or []))
            self.act("item_reveal_confirm")
            time.sleep(1.0)
        elif phase == "slot":   # Chaos Paradise: leave at once (see SlotMode.cs); the game's confirmation window follows
            choice = slot_action(st) if self.mode == "slot" else None
            if choice:
                log(f"slot: leaving now, {st.get('slot')}")
                self.act(choice)
            time.sleep(1.5)
        elif phase == "modal":   # the game's yes/no window; classic mode with a saved run asks "continue game / new game"
            log("modal: " + " / ".join(x for x in (st.get("modalTitle"), st.get("modalText")) if x))
            choice = modal_choice(st.get("actions", []), allow_end_run=self.mode == "slot")
            if choice:
                self.act(choice)
            time.sleep(1.5)
        elif phase == "mode_choice":   # story / classic roguelike / slot (Chaos Paradise): the mode asked for
            self.act(mode_pick(ids, self.mode))
            time.sleep(3.0)
        elif phase == "run_end":   # the run is over: first click shows the score, the second returns to the main menu
            line = run_end_line(st)
            if line and line != self.last_end:
                self.last_end = line
                log(line)
            if "run_end_continue" in ids:
                self.act("run_end_continue")
            time.sleep(2.5)
        elif phase == "event":   # story event: the best-reading option (see event_value); after a choice the one button that leaves it
            choice = event_choice(st)
            if choice:
                event = st.get("event") or {}
                shown = next((o for o in event.get("options") or [] if f"event_option_{o['index']}" == choice), None)
                if shown and not event.get("resolved"):
                    log(f"event {event.get('title')}: {shown.get('label')} ({shown.get('effect')}) of {len(event.get('options') or [])} options")
                self.act(choice)
            time.sleep(1.5)
        elif phase == "rest":   # the rest room: eat what is on the table (the food is renumbered after each meal), then leave
            eat = [i for i in ids if i.startswith("rest_eat_")]
            if eat or "rest_leave" in ids:
                self.act(eat[0] if eat else "rest_leave")
            time.sleep(0.6 if eat else 3.0)
        elif phase == "unknown":
            self.leave_screen(st)
        else:
            time.sleep(0.4)

    LEAVE = ("ExitButton", "Close", "离开", "退出", "返回")

    def leave_screen(self, st):
        """A screen the bridge does not model (shop, event, ...): the UI dump is all there is. Leave through its exit button."""
        for window in (st.get("ui") or {}).get("windows", []):
            for c in window.get("clickables", []):
                if c.get("interactable") and any(word == c.get("label") for word in self.LEAVE):
                    log(f"leaving {window['name']} via '{c['label']}'")
                    self.run("click", "--", str(c["id"]))
                    time.sleep(2.0)
                    return
        time.sleep(0.5)

    def pick_language(self):
        code, out, err = self.run("ui", "--json")
        if code != 0:
            return
        for window in json.loads(out)["windows"]:
            if window["name"] == "LanguageUseUI" and window["clickables"]:
                self.run("click", "--", str(window["clickables"][0]["id"]))
                return


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--port", type=int, default=47800)
    ap.add_argument("--seconds", type=float, default=600, help="give up after this long")
    ap.add_argument("--fights", type=int, default=0, help="stop on the map after winning this many fights (0: no limit)")
    ap.add_argument("--runs", type=int, default=0, help="stop once this many runs have ended (0: no limit)")
    ap.add_argument("--route", choices=("first", "rest", "bold", "rest-first"), default="bold",
                    help="which cards fill the route when the player builds it: the hardest fights, then the shop, the rest room last (default, bold); "
                         "the first four of the tray (first); tray order with the rest room kept for the last frame (rest); or the rest room first, then bold (rest-first: "
                         "the safe box that hands profile cards to a run is in the rest room)")
    ap.add_argument("--mode", choices=("classic", "slot"), default="classic",
                    help="classic: play the run; slot: Chaos Paradise, leave at once (a run is a minute and pays its score; needs player level 25 and the "
                         "difficulty tags set, see CLAUDE.md trap 37)")
    ap.add_argument("--prefer", default="", help="comma-separated card names to take over rarer ones (earlier = better), e.g. 复仇短匕,绯咏弦奏")
    ap.add_argument("--max-deck", type=int, default=0, help="never equip spare cards beyond this deck size (0: up to the game's maximum); a lean deck draws its key card more often")
    ap.add_argument("--career", default="", help="career to pick before a classic run, by name ('' keeps the game's choice)")
    ap.add_argument("--partner", default="匣上黑猫", help="partner to pick before a classic run, by name (default 匣上黑猫: a revive at every battle start; '' keeps the game's choice)")
    ap.add_argument("--packs", default="蛇自阴影中袭来", help="comma-separated card packs to switch on before a classic run (default the rebirth pack)")
    ap.add_argument("--speed", type=float, default=0, help="set the game's clock before playing (`witchctl speed`; 0 leaves it alone)")
    ap.add_argument("--pause", default="", help="comma-separated phases or window names: stop (exit code 10) the first time one is on screen, without acting")
    ap.add_argument("--recon", action="store_true", help="record every new screen (phase + windows) with `witchctl recon` into .recon/ while playing")
    args = ap.parse_args()

    pause = tuple(x for x in args.pause.split(",") if x)
    player = Player(args.port, args.route, args.mode, args.partner, [p for p in args.packs.split(',') if p], args.career or None, [n for n in args.prefer.split(',') if n], args.max_deck)
    if args.speed:
        code, out, err = player.run("speed", str(args.speed))
        log((out or err).strip())
    deadline = time.time() + args.seconds
    watch = StuckWatch()
    previous, runs_done = None, 0
    seen_screens = set()
    while time.time() < deadline:
        st = player.state()
        if st is None:
            log("bridge unreachable")
            return 2
        # The game usually opens the chest by itself, so `battle_won` may never be seen: any post-fight phase right after `battle` is a win.
        if previous == "battle" and st["phase"] in AFTER_WIN:
            player.fights_won += 1
            log(f"fight won ({player.fights_won})")
        if run_over(previous, st["phase"]):
            runs_done += 1
            log(f"run over ({runs_done})")
        previous = st["phase"]
        if pause and should_pause(st["phase"], pause, st.get("windows") or []):
            log(f"paused at {st['phase']} (windows {st.get('windows')})")
            return 10
        if args.runs and runs_done >= args.runs:
            log("played the requested number of runs")
            return 0
        if args.fights and player.fights_won >= args.fights and st["phase"] == "map_select":
            log("reached the requested number of wins")
            return 0
        if watch.stuck((st["phase"], json.dumps(st.get("battle", {}).get("player"), sort_keys=True))):
            log(f"stuck in phase {st['phase']} for 90 s; state:")
            print(player.run("state")[1])
            return 3
        if args.recon and recon_key(st) not in seen_screens:
            seen_screens.add(recon_key(st))
            code, out, err = player.run("recon")
            log(f"recon {recon_key(st)}: {(out or err).strip()}")
        player.step(st)
    log("out of time")
    return 1


if __name__ == "__main__":
    sys.exit(main())
