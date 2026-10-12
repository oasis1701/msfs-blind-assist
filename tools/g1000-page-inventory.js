// WHAT EACH G1000 PAGE OFFERS, PAGE BY PAGE: its softkeys, and any box it DRAWS but
// registers no control for.
//
//   node tools/g1000-page-inventory.js [pageRegex] [agentGlobal] [agentFile]
//   node tools/g1000-page-inventory.js AS1000_MFD
//   node tools/g1000-page-inventory.js AS1000_PFD
//
// WHY THIS EXISTS SEPARATELY FROM coherent-coverage.js. That tool answers "is anything on
// screen unread", which is the right question and a blunt one: it cannot tell a fifty-row
// list a pilot arrows through from a value nothing can reach, so its worst pages are the
// list pages and its number moves run to run as the aeroplane's own readings drift. This
// answers two exact questions instead, and both have crisp answers:
//
//   1. WHICH SOFTKEYS DOES THIS PAGE HAVE? Twelve slots, relabelled per page, and a blank
//      slot is a real answer (that page has no key there). A pilot arrows to the softkey row
//      and presses Enter, so the LABELS are the whole of what they get to choose from.
//
//   2. WHAT DOES THIS PAGE DRAW THAT THE FIELD WALK CANNOT SEE? A.M.fields() walks the view's
//      registered controls, so a box that renders a value and registers nothing is invisible
//      to it. Those boxes are not clickable BY ANYONE - a sighted pilot cannot cursor to them
//      either - but a sighted pilot READS them, and the rule is that whatever they can see we
//      must be able to see, ours being the choice of whether to say it.
//
// Requires the sim running with the aircraft loaded, and NOTHING ELSE holding the display's
// inspector socket - Coherent GT allows exactly one per view, so close MSFSBA's display
// window (and note the DA40's background CAS monitor holds the PFD).

const fs = require('fs');
const path = require('path');
const { get, evalOn } = require(path.join(__dirname, 'g1000-cdp.js'));

const WS = id => 'ws://127.0.0.1:19999/devtools/page/' + id;

const PAGE_RE = process.argv[2] || 'AS1000_MFD';
const GLOBAL = process.argv[3] || '__MSFSBA_DA40G1000';
const AGENT = process.argv[4] ||
    path.join(__dirname, '..', 'MSFSBlindAssist', 'Resources', 'coherent-da40-g1000-agent.js');

const sleep = ms => new Promise(r => setTimeout(r, ms));

// Walks the pages the way the coverage sweep does - stubs skipped, the page selector given
// its ~1.1 s to commit - and records, per page, the softkey labels plus every visible
// groupbox with its title, its text, and whether the field walk already covered it.
const SWEEP = `(function(){
  var A = window.__GLOBAL__;
  if (!A) return JSON.stringify({error:"agent not installed"});

  function vis(e){
    var n = e;
    while (n && n.nodeType === 1) {
      var s; try { s = getComputedStyle(n); } catch (x) { return false; }
      if (s.display === "none" || s.visibility === "hidden") return false;
      if (parseFloat(s.opacity) === 0) return false;
      n = n.parentElement;
    }
    var r = e.getBoundingClientRect();
    return r.width > 0 && r.height > 0;
  }

  function norm(t){ return String(t||"").toUpperCase().replace(/[^A-Z0-9]/g,""); }

  function snapshot(){
    // ⚠️ A.softkeys() returns OBJECTS ({index, label, value, ...}), not strings - reading
    // it as strings printed twelve "[object Object]".
    var keys = [];
    try {
      var sk = A.softkeys() || [];
      for (var k = 0; k < sk.length; k++) {
        var lab = String(sk[k].label || "").trim();
        var val = String(sk[k].value || "").trim();
        keys.push(val ? (lab + " " + val) : lab);
      }
    } catch (e) { keys = ["!softkeys threw: " + e.message]; }

    // What the field walk says, so a box it already covers is not reported as missing.
    var said = "";
    try { said = norm((A.M.fieldRows() || []).join(" ")); } catch (e) { said = ""; }

    var drawn = [];
    var boxes = [];
    try { boxes = document.querySelectorAll(".groupbox"); } catch (e) { boxes = []; }
    for (var b = 0; b < boxes.length; b++) {
      var box = boxes[b];
      if (!vis(box)) continue;
      var t = "", body = "";
      try { t = (box.querySelector(".groupbox-title")||{}).textContent || ""; } catch (e) {}
      try { body = box.textContent || ""; } catch (e) {}
      t = t.replace(/\\s+/g," ").trim();
      body = body.replace(/\\s+/g," ").trim();
      if (!t && !body) continue;
      drawn.push({ title: t, text: body.slice(0,160), covered: !!(norm(t) && said.indexOf(norm(t)) >= 0) });
    }
    return { keys: keys, drawn: drawn };
  }

  // ⚠️ A.pageList() is a newline-joined STRING for the pilot's jump list, not an array.
  // The real enumeration is A.M.pageMap(), which is what the coverage sweep walks, and a
  // page with an EMPTY KEY is one of the stock instrument's stubs - it has no page behind
  // it and the knob does nothing on it for a sighted pilot either.
  var real = [];
  try {
    var map = (A.M && A.M.pageMap) ? A.M.pageMap() : null;
    if (map) {
      for (var g = 0; g < map.length; g++)
        for (var p = 0; p < map[g].pages.length; p++)
          if (map[g].pages[p].key)
            real.push({ label: map[g].group + " / " + map[g].pages[p].name,
                        key: map[g].pages[p].key });
    }
  } catch (e) { real = []; }
  if (!real.length) real = [{ key: null, label: "(current view)" }];

  var startPage = null;
  try { startPage = A.M.view() && A.M.view().openPageKey; } catch (e) {}

  window.__G1000_INV = { pages:{}, order:[], done:false };
  var i = 0;
  function step(){
    if (i >= real.length) {
      if (startPage) { try { A.M.goPage(startPage); } catch (e) {} }
      window.__G1000_INV.done = true;
      return;
    }
    var pg = real[i++];
    var r = "ok";
    if (pg.key) {
      try { A.M.escape(); } catch (e) {}
      try { r = A.M.goPage(pg.key); } catch (e) { r = "threw: " + e.message; }
    }
    setTimeout(function(){
      window.__G1000_INV.pages[pg.label] = (r === "ok") ? snapshot() : { error:String(r) };
      window.__G1000_INV.order.push(pg.label);
      step();
    }, 1100);
  }
  step();
  return JSON.stringify({ started: real.length });
})()`;

const READ = `JSON.stringify(window.__G1000_INV || {done:false, pages:{}, order:[]})`;

(async () => {
    const list = JSON.parse(await get('/pagelist.json'));
    const pages = list.pages || list;
    const target = pages.find(p => new RegExp(PAGE_RE).test(p.title || ''));
    if (!target) {
        console.error(`No Coherent page matching /${PAGE_RE}/. Is the sim running with the aircraft loaded?`);
        console.error('Available: ' + pages.map(p => p.title).join(', '));
        process.exit(1);
    }
    const url = WS(target.id);

    const installed = await evalOn(url, fs.readFileSync(AGENT, 'utf8'), 25000);
    if (!/MSFSBA_DISP_INSTALLED/.test(String(installed))) {
        console.error('Agent did not install. Got: ' + String(installed).slice(0, 300));
        process.exit(1);
    }
    console.log(`page: ${target.title}\n`);

    const started = await evalOn(url, SWEEP.replace('__GLOBAL__', GLOBAL), 20000);
    let meta; try { meta = JSON.parse(started); } catch (e) { meta = {}; }
    if (meta.error) { console.error('Refused: ' + meta.error); process.exit(1); }
    console.log(`walking ${meta.started} real page(s)...\n`);

    let data = null;
    for (let waited = 0; waited < 120000; waited += 1500) {
        await sleep(1500);
        try { data = JSON.parse(await evalOn(url, READ, 10000)); } catch (e) { continue; }
        if (data && data.done) break;
        process.stderr.write('.');
    }
    process.stderr.write('\n');
    if (!data || !data.done) { console.error('Did not finish.'); process.exit(1); }

    console.log('================ SOFTKEYS, PAGE BY PAGE ================');
    console.log('Twelve slots. A blank slot means that page has no key there.\n');
    for (const label of data.order) {
        const r = data.pages[label];
        if (!r) continue;
        if (r.error) { console.log(`${label}\n   ! ${r.error}\n`); continue; }
        const keys = (r.keys || []).map((k, n) => `${n + 1}:${String(k).trim() || '-'}`);
        console.log(`${label}\n   ${keys.length ? keys.join('  ') : '(none reported)'}\n`);
    }

    console.log('======== DRAWN BUT NOT COVERED BY THE FIELD WALK ========');
    console.log('Boxes a sighted pilot reads that the row list does not carry.\n');
    let holes = 0;
    for (const label of data.order) {
        const r = data.pages[label];
        if (!r || r.error) continue;
        const miss = (r.drawn || []).filter(d => !d.covered && d.title);
        if (!miss.length) continue;
        holes += miss.length;
        console.log(`${label}  (${miss.length})`);
        for (const m of miss) console.log(`   ${m.title}  ::  ${m.text.slice(0, 90)}`);
        console.log('');
    }
    if (!holes) console.log('   none - every drawn box is covered.\n');
    console.log(`SUMMARY: ${holes} drawn-but-uncovered box(es) across ${data.order.length} page(s).`);
})().catch(e => { console.error('ERR ' + e.message); process.exit(1); });
