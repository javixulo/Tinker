#!/usr/bin/env bash
# Prints the changelog entry of a version (the text under its `## [x.y.z] - date` heading, up to the next `## [` heading).
# The release workflow uses it as the notes of the GitHub release, and checks with it, before publishing anything, that the
# entry exists and is not empty.
#
# Usage: scripts/changelog-entry.sh <version> [changelog]      (version without the leading v, for example 0.1.0-beta.1)
set -euo pipefail

version="${1:?Usage: changelog-entry.sh <version> [changelog], for example 0.1.0-beta.1}"
changelog="${2:-CHANGELOG.md}"

[ -f "$changelog" ] || { echo "$changelog does not exist." >&2; exit 1; }

# index() compares text, not a pattern, so the dots of the version are not wildcards.
entry="$(awk -v heading="## [$version]" '
  index($0, "## [") == 1 { if (found) exit; if (index($0, heading) == 1) { found = 1; next } }
  found { print }
' "$changelog")"

if ! grep -q '[^[:space:]]' <<<"$entry"; then
  echo "$changelog has no entry for $version (or it is empty). Rename 'Unreleased' to '## [$version] - <date>' and commit it before tagging." >&2
  exit 1
fi

printf '%s\n' "$entry"
