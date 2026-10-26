#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_HOME="$PWD/.local/dotnet"
export NUGET_PACKAGES="$PWD/.local/nuget"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
mkdir -p .artifacts/logs
dotnet build experiments/presentation/QuestSim.Presentation.csproj -p:DisableTransitiveFrameworkReferenceDownloads=true -p:UseSharedCompilation=false --disable-build-servers 2>&1 | tee .artifacts/logs/lab-build.log
