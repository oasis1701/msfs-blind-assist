'use strict';
const { test } = require('node:test');
const assert = require('node:assert');
const { load } = require('./run');

function scraped(A) { return JSON.parse(A.scrape()).elements; }

test('a button press is ONE click and nothing else', () => {
  const { A, window } = load('aircraft-main');
  const b = scraped(A).find(e => e.text === 'Checklists');
  assert.equal(A.clickElement(b.idx), true);
  assert.deepStrictEqual(window.__presses, ['click:Checklists']);
});

test('a search result is pressed with mousedown alone, the one event it answers', () => {
  const { A, window, document } = load('aircraft-environment-takeoff-ksea');
  const list = document.querySelector('.environment-facility .search-bar-list');
  const item = document.createElement('div');
  item.className = 'facility-result-item';
  item.innerHTML = '<span class="icao">KSEA, </span><span class="facility-name">Seattle-Tacoma Intl</span>';
  item.onmousedown = () => window.__presses.push('mousedown:result');
  list.appendChild(item);
  const r = scraped(A).find(e => e.text === 'Result: KSEA, Seattle-Tacoma Intl');
  assert.ok(r, 'a hidden result list is read while the box has text');
  assert.equal(A.clickElement(r.idx), true);
  assert.deepStrictEqual(window.__presses, ['mousedown:result']);
});

test('typing sets the value and fires input, and NEVER focuses the box (that takes the keyboard)', () => {
  const { A, document, window } = load('aircraft-mass-seats');
  const seat = scraped(A).find(e => e.text === 'Co-Pilot (LBS, max 300)');
  const inp = document.querySelector('[data-sim-tablet-idx="' + seat.idx + '"]');
  let inputs = 0, focuses = 0;
  inp.addEventListener('input', () => inputs++);
  inp.addEventListener('focus', () => focuses++);
  inp.addEventListener('mousedown', () => focuses++);
  assert.equal(A.setValue(seat.idx, '160'), true);
  assert.equal(inp.value, '160');
  assert.equal(inputs, 1);
  assert.equal(focuses, 0);
  assert.notEqual(document.activeElement, inp);
  assert.deepStrictEqual(window.__presses, []);
});

test('a focus the tablet takes by itself after a press is handed back', async () => {
  const { A, document } = load('aircraft-mass-seats');
  const inp = document.querySelector('.quick-fill-textbox input');
  let outs = 0;
  inp.addEventListener('focusout', () => outs++);
  inp.focus();
  assert.equal(document.activeElement, inp);
  A.releaseKeyboard();
  assert.notEqual(document.activeElement, inp);
  assert.ok(outs >= 1, 'the box\'s own focus-out path ran (it tells the sim to stop sending keys)');
});

test('a dropdown choice is picked by clicking its (hidden) list item', () => {
  const { A, window } = load('aircraft-environment-takeoff-ksea');
  const flaps = scraped(A).find(e => e.text === 'Aircraft Configuration');
  assert.equal(A.setValue(flaps.idx, 'Flaps 10°'), true);
  assert.deepStrictEqual(window.__presses, ['click:Flaps 10°']);
  assert.equal(A.setValue(flaps.idx, 'Flaps 99'), false, 'no such choice');
});

test('a checklist tick box is pressed only when its state differs', () => {
  const { A, window } = load('aircraft-checklists');
  const item = scraped(A).find(e => e.controlType === 'checkbox');
  assert.equal(A.setValue(item.idx, 'false'), true);
  assert.deepStrictEqual(window.__presses, [], 'already unticked: nothing pressed');
  assert.equal(A.setValue(item.idx, 'true'), true);
  assert.equal(window.__presses.length, 1);
});

test('a disabled control is refused, never pressed', () => {
  const { A, window } = load('atlas-load-from-web');
  const b = scraped(A).find(e => e.text === 'NO FURTHER FLIGHT PLANS');
  assert.equal(A.clickElement(b.idx), false);
  assert.deepStrictEqual(window.__presses, []);
});

test('the SimBrief action idx is never pressed in the page: the app runs it', () => {
  const { A, window } = load('atlas-route-loaded');
  assert.equal(A.clickElement(990001), false);
  assert.deepStrictEqual(window.__presses, []);
});

// The await matters: the MutationObserver callback is a microtask, so without yielding no record
// is delivered and the gate would pass whether or not the reader's own stamping re-arms it.
test('a second scrape with no change answers unchanged:true; a real change makes it full again', async () => {
  const { A, document } = load('aircraft-main');
  A.scrape();
  await new Promise(r => setTimeout(r, 0));
  assert.deepStrictEqual(JSON.parse(A.scrape()), { ok: true, unchanged: true });
  document.querySelector('.aircraft-name').textContent = 'Cessna 172 Skyhawk Classic';
  await new Promise(r => setTimeout(r, 0));
  const r = JSON.parse(A.scrape());
  assert.equal(r.unchanged, undefined);
  assert.ok(r.elements.some(e => e.text === 'Cessna 172 Skyhawk Classic: G1000'));
});
