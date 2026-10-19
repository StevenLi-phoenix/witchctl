#!/usr/bin/env bash
# One whole classic run as 奈奈 with the doom-power engine, hands-free, in the lab game (hidden, 2x speed):
#   craft the hand-made card (if none is in storage) -> play to the first rest room (the route puts it first) -> take the card out of the safe box
#   -> play the rest of the run. Log: .lab/nana.log (override with LOG=). Needs the lab game running in the hub (`scripts/lab.sh start`, then
#   `witchctl act main_menu_start_game`) and truth for the card (50). Exit code: the bot's.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT"
export PATH="$ROOT/cli:$PATH"
LOG="${LOG:-$ROOT/.lab/nana.log}"
SPEED="${SPEED:-2}"
python3 scripts/steam-whoami.py --expect eqckd
say() { echo "$(date +%T) $*" | tee -a "$LOG"; }

BOT=(python3 -I examples/greedy.py --mode classic --career 奈奈 --partner 匣上黑猫 --packs 蛇自阴影中袭来 --route rest-first
     --prefer 复仇短匕,绯咏弦奏,血契重锋,复仇链枪,通感熔断 --max-deck 20 --speed "$SPEED")

say "=== nana run: craft ==="
python3 -I scripts/nana/craft_feast.py | tee -a "$LOG"
say "=== nana run: to the first rest room ==="
rc=0; "${BOT[@]}" --pause rest --seconds 600 >> "$LOG" 2>&1 || rc=$?
[ "$rc" -eq 10 ] || { say "bot ended with $rc before the rest room"; exit "$rc"; }
say "=== nana run: safe box ==="
python3 -I scripts/nana/safebox.py | tee -a "$LOG"
say "=== nana run: the rest of the run ==="
"${BOT[@]}" --runs 1 --seconds 7200 >> "$LOG" 2>&1
rc=$?
say "bot exit=$rc"
grep -E "run end:" "$LOG" | tail -1 || true
exit "$rc"
