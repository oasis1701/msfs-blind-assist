const { test } = require('node:test');
const assert = require('node:assert');
const { scrape } = require('./run');

// The settings index renders all 8 category cards with the standalone
// bg-theme-accent token as their BASE background (verified in fixtures/index.html),
// so isActiveTab over-reports every category as "(selected)". The index does not
// highlight an active category — none should be marked.
test('settings index marks at most one category (selected)', () => {
  const cats = scrape('index').elements.filter(e =>
    e.kind === 'link' && /(Aircraft Options|Sim Options|Realism|3rd Party|ATSU|Audio|flyPad|About)/i.test(e.text));
  const selected = cats.filter(e => /\(selected\)/.test(e.text));
  assert.ok(selected.length <= 1, 'multiple categories marked selected: ' + selected.map(e => e.text).join(' | '));
});
