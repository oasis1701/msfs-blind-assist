const { test } = require('node:test');
const assert = require('node:assert');
const { scrape } = require('./run');

test('Sign Out / Factory Reset buttons pair with their left-column labels', () => {
  const els = scrape('settings', { settings: { airspeed_unit: 'kt' } });
  assert.ok(els.some(e => e.kind === 'button' && e.text === 'Navigraph Authentication: Sign Out'),
    'Sign Out pairs with Navigraph Authentication');
  assert.ok(els.some(e => e.kind === 'button' && e.text === 'Reset to Factory Settings: Factory Reset'),
    'Factory Reset pairs with its label');
});

test('paired labels are consumed, not left as orphan statics', () => {
  const els = scrape('settings', { settings: { airspeed_unit: 'kt' } });
  assert.ok(!els.some(e => e.kind === 'static' && e.text === 'Navigraph Authentication'), 'no orphan Navigraph static');
  assert.ok(!els.some(e => e.kind === 'static' && e.text === 'Reset to Factory Settings'), 'no orphan Reset static');
});

test('unit toggle pairs to its label by vertical centre (faithful "AirSpeed" casing)', () => {
  const els = scrape('settings', { settings: { airspeed_unit: 'kt' } });
  const t = els.find(e => e.controlType === 'checkbox' && /AirSpeed Unit/.test(e.text));
  assert.ok(t, 'toggle keeps the source "AirSpeed" casing (not id-derived "Airspeed")');
  assert.strictEqual(t.text, 'AirSpeed Unit: knots');
});
