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
  assert.ok(texts.includes('Target Fuel: 0 kg'), 'Target Fuel folds in its output-unit');
  assert.ok(texts.includes('Uplift: 0'), 'Uplift (fuel) value');
  assert.ok(texts.includes('Turnaround: 5%'), 'turnaround progress bar percentage');
});
