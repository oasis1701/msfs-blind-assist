'use strict';
// What the tablet puts OVER its pages: the control box (behind the header's gear), its panel-state
// confirmation, the powered-off screen and the pause dialog. Each covers the page for a sighted
// pilot, so while one is open the reader gives that alone, under its own page name; nothing behind
// it is read or pressed. The maintenance timer list is not modal and is read above the page.
const test = require('node:test');
const assert = require('node:assert');
const { load, scrape, find } = require('./run');

const texts = r => r.elements.map(e => e.text);

test('the control box is read on its own, with a way to close it', () => {
  const { A, window } = load('control-box');
  const r = scrape(A);
  assert.strictEqual(r.page, 'Control box');
  assert.deepStrictEqual(texts(r), [
    'Power Off EFB', 'Lock EFB',
    'TIME COMPRESSION', 'DISABLED', 'MAX 2x', 'MAX 4x', 'Time Compression: OFF',
    'Brightness',
    'Panel States', 'Cold and Dark', 'Ready for Takeoff', 'On APU', 'On GPU',
    'Close control box',
  ]);
  assert.strictEqual(find(r, 'Brightness').controlType, 'range');
  let closed = 0;
  window.document.getElementById('control-box-background').onclick = () => closed++;
  assert.ok(A.clickElement(find(r, 'Close control box').idx));
  assert.strictEqual(closed, 1);
});

test('the panel-state confirmation names the state it will set', () => {
  const { A, window } = load('control-box');
  const r = scrape(A);
  assert.ok(A.clickElement(find(r, 'Ready for Takeoff').idx));
  window.document.getElementById('confirm-box').removeAttribute('data-display');
  const c = scrape(A);
  assert.strictEqual(c.page, 'Control box, confirm panel state');
  assert.deepStrictEqual(texts(c), ['Confirm New Panel State', 'Panel state: Ready for Takeoff', 'Confirm', 'Cancel']);
});

test('the confirmation without a state the reader saw pressed still reads', () => {
  const r = scrape(load('confirm-box').A);
  assert.strictEqual(r.page, 'Control box, confirm panel state');
  assert.deepStrictEqual(texts(r), ['Confirm New Panel State', 'Confirm', 'Cancel']);
});

test('a powered-off tablet says so and offers to power on', () => {
  const { A, window } = load('powered-off');
  const r = scrape(A);
  assert.strictEqual(r.page, 'Powered off');
  assert.deepStrictEqual(texts(r), ['The tablet is powered off.', 'Power on']);
  let on = 0;
  window.document.getElementById('powered-off').onclick = () => on++;
  assert.ok(A.clickElement(find(r, 'Power on').idx));
  assert.strictEqual(on, 1);
});

test('the pause dialog is read with its resume button', () => {
  const r = scrape(load('paused').A);
  assert.strictEqual(r.page, 'Paused');
  assert.deepStrictEqual(texts(r), [
    'TOP OF DESCENT PAUSE',
    'Aircraft has reached top of descent. The simulation has been paused. Click below to resume.',
    'RESUME FLIGHT',
  ]);
  assert.strictEqual(find(r, 'RESUME FLIGHT').kind, 'button');
});

test('running maintenance is listed above the page, each with its own Finish Now', () => {
  const r = scrape(load('timers').A);
  assert.strictEqual(r.page, 'Home');
  const t = texts(r);
  assert.deepStrictEqual(t.slice(0, 5), [
    'Maintenance in progress', 'Replace Brakes', 'All eight brakes are being replaced.', 'Complete in 4:59', 'Finish Now: Replace Brakes',
  ]);
  assert.strictEqual(find(r, 'Finish Now: Replace Brakes').kind, 'button');
  assert.strictEqual(find(r, 'Maintenance in progress').kind, 'heading');
});

test('the header button that shows the timers says whether they show', () => {
  const { A, window } = load('timers');
  const r = scrape(A);
  const b = find(r, 'Maintenance timers: shown');
  assert.ok(b && b.kind === 'button' && b.announceChange === true, 'state-carrying button');
  window.document.getElementById('timerContainer').setAttribute('data-display', 'none');
  const after = scrape(A);
  assert.ok(find(after, 'Maintenance timers: hidden'));
  assert.strictEqual(find(after, 'Maintenance timers: hidden').key, b.key, 'one node across the change');
});
