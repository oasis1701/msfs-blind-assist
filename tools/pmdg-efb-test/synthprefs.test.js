const { test } = require('node:test');
const assert = require('node:assert');
const { load } = require('./run');

// Synthetic Preferences controls for config-only settings with no native UI:
// time_format (UTC/Local, via updateSetting) and selected_map / Map Provider
// (Navigraph/PMDG, via Dashboard.dualMap.switchMap + updateSetting, auth-guarded).

function prefs(settings) {
  const ctx = load('settings', { settings: settings });
  ctx.window.WeatherSource = { AWC: 'REAL-WORLD', SIM: 'SIM' };
  ctx.updates = [];
  ctx.window.Settings.updateSetting = function (k, v) { ctx.updates.push([k, v]); ctx.window.Settings[k] = v; };
  ctx.switched = [];
  ctx.window.DualMap = { isNGAuthed: true };
  ctx.window.Dashboard = { dualMap: { switchMap: function (m) { ctx.switched.push(m); } } };
  return ctx;
}
function find(A, label) { return JSON.parse(A.scrape()).elements.find(e => e.controlType === 'select' && e.text === label); }

test('Time Format select appears with the active value + options', () => {
  const { A } = prefs({ time_format: 'utc' });
  const tf = find(A, 'Time Format');
  assert.ok(tf, 'Time Format present');
  assert.deepStrictEqual(tf.options, ['UTC', 'Local']);
  assert.strictEqual(tf.value, 'UTC');
});

test('setting Time Format drives updateSetting(utc/local)', () => {
  const ctx = prefs({ time_format: 'utc' });
  const tf = find(ctx.A, 'Time Format');
  ctx.A.setValue(tf.idx, 'Local');
  assert.deepStrictEqual(ctx.updates, [['time_format', 'local']]);
  ctx.A.setValue(tf.idx, 'UTC');
  assert.deepStrictEqual(ctx.updates[1], ['time_format', 'utc']);
});

test('Map Provider select appears with the active value + options', () => {
  const { A } = prefs({ selected_map: 'navigraph' });
  const mp = find(A, 'Map Provider');
  assert.ok(mp, 'Map Provider present');
  assert.deepStrictEqual(mp.options, ['Navigraph', 'PMDG']);
  assert.strictEqual(mp.value, 'Navigraph');
});

test('setting Map Provider switches the live map AND persists', () => {
  const ctx = prefs({ selected_map: 'navigraph' });
  const mp = find(ctx.A, 'Map Provider');
  ctx.A.setValue(mp.idx, 'PMDG');
  assert.deepStrictEqual(ctx.switched, ['pmdg'], 'switchMap called with pmdg');
  assert.ok(ctx.updates.some(u => u[0] === 'selected_map' && u[1] === 'pmdg'), 'selected_map persisted');
});

test('Map Provider to Navigraph while unauthenticated does not desync (no persist)', () => {
  const ctx = prefs({ selected_map: 'pmdg' });
  ctx.window.DualMap.isNGAuthed = false;
  const mp = find(ctx.A, 'Map Provider');
  ctx.A.setValue(mp.idx, 'Navigraph');
  assert.ok(!ctx.updates.some(u => u[0] === 'selected_map'), 'selected_map NOT persisted when switch is blocked');
});

test('all three synthetic controls coexist and are off-Preferences absent', () => {
  const { A } = prefs({ weather_source: 'REAL-WORLD', time_format: 'utc', selected_map: 'navigraph' });
  const labels = JSON.parse(A.scrape()).elements.filter(e => e.controlType === 'select').map(e => e.text);
  assert.ok(labels.includes('Weather Source') && labels.includes('Time Format') && labels.includes('Map Provider'));
});

test('synthetics land in the settings list (after the last pref control, before Sign Out) — NOT in the nav rail', () => {
  // synthpos.html has a NAV RAIL whose Preferences button id is also "efb_preferences_*";
  // the splice must not anchor on it.
  const ctx = load('synthpos', { settings: { weather_source: 'REAL-WORLD', time_format: 'utc', selected_map: 'navigraph' } });
  ctx.window.Settings.updateSetting = function (k, v) { ctx.window.Settings[k] = v; };
  const els = JSON.parse(ctx.A.scrape()).elements;
  const at = (pred) => els.findIndex(pred);
  const mapZoom = at(e => e.controlType === 'select' && e.text === 'Map Zoom Step');
  const signOut = at(e => e.kind === 'button' && /Sign Out/.test(e.text));
  assert.ok(mapZoom >= 0 && signOut > mapZoom, 'anchors present in order');
  ['Weather Source', 'Time Format', 'Map Provider'].forEach(function (label) {
    const i = at(e => e.controlType === 'select' && e.text === label);
    assert.ok(i > mapZoom && i < signOut, label + ' must sit between Map Zoom Step and Sign Out (got idx ' + i + ', mapZoom ' + mapZoom + ', signOut ' + signOut + ')');
  });
});
