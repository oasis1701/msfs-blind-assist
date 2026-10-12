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

// The CPNY WIND "*" carries FBW's `hidden` class once a request is pending or an uplink is
// received, but its inline style="display: flex" beats `.hidden { display: none; }`: it stays on
// screen, so it is read. A child that is not drawn is not.
test('A received uplink reads as the menu it is; the star reads while it is drawn', () => {
  const b = scrapeElements('wind_crz').find((e) => /RECEIVED/.test(e.text));
  assert.ok(b, 'uplink button missing');
  assert.strictEqual(b.text, 'RECEIVED CPNY WIND *');
  assert.strictEqual(b.kind, 'dropdown');
  const pending = scrapeElements('wind_des').find((e) => /PENDING/.test(e.text));
  assert.strictEqual(pending.text, 'REQUEST PENDING... *');
  assert.strictEqual(pending.kind, 'button', 'a button with no menu stays a button');

  const { window, A } = loadFixture('wind_des');
  window.document.querySelector('.mfd-button span.hidden').removeAttribute('data-vis');
  const undrawn = Array.from(A.enumerateLines(window.document.body.firstElementChild)).find((e) => /PENDING/.test(e.text));
  assert.strictEqual(undrawn.text, 'REQUEST PENDING...');
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

// A level with no wind recorded (HistoryWind.ts: an undefined vector, e.g. the first flight's
// FL050/150/250 defaults) is drawn "---" / "---": not recorded, never calm.
test('HISTORY: a level with no wind recorded is not read as calm', () => {
  const { window, A } = loadFixture('wind_history');
  const row = window.document.querySelectorAll('.mfd-fms-wind-page-table-row.entry')[2];
  row.firstElementChild.setAttribute('style', 'display: flex; flex-direction: row;');
  row.querySelectorAll('*').forEach((e) => e.setAttribute('data-vis', '1'));
  row.firstElementChild.setAttribute('data-vis', '1');
  row.querySelector('.mfd-fms-wind-page-history-wind-fl-value').textContent = '250';
  const vals = row.querySelectorAll('.mfd-fms-wind-history-wind-entry .mfd-label');
  vals[0].textContent = '---';
  vals[1].textContent = ' ---';
  const t = Array.from(A.enumerateLines(window.document.body.firstElementChild), (e) => String(e.text));
  assert.ok(t.includes('History wind 3: FL 250, not recorded'), t.join(NL));
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

// A HOLD row keeps its hold speed ("SPD 210") in the time column in the EFOB/T.WIND view too: FBW
// draws it there in both views. fpln_polish, switched with its column-header toggle.
test('F-PLN EFOB/T.WIND: a hold row keeps its hold speed', () => {
  function holdLine(efob) {
    const { window, A } = loadFixture('fpln_polish');
    const doc = window.document;
    const root = doc.body.firstElementChild;
    const nav = doc.querySelector('.mfd-navigator-container') || root;
    const toggle = doc.createElement('div');
    toggle.className = 'mfd-dropdown-container';
    toggle.setAttribute('data-vis', '1');
    toggle.setAttribute('data-rect', '150,300,556,200');
    toggle.innerHTML = '<span class="mfd-button" data-vis="1" data-rect="150,300,556,200"><span data-vis="1" data-rect="150,300,556,200">' +
      '<div class="mfd-fms-fpln-button-speed-alt" data-vis="1" data-rect="155,310,546,195">' + (efob ? 'EFOB T.WIND' : 'SPD ALT') +
      '</div></span></span><div class="mfd-dropdown-menu" style="display: none" data-rect="0,0,0,0"></div>';
    nav.insertBefore(toggle, nav.firstChild);
    return Array.from(A.enumerateLines(root), (e) => String(e.text)).find((s) => /^HOLD/.test(s)) || '(missing)';
  }
  assert.ok(/SPD 210$/.test(holdLine(false)), holdLine(false));
  assert.ok(/SPD 210$/.test(holdLine(true)), holdLine(true));
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
});

// FBW's DropdownMenu as sendToField meets it: a click on the outer toggles the list. A list that
// takes typing shows each key in its field at once and, after ENTER, validates asynchronously and
// closes the list only then (InputField.validateAndUpdate: an async parse, then a 500 ms wait). A
// fixed-value list (neither keyboardEntryAllowed nor freeTextAllowed) ignores every key, ENTER
// included, and the MFD says nothing, so sendToField reports "nomatch" / "noclear" for the window
// to speak.
function pickListHarness(acceptsTyping) {
  const { window, A } = loadFixture('wind_crz');
  const root = window.document.body.firstElementChild;
  const combo = Array.from(A.enumerateLines(root)).find((e) => /^CRZ WIND AT/.test(e.text));
  const outer = window.document.querySelector('.mfd-dropdown-outer');
  const menu = outer.parentElement.querySelector('.mfd-dropdown-menu');
  const field = outer.querySelector('.mfd-input-field-text-input');
  const options = Array.from(menu.querySelectorAll('.mfd-dropdown-menu-element'));
  const clicked = [], typed = [];
  for (const opt of options) opt.addEventListener('click', () => clicked.push(opt.textContent));
  outer.addEventListener('click', () => { menu.style.display = menu.style.display === 'none' ? 'block' : 'none'; });
  field.addEventListener('keypress', (ev) => typed.push(ev.keyCode));
  let commit;
  const committed = new Promise((resolve) => { commit = resolve; });
  if (acceptsTyping) {
    let buffer = '';
    field.addEventListener('keydown', (ev) => { if (ev.keyCode === 8) { buffer = ''; field.textContent = buffer; } });
    field.addEventListener('keypress', (ev) => {
      if (ev.keyCode !== 13) { buffer += String.fromCharCode(ev.keyCode); field.textContent = buffer; return; }
      setTimeout(() => { menu.style.display = 'none'; commit(buffer); }, 0);
    });
  }
  return { A, combo, menu, options, clicked, typed, committed };
}

test('A fixed-value pick list that ignores a typed value reports nomatch and is closed again', () => {
  const { A, combo, menu, typed } = pickListHarness(false);
  assert.strictEqual(A.sendToField(combo.idx, 'XYZ'), 'nomatch');
  assert.strictEqual(menu.style.display, 'none', 'the list was left open over the page');
  assert.ok(!typed.includes(13), 'ENTER sent to a list that ignored the keys');
});

test('A fixed-value pick list that ignores a clear reports noclear and is closed again', () => {
  const { A, combo, menu } = pickListHarness(false);
  assert.strictEqual(A.sendToField(combo.idx, ''), 'noclear');
  assert.strictEqual(menu.style.display, 'none');
});

// Whether the list is still open when sendToField returns says nothing: FBW closes it only after
// its asynchronous validation.
test('A pick list that takes typing reports ok, and FBW closes it after validating', async () => {
  const { A, combo, menu, typed, committed } = pickListHarness(true);
  assert.strictEqual(A.sendToField(combo.idx, 'XYZ'), 'ok');
  assert.strictEqual(typed.join(','), '88,89,90,13');
  assert.strictEqual(menu.style.display, 'block', 'open until FBW has validated');
  assert.strictEqual(await committed, 'XYZ');
  assert.strictEqual(menu.style.display, 'none');
});

test('A clear on a pick list that takes typing reports ok', async () => {
  const { A, combo, committed } = pickListHarness(true);
  assert.strictEqual(A.sendToField(combo.idx, ''), 'ok');
  assert.strictEqual(await committed, '');
});

// DIRECT TO and INSERT NEXT WPT take FREE TEXT, which #10929 left alone: a typed ident there has
// always been looked up in the database (DUPLICATE NAMES when ambiguous), so it is typed even when
// the list offers it. Simulated by giving the CRZ WIND AT options the DIRECT TO list's ids.
test('A free-text list types a listed ident instead of picking the plan option', async () => {
  const { A, combo, options, clicked, typed, committed } = pickListHarness(true);
  options.forEach((opt, i) => { opt.id = 'CAPT_MFD_directToDropdown_' + i; });
  assert.strictEqual(A.sendToField(combo.idx, 'elb'), 'ok');
  assert.strictEqual(clicked.length, 0, 'the plan option was picked');
  assert.strictEqual(typed.join(','), '69,76,66,13');
  assert.strictEqual(await committed, 'ELB');
});

// CLR or Delete on a button must never press it: INSERT WIND * would commit the draft winds,
// CANCEL WIND * discard them.
test('A clear sent to a button is refused, never clicked', () => {
  const { window, A } = loadFixture('wind_clb');
  const items = Array.from(A.enumerateLines(window.document.body.firstElementChild));
  for (const label of ['INSERT WIND *', 'CANCEL WIND *']) {
    const btn = items.find((e) => e.text === label);
    let pressed = 0;
    window.document.querySelector('[data-fbwa380-mcdu-idx="' + btn.idx + '"]').addEventListener('click', () => pressed++);
    assert.strictEqual(A.sendToField(btn.idx, ''), 'noclear', label);
    assert.strictEqual(pressed, 0, label + ' was pressed');
  }
});
