const { test } = require('node:test');
const assert = require('node:assert');
const { scrape } = require('./run');

// Issue #113: the Automated Ground Ops page showed only the settable Plan-Fuel input — the
// turnaround Turn Time / Turn Time Remaining and the other groundops_ui_outputlabel readouts
// (Uplift, Fuel Uplift Remaining, Target Fuel) + the turnaround progress bar were all dropped.
test('Ground Ops output values + turnaround progress are captured (issue #113)', () => {
  const texts = scrape('groundops').map(e => e.text);
  assert.ok(texts.includes('Turn Time: 00:55'), 'Turn Time (total turnaround)');
  assert.ok(texts.includes('Turn Time Remaining: 00:52:18'), 'Turn Time Remaining');
  assert.ok(texts.includes('Target Fuel: 5400 kg'), 'Target Fuel folds in its output-unit');
  assert.ok(texts.includes('Uplift: 0'), 'Uplift (fuel) value');
  assert.ok(texts.includes('Turnaround: 5%'), 'turnaround progress bar percentage');
});

// A groundops_ui_outputlabel that ALSO carries `pmdg_measurement` (Target Fuel and the fuel
// readouts) was emitted TWICE: once as a bare "5400 kg" by the generic measurement-value path and
// once as the named "Target Fuel: 5400 kg" by the groundops pass. The original fixture used "0",
// which the single-char glyph-skip suppressed, masking the duplicate. The dedicated groundops pass
// must OWN these labels — the generic path emits no orphan value line.
test('Ground Ops measurement outputs are not double-emitted as orphan value lines (#113)', () => {
  const texts = scrape('groundops').map(e => e.text);
  assert.ok(texts.includes('Target Fuel: 5400 kg'), 'named Target Fuel line present');
  assert.ok(!texts.includes('5400 kg'), 'no orphan bare "5400 kg" value line');
});
