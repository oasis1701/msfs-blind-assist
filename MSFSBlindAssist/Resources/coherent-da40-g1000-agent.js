// MSFSBA in-page agent for the COWS DA40's Working Title G1000 - BOTH displays.
//
// SCOPE, deliberately narrow: this scrapes ONLY what the display alone knows — the CAS
// window, the FMA, the navigation source and the softkey labels. Airspeed, altitude,
// vertical speed, heading and the barometric setting are NOT scraped, because they are
// stock SimVars MSFSBA already reads, and the DOM is a far worse source for them: the
// airspeed box is a SCROLLER, so its textContent is the whole digit strip
// ("210987654321 123456789012-2109876543210...") rather than the value. Scraping a number
// that SimConnect already answers correctly would be choosing the fragile source.
//
// ONE agent file serves the PFD and the MFD, because CoherentDisplayClient installs one
// agent per view and the two displays are the same instrument with different pages up.
// A.side() decides which by what is in the DOM, and A.rows() renders the half that view
// actually has - so neither window ever reports the other's empty selectors as missing.
//
// Installed once per connection under window.__MSFSBA_DA40G1000.
(function () {
    var A = {};

    A.VERSION = 16;

    function visible(el) {
        if (!el) return false;
        var s = window.getComputedStyle(el);
        if (s.display === "none" || s.visibility === "hidden") return false;
        var r = el.getBoundingClientRect();
        return r.width > 0 && r.height > 0;
    }
    A.visible = visible;

    // ⚠️ TEXT THE SCREEN IS NOT SHOWING MUST NOT BE READ, AND textContent SHOWS IT ALL.
    //
    // Every G1000 editable field keeps TWO copies of its value in the DOM - the edit-mode
    // one and the display one - and hides whichever is not in use. Aux System Setup's
    // Minimum Length is the case that found it:
    //
    //     DIV.number-input
    //       DIV.number-input-active   "03000FT"   display:none
    //       DIV.number-input-inactive "3000FT"    shown
    //
    // so the row read "Minimum Length: 03000 FT 3000 FT" - the same number twice, once in
    // a zero-padded edit form the pilot cannot see. Same shape wherever a field can be
    // typed into, which is most of the setup and waypoint pages.
    //
    // The walk REJECTS a hidden element, which skips its whole subtree - so this is also
    // cheaper than what it replaces: one style lookup per element instead of one per text
    // node, and nothing at all inside a hidden branch.
    function walkVisibleText(el, push) {
        if (!el) return;
        var w = document.createTreeWalker(el,
            NodeFilter.SHOW_ELEMENT | NodeFilter.SHOW_TEXT, {
                acceptNode: function (n) {
                    if (n.nodeType !== 1) return NodeFilter.FILTER_ACCEPT;
                    var st;
                    try { st = window.getComputedStyle(n); }
                    catch (e) { return NodeFilter.FILTER_SKIP; }
                    if (st.display === "none" || st.visibility === "hidden" ||
                        parseFloat(st.opacity) === 0) return NodeFilter.FILTER_REJECT;
                    // Descend into it, but the element itself is not text.
                    return NodeFilter.FILTER_SKIP;
                }
            }, false);
        var n;
        while ((n = w.nextNode())) push(n.nodeValue || "");
    }

    function text(el) {
        if (!el) return "";
        // textContent concatenates with no separator; this reproduces that exactly, minus
        // whatever the screen is hiding.
        var buf = "";
        walkVisibleText(el, function (t) { buf += t; });
        return buf.replace(/\s+/g, " ").trim();
    }

    // textContent CONCATENATES with no separator, so a pane full of little spans reads
    // back as "Timer0:00:00UpStart?VNE172KT On". Collecting the text NODES and joining
    // them with a space is what turns that into "Timer 0:00:00 Up Start? VNE 172 KT On".
    function spacedText(el) {
        if (!el) return "";
        var parts = [];
        walkVisibleText(el, function (raw) {
            var t = raw.replace(/\s+/g, " ").trim();
            if (t) parts.push(t);
        });

        // Join with a space BETWEEN WORDS but not inside a number. The G1000 renders a
        // clock and a scrolling readout as one span per character, so a blanket space
        // turns 0:00:00 into "0 : 0 0 : 0 0" - unreadable as a time. Two adjacent
        // digit-ish characters belong to one value; anything else gets the space.
        var out = "";
        for (var i = 0; i < parts.length; i++) {
            if (!out) { out = parts[i]; continue; }
            var a = out.charAt(out.length - 1);
            var b = parts[i].charAt(0);
            // Two adjacent digit-ish characters belong to one value. Punctuation binds
            // too: the G1000 renders a unit list, a date and a co-ordinate one span per
            // token, so a blanket space produced "Feet( FT,FPM )" and "31 - AUG - 26"
            // where the screen says "Feet(FT,FPM)" and "31-AUG-26".
            //
            // A DASH IS NOT GLUE, however date-like it looks. Binding it closed
            // "31 - AUG - 26" into a tidier date and, in the same stroke, closed the CAS
            // alert window's "PITOT HT OFF - Pitot heat is off." into
            // "PITOT HT OFF-Pitot heat is off." - the dash there separates the
            // abbreviation from its meaning, and running them together is a real loss on
            // the one pane whose whole job is spelling an abbreviation out. The date
            // reads perfectly well spaced; the alert does not read well closed up.
            var glue = (/[0-9.:]/.test(a) && /[0-9.:]/.test(b)) ||
                       /[([]/.test(a) || /[)\],.]/.test(b) ||
                       /[\u00B0]/.test(a) || /[\u00B0]/.test(b);
            out += (glue ? "" : " ") + parts[i];
        }

        // A field the pilot has not filled in renders as a row of underscores. "blank"
        // is what that means, and it is one word instead of five. An unset TIME is
        // "__:__" - underscore runs with a separator BETWEEN them - and matching the runs
        // alone turned that into "blank :blank", which reads as two empty fields with a
        // colon between rather than as one empty time. The separators are swallowed into
        // the same match.
        // The same field can be blanked with DASHES instead ("--:--" for an unset time
        // offset), so both placeholder characters are collapsed. A single dash between
        // two words is not a placeholder and must survive - "31-AUG-26" is a date - which
        // is why each alternative needs a RUN of at least two of its own character.
        return out.replace(/_(?:[\s:.\/-]*_)+/g, "blank")
                  .replace(/-(?:[\s:.\/]*-)+/g, "blank")
                  .replace(/\s+/g, " ").trim();
    }

    // THE FIRST MATCH IS NOT ALWAYS THE ONE ON SCREEN.
    //
    // The G1000 keeps more than one copy of several of its blocks in the DOM — a full-width
    // one and a half-width one for the split layouts — and only one is ever rendered. A
    // plain querySelector takes whichever comes first in document order, which on this
    // aeroplane is the HIDDEN one.
    //
    // That is not a cosmetic difference. The PFD carries TWO ".cas-display" blocks of 32
    // rows each; the first is display:none with stale content, so A.cas() read the dead
    // copy, found every row hidden, and reported "CAS messages: none" while ECU A FAIL,
    // ECU B FAIL and PITOT HT OFF were on the screen. On an aeroplane whose only channel
    // for most failures IS the CAS window, that is the worst thing this agent could get
    // wrong, and it read as a clean scan rather than as an error.
    //
    // So anything that can plausibly be duplicated is looked up through here.
    function firstVisible(sel, root) {
        var all = (root || document).querySelectorAll(sel);
        for (var i = 0; i < all.length; i++) {
            if (visible(all[i])) return all[i];
        }
        return null;
    }
    A.firstVisible = firstVisible;

    // ⚠️ classList() returns an ARRAY, so its indexOf is an EXACT-token search. That is
    // right for a whole class name ("cyan", "hide-element") and WRONG for a partial one:
    // classList(e).indexOf("row") never matches "mfd-system-setup-row-right", because no
    // token IS "row". Two call sites assumed a string and both failed silently - the
    // cursor read back with no label at all, and pfdPopout called .split on an array,
    // threw, and left summary() empty so every bezel key announced its fallback text.
    // Use this when the class is a FRAGMENT.
    /// The aeroplane marks something as not-shown under MORE THAN ONE NAME, and they
    /// differ by a single letter: "hide-element" AND "hidden-element" both occur on the
    /// MFD. Neither uses display:none, so their CHILDREN still pass an offsetParent
    /// visibility test - which is how a hidden layout got welded into the wind box - and
    /// a reader that knows only one of the two names silently reads the other aloud.
    ///
    /// Found by inventorying every class token the instrument uses and diffing that
    /// against the names this agent mentions; see tools/g1000-class-inventory.js. Any new
    /// marking should be found that way rather than by waiting for it to be reported.
    function isMarkedHidden(el) {
        var parts = classList(el);
        for (var i = 0; i < parts.length; i++) {
            if (parts[i] === "hide-element" || parts[i] === "hidden-element") return true;
        }
        return false;
    }

    function hasClassContaining(el, fragment) {
        var parts = classList(el);
        for (var i = 0; i < parts.length; i++) {
            if (parts[i].indexOf(fragment) >= 0) return true;
        }
        return false;
    }

    function classList(el) {
        var cn = el.className;
        if (cn && cn.baseVal !== undefined) cn = cn.baseVal;
        return typeof cn === "string" ? cn.split(/\s+/) : [];
    }

    // ---------------------------------------------------------------- which display
    //
    // The MFD carries the engine strip and a paged content area; the PFD carries the CAS
    // window and the HSI. Detecting by CONTENT rather than by a name passed in from
    // outside means the agent cannot be told the wrong thing, and a view that somehow has
    // both would still render both halves rather than half a screen.
    // ASK THE INSTRUMENT, NOT THE PAGE. Detecting by content (".eis" / ".mfd-page") read
    // the PFD as an MFD - both views carry some of the other's markup, unused and
    // invisible - and the whole PFD then rendered as MFD rows: no CAS, no FMA, no
    // navigation source, on the one display where those are the point.
    A.side = function () {
        return document.querySelector("wtg1000-mfd") ? "MFD" : "PFD";
    };

    // ---------------------------------------------------------------- CAS
    //
    // The CAS window PRE-ALLOCATES its rows: 32 of them on this aircraft, and a cleared
    // message leaves its slot in the DOM with display:none rather than removing it. So a
    // row counts only when it is BOTH visible and non-empty — reading the node list alone
    // would report every message the flight has ever raised.
    //
    // Severity comes from the CLASS LIST and never from colour. The rows carry a "new"
    // class while the message is flashing, which changes the rendered colour underneath
    // it, so a colour test reports the same caution differently depending on when it is
    // sampled.
    A.cas = function () {
        // Every visible copy, not the first copy. See firstVisible above for what this
        // cost before: a clean "no messages" over three standing cautions.
        var hosts = document.querySelectorAll(".cas-display");
        var rows = [];
        for (var h = 0; h < hosts.length; h++) {
            if (!visible(hosts[h])) continue;
            var found = hosts[h].querySelectorAll(".annunciation");
            for (var f = 0; f < found.length; f++) rows.push(found[f]);
        }

        var out = [];
        var seen = {};

        for (var i = 0; i < rows.length; i++) {
            var row = rows[i];
            var t = text(row);
            if (!t || !visible(row)) continue;

            var cls = classList(row);
            var severity = "advisory";
            if (cls.indexOf("warning") >= 0) severity = "warning";
            else if (cls.indexOf("caution") >= 0) severity = "caution";
            else if (cls.indexOf("safe-op") >= 0) severity = "status";

            // Two visible copies would otherwise report every caution twice.
            if (seen[t]) continue;
            seen[t] = 1;

            out.push({ text: t, severity: severity, isNew: cls.indexOf("new") >= 0 });
        }

        return out;
    };

    // ---------------------------------------------------------------- FMA
    A.fma = function () {
        function one(sel) {
            var e = firstVisible(sel);
            return e ? text(e) : "";
        }
        // ⚠️ THE LATERAL HALF OF THE FMA HAS NO CSS CLASS, AND WAS THEREFORE NEVER READ.
        // The vertical modes carry `.fma-ap-vertical-modes`; the lateral mode - GPS, HDG, ROL,
        // LOC, VOR, and whether each is ARMED or CAPTURED - sits in an unclassed sibling. So
        // MSFSBA announced "FLC116KTALTS" faithfully while the entire left half of the strip
        // was invisible, which is why a NAV mode that was engaged and NOT capturing looked
        // exactly like one that was working. Measured live: that sibling read "GPS".
        //
        // Anchored on `.fma-ap-displayed`, which DOES have a class and sits immediately after
        // it - a position relative to a named neighbour, rather than a bare nth-child that
        // would silently pick up the wrong box the moment the layout gains an element.
        function lateral() {
            var anchor = firstVisible(".fma-ap-displayed");
            if (!anchor || !anchor.parentElement) return "";
            var kids = anchor.parentElement.children;
            for (var i = 0; i < kids.length; i++) {
                if (kids[i] === anchor) return i > 0 ? text(kids[i - 1]) : "";
            }
            return "";
        }

        return {
            autopilot: one(".fma-ap-label"),
            yawDamper: one(".fma-yd-label"),
            lateral: lateral(),
            vertical: one(".fma-ap-vertical-modes")
        };
    };

    // ------------------------------------------------------- navigation source
    A.nav = function () {
        function one(sel) {
            var e = firstVisible(sel);
            return e ? text(e) : "";
        }
        return {
            source: one(".hsi-nav-source") || one(".hsi-map-nav-src"),
            sensitivity: one(".hsi-nav-sensitivity") || one(".hsi-map-nav-sensitivity"),
            suspended: (one(".hsi-nav-susp") || one(".hsi-map-nav-susp")) !== "",
            crossTrack: one(".hsi-gps-xtrack-number"),
            message: one(".hsi-gps-msg")
        };
    };

    // ------------------------------------------------- the PFD's optional windows
    //
    // Five things the PFD draws that NOTHING ELSE ON THE AEROPLANE ANSWERS, and that the
    // first pass missed because they are all OFF by default - they appear only once the
    // pilot switches them on from PFD Opt, so a scrape taken at rest finds an empty
    // screen and concludes there is nothing there.
    //
    // That is exactly why they matter: a sighted pilot turns a bearing pointer on BECAUSE
    // they want to look at it. Being able to press the softkey and then not read the
    // result is the worst of both worlds.
    //
    // Every one is reported only while VISIBLE, which is the same rule the popout panes
    // use, and for the same reason - the markup is permanent and the visibility is not.
    /// THE WIND BOX, which nothing read at all.
    ///
    /// The PFD draws it and MSFSBA never looked, so a pilot had no way to know what the
    /// aeroplane thought the wind was - or, on the ground, that it thought nothing yet.
    /// The G1000's own words there are "NO WIND DATA"; that text is the AEROPLANE's, not
    /// something MSFSBA invented, and it is correct until the air data has a valid wind.
    ///
    /// ⚠️ The overlay holds several alternative LAYOUTS at once - opt1, opt2 - and the
    /// unused ones are classed hide-element. Their CHILDREN still pass an offsetParent
    /// visibility test, because hide-element does not use display:none, so reading the
    /// overlay's textContent yields "NO WIND DATA000360°0KT": the live text with two dead
    /// layouts welded onto it. Skip any child carrying hide-element, and check that class
    /// rather than trusting the visibility of what is inside it. Fourth time on this
    /// aeroplane that hide-element has had to be handled by name.
    A.wind = function () {
        var w = firstVisible(".wind-overlay");
        if (!w) return "";

        var parts = [];
        for (var i = 0; i < w.children.length; i++) {
            var c = w.children[i];
            if (isMarkedHidden(c)) continue;
            if (!visible(c)) continue;

            var t = text(c);
            if (t && parts.indexOf(t) < 0) parts.push(t);
        }

        return parts.join(" ");
    };

    /// THE RADIO BOXES, including the two facts a pilot cannot get from a SimVar.
    ///
    /// Which radio the tuning knob will act on, and which COM would actually TRANSMIT, are
    /// display state - the aeroplane marks them with a `select` class on the frequency
    /// container and a `transmit-selected` on the active frequency. Nothing read either,
    /// so a pilot turning the NAV knob had no way to know it was about to retune NAV 2
    /// rather than NAV 1, and no way at all to know which COM was live. Both were found by
    /// class inventory rather than by being reported.
    ///
    /// Structure, read from the live PFD: `.navcom-frequencies left` holds the two NAV
    /// rows and `right` the two COM rows; each row is a
    /// `.navcom-frequencyelement-container` carrying `select` when the knob is on it, with
    /// `.navcom-freqstandby` and `.navcom-freqactive` inside.
    // WHICH FACILITY THE TUNED COM BELONGS TO — "VCBI GROUND" beside the frequency.
    //
    // The G1000 names the station for the active COM in its own box on the PFD, and a
    // sighted pilot therefore knows they are about to talk to Ground rather than Tower
    // without decoding the number. We read every frequency and never read the name; found
    // by the coverage sweep, which saw it drawn on most PFD pages and absent from everything
    // MSFSBA said.
    //
    // The box exists on the PFD only (measured: one .com-info-box there, none on the MFD),
    // so this contributes nothing on the MFD rather than needing a side test.
    A.pushComStation = function (rows) {
        var box = firstVisible(".com-info-box");
        if (!box) return;
        var name = text(box);
        if (name) rows.push("COM station: " + name);
    };

    A.radios = function () {
        var out = [];

        var sides = [[".navcom-frequencies.left", "NAV"], [".navcom-frequencies.right", "COM"]];

        for (var s = 0; s < sides.length; s++) {
            var box = firstVisible(sides[s][0]);
            if (!box) continue;

            var rows = box.querySelectorAll(".navcom-frequencyelement-container");
            var n = 0;

            for (var i = 0; i < rows.length; i++) {
                if (!visible(rows[i])) continue;
                n++;

                var active = rows[i].querySelector(".navcom-freqactive");
                var standby = rows[i].querySelector(".navcom-freqstandby");
                if (!active && !standby) continue;

                var bits = [sides[s][1] + " " + n];
                if (active) bits.push("active " + text(active));
                if (standby) bits.push("standby " + text(standby));

                // The knob acts on exactly one radio per side, and turning it while the
                // box is on the other one retunes a radio the pilot did not mean to.
                if (hasClassContaining(rows[i], "select")) bits.push("TUNING");

                // Which COM the transmit key would key. There is no SimVar for it.
                if (active && hasClassContaining(active, "transmit-selected")) {
                    bits.push("TRANSMIT");
                }

                // ⚠️ A FAILED RADIO, WHICH IS WHY A KNOB CAN DO NOTHING AND LOOK BROKEN.
                // Measured live on this airframe: NAV 2 and COM 2 carry "failed-instr" while
                // NAV 1 and COM 1 do not, and the class does NOT follow the tuning cursor -
                // pushing the cursor from COM 2 to COM 1 left the class on COM 2 and did not
                // put it on COM 1. So it is a real state, not selection styling.
                //
                // It cost a pilot a session: two knob pushes parked the cursor on COM 2, the
                // knob then moved nothing, and the radios were reported as broken. Nothing is
                // ANNOUNCED for it - the pilot's ruling is that a state the display can report
                // is scanned for, not spoken at them - but the row has to CARRY it or there is
                // nothing to scan.
                if (hasClassContaining(rows[i], "failed-instr")) bits.push("FAILED");

                out.push(bits.join(", "));
            }
        }

        return out;
    };

    A.pfdWindows = function () {
        var out = [];

        function shown(sel) {
            var e = document.querySelector(sel);
            return e && visible(e) ? e : null;
        }

        // THE THREE SELECTED VALUES, which are what the pilot has ASKED FOR rather than
        // what the aeroplane is doing. Airspeed, altitude, heading and vertical speed are
        // all readable as SimVars, but the SELECTED ones are the targets, and on a display
        // that is where they live. Altitude is always on the screen; heading and course
        // sit in their own boxes; the selected vertical speed appears only in VS mode.
        // The radios moved to the main row list so BOTH displays get them - see the note
        // there. Pushing them here as well would read every frequency twice on the PFD.

        var wind = A.wind();
        if (wind) out.push("Wind: " + wind);

        var pre = shown(".preselect-box");
        if (pre) {
            var alt = spacedText(pre);
            if (alt) out.push("Selected altitude: " + alt + " feet");
        }

        // The boxes carry their own abbreviation ("HDG 006"), so the label is stripped
        // rather than lower-cased - "Selected hdg 006" reads as a typo, not a heading.
        //
        // ⚠️ BUT THE ABBREVIATION IS THE FACT, NOT NOISE — IT WAS STRIPPED AND THEN A FIXED
        // WORD WAS PUT BACK, AND THE TWO DISAGREED. The lower box swaps between CRS and DTK
        // and this said "course" for both, so a pilot tracking a GPS leg with the HSI
        // showing "DTK 220" heard "Selected course: 220°" — told they had dialled something
        // they had not. They are different quantities: CRS is the course the PILOT selects
        // for VOR/LOC or OBS, DTK is the DESIRED TRACK the GPS computes from the active leg,
        // which no knob sets. Which one is showing also says whether the HSI is in OBS mode,
        // and that is not recoverable from the number. Found by the coverage sweep, which
        // reported "DTK" drawn on nine of fifteen pages and never spoken.
        var ABBREV = { HDG: "heading", CRS: "course", DTK: "desired track" };
        function selected(sel, label) {
            var e = shown(sel);
            if (!e) return;
            var raw = spacedText(e).trim();
            var m = /^(HDG|CRS|DTK)\s*/i.exec(raw);
            var v = raw.replace(/^(HDG|CRS|DTK)\s*/i, "").trim();
            // A "Selected" value the pilot did NOT select would be a lie, so DTK drops the
            // word rather than borrowing it.
            var word = m ? ABBREV[m[1].toUpperCase()] : label;
            if (!v) return;
            out.push((word === "desired track" ? "Desired track: " : "Selected " + word + ": ") + v);
        }
        selected(".hdg-box", "heading");
        selected(".dtk-box", "course");

        var svs = shown(".vsi-selected-vs");
        if (svs) {
            var vs = spacedText(svs);
            if (vs) out.push("Selected vertical speed: " + vs + " feet per minute");
        }

        // WHERE THE NEXT WAYPOINT IS. Bearing and distance to the active fix - the two
        // numbers a pilot navigating by the flight plan looks at most, and neither is a
        // SimVar MSFSBA reads elsewhere.
        var brg = shown(".FixBrgValue");
        var dis = shown(".FixDistValue");
        if (brg || dis) {
            var bits = [];

            // The IDENT, which was the one part missing: bearing and distance to an
            // unnamed fix say how far without saying to WHAT. It has no class of its own -
            // it is the plain .dataField beside the two that do - so it is found by
            // elimination rather than by name.
            var fields = document.querySelectorAll(".dataField");
            for (var f = 0; f < fields.length; f++) {
                var c = classList(fields[f]);
                if (c.indexOf("FixBrgValue") >= 0 || c.indexOf("FixDistValue") >= 0) continue;
                var id = text(fields[f]);
                if (id && /^[A-Z0-9]{2,6}$/.test(id)) { bits.push(id); break; }
            }

            if (brg) bits.push("bearing " + spacedText(brg));
            if (dis) bits.push("distance " + spacedText(dis));
            out.push("Active waypoint: " + bits.join(", "));
        }

        // Vertical deviation - the glideslope or glidepath needle, and whether there is
        // one at all. Read field-wise: run together it says "GNOGS" for "G, NO GS".
        var vdev = shown(".verticaldev-box");
        if (vdev) {
            var v = A.fieldsOf(vdev);
            // "G, NO, GS" is the fields of "G" and "NO GS" - the scale letter and the
            // flag saying there is no signal behind it. Said plainly it is one fact.
            if (/NO,?\s*GS/i.test(v)) v = "no glideslope signal";
            else if (/NO,?\s*GP/i.test(v)) v = "no glidepath signal";
            if (v) out.push("Vertical deviation: " + v);
        }

        // The transponder as the DISPLAY shows it - code, mode and whether it is
        // identing - which is the one place the mode and the ident are visible together.
        // The transponder in words. The display abbreviates - "XPDR 1000 STBY" - and the
        // abbreviation is the least useful part: the pilot already knows it is the
        // transponder, and STBY versus ALT is the difference between being seen by radar
        // and not. The code is spelt out so a screen reader reads four digits rather than
        // "one thousand", because a squawk is four characters and not a number.
        // THE INFORMATION BAR along the bottom - what a sighted pilot takes in at a glance
        // without moving their eyes. Outside air temperature, ISA deviation, true
        // airspeed, ground speed. Some of these have SimVars MSFSBA answers elsewhere, and
        // they are read here anyway: the point of a display window is to say what the
        // DISPLAY says, and a pilot asking "what does the bottom of the PFD show" should
        // not have to assemble it from four other hotkeys.
        var info = [];
        var bar = [[".bip-oat", ""], [".bip-isa", ""], [".airspeed-tas-display", ""],
                   [".bip-gs", ""]];
        for (var b = 0; b < bar.length; b++) {
            var e = shown(bar[b][0]);
            if (!e) continue;
            var v = A.fieldsOf(e).replace(/,\s*/g, " ").trim();
            if (v) info.push(v);
        }
        if (info.length) out.push("Information bar: " + info.join(", "));

        // The timer and the clock share a box and both matter: the timer is what a pilot
        // starts on a hold or an approach, the clock is UTC for a position report.
        var clock = shown(".bip-time");
        if (clock) {
            // The box labels its clock "UTC" and the value carries the suffix too, so the
            // fields join into "UTC 06:24:02 UTC". One is a label and one is a unit; the
            // pilot needs to hear it once.
            var ct = A.fieldsOf(clock).replace(/,\s*/g, " ").trim()
                      .replace(/\s+UTC$/, "").replace(/\s+/g, " ").trim();
            if (ct) out.push("Time: " + ct);
        }

        // THE ALTIMETER SETTING, and whether it is on STANDARD. "STD BARO" means 29.92 is
        // set, which above the transition altitude is correct and below it is an error
        // worth hearing about - and it is a MODE, not a number, so no barometric readout
        // elsewhere in MSFSBA reports it.
        var press = shown(".pressure-box");
        if (press) {
            var pv = spacedText(press);
            if (pv) out.push("Altimeter setting: " + pv);
        }

        // Which V-speed bugs the airspeed tape is showing. The DA40's own references -
        // Vne, Vg, Vy, Vx, Vr - are set on the Timer/References window, and which are
        // ENABLED is a choice the pilot made that nothing else reports back.
        var bugs = shown(".airspeed-vspeed-bug-container");
        if (bugs) {
            var names = { NE: "Vne", G: "Vg", Y: "Vy", X: "Vx", R: "Vr" };
            var listed = [];
            var raw = A.fieldsOf(bugs).split(/[,\s]+/);
            for (var r2 = 0; r2 < raw.length; r2++) {
                var n = raw[r2].trim();
                if (n && names[n]) listed.push(names[n]);
            }
            if (listed.length) out.push("Speed bugs shown: " + listed.join(", "));
        }

        var xpdr = shown(".xpdr-content");
        if (xpdr) {
            var parts = A.fieldsOf(xpdr).split(", ");
            var bits = [];
            for (var x = 0; x < parts.length; x++) {
                var v = parts[x];
                if (/^XPDR$/i.test(v)) continue;
                if (/^\d{4}$/.test(v)) { bits.push("code " + v.split("").join(" ")); continue; }
                if (/^STBY$/i.test(v)) { bits.push("standby"); continue; }
                if (/^ALT$/i.test(v)) { bits.push("altitude reporting"); continue; }
                if (/^GND$/i.test(v)) { bits.push("ground"); continue; }
                if (/^ON$/i.test(v)) { bits.push("on"); continue; }
                bits.push(v);
            }
            if (bits.length) out.push("Transponder: " + bits.join(", "));
        }

        // Bearing pointers 1 and 2. Each carries the navaid it is pointing at, the
        // bearing to it and the distance - and an unset field renders as underscores,
        // which spacedText already turns into "blank".
        var sides = [["left", "1"], ["right", "2"]];
        for (var i = 0; i < sides.length; i++) {
            var host = shown("." + sides[i][0] + "-brg-ptr-container");
            if (!host) continue;
            var ident = spacedText(host.querySelector("." + sides[i][0] + "-brg-ptr-ident"));
            var crs = spacedText(host.querySelector("." + sides[i][0] + "-brg-ptr-crs"));
            var dist = spacedText(host.querySelector("." + sides[i][0] + "-brg-ptr-dist"));
            var bits = [];
            if (ident) bits.push(ident);
            if (crs) bits.push(crs);
            if (dist) bits.push(dist);
            if (bits.length) out.push("Bearing pointer " + sides[i][1] + ": " + bits.join(", "));
        }

        // FIELD-WISE, not as text. Both of these are little boxes of separate elements,
        // and joined as text they close up into "DME NAV1110.50 blank NM" and
        // "NO WIND DATA 000360 0 KT" - the source running into the frequency, the
        // direction into the speed. Same lesson as the nearest lists.
        var dme = shown(".DME-window");
        if (dme) out.push("DME: " + A.fieldsOf(dme));

        // The wind window has four settings - off and three display modes - and says so
        // itself when it has no data to show, which is worth passing on rather than
        // hiding: "no wind data" is why the number is missing.
        // The wind window has four settings - off and three display modes - and says so
        // itself when it has no data to show. THE THREE MODES LOOK IDENTICAL WITHOUT A
        // WIND SOLUTION, which is why they were reported as doing nothing: each mode has
        // its own sub-block, all three stay hidden while the G1000 has no wind, and the
        // overlay shows its placeholder instead. Measured: Off hides the overlay and all
        // three options show it, so the softkeys work. Saying WHY there is no number is
        // the difference between a broken-looking control and an honest one.
        var wind = shown(".wind-overlay");
        if (wind) {
            var w = A.fieldsOf(wind);
            if (/NO\s*WIND\s*DATA/i.test(w)) {
                w = "no wind data - the G1000 needs a wind solution, which it computes in flight";
            }
            out.push("Wind: " + w);
        }

        // Approach minimums, shown against the altitude tape once the pilot has set one.
        var mins = shown(".mins-temp-comp-container");
        if (mins) out.push("Minimums: " + spacedText(mins));

        return out;
    };

    // ---------------------------------------------------------------- softkeys
    //
    // The bezel's twelve softkeys, in order, with the label the display is CURRENTLY
    // showing — they change per page, so they have to be read live rather than laid out
    // as a fixed list. A blank slot is a real state and is reported as such: the pilot
    // needs to know a key does nothing here, not that it is missing.
    A.softkeys = function () {
        var host = firstVisible(".softkeys-container");
        if (!host) return [];

        var tabs = host.querySelectorAll(".softkey-tab");
        var out = [];

        for (var i = 0; i < tabs.length; i++) {
            var tab = tabs[i];
            var label = text(tab.querySelector(".softkey-tab-label"));
            var value = text(tab.querySelector(".softkey-tab-value"));
            var cls = classList(tab);

            out.push({
                index: i + 1,
                label: label,
                value: value,
                active: cls.indexOf("active") >= 0 || cls.indexOf("highlight") >= 0,
                // ⚠️ A GREYED-OUT SOFTKEY READ AS AN ORDINARY ONE. The G1000 disables a
                // softkey it cannot honour on the current page and marks it `text-disabled`
                // (grey, rgb(70,70,70)) — 22 labelled keys across 8 pages, measured live.
                // Without this the pilot hears "Softkey 5: Activate", presses it, and gets
                // silence with nothing to say why. The Flight Plan Catalog is the worst
                // case: NINE of its twelve keys are dimmed until a flight is focused, and
                // that page IS the documented SimBrief import workflow.
                disabled: cls.indexOf("text-disabled") >= 0
            });
        }

        return out;
    };

    // ------------------------------------------------------------ popout panes
    //
    // Several softkeys do not change the softkey row at all - they open a DIALOG over the
    // display. Tmr/Ref and Nearest are the two on the PFD, and until this existed they
    // were pressable but unreadable: the key worked, and nothing could be read back.
    //
    // A dialog is open when its OPACITY is not zero, and by nothing else. All four of the
    // PFD's dialogs are permanently display:block, visibility:visible, 310 by 220, and
    // carrying the class "quickclosed" whether open or shut - measured. A closed one is
    // simply transparent. Filtering on display, visibility, size or that class name finds
    // four windows that are not on screen; filtering on opacity finds the one that is.
    //
    // The four are Nearest Airports, Alerts (the full text behind the CAS abbreviations),
    // Timer and References, and ADF/DME Tuning.
    A.panes = function () {
        var out = [];
        var dialogs = document.querySelectorAll(".popout-dialog");

        for (var i = 0; i < dialogs.length; i++) {
            var d = dialogs[i];
            var cls = classList(d).join(" ");
            if (!visible(d)) continue;
            if (parseFloat(window.getComputedStyle(d).opacity || "1") < 0.05) continue;

            var title = text(d.querySelector(".popout-dialog-title")) ||
                        (/nearest-airport/.test(cls) ? "Nearest Airports" : "");
            var lines = A.procChoiceLines(d);

            // THE PAGE SELECTOR. What the FMS knob opens: the six page GROUPS across the
            // bottom and the PAGES of whichever group is current. Read as its own shape
            // because run together it is the useless "MapWPTAuxFPLNRSTEIS" - and because
            // this window is the only way a blind pilot can see where the knob is about
            // to take them. The selector closes itself a second or so after the last
            // turn, so what it says is genuinely transient.
            // ⚠️ A STRUCTURED READ MUST WIN OUTRIGHT, NOT MERELY GO FIRST. Seeding `lines`
            // with the procedure-dialog pairs was not enough: the generic arm below pushes
            // into the SAME array, so Select Departure announced the three tidy lines AND
            // then the welded run they were there to replace.
            if (lines.length) {
                // Already read properly by procChoiceLines - leave it alone.
            } else if (/mfd-pageselect/.test(cls)) {
                var tabs = d.querySelectorAll(".mfd-pageselect-tabs > *");
                var groups = [];
                for (var g = 0; g < tabs.length; g++) {
                    var gt = text(tabs[g]);
                    if (!gt) continue;
                    groups.push(gt + (classList(tabs[g]).indexOf("active-tab") >= 0 ? " (current)" : ""));
                }
                if (groups.length) lines.push("Groups: " + groups.join(", "));

                var pgs = d.querySelectorAll(".mfd-pageselect-group .popout-menu-item");
                for (var q = 0; q < pgs.length; q++) {
                    var pt = text(pgs[q]);
                    if (!pt) continue;
                    var cur = classList(pgs[q]).indexOf("highlight-select") >= 0 ||
                              !!pgs[q].querySelector(".highlight-select");
                    lines.push("  " + pt + (cur ? " (current)" : ""));
                }
                out.push({ title: "Page selector", lines: lines });
                continue;
            }

            // THE PAGE MENU. The MENU key's options for whatever page is up. An entry the
            // page cannot offer right now is CLASSED disabled rather than removed, and
            // saying so is the point: a pilot who cannot see it greyed out would otherwise
            // select it and get silence.
            if (/mfd-pagemenu/.test(cls)) {
                var items = d.querySelectorAll(".popout-menu-item");
                for (var m = 0; m < items.length; m++) {
                    var mt = text(items[m]);
                    if (!mt) continue;
                    var mc = classList(items[m]);
                    var selected = mc.indexOf("highlight-select") >= 0 ||
                                   !!items[m].querySelector(".highlight-select");
                    lines.push(mt
                        + (mc.indexOf("text-disabled") >= 0 ? ", dimmed" : "")
                        + (selected ? ", selected" : ""));
                }
                var back = text(d.querySelector(".mfd-pagemenu-backmessage"));
                if (back) lines.push(back);
                out.push({ title: "Page menu", lines: lines });
                continue;
            }

            // The nearest-airport list has NAMED fields, so it is read as fields rather
            // than as its own textContent - which runs together into "VCBI0200.4 NMILS".
            var items = d.querySelectorAll(".nearest-airport-item");
            // ⚠️ AND AGAIN HERE, BECAUSE THIS METHOD HAS THREE SEPARATE if-CHAINS, NOT ONE.
            // Guarding only the first (the page selector) left this one still pushing into
            // the same `lines`, so Select Departure kept announcing the three tidy pairs AND
            // the welded run underneath them. Whatever reads a dialog properly must stop
            // every later arm, not just the next one.
            if (lines.length) {
                // Already read properly above - leave it alone.
            } else if (items.length) {
                for (var k = 0; k < items.length; k++) {
                    var it = items[k];

                    // The list PRE-ALLOCATES its rows exactly like the CAS window does -
                    // an unused slot renders as "____, 359, __._ NM, VFR, _____ FT", which
                    // is four empty aerodromes read out after the real ones. A row counts
                    // only when its identifier is a real one.
                    var ident = text(it.querySelector(".nearest-airport-name"));
                    if (!ident || /^_+$/.test(ident)) continue;

                    var parts = [
                        text(it.querySelector(".nearest-airport-name")),
                        text(it.querySelector(".nearest-airport-bearing")),
                        text(it.querySelector(".nearest-airport-distance")),
                        text(it.querySelector(".nearest-airport-approach")),
                        text(it.querySelector(".nearest-airport-freqtype")),
                        text(it.querySelector(".nearest-airport-frequency")),
                        text(it.querySelector(".nearest-airport-rwy-number"))
                    ].filter(function (x) { return x; });
                    if (parts.length) lines.push(parts.join(", "));
                }
            } else if (A.groupboxLines(d).length) {
                lines = A.groupboxLines(d);
            } else {
                // One line per row of the pane, so a reader can arrow through it, with
                // the text nodes spaced rather than run together.
                var kids = d.children.length === 1 ? d.children[0].children : d.children;
                for (var c = 0; c < kids.length; c++) {
                    var line = spacedText(kids[c]);
                    if (line) lines.push(line);
                }
                if (!lines.length) {
                    var whole = spacedText(d);
                    if (whole) lines.push(whole);
                }
            }

            // No dialog carries a title element, so the pane names itself with its own
            // first line - "Timer", "Alerts", "ADF/DME TUNING" - which is what a sighted
            // pilot reads at the top of it anyway.
            //
            // A dialog built from GROUPBOXES needs its heading found separately: the
            // header is a plain child that the groupbox walk skips, so shifting the first
            // line instead stole a group heading and the Direct-To window announced itself
            // as "Ident, Facility, City" with its own fields orphaned under it.
            if (!title) {
                for (var h = 0; h < d.children.length; h++) {
                    // FRAGMENT, not a token: the children are "groupbox-container" and
                    // "groupbox mfd-system-setup-group", and an exact-token test skips
                    // only the second - so a groupbox's own text could be stolen as the
                    // dialog title.
                    if (hasClassContaining(d.children[h], "groupbox")) continue;
                    var ht = text(d.children[h]);
                    if (ht && ht.length <= 40) { title = ht; break; }
                }
            }
            if (!title && lines.length && lines[0].charAt(lines[0].length - 1) !== ":") {
                title = lines.shift();
            }
            if (lines.length || title) out.push({ title: title || "Window", lines: lines });
        }

        return out;
    };

    // =========================================================== the MFD
    //
    // Everything below belongs to the multi-function display. It is a different SHAPE of
    // screen from the PFD: a permanent engine strip down the left, a NAV/COM box and a
    // navigation data bar across the top, and one PAGE in the middle that the FMS knob
    // steps through. The softkey row is shared and is read by the same A.softkeys().

    // ------------------------------------------------------------ power-up screen
    //
    // The MFD comes up on a confirmation screen carrying the NAVIGATION DATA CYCLE and
    // its expiry, and nothing else on the aeroplane says when the database runs out - a
    // real airworthiness item for IFR, and the one thing a sighted pilot reads there
    // before pressing ENT. Reported as its own rows, because until it is acknowledged the
    // MFD shows no pages at all and every other selector below is empty: without this the
    // window would read as broken rather than as waiting.
    A.startup = function () {
        var sc = document.querySelector(".startup-confirm-screen");
        if (!sc || !visible(sc)) return null;
        return spacedText(sc);
    };

    // ------------------------------------------------------------- NAV/COM box
    //
    // Two radios per side, each rendering ACTIVE and STANDBY in one container whose
    // textContent runs them together with the tuning scroller between them
    // ("110.30100.12110.50"). The frequencies themselves are stock SimVars MSFSBA already
    // reads on the Radios panel, so what is taken here is what the DISPLAY alone knows:
    // which radio the tuning cursor is on, and which one is transmitting.
    A.navcom = function () {
        function oneSide(rootSel, kind) {
            // ⚠️ firstVisible, NEVER document.querySelector - this instrument keeps hidden
            // duplicates of its boxes mounted, which is what made the CAS block read off a
            // dead copy and report "none" over three live messages. The MFD carries a
            // NavComBox it never shows, so a bare querySelector reported a tuning state for
            // radios that are not on that screen at all.
            var root = firstVisible(rootSel);
            if (!root) return null;
            var containers = root.querySelectorAll(".navcom-frequencyelement-container");
            var selected = 0;
            for (var i = 0; i < containers.length; i++) {
                if (classList(containers[i]).indexOf("selected") >= 0) selected = i + 1;
            }
            var border = root.querySelector(".radio-armed-border");
            var state = "";
            if (border) {
                var bc = classList(border);
                if (bc.indexOf("standby") >= 0) state = "standby";
                else if (bc.indexOf("inactive") >= 0) state = "inactive";
                else if (bc.indexOf("active") >= 0) state = "transmitting";
            }
            return { kind: kind, selected: selected, state: state };
        }
        var sides = [oneSide("#NavComBox #Left", "NAV"), oneSide("#NavComBox #Right", "COM")];
        var out = [];
        for (var i = 0; i < sides.length; i++) if (sides[i]) out.push(sides[i]);
        return out;
    };

    // ------------------------------------------------------- navigation data bar
    //
    // Four PILOT-CONFIGURABLE slots (GS, DTK, TRK and ETE by default, changed on the AUX
    // system-setup page), so they are read as whatever they currently are rather than
    // against a fixed list - a fixed list would silently report the wrong label for a
    // pilot who reconfigured them.
    A.navDataBar = function () {
        var out = [];
        var slots = document.querySelectorAll(".nav-data-bar-field-slot");
        for (var i = 0; i < slots.length; i++) {
            var t = spacedText(slots[i]);
            if (t) out.push(t);
        }
        return out;
    };

    // The page's name. Most pages put it in the data bar, but SOME LEAVE IT BLANK - every
    // NRST page does - and a window that answers "not shown" when the pilot has just
    // navigated somewhere is reporting a fault it does not have.
    //
    // The page selector is the fallback, and it works while CLOSED: it keeps its active
    // tab and highlighted entry in the DOM, so it can still say which group and page are
    // current long after it has faded out.
    //
    // ⚠️ AND THE DATA BAR LIES. IT NAMED A DIFFERENT PAGE ON NINE OF THE SEVENTEEN.
    //
    // Measured across every real MFD page: VOR Information, NDB Information and Intersection
    // Information all announced "WPT - Airport Information"; Nearest Intersections, Nearest
    // NDB and Nearest VOR all announced "NRST - Nearest Airports"; Airport Information
    // announced "WPT - Airport Chart". The pageKey was correct every single time — only the
    // drawn title lagged, and it is not BLANK when it lags, so the page-selector fallback
    // below never ran. A pilot who jumps with Ctrl+G and is told they are still on the page
    // they left has no way to tell a stale title from a jump that failed.
    //
    // So the INSTRUMENT decides which page this is — the same rule the cursor, the field
    // list and the open-dialog test already follow — and the drawn title is allowed only to
    // ADD to that name, never to replace it. "Aux - System Setup 1" extends "System Setup"
    // with the sub-page number and is kept; "Airport Information" does not extend "VOR
    // Information" and is discarded.
    //
    // ⚠️ THE PFD IS UNTOUCHED AND MUST STAY SO. Its openPageKey is permanently empty (it has
    // windows, not pages), so pageName() returns "" there and every PFD path falls through
    // to exactly the DOM behaviour it had before.


    A.pageTitle = function () {
        var shown = text(firstVisible(".nav-data-bar-page-title"));

        var model = A.M.pageName();
        if (model && model.page) {
            // The drawn title's own page half, after the group prefix if it has one. Both a
            // hyphen and an en dash appear in the live markup.
            var drawnPage = shown ? String(shown).split(/\s+[-–—]\s+/).pop().trim() : "";
            var norm = function (t) { return String(t).toUpperCase().replace(/[^A-Z0-9]/g, ""); };

            // ⚠️ A DRAWN TITLE THAT NAMES ANOTHER PAGE IS STALE; ONE THAT NAMES NO PAGE IS A
            // SUB-VIEW, AND THROWING IT AWAY LOSES REAL INFORMATION. Both shapes are live
            // here: on VorInformation the bar still says "Airport Information", which IS a
            // page in the map and is simply the one we left; on AirportInformation it says
            // "Airport Chart", which is NO page in the map because it is the Charts tab of
            // this one, and the pilot is genuinely looking at a chart. An earlier rule kept
            // the drawn name only when it EXTENDED the model's, which fixed the stale half
            // and silently renamed the chart back to "Airport Information".
            //
            // So: name another page and you are stale. Otherwise you are detail, and detail
            // wins - that is what keeps "System Setup 1" numbered and the chart a chart.
            var stale = false;
            if (drawnPage && norm(drawnPage) !== norm(model.page)) {
                try {
                    var map = A.M.pageMap() || [];
                    for (var g = 0; g < map.length && !stale; g++) {
                        var ps = map[g].pages || [];
                        for (var p = 0; p < ps.length; p++) {
                            if (ps[p].key && norm(ps[p].name) === norm(drawnPage)) { stale = true; break; }
                        }
                    }
                } catch (e) { stale = true; }   // cannot check - trust the instrument
            }

            // The GROUP always comes from the model, so the name a pilot hears back is the
            // name they picked out of the Ctrl+G jump list.
            return model.group + " - " + ((drawnPage && !stale) ? drawnPage : model.page);
        }

        if (shown) return shown;

        var d = document.querySelector(".mfd-pageselect");
        if (!d) return "";

        var group = "";
        var tabs = d.querySelectorAll(".mfd-pageselect-tabs > *");
        for (var i = 0; i < tabs.length; i++) {
            if (classList(tabs[i]).indexOf("active-tab") >= 0) { group = text(tabs[i]); break; }
        }

        var page = "";
        var pgs = d.querySelectorAll(".mfd-pageselect-group .popout-menu-item");
        for (var q = 0; q < pgs.length; q++) {
            if (classList(pgs[q]).indexOf("highlight-select") >= 0 ||
                pgs[q].querySelector(".highlight-select")) { page = text(pgs[q]); break; }
        }

        return (group && page) ? (group + " - " + page) : (group || page);
    };

    // ------------------------------------------------------------------- the EIS
    //
    // The engine strip. THREE gauge shapes, and the readable content differs by shape:
    //
    //   dial        Load % and RPM        - carry a NUMBER (.eis-dial-gauge-value)
    //   text        Fuel Flow             - carries a number (.ff-text)
    //   horizontal  Oil Temp, Oil Press, Coolant Temp, Fuel Temp, Fuel Qty - NO NUMBER
    //
    // The horizontal gauges are the interesting case: the real G1000 draws them as a
    // needle against a coloured scale with no digits at all, so "what does the MFD say
    // the oil pressure is" has no numeric answer - the answer is WHERE THE NEEDLE IS.
    // That is read here as the colour band the needle sits in plus how far along the
    // scale it has travelled, which is the same information a sighted pilot takes off the
    // strip at a glance.
    //
    // Numbers for these quantities exist on MSFSBA's own Engine panel, read from the
    // aircraft's sensor L:vars at full resolution. This is deliberately NOT that: it is
    // what the SCREEN shows, which is a different question and the one this window
    // answers. Keeping both is not a duplicate control - the panel is where the pilot
    // reads the engine, this is where they read the display.
    A.eis = function () {
        var host = firstVisible(".eis");
        if (!host) return [];

        var out = [];
        // ⚠️ .eis-gauge AND .eis-dial-gauge: the XLS strip carries its two dials (MAN IN,
        // RPM) under the second class, and selecting only the first read seven bars and
        // neither number - the two the whole strip exists for on a Lycoming.
        var gauges = host.querySelectorAll(".eis-gauge, .eis-dial-gauge");

        for (var i = 0; i < gauges.length; i++) {
            var g = gauges[i];
            if (!visible(g)) continue;

            var labelEl = g.querySelector(".gauge-label") ||
                          g.querySelector(".eis-dial-gauge-label") ||
                          g.querySelector(".ff-label");
            var label = text(labelEl);
            if (!label) continue;

            var lcls = labelEl ? classList(labelEl) : [];
            var alert = lcls.indexOf("warning") >= 0 ? "warning"
                      : lcls.indexOf("caution") >= 0 ? "caution" : "";

            var value = text(g.querySelector(".eis-dial-gauge-value")) ||
                        text(g.querySelector(".ff-text"));

            out.push({
                label: label,
                value: value,
                bands: A.gaugeBands(g),
                scale: A.gaugeScale(g),
                alert: alert
            });
        }

        return out;
    };

    // Where each needle sits on its coloured scale. Returns ONE ENTRY PER NEEDLE, so the
    // two-sided fuel gauges report both tanks instead of collapsing to one reading.
    A.gaugeBands = function (g) {
        var barHost = g.querySelector(".color-bars");
        if (!barHost) return [];

        var barEls = barHost.querySelectorAll(".eis-gauge-bar");
        var bars = [];
        for (var i = 0; i < barEls.length; i++) {
            var r = barEls[i].getBoundingClientRect();
            if (r.width <= 0) continue;
            var c = classList(barEls[i]);
            var colour = c.indexOf("red-bar") >= 0 ? "red"
                       : c.indexOf("yellow-bar") >= 0 ? "yellow"
                       : c.indexOf("green-bar") >= 0 ? "green"
                       : c.indexOf("white-bar") >= 0 ? "white" : "";
            bars.push({ left: r.left, right: r.left + r.width, colour: colour });
        }
        if (!bars.length) return [];

        var scaleLeft = bars[0].left;
        var scaleRight = bars[bars.length - 1].right;
        var span = scaleRight - scaleLeft;

        // The needle is an svg TRANSLATED along the scale. Its bounding box is as wide as
        // the whole gauge, so the box tells you nothing about where the needle points -
        // the offset inside the transform is what locates it, measured from the same
        // origin the bars start at.
        // ASK FOR THE TRANSFORM, NOT FOR A CLASS NAME. A single-needle gauge wraps its
        // svg in ".pointer"; the two-needle fuel gauges wrap theirs in ".pointers >
        // .pointer-one-div" / ".pointer-two-div", which a ".pointer" lookup does not match
        // at all - so both fuel gauges read "no reading" while every other gauge worked.
        // The one thing every needle on every shape of gauge has is the translateX that
        // positions it, so that is what is selected on.
        var out = [];
        var pointers = g.querySelectorAll("svg[style*='translateX']");
        for (var p = 0; p < pointers.length; p++) {
            var svg = pointers[p];
            var m = /translateX\(\s*(-?[\d.]+)px/.exec(svg.getAttribute("style") || "");
            if (!m) continue;

            var x = scaleLeft + parseFloat(m[1]);
            var colour = "";
            for (var b = 0; b < bars.length; b++) {
                if (x >= bars[b].left && x < bars[b].right) { colour = bars[b].colour; break; }
            }
            if (!colour) colour = (x < scaleLeft) ? "below the scale" : "above the scale";

            out.push({
                colour: colour,
                percent: span > 0 ? Math.round(((x - scaleLeft) / span) * 100) : -1
            });
        }

        return out;
    };

    // Some gauges print their scale as tick labels ("0 5 10 14" on the fuel quantity).
    // Where they do, the ends of that scale turn a percentage along the bar into a
    // quantity, which is the difference between "93 percent along" and "about 13 gallons".
    A.gaugeScale = function (g) {
        var host = g.querySelector(".min-max");
        if (!host) return null;
        var marks = [];
        for (var i = 0; i < host.children.length; i++) {
            var t = text(host.children[i]);
            if (t) marks.push(t);
        }
        return marks.length >= 2 ? { low: marks[0], high: marks[marks.length - 1] } : null;
    };

    // ---------------------------------------------------------------- the page
    //
    // The middle of the screen. Its content is entirely page-dependent - a map, a flight
    // plan, an airport record, a checklist - so it is read by STRUCTURE rather than
    // against a per-page selector list: a per-page list renders nothing at all on any
    // page nobody wrote a branch for, and the pilot cannot tell that from an empty page.
    // The labelled rows inside one box, as "setting: value".
    //
    // A row's own class tokens are whole words, so ".mfd-system-setup-row" selects the
    // ROWS and never their ".mfd-system-setup-row-title" / "-left" / "-right" parts -
    // which is what keeps every field from being emitted three times over.
    // One list row as "field, field, field".
    //
    // A row's fields are separate elements ("VCBI", "020" + the degree sign, "0.4 NM"),
    // and running them through spacedText welds the first two together: the last digit of
    // an identifier and the first of a bearing are both digits, so the rule that keeps a
    // clock from becoming "0 : 0 0" also turns VCBI 020 into one token. Reading the
    // FIELDS instead of the text keeps every boundary the screen draws.
    A.fieldsOf = function (row) {
        var parts = [];
        for (var i = 0; i < row.children.length; i++) {
            if (!visible(row.children[i])) continue;
            var t = spacedText(row.children[i]);
            if (t) parts.push(t);
        }
        return parts.length ? parts.join(", ") : spacedText(row);
    };

    // One spelling for one title, so a box and the field walk's `group` can be compared
    // without caring about spacing, case or punctuation ("Date / Time" vs "DATE/TIME").
    function normKey(t) {
        return String(t || "").toUpperCase().replace(/[^A-Z0-9]/g, "");
    }

    // THE SET OF GROUP BOXES THAT CONTAIN A REGISTERED CONTROL.
    //
    // ⚠️ THE TEST IS STRUCTURAL — "does this box hold a control" — NEVER "did anyone say the
    // words". An earlier attempt compared a box's TEXT against the rows already built and on
    // Aux System Setup marked TEN boxes dimmed, Date / Time and Display Units among them,
    // every one of which the pilot can select perfectly well: the field walk renders
    // "Date: 10 - SEP - 26" while the box concatenates differently, so the match failed.
    // Telling a pilot they cannot reach something they can is worse than the gap this closes.
    // A.M.fields() reports each field's own groupbox title, straight from the view's scroll
    // controller, so the answer comes from the instrument rather than from string similarity.
    //
    // Returns null when the view cannot be asked — and a null `owned` marks NOTHING dimmed,
    // which is the safe direction: a missing answer must never invent unselectable boxes.
    A.ownedGroupTitles = function () {
        var owned = {};
        try {
            var f = A.M.fields() || [];
            if (!f.length) return null;
            for (var i = 0; i < f.length; i++) {
                if (f[i].group) owned[normKey(f[i].group)] = true;
            }
        } catch (e) { return null; }
        return owned;
    };

    // Every box of labelled rows under one root, as "Title:" then its rows indented.
    //
    // SHARED between pages and dialogs, because the G1000 builds both the same way. The
    // Direct-To dialog is the case that proved it: read as plain children its group titles
    // came out at the END of each line ("BRG 020 DIS 0.4 NM Location") because the title
    // element is last in the DOM, so the pilot heard every field before being told what
    // the group was.
    //
    // ⚠️ `owned` IS OPTIONAL AND ONLY THE PAGE PATH PASSES IT. When given, it is the set of
    // groupbox titles that contain a REGISTERED CONTROL (from A.M.fields()), and a box
    // outside it is marked ", dimmed" — the G1000 draws it and the knob walks straight past.
    // Dialogs deliberately pass nothing: a dialog box with no registered field would other-
    // wise be announced as unselectable purely because the view models dialogs differently.
    A.groupboxLines = function (root, owned) {
        var lines = [];
        var boxes = root.querySelectorAll(".groupbox");
        for (var b = 0; b < boxes.length; b++) {
            var box = boxes[b];
            if (!visible(box)) continue;

            var boxTitle = text(box.querySelector(".groupbox-title"));
            var boxLines = A.rowsOf(box);

            // ⚠️ AN EMPTY BOX IS STILL DRAWN, AND SKIPPING IT HID THREE OF THEM. The
            // Intersection page's "Reference VOR", the NDB page's "Frequency" and the flight
            // plan's "Selected Waypoint Weather" are boxes the display puts on screen with
            // nothing in them yet - a sighted pilot sees the heading and knows where the
            // value will appear, and the coverage sweep reported all three as content
            // MSFSBA never said.
            //
            // Reported as the title plus ", empty", which is the same honesty as ", dimmed":
            // the state of the box IS the reading. A box with no title at all is skipped -
            // that is a layout wrapper, not something a pilot is looking at.
            if (!boxLines.length) {
                if (boxTitle) lines.push(boxTitle + ", empty");
                continue;
            }

            if (boxTitle) lines.push(boxTitle
                + (owned && !owned[normKey(boxTitle)] ? ", dimmed" : "") + ":");
            for (var r = 0; r < boxLines.length; r++) {
                lines.push((boxTitle ? "  " : "") + boxLines[r]);
            }
        }
        return lines;
    };

    // A WAYPOINT ENTRY FIELD, as "ident, place, name".
    //
    // This is the control the pilot TYPES A WAYPOINT INTO - Direct-To, the flight plan,
    // every WPT page - so it is the one field on the MFD whose exact contents matter most.
    // Read as text it comes out "VCBI__KatunayakeBandaranaike Intl Colombo": the entry
    // box, the city and the facility name with no boundary between them.
    //
    // The ident lives in a SCROLLER, padded to its full width with underscores for the
    // characters not yet entered. Those are placeholder, not content - an ident of VCBI
    // is "VCBI", not "VCBI blank" - so they are trimmed from the end, and a field with
    // nothing in it at all says so.
    A.wptEntry = function (entry) {
        var scroller = entry.querySelector(".input-component-scroller");
        var ident = text(scroller).replace(/_+$/, "");
        if (!ident) ident = "blank";

        // ⚠️ AN UNFILLED PART IS A ROW OF UNDERSCORES, NOT AN EMPTY STRING, so testing it
        // for truthiness keeps it. The VOR page read "I5Q, ________________, FONTANGES" -
        // sixteen underscores spoken in the middle of a facility a pilot had just looked
        // up, because that VOR has no city and the field is padded to its full width.
        // The ident is trimmed just above for the same reason; the other two were not.
        //
        // They are DROPPED rather than read as "blank": the ident's blank is news (nothing
        // has been typed yet), but a facility with no city is complete as it stands, and
        // "I5Q, blank, FONTANGES" invites a pilot to go looking for something missing.
        var filled = function (t) {
            return /[^_\-\s:.\/]/.test(String(t || "")) ? String(t) : "";
        };

        var parts = [ident];
        var place = filled(text(entry.querySelector(".wpt-entry-location")));
        var name = filled(text(entry.querySelector(".wpt-entry-name")));
        if (place) parts.push(place);
        if (name) parts.push(name);
        return parts.join(", ");
    };

    // ------------------------------------------------------- the active flight plan
    //
    // THE MOST IMPORTANT PAGE ON THE AEROPLANE FOR AN IFR FLIGHT, and the one the generic
    // readers were worst at: it is a list of lists, and read as one it came out as a
    // single 300-character line carrying an entire approach —
    // "BUSLI 239 9.5 NM 2500 FT ... LIKRA faf 040 1.5 NM 1500 FT RW04 map ... HOLD 220".
    // No leg could be arrowed to, and the active waypoint was invisible.
    //
    // The shape is SEGMENTS, each with a header (Origin, Enroute, the loaded approach,
    // Destination) and its own nested list of legs. Which leg is ACTIVE is the single
    // most useful fact on the page — it is the one the aeroplane is flying to.
    //
    // Placeholder legs are CLASSED hide-element rather than removed, exactly like the CAS
    // window's pre-allocated rows and the nearest list's empty slots. Same trap, third
    // occurrence on this aircraft: what is in the DOM is not what is on the screen.
    A.flightPlanRows = function (container) {
        var out = [];
        var host = container.querySelector(".ui-control-list-content");
        if (!host) return out;

        for (var i = 0; i < host.children.length; i++) {
            var seg = host.children[i];
            if (!visible(seg)) continue;

            // spacedText, not text: an unset runway renders as underscores, and the
            // header is where "Origin - RW ______" would otherwise reach the pilot raw.
            var header = spacedText(seg.querySelector(".header-name"));
            if (header) out.push(header + ":");

            var legs = seg.querySelectorAll(".fix-container");
            var any = false;
            for (var g = 0; g < legs.length; g++) {
                var leg = legs[g];
                if (!visible(leg)) continue;
                if (isMarkedHidden(leg)) continue;

                // An empty segment still carries one placeholder leg whose every field
                // is unset. spacedText has already turned those underscores into "blank",
                // so the test is for a row that is nothing BUT blanks.
                var line = A.fieldsOf(leg);
                if (!line) continue;
                if (!line.replace(/blank|[\s,_]/g, "")) continue;

                if (classList(leg).indexOf("active-wpt") >= 0) line += " (active)";
                out.push((header ? "  " : "") + line);
                any = true;
            }
            if (header && !any) out.push("  empty");
        }

        return out;
    };

    // ⚠️ THE PROCEDURE SELECTION DIALOGS HAVE NO GROUPBOX AT ALL, so the boxed reader
    // never saw them and the generic walk welded them into one run: Select Departure read
    // back as "DepartureARNE2ZRunway TransitionARNEM: 36L" - the three choices a pilot has
    // just made, run together, with the runway detached from its own label. Shared by
    // Select Departure, Select Arrival and Select Approach, so one reader serves all three,
    // and it is called from BOTH the boxed reader and the popout-dialog reader because
    // which one sees it depends on how that dialog happens to be built.
    //
    // The DOM pairs them properly and the walk was throwing it away: .slctproc-<what>-label
    // immediately followed by .slctproc-<what>-value, three pairs of them. Take the
    // children in order and let a label claim the value after it - no class list to keep up
    // to date, so a fourth field on some other procedure dialog reads correctly the day it
    // appears.
    A.procChoiceLines = function (root) {
        var out = [];
        if (!root) return out;
        var proc = root.querySelector(".slctproc-container");
        if (!proc || !visible(proc)) return out;

        // ⚠️ WHICH AIRPORT THE PROCEDURE BELONGS TO COMES FIRST, and the first cut of this
        // reader dropped it - the coverage sweep caught "Amsterdam" on Select Departure and
        // "Milan" on Select Arrival going unread. A departure named without its aerodrome
        // is the one thing on the dialog a pilot cannot infer. Read through A.wptEntry so
        // it comes out "EHAM, Amsterdam Schiphol" rather than the generic walk's
        // "E HAMblank Amsterdam Schiphol" - that box is a per-character scroller.
        var apt = root.querySelector(".wpt-entry");
        if (apt && visible(apt)) {
            var aptLine = A.wptEntry(apt);
            if (aptLine) out.push("Airport: " + aptLine);
        }

        var pendingLabel = "";
        for (var i = 0; i < proc.children.length; i++) {
            var kid = proc.children[i];
            if (!visible(kid)) continue;
            var cls = String(kid.className || "");
            var body = text(kid);
            if (cls.indexOf("-label") >= 0) { pendingLabel = body; continue; }
            if (cls.indexOf("-value") >= 0) {
                out.push((pendingLabel ? pendingLabel + ": " : "") + (body || "blank"));
                pendingLabel = "";
            }
        }

        // ⚠️ AND THE ACTION PROMPT, WHICH IS A SIBLING OF THE CONTAINER AND WAS THEREFORE
        // DROPPED. "Load?" sits in .slctproc-load NEXT TO the label/value block, not inside
        // it, so a reader that walked only the container's children lost the one row that
        // says what ENT will do - the whole point of the dialog. Caught by the coverage
        // sweep on the XLS, on both Select Departure and Select Arrival, after this reader
        // replaced the generic walk that had been picking it up by accident.
        //
        // It is CLASSED hide-element until the selection is loadable, so reading it
        // unconditionally would promise an action that is not on offer - the visibility test
        // is what makes it honest.
        var load = root.querySelector(".slctproc-load");
        if (load && visible(load)) {
            var prompt = text(load);
            if (prompt) out.push(prompt);
        }

        return out;
    };

    A.rowsOf = function (box) {
        var out = [];

        var fpln = box.querySelector(".mfd-fpln-container");
        if (fpln) {
            var planned = A.flightPlanRows(fpln);
            if (planned.length) return planned;
        }

        // ⚠️ THE PROCEDURE SELECTION DIALOGS WELD INTO ONE RUN, AND THEY ARE THE IFR
        // WORKFLOW. Select Departure read back as
        // "DepartureARNE2ZRunway TransitionARNEM: 36L" - the three choices a pilot has just
        // made, run together, with the runway detached from its own label. Shared by Select
        // Departure, Select Arrival and Select Approach, so one reader serves all three.
        //
        // The DOM pairs them properly and the generic walk was throwing it away: the
        // container holds .slctproc-<what>-label immediately followed by
        // .slctproc-<what>-value, three pairs of them. Walk the children in order and let a
        // label claim the value that follows it - no class list to keep up to date, so a
        // fourth field on some other procedure dialog reads correctly the day it appears.
        var procLines = A.procChoiceLines(box);
        if (procLines.length) return procLines;

        // Waypoint entry fields first: they are the typed-into controls, and the generic
        // readers weld their three parts together.
        var entries = box.querySelectorAll(".wpt-entry");
        if (entries.length) {
            for (var e = 0; e < entries.length; e++) {
                if (!visible(entries[e])) continue;
                var line = A.wptEntry(entries[e]);
                if (line) out.push(line);
            }
            if (out.length) return out;
        }

        // Every scrollable G1000 list - nearest airports, intersections, VORs, NDBs, the
        // flight plan - is built the same way, so ONE generic selector serves all of them
        // and a page nobody anticipated still reads as a list rather than a paragraph.
        // A PROCEDURE'S LEG SEQUENCE is a list that is not built as one - the legs of a
        // SID, STAR or approach sit in a plain container rather than a ui-control-list, so
        // the generic reader ran the whole procedure together as
        // "CI04 0.0 NM FI04 faf 39 2.8 NM RW04 map 38 5.8 NM". These are the legs the
        // aeroplane is about to fly, and which one is the final approach fix and which is
        // the missed approach point are the two facts on the page that matter most.
        var legs = box.querySelectorAll(".proc-sequence-item");
        if (legs.length) {
            for (var g = 0; g < legs.length; g++) {
                if (!visible(legs[g])) continue;
                var leg = A.fieldsOf(legs[g]);
                if (leg) out.push(leg);
            }
            if (out.length) return out;
        }

        var listHost = box.querySelector(".ui-control-list-content");
        if (listHost) {
            for (var L = 0; L < listHost.children.length; L++) {
                var item = listHost.children[L];
                if (!visible(item)) continue;
                var line = A.fieldsOf(item);
                if (!line) continue;
                if (classList(item).indexOf("highlight-select") >= 0 ||
                    item.querySelector(".highlight-select")) line += ", selected";
                out.push(line);
            }
            if (out.length) return out;
        }

        // THE NAVIGRAPH FLIGHT PLAN CATALOG. Its plan list is a plain stack of divs under
        // an overflow-hidden scroller - 99 numbered slots, most of them "_ _ _ _/_ _ _ _" -
        // with two status overlays ("Loading flights...", "Importing...") mounted beside
        // them and shown only while true. Read generically, all 99 welded into one row
        // with both overlays' text in it whether shown or not. One row per plan, the empty
        // tail collapsed, and an overlay only when it is actually up.
        var stack = box.querySelector("[class*='overflow-hidden']");
        if (stack) {
            var overlays = [];
            var listEl = null;
            for (var sc = 0; sc < stack.children.length; sc++) {
                var kid = stack.children[sc];
                if (!visible(kid)) continue;
                if (/absolute/.test(String(kid.className))) { var ot = text(kid); if (ot) overlays.push(ot); }
                else if (!listEl && kid.children.length >= 5) listEl = kid;
            }
            for (var o = 0; o < overlays.length; o++) out.push(overlays[o]);
            if (listEl) {
                var firstEmpty = -1, filled = 0;
                for (var li = 0; li < listEl.children.length; li++) {
                    var item = listEl.children[li];
                    if (!visible(item)) continue;
                    var it = text(item);
                    if (!it) continue;
                    // "1EGNX/EGHI" - the slot number and the plan run together in the DOM.
                    it = it.replace(/^(\d+)(?=\S)/, "$1 ");
                    if (/_ _/.test(it)) { if (firstEmpty < 0) firstEmpty = li + 1; continue; }
                    filled++;
                    var line = it;
                    if (classList(item).indexOf("highlight-select") >= 0 ||
                        item.querySelector(".highlight-select")) line += ", selected";
                    out.push(line);
                }
                if (firstEmpty > 0) out.push(firstEmpty + " to " + listEl.children.length + " empty");
            }
            if (out.length) return out;
        }

        // A box of label / value pairs in plain flex rows ("Departure EGNX"), which the
        // one-level walk welds into "Departure EGNX Destination EGHI Total Distance ...".
        var pairs = box.querySelectorAll("div.flex");
        var pairLines = [];
        for (var pr = 0; pr < pairs.length; pr++) {
            var spans = pairs[pr].children;
            if (spans.length !== 2 || !visible(pairs[pr])) continue;
            if (spans[0].tagName !== "SPAN" || spans[1].tagName !== "SPAN") continue;
            var pl = text(spans[0]), pv = text(spans[1]);
            if (!pl && !pv) continue;
            pairLines.push(pl + ": " + (pv || "blank"));
        }
        if (pairLines.length >= 2) return pairLines;

        var rows = box.querySelectorAll(".mfd-system-setup-row, .mfd-setup-row, " +
                                        ".popout-menu-item, .mfd-status-row");
        for (var i = 0; i < rows.length; i++) {
            var row = rows[i];
            if (!visible(row)) continue;

            // A row is up to THREE things: a left-hand toggle, a title, and the value
            // that follows the title inside the same container. Reading the toggle as
            // the value put "Altitude: Off" where the screen says the transition alert
            // is set to 18000 FT and is switched off - two facts, and the wrong one won.
            var titleEl = row.querySelector(".mfd-system-setup-row-title");
            var title = text(titleEl);

            var value = spacedText(row.querySelector(".mfd-system-setup-row-right"));
            if (!value && titleEl && titleEl.parentElement) {
                // The value has no element of its own; it is whatever the title's own
                // container says after the title.
                var whole = spacedText(titleEl.parentElement);
                if (title && whole.indexOf(title) === 0) value = whole.substring(title.length).trim();
            }

            var toggle = spacedText(row.querySelector(".arrow-toggle-value")) ||
                         spacedText(row.querySelector(".mfd-system-setup-row-left"));

            var line = (title && value) ? (title + ": " + value)
                     : (title && toggle) ? (title + ": " + toggle)
                     : spacedText(row);
            if (!line) continue;
            if (title && value && toggle && toggle !== value) line += ", " + toggle;

            var cls = classList(row);
            if (cls.indexOf("text-disabled") >= 0) line += ", dimmed";
            if (cls.indexOf("highlight-select") >= 0 ||
                row.querySelector(".highlight-select")) line += ", selected";

            out.push(line);
        }

        // A box with no rows of its own still has content worth reading - render it as
        // one line rather than dropping the box on the floor.
        // A box with no rows of its own still has content worth reading - render it as
        // one line rather than dropping the box on the floor. The title is stripped from
        // EITHER end: it leads in a setup box and TRAILS in a dialog box, where the title
        // element is the last child.
        if (!out.length) {
            var whole = spacedText(box);
            var bt = text(box.querySelector(".groupbox-title"));
            if (whole && bt) {
                if (whole.indexOf(bt) === 0) whole = whole.substring(bt.length).trim();
                else if (whole.length > bt.length &&
                         whole.lastIndexOf(bt) === whole.length - bt.length) {
                    whole = whole.substring(0, whole.length - bt.length).trim();
                }
            }
            if (whole) out.push(whole);
        }

        return out;
    };

    // ------------------------------------------------------ the electronic checklist
    //
    // The DA40's own AFM checklist, on the MFD. This is the page where "documented equals
    // doable" is most literally true: it IS the documentation, and a blind pilot who
    // cannot read it is flying without the checklist the aeroplane ships with.
    //
    // It is fully operable, verified live. The interaction is a three-step the softkey
    // labels do not spell out:
    //
    //   CHECKLIST or GROUP softkey  - arms the selector
    //   FMS knob                    - steps through the choices
    //   ENT                         - commits
    //
    // and NEXT ITEM walks the items. The middle step is invisible in the DOM - the DA40's
    // plugin does not redraw the title until ENT commits - so a pilot turning the knob
    // hears nothing change until they press ENT. That is the aircraft's own behaviour, not
    // a scrape limitation, and it is worth knowing rather than being surprised by.
    //
    // Three kinds of line, and they are not interchangeable:
    //   .Da40-checklist-checkbox  a CHECKABLE action ("Electric master....OFF")
    //   .Da40-checklist-text      a note or a condition ("If External Power will be used:")
    //   .checklist-focus          the item the cursor is on
    // The two checklist SELECTION popups. Each is a ui-control-list whose items are
    // separate elements, so the generic reader ran all nine categories into one token -
    // "EMERGENCY Procedures ENGINEEMERGENCY Procedures ELECTRICAL SYSTEM..." - and, worse,
    // never said which one the FMS knob was sitting on. The knob WAS moving; the pilot
    // simply had no way to tell, which reads as a page you cannot leave.
    // The selected item carries "highlight-select" (NOT "checklist-focus", which is the
    // display list's own marker - they are different lists with different classes).
    A.checklistSelection = function () {
        // ⚠️ ASK THE INSTRUMENT WHETHER THE POPUP IS OPEN. Both selection lists stay in the
        // DOM with offsetParent set AND opacity 1 long after they are closed - measured on
        // the live MFD, where both still reported visible with opacity 1 while the pilot
        // was scrolling checklist ITEMS behind them. Gating on the checklist PAGE instead
        // was the first fix and it was not enough: it stops this leaking onto other pages,
        // but ON the checklist page it still answered "Currently on: TERMS AND CONDITIONS
        // FOR USE" over and over while the pilot moved through a checklist. Each popup is a
        // registered VIEW, so the view key is the honest signal and the only one.
        var open = A.M.viewKey();
        if (open === "Da40NgChecklistSelectionPopup" ||
            open === "Da40NgChecklistCategorySelectionPopup" ||
            open === "Da40ChecklistSelectionPopup" ||
            open === "Da40ChecklistCategorySelectionPopup") {
            // Open - fall through and read it.
        } else if (open) {
            // The model answered, and it is not a selection popup. Nothing to report.
            return [];
        } else if (!firstVisible(".Da40-checklist-page-container")) {
            // No model (the instrument is not up yet); the page container is the fallback.
            return [];
        }

        // ORDER MATTERS, and it is the reverse of the order a pilot meets them. Choosing
        // a GROUP opens the CHECKLIST list ON TOP of it, and both stay visible - measured
        // live: the category list stayed visible:1 while the selection list went 0 to 1.
        // So the deeper popup must be tested FIRST, or the pilot is read the list they
        // have already left while the knob moves a different one.
        var specs = [
            [".Da40-checklist-selection-list", "checklist"],
            [".Da40-checklist-category-selection-list", "checklist group"]
        ];

        for (var s = 0; s < specs.length; s++) {
            var host = firstVisible(specs[s][0]);
            if (!host) continue;

            var content = host.querySelector(".ui-control-list-content");
            if (!content) continue;

            var lines = ["Select a " + specs[s][1] + ":"];
            var selected = null;

            for (var i = 0; i < content.children.length; i++) {
                var item = content.children[i];
                if (!visible(item)) continue;

                var label = text(item);
                if (!label) continue;

                var chosen = classList(item).indexOf("highlight-select") >= 0;
                if (chosen) selected = label;
                lines.push(label + (chosen ? " (selected)" : ""));
            }

            if (lines.length === 1) continue;

            // Lead with the selection so a pilot who only hears the first line still
            // learns where the knob is, then let them arrow the whole list.
            if (selected) lines.splice(1, 0, "Currently on: " + selected);
            return lines;
        }

        return [];
    };

    A.checklistPage = function (p) {
        // A selection popup sits ON TOP of the page, so it must be answered first or the
        // pilot is read the checklist behind the list they are actually navigating.
        var picking = A.checklistSelection();
        if (picking.length) return picking;

        var host = p.querySelector(".Da40-checklist-page-container");
        if (!host) return [];

        var lines = [];
        var category = text(host.querySelector(".checklist-category"));
        var title = text(host.querySelector(".checklist-title"));
        if (category) lines.push("Checklist group: " + category);
        if (title) lines.push("Checklist: " + title);

        var listHost = host.querySelector(".Da40-checklist-display-list .ui-control-list-content");
        if (listHost) {
            for (var i = 0; i < listHost.children.length; i++) {
                var item = listHost.children[i];
                if (!visible(item)) continue;

                var box = item.querySelector(".Da40-checklist-checkbox");
                var raw = text(box || item);
                if (!raw) continue;

                // The leader is a run of dots holding the action out to the right margin.
                // Read literally a screen reader says "dot" forty times, so it collapses
                // to the pause it is drawn to be.
                var line = raw.replace(/\.{2,}/g, " ... ").replace(/\s+/g, " ").trim();

                // A note is not an action, and saying so stops a condition being read as
                // something to do.
                if (!box) line = "note: " + line;

                var cls = classList(item);
                if (cls.indexOf("checklist-focus") >= 0) line += " (current)";
                for (var c = 0; c < cls.length; c++) {
                    if (/checked|completed|complete/i.test(cls[c])) { line += ", done"; break; }
                }

                lines.push("  " + line);
            }
        }

        var done = host.querySelector(".Da40-checklist-completed-label");
        if (done && visible(done)) lines.push(text(done));
        var next = host.querySelector(".Da40-next-checklist-label");
        if (next && visible(next)) lines.push(text(next));

        return lines;
    };

    /// The MFD ENGINE page, read a field at a time.
    ///
    /// Its lower half is four boxes - Fluids, Electrical, Fuel System, Fuel Calculator -
    /// and the generic walk rendered each box as ONE line, so a screen reader said
    /// "Fluids Coolant °C 82 Gearbox °C 81" and, worse,
    /// "Fuel System FFlow GPH 0.641°C 34 L R Main 14105 Gal 814", in which no value can be
    /// arrowed to and two numbers run together into a third that does not exist.
    ///
    /// Every value on the page is an .engine-page-gauge, and there are exactly two shapes:
    /// a VERTICAL gauge splits into .gauge-label and .gauge-value, and a HORIZONTAL one
    /// welds both into a single .gauge-text ("Volts28.2"), which has to be split on the
    /// numeric tail because the DOM offers no boundary. The fuel calculator uses its own
    /// .fuel-calculator-field, label and value already separate.
    /// Collapses a line that is immediately repeated. The engine page draws Load % twice
    /// - once in the EIS strip and once in the page body - and a screen reader reading
    /// "Load %: 4  Load %: 4" sounds like a stutter or a fault rather than two gauges.
    /// Adjacent only: two identical values far apart on a page are two real readings.
    /// WHERE THE G1000 CURSOR IS, which is the single fact that makes the setup pages
    /// usable and the one nothing was reporting.
    ///
    /// On a real G1000 the knobs change PAGES until you push the FMS knob to turn the
    /// cursor ON; only then do they move between fields and change values. That is why the
    /// Aux setup page read as completely inert - the knobs were paging (to Navigraph one
    /// way, the flight plan catalogue the other) and ENT had nothing focused to act on.
    /// The behaviour was correct; it was simply invisible, and a sighted pilot sees a cyan
    /// box that a blind one had no equivalent for.
    ///
    /// The cursor is the element carrying BOTH "cyan" and "highlight-select". Requiring
    /// cyan is what separates it from the page selector and the checklist lists, which use
    /// highlight-select on its own.
    A.cursorField = function () {
        // ⚠️ THE CURSOR IS NOT MARKED THE SAME WAY ON EVERY PAGE, which is why it read as
        // absent on half of them. The setup pages put "cyan" on a "highlight-select"
        // element; the WPT pages instead add a highlight class to an
        // "input-component-value" and use no cyan at all - found by counting every class
        // on the instrument before and after a cursor push and diffing the two, which is
        // the only way to see a marking you do not already know the name of.
        //
        // Requiring cyan alone therefore reported "cursor off" on a page where it was
        // plainly on. Both markings are accepted; what they have in common is a highlight
        // class, and the two qualifiers keep the page selector and the checklist lists -
        // which use bare "highlight-select" - from being mistaken for an edit cursor.
        var all = document.querySelectorAll("[class*=highlight]");

        for (var i = 0; i < all.length; i++) {
            var cls = classList(all[i]);
            // THREE markings, not one. The setup pages use "cyan"; the WPT pages mark an
            // "input-component-value"; and a NUMERIC field uses "number-input-active".
            // All three were found by class inventory rather than by being reported.
            var isCursor = cls.indexOf("cyan") >= 0 ||
                           cls.indexOf("input-component-value") >= 0 ||
                           cls.indexOf("number-input-active") >= 0;
            if (!isCursor) continue;
            if (!hasClassContaining(all[i], "highlight")) continue;
            if (!visible(all[i])) continue;

            var value = text(all[i]);
            var label = "";

            // The row above carries label AND value run together ("Time FormatUTC"), so
            // the label is what is left once the value is taken off the end.
            // ⚠️ Do NOT stop at the first ancestor whose class mentions "row". The setup
            // page nests three of them - row-right inside row-title-right inside row - and
            // the innermost holds ONLY the value, so stopping there yields no label at all
            // ("Cursor on. UTC" rather than "Cursor on. Time Format: UTC"). Keep climbing
            // until a row is found that carries MORE than the value.
            var e = all[i].parentElement;
            for (var d = 0; d < 6 && e; d++) {
                var whole = text(e);
                if (hasClassContaining(e, "row") &&
                    whole.length > value.length &&
                    whole.lastIndexOf(value) === whole.length - value.length) {
                    label = whole.substring(0, whole.length - value.length).trim();
                    break;
                }
                e = e.parentElement;
            }

            // TWO CURSOR STATES, and telling them apart is the difference between a
            // usable setup page and an incomprehensible one. "highlight-select" means the
            // cursor is ON the field and the knob will move to the NEXT field;
            // "highlight-active" means the field is OPEN FOR EDITING and the knob is
            // meant to change it. A pilot who cannot see the box has no other way to know
            // which of those two things the next turn will do.
            //
            // This is also what made the old detector lie: it matched only
            // "highlight-select", so ACTIVATING a field looked exactly like the cursor
            // being switched off, and the aeroplane appeared to fight every attempt to
            // edit anything.
            var editing = hasClassContaining(all[i], "highlight-active") ||
                          cls.indexOf("number-input-active") >= 0;
            var text0 = label ? label + ": " + value : value;
            return editing ? text0 + ", editing" : text0;
        }

        return "";
    };

    A.dedupeAdjacent = function (lines) {
        var out = [];
        for (var i = 0; i < lines.length; i++) {
            if (i > 0 && lines[i] === lines[i - 1]) continue;
            out.push(lines[i]);
        }
        return out;
    };

    A.engineGaugeLine = function (g) {
        // A TEXT gauge keeps its label and value in their own nodes under different class
        // names again (.ff-label / .ff-text), so the generic pair below misses it and the
        // welded fallback produced "FFlow GPH0.6".
        var ffLabel = g.querySelector(".ff-label");
        var ffText = g.querySelector(".ff-text");
        if (ffLabel && ffText) {
            return text(ffLabel) + ": " + text(ffText);
        }

        // A DOUBLE gauge is the fuel pair - one gauge, TWO needles, left tank and right.
        // Its .gauge-text holds .left-value, .label and .right-value as separate nodes, so
        // reading the parent welds three readings into one number: "41°C34" was two tank
        // temperatures of 41 and 34, and "Main14105Gal814" was worse.
        var left = g.querySelector(".left-value");
        var right = g.querySelector(".right-value");
        if (left && right) {
            var mid = g.querySelector(".gauge-text > .label");
            var unit = mid ? text(mid) : "";
            return (unit ? unit + ": " : "") + "left " + text(left) + ", right " + text(right);
        }

        var label = g.querySelector(".gauge-label");
        var value = g.querySelector(".gauge-value");
        if (label && value) {
            var l = text(label), v = text(value);
            if (l || v) return (l ? l + ": " : "") + v;
        }

        var texts = g.querySelectorAll(".gauge-text");
        if (texts.length >= 2) {
            // The XLS's Electrical block: label in one node, value in the next.
            var tl = text(texts[0]), tv = text(texts[1]);
            if (tl || tv) return (tl ? tl + ": " : "") + tv;
        }

        var welded = g.querySelector(".gauge-text");
        if (welded) {
            var t = text(welded);
            // The value is the trailing number, optionally signed and decimal. Anchored at
            // the END so a label containing digits ("Fuel Qty Gal") keeps them.
            var m = t.match(/^(.*?)([+-]?\d+(?:\.\d+)?)$/);
            if (m && m[1].trim()) return m[1].trim() + ": " + m[2];
            if (t) return t;
        }

        var whole = text(g);
        return whole || "";
    };

    A.enginePageLines = function (p) {
        var boxes = [
            [".fluids-container", ".fluids-container-label"],
            [".electrical-container", ".electrical-container-label"],
            [".fuel-system-container", ".fuel-system-container-label"],
            [".fuel-calculator-container", ".fuel-calculator-label"]
        ];

        var lines = [];

        for (var b = 0; b < boxes.length; b++) {
            var box = p.querySelector(boxes[b][0]);
            if (!box || !visible(box)) continue;

            var heading = text(box.querySelector(boxes[b][1]));
            if (heading) lines.push(heading);

            var fields = box.querySelectorAll(".fuel-calculator-field");
            if (fields.length) {
                for (var f = 0; f < fields.length; f++) {
                    if (!visible(fields[f])) continue;
                    var kids = fields[f].children;
                    if (kids.length >= 2) {
                        var fl = text(kids[0]), fv = text(kids[1]);
                        if (fl || fv) lines.push("  " + fl + ": " + fv);
                    } else {
                        var ft = text(fields[f]);
                        if (ft) lines.push("  " + ft);
                    }
                }
                continue;
            }

            var gauges = box.querySelectorAll(".engine-page-gauge");
            for (var i = 0; i < gauges.length; i++) {
                if (!visible(gauges[i])) continue;
                var line = A.engineGaugeLine(gauges[i]);
                if (line) lines.push("  " + line);
            }
        }

        return lines;
    };

    A.xlsEnginePageLines = function (p) {
        if (classList(p).indexOf("da40-engine-page") < 0) return [];
        var lines = [];

        // The dials, in screen order. A centre dial carries its own label; a double dial
        // carries units, and its NAME is the .oil-label / .fuel-label sibling - the first
        // double is Oil, the second Fuel, which is the order the page draws them in.
        var groupNames = [text(p.querySelector(".oil-label")) || "Oil",
                          text(p.querySelector(".fuel-label")) || "Fuel"];
        var doubles = 0;
        var container = p.querySelector(".gauge-container") || p;
        for (var c = 0; c < container.children.length; c++) {
            var kid = container.children[c];
            if (!visible(kid)) continue;
            if (classList(kid).indexOf("double-gauge-page") >= 0) {
                var halves = kid.querySelectorAll(".dial-gauge-parent");
                var parts = [];
                for (var h = 0; h < halves.length; h++) {
                    var hl = text(halves[h].querySelector(".dial-gauge-label-left")) ||
                             text(halves[h].querySelector(".dial-gauge-label-right"));
                    var hv = text(halves[h].querySelector(".dial-gauge-value-left")) ||
                             text(halves[h].querySelector(".dial-gauge-value-right"));
                    if (hv || hl) parts.push((hv || "no reading") + (hl ? " " + hl : ""));
                }
                var name = groupNames[Math.min(doubles, groupNames.length - 1)];
                doubles++;
                if (parts.length) lines.push(name + ": " + parts.join(", "));
            } else if (classList(kid).indexOf("dial-gauge-parent") >= 0) {
                var dl = text(kid.querySelector(".dial-gauge-label-center"));
                var dv = text(kid.querySelector(".dial-gauge-value-center"));
                if (dl || dv) lines.push((dl ? dl + ": " : "") + (dv || "no reading"));
            }
        }

        // The bar charts: the numbers the bars are drawn from, and the assist state.
        var temps = p.querySelector(".temperature-container");
        if (temps && visible(temps)) {
            lines.push(text(temps.querySelector(".temperature-container-label")) || "Temperature");
            var egt = [], cht = [];
            for (var n = 1; n <= 4; n++) {
                egt.push(n + ": " + A.lvar("L:DISP_EGT:" + n, 0));
                cht.push(n + ": " + A.lvar("L:DISP_CHT:" + n, 0));
            }
            var egtLabel = text(temps.querySelector(".egt-bar-chart .bar-chart-label")) || "EGT °F";
            var chtLabel = text(temps.querySelector(".cht-bar-chart .bar-chart-label")) || "CHT °F";
            lines.push("  " + egtLabel + " - cylinder " + egt.join(", "));
            lines.push("  " + chtLabel + " - cylinder " + cht.join(", "));
            // ⚠️ EACH CHART HAS ITS OWN DELTA-FROM-PEAK, AND THE FIRST ONE IN THE DOM IS
            // THE HIDDEN ONE. The EGT chart's block is display:none outside lean assist
            // while the CHT chart's is on screen, so a single querySelector read the dead
            // copy and the visible number was never spoken - the same trap that made the
            // CAS block read off a hidden duplicate. Walk both, name which chart each
            // belongs to, and emit only the ones actually showing.
            var charts = [[".egt-bar-chart", egtLabel], [".cht-bar-chart", chtLabel]];
            for (var ch = 0; ch < charts.length; ch++) {
                var host = temps.querySelector(charts[ch][0]);
                var peak = host && host.querySelector(".bar-chart-delta-peak");
                if (!peak || !visible(peak)) continue;
                lines.push("  " + charts[ch][1] + " " +
                           (text(peak.querySelector(".delta-peak-label")) || "Delta peak:") + " " +
                           (text(peak.querySelector(".delta-peak-value")) || ""));
            }
            lines.push("  Lean assist: " + (A.lvar("L:DISP_LEAN_ASSIST", 0) > 0.5 ? "on" : "off"));
        }

        // Electrical and the fuel calculator share the NG's box reader; total time is its own.
        var boxes = [
            [".electrical-container", ".electrical-container-label"],
            [".fuel-calculator-container", ".fuel-calculator-label"]
        ];
        for (var b = 0; b < boxes.length; b++) {
            var box = p.querySelector(boxes[b][0]);
            if (!box || !visible(box)) continue;
            var heading = text(box.querySelector(boxes[b][1]));
            if (heading) lines.push(heading);
            var fields = box.querySelectorAll(".fuel-calculator-field");
            for (var f = 0; f < fields.length; f++) {
                if (!visible(fields[f])) continue;
                var fk = fields[f].children;
                if (fk.length >= 2) lines.push("  " + text(fk[0]) + ": " + text(fk[1]));
            }
            var gauges = box.querySelectorAll(".engine-page-gauge");
            for (var i = 0; i < gauges.length; i++) {
                if (!visible(gauges[i])) continue;
                var line = A.engineGaugeLine(gauges[i]);
                if (line) lines.push("  " + line);
            }
        }
        var total = p.querySelector(".total-time-container");
        if (total && visible(total)) {
            var spans = total.querySelectorAll("span");
            var tl = spans.length ? text(spans[0]) : "Total Time";
            var tv = text(total.querySelector(".total-time-value"));
            lines.push((text(total.querySelector(".total-time-container-label")) || "Total Time") +
                       (tl && tv ? " - " + tl + ": " + tv : ""));
        }
        return lines;
    };

    /// A local variable read from inside the instrument - allowed, unlike a WRITE, which
    /// silently no-ops from an injected agent. Rounded, because these are gauge figures.
    A.lvar = function (name, fallback) {
        try {
            var v = SimVar.GetSimVarValue(name, "number");
            return (typeof v === "number" && isFinite(v)) ? Math.round(v) : fallback;
        } catch (e) { return fallback; }
    };

    A.page = function () {
        var p = document.querySelector(".mfd-page.open");
        if (!p || !visible(p)) return [];

        var lines = [];

        var checklist = A.checklistPage(p);
        if (checklist.length) return checklist;

        // THE EIS PAGE. The MFD's own full engine page draws DIALS, and it is the one
        // place on the aeroplane that prints the numbers the strip only gestures at -
        // oil pressure in bar, oil, coolant and gearbox temperatures, volts, amps and the
        // service hours. It uses a DIFFERENT markup from the strip beside it
        // (".dial-gauge-parent" with centred label and value elements, not ".eis-gauge"),
        // so the strip's reader finds nothing here and the page fell through to raw text:
        // "0100 Load % 303000 RPM 7000" - three gauges, their scale ends, and their values
        // welded into one token.
        // THE XLS ENGINE PAGE. A different page from the NG's (.da40-engine-page): six
        // dials, two of them DOUBLE (Oil = temperature and pressure, Fuel = flow and
        // pressure) whose labels are the UNITS with the name in a sibling .oil-label /
        // .fuel-label; then the EGT and CHT bar charts, which carry NO digits at all - the
        // bars are drawn from L:DISP_EGT:n / L:DISP_CHT:n and those are read back, which
        // is exactly the number each bar's height encodes - then the electrical block, the
        // fuel calculator and the Hobbs-style total time. Read generically it came out as
        // "Temperature EGT °F 1300 1400 1500 1600 1700 1 2 3 4 CHT °F ..." - the scale ticks
        // and cylinder numbers welded together with no temperature in it anywhere.
        var xls = A.xlsEnginePageLines(p);
        if (xls.length) return xls;

        var dials = p.querySelectorAll(".dial-gauge-parent");
        if (dials.length) {
            for (var g = 0; g < dials.length; g++) {
                if (!visible(dials[g])) continue;
                var dl = text(dials[g].querySelector(".dial-gauge-label-center")) ||
                         text(dials[g].querySelector(".eis-dial-gauge-label"));
                var dv = text(dials[g].querySelector(".dial-gauge-value-center")) ||
                         text(dials[g].querySelector(".eis-dial-gauge-value"));
                if (!dl && !dv) continue;
                lines.push(dl ? (dl + ": " + (dv || "no reading")) : dv);
            }

            // The rest of the page - the labelled blocks the dials sit among - read one
            // level down, skipping anything a dial has already answered for.
            var host = p.children.length === 1 ? p.children[0] : p;
            for (var k = 0; k < host.children.length; k++) {
                var kid = host.children[k];
                if (!visible(kid)) continue;
                if (kid.querySelector(".dial-gauge-parent") ||
                    classList(kid).indexOf("dial-gauge-parent") >= 0) continue;
                var kl = spacedText(kid);
                if (kl) lines.push(kl);
            }
            if (lines.length) return lines;
        }

        // THE ENGINE PAGE FIRST. Its four boxes are gauges, not group-boxes, so the
        // grouped reader below renders each box as one welded line.
        var engine = A.enginePageLines(p);
        if (engine.length) return A.dedupeAdjacent(engine);

        // GROUPED PAGES FIRST. The setup, status and utility pages are built as boxes of
        // labelled rows, and the generic one-level walk renders each COLUMN as a single
        // paragraph - "Date 31-AUG-26 Time 21:24:35 UTC Time Format UTC Time Offset..." -
        // in which no individual setting can be arrowed to or read on its own. These are
        // the pages a pilot CHANGES things on, so they are the ones that most need to be
        // read a field at a time.
        var boxed = A.groupboxLines(p, A.ownedGroupTitles());
        if (boxed.length) return boxed;

        // Prefer the page's own row-like structures where it has them: a flight plan and
        // a nearest list ARE lists, and reading them as one blob loses every boundary.
        var rowSel = ".fpl-list-item, .mfd-fpl-row, .checklist-item, .nearest-list-item, " +
                     ".mfd-list-item, .selectable-item";
        var rows = p.querySelectorAll(rowSel);
        if (rows.length) {
            for (var i = 0; i < rows.length; i++) {
                if (!visible(rows[i])) continue;
                var t = spacedText(rows[i]);
                if (!t) continue;
                var cls = classList(rows[i]);
                if (cls.indexOf("selected") >= 0 || cls.indexOf("highlight-select") >= 0) {
                    t += " (selected)";
                }
                lines.push(t);
            }
            if (lines.length) return lines;
        }

        // Otherwise walk one level of children, which keeps a form's fields on separate
        // rows instead of welding them into a paragraph.
        var host = p.children.length === 1 ? p.children[0] : p;
        for (var c = 0; c < host.children.length; c++) {
            if (!visible(host.children[c])) continue;
            var line = spacedText(host.children[c]);
            if (line) lines.push(line);
        }
        if (!lines.length) {
            var whole = spacedText(p);
            if (whole) lines.push(whole);
        }
        return lines;
    };

    // ---------------------------------------------------------------- pressing
    //
    // ONE SOFTKEY IS ONE BUTTON. There is no such thing as several items behind one key.
    // What a press does is either CYCLE A VALUE in place (CDI steps GPS, VOR1, VOR2, and
    // the current one shows in softkey-tab-value) or REPLACE ALL TWELVE KEYS with a
    // sub-menu — verified live: pressing 4, "PFD Opt", turned the row into
    // SVT / blank / Wind / DME / Bearing 1 / blank / Bearing 2 / blank / ALT Units /
    // STD Baro / Back / Alerts, and pressing 11, "Back", restored it. So the menu is a
    // TREE, and the way out is always a Back key somewhere in the row rather than a
    // separate gesture.
    //
    // Blank slots are real and are reported as blank: on that sub-page keys 2, 6 and 8 do
    // nothing, and a pilot needs to know that rather than wonder whether the key is
    // missing.
    // ⚠️ THERE IS NO A.press FOR SOFTKEYS, and there must not be one. A softkey press
    // goes over SimConnect from CowsDA40DisplayForm - see the split described below - and
    // an A.press(index, side) that lived here was silently overwritten by the bezel
    // A.press(name) a few lines further down, so it had never once been callable.

    // ---------------------------------------------------- the BEZEL, not the keys
    //
    // Everything on the bezel that is NOT a softkey: the FMS knob, MENU, ENT, CLR,
    // Direct-To, FPL, PROC, the range knob and the map joystick.
    //
    // THESE DO NOT ARRIVE OVER SIMCONNECT AND THE SOFTKEYS DO. Measured both ways, twice:
    // `1 (>H:AS1000_MFD_SOFTKEYS_11)` through the ordinary calculator path exits the
    // checklist, while `1 (>H:AS1000_MFD_FMS_Lower_DEC)` down the same path changes
    // nothing at all — and it is not the MobiFlight duplicate-command trap either, because
    // the same write carrying a `901 0 *` uniquifying prefix is equally inert. Fired
    // in-page through the instrument's own onInteractionEvent they all work.
    //
    // So this is a deliberate, MEASURED exception to "read over Coherent, write over
    // SimConnect": the softkeys keep the SimConnect path, and the rest of the bezel goes
    // down the socket, because for these keys there is no other road. It is the same
    // shape of finding as the A380's KCCU keys, which reach the MFD only when published
    // on the display's own event bus.
    //
    // The instrument element is the one custom tag that carries the handler —
    // wtg1000-pfd on one screen, wtg1000-mfd on the other — so the lookup asks for the
    // handler rather than for a name, and this agent stays identical on both displays.
    /// Fire a bezel key AND answer what the display now says, in ONE round trip.
    ///
    /// The window used to spend FOUR round trips on a single arrow key - read the summary
    /// before, fire the key, wait, read the summary again, then scrape - and each one is a
    /// full socket exchange with the Coherent debugger. Four of those per keystroke is
    /// what made the whole window feel slow, and no amount of shortening the settle could
    /// fix it because the round trips, not the wait, were the cost.
    ///
    /// Fired and read in the same call, the answer is occasionally the state from just
    /// BEFORE the key - the page needs a frame. That is why the caller compares it with
    /// what it last spoke and only then pays for a second look. In the common case, a
    /// highlight moving inside a list, the value has already changed and one trip is all
    /// it costs.
    A.press = function (name) {
        var fired = A.key(name);
        if (fired !== "ok") return fired;
        return A.state();
    };

    /// The display's state as ONE string: "ok|<cursor 0 or 1>|<summary>".
    ///
    /// The cursor flag is reported SEPARATELY rather than sniffed out of the summary text,
    /// because the caller has to tell "the cursor just came on" from "the summary happens
    /// to read the same as last time", and those are different questions. Inferring it
    /// from a "Cursor on." prefix meant a cursor that armed a frame later looked identical
    /// to a key that did nothing - which is exactly how pressing the cursor appeared to
    /// need TWO presses before it said anything.
    A.state = function () {
        // THE CURSOR COMES FROM THE INSTRUMENT NOW, not from a colour. A.M.cursor() asks
        // the page's own scroll controller, which is the flag the aeroplane itself tests,
        // so it cannot be wrong about a page whose highlight class this reader has never
        // seen - which is what every previous cursor bug was. The DOM reader stays as the
        // fallback for the one case the model cannot answer: a display whose instrument
        // element is not up yet, where A.M.cursor() returns null rather than false.
        var cursor;
        var modelCursor = null;
        try { modelCursor = A.M.cursor(); } catch (e) { modelCursor = null; }
        if (modelCursor === null) {
            var domCursor = "";
            try { domCursor = A.cursorField(); } catch (e) { domCursor = ""; }
            cursor = domCursor ? "1" : "0";
        } else {
            cursor = modelCursor ? "1" : "0";
        }

        var summary = "";
        try { summary = A.summary(); } catch (e) { summary = ""; }

        // The VIEW KEY rides along because the caller has to know WHAT it is looking at
        // before it can decide how long to wait for it. The page selector commits about a
        // second after the last turn and needs the long settle; a list highlight moves on
        // the frame it is asked to and waiting a second for it is an eternity.
        var key = "";
        try { key = A.M.viewKey(); } catch (e) { key = ""; }

        // A CHANGE OF VIEW SAYS WHERE IT LANDED. Accepting the Catalog's "Activate SimBrief
        // flight plan?" jumps to the flight plan page, and the readback was "SILVA" - the
        // leg the new page happened to focus - with nothing to say the page had changed.
        // The first readback after the view changes leads with the page title (a dialog
        // already leads with its own question). Only the first: a title on every field of
        // a page is the "Cursor on" fourteen times over again.
        if (A._saidView !== undefined && key !== A._saidView && key !== "MessageDialog") {
            var title = "";
            try { title = A.pageTitle(); } catch (e) { title = ""; }
            if (title && summary.indexOf(title) < 0) summary = title + (summary ? ", " + summary : "");
        }
        A._saidView = key;

        // WHICH field the cursor is on, as a number. Without it the window cannot tell a
        // knob turn that moved from one that could not - the knob does NOT wrap at the end
        // of a page, so a pilot at the bottom of a setup page heard the same field read
        // back a dozen times with nothing to say it was the last one.
        var focus = "";
        try {
            var f = A.M.fields();
            for (var i = 0; i < f.length; i++) if (f[i].focused) { focus = String(i); break; }
        } catch (e) { focus = ""; }

        return "ok|" + cursor + "|" + key + "|" + focus + "|" + summary;
    };

    /// Type an ident into whatever text field the cursor is on.
    A.typeIdent = function (str) {
        try { return A.M.type(str); } catch (e) { return "error " + e; }
    };

    /// What the aeroplane resolved the typed ident to, once its search has run.
    A.typedResult = function () {
        try { return A.M.typed(); } catch (e) { return "error " + e; }
    };

    /// The page map, as "Group|Page|Key" rows. An empty key means the stock G1000 draws
    /// the name but never built the page.
    A.pageList = function () {
        var out = [];
        try {
            var map = A.M.pageMap();
            for (var g = 0; g < map.length; g++) {
                for (var p = 0; p < map[g].pages.length; p++) {
                    // ⚠️ STUBS ARE LEFT OUT OF THE JUMP LIST, AND THAT DOES NOT CONTRADICT THE
                    // RULE ABOUT NEVER HIDING THEM - the two are different questions.
                    //
                    // Fifteen of this G1000's thirty-two advertised pages have an EMPTY KEY:
                    // Weather Data Link, TAWS-B, Trip Planning, Utility, GPS Status, XM Radio,
                    // System Status, Connext Setup, Databases, VRP and User WPT Information,
                    // and four of the Nearest pages. The instrument draws their names and never
                    // built them, so a sighted pilot's knob lands on them and nothing happens.
                    //
                    // When the KNOB lands on one, the readout must still say so - "not in this
                    // G1000" - because a page the aeroplane never built has to be tellable from
                    // one MSFSBA is failing to read. That rule is untouched.
                    //
                    // A JUMP LIST is a different thing: it is a menu of places you can GO, and
                    // an entry that cannot be gone to is a dead key with a name on it. Offering
                    // fifteen of them means a pilot picks one, hears nothing happen, and has to
                    // learn by trial which half of the list is real.
                    if (!map[g].pages[p].key) continue;
                    out.push(map[g].group + "|" + map[g].pages[p].name + "|" + map[g].pages[p].key);
                }
            }
        } catch (e) { }
        return out.join(String.fromCharCode(10));
    };

    /// Open a page directly, then answer with the same state string a key press does.
    A.goPage = function (key) {
        var r;
        try { r = A.M.goPage(key); } catch (e) { r = "error " + e; }
        if (r !== "ok") return r;
        return A.state();
    };

    A.key = function (name) {
        var el = document.querySelector("wtg1000-mfd") || document.querySelector("wtg1000-pfd");
        if (!el || typeof el.onInteractionEvent !== "function") return "no instrument";
        try {
            el.onInteractionEvent([name]);
            return "ok";
        } catch (e) {
            return "error " + e;
        }
    };

    // ------------------------------------------------- what to say after a key
    //
    // THE FIRST TURN OF THE FMS KNOB DOES NOT CHANGE THE PAGE. It opens the page
    // SELECTOR, showing where you are; the next turn moves. That is how the real G1000
    // behaves and it is deliberately not smoothed over here — a sighted pilot gets the
    // same two-step, and quietly sending a second turn would make the knob skip a page
    // every time the selector had closed.
    //
    // But it does mean that reading back the page TITLE after a turn reports the page the
    // pilot is still on, which sounds exactly like a key that did nothing. So while the
    // selector is up, what gets spoken is the SELECTOR: the group and page it is offering.
    /// The PFD's open window, which is its equivalent of the MFD's page.
    ///
    /// The PFD has no .nav-data-bar-page-title - that is an MFD thing - so A.summary()
    /// fell all the way through to A.pageTitle() and answered every bezel key on the PFD
    /// with a page name that does not exist there. Exactly the fault the MFD had when it
    /// said "CHKLST - Checklist" to everything, one display over.
    ///
    /// What a PFD keystroke actually changes is which POPOUT is open - nearest airports,
    /// the timer, the transponder - and which entry inside it is selected. A popout is
    /// open iff its opacity is non-zero: they all stay display:block and full size whether
    /// open or shut, which is the trap this aeroplane has sprung four times now.
    A.pfdPopout = function () {
        // ⚠️ PFD ONLY, and this was NOT belt-and-braces - it fired on the MFD and said
        // "mfd pageselect, System Setup" to a pilot, because the MFD's own page selector
        // is itself a .popout-dialog. The MFD has its own, better branches for the
        // selector and the page menu further down summary(); this one exists purely
        // because the PFD has no page title to fall back on.
        if (A.side() !== "PFD") return "";

        var dialogs = document.querySelectorAll(".popout-dialog");

        for (var i = 0; i < dialogs.length; i++) {
            var d = dialogs[i];
            if (!visible(d)) continue;
            if (parseFloat(window.getComputedStyle(d).opacity || "1") < 0.05) continue;

            // The class carries the identity ("pfd-nearest-airport"); turn it into words
            // rather than reading a CSS class name aloud.
            var name = "";
            var parts = classList(d);
            for (var c = 0; c < parts.length; c++) {
                var t = parts[c];
                if (!t || t === "popout-dialog" || t === "open" || t === "subview") continue;
                name = t.replace(/^pfd-/, "").replace(/-/g, " ");
                break;
            }
            if (!name) continue;

            var chosen = d.querySelector(".highlight-select");
            var entry = chosen && visible(chosen) ? text(chosen) : "";

            return name + (entry ? ", " + entry : "");
        }

        return "";
    };

    // ================================================== THE INSTRUMENT'S OWN VIEW MODEL
    //
    // Everything above this line reads the G1000 by looking at its MARKUP. That is right
    // for gauges and text, and it has now been wrong about the CURSOR five separate times,
    // because a cursor is not a colour - it is part of the instrument's STATE, and the
    // markup is only where that state happens to get painted. Every fix so far was "learn
    // one more class name", and every next page used a different one.
    //
    // The G1000 is a JavaScript instrument and it keeps that state in an object we can
    // simply ask. `wtg1000-mfd` IS the instrument, and it carries a `viewService` that owns
    // every page and dialog; each of those owns a scroll controller that knows
    //
    //   whether the cursor is on ....... getIsScrollEnabled()
    //   which field it is on ........... getFocusedUiControl()
    //   whether that field is OPEN ..... getActivatedUiControl()
    //   what the field's choices are ... props.data
    //
    // read out of the aeroplane instead of inferred from a stylesheet.
    //
    // THE INTERACTION RULES BELOW ARE THE AIRCRAFT'S OWN SOURCE, not a guess: they were
    // read off the live instrument with Function.prototype.toString and then confirmed one
    // keystroke at a time against the real display.
    //
    //   UPPER_PUSH ................ toggles the cursor
    //   cursor OFF + any knob ..... opens the PAGE SELECTOR (upper turns the page,
    //                               lower turns the page GROUP)
    //   cursor ON,  LOWER ......... moves BETWEEN fields
    //   cursor ON,  UPPER ......... acts ON the focused field: opens its list of choices,
    //                               or cycles the character of a text field
    //   in a list,  LOWER ......... moves the highlight; ENT accepts, CLR cancels
    //   in a text field, UPPER .... cycles the character, LOWER moves along the field
    //
    // That is the whole interaction model of both displays, and none of it was knowable
    // from the DOM. It also settles a question a pilot asked out loud: the FIRST turn of
    // the upper knob on a text field only ACTIVATES it and changes nothing, which is the
    // aeroplane behaving correctly rather than a dropped keystroke.
    //
    // TWO CONTROL FRAMEWORKS live in this instrument and they are not interchangeable.
    // The setup pages, the dialogs and Direct-To use the older one (`scrollController`
    // with a flat `controls` array). The flight plan page and every LIST use the newer
    // `G1000UiControl` (`registeredControls`, `focusedIndex`, `getSelectedIndex`). That is
    // why the flight plan page "would not even arm": its scrollController is EMPTY because
    // all of its controls live in the other framework, so a control count said zero and the
    // page read as dead when it was working perfectly. The CURSOR flag stays on the scroll
    // controller for both - which is exactly why that, and not the control count, is what
    // gets asked here.
    A.M = {};

    A.M.el = function () {
        return document.querySelector("wtg1000-mfd") || document.querySelector("wtg1000-pfd");
    };

    A.M.vs = function () {
        var e = A.M.el();
        return (e && e.viewService) ? e.viewService : null;
    };

    /// Subjects and plain values look the same from here; this reads either.
    A.M.get = function (sub) {
        try { return (sub && typeof sub.get === "function") ? sub.get() : sub; }
        catch (e) { return null; }
    };

    A.M.view = function () { var v = A.M.vs(); return v ? A.M.get(v.activeView) : null; };
    A.M.viewKey = function () { var v = A.M.vs(); return v ? (A.M.get(v.activeViewKey) || "") : ""; };
    A.M.pageKey = function () { var v = A.M.vs(); return v ? (A.M.get(v.openPageKey) || "") : ""; };

    /// The cursor, from the instrument rather than from a colour. Null means the model
    /// could not be reached at all, which is a different answer from "off" and is treated
    /// as such - the DOM reader is the fallback for that case only.
    A.M.cursor = function () {
        var view = A.M.view();
        if (!view || !view.scrollController) return null;
        try { return !!view.scrollController.getIsScrollEnabled(); } catch (e) { return null; }
    };

    A.M.isGroup = function (c) { return !!(c && c.scrollController); };

    /// What KIND of thing a control is, which decides what a keystroke will do to it.
    A.M.kindOf = function (c) {
        if (!c) return "";
        // A waypoint entry box is a group wrapping a text input; it is a LEAF here,
        // because descending into it would lose the fact that it can be typed into.
        if (c.inputComponentRef) return "waypoint";
        // ⚠️ A NUMBER field is a group wrapping a newer-framework digit input, and its
        // inner scroll controller is EMPTY - the digits register on the other framework.
        // Treating it as a group therefore dropped it entirely, which is how the Aux
        // page's Time Offset and the nearest-airport Minimum Length came to be missing
        // from a field list that claimed to be complete.
        if (c.control && c.control.digitValues !== undefined) return "number";
        // An ACTION BUTTON carries its own caption in props.text and has nothing else -
        // "Sign In", "Check Subscription", "Activate?", "Hold?". Without this it went
        // through the label heuristic, which climbed out of the button into the box around
        // it and answered "AliasN/ASubscriptionN/ACheck Subscription: Sign In".
        //
        // ⚠️ THE CAPTION IS SOMETIMES A SUBJECT rather than a string, because it CHANGES -
        // Navigraph's button reads "Sign In" or "Sign Out" depending on who is logged in.
        // A typeof "string" test therefore caught one of the two buttons on that page and
        // left the other welded to its box.
        if (A.M.captionOf(c)) return "button";
        if (c.MenuItems !== undefined) return "select";
        if (typeof c.getText === "function" && c.dataEntry) return "input";
        if (A.M.isGroup(c)) return "group";
        return "field";
    };

    /// A button's caption, whether it is a fixed string or a Subject that changes.
    A.M.captionOf = function (c) {
        try {
            var t = c.props && c.props.text;
            if (t === undefined || t === null) return "";
            var v = (typeof t.get === "function") ? t.get() : t;
            return (typeof v === "string" && v.length > 0) ? v : "";
        } catch (e) { return ""; }
    };

    A.M.valueOf = function (c) {
        try {
            // A button IS its caption; there is no label-and-value to separate.
            var caption = A.M.captionOf(c);
            if (caption) return caption;
            if (c.inputComponentRef && c.inputComponentRef.instance) {
                return c.inputComponentRef.instance.getText();
            }
            // A number field draws its value TWICE - once for the resting display and once
            // for the digit-by-digit editor - and both nodes stay in the DOM, so reading
            // the wrapper gives "03000 FT 3000 FT". Which one is live depends on whether
            // the field is being edited.
            if (c.control && c.control.digitValues !== undefined) {
                var editing = false;
                try { editing = !!A.M.get(c.control.isEditing); } catch (e3) { }
                var order = editing
                    ? [c.control.activeRef, c.control.inactiveRef]
                    : [c.control.inactiveRef, c.control.activeRef];
                for (var r = 0; r < order.length; r++) {
                    var inst = order[r] && order[r].instance;
                    var v = inst ? text(inst) : "";
                    if (v) return v;
                }
                if (c.control.rootRef && c.control.rootRef.instance) {
                    return text(c.control.rootRef.instance);
                }
            }
            if (typeof c.getText === "function") return c.getText();
            var hl = c.getHighlightElement ? c.getHighlightElement() : null;
            if (hl) return text(hl);
        } catch (e) { }
        return "";
    };

    /// The label is whatever the row says once the VALUE is taken off the end of it -
    /// "Time FormatUTC" minus "UTC". The climb is capped and a suspiciously long answer is
    /// thrown away, because the ancestor chain ends at the whole page and a "label" of four
    /// hundred characters is worse than none.
    /// The DOM node a control actually draws itself into. There are three shapes of it and
    /// only knowing one of them is why the two number fields on the Aux page came out with
    /// no label at all: they have neither a containerRef nor a highlight element, and their
    /// markup hangs off the digit input in the OTHER framework.
    A.M.hostOf = function (c) {
        try {
            if (c.containerRef && c.containerRef.instance) return c.containerRef.instance;
            if (c.control && c.control.rootRef && c.control.rootRef.instance) {
                return c.control.rootRef.instance;
            }
            if (c.getHighlightElement) {
                var h = c.getHighlightElement();
                if (h) return h;
            }
        } catch (e) { }
        return null;
    };

    A.M.labelOf = function (c, value) {
        var host = A.M.hostOf(c);
        if (!host) return "";

        // WHAT TO TAKE OFF THE ROW. Usually just the value, but a NUMBER field renders
        // itself TWICE - "03000FT" for the digit editor and "3000FT" for the resting
        // display, both left in the DOM - so taking off only the one being shown left the
        // other welded to the label: "Minimum Length03000FT". Both come off.
        var strip = [];
        if (value) strip.push(value);
        try {
            if (c.control && c.control.digitValues !== undefined) {
                var refs = [c.control.activeRef, c.control.inactiveRef];
                for (var r = 0; r < refs.length; r++) {
                    var t2 = (refs[r] && refs[r].instance) ? text(refs[r].instance) : "";
                    if (t2 && strip.indexOf(t2) < 0) strip.push(t2);
                }
            }
        } catch (e) { }

        // Longest first, or stripping "3000FT" out of "03000FT" leaves a stray zero.
        strip.sort(function (a, b) { return b.length - a.length; });

        var e2 = host.parentElement;
        for (var d = 0; d < 6 && e2; d++) {
            var whole = text(e2);
            var cut = whole;
            for (var i = 0; i < strip.length; i++) cut = cut.split(strip[i]).join(" ");
            cut = cut.replace(/\s+/g, " ").trim();
            // A field that is EMPTY draws placeholder dashes, and those are not part of
            // its name: "Time Offset --:--" is the row, "Time Offset" is the label.
            cut = cut.replace(/[^A-Za-z0-9)]+$/, "").trim();

            // The row has to say MORE than the value, or this is the value's own node and
            // the label lives further up. A LABEL IS WORDS: without that test the Aux
            // page's Time Offset answered with the dashes of its own empty second
            // rendering ("--:--") instead of climbing one more level to its name.
            // A LABEL IS A NAME, and these two tests are what stop the climb from walking
            // past the row into the whole panel and calling the OTHER fields' values a
            // label. The PFD backlight window did exactly that and answered
            // "Auto100.00%Auto100.00: PFD Display" - four neighbouring values welded into
            // a name. A real label has words, no percent sign and no long runs of digits.
            var looksLikeValues = cut.indexOf("%") >= 0 || /\d{2,}/.test(cut);
            if (cut.length > 0 && cut.length <= 48 && cut !== whole &&
                /[A-Za-z]/.test(cut) && !looksLikeValues) {
                return cut;
            }
            if (!strip.length && whole.length > 0 && whole.length <= 48) return whole;
            e2 = e2.parentElement;
        }
        return "";
    };

    /// The BOX a field lives in - "Date / Time", "Display Units", "Nearest Airport".
    ///
    /// A pilot turning the knob down a setup page hears fourteen field names with nothing
    /// to hang them on, and "Minimum Length" means very little until you know it is the
    /// NEAREST AIRPORT box. A sighted pilot never has to ask: the boxes are drawn round the
    /// fields and the heading is right there.
    ///
    /// `groupbox` + `groupbox-title` is the instrument's own pairing and is used on every
    /// page family - the setup pages, Direct-To, the page menus, the procedure pages - so
    /// this is one rule rather than a per-page table.
    A.M.groupOf = function (c, label) {
        var host = A.M.hostOf(c);

        // FIRST PASS: the real box. The setup pages, Direct-To, the page menus and the
        // procedure pages all use groupbox + groupbox-title, and that pairing is exact.
        var e = host;
        for (var d = 0; d < 8 && e; d++) {
            if (hasClassContaining(e, "groupbox")) {
                var boxed = e.querySelector(".groupbox-title");
                if (boxed) {
                    var bv = text(boxed);
                    if (bv && bv.length <= 40) return bv;
                }
            }
            e = e.parentElement;
        }

        // SECOND PASS, and ONLY when there was no box at all. The PFD's own windows name
        // their rows with their own classes - "timerref-timer-title", "timerref-mins-title"
        // - so a groupbox-only lookup left every field in the timer window with no label,
        // "0:00:00" with nothing to say what it was.
        //
        // ⚠️ IT MUST NOT RUN WHEN A BOX EXISTS. The setup page's ROWS also carry a title
        // element, so letting this pass run there answered "Time Format, Time Format: UTC" -
        // the label twice. Anything matching the label is rejected for the same reason.
        e = host;
        for (var d2 = 0; d2 < 8 && e; d2++) {
            var titled = e.querySelector("[class*=title]");
            if (titled && titled !== host && !titled.contains(host)) {
                var tv = text(titled);
                if (tv && tv.length <= 40 && tv !== label) return tv;
            }
            e = e.parentElement;
        }

        return "";
    };

    /// Every field on the page, in the order the knob walks them.
    A.M.fields = function () {
        var view = A.M.view();
        var out = [];
        if (!view || !view.scrollController) return out;
        A.M.walk(view.scrollController, "", out, 0);
        A.M.nameByRow(out);
        return out;
    };

    // ⚠️ A GROUP TITLE EVERY FIELD SHARES IS NOT A LABEL, AND THE ROW IS THROWN AWAY.
    //
    // The PFD's nearest-airport window is the case that found both. Its fifty fields all
    // report the groupbox title "RWY", and their own labels are empty, so the readout was
    // "RWY, EHAM" then "RWY, 118.105" then "RWY, EHRD" - the same meaningless word before
    // every entry, and nothing saying which airport a frequency belonged to. Arrowing it
    // gave "RWY, 129.405", a number with no owner.
    //
    // The instrument pairs them itself and we were discarding it: the control paths run
    // 0.0.0 / 0.0.1, 0.1.0 / 0.1.1 - <list>.<row>.<field> - so an airport and its frequency
    // are the two controls of ONE row. Ask the path, do not pattern-match the value; an
    // ident and a frequency are only sometimes distinguishable by shape, and the next
    // window will pair different things.
    //
    // Two rules, both structural:
    //   - a group title shared by EVERY field discriminates nothing (the window is already
    //     announced by name), so it is dropped. One that varies is kept - it is what makes
    //     "Nearest Airport, Minimum Length" read correctly on the setup page.
    //   - a field that is not the FIRST of its row is labelled with the first one's value,
    //     so the frequency is announced as the airport's.
    A.M.nameByRow = function (fields) {
        if (!fields || !fields.length) return;

        // Drop a group that is the same on every field.
        var g0 = fields[0].group || "", uniform = true;
        for (var i = 1; i < fields.length; i++) {
            if ((fields[i].group || "") !== g0) { uniform = false; break; }
        }
        if (uniform && g0) {
            for (var j = 0; j < fields.length; j++) fields[j].group = "";
        }

        // Label the rest of a row with the row's first value.
        var rowOf = function (p) {
            var t = String(p || "");
            var k = t.lastIndexOf(".");
            return k < 0 ? "" : t.substring(0, k);
        };

        // ⚠️ A TOP-LEVEL FIELD HAS NO ROW, AND THAT IS WHAT KEEPS THIS SAFE. Aux System
        // Setup's fourteen controls sit flat at paths "0".."13" with no dot at all, so
        // rowOf() gives "" and every one of them is skipped here - without that, they would
        // read as one row and thirteen fields would be labelled with the first one's value.
        // Row naming applies only where the instrument actually nested the controls.
        // ⚠️ TWO THINGS CANNOT NAME A ROW, AND THE HOLD DIALOG HAD BOTH.
        //
        // Its leg row draws EITHER a distance or a time, and keeps both in the DOM: the
        // hidden "4.0 NM" pair registers as the row's FIRST control and the visible time
        // digits follow it. So the row was named after the alternative that is not on
        // screen, and the readout said "4.0: 1" - announcing a four-mile leg to a pilot
        // holding for one minute. The same row then offers its minute digit as a name for
        // the seconds digits, which names nothing at all.
        //
        //   - A field that is NOT SELECTABLE is not the row's subject. The knob cannot
        //     reach it, and here it is the branch the page is not drawing.
        //   - A value with NO LETTER IN IT cannot be a name. "4.0" and "1" describe the
        //     thing they sit in; only a word can say what the thing IS.
        //
        // A row that has neither leaves its later fields unlabelled, which reads as the
        // bare value - imperfect, and still better than a confident wrong one.
        var canName = function (f) {
            if (!f || f.able === false) return false;
            return /[A-Za-z]/.test(String(f.value || ""));
        };

        var firstOfRow = {};
        for (var m = 0; m < fields.length; m++) {
            var r = rowOf(fields[m].p);
            if (!r) continue;
            if (!(r in firstOfRow)) { firstOfRow[r] = canName(fields[m]) ? fields[m].value : ""; continue; }
            // Not the first of its row, and carrying no label of its own.
            if (!fields[m].label && firstOfRow[r]) fields[m].label = firstOfRow[r];
        }
    };

    A.M.walk = function (sc, prefix, out, depth) {
        var n = 0;
        try { n = sc.getControlsCount(); } catch (e) { return; }

        for (var i = 0; i < n; i++) {
            var c = sc.controls[i];
            var path = prefix + (prefix ? "." : "") + i;
            var kind = A.M.kindOf(c);

            if (kind === "group" && depth < 4) {
                var inner = 0;
                try { inner = c.scrollController.getControlsCount(); } catch (e) { }
                // An EMPTY group with nothing in the other framework either is a
                // placeholder the page never filled in; reporting "blank field" four times
                // running is noise, so it is dropped.
                if (inner > 0) { A.M.walk(c.scrollController, path, out, depth + 1); }
                continue;
            }

            var value = A.M.valueOf(c);
            // A field the aeroplane has GREYED OUT is still a field, and a pilot needs to
            // know it is there and why the knob skips it - the Aux page's Time Offset is
            // unavailable precisely because Time Format is UTC. Silently dropping it would
            // be deciding on the pilot's behalf what they are allowed to know about.
            //
            // ⚠️ IT IS SPOKEN AS ", dimmed" - ONE WORD FOR ONE IDEA, APP-WIDE. The A380 MFD,
            // the A380 MCDU and the flyPad browser view all say "dimmed"; this display said
            // ", not available" in three places and " (not available)" in a fourth, so a
            // pilot flying both aeroplanes met three spellings of the same fact. The one
            // survivor of that phrase is A.M.goPage's RETURN CODE, which means a page key
            // the display does not know - a different thing entirely, and read by the window
            // rather than spoken.
            var able = true;
            try { if (c.getIsFocusable) able = !!c.getIsFocusable(); } catch (e2) { }

            // ⚠️ A WAYPOINT BOX IS OPEN WHEN THE INPUT INSIDE IT IS OPEN, not when the
            // wrapper is. Asking the wrapper said "not editing" while the pilot was
            // typing into it, which is the one moment they most need to be told they are.
            var active = !!(c.getIsActivated && c.getIsActivated());
            if (kind === "waypoint") {
                try { active = !!c.inputComponentRef.instance.getIsActivated(); } catch (e4) { }
            }
            if (kind === "number") {
                try { active = !!A.M.get(c.control.isEditing); } catch (e5) { }
            }

            var label = kind === "button" ? "" : A.M.labelOf(c, value);
            var group = A.M.groupOf(c, label);
            // A Direct-To box has no label of its own on screen - the pilot is looking at
            // a dialog whose whole subject is the waypoint - so it gets named rather than
            // read out as a bare ident with no idea what it is.
            if (!label && kind === "waypoint") label = "Waypoint";

            out.push({
                p: path,
                kind: kind,
                group: group,
                label: label,
                value: value,
                ctrl: c,
                able: able,
                focused: !!(c.getIsFocused && c.getIsFocused()),
                active: active
            });
        }
    };

    A.M.focused = function () {
        var f = A.M.fields();
        for (var i = 0; i < f.length; i++) if (f[i].focused) return f[i];
        return null;
    };

    /// A LIST - the page selector, a page menu, or the choices behind a setup field. All
    /// three are the same newer-framework list component, so one reader serves them all.
    A.M.list = function () {
        var view = A.M.view();
        if (!view || !view.listRef || !view.listRef.instance) return null;

        var lr = view.listRef.instance;
        var items = [];
        try {
            var ic = (lr.itemsContainer && lr.itemsContainer.instance)
                ? lr.itemsContainer.instance : lr.itemsContainer;
            var kids = (ic && ic.children) ? ic.children : [];
            for (var i = 0; i < kids.length; i++) {
                var v = text(kids[i]);
                if (v) items.push(v);
            }
        } catch (e) { }

        var sel = -1;
        try { sel = lr.getSelectedIndex(); } catch (e) { }
        if (!items.length) return null;
        return { items: items, sel: sel, len: items.length };
    };

    /// The text input the pilot is on, if any. Preferring the ACTIVATED one matters: a
    /// page can hold several boxes and only the open one is being typed into.
    A.M.input = function () {
        var view = A.M.view();
        if (!view) return null;

        var best = null, fallback = null;

        function consider(c) {
            var ic = (c.inputComponentRef && c.inputComponentRef.instance)
                ? c.inputComponentRef.instance
                : ((typeof c.getText === "function" && c.dataEntry) ? c : null);
            if (!ic) return false;
            if (!fallback) fallback = ic;
            try {
                if (ic.getIsActivated() || ic.getIsFocused() ||
                    (c.getIsFocused && c.getIsFocused())) best = ic;
            } catch (e) { }
            return true;
        }

        function scan(sc, depth) {
            var n = 0;
            try { n = sc.getControlsCount(); } catch (e) { return; }
            for (var i = 0; i < n; i++) {
                var c = sc.controls[i];
                if (consider(c)) continue;
                if (c.scrollController && depth < 4) scan(c.scrollController, depth + 1);
            }
        }

        if (view.scrollController) { try { scan(view.scrollController, 0); } catch (e) { } }
        if (best) return best;

        // ⚠️ AND NOW THE OTHER FRAMEWORK, WITHOUT WHICH TYPING WAS DEAD ON THE ONE PAGE THAT
        // NEEDS IT MOST. This function used to give up at the top on `!view.scrollController`,
        // so on the FLIGHT PLAN PAGE - whose scroll controller is EMPTY, because it is built on
        // G1000UiControl - it always returned null and Ctrl+T answered "Nothing to type into.
        // Put the cursor on a waypoint field first." at a pilot whose cursor was sitting
        // exactly where it should be, on the destination box.
        //
        // The READER was taught to walk both frameworks when the flight plan page first read
        // as dead. This, the WRITER, was not, and the split went unnoticed because reading the
        // field worked perfectly - the announcement said "Destination, blank" and then refused
        // to let anybody fill it in. Live report, 2026-09-03, with the speech dump to prove the
        // cursor was on the right field.
        //
        // Deepest link first: the focus chain ends at the control actually being edited, and a
        // parent group can carry a stale input reference from a sibling field.
        try {
            var roots = A.M.f2Roots(view);
            for (var r = 0; r < roots.length; r++) {
                var chain = A.M.f2Chain(roots[r]);
                for (var k = chain.length - 1; k >= 0; k--) {
                    if (consider(chain[k]) && best) return best;
                }
            }
        } catch (e) { }

        return best || fallback;
    };

    /// Which character of a text field the knob is sitting on. A pilot editing an ident one
    /// character at a time has no other way to know where in the box they are.
    /// ⚠️ THE POSITION IS NEWS ONLY WHEN IT MOVES. Spelling an ident is dozens of knob
    /// clicks, and this said "character 1" before every one of them: "character 1, A",
    /// "character 1, B", "character 1, C" as the pilot cycled ONE letter. Reported from the
    /// cockpit as "clunky as hell", and the ruling is exact - "I should only say the number
    /// of the character when moving to it first, and then when pressing up and down... just
    /// say A, B, C".
    ///
    /// So the LOWER knob, which steps between characters, changes the index and earns the
    /// number; the UPPER knob, which cycles the character in place, does not and answers
    /// with the letter alone. Nothing here has to know which knob was turned - the index
    /// itself is the signal, which is why this is a comparison and not a key-name test.
    ///
    /// Returning to a position later says its number again, because the pilot has moved.
    A.M.charSay = function () {
        var ic = A.M.input();
        if (!ic) return "";
        try {
            var t = ic.dataEntry.text, i = ic.dataEntry.highlightIndex;
            var ch = t.charAt(i);
            var said = (ch === "_" || ch === "") ? "blank" : ch;

            var moved = (A.M._edIdx !== i);
            A.M._edIdx = i;

            return moved ? ("character " + (i + 1) + ", " + said) : said;
        } catch (e) { return ""; }
    };

    /// TYPE AN IDENT. This is the display's own text input driven at full speed rather than
    /// one knob click per letter, and it is NOT a shortcut around the aeroplane: the G1000
    /// itself offers keyboard entry into these boxes - the input component carries
    /// activateKeyboardInput and setValueFromOS for exactly that - and every character goes
    /// through the same setText the on-screen keyboard uses. The database search, the
    /// autocomplete and the facility lookup then all run exactly as they do for a sighted
    /// pilot, because they are the aircraft's own and nothing here reimplements them.
    ///
    /// Twenty-eight knob clicks to spell one four-letter ident is not "the same aircraft at
    /// the same depth"; it is the same aircraft made unusable. The knob path is still there
    /// unchanged for anyone who wants it, and both end in the same input component.
    A.M.type = function (str) {
        var ic = A.M.input();
        if (!ic) return "no text field";

        var s = String(str || "").toUpperCase().replace(/[^A-Z0-9]/g, "");
        if (!s) return "nothing to type";

        try {
            if (!ic.getIsActivated()) ic.activate();
            // Park the edit cursor on the LAST character typed, which is where a pilot who
            // had turned the knob would be, so the next turn continues the ident rather
            // than restarting it.
            ic.setText(s, s.length - 1, true);
            return "ok";
        } catch (e) { return "error " + e; }
    };

    /// What the aeroplane made of what was typed, after its own debounced database search
    /// has had time to run. The ident that comes back is the AUTOCOMPLETED one, so a pilot
    /// who typed three letters hears the whole ident and the facility it belongs to.
    A.M.typed = function () {
        var ic = A.M.input();
        var bits = [];
        if (ic) { try { bits.push(ic.getRawText()); } catch (e) { } }

        var name = text(firstVisible(".wpt-entry-name"));
        var city = text(firstVisible(".wpt-entry-location"));
        if (name) bits.push(name);
        if (city) bits.push(city);

        if (!bits.length) return "nothing entered";
        if (bits.length === 1) return bits[0] + ", no match";
        return bits.join(", ");
    };

    // ------------------------------------------------------- the OTHER control framework
    //
    // The flight plan page, the checklist and every list are built on `G1000UiControl`,
    // which does not use the scroll controller at all: a control holds its children in
    // `registeredControls`, remembers which of them has focus in `focusedIndex`, and the
    // focused one may itself have children. So "what is the cursor on" is a WALK, not a
    // lookup, and a reader that only knew the flat framework reported those pages as
    // having nothing on them - which is precisely why the flight plan page and the
    // checklist both read as dead when they were working.
    //
    // Where the text lives differs by control: a checklist item keeps it in `itemRef`, a
    // list in `el`, a number field in `rootRef`. All of them are tried rather than assumed,
    // because assuming one is how this went wrong the first four times.
    A.M.f2Roots = function (view) {
        var roots = [];
        if (!view) return roots;

        var names = Object.getOwnPropertyNames(view);
        for (var i = 0; i < names.length; i++) {
            var v;
            try { v = view[names[i]]; } catch (e) { continue; }
            if (!v || typeof v !== "object") continue;

            var inst = (v.instance !== undefined && v.instance !== null) ? v.instance : v;
            if (inst && typeof inst === "object" && inst._UICONTROL_ !== undefined) {
                roots.push(inst);
            }
        }
        return roots;
    };

    /// The focus chain from a root DOWNWARDS, deepest last.
    ///
    /// It is the chain and not just the last link, because the deepest control does not
    /// always carry the readable element: the nearest-airport page focuses a group whose
    /// innermost child renders nothing of its own, and asking only the bottom of the chain
    /// answered "nothing under the cursor" over a page full of aerodromes. The deepest link
    /// that HAS text is the answer.
    A.M.f2Chain = function (root) {
        var chain = [], d = root, guard = 0;
        while (d && guard++ < 10) {
            chain.push(d);
            if (!(d.length > 0)) break;

            var i = -1;
            try { i = d.getFocusedIndex(); } catch (e) { break; }
            if (i < 0) break;

            var c = null;
            try { c = d.getChild(i); } catch (e) { break; }
            if (!c) break;
            d = c;
        }
        return chain;
    };

    A.M.f2Element = function (c) {
        // ⚠️ A LIST IS NOT ONE THING. Asking a list for its element hands back every row it
        // holds, and the nearest-airport page then read back all ten aerodromes welded
        // together after every knob click. A list knows which row is selected; ask it.
        try {
            if (typeof c.getSelectedElement === "function") {
                var sel = c.getSelectedElement();
                if (sel && sel.nodeType === 1) return sel;
            }
        } catch (e) { }

        var names = ["itemRef", "el", "rootRef", "activeRef", "containerRef"];
        for (var i = 0; i < names.length; i++) {
            try {
                var v = c[names[i]];
                var inst = (v && v.instance) ? v.instance : v;
                if (inst && inst.nodeType === 1) return inst;
            } catch (e) { }
        }
        try { if (c.props && c.props.ref && c.props.ref.instance) return c.props.ref.instance; }
        catch (e) { }
        try {
            if (c.getHighlightElement) {
                var h = c.getHighlightElement();
                if (h) return h;
            }
        } catch (e) { }
        return null;
    };

    /// A LIST ROW IS READ FIELD BY FIELD, never through the text joiner. The last digit of
    /// an identifier and the first of a bearing are both digits, so any rule that keeps a
    /// clock from becoming "0 : 0 0" welds "VCBI 020" into one token. Its own child
    /// elements ARE the fields: "VCBI, 250°, 0.1 NM".
    A.M.rowText = function (el2) {
        if (!el2) return "";

        var kids = el2.children;
        if (kids && kids.length > 1) {
            var parts = [];
            for (var i = 0; i < kids.length; i++) {
                var v = text(kids[i]);
                if (v) parts.push(v);
            }
            if (parts.length) return parts.join(", ");
        }
        return text(el2);
    };

    A.M.f2Say = function () {
        var view = A.M.view();
        if (!view) return "";

        var roots = A.M.f2Roots(view);
        for (var i = 0; i < roots.length; i++) {
            var focused = false;
            try { focused = !!roots[i].isFocused; } catch (e) { }
            if (!focused) continue;

            var chain = A.M.f2Chain(roots[i]);
            var t2 = "";
            // Deepest first: the innermost thing with any text of its own is the one the
            // cursor is actually on. The root itself is skipped - its text is the whole
            // page.
            for (var c = chain.length - 1; c >= 1; c--) {
                var el2 = A.M.f2Element(chain[c]);
                var candidate = A.M.rowText(el2);
                if (candidate) { t2 = candidate; break; }
            }
            if (!t2) continue;

            // A checklist NOTE or a terms-and-conditions paragraph is a legitimate item and
            // it can run to five hundred characters. Speaking all of it after every knob
            // click would bury the next one, and the window itself still carries the whole
            // text for a pilot who wants to read it.
            return t2.length > 160 ? t2.substring(0, 160) + ", continues in the window" : t2;
        }
        return "";
    };

    // ------------------------------------------------------------- the DISPLAY UNITS
    //
    // The pilot sets these on the MFD's Aux System Setup page — NAV angle, distance and
    // speed, altitude and vertical speed, temperature, weight, fuel — and having done so
    // they expect the whole aeroplane to answer in them. MSFSBA answered in its own units
    // regardless, so a pilot who had switched the G1000 to kilograms was still told pounds
    // by every panel.
    //
    // WHERE THEY LIVE. Not in a SimVar and not in an L:var: they are Working Title user
    // settings, held by the instrument's `settingSaveManager` and persisted into the
    // aeroplane's own profile (`DA40.profile_1`). Nothing outside the instrument can read
    // them, which is why they reach MSFSBA down this socket and no other way.
    //
    // BOTH DISPLAYS CARRY THE SAME SIX, verified live — the settings are shared over the
    // instrument bus, so the PFD's copy is as good as the MFD's. That matters: the CAS
    // monitor holds an always-on socket to the PFD, so the units arrive whether or not a
    // display window is open.
    A.M.UNIT_KEYS = [
        ["unitsNavAngle", "bearings"],
        ["unitsDistance", "distance"],
        ["unitsAltitude", "altitude"],
        ["unitsTemperature", "temperature"],
        ["unitsWeight", "weight"],
        ["unitsFuel", "fuel"],

        // ⚠️ THE BAROMETRIC UNIT IS A SEPARATE SETTING and it is not in the units box on
        // the setup page - it lives on the PFD, under PFD Opt, ALT Units. It matters more
        // than any of the others outside North America: a pilot flying in Europe or Asia is
        // given QNH in hectopascals by every controller they talk to, and an altimeter
        // readout in inches is a conversion they have to do in their head on every descent.
        // It is a boolean rather than a name, so it is turned into one here.
        ["baroHpa", "pressure"]
    ];

    A.M.units = function () {
        var el = A.M.el();
        var mgr = el && el.settingSaveManager;
        if (!mgr) return [];

        var found = {};
        try {
            var entries = mgr.entries;
            for (var i = 0; i < entries.length; i++) {
                var s = entries[i], name = null, value = null;
                try {
                    var setting = s.setting || s;
                    name = setting.definition ? setting.definition.name : setting.name;
                    value = setting.value;
                } catch (e) { continue; }
                if (name) found[name] = value;
            }
        } catch (e) { return []; }

        var out = [];
        for (var k = 0; k < A.M.UNIT_KEYS.length; k++) {
            var key = A.M.UNIT_KEYS[k][0];
            if (found[key] === undefined || found[key] === null) continue;

            var value = found[key];
            if (key === "baroHpa") value = value ? "hectopascals" : "inches";
            out.push(A.M.UNIT_KEYS[k][1] + " " + String(value));
        }
        return out;
    };

    /// The units as ONE row, readable aloud and parseable by the app.
    ///
    /// ⚠️ THE WORDING IS A CONTRACT with CowsDA40Definition.Units.cs, which reads this row
    /// off the same scrape the CAS monitor already runs and converts every DA40 readout
    /// into it. "Display units: " and the "<dimension> <value>" pairs are what it matches.
    A.pushUnits = function (rows) {
        var u = [];
        try { u = A.M.units(); } catch (e) { u = []; }
        if (u.length) rows.push("Display units: " + u.join(", "));
    };

    // ------------------------------------------------------------------- the page map
    //
    // WHY SOME AUX PAGES CANNOT BE REACHED. The page selector's own table gives every page
    // a KEY, and a page with an EMPTY key has no view behind it at all - it is a label the
    // stock Working Title G1000 draws for a page it has never implemented. Seven of the
    // nine Aux pages are like that: Trip Planning, Utility, GPS Status, XM Radio, System
    // Status, Connext Setup and Databases. So are Weather Data Link, TAWS-B, VRP
    // Information, User WPT Information, and four of the eight Nearest pages.
    //
    // That is NOT an MSFSBA fault and it is not something MSFSBA can fix - a sighted pilot
    // turning the knob lands on those names and nothing happens for them either. What WAS
    // wrong was saying nothing about it, because then a pilot cannot tell a page the
    // aeroplane never built from a page this reader is failing to read.
    //
    // The table lives on the page-selector view, so it is read once, on demand, by opening
    // the selector and closing it again, and then cached for the session.
    /// The PFD has no PAGES at all - it has WINDOWS, and several of them (the timer, the
    /// ADF/DME tuner, the nearest-airport list, the alert list, the display backlighting)
    /// have no bezel button and live behind softkeys a pilot has to find first. They are
    /// all registered views, so the same list serves them, and the same open call reaches
    /// them. These names are the Garmin ones, because the view keys are not English.
    A.M.PFD_WINDOWS = [
        ["Nearest", "Nearest Airports"],
        ["TimerRef", "Timer and References"],
        ["ADFDME", "ADF and DME Tuning"],
        ["Alerts", "Alerts"],
        ["PFDSetup", "PFD Setup and Backlighting"],
        ["DirectTo", "Direct To"],
        ["FPL", "Active Flight Plan"],
        ["PROC", "Procedures"],
        ["WptInfo", "Waypoint Information"],
        ["SetRunway", "Select Runway"],
        ["SelectDeparture", "Select Departure"],
        ["SelectArrival", "Select Arrival"],
        ["SelectApproach", "Select Approach"],
        ["SelectAirway", "Select Airway"],
        ["HoldAt", "Hold At"]
    ];

    A.M.pageMap = function () {
        if (A._pageMap) return A._pageMap;

        var vs = A.M.vs();
        if (!vs) return [];

        if (A.side() === "PFD") {
            var wins = [];
            for (var w = 0; w < A.M.PFD_WINDOWS.length; w++) {
                var key = A.M.PFD_WINDOWS[w][0];
                wins.push({ name: A.M.PFD_WINDOWS[w][1], key: A.M.has(key) ? key : "" });
            }
            A._pageMap = [{ group: "PFD", pages: wins }];
            return A._pageMap;
        }

        var weOpened = false, d = null;
        try {
            if (A.M.viewKey() === "PageSelect") { d = A.M.view(); }
            else { d = vs.open("PageSelect", true); weOpened = true; }

            var tabs = [];
            try {
                for (var i = 0; i < (d.tabRefs || []).length; i++) {
                    tabs.push(text(d.tabRefs[i].instance));
                }
            } catch (e) { }

            var out = [];
            for (var g = 0; g < d.pageGroups.length; g++) {
                var arr = d.pageGroups[g];
                if (arr && arr.getArray) arr = arr.getArray();
                if (arr && arr.get) arr = arr.get();
                var pages = [];
                for (var j = 0; j < arr.length; j++) {
                    var p = arr[j];
                    if (!p || !p.name) continue;
                    pages.push({ name: p.name, key: p.key || "" });
                }
                out.push({ group: tabs[g] || ("Group " + (g + 1)), pages: pages });
            }

            // The EIS tab carries the AIRCRAFT's own pages and is not in that table, so it
            // is added from the registered views. Leaving it out would hide the two pages
            // this aeroplane adds - the very ones a DA40 pilot most wants.
            // ⚠️ TWO SPELLINGS. The NG's plugin registers Da40NgEnginePage and
            // Da40NgChecklistPage; the XLS's registers Da40EnginePage and Da40ChecklistPage.
            // Looking for the NG's alone left the XLS's Engine page - the one with EGT, CHT
            // and the lean assist on it - out of the enumeration entirely (found by the
            // coverage sweep, which never visited it).
            var eis = [];
            var engineKey = A.M.has("Da40NgEnginePage") ? "Da40NgEnginePage"
                          : A.M.has("Da40EnginePage") ? "Da40EnginePage" : "";
            var checklistKey = A.M.has("Da40NgChecklistPage") ? "Da40NgChecklistPage"
                             : A.M.has("Da40ChecklistPage") ? "Da40ChecklistPage" : "";
            if (engineKey) eis.push({ name: "Engine", key: engineKey });
            if (checklistKey) eis.push({ name: "Checklist", key: checklistKey });
            if (eis.length) out.push({ group: tabs[out.length] || "EIS", pages: eis });

            A._pageMap = out;
            return out;
        } catch (e) {
            return [];
        } finally {
            try { if (weOpened && d && d.close) d.close(); } catch (e2) { }
        }
    };

    // ⚠️ DEFINED HERE, NOT BESIDE A.pageTitle WHICH USES IT. A.M does not exist until
    // much further down this file, so declaring this next to its caller threw
    // "undefined is not an object (evaluating 'A.M')" at LOAD — and a load that throws
    // leaves the PREVIOUS agent resident, so every read still answered and the change
    // simply appeared to do nothing. It needs A.M.pageKey and A.M.pageMap, so it goes
    // after both.
    A.M.pageName = function () {
        try {
            var key = String(A.M.pageKey() || "");
            if (!key) return "";
            var map = A.M.pageMap() || [];
            for (var g = 0; g < map.length; g++) {
                var ps = map[g].pages || [];
                for (var p = 0; p < ps.length; p++) {
                    if (ps[p].key === key) return { group: map[g].group, page: ps[p].name };
                }
            }
        } catch (e) { }
        return "";
    };

    A.M.has = function (key) {
        var vs = A.M.vs();
        try { return !!(vs && vs.registeredViews && vs.registeredViews.get(key)); }
        catch (e) { return false; }
    };

    /// Open a page by its key. This is the same call the page selector makes when the knob
    /// lands on a page and its timer commits, so nothing is being bypassed - the pilot
    /// simply does not have to count knob clicks through five groups to get there.
    /// ⚠️ GET ME OUT. A view opened over a page - Waypoint Information, a duplicate-ident
    /// picker, the page selector - swallows the bezel buttons underneath it, so PROC, FPL and
    /// the rest silently do nothing while it is up. And CLR does NOT close one: measured live,
    /// firing AS1000_MFD_CLR at a stuck WptInfo left it exactly where it was.
    ///
    /// A pilot who typed an ident that resolved to somewhere unexpected therefore had no way
    /// out at all - reported from the cockpit as "I got stuck, can't select the procedure".
    /// The close loop existed, but only buried inside goPage, so it could only be reached by
    /// navigating somewhere else entirely.
    /// THE ACTIVE LEG, AND THE ONE BEFORE IT, STRAIGHT OUT OF THE FLIGHT PLAN.
    ///
    /// ⚠️ THIS EXISTS BECAUSE THE STOCK GPS IDENT SIMVARS ARE EMPTY ON EVERY PROCEDURE.
    /// The waypoint readout and the passing call were built on `GPS WP NEXT ID` and
    /// `GPS WP PREV ID`, which the G1000's GpsSynchronizer writes from
    /// `plan.getLeg(plan.activeLateralLeg).name` - and measured live on a hand-built ANUT1D
    /// departure, they were BOTH EMPTY STRINGS while the flight plan's own
    /// `getLeg(5).name` returned "BI583" perfectly.
    ///
    /// Distance and bearing were correct throughout, because those ride continuous LNAV
    /// events; the IDENT rides a plan-change event that does not fire as a procedure
    /// sequences. So the aeroplane passed BI551 and BI582 in silence, which is precisely
    /// the call the whole feature exists to make.
    ///
    /// Read from the planner instead. Returns "prev|next", either half possibly empty -
    /// a fix-less path/terminator leg genuinely has no name and must stay empty rather
    /// than be invented.
    A.M.activeLeg = function () {
        try {
            var e = A.M.el();
            var p = e && e.planner;
            if (!p || !p.hasActiveFlightPlan()) return "|";

            var fp = p.getActiveFlightPlan();
            var a = fp.activeLateralLeg;
            if (!(a >= 0) || a >= fp.length) return "|";

            // ⚠️ A LEG CAN CARRY A FIX AND NO NAME, AND Ctrl+W THEN SAID "UNNAMED LEG".
            // Reported from the cockpit: Direct-To VCRI, and the waypoint readout went from
            // naming the previous waypoint to naming nothing at all. A Direct-To leg is
            // built by the FMS rather than lifted out of a procedure, so `name` can be
            // empty where an enroute leg's is filled in - and the SimVar is empty too on
            // this build, so BOTH sources had nothing and the readout was honest but
            // useless. The fix is still right there on the leg as its ICAO.
            //
            // The ident is the LAST whitespace-separated token of an MSFS ICAO string:
            // "A          VCRI " gives VCRI, "V  ED  SPY  " gives SPY. Taking the token
            // rather than a fixed slice is what makes it work across facility types, whose
            // region and airport fields are filled in differently.
            function identOf(lg) {
                try {
                    var icao = lg && lg.leg && lg.leg.fixIcao;
                    if (!icao) return "";
                    var parts = String(icao).trim().split(/\s+/);
                    var last = parts[parts.length - 1] || "";
                    // A single leading type letter on its own is not an ident.
                    return last.length > 1 ? last : "";
                } catch (e) { return ""; }
            }

            function nameAt(n) {
                if (!(n >= 0) || n >= fp.length) return "";
                try {
                    var lg = fp.getLeg(n);
                    if (!lg) return "";
                    return lg.name ? lg.name : identOf(lg);
                }
                catch (e) { return ""; }
            }
            return nameAt(a - 1) + "|" + nameAt(a);
        } catch (e) { return "|"; }
    };

    A.M.escape = function () {
        var vs = A.M.vs();
        if (!vs) return "no instrument";
        var guard = 0;
        try {
            while (A.M.viewKey() !== A.M.pageKey() && guard++ < 6) vs.closeActiveView();
        } catch (e) { return "error " + e; }
        return guard > 0 ? "closed" : "nothing open";
    };

    A.M.goPage = function (key) {
        var vs = A.M.vs();
        if (!vs) return "no instrument";
        if (!A.M.has(key)) return "not available";
        try {
            // Shut any dialog first, or the new page opens UNDERNEATH the one that is up.
            var guard = 0;
            while (A.M.viewKey() !== A.M.pageKey() && guard++ < 6) vs.closeActiveView();

            // ⚠️ A PFD WINDOW IS A SUBVIEW AND AN MFD PAGE IS NOT, and opening one the
            // other way leaves it registered but never shown. The PFD has no pages at all -
            // openPageKey there is permanently empty - which is exactly the test.
            vs.open(key, A.side() === "PFD");
            return "ok";
        } catch (e) { return "error " + e; }
    };

    // ---------------------------------------------------------- what to say after a key
    //
    // ONE sentence, and it answers "what am I on now" rather than "what key did I press".
    // The order is the order of what covers what on the screen: a list is drawn OVER the
    // page, a focused field is INSIDE the page, and the page title is what is left when
    // neither of those is true.
    A.M.say = function () {
        var key = A.M.viewKey();

        // A message dialog answers with its QUESTION before anything the list or the
        // fields would say - it is the reason the pilot is looking at the screen.
        if (key === "MessageDialog") {
            var asked = A.dialogSay();
            if (asked) return asked;
        }

        var L = A.M.list();
        if (L) {
            var lead = key === "PageSelect" ? "Page" :
                       key === "ContextMenuDialog" ? "Choose" :
                       (key === "PageMenuDialog" || key === "EnginePageMenuDialog" ||
                        key === "ChecklistPageMenuDialog") ? "Menu" : "List";

            var group = "";
            if (key === "PageSelect") {
                var d = A.M.view();
                try {
                    var gi = A.M.get(d.activeGroupIndex);
                    if (d.tabRefs && d.tabRefs[gi]) group = text(d.tabRefs[gi].instance);
                } catch (e) { }
            }

            var item = (L.sel >= 0 && L.sel < L.len) ? L.items[L.sel] : "";
            return lead + (group ? " " + group : "") + ", " + (item || "nothing selected") +
                   ", " + (L.sel + 1) + " of " + L.len;
        }

        // ⚠️ THE PAGE SELECTOR MUST BE READ AS A SELECTOR, NOT WALKED AS CONTROLS.
        //
        // Turning the knob with the cursor OFF opens PageSelect, and its list is not always
        // reachable through listRef the instant the key lands. When it is not, the generic
        // walk below takes over and lands on whatever control it can find on the page
        // UNDERNEATH - which on the system-setup page is a field whose value is the word
        // "NONE". So a pilot changing pages heard, in full, "none": measured live as
        // A.press("AS1000_MFD_FMS_Upper_INC") answering "ok|1|PageSelect||NONE".
        //
        // The selector's own DOM carries its active tab and highlighted entry whether or not
        // the list component is ready - that is the same reading pageTitle() already falls
        // back on while the selector is CLOSED - so it is used here rather than guessing.
        if (A.M.viewKey() === "PageSelect") {
            var sel = A.pageTitle();
            return sel ? ("Page selector, " + sel) : "Page selector";
        }

        var f = A.M.focused();
        // Leaving edit mode forgets the position too, so re-entering a field announces
        // where the cursor is rather than assuming the pilot remembers.
        if (!f || !f.active) { A.M._edKey = null; A.M._edWho = null; A.M._edIdx = null; }
        if (f) {
            // The BOX first, then the field. "Nearest Airport, Minimum Length: 3000FT"
            // answers what a pilot actually wants to know, and it is what the screen says.
            var s = (f.group ? f.group + ", " : "") +
                    (f.label ? f.label + ": " : "") + (f.value || "blank");
            if (f.able === false) s += ", dimmed";
            if (f.active) {
                // "Editing" is not decoration. It is the difference between the next turn
                // moving to the next field and the next turn changing this one, and a pilot
                // who cannot see the box has nothing else to tell them which.
                // ⚠️ SAY THE WHOLE FIELD ONCE, NOT ON EVERY KNOB CLICK. Spelling one ident is
                // dozens of clicks, and each was answering with the box, the value, the word
                // "editing", the character position AND the autocomplete - "Waypoint: VCRI__,
                // editing, character 4, I, Mattala Rajapaksa Intl" - twenty-eight times over.
                // Reported from the cockpit as "verbose as hell", and it is: the pilot chose
                // this field and is holding the knob, so the only NEWS is the character under
                // it and any facility the aeroplane has just matched.
                var key = (f.group || "") + "|" + (f.label || "");
                var fresh = (A.M._edKey !== key);
                // A DIFFERENT FIELD starts a new spelling, so its first character announces
                // its position even if it happens to sit at the same index as the last one.
                if (fresh) A.M._edIdx = null;
                A.M._edKey = key;

                var cs = A.M.charSay();
                var who = "";
                if (f.kind === "waypoint" || f.kind === "input") {
                    who = text(firstVisible(".wpt-entry-name")) || "";
                }

                // ⚠️ ONLY A FIELD BEING SPELT EARNS THE SHORT FORM, AND DROPPING THAT TEST
                // BROKE A DIFFERENT FIELD IMMEDIATELY. The short form exists because spelling
                // an ident is dozens of knob clicks and the only news each time is the
                // character. A field that CYCLES A VALUE - the approach's runway box, a units
                // choice - has no character position at all, so the short form fell back to the
                // bare value and announced a runway field as, in full, "none". Reported from
                // the cockpit exactly that way, and it is worse than the verbosity it replaced:
                // a value with no label is a word with no meaning.
                //
                // The presence of a character position IS the test for "being spelt", so it
                // gates the whole thing rather than merely decorating it.
                if (!fresh && cs) {
                    // Mid-spell: the character, and the match ONLY when it has changed.
                    var quiet = cs;
                    if (who && who !== A.M._edWho) quiet += ", " + who;
                    A.M._edWho = who;
                    return quiet;
                }

                A.M._edWho = who;
                s += ", editing";
                if (f.kind === "waypoint" || f.kind === "input") {
                    if (cs) s += ", " + cs;

                    // AUTOCOMPLETE HAS TO BE HEARD, not just happen. The aeroplane fills
                    // in the rest of the ident and looks up the facility as soon as enough
                    // letters are in, and a sighted pilot sees both appear - so a pilot
                    // turning the knob one letter at a time is told which aerodrome they
                    // have landed on the moment the aircraft knows.
                    var who = text(firstVisible(".wpt-entry-name"));
                    if (who) s += ", " + who;
                }
            }
            return s;
        }

        // NOTHING IN THE FLAT FRAMEWORK - so try the other one. This is what makes the
        // flight plan page and the checklist speak: their controls are not in the scroll
        // controller at all.
        var f2 = A.M.f2Say();

        // A DIALOG WITH NO CONTROLS IS STILL SOMETHING THE PILOT OPENED. The Navigraph
        // sign-in is the case that found this: pressing ENT on "Sign In" DOES work - it
        // opens the Auth view, which shows a device code and the address to type it at -
        // but that view registers no controls at all, so the readback said nothing and the
        // button read as dead. Reported as "either it's not clickable, or it's clickable
        // and it's not being clicked"; it was neither.
        // A MESSAGE DIALOG IS ITS QUESTION. "Activate SimBrief flight plan?" read back as
        // "NAV, OK" - a group label borrowed from the wrong ancestor and the focused
        // button - so a pilot who pressed Activate heard nothing that said what OK would do.
        if (!f2) return A.M.dialogText();

        // A CHECKLIST POPUP HAS TO SAY WHICH LIST IT IS. Both popups are plain lists of
        // capitals, and "NORMAL OPERATING PROCEDURES" alone does not tell a pilot whether
        // they are choosing a GROUP or a CHECKLIST inside one - which is two different
        // presses of ENT away from two different places.
        if (key === "Da40NgChecklistCategorySelectionPopup" ||
            key === "Da40ChecklistCategorySelectionPopup") return "Checklist group, " + f2;
        if (key === "Da40NgChecklistSelectionPopup" ||
            key === "Da40ChecklistSelectionPopup") return "Checklist, " + f2;
        return f2;
    };

    /// The text of a DIALOG that carries no controls - an instruction, a code, a warning.
    ///
    /// Bounded to dialogs on purpose: the same call on a PAGE would read the entire page
    /// after every keystroke. A dialog is a view that is not the open page, which the
    /// instrument already tells us.
    A.M.dialogText = function () {
        // ⚠️ A WHITELIST, AND IT HAS TO BE. The first version answered for any view with no
        // controls, no compiled map and short text - and the PAGE SELECTOR is exactly that
        // while it is fading out: its list has already gone, so A.M.list() returns null, the
        // view key is still PageSelect, and this read the whole selector back as one run-on
        // blob. Reported from the cockpit as "Navigation Map IFR/VFR Charts Traffic Map
        // Weather Data Link TAWS-B Map WPT Aux FPL NRST EIS", over and over.
        //
        // There is exactly one view on this instrument whose content IS its text, so it is
        // named rather than inferred. Guessing which views qualify is what broke it.
        var key = A.M.viewKey();
        if (key !== "Auth") return "";

        var view = A.M.view();
        var root = view && view.viewContainerRef && view.viewContainerRef.instance;
        if (!root) return "";

        // spacedText, not text: the Navigraph code sits against the word after it
        // ("BEM7NMMPOR") when the nodes are run together, and a code that cannot be told
        // from the text around it is no use to anybody.
        var whole = spacedText(root);
        if (!whole || whole.length > 200) return "";

        return A.M.spellCodes(whole);
    };

    /// A one-off CODE has to be spelled, or it is unusable.
    ///
    /// Navigraph's sign-in shows an eight-character device code the pilot has to type into
    /// a phone. Read as a word it is gone; read a letter at a time it can be written down.
    /// Only runs of capitals and digits with no vowel-shaped word behind them are treated
    /// this way, so ordinary text is left alone.
    A.M.spellCodes = function (str) {
        return str.replace(/\b[A-Z0-9]{6,12}\b/g, function (token) {
            if (!/[0-9]/.test(token) && /^[A-Z]+$/.test(token) && token.length < 8) return token;
            return token.split("").join(" ");
        });
    };

    /// Every field on the page as readable rows, so a pilot can SCAN the page in the window
    /// instead of turning the knob through it and hearing one field at a time.
    A.M.fieldRows = function () {
        var rows = [];
        var f = A.M.fields();
        var lastGroup = null;

        for (var i = 0; i < f.length; i++) {
            if (f[i].kind === "group") continue;

            // The box heading, once, above the fields it contains - the same shape the
            // screen has, so arrowing the window reads like looking at the page.
            if (f[i].group && f[i].group !== lastGroup) {
                rows.push(f[i].group);
                lastGroup = f[i].group;
            }

            var line = (f[i].group ? "  " : "") +
                       (f[i].label ? f[i].label + ": " : "") + (f[i].value || "blank");
            if (f[i].able === false) line += ", dimmed";
            if (f[i].focused) line += "   <-- cursor" + (f[i].active ? ", editing" : "");
            rows.push(line);
        }
        return rows;
    };

    A.summary = function () {
        // A SELECTION POPUP outranks everything, and this is why the checklist page felt
        // like a dead end: the knob really was moving the highlight, but the readback fell
        // through to the page TITLE and said "CHKLST - Checklist" after every press. The
        // pilot heard the same four words whichever way they turned, so the page looked
        // frozen when it was working perfectly.
        // THE MODEL FIRST. A.M.say() answers from the instrument's own view objects - the
        // open list and its position in it, the focused field and whether it is open for
        // editing - and it is right on pages whose markup this reader has never seen,
        // which is every page that has ever read as dead. The DOM branches below it are
        // kept for what the model does not carry: the checklist, the startup screens and
        // the PFD's popouts.
        var modelSaid = "";
        try { modelSaid = A.M.say(); } catch (e) { modelSaid = ""; }
        // ⚠️ NO "Cursor on." PREFIX. It was on EVERY field, and a pilot walking fourteen
        // fields down a setup page heard it fourteen times for one bit of information they
        // already had. The window announces the cursor when it CHANGES, which is the only
        // moment it is news; the rest of the time the answer to "where am I" is the field.
        if (modelSaid) return modelSaid;

        // THE CURSOR OUTRANKS EVERYTHING. If it is on, the pilot is editing a field and
        // the only thing a keystroke has to answer is which field and what it now says.
        var cursor = A.cursorField();
        if (cursor) return cursor;

        // The PFD's open window, before the MFD-only branches below it - which on the PFD
        // all miss and drop through to a page title the PFD does not have.
        var popout = A.pfdPopout();
        if (popout) return popout;

        var picking = A.checklistSelection();
        if (picking.length) {
            // Lead with the item under the knob. The whole list is still available from
            // the window itself; what a keypress must answer is "where am I now".
            for (var c = 0; c < picking.length; c++) {
                if (picking[c].indexOf("Currently on: ") === 0) return picking[c];
            }
            return picking[0];
        }

        var d = document.querySelector(".mfd-pageselect");
        if (d && visible(d) && parseFloat(window.getComputedStyle(d).opacity || "1") >= 0.05) {
            var group = "";
            var tabs = d.querySelectorAll(".mfd-pageselect-tabs > *");
            for (var i = 0; i < tabs.length; i++) {
                if (classList(tabs[i]).indexOf("active-tab") >= 0) { group = text(tabs[i]); break; }
            }

            var page = "";
            var pgs = d.querySelectorAll(".mfd-pageselect-group .popout-menu-item");
            for (var q = 0; q < pgs.length; q++) {
                if (classList(pgs[q]).indexOf("highlight-select") >= 0 ||
                    pgs[q].querySelector(".highlight-select")) { page = text(pgs[q]); break; }
            }

            var bits = [];
            if (group) bits.push(group);
            if (page) bits.push(page);
            return "Page selector" + (bits.length ? ", " + bits.join(", ") : "");
        }

        // A page menu is the other window a bezel key opens, and its selected entry is
        // the thing the pilot is about to press ENT on.
        var pm = document.querySelector(".mfd-pagemenu");
        if (pm && visible(pm) && parseFloat(window.getComputedStyle(pm).opacity || "1") >= 0.05) {
            var items = pm.querySelectorAll(".popout-menu-item");
            for (var m = 0; m < items.length; m++) {
                if (classList(items[m]).indexOf("highlight-select") >= 0 ||
                    items[m].querySelector(".highlight-select")) {
                    return "Page menu, " + text(items[m]);
                }
            }
            return "Page menu";
        }

        // THE CURSOR IS ON AND NOTHING IS UNDER IT. That happens for real - the knob runs
        // off the end of a checklist, or the cursor arms a page whose only control is the
        // map pointer - and answering with the page title alone is indistinguishable from a
        // key that did nothing, which is the complaint that started all of this. Say which
        // it is.
        // ⚠️ THE PFD HAS NO PAGES, only windows, so its FMS knob and cursor do nothing at
        // all until one is open - and a key that does nothing in silence is the fault this
        // whole file exists to avoid. Say what would make it work.
        if (A.side() === "PFD" && !A.M.viewKey()) {
            return "No window open. The PFD cursor works inside a window - " +
                   "press Control G, or a softkey such as Nearest or Timer and References.";
        }

        var title = A.pageTitle();
        var on = false;
        try { on = A.M.cursor() === true; } catch (e) { on = false; }
        return on ? title + ", nothing under the cursor" : title;
    };

    A.snapshot = function () {
        return JSON.stringify({
            v: A.VERSION,
            cas: A.cas(),
            panes: A.panes(),
            side: A.side(),
            fma: A.fma(),
            nav: A.nav(),
            eis: A.eis(),
            page: A.pageTitle(),
            softkeys: A.softkeys()
        });
    };

    // ------------------------------------------------- the shared display contract
    //
    // CoherentDisplayClient is generic: it resolves a view by title needle, installs an
    // agent, and polls `__MSFSBA_DISP.scrape()` for a JSON {ok, rows}. Exposing that
    // contract here means the DA40 needs no client of its own and inherits every piece of
    // connection handling that client already gets right - re-installing on a still-open
    // socket, the connect lock, the reconnect backoff.
    // THE OPEN CHOICE LIST — the dropdown the G1000 puts up when a field is activated.
    //
    // ⚠️ THIS WAS COMPLETELY UNREAD, AND IT IS THE WHOLE IFR PROCEDURE WORKFLOW. Opening
    // PFD Select Departure at VCBI puts up a sixteen-entry list with ANUT1D under the
    // cursor, and the pilot heard nothing of it — not the options, not which one the knob
    // was on. Same list for Select Arrival, Select Approach, the runway and transition
    // fields, and every setup-page enum. The coverage sweep found it: 13 departure names
    // and 15 arrival names visible on screen and absent from everything MSFSBA said.
    //
    // A.M.list() could not see it. That reads the VIEW's own listRef, and a context menu
    // is a separate overlay view stacked on top — so the field walk kept describing the
    // page underneath while the pilot was actually inside a list.
    //
    // ⚠️ ASK THE INSTRUMENT WHETHER IT IS OPEN. The checklist popups taught this the hard
    // way (they stay in the DOM at opacity 1 long after closing), and although THIS overlay
    // happens to hide honestly — measured: items drop to 0 and the ancestor walk returns
    // false — the view key is the signal that cannot rot. `ContextMenuDialog` is the
    // instrument's own name for it, and it is empty the moment the list is dismissed.
    A.contextMenuLines = function () {
        var open = "";
        try { open = String(A.M.viewKey() || ""); } catch (e) { return []; }
        if (open !== "ContextMenuDialog") return [];

        var host = firstVisible(".contextmenu-background");
        if (!host) return [];

        var items = host.querySelectorAll(".contextmenu-item");
        var lines = [], selected = null, at = 0, n = 0;

        for (var i = 0; i < items.length; i++) {
            if (!visible(items[i])) continue;
            var label = text(items[i]);
            if (!label) continue;
            n++;
            var chosen = classList(items[i]).indexOf("highlight-select") >= 0;
            if (chosen) { selected = label; at = n; }
            lines.push("  " + label + (chosen ? " (selected)" : ""));
        }

        if (!lines.length) return [];

        // Lead with where the knob IS, so a pilot who hears only the first line still knows
        // their position, then let them arrow the list. Same shape as the checklist popups.
        var head = "Choice list open, " + n + " option" + (n === 1 ? "" : "s");
        if (selected) head += ", currently on " + selected + ", " + at + " of " + n;
        lines.unshift(head + ":");
        return lines;
    };

    A.rows = function () {
        // ⚠️ THIS USED TO END WITH A pushUnreadBoxes() BACKSTOP AND THE PAGE WAS THEN READ
        // THREE TIMES. Aux System Setup came to 111 rows for about forty distinct facts: the
        // field walk (what the cursor reaches), "Page content" (everything drawn), and the
        // backstop re-emitting the five unowned boxes a third time. "Page content" already
        // covered them, so the backstop's ONLY real contribution was the ", dimmed" marking —
        // which now happens in A.groupboxLines where the pilot reads the value, per item, the
        // way the A380 MFD marks one. Same information, twelve fewer rows on that page, and
        // one place that decides whether a box is selectable instead of two.
        var rows = A.side() === "MFD" ? A.mfdRows() : A.pfdRows();

        // ⚠️ THE OPEN LIST GOES FIRST, ON BOTH DISPLAYS. A choice list is MODAL — it is the
        // only thing the knob and ENT can act on — so it outranks the page underneath, and
        // the window's "first row that changed" announcement then names the option the
        // pilot just scrolled onto rather than something on the page behind it.
        var menu = A.contextMenuLines();
        return menu.length ? menu.concat(rows) : rows;
    };



    // ---------------------------------------------------------------- MFD rows
    //
    // Ordered the way the screen is: what page you are on, the radios and data bar across
    // the top, the engine strip down the side, the page itself, then any window that is
    // open over it, then the softkeys under your fingers.
    A.mfdRows = function () {
        var rows = [];

        // Before the database is acknowledged the MFD has no pages at all, so this comes
        // first and alone - anything else would be reporting empty selectors as facts.
        var startup = A.startup();
        if (startup) {
            rows.push("Display starting up:");
            rows.push("  " + startup);
            var startKeys = A.softkeys();
            for (var z = 0; z < startKeys.length; z++) {
                rows.push("Softkey " + startKeys[z].index + ": " +
                    (startKeys[z].label || "blank") +
                    (startKeys[z].disabled && startKeys[z].label ? ", dimmed" : ""));
            }
            return rows;
        }

        rows.push("Page: " + (A.pageTitle() || "not shown"));

        A.pushUnits(rows);
        A.pushDialog(rows);
        A.pushFields(rows);

        var bar = A.navDataBar();
        if (bar.length) rows.push("Data bar: " + bar.join(", "));

        // ⚠️ THE FREQUENCIES, WHICH IS THE WHOLE QUESTION. This used to emit A.navcom()'s
        // summary - "NAV radios: tuning NAV 1, inactive" - which names which radio the knob
        // is on and its armed state and NEVER ONCE SAYS WHAT IS TUNED. A pilot reading it
        // learns that the knob is on NAV 1 and nothing about 110.30 or 109.50, on the one
        // row that exists to answer exactly that.
        //
        // A.radios() has carried active, standby, TUNING and TRANSMIT all along and was
        // reachable only from A.pfdWindows(), so the MFD never saw it. It also reads through
        // firstVisible, so it emits nothing on a display with no radio box rather than
        // reporting a hidden one.
        var radioRows = A.radios();
        for (var r = 0; r < radioRows.length; r++) rows.push("Radio: " + radioRows[r]);
        A.pushComStation(rows);

        // The armed state (standby / inactive / transmitting) is the one thing A.radios()
        // does not carry, so it rides alongside rather than replacing anything.
        var nc = A.navcom();
        for (var r = 0; r < nc.length; r++) {
            if (nc[r].state) rows.push(nc[r].kind + " armed state: " + nc[r].state);
        }

        var eis = A.eis();
        if (eis.length) {
            rows.push("Engine strip:");
            for (var e = 0; e < eis.length; e++) rows.push("  " + A.describeGauge(eis[e]));
        }

        // How the map is oriented - north up, heading up or track up - which changes what
        // every bearing on it means and is a setting the pilot chose.
        var orient = firstVisible(".map-orientation");
        if (orient) rows.push("Map orientation: " + text(orient));

        // ⚠️ SAY IT ONCE. On a setup page the field walk above and this content walk are two
        // descriptions of the SAME pavement: the walk reports what the knob can reach, in
        // knob order, and this reports what is drawn, in visual order - and on Aux System
        // Setup fourteen rows appeared in both. The pilot's report of the general problem was
        // "the values are shown twice", and a settings page that reads its own contents twice
        // is a page nobody can scan.
        //
        // The field walk KEEPS its copy, because that is the one carrying the cursor and the
        // dimmed marking; this walk drops the repeats and keeps everything the field walk
        // cannot see - Date, Time, MAG VAR, Fuel, Position, and every box with no control
        // behind it. Compared on the normalised text, because the two render the same field
        // differently ("Time Offset: +00:00, dimmed" against "Time Offset: + 00:00 --:--"),
        // which is exactly why a plain string compare never caught this.
        //
        // Inert on every page that emits no field walk: there is then nothing to repeat.
        var page = A.page();
        if (page.length) {
            var already = {};
            for (var k = 0; k < rows.length; k++) {
                var kk = String(rows[k]).toUpperCase().replace(/[^A-Z0-9]/g, "");
                if (kk) already[kk] = true;
            }

            var kept = [];
            for (var g = 0; g < page.length; g++) {
                // ⚠️ A HEADING IS STRUCTURE, NOT A REPEAT. "Date / Time:" names the box the
                // rows under it belong to, so deduping it against the field walk's copy left
                // "Date: 10 - SEP - 26" indented beneath nothing - a screen reader then reads
                // a value at a depth whose parent was never announced. Headings are kept
                // wherever they appear; only VALUES are said once.
                if (/:$/.test(String(page[g]).trim())) { kept.push(page[g]); continue; }

                var pk = String(page[g]).toUpperCase().replace(/[^A-Z0-9]/g, "");
                if (pk && already[pk]) continue;
                kept.push(page[g]);
            }

            // ⚠️ AND A HEADING WHOSE VALUES ALL WENT IS AN ANNOUNCEMENT OF NOTHING. Keeping
            // every heading (so a surviving value never loses its parent) left "MFD Data Bar
            // Fields:" and "COM Configuration:" standing alone on Aux System Setup, because
            // the field walk above had already said every row inside them. A heading with no
            // rows under it tells the pilot a box exists and then stops - worse than silence,
            // because they will go looking for the contents.
            //
            // A heading survives only if a NON-heading line follows it before the next
            // heading at the same depth or shallower. Depth is the indent, which is how this
            // walk already expresses nesting.
            var indentOf = function (t) { return /^ */.exec(String(t))[0].length; };
            var isHead = function (t) { return /:$/.test(String(t).trim()); };

            var pruned = [];
            for (var h = 0; h < kept.length; h++) {
                if (!isHead(kept[h])) { pruned.push(kept[h]); continue; }

                var mine = indentOf(kept[h]), has = false;
                for (var w = h + 1; w < kept.length; w++) {
                    if (indentOf(kept[w]) <= mine) break;      // out of this heading's block
                    if (!isHead(kept[w])) { has = true; break; }
                }
                if (has) pruned.push(kept[h]);
            }
            kept = pruned;

            // A block that came down to nothing but its own headings is not content.
            var anyValue = false;
            for (var v = 0; v < kept.length; v++) {
                if (!isHead(kept[v])) { anyValue = true; break; }
            }
            if (!anyValue) kept = [];

            if (kept.length) {
                rows.push("Page content:");
                for (var q = 0; q < kept.length; q++) rows.push("  " + kept[q]);
            }
        }

        // ⚠️ THE MAP RANGE, WHICH FIVE PAGES DREW AND NONE OF THEM SAID. The WPT pages and
        // the flight plan catalogue each embed a small map, and its range sits in its own
        // .map-range-display - so "15 NM" was on screen and absent from everything MSFSBA
        // said, which is how the coverage sweep kept reporting a bare "NM" on five pages.
        // The range is what a distance on that map MEANS; without it the map is a picture.
        //
        // ⚠️ AFTER the page content and checked against it, because the Navigation Map
        // already reads its own range there ("AUTO 25 NM") - emitting it unconditionally
        // would put the same value on that page twice, which is the duplication just spent
        // several commits removing.
        var range = firstVisible(".map-range-display");
        if (range) {
            var rangeText = spacedText(range);
            if (rangeText) {
                var saidAlready = false;
                var rk = rangeText.toUpperCase().replace(/[^A-Z0-9]/g, "");
                for (var rr = 0; rr < rows.length && !saidAlready; rr++) {
                    if (String(rows[rr]).toUpperCase().replace(/[^A-Z0-9]/g, "").indexOf(rk) >= 0)
                        saidAlready = true;
                }
                if (!saidAlready) rows.push("Map range: " + rangeText);
            }
        }

        // ⚠️ THE DECLUTTER LEVEL, WHOSE NUMBER IS IN THE CLASS AND NOT IN THE TEXT - AND
        // WHOSE FULL-DETAIL STATE RENDERS NOTHING AT ALL.
        //
        // The indicator element reads the bare word "Detail"; which level is selected is
        // carried on its class and drawn as a filled bar. Reading the text alone announces
        // "Detail" and tells the pilot nothing - the level IS the reading, the same way an
        // EIS gauge's arc is.
        //
        // Stepped through the WHOLE cycle live with the Detail softkey and watched both the
        // class and the softkey's own label at each stop:
        //
        //     indicator absent   softkey "Detail All"   full detail
        //     detail-3           softkey "Detail-1"     one level decluttered
        //     detail-2           softkey "Detail-2"     two levels
        //     detail-1           softkey "Detail-3"     three levels
        //
        // and it wraps All -> -1 -> -2 -> -3 -> All. So the class COUNTS DOWN as the screen
        // counts up, and the level a pilot sees is 4 minus the class number.
        //
        // ⚠️ A FIRST CUT ANNOUNCED THE RAW CLASS NUMBER - "Map detail: 3" where the display
        // says "-1" - so the readout disagreed with the softkey label beside it. And at full
        // detail nothing is drawn, so the row simply VANISHED and a pilot could not tell
        // "everything is shown" from "the row is missing". Both states are named now.
        var detailShown = firstVisible(".map-detail");
        if (detailShown) {
            var lvl = /(?:^|\s)detail-(\d+)(?:\s|$)/.exec(String(detailShown.className || ""));
            if (lvl) rows.push("Map detail: minus " + (4 - parseInt(lvl[1], 10)));
        } else if (range) {
            // No indicator, but there IS a map on this page - that is full detail, which the
            // display shows by drawing no declutter marker at all.
            rows.push("Map detail: all");
        }

        // A page that tells you how to leave it. The flight plan page carries one, and it
        // is the only place the gesture is written down.
        var prompt = firstVisible(".mfd-fpl-bottom-prompt");
        if (prompt) rows.push(text(prompt));

        A.pushPanes(rows);
        A.pushSoftkeys(rows);
        return rows;
    };

    // One gauge as one sentence. A gauge that prints a number says the number; a gauge
    // that only draws a needle says where the needle is, which is all the screen shows.
    A.describeGauge = function (g) {
        var parts = [];

        if (g.value) parts.push(g.value);

        for (var i = 0; i < g.bands.length; i++) {
            var b = g.bands[i];
            var where = b.colour + (b.percent >= 0 ? " " + b.percent + " percent along" : "");
            // Two needles on one scale are the two fuel tanks, left then right, and
            // saying which is which is the whole value of reading that gauge.
            parts.push(g.bands.length > 1 ? (i === 0 ? "left " : "right ") + where : where);
        }

        if (!g.value && !g.bands.length) parts.push("no reading");
        if (g.scale && g.bands.length) parts.push("scale " + g.scale.low + " to " + g.scale.high);
        if (g.alert) parts.push(g.alert);

        return g.label + ": " + parts.join(", ");
    };

    // ---------------------------------------------------------------- PFD rows
    A.pfdRows = function () {
        var rows = [];

        var cas = A.cas();
        rows.push("CAS messages: " + (cas.length === 0 ? "none" : cas.length));
        for (var i = 0; i < cas.length; i++) {
            var label = cas[i].severity === "warning" ? "WARNING"
                : cas[i].severity === "caution" ? "Caution"
                : cas[i].severity === "status" ? "Status" : "Advisory";
            rows.push("  " + label + ": " + cas[i].text);
        }

        var f = A.fma();
        // LATERAL BEFORE VERTICAL, which is the order the strip is drawn in and the order a
        // pilot reads it. Its absence is what made an uncaptured NAV indistinguishable from a
        // captured one - see A.fma().
        var modes = [f.autopilot, f.lateral, f.vertical, f.yawDamper]
            .filter(function (x) { return x; });
        rows.push("Autopilot: " + (modes.length ? modes.join(", ") : "off"));

        var n = A.nav();
        rows.push("Navigation source: " + (n.source || "not shown")
            + (n.sensitivity ? ", " + n.sensitivity : "")
            + (n.suspended ? ", SUSPENDED" : ""));
        if (n.crossTrack) rows.push("Cross track: " + n.crossTrack);
        if (n.message) rows.push("GPS message: " + n.message);

        // ⚠️ THE RADIOS BELONG HERE MOST OF ALL - THE BOXES ARE ON THIS BEZEL. Moving the
        // frequencies onto the MFD row list and dropping A.pfdWindows()'s copy took them OFF
        // the one display that physically has them, which is exactly backwards: the pilot got
        // frequencies on the MFD, which has no radio box, and nothing on the PFD, where they
        // tune. Both displays carry the row now - the MFD's elements read the same shared
        // state and are correct, they are simply not where the knobs are.
        var pfdRadios = A.radios();
        for (var r = 0; r < pfdRadios.length; r++) rows.push("Radio: " + pfdRadios[r]);
        A.pushComStation(rows);

        var pfdNc = A.navcom();
        for (var r = 0; r < pfdNc.length; r++) {
            if (pfdNc[r].state) rows.push(pfdNc[r].kind + " armed state: " + pfdNc[r].state);
        }

        var wins = A.pfdWindows();
        for (var w = 0; w < wins.length; w++) rows.push(wins[w]);

        A.pushUnits(rows);

        // The PFD's popouts - the transponder, the timer, the nearest-airport window - are
        // built from the same controls as the MFD's setup pages, so the same reader serves
        // them and a pilot can scan a popout instead of turning through it.
        A.pushDialog(rows);
        A.pushFields(rows);

        A.pushPanes(rows);
        A.pushSoftkeys(rows);
        return rows;
    };

    /// THE EDITABLE FIELDS OF THE PAGE, WITH THEIR VALUES, and which one the cursor is on.
    ///
    /// This is the difference between a setup page a pilot can USE and one they can only
    /// grope through. Before it, the only way to learn what the Aux setup page held was to
    /// turn the knob onto every field in turn and listen - so a pilot could not answer
    /// "what are my units set to" without disturbing them, and could not tell a page with
    /// nothing on it from a page the reader could not read.
    ///
    /// The values come from the instrument's own controls, so they are what the aeroplane
    /// has, not what a stylesheet happens to render.
    /// The open message dialog - the G1000's yes/no box (.popout-dialog.msgdialog): its
    /// question, the focused button, and the other. Rendered as ONE row placed before the
    /// fields, because CowsDA40DisplayForm speaks the FIRST ROW THAT CHANGED after a
    /// softkey press: "Activate" opening this dialog used to change "Fields (99)" to
    /// "Fields (2)" first, and that was what got spoken.
    A.dialogSay = function () {
        var box = firstVisible(".popout-dialog.msgdialog.open");
        if (!box) return "";
        var question = spacedText(box.querySelector(".msgdialog-content"));
        // ⚠️ THE BUTTONS KEEP THEIR OWN ORDER AND THE FOCUSED ONE IS MARKED. This used to
        // put the focused button FIRST and the rest after an "or", so the SimBrief import
        // dialog read "Import plan from SimBrief, OK or cancel" and then, one cursor move
        // later, "cancel or OK" - the same two buttons announced in two different orders,
        // which reads as the dialog having changed rather than the cursor having moved.
        // Reported from the cockpit as exactly that.
        //
        // ", selected" is the word this display already uses for the focused item of a
        // softkey row and a choice list, so a dialog now answers the same way.
        var buttons = box.querySelectorAll(".action-button");
        var labels = [];
        for (var i = 0; i < buttons.length; i++) {
            if (!visible(buttons[i])) continue;
            var label = text(buttons[i]);
            if (!label) continue;
            labels.push(label +
                (classList(buttons[i]).indexOf("highlight-select") >= 0 ? ", selected" : ""));
        }
        var parts = [];
        if (question) parts.push(question);
        if (labels.length) parts.push(labels.join(", "));
        return parts.join(", ");
    };

    A.pushDialog = function (rows) {
        var said = A.dialogSay();
        if (said) rows.push("Dialog: " + said);
    };

    A.pushFields = function (rows) {
        var fields = [];
        try { fields = A.M.fieldRows(); } catch (e) { fields = []; }
        if (!fields.length) return;

        // The COUNT is fields, not rows: the box headings are in the list too and counting
        // them said "Fields (19)" over fourteen fields.
        var n = 0;
        try { n = A.M.fields().length; } catch (e) { n = fields.length; }

        var on = null;
        try { on = A.M.cursor(); } catch (e) { on = null; }
        rows.push("Fields (" + n + "), cursor " +
            (on === null ? "unknown" : on ? "on" : "off") + ":");
        for (var i = 0; i < fields.length; i++) rows.push("  " + fields[i]);
    };

    A.pushPanes = function (rows) {
        var panes = A.panes();
        for (var p = 0; p < panes.length; p++) {
            rows.push(panes[p].title + ":");
            for (var q = 0; q < panes[p].lines.length; q++) rows.push("  " + panes[p].lines[q]);
        }
    };

    // "Softkey N:" is a CONTRACT with CowsDA40DisplayForm, which matches that prefix to
    // know which rows can be pressed and which key each one is. Change the wording here
    // and Enter stops working there.
    A.pushSoftkeys = function (rows) {
        var keys = A.softkeys();
        for (var k = 0; k < keys.length; k++) {
            var key = keys[k];
            rows.push("Softkey " + key.index + ": "
                + (key.label || "blank")
                + (key.value ? " " + key.value : "")
                + (key.active ? ", selected" : "")
                // ", dimmed" — the same word the A380 MFD, the A380 MCDU, the flyPad view
                // and this display's own fields and group boxes already use. One word for
                // one idea. A blank slot still reads "blank": no label means nothing is
                // greyed out, it means the key does nothing here at all.
                + (key.disabled && key.label ? ", dimmed" : ""));
        }
    };

    window.__MSFSBA_DISP = {
        scrape: function () {
            try {
                return JSON.stringify({ ok: true, rows: A.rows() });
            } catch (e) {
                return JSON.stringify({ ok: false, error: String(e) });
            }
        }
    };

    window.__MSFSBA_DA40G1000 = A;

    // MUST return a string containing MSFSBA_DISP_INSTALLED. CoherentDisplayClient tests
    // for that exact token to decide the agent is in place; anything else leaves
    // _agentInstalled false, so EnsureConnected returns false and the poll loop retries
    // for ever WITHOUT raising an error - a window stuck on "Connecting..." with nothing
    // in the log. This agent returned its own version string at first and did exactly
    // that. The token is the contract; the version rides alongside it.
    return "MSFSBA_DISP_INSTALLED da40-g1000 v" + A.VERSION;
})();
