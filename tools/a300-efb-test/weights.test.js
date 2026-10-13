'use strict';
// The A300 freighter's Weight and Balance page. It has two halves that slide by 100vw: payload
// selection (preset loads) and loading (the hold weights, fuel, the Update and Apply buttons and
// the results). Choosing a preset fills the hold weights and slides to the loading half; nothing
// goes on board until Apply Load to Aircraft (read from the tablet's own code, 2026-10-04).
const test = require('node:test');
const assert = require('node:assert');
const { load, scrape, texts, find } = require('./run');

// Payload stations 1 and 2 are the pilots; 3 to 8 are the holds and cabin zones.
const KG = {
  'PAYLOAD STATION WEIGHT:1|kilograms': 82, 'PAYLOAD STATION WEIGHT:2|kilograms': 84,
  'PAYLOAD STATION WEIGHT:3|kilograms': 0, 'PAYLOAD STATION WEIGHT:4|kilograms': 0, 'PAYLOAD STATION WEIGHT:5|kilograms': 0,
  'PAYLOAD STATION WEIGHT:6|kilograms': 10333, 'PAYLOAD STATION WEIGHT:7|kilograms': 10333, 'PAYLOAD STATION WEIGHT:8|kilograms': 10334,
  'FUEL TOTAL QUANTITY WEIGHT|kilograms': 60137.4,
  'L:INI_IS_REFUELING|number': 0,
};

const buttons = r => r.elements.filter(e => e.kind === 'button').map(e => e.text);

test('the selection half reads only its own controls, with the loads in words', () => {
  const { A } = load('weights-selection', { simvars: KG });
  const r = scrape(A);
  assert.strictEqual(r.page, 'Weight and Balance, payload selection');
  assert.deepStrictEqual(buttons(r), [
    'Racing Team Charter: 31,000 kilograms, 68,300 pounds',
    'Aero Parts Transport: 26,500 kilograms, 58,400 pounds',
    'Horse Stable Transport: 15,700 kilograms, 34,600 pounds',
    'Humanitarian Charter: 21,000 kilograms, 46,300 pounds',
    'Postal Freight: 18,500 kilograms, 40,800 pounds',
    'Custom cargo',
    'Cargo from SimBrief',
    'Unload Cargo',
    'Home',
    'Control box',
  ]);
  assert.ok(!texts(r).some(t => /^(Apply Load|Total fuel|Gross weight)/.test(t)), 'the loading half is off screen');
});

test('the selection half says what choosing a load does', () => {
  const { A } = load('weights-selection', { simvars: KG });
  const r = scrape(A);
  // Unload Cargo only clears the cargo models (the INI_LOAD_*_SHOW visuals) and says "Payload
  // Removed"; the station weights stay on board (clearPayload, read 2026-10-04).
  const help = 'Choosing a load fills in the hold weights and opens the loading page. Nothing goes on board until you press Apply Load to Aircraft there. ' +
    'Unload Cargo only removes the cargo you can see in the hold; to take its weight off, set the holds to 0 on the loading page and apply.';
  assert.ok(find(r, help), 'help line');
  assert.ok(texts(r).indexOf(help) < texts(r).indexOf('Racing Team Charter: 31,000 kilograms, 68,300 pounds'), 'help before the loads');
});

test('the forward chevron is a button once a load has been chosen', () => {
  assert.ok(!find(scrape(load('weights-selection', { simvars: KG }).A), 'Loading page'), 'hidden before any load');
  const { A, window } = load('weights-selection-chosen', { simvars: KG });
  const r = scrape(A);
  const fwd = find(r, 'Loading page');
  assert.ok(fwd && fwd.kind === 'button' && fwd.clickable);
  let clicked = 0;
  window.document.getElementById('switcher_page2').addEventListener('click', () => clicked++);
  assert.ok(A.clickElement(fwd.idx));
  assert.strictEqual(clicked, 1, 'one click event');
});

test('the loading half reads its fields by what they hold', () => {
  const { A } = load('weights-loading', { simvars: KG });
  const r = scrape(A);
  assert.strictEqual(r.page, 'Weight and Balance, loading');
  const fields = r.elements.filter(e => e.controlType === 'text').map(e => [e.text, e.value]);
  assert.deepStrictEqual(fields, [
    ['Total cargo (kg)', '0'],
    ['Total fuel (kg)', '60110'],
    ['Forward hold (kg)', '10333'],
    ['Middle hold (kg)', '10333'],
    ['Aft hold (kg)', '10333'],
  ]);
  assert.ok(!texts(r).some(t => /Racing Team Charter/.test(t)), 'the selection half is off screen');
});

test('the loading half has its buttons and the way back', () => {
  const { A, window } = load('weights-loading', { simvars: KG });
  const r = scrape(A);
  assert.deepStrictEqual(buttons(r), ['Back to payload selection', 'Update from SimBrief', 'Apply Load to Aircraft', 'Home', 'Control box']);
  let clicked = 0;
  window.document.getElementById('switcher_page1').addEventListener('click', () => clicked++);
  assert.ok(A.clickElement(find(r, 'Back to payload selection').idx));
  assert.strictEqual(clicked, 1);
});

test('the loading half says what its two buttons do', () => {
  const { A } = load('weights-loading', { simvars: KG });
  const r = scrape(A);
  assert.ok(find(r, 'Update from SimBrief fills in the cargo and fuel from your latest SimBrief plan. Apply Load to Aircraft puts the cargo and fuel on board.'));
  assert.ok(find(r, 'Weight and balance'), 'heading without the bracketed unit');
});

test('the results are read-outs in words, not fields', () => {
  const { A } = load('weights-loading', { simvars: KG });
  const r = scrape(A);
  const outs = [
    'Zero fuel weight centre of gravity: 0.0 percent MAC',
    'Gross weight centre of gravity: 24.95 percent MAC',
    'Zero fuel weight: 0.0 tonnes',
    'Gross weight: 102.40 tonnes',
    'Payload: 31.0 tonnes',
    'Block fuel: 60.11 tonnes',
  ];
  for (const t of outs) {
    const e = find(r, t);
    assert.ok(e, t);
    assert.strictEqual(e.kind, 'static', t);
    assert.ok(!e.controlType, t + ' is not a field');
  }
});

test('in pounds the results are thousands of pounds', () => {
  const lbs = {};
  for (const k of Object.keys(KG)) lbs[k.replace('|kilograms', '|pounds')] = KG[k];
  const { A } = load('weights-loading-lbs', { simvars: lbs });
  const r = scrape(A);
  assert.ok(find(r, 'Gross weight: 102.40 thousand pounds'));
  assert.ok(find(r, 'Total fuel (lbs)'));
  assert.ok(find(r, 'On board: payload 31,000 pounds, fuel 60,100 pounds'));
});

test('a live line says what is on board, on both halves', () => {
  for (const fixture of ['weights-selection', 'weights-loading']) {
    const { A } = load(fixture, { simvars: KG });
    const e = find(scrape(A), 'On board: payload 31,000 kilograms, fuel 60,100 kilograms');
    assert.ok(e, fixture);
    assert.strictEqual(e.live, 'polite', fixture);
    assert.strictEqual(e.key, 'status:onboard', fixture);
  }
});

test('while the tablet is fuelling the line says so instead of a moving number', () => {
  const sim = Object.assign({}, KG, { 'L:INI_IS_REFUELING|number': 1 });
  const { A } = load('weights-loading', { simvars: sim });
  assert.ok(find(scrape(A), 'On board: payload 31,000 kilograms, fuelling in progress'));
});

test('a tablet message is an alert the window speaks', () => {
  const { A } = load('weights-toast', { simvars: KG });
  const r = scrape(A);
  assert.deepStrictEqual(r.elements[0], { idx: 0, text: 'Payload Removed', value: '', kind: 'alert', clickable: false });
});

test('a preset is still pressed with one click', () => {
  const { A, window } = load('weights-selection', { simvars: KG });
  const r = scrape(A);
  let clicked = 0;
  window.document.getElementById('payload_horse').addEventListener('click', () => clicked++);
  assert.ok(A.clickElement(find(r, 'Horse Stable Transport: 15,700 kilograms, 34,600 pounds').idx));
  assert.strictEqual(clicked, 1);
});

test('My Flight still reads as before', () => {
  const { A } = load('myflight', {});
  const r = scrape(A);
  assert.strictEqual(r.page, 'My Flight');
  for (const t of ['Departure: EGLL', 'Departure city: London/Heathrow', 'Arrival: EHAM', 'IMPORT FROM SIMBRIEF', 'Home'])
    assert.ok(find(r, t), t);
});
