# Updates and release channels

MSFS Blind Assist can update itself from GitHub. **Settings → Updates** controls which
builds it offers and whether it looks for them on its own, and whether the app offers
missing data (the navigation database, the location data) when it starts.

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

## Checking for missing data

*Check for missing data when the app starts* is on by default (UPD-9). At launch, before the
update check, it opens **Set up MSFS Blind Assist** when the navigation database is not built
for the simulator the app last connected to (the one every feature opens; one built for the
other simulator does not count), or the location data for Output C and Location Info is not
downloaded or needs an update. The window opens on a read-only box listing only what is
missing; its location data line is the GeoNames tab's status (a kept partial download, the
disk space needed). Then:

- **Download location data** (or **Update location data**, named as the GeoNames tab names it)
  starts the download in the background. The window stays open with focus on that button, so
  no focus change is spoken over the confirmation, and its box follows the download. The
  button is never dimmed; its label reads *Downloading location data* (then *Building location
  data*, then *Location data ready*), and a press then starts nothing. After a failure or
  cancel it reads *Download location data* again and a press retries. **Not now** then reads
  **Close**; the download keeps running when the window closes.
- **Build navigation database** closes the window and opens the Nav Database window
  (File → Nav Database).
- **Not now** (Escape) closes it.

While a download started from the window still runs, that launch skips the automatic update
check: an update accepted then would restart the app and cancel the download without a word.
The next launch checks again.

It appears at every launch while something is missing; a dismissal is never remembered. Turn
the checkbox off to stop it (`Services/StartupDataCheck.cs`, `Forms/StartupDataDialog.cs`).

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

Four things there are load-bearing:

- **The preview tag must never start with `v`.** `release.yml` triggers on `tags: ['v*']`
  and would publish a duplicate full release.
- **Every `git describe --tags --abbrev=0` must carry `--match 'v*'`.** The `preview` tag
  lives on `main`, so an unscoped lookup can return it instead of the previous release and
  silently truncate a release's notes.
- **`generate_release_notes` must stay `false` in `preview.yml`.** GitHub compares against
  the previous tag, which after the force-push is `preview` itself.
- **The preview's version travels in the release NAME, not the tag.** The tag is the fixed
  string `preview` so one rolling release can be updated in place; the app reads the
  version from the `Preview build <version>` name via
  `UpdateCandidateSelector.PreviewNamePrefix`. Change that format in either place and the
  preview channel silently stops offering anything.

Force-moving the tag means a local clone that has fetched it will report *"would clobber
existing tag"*; `git fetch --tags --force` clears that.
