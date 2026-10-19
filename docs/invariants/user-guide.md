# User guide rules — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/user-guide.md`. Background: [docs/user-guide.md](../user-guide.md).

## GUIDE-1

Added 2026-10-19 (PR #319). A link from one guide page to another is a plain relative Markdown link to the page's file, with the heading's id after a #: its target is `taxi-guidance.md#taxi-before-you-start`, or `index.md` for the contents. A link within the page targets `#id` alone. Never Liquid (`{{ '/guide/x/' | relative_url }}`, as the guide used while it lived in the website's repo), never a site path (`/guide/x/`, `../x/`) and never a raw `<a href>`.

Why. Three things read these links, and only the plain form works in all of them:

- The website builds the guide twice, at `/guide/` (the released edition) and `/guide/preview/`. GitHub Pages always runs the jekyll-relative-links plugin, which turns `page.md#id` into that edition's own page address. A site path would send a reader of the preview edition into the released one, and the plugin does not touch raw HTML.
- GitHub's file view follows `page.md#id`, so a reviewer can move through the guide while reading a PR. It shows Liquid as literal text.
- The one-page build (`OnePageBuilder.RewriteLinks`) turns `page.md#id` into `#id` and `page.md` into `#page`, the chapter heading's id.

`GuideChecker` reports every other form, a link to a page that does not exist and a #fragment no heading on the target page has, and `GuideBuilderTests.The_real_guide_passes_its_checks` runs it on every PR. The one exception is `one-page.html`, the file the website build adds next to the pages, and only the contents page may link to it: the site serves a chapter at `/guide/<chapter>/`, so from a chapter the relative link would point inside that chapter's own folder. The checker also refuses a relative image, since the one-page guide ships as a single file with nothing beside it, and it reads a link whose text wraps onto the next line like any other.

Found on the move: the taxi chapter's "navigation database" link had escaped brackets, so the live site showed it as literal text, and its `#nav-database` anchor was on another page anyway. It now targets `getting-started.md#nav-database`.

## GUIDE-2

Added 2026-10-19 (PR #319). Every heading in `guide/` ends with an explicit id, `## Taxi Guidance Settings {#taxi-settings}`, and the id is unique across the whole guide, not only its page: no other heading, no chapter file name (each chapter's slug is its id in the one-page guide) and neither `main` nor `contents`, which the one-page guide's own frame uses. An id never changes once published.

Why. The one-page guide joins every chapter into one HTML page, where two equal ids would make one of them unreachable and its links land on the other. Other pages, the Hotkey Reference, outside links (the forum, the Discord, search engines) and pilots' bookmarks all point at ids, and an id the generator made up from the heading text would change with every rewording. The guide came with explicit, unique ids on all 687 headings, which the conversion kept. `GuideChecker` reports a heading with no id, a duplicate, a skipped level (a `####` straight under a `##`, which a screen-reader user moving by level would miss) and a level-1 heading (the front matter's title is the page's only one). Headings are written with `#`s (indented by up to three spaces counts too); a line of `=` or `-` right under text, which Markdown also reads as a heading, is reported, since it can carry no id and would escape the one-page build's demotion. The one-page build moves every heading down two levels (parts are level 2, chapters 3), so a chapter can use `##` and `###` only: the guide has nothing deeper, and `OnePageBuilder` refuses a heading that would pass level 6.

## GUIDE-3

Added 2026-10-19 (PR #319). Help, User Guide opens `HelpLinks.BundledGuideFileName` ("MSFS Blind Assist User Guide.html") next to the exe, falling back to the online guide when it is missing. release.yml and preview.yml write that file before they zip the build folder, both through `tools/user-guide-publish.sh bundle`, so the path lives in one place outside the app. Nothing at run time would notice a mismatch between the script and the constant: the menu item would simply open the online guide in every release, which looks like it works. `HelpLinksTests.The_publish_script_writes_the_guide_where_the_app_looks` fails when they disagree, and `The_workflows_bundle_the_guide_with_the_publish_script` when a workflow stops calling the script. The script only warns when the guide fails to build, so a guide problem never blocks a release; that build then ships without the file and falls back like a local build, which has no bundled copy by design: the fallback says so and opens the online guide.
