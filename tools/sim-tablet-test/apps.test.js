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
  window.getComputedStyle = el => ({ display: opts.display ? opts.display(el) : 'block', visibility: 'visible', opacity: '1',
                                     cursor: (opts.pointer && opts.pointer(el)) ? 'pointer' : 'auto' });
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

test('repeated buttons: a row with no name says its whole line, and a number changing in it never touches the key', () => {
  const rows = n => '<div class="r"><span>' + n + '</span><span>GAL</span><button>More information</button></div>' +
    '<div class="r"><span>Total mass</span><button>More information</button></div>';
  const a = scraped(page(rows(29)).A).filter(e => /More information/.test(e.text));
  const b = scraped(page(rows(27)).A).filter(e => /More information/.test(e.text));
  assert.deepStrictEqual(a.map(e => e.text), ['29 GAL: More information', 'Total mass: More information']);
  assert.deepStrictEqual(b.map(e => e.text), ['27 GAL: More information', 'Total mass: More information']);
  assert.deepStrictEqual(b.map(e => e.key), a.map(e => e.key), 'the shell keeps the node, and the reader\'s place on it');
  assert.ok(a.every(e => e.key === 'button:More information'));
});

test('spacing: a word split over inline runs stays one word; two boxed words do not run together', () => {
  const inline = el => (el.tagName === 'B' || el.tagName === 'SPAN' ? 'inline' : 'block');
  const one = lines(scraped(page('<p class="w"><b data-rect="0,0,30,20">Lon</b><span data-rect="0,30,60,20">don</span></p>', { display: inline }).A));
  assert.deepStrictEqual(one, ['static|London']);
  const two = lines(scraped(page('<div class="kv"><div data-rect="0,0,100,20">Airport module</div><div data-rect="0,100,200,20">not responding</div></div>').A));
  assert.deepStrictEqual(two, ['static|Airport module not responding']);
});

test('a control the page shows on hover is read, from the page\'s own :hover rule; one no rule reveals is not', () => {
  const { A } = page('<style>.bar:hover .closer { visibility: visible; }</style>' +
    '<div class="bar"><span>OMDB</span><div class="closer"><button title="Clear Selected Facility"></button></div>' +
    '<div class="gone"><button title="Never shown"></button></div></div>', {
    before: w => {
      const orig = w.getComputedStyle;
      w.__hidden = el => el.classList && (el.classList.contains('closer') || el.classList.contains('gone') ||
        (el.parentElement && el.parentElement.classList && (el.parentElement.classList.contains('closer') || el.parentElement.classList.contains('gone'))));
    },
  });
  const gcs = window.getComputedStyle;
  window.getComputedStyle = el => Object.assign({}, gcs(el), window.__hidden(el) ? { visibility: 'hidden' } : {});
  const l = lines(scraped(A));
  assert.ok(l.includes('button|Clear Selected Facility'));
  assert.ok(!l.includes('button|Never shown'));
});

// A page whose computed style the test sets: style(el) returns what differs from the visible default
// (visibility is inherited, so hide a whole subtree with el.closest).
function styled(inner, style) {
  const p = page(inner);
  const gcs = p.window.getComputedStyle;
  p.window.getComputedStyle = el => Object.assign({}, gcs(el), style(el) || {});
  return p;
}

test('hover reveal: only through a hover on an ancestor, never a rule hovering the faded control itself', () => {
  const l = lines(scraped(styled('<style>.btn:hover { opacity: 1; }</style>' +
    '<div class="toast"><span>Plan deleted</span><button class="btn">Undo</button></div>',
    el => (el.classList.contains('btn') ? { opacity: '0' } : null)).A));
  assert.ok(!l.some(x => /Undo/.test(x)), 'a dismissed toast\'s Undo stays unread');
});

test('hover reveal: a tooltip never joins the name of the control it belongs to', () => {
  const own = lines(scraped(styled('<style>.has-tip:hover .tip { visibility: visible; }</style>' +
    '<button class="has-tip">OK<span class="tip">Confirm the change and close</span></button>',
    el => (el.closest('.tip') ? { visibility: 'hidden' } : null)).A));
  assert.ok(own.includes('button|OK'), own.join(' / '));
  const inner = lines(scraped(styled('<style>.row:hover .act { visibility: visible; }</style>' +
    '<div class="row"><span>Plan</span><button class="act">Apply<span class="tooltip">Applies it to the route</span></button></div>',
    el => (el.closest('.act') ? { visibility: 'hidden' } : null)).A));
  assert.ok(inner.includes('button|Apply'), inner.join(' / '));
});

test('hover reveal: a revealed button reads dimmed exactly when its disabled-layer shows (STAB-3)', () => {
  // As the OS does (measured): the layer is display:none on an enabled control, shown on a disabled one.
  const page2 = disabled => styled('<style>.row:hover .act { visibility: visible; }</style>' +
    '<div class="row"><span>Plan</span><button class="act">Delete<div class="disabled-layer"></div></button></div>',
    el => Object.assign({}, el.closest('.act') ? { visibility: 'hidden' } : {},
                        el.classList.contains('disabled-layer') && !disabled ? { display: 'none' } : {}));
  const on = lines(scraped(page2(false).A));
  assert.ok(on.includes('button|Delete'), on.join(' / '));
  const off = lines(scraped(page2(true).A));
  assert.ok(off.includes('button|Delete [dim]'), off.join(' / '));
});

test('hover reveal: a control inside a wrapper named for its tooltip is still revealed', () => {
  // The hover reveals the group around the wrapper, so the walk up passes the wrapper first.
  const l = lines(scraped(styled('<style>.row:hover .acts { visibility: visible; }</style>' +
    '<div class="row"><span>KDFW</span><div class="acts"><div class="tooltip-component"><button>Remove</button></div></div></div>',
    el => (el.closest('.acts') ? { visibility: 'hidden' } : null)).A));
  assert.ok(l.includes('button|Remove'), l.join(' / '));
});

test('a field named by its section\'s heading (the row\'s name, past the caption beside it) leaves the heading in place', () => {
  const l = lines(scraped(page('<section><h3>Fuel</h3><p></p><p></p><p></p><div class="row"><i></i><i></i><i></i>' +
    '<div class="textbox"><input value="12"></div></div></section>').A));
  assert.ok(l.includes('heading|Fuel'), l.join(' / '));
  assert.ok(l.includes('/text|Fuel=12'), l.join(' / '));
});

test('a choice row whose every item is hidden is read as it is', () => {
  const l = lines(scraped(styled('<div class="multiple-buttons"><span>Units</span><button class="h">KG</button></div>',
    el => (el.classList.contains('h') ? { display: 'none' } : null)).A));
  assert.ok(l.includes('static|Units'), l.join(' / '));
});

test('hover reveal: a row\'s hover menu is read with its items (a sighted pilot reaches them), a hint with no control is not', () => {
  const l = lines(scraped(styled('<style>.menu:hover .sub { visibility: visible; } .card:hover .hint-text { visibility: visible; }</style>' +
    '<div class="menu"><span>Plans</span><div class="sub"><button>Export</button></div></div>' +
    '<div class="card"><span>KDFW</span><div class="hint-text">Click to open the airport</div></div>',
    el => (el.closest('.sub') || el.closest('.hint-text') ? { visibility: 'hidden' } : null)).A));
  assert.ok(l.includes('button|Export'), l.join(' / '));
  assert.ok(!l.some(x => /Click to open/.test(x)), 'words alone are never revealed');
});

test('hover reveal: rules inside @media count, and a rule added after the first read is seen', () => {
  const p = styled('<style id="s">@media screen { .row:hover .x-close { visibility: visible; } }</style>' +
    '<div class="row"><span>KDFW</span><button class="x-close">Clear</button></div>' +
    '<div class="row2"><span>KSAV</span><button class="y-close">Remove</button></div>',
    el => (el.closest('.x-close') || el.closest('.y-close') ? { visibility: 'hidden' } : null));
  const first = lines(scraped(p.A));
  assert.ok(first.includes('button|Clear'), first.join(' / '));
  assert.ok(!first.includes('button|Remove'));
  p.document.getElementById('s').sheet.insertRule('.row2:hover .y-close { visibility: visible; }', 1);
  p.A._markDirty();
  assert.ok(lines(scraped(p.A)).includes('button|Remove'));
});

test('an icon is named by a word, never an editor\'s numbered layer', () => {
  const { A, document } = page(
    '<span id="a"><svg id="svg1"><g id="layer1"><path id="down"></path></g></svg></span>' +
    '<span id="b"><svg id="svg2"><g id="Calque_1"><rect id="Rectangle_4"></rect><path id="paint0_linear_12_3"></path></g></svg></span>' +
    '<span id="c"><svg><g id="Tracé_12"><path id="linearGradient123"></path><path id="filter0_d_1_2"></path></g></svg></span>' +
    '<span id="d"><svg><g id="Mask group"><g id="Clip path group"><g id="SVGRepo_bgCarrier"></g><g id="SVGRepo_tracerCarrier"></g>' +
    '<g id="SVGRepo_iconCarrier"><path id="settings"></path></g></g></g></svg></span>');
  assert.equal(A.iconName(document.getElementById('a')), 'down');
  assert.equal(A.iconName(document.getElementById('b')), '');
  assert.equal(A.iconName(document.getElementById('c')), '');
  assert.equal(A.iconName(document.getElementById('d')), 'settings');
});

test('a field is keyed by its name alone or with a twin, so a twin coming or going never rebuilds it', () => {
  const one = scraped(page('<div class="r"><span>Left</span><label>Manual <input type="checkbox"></label></div>').A);
  const two = scraped(page('<div class="r"><span>Left</span><label>Manual <input type="checkbox"></label></div>' +
    '<div class="r"><span>Right</span><label>Manual <input type="checkbox"></label></div>').A);
  assert.equal(one.find(e => e.controlType === 'checkbox').key, 'field:Manual');
  assert.deepStrictEqual(two.filter(e => e.controlType === 'checkbox').map(e => e.key + '=' + e.text),
    ['field:Manual=Left: Manual', 'field:Manual=Right: Manual']);
});

test('a box named by its row\'s caption reads the caption once, with the box', () => {
  const l = lines(scraped(page('<div class="row"><span>Cruise altitude</span><i></i><i></i><i></i>' +
    '<div class="textbox"><input value="350"></div></div>').A));
  assert.ok(l.includes('/text|Cruise altitude=350'), l.join(' / '));
  assert.ok(!l.includes('static|Cruise altitude'), l.join(' / '));
});

test('a choice row with nothing to pick is read as it is, not dropped', () => {
  const l = lines(scraped(page('<div class="multiple-buttons"><span>No unit to choose on this aircraft</span></div>').A));
  assert.ok(l.includes('static|No unit to choose on this aircraft'), l.join(' / '));
});

test('a row of exclusive choices made of pressable divs reads its items with the selected one', () => {
  const { A, window } = page('<div class="multiple-buttons"><div class="opt selected" data-h="click">Default</div><div class="opt">Satellite</div></div>', {
    before: w => {
      w.__presses = [];
      w.document.querySelectorAll('.opt').forEach(d => { d.onclick = () => w.__presses.push(d.textContent); });
    },
  });
  const els = scraped(A);
  assert.deepStrictEqual(lines(els), ['button|Default (selected)', 'button|Satellite']);
  A.clickElement(find(els, 'Satellite').idx);
  assert.deepStrictEqual(window.__presses, ['Satellite']);
});

test('repeated fields say their row under a stable key, never their own caption', () => {
  const box = n => '<div class="row"><span class="t">' + n + '</span><div class="f"><span>Manual</span>' +
    '<div class="switch-container"></div></div><span>0 KG</span></div>';
  const els = scraped(page(box('Trip fuel') + box('Alternate fuel')).A).filter(e => e.controlType === 'checkbox');
  assert.deepStrictEqual(els.map(e => e.text), ['Trip fuel: Manual', 'Alternate fuel: Manual']);
  assert.deepStrictEqual(els.map(e => e.key), ['field:Manual', 'field:Manual']);
});

test('a chevron in a header folds; a map\'s layer of controls is no dialog', () => {
  const l = lines(scraped(page('<div class="facility-header"><div class="title-container"><span>OMDB</span>' +
    '<button><svg id="svg1"><g id="layer1"><path id="down"></path></g></svg></button></div></div>' +
    '<div class="map-overlay" data-rect="0,0,600,700"><div class="title">Ground Speed</div><button>Zoom in</button></div>').A));
  assert.ok(l.includes('button|Fold or unfold'));
  assert.ok(!l.some(x => /^alert/.test(x)), 'no alert for the map overlay');
});

test('state words: a class that says "not selected" is not selected; a keypad\'s X is X', () => {
  const l = lines(scraped(page('<button class="opt-btn not-selected">Option A</button><button class="opt-btn opt-btn-selected">Option B</button>' +
    '<div class="keys"><button>W</button><button>X</button><button>Y</button></div>').A));
  assert.ok(l.includes('button|Option A'));
  assert.ok(l.includes('button|Option B (selected)'));
  assert.ok(l.includes('button|X'));
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

test('a view that gets no frames: the app on screen is updated before each read, press and set', () => {
  let updates = 0;
  const { A, window } = page('<button class="b">Door 1</button>', {
    before: w => {
      w.requestAnimationFrame = () => 0;     // queued, never run: nothing shows the tablet
      w.EFB_API = { _installedApps: { array: [{ options: { efbViewService: { update: () => updates++ } } }] } };
    },
  });
  const els = scraped(A);
  assert.equal(updates, 1);
  A.clickElement(find(els, 'Door 1').idx);
  assert.equal(updates, 3, 'before the press, and after it, so what it set going runs before the next read');
  A.setValue(find(els, 'Door 1').idx, 'x');
  assert.equal(updates, 4);
});

const sleep = ms => new Promise(r => setTimeout(r, ms));

test('a view that gets frames, read at the client\'s pace: the reader adds nothing after the first read', async () => {
  let updates = 0;
  const { A } = page('<button class="b">Door 1</button>', {
    before: w => {
      w.requestAnimationFrame = cb => setTimeout(() => cb(Date.now()), 33);   // about 30 a second
      w.EFB_API = { _installedApps: { array: [{ options: { efbViewService: { update: () => updates++ } } }] } };
    },
  });
  for (let i = 0; i < 4; i++) { scraped(A); await sleep(620); }
  assert.equal(updates, 1, 'only the very first read, before any frame was seen');
});

test('a view that gets no frames, read at the client\'s pace: every read updates the app, with a rising time', async () => {
  const times = [];
  const { A } = page('<button class="b">Door 1</button>', {
    before: w => {
      w.requestAnimationFrame = () => 0;
      w.EFB_API = { _installedApps: { array: [{ options: { efbViewService: { update: t => times.push(t) } } }] } };
    },
  });
  for (let i = 0; i < 3; i++) { scraped(A); await sleep(620); }
  assert.equal(times.length, 3);
  assert.ok(times[0] < times[1] && times[1] < times[2]);
});

test('"Show the tablet in the sim" is offered once a frame has been missing a second, never on a view that renders (STAB-16)', async () => {
  const SHOW = 'Show the tablet in the sim (some lists fill in only while the sim shows it)';
  const stalled = page('<button class="b">Door 1</button>', { before: w => { w.requestAnimationFrame = () => 0; } });
  assert.equal(find(scraped(stalled.A), SHOW), undefined, 'not at the first read: no frame asked for yet');
  await sleep(1100);
  const offer = find(scraped(stalled.A), SHOW);
  assert.ok(offer, 'offered once the frame asked for is a second late');
  assert.equal(offer.idx, 990002);
  assert.equal(offer.key, 'action:tablet-panel');
  assert.equal(stalled.A.clickElement(990002), false, 'the client runs it, never the page');

  const rendering = page('<button class="b">Door 1</button>', {
    before: w => { w.requestAnimationFrame = cb => setTimeout(() => cb(Date.now()), 33); },
  });
  scraped(rendering.A);
  await sleep(1100);
  const again = JSON.parse(rendering.A.scrape());
  assert.ok(again.unchanged || !find(again.elements, SHOW), 'a view that renders: nothing to offer, nothing re-read');
});

test('the tablet panel button: frames coming back re-read the list, and the panel MSFSBA showed reads "Hide" on the same key', async () => {
  const SHOW = 'Show the tablet in the sim (some lists fill in only while the sim shows it)';
  let pending = [];
  let framesOn = false;   // the view's frames: off (queued, never run) or about 30 a second
  const p = page('<button class="b">Door 1</button>', {
    before: w => { w.requestAnimationFrame = cb => (framesOn ? setTimeout(() => cb(Date.now()), 33) : (pending.push(cb), 1)); },
  });
  scraped(p.A);
  await sleep(1100);
  assert.ok(find(scraped(p.A), SHOW));
  // The client shows the panel: frames come, and it marks the page.
  pending.splice(0).forEach(cb => cb(Date.now()));
  p.window.__MSFSBA_TABLET_SHOWN = true;
  framesOn = true;
  const after = JSON.parse(p.A.scrape());
  assert.ok(!after.unchanged, 'the offer moved, so the list is read again though no element changed');
  const hide = find(after.elements, 'Hide the tablet in the sim');
  assert.ok(hide, after.elements.map(e => e.text).join(' / '));
  assert.equal(hide.idx, 990002);
  assert.equal(hide.key, 'action:tablet-panel', 'the same node: the screen reader keeps its place');
  // Hidden again and the mark cleared: nothing offered while frames still come.
  p.window.__MSFSBA_TABLET_SHOWN = false;
  const gone = JSON.parse(p.A.scrape());
  assert.ok(!gone.unchanged && !find(gone.elements, 'Hide the tablet in the sim') && !find(gone.elements, SHOW));
});

test('a view that gets no frames: the page\'s queued frame callbacks run once, by the pump or the browser, never twice', () => {
  const queued = [];
  const p = page('<div id="out">loading</div>', {
    before: w => { w.requestAnimationFrame = cb => { queued.push(cb); return queued.length; }; w.cancelAnimationFrame = () => {}; },
  });
  let runs = 0;
  p.window.requestAnimationFrame(() => { runs++; p.document.getElementById('out').textContent = 'loaded'; });
  const first = lines(scraped(p.A));
  assert.ok(first.includes('static|loaded'), first.join(' / '));
  assert.equal(runs, 1);
  queued.forEach(cb => cb(Date.now()));       // the browser's frame comes after all
  assert.equal(runs, 1, 'already run by the pump: the browser\'s call is a no-op');
  // A cancelled callback never runs.
  let cancelled = 0;
  const id = p.window.requestAnimationFrame(() => { cancelled++; });
  p.window.cancelAnimationFrame(id);
  p.A._markDirty();
  scraped(p.A);
  assert.equal(cancelled, 0);
});

test('a virtual list reads in its on-screen order and offers Scroll up and Scroll down, which move it a page', () => {
  // As the OS's VirtualList: rows placed with position:absolute and a top, in any DOM order.
  const rows = [['Sep 22', 424], ['Oct 19', 154], ['Oct 15', 208]];
  const p = page('<div class="virtual-list-container"><div class="virtual-list">' +
    rows.map(r => '<div class="virtual-list-item" data-top="' + r[1] + '"><button>' + r[0] + '</button></div>').join('') +
    '</div></div><button class="to-top-button"><svg><path id="arrow"></path></svg></button>', {
    before: w => {
      const gcs = w.getComputedStyle;
      w.getComputedStyle = el => Object.assign({}, gcs(el), el.classList && el.classList.contains('virtual-list-item') ? { position: 'absolute' } : {});
    },
  });
  const list = p.document.querySelector('.virtual-list-container');
  Object.defineProperty(list, 'clientHeight', { value: 500 });
  Object.defineProperty(list, 'scrollHeight', { value: 5000 });
  let scrolled = 0;
  list.addEventListener('scroll', () => scrolled++);
  p.document.querySelectorAll('.virtual-list-item').forEach(el => {
    const t = +el.getAttribute('data-top');
    el.getBoundingClientRect = () => ({ top: t, left: 0, right: 300, bottom: t + 50, width: 300, height: 50 });
  });
  const els = scraped(p.A);
  const l = lines(els);
  assert.deepStrictEqual(l.filter(x => /Sep|Oct/.test(x)), ['button|Oct 19', 'button|Oct 15', 'button|Sep 22']);
  assert.ok(l.includes('button|Scroll up (top of list) [dim]'), 'at the top: dimmed, never gone (the node keeps its place)');
  const down = els.find(e => /^Scroll down/.test(e.text));
  assert.equal(down.text, 'Scroll down (list at 0%)');
  assert.equal(down.key, 'scroll:0:down');
  assert.ok(l.includes('button|Back to the top'));
  assert.equal(p.A.clickElement(down.idx), true);
  assert.equal(list.scrollTop, 450);
  assert.equal(scrolled, 1, 'the list hears its scroll');
  const after = lines(scraped(p.A));
  assert.ok(after.includes('button|Scroll up (list at 10%)') && after.includes('button|Scroll down (list at 10%)'), after.join(' / '));
});

test('the OS frame loop, once recorded, never repeats the view update in one pump, nor fills the browser queue', () => {
  // As efb_os.js: update() runs the view service, then queues itself again through window.requestAnimationFrame.
  let framesOn = true, updates = 0;
  const queue = new Map();
  let nextId = 1;
  const p = page('<button class="b">Door 1</button>', {
    before: w => {
      w.requestAnimationFrame = cb => { const id = nextId++; queue.set(id, cb); if (framesOn) setTimeout(() => { const f = queue.get(id); queue.delete(id); if (f) f(Date.now()); }, 0); return id; };
      w.cancelAnimationFrame = id => { queue.delete(id); };
      w.EFB_API = { _installedApps: { array: [{ options: { efbViewService: { update: () => updates++ } } }] } };
    },
  });
  const vs = p.window.EFB_API._installedApps.array[0].options.efbViewService;
  const os = { update() { vs.update(Date.now()); p.window.requestAnimationFrame(this.update.bind(this)); } };
  os.update();                                  // one real frame after the reader came: the loop is recorded
  framesOn = false;
  updates = 0;
  p.A._lastFrame = 0;                           // stalled from here
  scraped(p.A);
  assert.equal(updates, 1, 'one view update per pump');
  const size = queue.size;
  for (let i = 0; i < 2; i++) {
    p.A._askedAt = Date.now() - 1000;           // the frame asked for is late: this read pumps
    updates = 0;
    p.A._markDirty(); scraped(p.A);
    assert.equal(updates, 1, 'one view update per pump');
    assert.equal(queue.size, size, 'the browser queue does not grow');
  }
});

test('a view update the pump makes runs what it queued for a frame in the same pump', () => {
  const p = page('<div id="door">Door 1 Closed</div><button>Go</button>', {
    before: w => {
      w.requestAnimationFrame = () => 0;
      w.EFB_API = { _installedApps: { array: [{ options: { efbViewService: {
        update: () => w.requestAnimationFrame(() => { w.document.getElementById('door').textContent = 'Door 1 Open'; }),
      } } }] } };
    },
  });
  assert.ok(lines(scraped(p.A)).includes('static|Door 1 Open'));
});

test('a press that makes a box focus itself hands the keyboard back, though Coherent leaves activeElement on the body (STAB-1)', () => {
  const p = page('<div class="textbox"><input id="q"></div><button id="add">ADD FAVORITE</button>');
  const inp = p.document.getElementById('q');
  let focused = false;
  inp.addEventListener('focus', () => { focused = true; });
  inp.addEventListener('focusout', () => { focused = false; });
  Object.defineProperty(p.document, 'activeElement', { get: () => p.document.body });
  p.document.getElementById('add').onclick = () => inp.focus();
  const els = scraped(p.A);
  assert.equal(p.A.clickElement(els.find(e => e.text === 'ADD FAVORITE').idx), true);
  assert.equal(focused, false, 'focusout sent: the OS box sends UNFOCUS_INPUT_FIELD');
});

test('a group the OS disables dims every button in it (Settings, Tablet size while the sim shows no tablet)', () => {
  const l = lines(scraped(page('<div class="item" disabled=""><div class="item-title">Tablet size</div>' +
    '<div class="multiple-buttons" disabled=""><button class="selected">SMALL</button><button>MEDIUM</button></div></div>' +
    '<div class="item"><div class="item-title">Units</div><div class="multiple-buttons"><button>KTS</button></div></div>').A));
  assert.ok(l.includes('button|SMALL (selected) [dim]') && l.includes('button|MEDIUM [dim]'), l.join(' / '));
  assert.ok(l.includes('button|KTS'), 'a group with no disabled attribute stays live');
});

test('a search list that shows before anything is typed is read (Settings, Add favorite)', () => {
  const l = lines(scraped(page('<div class="search-bar"><div class="textbox"><input placeholder="search"></div>' +
    '<div class="search-bar-list-container"><div class="search-bar-list">' +
    '<div class="search-app-list-item">Aircraft</div><div class="search-app-list-item">GSX</div></div></div></div>').A));
  assert.ok(l.includes('button|Result: Aircraft') && l.includes('button|Result: GSX'), l.join(' / '));
});

test('SayIntentions: a button showing only a symbol or number takes its aria-label, keeping the number', () => {
  const l = lines(scrape('sayintentions-atc'));
  for (const want of ['button|Zoom in', 'button|Reset zoom (100%)', 'button|Zoom out']) assert.ok(l.includes(want), want);
});

test('a button that is only a value reads with the label right before it (SayIntentions’ airport frequencies)', () => {
  const l = lines(scraped(page('<div class="f"><span>APP</span><button>122.5</button></div>' +
    '<div class="f"><span>TWR</span><button>118.75</button></div><div class="k"><span>Total 12</span><button>3</button></div>').A));
  assert.ok(l.includes('button|APP 122.5') && l.includes('button|TWR 118.75'), l.join(' / '));
  assert.ok(!l.includes('static|APP'), 'the label reads once, with its button');
  assert.ok(l.includes('button|3') && l.includes('static|Total 12'), 'a label holding a number is a value, never a name');
});

test('AtmosphereMod: its status line reads its two parts with a comma', () => {
  const l = lines(scrape('atmo-haze'));
  assert.ok(l.includes('static|METAR link: running, last poll 2026-11-15T18:16:59.155Z, AtmosphereMod 0.7.3'), l.filter(x => /METAR link/.test(x)).join(' / '));
});

test('a press that moves a virtual list itself (Back to the top) makes the list hear its scroll', () => {
  const p = page('<div class="virtual-list-container"><div class="virtual-list"><button>Row</button></div></div>' +
    '<button class="to-top-button">Top</button>');
  const list = p.document.querySelector('.virtual-list-container');
  Object.defineProperty(list, 'clientHeight', { value: 500 });
  Object.defineProperty(list, 'scrollHeight', { value: 5000 });
  list.scrollTop = 900;
  let heard = 0;
  list.addEventListener('scroll', () => heard++);
  p.document.querySelector('.to-top-button').onclick = () => { list.scrollTop = 0; };
  const els = scraped(p.A);
  assert.equal(p.A.clickElement(els.find(e => e.text === 'Top').idx), true);
  assert.equal(heard, 1);
});

test('two rows that are each a name with its value join with a comma, a caption over its value with a colon', () => {
  const l = lines(scraped(page('<button>Go</button>' +
    '<div class="pair"><div><span>By day</span> <span>00h26</span></div><div><span>By night</span> <span>0h</span></div></div>' +
    '<div class="cap"><div>Ground Speed</div><div>0 KT</div></div>', {
    before: w => {
      const rect = w.Element.prototype.getBoundingClientRect;
      // Each row of .pair and .cap, and everything in it, on a line of its own.
      w.Element.prototype.getBoundingClientRect = function () {
        for (let n = this; n && n.parentElement; n = n.parentElement) {
          const p = n.parentElement;
          if (p.classList.contains('pair') || p.classList.contains('cap')) {
            const top = n === p.children[0] ? 0 : 30;
            return { top, left: 0, right: 100, bottom: top + 20, width: 100, height: 20 };
          }
        }
        return rect.call(this);
      };
    },
  }).A));
  assert.ok(l.includes('static|By day 00h26, By night 0h'), l.join(' / '));
  assert.ok(l.includes('static|Ground Speed: 0 KT'), l.join(' / '));
});

test('the Pilot book: a chart bar reads as one line, the period first, and the day and night icons as words', () => {
  const l = lines(scraped(page('<button>WEEK</button>' +
    '<div class="flight-hours-stick"><div class="flight-duration-display"><div class="date-display-container hide">00h45</div><span>1h</span></div>' +
    '<div class="stick current"></div><span class="period">15/11</span></div>' +
    '<div class="total-statistics"><div class="total-statistics-header"><span>Total landings</span><div>28</div></div>' +
    '<div class="total-statistics-body"><div class="statistic"><div class="icon-element day-icon"><svg></svg></div><div>9</div></div>' +
    '<div class="statistic"><div class="icon-element night-icon"><svg></svg></div><div>19</div></div></div></div>').A));
  assert.ok(l.includes('static|15/11 (current): 1h, 00h45'), l.join(' / '));
  assert.ok(l.some(x => /day 9 night 19/.test(x)), l.join(' / '));
});

test('the tablet panel button stays in the list through a Show and a Hide (the press is stamped)', async () => {
  const SHOW = 'Show the tablet in the sim (some lists fill in only while the sim shows it)', HIDE = 'Hide the tablet in the sim';
  let pending = [];
  let framesOn = false;   // the view's frames: off (queued, never run) or about 30 a second
  const p = page('<button class="b">Door 1</button>', {
    before: w => { w.requestAnimationFrame = cb => (framesOn ? setTimeout(() => cb(Date.now()), 33) : (pending.push(cb), 1)); },
  });
  // As the client reads: an "unchanged" read keeps the last list, so the offer must move the read itself.
  let last = [];
  const offer = () => {
    const r = JSON.parse(p.A.scrape());
    if (!r.unchanged) last = r.elements;
    const e = last.find(x => x.idx === 990002);
    return e ? e.text + ' ' + e.key : 'none';
  };
  scraped(p.A);
  await sleep(1100);
  assert.equal(offer(), SHOW + ' action:tablet-panel');
  // Show: the client stamps the press, frames start, and the mark comes only once they are seen.
  p.window.__MSFSBA_TABLET_PRESS_AT = Date.now();
  pending.splice(0).forEach(cb => cb(Date.now()));
  framesOn = true;
  await sleep(100);
  assert.equal(offer(), SHOW + ' action:tablet-panel', 'between the first frame and the mark');
  p.window.__MSFSBA_TABLET_SHOWN = true;
  assert.equal(offer(), HIDE + ' action:tablet-panel');
  await sleep(3100);
  assert.equal(offer(), HIDE + ' action:tablet-panel', 'shown, long after the press');
  // Hide: stamped, frames stop, the mark stays until they are seen to stop, then clears.
  p.window.__MSFSBA_TABLET_PRESS_AT = Date.now();
  framesOn = false;
  await sleep(700);
  assert.equal(offer(), HIDE + ' action:tablet-panel', 'while the client checks the frames stopped');
  p.window.__MSFSBA_TABLET_SHOWN = false;
  assert.equal(offer(), SHOW + ' action:tablet-panel', 'hidden: offered again at once');
  await sleep(2500);
  assert.equal(offer(), SHOW + ' action:tablet-panel', 'and after the press window, by the missing frames');
});

test('the tablet panel button is never offered outside the tablet (an app\'s own frame)', async () => {
  const dom = new JSDOM('<!DOCTYPE html><html><body><button>Go</button></body></html>');
  const w = dom.window;
  global.window = w; global.document = w.document;
  w.requestAnimationFrame = () => 0;
  w.getComputedStyle = () => ({ display: 'block', visibility: 'visible', opacity: '1', cursor: 'auto' });
  w.Element.prototype.getBoundingClientRect = () => ({ top: 0, left: 0, right: 100, bottom: 20, width: 100, height: 20 });
  w.eval(require('fs').readFileSync(require('path').join(__dirname, '..', '..', 'MSFSBlindAssist', 'Resources', 'coherent-sim-tablet-agent.js'), 'utf8'));
  const A = w.__MSFSBA_SIM_TABLET;
  scraped(A);
  await sleep(1100);
  A._markDirty();
  assert.ok(!scraped(A).some(e => e.idx === 990002));
});

test('choice rows: a pick keeps its row name when the selection moves, and short names count', () => {
  const rows = landing => ['Take off weather', 'Landing weather'].map((n, i) =>
    '<div class="row"><span class="name">' + n + '</span><div class="multiple-buttons">' +
    '<button class="' + (i === 1 && landing === 'MANUAL' ? '' : 'selected') + '">METAR</button>' +
    '<button class="' + (i === 1 && landing === 'MANUAL' ? 'selected' : '') + '">MANUAL</button></div></div>').join('');
  const a = lines(scraped(page(rows('METAR')).A));
  const b = lines(scraped(page(rows('MANUAL')).A));
  assert.ok(a.includes('button|Landing weather: METAR (selected)'));
  assert.ok(b.includes('button|Landing weather: MANUAL (selected)'));
  assert.ok(b.includes('button|Take off weather: METAR (selected)'));
  const lr = lines(scraped(page('<div class="r"><span>Left</span><button>More information</button></div>' +
    '<div class="r"><span>Right</span><button>More information</button></div>').A));
  assert.deepStrictEqual(lr.filter(x => /More/.test(x)), ['button|Left: More information', 'button|Right: More information']);
});

test('a read-only or fieldset-disabled box refuses a typed value', () => {
  const { A } = page('<input id="a" type="text" readonly value="x"><fieldset disabled><input id="b" type="text" value="y"></fieldset>');
  const els = scraped(A);
  assert.equal(els[0].disabled, true);
  assert.equal(A.setValue(els[0].idx, 'z'), false);
  assert.equal(els[1].disabled, true);
  assert.equal(A.setValue(els[1].idx, 'z'), false);
});
