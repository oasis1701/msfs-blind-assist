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
  const w = els.find(e => e.controlType === 'checkbox' && e.text === 'Weight Unit');
  assert.strictEqual(w.value, 'kg');
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

test('duplicate-text buttons disambiguated by id', () => {
  const els = scrape('wb');
  const texts = els.filter(e => e.kind === 'button').map(e => e.text).sort();
  assert.deepStrictEqual(texts, ['Cargo Level: Randomize', 'Pax Level: Randomize']);
});
