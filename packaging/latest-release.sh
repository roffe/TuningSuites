#!/usr/bin/env bash
# releases/latest: TxSuite's and TxExtras' newest releases on one page, its `latest` tag on the newest of them. A tag's run passes its
# own packages, which replace TxSuite's (or TxExtras') files there; a tag that isn't its program's newest version leaves the page as it
# is (a re-run of an old release's workflow). The update check skips it (no Txsuite_v tag).
# Called from .github/workflows/build.yml and extras.yml with GH_TOKEN set: bash packaging/latest-release.sh dist/*
set -eu

case "$GITHUB_REF_NAME" in
  Txsuite_v*) program=TxSuite ;;
  Txextras_v*) program=TxExtras ;;
  *) echo "latest-release.sh: $GITHUB_REF_NAME is no TxSuite or TxExtras release tag" >&2; exit 1 ;;
esac

# a program's newest plain version tag; labelled ones (Txsuite_v1.1.0-beta) are pre-releases
newest() { git tag -l "$1_v*" --sort=-v:refname | grep -E '_v[0-9]+\.[0-9]+\.[0-9]+$' | head -1; }

if [ "$(newest "${GITHUB_REF_NAME%%_v*}")" != "$GITHUB_REF_NAME" ]; then
  echo "$GITHUB_REF_NAME is not $program's newest release, releases/latest stays as it is"
  exit 0
fi

notes="Newest release of TxSuite and TxExtras, all on one page.

TxSuite is T5Suite, T7Suite and T8Suite in one program.

Windows: TxSuiteSetup.exe, or TxSuite-win-x86.zip to run it without installing. Linux: the AppImage or the .tar.gz for your arch. macOS: the osx zip for your Mac.

TxExtras are the stock binaries of all three ECUs (Compare to original file, Lookup partnumber, Create binary from TIS file). On Windows run TxExtrasSetup.exe after TxSuite's setup. With TxSuite from a zip or tar.gz (the Windows zip, Linux, macOS) unpack TxExtras.zip into TxSuite's folder.
"
for p in Txsuite:TxSuite Txextras:TxExtras; do
  t=$(newest "${p%%:*}")
  if [ -n "$t" ]; then notes+=$'\n'"- [${p#*:} ${t#*_v}](https://github.com/$GITHUB_REPOSITORY/releases/tag/$t)"; fi
done

# tags pushed together race to create it, the losers just upload
title="TxSuite"
gh release view latest > /dev/null 2>&1 || gh release create latest --target "$GITHUB_SHA" --title "$title" --notes "" || true
gh release upload latest "$@" --clobber
# the program's files this release no longer has go; the other program's stay
uploaded=$(for f in "$@"; do basename "$f"; done)
gh release view latest --json assets -q '.assets[].name' | grep "^$program" | grep -v -x -F "$uploaded" | while read -r a; do gh release delete-asset latest "$a" -y; done
# the tag on this release's commit, as the nightly's: the newest TxSuite or TxExtras release, whichever came last
git tag -f latest "$GITHUB_SHA"
git push -f origin refs/tags/latest
gh release edit latest --latest --title "$title" --notes "$notes"
