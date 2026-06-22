const { test } = require('node:test');
const assert = require('node:assert');
const { scrape } = require('./run');

test('unit toggles read the LIVE active unit (::after) in full words', () => {
  const els = scrape('unittoggle');
  const cb = els.filter(e => e.controlType === 'checkbox').map(e => e.text);
  // ::after is the active unit; expanded to full words
  assert.ok(cb.includes('Weight Unit: kilograms'), 'weight ::after=kg -> kilograms');
  assert.ok(cb.includes('Distance Unit: nautical miles'), 'distance ::after=nm -> nautical miles');
  assert.ok(cb.includes('Temperature Unit: Celsius'), 'temperature ::after=C -> Celsius');
  // never the stale/abbreviated form
  assert.ok(!cb.some(t => /: (kg|nm|C)\b/.test(t)), 'no abbreviated unit leaks');
});

test('duplicate buttons with junk id qualifiers disambiguate by section heading', () => {
  const els = scrape('dupsection');
  const btns = els.filter(e => e.kind === 'button').map(e => e.text).sort();
  assert.deepStrictEqual(btns, ['Aircraft: Import From OFP', 'Airport: Import From OFP']);
});

test('active in-page tab marked "(selected)", active nav page "(current page)", others unmarked', () => {
  const els = scrape('tabs');
  const btns = els.filter(e => e.kind === 'button').map(e => e.text);
  assert.ok(btns.includes('Take Off (selected)'), 'active opt_page_button -> (selected)');
  assert.ok(btns.includes('Landing Dispatch'), 'inactive tab present');
  assert.ok(!btns.some(t => /^Landing Dispatch \(/.test(t)), 'inactive tab unmarked');
  assert.ok(btns.includes('Preferences (current page)'), 'active efb_main_menu_button -> (current page)');
  assert.ok(btns.includes('Charts'), 'inactive nav present');
  assert.ok(!btns.some(t => /^Charts \(/.test(t)), 'inactive nav unmarked');
});
