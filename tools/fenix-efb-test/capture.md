# Capturing the Fenix EFB fixtures

`fixtures/*.html` are trimmed copies of the Fenix EFB page's markup. The Node tests in this folder
load them into jsdom with the accessibility layer (`MSFSBlindAssist/Resources/fenix-efb-accessibility.js`)
and check that every control ends up named. A fixture is the page's markup only: the snippet below
strips scripts, styles, the map tiles and every `value` attribute, and keeps each picture as a bare
element with no picture data. Never commit a Fenix bundle or a whole document set.

**Re-run this after every Fenix update.** A renamed class or a new control shows up in the sweep
(`npm test`) only if the captures are fresh. Then do the live check at the end of this file: the
captures cannot show what the page's styles hide.

## Procedure

Fenix loaded, any variant. In any Chromium browser open

    http://127.0.0.1:8083/?target=external#/efb

For each screen in the table below, go there by mouse, open the browser's developer console, run
the snippet, then paste the clipboard into `fixtures/<name>.html`. Take each screen with the browser
window on screen: some screens (home, Mass and Balance) keep their content at `visibility: hidden` until
their pictures are laid out.

```js
copy((() => { const c = document.body.cloneNode(true);
  c.querySelectorAll('script,style,link,canvas,svg,.leaflet-pane').forEach(n => n.remove());
  // Every picture stays as a bare element: its id, class, src and alt, nothing else, and no picture data.
  c.querySelectorAll('img').forEach(img => {
    const bare = document.createElement('img');
    ['id', 'class', 'src', 'alt'].forEach(a => { if (img.hasAttribute(a)) bare.setAttribute(a, img.getAttribute(a)); });
    img.replaceWith(bare);
  });
  // Inline data: URIs (picture data) are cut to an empty "data:,".
  c.querySelectorAll('[src^="data:"],[href^="data:"]').forEach(n =>
    ['src', 'href'].forEach(a => { if ((n.getAttribute(a) || '').startsWith('data:')) n.setAttribute(a, 'data:,'); }));
  c.querySelectorAll('[value]').forEach(n => n.removeAttribute('value'));
  return '<!doctype html><html><body>' + c.innerHTML + '</body></html>'; })())
```

The seat map (`#seatContainer`) is kept: the layer hides it from a screen reader, and the tests check that.

## The 22 screens

| Fixture name                | Screen |
|---|---|
| `home`                      | The EFB home screen (the app tiles). |
| `switcher`                  | Any app open, then the top-bar arrow pressed (the app switcher). |
| `my-flight`                 | My Flight. |
| `ground-services`           | Ground Services. |
| `mass-balance`              | Mass and balance, with its chart and seat map. |
| `panel-states`              | Panel States, with its pop-up open. Close it with Escape afterwards, pressing nothing. |
| `depperf`                   | Departure performance. |
| `arrperf`                   | Arrival performance. |
| `arrperf-electrical`        | Arrival performance, "Electrical System" pressed (the procedure sub-page). Go back with its arrow afterwards, ticking nothing. |
| `settings`                  | Settings, the Aircraft category. |
| `settings-audio`            | Settings, the Audio category. |
| `settings-cabin`            | Settings, the Cabin category. |
| `settings-connections`      | Settings, the Connections category. |
| `settings-ground-services`  | Settings, the Ground Services category. |
| `settings-misc`             | Settings, the Misc category. |
| `settings-printer`          | Settings, the Printer category. |
| `settings-realism`          | Settings, the Realism category. |
| `settings-overflow-open`    | Settings, the Aircraft category, with the first "More options" (three dots) menu open. Close it with a click outside, pressing no item. |
| `failures`                  | Failures, with ATA 21 open. |
| `documents`                 | Documents. |
| `charts`                    | Charts. |
| `quick-settings`            | Quick settings. |

These 22 names are the explicit list in `sweep.test.js`. A missing capture fails its test with
"fixture missing"; the sweep never lists the fixtures folder, so a hand-made fixture another task
adds there is not part of it.

## Pictures in the older captures

The captures before 2026-10-22's fuller ones were taken with a snippet that removed every `<img>`, and
had back, as bare elements, only the pictures the layer keyed on then:

- In `switcher`: each `<img class="app-img">` with its `src` path and nothing else, e.g.
  `<img class="app-img" src="/assets/img/fenixapp3.png">`. The paths show that the switcher's pictures
  are the home tiles' own, in home order, which is what lets the layer name them from the tile titles
  (`navigation.test.js` pins it).
- In `quick-settings`: the airplane button's picture, as
  `<img class="quick-settings-btn-icon flight-mode-btn-icon" src="/assets/img/airplane.png">`
  (it is the button's only content; without it the button is empty).

The snippet above keeps every picture, so a fresh capture needs nothing put back.

## Check the Settings captures for credentials

Before committing a Settings capture, search it (case-insensitive) for `hoppie`, `simbrief`,
`navigraph` and `pushover`. The snippet drops `value` attributes, but a credential can still sit in
text, a `placeholder`, a `title` or a `data-` attribute. A setting's own name ("Hoppie Logon Code")
is fine; delete any value you find, keeping the element. Connections holds the credential boxes.

## Captured:

- Date: 2026-10-22
- Fenix version: 3.0.0.7438 (`query { settings { softwareVersion } }`)
- Aircraft: A320-232 IAE (Fenix default units: kilograms, hPa)
- How: the snippet's clone-and-strip run in the page through a debugger and posted to a local
  file writer instead of `copy()`; the pictures above put back by the same script; no other change.
  Opening the screens pressed nothing that changes the aircraft (Panel States closed with Escape).
- Credential search of `settings.html`: nothing found (its only text box is "Search settings").
- `home` and `switcher` re-captured with the `src` paths: 2026-10-22, Fenix 3.0.0.7438, the same way.
  The `home` capture caught the tiles before the page showed them (`#appContainer` at
  `visibility: hidden`: the page shows the tiles once their pictures have loaded); `load.js` shows them
  as the page does a moment later. A future capture of `home` is best taken once the tiles are on screen.
- The fuller captures (2026-10-22, Fenix 3.0.0.7438, A320-232 IAE, the same way, with the snippet's
  picture rule above: every `<img>` bare with its id, class, src and alt, `data:` URIs cut to `data:,`,
  `#seatContainer` kept):
  - `home`, again: every picture bare, including the background `img#appBg` and the top-bar status
    icons. `#appContainer` is still at `visibility: hidden`; `load.js` shows it.
  - `mass-balance`, again: with the seat map `#seatContainer` and the W&B chart picture `img#mainWandB`.
    Its `div.tab-content` was caught at `visibility: hidden` (the capture before it had it visible);
    `load.js` shows it the same way as the home tiles.
  - `arrperf-electrical`: Landing's "Electrical System" sub-page.
  - `settings-audio`, `settings-cabin`, `settings-connections`, `settings-ground-services`,
    `settings-misc`, `settings-printer`, `settings-realism`: one per Settings category.
  - Credential search: clean. Connections holds only the settings' names and empty password boxes.
- `settings-overflow-open` (2026-10-22, Fenix 3.0.0.7438, the same way): Settings, Aircraft, with the first "More
  options" menu open. The menu is a CDK overlay at the end of `<body>`: the backdrop
  `div.cdk-overlay-backdrop`, then `div.cdk-overlay-connected-position-bounding-box > div#cdk-overlay-0.cdk-overlay-pane
  > div.overflow-menu` with its two `button.overflow-menu__item`; the first `button.overflow-button` has
  `aria-expanded="true"`. Credential search: clean (Aircraft has no credential settings).

## Check live after every Fenix update

jsdom has no CSS, so the tests here cannot see `display: none`, `visibility: hidden` or the glyph an
icon font draws (its name is a private-use character from a style sheet). A screen reader can: the
Settings checkboxes the page hides with `display: none` and the icon glyphs that became button names
were found in the sim, not here. So after every Fenix update, and after every change to the layer:

1. Open the EFB in any Chromium browser at the address above, Fenix loaded.
2. Load the layer from the built Resources folder (`Resources\fenix-efb-accessibility.js` next to
   `MSFSBlindAssist.exe`, the file the window injects): paste its text into the developer console, or
   serve the folder from a local file server and add it with a `<script>` element from the console.
3. Read each screen of the table above with the browser's accessibility tree (developer tools,
   Accessibility pane, full-page tree) or with a screen reader. Look for:
   - controls with no name, or a name that is a stray glyph;
   - "clickable" generics (a plain element the page gave a click handler);
   - unnamed graphics (a picture with no alt that is not hidden);
   - checkboxes and other controls the page hides with `display: none`, so that only a styled
     element is left on screen.
4. Record what you find before changing the layer, and capture any screen that shows something new.
