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
  const SHOW = 'Show the tablet in the sim (some apps and lists update only while the sim shows it)';
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
  const SHOW = 'Show the tablet in the sim (some apps and lists update only while the sim shows it)';
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
  inp.blur = () => {};                          // as in Coherent: blur() does nothing for a box it never saw focused
  p.document.getElementById('add').onclick = () => inp.focus();
  const els = scraped(p.A);
  assert.equal(p.A.clickElement(els.find(e => e.text === 'ADD FAVORITE').idx), true);
  assert.equal(focused, false, 'the focusout the reader sent reached it: the OS box sends UNFOCUS_INPUT_FIELD');
});

test('a box the OS marks focused, with no focus event seen, is handed back too; a field of the page itself is left alone (STAB-1)', () => {
  const p = page('<div class="textbox" id="w"><input id="q"></div><input id="own" type="text"><button id="go">Go</button>');
  const q = p.document.getElementById('q'), own = p.document.getElementById('own');
  let qOut = 0, ownOut = 0;
  q.addEventListener('focusout', () => qOut++);
  own.addEventListener('focusout', () => ownOut++);
  Object.defineProperty(p.document, 'activeElement', { get: () => p.document.body });
  q.blur = () => {};
  p.window.__MSFSBA_LAST_FOCUS = own;           // a page field focused earlier: not the OS's, not focused now
  p.document.getElementById('go').onclick = () => p.document.getElementById('w').classList.add('textbox--focused');
  const els = scraped(p.A);
  p.A.clickElement(els.find(e => e.text === 'Go').idx);
  assert.equal(qOut, 1, 'the marked OS box got its focusout');
  assert.equal(ownOut, 0, 'the page field did not');
});

test('the view-update wrapper of the pump disarms itself: a reference taken during a pump still updates afterwards', () => {
  let updates = 0, kept = null;
  const p = page('<button>Go</button>', {
    before: w => {
      w.requestAnimationFrame = () => 0;
      const vs = new (class ViewService { update() { updates++; } })();   // a class method, as in efb_os
      w.EFB_API = { _installedApps: { array: [{ options: { efbViewService: vs } }] } };
      w.requestAnimationFrame(() => { kept = vs.update; });   // queued before: never run by the pump
    },
  });
  const vs = p.window.EFB_API._installedApps.array[0].options.efbViewService;
  p.window.requestAnimationFrame(() => { kept = vs.update; });  // recorded: runs inside the pump, sees the wrapper
  scraped(p.A);
  updates = 0;
  kept();
  kept();
  assert.equal(updates, 2, 'outside a pump the wrapper passes every call through');
  assert.ok(!Object.prototype.hasOwnProperty.call(vs, 'update'), 'the class method is back, no own wrapper left');
});

test('a shown search list skips its hidden rows', () => {
  const p = styled('<div class="search-bar"><div class="textbox"><input></div><div class="search-bar-list-container"><div class="search-bar-list">' +
    '<div class="r">Aircraft</div><div class="r gone">GSX</div></div></div></div>',
    el => (el.classList.contains('gone') ? { display: 'none' } : null));
  const l = lines(scraped(p.A));
  assert.ok(l.includes('button|Result: Aircraft') && !l.some(x => /GSX/.test(x)), l.join(' / '));
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

test('an abbreviation with a hidden tip reads as text with the tip spelled out, never a button (TechLog)', () => {
  const p = styled('<button>Perform</button><div class="ck"><div class="ring"><div>750.0</div></div>' +
    '<div class="small"><span class="term">FH<span class="term-tip">Flight hours</span></span> left</div></div>',
    el => (el.classList.contains('term-tip') ? { display: 'none' } : null));
  p.document.querySelector('.term').addEventListener('click', () => {});
  const l = lines(scraped(p.A, p.window.__gel));
  assert.ok(!l.some(x => /^button\|.*FH/.test(x)), l.join(' / '));
  assert.ok(l.some(x => /FH \(Flight hours\) left/.test(x)), l.join(' / '));
});

test('a wrapping paragraph with inline badges reads whole and in order (TechLog MEL help)', () => {
  const p = styled('<button>Go</button><p class="h">2. Read whether the aircraft may be dispatched. <span class="b">NO-GO</span> items must be fixed.</p>',
    el => (el.tagName === 'SPAN' ? { display: 'inline-block' } : null));
  // A paragraph over two rows: the badge and the words after it sit lower than its start.
  const b = p.document.querySelector('.b');
  b.getBoundingClientRect = () => ({ top: 30, left: 0, right: 40, bottom: 50, width: 40, height: 20 });
  const l = lines(scraped(p.A));
  assert.ok(l.includes('static|2. Read whether the aircraft may be dispatched. NO-GO items must be fixed.'), l.join(' / '));
  assert.ok(!l.includes('static|NO-GO'), 'the badge is no line of its own');
});

test('a control with a tooltip stays a control: only an abbreviation shape reads as text (review round 13)', () => {
  const p = styled('<div class="x" id="x" data-rect="0,0,20,10">✕<span class="tooltip">Close</span></div>' +
    '<span class="s" id="s" data-rect="0,30,60,10">Save<span class="act-tip">Save the plan</span></span>' +
    '<div class="tooltip-component" id="g" data-rect="20,0,40,30">GPU<div class="tooltip forceHide">Not compatible</div></div>',
    el => (el.classList.contains('tooltip') || el.classList.contains('act-tip') ? { display: 'none' } : null));
  for (const id of ['x', 's', 'g']) p.document.getElementById(id).onclick = () => {};
  const l = lines(scraped(p.A));
  assert.ok(l.includes('button|Close') || l.some(x => /^button\|.*Close/.test(x)), 'the cross keeps its press: ' + l.join(' / '));
  assert.ok(l.some(x => /^button\|Save/.test(x)), 'Save keeps its press: ' + l.join(' / '));
  assert.ok(!l.some(x => /Not compatible/.test(x)), 'a tooltip the OS hides for good is never read');
});

test('a field the press itself focuses is handed back, any field; one focused before the press is not (STAB-1)', () => {
  const p = page('<input id="own" type="text"><button id="go">Go</button>');
  const own = p.document.getElementById('own');
  let outs = 0;
  own.addEventListener('focusout', () => outs++);
  own.blur = () => {};
  Object.defineProperty(p.document, 'activeElement', { get: () => p.document.body });
  p.document.getElementById('go').onclick = () => own.focus();
  const els = scraped(p.A);
  p.A.clickElement(els.find(e => e.text === 'Go').idx);
  assert.equal(outs, 1, 'focused by the press: released');
});

test('an all-capitals control word with a tooltip keeps its press; only a tip that spells the letters out makes an abbreviation', () => {
  const words = [['SAVE', 'Save the plan'], ['OK', 'Confirm'], ['X', 'Close'], ['ARM', 'Arms the autobrake'], ['ON', 'Turn on'],
                 ['LOAD', 'Loading the plan'], ['CLR', 'Clear'], ['HDG', '(Heading)']];
  const p = styled(words.map((w, i) => '<div class="w" id="w' + i + '" data-rect="' + (i * 20) + ',0,40,' + (i * 20 + 10) + '">' + w[0] +
    '<span class="tip">' + w[1] + '</span></div>').join('') +
    '<div class="ab"><span class="term" id="t">FH<span class="term-tip">Flight hours</span></span> left</div>',
    el => (el.classList.contains('tip') || el.classList.contains('term-tip') ? { display: 'none' } : null));
  words.forEach((w, i) => { p.document.getElementById('w' + i).onclick = () => {}; });
  p.document.getElementById('t').onclick = () => {};
  const l = lines(scraped(p.A));
  for (const w of words) assert.ok(l.some(x => x.indexOf('button|' + w[0]) === 0), w[0] + ' keeps its press: ' + l.join(' / '));
  assert.ok(l.some(x => /FH \(Flight hours\) left/.test(x)), l.join(' / '));
});

test('a paragraph keeps a word that takes a press by its own pointer as a button, and reads around a hidden tip in order', () => {
  const p = styled('<button>Go</button><p class="a">Read the help first. <span class="lnk">Open help</span> for more.</p>' +
    '<p class="b">The check is due in 750 <span class="term">FH<span class="term-tip">Flight hours</span></span> or 90 days.</p>',
    el => (el.tagName !== 'SPAN' ? null : Object.assign({ display: el.classList.contains('term-tip') ? 'none' : 'inline' },
          el.classList.contains('lnk') ? { cursor: 'pointer' } : {})));
  const lnk = p.document.querySelector('.lnk');
  lnk.getBoundingClientRect = () => ({ top: 30, left: 0, right: 60, bottom: 50, width: 60, height: 20 });
  const term = p.document.querySelector('.term');
  term.getBoundingClientRect = () => ({ top: 30, left: 0, right: 20, bottom: 50, width: 20, height: 20 });
  const l = lines(scraped(p.A));
  assert.ok(!l.includes('static|Read the help first. Open help for more.'), 'the link is no plain word: ' + l.join(' / '));
  assert.ok(l.includes('button|Open help'), 'and it is a button: ' + l.join(' / '));
  assert.ok(l.some(x => /The check is due in 750 FH \(Flight hours\) or 90 days\./.test(x)), l.join(' / '));
});

// A word with a hidden tip, inside running text, so each abbreviation rule is reached on its own (a
// word standing alone is refused earlier, by the running-text rule).
function inText(word, tip, wrap) {
  const w = '<span class="w" data-rect="0,40,80,20">' + word + '<span class="tip">' + tip + '</span></span>';
  const p = styled('<div class="t">Then ' + (wrap ? '<span class="chip">' + w + '</span>' : w) + ' it</div>',
    el => (el.classList.contains('tip') ? { display: 'none' } : null));
  p.document.querySelector('.w').onclick = () => {};
  return lines(scraped(p.A));
}

test('in running text, a control word whose tip is its own word, inflected, keeps its press', () => {
  for (const [w, t] of [['LOAD', 'Loading the plan'], ['ARM', 'Arms the autobrake'], ['SAVE', 'Saving the plan'],
                        ['STOP', 'Stopped by the pilot'], ['RUN', 'Running the check'], ['SET', 'Setting the speed'],
                        ['SAVE', 'Saves the plan'], ['SAVE', 'Save: store the plan'], ['SAVE', 'Save the plan'], ['SAVE', 'Save'], ['ON', 'Turn on']]) {
    const l = inText(w, t);
    assert.ok(l.includes('button|' + w), w + ' "' + t + '" keeps its press: ' + l.join(' / '));
  }
});

test('in running text, an abbreviation reads spelled out, a tip restating it first included, and through a chip around it', () => {
  for (const [w, t, want] of [['HDG', '(Heading)', 'Then HDG (Heading) it'], ['FH', 'FH: Flight hours', 'Then FH (FH: Flight hours) it'],
                              ['CYC', 'Cycles', 'Then CYC (Cycles) it'], ['MIN', 'Minimum', 'Then MIN (Minimum) it']]) {
    const l = inText(w, t);
    assert.ok(l.includes('static|' + want), w + ': ' + l.join(' / '));
  }
  const chip = inText('FH', 'Flight hours', true);
  assert.ok(chip.includes('static|Then FH (Flight hours) it'), 'a wrapper holding only the word is no running text of its own: ' + chip.join(' / '));
});

test('a one-line row keeps a pointer word sharing its line as a button; a nameless pointer icon does not split it', () => {
  const ptr = el => el.classList && el.classList.contains('p');
  const named = page('<div class="r" data-rect="0,0,200,20">Signed in as <span class="p" data-rect="0,100,140,20">Pilot</span></div>',
    { pointer: ptr });
  const l = lines(scraped(named.A));
  assert.ok(l.includes('button|Pilot'), l.join(' / '));
  assert.ok(!l.includes('static|Signed in as Pilot'), l.join(' / '));
  const icon = page('<div class="r" data-rect="0,0,200,20"><span data-rect="0,0,40,20">Fuel</span><span data-rect="0,40,90,20">1200 kg</span>' +
    '<svg class="p" data-rect="0,90,110,20"></svg></div>', { pointer: ptr });
  const li = lines(scraped(icon.A));
  assert.ok(li.includes('static|Fuel 1200 kg'), li.join(' / '));
});

test('a line that falls through keeps its words and its pointer word in their order', () => {
  const p = page('<div class="r">Signed in as <span class="p" data-rect="30,100,140,50">Pilot</span>, member since 2024</div>',
    { pointer: el => el.classList && el.classList.contains('p') });
  const l = lines(scraped(p.A));
  const at = s => l.indexOf(s);
  assert.ok(at('static|Signed in as') >= 0 && at('button|Pilot') > at('static|Signed in as') &&
            at('static|, member since 2024') > at('button|Pilot'), l.join(' / '));
});

test('a caption over a pointer value keeps the value\'s press', () => {
  const p = page('<div class="kv"><div data-rect="0,0,40,20">Gate</div><div class="p" data-rect="30,0,40,50">A12</div></div>',
    { pointer: el => el.classList && el.classList.contains('p') });
  const l = lines(scraped(p.A));
  assert.ok(l.includes('button|A12'), l.join(' / '));
  assert.ok(!l.includes('static|Gate: A12'), l.join(' / '));
});

test('two rows under words of the element\'s own never join and drop those words', () => {
  const p = page('<div class="kv">Signed in<div data-rect="0,0,40,20">Pilot</div><div data-rect="30,0,40,50">2024</div></div>');
  const l = lines(scraped(p.A));
  assert.ok(!l.includes('static|Pilot: 2024'), l.join(' / '));
  assert.deepStrictEqual(l.slice(0, 3), ['static|Signed in', 'static|Pilot', 'static|2024']);
});

test('an abbreviation with its own pointer in a paragraph reads in place', () => {
  const p = styled('<p class="b">Due in 750 <span class="term">FH<span class="term-tip">Flight hours</span></span> or 90 days.</p>',
    el => (el.tagName !== 'SPAN' ? null : Object.assign({ display: el.classList.contains('term-tip') ? 'none' : 'inline' },
          el.classList.contains('term') ? { cursor: 'pointer' } : {})));
  p.document.querySelector('.term').getBoundingClientRect = () => ({ top: 30, left: 0, right: 20, bottom: 50, width: 20, height: 20 });
  const l = lines(scraped(p.A));
  assert.ok(l.includes('static|Due in 750 FH (Flight hours) or 90 days.'), l.join(' / '));
});

test('repeated buttons: a row names a repeat only by words on its own line, never by a section around it', () => {
  // TechLog's Flights page: the main menu's "Flights" tab and the page's own "Flights" sub-tab, whose
  // nearest words are the empty-list notes far below it.
  // Its sidebar ends in a status line, far below its tabs; the page's header sits on the sub-tabs' line.
  const p = page('<div class="nav" data-rect="0,0,200,600"><div data-rect="0,0,200,20">TechLog v1.5.4</div><button data-rect="40,0,200,60">Flights</button>' +
    '<div data-rect="560,0,200,580">ASXGS: figures written into 8 service values</div></div>' +
    '<div class="sec" data-rect="100,300,800,600"><div class="tabs" data-rect="100,300,500,120"><button data-rect="100,300,400,120">Flights</button><button data-rect="100,400,500,120">History</button></div>' +
    '<div class="empty" data-rect="160,300,800,200">No flights yet. Fly with TechLog running.</div></div>');
  const l = lines(scraped(p.A));
  assert.ok(!l.some(x => /(No flights yet|ASXGS).*: Flights/.test(x)), l.join(' / '));
  assert.deepStrictEqual(l.filter(x => /Flights$/.test(x) && x.indexOf('button|') === 0), ['button|Flights', 'button|Flights']);
});

test('GSX: the open menu\'s title bar reads as its heading and a Close button, never a button named after the question', () => {
  const head = open => '<section id="menuSurface"><div class="menu-head menu-head--action" role="button" data-rect="0,0,50,20">' +
    (open ? '<div class="m-title">Activate Services at VCBI/Bandaranaike Intl Colombo</div><div class="m-sub">Gate 6</div>'
          : '<div class="m-title">GSX Menu</div><div class="m-sub m-hint">Tap to open</div>') + '</div>' +
    (open ? '<div class="menu-list"><button>Request Deboarding</button></div>' : '') + '</section>';
  const shut = page(head(false));
  shut.document.querySelector('.menu-head').onclick = () => {};
  assert.ok(lines(scraped(shut.A)).includes('button|GSX Menu Tap to open'));
  const p = page(head(true));
  let closes = 0;
  p.document.querySelector('.menu-head').onclick = () => closes++;
  const els = scraped(p.A);
  const l = lines(els);
  assert.deepStrictEqual(l.slice(0, 3), ['heading|Activate Services at VCBI/Bandaranaike Intl Colombo Gate 6', 'button|Close the GSX menu', 'button|Request Deboarding']);
  assert.equal(p.A.clickElement(els.find(e => e.text === 'Close the GSX menu').idx), true);
  assert.equal(closes, 1, 'the Close button presses the title bar');
});

test('a page\'s own dropdown (a button over a hidden list of choices) reads as a choice and is set by opening it and pressing the choice', () => {
  // GSX Remote's settings: button.dd-btn shows the choice, div.dd-list holds the options, is-sel marks one.
  const p = styled('<div class="set-field"><div class="set-label">Automated reposition</div><div class="set-ctrl"><div class="set-dropdown">' +
    '<button class="set-select dd-btn" type="button"><span class="dd-label">Yes</span></button>' +
    '<div class="dd-list" hidden=""><div class="dd-opt">No</div><div class="dd-opt is-sel">Yes</div><div class="dd-opt">Auto</div></div></div></div></div>',
    el => (el.closest('.dd-list') && el.closest('.dd-list').hidden ? { display: 'none' } : null));
  const { document } = p;
  const btn = document.querySelector('.dd-btn'), list = document.querySelector('.dd-list');
  const did = [];
  btn.addEventListener('click', () => { did.push('open'); list.hidden = false; });
  document.querySelectorAll('.dd-opt').forEach(o => o.addEventListener('click', () => {
    did.push('pick:' + o.textContent); document.querySelector('.dd-label').textContent = o.textContent; list.hidden = true; }));
  const els = scraped(p.A);
  const l = lines(els);
  assert.ok(l.includes('/select|Automated reposition=Yes'), l.join(' / '));
  assert.ok(!l.includes('static|Automated reposition'), 'its caption is read with it: ' + l.join(' / '));
  const dd = els.find(e => e.controlType === 'select');
  assert.deepStrictEqual(dd.options, ['No', 'Yes', 'Auto']);
  assert.equal(p.A.setValue(dd.idx, 'Auto'), true);
  assert.deepStrictEqual(did, ['open', 'pick:Auto']);
  assert.equal(p.A.setValue(dd.idx, 'Never'), false, 'no such choice: nothing pressed');
  assert.equal(p.A.setValue(dd.idx, 'Auto'), true, 'already the choice');
  assert.deepStrictEqual(did, ['open', 'pick:Auto'], 'nothing pressed for a choice already made');
});

test('a lone dropdown choice reads as a dropdown only when the page marks it chosen', () => {
  const one = sel => styled('<div class="f"><div class="set-label">Invoice printer</div><div class="set-ctrl"><div class="set-dropdown">' +
    '<button class="dd-btn" type="button"><span class="dd-label">Auto</span></button><div class="dd-list" hidden=""><div class="dd-opt' + sel + '">Auto</div></div></div></div></div>',
    el => (el.closest('.dd-list') ? { display: 'none' } : null));
  assert.ok(lines(scraped(one(' is-sel').A)).includes('/select|Invoice printer=Auto'));
  assert.ok(!scraped(one('').A).some(e => e.controlType === 'select'), 'unmarked: a button and its hidden twin, no dropdown');
});

test('a button beside a list of other buttons is no dropdown', () => {
  const p = styled('<div class="bar"><button>Edit</button><div class="more" hidden=""><button>Copy</button><button>Edit</button></div></div>',
    el => (el.closest('.more') ? { display: 'none' } : null));
  assert.ok(!scraped(p.A).some(e => e.controlType === 'select'));
});

test('a label around its field reads its words once, a word after the field included', () => {
  const l = lines(scraped(page('<label>Wind speed <input type="text"> kt</label>').A));
  assert.ok(!l.includes('static|kt'), l.join(' / '));
  assert.ok(!l.includes('static|Wind speed'), l.join(' / '));
});

test('words around a child that says nothing, or around an inline word, stay one line', () => {
  const inl = el => (el.tagName === 'B' ? 'inline' : 'block');
  const a = lines(scraped(page('<div class="r">Fuel <svg></svg> 1200 kg <button>Edit</button></div>').A));
  assert.deepStrictEqual(a.slice(0, 2), ['static|Fuel 1200 kg', 'button|Edit']);
  const b = lines(scraped(page('<div class="r">Total: <b>1200</b> kg <button>Edit</button></div>', { display: inl }).A));
  assert.deepStrictEqual(b.slice(0, 2), ['static|Total: 1200 kg', 'button|Edit']);
});

test('a full stop after a link ends the line before it, never a line of its own', () => {
  const l = lines(scraped(page('<p class="t">See the <a href="#">Terms</a> and the <a href="#">Privacy</a>.</p>').A));
  assert.ok(!l.includes('static|.'), l.join(' / '));
  assert.deepStrictEqual(l.slice(0, 4), ['static|See the', 'button|Terms', 'static|and the', 'button|Privacy']);
});

test('words of its own before rows a list re-sorts by their place read first, the rows top to bottom', () => {
  const p = styled('<div class="list">Flights<div class="row" data-rect="100,0,100,120">Row B</div><div class="row" data-rect="0,0,100,20">Row A</div></div>',
    el => (el.classList.contains('row') ? { position: 'absolute' } : null));
  assert.deepStrictEqual(lines(scraped(p.A)).slice(0, 3), ['static|Flights', 'static|Row A', 'static|Row B']);
});

test('repeated buttons: a two-line card names its button by the whole card; a row longer than a line names none', () => {
  const card = f => '<div class="card"><div data-rect="0,0,60,20">DUBAI</div><div data-rect="20,0,60,40">' + f + '</div><button data-rect="40,0,60,60">SET</button></div>';
  const l = lines(scraped(page(card('118.350') + card('121.650')).A));
  assert.deepStrictEqual(l.filter(x => /SET$/.test(x)), ['button|DUBAI 118.350: SET', 'button|DUBAI 121.650: SET']);
  const long = n => '<div class="r"><span>DUBAI</span><span>' + n + ' ' + 'approach and departure control, all sectors of the terminal area east and west' + '</span><button>SET</button></div>';
  const m = lines(scraped(page(long('ONE') + long('TWO')).A));
  assert.deepStrictEqual(m.filter(x => /SET$/.test(x)), ['button|SET', 'button|SET']);
});

test('repeated buttons: a row\'s own words before the button name it', () => {
  const l = lines(scraped(page('<div class="r">Default <button>LOAD</button></div><div class="r">Rainy <button>LOAD</button></div>').A));
  assert.deepStrictEqual(l.filter(x => /LOAD$/.test(x)), ['button|Default: LOAD', 'button|Rainy: LOAD']);
});

test('a caption its field claims takes its lines with it, a row below included', () => {
  const l = lines(scraped(page('<div class="f"><div class="cap">Cruise<div data-rect="30,0,50,50">level (FL)</div></div><input type="text"></div>').A));
  assert.ok(!l.includes('static|level (FL)'), l.join(' / '));
  assert.ok(!l.includes('static|Cruise'), l.join(' / '));
});

test('an app panel over its own page (fixed, opaque, covering) is read first as a popup; a see-through layer over a map is not', () => {
  const html = '<div class="main"><button>Customize Airport</button></div>' +
    '<div class="settings-overlay"><div class="settings-panel"><div class="settings-head menu-head--action" role="button" data-rect="0,0,50,20">' +
    '<div class="m-title">GSX Settings</div><div class="m-sub m-hint">Tap to close</div></div>' +
    '<button>Simulation</button></div></div>';
  const solid = styled(html, el => (el.classList.contains('settings-overlay') ? { position: 'fixed', backgroundColor: 'rgb(20, 20, 20)' } : null));
  const l = lines(scraped(solid.A));
  assert.deepStrictEqual(l.slice(0, 3), ['alert|GSX Settings', 'button|GSX Settings Tap to close', 'button|Simulation'], l.join(' / '));
  assert.ok(l.includes('button|Customize Airport'), 'the page under it follows');
  const clear = styled(html.replace('settings-overlay', 'map-overlay'), el => (el.classList.contains('map-overlay') ? { position: 'absolute', backgroundColor: 'rgba(0, 0, 0, 0)' } : null));
  assert.ok(!lines(scraped(clear.A)).some(x => x.indexOf('alert|') === 0));
  const seeThrough = styled(html, el => (el.classList.contains('settings-overlay') ? { position: 'fixed', backgroundColor: 'rgba(0, 0, 0, 0.2)' } : null));
  assert.ok(!lines(scraped(seeThrough.A)).some(x => x.indexOf('alert|') === 0), 'a see-through layer is no page of its own');
  const inPlace = styled(html, el => (el.classList.contains('settings-overlay') ? { position: 'absolute', backgroundColor: 'rgb(20, 20, 20)' } : null));
  assert.ok(!lines(scraped(inPlace.A)).some(x => x.indexOf('alert|') === 0), 'only a layer fixed over the page');
});

test('a box the page focuses by itself after a press, late, is handed back after its own focus handler (STAB-1)', () => {
  // GSX Remote: "Search parking..." asks the server, which answers; the page then focuses its search box,
  // and the box's focus listener masks the sim's keyboard (its blur listener unmasks).
  const p = page('<button id="go">Search parking...</button><input id="q" type="text" placeholder="Search parking...">');
  const { document } = p;
  const q = document.getElementById('q');
  const log = [];
  q.addEventListener('focus', () => log.push('mask'));
  q.addEventListener('blur', () => log.push('unmask'));
  document.getElementById('go').onclick = () => {};
  const els = scraped(p.A);
  p.A.clickElement(els.find(e => e.text === 'Search parking...').idx);
  log.length = 0;
  q.focus();                                   // later, from the server's answer
  assert.deepStrictEqual(log, ['mask', 'unmask'], 'masked by the page, then handed back');
  assert.notStrictEqual(document.activeElement, q);
});

test('a box the pilot focuses with the mouse, or long after a press, is left alone', () => {
  const p = page('<button id="go">Go</button><input id="q" type="text">');
  const { window, document } = p;
  const q = document.getElementById('q');
  const log = [];
  q.addEventListener('blur', () => log.push('unmask'));
  document.getElementById('go').onclick = () => {};
  const els = scraped(p.A);
  p.A.clickElement(els.find(e => e.text === 'Go').idx);
  window.__MSFSBA_USER_POINTER_AT = Date.now(); // what a real (trusted) mouse press on the box records
  q.focus();
  assert.deepStrictEqual(log, [], 'a mouse focus stays');
  q.blur(); log.length = 0;
  window.__MSFSBA_PRESSED_AT = Date.now() - 60000;
  window.__MSFSBA_USER_POINTER_AT = 0;
  q.focus();
  assert.deepStrictEqual(log, [], 'long after a press: not ours');
});

test('the reader\'s own mousedown press is never taken for the pilot\'s mouse', () => {
  const p = page('<div id="go" data-rect="0,0,40,20">Search</div><input id="q" type="text">');
  const { document } = p;
  const q = document.getElementById('q');
  const log = [];
  q.addEventListener('blur', () => log.push('unmask'));
  document.getElementById('go').onmousedown = () => {};
  const els = scraped(p.A);
  p.A.clickElement(els.find(e => e.text === 'Search').idx);
  log.length = 0;
  q.focus();
  assert.deepStrictEqual(log, ['unmask']);
});

test('a button over a card, panel or hover card that repeats its words stays a button; ARIA alone makes a dropdown', () => {
  const hid = el => (el.closest('[hidden]') ? { display: 'none' } : null);
  const shapes = ['<div class="w"><button>Refuel</button><div hidden=""><div>Refuel</div><div>Requests the fuel truck</div></div></div>',
                  '<div class="w"><button>KSEA</button><ul hidden=""><li>KSEA</li><li>Seattle-Tacoma Intl</li></ul></div>'];
  for (const sh of shapes) {
    const l = lines(scraped(styled(sh, hid).A));
    assert.ok(!l.some(x => x.indexOf('/select|') === 0), l.join(' / '));
    assert.ok(l.some(x => x.indexOf('button|') === 0), l.join(' / '));
  }
  const aria = lines(scraped(styled('<div class="f"><span class="lbl">Units</span><div class="w"><button aria-expanded="false">KG</button>' +
    '<div class="opts" hidden=""><div>KG</div><div>LBS</div></div></div></div>', hid).A));
  assert.ok(aria.some(x => /^\/select\|.*=KG$/.test(x)), aria.join(' / '));
});

test('an open dropdown is not pressed shut before its choice is pressed', () => {
  const p = styled('<div class="f"><div class="set-label">Mode</div><div class="set-ctrl"><div class="set-dropdown">' +
    '<button class="dd-btn" type="button"><span class="dd-label">Yes</span></button>' +
    '<div class="dd-list"><div class="dd-opt">No</div><div class="dd-opt is-sel">Yes</div></div></div></div></div>', () => null);
  const did = [];
  p.document.querySelector('.dd-btn').addEventListener('click', () => did.push('toggle'));
  p.document.querySelectorAll('.dd-opt').forEach(o => o.addEventListener('click', () => did.push('pick:' + o.textContent)));
  const dd = scraped(p.A).find(e => e.controlType === 'select');
  assert.equal(p.A.setValue(dd.idx, 'No'), true);
  assert.deepStrictEqual(did, ['pick:No']);
});

test('a block of text a field took as its name is still read; a field\'s own row words name it once', () => {
  const t = lines(scraped(page('<div class="log"><div data-rect="0,0,90,20">Tower: cleared to land 16L</div><div data-rect="20,0,90,40">You: cleared to land</div>' +
    '<div data-rect="40,0,90,60">Tower: wind 160 at 8</div></div><input type="text" placeholder="Message ATC...">').A));
  for (const w of ['static|Tower: cleared to land 16L', 'static|You: cleared to land', 'static|Tower: wind 160 at 8']) assert.ok(t.includes(w), t.join(' / '));
  const r = styled('<div class="r">Automated reposition <div class="set-dropdown"><button class="dd-btn" type="button">Yes</button>' +
    '<div class="dd-list" hidden=""><div class="dd-opt">No</div><div class="dd-opt is-sel">Yes</div></div></div></div>',
    el => (el.closest('[hidden]') ? { display: 'none' } : null));
  const rl = lines(scraped(r.A));
  assert.ok(rl.includes('/select|Automated reposition=Yes'), rl.join(' / '));
  assert.ok(!rl.includes('static|Automated reposition'), rl.join(' / '));
});

test('an inline word joins the words around it with the page\'s own spacing', () => {
  const inl = el => (el.tagName === 'B' || el.tagName === 'SPAN' ? 'inline' : el.tagName === 'I' ? 'inline-block' : 'block');
  const l = lines(scraped(page('<div class="a">Fly to <b>Lon</b>don <button>Go</button></div><div class="b">Price <span>$</span>1200 <button>Buy</button></div>' +
    '<div class="c">750 <i>FH</i> left <button>Log</button></div><div class="d">Total<i>1200</i>kg <button>Edit</button></div>', { display: inl }).A));
  for (const w of ['static|Fly to London', 'static|Price $1200', 'static|750 FH left', 'static|Total 1200 kg']) assert.ok(l.includes(w), l.join(' / '));
});

test('a label around its field keeps its read-out; a full stop after a line of text ends that line', () => {
  const l = lines(scraped(page('<label>Volume <input type="range" min="0" max="100" value="80"><b data-rect="0,60,90,20">80%</b></label>').A));
  assert.ok(l.includes('static|80%'), l.join(' / '));
  const f = lines(scraped(page('<div class="n">See <div data-rect="20,0,90,40">the notes</div>.<button>Go</button></div>').A));
  assert.ok(f.includes('static|the notes.'), f.join(' / '));
});

test('a frame that loads a document of its own after a read (GSX\'s price lists) is read when it loads', () => {
  const p = page('<button>Back</button><iframe class="menu-document"></iframe>');
  const { window, document } = p;
  const f = document.querySelector('iframe');
  f.contentWindow.Element.prototype.getBoundingClientRect = window.Element.prototype.getBoundingClientRect;   // boxes as on the page
  const first = JSON.parse(p.A.scrape());
  assert.ok(first.elements.length >= 1);
  const fd = f.contentDocument;
  const body = fd.createElement('body');
  body.innerHTML = '<h1>CATERING PRICE LIST</h1>';
  fd.documentElement.replaceChild(body, fd.body);        // a new document's body: nothing observed changed
  const unchanged = JSON.parse(p.A.scrape());
  assert.ok(!(unchanged.elements || []).some(e => /CATERING/.test(e.text)), 'nothing told the reader yet');
  f.dispatchEvent(new window.Event('load'));
  const after = JSON.parse(p.A.scrape());
  assert.ok(after.elements.some(e => e.text === 'CATERING PRICE LIST'), JSON.stringify(after).slice(0, 300));
});

test('a two-cell table row reads as its name and value; a caption ending in a colon gets no second one; a value over its conversion takes a comma', () => {
  const p = page('<table><tbody><tr><td data-rect="0,0,40,20">ICAO:</td><td data-rect="0,40,90,20">VCBI</td></tr>' +
    '<tr><td data-rect="20,0,40,40">Boarding fee</td><td data-rect="20,40,90,40"><div data-rect="20,40,90,30">Rs 2,498.10/pax</div><div data-rect="30,40,90,40">~$7.50/pax</div></td></tr></tbody></table>' +
    '<table><thead><tr><th>Level</th><th>Mean EDR</th><th>Peak</th></tr></thead><tbody><tr><td data-rect="60,0,30,80">FL350</td><td data-rect="60,30,60,80">0.12</td><td data-rect="60,60,90,80">0.30</td></tr>' +
    '<tr><td data-rect="80,0,30,100">FL390</td><td data-rect="80,30,60,100">0.08</td><td data-rect="80,60,90,100">0.20</td></tr>' +
    '<tr><td colspan="2" data-rect="100,0,60,120">Average</td><td data-rect="100,60,90,120">0.25</td></tr></tbody></table>' +
    '<table><thead><tr><th data-rect="200,0,40,220">Service</th><th data-rect="200,40,90,220">Price</th></tr></thead><tbody>' +
    '<tr><td data-rect="220,0,40,240">De-icing</td><td data-rect="220,40,90,240"><div data-rect="220,40,90,230">Rs 4,673,951.76</div><div data-rect="230,40,90,240">~$14,032.52</div></td></tr>' +
    '<tr><td data-rect="240,0,40,260">GPU</td><td data-rect="240,40,90,260">Rs 16,654.00</td></tr></tbody></table>' +
    '<div class="party"><div data-rect="100,0,90,120">A. PROVIDER:</div><div data-rect="120,0,90,140">SriLankan Airlines</div></div>' +
    '<div class="amt"><div data-rect="150,0,90,170">Rs 6,661.60</div><div data-rect="170,0,90,190">~$20.00</div></div>');
  const l = lines(scraped(p.A));
  for (const w of ['static|ICAO: VCBI', 'static|Boarding fee: Rs 2,498.10/pax ~$7.50/pax', 'static|A. PROVIDER: SriLankan Airlines', 'static|Rs 6,661.60, ~$20.00'])
    assert.ok(l.includes(w), w + ' in ' + l.join(' / '));
  assert.ok(l.includes('static|FL350 0.12 0.30') && l.includes('static|FL390 0.08 0.20') && l.includes('static|Average 0.25'),
    'a wider data table keeps its own reading, cells apart, a spanning row too: ' + l.join(' / '));
  for (const w of ['static|Service Price', 'static|De-icing: Rs 4,673,951.76 ~$14,032.52', 'static|GPU: Rs 16,654.00'])
    assert.ok(l.includes(w), w + ' in ' + l.join(' / '));
});

test('a late-focus hand-back never outlives the focus it was for, and never loops with a page that keeps its box focused', () => {
  const p = page('<button id="go">Go</button><div id="wrap"><input id="q" type="text"></div>');
  const { window, document } = p;
  const q = document.getElementById('q');
  const log = [];
  q.addEventListener('blur', () => log.push('blur'));
  document.getElementById('go').onclick = () => {};
  const els = scraped(p.A);
  p.A.clickElement(els.find(e => e.text === 'Go').idx);
  let stop = true;
  document.getElementById('wrap').addEventListener('focus', ev => { if (stop) ev.stopPropagation(); }, true);
  q.focus();                                   // late, but the page stops it before the box
  stop = false; q.blur(); log.length = 0;
  window.__MSFSBA_PRESSED_AT = Date.now() - 600000;
  q.focus();                                   // ten minutes later, someone else's focus
  assert.deepStrictEqual(log, [], 'a stale hand-back stays out of it');
  const k = page('<button id="go">Go</button><input id="q" type="text">');
  const kq = k.document.getElementById('q');
  let focuses = 0;
  kq.addEventListener('focus', () => focuses++);
  kq.addEventListener('blur', () => { if (focuses < 20) kq.focus(); });   // a page that keeps its box focused
  k.document.getElementById('go').onclick = () => {};
  k.A.clickElement(scraped(k.A).find(e => e.text === 'Go').idx);
  kq.focus();
  assert.ok(focuses <= 2, 'one hand-back per press, then the page keeps its box: ' + focuses);
});

test('a pointer or key press outside the reader\'s own presses counts as the pilot\'s, trusted or not; only text boxes are handed back', () => {
  for (const ev of ['mousedown', 'keydown']) {
    const p = page('<button id="go">Go</button><input id="q" type="text">');
    const { window, document } = p;
    const q = document.getElementById('q');
    const log = [];
    q.addEventListener('blur', () => log.push('blur'));
    document.getElementById('go').onclick = () => {};
    p.A.clickElement(scraped(p.A).find(e => e.text === 'Go').idx);
    q.dispatchEvent(new window.Event(ev, { bubbles: true }));
    q.focus();
    assert.deepStrictEqual(log, [], ev + ' before the focus: the pilot\'s own');
  }
  const c = page('<button id="go">Go</button><input id="c" type="checkbox">');
  const cb = c.document.getElementById('c');
  const clog = [];
  cb.addEventListener('blur', () => clog.push('blur'));
  c.document.getElementById('go').onclick = () => {};
  c.A.clickElement(scraped(c.A).find(e => e.text === 'Go').idx);
  cb.focus();
  assert.deepStrictEqual(clog, [], 'a tick box takes no keyboard');
});

test('tables: a name cell may be a header cell; a value cell holding headings or lists keeps them; a row holding a control keeps its press; one cell never splits a word', () => {
  const inl = el => (/^(SPAN|B|I|U|S|EM)$/.test(el.tagName) ? 'inline' : 'block');
  const p = page('<table><tbody><tr><th data-rect="0,0,40,20">ICAO</th><td data-rect="0,40,90,20">VCBI</td></tr>' +
    '<tr><td data-rect="20,0,40,40">Fuel</td><td data-rect="20,40,90,40"><button>Edit</button></td></tr>' +
    '<tr><td data-rect="40,0,40,140">Notes</td><td data-rect="40,40,90,140"><h3 data-rect="40,40,90,60">Before you start</h3><p data-rect="60,40,90,80">Check the doors.</p>' +
    '<ul data-rect="80,40,90,140"><li data-rect="80,40,90,100">Door 1 closed</li><li data-rect="100,40,90,120">Door 2 closed</li></ul></td></tr></tbody></table>' +
    '<table><tbody><tr><td><span>Lon</span><b><i><u><s><em>don</em></s></u></i></b></td></tr></tbody></table>', { display: inl });
  const l = lines(scraped(p.A));
  for (const w of ['static|ICAO: VCBI', 'button|Edit', 'heading|Before you start', 'static|Door 1 closed']) assert.ok(l.includes(w), w + ' in ' + l.join(' / '));
  assert.ok(l.includes('static|London'), l.join(' / '));
});

test('an app\'s whole page fixed in place is its page, not a popup over one', () => {
  const p = styled('<div class="app-overlay"><h1>Tow planner</h1><button>Plan</button></div>',
    el => (el.classList.contains('app-overlay') ? { position: 'fixed', backgroundColor: 'rgb(0, 0, 0)' } : null));
  const l = lines(scraped(p.A));
  assert.ok(!l.some(x => x.indexOf('alert|') === 0), l.join(' / '));
  assert.ok(l.includes('heading|Tow planner'), l.join(' / '));
});

test('only input that chose the box keeps a late focus: a page\'s own click elsewhere or a stray key does not', () => {
  const run = (setup) => {
    const p = page('<button id="go">Go</button><label id="lb" for="q">Search</label><input id="q" type="text"><div id="map">Map</div>');
    const { window, document } = p;
    const q = document.getElementById('q');
    const log = [];
    q.addEventListener('blur', () => log.push('blur'));
    document.getElementById('go').onclick = () => {};
    p.A.clickElement(scraped(p.A).find(e => e.text === 'Go').idx);
    setup(p);
    q.focus();
    return log;
  };
  assert.deepStrictEqual(run(p => p.document.getElementById('map').dispatchEvent(new p.window.Event('mousedown', { bubbles: true }))), ['blur'],
    'a page\'s own mousedown elsewhere: handed back');
  assert.deepStrictEqual(run(p => p.document.body.dispatchEvent(new p.window.Event('keydown', { bubbles: true }))), ['blur'],
    'a key with no box focused: handed back');
  assert.deepStrictEqual(run(p => p.document.getElementById('lb').dispatchEvent(new p.window.Event('mousedown', { bubbles: true }))), [],
    'a press on the box\'s label: the pilot chose it');
});

test('the reader\'s own presses and sets are marked as the reader\'s while they run, and never after', () => {
  const p = styled('<div id="go" data-rect="0,0,40,20">Search</div><div class="f"><div class="set-label">Mode</div><div class="set-ctrl"><div class="set-dropdown">' +
    '<button class="dd-btn" type="button">Yes</button><div class="dd-list" hidden=""><div class="dd-opt">No</div><div class="dd-opt is-sel">Yes</div></div></div></div></div>',
    el => (el.closest('[hidden]') ? { display: 'none' } : null));
  const seen = [];
  p.document.getElementById('go').onmousedown = () => seen.push('press:' + p.A._pressing);
  p.document.querySelector('.dd-btn').addEventListener('click', () => seen.push('set:' + p.A._pressing));
  const els = scraped(p.A);
  p.A.clickElement(els.find(e => e.text === 'Search').idx);
  p.A.setValue(els.find(e => e.controlType === 'select').idx, 'No');
  assert.deepStrictEqual(seen, ['press:true', 'set:true']);
  assert.equal(p.A._pressing, false);
  // A slider's drag is the reader's too (it presses nothing through A.press).
  const sl = osSlider({ readout: 'sync' });
  const drags = [];
  sl.document.querySelector('.slider-bar').addEventListener('mousedown', () => drags.push(sl.A._pressing));
  const box = scraped(sl.A).find(e => /Haze strength/.test(e.text));
  assert.equal(sl.A.setValue(box.idx, '120'), true);
  assert.ok(drags.length > 0 && drags.every(Boolean), JSON.stringify(drags));
});

test('a definition term over its value is a caption and its value; an overlay counts only shown controls under it', () => {
  const l = lines(scraped(page('<dl><dt data-rect="0,0,40,20">Gate</dt><dd data-rect="20,0,40,40">A12</dd></dl>').A));
  assert.ok(l.includes('static|Gate: A12'), l.join(' / '));
  const o = styled('<div class="toast" hidden=""><button>Undo</button></div><div class="settings-overlay"><div class="m-title">Settings</div><button>Close</button></div>',
    el => (el.classList.contains('settings-overlay') ? { position: 'fixed', backgroundColor: 'rgb(0, 0, 0)' } : el.closest('[hidden]') ? { display: 'none' } : null));
  assert.ok(!lines(scraped(o.A)).some(x => x.indexOf('alert|') === 0), 'only a hidden toast under it: no page under it');
});

test('a late focus is handed back however long the page\'s own focus listener takes, and a press on the page\'s background chose nothing', () => {
  const run = (setup, slow) => {
    const p = page('<button id="go">Go</button><div class="field" id="fw"><input id="q" type="text"></div>');
    const { window, document } = p;
    const q = document.getElementById('q');
    const log = [];
    if (slow) q.addEventListener('focus', () => { const t0 = Date.now(); while (Date.now() - t0 < 80) { /* a busy frame */ } });
    q.addEventListener('blur', () => log.push('blur'));
    document.getElementById('go').onclick = () => {};
    p.A.clickElement(scraped(p.A).find(e => e.text === 'Go').idx);
    if (setup) setup(p);
    q.focus();
    return log;
  };
  assert.deepStrictEqual(run(null, true), ['blur'], 'an 80 ms listener: still handed back');
  const md = (p, el) => el.dispatchEvent(new p.window.Event('mousedown', { bubbles: true }));
  assert.deepStrictEqual(run(p => md(p, p.document.body)), ['blur'], 'a press on the body chose nothing');
  assert.deepStrictEqual(run(p => md(p, p.document)), ['blur'], 'nor one on the document');
  assert.deepStrictEqual(run(p => md(p, p.document.getElementById('fw'))), [], 'a press on the box\'s own field wrapper chose it');
  assert.deepStrictEqual(run(p => { p.window.__MSFSBA_SAW_TRUSTED = true; md(p, p.document.getElementById('q')); }), ['blur'],
    'once real input has been seen, an untrusted press on the box is the page\'s own');
  // An add-on's own page: the box straight in its body.
  const f = page('<button id="go">Go</button>');
  const fq = f.document.createElement('input');
  fq.type = 'text';
  f.document.body.appendChild(fq);
  const flog = [];
  fq.addEventListener('blur', () => flog.push('blur'));
  f.document.getElementById('go').onclick = () => {};
  f.A.clickElement(scraped(f.A).find(e => e.text === 'Go').idx);
  md(f, f.document.body);
  fq.focus();
  assert.deepStrictEqual(flog, ['blur'], 'the body is never the box\'s field');
});

test('a press on a section far above the box chose nothing; real input seen survives a reinstall', () => {
  const p = page('<button id="go">Go</button><div id="sec"><div><div><div class="field"><input id="q" type="text"></div></div></div></div>');
  const { window, document } = p;
  const q = document.getElementById('q');
  const log = [];
  q.addEventListener('blur', () => log.push('blur'));
  document.getElementById('go').onclick = () => {};
  p.A.clickElement(scraped(p.A).find(e => e.text === 'Go').idx);
  document.getElementById('sec').dispatchEvent(new window.Event('mousedown', { bubbles: true }));
  q.focus();
  assert.deepStrictEqual(log, ['blur'], 'four levels up is a section, not the box\'s field');
  window.__MSFSBA_SAW_TRUSTED = true;
  window.eval(require('fs').readFileSync(require('path').join(__dirname, '..', '..', 'MSFSBlindAssist', 'Resources', 'coherent-sim-tablet-agent.js'), 'utf8'));
  const A2 = window.__MSFSBA_SIM_TABLET;
  q.blur(); log.length = 0;
  document.getElementById('go').onclick = () => {};
  A2.clickElement(JSON.parse(A2.scrape()).elements.find(e => e.text === 'Go').idx);
  q.dispatchEvent(new window.Event('mousedown', { bubbles: true }));
  q.focus();
  assert.deepStrictEqual(log, ['blur'], 'after a reinstall, the page\'s own press on the box still is not the pilot\'s');
});

test('a first line that ends a sentence joins the next with a space, never a colon', () => {
  const l = lines(scraped(page('<div class="r"><div data-rect="0,0,90,20">With the mod: 60 fps. Without: 65 fps.</div><div data-rect="20,0,90,40">The view changed while measuring</div></div>').A));
  assert.ok(l.includes('static|With the mod: 60 fps. Without: 65 fps. The view changed while measuring'), l.join(' / '));
});

test('a lone small i is the information button', () => {
  const p = page('<div id="i" data-rect="0,0,20,20">i</div><div id="I" data-rect="0,30,50,20">I</div>');
  p.document.getElementById('i').onclick = () => {};
  p.document.getElementById('I').onclick = () => {};
  const l = lines(scraped(p.A));
  assert.ok(l.includes('button|Information'), l.join(' / '));
  assert.ok(l.includes('button|I'), 'a capital I is a key or a roman one, left as it is: ' + l.join(' / '));
});

test('two values with units over each other read as a pair; a unit selector says it is the unit; date and time parts say which part', () => {
  const p = page('<div class="w"><div data-rect="0,0,90,20">Estimated takeoff weight 140272 KG</div><div data-rect="20,0,90,40">Block fuel 5 KG</div></div>' +
    '<div class="g"><div data-rect="50,0,90,70">Ground Speed</div><div data-rect="70,0,90,90">0 KT</div></div>');
  const l = lines(scraped(p.A));
  assert.ok(l.includes('static|Estimated takeoff weight 140272 KG, Block fuel 5 KG'), l.join(' / '));
  assert.ok(l.includes('static|Ground Speed: 0 KT'), l.join(' / '));
  const sel = (o, v) => '<select>' + o.map(x => '<option' + (x === v ? ' selected' : '') + '>' + x + '</option>').join('') + '</select>';
  // The tablet's own dropdowns, as the Flight Planner draws its Flight date and Off-block time.
  const osd = (o, v) => '<div class="dropdown-button"><button class="dropdown-button-selector"><span class="text">' + v + '</span></button>' +
    '<div class="dropdown-button-list">' + o.map(x => '<div class="dropdown-button-list-item">' + x + '</div>').join('') + '</div></div>';
  const months = ['January', 'February', 'March'], days = ['01', '02', '15', '30'], years = ['2024', '2025', '2026'];
  const q = page('<div class="r"><label>Cruise altitude</label>' + sel(['FL', 'FT'], 'FL') + '<input type="text" value="450"></div>' +
    '<div class="r"><span>Flight date</span>' + osd(months, 'March') + osd(days, '15') + osd(years, '2026') + '</div>' +
    '<div class="r"><span>Off-block time</span>' + osd(['01', '02', '12'], '01') + osd(['00', '05', '55'], '00') + osd(['am', 'pm'], 'am') + '</div>');
  const ql = lines(scraped(q.A));
  for (const w of ['/select|Cruise altitude unit=FL', '/select|Flight date month=March', '/select|Flight date day=15', '/select|Flight date year=2026',
                   '/select|Off-block time hour=01', '/select|Off-block time minutes=00', '/select|Off-block time am or pm=am'])
    assert.ok(ql.includes(w), w + ' in ' + ql.join(' / '));
});

test('a button whose tooltip goes away keeps its name, never its icon\'s file name', () => {
  const p = page('<button id="b" title="Display size" data-rect="0,0,30,20"><icon-element icon-url="coui://html_ui/efb_ui/efb_os/Assets/icons/NoMargin/Tailless_Line_Arrow_Right_1.svg"></icon-element></button>');
  const b = p.document.getElementById('b');
  assert.ok(lines(scraped(p.A)).includes('button|Display size'));
  b.removeAttribute('title');                     // its picker opened: the OS drops the tooltip
  b.querySelector('icon-element').innerHTML = '<svg data-icon="Tailless_Line_Arrow_Right_1"></svg>';   // and shows an arrow, as live
  const l = lines(scraped(p.A));
  assert.ok(l.includes('button|Display size'), l.join(' / '));
  assert.ok(!l.some(x => /Tailless/.test(x)), l.join(' / '));
});

test('date and time parts are named only when they make one date or one time; single letters are no units; a swapped icon drops its old name; a heading never joins', () => {
  const osd = (o, v) => '<div class="dropdown-button"><button class="dropdown-button-selector"><span class="text">' + v + '</span></button>' +
    '<div class="dropdown-button-list">' + o.map(x => '<div class="dropdown-button-list-item">' + x + '</div>').join('') + '</div></div>';
  const n = (a, b) => { const r = []; for (let i = a; i <= b; i++) r.push(String(i)); return r; };
  const p = page('<div class="r"><span>Flight date</span>' + osd(n(1, 12), '11') + osd(n(1, 31), '16') + osd(['2025', '2026'], '2026') + '</div>' +
    '<div class="r"><span>Departure</span>' + osd(n(1, 12), '9') + osd(['00', '15', '30', '45'], '30') + osd(['am', 'pm'], 'pm') + '</div>' +
    '<div class="r"><span>Setting</span>' + osd(['0', '15', '30', '45'], '15') + osd(n(0, 30), '5') + '</div>' +
    '<div class="r"><span>Wake category</span>' + osd(['L', 'M'], 'M') + '</div>');
  const l = lines(scraped(p.A));
  for (const w of ['/select|Flight date month=11', '/select|Flight date day=16', '/select|Flight date year=2026',
                   '/select|Departure hour=9', '/select|Departure minutes=30', '/select|Departure am or pm=pm', '/select|Wake category=M'])
    assert.ok(l.includes(w), w + ' in ' + l.join(' / '));
  assert.ok(!l.some(x => /Setting (minutes|day)/.test(x)), 'flaps beside a delay are no date: ' + l.join(' / '));
  const t = page('<button id="b" title="Pause" data-rect="0,0,30,20"><icon-element icon-url="coui://x/Pause.svg"></icon-element></button>');
  const b = t.document.getElementById('b');
  assert.ok(lines(scraped(t.A)).includes('button|Pause'));
  b.removeAttribute('title');
  b.querySelector('icon-element').setAttribute('icon-url', 'coui://x/Play.svg');
  assert.ok(!lines(scraped(t.A)).includes('button|Pause'), 'its icon changed: the old name went with it');
  const x = page('<button id="s" title="Search" data-rect="0,0,30,20"><icon-element icon-url="coui://x/Search.svg"><svg data-icon="search"></svg></icon-element></button>');
  const sb = x.document.getElementById('s');
  assert.ok(lines(scraped(x.A)).includes('button|Search'));
  sb.removeAttribute('title');
  sb.querySelector('icon-element').innerHTML = '<svg data-icon="close"></svg>';
  assert.ok(!lines(scraped(x.A)).includes('button|Search'), 'a close X is another action, never the old name');
  const h = page('<div class="c"><h3 data-rect="0,0,90,20">Need help?</h3><p data-rect="20,0,90,40">Contact support from the menu.</p></div>');
  const hl = lines(scraped(h.A));
  assert.ok(hl.includes('heading|Need help?'), hl.join(' / '));
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
  const SHOW = 'Show the tablet in the sim (some apps and lists update only while the sim shows it)', HIDE = 'Hide the tablet in the sim';
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
