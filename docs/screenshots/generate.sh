#!/usr/bin/env bash
# Renders docs/screenshots/<suite>/*.png from the real apps, headless: the Screenshots test in T5AppTest / T7AppTest / T8AppTest.
# bash docs/screenshots/generate.sh [T5 T7 T8]   (all three without arguments)
set -eu
cd "$(dirname "$0")/../.."
suites=("$@")
[ $# -gt 0 ] || suites=(T5 T7 T8)
for s in "${suites[@]}"; do
  out="$PWD/docs/screenshots/${s,,}"
  rm -f "$out"/*.png
  env "${s}APP_DUMP=$out" dotnet test "${s}AppTest" -c Release --filter "TestCategory=Screenshots"
done
