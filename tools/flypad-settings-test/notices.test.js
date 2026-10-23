'use strict';
// Error and warning notices (A.noticeItems): FBW's react-toastify pop-ups, caught by the agent's
// MutationObserver however briefly they live, and the inline throttle-calibration errors.
const { test } = require('node:test');
const assert = require('node:assert');
const { loadFixture } = require('./run');

// A toast exactly as FBW renders it (captured live from the A320 Performance page, trimmed).
function toastHtml(type, text) {
  return '<div class="Toastify__toast Toastify__toast-theme--light Toastify__toast--' + type + '" data-vis="1" data-rect="10,500,900,60">' +
    '<div role="alert" class="Toastify__toast-body" data-vis="1" data-rect="10,500,880,60"><div data-vis="1" data-rect="10,520,880,60">' + text + '</div></div>' +
    '<button class="Toastify__close-button" type="button" aria-label="close" data-vis="1" data-rect="10,880,30,890"><svg></svg></button></div>';
}

// FBW adds the toast, then removes it before any poll runs.
async function flashToast(window, type, text) {
  const host = window.document.querySelector('.Toastify');
  host.insertAdjacentHTML('beforeend', toastHtml(type, text));
  await new Promise((r) => setTimeout(r, 0));   // let the MutationObserver deliver
  host.innerHTML = '';
}

const notices = (A) => JSON.parse(A.scrape()).elements.filter((e) => e.key);

test('an error toast removed before any poll still reads as an error with a Dismiss button', async () => {
  const { window, A } = loadFixture('a320-index');
  await flashToast(window, 'error', 'Beyond Takeoff Weight Limit - Max 19943 lb');
  const n = notices(A);
  assert.deepStrictEqual(n.map((e) => e.kind + '|' + e.text), ['alert|Error: Beyond Takeoff Weight Limit - Max 19943 lb', 'button|Dismiss: Beyond Takeoff Weight Limit - Max 19943 lb']);
  assert.strictEqual(n[0].live, 'assertive');
  assert.ok(n[1].idx >= A.NOTICE_IDX_BASE, 'the Dismiss button idx is in the reserved range');
  assert.ok(n[1].clickable);
});

test('a toast still on screen is read once, without FBW\'s own text and icon-only close button', async () => {
  const { window, A } = loadFixture('a320-index');
  window.document.querySelector('.Toastify').insertAdjacentHTML('beforeend', toastHtml('error', 'OFP airport is invalid'));
  await new Promise((r) => setTimeout(r, 0));
  const els = JSON.parse(A.scrape()).elements, texts = els.map((e) => e.text);
  assert.deepStrictEqual(els.filter((e) => /OFP airport is invalid/.test(e.text)).map((e) => e.kind + '|' + e.text),
    ['alert|Error: OFP airport is invalid', 'button|Dismiss: OFP airport is invalid']);
  assert.ok(!texts.some((t) => /^close$/i.test(t)), 'FBW\'s close button leaked into the scrape');
});

test('Dismiss removes the error; nothing else does', async () => {
  const { window, A } = loadFixture('a320-index');
  await flashToast(window, 'error', 'Could not fetch airport');
  for (let i = 0; i < 3; i++) assert.strictEqual(notices(A).length, 2, 'the error must stay across polls');
  const dismiss = notices(A).find((e) => e.kind === 'button');
  assert.strictEqual(A.clickElement(dismiss.idx), 'ok');
  assert.deepStrictEqual(notices(A), []);
});

test('pressing the control that raised the error again clears it (a retry)', async () => {
  const { window, A } = loadFixture('a320-index');
  const calc = window.document.createElement('button');
  calc.textContent = 'Calculate';
  A.notePress(calc);
  await flashToast(window, 'error', 'Beyond Takeoff Weight Limit - Max 19943 lb');
  const other = window.document.createElement('button');
  other.textContent = 'Clear';
  A.notePress(other);
  assert.strictEqual(notices(A).length, 2, 'another control must not clear it');
  A.notePress(calc);
  assert.deepStrictEqual(notices(A), []);
});

test('the same error raised again is a new notice under a new key, so it is spoken again', async () => {
  const { window, A } = loadFixture('a320-index');
  await flashToast(window, 'error', 'Could not find OFP runway!');
  const first = notices(A)[0].key;
  await flashToast(window, 'error', 'Could not find OFP runway!');
  const n = notices(A);
  assert.strictEqual(n.filter((e) => e.kind === 'alert').length, 1, 'a repeat replaces the earlier copy');
  assert.notStrictEqual(n[0].key, first);
});

test('a warning is read for a few seconds without a Dismiss button, then drops out', async () => {
  const { window, A } = loadFixture('a320-index');
  await flashToast(window, 'warning', 'Aircraft needs to be powered to load presets');
  assert.deepStrictEqual(notices(A).map((e) => e.kind + '|' + e.text), ['alert|Warning: Aircraft needs to be powered to load presets']);
  A._notices[0].at -= A.NOTICE_FLASH_MS + 1;
  assert.deepStrictEqual(notices(A), []);
});

test('info and success toasts read without a prefix', async () => {
  const { window, A } = loadFixture('a320-index');
  await flashToast(window, 'info', 'Leaving the page will stop the pushback');
  assert.deepStrictEqual(notices(A).map((e) => e.text), ['Leaving the page will stop the pushback']);
});

// The overlap box exactly as FBW renders it: the exclamation-circle icon beside a plain h2.
function addOverlap(window, text) {
  const note = [...window.document.querySelectorAll('h2')].find((h) => /Remember to configure/.test(h.textContent));
  note.parentElement.insertAdjacentHTML('afterend',
    '<div class="w-full overflow-hidden rounded-md border-2 border-theme-accent" data-vis="1" data-rect="830,128,1406,900">' +
    '<div data-vis="1" data-rect="840,140,170,880"><svg></svg></div>' +
    '<h2 class="py-4 text-center" data-vis="1" data-rect="832,130,1404,898">' + text + '</h2></div>');
  return note.parentElement.nextElementSibling.querySelector('h2');
}

test('the throttle overlap reads as an error; the harmless note in the same kind of box does not', () => {
  const { window, A } = loadFixture('calibrate');
  assert.deepStrictEqual(notices(A), [], 'the "Remember to configure MSFS" note is not an error');
  addOverlap(window, 'Axis 1: Idle (-0.05) overlaps with Climb (-0.25)');
  assert.deepStrictEqual(notices(A).map((e) => e.kind + '|' + e.text),
    ['alert|Error: Axis 1: Idle (-0.05) overlaps with Climb (-0.25)', 'button|Dismiss: Axis 1: Idle (-0.05) overlaps with Climb (-0.25)']);
});

test('fixing the overlap removes the error by itself; the next overlap then appears as a new one', () => {
  const { window, A } = loadFixture('calibrate');
  const h = addOverlap(window, 'Axis 1: Idle (-0.55) overlaps with Climb (-0.60)');
  assert.strictEqual(notices(A).length, 2);
  h.textContent = 'Axis 3: Idle (-0.55) overlaps with Climb (-0.60)';   // FBW shows only the first
  assert.deepStrictEqual(notices(A).filter((e) => e.kind === 'alert').map((e) => e.key), ['inline:Axis 3: Idle (-0.55) overlaps with Climb (-0.60)']);
  h.parentElement.remove();
  assert.deepStrictEqual(notices(A), []);
});

test('a dismissed inline error stays hidden while FBW shows it, and returns once it has gone and come back', () => {
  const { window, A } = loadFixture('calibrate');
  const text = 'Axis 1: Idle (-0.05) overlaps with Climb (-0.25)';
  let h = addOverlap(window, text);
  A.clickElement(notices(A).find((e) => e.kind === 'button').idx);
  assert.deepStrictEqual(notices(A), []);
  assert.ok(JSON.parse(A.scrape()).elements.some((e) => e.text === text), 'the page text itself stays readable');
  h.parentElement.remove();
  notices(A);
  addOverlap(window, text);
  assert.strictEqual(notices(A).length, 2);
});

test('the deadzone warning is an error only while FBW shows it', () => {
  const { window, A } = loadFixture('calibrate');
  assert.deepStrictEqual(notices(A), [], 'the hidden deadzone headings must not read');
  const dz = window.document.querySelector('h2.text-utility-red');
  dz.setAttribute('data-vis', '1');
  assert.deepStrictEqual(notices(A).map((e) => e.text), ['Error: Please enter a valid deadzone (> 0.01)', 'Dismiss: Please enter a valid deadzone (> 0.01)']);
});
