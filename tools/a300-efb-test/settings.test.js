'use strict';
// The A300 tablet's Settings page. Its SimBrief box takes the numeric SimBrief Pilot ID: the
// tablet fetches xml.fetcher.php?userid=, so a user name never loads a plan. The box's only
// label is its "SimBrief" heading, which does not say so. Its Hoppie box is a password field:
// the reader never gives the code out, and never writes an empty one over it.
const test = require('node:test');
const assert = require('node:assert');
const { load, scrape, find } = require('./run');

const fields = r => r.elements.filter(e => e.controlType === 'text').map(e => e.text);

// Counts the events a write fires on a box: the tablet's own handlers run on them.
function countEvents(box) {
  const seen = [];
  for (const type of ['input', 'change', 'keyup', 'blur']) box.addEventListener(type, () => seen.push(type));
  return seen;
}

test('the SimBrief box says it wants the Pilot ID number', () => {
  const r = scrape(load('settings', {}).A);
  assert.strictEqual(r.page, 'Settings');
  assert.deepStrictEqual(fields(r), ['SimBrief Pilot ID, numbers only', 'Hoppie']);
});

test('a stored Hoppie code is named, never shown or written back', () => {
  const { A, window } = load('settings', {});
  const box = window.document.getElementById('hoppie');
  box.value = 'MYLOGON123';
  const f = scrape(A).elements.find(e => e.text === 'Hoppie, code set');
  assert.ok(f, 'the name says a code is stored');
  assert.strictEqual(f.value, '');
  assert.ok(!A.scrape().includes('MYLOGON123'), 'the code is nowhere in what is read out');
  const seen = countEvents(box);
  assert.strictEqual(A.setValue(f.idx, ''), false);
  assert.strictEqual(box.value, 'MYLOGON123');
  assert.deepStrictEqual(seen, [], 'a refused entry fires nothing');
  assert.strictEqual(A.setValue(f.idx, 'NEWCODE'), true);
  assert.strictEqual(box.value, 'NEWCODE');
  assert.deepStrictEqual(seen, ['input', 'change', 'keyup', 'blur'], 'a new code reaches the tablet like any typed entry');
});

test('spaces alone are not a Hoppie code', () => {
  const { A, window } = load('settings', {});
  const box = window.document.getElementById('hoppie');
  box.value = 'MYLOGON123';
  const f = find(scrape(A), 'Hoppie, code set');
  assert.strictEqual(A.setValue(f.idx, '   '), false);
  assert.strictEqual(A.setValue(f.idx, null), false);
  assert.strictEqual(box.value, 'MYLOGON123');
});

test('an empty Hoppie box is only named, and a code can be typed into it', () => {
  const { A, window } = load('settings', {});
  const f = find(scrape(A), 'Hoppie');
  assert.ok(f, 'named without "code set"');
  assert.strictEqual(f.value, '');
  assert.strictEqual(A.setValue(f.idx, ''), false, 'nothing to write');
  assert.strictEqual(A.setValue(f.idx, 'FIRSTCODE'), true);
  assert.strictEqual(window.document.getElementById('hoppie').value, 'FIRSTCODE');
  assert.ok(find(scrape(A), 'Hoppie, code set'), 'named for the code now stored');
});

test('a text box keeps exactly what was typed', () => {
  // Only a number box is normalized: a comma in a text box is the pilot's own.
  const { A, window } = load('settings', {});
  const f = find(scrape(A), 'SimBrief Pilot ID, numbers only');
  assert.strictEqual(A.setValue(f.idx, '12,5 kg'), true);
  assert.strictEqual(window.document.getElementById('simbrief').value, '12,5 kg');
});
