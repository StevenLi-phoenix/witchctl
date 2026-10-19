#!/usr/bin/env bash
# Installs a project-local .NET 8 SDK into .toolchain/dotnet (no global changes).
source "$(dirname "$0")/common.sh"
dl="$(mktemp -d)"
curl -fsSL -o "$dl/dotnet-install.sh" https://dot.net/v1/dotnet-install.sh
bash "$dl/dotnet-install.sh" --channel 8.0 --install-dir "$ROOT/.toolchain/dotnet" --no-path
rm -rf "$dl"
"$ROOT/.toolchain/dotnet/dotnet" --version
# Decompiler for reading the game's assemblies (Witch.dll etc.), also project-local: .toolchain/ilspycmd/ilspycmd
"$ROOT/.toolchain/dotnet/dotnet" tool install ilspycmd --version 8.2.0.7535 --tool-path "$ROOT/.toolchain/ilspycmd"
