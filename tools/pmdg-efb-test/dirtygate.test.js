const { test } = require('node:test');
const assert = require('node:assert');
const { load } = require('./run');

// Permanent regression test for the MutationObserver dirty gate (task 8.5, commit 64f40945).
// Promotes the manual review proof to a real test. Unlike every other file in this suite — which
// calls scrape()/load() fresh per assertion (a brand-new jsdom window + agent injection each time,
// so A._everScraped always starts false and every scrape is forced full) — this test deliberately
// keeps ONE agent instance (`A`) across multiple scrape() calls, because the dirty gate can only be
// observed across a SEQUENCE of polls against the same live page.
test('dirty gate: unchanged on a no-op poll, dirty on a real change, forced full every 10th poll', () => {
  const { window, A } = load('unitradio');

  // Poll 1: first-ever scrape on a fresh agent instance is always full (A._everScraped starts false).
  const r1 = JSON.parse(A.scrape());
  assert.equal(r1.ok, true);
  assert.ok(!r1.unchanged, 'poll 1 is a full scrape');
  assert.ok(Array.isArray(r1.elements) && r1.elements.length > 0, 'poll 1 returns real elements');
  const w1 = r1.elements.find(e => (e.text || '').indexOf('Weight Unit') === 0);
  assert.ok(w1, 'weight unit control present in poll 1');
  assert.equal(w1.value, 'kilograms', 'unchecked radio reads as kilograms');

  // Poll 2: nothing changed since poll 1 -> the gate must report unchanged:true with no elements.
  // This also proves collect()'s OWN idx-stamp mutations (it clears/re-sets data-pmdg-efb-idx on
  // every element it enumerates) do not leak back into _dirty via the observer -- the SELF-TRIGGER
  // TRAP the observer disconnect/reconnect-around-collect() guards against. If that guard regressed,
  // this poll would come back as a full scrape too.
  const r2 = JSON.parse(A.scrape());
  assert.equal(r2.ok, true);
  assert.equal(r2.unchanged, true, 'poll 2 is unchanged (no page mutation happened)');
  assert.equal(r2.elements, undefined, 'unchanged response carries no elements');

  // Real user action: a native tap on the unit-toggle radio (the unitradio fixture's checkbox/radio
  // in A.UNIT_PAIRS). Flipping .checked this way touches NO DOM attribute -- per the WHATWG DOM
  // spec, `.checked` decouples from the `checked` content attribute once flipped by either a tap or
  // script -- and mutates no other DOM structure, so this exercises ONLY the capture-phase
  // change/input listener (A._markDirty via document.addEventListener('change'/'input', ..., true)),
  // never the MutationObserver's attributes:true option.
  const radio = window.document.getElementById('efb_preferences_weight_unit');
  assert.equal(radio.checked, false, 'radio starts unchecked (kg)');
  radio.click();
  assert.equal(radio.checked, true, 'native click flips the radio to checked (lbs)');

  // Poll 3: must be a full scrape with the flipped value readable -- proves the change/input
  // listener closes the checked-property gap the MutationObserver alone cannot see.
  const r3 = JSON.parse(A.scrape());
  assert.equal(r3.ok, true);
  assert.ok(!r3.unchanged, 'poll 3 is a full scrape (change/input listener caught the toggle)');
  const w3 = r3.elements.find(e => (e.text || '').indexOf('Weight Unit') === 0);
  assert.ok(w3, 'weight unit control present in poll 3');
  assert.equal(w3.value, 'pounds', 'flipped value is readable in the very next scrape');

  // Polls 4-9: no further page changes -> stay unchanged.
  for (let i = 4; i <= 9; i++) {
    const r = JSON.parse(A.scrape());
    assert.equal(r.ok, true);
    assert.equal(r.unchanged, true, `poll ${i} is unchanged`);
    assert.equal(r.elements, undefined, `poll ${i} carries no elements`);
  }

  // Poll 10: the FORCE_FULL_EVERY safety net fires regardless of _dirty, bounding worst-case
  // staleness to a few seconds even against a hypothetical future path that flips a property with
  // neither a DOM mutation nor a change/input event.
  const r10 = JSON.parse(A.scrape());
  assert.equal(r10.ok, true);
  assert.ok(!r10.unchanged, 'poll 10 is forced full regardless of dirty state');
  assert.ok(Array.isArray(r10.elements) && r10.elements.length > 0, 'poll 10 returns real elements');
  const w10 = r10.elements.find(e => (e.text || '').indexOf('Weight Unit') === 0);
  assert.ok(w10, 'weight unit control still present in the forced poll 10 scrape');
  assert.equal(w10.value, 'pounds', 'value from poll 3 is still correctly reflected in poll 10');
});
