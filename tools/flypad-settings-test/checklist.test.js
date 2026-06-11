// Checklist item state — valueOf("checkitem", …) uses text-utility-green.
// Regression for the auto-fill misread: the old "svg in .border-4" test
// returned "true" for every auto-sensed condition item because FBW always
// renders a Link45deg svg inside the checkbox box (ChecklistItemComponent.tsx:113-121),
// whether or not the item is actually complete.
const { test } = require('node:test');
const assert = require('node:assert');
const { JSDOM } = require('jsdom');
const fs = require('node:fs');
const path = require('node:path');
const AGENT = path.join(__dirname, '..', '..', 'MSFSBlindAssist', 'Resources', 'coherent-flypad-agent.js');

// Build a minimal checklist item row.
// The row has the shape classify() recognises (flex-row + space-x-4 + .border-4 box).
// `withGreen`  — adds text-utility-green to the ROW class (item complete)
// `withSvg`    — puts an <svg> inside the .border-4 box (the Link45deg auto-fill icon)
function makeRow(withGreen, withSvg) {
  var rowClass = 'flex flex-row items-center space-x-4' + (withGreen ? ' text-utility-green' : '');
  var box = '<div class="border-4">' + (withSvg ? '<svg><path/></svg>' : '') + '</div>';
  return '<div class="' + rowClass + '">' + box + '<span>Park Brakes</span></div>';
}

function loadAgent() {
  var dom = new JSDOM('<!DOCTYPE html><body></body>', { runScripts: 'outside-only' });
  var w = dom.window;
  w.SimVar = { GetSimVarValue: function () { return 0; }, SetSimVarValue: function () {} };
  w.eval(fs.readFileSync(AGENT, 'utf8'));
  return { w: w, A: w.__MSFSBA_FLYPAD };
}

// (a) Row WITH text-utility-green AND svg in box => "true" (complete)
test('valueOf checkitem: green row with svg => true', function () {
  var ref = loadAgent();
  var A = ref.A, w = ref.w;
  var div = w.document.createElement('div');
  div.innerHTML = makeRow(true, true);
  var row = div.firstChild;
  assert.strictEqual(A.valueOf('checkitem', row), 'true');
});

// (b) Row WITHOUT green token but WITH svg in box (the auto-fill Link45deg case) => "false"
test('valueOf checkitem: no-green row with svg (auto-fill link45deg) => false', function () {
  var ref = loadAgent();
  var A = ref.A, w = ref.w;
  var div = w.document.createElement('div');
  div.innerHTML = makeRow(false, true);
  var row = div.firstChild;
  assert.strictEqual(A.valueOf('checkitem', row), 'false',
    'svg inside .border-4 must NOT be treated as checked without text-utility-green');
});

// (c) Row WITHOUT green AND WITHOUT svg => "false"
test('valueOf checkitem: no-green no-svg => false', function () {
  var ref = loadAgent();
  var A = ref.A, w = ref.w;
  var div = w.document.createElement('div');
  div.innerHTML = makeRow(false, false);
  var row = div.firstChild;
  assert.strictEqual(A.valueOf('checkitem', row), 'false');
});

// (d) classify still returns "checkitem" for the row shape (both green and non-green)
test('classify: flex-row + space-x-4 + .border-4 => checkitem', function () {
  var ref = loadAgent();
  var A = ref.A, w = ref.w;

  var div = w.document.createElement('div');
  div.innerHTML = makeRow(false, false);
  var row = div.firstChild;
  assert.strictEqual(A.classify(row), 'checkitem');

  div.innerHTML = makeRow(true, true);
  var rowGreen = div.firstChild;
  assert.strictEqual(A.classify(rowGreen), 'checkitem');
});
