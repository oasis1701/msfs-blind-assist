'use strict';
// The every-control-named sweep: after the layer has run, every control on every captured screen
// must have an accessible name a screen reader can say. Re-run after each Fenix update (capture.md):
// a class the page renamed shows up here as a control the layer no longer reaches.
const test = require('node:test');
const assert = require('node:assert');
const { computeAccessibleName, getRole } = require('dom-accessibility-api');
const { load } = require('./load');

// The 13 captures, listed on purpose and never read from the fixtures folder: a capture that is
// missing must fail loudly here, and a hand-made fixture a later task adds has its own tests.
const CAPTURES = [
  'home', 'switcher', 'my-flight', 'ground-services', 'mass-balance', 'panel-states', 'depperf',
  'arrperf', 'settings', 'failures', 'documents', 'charts', 'quick-settings'
];

const CONTROLS = [
  'button', 'a[href]', 'input',
  '[role=switch]', '[role=checkbox]', '[role=radio]', '[role=combobox]', '[role=button]',
  '[role=tab]', '[role=slider]', '[tabindex]', 'div.appIcon'
].join(',');

// A name that says nothing: only dashes, slashes and spaces ("-", "--", " / "), or "on" / "x".
const EMPTY_NAME = /^[\s\-‐-―−/\\]*$/;
const BARE_NAMES = new Set(['on', 'x']);

function nameIsBad(name) {
  const n = name.trim();
  return EMPTY_NAME.test(n) || BARE_NAMES.has(n.toLowerCase());
}

function tagAndClass(el) {
  const cls = (el.getAttribute('class') || '').split(/\s+/).filter(Boolean).join('.');
  return el.localName + (cls ? '.' + cls : '');
}

// Returns { controls, offenders }: how many controls the sweep looked at and the ones whose
// computed name is bad, each described by tag, class, role and the name it computed.
function sweep(window) {
  const controls = [...window.document.querySelectorAll(CONTROLS)]
    .filter(el => !(el.localName === 'input' && el.type === 'hidden'));
  const offenders = [];
  for (const el of controls) {
    const name = computeAccessibleName(el);
    if (nameIsBad(name)) {
      offenders.push(tagAndClass(el) + ' [role=' + getRole(el) + '] name=' + JSON.stringify(name));
    }
  }
  return { controls: controls.length, offenders };
}

for (const name of CAPTURES) {
  test('Every_control_is_named_on_' + name, async () => {
    const window = await load(name);
    try {
      const { controls, offenders } = sweep(window);
      assert.ok(controls > 0, name + ': the sweep found no controls at all -- is the capture empty?');
      assert.strictEqual(offenders.length, 0, name + ': ' + offenders.length + ' of ' + controls +
        ' controls have no usable name:\n  ' + offenders.join('\n  '));
    } finally {
      window.close();
    }
  });
}

test('Without_the_layer_the_page_fails_the_sweep', async () => {
  const window = await load('ground-services', { layer: false });
  try {
    const { offenders } = sweep(window);
    assert.ok(offenders.length >= 14,
      'ground-services without the layer: expected at least 14 unnamed controls, found ' +
      offenders.length + ':\n  ' + offenders.join('\n  '));
  } finally {
    window.close();
  }
});
