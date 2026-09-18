'use strict';
const { test } = require('node:test');
const assert = require('node:assert');
const { load, scrape } = require('./run');

test('a field carries the unit box beside it in its name', () => {
  const els = scrape('perf-landing');
  const fields = els.filter(e => e.controlType === 'text').map(e => e.text);
  for (const n of ['Temperature (°C)', 'Pressure (inHg)', 'Landing Weight (lb)']) assert.ok(fields.includes(n), n + ' in ' + fields.join('; '));
  for (const u of ['°C', 'inHg', 'lb']) assert.ok(!els.some(e => e.kind === 'static' && e.text === u), u + ' read as a loose line');
});

test('a row of plain spans reads as one line, with a space before the unit', () => {
  const statics = scrape('perf-landing').filter(e => e.kind === 'static').map(e => e.text);
  assert.ok(statics.includes('Slope 0.1%'), statics.join('; '));
  assert.ok(statics.includes('Headwind 0 KT'), statics.join('; '));
  for (const frag of ['Slope', 'Headwind', '0KT', '0.1%']) assert.ok(!statics.includes(frag), frag + ' read alone');
});

test('spaceUnit separates a trailing letter unit from a number, and nothing else', () => {
  const { A } = load('charts-signedout');
  const cases = { '0KT': '0 KT', '----ft': '---- ft', '7587ft': '7587 ft', '0.1%': '0.1%', 'N/A': 'N/A', 'RW06L': 'RW06L', '34000 ft': '34000 ft', 'MIN': 'MIN' };
  for (const [i, o] of Object.entries(cases)) assert.equal(A.spaceUnit(i), o, i);
});

// A unit box is a short text-only <div> after a field, but so is a short number, and a number is a
// VALUE. Taken as a unit, it renamed the field "Gross Weight (507.9)" and, claimed as the field's
// caption, was never read at all.
test('a number beside a field is never its unit', () => {
  const els = scrape('unit-numeric', { autoVis: true });
  assert.deepStrictEqual(els.filter(e => e.controlType === 'text').map(e => e.text), ['Gross Weight', 'Temperature (°C)']);
  assert.ok(els.some(e => e.kind === 'static' && e.text === '507.9'), 'the number reads as its own line: ' + JSON.stringify(els.map(e => e.text)));
  assert.ok(!els.some(e => e.kind === 'static' && e.text === '°C'), 'the real unit is still folded into its field');
});
