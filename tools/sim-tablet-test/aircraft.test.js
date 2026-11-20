'use strict';
const { test } = require('node:test');
const assert = require('node:assert');
const { scrape, lines } = require('./run');

test('Mass and balance: the load tabs, the selected one marked, keyed so a switch patches in place', () => {
  const tabs = scrape('aircraft-mass-seats').filter(e => e.kind === 'tab' && e.key && e.key.indexOf('tab:') === 0);
  assert.deepStrictEqual(tabs.map(e => e.text), ['Seats (selected)', 'Cargo', 'Fuel', 'Empty']);
  assert.ok(tabs.every(e => e.announceChange === true));
});

test('each seat is a number box named from its hidden detail panel: name, unit and limit', () => {
  const seats = scrape('aircraft-mass-seats').filter(e => e.controlType === 'text' && /max/.test(e.text));
  assert.deepStrictEqual(seats.map(e => e.text + '=' + e.value), [
    'Co-Pilot (LBS, max 300)=170', 'Pilot (LBS, max 300)=170',
    'Passenger Right (LBS, max 300)=0', 'Passenger Left (LBS, max 300)=0']);
});

test('a fuel tank reads its shown unit only, never the hidden LBS twin of its GAL total', () => {
  const tanks = scrape('aircraft-mass-fuel').filter(e => e.controlType === 'text' && /tank/.test(e.text));
  assert.deepStrictEqual(tanks.map(e => e.text), ['Left tank (GAL, max 28)', 'Right tank (GAL, max 28)']);
});

test('a quick-fill box is named once, by its caption, which is not read again as a line', () => {
  const l = lines(scrape('aircraft-mass-seats'));
  assert.ok(l.includes('/text|Quick fill (%)=28'));
  assert.ok(!l.includes('static|Quick fill'));
});

test('Environment: exclusive buttons, captioned unit boxes, and dropdowns as selects', () => {
  const els = scrape('aircraft-environment-takeoff-ksea');
  const l = lines(els);
  for (const s of ['button|TAKE OFF (selected)', 'button|LANDING', 'button|METAR (selected)',
                   '/text|Wind magnitude (KT)=0', '/text|QNH (IN)=30', '/text|Runway=KSEA, Seattle-Tacoma Intl'])
    assert.ok(l.includes(s), s);
  const flaps = els.find(e => e.controlType === 'select' && e.text === 'Aircraft Configuration');
  assert.ok(flaps);
  assert.equal(flaps.value, 'Flaps up');
  assert.deepStrictEqual(flaps.options, ['Flaps up', 'Flaps 10°']);
  const surface = els.find(e => e.controlType === 'select' && e.value === 'Dry runway');
  assert.ok(surface, 'the selector shows "Dry"; the value is the option it stands for');
});

test('Environment: the take-off results read as lines', () => {
  const l = lines(scrape('aircraft-environment-takeoff-ksea'));
  assert.ok(l.includes('static|GR / TORA 821 / 11890 FT'));
  assert.ok(l.includes('static|TODR / TODA 1416 / 11890 FT'));
});

test('Checklists: each item a tick box carrying what to do, the groups saying open or folded', () => {
  const els = scrape('aircraft-checklists');
  const items = els.filter(e => e.controlType === 'checkbox');
  assert.equal(items.length, 15);
  assert.equal(items[0].text, 'Pitot tube cover: REMOVE. Remove pitot cover, check for blockage');
  assert.equal(items[0].value, 'false');
  const l = lines(els);
  assert.ok(l.includes('button|Preflight (expanded)'));
  assert.ok(l.includes('button|Inflight (collapsed)'));
  assert.ok(l.includes('button|Preflight inspection - cabin (selected)'));
  assert.ok(l.includes('/select|Level=Standard'));
  assert.ok(!l.some(x => /Look at/.test(x)), 'the camera-move eye buttons are left out');
});

test('Settings: switches read their state, unit choices their selection', () => {
  const l = lines(scrape('settings'));
  assert.ok(l.includes('/checkbox|Auto brightness=true'));
  assert.ok(l.includes('button|KTS (selected)'));
  assert.ok(l.includes('button|°C (selected)'));
});
