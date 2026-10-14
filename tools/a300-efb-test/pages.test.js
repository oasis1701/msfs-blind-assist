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
  const rwy = find(r, 'Runway 18L: 3,000 meters, 9,843 feet');
  assert.ok(rwy && rwy.kind === 'button');
  assert.ok(find(r, 'Runway 09: 1,500 meters, 4,921 feet, marked short'));
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

// Both pages read in the tablet's own words, abbreviations and units (Robin, 2026-10-14): OAT, AIR
// COND, TORA, hpa and a wind of direction/speed are the aircraft's and aviation's own terms, and a
// pilot knows them. Never rename them.
test('Take Off and Landing read in the tablet\'s own words', () => {
  const fieldsOf = r => r.elements.filter(e => e.controlType === 'text').map(e => e.text);
  const set = (doc, id, v) => { doc.getElementById(id).value = v; };
  const { A: to, window: tw } = load('takeoff');
  assert.deepStrictEqual(fieldsOf(scrape(to)),
    ['TORA (M)', 'HDG (°)', 'WIND', 'OAT (°C)', 'ALTIMETER (hpa)', 'WEIGHT (kg)']);
  // The values of a live calculation (KMEM 09, 2026-10-14).
  [['output_flex', '63'], ['output_v1', '137'], ['output_vr', '140'], ['output_v2', '144'], ['output_mtow', '152.1'],
    ['output_gwcg', '23.9%'], ['output_ths', '0.7UP']].forEach(p => set(tw.document, p[0], p[1]));
  const r = scrape(to);
  assert.deepStrictEqual(r.elements.filter(e => /^readout:output_/.test(e.key)).map(e => e.text),
    ['FLEX (°C): 63', 'V1: 137', 'VR: 140', 'V2: 144', 'MTOW [PERF]: 152.1', 'GWCG: 23.9%', 'THS: 0.7UP']);
  assert.ok(texts(r).includes('AIR COND: ON'));

  const { A: land, window: lw } = load('landing');
  assert.deepStrictEqual(fieldsOf(scrape(land)),
    ['LENGTH (M)', 'HDG (°)', 'WIND', 'TEMP (°C)', 'WEIGHT', 'EXTRA SPEED (Kts)']);
  // A live calculation (KBOS 04L, 2026-10-14).
  set(lw.document, 'land_output_dist', '1200');
  set(lw.document, 'land_output_fdist', '1440');
  const l = scrape(land);
  assert.deepStrictEqual(l.elements.filter(e => /^readout:land_output_/.test(e.key)).map(e => e.text),
    ['DIST (M): 1200', 'FACTORED (M): 1440']);
  for (const t of ['COND: DRY', 'CONFIG: 15/20', 'BRAKE: MANUAL']) assert.ok(texts(l).includes(t), t);
  assert.ok(l.elements.some(e => e.kind === 'heading' && e.text === 'Conditions Contd.'));
});

test('the landing diagram is not read: its two marks are the distances above', () => {
  const r = scrape(load('landing').A);
  assert.ok(!r.elements.some(e => /^(LD|FLD|Landing Performance)$/.test(e.text)));
});

// Landing's temperature, weight and extra speed are text boxes the tablet reads with parseInt, so
// "99,934" was 99 kg and a landing distance of 500 m where 99934 gave 1,200 m (measured live,
// 2026-10-14). They take a typed number the way a number box does.
test('Landing\'s number boxes take the number the pilot meant', () => {
  const { A, window } = load('landing');
  const doc = window.document;
  const idxOf = id => scrape(A).elements.find(e => e.key === 'field:' + id).idx;
  assert.strictEqual(A.setValue(idxOf('land_input_weight'), '99,934'), true);
  assert.strictEqual(doc.getElementById('land_input_weight').value, '99934');
  assert.strictEqual(A.setValue(idxOf('land_input_temp'), '-2,5'), true);
  assert.strictEqual(doc.getElementById('land_input_temp').value, '-2.5');
  assert.strictEqual(A.setValue(idxOf('land_input_vls'), '5 kt'), false, 'not a number: refused');
  assert.strictEqual(doc.getElementById('land_input_vls').value, '0', 'and left as it was');
  // The wind is typed as the tablet splits it, and kept as typed.
  assert.strictEqual(A.setValue(idxOf('land_input_wind'), '040/08'), true);
  assert.strictEqual(doc.getElementById('land_input_wind').value, '040/08');
});

// The RWY button opens the runway list ABOVE the page, away from the button, so a screen reader
// heard nothing happen when it was pressed (PR #300 in-sim run, 2026-10-14). It is read as a
// choice of the listed runways instead.
const pickerOf = (r, id) => r.elements.find(e => e.key === 'runway:' + id);
const TAKEOFF_RUNWAYS = ['Runway 18L: 3,000 meters, 9,843 feet', 'Runway 09: 1,500 meters, 4,921 feet, marked short'];
const LANDING_RUNWAYS = ['Runway 04R: 3,090 meters, 10,138 feet', 'Runway 22R: 2,391 meters, 7,844 feet',
  'Runway 15L: 785 meters, 2,575 feet, marked short'];

test('the runway button is a choice of the listed runways', () => {
  const r = scrape(load('takeoff').A);
  const pick = pickerOf(r, 'show_runway_select');
  assert.ok(pick, 'the picker is there');
  assert.strictEqual(pick.text, 'Runway');
  assert.strictEqual(pick.controlType, 'select');
  assert.ok(pick.idx > 0, 'stamped, so a choice can be written');
  assert.strictEqual(pick.value, 'None selected', 'the tablet says SELECT until a runway is picked');
  assert.deepStrictEqual(pick.options, ['None selected'].concat(TAKEOFF_RUNWAYS));
  assert.ok(!r.elements.some(e => e.kind === 'button' && /^RWY\b/.test(e.text)), 'no RWY button besides it');
});

test('the picked runway is the choice\'s value, from its own page\'s list', () => {
  // Take Off, hidden behind Landing, has a 04R of its own: only Landing's list is offered.
  const pick = pickerOf(scrape(load('landing').A), 'land_show_runway_select');
  assert.strictEqual(pick.value, 'Runway 22R: 2,391 meters, 7,844 feet');
  assert.deepStrictEqual(pick.options, LANDING_RUNWAYS, 'no placeholder once a runway is picked');
});

// Wires the fixture's Landing page the way the tablet does (its show and selectRunway handlers,
// read 2026-10-14): RWY opens the list; a runway button closes it and names its runway on RWY.
// Returns the presses in order; a press on Take Off's hidden list is marked as such.
function wireLanding(doc) {
  const presses = [];
  const panel = doc.getElementById('land_runwayPanel'), show = doc.getElementById('land_show_runway_select');
  show.onclick = () => { presses.push('open'); panel.removeAttribute('data-display'); };
  for (const b of doc.querySelectorAll('.land-rwy-select')) {
    b.onclick = () => {
      presses.push(b.id + (panel.hasAttribute('data-display') ? ' with the list closed' : ''));
      panel.setAttribute('data-display', 'none');
      show.value = 'RWY ' + b.id;
    };
  }
  for (const b of doc.querySelectorAll('.rwy-select')) b.onclick = () => presses.push('Take Off ' + b.id);
  return presses;
}

test('picking a runway opens the list and presses that runway, as a finger does', () => {
  const { A, window } = load('landing');
  const presses = wireLanding(window.document);
  const pick = pickerOf(scrape(A), 'land_show_runway_select');
  assert.strictEqual(A.setValue(pick.idx, LANDING_RUNWAYS[0]), true);
  assert.deepStrictEqual(presses, ['open', '04R'], 'one press each, and never Take Off\'s 04R');
  assert.strictEqual(pickerOf(scrape(A), 'land_show_runway_select').value, LANDING_RUNWAYS[0]);
});

test('a runway the list does not hold, or the placeholder, presses nothing', () => {
  const { A, window } = load('takeoff');
  const doc = window.document;
  const presses = [];
  doc.getElementById('show_runway_select').onclick = () => presses.push('open');
  for (const b of doc.querySelectorAll('.rwy-select')) b.onclick = () => presses.push(b.id);
  const pick = pickerOf(scrape(A), 'show_runway_select');
  assert.strictEqual(A.setValue(pick.idx, 'None selected'), false);
  assert.strictEqual(A.setValue(pick.idx, 'Runway 36R: 3,000 meters, 9,843 feet'), false);
  assert.deepStrictEqual(presses, []);
  assert.strictEqual(doc.getElementById('show_runway_select').value, 'SELECT', 'the button keeps its words');
});

test('with no runways listed the RWY control stays the tablet\'s button', () => {
  // Before the tablet knows the airport its list is empty, and a choice of nothing helps no one.
  const { A, window } = load('takeoff');
  window.document.getElementById('runway_opts').innerHTML = '';
  const r = scrape(A);
  assert.ok(!pickerOf(r, 'show_runway_select'));
  const btn = find(r, 'RWY: SELECT');
  assert.ok(btn && btn.kind === 'button');
});

test('the tablet\'s "Runway Updated" is not read: the choice already says the new runway', () => {
  const { A, window } = load('landing');
  const doc = window.document;
  const toast = doc.createElement('div');
  toast.className = 'notie-container notie-alert notie-background-success';
  toast.innerHTML = '<div class="notie-textbox"><div class="notie-textbox-inner">Runway Updated</div></div>';
  doc.body.appendChild(toast);
  assert.ok(!scrape(A).elements.some(e => e.kind === 'alert'));
  doc.querySelector('.notie-textbox-inner').textContent = 'Unable to calculate';
  assert.deepStrictEqual(scrape(A).elements.filter(e => e.kind === 'alert').map(e => e.text), ['Unable to calculate'],
    'any other message still is');
});

// ---- Ground Equipment ----------------------------------------------------------------------

test('the slides\' color legend is not read, the states being in words on the slide buttons', () => {
  // The legend sits after the slide buttons: read as a heading it would title what follows it.
  const r = scrape(load('groundequip').A);
  assert.ok(!r.elements.some(e => /Emergency slides|Red - Armed/i.test(e.text)));
  assert.ok(r.elements.some(e => /^Door 1L slide/.test(e.text)));
});

test('a slide button says armed or disarmed from its color', () => {
  // Red is armed and green disarmed, the page's own legend. The harness reports an element's inline
  // background color as its computed one, so the color the fixture draws is what the reader sees.
  const { A, window } = load('groundequip');
  const slide = window.document.getElementById('door1Larmed');
  const label = () => texts(scrape(A)).find(t => /^Door 1L slide/.test(t));
  assert.strictEqual(label(), 'Door 1L slide: disarmed', 'the green 1L slide');
  slide.style.backgroundColor = 'rgb(255, 0, 0)';
  assert.strictEqual(label(), 'Door 1L slide: armed', 'red');
  slide.style.backgroundColor = 'rgba(0, 128, 0, 0.5)';
  assert.strictEqual(label(), 'Door 1L slide: disarmed', 'green with an alpha');
  slide.style.backgroundColor = 'rgb(128, 128, 128)';
  assert.strictEqual(label(), 'Door 1L slide', 'a color that is neither says nothing');
  slide.style.backgroundColor = '';
  assert.strictEqual(label(), 'Door 1L slide', 'no color says nothing');
});

// ---- Aircraft Maintenance ------------------------------------------------------------------

const WEAR = {};
for (const b of ['LFI', 'LFO', 'LRI', 'LRO', 'RFI', 'RFO', 'RRI', 'RRO']) WEAR['L:INI_Brake_Wear_Indicator_' + b + '|number'] = 0.05;
for (let t = 0; t < 8; t++) WEAR['L:INI_TIRE' + t + '_WEAR|number'] = 6;
WEAR['L:INI_TIRE0_NG_WEAR|number'] = 6;
WEAR['L:INI_TIRE1_NG_WEAR|number'] = 6;

test('wear is read from the tablet\'s indicators in words, one line each for brakes and tires', () => {
  const r = scrape(load('maintenance', { simvars: WEAR }).A);
  assert.ok(find(r, 'Brake wear: all 8 under 20 percent'));
  assert.ok(find(r, 'Tire wear: all 10 under 20 percent'));
});

test('uneven wear gives the most common band as a count and names the other wheels', () => {
  const sim = Object.assign({}, WEAR, {
    'L:INI_Brake_Wear_Indicator_RRO|number': 0.45, 'L:INI_Brake_Wear_Indicator_LFI|number': 0.25,
    'L:INI_TIRE1_NG_WEAR|number': 199, 'L:INI_TIRE6_WEAR|number': 240,
  });
  const r = scrape(load('maintenance', { simvars: sim }).A);
  assert.ok(find(r, 'Brake wear: 6 under 20 percent; left forward inner 20 to 40 percent; right rear outer 40 to 60 percent'));
  assert.ok(find(r, 'Tire wear: 8 under 20 percent; nose right 80 to 100 percent; left forward inner beyond the scale'));
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
