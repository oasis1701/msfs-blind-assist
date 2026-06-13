// A32NX flyPad Settings + Doors regression. Fixtures captured LIVE on the A320neo
// FlyByWire (2026-06-06) with data-vis/data-rect baked in by _capture_flypad_fixture.js.
// Mirrors the A380 assertions (settings.test.js / doors.test.js) against the A320's own
// captured DOM, locking in the shared coherent-flypad-agent.js behaviour on the A320.
//
// NOTE: the precise door NAMES (Forward Left Door, …) resolve from the React fiber and are
// verified LIVE (jsdom has no fibers, so the offline ground fixture shows generic FBW labels
// like "Door Fwd"). The fixture therefore asserts door STATE + GROUPING only — exactly as the
// A380 doors.test.js does for its ground fixture.
const { test } = require('node:test');
const assert = require('node:assert');
const { scrape } = require('./run');
const texts = (n) => scrape(n).elements.map(e => e.text);

// ---- Settings skeleton: builder fired (back link present on every detail page) ----
test('a320 settings detail pages start with "Back to Settings"', () => {
  for (const n of ['a320-aircraft', 'a320-simoptions', 'a320-realism', 'a320-thirdparty', 'a320-atsu_aoc', 'a320-audio', 'a320-flypad']) {
    assert.ok(texts(n).some(t => t === 'Back to Settings'), n + ': no back link — settings builder did not fire');
  }
});

// ---- Graceful degrade: About has no controls, reads via the generic pass, not blank ----
test('a320 about page readable (degrade), page label not doubled', () => {
  const t = texts('a320-about');
  assert.ok(t.some(x => x === 'Settings - About'), 'About page label missing');
  assert.ok(!t.some(x => /^Settings:\s*Settings - About$/.test(x)), 'About page label doubled');
  assert.ok(t.some(x => x === 'flyPadOS 3'), 'About content missing (blank degrade)');
});

// ---- Settings index: the static category list must NOT read "(selected)" ----
test('a320 settings index: no category card reads "(selected)"', () => {
  const cats = scrape('a320-index').elements.filter(e => e.kind === 'link' && e.navRail !== true);
  assert.ok(cats.length >= 6, 'expected the settings category links, got ' + cats.length);
  assert.ok(!cats.some(e => /\(selected\)/.test(e.text)), 'a category card falsely reads (selected): ' + cats.map(e => e.text).join(' | '));
});

// ---- SelectGroups: name line then tight options, exactly one (selected) ----
test('a320 aircraft PAX Signs: name line then options with one selected', () => {
  const t = texts('a320-aircraft');
  const i = t.indexOf('PAX Signs');
  assert.ok(i >= 0, 'no PAX Signs name line');
  assert.deepStrictEqual(t.slice(i + 1, i + 3), ['No Smoking', 'No Portable Device (selected)']);
});

test('a320 realism ADIRS Align Time: name then Instant/Fast/Real, one selected', () => {
  const t = texts('a320-realism');
  const i = t.indexOf('ADIRS Align Time');
  assert.ok(i >= 0, 'no ADIRS Align Time name line');
  assert.deepStrictEqual(t.slice(i + 1, i + 4), ['Instant', 'Fast', 'Real (selected)']);
});

test('a320 atsu/aoc ATIS source: exactly one option selected', () => {
  const t = texts('a320-atsu_aoc');
  const i = t.indexOf('ATIS/ATC Source');
  assert.ok(i >= 0, 'no ATIS/ATC Source name line');
  const opts = t.slice(i + 1, i + 5);
  assert.strictEqual(opts.filter(x => /\(selected\)/.test(x)).length, 1, 'ATIS source not exactly-one-selected: ' + opts.join(' | '));
});

// ---- Toggles read as a checkbox with state ----
test('a320 toggle reads as checkbox with state', () => {
  const us = scrape('a320-aircraft').elements.find(e => e.text === 'US Units');
  assert.ok(us, 'US Units toggle missing');
  assert.strictEqual(us.controlType, 'checkbox');
  assert.strictEqual(us.value, 'false');
});

// ---- Inputs labeled from their OWN row, no value folded into the label ----
test('a320 aircraft inputs use own-row labels, value not in label', () => {
  const inputs = scrape('a320-aircraft').elements.filter(e => e.kind === 'input');
  assert.ok(inputs.some(e => /Engine-Out Acceleration Height/i.test(e.text)), 'engine-out input mislabeled: ' + inputs.map(e => e.text).join(' | '));
  assert.ok(!inputs.some(e => /\(\d{3,}\)/.test(e.text)), 'value folded into an input label');
});

test('a320 simoptions external simbridge port input labeled', () => {
  const port = scrape('a320-simoptions').elements.find(e => e.kind === 'input' && e.value === '8380');
  assert.ok(port, 'port input missing');
  assert.match(port.text, /External SimBridge Port/i);
});

test('a320 thirdparty override simbrief id input labeled (not page title)', () => {
  assert.ok(scrape('a320-thirdparty').elements.some(e => e.kind === 'input' && /Override SimBrief User ID/i.test(e.text)),
    'simbrief id input mislabeled');
});

// ---- Sub-page / action links qualified by the setting name ----
test('a320 automatic call outs link qualified by setting name', () => {
  assert.ok(texts('a320-aircraft').some(x => /Automatic Call Outs:\s*Select/i.test(x)), 'call-outs link not qualified');
});

test('a320 throttle detents calibrate link qualified', () => {
  assert.ok(texts('a320-simoptions').some(x => /Throttle Detents:\s*Calibrate/i.test(x)), 'calibrate link not qualified');
});

// ---- Audio rc-slider phantoms suppressed; one labeled control per volume ----
test('a320 audio: no phantom "Settings - Audio" slider lines', () => {
  assert.strictEqual(scrape('a320-audio').elements.filter(e => /^Settings - Audio$/.test(e.text)).length, 0, 'phantom slider lines remain');
});

test('a320 audio: one labeled control per volume, no empty slider lines', () => {
  const t = texts('a320-audio');
  ['Exterior Master Volume', 'Engine Interior Volume', 'Wind Interior Volume'].forEach(v =>
    assert.ok(t.some(x => x === v || x.indexOf(v) === 0), 'missing volume: ' + v));
  assert.strictEqual(scrape('a320-audio').elements.filter(e => e.kind === 'slider' && !e.text).length, 0, 'empty slider lines remain');
});

// ---- Doors: open/closed/in-transit state, never "(active)", grouped contiguously ----
// (precise A320 names verified LIVE — generic offline because jsdom has no React fibers)
test('a320 ground doors read open/closed/in-transit, never "(active)"', () => {
  const doors = scrape('a320-ground').elements.filter(e => /\bdoor\b/i.test(e.text));
  assert.ok(doors.length >= 3, 'expected several door tiles, got ' + doors.length);
  doors.forEach(d => assert.match(d.text, /\((closed|open|in transit)\)$/, 'door without open/closed state: ' + d.text));
  assert.ok(!doors.some(d => /\(active\)/.test(d.text)), 'a door still reads "(active)": ' + doors.map(d => d.text).join(' | '));
});

test('a320 ground door tiles are grouped contiguously', () => {
  const tiles = scrape('a320-ground').elements.filter(e => e.kind === 'heading' || e.kind === 'button');
  const di = tiles.map((e, i) => (/\bdoor\b/i.test(e.text) ? i : -1)).filter(i => i >= 0);
  assert.ok(di.length >= 3, 'expected several door tiles');
  assert.strictEqual(di[di.length - 1] - di[0], di.length - 1, 'door tiles are not contiguous: positions ' + di.join(','));
});
