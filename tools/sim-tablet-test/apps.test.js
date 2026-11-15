'use strict';
// The add-on apps installed into the tablet, captured live on 2026-11-15 (MSFS 2024, iniBuilds A350 at
// OMDB gate A3), and the generic rules that read them: native form controls, ARIA, switches drawn
// with a class, presses bound with addEventListener (STAB-13), state words, OS sliders set by their
// read-out (STAB-12), frames (STAB-14).
const { test } = require('node:test');
const assert = require('node:assert');
const { JSDOM } = require('jsdom');
const { load, scrape, lines } = require('./run');

const find = (els, text) => els.find(e => e.text === text);
const scraped = (A, gel) => JSON.parse(A.scrape(gel)).elements;

test('iniJetways: each jetway and door says its state, keyed so the state can change under the pilot', () => {
  const els = scrape('inijetways-docked');
  const l = lines(els);
  for (const want of ['button|Lower Jetway 1 (connected)', 'button|Lower Jetway 2 (idle)', 'button|Left Door 1 (active)',
                      'button|Left Door 2', 'button|Disconnect All']) assert.ok(l.includes(want), want);
  const j1 = find(els, 'Lower Jetway 1 (connected)');
  assert.equal(j1.key, 'button:Lower Jetway 1');
  assert.equal(j1.announceChange, true);
  // A door with no state yet is keyed all the same: when it docks, its node keeps the reader's place.
  assert.equal(find(els, 'Left Door 2').key, 'button:Left Door 2');
});

test('iniJetways: a jetway press is its one click', () => {
  const { A, window } = load('inijetways-docked');
  assert.equal(A.clickElement(find(scraped(A), 'Lower Jetway 2 (idle)').idx), true);
  // One click, which reaches the jetway first and then bubbles to the page's own click handler (which
  // clears a selection only for a click outside the jetways and doors), as a real one does.
  assert.equal(window.__presses[0], 'click:Lower Jetway 2');
  assert.ok(window.__presses.every(p => p.indexOf('click:') === 0), 'never a mousedown');
});

test('CIVA: a switch drawn by its class reads as a tick box, and a picture names its button', () => {
  const l = lines(scrape('civa-navlog'));
  assert.ok(l.includes('/checkbox|Import procedures=false'));
  assert.ok(l.includes('/checkbox|Auto-sync to INS=true'));
  assert.ok(l.includes('button|CIVA Options'), 'the cog is named by its alt');
  assert.ok(l.includes('button|CIVA Internal State Viewer'));
});

test('CIVA: ticking a class switch presses it once, and a switch already there is left alone', () => {
  const { A, window } = load('civa-navlog');
  const els = scraped(A);
  assert.equal(A.setValue(find(els, 'Auto-sync to INS').idx, 'true'), true);
  assert.deepStrictEqual(window.__presses, [], 'already on: nothing pressed');
  assert.equal(A.setValue(find(els, 'Import procedures').idx, 'true'), true);
  assert.equal(window.__presses.length, 1);
});

test('MK Airport: tabs and switches bound with addEventListener before the reader came are found by their listeners', () => {
  // As live: the listeners are bound before the reader is installed, so the hook never saw them, and
  // only getEventListeners (here window.__gel) shows them.
  const { A, window } = load('mk-airport-settings', { listeners: 'bound' });
  const els = scraped(A, window.__gel);
  const l = lines(els);
  assert.ok(l.includes('button|Settings (selected)'), 'mk-tab--on is the selected tab');
  assert.ok(l.includes('button|Jetways'));
  const sw = els.find(e => e.controlType === 'checkbox' && /^Follow the MSFS jetway/.test(e.text));
  assert.ok(sw, 'the mk-switch reads as a tick box');
  assert.equal(sw.value, 'true');
  A.clickElement(find(els, 'Jetways').idx, window.__gel);
  assert.deepStrictEqual(window.__presses, ['click:Jetways']);
});

test('MK Airport: without getEventListeners the -tab class still finds the tabs', () => {
  const { A } = load('mk-airport-settings', { listeners: 'bound' });
  assert.ok(lines(scraped(A)).includes('button|Settings (selected)'));
});

test('UTL: a native select, a number box with its bounds, tick boxes inside their labels', () => {
  const els = scrape('utl-main');
  const utc = find(els, 'UTC');
  assert.equal(utc.controlType, 'select');
  assert.equal(utc.value, 'Now');
  assert.equal(utc.options[0], 'Now');
  assert.ok(utc.options.length > 1);
  assert.equal(find(els, 'Level (100 to 450)').value, '330');
  assert.equal(find(els, 'Taller profile').controlType, 'checkbox');
  assert.equal(find(els, 'Ahead of aircraft (active)').kind, 'button');
  // A caption is read with its control, never again as a line of its own.
  assert.ok(!els.some(e => e.kind === 'static' && (e.text === 'Level' || e.text === 'Taller profile')));
});

test('UTL: picking a choice sets the select by its own setter and tells the page', () => {
  const { A, document } = load('utl-main');
  const els = scraped(A);
  const sel = document.querySelector('select');
  const choice = find(els, 'UTC').options[1];
  let changes = 0;
  sel.addEventListener('change', () => changes++);
  assert.equal(A.setValue(find(els, 'UTC').idx, choice), true);
  assert.equal(sel.options[sel.selectedIndex].textContent.replace(/\s+/g, ' ').trim(), choice);
  assert.equal(changes, 1);
  assert.equal(A.setValue(find(els, 'UTC').idx, 'No such time'), false);
});

test('UTL: ticking a tick box clicks it, so the page hears it as the browser says it', () => {
  const { A, document } = load('utl-main');
  const box = find(scraped(A), 'Taller profile');
  const input = document.querySelector('[data-sim-tablet-idx="' + box.idx + '"]');
  let changes = 0;
  input.addEventListener('change', () => changes++);
  assert.equal(A.setValue(box.idx, 'true'), true);
  assert.equal(input.checked, true);
  assert.equal(changes, 1);
  assert.equal(A.setValue(box.idx, 'true'), true);
  assert.equal(changes, 1, 'already ticked: nothing more');
});

test('AtmosphereMod: a slider beside its read-out reads as that value, and its tabs and switches read', () => {
  const l = lines(scrape('atmo-haze'));
  assert.ok(l.includes('/text|Haze strength (slider)=100 %'));
  assert.ok(l.includes('/text|Sun glare in haze (slider)=57 %'));
  assert.ok(l.includes('tab|Haze (selected)'));
  assert.ok(l.includes('/checkbox|Haze from METAR=true'));
  assert.ok(!l.some(x => /^static\|\d+ %$/.test(x)), 'the read-out is the slider\'s value, never a line apart');
});

test('AtmosphereMod: buttons that say the same thing say which row they are on; a box is named by its placeholder', () => {
  const l = lines(scrape('atmo-presets'));
  assert.ok(l.includes('button|Default: LOAD'));
  assert.ok(l.includes('button|AirborneGeek_V1: LOAD'));
  assert.ok(l.includes('/text|A name for your settings'), 'not the "Active: unsaved settings" line two rows up');
  assert.ok(l.includes('static|Active: unsaved settings'));
});

test('SayIntentions: a pick shown with data-active reads as selected', () => {
  const l = lines(scrape('sayintentions-atc'));
  assert.ok(l.includes('button|Pilot (selected)'));
  assert.ok(l.includes('button|Co-Pilot'));
  assert.ok(l.includes('button|ATC (selected)'));
});

test('GSX Remote: an app\'s own frame is read whole, its role="button" heads pressable', () => {
  const { A } = load('gsx-remote');
  assert.equal(A.inEfb(), false);
  const l = lines(scraped(A));
  assert.ok(l.includes('button|GSX Menu Tap to open'));
  assert.ok(l.includes('static|Our iniBuilds A350 is Parked'));
  assert.ok(l.includes('button|Restart Couatl'));
});

// ---------------------------------------------------------------------------------------------
// small built pages for the edge cases
// ---------------------------------------------------------------------------------------------

// A page of the tablet with one app view on screen holding `inner`; every element is shown.
function page(inner, opts) {
  opts = opts || {};
  const dom = new JSDOM('<!DOCTYPE html><html><body><div class="efb"><div class="efb-container">' +
    '<div class="efb-view TestApp">' + inner + '</div></div></div></body></html>');
  const { window } = dom;
  global.window = window; global.document = window.document;
  global.MutationObserver = window.MutationObserver; global.InputEvent = window.InputEvent; global.MouseEvent = window.MouseEvent;
  global.Event = window.Event;
  window.getComputedStyle = el => ({ display: 'block', visibility: 'visible', opacity: '1', cursor: (opts.pointer && opts.pointer(el)) ? 'pointer' : 'auto' });
  window.Element.prototype.getBoundingClientRect = function () {
    const r = (this.getAttribute('data-rect') || '0,0,100,20').split(',').map(Number);
    return { top: r[0], left: r[1], right: r[2], bottom: r[3], width: r[2] - r[1], height: r[3] - r[0] };
  };
  if (opts.before) opts.before(window);
  window.eval(require('fs').readFileSync(require('path').join(__dirname, '..', '..', 'MSFSBlindAssist', 'Resources', 'coherent-sim-tablet-agent.js'), 'utf8'));
  return { window, document: window.document, A: window.__MSFSBA_SIM_TABLET };
}

// An OS slider (the shape of efb_os's Slider: --ratio-value on .slider, mousedown on .slider-bar, then
// the document's mousemove and mouseup) over 0-200, with a read-out beside it.
function osSlider(opts) {
  const p = page('<div class="row"><span class="name">Haze strength</span><div class="ctl">' +
    '<div class="slider" style="--ratio-value: 40%;" data-rect="0,0,200,20"><div class="slider-bar" data-rect="0,0,200,20"></div></div></div>' +
    '<div class="value">80 %</div></div>');
  const { window, document } = p;
  const sl = document.querySelector('.slider'), ro = document.querySelector('.value');
  let dragging = false;
  const show = pct => {
    sl.setAttribute('style', '--ratio-value: ' + pct + '%;');
    const text = Math.round(pct * 2) + ' %';
    if (opts.readout === 'sync') ro.textContent = text;
    else if (opts.readout === 'async') window.setTimeout(() => { ro.textContent = text; }, 0);
  };
  document.querySelector('.slider-bar').onmousedown = () => { dragging = true; };
  document.addEventListener('mousemove', e => { if (dragging) show(Math.max(0, Math.min(100, e.clientX / 2))); });
  document.addEventListener('mouseup', () => { dragging = false; });
  return p;
}

test('a slider beside its read-out is set to a typed value in the read-out\'s own units', () => {
  const { A, document } = osSlider({ readout: 'sync' });
  const el = find(scraped(A), 'Haze strength (slider)');
  assert.equal(el.value, '80 %');
  assert.equal(A.setValue(el.idx, '120'), true);
  assert.equal(document.querySelector('.value').textContent, '120 %');
  assert.equal(A.setValue(el.idx, '+10'), true, 'a signed number moves it from where it is');
  assert.equal(document.querySelector('.value').textContent, '130 %');
});

test('a read-out that does not follow the slider leaves it where it was, and the set is refused', () => {
  const { A, document } = osSlider({ readout: 'async' });
  const el = find(scraped(A), 'Haze strength (slider)');
  assert.equal(A.setValue(el.idx, '120'), false);
  assert.equal(A.ratioOf(document.querySelector('.slider')), 40);
});

test('a slider whose read-out says nothing it can measure is refused and put back', () => {
  const { A, document } = osSlider({ readout: 'none' });
  document.querySelector('.value').textContent = 'v2.1';     // a number, but not this slider's
  const el = scraped(A).find(e => /Haze strength/.test(e.text));
  assert.equal(A.setValue(el.idx, '50'), false);
  assert.equal(A.ratioOf(document.querySelector('.slider')), 40);
});

test('numbers: a typed comma decimal and a Unicode minus; a read-out\'s thousands', () => {
  const { A } = page('');
  assert.equal(A.numberIn('0,5'), 0.5);
  assert.equal(A.numberIn('−5'), -5);
  assert.equal(A.readoutNumber('1,013 hPa'), 1013);
  assert.equal(A.readoutNumber('4.4°'), 4.4);
});

test('a label with a pointer cursor is a caption, never a button (pressing it would focus its box)', () => {
  const { A } = page('<label for="cs" class="lab">Callsign</label><input id="cs" type="text" value="DAL12">',
    { pointer: el => el.tagName === 'LABEL' });
  const l = lines(scraped(A));
  assert.deepStrictEqual(l, ['/text|Callsign=DAL12']);
});

test('a button listening to mousedown alone is pressed with mousedown, even a <button>', () => {
  const { A, window } = page('<button class="go" data-h="mousedown">Go</button>', {
    before: w => w.document.querySelector('.go').addEventListener('mousedown', () => w.__presses.push('mousedown')),
  });
  window.__presses = window.__presses || [];
  const gel = el => (el.className === 'go' ? { mousedown: [{}] } : {});
  const els = scraped(A, gel);
  assert.equal(A.clickElement(find(els, 'Go').idx, gel), true);
  assert.deepStrictEqual(window.__presses, ['mousedown']);
});

test('a dialog says its title as it opens: the OS popup view and its -popup-title', () => {
  const { A } = page('<div class="ui-view LoadPopup popup"><div class="mb-popup"><div class="mb-popup-title">LOAD AIRCRAFT</div>' +
    '<div class="question"><span>Load now?</span></div><button>LOAD IMMEDIATELY</button></div></div><button>Behind</button>');
  const l = lines(scraped(A));
  assert.deepStrictEqual(l.slice(0, 3), ['alert|LOAD AIRCRAFT', 'static|Load now?', 'button|LOAD IMMEDIATELY']);
});

test('a frame from another origin is a marker with its origin and size, for the client to read', () => {
  const { A } = page('<iframe class="f" src="http://127.0.0.1:8744/?t=1" data-rect="10,0,554,753"></iframe>');
  A.frameDoc = () => null;          // jsdom loads no cross-origin page; live, contentDocument is null
  const m = scraped(A).find(e => e.kind === 'frame');
  assert.equal(m.value, 'http://127.0.0.1:8744');
  assert.equal(m.text, '554x743');
});

test('a native slider reads its bounds and takes a typed value through its own setter', () => {
  const { A, document } = page('<label>Briefing volume <input id="v" type="range" min="0" max="100" step="5" value="75"><output>75%</output></label>');
  const el = find(scraped(A), 'Briefing volume');
  assert.deepStrictEqual([el.controlType, el.value, el.min, el.max, el.step], ['range', '75', 0, 100, 5]);
  let inputs = 0;
  document.getElementById('v').addEventListener('input', () => inputs++);
  assert.equal(A.setValue(el.idx, '40'), true);
  assert.equal(document.getElementById('v').value, '40');
  assert.equal(inputs, 1);
});

test('repeated buttons: the row names the text, never the key, and a selected row stays in its group', () => {
  const rows = active => ['Default', 'Rainy', 'Sunny'].map(n =>
    '<div class="row"><span class="name">' + n + '</span><button class="load' + (n === active ? ' active' : '') + '">LOAD</button></div>').join('');
  const before = page(rows('Default'));
  const b = scraped(before.A).filter(e => /LOAD/.test(e.text));
  assert.deepStrictEqual(b.map(e => e.text), ['Default: LOAD (active)', 'Rainy: LOAD', 'Sunny: LOAD']);
  assert.deepStrictEqual(b.map(e => e.key), ['button:LOAD', 'button:LOAD', 'button:LOAD'], 'the shell keeps repeats apart by order');
  const after = page(rows('Rainy'));
  const a = scraped(after.A).filter(e => /LOAD/.test(e.text));
  assert.deepStrictEqual(a.map(e => e.text), ['Default: LOAD', 'Rainy: LOAD (active)', 'Sunny: LOAD']);
  assert.deepStrictEqual(a.map(e => e.key), b.map(e => e.key));
});

test('repeated buttons: a value is never a row\'s name, so a changing number cannot touch them', () => {
  const { A } = page('<div class="r"><span>29</span><span>GAL</span><button>More information</button></div>' +
    '<div class="r"><span>Total mass</span><button>More information</button></div>');
  const l = lines(scraped(A));
  // "29 GAL" is a value: the pair cannot be told apart by name, so both stay as the page says them.
  assert.deepStrictEqual(l.filter(x => /More information/.test(x)), ['button|More information', 'button|More information']);
});

test('a link is followed even when it also listens to mousedown', () => {
  let followed = 0;
  const { A, window } = page('<a class="go" href="#there">Go there</a>', {
    before: w => {
      const a = w.document.querySelector('.go');
      a.addEventListener('mousedown', () => {});
      a.addEventListener('click', ev => { followed++; ev.preventDefault(); });
    },
  });
  const gel = el => (el.className === 'go' ? { mousedown: [{}] } : {});
  assert.equal(A.clickElement(find(scraped(A, gel), 'Go there').idx, gel), true);
  assert.equal(followed, 1);
});

test('a slider whose position is not shown refuses "+5": from where?', () => {
  const { A } = page('<div class="row"><span class="name">Gain</span><div class="slider" data-rect="0,0,200,20"><div class="slider-bar"></div></div></div>');
  const el = find(scraped(A), 'Gain (slider, percent)');
  assert.equal(el.controlType, 'text');
  assert.equal(A.setValue(el.idx, '+5'), false);
});

test('a native slider refuses a value that is no number (the browser would put it at its midpoint)', () => {
  const { A, document } = page('<label>Volume <input id="v" type="range" min="0" max="100" value="75"></label>');
  const el = find(scraped(A), 'Volume');
  assert.equal(A.setValue(el.idx, 'loud'), false);
  assert.equal(document.getElementById('v').value, '75');
});

test('a read-only or fieldset-disabled box refuses a typed value', () => {
  const { A } = page('<input id="a" type="text" readonly value="x"><fieldset disabled><input id="b" type="text" value="y"></fieldset>');
  const els = scraped(A);
  assert.equal(els[0].disabled, true);
  assert.equal(A.setValue(els[0].idx, 'z'), false);
  assert.equal(els[1].disabled, true);
  assert.equal(A.setValue(els[1].idx, 'z'), false);
});
