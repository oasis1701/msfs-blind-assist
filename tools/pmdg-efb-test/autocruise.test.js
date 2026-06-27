const { test } = require('node:test');
const assert = require('node:assert');
const { scrape } = require('./run');

test('AUTO CRUISE popup: Sim Rate button pairs with its popup-label; Reset stays standalone', () => {
  const els = scrape('autocruise');
  assert.ok(els.some(e => e.kind === 'button' && e.text === 'Sim Rate: 1X'), 'Sim Rate value paired with label');
  assert.ok(els.some(e => e.kind === 'button' && e.text === 'Reset'), 'Reset button (no spurious pair)');
  assert.ok(!els.some(e => e.kind === 'static' && e.text === 'Sim Rate'), 'no orphan Sim Rate label');
  assert.ok(els.some(e => e.kind === 'heading' && e.text === 'Auto Cruise'));
});
