'use strict';
// The A300 tablet's pages, swept live on 2026-10-04: what each page shows a sighted pilot that the
// reader dropped, split up or read as symbols. The tablet's own words are kept; only what a screen
// reader would mangle or could not reach is put into words.
const test = require('node:test');
const assert = require('node:assert');
const { load, scrape, find } = require('./run');

const texts = r => r.elements.map(e => e.text);

// ---- My Flight -----------------------------------------------------------------------------

test('the checklist viewer says it is a picture and offers to close it', () => {
  const r = scrape(load('myflight-checklist').A);
  assert.strictEqual(r.page, 'My Flight, checklist');
  assert.deepStrictEqual(texts(r), ['The checklist is a picture and cannot be read here.', 'Close checklist']);
});

test('the flight plan viewer gives the plan with its lines kept', () => {
  const r = scrape(load('myflight-ofp').A);
  assert.strictEqual(r.page, 'My Flight, flight plan');
  const pre = r.elements[0];
  assert.strictEqual(pre.controlType, 'pre');
  assert.strictEqual(pre.kind, 'static');
  assert.strictEqual(pre.text, '[ OFP ]\nEGLL-EHAM   TST101\n\nBLOCK FUEL   6200');
  assert.deepStrictEqual(texts(r).slice(1), ['Close flight plan']);
});

test('a viewer on a page that is not showing is not open', () => {
  const r = scrape(load('myflight-hidden-ofp').A);
  assert.strictEqual(r.page, 'Home');
});

// ---- Take Off / Landing --------------------------------------------------------------------

test('the runway list reads each runway in words, marking the ones shown in red', () => {
  const r = scrape(load('takeoff').A);
  assert.ok(find(r, 'Cancel runway selection'), 'the Cancel button says what it cancels');
  const rwy = find(r, 'Runway 18L: 3,000 metres, 9,843 feet');
  assert.ok(rwy && rwy.kind === 'button');
  assert.ok(find(r, 'Runway 09: 1,500 metres, 4,921 feet, marked short'));
});

test('arrows are dropped from a button\'s words', () => {
  assert.ok(find(scrape(load('takeoff').A), 'CALCULATE'));
});

test('the results are read-outs, not fields', () => {
  const r = scrape(load('takeoff').A);
  const flex = find(r, 'FLEX (°C): 45');
  assert.ok(flex, 'value with its unit');
  assert.strictEqual(flex.kind, 'static');
  assert.ok(!flex.controlType);
  assert.strictEqual(flex.key, 'readout:output_flex');
  assert.ok(find(r, 'V1: blank'), 'not calculated yet');
});

// ---- Ground Equipment ----------------------------------------------------------------------

test('the slides\' colour legend is not read, the states being in words on the slide buttons', () => {
  // The legend sits after the slide buttons: read as a heading it would title what follows it.
  const r = scrape(load('groundequip').A);
  assert.ok(!r.elements.some(e => /Emergency slides|Red - Armed/i.test(e.text)));
  assert.ok(r.elements.some(e => /^Door 1L slide/.test(e.text)));
});

// ---- Aircraft Maintenance ------------------------------------------------------------------

const WEAR = {};
for (const b of ['LFI', 'LFO', 'LRI', 'LRO', 'RFI', 'RFO', 'RRI', 'RRO']) WEAR['L:INI_Brake_Wear_Indicator_' + b + '|number'] = 0.05;
for (let t = 0; t < 8; t++) WEAR['L:INI_TIRE' + t + '_WEAR|number'] = 6;
WEAR['L:INI_TIRE0_NG_WEAR|number'] = 6;
WEAR['L:INI_TIRE1_NG_WEAR|number'] = 6;

test('wear is read from the tablet\'s indicators in words, one line each for brakes and tyres', () => {
  const r = scrape(load('maintenance', { simvars: WEAR }).A);
  assert.ok(find(r, 'Brake wear: all 8 under 20 percent'));
  assert.ok(find(r, 'Tyre wear: all 10 under 20 percent'));
});

test('uneven wear gives the most common band as a count and names the other wheels', () => {
  const sim = Object.assign({}, WEAR, {
    'L:INI_Brake_Wear_Indicator_RRO|number': 0.45, 'L:INI_Brake_Wear_Indicator_LFI|number': 0.25,
    'L:INI_TIRE1_NG_WEAR|number': 199, 'L:INI_TIRE6_WEAR|number': 240,
  });
  const r = scrape(load('maintenance', { simvars: sim }).A);
  assert.ok(find(r, 'Brake wear: 6 under 20 percent; left forward inner 20 to 40 percent; right rear outer 40 to 60 percent'));
  assert.ok(find(r, 'Tyre wear: 8 under 20 percent; nose right 80 to 100 percent; left forward inner beyond the scale'));
});

test('oil and hydraulic levels read as one line each', () => {
  const r = scrape(load('maintenance', { simvars: WEAR }).A);
  for (const t of ['APU oil: 100%', 'Engine 1 oil: 97%', 'Engine 2 oil: 100%',
    'Blue hydraulic reservoir: 14.5', 'Green hydraulic reservoir: 31.5', 'Yellow hydraulic reservoir: 19.5'])
    assert.ok(find(r, t), t);
  assert.ok(!find(r, 'ENG1') && !find(r, 'B'), 'no orphaned labels');
});

test('the maintenance panels say whether they are open', () => {
  const sim = Object.assign({}, WEAR, { 'L:INI_Fuel_Panel|number': 1, 'L:INI_APU_Cowl_TGT|number': 0 });
  const r = scrape(load('maintenance', { simvars: sim }).A);
  const fuel = find(r, 'Fuel Panel: open');
  assert.ok(fuel && fuel.announceChange === true && fuel.key === 'press:fuel_panel');
  assert.ok(find(r, 'Eng Cowl L: closed'));
  assert.ok(find(r, 'Eng Cowl R: closed'));
  assert.ok(find(r, 'APU Cowl: closed'));
  assert.ok(find(r, 'Stow RAT'), 'a one-way action keeps its words');
});

// ---- Throttle Calibration ------------------------------------------------------------------

test('the throttles read as positions, without the picture\'s scale words', () => {
  const r = scrape(load('throttle').A);
  assert.ok(find(r, 'Left throttle: 50%'));
  assert.ok(find(r, 'Right throttle: 48%'));
  assert.ok(!find(r, 'TOGA') && !find(r, 'IDLE'));
});

test('the calibration instruction is spoken as it changes', () => {
  const e = find(scrape(load('throttle').A), 'Move both throttles to TOGA and press Set TOGA Position.');
  assert.ok(e);
  assert.strictEqual(e.live, 'polite');
});

// ---- Settings ------------------------------------------------------------------------------

test('the maintenance mode buttons say which is chosen', () => {
  const r = scrape(load('settings-maint').A);
  assert.ok(find(r, 'Maintenance system is currently set to: REALISTIC'), 'one line');
  const real = find(r, 'Realistic (selected)');
  assert.ok(real && real.announceChange === true && real.key === 'press:maint_real');
  assert.ok(find(r, 'Disabled') && find(r, 'Fast'));
});

// ---- Charts --------------------------------------------------------------------------------

test('the Charts page is named and says the charts are pictures', () => {
  const r = scrape(load('charts').A);
  assert.strictEqual(r.page, 'Charts');
  assert.strictEqual(r.elements[0].text, 'Charts are pictures and cannot be read here; the list gives their names.');
  assert.ok(find(r, 'Airport'), 'the search box');
});

test('each chart is a button with its name and kind, and its pin says what it does', () => {
  const { A, window } = load('charts');
  const r = scrape(A);
  const c = find(r, 'CONDR 4 RNAV [ATC], STAR');
  assert.ok(c && c.kind === 'button');
  let opened = 0;
  window.document.querySelector('[data-guid="g1"]').onclick = () => opened++;
  assert.ok(A.clickElement(c.idx));
  assert.strictEqual(opened, 1);
  const pin = find(r, 'Pin CONDR 4 RNAV [ATC]');
  assert.ok(pin && pin.key === 'pin:g1' && pin.announceChange === true);
  assert.ok(find(r, 'Unpin ELVIS 4'), 'a pinned chart');
  assert.ok(!find(r, 'CONDR 4 RNAV [ATC]'), 'no separate heading for the name');
});

test('a chart named after its kind says it once', () => {
  const { A, window } = load('charts');
  const row = window.document.querySelector('[data-guid="g1"]');
  row.querySelector('h1').textContent = 'AFC';
  row.querySelector('p').textContent = 'AFC ';
  const r = scrape(A);
  assert.ok(find(r, 'AFC') && find(r, 'AFC').kind === 'button');
  assert.ok(!find(r, 'AFC, AFC'));
});

test('the chart controls are named', () => {
  const r = scrape(load('charts').A);
  for (const t of ['Pinned charts', 'Zoom in', 'Pan up', 'Previous chart page', 'Chart page 1 of 2', 'Next chart page'])
    assert.ok(find(r, t), t);
});

// ---- Enroute Map ---------------------------------------------------------------------------

test('the Enroute Map is named, says it is a picture, and names its buttons', () => {
  const r = scrape(load('enroute').A);
  assert.strictEqual(r.page, 'Enroute Map');
  assert.strictEqual(r.elements[0].text, 'The map is a picture and cannot be read here.');
  for (const t of ['Map type', 'Zoom in', 'Zoom out']) assert.ok(find(r, t), t);
  assert.ok(!find(r, 'ENROUTE'), 'the title is the page name');
});
