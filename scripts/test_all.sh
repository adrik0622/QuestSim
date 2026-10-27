#!/usr/bin/env bash
# Local test runner: bash scripts/test_all.sh [--quick | --all]
#   default  domain checks, game build, every ENet suite, living world, Xvfb screens, E1 population checks
#   --quick  domain checks, game build and the core ENet suites
#   --all    default plus the 8-player / 1M-agent load test and the 40-day economy soak
# Logs: .artifacts/logs/test_all/<timestamp>/<step>.log. Exit code is non-zero if any step fails.
set -uo pipefail
cd "$(dirname "$0")/.."
mode="${1:-default}"
logs=".artifacts/logs/test_all/$(date +%Y%m%d-%H%M%S)"
mkdir -p "$logs"
results=()
failed=0

# Heavy steps get a memory cap where systemd user scopes exist (this workstation has no swap).
capped() {
  if command -v systemd-run >/dev/null 2>&1 && systemd-run --user --scope -q true 2>/dev/null; then
    systemd-run --user --scope -q -p MemoryMax="$1" -p MemorySwapMax=0 "${@:2}"
  else
    "${@:2}"
  fi
}

step() {   # step NAME COMMAND...
  local name="$1"; shift
  local start=$SECONDS
  printf '%-22s ' "$name"
  if "$@" > "$logs/$name.log" 2>&1; then status=PASS; else status=FAIL; failed=1; fi
  local passes; passes=$(grep -c '^PASS' "$logs/$name.log" || true)
  printf '%s  %4ss  %s checks\n' "$status" "$((SECONDS - start))" "$passes"
  results+=("$status $name")
}

# Windows' python3 is usually the Microsoft Store stub; the real interpreter is python there.
py=python3
if ! python3 -c '' >/dev/null 2>&1; then py=python; fi

skip() { printf '%-22s SKIP  (%s)\n' "$1" "$2"; results+=("SKIP $1"); }

step domain-checks bash scripts/check.sh
step game-build bash scripts/build_game.sh
step items "$py" scripts/verify_items.py
step build "$py" scripts/verify_build.py
step multiplayer "$py" scripts/verify_multiplayer.py
step save-failures "$py" scripts/verify_save_failures.py
step defeat "$py" scripts/verify_defeat.py
step content "$py" scripts/verify_content.py
step console "$py" scripts/verify_console.py

if [ "$mode" != "--quick" ]; then
  step living capped 12G "$py" scripts/verify_living.py
  if command -v xvfb-run >/dev/null 2>&1; then
    step build-screen "$py" scripts/verify_build_screen.py
    step city-screen capped 8G "$py" scripts/verify_city_screen.py
  else
    skip build-screen "xvfb-run not installed"; skip city-screen "xvfb-run not installed"
  fi
  if command -v glslc >/dev/null 2>&1 && command -v vulkaninfo >/dev/null 2>&1 && vulkaninfo --summary >/dev/null 2>&1; then
    step population-e1 capped 8G bash scripts/run_population.sh check
  elif command -v glslc >/dev/null 2>&1; then
    step population-e1-cpu capped 8G bash scripts/run_population.sh check --backend cpu
  else
    skip population-e1 "glslc not installed"
  fi
fi
if [ "$mode" = "--all" ]; then
  step load-8-players capped 14G "$py" scripts/verify_load.py
  step soak-40-days capped 8G bash scripts/run_soak.sh --days 40 --agents 100000
fi

echo
echo "Logs: $logs"
if [ "$failed" -ne 0 ]; then
  echo "FAILED: $(printf '%s\n' "${results[@]}" | grep '^FAIL' | cut -d' ' -f2 | tr '\n' ' ')"
  exit 1
fi
echo "All steps passed."
