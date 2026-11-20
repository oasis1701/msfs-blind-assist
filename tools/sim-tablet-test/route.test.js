'use strict';
const { test } = require('node:test');
const assert = require('node:assert');
const { load, scrape, lines } = require('./run');

// The Flight Planner route primitives the app's SimBrief import drives (SimTabletSimBriefImport).

test('the Route page reads its airports, runways, procedures and waypoints', () => {
  const l = lines(scrape('atlas-route-loaded'));
  for (const s of [
    '/text|Departure airport=KFFC, Atlanta Rgnl Falcon ...',
    '/select|Departure airport runway=runway 31',
    '/text|Arrival airport=KSAV, Savannah/Hilton Head...',
    '/select|Arrival airport runway=runway 01',
    'static|SOONE: Hdg 138°, Dist 24.6 NM',
    'static|NOKIE: Hdg 135°, Dist 36.5 NM',
    'static|ILS 1 via Direct',
    'button|Send: SEND TO AVIONICS',
    'button|Import: ENTER ROUTE STRING',
    'heading|Route'
  ]) assert.ok(l.includes(s), s);
});

test('the waypoint lists\' column headers are never read as a line of their own', () => {
  const { A, document } = load('atlas-route-loaded');
  // Live, the two header rows' wrapper qualifies as a caption-over-value pair; give it the live
  // shape (exactly the two header rows on two rows) so this guards that case too.
  const wrap = document.querySelector('.waypoint-list-header').parentElement;
  for (const extra of [...wrap.children].slice(2)) extra.remove();
  const els = JSON.parse(A.scrape()).elements;
  assert.ok(!els.some(e => /mm:ss/.test(e.text)), lines(els).filter(x => /mm:ss/.test(x)).join('\n'));
});

test('route.state: the import reads the loaded plan back by structure', () => {
  const { A } = load('atlas-route-loaded');
  const st = JSON.parse(A.route.state());
  assert.equal(st.ok, true);
  assert.equal(st.atlas, true);
  assert.equal(st.routeShown, true);
  assert.deepStrictEqual(st.airports.slice(0, 2).map(a => a.icao + '/' + a.runwayKey), ['KFFC/31', 'KSAV/1']);
  assert.deepStrictEqual(st.airports[1].runways, ['runway 01', 'runway 10', 'runway 19', 'runway 28']);
  assert.deepStrictEqual(st.waypoints, ['SOONE', 'NOKIE']);
  assert.deepStrictEqual(st.procedures, ['None', 'None', 'ILS 1 via Direct']);
});

test('route.state reads the plan\'s cruise altitude from the Performance panel, with its shown warnings only', () => {
  const { A, document } = load('atlas-route-loaded');
  assert.equal(JSON.parse(A.route.state()).cruise, undefined, 'no Performance panel built yet: no reading');
  // The Performance panel's cruise row, as captured live (the fixture elides hidden panels).
  const stack = document.querySelector('.efb-view.AtlasApp .view.Route').parentElement;
  const perf = document.createElement('div');
  perf.className = 'view hidden Performance';
  perf.innerHTML = '<div class="atlas-data-table-row multi-row"><div class="atlas-data-table-row">' +
    '<div class="atlas-data-table-row-title"><span>Cruise altitude</span></div>' +
    '<div class="atlas-data-table-row-dropdown"><div class="dropdown-button"><button class="dropdown-button-selector"><span class="text bold-text">FL</span></button></div></div>' +
    '<div class="atlas-data-table-row-value"><div class="unitbox"><div class="textbox"><input type="number" value="30"></div></div></div></div>' +
    '<div class="cruise-altitude-ceiling"><div class="value">FL 140</div></div>' +
    '<div class="warning hidden"><span>The selected cruise altitude is higher than the ceiling of the aircraft.</span></div>' +
    '<div class="warning"><span>The selected cruise altitude is below the departure or destination altitude.</span></div></div>';
  stack.appendChild(perf);
  const st = JSON.parse(A.route.state());
  assert.equal(st.cruise, '30');
  assert.equal(st.cruiseUnit, 'FL');
  assert.deepStrictEqual(st.cruiseWarnings, ['The selected cruise altitude is below the departure or destination altitude.']);
});

test('route.state off the Flight Planner says so and lists nothing', () => {
  const { A } = load('home');
  const st = JSON.parse(A.route.state());
  assert.equal(st.atlas, false);
  assert.deepStrictEqual(st.airports, []);
});

test('route.open from another app presses the Flight Planner dock button', () => {
  const { A, window } = load('home');
  assert.deepStrictEqual(JSON.parse(A.route.open()), { ok: true, done: false });
  assert.deepStrictEqual(window.__presses, ['click:']);
});

test('route.open on the Route panel is done', () => {
  const { A, window } = load('atlas-route-loaded');
  assert.deepStrictEqual(JSON.parse(A.route.open()), { ok: true, done: true });
  assert.deepStrictEqual(window.__presses, []);
});

test('route.enter opens ENTER ROUTE STRING by its position in the Import menu and types without focus', () => {
  const { A, window, document } = load('atlas-route-string');
  const ta = document.querySelector('.route-parsing-textarea');
  let inputs = 0;
  ta.addEventListener('input', () => inputs++);
  assert.deepStrictEqual(JSON.parse(A.route.enter('KFFC DCT SOONE DCT NOKIE DCT KSAV')), { ok: true });
  assert.deepStrictEqual(window.__presses, ['click:ENTER ROUTE STRING']);
  assert.equal(ta.value, 'KFFC DCT SOONE DCT NOKIE DCT KSAV');
  assert.equal(inputs, 1);
  assert.notEqual(document.activeElement, ta);
});

test('route.enter on a box already holding the route closes it and says unchanged (SAVE would never enable)', () => {
  const { A, window, document } = load('atlas-route-string');
  const ta = document.querySelector('.route-parsing-textarea');
  ta.value = 'KFFC DCT SOONE DCT NOKIE DCT KSAV';          // the box opens holding the plan's route
  let inputs = 0;
  ta.addEventListener('input', () => inputs++);
  assert.deepStrictEqual(JSON.parse(A.route.enter('kffc  dct soone dct nokie dct ksav')), { ok: true, unchanged: true });
  assert.deepStrictEqual(window.__presses, ['click:ENTER ROUTE STRING', 'click:'], 'opened, then the header close');
  assert.equal(inputs, 0, 'nothing typed');
});

test('route.closeBox presses the route box header close, and is done when no box is open', () => {
  const { A, window } = load('atlas-route-string');
  assert.deepStrictEqual(JSON.parse(A.route.closeBox()), { ok: true, done: true });
  assert.deepStrictEqual(window.__presses, ['click:']);
  const closed = load('atlas-route-loaded');
  assert.deepStrictEqual(JSON.parse(closed.A.route.closeBox()), { ok: true, done: true });
  assert.deepStrictEqual(closed.window.__presses, []);
});

test('route.enter refuses a locked plan instead of pressing a dead item', () => {
  const { A, window, document } = load('atlas-route-string');
  const menu = A.route.menu('Folder');
  menu.querySelectorAll('.dropdown-button-list-item')[2].setAttribute('disabled', '');
  assert.deepStrictEqual(JSON.parse(A.route.enter('KFFC KSAV')), { ok: false, error: 'locked' });
  assert.deepStrictEqual(window.__presses, []);
});

test('route.save refuses while the route box is empty (SAVE disabled) and presses it once enabled', () => {
  const { A, window, document } = load('atlas-route-string');
  assert.deepStrictEqual(JSON.parse(A.route.save()), { ok: false, error: 'save-disabled' }, 'captured with an empty box');
  assert.deepStrictEqual(window.__presses, []);
  // What the OS does once the box has text: the button's disabled layer goes away.
  document.querySelector('.route-parsing-popup-button .disabled-layer').removeAttribute('data-vis');
  assert.deepStrictEqual(JSON.parse(A.route.save()), { ok: true });
  assert.deepStrictEqual(window.__presses, ['click:SAVE']);
});

test('route.setRunway matches the designator, not the word: SimBrief "01" and "1" both pick "runway 01"', () => {
  for (const want of ['01', '1', 'RW01']) {
    const { A, window } = load('atlas-route-loaded');
    assert.deepStrictEqual(JSON.parse(A.route.setRunway(1, want)), { ok: true, picked: 'runway 01' }, want);
    assert.deepStrictEqual(window.__presses, ['click:runway 01']);
  }
  const { A } = load('atlas-route-loaded');
  assert.deepStrictEqual(JSON.parse(A.route.setRunway(1, '05')), { ok: false, error: 'no-such-runway' });
  assert.deepStrictEqual(JSON.parse(A.route.setRunway(0, '31')).picked, 'runway 31');
});

test('rwyKey keeps the side letter', () => {
  const { A } = load('home');
  assert.equal(A.route.rwyKey('runway 16L'), '16L');
  assert.equal(A.route.rwyKey('16l'), '16L');
  assert.equal(A.route.rwyKey('runway 09'), '9');
  assert.equal(A.route.rwyKey(''), '');
});
