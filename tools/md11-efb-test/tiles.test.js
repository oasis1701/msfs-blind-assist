'use strict';
const { test } = require('node:test');
const assert = require('node:assert');
const { load, scrape, lines } = require('./run');

// Live capture, MD-11F on the ground at UCFM, 2026-09-06: GPU and the wheel chocks were already
// connected/set when this capture was taken, so their tiles read the opposite action (Disconnect /
// Remove) from a cold-and-dark ramp start.
test('Services tiles read their name with the action', () => {
  const els = scrape('services-ground');
  const btns = els.filter(e => e.kind === 'button').map(e => e.text);
  assert.deepStrictEqual(btns, ['Passenger 1L: Closed', 'Passenger 1R: Closed', 'Cargo Main: Closed', 'Cargo 1R: Closed',
    'Cargo 2R: Closed', 'Bulk Cargo: Closed', 'Nose Weight: Set', 'GPU: Disconnect', 'ASU: Connect', 'Wheel Chocks: Remove']);
  assert.ok(!els.some(e => e.kind === 'static' && e.text === 'Passenger 1L'), 'the name is not read a second time');
  assert.ok(els.filter(e => e.kind === 'button').every(e => !e.disabled), 'on the ground nothing is dimmed');
});

// The default marker is a BUTTON (B5, review round 2 of PR 189). It takes the place of the
// "Set as default" button the pilot pressed, so it keeps that button's kind and key, and the shell
// patches it in place.
test('State tiles: the load button, then the default marker or the set-default button', () => {
  const ls = lines(scrape('state-ground')).slice(7);
  assert.deepStrictEqual(ls, ['button|Cold and Dark', 'button|Cold and Dark is the default',
    'button|Ready to Start', 'button|Ready to Start: Set as default',
    'button|Ready to Fly', 'button|Ready to Fly: Set as default',
    'button|Load Last Save', 'button|Load Last Save: Set as default']);
});

test('a tile button is stamped on the EFB button itself', () => {
  const { A, document } = load('services-ground');
  const gpu = JSON.parse(A.scrape()).elements.find(e => e.text === 'GPU: Disconnect');
  const node = document.querySelector('[data-md11-efb-idx="' + gpu.idx + '"]');
  assert.equal(node.tagName, 'BUTTON');
  assert.equal(node.textContent.trim(), 'Disconnect');
});

// The shared EFB shell speaks a control's post-press label change ONLY for an element the agent
// flagged announceChange: true. A tile's label carries its own state, so the flip the pilot's press
// produced ("Passenger 1L: Closed" → "Passenger 1L: Open") is the OUTCOME they asked for and nobody
// else reads it to them. Whole-page assertion: exactly the tiles ask for it here, nothing else on
// the Services page does (not the headings, not the read-outs, not the nav tabs).
test('exactly the Services tiles ask the shell to speak their post-press label', () => {
  const els = scrape('services-ground');
  assert.deepStrictEqual(els.filter(e => e.announceChange === true).map(e => e.text),
    ['Passenger 1L: Closed', 'Passenger 1R: Closed', 'Cargo Main: Closed', 'Cargo 1R: Closed',
      'Cargo 2R: Closed', 'Bulk Cargo: Closed', 'Nose Weight: Set', 'GPU: Disconnect', 'ASU: Connect', 'Wheel Chocks: Remove']);
  assert.ok(els.some(e => e.announceChange !== true), 'the page also carries unflagged elements');
});

// A state tile's ACTION button carries its state the same way ("Ready to Fly: Set as default"). So
// does the default marker that takes its place, because "Ready to Fly is the default" is the outcome
// the pilot's press asked for. The load button beside it ("Ready to Fly") never changes, so it asks
// for nothing.
test('a state tile flags its action button, default marker included, and never its load button', () => {
  const els = scrape('state-ground');
  assert.deepStrictEqual(els.filter(e => e.announceChange === true).map(e => e.text),
    ['Cold and Dark is the default', 'Ready to Start: Set as default', 'Ready to Fly: Set as default', 'Load Last Save: Set as default']);
});

// A harness edit is no React render, so the dirty gate never sees it: force the full scrape the
// live window's next poll would run.
const rescrape = (A) => { A._dirty = true; return JSON.parse(A.scrape()).elements; };

// The shell keys a node by the agent's key (B5). A reconcile needs the SAME key before and after a
// flip and a different one for everything else on the page; both halves are pinned on the real
// captures.
test('exactly the Services tiles carry a key, each named after its tile', () => {
  assert.deepStrictEqual(scrape('services-ground').filter(e => e.key).map(e => [e.key, e.text]), [
    ['tile:Passenger 1L', 'Passenger 1L: Closed'], ['tile:Passenger 1R', 'Passenger 1R: Closed'],
    ['tile:Cargo Main', 'Cargo Main: Closed'], ['tile:Cargo 1R', 'Cargo 1R: Closed'], ['tile:Cargo 2R', 'Cargo 2R: Closed'],
    ['tile:Bulk Cargo', 'Bulk Cargo: Closed'], ['tile:Nose Weight', 'Nose Weight: Set'], ['tile:GPU', 'GPU: Disconnect'],
    ['tile:ASU', 'ASU: Connect'], ['tile:Wheel Chocks', 'Wheel Chocks: Remove']]);
});

test('a Services tile keeps its key across the flip its press produces', () => {
  const { A, document } = load('services-ground');
  const gpu = JSON.parse(A.scrape()).elements.find(e => e.text === 'GPU: Disconnect');
  document.querySelector('[data-md11-efb-idx="' + gpu.idx + '"]').textContent = 'Connect';
  const after = rescrape(A).find(e => e.key === 'tile:GPU');
  assert.deepStrictEqual([after.kind, after.text], ['button', 'GPU: Connect']);
});

test('"Set as default": the new default keeps its button\'s kind and key, and the old one gets them back', () => {
  const { A, document } = load('state-ground');
  const before = JSON.parse(A.scrape()).elements;
  assert.deepStrictEqual(before.filter(e => /^tile-action:/.test(e.key || '')).map(e => [e.key, e.kind, e.text]), [
    ['tile-action:Cold and Dark', 'button', 'Cold and Dark is the default'],
    ['tile-action:Ready to Start', 'button', 'Ready to Start: Set as default'],
    ['tile-action:Ready to Fly', 'button', 'Ready to Fly: Set as default'],
    ['tile-action:Load Last Save', 'button', 'Load Last Save: Set as default']]);

  // The EFB makes Ready to Fly the default: its action button turns into the check mark, and Cold
  // and Dark's check mark turns back into "Set as default".
  const actionOf = name => [...document.querySelectorAll('p')].find(p => p.textContent.trim() === name).closest('button').nextElementSibling;
  const fly = actionOf('Ready to Fly');
  const cold = actionOf('Cold and Dark');
  const check = cold.innerHTML;
  cold.textContent = 'Set as default';
  fly.innerHTML = check;

  const after = rescrape(A);
  const f = after.find(e => e.key === 'tile-action:Ready to Fly');
  assert.deepStrictEqual([f.kind, f.clickable, f.text, f.announceChange], ['button', true, 'Ready to Fly is the default', true]);
  const c = after.find(e => e.key === 'tile-action:Cold and Dark');
  assert.deepStrictEqual([c.kind, c.text], ['button', 'Cold and Dark: Set as default']);
});

// Because nothing is stamped for it, a press reaches A.find(idx) -> null and clickElement returns
// false — which NOTHING surfaces. Reported enabled, the pilot heard the browser shell's "Activating
// Cold and Dark is the default" (or, in the native list fallback, nothing at all) over a control
// that did nothing and said nothing. Emitted DISABLED, both refusal paths answer "Unavailable": the
// shell's data-disabled branch in onActivate, and FbwEfbForm.AnnounceUnavailable in list mode. The
// key and text are unchanged, so the reconcile still holds the node the press landed on and
// announceChange still speaks the outcome.
test('"is the default" is emitted disabled, so both shells refuse it out loud', () => {
  const els = scrape('state-ground');
  const def = els.find(e => e.text === 'Cold and Dark is the default');
  assert.deepStrictEqual([def.kind, def.clickable, def.disabled, def.key, def.announceChange],
    ['button', true, true, 'tile-action:Cold and Dark', true]);
  // It is the marker that is unpressable, not the page: every other button on it stays live.
  assert.deepStrictEqual(els.filter(e => e.kind === 'button' && e.disabled).map(e => e.text),
    ['Cold and Dark is the default']);
});

// Nothing here knows what the EFB does with a click on its check mark, so the marker is not stamped
// on it: a press finds nothing to press, and says so.
test('a press on "is the default" is refused and reaches no EFB button', () => {
  const { A, document } = load('state-ground');
  const def = JSON.parse(A.scrape()).elements.find(e => e.text === 'Cold and Dark is the default');
  const check = [...document.querySelectorAll('svg')].find(s => /lucide-check/.test(s.getAttribute('class') || '')).closest('button');
  let clicks = 0;
  check.addEventListener('click', () => clicks++);
  assert.equal(document.querySelectorAll('[data-md11-efb-idx="' + def.idx + '"]').length, 0, 'no element carries its idx');
  assert.equal(A.clickElement(def.idx), false);
  assert.equal(clicks, 0);
});
