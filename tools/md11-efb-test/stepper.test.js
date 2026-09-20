'use strict';
const { test } = require('node:test');
const assert = require('node:assert');
const { load, scrape, lines } = require('./run');

test('a stepper whose option list is readable is one dropdown with the full list', () => {
  const els = scrape('perf-landing');
  const sels = els.filter(e => e.controlType === 'select').map(e => [e.text, e.options, e.value]);
  assert.deepStrictEqual(sels.slice(0, 2), [
    ['Runway', ['RW06L', 'RW06R', 'RW07L', 'RW07R', 'RW24L', 'RW24R', 'RW25L', 'RW25R'], 'RW06L'],
    ['Runway Condition', ['DRY', 'WET', 'CONTAMINATED'], 'DRY']]);
  assert.ok(sels.some(s => s[0] === 'Autobrake' && s[2] === 'MIN' && s[1].join() === 'MIN,MED,MAX'));
  assert.ok(sels.some(s => s[0] === 'Reversers' && s[2] === 'ALL' && s[1].join() === 'ALL,WING,TAIL,NONE'));
  assert.ok(!els.some(e => /previous|next/.test(e.text)), 'no arrow buttons remain');
  assert.ok(!els.some(e => e.controlType === 'text' && e.text === 'Runway'), 'the locked field is not an edit box');
});

test('without a readable option list the stepper reads its value and keeps the two arrow buttons', () => {
  const ls = lines(scrape('perf-stepper-fallback', { autoVis: true, nav: 'Perf' })).slice(7);
  assert.deepStrictEqual(ls, ['static|Runway: RW06L', 'button|Runway previous (now RW06L)', 'button|Runway next (now RW06L)']);
});

function wire(input, opts, counts) {
  // The EFB applies a press on the NEXT tick (React 18 flushes after the event, live-verified
  // 2026-09-05: the DOM read inline after a press still showed the old value). Emulate that.
  let i = opts.indexOf(input.value);
  const [up, down] = Array.from(input.parentElement.getElementsByTagName('button'));
  // React re-renders both arrows' disabled state from the new index on every change too.
  down.addEventListener('mousedown', () => { counts.down++; queueMicrotask(() => { i = Math.min(i + 1, opts.length - 1); input.value = opts[i]; up.disabled = (i === 0); down.disabled = (i === opts.length - 1); }); });
  up.addEventListener('mousedown', () => { counts.up++; queueMicrotask(() => { i = Math.max(i - 1, 0); input.value = opts[i]; up.disabled = (i === 0); down.disabled = (i === opts.length - 1); }); });
}

test('picking a later value presses the down arrow until the field shows it, then back up', async () => {
  const { A, document } = load('perf-landing');
  A.STEP_DELAY_MS = 0;
  const sel = JSON.parse(A.scrape()).elements.find(e => e.controlType === 'select' && e.text === 'Autobrake');
  const input = document.querySelector('[data-md11-efb-idx="' + sel.idx + '"]');
  assert.equal(input.tagName, 'INPUT');
  const counts = { up: 0, down: 0 };
  wire(input, ['MIN', 'MED', 'MAX'], counts);
  const ok = await new Promise(res => assert.equal(A.setValue(String(sel.idx), 'MAX', res), true));
  assert.equal(ok, true);
  assert.deepStrictEqual(counts, { up: 0, down: 2 });
  assert.equal(input.value, 'MAX');
  const back = await new Promise(res => A.setValue(String(sel.idx), 'MIN', res));
  assert.equal(back, true);
  assert.deepStrictEqual(counts, { up: 2, down: 2 });
  assert.equal(input.value, 'MIN');
});

test('a press that does not move the field stops the walk instead of spinning', async () => {
  const { A, document } = load('perf-landing');
  A.STEP_DELAY_MS = 0;
  const sel = JSON.parse(A.scrape()).elements.find(e => e.controlType === 'select' && e.text === 'Reversers');
  const input = document.querySelector('[data-md11-efb-idx="' + sel.idx + '"]');
  let presses = 0;
  for (const b of input.parentElement.getElementsByTagName('button')) b.addEventListener('mousedown', () => presses++);
  const ok = await new Promise(res => A.setValue(String(sel.idx), 'NONE', res));
  assert.equal(ok, false);
  assert.equal(presses, 1);
});

test('an unknown value is refused before any press', () => {
  const { A, document } = load('perf-landing');
  const sel = JSON.parse(A.scrape()).elements.find(e => e.controlType === 'select' && e.text === 'Runway');
  const input = document.querySelector('[data-md11-efb-idx="' + sel.idx + '"]');
  let presses = 0;
  for (const b of input.parentElement.getElementsByTagName('button')) b.addEventListener('mousedown', () => presses++);
  assert.equal(A.setValue(String(sel.idx), 'RW99X'), false);
  assert.equal(presses, 0);
});

test('a stepper the EFB has not filled in yet reads (empty) and keeps its arrows', () => {
  const ls = lines(scrape('perf-stepper-empty', { autoVis: true, nav: 'Perf' })).slice(7);
  assert.deepStrictEqual(ls, ['static|Runway: (empty)', 'button|Runway previous', 'button|Runway next']);
});

// The Select mounts with an EMPTY options array before its list exists. Walking past that fiber to
// keep looking reaches an unrelated ANCESTOR's `options` prop and offers a dropdown of somebody
// else's choices — so the FIRST fiber carrying an options array wins, whatever it holds.
test('an empty option list falls back to the arrows, never an ancestor component\'s list', () => {
  const { A, document } = load('perf-stepper-empty', { autoVis: true, nav: 'Perf' });
  const inp = document.querySelector('input[disabled]');
  inp['__reactFiber$jsdom'] = {
    memoizedProps: {},
    return: {
      memoizedProps: {},
      return: {
        memoizedProps: { options: [] },                                       // this Select's own
        return: { memoizedProps: { options: [{ label: 'A' }, { label: 'B' }] } }  // an ancestor's
      }
    }
  };
  assert.equal(A.fiberOptions(inp), null);
  const els = JSON.parse(A.scrape()).elements;
  assert.ok(!els.some(e => e.controlType === 'select'), 'no dropdown is offered: ' + JSON.stringify(els.map(e => e.text)));
});

// The shared EFB shell speaks a control's post-press label change ONLY for an element the agent
// flagged announceChange: true. On the Perf page the two arrows are the sole source of that flag —
// the static value line beside them, the nav tabs and everything else on the page must not carry it.
test('only the two arrow buttons ask the shell to speak their post-press label', () => {
  const hinted = els => els.filter(e => e.announceChange === true).map(e => e.text);
  const fallback = scrape('perf-stepper-fallback', { autoVis: true, nav: 'Perf' });
  assert.deepStrictEqual(hinted(fallback), ['Runway previous (now RW06L)', 'Runway next (now RW06L)']);
  assert.ok(fallback.some(e => e.announceChange !== true), 'the page also carries unflagged elements');
  // A field the EFB has not filled in yet: the arrows gain their "(now …)" only after the first
  // press, which is exactly the change the pilot must hear.
  assert.deepStrictEqual(hinted(scrape('perf-stepper-empty', { autoVis: true, nav: 'Perf' })),
    ['Runway previous', 'Runway next']);
});

// The arrows' labels carry the current choice, so a step changes every one of them. The key is the
// field's name, so the shell patches each in place and never rebuilds it under the pilot's focus.
// (A harness edit is no React render, so the test forces the full scrape the dirty gate would run.)
test('the arrows and the value line keep their keys across a step', () => {
  const { A, document } = load('perf-stepper-fallback', { autoVis: true, nav: 'Perf' });
  const keyed = els => els.filter(e => e.key).map(e => [e.key, e.text]);
  assert.deepStrictEqual(keyed(JSON.parse(A.scrape()).elements), [
    ['step-value:Runway', 'Runway: RW06L'],
    ['step-prev:Runway', 'Runway previous (now RW06L)'],
    ['step-next:Runway', 'Runway next (now RW06L)']]);
  document.querySelector('input[disabled]').value = 'RW06R';
  A._dirty = true;
  assert.deepStrictEqual(keyed(JSON.parse(A.scrape()).elements), [
    ['step-value:Runway', 'Runway: RW06R'],
    ['step-prev:Runway', 'Runway previous (now RW06R)'],
    ['step-next:Runway', 'Runway next (now RW06R)']]);
});

test('a stepper not yet filled in keys its arrows the same way, before and after its first value', () => {
  const { A, document } = load('perf-stepper-empty', { autoVis: true, nav: 'Perf' });
  const arrows = els => els.filter(e => /^step-(prev|next):/.test(e.key || '')).map(e => [e.key, e.text]);
  assert.deepStrictEqual(arrows(JSON.parse(A.scrape()).elements),
    [['step-prev:Runway', 'Runway previous'], ['step-next:Runway', 'Runway next']]);
  document.querySelector('input[disabled]').value = 'RW07';
  A._dirty = true;
  assert.deepStrictEqual(arrows(JSON.parse(A.scrape()).elements),
    [['step-prev:Runway', 'Runway previous (now RW07)'], ['step-next:Runway', 'Runway next (now RW07)']]);
});
