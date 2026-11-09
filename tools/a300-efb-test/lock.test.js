'use strict';
// The A300 tablet's lock screen and header. The tablet wires its controls with element.onclick, and
// two of them hang the handler on the picture INSIDE the control rather than on the control: the
// lock screen's Unlock (on img.home-button) and the header's gear (on #control-box-button-icon).
// A press is dispatched where a finger would land, so the handler inside the control runs.
//
// jsdom has no layout, so the harness answers document.elementFromPoint from the fixtures' own
// data-rect boxes (run.js) unless a test loads with noHitTest. Both variants are run: the Chromium
// view has the call, and the agent keeps a search for the handler inside the control for a view
// that lacks it.
const test = require('node:test');
const assert = require('node:assert');
const { load, scrape, find } = require('./run');

for (const hitTest of [true, false]) {
  test('Unlock runs the handler on the picture inside the button' + (hitTest ? '' : ', with no hit testing'), () => {
    const { A, window } = load('lock', { noHitTest: !hitTest });
    // The two variants must really differ: only the first can be asked where a finger would land.
    assert.strictEqual(typeof window.document.elementFromPoint, hitTest ? 'function' : 'undefined');
    const r = scrape(A);
    assert.strictEqual(r.page, 'Locked');
    let unlocked = 0, clicks = 0;
    window.document.querySelector('.unlock-button img.home-button').onclick = () => unlocked++;
    window.document.querySelector('.unlock-button').addEventListener('click', () => clicks++);
    assert.ok(A.clickElement(find(r, 'Unlock').idx));
    assert.strictEqual(unlocked, 1, 'the tablet unlocks');
    assert.strictEqual(clicks, 1, 'one click event');
  });
}

for (const hitTest of [true, false]) {
  test('with no handler to find, a press lands ' + (hitTest ? 'on the picture under the center of the button' : 'on the button itself, with no hit testing'), () => {
    const { A, window } = load('lock', { noHitTest: !hitTest });
    const button = window.document.querySelector('.unlock-button');
    const picture = button.querySelector('img.home-button');
    const landed = [];
    button.addEventListener('click', e => landed.push(e.target));
    assert.ok(A.clickElement(find(scrape(A), 'Unlock').idx));
    assert.strictEqual(landed.length, 1, 'one click event');
    assert.strictEqual(landed[0], hitTest ? picture : button);
  });
}

test('a press still reaches the handler inside a button that something else covers', () => {
  const { A, window } = load('lock');
  const r = scrape(A);
  let unlocked = 0;
  window.document.querySelector('.unlock-button img.home-button').onclick = () => unlocked++;
  // A sheet over the whole view, after the page: it is what a finger would land on.
  const sheet = window.document.createElement('div');
  sheet.setAttribute('data-rect', '0,0,2048,1536');
  window.document.body.appendChild(sheet);
  assert.strictEqual(window.document.elementFromPoint(1023, 778), sheet, 'the sheet covers the button');
  assert.ok(A.clickElement(find(r, 'Unlock').idx));
  assert.strictEqual(unlocked, 1, 'the press went to the handler inside the button, not to the sheet');
});

test('the harness finds the deepest displayed element at a point', () => {
  const { window } = load('lock');
  const doc = window.document;
  const button = doc.querySelector('.unlock-button');
  const row = button.querySelector('.menu-row-item');
  const picture = button.querySelector('img.home-button');
  // The picture sits in the middle of the row, which fills the button: the last of the nested boxes.
  assert.strictEqual(doc.elementFromPoint(1023, 778), picture, 'the picture');
  assert.strictEqual(doc.elementFromPoint(200, 700), row, 'the row, beside the picture');
  assert.strictEqual(doc.elementFromPoint(1995, 50), doc.getElementById('control-box-button-icon'), 'the gear\'s picture');
  picture.setAttribute('data-display', 'none');
  assert.strictEqual(doc.elementFromPoint(1023, 778), row, 'a hidden element is not hit');
  button.setAttribute('data-display', 'none');
  assert.strictEqual(doc.elementFromPoint(1023, 778), doc.body, 'nor is anything under a hidden one');
  assert.strictEqual(doc.elementFromPoint(-1, 10), null, 'left of the view');
  assert.strictEqual(doc.elementFromPoint(2048, 10), null, 'right of the view');
  assert.strictEqual(doc.elementFromPoint(10, -1), null, 'above the view');
});
