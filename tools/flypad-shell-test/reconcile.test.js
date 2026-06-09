'use strict';
// Regression tests for the FBW flyPad WebView2 shell reconcile
// (MSFSBlindAssist/Resources/flypad-shell.html). Loads the REAL shell HTML under
// jsdom, captures its webview "message" handler, and drives render() with realistic
// scrape payloads to prove that switching Ground sub-tabs (Fuel -> Payload, both
// reporting page "Ground") does NOT leave stale/cross-patched controls behind, while
// same-page value updates still reuse nodes in place (NVDA focus preservation).

const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs');
const path = require('path');
const { JSDOM } = require('jsdom');

const SHELL = path.join(__dirname, '..', '..', 'MSFSBlindAssist', 'Resources', 'flypad-shell.html');

// Build a fresh shell instance: parse the HTML, stub window.chrome.webview, run the
// inline script, and return helpers to fire renders and inspect #root.
function loadShell() {
  const html = fs.readFileSync(SHELL, 'utf8');
  const dom = new JSDOM(html, { runScripts: 'outside-only' });
  const { window } = dom;

  let messageHandler = null;
  window.chrome = {
    webview: {
      postMessage() {},
      addEventListener(type, fn) { if (type === 'message') messageHandler = fn; },
      removeEventListener() {},
    },
  };

  // Extract and run every inline (no-src) <script> in the shell.
  const scripts = [...html.matchAll(/<script>([\s\S]*?)<\/script>/g)].map((m) => m[1]);
  for (const code of scripts) window.eval(code);

  assert.ok(messageHandler, 'shell did not register a webview message handler');

  return {
    window,
    render(payload) { messageHandler({ data: Object.assign({ type: 'render' }, payload) }); },
    message(obj) { messageHandler({ data: obj }); },
    byId(id) { return window.document.getElementById(id); },
    root() { return window.document.getElementById('root'); },
    rootText() { return window.document.getElementById('root').textContent; },
    buttonsByText() {
      return [...window.document.getElementById('root').querySelectorAll('button')].map((b) => b.textContent);
    },
  };
}

const FUEL = {
  page: 'Ground',
  elements: [
    { idx: 11, kind: 'link', controlType: '', text: 'Services', clickable: true },
    { idx: 12, kind: 'link', controlType: '', text: 'Fuel', clickable: true },
    { idx: 13, kind: 'link', controlType: '', text: 'Payload', clickable: true },
    { idx: 0, kind: 'heading', controlType: '', text: 'Total Fuel', level: 2 },
    { idx: 15, kind: 'button', controlType: '', text: 'Start refueling', clickable: true },
    { idx: 0, kind: 'heading', controlType: '', text: 'Refuel Time', level: 2 },
    { idx: 21, kind: 'button', controlType: '', text: 'Instant (selected)', clickable: true },
    { idx: 22, kind: 'button', controlType: '', text: 'Fast', clickable: true },
    { idx: 23, kind: 'button', controlType: '', text: 'Real', clickable: true },
  ],
};

const PAYLOAD = {
  page: 'Ground', // SAME page label as Fuel — this is the sub-tab case
  elements: [
    { idx: 11, kind: 'link', controlType: '', text: 'Services', clickable: true },
    { idx: 12, kind: 'link', controlType: '', text: 'Fuel', clickable: true },
    { idx: 13, kind: 'link', controlType: '', text: 'Payload', clickable: true },
    { idx: 0, kind: 'heading', controlType: '', text: 'Passengers', level: 2 },
    { idx: 15, kind: 'input', controlType: 'text', text: 'Passengers', value: '118' },
    { idx: 16, kind: 'input', controlType: 'text', text: 'Cargo (LBS)', value: '5192' },
    { idx: 20, kind: 'button', controlType: '', text: 'Start boarding', clickable: true },
    { idx: 21, kind: 'button', controlType: '', text: 'Start deboarding', clickable: true },
    { idx: 0, kind: 'heading', controlType: '', text: 'Boarding Time', level: 2 },
    { idx: 23, kind: 'button', controlType: '', text: 'Instant (selected)', clickable: true },
    { idx: 24, kind: 'button', controlType: '', text: 'Fast', clickable: true },
    { idx: 25, kind: 'button', controlType: '', text: 'Real', clickable: true },
  ],
};

test('Fuel -> Payload sub-tab switch leaves no stale/cross-patched Fuel controls', () => {
  const s = loadShell();
  s.render(FUEL);
  s.render(PAYLOAD);

  const text = s.rootText();
  // The Fuel-only controls must be gone (no cross-patched leftovers).
  assert.ok(!text.includes('Start refueling'), 'stale "Start refueling" button survived on Payload');
  assert.ok(!text.includes('Total Fuel'), 'stale "Total Fuel" heading survived on Payload');
  assert.ok(!text.includes('Refuel Time'), 'stale "Refuel Time" heading survived on Payload');

  // The Payload controls must be present and correct.
  assert.ok(text.includes('Start boarding'), 'Payload "Start boarding" missing');
  assert.ok(text.includes('Start deboarding'), 'Payload "Start deboarding" missing');
  assert.ok(text.includes('Passengers'), 'Payload "Passengers" missing');

  // The rate selector must appear ONCE, not duplicated across the two sub-tabs.
  const rateButtons = s.buttonsByText().filter((t) => ['Instant (selected)', 'Fast', 'Real'].includes(t));
  assert.strictEqual(rateButtons.length, 3, `expected one rate selector (3 buttons), got ${rateButtons.length}: ${rateButtons}`);

  // Total button count = links(3) + boarding/deboarding(2) + rate(3) = 8.
  assert.strictEqual(s.buttonsByText().length, 8, 'unexpected button count after sub-tab switch');
});

test('same-page value update reuses the input node in place (focus preservation)', () => {
  const s = loadShell();
  s.render(PAYLOAD);
  const inputBefore = s.root().querySelector('input[type="text"]');
  assert.strictEqual(inputBefore.value, '118');

  // Re-render the same page with only the passenger value changed.
  const PAYLOAD2 = JSON.parse(JSON.stringify(PAYLOAD));
  PAYLOAD2.elements.find((e) => e.text === 'Passengers' && e.controlType === 'text').value = '120';
  s.render(PAYLOAD2);

  const inputAfter = s.root().querySelector('input[type="text"]');
  assert.strictEqual(inputAfter, inputBefore, 'input node was rebuilt instead of patched in place (would reset NVDA focus)');
  assert.strictEqual(inputAfter.value, '120', 'patched input did not pick up the new value');
});

test('page change to a different label rebuilds cleanly too', () => {
  const s = loadShell();
  s.render(FUEL);
  s.render({ page: 'Settings', elements: [
    { idx: 0, kind: 'heading', controlType: '', text: 'flyPad Settings', level: 1 },
    { idx: 1, kind: 'checkbox', controlType: 'checkbox', text: 'Show Status Bar', value: 'true' },
  ] });
  const text = s.rootText();
  assert.ok(!text.includes('Start refueling'), 'Fuel control survived a full page change');
  assert.ok(text.includes('flyPad Settings'), 'Settings heading missing after page change');
});

test('field ids stay unique and correctly label-associated across a content shuffle', () => {
  const s = loadShell();
  // One input "Passengers" at scrape idx 5.
  s.render({ page: 'Ground', elements: [
    { idx: 5, kind: 'input', controlType: 'text', text: 'Passengers', value: '100' },
  ] });
  // Same page label: "Passengers" reused (now idx 7); a NEW "Cargo" input takes idx 5.
  // Under the old "i"+idx id scheme this produced two elements with id "i5".
  s.render({ page: 'Ground', elements: [
    { idx: 7, kind: 'input', controlType: 'text', text: 'Passengers', value: '100' },
    { idx: 5, kind: 'input', controlType: 'text', text: 'Cargo', value: '200' },
  ] });

  const root = s.root();
  const ids = [...root.querySelectorAll('input,select')].map((c) => c.id);
  assert.strictEqual(new Set(ids).size, ids.length, 'duplicate field ids: ' + ids.join(','));
  // Every label points at an input that exists, inside its own .fld row.
  [...root.querySelectorAll('label')].forEach((lab) => {
    const target = s.byId(lab.htmlFor);
    assert.ok(target, 'label points at a missing id: ' + lab.htmlFor);
    assert.ok(lab.closest('.fld').contains(target), 'label associated with a control outside its own row');
  });
});

test('reused control updates its data-idx so clicks route to the current element', () => {
  const s = loadShell();
  s.render({ page: 'Ground', elements: [{ idx: 5, kind: 'button', controlType: '', text: 'Start boarding', clickable: true }] });
  const b1 = s.root().querySelector('button');
  assert.strictEqual(b1.dataset.idx, '5');
  // Same control, re-stamped to idx 9 next scrape.
  s.render({ page: 'Ground', elements: [{ idx: 9, kind: 'button', controlType: '', text: 'Start boarding', clickable: true }] });
  const b2 = s.root().querySelector('button');
  assert.strictEqual(b2, b1, 'button was rebuilt instead of reused');
  assert.strictEqual(b2.dataset.idx, '9', 'data-idx was not patched to the current scrape idx');
});

test('disabled-only change is patched in place (node reused, not rebuilt)', () => {
  const s = loadShell();
  s.render({ page: 'Ground', elements: [{ idx: 3, kind: 'button', controlType: '', text: 'Start deboarding', clickable: true, disabled: false }] });
  const before = s.root().querySelector('button');
  assert.strictEqual(before.disabled, false);
  s.render({ page: 'Ground', elements: [{ idx: 3, kind: 'button', controlType: '', text: 'Start deboarding', clickable: true, disabled: true }] });
  const after = s.root().querySelector('button');
  assert.strictEqual(after, before, 'button rebuilt for a disabled-only change (would reset NVDA cursor)');
  assert.strictEqual(after.disabled, true);
});

test('a status message never rebuilds or touches #root', () => {
  const s = loadShell();
  s.render(PAYLOAD);
  const rootBefore = s.root().innerHTML;
  s.message({ type: 'status', text: 'Live: "Ground" (updated 2s ago)' });
  assert.strictEqual(s.byId('status').textContent, 'Live: "Ground" (updated 2s ago)', 'status text not applied');
  assert.strictEqual(s.root().innerHTML, rootBefore, 'status message mutated #root');
});

test('state-suffix change reuses the node in place (NVDA focus preservation)', () => {
  const s = loadShell();
  s.render({ page: 'Ground', elements: [
    { idx: 21, kind: 'button', controlType: '', text: 'Door Fwd', clickable: true },
  ] });
  const before = [...s.root().querySelectorAll('button')]
    .find((b) => b.textContent.indexOf('Door Fwd') === 0);
  assert.ok(before, 'baseline button not rendered');

  // Same control gains the dynamic "(active)" suffix — the reconcile key strips it
  // (baseLabel), so the node must be REUSED (patched in place), not destroyed +
  // rebuilt: rebuilding moves the screen-reader focus off the control the user
  // just activated.
  s.render({ page: 'Ground', elements: [
    { idx: 21, kind: 'button', controlType: '', text: 'Door Fwd (active)', clickable: true },
  ] });
  const after = [...s.root().querySelectorAll('button')]
    .find((b) => b.textContent.indexOf('Door Fwd') === 0);
  assert.ok(after, 'button missing after state change');
  assert.strictEqual(after, before, 'state-suffix change destroyed and rebuilt the node');
  assert.match(after.textContent, /\(active\)/, 'visible label did not update');
});
