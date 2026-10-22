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

## Add back the pictures in `home` and `switcher`

The snippet removes every `<img>`. In `home` and `switcher` only, put back one
`<img class="app-img">` per app tile, where the picture stood (the layer needs the elements, not
the pictures). The other fixtures need none.

## Check `settings.html` for credentials

Before committing the Settings capture, search `fixtures/settings.html` (case-insensitive) for
`hoppie`, `simbrief`, `navigraph` and `pushover`. The snippet drops `value` attributes, but a
credential can still sit in text, a `placeholder`, a `title` or a `data-` attribute. Delete any
value you find, keeping the element.

## Captured:

- Date: _to be filled in_
- Fenix version: _to be filled in_
- Aircraft: _to be filled in_
