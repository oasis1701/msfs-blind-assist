// COHERENT DISPLAY READ-COVERAGE PROBE
//
// Answers one question, for a whole instrument at once: WHAT IS ON THE SCREEN THAT MSFSBA
// NEVER SAYS?
//
// It visits every page the instrument actually has, and for each one subtracts everything the
// display agent emits from everything a sighted pilot can see. What is left is the gap.
//
// ⚠️ WHY THIS EXISTS. Every read-coverage hole found in this project was found by a pilot
// flying into it, one at a time, months apart - the CAS block read off a hidden duplicate, the
// flight plan page that reported itself empty, and the entire LATERAL HALF of the flight mode
// annunciator, which sat in an unclassed element and was never scraped at all. That last one
// meant a NAV mode which was engaged and NOT capturing looked exactly like one that was
// working. None of them were findable by reading code; all of them are findable by this.
//
// ⚠️ IT IS DELIBERATELY AIRCRAFT-AGNOSTIC. The DA40's G1000, the DA40-XLS's and the DA42's are
// different builds with different pages, and a probe hard-wired to one is a probe that has to
// be rewritten to be useful again. The instrument names its own pages (pageMap) and the agent
// names its own global, so both are arguments.
//
//   node tools/coherent-coverage.js [pageRegex] [agentGlobal] [agentFile] [saysExpression]
//
//   node tools/coherent-coverage.js AS1000_MFD
//   node tools/coherent-coverage.js AS1000_PFD
//
// ⚠️ IT IS NOT A G1000 TOOL, and was one only by accident of implementation. Every argument
// after the first exists so it is not: the AGENT and its GLOBAL differ per aircraft, and the
// SAYS expression differs per DISPLAY, because no two agents in this codebase share an entry
// point - only the DA40's publishes rows(). For another display, pass its own:
//
//   node tools/coherent-coverage.js A380X_EWD __MSFSBA_EWD //        MSFSBlindAssist/Resources/coherent-ewd-agent.js "A.scrape()"
//
// A display that cannot enumerate its own pages (an E/WD, a CDU, a flyPad) sweeps whatever is
// ON SCREEN - drive it to the page you care about and run it again.
//
// Requires the sim running with the aircraft loaded, and NOTHING ELSE holding the display's
// inspector socket - Coherent GT allows exactly one per view, so close MSFSBA's display window
// (and be aware the DA40's background CAS monitor holds the PFD; see docs/tooling.md).

const fs = require('fs');
const path = require('path');
const { get, evalOn } = require(path.join(__dirname, 'g1000-cdp.js'));

const WS = id => 'ws://127.0.0.1:19999/devtools/page/' + id;

const PAGE_RE = process.argv[2] || 'AS1000_MFD';
const GLOBAL = process.argv[3] || '__MSFSBA_DA40G1000';
const AGENT = process.argv[4] ||
    path.join(__dirname, '..', 'MSFSBlindAssist', 'Resources', 'coherent-da40-g1000-agent.js');

/// The expression producing EVERYTHING MSFSBA SAYS about this display - an array or a string.
/// Defaults to the G1000's rows(); every other agent needs its own, because each answers a
/// different question and none of them shares an entry point.
const SAYS = process.argv[5] || 'A.rows()';

/// ⚠️ THE VISIBILITY TEST IS THE WHOLE TOOL, AND THE OBVIOUS ONE IS WRONG.
///
/// Checking only the element's own computed style passes anything sitting inside a hidden
/// PARENT - and this instrument keeps its page selector, its dialogs and a second copy of the
/// CAS block in the DOM at all times. A leaf-only test therefore reported sixty to eighty
/// "unread" items on every single page, almost all of them furniture that was never on screen,
/// which is worse than no tool: a number that large gets skimmed and the three real gaps
/// buried in it are never read.
///
/// So the ancestor chain is walked to the root. The rect check stays as a second filter for
/// zero-sized and scrolled-away elements.
const SWEEP = `(function(){
  var A = window.__GLOBAL__;
  if (!A) return JSON.stringify({error:"agent not installed"});

  function shown(e){
    var n = e;
    while (n && n.nodeType === 1) {
      var s;
      try { s = getComputedStyle(n); } catch (x) { return false; }
      if (s.display === "none" || s.visibility === "hidden") return false;
      if (parseFloat(s.opacity) === 0) return false;
      n = n.parentElement;
    }
    var r = e.getBoundingClientRect();
    return r.width > 0 && r.height > 0;
  }

  // Everything the agent SAYS, flattened so formatting differences cannot cause a false gap:
  // the scrape may render "DIS 53.0NM" where the screen shows "53.0NM", and a raw string
  // compare would call that missing.
  // ⚠️ WHAT THE APP SAYS IS AN ARGUMENT, because every agent in this codebase answers a
  // DIFFERENT question. Only the DA40's exposes rows(); the A380 E/WD, the flyPad, the PMDG
  // EFB and the HS787 CDU each have their own entry point and their own global. Hard-wiring
  // rows() made a general method look like a G1000 tool.
  function saidNow(){
    try {
      var v = (__SAYS__);
      if (v === null || v === undefined) return null;
      var t = (v.join ? v.join(" ") : String(v));
      return t.toUpperCase().replace(/[^A-Z0-9]/g,"");
    }
    catch (e) { return null; }
  }

  // ⚠️ AN ELEMENT CAN HOLD TEXT NOBODY CAN SEE, BECAUSE SOMETHING IS DRAWN ON TOP OF IT.
  //
  // The G1000's softkey slots are the case that found this. Each slot is a .SoftKey whose own
  // text node reads "KEY4" - a placeholder - with the REAL label ("New WPT") drawn over it at
  // the same coordinates. The slot has no ELEMENT children, so a leaves-only rule takes it,
  // and the probe then reports "KEY4", "KEY7", "Detail" as unread text when a pilot sees
  // "New WPT", "Cncl VNV", "ACT Leg" and the scrape reads those correctly.
  //
  // That was six of the DA40 MFD's remaining hits and it nearly cost the scrape a bug report
  // it did not deserve. So: a candidate whose rectangle CONTAINS the centre of another visible
  // text element is a container being overdrawn, not a label, and is dropped. Geometry rather
  // than a name pattern, because the next instrument will use different names for the same
  // trick.
  function overdrawn(e, boxes){
    var r = e.getBoundingClientRect();
    for (var i = 0; i < boxes.length; i++) {
      var o = boxes[i];
      if (o.el === e) continue;
      var cx = o.r.left + o.r.width / 2, cy = o.r.top + o.r.height / 2;
      if (cx > r.left && cx < r.right && cy > r.top && cy < r.bottom) {
        // Only when the other element is genuinely SMALLER - two boxes of the same size
        // sitting on each other are siblings in a row, not a label over a container.
        if (o.r.width * o.r.height < r.width * r.height) return true;
      }
    }

    // ⚠️ AND THE SAME TRICK AGAIN WHERE THE COVERING LAYER HAS NO TEXT OF ITS OWN.
    //
    // The G1000 draws its softkey row TWICE: the base instrument template's .SoftKey divs,
    // carrying placeholder "KEY2".."KEY9" and whatever labels the PREVIOUS page left behind,
    // and painted over them pixel for pixel the Working Title .softkey-tab row with the live
    // ones. Measured on WPT Airport Information: .SoftKey[9] and .softkey-tab[9] BOTH at
    // (765,734 85x34), the tab on top and correctly reading "blank" while the dead layer
    // underneath still said "Detail". 22 such stale strings across 8 pages were reported as
    // unread content no pilot can see.
    //
    // ⚠️ NEITHER RULE ABOVE CAN CATCH IT, and the second attempt at this failed too. The
    // areas are EQUAL, so the smaller-than test cannot fire; and the covering slot is BLANK,
    // so it carries no text and never enters the boxes list at all - a rule comparing candidates
    // against other TEXT elements has nothing to compare against. What covers a label need
    // not be a label.
    //
    // So ask the browser what it actually paints here. elementFromPoint is exact where a
    // z-index and stacking-order reimplementation would not be.
    var hit = null;
    try { hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2); }
    catch (x) { hit = null; }

    // ⚠️ EVERY UNCERTAIN CASE KEEPS THE CANDIDATE. This tool exists to find content nobody
    // reads, so a filter that drops a real gap is far worse than one that lets a false hit
    // through - a wrong number here is worse than no tool. Nothing at that point (a page
    // with pointer-events:none throughout), or a hit inside the candidate, or a hit on one
    // of its ANCESTORS - which means the candidate simply is not hit-testable itself, not
    // that anything covers it - all keep it.
    if (!hit) return false;
    if (hit === e || e.contains(hit) || hit.contains(e)) return false;

    // ⚠️ AND THE COVERING ELEMENT MUST HAVE ESSENTIALLY THE SAME RECTANGLE. THE HIT TEST
    // ALONE IS NOT ENOUGH - that was tried and it silently HID REAL CONTENT, the one thing
    // this tool must never do. On its own it dropped the PFD's DTK box (the compass SVG is
    // painted across it) and the OAT label (the bottom info panel), both of which a sighted
    // pilot reads perfectly well. The count fell from 22 to 6 and looked like an improvement
    // while the tool had quietly started lying.
    //
    // Same position AND same size is what makes something a duplicate LAYER rather than a
    // big graphic a label sits on top of: the compass and the info panel are far larger than
    // the labels they cover and are excluded by that alone, while a second copy of the same
    // widget is not.
    //
    // ⚠️ CLIMB FROM THE HIT, because elementFromPoint returns the DEEPEST element and that is
    // usually an inner part of the covering widget rather than the widget. The softkey case
    // again: the hit is .softkey-tab-borders at (765,741 85x33), inset seven pixels inside
    // the .softkey-tab at (765,734 85x34) which is the layer that actually coincides. Testing
    // the hit alone kept every one of the 22 stale strings this exists to remove.
    for (var a = hit, up = 0; a && up < 4; a = a.parentElement, up++) {
      if (a === e || e.contains(a) || a.contains(e)) break;
      var hr = a.getBoundingClientRect();
      if (Math.abs(hr.left - r.left) <= 2 && Math.abs(hr.top - r.top) <= 2 &&
          Math.abs(hr.width - r.width) <= 2 && Math.abs(hr.height - r.height) <= 2) return true;
    }
    return false;
  }

  function unread(){
    var said = saidNow();
    if (said === null) return {error:"rows() threw"};
    var seen = {}, out = [];
    var all = document.querySelectorAll("*");

    // Every visible text box, gathered once, so the overdraw test is a lookup rather than a
    // second full-document walk per candidate.
    var boxes = [];
    for (var b = 0; b < all.length; b++) {
      var eb = all[b];
      var tb = (eb.textContent || "").trim();
      if (!tb || !shown(eb)) continue;
      boxes.push({el: eb, r: eb.getBoundingClientRect()});
    }

    for (var i = 0; i < all.length; i++) {
      var e = all[i];
      if (e.children.length) continue;                 // leaves only - no double counting
      var t = (e.textContent || "").replace(/\\s+/g," ").trim();
      if (!t || t.length < 2 || t.length > 40) continue;
      // Bare numbers are tape graduations and compass ticks. They are not readable content
      // and including them drowns the report - the NUMBER a pilot wants is always labelled.
      if (/^[-+0-9.,:°%\\/]+$/.test(t)) continue;
      if (!shown(e)) continue;
      if (overdrawn(e, boxes)) continue;               // covered by a label drawn on top
      var key = t.toUpperCase().replace(/[^A-Z0-9]/g,"");
      if (!key || seen[key]) continue;
      seen[key] = 1;
      if (said.indexOf(key) < 0) out.push(t);
    }
    return {items: out};
  }

  // Only pages the instrument ACTUALLY BUILT. A stub carries an empty key, its knob does
  // nothing for a sighted pilot either, and sweeping it measures the page underneath.
  // ⚠️ A DISPLAY THAT CANNOT ENUMERATE ITS OWN PAGES IS STILL WORTH SWEEPING. Only the G1000
  // publishes a pageMap; an E/WD or a CDU has one view, and a flyPad's pages are reached by
  // touching it. Falling back to "sweep what is on screen" is what makes this usable on every
  // Coherent display in the app rather than one - drive the display to a page yourself and
  // run it again.
  var pages = [];
  try {
    var map = (A.M && A.M.pageMap) ? A.M.pageMap() : null;
    if (map) {
      for (var g = 0; g < map.length; g++)
        for (var p = 0; p < map[g].pages.length; p++)
          if (map[g].pages[p].key)
            pages.push({label: map[g].group + " / " + map[g].pages[p].name,
                        key: map[g].pages[p].key});
    }
  } catch (e) { pages = []; }
  var singleView = pages.length === 0;
  if (singleView) pages.push({label: "(current view)", key: null});

  window.__G1000_COVERAGE = {done:false, pages:{}, order:[], total:pages.length};
  var i = 0, startPage = null;
  try { startPage = (A.M && A.M.pageKey) ? A.M.pageKey() : null; } catch (e) {}

  function step(){
    if (i >= pages.length) {
      if (startPage && !singleView) { try { A.M.goPage(startPage); } catch (e) {} }
      window.__G1000_COVERAGE.done = true;
      return;
    }
    var pg = pages[i++];
    var r = "ok";
    if (pg.key) {
      try { A.M.escape(); } catch (e) {}        // a dialog left open measures the dialog
      try { r = A.M.goPage(pg.key); } catch (e) { r = "threw: " + e.message; }
    }

    // The page selector commits about a second after the last turn, and a page read before
    // it has drawn reports the page the probe has just left.
    setTimeout(function(){
      var res = (r === "ok") ? unread() : {error: String(r)};
      window.__G1000_COVERAGE.pages[pg.label] = res;
      window.__G1000_COVERAGE.order.push(pg.label);
      step();
    }, 1100);
  }
  step();
  return JSON.stringify({started: pages.length});
})()`;

const READ = `JSON.stringify(window.__G1000_COVERAGE || {done:false, pages:{}, order:[]})`;

const sleep = ms => new Promise(r => setTimeout(r, ms));

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

    // Install the agent. It is idempotent - installing over a live one is how MSFSBA itself
    // recovers a socket - and doing it unconditionally means the probe never reports a false
    // "agent not installed" just because nothing had opened the display yet.
    const agentJs = fs.readFileSync(AGENT, 'utf8');
    const installed = await evalOn(url, agentJs, 25000);
    if (!/MSFSBA_DISP_INSTALLED/.test(String(installed))) {
        console.error('Agent did not install. It must return the MSFSBA_DISP_INSTALLED token.');
        console.error('Got: ' + String(installed).slice(0, 300));
        process.exit(1);
    }
    console.log(`agent: ${String(installed).trim()}   page: ${target.title}\n`);

    const started = await evalOn(
        url, SWEEP.replace('__GLOBAL__', GLOBAL).replace('__SAYS__', SAYS), 20000);
    let meta;
    try { meta = JSON.parse(started); } catch (e) { meta = {}; }
    if (meta.error) { console.error('Sweep refused: ' + meta.error); process.exit(1); }
    console.log(`sweeping ${meta.started} real pages (stubs skipped)...\n`);

    // Poll rather than guess a duration: page count varies by aircraft, and a fixed wait is
    // either a stall or a truncated report.
    let data = null;
    for (let waited = 0; waited < 120000; waited += 1500) {
        await sleep(1500);
        try { data = JSON.parse(await evalOn(url, READ, 10000)); } catch (e) { continue; }
        if (data && data.done) break;
        process.stderr.write('.');
    }
    process.stderr.write('\n');
    if (!data || !data.done) { console.error('Sweep did not finish.'); process.exit(1); }

    // ---- CHROME FILTER -------------------------------------------------------------------
    //
    // ⚠️ THE COUNT IS MEANINGLESS WITHOUT THIS. Softkey labels, the page-selector entries and
    // any dialog left mounted appear on EVERY page, so a raw per-page list is dominated by
    // furniture. Anything present nearly everywhere is chrome by definition - it belongs to
    // the instrument, not the page - and is reported ONCE, separately, rather than repeated
    // twenty times.
    const seenOn = new Map();
    const order = data.order || [];
    for (const label of order) {
        const r = data.pages[label];
        if (!r || !r.items) continue;
        for (const t of new Set(r.items)) seenOn.set(t, (seenOn.get(t) || 0) + 1);
    }
    const n = order.length || 1;
    const CHROME_AT = Math.max(2, Math.ceil(n * 0.6));
    const chrome = [...seenOn.entries()].filter(([, c]) => c >= CHROME_AT).map(([t]) => t).sort();
    const chromeSet = new Set(chrome);

    let gaps = 0;
    console.log('================ PAGE-SPECIFIC UNREAD CONTENT ================');
    for (const label of order) {
        const r = data.pages[label];
        if (!r) continue;
        if (r.error) { console.log(`\n${label}\n   ! ${r.error}`); continue; }
        const own = [...new Set(r.items)].filter(t => !chromeSet.has(t)).sort();
        if (!own.length) continue;
        gaps += own.length;
        console.log(`\n${label}  (${own.length})`);
        for (const t of own) console.log('   ' + t);
    }
    if (!gaps) console.log('\n   nothing - every page\'s own content is read.');

    console.log(`\n================ INSTRUMENT CHROME (on >=${CHROME_AT}/${n} pages) ================`);
    console.log('Softkeys, page-selector entries and mounted dialogs. Reported once, not per page.');
    console.log('Worth a look ONLY if something here should be readable on demand.\n');
    console.log('   ' + (chrome.length ? chrome.join(' | ') : 'none'));
    console.log(`\nSUMMARY: ${gaps} page-specific unread item(s) across ${n} page(s); ${chrome.length} chrome item(s).`);
})().catch(e => { console.error('ERR ' + e.message); process.exit(1); });
