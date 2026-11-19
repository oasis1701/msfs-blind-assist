'use strict';
// The every-control-named sweep: after the layer has run, every control on every captured screen
// must have an accessible name a screen reader can say. Re-run after each Fenix update (capture.md):
// a class the page renamed shows up here as a control the layer no longer reaches.
const test = require('node:test');
const assert = require('node:assert');
const { JSDOM } = require('jsdom');
const { computeAccessibleName, getRole } = require('dom-accessibility-api');
const { load } = require('./load');

// The 22 captures, listed on purpose and never read from the fixtures folder: a capture that is
// missing must fail loudly here, and a hand-made fixture a later task adds has its own tests.
const CAPTURES = [
  'home', 'switcher', 'my-flight', 'ground-services', 'mass-balance', 'panel-states', 'depperf',
  'arrperf', 'arrperf-electrical', 'settings', 'settings-audio', 'settings-cabin', 'settings-connections',
  'settings-ground-services', 'settings-misc', 'settings-printer', 'settings-realism', 'settings-overflow-open',
  'failures', 'documents', 'charts', 'quick-settings'
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

// A control a screen reader never meets: it, or any ancestor, carries aria-hidden="true" or the `hidden`
// attribute (a framework's focus-trap anchors, a closed panel). Nothing to name there, and the layer does
// not override a page's own aria-hidden to make a name possible.
const hiddenFromReaders = el => el.closest('[aria-hidden="true"], [hidden]') !== null;

// Returns { controls, offenders }: how many controls the sweep looked at and the ones whose
// computed name is bad, each described by tag, class, role and the name it computed.
function sweep(window) {
  const controls = [...window.document.querySelectorAll(CONTROLS)]
    .filter(el => !(el.localName === 'input' && el.type === 'hidden'))
    .filter(el => !hiddenFromReaders(el));
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

// The sweep skips what a screen reader never meets, and still catches what it does meet.
test('The_sweep_skips_controls_hidden_from_a_screen_reader_and_still_catches_a_visible_unnamed_one', () => {
  const { window } = new JSDOM(
    '<!doctype html><html><body>' +
    '<button>Named</button>' +
    '<button class="itself-hidden" aria-hidden="true"></button>' +
    '<input class="hidden-attribute" type="text" hidden>' +
    '<div hidden><button class="inside-hidden"></button></div>' +
    '<div aria-hidden="true"><div><input class="inside-aria-hidden" type="checkbox"><div tabindex="0"></div></div></div>' +
    '<button class="visible-unnamed"></button>' +
    '<div aria-hidden="false"><a class="not-hidden" href="#x"></a></div>' +
    '</body></html>');
  try {
    const { controls, offenders } = sweep(window);
    // "Named", the visible unnamed button and the link under aria-hidden="false" are met; the rest are not.
    assert.strictEqual(controls, 3);
    assert.deepStrictEqual(offenders.map(o => o.split(' ')[0]), ['button.visible-unnamed', 'a.not-hidden']);
  } finally {
    window.close();
  }
});

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
