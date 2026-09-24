'use strict';
const { test } = require('node:test');
const assert = require('node:assert');
const { load } = require('./run');

test('typing into a field commits like a keyboard: input, change, then blur/focusout for the EFB\'s onBlur', () => {
  const { A, document } = load('perf-landing');
  const wind = JSON.parse(A.scrape()).elements.find(e => e.controlType === 'text' && e.text === 'Wind');
  const input = document.querySelector('[data-md11-efb-idx="' + wind.idx + '"]');
  const seen = [];
  for (const ev of ['input', 'change', 'blur', 'focusout']) input.addEventListener(ev, () => seen.push(ev + ':' + input.value));
  assert.equal(A.setValue(String(wind.idx), '270/10'), true);
  assert.deepStrictEqual(seen, ['input:270/10', 'change:270/10', 'blur:270/10', 'focusout:270/10']);
});

// A checkbox pick is a press through clickElement, so a press that clickElement refuses (a
// greyed-out box) is a FAILED set, never reported as done.
test('setValue on a disabled checkbox reports the refused press, and nothing moves', () => {
  const { A, document } = load('readout-types', { autoVis: true });
  const cb = JSON.parse(A.scrape()).elements.find(e => e.controlType === 'checkbox');
  const node = document.querySelector('[data-md11-efb-idx="' + cb.idx + '"]');
  assert.equal(node.disabled, true);
  let clicks = 0;
  node.addEventListener('click', () => clicks++);
  assert.equal(A.setValue(cb.idx, 'false'), false);
  assert.equal(clicks, 0);
  assert.equal(node.checked, true);
  assert.equal(A.setValue(cb.idx, 'true'), true, 'already where asked: nothing to press, and that is success');
});

// The typed-value path wrote straight into a DISABLED field through the native setter, and said it
// had. A field the EFB has greyed out is never written.
test('setValue refuses a disabled field on the typed-value path and leaves it alone', () => {
  const { A, document } = load('readout-loose', { autoVis: true });
  const ro = JSON.parse(A.scrape()).elements.find(e => e.text === 'Note: 42');
  const input = document.querySelector('[data-md11-efb-idx="' + ro.idx + '"]');
  assert.equal(input.tagName, 'INPUT');
  const seen = [];
  for (const ev of ['input', 'change', 'blur']) input.addEventListener(ev, () => seen.push(ev));
  assert.equal(A.setValue(ro.idx, '99'), false);
  assert.equal(input.value, '42');
  assert.deepStrictEqual(seen, []);
});
