const { test } = require('node:test');
const assert = require('node:assert');
const { scrape } = require('./run');
const texts = (n) => scrape(n).elements.map(e => e.text);

// ---- Task 3: skeleton (back link + throttle-calib bail) ----
test('back link reads "Back to Settings"', () => {
  assert.ok(texts('aircraft').some(t => t === 'Back to Settings'), 'no back link');
});

test('throttle calibrate page is left to orderThrottleCalib (builder bails)', () => {
  assert.ok(texts('calibrate').some(t => /Axis\s+\d+/i.test(t)),
    'axis labels missing — settings builder wrongly owned calibrate');
});

// ---- Task 4: name line + toggles + segmented options ----
test('realism segmented selectors: name line then tight options with one selected', () => {
  const t = texts('realism');
  const i = t.indexOf('ADIRS Align Time');
  assert.ok(i >= 0, 'no ADIRS name line');
  assert.deepStrictEqual(t.slice(i + 1, i + 4), ['Instant', 'Fast (selected)', 'Real']);
});

test('toggle reads as a checkbox with state', () => {
  const us = scrape('aircraft').elements.find(e => e.text === 'US Units');
  assert.ok(us, 'US Units toggle missing');
  assert.strictEqual(us.controlType, 'checkbox');
  assert.strictEqual(us.value, 'true');
});

// ---- Task 5: inputs labeled from own row, no value duplication ----
test('engine-out accel height input uses its OWN row label', () => {
  const labels = scrape('aircraft').elements.filter(e => e.kind === 'input').map(e => e.text);
  assert.ok(labels.some(t => /Engine-Out Acceleration Height/i.test(t)), 'engine-out mislabeled: ' + labels.join(' | '));
  assert.ok(!labels.some(t => /\(\d{3,}\)/.test(t)), 'value folded into label: ' + labels.join(' | '));
});

test('external simbridge port input labeled correctly', () => {
  const port = scrape('simoptions').elements.find(e => e.kind === 'input' && e.value === '8380');
  assert.ok(port, 'port input missing');
  assert.match(port.text, /External SimBridge Port/i);
});

test('override simbrief user id input labeled (not the page title)', () => {
  assert.ok(scrape('thirdparty').elements.some(e => e.kind === 'input' && /Override SimBrief User ID/i.test(e.text)),
    'simbrief id input mislabeled');
});

// ---- Task 6: sub-page / action links qualified by setting name ----
test('automatic call outs select link qualified by setting name', () => {
  assert.ok(texts('aircraft').some(x => /Automatic Call Outs:\s*Select/i.test(x)), 'call-outs link not qualified');
});

test('throttle detents calibrate link qualified', () => {
  assert.ok(texts('simoptions').some(x => /Throttle Detents:\s*Calibrate/i.test(x)), 'calibrate link not qualified');
});

// ---- Task 7: Audio rc-slider phantoms suppressed ----
test('audio page has no phantom "Settings - Audio" slider lines', () => {
  const phantom = scrape('audio').elements.filter(e => /^Settings - Audio$/.test(e.text));
  assert.strictEqual(phantom.length, 0, 'phantom slider lines remain: ' + phantom.length);
});

test('audio page exposes one labeled control per volume', () => {
  const t = texts('audio');
  ['Exterior Master Volume', 'Engine Interior Volume', 'Wind Interior Volume'].forEach(v => {
    assert.ok(t.some(x => x === v || x.indexOf(v) === 0), 'missing volume: ' + v);
  });
  assert.strictEqual(scrape('audio').elements.filter(e => e.kind === 'slider' && !e.text).length, 0, 'empty slider lines remain');
});
