// flyPad SelectInput dropdowns (Performance takeoff/landing calculators): the
// clickable ROOT has no direct text (value sits in an inner cell beside the
// chevron svg), so the generic rules missed it and the dropdowns were
// UNREACHABLE. Class-shape detection + "FieldLabel: Value" composition.
// Fixture markup is source-derived from SelectInput.tsx + TakeoffWidget.tsx's
// Label component (verbatim class lists), not live-captured.
const { test } = require('node:test');
const assert = require('node:assert');
const { JSDOM } = require('jsdom');
const fs = require('node:fs');
const path = require('node:path');
const AGENT = path.join(__dirname, '..', '..', 'MSFSBlindAssist', 'Resources', 'coherent-flypad-agent.js');

const selectInput = (value, opts) => {
  opts = opts || {};
  const cursor = opts.disabled ? 'cursor-not-allowed opacity-50' : 'cursor-pointer';
  const options = (opts.options || []).map(
    (o) => '<div class="hover:bg-theme-highlight/5 px-3 py-1.5 transition duration-300 hover:text-theme-body">' + o + '</div>'
  ).join('');
  const dropdown = options
    ? '<div class="absolute -inset-x-0.5 z-10 flex overflow-hidden border-2 border-theme-accent bg-theme-body pb-2 pr-2 bottom-0 translate-y-full flex-col rounded-b-md border-t-0"><div>' + options + '</div></div>'
    : '';
  return '<div class="flex flex-row">' +
    '<div class="relative ' + cursor + ' rounded-md border-2 border-theme-accent w-60" data-si="root">' +
    '<div class="relative flex px-3 py-1.5">' + value + '<svg width="20" height="20"></svg></div>' +
    dropdown + '</div></div>';
};

const labeled = (label, inner) =>
  '<div class="flex flex-row items-center justify-between">' +
  '<p class="mr-4 text-theme-text">' + label + '</p>' + inner + '</div>';

function load(html) {
  const dom = new JSDOM('<!DOCTYPE html><body>' + html + '</body>', { runScripts: 'outside-only' });
  const w = dom.window;
  w.SimVar = { GetSimVarValue() { return 0; }, SetSimVarValue() {} };
  w.eval(fs.readFileSync(AGENT, 'utf8'));
  return w;
}

test('closed SelectInput root classifies as a button and reads "Field: Value"', () => {
  const w = load(labeled('Runway Condition', selectInput('Dry')));
  const A = w.__MSFSBA_FLYPAD;
  const root = w.document.querySelector('[data-si="root"]');
  assert.strictEqual(A.classify(root), 'button', 'SelectInput root not classified');
  assert.strictEqual(A.labelFor(root), 'Runway Condition: Dry');
  assert.strictEqual(A.disabledFor(root), false);
});

test('disabled SelectInput reads dimmed but keeps its composed label', () => {
  const w = load(labeled('Anti-Ice', selectInput('On', { disabled: true })));
  const A = w.__MSFSBA_FLYPAD;
  const root = w.document.querySelector('[data-si="root"]');
  assert.strictEqual(A.classify(root), 'button');
  assert.strictEqual(A.labelFor(root), 'Anti-Ice: On');
  assert.strictEqual(A.disabledFor(root), true, 'cursor-not-allowed not treated as disabled');
});

test('label-less SelectInput reads its bare value; open options classify as buttons', () => {
  const w = load(selectInput('OFP', { options: ['METAR', 'OFP'] }));
  const A = w.__MSFSBA_FLYPAD;
  const root = w.document.querySelector('[data-si="root"]');
  assert.strictEqual(A.labelFor(root), 'OFP');
  const opts = [...w.document.querySelectorAll('div')].filter((d) => /hover:bg-theme-highlight\/5/.test(d.className));
  assert.strictEqual(opts.length, 2);
  for (const o of opts) assert.strictEqual(A.classify(o), 'button', 'dropdown option not clickable: ' + o.textContent);
});

test('a plain bordered div without the chevron/value shape is NOT a SelectInput', () => {
  const w = load('<div class="relative cursor-pointer rounded-md border-2 border-theme-accent" data-si="root"></div>');
  const A = w.__MSFSBA_FLYPAD;
  assert.strictEqual(A.isSelectInputRoot(w.document.querySelector('[data-si="root"]')), false);
});

test('orderSelectOptions: open-dropdown options regroup right after the dropdown items', () => {
  const w = load('<div></div>');
  const A = w.__MSFSBA_FLYPAD;
  const root = {}; // identity token standing in for the SelectInput root element
  const items = [
    { text: 'Runway Condition', top: 10, _selRoot: null, _selOpt: false },
    { text: 'Dry (6)', top: 12, _selRoot: root, _selOpt: false },
    { text: 'Landing Weight', top: 20, _selRoot: null, _selOpt: false },
    { text: 'Good (5)', top: 30, _selRoot: root, _selOpt: true },
    { text: 'Overweight Procedure', top: 40, _selRoot: null, _selOpt: false },
    { text: 'Medium (3)', top: 50, _selRoot: root, _selOpt: true },
    { text: 'Calculate', top: 90, _selRoot: null, _selOpt: false },
  ];
  A.orderSelectOptions(items);
  assert.deepStrictEqual(items.map((i) => i.text), [
    'Runway Condition', 'Dry (6)', 'Good (5)', 'Medium (3)',
    'Landing Weight', 'Overweight Procedure', 'Calculate',
  ]);
});

test('scrape() serializes pages containing a SelectInput (no cyclic _selRoot leak)', () => {
  // one CLOSED labeled dropdown (composed label must surface) + one OPEN one
  // (mounted options = the cyclic-_selRoot risk case for serialization).
  const w = load(labeled('Runway Condition', selectInput('Dry')) + selectInput('OFP', { options: ['METAR'] }));
  const A = w.__MSFSBA_FLYPAD;
  // raw-JSDOM shims (the fixture harness normally provides these): everything
  // visible, monotonically increasing rects so row clustering works.
  A.isVisible = function () { return true; };
  let y = 0;
  w.Element.prototype.getBoundingClientRect = function () {
    y += 10; return { top: y, left: 120, right: 220, bottom: y + 8, width: 100, height: 8 };
  };
  const out = JSON.parse(A.scrape());
  assert.strictEqual(out.ok, true, 'scrape failed: ' + (out.error || ''));
  assert.ok(out.elements.some((e) => /Runway Condition: Dry/.test(e.text)), 'composed dropdown missing from scrape');
});
