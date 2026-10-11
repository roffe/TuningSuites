#!/usr/bin/env bash
# releases/latest: the newest release of every suite and its Extras on one page. A tag's run passes its own packages,
# which replace that suite's (or Extras') files there. The update check skips it (no <suite>_v tag).
# Called from .github/workflows/build.yml and extras.yml with GH_TOKEN set: bash packaging/latest-release.sh dist/*
set -eu

notes="Newest release of every suite and its Extras, all on one page.

Windows: the .msi. Linux: the .tar.gz for your arch. macOS: the osx zip for your Mac.

The Extras are the stock binaries (Compare to original file, Lookup partnumber, Create binary from TIS file). On Windows install the Extras .msi after the suite. On Linux and macOS unpack the Extras zip into the suite's folder.
"
for n in T5Suite T5Extras T7Suite T7Extras T8Suite T8Extras; do
  rest=${n:2}
  t=$(git tag -l "${n:0:2}${rest,,}_v*" --sort=-v:refname | grep -E '_v[0-9]+\.[0-9]+\.[0-9]+$' | head -1)
  if [ -n "$t" ]; then notes+=$'\n'"- [$n ${t#*_v}](https://github.com/$GITHUB_REPOSITORY/releases/tag/$t)"; fi
done

# tags pushed together race to create it, the losers just upload
gh release view latest > /dev/null 2>&1 || gh release create latest --target "$GITHUB_SHA" --title "T5Suite, T7Suite and T8Suite" --notes "" || true
gh release upload latest "$@" --clobber
gh release edit latest --latest --notes "$notes"
