'use strict';
// The tablet's header bar: the local and Zulu clocks, the tablet's battery, the gear that opens the
// control box, the simulation-rate badge and, while servicing runs, the maintenance button. The
// reader used to read only the Home button from it, so the control box could not be reached.
const test = require('node:test');
const assert = require('node:assert');
const { load, scrape, find } = require('./run');

test('the header gives the clocks and the battery in one line', () => {
  // The clocks keep the tablet's LOCAL and ZULU; "1357" is read as 13:57, the time it is, not a
  // number in the thousands. The battery is an icon, put in words.
  const r = scrape(load('home').A);
  const e = find(r, 'LOCAL 13:57, ZULU 20:57, charging');
  assert.ok(e, 'status line');
  assert.strictEqual(e.kind, 'static');
  assert.strictEqual(e.key, 'status:header');
  assert.ok(find(scrape(load('home-battery').A), 'LOCAL 13:57, ZULU 20:57, battery half'));
});

test('the gear is a Control box button that runs the handler on its icon', () => {
  const { A, window } = load('home');
  const r = scrape(A);
  const gear = find(r, 'Control box');
  assert.ok(gear && gear.kind === 'button' && gear.clickable, 'Control box button');
  let opened = 0;
  window.document.getElementById('control-box-button-icon').onclick = () => opened++;
  assert.ok(A.clickElement(gear.idx));
  assert.strictEqual(opened, 1);
});

test('the simulation-rate badge is read while it shows', () => {
  const r = scrape(load('home').A);
  assert.ok(find(r, 'SIMULATION RATE: 4x'));
  assert.ok(!scrape(load('home-battery').A).elements.some(e => /^SIMULATION RATE/.test(e.text)), 'hidden at 1x');
});

test('the header comes after the page: status line, then its buttons', () => {
  const r = scrape(load('home').A);
  const t = r.elements.map(e => e.text);
  assert.deepStrictEqual(t.slice(-3), ['SIMULATION RATE: 4x', 'LOCAL 13:57, ZULU 20:57, charging', 'Control box']);
});

test('a Home menu item the tablet has switched off is dimmed', () => {
  const r = scrape(load('home').A);
  assert.strictEqual(find(r, 'My Flight').disabled, false);
  assert.strictEqual(find(r, 'Charts').disabled, true);
});
