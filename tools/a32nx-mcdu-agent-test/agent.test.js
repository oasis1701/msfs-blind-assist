'use strict';
// Regression tests for the FlyByWire A32NX MCDU in-page agent
// (MSFSBlindAssist/Resources/coherent-a32nx-mcdu-agent.js).
//
// The agent replaces SimBridge's MCDU relay as MSFSBA's read/key transport. Its read()
// must produce the {left, right} body A320_Neo_CDU_MainDisplay.sendUpdate() puts on the
// relay, as far as the shared C# decoder reads it — so these tests compare the agent
// against a HAND TRANSCRIPTION of sendUpdate() (expectedScreen below) over a stub of the
// legacy display object. The transcription is not FBW's source: it omits the brightness
// fields the decoder ignores, and an upstream change to sendUpdate() must be carried into
// it and the agent by hand — this file catches the agent drifting from the transcription,
// not the transcription drifting from FBW. No jsdom: the agent touches only
// document.querySelector, SimVar, window and JSON, all supplied by a sandbox under node:vm.

const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs');
const path = require('path');
const vm = require('vm');

const AGENT = path.join(__dirname, '..', '..', 'MSFSBlindAssist', 'Resources', 'coherent-a32nx-mcdu-agent.js');
const source = fs.readFileSync(AGENT, 'utf8');

// A stub of the legacy display with the fields sendUpdate() reads.
function makeFms() {
  return {
    _labels: [['CO RTE', 'FROM/TO', ''], ['ALTN/CO RTE', '', ''], [], [], [], []],
    _lines: [['', '{cyan}____/____{end}', ''], ['{cyan}NONE{end}', '', ''], [], [], [], []],
    _title: '{white}INIT{end}',
    _pageCurrent: 1,
    _pageCount: 2,
    _arrows: [false, false, true, true],
    scratchpadDisplay: { getText: () => 'DELETE', getColor: () => 'amber' },
    annunciators: {
      left:  { fmgc: false, fail: false, mcdu_menu: true,  fm1: false, ind: false, rdy: false, blank: false, fm2: false },
      right: { fmgc: false, fail: false, mcdu_menu: false, fm1: false, ind: false, rdy: false, blank: false, fm2: true },
    },
    onEvent() {},
  };
}

// What sendUpdate() itself would send for the stub above (transcribed from
// A320_Neo_CDU_MainDisplay.ts: 12 rows interleaving label[k]/line[k], the scratchpad
// wrapped in its colour, the page counter in {small}, empty title/titleLeft strings).
function expectedScreen(fms) {
  const lines = [];
  for (let k = 0; k < 6; k++) { lines.push(fms._labels[k]); lines.push(fms._lines[k]); }
  return {
    lines,
    scratchpad: `{${fms.scratchpadDisplay.getColor()}}${fms.scratchpadDisplay.getText()}{end}`,
    title: fms._title,
    titleLeft: '',
    page: `{small}${fms._pageCurrent}/${fms._pageCount}{end}`,
    arrows: fms._arrows,
  };
}

function install(opts) {
  const o = Object.assign({ fms: makeFms(), powered1: 1, powered2: 1, element: 'a32nx-mcdu' }, opts);
  const dispatched = [];
  const instrument = {
    legacyFms: o.fms,
    hEventPublisher: o.noPublisher ? undefined : { dispatchHEvent: (name) => dispatched.push(['dispatchHEvent', name]) },
    bus: o.noBus ? undefined : { pub: (topic, name, sync, isCached) => dispatched.push(['bus.pub', topic, name, sync, isCached]) },
  };
  const el = o.fms ? { fsInstrument: instrument } : null;
  const simVars = {
    'L:A32NX_ELEC_AC_ESS_SHED_BUS_IS_POWERED': o.powered1,
    'L:A32NX_ELEC_AC_2_BUS_IS_POWERED': o.powered2,
  };
  const writes = [];
  const sandbox = {
    document: { querySelector: (sel) => (sel === o.element ? el : null) },
    SimVar: {
      GetSimVarValue: (name) => simVars[name],
      SetSimVarValue: (name, unit, value) => { writes.push([name, unit, value]); return Promise.resolve(); },
    },
    JSON, Object, String,
  };
  // As in the MCDU page, window IS the global object: the app's scripts name the agent both
  // as window.__MSFSBA_A32NX_MCDU and as a bare global.
  sandbox.window = sandbox;
  const marker = vm.runInNewContext(source, sandbox);
  // Evaluate a script the app sends (Runtime.evaluate) in the same page; returns its value.
  const run = (script) => vm.runInContext(script, sandbox);
  return { marker, agent: sandbox.__MSFSBA_A32NX_MCDU, dispatched, writes, run };
}

// The script the app sends for one key press (CoherentA32nxMcduClient.BuildPressExpression),
// pinned byte-for-byte by the xUnit suite. Executing it here checks it as JavaScript.
const PRESS_DIV = fs.readFileSync(
  path.join(__dirname, '..', '..', 'tests', 'MSFSBlindAssist.Tests', 'Fixtures', 'a32nx-mcdu-press-DIV.js'), 'utf8');

test('installs itself on window and returns the marker the C# client looks for', () => {
  const { marker, agent } = install();
  assert.strictEqual(marker, 'MSFSBA_A32NX_MCDU_INSTALLED');
  assert.ok(agent && typeof agent.read === 'function' && typeof agent.press === 'function');
});

test('read() rebuilds sendUpdate()\'s body field for field with both MCDUs powered', () => {
  const fms = makeFms();
  const { agent } = install({ fms });
  const body = JSON.parse(agent.read());
  assert.strictEqual(body.ok, true);

  const screen = expectedScreen(fms);
  // A missing label/line row (the stub's [] entries) is the relay's ['', '', ''].
  screen.lines = screen.lines.map(row => [0, 1, 2].map(i => (row && row[i] != null) ? String(row[i]) : ''));

  assert.deepStrictEqual(body.content.left, Object.assign({}, screen, { annunciators: fms.annunciators.left }));
  assert.deepStrictEqual(body.content.right, Object.assign({}, screen, { annunciators: fms.annunciators.right }));
});

test('an unpowered MCDU 1 renders the relay\'s empty shape on the left while the right keeps the screen', () => {
  const fms = makeFms();
  const { agent } = install({ fms, powered1: 0, powered2: 1 });
  const body = JSON.parse(agent.read());

  assert.strictEqual(body.content.left.title, '');
  assert.strictEqual(body.content.left.scratchpad, '');
  assert.strictEqual(body.content.left.lines.length, 12);
  assert.deepStrictEqual(body.content.left.lines[0], ['', '', '']);
  assert.strictEqual(body.content.left.displayBrightness, 0);
  assert.strictEqual(body.content.right.title, fms._title);
});

test('with both MCDUs unpowered both sides are the empty shape', () => {
  const { agent } = install({ powered1: 0, powered2: 0 });
  const body = JSON.parse(agent.read());
  assert.strictEqual(body.ok, true);
  assert.strictEqual(body.content.left.title, '');
  assert.strictEqual(body.content.right.title, '');
});

test('no page counter when the page has no pages', () => {
  const fms = makeFms();
  fms._pageCount = 0;
  const { agent } = install({ fms });
  assert.strictEqual(JSON.parse(agent.read()).content.left.page, '');
});

test('read() reports not-ready when the display fields it rebuilds the screen from are missing', () => {
  // If an FBW update moves these fields while legacyFms stays, read() must not answer ok:true
  // with a blank screen: that keeps Coherent "readable", so the window shows an empty MCDU
  // labelled Connected while SimBridge — whose payload FBW maintains — is never used.
  for (const missing of ['_labels', '_lines', 'scratchpadDisplay']) {
    const fms = makeFms();
    delete fms[missing];
    const { agent } = install({ fms });
    assert.strictEqual(JSON.parse(agent.read()).ok, false, missing);
  }
});

test('read() stays ok while the page has no title yet', () => {
  // FBW leaves _title undefined until the first setTitle — that is a loading page, not a
  // missing field.
  const fms = makeFms();
  fms._title = undefined;
  const { agent } = install({ fms });
  assert.strictEqual(JSON.parse(agent.read()).ok, true);
});

test('read() reports not-ready, never throws, while the instrument is absent', () => {
  const { agent } = install({ fms: null });
  const body = JSON.parse(agent.read());
  assert.strictEqual(body.ok, false);
});

test('the Headwind A330 element name is accepted too', () => {
  const { agent } = install({ element: 'a339x-mcdu' });
  assert.strictEqual(JSON.parse(agent.read()).ok, true);
});

test('the agent has no separate liveness ping: a closed MCDU window reads the screen too', () => {
  // FMS messages ("DEST EFOB BELOW MIN") must still reach a pilot whose MCDU window is closed,
  // so the client reads the screen at the idle rate instead of pinging (PR #253 review).
  const { agent } = install();
  assert.strictEqual(agent.ping, undefined);
});

test('press() dispatches the Captain H-event through the instrument\'s own publisher', () => {
  const { agent, dispatched } = install();
  assert.strictEqual(agent.press('INIT'), 'dispatchHEvent');
  assert.deepStrictEqual(dispatched, [['dispatchHEvent', 'A320_Neo_CDU_1_BTN_INIT']]);
});

test('press() falls back to a bus publish, then to onEvent, and names what it used', () => {
  const viaBus = install({ noPublisher: true });
  assert.strictEqual(viaBus.agent.press('L1'), 'bus.pub');
  // Exactly what HEventPublisher.dispatchHEvent publishes: sync false, isCached false. Synced,
  // the key would be broadcast to every other instrument; cached, it would be replayed to any
  // hEvent subscriber that subscribes later — a phantom key press.
  assert.deepStrictEqual(viaBus.dispatched, [['bus.pub', 'hEvent', 'A320_Neo_CDU_1_BTN_L1', false, false]]);

  const fms = makeFms();
  const seen = [];
  fms.onEvent = (ev) => seen.push(ev);
  const direct = install({ fms, noPublisher: true, noBus: true });
  assert.strictEqual(direct.agent.press('CLR'), 'onEvent');
  assert.deepStrictEqual(seen, ['1_BTN_CLR']);
});

test('the app\'s press script presses the key and then clicks the cockpit key under its model name', () => {
  // FBW's relay handler sets L:A32NX_MCDU_PUSH_ANIM_<n>_<key> on every press — the key's
  // animation and its click. The script sets it at its TOP LEVEL (Coherent drops a write made
  // from inside a stored agent function), and "/" (H-event DIV) animates as SLASH.
  const { run, dispatched, writes } = install();
  assert.strictEqual(run(PRESS_DIV), 'dispatchHEvent');
  assert.deepStrictEqual(dispatched, [['dispatchHEvent', 'A320_Neo_CDU_1_BTN_DIV']]);
  assert.deepStrictEqual(writes, [['L:A32NX_MCDU_PUSH_ANIM_1_SLASH', 'Number', 1]]);
});

test('the press script clicks nothing when the instrument did not take the key', () => {
  const noPath = install({ noPublisher: true, noBus: true, fms: Object.assign(makeFms(), { onEvent: undefined }) });
  assert.strictEqual(noPath.run(PRESS_DIV), 'no-dispatch-path');
  assert.deepStrictEqual(noPath.writes, []);

  const noInstrument = install({ fms: null });
  assert.strictEqual(noInstrument.run(PRESS_DIV), 'no-instrument');
  assert.deepStrictEqual(noInstrument.writes, []);
});

test('the press script answers no-agent, and clicks nothing, on a page that lost the agent', () => {
  const { run, writes, agent } = install();
  delete agent.press;                        // stand-in for a page re-evaluated without the agent
  run('window.__MSFSBA_A32NX_MCDU = undefined;');
  assert.strictEqual(run(PRESS_DIV), 'no-agent');
  assert.deepStrictEqual(writes, []);
});

test('press() with no instrument reports it instead of throwing', () => {
  const { agent } = install({ fms: null });
  assert.strictEqual(agent.press('INIT'), 'no-instrument');
});

test('the agent is ES5 (Coherent GT is Chromium 49)', () => {
  // A cheap tripwire, not a parser: the constructs Chromium 49 rejects outright.
  assert.doesNotMatch(source, /=>/, 'arrow function');
  assert.doesNotMatch(source, /\b(let|const)\s/, 'let/const');
  assert.doesNotMatch(source, /`/, 'template string');
  assert.doesNotMatch(source, /\.includes\(/, 'String.prototype.includes');
});
