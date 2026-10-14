'use strict';
// The A300 tablet's Settings page. Its SimBrief box takes the numeric SimBrief Pilot ID: the
// tablet fetches xml.fetcher.php?userid=, so a user name never loads a plan. The box's only
// label is its "SimBrief" heading, which does not say so. Its Hoppie box is a password field:
// the reader never gives the code out, and never writes an empty one over it.
const test = require('node:test');
const assert = require('node:assert');
const { load, scrape, find, recordEvents, WRITE_EVENTS } = require('./run');

const fields = r => r.elements.filter(e => e.controlType === 'text').map(e => e.text);

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
  const seen = recordEvents(box);
  assert.strictEqual(A.setValue(f.idx, ''), false);
  assert.strictEqual(box.value, 'MYLOGON123');
  assert.deepStrictEqual(seen, [], 'a refused entry fires nothing');
  assert.strictEqual(A.setValue(f.idx, 'NEWCODE'), true);
  assert.strictEqual(box.value, 'NEWCODE');
  assert.deepStrictEqual(seen, WRITE_EVENTS, 'a new code reaches the tablet like any typed entry');
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

test('the Hoppie heading is an echo of the box name, with a code stored or not', () => {
  // "Hoppie, code set" is the box's name plus its state, so the heading above it is still dropped.
  // The SimBrief box is named at more length than its heading, so that heading stays.
  for (const code of ['', 'MYLOGON123']) {
    const { A, window } = load('settings', {});
    window.document.getElementById('hoppie').value = code;
    const r = scrape(A);
    const state = code ? 'a code stored' : 'no code stored';
    assert.ok(find(r, code ? 'Hoppie, code set' : 'Hoppie'), 'the box is named, with ' + state);
    assert.ok(!r.elements.some(e => e.kind === 'heading' && e.text === 'Hoppie'), 'no Hoppie heading, with ' + state);
    assert.ok(r.elements.some(e => e.kind === 'heading' && e.text === 'SimBrief'), 'the SimBrief heading stays, with ' + state);
  }
});

test('a text box keeps exactly what was typed', () => {
  // Only a number box is normalized: a comma in a text box is the pilot's own.
  const { A, window } = load('settings', {});
  const f = find(scrape(A), 'SimBrief Pilot ID, numbers only');
  assert.strictEqual(A.setValue(f.idx, '12,5 kg'), true);
  assert.strictEqual(window.document.getElementById('simbrief').value, '12,5 kg');
});
