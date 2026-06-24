const { test } = require('node:test');
const assert = require('node:assert');
const { scrape } = require('./run');

test('unit toggles render as 2-option selects reading the LIVE active unit (from el.checked) in full words', () => {
  // This EFB build renders the unit toggles as TEXTLESS checkboxes (::before/::after empty,
  // Settings lags until Save), so the active unit can only come from el.checked via UNIT_PAIRS.
  // The agent reports them as 2-option SELECTS (clearer for a screen reader than a bare checkbox).
  const els = scrape('unittoggle');
  const sel = {};
  els.filter(e => e.controlType === 'select').forEach(e => { sel[e.text] = e; });
  // active unit derived from el.checked (weight unchecked->kg, distance/temp checked->nm/C), full words
  assert.equal(sel['Weight Unit'] && sel['Weight Unit'].value, 'kilograms', 'weight unchecked -> kilograms');
  assert.equal(sel['Distance Unit'] && sel['Distance Unit'].value, 'nautical miles', 'distance checked -> nautical miles');
  assert.equal(sel['Temperature Unit'] && sel['Temperature Unit'].value, 'Celsius', 'temperature checked -> Celsius');
  // both unit options are offered, in full words, so the user can pick the other one
  assert.deepEqual(sel['Weight Unit'].options, ['kilograms', 'pounds'], 'weight options');
  assert.deepEqual(sel['Temperature Unit'].options, ['Fahrenheit', 'Celsius'], 'temperature options');
  // never the abbreviated form as the displayed value
  assert.ok(!Object.values(sel).some(e => /^(kg|nm|c|lbs|f|km|ft|m|hpa|inhg)$/i.test(e.value)), 'no abbreviated unit leaks');
});

test('Ground Operations active sub-tab marked "(selected)" via *_highlighted', () => {
  const els = scrape('groundtabs');
  const b = els.filter(e => e.kind === 'button').map(e => e.text);
  assert.ok(b.includes('Ground Maintenance (selected)'), 'highlighted ground-ops tab -> (selected)');
  assert.ok(b.includes('Ground Connections'), 'inactive ground-ops tab present');
  assert.ok(!b.some(t => /^Ground Connections \(/.test(t)), 'inactive ground-ops tab unmarked');
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
