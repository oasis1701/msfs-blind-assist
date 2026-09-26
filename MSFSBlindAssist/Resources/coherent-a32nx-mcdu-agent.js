// In-page agent for the FlyByWire A32NX MCDU, installed once into the "A32NX_MCDU"
// Coherent view (the Headwind A330 fork's "A339X_MCDU" hosts the same instrument under
// <a339x-mcdu>) by CoherentA32nxMcduClient via Runtime.evaluate, then called per poll.
//
// It replaces the SimBridge MCDU relay as the READ and KEY transport. FBW's own
// A320_Neo_CDU_MainDisplay.sendUpdate() builds the relay payload from plain fields on
// the legacy display object (_labels, _lines, _title, _pageCurrent/_pageCount, _arrows,
// scratchpadDisplay, annunciators) gated on the two MCDU power busses; read() rebuilds
// that {left, right} object from the same fields, so ONE C# decoder (FbwMcduFormat /
// FbwMcduUpdate) serves both transports. Parity is on the fields the decoder READS —
// the relay also carries integralBrightness / displayBrightness, which it ignores — and
// the node test pins the agent against a transcription of sendUpdate(), not FBW's
// source, so an FBW change to the payload shape must be re-transcribed here by hand.
//
// ONE instrument serves BOTH MCDU screens (panel.cfg declares a single mcdu.html gauge
// on the shared MCDU texture), so — as over SimBridge — left and right carry the same
// screen and only the Captain side is driven.
//
// ES5 only (Coherent GT = Chromium 49): var, no arrow functions, no let/const, no
// template strings, no String.prototype.includes; every entry point in try/catch.
(function () {
  var A = {};

  A.element = function () {
    return document.querySelector("a32nx-mcdu") || document.querySelector("a339x-mcdu");
  };

  A.fms = function () {
    var el = A.element();
    return (el && el.fsInstrument && el.fsInstrument.legacyFms) || null;
  };

  function simVar(name, unit) {
    try {
      if (typeof SimVar !== "undefined" && typeof SimVar.GetSimVarValue === "function") {
        return SimVar.GetSimVarValue(name, unit);
      }
    } catch (e) {}
    return null;
  }

  function isTrue(v) { return v === true || v === 1 || v === "1"; }

  function cellRow(row) {
    // A row is [left, right, center]; a missing cell is "" as in the relay payload.
    var out = ["", "", ""];
    if (!row) return out;
    for (var i = 0; i < 3; i++) {
      var c = row[i];
      out[i] = (c === undefined || c === null) ? "" : String(c);
    }
    return out;
  }

  function emptyAnnunciators() {
    return { fmgc: false, fail: false, mcdu_menu: false, fm1: false, ind: false, rdy: false, blank: false, fm2: false };
  }

  function emptyLines() {
    var lines = [];
    for (var i = 0; i < 12; i++) lines.push(["", "", ""]);
    return {
      lines: lines,
      scratchpad: "",
      title: "",
      titleLeft: "",
      page: "",
      arrows: [false, false, false, false],
      annunciators: emptyAnnunciators(),
      displayBrightness: 0
    };
  }

  // Mirror of sendUpdate()'s screenState: 12 rows interleaving label[k] / line[k].
  function screenState(fms) {
    var lines = [];
    for (var k = 0; k < 6; k++) {
      lines.push(cellRow(fms._labels && fms._labels[k]));
      lines.push(cellRow(fms._lines && fms._lines[k]));
    }
    var sp = fms.scratchpadDisplay;
    var spText = "", spColor = "white";
    try {
      if (sp) {
        if (typeof sp.getText === "function") spText = String(sp.getText() || "");
        if (typeof sp.getColor === "function") spColor = String(sp.getColor() || "white");
      }
    } catch (e) {}
    var page = "";
    if (typeof fms._pageCount === "number" && fms._pageCount > 0) {
      page = "{small}" + fms._pageCurrent + "/" + fms._pageCount + "{end}";
    }
    var arrows = [false, false, false, false];
    if (fms._arrows && fms._arrows.length) {
      for (var a = 0; a < 4; a++) arrows[a] = !!fms._arrows[a];
    }
    return {
      lines: lines,
      scratchpad: "{" + spColor + "}" + spText + "{end}",
      title: (fms._title === undefined || fms._title === null) ? "" : String(fms._title),
      titleLeft: "",
      page: page,
      arrows: arrows
    };
  }

  function sideFrom(state, annunciators) {
    var side = {};
    for (var k in state) if (Object.prototype.hasOwnProperty.call(state, k)) side[k] = state[k];
    side.annunciators = annunciators || emptyAnnunciators();
    return side;
  }

  // The relay's "update:{...}" body — {left, right}, each the screen or the
  // unpowered empty shape — serialised as JSON. {ok:false} when the instrument is
  // not reachable (view still loading, aircraft unloading).
  A.read = function () {
    try {
      var fms = A.fms();
      if (!fms) return JSON.stringify({ ok: false, error: "MCDU not ready" });

      var mcdu1Powered = isTrue(simVar("L:A32NX_ELEC_AC_ESS_SHED_BUS_IS_POWERED", "bool"));
      var mcdu2Powered = isTrue(simVar("L:A32NX_ELEC_AC_2_BUS_IS_POWERED", "bool"));

      var left = emptyLines(), right = emptyLines();
      if (mcdu1Powered || mcdu2Powered) {
        var state = screenState(fms);
        var ann = fms.annunciators || {};
        if (mcdu1Powered) left = sideFrom(state, ann.left);
        if (mcdu2Powered) right = sideFrom(state, ann.right);
      }
      return JSON.stringify({ ok: true, content: { left: left, right: right } });
    } catch (e) {
      return JSON.stringify({ ok: false, error: (e && e.message) ? e.message : String(e) });
    }
  };

  // Press one Captain-MCDU key ("INIT", "L1", "DOT", "CLR", ...). The relay handler does
  // SimVar.SetSimVarValue("H:A320_Neo_CDU_1_BTN_<key>") from inside this same page; the
  // instrument receives that as an hEvent on its own bus (McduFsInstrument's
  // HEventPublisher, fed by onInteractionEvent). We take the same road in order of
  // fidelity: the publisher's own dispatch, then a bus publish, then the display's
  // onEvent handler directly. Returns the path used, so a live probe can tell.
  A.press = function (key) {
    try {
      var el = A.element();
      var inst = el && el.fsInstrument;
      if (!inst) return "no-instrument";
      var name = "A320_Neo_CDU_1_BTN_" + key;

      var pub = inst.hEventPublisher;
      if (pub && typeof pub.dispatchHEvent === "function") { pub.dispatchHEvent(name); return "dispatchHEvent"; }

      var bus = inst.bus;
      if (bus && typeof bus.pub === "function") { bus.pub("hEvent", name, true); return "bus.pub"; }

      var fms = inst.legacyFms;
      if (fms && typeof fms.onEvent === "function") { fms.onEvent("1_BTN_" + key); return "onEvent"; }

      return "no-dispatch-path";
    } catch (e) {
      return "error: " + ((e && e.message) ? e.message : String(e));
    }
  };

  // Cheap liveness check while the MCDU window is closed (socket kept warm for
  // D / Shift+D). "ready" when the instrument is reachable, "loading" otherwise.
  A.ping = function () {
    try { return A.fms() ? "ready" : "loading"; } catch (e) { return "loading"; }
  };

  window.__MSFSBA_A32NX_MCDU = A;
  return "MSFSBA_A32NX_MCDU_INSTALLED";
})();
