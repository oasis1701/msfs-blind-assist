'use strict';
// The layer's core: text folding, the button helper, the per-rule safety net, the observer, the keys.
// Runs on two small HAND-MADE fixtures (fixtures/core.html, fixtures/unrelated.html), not live
// captures; the real-page checks (navigation.test.js, Second_pass_changes_nothing) follow the captures.
const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs');
const path = require('path');
const { JSDOM } = require('jsdom');
const { computeAccessibleName } = require('dom-accessibility-api');
const { load, loadCounting, press, turns, LAYER, TITLES } = require('./load');

const tilesOf = window => [...window.document.querySelectorAll('#appContainer > div.appIcon')];

// Errors the page would see: an exception thrown out of a timer or an observer callback.
function collectErrors(window) {
  const errors = [];
  window.addEventListener('error', e => errors.push(e.message));
  return errors;
}

// A hand-made guess at a switcher: one clickable parent per img.app-img. Part 4b checks the rules
// against the real capture.
function addSwitcher(window, count, { sharedParent = false } = {}) {
  const doc = window.document;
  const switcher = doc.createElement('div');
  switcher.className = 'app-switcher';
  for (let i = 0; i < count; i++) {
    const img = doc.createElement('img');
    img.className = 'app-img';
    if (sharedParent) {
      switcher.appendChild(img);
    } else {
      const item = doc.createElement('div');
      item.className = 'switcher-item';
      item.appendChild(img);
      switcher.appendChild(item);
    }
  }
  doc.body.appendChild(switcher);
  return switcher;
}

test('textOf_folds_br_and_block_boundaries_into_single_spaces', async () => {
  const window = await load('core');
  try {
    const { textOf } = window.__msfsbaFenixEfb;
    const make = html => {
      const el = window.document.createElement('div');
      el.innerHTML = html;
      return el;
    };
    assert.strictEqual(textOf(make('Resend<br>Loadsheet')), 'Resend Loadsheet');
    assert.strictEqual(textOf(make('6<br>DRY')), '6 DRY');
    assert.strictEqual(textOf(make('<div>Passengers</div><div>Planned</div>')), 'Passengers Planned');
    assert.strictEqual(textOf(make('  Max \n\t 180  ')), 'Max 180');
    // Inline elements add no space of their own.
    assert.strictEqual(textOf(make('<b>Pas</b>sengers')), 'Passengers');
    // The page's own hidden content is not part of the name.
    assert.strictEqual(textOf(make('Fenix<span aria-hidden="true">icon</span>')), 'Fenix');
    assert.strictEqual(textOf(make('')), '');
  } finally {
    window.close();
  }
});

test('Home_tiles_and_the_home_button_become_named_buttons', async () => {
  const window = await load('core');
  try {
    const tiles = tilesOf(window);
    assert.strictEqual(tiles.length, 3);
    ['Fenix', 'Takeoff', 'Landing'].forEach((title, i) => {
      assert.strictEqual(tiles[i].getAttribute('role'), 'button');
      assert.strictEqual(tiles[i].tabIndex, 0);
      assert.strictEqual(tiles[i].getAttribute('data-msfsba'), '1');
      assert.strictEqual(computeAccessibleName(tiles[i]), title);
    });
    const home = window.document.getElementById('homeButtonFree');
    assert.strictEqual(home.getAttribute('role'), 'button');
    assert.strictEqual(home.tabIndex, 0);
    assert.strictEqual(home.getAttribute('data-msfsba'), '1');
    assert.strictEqual(computeAccessibleName(home), 'Home');
  } finally {
    window.close();
  }
});

test('Late_elements_are_fixed', async () => {
  const window = await load('core');
  try {
    const doc = window.document;
    const late = doc.createElement('div');
    late.className = 'appIcon';
    late.innerHTML = '<img class="app-img"><br><span class="appTitle">Test</span>';
    doc.getElementById('appContainer').appendChild(late);
    await turns();
    assert.strictEqual(late.getAttribute('role'), 'button');
    assert.strictEqual(late.tabIndex, 0);
    assert.strictEqual(late.getAttribute('data-msfsba'), '1');
    assert.strictEqual(computeAccessibleName(late), 'Test');

    // An element that only becomes a tile later (the page adds the class) is fixed too.
    const plain = doc.createElement('div');
    plain.innerHTML = '<span class="appTitle">Later</span>';
    doc.getElementById('appContainer').appendChild(plain);
    await turns();
    assert.strictEqual(plain.getAttribute('role'), null);
    plain.className = 'appIcon';
    await turns();
    assert.strictEqual(plain.getAttribute('role'), 'button');
    assert.strictEqual(computeAccessibleName(plain), 'Later');
  } finally {
    window.close();
  }
});

test('A_layer_injected_before_the_body_exists_starts_when_the_body_does', async () => {
  // The app registers the layer with AddScriptToExecuteOnDocumentCreatedAsync, which runs before
  // <body> exists: beforeParse is jsdom's equivalent.
  let bodyAtInjection = 'not run';
  const dom = new JSDOM(fs.readFileSync(path.join(__dirname, 'fixtures', 'core.html'), 'utf8'), {
    runScripts: 'outside-only',
    url: 'http://127.0.0.1:8083/?target=external#/efb',
    beforeParse(window) {
      bodyAtInjection = window.document.body;
      window.eval(fs.readFileSync(LAYER, 'utf8'));
    },
  });
  const window = dom.window;
  try {
    assert.strictEqual(bodyAtInjection, null, 'the layer was injected before the body');
    await turns();
    assert.deepStrictEqual(tilesOf(window).map(t => computeAccessibleName(t)), ['Fenix', 'Takeoff', 'Landing']);
    assert.strictEqual(computeAccessibleName(window.document.getElementById('homeButtonFree')), 'Home');
  } finally {
    window.close();
  }
});

test('Unknown_page_is_left_alone', async () => {
  const bare = await load('unrelated', { layer: false });
  const untouched = bare.document.documentElement.outerHTML;
  bare.close();

  const window = await load('unrelated');
  try {
    const errors = collectErrors(window);
    assert.doesNotThrow(() => window.__msfsbaFenixEfb.pass());
    assert.strictEqual(window.document.documentElement.outerHTML, untouched);
    assert.strictEqual(window.document.querySelectorAll('[data-msfsba]').length, 0);

    // A change on the page makes the observer run a pass; that changes nothing either.
    const extra = window.document.createElement('p');
    extra.textContent = 'Added later';
    window.document.querySelector('section').appendChild(extra);
    await turns();
    extra.remove();
    await turns();
    assert.strictEqual(window.document.documentElement.outerHTML, untouched);
    assert.deepStrictEqual(errors, []);
  } finally {
    window.close();
  }
});

test('Keys_press_buttons_but_not_text_boxes', async () => {
  const window = await load('core');
  try {
    const doc = window.document;
    const [tile] = tilesOf(window);
    let clicks = 0;
    tile.click = () => { clicks++; };

    // Enter and Space press the tile once each, and the page's own handling (scrolling) is stopped.
    let event = press(window, tile, { key: 'Enter' });
    assert.strictEqual(clicks, 1);
    assert.ok(event.defaultPrevented, 'Enter on a tile is handled');
    event = press(window, tile, { key: ' ' });
    assert.strictEqual(clicks, 2);
    assert.ok(event.defaultPrevented, 'Space on a tile is handled');

    // Any other key, and Enter with a modifier, is the page's.
    event = press(window, tile, { key: 'a' });
    assert.ok(!event.defaultPrevented);
    event = press(window, tile, { key: 'Enter', ctrlKey: true });
    assert.ok(!event.defaultPrevented);
    assert.strictEqual(clicks, 2);

    // A text box inside a handled element keeps its keys: nothing pressed, nothing prevented.
    const inner = doc.createElement('input');
    tile.appendChild(inner);
    for (const key of [' ', 'Enter']) {
      event = press(window, inner, { key });
      assert.ok(!event.defaultPrevented, key + ' in an input is left alone');
    }
    assert.strictEqual(clicks, 2);

    // So does any other control inside it: only the tile itself is pressed.
    const nested = doc.createElement('span');
    nested.setAttribute('tabindex', '0');
    tile.appendChild(nested);
    event = press(window, nested, { key: 'Enter' });
    assert.ok(!event.defaultPrevented, 'Enter on a control inside the tile is left alone');
    assert.strictEqual(clicks, 2);

    // So does the stand-alone text box on the page.
    const note = doc.getElementById('note');
    event = press(window, note, { key: ' ' });
    assert.ok(!event.defaultPrevented);

    // Alt+Home goes to the EFB home from anywhere, including from inside a text box.
    window.location.hash = '#/efb/settings';
    event = press(window, note, { key: 'Home' });
    assert.ok(!event.defaultPrevented, 'Home alone is the page\'s');
    assert.strictEqual(window.location.hash, '#/efb/settings');
    event = press(window, note, { key: 'Home', altKey: true });
    assert.strictEqual(window.location.hash, '#/efb');
    assert.ok(event.defaultPrevented);
  } finally {
    window.close();
  }
});

test('A_second_pass_adds_no_second_listener_and_changes_nothing', async () => {
  // Counts every listener the layer adds to an element, from its first pass on.
  const { window, counts } = await loadCounting('core');
  try {
    const before = window.document.documentElement.outerHTML;
    window.__msfsbaFenixEfb.pass();
    window.__msfsbaFenixEfb.pass();
    assert.strictEqual(window.document.documentElement.outerHTML, before);

    const handled = [...window.document.querySelectorAll('[data-msfsba]')];
    assert.strictEqual(handled.length, 5, 'three tiles, the home button and the top bar chevron');
    for (const el of handled) assert.strictEqual(counts.get(el), 1, 'one listener on ' + el.className);
    assert.strictEqual(counts.size, 5, 'no other element got a listener');
  } finally {
    window.close();
  }
});

test('A_pages_own_label_is_kept', async () => {
  const window = await load('core');
  try {
    const doc = window.document;
    const make = (label, title) => {
      const el = doc.createElement('div');
      el.className = 'appIcon';
      if (label !== null) el.setAttribute('aria-label', label);
      el.innerHTML = '<img class="app-img"><br><span class="appTitle">' + title + '</span>';
      doc.getElementById('appContainer').appendChild(el);
      return el;
    };
    const named = make('Mine', 'Other');
    const blank = make('   ', 'Third');
    await turns();
    assert.strictEqual(named.getAttribute('aria-label'), 'Mine');
    assert.strictEqual(named.getAttribute('role'), 'button');
    assert.strictEqual(blank.getAttribute('aria-label'), 'Third');
  } finally {
    window.close();
  }
});

// A label the layer wrote is the layer's to refresh; a label the page wrote is never touched.
test('A_label_the_layer_wrote_follows_the_pages_text', async () => {
  const window = await load('core');
  try {
    const [tile] = tilesOf(window);
    const title = tile.querySelector('.appTitle');
    assert.strictEqual(tile.getAttribute('aria-label'), 'Fenix');

    // Angular updates a text node in place: no node is added or removed, only its data changes.
    title.firstChild.nodeValue = 'Fenix A320';
    await turns();
    assert.strictEqual(tile.getAttribute('aria-label'), 'Fenix A320');
    assert.strictEqual(computeAccessibleName(tile), 'Fenix A320');

    // The text becomes empty: the layer takes its own label away (nothing is better than a stale name).
    title.firstChild.nodeValue = '   ';
    await turns();
    assert.strictEqual(tile.hasAttribute('aria-label'), false);
    assert.strictEqual(tile.getAttribute('role'), 'button', 'still a button');

    // The text comes back: so does the label.
    title.firstChild.nodeValue = 'Fenix again';
    await turns();
    assert.strictEqual(tile.getAttribute('aria-label'), 'Fenix again');

    // A whole new text node (a child-list change) is followed too.
    title.textContent = 'Fenix once more';
    await turns();
    assert.strictEqual(tile.getAttribute('aria-label'), 'Fenix once more');

    // The layer writes attributes only: the page's text is as the page left it.
    assert.strictEqual(title.textContent, 'Fenix once more');
    assert.strictEqual(title.childNodes.length, 1);
  } finally {
    window.close();
  }
});

test('A_label_the_page_wrote_is_never_touched', async () => {
  const window = await load('core');
  try {
    const doc = window.document;
    // A tile the page labelled before the layer saw it.
    const own = doc.createElement('div');
    own.className = 'appIcon';
    own.setAttribute('aria-label', 'Mine');
    own.innerHTML = '<img class="app-img"><br><span class="appTitle">First</span>';
    doc.getElementById('appContainer').appendChild(own);
    await turns();
    assert.strictEqual(own.getAttribute('aria-label'), 'Mine');

    own.querySelector('.appTitle').firstChild.nodeValue = 'Second';
    await turns();
    assert.strictEqual(own.getAttribute('aria-label'), 'Mine', 'text changed: the page\'s label stays');
    own.querySelector('.appTitle').firstChild.nodeValue = '';
    await turns();
    assert.strictEqual(own.getAttribute('aria-label'), 'Mine', 'text emptied: the page\'s label stays');

    // A label the layer wrote and the page then replaced with its own is the page's from then on.
    const [tile] = tilesOf(window);
    assert.strictEqual(tile.getAttribute('aria-label'), 'Fenix');
    tile.setAttribute('aria-label', 'Page label');
    const title = tile.querySelector('.appTitle');
    title.firstChild.nodeValue = 'Changed';
    await turns();
    assert.strictEqual(tile.getAttribute('aria-label'), 'Page label');
    title.firstChild.nodeValue = '';
    await turns();
    assert.strictEqual(tile.getAttribute('aria-label'), 'Page label');
  } finally {
    window.close();
  }
});

test('A_text_change_runs_one_pass_and_the_label_it_rewrites_runs_no_more', async () => {
  const window = await load('core');
  try {
    let scheduled = 0;
    const original = window.setTimeout;
    window.setTimeout = function (fn, ms, ...rest) {
      if (ms === 50) scheduled++;
      return original.call(window, fn, ms, ...rest);
    };
    const [tile] = tilesOf(window);
    tile.querySelector('.appTitle').firstChild.nodeValue = 'Changed';
    await turns();
    await turns();
    assert.strictEqual(tile.getAttribute('aria-label'), 'Changed');
    assert.strictEqual(scheduled, 1, 'the label the pass wrote did not wake the observer');
  } finally {
    window.close();
  }
});

test('The_layer_does_nothing_in_a_child_frame', async () => {
  // WebView2 runs document-created scripts in every iframe too. The layer belongs to the top document.
  const window = await load('core', { layer: false });
  try {
    const markup = window.document.body.innerHTML;
    const frame = window.document.createElement('iframe');
    window.document.body.appendChild(frame);
    const child = frame.contentWindow;
    assert.notStrictEqual(child.top, child, 'jsdom gives the frame a different top');
    child.document.body.innerHTML = markup;
    const untouched = child.document.documentElement.outerHTML;

    child.eval(fs.readFileSync(LAYER, 'utf8'));
    await turns();
    assert.strictEqual(child.__msfsbaFenixEfb, undefined, 'nothing is exported');
    assert.strictEqual(child.document.documentElement.outerHTML, untouched, 'nothing is stamped');

    // Alt+Home is not caught in the frame either.
    const event = press(child, child.document.getElementById('note'), { key: 'Home', altKey: true });
    assert.ok(!event.defaultPrevented);
    assert.strictEqual(child.location.hash, '');

    // The same script in the top window works (the frame is the only difference).
    window.eval(fs.readFileSync(LAYER, 'utf8'));
    await turns();
    assert.ok(window.__msfsbaFenixEfb);
    assert.deepStrictEqual(tilesOf(window).map(t => computeAccessibleName(t)), ['Fenix', 'Takeoff', 'Landing']);
    window.location.hash = '#/efb/settings';
    assert.ok(press(window, window.document.getElementById('note'), { key: 'Home', altKey: true }).defaultPrevented);
    assert.strictEqual(window.location.hash, '#/efb');
  } finally {
    window.close();
  }
});

test('A_failing_rule_does_not_stop_the_others_or_throw_into_the_page', async () => {
  const window = await load('core');
  try {
    const home = window.document.getElementById('homeButtonFree');
    home.removeAttribute('aria-label');

    // The tile rule reads each tile's title with querySelector('.appTitle'); make that throw.
    const proto = window.Element.prototype;
    const original = proto.querySelector;
    proto.querySelector = function (selector) {
      if (selector === '.appTitle') throw new Error('boom');
      return original.call(this, selector);
    };
    try {
      assert.doesNotThrow(() => window.__msfsbaFenixEfb.pass());
    } finally {
      proto.querySelector = original;
    }
    assert.strictEqual(home.getAttribute('aria-label'), 'Home', 'the rules after the failing one still ran');
  } finally {
    window.close();
  }
});

test('The_layers_own_writes_do_not_schedule_another_pass', async () => {
  const window = await load('core');
  try {
    // Count the 50 ms timers the layer sets. One new tile is one mutation, so one pass; if the
    // attributes the pass writes were observed it would schedule another, and another.
    let scheduled = 0;
    const original = window.setTimeout;
    window.setTimeout = function (fn, ms, ...rest) {
      if (ms === 50) scheduled++;
      return original.call(window, fn, ms, ...rest);
    };
    const late = window.document.createElement('div');
    late.className = 'appIcon';
    late.innerHTML = '<img class="app-img"><br><span class="appTitle">Test</span>';
    window.document.getElementById('appContainer').appendChild(late);
    await turns();
    await turns();
    assert.strictEqual(late.getAttribute('role'), 'button');
    assert.strictEqual(scheduled, 1);
  } finally {
    window.close();
  }
});

test('Switcher_icons_are_named_from_the_tile_titles_when_there_are_nine', async () => {
  const window = await load('core');
  try {
    const switcher = addSwitcher(window, 9);
    await turns();
    const items = [...switcher.children];
    items.forEach((item, i) => {
      assert.strictEqual(item.getAttribute('role'), 'button', 'icon ' + i);
      assert.strictEqual(item.tabIndex, 0);
      assert.strictEqual(computeAccessibleName(item), TITLES[i]);
    });
    // The home tiles are not switcher icons: they keep the names the page gives them.
    assert.deepStrictEqual(tilesOf(window).map(t => computeAccessibleName(t)), ['Fenix', 'Takeoff', 'Landing']);
  } finally {
    window.close();
  }
});

test('Switcher_icons_are_left_unnamed_unless_there_are_exactly_nine', async () => {
  const window = await load('core');
  try {
    const switcher = addSwitcher(window, 8);
    await turns();
    for (const item of switcher.children) {
      assert.strictEqual(item.getAttribute('role'), 'button');
      assert.strictEqual(computeAccessibleName(item), '', 'eight icons: no guessed names');
    }
    // The ninth arrives: the names are given.
    const item = window.document.createElement('div');
    item.innerHTML = '<img class="app-img">';
    switcher.appendChild(item);
    await turns();
    assert.deepStrictEqual([...switcher.children].map(c => computeAccessibleName(c)), TITLES);
  } finally {
    window.close();
  }
});

test('Switcher_icons_without_a_parent_of_their_own_are_buttons_themselves', async () => {
  const window = await load('core');
  try {
    const switcher = addSwitcher(window, 9, { sharedParent: true });
    await turns();
    assert.strictEqual(switcher.getAttribute('role'), null, 'the shared container is not a button');
    assert.deepStrictEqual([...switcher.children].map(c => computeAccessibleName(c)), TITLES);
    assert.ok([...switcher.children].every(c => c.getAttribute('role') === 'button' && c.tabIndex === 0));
  } finally {
    window.close();
  }
});

test('A_switcher_icons_parent_is_the_button_only_when_it_holds_just_the_picture_and_line_breaks', async () => {
  const window = await load('core');
  try {
    const doc = window.document;
    const holder = doc.createElement('div');
    // Nine parents: the first three hold only the picture and <br>s; the other six have text of their
    // own or another element, so the picture itself is the button there.
    const kinds = [
      '<img class="app-img"><br>',
      '<img class="app-img"><br><br>',
      '<img class="app-img">',
      '<img class="app-img"><br>Label',
      '<img class="app-img"> some text',
      '<span>Label</span><img class="app-img">',
      '<img class="app-img"><span aria-hidden="true"></span>',
      '<img class="app-img"><i class="icon"></i>',
      '<img class="app-img"><br><span>Extra</span>',
    ];
    const parentIsButton = [true, true, true, false, false, false, false, false, false];
    kinds.forEach(html => {
      const item = doc.createElement('div');
      item.innerHTML = html;
      holder.appendChild(item);
    });
    doc.body.appendChild(holder);
    await turns();
    [...holder.children].forEach((item, i) => {
      const img = item.querySelector('img.app-img');
      const button = parentIsButton[i] ? item : img;
      const other = parentIsButton[i] ? img : item;
      assert.strictEqual(button.getAttribute('role'), 'button', 'icon ' + i + ': the button');
      assert.strictEqual(button.tabIndex, 0, 'icon ' + i);
      assert.strictEqual(computeAccessibleName(button), TITLES[i], 'icon ' + i);
      assert.strictEqual(other.getAttribute('role'), null, 'icon ' + i + ': not the other one');
      assert.strictEqual(other.hasAttribute('data-msfsba'), false, 'icon ' + i);
    });
  } finally {
    window.close();
  }
});
