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

// ---- opt-in Enter-only edit box (item key commit = 'enter') ----
const fld = (idx, text, value, extra) => Object.assign({ idx, kind: 'field', controlType: 'text', text, value, clickable: false, level: 0, live: '', disabled: false }, extra || {});
const keydown = (win, el, k) => el.dispatchEvent(new win.KeyboardEvent('keydown', { key: k, bubbles: true, cancelable: true }));
const sets = (s) => s.posted.map((m) => JSON.parse(m)).filter((m) => m.type === 'set');
// The pilot typing: the box's text changes and the browser fires 'input', as a keystroke does (the Enter-only box counts only
// text typed since the shell last wrote it, so a test that sets the value without the event is no typing).
const typeIn = (win, box, v) => { box.value = v; box.dispatchEvent(new win.Event('input', { bubbles: true })); };

test('An_enter_only_box_posts_on_enter_only', (t) => {
  const s = loadShell(t);
  s.render('Fuel', [fld(3, 'Zero fuel weight', '100', { commit: 'enter' })]);
  const box = s.window.document.querySelector('input.in');
  typeIn(s.window, box, '250');
  box.dispatchEvent(new s.window.Event('change', { bubbles: true }));
  assert.strictEqual(sets(s).length, 0, 'change must not post');
  keydown(s.window, box, 'Enter');
  assert.deepStrictEqual(sets(s), [{ type: 'set', idx: '3', value: '250', controlType: 'text' }]);
});

test('Leaving_an_enter_only_box_restores_it', async (t) => {
  const s = loadShell(t);
  s.render('Fuel', [fld(3, 'Zero fuel weight', '100', { commit: 'enter' })]);
  const box = s.window.document.querySelector('input.in');
  typeIn(s.window, box, '250');
  box.dispatchEvent(new s.window.Event('blur'));
  assert.strictEqual(box.value, '100');
  assert.strictEqual(sets(s).length, 0);
  assert.strictEqual(await s.spoken(), 'Zero fuel weight not changed');
});

test('A_box_without_commit_posts_on_change_as_before', (t) => {
  const s = loadShell(t);
  s.render('Fuel', [fld(3, 'Zero fuel weight', '100')]);
  const box = s.window.document.querySelector('input.in');
  box.value = '250';
  box.dispatchEvent(new s.window.Event('change', { bubbles: true }));
  assert.deepStrictEqual(sets(s), [{ type: 'set', idx: '3', value: '250', controlType: 'text' }]);
  box.value = '260';
  box.dispatchEvent(new s.window.Event('blur'));
  assert.strictEqual(box.value, '260', 'no restore without commit');
});

// Left after Enter, before the read-back came: the box shows what the tablet shows (D2), its text before the typing, and says
// nothing (the set is on its way); the read-back's value then shows in it.
test('An_enter_only_box_left_before_the_read_back_shows_the_tablets_text_until_it_arrives', async (t) => {
  const s = loadShell(t);
  s.render('Fuel', [fld(3, 'Zero fuel weight', '100', { commit: 'enter' })]);
  const box = s.window.document.querySelector('input.in');
  typeIn(s.window, box, '250');
  keydown(s.window, box, 'Enter');
  assert.strictEqual(sets(s).length, 1);
  box.dispatchEvent(new s.window.Event('blur'));
  assert.strictEqual(box.value, '100');
  assert.strictEqual(await s.spoken(), 'EFB page: Fuel', 'only the page announce, nothing about the box');
  s.render('Fuel', [fld(3, 'Zero fuel weight', '250', { commit: 'enter' })]);
  assert.strictEqual(box.value, '250');
});

// The final review's I1. The read-back arrives while the pilot is still in the box (the normal case after Enter), in the tablet's
// own text: the Airport box upper-cases "kpdx". The box keeps the pilot's text while they are in it, and leaving it shows the
// tablet's text with nothing said: the runner said "Airport: KPDX", and "Airport not changed" after it would be wrong. A second
// Enter on the text the pilot typed types it again (the re-review's N1): the shell cannot tell a read-back from the push of a
// failed typing, and after a failure Enter is the pilot's retry.
test('An_enter_only_box_left_after_the_tablet_changed_the_text_shows_it_and_says_nothing', async (t) => {
  const s = loadShell(t);
  s.render('PERF calc', [fld(3, 'Airport', 'KSEA', { commit: 'enter' })]);
  const box = s.window.document.querySelector('input.in');
  box.focus();
  box.value = 'kpdx';
  box.dispatchEvent(new s.window.Event('input', { bubbles: true }));
  keydown(s.window, box, 'Enter');
  s.render('PERF calc', [fld(3, 'Airport', 'KPDX', { commit: 'enter' })]);
  assert.strictEqual(s.window.document.activeElement, box, 'the re-render kept the focus');
  assert.strictEqual(box.value, 'kpdx', 'the box the pilot is in is never written');
  keydown(s.window, box, 'Enter');
  assert.deepStrictEqual(sets(s).map((m) => m.value), ['kpdx', 'kpdx'], 'a second Enter on the typed text types it again');
  box.blur();
  assert.strictEqual(box.value, 'KPDX');
  assert.strictEqual(await s.spoken(), 'EFB page: PERF calc', 'only the page announce, nothing about the box');
});

// The tablet took none of it (Airport drops digits, so "k1" leaves KSEA): the window's next read shows the same value, so no new value
// arrives at all. Leaving the box shows the tablet's text again, and says nothing (the runner said what the tablet reads).
test('An_enter_only_box_whose_text_the_tablet_rejected_shows_the_tablets_text_on_leaving', async (t) => {
  const s = loadShell(t);
  s.render('PERF calc', [fld(3, 'Airport', 'KSEA', { commit: 'enter' })]);
  const box = s.window.document.querySelector('input.in');
  box.focus();
  box.value = 'k1';
  box.dispatchEvent(new s.window.Event('input', { bubbles: true }));
  keydown(s.window, box, 'Enter');
  s.render('PERF calc', [fld(3, 'Airport', 'KSEA', { commit: 'enter' })]);
  box.blur();
  assert.strictEqual(box.value, 'KSEA');
  assert.strictEqual(await s.spoken(), 'EFB page: PERF calc', 'only the page announce, nothing about the box');
  assert.strictEqual(sets(s).length, 1);
});

// A long text goes in several runs, and the window may read the box between them: a partial value arrives, then the final one (the
// scrub's made-up key, 44 characters: two runs). In the box throughout, the pilot leaves it showing the final value with nothing
// said; left between the two, it shows the partial one, silently, and follows the final one when it arrives.
const acarsKey = (v) => [fld(4, 'Sayintentions ACARS key', v, { commit: 'enter' })];
const longKey = 'MADEUPMADEUPMADEUPMADEUPMADEUPMADEUPMADEUPMA', partKey = longKey.slice(0, 40);

function enterLongKey(s) {
  s.render('AIRAC data', acarsKey(''));
  const box = s.window.document.querySelector('input.in');
  box.focus();
  box.value = longKey;
  box.dispatchEvent(new s.window.Event('input', { bubbles: true }));
  keydown(s.window, box, 'Enter');
  return box;
}

test('An_enter_only_box_takes_a_partial_value_then_the_final_one_while_the_pilot_is_in_it', async (t) => {
  const s = loadShell(t);
  const box = enterLongKey(s);
  s.render('AIRAC data', acarsKey(partKey));
  s.render('AIRAC data', acarsKey(longKey));
  box.blur();
  assert.strictEqual(box.value, longKey);
  assert.strictEqual(sets(s).length, 1);
  assert.strictEqual(await s.spoken(), 'EFB page: AIRAC data', 'only the page announce, nothing about the box');
});

test('An_enter_only_box_left_between_a_partial_value_and_the_final_one_follows_the_final_one', async (t) => {
  const s = loadShell(t);
  const box = enterLongKey(s);
  s.render('AIRAC data', acarsKey(partKey));
  box.blur();
  assert.strictEqual(box.value, partKey);
  s.render('AIRAC data', acarsKey(longKey));
  assert.strictEqual(box.value, longKey);
  assert.strictEqual(await s.spoken(), 'EFB page: AIRAC data', 'only the page announce, nothing about the box');
});

// Typing again after the read-back is a new edit: leaving the box without Enter then puts the tablet's text back and says so, as
// any edit left without Enter does.
test('Typing_again_after_the_read_back_is_a_new_edit_that_leaving_does_not_type', async (t) => {
  const s = loadShell(t);
  s.render('PERF calc', [fld(3, 'Airport', 'KSEA', { commit: 'enter' })]);
  const box = s.window.document.querySelector('input.in');
  box.focus();
  box.value = 'kpdx';
  box.dispatchEvent(new s.window.Event('input', { bubbles: true }));
  keydown(s.window, box, 'Enter');
  s.render('PERF calc', [fld(3, 'Airport', 'KPDX', { commit: 'enter' })]);
  box.value = 'KPDXY';
  box.dispatchEvent(new s.window.Event('input', { bubbles: true }));
  box.blur();
  assert.strictEqual(box.value, 'KPDX');
  assert.strictEqual(sets(s).length, 1);
  assert.strictEqual(await s.spoken(), 'Airport not changed');
});

// The re-review's N1. A typing that failed ("the tablet's keyboard did not come up", "not typed") leaves the item's value as it was,
// and the pilot's natural retry is Enter again on the text they typed: it types it again.
test('After_a_failed_typing_a_second_Enter_types_it_again', (t) => {
  const s = loadShell(t);
  s.render('PERF calc', [fld(3, 'Airport', 'KSEA', { commit: 'enter' })]);
  const box = s.window.document.querySelector('input.in');
  box.focus();
  typeIn(s.window, box, 'KPDX');
  keydown(s.window, box, 'Enter');
  s.render('PERF calc', [fld(3, 'Airport', 'KSEA', { commit: 'enter' })]);   // the failure's push: the value unchanged
  keydown(s.window, box, 'Enter');
  assert.deepStrictEqual(sets(s).map((m) => m.value), ['KPDX', 'KPDX']);
});

// The pilot sits in a box they have not typed in while BlueBird changes its value (PERF calc fills the runway length from the
// airport): the box shows the tablet's new value, Enter posts nothing (it would type the stale one back), and leaving says nothing.
test('An_untouched_box_the_pilot_is_in_follows_the_tablet_and_Enter_posts_nothing', async (t) => {
  const s = loadShell(t);
  s.render('PERF calc', [fld(4, 'Length (FT)', '3500', { commit: 'enter' })]);
  const box = s.window.document.querySelector('input.in');
  box.focus();
  s.render('PERF calc', [fld(4, 'Length (FT)', '4000', { commit: 'enter' })]);
  assert.strictEqual(box.value, '4000');
  keydown(s.window, box, 'Enter');
  assert.strictEqual(sets(s).length, 0);
  box.blur();
  assert.strictEqual(box.value, '4000');
  assert.strictEqual(await s.spoken(), 'EFB page: PERF calc', 'only the page announce, nothing about the box');
});

// Enter, then left before the read-back (the box shows the tablet's old text), then back in when the read-back arrives: the box shows
// the read-back, since nothing was typed since the shell wrote it, and Enter posts nothing (it would type the old text back over it).
test('Back_in_the_box_left_before_the_read_back_it_shows_the_read_back_and_Enter_posts_nothing', (t) => {
  const s = loadShell(t);
  s.render('PERF calc', [fld(3, 'Airport', 'KSEA', { commit: 'enter' })]);
  const box = s.window.document.querySelector('input.in');
  box.focus();
  typeIn(s.window, box, 'kpdx');
  keydown(s.window, box, 'Enter');
  box.blur();
  assert.strictEqual(box.value, 'KSEA');
  box.focus();
  s.render('PERF calc', [fld(3, 'Airport', 'KPDX', { commit: 'enter' })]);
  assert.strictEqual(box.value, 'KPDX');
  keydown(s.window, box, 'Enter');
  assert.deepStrictEqual(sets(s).map((m) => m.value), ['kpdx']);
});

// The final review's M6: nothing on an Enter-only box said that only Enter types it. It carries that as its accessible description
// (aria-description, which NVDA says after the box's name, role and value when it gets the focus); a box with no commit key, every
// other EFB's, carries none.
test('An_enter_only_box_says_that_Enter_types_it_and_another_box_says_nothing_more', (t) => {
  const s = loadShell(t);
  s.render('PERF calc', [fld(3, 'Airport', 'KSEA', { commit: 'enter' }), fld(4, 'Zero fuel weight', '100')]);
  const [enterOnly, other] = s.window.document.querySelectorAll('input.in');
  assert.strictEqual(enterOnly.getAttribute('aria-description'), 'Press Enter to type it on the tablet.');
  assert.strictEqual(enterOnly.getAttribute('aria-label'), 'Airport', 'the name stays the label alone');
  assert.strictEqual(other.getAttribute('aria-description'), null);
  assert.strictEqual(other.getAttribute('aria-describedby'), null);
});

test('Enter_with_the_text_unchanged_posts_and_says_nothing', async (t) => {
  const s = loadShell(t);
  s.render('Fuel', [fld(3, 'Zero fuel weight', '100', { commit: 'enter' })]);
  const box = s.window.document.querySelector('input.in');
  keydown(s.window, box, 'Enter');
  assert.strictEqual(sets(s).length, 0);
  assert.strictEqual(await s.spoken(), 'EFB page: Fuel', 'only the page announce, nothing about the box');
});
