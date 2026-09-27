'use strict';
const { test } = require('node:test');
const assert = require('node:assert');
const { load, scrape } = require('./run');

const statics = fx => scrape(fx).filter(e => e.kind === 'static').map(e => e.text);

test('landing results read as one line each, with a space before the unit', () => {
  const s = statics('perf-landing');
  for (const l of ['Estimated Landing Distance: ---- ft', 'Stop Distance Available: 7587 ft', 'Landing Distance Available: 7887 ft']) assert.ok(s.includes(l), l + ' in ' + s.join('; '));
  for (const frag of ['Estimated Landing Distance', '----ft', '7587ft', '7887ft']) assert.ok(!s.includes(frag), frag + ' read alone');
});

test('the Dispatch fuel and weight grid reads "Label: value"', () => {
  const els = scrape('dispatch');
  const s = els.filter(e => e.kind === 'static').map(e => e.text);
  const expect = ['Block Fuel: 77347 lbs', 'Enroute Fuel: 59634 lbs', 'Contingency Fuel: 4408 lbs', 'Taxi Fuel: 2000 lbs',
    'Extra + ETOPS Fuel: 0 lbs', 'Alternate Fuel: 7738 lbs', 'Reserve Fuel: 3567 lbs', 'Estimated ZFW: 430583 lbs',
    'Estimated TOW: 505930 lbs', 'Estimated LW: 446296 lbs'];
  for (const l of expect) assert.ok(s.includes(l), l + ' in ' + s.join('; '));
  assert.ok(!els.some(e => e.kind === 'heading' && e.text === 'Block Fuel'), 'the grid caption is no longer a heading');
});

test('a locked field is a read-out line, never an edit box (Payload summary in flight)', () => {
  const els = scrape('payload-locked');
  const s = els.filter(e => e.kind === 'static').map(e => e.text);
  for (const l of ['Load: 35%', 'GW (x1000 LBS): 507.9', 'ZFW (x1000 LBS): 430.6', 'Fuel (x1000 LBS): 77.3', 'Payload (x1000 LBS): 182']) assert.ok(s.includes(l), l + ' in ' + s.join('; '));
  assert.ok(!els.some(e => e.controlType === 'text'), 'no edit boxes on the locked summary');
});

test('a disabled input met outside a row still reads as a read-out', () => {
  const els = scrape('readout-loose', { autoVis: true });
  assert.ok(els.some(e => e.kind === 'static' && e.text === 'Note: 42'), JSON.stringify(els));
  assert.ok(!els.some(e => e.controlType === 'text'));
});

test('only a text field is a read-out: a disabled checkbox or range stays its own control', () => {
  const els = scrape('readout-types', { autoVis: true });
  assert.ok(els.some(e => e.kind === 'static' && e.text === 'Landing Weight: 507900'), JSON.stringify(els.map(e => e.kind + '|' + e.text)));

  const cb = els.find(e => e.controlType === 'checkbox');
  assert.ok(cb, 'the disabled checkbox is still a checkbox');
  assert.equal(cb.value, 'true');
  const rg = els.find(e => e.controlType === 'range');
  assert.ok(rg, 'the disabled range is still a range');
  assert.equal(rg.value, '80');

  for (const t of ['Deflected Ailerons: on', 'Screen Brightness: 80'])
    assert.ok(!els.some(e => e.kind === 'static' && e.text === t), t + ' read as a read-out');
});

// A read-out's label carries its value, so the label is no identity. The caption is the key, and a
// changed value is patched into the same line. (A harness edit is no React render, so the test
// forces the full scrape the dirty gate would run.)
test('a read-out keeps its key across a value change', () => {
  const { A, document } = load('payload-locked');
  assert.equal(JSON.parse(A.scrape()).elements.find(e => e.text === 'Load: 35%').key, 'readout:Load');
  [...document.querySelectorAll('label')].find(l => l.textContent.trim() === 'Load').nextElementSibling.value = '40%';
  A._dirty = true;
  assert.equal(JSON.parse(A.scrape()).elements.find(e => e.key === 'readout:Load').text, 'Load: 40%');
});

test('every read-out shape is keyed by its caption', () => {
  const keyFor = (fx, text, opts) => scrape(fx, opts).find(e => e.text === text).key;
  assert.equal(keyFor('perf-landing', 'Estimated Landing Distance: ---- ft'), 'readout:Estimated Landing Distance');   // pair row
  assert.equal(keyFor('payload-locked', 'GW (x1000 LBS): 507.9'), 'readout:GW (x1000 LBS)');                          // read-out row
  assert.equal(keyFor('readout-loose', 'Note: 42', { autoVis: true }), 'readout:Note');                                // a locked field met loose
});
