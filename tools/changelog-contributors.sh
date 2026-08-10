#!/usr/bin/env bash
# Builds the <pr>=<login>,<login> contributor map that ChangelogBuilder's --contributors
# option consumes, from a fragments list (newline-delimited paths, the same file the
# workflows already feed to --from-file). Requires gh with GH_TOKEN set.
#
# Usage: changelog-contributors.sh <fragments-list-file> <out-file>
#
# Credit rule (see docs and changelog.d/README.md): the PR opener plus every distinct
# commit author in the PR, opener first, deduplicated in first-seen order.
#
# Two filters, both load-bearing (probed against PR #184 on 2026-08-10):
#   - gh's commits JSON includes CO-AUTHOR TRAILERS as authors, and the Claude trailer's
#     noreply@anthropic.com maps to the UNRELATED GitHub login "claude" — so filtering
#     keys on the EMAIL, never the login, or every entry credits a stranger.
#   - *[bot] logins are machinery, not contributors.
#
# This script must NEVER fail a release over attribution: a PR that cannot be resolved
# (deleted, network, rate limit) is logged and OMITTED, which renders that entry
# unattributed. Loud validation of the map's SHAPE happens in ChangelogBuilder instead.

set -u

FRAGMENTS="$1"
OUT="$2"

: > "$OUT"

# Distinct PR numbers from the fragment basenames (<pr>-<slug>.<category>.md).
PRS=$(sed 's|.*/||' "$FRAGMENTS" | grep -oE '^[1-9][0-9]*' | sort -un || true)

for pr in $PRS; do
  logins=$(gh pr view "$pr" --json author,commits -q '
      ([.author.login]
       + [.commits[].authors[] | select(.email != "noreply@anthropic.com") | .login])
      | map(select(. != null and . != "" and (endswith("[bot]") | not)))
      | reduce .[] as $x ([]; if index($x) then . else . + [$x] end)
      | join(",")' 2>/dev/null)

  if [ $? -eq 0 ] && [ -n "$logins" ]; then
    echo "$pr=$logins" >> "$OUT"
  else
    echo "changelog-contributors: could not resolve PR #$pr; its entry will be unattributed." >&2
  fi
done

echo "--- contributor map ($OUT) ---"
cat "$OUT"
