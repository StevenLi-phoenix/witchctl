#!/usr/bin/env bash
# Compiles the mod against the game's managed assemblies (read-only) into build/.
source "$(dirname "$0")/common.sh"
[ -n "${DOTNET:-}" ] || { echo "no dotnet found; run scripts/bootstrap-dotnet.sh" >&2; exit 2; }
"$DOTNET" build "$ROOT/mod/WitchCtl.csproj" -c Release -o "$ROOT/build" -v quiet -nologo
echo "built $ROOT/build/WitchCtl.local.dll"
