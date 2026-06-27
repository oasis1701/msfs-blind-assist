const { test } = require('node:test');
const assert = require('node:assert');
const { scrape } = require('./run');
const PAGES = ['aircraft','simoptions','realism','thirdparty','atsu_aoc','audio','flypad','about','index','calibrate'];

test('every settings fixture scrapes ok with no agent error', () => {
  PAGES.forEach(p => { const d = scrape(p); assert.strictEqual(d.ok, true, p + ' scrape failed'); });
});

test('no control reads the bare page title as its label', () => {
  ['aircraft','simoptions','realism','thirdparty','audio','flypad'].forEach(p => {
    const bad = scrape(p).elements.filter(e => e.idx > 0 && /^Settings - /.test(e.text) && e.text !== 'Back to Settings');
    assert.strictEqual(bad.length, 0, p + ' has controls labeled with the page title: ' + bad.map(e => e.text).join(' | '));
  });
});

test('calibrate page still produces axis-labeled throttle lines', () => {
  assert.ok(scrape('calibrate').elements.some(e => /Axis\s+\d+/i.test(e.text)), 'calibrate regressed');
});

test('nav-rail Settings link still reads "(current page)" on a detail page', () => {
  assert.ok(scrape('aircraft').elements.some(e => /Settings \(current page\)/.test(e.text)),
    'nav-rail current-page marker lost');
});
