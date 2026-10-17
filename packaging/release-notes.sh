#!/usr/bin/env bash
# A TxSuite release's notes from the Conventional Commits since the previous release: features, fixes and speedups (docs, test, ci,
# build and chore left out), a t5 / t7 / t8 scope as the line's "T5: " prefix. Called from .github/workflows/build.yml for the
# releases and the nightly: bash packaging/release-notes.sh [<commit>] (default HEAD)
set -eu

to=${1:-HEAD}
# the previous plain release before it; labelled ones (Txsuite_v1.2.0-beta) are pre-releases
from=$(git describe --tags --abbrev=0 --match 'Txsuite_v*' --exclude 'Txsuite_v*-*' "$to^")

section() {
  local lines
  lines=$(git log --no-merges --format=%s "$from..$to" | grep -E "^$1(\([^)]*\))?!?: " |
    sed -E 's/^[a-z]+\((t[578])\)!?: /\U\1\E: /; s/^[a-z]+(\([^)]*\))?!?: //; s/^/- /') || true
  if [ -n "$lines" ]; then printf '### %s\n\n%s\n\n' "$2" "$lines"; fi
}

section feat Features
section fix Fixes
section perf Performance
echo "Full changelog: https://github.com/${GITHUB_REPOSITORY:-roffe/TuningSuites}/compare/$from...$(git describe --tags --exact-match --match 'Txsuite_v*' "$to" 2>/dev/null || git rev-parse "$to")"
