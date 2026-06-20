const { test } = require('node:test');
const assert = require('node:assert');
const { scrape } = require('./run');

test('OFP <pre> linearizes to one item per line, controlType pre', () => {
  const els = scrape('ofp');
  const pre = els.filter(e => e.controlType === 'pre');
  assert.ok(pre.some(e => e.text === '[ OFP ]'));
  assert.ok(pre.some(e => e.text.indexOf('MAXIMUM') === 0 && e.text.indexOf('ZFW 399997') >= 0));
  assert.ok(pre.some(e => e.text.indexOf('RWY 17L/35R CLOSED DUE TO WIP.') >= 0));
});

test('two-column form: real text label + value; checkbox active unit from Settings', () => {
  const els = scrape('prefs', { settings: { weight_unit: 'kg' } });
  const sb = els.find(e => e.controlType === 'text' && e.text === 'SimBrief Alias');
  assert.strictEqual(sb.value, 'ABC123');
  // unit toggle: active unit folded into the LABEL; value stays boolean (rendered checked state)
  const w = els.find(e => e.controlType === 'checkbox' && e.text === 'Weight Unit: kg');
  assert.ok(w, 'Weight Unit checkbox shows active unit in label');
  assert.ok(w.value === 'true' || w.value === 'false', 'checkbox value is boolean');
});

test('dashboard: CALLSIGN merges with value; leaflet markers dropped', () => {
  const els = scrape('dashboard');
  assert.ok(els.some(e => e.text === 'CALLSIGN: BVI214'));
  assert.ok(!els.some(e => e.kind === 'button'), 'no leaflet marker buttons');
});

test('groundops: row label folds into button text', () => {
  const els = scrape('ground');
  assert.ok(els.some(e => e.kind === 'button' && e.text === 'Air Start Unit: REQUEST'));
});

test('unlabeled buttons dropped; single-char glyph fragments dropped; real content kept', () => {
  const els = scrape('noise');
  // labeled button survives
  assert.ok(els.some(e => e.kind === 'button' && e.text === 'Calculate'));
  // the 3 unlabeled overlay buttons are gone (no "(button)" noise)
  assert.ok(!els.some(e => e.kind === 'button' && !e.text), 'no unlabeled buttons');
  // runway-designator glyph fragments (3/5/R/1/7/L) never surface — not as singles, not merged
  const texts = els.map(e => e.text);
  assert.ok(!texts.some(t => /^(3|5|R|1|7|L|R 1|3 5|7 L)$/.test(t)), 'no runway glyph fragments');
  // real document text still reads
  assert.ok(texts.includes('Runway too short.'));
});

test('weather widget value labels (Temp/QNH) never bleed into the toggle/icao field labels', () => {
  const els = scrape('weather');
  // no control may borrow a bare numeric value ("28"/"1021") as its label
  assert.ok(!els.some(e => (e.controlType === 'checkbox' || e.controlType === 'text') && /^(28|1021)$/.test(e.text)), 'no control labeled with a temp/QNH value');
  // the toggle + icao field resolve their real id-derived labels
  const tog = els.find(e => e.controlType === 'checkbox');
  const icao = els.find(e => e.controlType === 'text');
  assert.match(tog.text, /[A-Za-z]/, 'toggle has a real (letter) label');
  assert.match(icao.text, /[A-Za-z]/, 'icao field has a real (letter) label');
  assert.strictEqual(icao.value, 'LIMC');
  // the METAR line still reads
  assert.ok(els.some(e => /LIMC 201950Z/.test(e.text)));
});

test('colliding icon-only buttons read clean id names (no "X Button: Refresh" clutter)', () => {
  const els = scrape('iconbtn');
  const btns = els.filter(e => e.kind === 'button').map(e => e.text).sort();
  assert.deepStrictEqual(btns, ['Statusbar Restart', 'Weather Search']);
  // no colon-prefixed clutter and no redundant "Button" word
  assert.ok(!btns.some(t => /:|button/i.test(t)), 'no colon or "Button" word');
});

test('duplicate-text buttons disambiguated by id', () => {
  const els = scrape('wb');
  const texts = els.filter(e => e.kind === 'button').map(e => e.text).sort();
  assert.deepStrictEqual(texts, ['Cargo Level: Randomize', 'Pax Level: Randomize']);
});
