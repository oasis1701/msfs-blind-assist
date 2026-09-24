'use strict';
const { test } = require('node:test');
const assert = require('node:assert');
const { load, scrape } = require('./run');

const isControl = e => e.controlType === 'text' || e.controlType === 'select' || e.controlType === 'range' || e.controlType === 'checkbox' || e.kind === 'button' || e.kind === 'link';

test('a page locked in flight explains itself first, then reads its content with every control dimmed', () => {
  for (const fx of ['payload-locked', 'services-locked', 'state-locked']) {
    const els = scrape(fx).slice(7);
    assert.equal(els[0].kind, 'heading', fx); assert.equal(els[0].text, 'This page cannot be used right now', fx);
    assert.equal(els[1].text, "Please try again when you're on the ground", fx);
    const ctrls = els.slice(2).filter(isControl);
    assert.ok(ctrls.length > 0, fx + ': content still read');
    assert.ok(ctrls.every(e => e.disabled), fx + ': ' + JSON.stringify(ctrls.filter(e => !e.disabled).map(e => e.text)));
  }
});

test('the nav tabs above a locked page are never dimmed', () => {
  assert.ok(scrape('services-locked').slice(0, 7).every(e => !e.disabled));
});

test('on the ground the same controls are live', () => {
  const ctrls = scrape('services-ground').slice(7).filter(isControl);
  assert.ok(ctrls.length > 0);
  assert.ok(ctrls.every(e => !e.disabled));
});

// Reading a locked page and ACTING on it are different questions, and the walk only answered the
// first. `pointer-events: none` blocks real hit-testing only — a dispatched event or el.click()
// reaches the handler regardless — and the EFB's door/GPU/state handlers have no ground check of
// their own, so the reader has to refuse the press itself. (The shell's native-list fallback has
// no dimmed gate at all, so it is reached by exactly this path.)
function counters(el, types) {
  const seen = {};
  for (const t of types) {
    seen[t] = 0;
    el.addEventListener(t, (function (k) { return function () { seen[k]++; }; })(t));
  }
  return seen;
}
const SEQUENCE = ['pointerdown', 'mousedown', 'mouseup', 'click'];

function gpuTile(fixture) {
  const { A, document } = load(fixture);
  const el = JSON.parse(A.scrape()).elements.find(e => /^GPU:/.test(e.text));
  assert.ok(el, fixture + ': the GPU tile is read');
  return { A, el, btn: document.querySelector('[data-md11-efb-idx="' + el.idx + '"]') };
}

test('a press on a page the EFB has locked is refused, and nothing is dispatched', () => {
  const { A, el, btn } = gpuTile('services-locked');
  assert.equal(A.isInert(btn), true, 'the tile sits inside the locked wrapper');
  const seen = counters(btn, SEQUENCE);
  assert.equal(A.clickElement(el.idx), false);
  assert.deepStrictEqual(seen, { pointerdown: 0, mousedown: 0, mouseup: 0, click: 0 });
});

test('setValue on a locked control is refused too', () => {
  const { A, el, btn } = gpuTile('services-locked');
  const seen = counters(btn, SEQUENCE);
  assert.equal(A.setValue(el.idx, 'Disconnect'), false);
  assert.equal(seen.click, 0);
});

// The locked Payload fixture's own fields are all DOM-disabled, so they never reach setValue's
// write path. Enabling one reproduces the case that actually matters: a field the EFB left
// enabled under the lock, where `pointer-events: none` is the ONLY thing between the pilot and a
// write in flight.
test('setValue refuses a field the EFB left enabled inside the locked wrapper', () => {
  const { A, document } = load('payload-locked');
  const field = document.querySelector('.pointer-events-none input');
  assert.ok(field, 'payload-locked has a field inside the locked wrapper');
  field.disabled = false;
  const before = field.value;

  const el = JSON.parse(A.scrape()).elements.find(e => e.controlType === 'text');
  assert.ok(el, 'the enabled field is read as an edit box');
  assert.equal(A.isInert(document.querySelector('[data-md11-efb-idx="' + el.idx + '"]')), true);
  assert.equal(A.setValue(el.idx, '999'), false);
  assert.equal(field.value, before, 'the value was not written');
});

test('on the ground that same press goes through, exactly once', () => {
  const { A, el, btn } = gpuTile('services-ground');
  assert.equal(A.isInert(btn), false);
  const seen = counters(btn, SEQUENCE);
  assert.equal(A.clickElement(el.idx), true);
  assert.deepStrictEqual(seen, { pointerdown: 1, mousedown: 1, mouseup: 1, click: 1 });
});

// The generic path's record is built on A.el, like every block's, so the locked-page rule lives in
// ONE place: A.controlFor itself dims a control met inside an inert subtree, and stamps its element.
test('A.controlFor dims a control read inside a locked subtree, and stamps it', () => {
  const { A, document } = load('services-ground');
  const btn = document.querySelector('button');
  A._idx = 0;
  A._inert = true;
  let rec;
  try { rec = A.controlFor(btn); } finally { A._inert = false; }
  assert.equal(rec.disabled, true, 'dimmed inside a locked subtree');
  assert.equal(btn.getAttribute('data-md11-efb-idx'), String(rec.idx), 'stamped with its own idx');
  assert.equal(A.controlFor(btn).disabled, false, 'live outside one');
});
