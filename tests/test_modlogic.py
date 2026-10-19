"""Runs tests/modlogic (the Unity-free logic of the mod, compiled alone with the local .NET SDK)."""
import os
import subprocess
import unittest

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DOTNET = os.path.join(ROOT, ".toolchain", "dotnet", "dotnet")


@unittest.skipUnless(os.path.exists(DOTNET), "no local .NET SDK (scripts/bootstrap-dotnet.sh)")
class ModLogic(unittest.TestCase):
    def test_pure_logic(self):
        env = dict(os.environ, DOTNET_ROOT=os.path.dirname(DOTNET), DOTNET_CLI_HOME=os.path.join(ROOT, ".toolchain", "dotnet-home"),
                   DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
        out = subprocess.run([DOTNET, "run", "--project", os.path.join(ROOT, "tests", "modlogic"), "-c", "Release", "-v", "quiet", "-o",
                              os.path.join(ROOT, "build", "modlogic")], capture_output=True, text=True, env=env, timeout=180)
        self.assertEqual(out.returncode, 0, out.stdout + out.stderr)
        self.assertIn(" 0 failed", out.stdout)


if __name__ == "__main__":
    unittest.main()
