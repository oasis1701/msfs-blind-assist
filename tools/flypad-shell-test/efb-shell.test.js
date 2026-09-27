'use strict';
// Tests for the SHIPPING EFB shell — the PageHtml verbatim string inside
// MSFSBlindAssist/Forms/FBWA380/FbwEfbForm.cs, the one document every EFB window (the flyPad, both
// PMDG tablets and the MD-11 EFB) actually renders. The string is extracted from the C# source,
// loaded under jsdom and driven through window.__render exactly as the form does, so a change to
// the shipping script is what these tests see. flypad-shell.html (reconcile.test.js) is a
// hand-maintained reconcile spec with no live region and no click announce; it cannot stand in
// for the announce behaviour pinned here.

const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs');
const path = require('path');
const { JSDOM } = require('jsdom');

const FORM = path.join(__dirname, '..', '..', 'MSFSBlindAssist', 'Forms', 'FBWA380', 'FbwEfbForm.cs');

// Pull the PageHtml verbatim string out of the C# source: "" is the verbatim escape for one
// quote, and the two {{...}} placeholders are what BuildPageHtml fills at runtime.
function shippingShellHtml() {
  const src = fs.readFileSync(FORM, 'utf8');
  const marker = 'PageHtml = @"';
  const start = src.indexOf(marker);
  assert.ok(start >= 0, 'PageHtml verbatim string not found in FbwEfbForm.cs');
  const open = start + marker.length;
  const close = src.indexOf('</html>";', open);
  assert.ok(close > open, 'PageHtml verbatim string has no </html>"; terminator');
  return src.slice(open, close + '</html>'.length)
    .replace(/""/g, '"')
    .replace(/\{\{TITLE\}\}/g, 'EFB')
    .replace(/\{\{NOUN\}\}/g, 'EFB');
}

function loadShell(t) {
  const html = shippingShellHtml();
  const dom = new JSDOM(html, { runScripts: 'outside-only' });
  const { window } = dom;
  const posted = [];
  window.chrome = { webview: { postMessage(m) { posted.push(m); }, addEventListener() {}, removeEventListener() {} } };
  const scripts = [...html.matchAll(/<script>([\s\S]*?)<\/script>/g)].map((m) => m[1]);
  assert.strictEqual(scripts.length, 1, 'expected exactly one inline <script> in PageHtml');
  // runScripts:'outside-only' gives window.eval the jsdom window's OWN realm, so the shell's bare
  // `document`/`window`/`setTimeout` resolve there. The MD-11 reader harness (tools/md11-efb-test/
  // run.js) documents the other case — a jsdom built WITHOUT runScripts hands back Node's own eval,
  // where those bare identifiers resolve against the Node global instead — so the Node globals are
  // pointed at this window too. Harmless when the realm eval is in force, and the difference stops
  // being a silent nothing-defined failure if a jsdom upgrade changes which one applies.
  //
  // Put them back when the test ends: this window is jsdom's, not Node's, and a leaked one
  // outliving its test is exactly the failure the assignment above exists to make visible.
  const prev = { window: global.window, document: global.document, had: 'window' in global };
  global.window = window;
  global.document = window.document;
  if (t && typeof t.after === 'function') {
    t.after(() => {
      if (prev.had) { global.window = prev.window; global.document = prev.document; }
      else { delete global.window; delete global.document; }
    });
  }
  window.eval(scripts[0]);
  assert.strictEqual(typeof window.__render, 'function', 'shell did not define window.__render');
  return {
    window,
    posted,
    render(page, items) { window.__render({ page, items }); },
    buttonByLabel(prefix) {
      return [...window.document.querySelectorAll('#list button')].find((b) => b.textContent.indexOf(prefix) === 0);
    },
    // What the aria-live region holds — the audible channel. announce() clears the region and
    // writes the text 30 ms later, so the read waits past that timer.
    async spoken() { await new Promise((r) => setTimeout(r, 80)); return window.document.getElementById('live').textContent; },
  };
}

// Items in the shape BuildRenderJson posts.
const btn = (idx, text, extra) => Object.assign({ idx, kind: 'button', controlType: '', text, clickable: true, level: 0, live: '', disabled: false }, extra || {});
const tab = (idx, text) => ({ idx, kind: 'tab', controlType: '', text, clickable: true, level: 0, live: '', disabled: false });

// The MD-11 reader stamps the arrow's key ('step-next:Runway'). Keyed by its label instead, the
// button would be a NEW node once '(now 06L)' became '(now 06R)': rebuilt under the pilot's focus,
// its new label never spoken.
test('a control flagged announceChange speaks its new label after the pilot\'s own press', async (t) => {
  const s = loadShell(t);
  s.render('Perf', [btn(5, 'Runway next (now 06L)', { announceChange: true, key: 'step-next:Runway' })]);
  assert.strictEqual(await s.spoken(), 'EFB page: Perf');
  const b = s.buttonByLabel('Runway next');
  assert.ok(b, 'stepper button not rendered');
  b.focus(); b.click();
  assert.strictEqual(await s.spoken(), 'Activating Runway next (now 06L)');
  assert.deepStrictEqual(s.posted, [JSON.stringify({ type: 'click', idx: '5' })]);
  // The next scrape shows the field one step on: patched in place (focus kept) and spoken.
  s.render('Perf', [btn(5, 'Runway next (now 06R)', { announceChange: true, key: 'step-next:Runway' })]);
  assert.strictEqual(s.buttonByLabel('Runway next'), b, 'stepper button was rebuilt instead of patched');
  assert.strictEqual(b.textContent, 'Runway next (now 06R)', 'visible label did not update');
  assert.strictEqual(await s.spoken(), 'Runway next (now 06R)');
});

// The MD-11 door/GPU/chocks tiles carry their state after a colon, and the flip a press produces is
// the OUTCOME the pilot asked for — the same class as the stepper, flagged the same way and keyed
// the same way (by the tile's name, 'tile:Passenger 1L').
test('a flagged tile speaks the state its own press produced', async (t) => {
  const s = loadShell(t);
  s.render('Services', [btn(9, 'Passenger 1L: Closed', { announceChange: true, key: 'tile:Passenger 1L' })]);
  assert.strictEqual(await s.spoken(), 'EFB page: Services');
  const door = s.buttonByLabel('Passenger 1L');
  door.focus(); door.click();
  assert.strictEqual(await s.spoken(), 'Activating Passenger 1L: Closed');
  s.render('Services', [btn(9, 'Passenger 1L: Open', { announceChange: true, key: 'tile:Passenger 1L' })]);
  assert.strictEqual(s.buttonByLabel('Passenger 1L'), door, 'tile was rebuilt instead of patched');
  assert.strictEqual(await s.spoken(), 'Passenger 1L: Open');
});

test('a control without the flag stays silent when its label changes after a press', async (t) => {
  const s = loadShell(t);
  // The PMDG tablet's nav bar: the pressed tab gains ' (current page)' on the next scrape.
  s.render('Dashboard', [tab(1, 'Dashboard (current page)'), tab(2, 'Performance'), btn(7, 'Baggage')]);
  assert.strictEqual(await s.spoken(), 'EFB page: Dashboard');
  const perf = s.buttonByLabel('Performance');
  perf.focus(); perf.click();
  assert.strictEqual(await s.spoken(), 'Activating Performance');
  s.render('Dashboard', [tab(1, 'Dashboard'), tab(2, 'Performance (current page)'), btn(7, 'Baggage')]);
  assert.strictEqual(s.buttonByLabel('Performance'), perf, 'tab was rebuilt instead of patched');
  assert.strictEqual(perf.textContent, 'Performance (current page)', 'visible label did not update');
  assert.strictEqual(await s.spoken(), 'Activating Performance', 'the press echo "(current page)" was announced');
  // The flyPad's service tiles: the pressed tile gains ' (called)'.
  const bag = s.buttonByLabel('Baggage');
  bag.focus(); bag.click();
  assert.strictEqual(await s.spoken(), 'Activating Baggage');
  s.render('Dashboard', [tab(1, 'Dashboard'), tab(2, 'Performance (current page)'), btn(7, 'Baggage (called)')]);
  assert.strictEqual(s.buttonByLabel('Baggage'), bag, 'tile was rebuilt instead of patched');
  assert.strictEqual(bag.textContent, 'Baggage (called)', 'visible label did not update');
  assert.strictEqual(await s.spoken(), 'Activating Baggage', 'the press echo "(called)" was announced');
});

// B5 (review round 2 of PR 189): the reconcile key is EXPLICIT, or it is the label — never a text
// heuristic. The flyPad ATC page repeats 'Set Active' / 'Set Standby' on every frequency card, so its
// agent prefixes the card: 'UNICOM 122.800: Set Active'. The after-colon strip this shell briefly
// carried keyed both buttons 'UNICOM 122.800', held apart only by DOM order, so a re-sort patched the
// Set Standby label onto the node the pilot was sitting on.
test('without a key, two labels that differ only after a colon are two keys, and a re-sort keeps each on its own node', (t) => {
  const s = loadShell(t);
  s.render('ATC', [btn(3, 'UNICOM 122.800: Set Active'), btn(4, 'UNICOM 122.800: Set Standby')]);
  const active = s.buttonByLabel('UNICOM 122.800: Set Active');
  const standby = s.buttonByLabel('UNICOM 122.800: Set Standby');
  assert.ok(active && standby && active !== standby, 'both ATC buttons rendered');
  assert.strictEqual(active.getAttribute('data-key'), 'btn|UNICOM 122.800: Set Active#1');
  assert.strictEqual(standby.getAttribute('data-key'), 'btn|UNICOM 122.800: Set Standby#1');
  s.render('ATC', [btn(3, 'UNICOM 122.800: Set Standby'), btn(4, 'UNICOM 122.800: Set Active')]);
  assert.strictEqual(s.buttonByLabel('UNICOM 122.800: Set Active'), active, 'Set Active was re-homed onto another node');
  assert.strictEqual(s.buttonByLabel('UNICOM 122.800: Set Standby'), standby, 'Set Standby was re-homed onto another node');
  assert.strictEqual(active.getAttribute('data-idx'), '4', 'the moved node carries its own current idx');
  assert.strictEqual(standby.getAttribute('data-idx'), '3');
});

// The MD-11 State page swaps 'Ready to Fly: Set as default' for its check mark, which the reader
// reads as 'Ready to Fly is the default' — no suffix rule relates the two labels, the agent's key does.
test('an explicit key keeps a control on its node when its label changes shape', (t) => {
  const s = loadShell(t);
  s.render('State', [btn(12, 'Ready to Fly: Set as default', { key: 'tile-action:Ready to Fly' })]);
  const b = s.buttonByLabel('Ready to Fly');
  assert.strictEqual(b.getAttribute('data-key'), 'btn|k:tile-action:Ready to Fly#1');
  s.render('State', [btn(12, 'Ready to Fly is the default', { key: 'tile-action:Ready to Fly' })]);
  assert.strictEqual(s.buttonByLabel('Ready to Fly'), b, 'the keyed control was rebuilt instead of patched');
  assert.strictEqual(b.textContent, 'Ready to Fly is the default', 'visible label did not update');
});

// The MD-11 State page once more, now for the control that REPLACES the one just pressed. The check
// mark the EFB swaps in is stamped on no DOM node, so a press on it could only reach the reader's
// A.find(idx) -> null and be dropped with nothing said; the reader emits it DISABLED instead. This
// is what the shell then owes the pilot: the node survives (same key), the outcome of their press is
// still spoken because the element is flagged, and a further press is refused out loud and posts
// nothing — rather than the shell's cheerful "Activating Ready to Fly is the default" over a control
// that does nothing.
test('a flagged control that arrives disabled keeps its node, speaks its outcome, then refuses a press', async (t) => {
  const s = loadShell(t);
  s.render('State', [btn(12, 'Ready to Fly: Set as default', { announceChange: true, key: 'tile-action:Ready to Fly' })]);
  assert.strictEqual(await s.spoken(), 'EFB page: State');
  const b = s.buttonByLabel('Ready to Fly');
  b.focus(); b.click();
  assert.strictEqual(await s.spoken(), 'Activating Ready to Fly: Set as default');
  assert.deepStrictEqual(s.posted, [JSON.stringify({ type: 'click', idx: '12' })]);

  s.render('State', [btn(12, 'Ready to Fly is the default', { announceChange: true, disabled: true, key: 'tile-action:Ready to Fly' })]);
  assert.strictEqual(s.buttonByLabel('Ready to Fly'), b, 'the keyed control was rebuilt instead of patched');
  assert.strictEqual(b.textContent, 'Ready to Fly is the default, dimmed', 'visible label did not update');
  assert.strictEqual(b.getAttribute('data-disabled'), 'true');
  assert.strictEqual(await s.spoken(), 'Ready to Fly is the default, dimmed', 'the outcome of the press went unspoken');

  b.click();
  assert.strictEqual(await s.spoken(), 'Unavailable');
  assert.deepStrictEqual(s.posted, [JSON.stringify({ type: 'click', idx: '12' })], 'the refused press posted a command');
});

test('the explicit key wins over the label: the same text under another key is another node', (t) => {
  const s = loadShell(t);
  s.render('Perf', [btn(5, 'Runway next', { key: 'step-next:Runway' })]);
  const first = s.buttonByLabel('Runway next');
  s.render('Perf', [btn(5, 'Runway next', { key: 'step-next:Arrival Runway' })]);
  assert.notStrictEqual(s.buttonByLabel('Runway next'), first, 'a different key was patched onto the old node');
});

// With the heuristics gone, an element WITHOUT a key is keyed by its whole label again (the flyPad's
// and the PMDG tablet's keys are exactly what they were before PR 189).
test('without a key, "(now …)" and an after-colon tail are part of the key again', (t) => {
  const s = loadShell(t);
  s.render('Perf', [btn(5, 'Runway next (now 06L)'), btn(6, 'GPU: Connect')]);
  const step = s.buttonByLabel('Runway next');
  const gpu = s.buttonByLabel('GPU');
  assert.strictEqual(step.getAttribute('data-key'), 'btn|Runway next (now 06L)#1');
  assert.strictEqual(gpu.getAttribute('data-key'), 'btn|GPU: Connect#1');
  s.render('Perf', [btn(5, 'Runway next (now 06R)'), btn(6, 'GPU: Disconnect')]);
  assert.notStrictEqual(s.buttonByLabel('Runway next'), step, 'an unkeyed "(now …)" change was patched in place');
  assert.notStrictEqual(s.buttonByLabel('GPU'), gpu, 'an unkeyed after-colon change was patched in place');
});
