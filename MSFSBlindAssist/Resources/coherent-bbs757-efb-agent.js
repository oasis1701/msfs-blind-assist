// BlueBird 757-200 EFB agent: presses buttons on BlueBird's tablet ("BBOS") by scripted clicks,
// over the Coherent GT remote debugger (port 19999). Installed by CoherentBbs757EfbClient into the
// cockpit view whose title holds "BBSNavigraphMap"; exposes window.__MSFSBA_BBS757EFB.
//
// The tablet is the WASM gauge efb_l, drawn into a <wasm-sim-canvas> inside its <wasm-instrument>.
// That element forwards the mouse to the WASM through its own four handlers (onMouseMove,
// OnMouseDown, OnMouseUp, onClick: each a Coherent.call WASM_MOUSE_* / WASM_CLICK with page
// coordinates). A click here calls those four with the canvas origin plus the tablet-local point,
// exactly as the mouse would (measured 2026-10-25; docs/b757.md, "Pressing"). No camera move, no
// real mouse. Nothing on the tablet can be read: the app checks results through files BlueBird
// writes.
//
// Nothing here judges where a click lands (the owner's ruling of 2026-10-29): a click goes where it
// is asked to, the AIRAC page's UPDATE/REMOVE spot included, and the app checks what it taps before
// it taps. A run is refused only for what the agent cannot run: a sequence that is not
// [[x, y, delayAfterMs], ...], a drag point off the canvas, one still going, or a tablet that is not
// the measured size.
//
// Sequences: clickSeq takes [[x, y, delayAfterMs], ...] and runs it in the page with setTimeout,
// one run at a time, so a sequence is never interleaved with another and never depends on the
// app's link staying up between its clicks. Before a new run starts, any older run's pending click
// is cleared. status(id) says how far a run got; cancel() stops one.
//
// Install: an agent that cannot click is no agent. When Coherent.call or the tablet's canvas (with
// its four handlers, at the measured size) is missing - the view's scripts or gauges not ready
// after an aircraft reload - the install answers NOT_READY instead of the marker, so the app tries
// again. A re-install of a healthy agent keeps it, and with it any run in progress.
//
// ES5 ONLY (Coherent GT = old Chromium): var, no arrow functions, no String.includes, top-level
// try/catch.
(function () {
  try {
    // 3: refused a click near the AIRAC page's UPDATE spot unless the run carried the nav data update's page guards.
    // 4: mirror(on), for the tablet reader (Bbs757EfbPopout / Bbs757EfbPageReader).
    // 5: wheel(x, y, deltaY), for scrolling a long tablet page.
    // 6: readVars(names), the L:vars the reader judges a tap by.
    // 7: that refusal, the run guards it needed and the page clock they ran on are gone: a click runs anywhere on the tablet.
    // 8: drag(x, y0, y1) replaces wheel: this build scrolls on a drag (plan 20, Task 1)
    // 9: drag answers with its run, as clickSeq does, so the app waits for it by status(id); a throw at its start ends the run
    // 11: a drag eases out to a stop, so the page does not fling on past it (measured live; 10 was a trial)
    // The app's calls ask for this version or later; an older agent on the page is replaced.
    var VERSION = 11;
    var MARK = 'MSFSBA_BBS757_EFB_INSTALLED';
    var NOT_READY = 'MSFSBA_BBS757_EFB_NOT_READY:';
    // The tablet gauge's size in BlueBird 0.1.0 (measured 1397 x 1058). Another size means another
    // layout: the measured positions would miss, so nothing is clicked.
    var WIDTH = 1397, MIN_HEIGHT = 1048, MAX_HEIGHT = 1070, SIZE_SLACK = 2;
    var MAX_STEPS = 40, MAX_DELAY_MS = 10000;
    // A run still marked running this long after its last click was due is taken as dead.
    var STALE_MS = 5000;

    function canvasProblem(c) {
      if (!c) return 'canvas';
      if (typeof c.onMouseMove !== 'function' || typeof c.OnMouseDown !== 'function'
          || typeof c.OnMouseUp !== 'function' || typeof c.onClick !== 'function') return 'handlers';
      var r = c.getBoundingClientRect();
      if (Math.abs(r.width - WIDTH) > SIZE_SLACK || r.height < MIN_HEIGHT || r.height > MAX_HEIGHT)
        return 'size ' + Math.round(r.width) + 'x' + Math.round(r.height);
      return null;
    }

    // The tablet's canvas, looked up afresh each time: the gauge can be rebuilt under the page.
    function findCanvas() {
      var insts = document.querySelectorAll('wasm-instrument');
      for (var i = 0; i < insts.length; i++) {
        var url = insts[i].getAttribute('url') || '';
        if (/[?&]wasm_gauge=efb_l(&|$)/.test(url)) return insts[i].querySelector('wasm-sim-canvas');
      }
      return null;
    }

    function coherentProblem() {
      if (typeof Coherent === 'undefined' || !Coherent || typeof Coherent.call !== 'function') return 'Coherent';
      return null;
    }

    var old = window.__MSFSBA_BBS757EFB;
    if (old && old.version === VERSION && old._state && !coherentProblem() && !canvasProblem(findCanvas())) return MARK;

    var problem = coherentProblem() || canvasProblem(findCanvas());
    if (problem) return NOT_READY + problem;

    // An older agent on this page is replaced: its run, if any, stops first (one run at a time).
    if (old && typeof old.cancel === 'function') {
      try { old.cancel(); } catch (e) { }
    }

    var A = { version: VERSION };
    var state = { nextId: 1, run: null };
    A._state = state;

    function isWhole(v) { return typeof v === 'number' && isFinite(v) && Math.floor(v) === v; }

    // One click at a tablet-local point: 'ok', or why not.
    A.click = function (x, y) {
      try {
        if (coherentProblem()) return 'no-coherent';
        var c = findCanvas();
        var p = canvasProblem(c);
        if (p) return 'no-canvas: ' + p;
        var r = c.getBoundingClientRect();
        if (!isWhole(x) || !isWhole(y) || x < 0 || y < 0 || x >= r.width || y >= r.height) return 'bad-point';
        var ev = { clientX: r.left + x, clientY: r.top + y, button: 0, preventDefault: function () { } };
        c.onMouseMove(ev);
        c.OnMouseDown(ev);
        c.OnMouseUp(ev);
        c.onClick(ev);
        return 'ok';
      } catch (e) {
        return 'error: ' + (e && e.message);
      }
    };

    // L:vars read in this page (SimVar.GetSimVarValue), for the reader to judge a tile before and after
    // a tap: the ground services' BBS757_vis_* flags, the doors, the brake temperatures. Costs no
    // SimConnect definition. names: an array of L:var names without the "L:" prefix, at most 64, each
    // letters, digits and underscores. Answers {ok, values: {name: number|null}} or {ok:false, error}.
    A.readVars = function (names) {
      try {
        if (typeof SimVar === 'undefined' || !SimVar || typeof SimVar.GetSimVarValue !== 'function') return { ok: false, error: 'no-simvar' };
        if (!names || typeof names.length !== 'number' || names.length > 64) return { ok: false, error: 'bad-names' };
        var out = {};
        for (var i = 0; i < names.length; i++) {
          var n = names[i];
          if (typeof n !== 'string' || !/^[A-Za-z0-9_]{1,80}$/.test(n)) return { ok: false, error: 'bad-name' };
          var v = null;
          try { v = SimVar.GetSimVarValue('L:' + n, 'number'); } catch (e) { v = null; }
          out[n] = (typeof v === 'number' && isFinite(v)) ? v : null;
        }
        return { ok: true, values: out };
      } catch (e) {
        return { ok: false, error: String(e && e.message) };
      }
    };

    // A drag at a tablet-local x from y0 to y1, for the reader to scroll a page longer than the
    // tablet (this build scrolls on a drag, not reliably on the wheel): mouse move and down at
    // (x, y0), 20 moves 50 ms apart to (x, y1), each shorter than the last, held 400 ms, then up (the page's
    // timers run slower: about 1.7 s in all, measured). It runs in the page, one run
    // at a time with the click runs: refused while one is going. It answers as clickSeq does,
    // {ok, id, n, done, totalMs, error}, and the app waits for the run by status(id) before it reads
    // the page again; refused: {ok:false, error}.
    var DRAG_MOVES = 20, DRAG_MOVE_MS = 50, DRAG_HOLD_MS = 400;
    A.drag = function (x, y0, y1) {
      var run = null;
      try {
        var c = findCanvas();
        var p = coherentProblem() || canvasProblem(c);
        if (p) return refuse('no-canvas: ' + p);
        if (runIsLive(state.run)) return refuse('busy');
        var r = c.getBoundingClientRect();
        // A point off the canvas cannot be dragged at all (not a judgment of where on the tablet).
        if (!isWhole(x) || !isWhole(y0) || !isWhole(y1) || x < 0 || x >= r.width
            || y0 < 0 || y0 >= r.height || y1 < 0 || y1 >= r.height) return refuse('bad-point');
        stopRun(state.run, 'canceled');
        var total = DRAG_MOVES * DRAG_MOVE_MS + DRAG_HOLD_MS;
        run = { id: state.nextId++, n: DRAG_MOVES + 2, done: 0, running: true, error: null,
                startedAt: Date.now(), totalMs: total, timer: null };
        state.run = run;
        var ev = function (y) { return { clientX: r.left + x, clientY: r.top + y, button: 0, preventDefault: function () { } }; };
        // The mouse button counts as down from the call to OnMouseDown (a throw inside it may come after
        // the press) until letGo: run.letGo is set from the start, and stopRun and fail call it, so a
        // cancel or a failed move never leaves the tablet's mouse button held, and a throw in the first
        // mouse move, before any press, lets nothing go.
        var lastY = y0, down = false;
        run.letGo = function () {
          run.letGo = null;
          if (!down) return;
          try { c.OnMouseUp(ev(lastY)); } catch (e) { }
        };
        var fail = function (e) {
          run.error = 'error: ' + (e && e.message);
          run.running = false;
          if (run.letGo) run.letGo();
        };
        var release = function () {
          run.timer = null;
          if (run.letGo) run.letGo();
          run.done = run.n;
          run.running = false;
        };
        var move = function (k) {
          run.timer = null;
          if (!run.running) return;
          try {
            // Eased out: each step shorter than the last, the final one a 400th of the drag. A scrollbox flings on when the
            // button comes up, by about the square of the last step, hold or no hold (measured: 20 even steps of 5 px scrolled
            // the page 125.7 px), so a drag that slows to a stop leaves the page where it let go.
            var t = 1 - k / DRAG_MOVES;
            lastY = y0 + (y1 - y0) * (1 - t * t);
            c.onMouseMove(ev(lastY));
            run.done = k + 1;
            if (k < DRAG_MOVES) run.timer = setTimeout(function () { move(k + 1); }, DRAG_MOVE_MS);
            else run.timer = setTimeout(release, DRAG_HOLD_MS);
          } catch (e) { fail(e); }
        };
        try {
          c.onMouseMove(ev(y0));
          down = true;
          c.OnMouseDown(ev(y0));
        } catch (e) {
          fail(e);   // the run is no longer running (never 'busy' for its 6 s), and lets go if the button went down
          return runInfo(run);
        }
        move(1);   // a throw in it goes to fail, which lets go
        return { ok: true, id: run.id, n: run.n, done: run.done, totalMs: total, error: run.error };
      } catch (e) {
        if (run) {
          run.error = 'error: ' + (e && e.message);
          run.running = false;
          if (run.letGo) run.letGo();
          return runInfo(run);
        }
        return refuse('error: ' + (e && e.message));
      }
    };

    // The tablet reader's mirror. The tablet is drawn natively and nothing in this page can read
    // its pixels (getImageData is NotSupported in Coherent GT, toDataURL is empty: measured). But
    // the tablet's live image (an <img> inside the efb_l instrument, src "WasmGaugeLiveView_N")
    // can be laid over the captain's ND (nd_l) in this same page, and the app's pop-out window of
    // that ND then shows the tablet at full brightness, cold and dark included. The overlay keeps
    // the tablet's aspect: the ND's width, its height scaled (the top 528 of the ND's 859 rows).
    // While it is on, the ND in the 3D cockpit shows the tablet too, so the app turns it on only for
    // a read. Both gauges are found by NAME, never by position or live-view number (both change).
    // mirror(true) adds or refreshes it, mirror(false) removes it:
    // {ok, on, nd:[x,y,w,h], efb:[w,h]} or {ok:false, error}.
    var MIRROR_ID = 'msfsba_bbs757_efb_mirror';
    function gaugeInstrument(name) {
      var insts = document.querySelectorAll('wasm-instrument');
      var re = new RegExp('[?&]wasm_gauge=' + name + '(&|$)');
      for (var i = 0; i < insts.length; i++) {
        if (re.test(insts[i].getAttribute('url') || '')) return insts[i];
      }
      return null;
    }
    A.mirror = function (on) {
      try {
        var old = document.getElementById(MIRROR_ID);
        if (!on) {
          if (old && old.parentNode) old.parentNode.removeChild(old);
          return { ok: true, on: false };
        }
        var nd = gaugeInstrument('nd_l'), efb = gaugeInstrument('efb_l');
        if (!nd || !efb) return { ok: false, error: 'no-gauge' };
        var live = efb.querySelector('img');
        var src = live && live.getAttribute('src');
        if (!src) return { ok: false, error: 'no-efb-image' };
        var nr = nd.getBoundingClientRect(), er = efb.getBoundingClientRect();
        if (!(nr.width > 0) || !(er.width > 0)) return { ok: false, error: 'no-size' };
        var h = Math.round(nr.width * er.height / er.width);
        var el = old;
        if (!el) {
          el = document.createElement('img');
          el.id = MIRROR_ID;
          document.body.appendChild(el);
        }
        if (el.getAttribute('src') !== src) el.setAttribute('src', src);
        el.style.cssText = 'position:absolute;left:' + Math.round(nr.left) + 'px;top:' + Math.round(nr.top)
          + 'px;width:' + Math.round(nr.width) + 'px;height:' + h + 'px;z-index:99999;pointer-events:none;';
        return { ok: true, on: true,
                 nd: [Math.round(nr.left), Math.round(nr.top), Math.round(nr.width), Math.round(nr.height)],
                 efb: [Math.round(er.width), Math.round(er.height)] };
      } catch (e) {
        return { ok: false, error: String(e && e.message) };
      }
    };

    function runIsLive(run) {
      return !!run && run.running && (Date.now() - run.startedAt) < run.totalMs + STALE_MS;
    }

    // Stops a run: its pending click is cleared, so nothing more of it is clicked. A drag still holding
    // the mouse button down lets go first.
    function stopRun(run, reason) {
      if (!run) return false;
      if (run.timer) clearTimeout(run.timer);
      run.timer = null;
      if (run.letGo) run.letGo();
      if (!run.running) return false;
      run.running = false;
      run.error = reason;
      return true;
    }

    function parseSeq(json) {
      var steps;
      try { steps = JSON.parse(json); } catch (e) { return null; }
      if (!steps || typeof steps.length !== 'number' || steps.length < 1 || steps.length > MAX_STEPS) return null;
      for (var i = 0; i < steps.length; i++) {
        var s = steps[i];
        if (!s || s.length !== 3 || !isWhole(s[0]) || !isWhole(s[1]) || !isWhole(s[2])) return null;
        if (s[2] < 0 || s[2] > MAX_DELAY_MS) return null;
      }
      return steps;
    }

    function refuse(error) { return { ok: false, error: error }; }

    function runInfo(run) {
      return { ok: true, id: run.id, n: run.n, done: run.done, running: runIsLive(run), error: run.error };
    }

    // Starts a run: the first click now, each next one after the previous step's delay.
    A.clickSeq = function (json) {
      if (runIsLive(state.run)) return refuse('busy');
      var steps = parseSeq(json);
      if (!steps) return refuse('bad-seq');
      var p = coherentProblem() || canvasProblem(findCanvas());
      if (p) return refuse('no-canvas: ' + p);

      // A dead run may still hold a pending click: cleared before the new run starts.
      stopRun(state.run, 'canceled');

      var total = 0;
      for (var i = 0; i < steps.length; i++) total += steps[i][2];
      var run = { id: state.nextId++, n: steps.length, done: 0, running: true, error: null,
                  startedAt: Date.now(), totalMs: total, timer: null };
      state.run = run;

      function step(k) {
        run.timer = null;
        if (!run.running) return;
        var r = A.click(steps[k][0], steps[k][1]);
        if (r !== 'ok') {
          run.error = r;
          run.running = false;
          return;
        }
        run.done = k + 1;
        if (k + 1 >= steps.length) {
          run.running = false;
          return;
        }
        run.timer = setTimeout(function () { step(k + 1); }, steps[k][2]);
      }
      step(0);
      return { ok: true, id: run.id, n: run.n, done: run.done, totalMs: total, error: run.error };
    };

    // How far run `id` got.
    A.status = function (id) {
      var run = state.run;
      if (!run || run.id !== id) return refuse('unknown-run');
      return runInfo(run);
    };

    // Stops the run in progress: no further click is made.
    A.cancel = function () {
      return { ok: true, canceled: stopRun(state.run, 'canceled') };
    };

    A.ping = function () {
      var p = coherentProblem() || canvasProblem(findCanvas());
      return { ok: true, ready: !p, problem: p, running: runIsLive(state.run) };
    };

    window.__MSFSBA_BBS757EFB = A;
    return MARK;
  } catch (e) {
    return 'MSFSBA_BBS757_EFB_ERROR:' + (e && e.message);
  }
})()
