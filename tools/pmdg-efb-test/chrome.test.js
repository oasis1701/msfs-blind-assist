const { test } = require('node:test');
const assert = require('node:assert');
const { scrape } = require('./run');

test('nav-rail icon buttons read their authoritative app names (not FA-icon guesses)', () => {
  const els = scrape('chrome');
  const names = els.filter(e => e.kind === 'button').map(e => e.text);
  assert.ok(names.includes('Dashboard'));
  assert.ok(names.includes('Paperwork'), 'file-lines is Paperwork, not "Charts"');
  assert.ok(names.includes('Charts'), 'map-location-dot is Charts, not "Navigation"');
  assert.ok(names.includes('Preferences'));
  assert.ok(names.includes('Authenticate'), 'fa-plane-lock has no FA-map entry; id supplies the name');
  assert.ok(names.some(n => /Navdata/.test(n)));
  assert.ok(names.some(n => /Info/.test(n)));
});

test('status-bar tablet-restart reads "Restart" (not "Refresh"); AUTO CRUISE keeps its text', () => {
  const els = scrape('chrome');
  assert.ok(els.some(e => e.kind === 'button' && e.text === 'Restart'), 'restart icon reads Restart');
  assert.ok(!els.some(e => e.kind === 'button' && e.text === 'Refresh'), 'no "Refresh" leak');
  assert.ok(els.some(e => e.kind === 'button' && e.text === 'AUTO CRUISE'));
});

test('status-bar Signal + Battery indicators are surfaced WITH their current value', () => {
  const els = scrape('chrome');
  // Battery level from the fa-battery-* class + charging from fa-bolt; Signal state from colour.
  assert.ok(els.some(e => e.kind === 'static' && e.text === 'Battery: Full, charging'), 'battery shows level + charging');
  assert.ok(els.some(e => e.kind === 'static' && e.text === 'Signal: Connected'), 'signal shows connected (green)');
});
