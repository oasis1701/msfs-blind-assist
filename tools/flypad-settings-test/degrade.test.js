// Regression for the ownership-gate fix: buildSettingsLines must OWN the settings
// region only when it actually has a recognizable control. Otherwise it returns
// null so the generic pass still surfaces the content (graceful degrade) — instead
// of owning the region and re-emitting it blank (the A320 / future-layout risk).
const { test } = require('node:test');
const assert = require('node:assert');
const { JSDOM } = require('jsdom');
const fs = require('node:fs');
const path = require('node:path');
const AGENT = path.join(__dirname, '..', '..', 'MSFSBlindAssist', 'Resources', 'coherent-flypad-agent.js');

function mk(html) {
  const dom = new JSDOM('<!DOCTYPE html><body><div id="MSFS_REACT_MOUNT">' + html + '</div></body>', { runScripts: 'outside-only' });
  const w = dom.window;
  w.Element.prototype.getBoundingClientRect = function () {
    const r = (this.getAttribute && this.getAttribute('data-rect')) || '0,0,0,0';
    const p = r.split(',').map(Number);
    const top = p[0] || 0, left = p[1] || 0, right = p[2] || 0, bottom = p[3] || 0;
    return { top, left, right, bottom, width: right - left, height: bottom - top, x: left, y: top };
  };
  w.SimVar = { GetSimVarValue() { return 0; }, SetSimVarValue() {} };
  w.eval(fs.readFileSync(AGENT, 'utf8'));
  const A = w.__MSFSBA_FLYPAD;
  A.isVisible = (el) => !!(el && el.getAttribute && el.getAttribute('data-vis') === '1');
  return { w, A };
}

test('graceful degrade: unrecognized row layout returns null (not blank)', () => {
  // A controls page whose rows are grid-cols (not the flex-row label+control shape).
  const { w, A } = mk(
    '<a href="/settings" data-vis="1" data-rect="50,200,200,80">Settings - X</a>' +
    '<div class="divide-y-2" data-vis="1" data-rect="100,150,1200,800">' +
    '  <div class="grid grid-cols-2" data-vis="1" data-rect="120,150,1200,160">' +
    '    <span data-vis="1" data-rect="120,150,400,160">Some Setting</span>' +
    '    <input value="42" data-vis="1" data-rect="120,1100,1200,160">' +
    '  </div>' +
    '</div>');
  const res = A.buildSettingsLines(w.document.getElementById('MSFS_REACT_MOUNT'), { v: 1 });
  assert.strictEqual(res, null, 'should bail so the generic pass surfaces the input');
});

test('owns + emits when a recognizable control is present', () => {
  const { w, A } = mk(
    '<a href="/settings" data-vis="1" data-rect="50,200,200,80">Settings - X</a>' +
    '<div class="divide-y-2" data-vis="1" data-rect="100,150,1200,800">' +
    '  <div class="flex flex-row" data-vis="1" data-rect="120,150,1200,160">' +
    '    <span data-vis="1" data-rect="120,150,400,160">Real Setting</span>' +
    '    <div class="undefined" data-vis="1" data-rect="120,1100,1200,160"><input value="7" data-vis="1" data-rect="120,1100,1200,160"></div>' +
    '  </div>' +
    '</div>');
  const res = A.buildSettingsLines(w.document.getElementById('MSFS_REACT_MOUNT'), { v: 1 });
  assert.ok(res && res.some(e => e.text === 'Real Setting' && e.value === '7'), 'should emit the labeled input');
});
