#!/usr/bin/env bash
# releases/latest: TxSuite's and TxExtras' newest releases on one page. A tag's run passes its own packages, which replace
# TxSuite's (or TxExtras') files there. The update check skips it (no Txsuite_v tag).
# Called from .github/workflows/build.yml and extras.yml with GH_TOKEN set: bash packaging/latest-release.sh dist/*
set -eu

notes="Newest release of TxSuite and TxExtras, all on one page.

TxSuite is T5Suite, T7Suite and T8Suite in one program.

Windows: TxSuiteSetup.exe. Linux: the .tar.gz for your arch. macOS: the osx zip for your Mac.

TxExtras are the stock binaries of all three ECUs (Compare to original file, Lookup partnumber, Create binary from TIS file). On Windows run TxExtrasSetup.exe after TxSuite's setup. On Linux and macOS unpack TxExtras.zip into TxSuite's folder.
"
for n in TxSuite TxExtras; do
  rest=${n:2}
  t=$(git tag -l "${n:0:2}${rest,,}_v*" --sort=-v:refname | grep -E '_v[0-9]+\.[0-9]+\.[0-9]+$' | head -1)
  if [ -n "$t" ]; then notes+=$'\n'"- [$n ${t#*_v}](https://github.com/$GITHUB_REPOSITORY/releases/tag/$t)"; fi
done

# tags pushed together race to create it, the losers just upload
title="TxSuite"
gh release view latest > /dev/null 2>&1 || gh release create latest --target "$GITHUB_SHA" --title "$title" --notes "" || true
gh release upload latest "$@" --clobber
# the single suites' packages from before TxSuite go (their own T5suite_v / T7suite_v / T8suite_v releases keep them)
gh release view latest --json assets -q '.assets[].name' | grep -v -E '^Tx(Suite|Extras)' | while read -r a; do gh release delete-asset latest "$a" -y; done
gh release edit latest --latest --title "$title" --notes "$notes"
