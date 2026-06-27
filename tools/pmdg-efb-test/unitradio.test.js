const { test } = require('node:test');
const assert = require('node:assert');
const { load } = require('./run');

// Regression for the collect()/setValue mismatch: collect() rewrites a UNIT_PAIRS toggle into a
// 2-option SELECT for BOTH checkbox AND radio inputs, so setValue must be able to flip a radio too.
// Before the fix, setValue only special-cased el.type === 'checkbox', so a radio-typed unit toggle
// was reported as a settable select the user could never actually change.
test('a radio-typed unit toggle is reported as a select AND can be flipped via setValue', () => {
  const { window, A } = load('unitradio');
  const els = JSON.parse(A.scrape()).elements;
  const sel = els.find(e => e.text === 'Weight Unit');
  assert.ok(sel && sel.controlType === 'select', 'radio unit toggle reported as a 2-option select');
  assert.equal(sel.value, 'kilograms', 'unchecked radio -> kilograms');
  assert.deepEqual(sel.options, ['kilograms', 'pounds'], 'both unit options offered');

  const radio = window.document.getElementById('efb_preferences_weight_unit');
  assert.equal(radio.checked, false, 'radio starts unchecked');
  A.setValue(sel.idx, 'pounds');
  assert.equal(radio.checked, true, 'selecting "pounds" flips the radio to checked (lbs)');
});
