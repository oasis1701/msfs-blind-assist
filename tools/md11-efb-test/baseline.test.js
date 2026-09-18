'use strict';
const { test } = require('node:test');
const assert = require('node:assert');
const { load, scrape } = require('./run');

test('the seven nav tabs come first, the active one marked (current page)', () => {
  const tabs = scrape('options-general').slice(0, 7);
  assert.deepStrictEqual(tabs.map(e => e.kind), Array(7).fill('tab'));
  assert.deepStrictEqual(tabs.map(e => e.text), ['Dispatch', 'Payload', 'Perf', 'Charts', 'Services', 'State', 'Options (current page)']);
  assert.ok(tabs.every(e => e.clickable));
});

test('the brightness slider is a range carrying its bounds', () => {
  const r = scrape('options-general').find(e => e.controlType === 'range');
  assert.equal(r.text, 'Screen Brightness');
  assert.equal(r.value, '100');
  assert.equal(r.min, 10);
  assert.equal(r.max, 100);
});

// The await matters: the MutationObserver callback runs as a MICROTASK, so without yielding to
// the event loop between the two scrapes no observer record is ever delivered and the test passes
// whether or not collect()'s own attribute stamping re-arms the gate. Live, the window polls every
// ~400-600 ms, so the microtask always lands before the next scrape.
test('a second scrape with no DOM change answers unchanged:true', async () => {
  const { A } = load('options-general');
  A.scrape();
  await new Promise(r => setTimeout(r, 0));
  assert.deepStrictEqual(JSON.parse(A.scrape()), { ok: true, unchanged: true });
});

test('a real DOM change makes the next scrape a full one carrying the new page', async () => {
  const { A, document } = load('options-general');
  A.scrape();
  await new Promise(r => setTimeout(r, 0));
  assert.equal(JSON.parse(A.scrape()).unchanged, true, 'gate engaged before the change');

  const active = document.querySelector('.bg-red-800');
  const tabs = [...document.querySelectorAll('button')].filter(b => /bg-(red-800|zinc-600)/.test(b.className));
  const next = tabs.find(b => b !== active);
  active.className = active.className.replace('bg-red-800', 'bg-zinc-600');
  next.className = next.className.replace('bg-zinc-600', 'bg-red-800');
  await new Promise(r => setTimeout(r, 0));

  const r = JSON.parse(A.scrape());
  assert.equal(r.unchanged, undefined, 'the change re-armed the gate');
  assert.ok(Array.isArray(r.elements) && r.elements.length > 0);
  assert.equal(r.page, A.txt(next));
  assert.ok(r.elements.some(e => e.kind === 'tab' && e.text === A.txt(next) + ' (current page)'));
});

test('every FORCE_FULL_EVERY-th scrape is full even with nothing changed', async () => {
  const { A } = load('options-general');
  assert.equal(A.FORCE_FULL_EVERY, 10);
  const full = [];
  for (let i = 1; i <= A.FORCE_FULL_EVERY; i++) {
    const r = JSON.parse(A.scrape());
    if (!r.unchanged) full.push(i);
    await new Promise(r2 => setTimeout(r2, 0));
  }
  assert.deepStrictEqual(full, [1, A.FORCE_FULL_EVERY]);
});

test('hidden text inside a button is not read (Services "Closed", never "Closed0")', () => {
  const els = scrape('services-locked');
  assert.ok(els.some(e => e.kind === 'button' && /Closed$/.test(e.text)));
  assert.ok(!els.some(e => /Closed0/.test(e.text)));
});

test('a field whose caption sits several wrappers up is still named (Perf "Runway")', () => {
  const els = scrape('perf-landing');
  assert.ok(els.some(e => /^Runway/.test(e.text)));
});

test('capture support: findRoot and isVisible exist for tools/coherent.ps1', () => {
  const { A, document } = load('charts-signedout');
  assert.equal(A.findRoot(), document.getElementById('MSFS_REACT_MOUNT'));
  assert.equal(A.isVisible(document.querySelector('h1')), true);
  assert.equal(A.isVisible(document.querySelector('.hidden') || document.createElement('i')), false);
});
