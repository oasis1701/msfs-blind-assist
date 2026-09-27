'use strict';
const { test } = require('node:test');
const assert = require('node:assert');
const { scrape, lines } = require('./run');

const TABS = (active) => ['Dispatch', 'Payload', 'Perf', 'Charts', 'Services', 'State', 'Options'].map(t => 'tab|' + t + (t === active ? ' (current page)' : ''));

test('Options / General, top to bottom', () => {
  assert.deepStrictEqual(lines(scrape('options-general')), [...TABS('Options'),
    'tab|General (selected)', 'tab|Systems', 'tab|CAWS', 'tab|Perf', 'tab|Comms', 'tab|Behavior',
    '/range|Screen Brightness=100', '/select|Weight Units=Imperial', '/select|Temperature Units=°C',
    '/select|MANPADS Defense System=Hidden', '/select|Pause at TOD=No', 'button|Save']);
});

test('Perf in flight (landing), top to bottom', () => {
  const els = scrape('perf-landing');
  assert.deepStrictEqual(lines(els), [...TABS('Perf'),
    'heading|Airport', '/text|ICAO code=KLAX', '/select|Runway=RW06L', 'static|Slope 0.1%', '/select|Runway Condition=DRY',
    '/text|Wind', 'static|Headwind 0 KT', '/text|Temperature (°C)', '/text|Pressure (inHg)', 'button|Get weather information',
    'heading|Aircraft', '/text|Landing Weight (lb)', '/select|Autobrake=MIN', '/select|Reversers=ALL', '/select|Flaps=35',
    'button|Get Landing Weight', 'static|RW06L', 'button|Calculate',
    'static|Estimated Landing Distance: ---- ft', 'static|Stop Distance Available: 7587 ft', 'static|Landing Distance Available: 7887 ft']);
  assert.equal(els.find(e => e.text === 'Calculate').disabled, true, 'Calculate is dimmed until the EFB enables it');
});

// Live capture, MD-11F on the ground at UCFM, 2026-09-06. The fixture already carries the real nav
// bar (like every other live capture), so it is loaded plain -- no autoVis, no synthetic nav.
test('Perf on the ground (takeoff), top to bottom', () => {
  assert.deepStrictEqual(lines(scrape('perf-takeoff')), [...TABS('Perf'),
    'heading|Airport', '/text|ICAO code=UCFM', '/select|Runway=RW07', 'static|Slope -0.3%', '/text|Runway Length (ft)=13793',
    '/text|Wind', 'static|Headwind 0 KT', '/text|Temperature (°C)', '/text|Pressure (hPA)', 'button|Get weather information',
    'heading|Aircraft', '/text|Takeoff Weight (lb)', '/select|Thrust Setting=FLEX', '/select|Anti-Ice=NONE', '/select|Flaps=Optimum',
    'static|Specific Flaps: (empty)', '/select|Packs=Off', 'button|Get Takeoff Weight', 'button|Calculate',
    'static|V1: ---', 'static|VR: ---', 'static|V2: ---', 'static|Flex Temperature: --- °C', 'static|Flaps: ---']);
});

// Live capture, MD-11F (freighter) at UCFM, 2026-09-06: the Passenger & Cargo tab carries no
// passenger rows at all on this airframe -- only Cargo and Fuel Quantity.
test('Payload on the ground (Passenger & Cargo tab), top to bottom', () => {
  assert.deepStrictEqual(lines(scrape('payload-form')), [...TABS('Payload'),
    'tab|Passenger & Cargo (selected)', 'tab|ZFW', '/text|Cargo (LBS)=0', '/text|Fuel Quantity (LBS)=33069', 'button|Set Payload']);
});

test('Payload ZFW tab, top to bottom', () => {
  assert.deepStrictEqual(lines(scrape('payload-zfw')), [...TABS('Payload'),
    'tab|Passenger & Cargo', 'tab|ZFW (selected)', '/text|ZFW (LBS)=248567', '/text|Fuel Quantity (LBS)=33069', 'button|Set Payload']);
});

test('Payload in flight (locked summary), top to bottom', () => {
  assert.deepStrictEqual(lines(scrape('payload-locked')), [...TABS('Payload'),
    'heading|This page cannot be used right now', "static|Please try again when you're on the ground",
    'static|Load: 35%', 'static|GW (x1000 LBS): 507.9', 'static|ZFW (x1000 LBS): 430.6', 'static|Fuel (x1000 LBS): 77.3',
    'static|Payload (x1000 LBS): 182', 'button|Go back']);
});

test('Services in flight, top to bottom', () => {
  assert.deepStrictEqual(lines(scrape('services-locked')), [...TABS('Services'),
    'heading|This page cannot be used right now', "static|Please try again when you're on the ground",
    'button|Passenger 1L: Closed', 'button|Passenger 1R: Closed', 'button|Cargo Main: Closed', 'button|Cargo 1R: Closed',
    'button|Cargo 2R: Closed', 'button|Bulk Cargo: Closed', 'button|Nose Weight: Set', 'button|GPU: Connect',
    'button|ASU: Connect', 'button|Wheel Chocks: Set']);
});

const LIVE = ['dispatch', 'dispatch-ofp', 'payload-locked', 'perf-landing', 'charts-signedout', 'services-locked', 'state-locked',
  'options-general', 'options-systems', 'options-caws', 'options-perf', 'options-comms', 'options-behavior', 'services-ground', 'state-ground',
  'payload-form', 'payload-zfw', 'perf-takeoff'];

test('every fixture scrapes without an error and never emits an empty-text control', () => {
  for (const fx of LIVE) {
    const els = scrape(fx);
    assert.ok(els.length > 7, fx);
    for (const e of els) if (e.kind === 'button' || e.kind === 'tab' || e.controlType) assert.ok(e.text, fx + ': unnamed ' + JSON.stringify(e));
  }
});

// The shell keys a node by the agent's key when there is one (B5, review round 2 of PR 189). Two
// elements sharing a key on one page would be held apart by DOM order alone, which is exactly the
// failure the explicit key replaced.
test('every reconcile key is unique on its page, and the live pages carry some', () => {
  let total = 0;
  for (const fx of LIVE) {
    const keys = scrape(fx).filter(e => e.key).map(e => e.key);
    assert.deepStrictEqual(keys, [...new Set(keys)], fx + ': a key is repeated');
    total += keys.length;
  }
  assert.ok(total > 0, 'no live fixture carries a single key');
});
