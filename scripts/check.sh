#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_HOME="$PWD/.local/dotnet"
export NUGET_PACKAGES="$PWD/.local/nuget"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
mkdir -p .artifacts/logs .local/empty-feed
dotnet --info > .artifacts/logs/dotnet-info.log
dotnet restore checks/QuestSim.Checks --source "$PWD/.local/empty-feed" -p:DisableTransitiveFrameworkReferenceDownloads=true > .artifacts/logs/restore.log 2>&1
dotnet build checks/QuestSim.Checks -c Release --no-restore -p:DisableTransitiveFrameworkReferenceDownloads=true -p:UseSharedCompilation=false --disable-build-servers > .artifacts/logs/build.log 2>&1
dotnet run --project checks/QuestSim.Checks -c Release --no-build > .artifacts/logs/checks.log 2>&1
cat .artifacts/logs/checks.log
