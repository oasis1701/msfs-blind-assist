#!/usr/bin/env bash
# The user guide's two jobs at the end of a release or preview build, shared by release.yml and
# preview.yml so the two never drift apart (docs/user-guide.md).
#
#   tools/user-guide-publish.sh bundle <release|preview> <version>
#       Builds the one-page guide into the Release build folder, so it ships in the zip next to the
#       exe, where Help, User Guide opens it. The file name is a contract with
#       HelpLinks.BundledGuideFileName (GUIDE-3). It reuses the GuideBuilder the solution's Release
#       build just compiled. A guide problem never fails the build: the zip then ships without the
#       file and Help, User Guide opens the online guide, so it only warns.
#
#   tools/user-guide-publish.sh refresh-site
#       Asks the website (oasis1701/msfs-blind-assist-site) to rebuild its guide now rather than at
#       its next six-hourly run, with the token in GH_TOKEN (the SITE_PAGES_TOKEN secret: Actions read
#       and write on that repository only). Best-effort: with no token, or when the request fails, it
#       only says so.

set -u

BUNDLED="MSFSBlindAssist/bin/x64/Release/net10.0-windows/MSFS Blind Assist User Guide.html"

case "${1:-}" in
  bundle)
    if ! dotnet run --project tools/GuideBuilder -c Release --no-build -- \
        one-page --guide guide --out "$BUNDLED" --edition "${2:?edition}" --version "${3:?version}"; then
      echo "::warning::The one-page user guide was not built (see above): this build ships without it, and Help, User Guide opens the online guide."
    fi
    ;;
  refresh-site)
    if [ -z "${GH_TOKEN:-}" ]; then
      echo "No SITE_PAGES_TOKEN secret; the website picks this up at its next scheduled build."
    elif ! gh workflow run pages.yml -R oasis1701/msfs-blind-assist-site; then
      echo "::warning::Could not start the website's build (is SITE_PAGES_TOKEN expired?); it picks this up at its next scheduled build."
    fi
    ;;
  *)
    echo "Usage: $0 bundle <release|preview> <version> | refresh-site" >&2
    exit 2
    ;;
esac
