'use strict';
// The A380 MFD WIND page (FBW #10929, 76db0aef1): every wind field is named by its phase,
// row and column, read altitude-direction-speed in row order, empty fields read "blank", an
// FMS-computed value says so, and the page's own column header ("ALT, T.WIND") is not read
// on top. Fixtures are source-derived (fixtures/wind_*.html says from which files), not
// live-captured: the first live capture should be added beside them.

const test = require('node:test');
const assert = require('node:assert');
const { scrapeElements, loadFixture } = require('./run');

const INTERACTIVE = ['input', 'dropdown', 'radio', 'subtab', 'button', 'menu', 'icon'];
const NL = '\n';

// Arrays built inside jsdom belong to its realm, so compare plain strings, never with deepStrictEqual.
function texts(name) { return Array.from(scrapeElements(name), (e) => String(e.text)); }
function line(name, text) { return scrapeElements(name).find((e) => e.text === text); }
function run(t, first, count) { return t.slice(first, first + count).join(NL); }

function assertSelectable(name) {
  for (const e of scrapeElements(name)) {
    if (INTERACTIVE.includes(e.kind)) assert.ok(e.idx > 0, `${name}: ${e.kind} "${e.text}" lost its idx`);
  }
}

test('CLB: each field is named by row and column, in row order', () => {
  const t = texts('wind_clb');
  const rows = [
    'Climb wind 1 altitude: FL 350', 'Climb wind 1 direction: 270 °', 'Climb wind 1 speed: 045 KT',
    'Climb wind 2 altitude: 5000 FT (computed)', 'Climb wind 2 direction: 250 °', 'Climb wind 2 speed: 030 KT',
    'Climb wind 3 altitude: blank', 'Climb wind 3 direction: blank', 'Climb wind 3 speed: blank',
  ];
  const at = t.indexOf(rows[0]);
  assert.ok(at >= 0, 'row 1 altitude not labelled:' + NL + t.join(NL));
  assert.strictEqual(run(t, at, rows.length), rows.join(NL));
  assert.ok(!t.some((s) => /^Climb wind [45]/.test(s)), 'hidden rows 4-5 must not be read');
  assertSelectable('wind_clb');
});

test('The column header is not read on top of the labelled rows', () => {
  for (const f of ['wind_clb', 'wind_crz', 'wind_des', 'wind_history']) {
    assert.ok(!texts(f).includes('ALT, T.WIND'), `${f}: header still read`);
  }
});

test('Draft winds: the title and both draft buttons read', () => {
  const t = texts('wind_clb');
  assert.ok(t.includes('DRAFT, CLB WIND'));
  assert.ok(t.includes('CANCEL WIND *'));
  assert.ok(t.includes('INSERT WIND *'));
  assert.ok(t.includes('CPNY WIND REQUEST *'), 'the request button keeps its visible star');
  // Its menu holds Button.tsx's empty wrapper <div> and no option: still a plain button (as is
  // INIT's CPNY F-PLN REQUEST, built the same way).
  assert.strictEqual(line('wind_clb', 'CPNY WIND REQUEST *').kind, 'button');
});

test('CRZ: the waypoint arrows are named, propagated values say computed', () => {
  const t = texts('wind_crz');
  const next = line('wind_crz', 'Next cruise waypoint');
  const prev = line('wind_crz', 'Previous cruise waypoint');
  assert.ok(next && next.kind === 'icon' && next.idx > 0, 'next arrow missing');
  assert.ok(prev && prev.kind === 'icon' && prev.idx > 0, 'previous arrow missing');
  const pick = t.indexOf('CRZ WIND AT: BIKTA (combobox)');
  assert.ok(pick >= 0, 'CRZ WIND AT list missing');
  assert.strictEqual(run(t, pick, 3),
    ['CRZ WIND AT: BIKTA (combobox)', 'Next cruise waypoint', 'Previous cruise waypoint'].join(NL),
    'the arrows read after the list they step through');
  const rows = [
    'Cruise wind 1 flight level: FL 350', 'Cruise wind 1 direction: 270 °', 'Cruise wind 1 speed: 045 KT',
    'Cruise wind 2 flight level: FL 390', 'Cruise wind 2 direction: 270 ° (computed)', 'Cruise wind 2 speed: 045 KT (computed)',
  ];
  const at = t.indexOf(rows[0]);
  assert.ok(at >= 0, 'cruise row 1 not labelled:' + NL + t.join(NL));
  assert.strictEqual(run(t, at, rows.length), rows.join(NL));
  assertSelectable('wind_crz');
});

test('A received uplink: no hidden star, and the button reads as the menu it is', () => {
  const b = scrapeElements('wind_crz').find((e) => /RECEIVED/.test(e.text));
  assert.ok(b, 'uplink button missing');
  assert.strictEqual(b.text, 'RECEIVED CPNY WIND');
  assert.strictEqual(b.kind, 'dropdown');
  const pending = scrapeElements('wind_des').find((e) => /PENDING/.test(e.text));
  assert.strictEqual(pending.text, 'REQUEST PENDING...');
  assert.strictEqual(pending.kind, 'button', 'a button with no menu stays a button');
});

test('DES: the alternate row names its cruise level once, and its two fields', () => {
  const t = texts('wind_des');
  assert.ok(t.includes('Descent wind 2 altitude: 10000 FT'));
  const at = t.indexOf('ALTN trip wind, cruise FL 320');
  assert.ok(at >= 0, 'ALTN row heading missing:' + NL + t.join(NL));
  assert.strictEqual(run(t, at, 3),
    ['ALTN trip wind, cruise FL 320', 'ALTN trip wind direction: blank', 'ALTN trip wind speed: blank'].join(NL));
  assert.ok(!t.includes('FL'), 'stray FL unit line');
  assert.ok(!t.some((s) => /^320:/.test(s)), 'the cruise level labelled the direction field');
  assertSelectable('wind_des');
});

test('HISTORY: one line per recorded wind', () => {
  const t = texts('wind_history');
  assert.ok(t.includes('History wind 1: FL 350 (cruise), 270 °, 045 KT'), t.join(NL));
  assert.ok(t.includes('History wind 2: FL 300, 265 °, 040 KT'), t.join(NL));
  assert.ok(!t.some((s) => /^History wind 3/.test(s)), 'empty entries must not be read');
  assert.ok(t.includes('INSERT HISTORY WIND *'));
});

// F-PLN in the EFOB / T.WIND view: since #10929 the wind's direction and magnitude are the text of
// two 45 px spans either side of a "/", so a magnitude can start left of the x=472 column split
// the parse used; the "/" is what separates them.
test('F-PLN EFOB/T.WIND: direction and magnitude split at the "/", wherever they sit', () => {
  const w = Array.from(scrapeElements('fpln_efob_wind'), (e) => String(e.text));
  const wpt = (id) => w.find((s) => s.indexOf(id) === 0) || '(missing)';
  assert.strictEqual(wpt('WAYPT1'), 'WAYPT1  J102  261° 29  36.5 TL/7   18:04');
  assert.strictEqual(wpt('WAYPT2'), 'WAYPT2  J102  261° 29  35.9 "/"    18:04');
  assert.strictEqual(wpt('WAYPT3'), 'WAYPT3  J102  261° 29  35.1 270°/45 18:04');
  assert.strictEqual(wpt('WAYPT4'), 'WAYPT4  J102  261° 29  34.8        18:04');
});

test('Entering a waypoint in a pick list selects the matching option instead of typing it', () => {
  const { window, A } = loadFixture('wind_crz');
  const root = window.document.body.firstElementChild;
  const items = A.enumerateLines(root);
  const combo = Array.from(items).find((e) => /^CRZ WIND AT/.test(e.text));
  const clicked = [];
  for (const opt of window.document.querySelectorAll('.mfd-dropdown-menu-element')) {
    opt.addEventListener('click', () => clicked.push(opt.textContent));
  }
  const typed = [];
  window.document.addEventListener('keypress', (ev) => typed.push(ev.keyCode), true);

  assert.strictEqual(A.sendToField(combo.idx, 'elb'), 'ok');
  assert.strictEqual(clicked.join(','), 'ELB', 'the ELB option was not clicked');
  assert.strictEqual(typed.length, 0, 'a listed waypoint must not be typed');

  // Not in the list: typed as before (the MFD then says ENTRY NOT IN LIST).
  assert.strictEqual(A.sendToField(combo.idx, 'XYZ'), 'ok');
  assert.strictEqual(clicked.join(','), 'ELB');
  assert.strictEqual(typed.join(','), '88,89,90,13');
});

// A fixed-value list (DropdownMenu with neither keyboardEntryAllowed nor freeTextAllowed) ignores
// typed keys and ENTER since #10929 and stays open: nothing was set, and the MFD says nothing, so
// sendToField reports "nomatch" for the window to speak. A list that takes typing closes on ENTER.
function pickListHarness(acceptsTyping) {
  const { window, A } = loadFixture('wind_crz');
  const root = window.document.body.firstElementChild;
  const combo = Array.from(A.enumerateLines(root)).find((e) => /^CRZ WIND AT/.test(e.text));
  const outer = window.document.querySelector('.mfd-dropdown-outer');
  const menu = outer.parentElement.querySelector('.mfd-dropdown-menu');
  // FBW's DropdownMenu: a click on the outer toggles the list; ENTER in the field closes it
  // only when the list takes typing.
  outer.addEventListener('click', () => { menu.style.display = menu.style.display === 'none' ? 'block' : 'none'; });
  if (acceptsTyping) {
    outer.querySelector('.mfd-input-field-text-input').addEventListener('keypress', (ev) => {
      if (ev.keyCode === 13) menu.style.display = 'none';
    });
  }
  return { A, combo, menu };
}

test('A fixed-value pick list that ignores a typed value reports nomatch and is closed again', () => {
  const { A, combo, menu } = pickListHarness(false);
  assert.strictEqual(A.sendToField(combo.idx, 'XYZ'), 'nomatch');
  assert.strictEqual(menu.style.display, 'none', 'the list was left open over the page');
});

test('A pick list that takes typing reports ok', () => {
  const { A, combo, menu } = pickListHarness(true);
  assert.strictEqual(A.sendToField(combo.idx, 'XYZ'), 'ok');
  assert.strictEqual(menu.style.display, 'none');
});
