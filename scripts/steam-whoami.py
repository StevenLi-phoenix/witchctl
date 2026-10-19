#!/usr/bin/env -S python3 -I -S
"""steam-whoami: which Steam account this Mac's Steam client is (or will be) logged into.

Reads the client's own files only (no Steamworks call: SteamAPI_Init would make this process count as a running game).
  config/loginusers.vdf      every account that logged in here: SteamID64 -> AccountName, PersonaName
  registry.vdf               HKCU AutoLoginUser (the account Steam logs into on start), HKLM SteamPID (the running client)
  logs/connection_log.txt    last line with a connection state and [U:1:<account id>] = the live session

  steam-whoami.py                    one line for humans
  steam-whoami.py --expect eqckd     exit 1 unless the account's persona or account name is eqckd
  steam-whoami.py --hook             JSON for a Claude Code SessionStart hook (always exit 0)

STEAM_DIR overrides ~/Library/Application Support/Steam (tests).
"""
import argparse
import json
import os
import re
import sys

STEAM_ID64_BASE = 76561197960265728
LOG_TAIL_BYTES = 256 * 1024
_TOKEN = re.compile(r'"((?:[^"\\]|\\.)*)"|([{}])')
_LOG_LINE = re.compile(r"^\[([^\]]+)\] \[([A-Za-z ]+), \d+, \d+\] \[U:1:(\d+)\]", re.M)


def parse_vdf(text):
    """Valve KeyValues text -> nested dicts (keys as written)."""
    tokens = [(m.group(1), m.group(2)) for m in _TOKEN.finditer(text)]
    pos = 0

    def block():
        nonlocal pos
        out = {}
        while pos < len(tokens):
            key, brace = tokens[pos]
            pos += 1
            if brace == "}":
                return out
            if brace == "{":   # stray brace: skip
                continue
            value, vbrace = tokens[pos] if pos < len(tokens) else (None, None)
            pos += 1
            out[key.replace("\\\\", "\\")] = block() if vbrace == "{" else (value or "").replace("\\\\", "\\")
        return out

    return block()


def dig(node, *keys):
    """Case-insensitive path lookup; None when any step is missing."""
    for key in keys:
        if not isinstance(node, dict):
            return None
        node = next((v for k, v in node.items() if k.lower() == key.lower()), None)
    return node


def read(path, tail=None):
    try:
        with open(path, "rb") as f:
            if tail:
                f.seek(max(0, os.fstat(f.fileno()).st_size - tail))
            return f.read().decode("utf-8", "replace")
    except OSError:
        return None


def pid_alive(pid):
    if pid <= 0:
        return False
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    except PermissionError:
        return True
    return True


def whoami(steam_dir):
    """-> dict(account, persona, steamid, source, status, since); account None when nothing is known."""
    users = {}
    for sid, row in (dig(parse_vdf(read(os.path.join(steam_dir, "config", "loginusers.vdf")) or ""), "users") or {}).items():
        if isinstance(row, dict) and sid.isdigit():
            users[int(sid)] = {"account": dig(row, "AccountName"), "persona": dig(row, "PersonaName")}
    registry = parse_vdf(read(os.path.join(steam_dir, "registry.vdf")) or "")
    auto = dig(registry, "Registry", "HKCU", "Software", "Valve", "Steam", "AutoLoginUser") or ""
    pid = dig(registry, "Registry", "HKLM", "Software", "Valve", "Steam", "SteamPID") or "0"
    running = pid_alive(int(pid)) if pid.isdigit() else False

    out = {"account": None, "persona": None, "steamid": None, "source": None, "status": None, "since": None, "running": running}
    if running:
        last = None
        for last in _LOG_LINE.finditer(read(os.path.join(steam_dir, "logs", "connection_log.txt"), LOG_TAIL_BYTES) or ""):
            pass
        if last and int(last.group(3)):
            sid = STEAM_ID64_BASE + int(last.group(3))
            out.update(users.get(sid, {}), steamid=sid, source="session", status=last.group(2), since=last.group(1))
            return out
        out["status"] = "Logged Off" if last else None
    if auto:
        sid = next((s for s, u in users.items() if (u["account"] or "").lower() == auto.lower()), None)
        out.update(users.get(sid, {"account": auto, "persona": None}), steamid=sid, source="autologin")
    return out


def describe(me):
    if not me["account"]:
        return "Steam：没有已知账号" + ("（客户端在运行但未登录）" if me["running"] else "（客户端未运行，也没有自动登录账号）")
    who = f"{me['persona'] or '?'}（账号 {me['account']}" + (f"，{me['steamid']}" if me["steamid"] else "") + "）"
    if me["source"] == "session":
        state = "在线" if me["status"] == "Logged On" else f"连接状态 {me['status']}"
        return f"Steam：{who}，{state}，日志时间 {me['since']}"
    lead = "客户端在运行但未登录，" if me["running"] else "客户端未运行，"
    return f"Steam：{lead}启动后会自动登录 {who}"


def matches(me, expect):
    names = {(me["persona"] or "").lower(), (me["account"] or "").lower()} - {""}
    return expect.lower() in names


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--expect", help="persona or account name the client must be on; exit 1 otherwise")
    ap.add_argument("--hook", action="store_true", help="print SessionStart hook JSON, always exit 0")
    args = ap.parse_args(argv)

    steam_dir = os.environ.get("STEAM_DIR") or os.path.expanduser("~/Library/Application Support/Steam")
    try:
        me = whoami(steam_dir)
        line = describe(me)
    except Exception as e:   # a hook must never break the session
        print(f"steam-whoami: {type(e).__name__}: {e}", file=sys.stderr)
        me, line = None, f"Steam：读取失败（{type(e).__name__}: {e}）"
    ok = bool(me and args.expect and matches(me, args.expect))
    if args.expect:
        line += f"  ✓ 是 {args.expect}" if ok else f"  ✗ 不是 {args.expect}：不要启动游戏"

    if args.hook:
        context = line
        if args.expect and not ok:
            context += (f"\nwitchctl 规则（CLAUDE.md）：Steam 账号不是 {args.expect} 时不得启动游戏（lab.sh start 或任何启动方式）；"
                        "先停下告诉用户。")
        print(json.dumps({"systemMessage": line,
                          "hookSpecificOutput": {"hookEventName": "SessionStart", "additionalContext": context}},
                         ensure_ascii=False))
        return 0
    print(line)
    return 0 if ok or not args.expect else 1


if __name__ == "__main__":
    sys.exit(main())
