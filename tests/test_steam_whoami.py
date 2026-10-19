import json
import os
import pathlib
import subprocess
import sys
import tempfile
import unittest

SCRIPT = pathlib.Path(__file__).resolve().parent.parent / "scripts" / "steam-whoami.py"
DEAD_PID = 99999999   # above macOS/Linux pid_max: never a live process

LOGINUSERS = '''"users"
{
\t"76561197961265729"
\t{
\t\t"AccountName"\t\t"altacct"
\t\t"PersonaName"\t\t"eqckd"
\t\t"RememberPassword"\t\t"1"
\t}
\t"76561197961265730"
\t{
\t\t"AccountName"\t\t"mainacct"
\t\t"PersonaName"\t\t"MainPersona"
\t}
}
'''


def registry(pid, auto):
    return f'''"Registry"
{{
\t"HKLM" {{ "Software" {{ "Valve" {{ "Steam" {{ "SteamPID"\t\t"{pid}" }} }} }} }}
\t"HKCU" {{ "Software" {{ "Valve" {{ "Steam" {{
\t\t"SourceModInstallPath"\t\t"/x/steamapps\\\\sourcemods"
\t\t"AutoLoginUser"\t\t"{auto}"
\t}} }} }} }}
}}
'''


def log_line(state, account_id, when="2026-10-13 18:32:15", msg="RecvMsgClientLogOnResponse() : processing complete"):
    return f"[{when}] [{state}, 4, 335] [U:1:{account_id}] {msg}\n"


class SteamWhoami(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.dir = pathlib.Path(self.tmp.name)
        (self.dir / "config").mkdir()
        (self.dir / "logs").mkdir()
        (self.dir / "config" / "loginusers.vdf").write_text(LOGINUSERS)

    def tearDown(self):
        self.tmp.cleanup()

    def steam(self, pid, auto="altacct", log=""):
        (self.dir / "registry.vdf").write_text(registry(pid, auto))
        (self.dir / "logs" / "connection_log.txt").write_text(log)

    def run_script(self, *args, steam_dir=None):
        p = subprocess.run([sys.executable, "-I", str(SCRIPT), *args], capture_output=True, text=True, timeout=30,
                           env={**os.environ, "STEAM_DIR": str(steam_dir or self.dir)})
        return p.returncode, p.stdout, p.stderr

    def test_live_session_wins_over_autologin(self):
        # Steam is running (our own pid stands in) and the log's last session is the alt account; autologin names the other one.
        self.steam(os.getpid(), auto="mainacct",
                   log=log_line("Logged On", 1000002, "2026-10-12 09:00:00") + log_line("Logged On", 1000001)
                   + "[2026-10-13 18:34:01] CClientJobGetClientUpdateHosts: cached version not expired\n")
        code, out, _ = self.run_script("--expect", "eqckd")
        self.assertEqual(code, 0, out)
        self.assertIn("eqckd（账号 altacct，76561197961265729）", out)
        self.assertIn("在线", out)
        self.assertIn("✓ 是 eqckd", out)

    def test_other_account_fails_expect(self):
        self.steam(os.getpid(), log=log_line("Logged On", 1000002))
        code, out, _ = self.run_script("--expect", "eqckd")
        self.assertEqual(code, 1)
        self.assertIn("MainPersona（账号 mainacct", out)
        self.assertIn("✗ 不是 eqckd：不要启动游戏", out)

    def test_account_name_also_matches_and_case_is_ignored(self):
        self.steam(os.getpid(), log=log_line("Logged On", 1000001))
        self.assertEqual(self.run_script("--expect", "ALTACCT")[0], 0)

    def test_reconnecting_session_keeps_its_account(self):
        self.steam(os.getpid(), log=log_line("Logged On", 1000001) + log_line("Connecting", 1000001, msg="PingWebSocketCM()"))
        code, out, _ = self.run_script("--expect", "eqckd")
        self.assertEqual(code, 0)
        self.assertIn("连接状态 Connecting", out)

    def test_client_not_running_reports_autologin(self):
        self.steam(DEAD_PID, auto="mainacct", log=log_line("Logged On", 1000001))   # a stale log must not count
        code, out, _ = self.run_script("--expect", "eqckd")
        self.assertEqual(code, 1)
        self.assertIn("客户端未运行，启动后会自动登录 MainPersona（账号 mainacct", out)

    def test_running_but_logged_out_falls_back_to_autologin(self):
        self.steam(os.getpid(), log=log_line("Logged On", 1000001) + log_line("Logged Off", 0, msg="ConnectionDisconnected"))
        code, out, _ = self.run_script()
        self.assertEqual(code, 0)   # no --expect: informational only
        self.assertIn("客户端在运行但未登录，启动后会自动登录 eqckd", out)

    def test_no_steam_at_all(self):
        code, out, _ = self.run_script("--expect", "eqckd", steam_dir=self.dir / "missing")
        self.assertEqual(code, 1)
        self.assertIn("没有已知账号", out)

    def test_hook_json_always_exits_zero_and_carries_the_rule_on_mismatch(self):
        self.steam(os.getpid(), log=log_line("Logged On", 1000002))
        code, out, _ = self.run_script("--hook", "--expect", "eqckd")
        self.assertEqual(code, 0)
        doc = json.loads(out)
        self.assertIn("✗ 不是 eqckd", doc["systemMessage"])
        self.assertEqual(doc["hookSpecificOutput"]["hookEventName"], "SessionStart")
        self.assertIn("不得启动游戏", doc["hookSpecificOutput"]["additionalContext"])

    def test_hook_json_on_match_has_no_rule_text(self):
        self.steam(os.getpid(), log=log_line("Logged On", 1000001))
        doc = json.loads(self.run_script("--hook", "--expect", "eqckd")[1])
        self.assertIn("✓ 是 eqckd", doc["systemMessage"])
        self.assertNotIn("不得启动游戏", doc["hookSpecificOutput"]["additionalContext"])

    def test_broken_files_do_not_crash_the_hook(self):
        (self.dir / "registry.vdf").write_text('"Registry" { "HKLM" { "Software" ')   # truncated mid-write
        (self.dir / "config" / "loginusers.vdf").write_bytes(b'\xff\xfe"users" {')
        code, out, _ = self.run_script("--hook")
        self.assertEqual(code, 0)
        self.assertIn("Steam", json.loads(out)["systemMessage"])


if __name__ == "__main__":
    unittest.main()
