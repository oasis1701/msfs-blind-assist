const { test } = require('node:test');
const assert = require('node:assert');
const { scrape } = require('./run');

test('settings page label is not doubled', () => {
  const page = scrape('aircraft').page;
  assert.ok(!/Settings:\s*Settings/i.test(page), 'page label still doubled: ' + page);
  assert.match(page, /Aircraft Options/);
});
