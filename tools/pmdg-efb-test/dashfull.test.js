const { test } = require('node:test');
const assert = require('node:assert');
const { scrape } = require('./run');

test('flight-details measurement values merge with their colon-heading (ZFW / ROUTE DIST)', () => {
  const els = scrape('dashfull');
  const texts = els.map(e => e.text);
  assert.ok(texts.includes('ZFW: 462,330 lb'), 'ZFW value + unit captured and merged');
  assert.ok(texts.includes('ROUTE DIST: 1,183 nm'), 'Route Dist value + unit captured and merged');
  assert.ok(!texts.includes('ROUTE DIST: LAX'), 'Route Dist no longer mis-merges with the origin code');
  assert.ok(!texts.some(t => t === 'ZFW:' || t === 'ROUTE DIST:'), 'no orphan colon labels');
});

test('leaflet map overlay text (waypoint tooltips) is suppressed; map controls kept', () => {
  const els = scrape('dashfull');
  assert.ok(!els.some(e => e.text === 'HOLTZ'), 'leaflet tooltip waypoint dropped');
  assert.ok(els.some(e => e.kind === 'button' && e.text === 'Zoom In'), 'map control button kept');
});

test('route-header codes are labeled (Origin / Destination / STD / STA)', () => {
  const els = scrape('dashfull');
  const texts = els.map(e => e.text);
  assert.ok(texts.includes('Origin: LAX / KLAX'));
  assert.ok(texts.includes('Destination: KDFW / DFW'));
  assert.ok(texts.includes('STD: 14:30 UTC / 14:50 UTC'));
  assert.ok(texts.includes('STA: 17:20 UTC / 17:28 UTC'));
  assert.ok(!texts.includes('LAX') && !texts.includes('KDFW'), 'bare floating codes are gone');
});
