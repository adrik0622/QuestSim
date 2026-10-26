#!/usr/bin/env bash
# Soak run (F19): fast-forward a seeded world's population and economy; fails on degenerate history.
#   bash scripts/run_soak.sh [--days 30] [--agents 100000] [--seed N] [--out DIR] [--map-every 5]
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_HOME="$PWD/.local/dotnet"
export NUGET_PACKAGES="$PWD/.local/nuget"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
mkdir -p .artifacts/logs .local/empty-feed
dotnet build tools/QuestSim.Soak -c Release --source "$PWD/.local/empty-feed" -p:DisableTransitiveFrameworkReferenceDownloads=true -p:UseSharedCompilation=false --disable-build-servers > .artifacts/logs/soak-build.log 2>&1 || { cat .artifacts/logs/soak-build.log; exit 1; }
dotnet run --project tools/QuestSim.Soak -c Release --no-build -- "$@"
