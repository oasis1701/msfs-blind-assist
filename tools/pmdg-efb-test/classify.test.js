const { test } = require('node:test');
const assert = require('node:assert');
const { scrape } = require('./run');

test('fa-icon nav button is labeled "Preferences"', () => {
  const els = scrape('perf');
  const btn = els.find(e => e.kind === 'button' && e.text === 'Preferences');
  assert.ok(btn, 'fa-sliders should label as Preferences');
});

test('text input gets its row label and value', () => {
  const els = scrape('perf');
  const icao = els.find(e => e.controlType === 'text' && e.text === 'Airport ICAO');
  assert.ok(icao, 'Airport ICAO input present with row label');
  assert.strictEqual(icao.value, 'KSEA');
});

test('custom-select emits options and selected value, no duplicate trigger button', () => {
  const els = scrape('perf');
  const sel = els.find(e => e.controlType === 'select' && e.text === 'Runway Condition');
  assert.ok(sel);
  assert.deepStrictEqual(sel.options, ['Dry', 'Wet']);
  assert.strictEqual(sel.value, 'Dry');
  assert.ok(!els.some(e => e.kind === 'button' && e.text === 'Dry'), 'no select-trigger button leaks');
});

test('field unit is appended; empty value -> label gets (ft)', () => {
  const els = scrape('perf');
  const elev = els.find(e => e.controlType === 'text' && /Elevation/.test(e.text));
  assert.strictEqual(elev.text, 'Elevation (ft)');
});
