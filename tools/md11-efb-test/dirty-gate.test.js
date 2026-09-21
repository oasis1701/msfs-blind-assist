'use strict';
const { test } = require('node:test');
const assert = require('node:assert');
const { load, reinject } = require('./run');

// The observer callback runs as a MICROTASK, so a test that scrapes twice without yielding to the
// event loop never has a MutationRecord delivered and passes whether or not the gate re-armed.
// Live, the window polls every ~400-600 ms, so the microtask always lands before the next scrape.
const tick = () => new Promise(r => setTimeout(r, 0));

const cbOf = (els) => els.find(e => e.controlType === 'checkbox');

// CHECKED-PROPERTY CAVEAT (agent, above A._markDirty). `.checked` is DECOUPLED from the `checked`
// content attribute once it is flipped by a tap or by script — neither path writes the attribute —
// so MutationObserver's attributes:true NEVER fires for a toggle flip. Both halves are pinned: the
// gap is real (the observer alone does not see it) and the capture-phase change/input listeners are
// what close it. Without them the reader kept reporting the OLD toggle state until the next
// FORCE_FULL_EVERY poll: ~6 s at the client's 600 ms cadence.
test('flipping .checked alone leaves the observer blind — the change event is what re-arms the gate', async () => {
  const { A, window, document } = load('toggle-checkbox', { autoVis: true });
  assert.equal(cbOf(JSON.parse(A.scrape()).elements).value, 'true');

  await tick();
  assert.equal(JSON.parse(A.scrape()).unchanged, true, 'gate engaged with nothing changed');

  const cb = document.querySelector('input[type="checkbox"]');
  cb.checked = false;
  assert.equal(cb.getAttribute('checked'), '', 'the flip wrote no content attribute — this is the caveat');
  await tick();
  assert.equal(JSON.parse(A.scrape()).unchanged, true, 'so the observer never saw it');

  // What a native click dispatches as part of its default action, on its own here.
  cb.dispatchEvent(new window.Event('change', { bubbles: true }));
  await tick();
  const after = JSON.parse(A.scrape());
  assert.equal(after.unchanged, undefined, 'the capture-phase change listener re-armed the gate');
  assert.equal(cbOf(after.elements).value, 'false', 'and the new state is what the reader reports');
});

// The realistic path end to end: a real tap on the tablet (and our own A.click's el.click(), whose
// default action is the same) flips .checked and fires bubbling input + change.
test('a real click on a toggle re-arms the gate and the next scrape carries the new state', async () => {
  const { A, document } = load('toggle-checkbox', { autoVis: true });
  A.scrape();
  await tick();
  assert.equal(JSON.parse(A.scrape()).unchanged, true, 'gate engaged before the tap');

  document.querySelector('input[type="checkbox"]').click();
  await tick();
  const after = JSON.parse(A.scrape());
  assert.equal(after.unchanged, undefined, 'the tap re-armed the gate');
  assert.equal(cbOf(after.elements).value, 'false');
});

// Re-injection replaces `A` wholesale, so a listener left behind from the previous injection keeps
// firing against the ORPHANED closure — marking a dead agent dirty forever and, worse, leaving a
// growing pile of listeners on document per re-install. The teardown reads the handles stashed on
// `window`, which survives an injection; the old A._markDirty is a different function object and
// could not be removed any other way.
test('re-injection tears down the previous listeners, not just the observer', async () => {
  const { A, window, document } = load('toggle-checkbox', { autoVis: true });
  const first = window.__MSFSBA_MD11_EFB_HANDLERS;
  assert.ok(first && typeof first.change === 'function' && typeof first.input === 'function', 'listeners installed');

  const B = reinject(window);
  assert.notEqual(B, A, 're-injection replaced the agent object');
  assert.notEqual(window.__MSFSBA_MD11_EFB_HANDLERS.change, first.change, 'the stashed handles are the new agent\'s');

  B.scrape();
  await tick();
  assert.equal(JSON.parse(B.scrape()).unchanged, true, 'the live agent\'s gate engages');

  A._dirty = false;
  document.querySelector('input[type="checkbox"]').click();
  await tick();
  assert.equal(A._dirty, false, 'the orphaned agent no longer hears the page');
  assert.equal(B._dirty, true, 'the live one does');
});
