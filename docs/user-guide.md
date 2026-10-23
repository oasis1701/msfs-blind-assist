# The user guide

The pilot's user guide lives in [`guide/`](../guide/): a contents page (`index.md`) and one Markdown file per chapter, about 97,000 words. It is written for blind and visually impaired pilots, nearly all of whom read it with NVDA or JAWS. Two things are built from it:

- **The website's guide**, at https://msfsblindassist.com/guide/, one page per chapter, in two editions: the released version's and the preview build's.
- **The one-page guide** that ships in every release and preview zip next to the exe, which Help, User Guide opens in the pilot's browser. It matches the installed version and works offline.

A PR that changes what a pilot presses, sees or hears updates the guide in the same PR (CORE-19, in CLAUDE.md). The rules for the guide's own files are GUIDE-1 to GUIDE-3 (`.claude/rules/user-guide.md`, full text in [invariants/user-guide.md](invariants/user-guide.md)).

History: the guide was written by the community as one long HTML page, published as Markdown chapters in the website's repo (`oasis1701/msfs-blind-assist-site`, its PR #6, 2026-10-19), and moved here the same day in PR #319, so that a change and its documentation land together.

## Writing for the guide

- **Page shape.** Each file starts with front matter holding its title, which is the page's only level-1 heading. Sections are `##`, subsections `###`; never skip a level, and never go deeper (the one-page guide moves everything down two levels). Every heading ends with an explicit id that never changes and is unique across the whole guide: `## Taxi Guidance Settings {#taxi-settings}` (GUIDE-2).
- **Links.** Link to another page as `page.md#id`, within a page as `#id` (GUIDE-1). External links are ordinary `https://` links. Only the contents page links to `one-page.html`.
- **No images.** The one-page guide ships as a single file, so describe in words what a picture would show; the checker refuses a relative image.
- **The section pattern.** Feature sections share the sub-headings "What it is", "When to use it", "How to use it", "What you will hear", "Settings" and "If something goes wrong". Follow it, so a pilot who knows the pattern can jump straight to the part they want.
- **Callouts and tables.** A callout is `<div class="callout note" role="note" markdown="1">` (or `tip`, `warning`), a blank line, the Markdown starting with `**Note:**`, a blank line, `</div>`. A table sits inside `<div class="table-wrap" markdown="1">`, with blank lines inside, and gets a sentence before it saying what it lists.
- **Site navigation blocks.** Each chapter opens with a `<div class="guide-part" markdown="1">` line ("Chapter 2 of 4 in Part 4") and an "In this chapter" `<nav class="chapter-toc">` list, and ends with a `<nav class="guide-nav">` list of the previous chapter, the next one and the contents. Keep them up to date when you add, remove or reorder a section or chapter. The one-page guide leaves all three out.
- **A new chapter.** Add the file, list it in `index.md` under its part as a numbered `[Title](page.md)` line (that list sets the one-page guide's order, and the checker fails a chapter it does not list), run the contents command below to list its sections, and update the neighboring chapters' part lines and previous and next links.
- **The contents' section lists.** Under each chapter's numbered line, `index.md` lists that chapter's `##` sections, one indented `- [Heading](page.md#id)` line each, and the one-page guide's contents lists them the same way. Never write those lines by hand: after you add, rename, remove or reorder a section, run `dotnet run --project tools/GuideBuilder -- contents --guide guide`, which rewrites every list from the chapters' headings. The checker fails a list that differs, and says which line.
- **Wording.** Write for a pilot, in US English. Name keys as the guide already does ("Output Shift+D", "Input Shift+R"). Quote what BA says in italics, as it says it. Keep the cockpit's own words as the display shows them.
- **What is in the guide on `main`.** It describes `main`, preview features included: the website's released edition comes from the newest release, so nothing needs marking as "preview only".

To check your change locally, bring the contents' section lists up to date, run the checker, then build the one-page guide and open it in a browser with your screen reader:

```bash
dotnet run --project tools/GuideBuilder -- contents --guide guide
```

```bash
dotnet run --project tools/GuideBuilder -- check --guide guide
```

```bash
dotnet run --project tools/GuideBuilder -- one-page --guide guide --out guide-one-page.html
```

There is no local build of the website itself (it needs Ruby and Jekyll). The "User guide build" check on the PR builds it and uploads the result as the `built-guide` artifact.

## How the website publishes it

The website lives in the `oasis1701/msfs-blind-assist-site` repo. Its `.github/workflows/pages.yml` checks out this repo twice, read-only and without a token (the repo is public):

- **The released edition** (`/guide/`) comes from `guide/` at the newest release's tag, which the GitHub API calls the latest release (the rolling preview is a pre-release, so it is never picked). While the newest release predates the move (v8.2.0 and older have no `guide/`), the site keeps publishing its own frozen copy of the guide there instead; that copy can be deleted from the site's repo after the first release that has `guide/`.
- **The preview edition** (`/guide/preview/`) comes from `guide/` on `main`. It is left out of the site's sitemap and marked not to be indexed, so search engines send pilots to the released edition.

Each edition gets its one-page guide (`one-page.html`), built with this repo's `tools/GuideBuilder` from the same checkout. Every page of the preview edition says at the top that it describes the preview build, with its version and a link to the released edition; the released edition's contents page names its version and links to the preview edition. The build checks every link in the built guide (`GuideBuilder check-site`).

The site rebuilds when its own repo changes, every six hours, and when this repo asks: the last step of release.yml and of preview.yml runs `tools/user-guide-publish.sh refresh-site`, which starts the site's `pages.yml`, so a release or a merge reaches the website within a few minutes. That step needs the repository secret `SITE_PAGES_TOKEN`: a fine-grained personal access token whose owner is the site repo's owner, with access to that repository only and the "Actions: Read and write" permission. Without the secret, or once the token expires, the step only says so (it never fails a release) and the website catches up at its next six-hourly build. To renew it, create a new token the same way and paste it into the same secret.

## How the app ships it

After the build, release.yml and preview.yml run `tools/user-guide-publish.sh bundle`, which writes `MSFS Blind Assist User Guide.html` into the build folder before it is zipped, as the release or preview edition with its version, using the GuideBuilder the Release build just compiled. If the guide fails to build, the script only warns and the zip ships without it. Help, User Guide (`HelpLinks`, `MainForm.MenuHandlers.cs`) opens that file; when it is missing, as in a local build, BA says so and opens the online guide. Help, Project Website opens msfsblindassist.com, and Help, About shows the version. The file name is a contract between the script and `HelpLinks.BundledGuideFileName` (GUIDE-3).

## The checks

| Check | Where | What it does |
|---|---|---|
| `GuideBuilderTests` | xUnit suite, every PR (`tests.yml`) | Runs the checker and the one-page build over the real `guide/`: contents and their section lists, ids, heading levels, links, no Liquid, image alt text. |
| User guide build | `.github/workflows/user-guide-build.yml`, PRs touching `guide/` or the tool | Builds the website with the PR's guide as both editions, checks every link in the result, uploads it as the `built-guide` artifact. |
| User guide line | `.github/workflows/user-guide-line.yml`, every PR, advisory | When a PR adds a feature, aircraft or improvement fragment or changes a hotkey guide, it passes if the PR changes `guide/` or its description has `User guide: needed: …` or `User guide: none, because …` (CORE-19). |
| The website's build | the site repo's `pages.yml` | Checks every link in the built guide; on the site's own pull requests a broken link fails the build. |

The checks are structural: they cannot tell whether the text is right or reads well with a screen reader. Read a new page with NVDA or JAWS before merging it.
