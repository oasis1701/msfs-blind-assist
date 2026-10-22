# Capturing the Fenix EFB fixtures

`fixtures/*.html` are trimmed copies of the Fenix EFB page's markup. The Node tests in this folder
load them into jsdom with the accessibility layer (`MSFSBlindAssist/Resources/fenix-efb-accessibility.js`)
and check that every control ends up named. A fixture is the page's markup only: the snippet below
strips scripts, styles, pictures, the seat map, the map tiles and every `value` attribute. Never
commit a Fenix bundle or a whole document set.

**Re-run this after every Fenix update.** A renamed class or a new control shows up in the sweep
(`npm test`) only if the captures are fresh.

## Procedure

Fenix loaded, any variant. In any Chromium browser open

    http://127.0.0.1:8083/?target=external#/efb

For each screen in the table below, go there by mouse, open the browser's developer console, run
the snippet, then paste the clipboard into `fixtures/<name>.html`.

```js
copy((() => { const c = document.body.cloneNode(true);
  c.querySelectorAll('script,style,link,canvas,svg,img,#seatContainer,.leaflet-pane').forEach(n => n.remove());
  c.querySelectorAll('[value]').forEach(n => n.removeAttribute('value'));
  return '<!doctype html><html><body>' + c.innerHTML + '</body></html>'; })())
```

## The 13 screens

| Fixture name      | Screen |
|---|---|
| `home`            | The EFB home screen (the app tiles). |
| `switcher`        | Any app open, then the top-bar arrow pressed (the app switcher). |
| `my-flight`       | My Flight. |
| `ground-services` | Ground Services. |
| `mass-balance`    | Mass and balance. |
| `panel-states`    | Panel States, with its pop-up open. Close it with Escape afterwards, pressing nothing. |
| `depperf`         | Departure performance. |
| `arrperf`         | Arrival performance. |
| `settings`        | Settings, the Aircraft category. |
| `failures`        | Failures, with ATA 21 open. |
| `documents`       | Documents. |
| `charts`          | Charts. |
| `quick-settings`  | Quick settings. |

These 13 names are the explicit list in `sweep.test.js`. A missing capture fails its test with
"fixture missing"; the sweep never lists the fixtures folder, so a hand-made fixture another task
adds there is not part of it.

## Add back the pictures the layer keys on

The snippet removes every `<img>`. Put back, as bare elements where the picture stood (the layer
needs the elements, not the pictures):

- In `home` and `switcher`: each `<img class="app-img">` with its `src` path and nothing else, no
  picture data, e.g. `<img class="app-img" src="/assets/img/fenixapp3.png">`. The paths show that the
  switcher's pictures are the home tiles' own, in home order, which is what lets the layer name them from
  the tile titles (`navigation.test.js` pins it).
- In `quick-settings`: the airplane button's picture, as
  `<img class="quick-settings-btn-icon flight-mode-btn-icon" src="/assets/img/airplane.png">`
  (it is the button's only content; without it the button is empty).

The other fixtures need none.

## Check `settings.html` for credentials

Before committing the Settings capture, search `fixtures/settings.html` (case-insensitive) for
`hoppie`, `simbrief`, `navigraph` and `pushover`. The snippet drops `value` attributes, but a
credential can still sit in text, a `placeholder`, a `title` or a `data-` attribute. Delete any
value you find, keeping the element.

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
