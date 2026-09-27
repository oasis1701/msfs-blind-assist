'use strict';
const { test } = require('node:test');
const assert = require('node:assert');
const { load, scrape } = require('./run');

// Every setting on every Options section, in EFB order, with the value the 2026-09-05 capture held.
const SECTIONS = {
  'options-general': [['Weight Units', ['Metric', 'Imperial'], 'Imperial'], ['Temperature Units', ['°C', '°F'], '°C'],
    ['MANPADS Defense System', ['Hidden', 'Visible'], 'Hidden'], ['Pause at TOD', ['No', 'Yes'], 'No']],
  'options-systems': [['Automatic Anti-Ice', ['Disabled', 'Enabled'], 'Enabled'], ['Digital Standby', ['Disabled', 'Enabled'], 'Disabled'],
    ['Fuel Dipstick', ['Disabled', 'Enabled'], 'Enabled'], ['RCWS', ['Disabled', 'Enabled'], 'Disabled'], ['Tape Displays', ['Disabled', 'Enabled'], 'Disabled'],
    ['High Decel Rate ABS', ['Disabled', 'Enabled'], 'Disabled'], ['Single Cue FD', ['Disabled', 'Enabled'], 'Disabled'],
    ['Wind Vector Type', ['Vector', 'Track'], 'Track'], ['Radio Altitude Behavior', ['Standard', 'Rising Box', 'Rising Runway'], 'Standard'],
    ['WBS', ['Not Installed', 'Installed'], 'Installed'], ['Gear Light Type', ['Classic', 'New'], 'New']],
  'options-caws': [['Altitude Alert Type', ['None', 'Tone', 'Voice and Tone'], 'Voice and Tone'],
    ['Altitude Alert Conditions', ['On Deviation', 'On Capture or Deviation'], 'On Capture or Deviation'],
    ['2500ft Callout', ['Disabled', 'Enabled'], 'Enabled'], ['1000ft Callout', ['Disabled', 'Enabled'], 'Enabled'], ['500ft Callout', ['Disabled', 'Enabled'], 'Enabled'],
    ['400ft Callout', ['Disabled', 'Enabled'], 'Enabled'], ['300ft Callout', ['Disabled', 'Enabled'], 'Enabled'], ['200ft Callout', ['Disabled', 'Enabled'], 'Enabled'],
    ['100ft Callout', ['Disabled', 'Enabled'], 'Enabled'], ['50ft to 10ft Callouts', ['Disabled', 'Enabled'], 'Enabled'],
    ['Tire Failure Alert', ['Always', 'Not On TO/LDG'], 'Always']],
  'options-perf': [['High-Efficiency Flap Pylon', ['Disabled', 'Enabled'], 'Disabled'], ['Deflected Ailerons', ['Disabled', 'Enabled'], 'Disabled']],
  'options-comms': [['CPDLC Provider', ['SayIntentions AI', 'Hoppie'], 'Hoppie']],
  'options-behavior': [['Allow Hardware Overforce', ['No', 'Yes'], 'Yes'], ['Automatic Seatbelt Behavior', ['Configuration', 'Altitude'], 'Altitude'],
    ['IRS Align Time', ['Instant', 'Fast', 'Realistic'], 'Fast'], ['Baro Sync', ['None', 'Capt + FO', 'All'], 'All'],
    ['Parking Brake Behavior', ['Simplified', 'Realistic'], 'Simplified'], ['Sync Minimums', ['No', 'Yes'], 'Yes'],
    ['Show Physical Throttle Position', ['Never', 'When moving', 'Always'], 'Never'], ['Scroll Acceleration', ['Disabled', 'Enabled'], 'Enabled'],
    ['Automatic Cabin Shades', ['Disabled', 'Enabled'], 'Enabled'], ['Cabin Light Behavior', ['Automatic', 'Manual'], 'Automatic']],
};

test('every Options setting is exactly one dropdown: caption as name, EFB order, highlighted value', () => {
  for (const [fx, rows] of Object.entries(SECTIONS)) {
    const els = scrape(fx);
    const sels = els.filter(e => e.controlType === 'select');
    assert.deepStrictEqual(sels.map(e => [e.text, e.options, e.value]), rows, fx);
    for (const [name] of rows) assert.ok(!els.some(e => e.kind === 'static' && e.text === name), fx + ': caption "' + name + '" also read as a loose line');
  }
});

test('the choice buttons are never read as bare buttons', () => {
  const btns = scrape('options-general').filter(e => e.kind === 'button').map(e => e.text);
  for (const t of ['Metric', 'Imperial', '°C', '°F', 'Hidden', 'Visible', 'No', 'Yes']) assert.ok(!btns.includes(t), t);
});

test('the Perf "Flaps" 35/50 pair is a dropdown too', () => {
  const f = scrape('perf-landing').find(e => e.controlType === 'select' && e.text === 'Flaps');
  assert.deepStrictEqual([f.options, f.value], [['35', '50'], '35']);
});

test('a setting the EFB greyed out reads its reason and is not a dropdown', () => {
  const els = scrape('options-disabled-toggle', { autoVis: true, nav: 'Options' });
  assert.ok(els.some(e => e.kind === 'static' && e.text === 'MANPADS Defense System: N/A on Pax Model'));
  assert.ok(!els.some(e => e.controlType === 'select'));
  assert.ok(!els.some(e => e.kind === 'button' && /N\/A/.test(e.text)));
});

test('choosing a value presses that choice\'s button; an unknown value presses nothing', () => {
  const { A, document } = load('options-general');
  const sel = JSON.parse(A.scrape()).elements.find(e => e.controlType === 'select' && e.text === 'Weight Units');
  const group = document.querySelector('[data-md11-efb-idx="' + sel.idx + '"]');
  assert.equal(group.getAttribute('role'), 'group');
  const pressed = [];
  // A.click fires the pointer/mouse sequence AND el.click(); count presses on mousedown (once each).
  for (const b of group.querySelectorAll('button')) b.addEventListener('mousedown', () => pressed.push(b.textContent.trim()));
  assert.equal(A.setValue(String(sel.idx), 'Metric'), true);
  assert.deepStrictEqual(pressed, ['Metric']);
  assert.equal(A.setValue(String(sel.idx), 'Furlongs'), false);
  assert.deepStrictEqual(pressed, ['Metric']);
});
