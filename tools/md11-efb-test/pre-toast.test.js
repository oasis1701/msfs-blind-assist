'use strict';
const { test } = require('node:test');
const assert = require('node:assert');
const { scrape, lines } = require('./run');

test('the OFP view reads "< Back" then pre-formatted blocks with their line breaks', () => {
  const els = scrape('dispatch-ofp');
  const body = els.slice(7);
  assert.equal(body[0].kind, 'button');
  assert.equal(body[0].text, '< Back');
  const pres = body.filter(e => e.controlType === 'pre');
  assert.ok(pres.length >= 1, 'pre blocks present');
  assert.ok(pres[0].text.startsWith('[ OFP ]'), pres[0].text.slice(0, 40));
  assert.ok(pres[0].text.includes('\n'), 'line breaks kept');
  assert.ok(!els.some(e => e.kind === 'static' && !e.controlType && e.text.length > 2000), 'no single-line OFP blob');
});

test('a pop-up is an alert spoken at once, with a Close button', () => {
  const ls = lines(scrape('toast', { autoVis: true, nav: 'Options' })).slice(7);
  assert.deepStrictEqual(ls, ['heading|Options', 'alert|Options saved successfully.', 'button|Close']);
  const alert = scrape('toast', { autoVis: true, nav: 'Options' }).find(e => e.kind === 'alert');
  assert.equal(alert.live, 'assertive');
});
