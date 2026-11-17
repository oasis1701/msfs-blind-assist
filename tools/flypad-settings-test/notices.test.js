'use strict';
// Error and warning notices (A.noticeItems): FBW's react-toastify pop-ups, caught by the agent's
// MutationObserver however briefly they live, and the inline throttle-calibration errors.
const { test } = require('node:test');
const assert = require('node:assert');
const fs = require('fs');
const path = require('path');
const { loadFixture } = require('./run');

// The client re-installs over a LIVE agent after one slow scrape (an eval timeout reads as "agent
// gone"), exactly as CoherentEFBClient.EnsureConnected does on the same socket.
function reinstall(window) {
  window.eval(fs.readFileSync(path.join(__dirname, '..', '..', 'MSFSBlindAssist', 'Resources', 'coherent-flypad-agent.js'), 'utf8'));
  const A = window.__MSFSBA_FLYPAD;
  A.isVisible = function (el) { return !!(el && el.getAttribute && el.getAttribute('data-vis') === '1'); };
  return A;
}

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

// A control on the page (in the document, as a real one is: a detached one is no retry). Not
// data-vis, so the scrape leaves it out.
function control(window, text) {
  const b = window.document.createElement('button');
  b.textContent = text;
  window.document.getElementById('MSFS_REACT_MOUNT').appendChild(b);
  return b;
}

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
  const calc = control(window, 'Calculate');
  A.notePress(calc);
  await flashToast(window, 'error', 'Beyond Takeoff Weight Limit - Max 19943 lb');
  A.notePress(control(window, 'Clear'));
  assert.strictEqual(notices(A).length, 2, 'another control must not clear it');
  A.notePress(calc);
  assert.deepStrictEqual(notices(A), []);
});

test('a control with the same label elsewhere (Landing "Calculate") does not clear the Takeoff error', async () => {
  const { window, A } = loadFixture('a320-index');
  A.notePress(control(window, 'Calculate'));
  await flashToast(window, 'error', 'Beyond Takeoff Weight Limit - Max 19943 lb');
  A.notePress(control(window, 'Calculate'));
  assert.strictEqual(notices(A).length, 2);
});

test('a control FBW has unmounted is let go: it is no retry, and holds no detached node', async () => {
  const { window, A } = loadFixture('a320-index');
  const calc = control(window, 'Calculate');
  A.notePress(calc);
  await flashToast(window, 'error', 'Beyond Takeoff Weight Limit - Max 19943 lb');
  calc.remove();                                         // FBW re-renders the page without it
  assert.strictEqual(notices(A).length, 2);
  assert.strictEqual(A._notices[0].origin, null, 'the notice still holds the unmounted control');
  assert.strictEqual(A._lastPress, null, 'the last press still holds the unmounted control');
});

test('a toast react-toastify lets in from its queue as another closes has no origin: only Dismiss clears it', async () => {
  const { window, A } = loadFixture('a320-index');
  const host = window.document.querySelector('.Toastify');
  host.insertAdjacentHTML('beforeend', toastHtml('error', 'SimBrief: no OFP') + toastHtml('error', 'SimBrief: no route'));
  await new Promise((r) => setTimeout(r, 0));
  const ground = control(window, 'Ground');
  A.notePress(ground);                                   // pressed while the third toast waits
  host.querySelector('.Toastify__toast').remove();       // the first closes, the queued one mounts
  host.insertAdjacentHTML('beforeend', toastHtml('error', 'SimBrief: no aircraft'));
  await new Promise((r) => setTimeout(r, 0));
  A.notePress(ground);
  assert.ok(notices(A).some((e) => e.text === 'Error: SimBrief: no aircraft'), 'a press of the Ground tab cleared it');
});

test('an error raised by a typed value belongs to that field, not to the button pressed before it', async () => {
  const { window, A } = loadFixture('a320-index');
  const tab = window.document.createElement('button');
  tab.textContent = 'Takeoff';
  A.notePress(tab);
  const field = window.document.createElement('input');
  field.setAttribute('data-fbw-efb-idx', '9999');
  field.setAttribute('data-vis', '1');                    // visible, so the scrape stamps it
  field.setAttribute('data-rect', '600,100,300,640');
  window.document.getElementById('MSFS_REACT_MOUNT').appendChild(field);
  assert.strictEqual(A.setValue(9999, 'EGLL'), 'ok');
  await flashToast(window, 'error', 'OFP airport is invalid');
  A.notePress(tab);
  assert.strictEqual(notices(A).length, 2, 'the tab pressed earlier must not clear it');
  A.setValue(Number(field.getAttribute('data-fbw-efb-idx')), 'EGKK');   // each scrape re-stamps idx
  assert.deepStrictEqual(notices(A), [], 'typing into the field again is the retry');
});

test('a burst of passing toasts never pushes out an undismissed error', async () => {
  const { window, A } = loadFixture('a320-index');
  await flashToast(window, 'error', 'Could not fetch airport');
  for (let i = 0; i < A.NOTICE_MAX + 2; i++) await flashToast(window, 'info', 'Preset ' + i + ' loaded');
  assert.ok(notices(A).some((e) => e.text === 'Error: Could not fetch airport'));
});

test('over the cap with only errors listed, a new passing toast is the one dropped', async () => {
  const { window, A } = loadFixture('a320-index');
  for (let i = 0; i < A.NOTICE_MAX; i++) await flashToast(window, 'error', 'Error number ' + i);
  await flashToast(window, 'info', 'Loading preset');
  const alerts = notices(A).filter((e) => e.kind === 'alert').map((e) => e.text);
  assert.strictEqual(alerts.length, A.NOTICE_MAX);
  assert.ok(alerts.includes('Error: Error number 0'), 'the oldest error was pushed out by an info toast');
});

test('when FBW re-mounts its toast container, the observer follows it at the next scrape', async () => {
  const { window, A } = loadFixture('a320-index');
  const old = window.document.querySelector('.Toastify');
  const fresh = window.document.createElement('div');
  fresh.className = 'Toastify';
  old.replaceWith(fresh);                                // a flyPad power cycle
  notices(A);
  await flashToast(window, 'error', 'Could not fetch airport');
  assert.strictEqual(notices(A).filter((e) => e.kind === 'alert').length, 1);
});

test('a re-install counts on from the previous agent\'s memo generation', () => {
  const { window, A } = loadFixture('a320-index');
  for (let i = 0; i < 3; i++) A.scrape();
  const B = reinstall(window);
  assert.strictEqual(B._gen, A._gen);
});

test('a re-install over a live agent keeps its errors and its counter, and re-reads no open toast', async () => {
  const { window, A } = loadFixture('a320-index');
  await flashToast(window, 'error', 'Could not fetch airport');
  window.document.querySelector('.Toastify').insertAdjacentHTML('beforeend', toastHtml('error', 'OFP airport is invalid'));
  await new Promise((r) => setTimeout(r, 0));
  const before = notices(A).map((e) => e.key);
  const B = reinstall(window);
  assert.deepStrictEqual(notices(B).map((e) => e.key), before, 'the notices, keys unchanged, survive');
  window.document.querySelector('.Toastify').innerHTML = '';
  await flashToast(window, 'error', 'Could not find OFP runway!');
  const fresh = notices(B).find((e) => /runway/.test(e.text)).key;
  assert.ok(!before.includes(fresh), 'a new error must not reuse a key the form has already spoken');
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

// The overlap box exactly as FBW renders it (ThrottleConfig.tsx): the red strip holding the
// exclamation-circle icon, then a plain h2.
function addOverlap(window, text) {
  const note = [...window.document.querySelectorAll('h2')].find((h) => /Remember to configure/.test(h.textContent));
  note.parentElement.insertAdjacentHTML('afterend',
    '<div class="w-full overflow-hidden rounded-md border-2 border-theme-accent" data-vis="1" data-rect="830,128,1406,900">' +
    '<div class="flex w-full items-center justify-center bg-utility-red py-3" data-vis="1" data-rect="840,140,170,880"><svg></svg></div>' +
    '<h2 class="py-4 text-center" data-vis="1" data-rect="832,130,1404,898">' + text + '</h2></div>');
  return note.parentElement.nextElementSibling.querySelector('h2');
}

test('in four-axis mode the axis titles, h2s beside a pencil icon, are not errors', () => {
  const { window, A } = loadFixture('calibrate');
  // BaseThrottleConfig.tsx renders the title as an h2 when numberOfUserAxes === 4 (the A380's
  // default), with the expert-mode PencilSquare icon in the same panel.
  for (const h1 of [...window.document.querySelectorAll('h1')].filter((h) => /^Axis \d/.test(h.textContent))) {
    const h2 = window.document.createElement('h2');
    h2.className = 'mb-2 text-center';
    h2.textContent = h1.textContent;
    h2.setAttribute('data-vis', '1');
    h1.replaceWith(h2);
    h2.parentElement.insertAdjacentHTML('beforeend', '<div class="flex w-60 flex-row"><p>Current Value: 0.00</p><svg class="text-theme-highlight"></svg></div>');
  }
  assert.deepStrictEqual(notices(A), []);
});

test('the throttle overlap reads as an error; the harmless note in the same kind of box does not', () => {
  const { window, A } = loadFixture('calibrate');
  assert.deepStrictEqual(notices(A), [], 'the "Remember to configure MSFS" note is not an error');
  addOverlap(window, 'Axis 1: Idle (-0.05) overlaps with Climb (-0.25)');
  assert.deepStrictEqual(notices(A).map((e) => e.kind + '|' + e.text),
    ['alert|Error: Axis 1: Idle (-0.05) overlaps with Climb (-0.25)', 'button|Dismiss: Axis 1: Idle (-0.05) overlaps with Climb (-0.25)']);
});

test('the overlap is found in a translated flyPad too: the page is known by structure, not its h1', () => {
  const { window, A } = loadFixture('calibrate');
  for (const h1 of window.document.querySelectorAll('h1')) h1.textContent = 'Achse 1 für Schub 1 + 2';
  addOverlap(window, 'Achse 1: Leerlauf (-0.05) überschneidet sich mit Steigflug (-0.25)');
  assert.strictEqual(notices(A).filter((e) => e.kind === 'alert').length, 1);
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
