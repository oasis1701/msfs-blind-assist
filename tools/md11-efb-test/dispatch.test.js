'use strict';
const { test } = require('node:test');
const assert = require('node:assert');
const { load, scrape, lines } = require('./run');

test('the Dispatch flight header reads as labelled lines, then the fuel grid, then the six actions', () => {
  assert.deepStrictEqual(lines(scrape('dispatch')).slice(7), [
    'static|Flight BVI2GP', 'static|Aircraft TFDi MD-11F GE (TFDI-MD11)',
    'static|From KMEM', 'static|Block time 03:50 (air 03:22)', 'static|Cruise 34000 ft (CI 20)', 'static|To KLAX',
    'static|Departure 23:35 UTC', 'static|Arrival 3:25 UTC', 'static|Alternate KONT',
    'static|Route CHLDR5 ANSWA DCT LIT DCT FUZ J4 WLVRN DCT ESTWD HLYWD1',
    'static|Block Fuel: 77347 lbs', 'static|Enroute Fuel: 59634 lbs', 'static|Contingency Fuel: 4408 lbs', 'static|Taxi Fuel: 2000 lbs',
    'static|Extra + ETOPS Fuel: 0 lbs', 'static|Alternate Fuel: 7738 lbs', 'static|Reserve Fuel: 3567 lbs', 'static|Estimated ZFW: 430583 lbs',
    'static|Estimated TOW: 505930 lbs', 'static|Estimated LW: 446296 lbs',
    'button|Send flight plan to FMC', 'button|Set payload & fuel', 'button|View departure airport charts',
    'button|View arrival airport charts', 'button|Reload flight plan', 'button|View OFP']);
});

test('the same header rows are left to the generic reading when Dispatch is not the active page', () => {
  const { A, document } = load('dispatch');
  // The EFB marks the active nav tab with bg-red-800. Move that mark from Dispatch to Perf in the
  // captured DOM, so the very same header rows sit under a non-Dispatch page.
  const active = document.querySelectorAll('button.bg-red-800');
  assert.equal(active.length, 1);
  active[0].classList.replace('bg-red-800', 'bg-zinc-600');
  const perf = Array.from(document.querySelectorAll('button')).find(b => b.textContent.trim() === 'Perf');
  perf.classList.replace('bg-zinc-600', 'bg-red-800');
  const ls = lines(JSON.parse(A.scrape()).elements);
  assert.ok(ls.includes('tab|Perf (current page)'), ls.slice(0, 8).join('; '));
  // The ten labelled header lines of the first test must all be absent …
  const HEADER = ['static|Flight BVI2GP', 'static|Aircraft TFDi MD-11F GE (TFDI-MD11)', 'static|From KMEM',
    'static|Block time 03:50 (air 03:22)', 'static|Cruise 34000 ft (CI 20)', 'static|To KLAX',
    'static|Departure 23:35 UTC', 'static|Arrival 3:25 UTC', 'static|Alternate KONT',
    'static|Route CHLDR5 ANSWA DCT LIT DCT FUZ J4 WLVRN DCT ESTWD HLYWD1'];
  for (const h of HEADER) assert.ok(!ls.includes(h), h + ' must not appear off the Dispatch page');
  // … and the values fall back to the generic reading (unlabelled headings), while the fuel grid,
  // which is not Dispatch-gated, still reads as before.
  assert.ok(ls.includes('heading|BVI2GP'), 'the callsign falls back to the generic heading');
  assert.ok(ls.includes('heading|KMEM'), 'the origin falls back to the generic heading');
  assert.ok(ls.includes('static|Block Fuel: 77347 lbs'), 'the fuel grid is unaffected by the page gate');
});
