// setValue disabled guard (Task 8.2 / JS-2): clickElement already honors
// A.disabledFor (agent.js ~:2354 — a disabled control must be REPORTED, never
// ACTUATED, because a synthetic click()/value-set bypasses pointer-events-none
// the same way a real tap could not get through). setValue's toggle/checkbox
// branch and its final clickNode fallback lacked that check — a set on a
// disabled toggle could actuate it. These tests build a toggle styled exactly
// the way disabledFor detects (pointer-events-none, per agent.js:684) and
// prove setValue neither flips its state nor dispatches a click on it.
const { test } = require('node:test');
const assert = require('node:assert');
const { JSDOM } = require('jsdom');
const fs = require('node:fs');
const path = require('node:path');
const AGENT = path.join(__dirname, '..', '..', 'MSFSBlindAssist', 'Resources', 'coherent-flypad-agent.js');

function load(html) {
  const dom = new JSDOM('<!DOCTYPE html><body>' + html + '</body>', { runScripts: 'outside-only' });
  const w = dom.window;
  w.SimVar = { GetSimVarValue() { return 0; }, SetSimVarValue() {} };
  w.eval(fs.readFileSync(AGENT, 'utf8'));
  return w;
}

// flyPad Toggle switch shape (UtilComponents/Form/Toggle): rounded-full +
// cursor-pointer + w-14 pill with a knob child (agent.js:182). Starts OFF
// (knob has no translate-x-5, per valueOf at agent.js:557-559). Disabled via
// pointer-events-none on the node itself (agent.js:684) — FBW's real disable
// idiom, which a synthetic click() would otherwise bypass.
function disabledToggle(idx) {
  return '<div class="pointer-events-none rounded-full cursor-pointer w-14" data-fbw-efb-idx="' + idx + '">' +
           '<div class="knob"></div>' +
         '</div>';
}

test('setValue on a disabled toggle does not actuate it', () => {
  const w = load(disabledToggle(0));
  const A = w.__MSFSBA_FLYPAD;
  const node = w.document.querySelector('[data-fbw-efb-idx="0"]');

  // Sanity: fixture really is what disabledFor/classify key on.
  assert.strictEqual(A.classify(node), 'toggle', 'fixture not classified as a toggle');
  assert.strictEqual(A.disabledFor(node), true, 'fixture not detected as disabled');
  assert.strictEqual(A.valueOf('toggle', node), 'false', 'fixture must start OFF for the want!==cur branch to fire');

  let clicked = false;
  node.addEventListener('click', () => { clicked = true; });

  const result = A.setValue(0, 'true'); // want=true, cur=false -> would actuate pre-fix

  assert.strictEqual(result, 'disabled', 'setValue must report "disabled" for a disabled control, not "ok"');
  assert.strictEqual(clicked, false, 'setValue must not dispatch a synthetic click on a disabled toggle');
  assert.strictEqual(A.valueOf('toggle', node), 'false', 'disabled toggle state must not change');
});

test('setValue on an ENABLED toggle still actuates it (guard is not overbroad)', () => {
  const w = load(
    '<div class="rounded-full cursor-pointer w-14" data-fbw-efb-idx="0"><div class="knob"></div></div>'
  );
  const A = w.__MSFSBA_FLYPAD;
  const node = w.document.querySelector('[data-fbw-efb-idx="0"]');
  assert.strictEqual(A.disabledFor(node), false, 'fixture must NOT be detected as disabled');

  let clicked = false;
  node.addEventListener('click', () => { clicked = true; });

  const result = A.setValue(0, 'true');

  assert.strictEqual(result, 'ok');
  assert.strictEqual(clicked, true, 'setValue must still click an enabled toggle to flip it');
});

test('setValue on a disabled generic control (clickNode fallback) does not actuate it', () => {
  // A non-input/select/toggle/checkbox/contenteditable node (e.g. a slider-ish
  // div) falls through setValue to the final "click as a fallback" branch.
  const w = load('<div class="pointer-events-none" data-fbw-efb-idx="0"></div>');
  const A = w.__MSFSBA_FLYPAD;
  const node = w.document.querySelector('[data-fbw-efb-idx="0"]');
  assert.strictEqual(A.disabledFor(node), true, 'fixture not detected as disabled');

  let clicked = false;
  node.addEventListener('click', () => { clicked = true; });

  const result = A.setValue(0, 'anything');

  assert.strictEqual(result, 'disabled', 'setValue must report "disabled", not fall through to clickNode');
  assert.strictEqual(clicked, false, 'setValue must not dispatch a synthetic click on a disabled fallback control');
});
