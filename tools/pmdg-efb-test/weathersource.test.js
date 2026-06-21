const { test } = require('node:test');
const assert = require('node:assert');
const { load, scrape } = require('./run');

// The PMDG tablet stores a `weather_source` setting (Real World / Sim) but renders NO control
// for it. We synthesize an accessible select on the Preferences page that reads + drives it via
// Settings.updateSetting('weather_source', ...).

function prefsWithSettings(weatherSource) {
  const ctx = load('settings', { settings: { weather_source: weatherSource } });
  ctx.window.WeatherSource = { AWC: 'REAL-WORLD', SIM: 'SIM' };
  ctx.captured = [];
  ctx.window.Settings.updateSetting = function (k, v) { ctx.captured.push([k, v]); ctx.window.Settings[k] = v; };
  return ctx;
}

test('synthetic Weather Source select appears on the Preferences page with the active value', () => {
  const { A } = prefsWithSettings('REAL-WORLD');
  const els = JSON.parse(A.scrape()).elements;
  const ws = els.find(e => e.controlType === 'select' && e.text === 'Weather Source');
  assert.ok(ws, 'Weather Source select present on Preferences');
  assert.deepStrictEqual(ws.options, ['Real World', 'Sim']);
  assert.strictEqual(ws.value, 'Real World');
});

test('Weather Source reflects the SIM value', () => {
  const { A } = prefsWithSettings('SIM');
  const ws = JSON.parse(A.scrape()).elements.find(e => e.controlType === 'select' && e.text === 'Weather Source');
  assert.strictEqual(ws.value, 'Sim');
});

test('setting the Weather Source select drives Settings.updateSetting with the enum value', () => {
  const ctx = prefsWithSettings('REAL-WORLD');
  const ws = JSON.parse(ctx.A.scrape()).elements.find(e => e.controlType === 'select' && e.text === 'Weather Source');
  const r = ctx.A.setValue(ws.idx, 'Sim');
  assert.match(r, /OK/);
  assert.deepStrictEqual(ctx.captured, [['weather_source', 'SIM']]);
  // and back to Real World
  ctx.A.setValue(ws.idx, 'Real World');
  assert.deepStrictEqual(ctx.captured[1], ['weather_source', 'REAL-WORLD']);
});

test('Weather Source is NOT added on non-Preferences pages', () => {
  const els = scrape('dashfull');
  assert.ok(!els.some(e => e.text === 'Weather Source'), 'no synthetic control off the Preferences page');
});
