// flyPad Performance page: Top of Descent and Temperature Correction (shared by the A32NX and the
// A380X). Fixtures captured LIVE on the A380X, FBW build 8a1532f93, 2026-11-20, parked at KATL,
// with data-vis/data-rect baked in by _capture_flypad_fixture.js:
//   perf-tod              ground speed from the simulator, altitude not synced, angle 3 entered
//   perf-tod-synced       the altitude button pressed: it now reads DESYNC and its input is disabled
//   perf-tod-manual       ground speed in manual input (one row)
//   perf-tod-result       manual 280 kt, 35000 ft to 3000 ft at 3 degrees: 100 NM
//   perf-tempcorr         Temperature Correction, empty
//   perf-tempcorr-result  elevation 1026 ft, -4 F, published 3000 ft: corrected 3130
// Before the reader these two tabs read row by row across their side-by-side cards, with the manual
// ground speed inputs named "Min. Altitude 1Ground Speed (feet)", bare SYNC / X buttons, every
// published altitude box named alike and a result spread over four lines.
const { test } = require('node:test');
const assert = require('node:assert');
const { loadFixture, scrape } = require('./run');

const NAV = /^(Dashboard|Dispatch|Ground|Performance( \(current page\))?|Navigation|Atc|Failures|Checklists|Presets|Settings)$/;
// The page's own lines: everything but the status bar and the nav rail.
function content(elements) {
  return elements
    .filter(e => !(e.kind === 'link' && NAV.test(e.text)) && !/^Status bar:/.test(e.text))
    .map(e => e.text + (e.value ? ' = ' + e.value : ''));
}
const page = (name) => content(scrape(name).elements);
function rescrape(env) { return JSON.parse(env.A.scrape()).elements; }
const byText = (els, t) => els.find(e => e.text === t);

test('top of descent reads card by card: ground speed, data, result', () => {
  assert.deepStrictEqual(page('perf-tod'), [
    'Top of Descent (selected)', 'Temperature Correction',
    'Ground Speed', 'Fetching speed from simulator', '0 kt', 'Manual Input',
    'Data', 'Current Altitude (feet) = 750', 'Current Altitude: SYNC', 'Target Altitude (feet)',
    'Angle (degrees) = 3', 'Angle: Clear',
    'Result', 'Start your descent about 2 NM before target',
  ]);
});

test('the altitude button keeps one key while its text flips SYNC to DESYNC', () => {
  const before = byText(scrape('perf-tod').elements, 'Current Altitude: SYNC');
  const after = byText(scrape('perf-tod-synced').elements, 'Current Altitude: DESYNC');
  assert.ok(before && after, 'the altitude sync button is named for its field in both states');
  assert.strictEqual(before.key, 'perf:sync:Current Altitude');
  assert.strictEqual(after.key, before.key, 'a key made from the label would rebuild the button under the focus');
  assert.strictEqual(before.clickable, true);
});

test('a synced altitude input reads disabled, and its value is the aircraft\'s', () => {
  const els = scrape('perf-tod-synced').elements;
  const input = byText(els, 'Current Altitude (feet)');
  assert.strictEqual(input.disabled, true);
  assert.strictEqual(byText(scrape('perf-tod').elements, 'Current Altitude (feet)').disabled, false);
});

test('manual ground speed: each input is named from its own column title', () => {
  const lines = page('perf-tod-manual');
  assert.deepStrictEqual(lines.slice(2, 8), [
    'Ground Speed', 'Min. Altitude 1 (feet) = 0', 'Ground Speed 1 (knots)', 'Remove Last', 'Add', 'Ground Speed: SYNC',
  ]);
  assert.ok(!lines.some(l => /Min\. Altitude 1Ground Speed/.test(l)), 'the two column titles ran together');
  // The column titles are folded into the inputs, never read again as loose lines.
  assert.ok(!lines.includes('Min. Altitude 1'));
  assert.strictEqual(lines.filter(l => l === 'Ground Speed').length, 1, 'only the card title reads "Ground Speed"');
});

test('manual ground speed: the first row\'s altitude is fixed, and Remove Last is unavailable with one row', () => {
  const els = scrape('perf-tod-manual').elements;
  assert.strictEqual(byText(els, 'Min. Altitude 1 (feet)').disabled, true);
  assert.strictEqual(byText(els, 'Ground Speed 1 (knots)').disabled, false);
  assert.strictEqual(byText(els, 'Remove Last').disabled, true);
});

test('manual ground speed: a second row lends its number to its ground speed column', () => {
  const env = loadFixture('perf-tod-manual');
  const doc = env.window.document;
  // The titles row and the inputs row of row 1, as GroundSpeedManual.tsx renders every row.
  const inputs = Array.from(doc.querySelectorAll('input')).filter(i => i.getAttribute('placeholder') === 'knots');
  const inputRow = inputs[0].parentElement.parentElement, titleRow = inputRow.previousElementSibling;
  const title2 = titleRow.cloneNode(true), input2 = inputRow.cloneNode(true);
  title2.querySelector('p').textContent = 'Min. Altitude 2';
  inputRow.parentElement.appendChild(title2);
  inputRow.parentElement.appendChild(input2);

  const lines = content(rescrape(env));
  const at = lines.indexOf('Min. Altitude 1 (feet) = 0');
  assert.deepStrictEqual(lines.slice(at, at + 4),
    ['Min. Altitude 1 (feet) = 0', 'Ground Speed 1 (knots)', 'Min. Altitude 2 (feet) = 0', 'Ground Speed 2 (knots)']);
});

test('the three buttons that share the text SYNC are told apart by field and by key', () => {
  const els = scrape('perf-tod-result').elements;
  const syncs = els.filter(e => /: SYNC$/.test(e.text));
  assert.deepStrictEqual(syncs.map(e => e.text), ['Ground Speed: SYNC', 'Current Altitude: SYNC']);
  assert.deepStrictEqual(syncs.map(e => e.key), ['perf:sync:Ground Speed', 'perf:sync:Current Altitude']);
  assert.strictEqual(byText(els, 'Angle: Clear').key, 'perf:clear:Angle');
  assert.ok(!els.some(e => e.text === 'SYNC' || e.text === 'X'), 'a bare SYNC or X is left');
});

test('a calculation row\'s SYNC says when it is on (FBW shows it by the fill alone)', () => {
  // Data.tsx: `border-theme-highlight px-3 text-theme-highlight` and, while synced to the aircraft,
  // `bg-theme-highlight !text-theme-body`. It shows only in a descent, so it is built from source.
  for (const [on, expected] of [[false, 'Angle: SYNC'], [true, 'Angle: SYNC (active)']]) {
    const env = loadFixture('perf-tod-result');
    const doc = env.window.document;
    const clear = Array.from(doc.querySelectorAll('button')).find(b => b.textContent.trim() === 'X');
    const sync = clear.cloneNode(false);
    sync.setAttribute('class', 'flex items-center rounded-md rounded-l-none border-2 border-theme-highlight px-3 text-theme-highlight transition duration-100'
      + (on ? ' bg-theme-highlight !text-theme-body' : ''));
    const p = doc.createElement('p');
    p.setAttribute('class', 'text-current');
    p.setAttribute('data-vis', '1');
    p.setAttribute('data-rect', clear.getAttribute('data-rect'));
    p.textContent = 'SYNC';
    sync.appendChild(p);
    clear.parentElement.insertBefore(sync, clear);

    const els = rescrape(env);
    const item = byText(els, expected);
    assert.ok(item, expected + ' not found among: ' + els.map(e => e.text).join(' | '));
    assert.strictEqual(item.key, 'perf:sync:Angle', 'the key does not move with the state');
  }
});

test('a result is one sentence, and the result card is not read piece by piece', () => {
  const lines = page('perf-tod-result');
  assert.deepStrictEqual(lines.slice(-2), ['Result', 'Start your descent about 100 NM before target']);
  for (const piece of ['Start your descent about', '100', 'NM', 'before target'])
    assert.ok(!lines.includes(piece), 'loose result piece: ' + piece);
});

test('two results (a distance entered) read as two sentences', () => {
  // Result.tsx renders one block per result: desired vertical speed and desired descent angle.
  const env = loadFixture('perf-tod-result');
  const doc = env.window.document;
  const h = Array.from(doc.querySelectorAll('h1')).find(x => x.textContent.trim() === 'Start your descent about');
  const block = h.parentElement, second = block.cloneNode(true);
  second.children[0].textContent = 'Desired Descent Angle';
  second.children[1].textContent = '3';
  second.children[2].textContent = 'degrees';
  second.removeChild(second.children[3]);
  block.parentElement.appendChild(second);

  assert.deepStrictEqual(content(rescrape(env)).slice(-3),
    ['Result', 'Start your descent about 100 NM before target', 'Desired Descent Angle 3 degrees']);
});

test('FBW\'s hover caption on the altitude button is not read as a line', () => {
  for (const n of ['perf-tod', 'perf-tod-synced', 'perf-tod-result'])
    assert.ok(!page(n).some(l => /ynchronize Input/.test(l)), n);
});

test('temperature correction reads its cards in turn, each field with its own name', () => {
  assert.deepStrictEqual(page('perf-tempcorr'), [
    'Top of Descent', 'Temperature Correction (selected)', 'Fill data from', 'OFP',
    'Airfield Data', 'Airport (ICAO)', 'Field/THR Elevation (ft AMSL)', 'Temperature (°F)', 'Temperature: °F',
    'Published Altitudes', 'Published Altitudes 1 (ft AMSL)', 'Clear',
  ]);
});

test('temperature correction: published altitudes are numbered and each result is one line', () => {
  assert.deepStrictEqual(page('perf-tempcorr-result').slice(4), [
    'Airfield Data', 'Airport (ICAO)', 'Field/THR Elevation (ft AMSL) = 1026', 'Temperature (°F) = -4', 'Temperature: °F',
    'Published Altitudes', 'Published Altitudes 1 (ft AMSL) = 3000', 'Published Altitudes 2 (ft AMSL)', 'Clear',
    'Results', 'Published 3000, Corrected 3130',
  ]);
});

test('two results that read alike keep one line each', () => {
  // The same altitude published twice: two table rows with the same words. The de-duplication
  // that drops a repeated text line must not take the second for an echo of the first.
  const env = loadFixture('perf-tempcorr-result');
  const row = env.window.document.querySelector('tbody tr');
  row.parentNode.appendChild(row.cloneNode(true));
  const lines = rescrape(env).filter(e => e.text === 'Published 3000, Corrected 3130');
  assert.strictEqual(lines.length, 2);
});

test('temperature correction: the Clear button follows FBW\'s own enabling', () => {
  assert.strictEqual(byText(scrape('perf-tempcorr').elements, 'Clear').disabled, true);
  assert.strictEqual(byText(scrape('perf-tempcorr-result').elements, 'Clear').disabled, false);
});

test('every input and button on both tabs is stamped for a click or a typed value', () => {
  for (const n of ['perf-tod', 'perf-tod-manual', 'perf-tod-result', 'perf-tempcorr-result']) {
    const env = loadFixture(n);
    for (const e of rescrape(env).filter(x => x.kind === 'input' || x.kind === 'button')) {
      assert.ok(e.idx > 0, n + ': ' + e.text + ' has no idx');
      assert.ok(env.A.findByIdx(e.idx), n + ': ' + e.text + ' idx ' + e.idx + ' finds no node');
    }
  }
});

test('the reader leaves every other page alone: nothing there is tagged for a card', () => {
  for (const n of ['ground', 'aircraft', 'index', 'a320-ground', 'calibrate']) {
    assert.ok(!scrape(n).elements.some(e => e._card), n + ': an item was taken for a Performance card');
  }
});
