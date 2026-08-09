# Updates and release channels

MSFS Blind Assist can update itself from GitHub. **Settings → Updates** controls two
things: which builds it offers, and whether it looks for them on its own.

## The two channels

**Release builds** (the default) offers only full, tagged releases.

**Preview builds** also offers the *rolling preview* — a build republished every time a
change lands on `main`. Preview builds contain the newest work, reviewed and tested, but
with far less flying time than a release, so bugs and stability problems are more likely.
Switching to preview asks for confirmation once.

The preview channel is a superset: it offers whichever is newer, the preview or the
release. A pilot on the preview channel can never miss a real release.

## Checking automatically

*Check for updates automatically when the app starts* is on by default. It runs one check
just after the main window appears — it never delays startup, and it stays completely
silent when there is nothing to report or when the check fails (no network, GitHub
unreachable). Only **Application → Check for Updates** reports those out loud.

When an update is found the app announces it and opens the update window. It will do this
at every launch while an update is outstanding; turn the checkbox off if you would rather
look on your own.

## Going back to a release build

Set the channel to **Release builds** and use **Application → Check for Updates**. The
current release will be offered even though its version number is *lower* than the preview
you are running, and the window says so explicitly. Install it as usual.

There is no automatic rollback to an *older preview*: only one preview exists at a time
and it is replaced on every merge. If a preview is broken and you need the last release
immediately, download `MSFSBA.zip` from the
[Releases page](https://github.com/oasis1701/msfs-blind-assist/releases), close MSFS Blind
Assist, and extract it over your installation folder.

One caveat when going back: a settings file written by a newer build may contain options
the older build does not know about, and those are dropped the next time the older build
saves. Nothing else is affected, and re-installing the newer build simply restores the
defaults for them.

## Version numbers

Releases are `8.0.0`. Previews are `8.0.1-pre.7` — the last release with its patch number
raised, plus a count of the changes merged since. That ordering is what makes a preview
outrank the release it was built on, and makes the next real release outrank every preview
before it.

**About** shows the exact build, e.g. `v8.0.1-pre.7 (build 4f7e7ba)`. Include that whole
string when reporting a problem: the build identifier names the exact code you were
running.

## For maintainers

`.github/workflows/preview.yml` runs on every push to `main`. It runs the test suite
first, then builds, force-moves the `preview` tag and updates one pre-release in place.
The notes are the changelog fragments added since the last `v` tag, so a preview and the
release that eventually contains it describe the same changes.

Three things there are load-bearing:

- **The preview tag must never start with `v`.** `release.yml` triggers on `tags: ['v*']`
  and would publish a duplicate full release.
- **Every `git describe --tags --abbrev=0` must carry `--match 'v*'`.** The `preview` tag
  lives on `main`, so an unscoped lookup can return it instead of the previous release and
  silently truncate a release's notes.
- **`generate_release_notes` must stay `false` in `preview.yml`.** GitHub compares against
  the previous tag, which after the force-push is `preview` itself.

Force-moving the tag means a local clone that has fetched it will report *"would clobber
existing tag"*; `git fetch --tags --force` clears that.
