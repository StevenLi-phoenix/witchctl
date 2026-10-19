# Shared by the scripts in this directory. Source it, do not execute it.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LAB="${WITCHCTL_LAB:-$ROOT/.lab}"
APP="$LAB/Witch.app"
MOD_DIR="$APP/Contents/Mods/WitchCtl"
LOG="$LAB/player.log"
STEAM_APP="${WITCHCTL_STEAM_APP:-$HOME/Library/Application Support/Steam/steamapps/common/Witch's Apocalyptic Journey/Witch's Apocalyptic Journey.app}"
PORT="${WITCHCTL_PORT:-47800}"
LAB_BUNDLE_ID="com.MeowAlive.Witch-s-Apocalyptic-Journey.lab"
LAB_DATA="$HOME/Library/Application Support/$LAB_BUNDLE_ID"
BUNDLE_BIN="$APP/Contents/MacOS/Witch's Apocalyptic Journey"

# Prefer the project-local SDK (scripts/bootstrap-dotnet.sh), fall back to whatever is on PATH.
if [ -x "$ROOT/.toolchain/dotnet/dotnet" ]; then
  export DOTNET_ROOT="$ROOT/.toolchain/dotnet"
  export DOTNET_CLI_HOME="$ROOT/.toolchain/dotnet-home"
  DOTNET="$DOTNET_ROOT/dotnet"
else
  DOTNET="$(command -v dotnet || true)"
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

# PIDs of the running lab game. Match the process's *executable* (its real path: `open` resolves /tmp to /private/tmp), not its
# command line: codesign, lldb or an agent's shell wrapper mention the .lab path too and must never be mistaken for the game.
lab_pids() {
  local bin
  bin="$(realpath "$BUNDLE_BIN" 2>/dev/null)" || return 0
  ps -axo pid=,comm= | awk -v bin="$bin" '{ pid = $1; sub(/^ *[0-9]+ +/, ""); if ($0 == bin) print pid }'
}
