#!/usr/bin/env bash
# E1 population/rumor experiment: bash scripts/run_population.sh check|bench|run [--agents 1e6 ...]
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_HOME="$PWD/.local/dotnet"
export NUGET_PACKAGES="$PWD/.local/nuget"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export QUESTSIM_COMMIT="$(git rev-parse --short HEAD 2>/dev/null || true)"
mkdir -p .artifacts/logs .artifacts/population
dotnet build experiments/population -c Release -p:UseSharedCompilation=false --disable-build-servers > .artifacts/logs/population-build.log 2>&1 \
  || { tail -30 .artifacts/logs/population-build.log; exit 1; }
exec dotnet experiments/population/bin/Release/net10.0/QuestSim.Population.dll "$@"
